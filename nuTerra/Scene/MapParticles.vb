Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Card (billboard) particles, driven by the game's own .vfxbin effect data and
''' the BWPs placements in space.bin. See docs/VFXBIN_PARTICLE_FORMAT.md.
'''
''' This is what produces the smoke rising from a burning building. That smoke
''' is NOT geometry - the building's .visual contains only fire materials - so
''' no amount of work on the volumetric shader could ever have produced it.
'''
''' Simulation is on the CPU into one dynamic instance buffer. Particle counts
''' here are small (a few hundred per building) so that is not worth doing on
''' the GPU yet.
''' </summary>
Public Class MapParticles

    ' One instance as the vertex shader wants it.
    <StructLayout(LayoutKind.Sequential)>
    Private Structure Inst
        Public pos As Vector3       ' world centre
        Public size As Single       ' world metres, half-extent
        Public colour As Vector4    ' rgba, straight (the shader premultiplies)
        Public uvOff As Vector2     ' atlas cell origin
        Public uvScale As Vector2   ' atlas cell size
    End Structure

    Private Class Particle
        Public pos As Vector3
        Public dir As Vector3
        Public age As Single
        Public life As Single
        Public baseSize As Single
        Public drift As Vector3
        Public em As modParticles.PfxEmitter
    End Class

    Private Class SystemInst
        Public origin As Vector3
        Public effect As modParticles.PfxEffect
        Public accum As Dictionary(Of modParticles.PfxEmitter, Single)
    End Class

    Private systems As New List(Of SystemInst)
    Private live As New List(Of Particle)
    Private rng As New Random(12345)

    Private vao As GLVertexArray
    Private vbo As GLBuffer
    Private instances As Inst()
    ''' <summary>
    ''' Back-to-front sort keys for the emitted instances, one per filled slot.
    ''' Preallocated with the instance array so a frame allocates nothing.
    ''' </summary>
    Private sortKeys As Single()
    Private texture As GLTexture = Nothing
    Private ready As Boolean = False
    Private logged_once As Boolean = False

    Public ReadOnly Property Count As Integer
        Get
            Return live.Count
        End Get
    End Property

    ''' <summary>
    ''' The effect id in a BWPs record is a 32-bit value that is NOT a hash of
    ''' the effect path under any algorithm tried (see the doc). Until it is
    ''' cracked, placements can only be matched to effects where the candidate
    ''' set is known from elsewhere.
    '''
    ''' Abbey's burning house is such a case: four placements cluster on it and
    ''' exactly four Smoke_* effects ship in that building's folder. The set is
    ''' certain; WHICH id is which within the set is not, and the visual
    ''' difference between them is small.
    ''' </summary>
    Private Shared ReadOnly HOUSE_EFFECTS As New Dictionary(Of UInteger, String) From {
        {&H72E5163FUI, "Big"},
        {&H838DB887UI, "Med"},
        {&HA0554F16UI, "Small"},
        {&H223B6CD9UI, "Ash_black"}
    }

    Public Sub Load()
        systems.Clear()
        live.Clear()
        ready = False
        If PFX_PLACEMENTS Is Nothing OrElse PFX_PLACEMENTS.Count = 0 Then Return

        Dim cache As New Dictionary(Of String, modParticles.PfxEffect)
        For Each pl In PFX_PLACEMENTS
            Dim which As String = Nothing
            If Not HOUSE_EFFECTS.TryGetValue(pl.effectId, which) Then Continue For

            Dim eff As modParticles.PfxEffect = Nothing
            If Not cache.TryGetValue(which, eff) Then
                Dim rel = String.Format(
                    "particles/content_deferred/PFX/Environment/Buildings/Bld_19_01_Vhouse_05_Smoke_{0}.vfxbin", which)
                Dim entry = ResMgr.Lookup(rel)
                If entry Is Nothing Then Continue For
                Using ms As New MemoryStream
                    entry.Extract(ms)
                    eff = modParticles.LoadVfx(ms.ToArray(), rel)
                End Using
                cache(which) = eff
            End If
            If eff Is Nothing OrElse eff.emitters.Count = 0 Then Continue For

            ' space.bin is the game's space: X is mirrored against nuTerra's.
            Dim t = pl.transform.Row3
            Dim s As New SystemInst With {
                .origin = New Vector3(-t.X, t.Y, t.Z),
                .effect = eff,
                .accum = New Dictionary(Of modParticles.PfxEmitter, Single)
            }
            For Each em In eff.emitters
                s.accum(em) = 0.0F
            Next
            systems.Add(s)
        Next

        If systems.Count = 0 Then
            LogThis("particles: no placement matched a known effect")
            Return
        End If

        Dim texPath = "particles/content_deferred/PFX_textures/eff_tex.dds"
        texture = TextureMgr.find_and_load_texture_from_pkgs(texPath)
        If texture Is Nothing Then
            LogThis("particles: eff_tex.dds not found - nothing will draw")
            Return
        End If

        ReDim instances(MAX_PARTICLES - 1)
        ReDim sortKeys(MAX_PARTICLES - 1)
        vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "pfx_instances")
        vbo.StorageNullData(MAX_PARTICLES * Marshal.SizeOf(Of Inst), BufferStorageFlags.DynamicStorageBit)
        vao = GLVertexArray.Create("pfx")
        vao.VertexBuffer(0, vbo, IntPtr.Zero, Marshal.SizeOf(Of Inst))
        ' pos + size, colour, uv offset + scale
        vao.AttribFormat(0, 4, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0) : vao.EnableAttrib(0)
        vao.AttribFormat(1, 4, VertexAttribType.Float, False, 16)
        vao.AttribBinding(1, 0) : vao.EnableAttrib(1)
        vao.AttribFormat(2, 4, VertexAttribType.Float, False, 32)
        vao.AttribBinding(2, 0) : vao.EnableAttrib(2)
        vao.BindingDivisor(0, 1)   ' one set of attributes per particle

        ready = True
        ' Stage 0 must begin when drawing begins, not when the scene object was
        ' constructed - the map load eats the first 25 seconds otherwise.
        stageWatch.Restart()
        lastStage = -1
        LogThis("particles: {0} system(s) live, {1} effect(s) loaded", systems.Count, cache.Count)
    End Sub

    Private Const MAX_PARTICLES As Integer = 4096

    ''' <summary>
    ''' Motion tuning, NOT authored data. The authored speed curve alone lifts
    ''' a particle about 7 m over its life, against four placements on the house
    ''' spanning 4.96 to 19.07 m, so the real column is roughly twice as tall as
    ''' we were producing.
    '''
    ''' The 4.0 was tuned when track 0 drove size and grew a card 12x over its
    ''' life. That premise HOLDS: size is track 0 read raw again, 0.572 -> 7.173
    ''' for Big/smoke_Slow, so the tuning context is the one it was set under and
    ''' the re-tune this note used to demand is not owed. It is still not
    ''' authored data - the game's own rise comes from the speed curve alone -
    ''' so treat it as a knob, just not an urgent one.
    '''
    ''' render_billboards_r also carries g_stretchParams / g_velocityToLength,
    ''' so the game stretches a card along its velocity, which a square card
    ''' cannot reproduce. STRETCH was meant to stand in for that and is NOT
    ''' WIRED UP - nothing reads it.
    ''' </summary>
    ''' <summary>
    ''' Amplitude of the size-over-life curve, NOT authored data.
    '''
    ''' Track 0 read raw grows a card about 12x over its life (0.572 -> 7.173
    ''' for Big/smoke_Slow), which closed the separated puffs into a continuous
    ''' column - that part was right. But the peak was too large against the
    ''' game: cards expanded well past the fire they belong to.
    '''
    ''' Applied to the curve rather than clamped, so the growth keeps its SHAPE
    ''' instead of growing a flat top partway through the life. Halving here
    ''' halves the peak, and the birth size with it - the two stay in the
    ''' authored ratio.
    '''
    ''' Card overlap is also what drives the FX pass into clipping: gColor is
    ''' Rgba8 and draw_fx composites AFTER deferred.frag has already tonemapped,
    ''' so N overlapping additive cards sum in 8 bits and clip channel by
    ''' channel - red first for fire, which turns orange into yellow and then
    ''' white. Fewer/smaller cards means less accumulation and more surviving
    ''' hue, but the clipping itself is structural and is not fixed here.
    ''' </summary>
    Private Const CARD_SIZE_SCALE As Single = 0.5F

    Private Const SPEED_GAIN As Single = 4.0F     ' on the authored speed curve
    Private Const STRETCH As Single = 1.6F        ' UNUSED - see above

    ''' <summary>
    ''' Lateral motion. The game's column is broader and drifts sideways - 71.6%
    ''' grey coverage in the upper frame against our 53.5% when both were
    ''' measured the same way - because it applies wind (WindParamsPack in the
    ''' engine cbuffer, wind_impulse_u / wind_sensor_u shaders) and per-particle
    ''' noise (noise_u).
    '''
    ''' nuTerra has no wind data at all, so DRIFT is invented, not authored.
    ''' TURBULENCE stands in for noise_u: a persistent random sideways velocity
    ''' per particle, which is what broadens a column instead of leaving it a
    ''' narrow chimney.
    ''' </summary>
    Private Shared ReadOnly DRIFT As Vector3 = New Vector3(-0.55F, 0.0F, 0.25F)
    Private Const TURBULENCE As Single = 0.7F     ' m/s, per particle

    Private Function Rand(a As Single, b As Single) As Single
        Return a + CSng(rng.NextDouble()) * (b - a)
    End Function

    Public Sub Update(dt As Single)
        If Not ready Then Return
        If dt <= 0.0F OrElse dt > 0.25F Then dt = 0.016F   ' ignore load-hitch spikes

        ' age out
        For i = live.Count - 1 To 0 Step -1
            live(i).age += dt
            If live(i).age >= live(i).life Then live.RemoveAt(i)
        Next

        For Each s In systems
            For Each em In s.effect.emitters
                ' Smoke only for now. The fire emitters would need the additive
                ' path and the building already draws its own fire geometry, so
                ' spawning them too would double it.
                If Not em.name.ToLower().Contains("smoke") Then Continue For

                s.accum(em) += em.rate * dt
                While s.accum(em) >= 1.0F AndAlso live.Count < MAX_PARTICLES
                    s.accum(em) -= 1.0F
                    Dim p As New Particle With {
                        .em = em,
                        .age = 0.0F,
                        .life = Rand(em.lifeMin, em.lifeMax),
                        .baseSize = Rand(em.sizeMin, em.sizeMax),
                        .pos = s.origin
                    }
                    ' Rise, cone-limited by the authored spread.
                    Dim ang = Rand(0.0F, em.spread)
                    Dim azi = Rand(0.0F, 6.2831853F)
                    p.dir = New Vector3(CSng(Math.Sin(ang) * Math.Cos(azi)),
                                        CSng(Math.Cos(ang)),
                                        CSng(Math.Sin(ang) * Math.Sin(azi)))
                    p.drift = New Vector3(Rand(-TURBULENCE, TURBULENCE), 0.0F,
                                          Rand(-TURBULENCE, TURBULENCE))
                    live.Add(p)
                End While
            Next
        Next

        ' integrate
        For Each p In live
            Dim t = If(p.life > 0.0F, p.age / p.life, 1.0F)
            Dim spd = If(p.em.speedTrack Is Nothing, 1.0F, p.em.speedTrack.Sample(t, 0))
            ' Wind and turbulence build with age, so the base stays tight and
            ' the top spreads - which is the shape a real column has.
            p.pos += (p.dir * spd * SPEED_GAIN + (p.drift + DRIFT) * t) * dt
        Next
    End Sub

    ''' <summary>Fill the instance buffer. Returns how many are drawable.</summary>
    Private Function BuildInstances(camPos As Vector3) As Integer
        Dim n = 0
        For Each p In live
            If n >= MAX_PARTICLES Then Exit For
            Dim t = If(p.life > 0.0F, p.age / p.life, 1.0F)
            ' Size over life is track 0 read RAW - the authored size range at
            ' 999+176/+180 is the size at track 0 = 1, not the final size, and
            ' the curve carries the card well past it. Measured diameters, mean
            ' over 40 cards:
            '
            '   emitter      authored   start   t=0.25   end
            '   smoke_Slow       7.37    4.88    29.80   52.77
            '   smoke_Big        2.91    1.96    14.01   20.84
            '   smoke_Fast       4.72    3.70     7.35   12.93
            '
            ' Settled by A/B on a fixed camera. Normalising track 0 so a card
            ' ends at its authored size leaves the same separated puffs we had
            ' before; reading it raw closes them into the continuous column the
            ' game shows.
            '
            ' The old objection to track 0 was that 52-72 m cards are absurd.
            ' They are not, because alpha is 0 at t = 1 - a card is fully
            ' transparent at its maximum. At peak alpha, t ~ 0.19, smoke_Slow
            ' is about 37 m, which is the scale of the real column.
            ' CARD_SIZE_SCALE halves the peak - the cards were expanding far
            ' past the fire they belong to.
            Dim scale = If(p.em.sizeTrack Is Nothing, 1.0F, p.em.sizeTrack.Sample(t, 0)) * CARD_SIZE_SCALE

            Dim col As New Vector4(1.0F, 1.0F, 1.0F, 1.0F)
            If p.em.colourTrack IsNot Nothing AndAlso p.em.colourTrack.values IsNot Nothing AndAlso
               p.em.colourTrack.values.Length > 0 AndAlso p.em.colourTrack.values(0).Length = 4 Then
                col = New Vector4(p.em.colourTrack.Sample(t, 0), p.em.colourTrack.Sample(t, 1),
                                  p.em.colourTrack.Sample(t, 2), p.em.colourTrack.Sample(t, 3))
            End If
            If PARTICLES_WIRE Then
                ' Age as colour: green at birth, red at death.
                col = New Vector4(t, 1.0F - t, 0.25F, 1.0F)
            ElseIf col.W <= 0.002F Then
                Continue For
            End If

            ' Sub-UV: the sheet is ONE rise-and-fade puff lifecycle, so it
            ' plays across the particle's life exactly once and never wraps.
            ' The alpha-gutter scan of the slow/fast sheet reads its eight rows
            ' as a single rise and fade - mean alpha 36.7, 49.7, 50.9, 45.2,
            ' 37.9, 29.8, 20.6, 9.7 - which is a lifecycle, not a loop.
            '
            ' Wrapping is what made cards look like they restarted in mid-air:
            ' the index fell back to the small opening cell while the card kept
            ' rising, so one card read as a second one being born up there. It
            ' wrapped for two reasons - a random frameSeed * cells offset at
            ' birth, and smoke_Fast needing 96 frames (16 fps over a 6 s life)
            ' from a 64-cell sheet.
            '
            ' This ignores the authored fps at 999+228. For two of the three
            ' smoke emitters fps and life are already near-equivalent over a
            ' 64-cell sheet - smoke_Fast 16 fps x 4 s = 64, smoke_Big 15 fps x
            ' 3.5 s = 53 - so the authored intent looks like one pass either
            ' way. smoke_Slow at 2 fps would show only 8 of its 64 cells, which
            ' is the reading that does not survive contact with the sheet.
            Dim cells = p.em.atlasCols * p.em.atlasRows
            Dim frame = 0
            If cells > 1 Then
                frame = CInt(Math.Floor(t * cells))
                If frame < 0 Then frame = 0
                If frame >= cells Then frame = cells - 1
            End If
            Dim cellX = frame Mod p.em.atlasCols
            Dim cellY = frame \ p.em.atlasCols     ' 0 = TOP row of the sheet

            Dim cellW = (p.em.uMax - p.em.uMin) / p.em.atlasCols
            Dim cellH = (p.em.vMax - p.em.vMin) / p.em.atlasRows
            ' The atlas stores v measured up from the bottom of the image as
            ' displayed, but TextureMgr uploads the DDS rows unflipped, so the
            ' file's TOP row lands on v = 0. Sampling v is therefore the
            ' complement of the stored value, and rows walk DOWN in sampler v.
            Dim uOff = p.em.uMin + cellX * cellW
            Dim vOff = (1.0F - p.em.vMax) + cellY * cellH

            ' Negated so an ascending sort puts the FARTHEST card first. Squared,
            ' because only the ordering matters and a sqrt per card per frame
            ' does not.
            sortKeys(n) = -(p.pos - camPos).LengthSquared

            instances(n) = New Inst With {
                .pos = p.pos,
                .size = p.baseSize * scale * 0.5F,
                .colour = col,
                .uvOff = New Vector2(uOff, vOff),
                .uvScale = New Vector2(cellW, cellH)
            }
            n += 1
        Next

        ' Back-to-front. Every card is order-dependent: particle.frag emits
        ' vec4(rgb * alpha, alpha) unconditionally - there is no additive branch -
        ' and the pass blends premultiplied ONE / ONE_MINUS_SRC_ALPHA with
        ' DepthMask(False), so depth cannot resolve card-against-card and the
        ' buffer order IS the composite order. Before this, that order was spawn
        ' order, which is a real compositing error rather than a cosmetic one.
        '
        ' The game itself needs no such sort: its particles go through
        ' order-independent transparency (moment-based on the high preset,
        ' weighted-blended on the low one), and its GPU cull appends visible
        ' particles with an atomic counter that scrambles order outright. There
        ' is no authored draw-order field anywhere in the .vfxbin to honour -
        ' that was searched for and is not there. Distance sorting is our
        ' substitute for OIT, not a port of anything.
        '
        ' Sorts the FILLED range only. Cards whose alpha rounded to nothing were
        ' skipped above and never occupied a slot.
        If n > 1 Then Array.Sort(sortKeys, instances, 0, n)

        Return n
    End Function

    ' Left over from the staged bring-up that found the black-frame bug: each
    ' stage added one more GL call and held for 5 seconds, so a run of captures
    ' showed which call blacked the frame. That staging is GONE - Draw now
    ' issues the whole sequence. Load still writes these two and nothing reads
    ' them, and Probe below is never called.
    Private stageWatch As Stopwatch = Stopwatch.StartNew()
    Private lastStage As Integer = -1


    ''' <summary>
    ''' Save and restore the GL state this pass touches.
    '''
    ''' Measured, not assumed. draw_fx leaves test=False mask=True cull=False
    ''' blend=False src=SRC_ALPHA dst=ONE_MINUS_SRC_ALPHA; this pass was leaving
    ''' test=True mask=False src=ONE. The blend function is what broke the base
    ''' rings and the minimap: both enable blend and INHERIT the function, so a
    ''' premultiplied ONE composites them at full intensity whatever their alpha
    ''' says - solid squares, white minimap.
    ''' </summary>
    Private Structure GlState
        Public test As Boolean, cull As Boolean, blend As Boolean, mask As Boolean
        Public func As Integer, srcRGB As Integer, dstRGB As Integer
        Public srcA As Integer, dstA As Integer
        Public prog As Integer, vao As Integer, tex0 As Integer
        Public polyMode As Integer
    End Structure

    Private Function SaveState() As GlState
        Dim g As GlState
        Dim dm(0) As Boolean
        GL.GetBoolean(GetPName.DepthWritemask, dm)
        g.mask = dm(0)
        g.test = GL.IsEnabled(EnableCap.DepthTest)
        g.cull = GL.IsEnabled(EnableCap.CullFace)
        g.blend = GL.IsEnabled(EnableCap.Blend)
        g.func = GL.GetInteger(GetPName.DepthFunc)
        g.srcRGB = GL.GetInteger(GetPName.BlendSrcRgb)
        g.dstRGB = GL.GetInteger(GetPName.BlendDstRgb)
        g.srcA = GL.GetInteger(GetPName.BlendSrcAlpha)
        g.dstA = GL.GetInteger(GetPName.BlendDstAlpha)
        g.prog = GL.GetInteger(GetPName.CurrentProgram)
        g.vao = GL.GetInteger(GetPName.VertexArrayBinding)
        g.tex0 = GL.GetInteger(GetPName.TextureBinding2D)
        Dim pm(1) As Integer
        GL.GetInteger(GetPName.PolygonMode, pm)
        g.polyMode = pm(0)
        Return g
    End Function

    Private Sub RestoreState(g As GlState)
        If g.test Then GL.Enable(EnableCap.DepthTest) Else GL.Disable(EnableCap.DepthTest)
        If g.cull Then GL.Enable(EnableCap.CullFace) Else GL.Disable(EnableCap.CullFace)
        If g.blend Then GL.Enable(EnableCap.Blend) Else GL.Disable(EnableCap.Blend)
        GL.DepthFunc(CType(g.func, DepthFunction))
        GL.DepthMask(g.mask)
        GL.BlendFuncSeparate(CType(g.srcRGB, BlendingFactorSrc), CType(g.dstRGB, BlendingFactorDest),
                             CType(g.srcA, BlendingFactorSrc), CType(g.dstA, BlendingFactorDest))
        GL.UseProgram(g.prog)
        GL.BindVertexArray(g.vao)
        GL.BindTextureUnit(0, g.tex0)
        GL.PolygonMode(MaterialFace.FrontAndBack, CType(g.polyMode, PolygonMode))
    End Sub

    Private Sub Probe(label As String, announce As Boolean)
        Dim e = GL.GetError()
        If announce Then
            LogThis("   pfx step {0,-18} err={1}", label, e)
        End If
    End Sub

    Public Sub Draw(camPos As Vector3)
        If Not ready OrElse live.Count = 0 Then Return
        Dim n = BuildInstances(camPos)
        If n = 0 Then Return

        Dim saved = SaveState()

        GL.NamedBufferSubData(vbo.buffer_id, IntPtr.Zero, n * Marshal.SizeOf(Of Inst), instances)

        ' Scene position for the soft-edge fade that particle.frag texelFetches
        ' at binding 3. This pass used to bind NOTHING to unit 3 and read
        ' whatever the frame happened to leave there, which was never correct in
        ' either branch:
        '
        '   draw_fx ran        -> it left gPosition on unit 3 but RE-ATTACHED it
        '                         (MapStaticModels.vb:613), so sampling it is the
        '                         feedback loop draw_fx's own comment says raised
        '                         GL_INVALID_OPERATION.
        '   draw_fx skipped    -> it early-returns on an empty FX bucket
        '                         (numAfterFrustum(3) = 0) without touching unit
        '                         3, leaving the water pass's SunShadowDepth
        '                         (MapWater.vb:257) - a depth texture with
        '                         comparison enabled, read through a plain
        '                         sampler2D. The driver reports that as undefined
        '                         behaviour, and it appears and disappears as the
        '                         FX meshes enter and leave the frustum.
        '
        ' Same detach / bind / re-attach as draw_fx, for the same reason: being
        ' merely absent from the draw buffers is not enough. attach_C names one
        ' draw buffer and it is not this attachment, but the feedback rule is
        ' about ATTACHMENT, not about being written.
        MainFBO.fbo.Texture(FramebufferAttachment.ColorAttachment3, Nothing, 0)
        MainFBO.gPosition.BindUnit(3)

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)   ' reversed Z, same as the FX pass
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        particleShader.Use()
        GL.Uniform1(particleShader("wireMode"), CInt(If(PARTICLES_WIRE, 1, 0)))
        If PARTICLES_WIRE Then GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
        texture.BindUnit(0)
        vao.Bind()
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, n)

        ' Put gPosition back before anything downstream expects to write it.
        ' There is no early return between the detach above and here, so this
        ' cannot be skipped.
        MainFBO.fbo.Texture(FramebufferAttachment.ColorAttachment3, MainFBO.gPosition, 0)

        RestoreState(saved)
    End Sub

End Class

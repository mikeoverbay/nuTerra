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
        Public frameSeed As Single
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
                        .frameSeed = CSng(rng.NextDouble()),
                        .pos = s.origin + New Vector3(
                            Rand(-em.boxHalf.X, em.boxHalf.X),
                            Rand(-em.boxHalf.Y, em.boxHalf.Y),
                            Rand(-em.boxHalf.Z, em.boxHalf.Z))
                    }
                    ' Rise, cone-limited by the authored spread.
                    Dim ang = Rand(0.0F, em.spread)
                    Dim azi = Rand(0.0F, 6.2831853F)
                    p.dir = New Vector3(CSng(Math.Sin(ang) * Math.Cos(azi)),
                                        CSng(Math.Cos(ang)),
                                        CSng(Math.Sin(ang) * Math.Sin(azi)))
                    live.Add(p)
                End While
            Next
        Next

        ' integrate
        For Each p In live
            Dim t = If(p.life > 0.0F, p.age / p.life, 1.0F)
            Dim spd = If(p.em.speedTrack Is Nothing, 1.0F, p.em.speedTrack.Sample(t, 0))
            p.pos += p.dir * spd * dt
        Next
    End Sub

    ''' <summary>Fill the instance buffer. Returns how many are drawable.</summary>
    Private Function BuildInstances(camPos As Vector3) As Integer
        Dim n = 0
        For Each p In live
            If n >= MAX_PARTICLES Then Exit For
            Dim t = If(p.life > 0.0F, p.age / p.life, 1.0F)
            Dim scale = If(p.em.scaleTrack Is Nothing, 1.0F, p.em.scaleTrack.Sample(t, 0))

            Dim col As New Vector4(1.0F, 1.0F, 1.0F, 1.0F)
            If p.em.colourTrack IsNot Nothing AndAlso p.em.colourTrack.values IsNot Nothing AndAlso
               p.em.colourTrack.values.Length > 0 AndAlso p.em.colourTrack.values(0).Length = 4 Then
                col = New Vector4(p.em.colourTrack.Sample(t, 0), p.em.colourTrack.Sample(t, 1),
                                  p.em.colourTrack.Sample(t, 2), p.em.colourTrack.Sample(t, 3))
            End If
            If col.W <= 0.002F Then Continue For

            ' Sub-UV: step through the atlas at the authored fps, wrapping.
            Dim cells = p.em.atlasCols * p.em.atlasRows
            Dim frame = 0
            If cells > 1 Then
                frame = CInt(Math.Floor(p.age * p.em.atlasFps + p.frameSeed * cells)) Mod cells
                If frame < 0 Then frame += cells
            End If
            Dim cx = frame Mod p.em.atlasCols
            Dim cy = frame \ p.em.atlasCols

            ' eff_tex.dds is a SHARED 4096x4096 sheet holding many unrelated
            ' sprite sheets, so an emitter needs both WHICH region is its sheet
            ' and how that region divides into frames.
            '
            ' The region encoding is NOT solved. The four authored floats
            ' (999 +192..+204, all multiples of 1/8) do not yield a coherent
            ' sheet under any rect reading tried - as (x,y,w,h) the smoke's
            ' region straddles puffs, a star flare and an ellipse, and
            ' smoke_Big's leading 1.0 cannot be an origin or a width if the
            ' others are. See docs/VFXBIN_PARTICLE_FORMAT.md.
            '
            ' So the sheet below is a STAND-IN, found by inspecting the atlas:
            ' a clean 8x8 grid of smoke puffs at u 0.25..0.50, v 0.00..0.25,
            ' cells of 1/32. Everything else about the particle - rate, size,
            ' lifetime, scale curve, colour curve - is real authored data. Only
            ' the choice of sprite region is guessed, and this is the one place
            ' to fix when the encoding is understood.
            Const SHEET_U0 As Single = 0.25F, SHEET_V0 As Single = 0.0F
            Const SHEET_W As Single = 0.25F, SHEET_H As Single = 0.25F
            Dim cellW = SHEET_W / p.em.atlasCols
            Dim cellH = SHEET_H / p.em.atlasRows
            instances(n) = New Inst With {
                .pos = p.pos,
                .size = p.baseSize * scale * 0.5F,
                .colour = col,
                .uvOff = New Vector2(SHEET_U0 + cx * cellW, SHEET_V0 + cy * cellH),
                .uvScale = New Vector2(cellW, cellH)
            }
            n += 1
        Next
        Return n
    End Function

    ' Staged bring-up. Each stage adds one more GL call and holds for 5
    ' seconds, so a series of captures shows exactly which call blacks the
    ' frame. glGetError is read after every step and the live particle count is
    ' printed, which also settles whether spawning is keeping up at all.
    Private stageWatch As Stopwatch = Stopwatch.StartNew()
    Private lastStage As Integer = -1


    ''' <summary>Put back every piece of state this pass changes.</summary>
    Private Sub RestoreState()
        particleShader.StopUse()
        defaultVao.Bind()
        GL.BindTextureUnit(0, 0)
        GL.Disable(EnableCap.Blend)
        GL.Enable(EnableCap.CullFace)
        GL.DepthMask(True)
        GL.Disable(EnableCap.DepthTest)
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

        GL.NamedBufferSubData(vbo.buffer_id, IntPtr.Zero, n * Marshal.SizeOf(Of Inst), instances)

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)   ' reversed Z, same as the FX pass
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        particleShader.Use()
        texture.BindUnit(0)
        vao.Bind()
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, n)
        RestoreState()
    End Sub

End Class

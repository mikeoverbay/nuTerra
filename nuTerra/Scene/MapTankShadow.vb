Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' A shadow map per tank, rebuilt every frame, so the fleet casts.
'''
''' WHY NOT THE SUN BAKE. MapSunShadow is baked ONCE at map load - its own header
''' says a baked shadow cannot hold anything animated, which is why the trees are
''' in it and nothing that moves is. A tank moves, so it needs its own map and it
''' needs it every frame. The owner put it plainly: "we cant bake tank shadows".
'''
''' ONE MAP PER TANK, 512 square, rather than one map over the fleet. Thirty
''' hulls spread over 800 m of map would share a box a kilometre across, and at
''' 512 that is two metres a texel - a shadow blurrier than the tank casting it.
''' Fitted to one hull, 512 is about 1.5 cm a texel and the track links have
''' edges. The cost of the choice is a draw per tank, which is why the range
''' test below matters.
'''
''' THE RANGE TEST IS THE FEATURE, not an optimisation bolted on. A tank a
''' kilometre away casts a shadow a few pixels across that nobody can see, and
''' both halves of the cost - the draw here and the sample in the shader - are
''' wasted on it. So a tank beyond RANGE_M gets no layer at all, and the shader
''' skips any tank whose sphere does not contain the pixel it is shading. The
''' owner asked for exactly that: "doing the math for them stopped if they are
''' out of range in the shader".
'''
''' AND IT FADES. A shadow that vanishes the instant a tank crosses the range
''' boundary pops. The last quarter of the range fades it out, so a tank driving
''' away loses its shadow gradually and the boundary is invisible.
'''
''' IT DRAWS THE TANK'S OWN MESHES, since 2026-09-14. It rasterised the hull BOX
''' before that, deliberately and as documented - a box proved the layers, the
''' matrices, the range test, the fade and the sampling while the skinned draw
''' sat in another session's file. The prediction written here was exact: one
''' call changed and the shadow became tank shaped.
'''
''' What it cost while it stood: every tank cast a soft rectangular slab, a
''' cuboid projected along the sun angle, and it read as a shadow BUG rather
''' than as a placeholder. An hour went into the depth conventions - the clear
''' value, the depth func, the ZeroToOne remap, the compare mode, tank_factor's
''' transform - and all of them were right the whole time. The owner is the one
''' who cut through it: "you can do the shading for the baked shadow and not
''' this? It's the same math." It was the same math. Only the geometry differed.
''' A placeholder that renders something plausible is harder to see than one
''' that renders nothing.
''' </summary>
Public Class MapTankShadow
    Implements IDisposable

    ''' <summary>Texels a side, per tank. The owner's number.</summary>
    ' 2048 from 512 on 2026-09-14, at the owner's ask - "i have Vram to burn".
    '
    ' THE COST IS THE ARRAY, AND IT IS PAID UP FRONT: SIZE * SIZE * 4 bytes *
    ' MAX_CASTERS, so 512 MB at 2048 against 32 MB at 512, allocated whether one
    ' tank is casting or thirty. The per-frame cost is 16x the rasterised area,
    ' and unlike the sun map this one is rebuilt EVERY FRAME for every caster in
    ' range - which is where it will show up if it shows up at all.
    '
    ' What it buys: the ortho is fitted per tank at span = radius * 2 + 6, about
    ' 14-16 m, so a texel goes from roughly 3 cm to roughly 7 mm.
    '
    ' Watch the "Tanks" GPU timer, which brackets this pass and the beauty pass
    ' together (modRender.vb:202-208) - the beauty pass does not change, so the
    ' delta on that counter is this.
    Public Const SIZE As Integer = 2048

    ''' <summary>Most tanks that can cast at once. Thirty is a full roster; the
    ''' rest are lit unshadowed rather than dropped, which degrades instead of
    ''' failing.</summary>
    Public Const MAX_CASTERS As Integer = 32

    Private ReadOnly map_scene As MapScene

    Private depth_tex As GLTexture
    Private fbo As GLFramebuffer
    Private allocated As Integer

    ''' <summary>How many layers hold a tank this frame.</summary>
    Public count As Integer

    ''' <summary>World to that tank's shadow clip, one per live layer.</summary>
    Public ReadOnly vp(MAX_CASTERS - 1) As Matrix4

    ''' <summary>xyz the tank's centre, w the radius the shader tests against -
    ''' outside it there is nothing this layer could possibly shadow, so the
    ''' whole projection is skipped.</summary>
    Public ReadOnly sphere(MAX_CASTERS - 1) As Vector4

    Public ready As Boolean
    Private said_count As Integer = -1
    Private probe_tick As Integer

    Public Sub New(scene As MapScene)
        map_scene = scene
    End Sub

    ''' <summary>
    ''' Rebuild every live layer. Called once a frame, AFTER the tanks draw and
    ''' before the shadow tiles resolve.
    '''
    ''' AFTER THE DRAW, and the order is the fix for a real bug. This said
    ''' "BEFORE the tanks draw ... because both read what this writes", and the
    ''' second half was wrong: tank_gbuffer.frag has no tank_maps sampler, so
    ''' the tanks never read this. Only the tiles resolve does. Running before
    ''' the draw cost a ONE FRAME STALE shadow, because advance_movement() is
    ''' inside Draw (TankRenderer.vb:557) - so the bake used last frame's
    ''' inst.position while the meshes drew at this frame's. Backing up made it
    ''' plain: the old position is ahead of the new one, so the shadow sat off
    ''' the front of the hull. Found by the owner at Abbey J8.5, 2026-09-15.
    '''
    ''' What still must hold: this runs before the tiles resolve, which reads
    ''' the array at modRender.vb ~1354. Two stale comments in two files kept
    ''' this bug alive - if the order changes again, fix BOTH.
    ''' </summary>
    Public Sub Render()
        ready = False
        count = 0
        If Not TANK_CAST_SHADOW Then Return
        If map_scene Is Nothing OrElse map_scene.tanks Is Nothing Then Return
        If Not map_scene.tanks.HasTanks Then Return

        Dim live = map_scene.tanks.instances
        If live Is Nothing OrElse live.Count = 0 Then Return

        ' The sun, from the same global the bake reads. A zero here would give a
        ' NaN view matrix and a blank map - the bake guards it for that reason
        ' and so does this.
        Dim dir = LIGHT_POS
        If dir.LengthSquared < 1.0E-6F Then dir = New Vector3(0.4F, 1.0F, 0.3F)
        Dim light_dir = Vector3.Normalize(dir)
        Dim up = If(Math.Abs(light_dir.Y) > 0.99F,
                    New Vector3(0.0F, 0.0F, 1.0F), New Vector3(0.0F, 1.0F, 0.0F))

        Dim eye_pos = map_scene.camera.CAM_POSITION

        ' Pick who casts: nearest first, so when there are more tanks in range
        ' than layers the ones that keep their shadows are the ones being looked
        ' at rather than whichever happened to be earliest in the list.
        Dim picked As New List(Of Tuple(Of Single, TankInstance))
        For Each t In live
            If t Is Nothing OrElse t.vehicle Is Nothing Then Continue For
            Dim d = (t.position - eye_pos).Length
            If d > TANK_SHADOW_RANGE Then Continue For
            picked.Add(Tuple.Create(d, t))
        Next
        If picked.Count = 0 Then Return
        picked.Sort(Function(a, b) a.Item1.CompareTo(b.Item1))

        ensure_target()

        ' Plain depth ordering, NOT the reversed-Z the main pass runs, and put
        ' back exactly as found. Leaving ClearDepth at 1.0 makes every later
        ' clear fail DepthFunc.Greater and the whole scene vanishes behind the
        ' sky - the same restore the sun and lamp bakes both do.
        GL_PUSH_GROUP("MapTankShadow::Render")
        GL.DepthFunc(DepthFunction.Less)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.PolygonOffsetFill)
        ' Steeper than the sun's. A 512 map over a 10 m box is fine texels, and
        ' acne shows at biases a map covering a kilometre never notices.
        GL.PolygonOffset(2.0F, 6.0F)

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, SIZE, SIZE)
        GL.ClearDepth(1.0)

        tankShadowShader.Use()

        Dim n = Math.Min(picked.Count, MAX_CASTERS)
        For i = 0 To n - 1
            Dim t = picked(i).Item2

            ' The hull box, in world. boundsMin/Max come from the visuals at
            ' their offsets - the box the game itself uses, and what a shot
            ' already tests against - so a turret's box is where the turret is.
            Dim lo = t.vehicle.boundsMin, hi = t.vehicle.boundsMax
            Dim half = (hi - lo) * 0.5F
            Dim mid = (hi + lo) * 0.5F
            Dim radius = half.Length

            Dim centre = t.position + Vector3.TransformPosition(mid,
                            Matrix4.CreateRotationY(t.headingRad))

            ' Ortho fitted to the hull with a margin. The margin is what the
            ' shadow is CAST ONTO - a box exactly the size of the tank would
            ' clip its own shadow at the hull edge.
            Dim span = radius * 2.0F + 6.0F
            Dim eye = centre + light_dir * (span * 1.5F)
            Dim view = Matrix4.LookAt(eye, centre, up)
            Dim proj = Matrix4.CreateOrthographicOffCenter(
                -span * 0.5F, span * 0.5F, -span * 0.5F, span * 0.5F,
                0.1F, span * 3.0F)

            ' ZeroToOne, the same remap the sun bake does and for the same
            ' reason: this app sets ClipControl(ZeroToOne) globally, and OpenTK's
            ' ortho emits -1..1. Without these two lines half the depth range
            ' lands negative and clamps, so EVERY texel in the box reads as
            ' occluded and the shadow comes out as a solid square the size of
            ' the ortho footprint rather than the shape of the tank. Written as
            ' a rescale rather than derived constants so it stays correct
            ' whatever convention the ortho used.
            proj.M33 *= 0.5F
            proj.M43 = (proj.M43 + 1.0F) * 0.5F

            vp(i) = view * proj
            sphere(i) = New Vector4(centre.X, centre.Y, centre.Z, span * 0.75F)

            GL.NamedFramebufferTextureLayer(fbo.fbo_id,
                FramebufferAttachment.DepthAttachment, depth_tex.texture_id, 0, i)
            GL.Clear(ClearBufferMask.DepthBufferBit)

            ' THE TANK ITSELF. DrawDepth is TankRenderer's own inner loop with
            ' the materials, lights, armour and recoil taken out - so the
            ' shadow is skinned by the same code, with the same palette and the
            ' same base-vertex-zero rule, as the tank a viewer is looking at.
            ' Re-deriving any of that here is how a shadow ends up beside its
            ' caster rather than under it, and the base-vertex rule alone had
            ' already been measured silently dropping group 1 of all 31
            ' multi-group meshes.
            '
            ' It sets sh("mvp") per mesh and uploads the bone palette; it does
            ' NOT Use() the program or touch depth or cull state, which is why
            ' the state block above and tankShadowShader.Use() still wrap it.
            map_scene.tanks.DrawDepth(tankShadowShader, vp(i), t)
        Next

        tankShadowShader.StopUse()

        GL.Disable(EnableCap.PolygonOffsetFill)
        GL.PolygonOffset(0.0F, 0.0F)
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0)
        GL.Enable(EnableCap.CullFace)
        MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, MainFBO.width, MainFBO.height)
        GL_POP_GROUP()

        count = n
        ready = True

        ' Once, and then only when the number changes - enough to answer "is it
        ' casting at all" from the log without a line a frame.
        ' TRACKING PROBE. Every 60th frame, say where caster 0's tank IS and
        ' where its shadow box was centred. If the tank moves and the centre
        ' does not, the shadow is pinned to something stale and no amount of
        ' looking at the ground will say which.
        probe_tick += 1
        If TANK_SHADOW_DEBUG AndAlso probe_tick Mod 60 = 0 AndAlso n > 0 Then
            Dim t0 = picked(0).Item2
            LogThis("tank shadow: {0} caster(s); tank 0 at ({1:0.0}, {2:0.0}, {3:0.0}) centre ({4:0.0}, {5:0.0}, {6:0.0})",
                    n, t0.position.X, t0.position.Y, t0.position.Z,
                    sphere(0).X, sphere(0).Y, sphere(0).Z)
        End If

        If n <> said_count Then
            said_count = n
            If TANK_SHADOW_DEBUG Then
                LogThis("tank shadow: {0} caster(s) at {1}x{1}, range {2:0} m", n, SIZE, TANK_SHADOW_RANGE)

                ' BEHIND THE SWITCH, AND IT HAS TO BE. verify() is a 512x512
                ' float READBACK plus a 262,144 element CPU loop, and a readback
                ' stalls the pipeline until the GPU catches up. It fires on every
                ' change of caster count, and that count THRASHES as hulls cross
                ' the range boundary - Tank AI work measured 21 of these in 400
                ' log lines, against the owner reporting the UI freezing about
                ' once a second.
                '
                ' It was a diagnostic for three bugs in this pass that are now
                ' fixed, and it was left running. Silencing its LOG would have
                ' hidden the stall rather than removed it: the readback is the
                ' cost, not the line it prints.
                verify(0)
            End If
        End If
    End Sub

    ''' <summary>
    ''' Read layer 0 back and say what is actually in it.
    '''
    ''' Because three bugs in this pass were each diagnosed by looking at pixels
    ''' and guessing, and the guesses were wrong twice. A depth map that is all
    ''' 1.0 means NOTHING DREW - the box missed the frustum. All 0.0 means
    ''' everything drew at the near plane, which is the saturation that paints
    ''' the whole ortho footprint as shadow. A spread between the two is a real
    ''' box. One readback of 512x512 floats, only when the caster count changes.
    ''' </summary>
    Private Sub verify(i As Integer)
        If depth_tex Is Nothing OrElse i >= count Then Return
        Try
            Dim px(SIZE * SIZE - 1) As Single
            GL.GetTextureSubImage(depth_tex.texture_id, 0, 0, 0, i, SIZE, SIZE, 1,
                                  PixelFormat.DepthComponent, PixelType.Float,
                                  px.Length * 4, px)
            Dim lo = Single.MaxValue, hi = Single.MinValue
            Dim sum As Double = 0, cleared = 0
            For k = 0 To px.Length - 1
                Dim v = px(k)
                If v < lo Then lo = v
                If v > hi Then hi = v
                sum += v
                If v >= 0.9999F Then cleared += 1
            Next
            LogThis("tank shadow: layer {0} depth min {1:0.0000} max {2:0.0000} mean {3:0.0000}, {4:0.0}% still cleared",
                    i, lo, hi, sum / px.Length, 100.0 * cleared / px.Length)
        Catch ex As Exception
            LogThis("tank shadow: could not read layer {0} back - {1}", i, ex.Message)
        End Try
    End Sub

    ''' <summary>Bind the layers where the resolve expects them.</summary>
    Public Sub BindUnit(unit As Integer)
        If depth_tex IsNot Nothing Then depth_tex.BindUnit(unit)
    End Sub

    Private Sub ensure_target()
        If depth_tex IsNot Nothing AndAlso allocated = MAX_CASTERS Then Return
        Dispose_gl()

        depth_tex = GLTexture.Create(TextureTarget.Texture2DArray, "TankShadowDepth")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
        ' Border of 1.0 - the far plane - so a pixel projecting outside the map
        ' reads "nothing between me and the sun" rather than clamping to whatever
        ' the edge texel happened to hold.
        GL.TextureParameter(depth_tex.texture_id, TextureParameterName.TextureBorderColor,
                            New Single() {1.0F, 1.0F, 1.0F, 1.0F})
        ' A comparison sampler, so the hardware does the depth test and the PCF
        ' filter in one fetch, exactly as the sun tiles do.
        ' READ BACK, not assumed. A sampler2DArrayShadow whose compare mode did
        ' not take is UNDEFINED in the spec and returns 0 on this driver - which
        ' reads as "shadowed" at every texel including the 86% of the map that is
        ' still at the far plane, and paints the whole ortho footprint. That is
        ' exactly the square that has been on screen all evening, and it is
        ' indistinguishable from a geometry bug by looking at it.
        GL.TextureParameter(depth_tex.texture_id, TextureParameterName.TextureCompareMode,
                            CInt(TextureCompareMode.CompareRefToTexture))
        GL.TextureParameter(depth_tex.texture_id, TextureParameterName.TextureCompareFunc,
                            CInt(All.Lequal))

        Dim got_mode(0) As Integer, got_func(0) As Integer
        GL.GetTextureParameter(depth_tex.texture_id, GetTextureParameter.TextureCompareMode, got_mode)
        GL.GetTextureParameter(depth_tex.texture_id, GetTextureParameter.TextureCompareFunc, got_func)
        LogThis("tank shadow: compare mode {0} (want {1}), func {2} (want {3})",
                got_mode(0), CInt(TextureCompareMode.CompareRefToTexture),
                got_func(0), CInt(All.Lequal))
        depth_tex.Storage3D(1, DirectCast(PixelInternalFormat.DepthComponent32f, SizedInternalFormat), SIZE, SIZE, MAX_CASTERS)

        fbo = GLFramebuffer.Create("TankShadowFBO")
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.None)
        GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)
        allocated = MAX_CASTERS
    End Sub

    Private Sub Dispose_gl()
        fbo?.Dispose() : fbo = Nothing
        depth_tex?.Dispose() : depth_tex = Nothing
        allocated = 0
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose_gl()
    End Sub
End Class

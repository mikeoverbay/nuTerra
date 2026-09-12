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
''' WHAT IT DRAWS TODAY IS THE HULL BOX, not the mesh. The skinned draw lives in
''' MapTanks, which is the Tank AI session's file, and one owner per file is the
''' rule we agreed. A box proves every part of this - the layers, the matrices,
''' the range test, the fade, the sampling - and produces a real shadow on the
''' ground today rather than correct code that cannot be looked at. When that
''' session hands over a depth-only draw, ONE call here changes and the shadow
''' becomes tank shaped. See draw_box.
''' </summary>
Public Class MapTankShadow
    Implements IDisposable

    ''' <summary>Texels a side, per tank. The owner's number.</summary>
    Public Const SIZE As Integer = 512

    ''' <summary>Most tanks that can cast at once. Thirty is a full roster; the
    ''' rest are lit unshadowed rather than dropped, which degrades instead of
    ''' failing.</summary>
    Public Const MAX_CASTERS As Integer = 32

    Private ReadOnly map_scene As MapScene

    Private depth_tex As GLTexture
    Private fbo As GLFramebuffer
    Private box_vao As GLVertexArray
    Private box_vbo As GLBuffer
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

    Public Sub New(scene As MapScene)
        map_scene = scene
    End Sub

    ''' <summary>
    ''' Rebuild every live layer. Called once a frame, BEFORE the tanks draw and
    ''' before the shadow tiles resolve, because both read what this writes.
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
        build_box()

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

            ' SCALE FIRST, and it was missing. The VAO is a UNIT cube, so
            ' without this every tank cast a 2 m box sitting at its hull centre
            ' whatever size it actually is - which is what "its all wrong"
            ' looked like on screen. half was computed and then never used.
            Dim model = Matrix4.CreateScale(half) *
                        Matrix4.CreateTranslation(mid) *
                        Matrix4.CreateRotationY(t.headingRad) *
                        Matrix4.CreateTranslation(t.position)
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

            GL.UniformMatrix4(tankShadowShader("mvp"), False, model * vp(i))
            draw_box()
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
        If n <> said_count Then
            said_count = n
            LogThis("tank shadow: {0} caster(s) at {1}x{1}, range {2:0} m", n, SIZE, TANK_SHADOW_RANGE)
            verify(0)
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

    ''' <summary>
    ''' ONE CALL, AND IT IS THE SEAM. Today it rasterises the hull box; when
    ''' MapTanks offers a depth-only draw this becomes that call and the shadow
    ''' stops being a rectangle. Nothing else in this file changes.
    ''' </summary>
    Private Sub draw_box()
        box_vao.Bind()
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36)
    End Sub

    Private Sub build_box()
        If box_vao IsNot Nothing Then Return
        ' A unit cube about the origin, scaled by the model matrix. 36 vertices,
        ' no index buffer - it is twelve triangles once per frame per tank and
        ' an index buffer would be ceremony.
        Dim c()() As Single = {
            New Single() {-1, -1, -1}, New Single() {1, -1, -1}, New Single() {1, 1, -1},
            New Single() {-1, -1, -1}, New Single() {1, 1, -1}, New Single() {-1, 1, -1},
            New Single() {-1, -1, 1}, New Single() {1, 1, 1}, New Single() {1, -1, 1},
            New Single() {-1, -1, 1}, New Single() {-1, 1, 1}, New Single() {1, 1, 1},
            New Single() {-1, -1, -1}, New Single() {-1, 1, 1}, New Single() {-1, -1, 1},
            New Single() {-1, -1, -1}, New Single() {-1, 1, -1}, New Single() {-1, 1, 1},
            New Single() {1, -1, -1}, New Single() {1, -1, 1}, New Single() {1, 1, 1},
            New Single() {1, -1, -1}, New Single() {1, 1, 1}, New Single() {1, 1, -1},
            New Single() {-1, -1, -1}, New Single() {1, -1, 1}, New Single() {1, -1, -1},
            New Single() {-1, -1, -1}, New Single() {-1, -1, 1}, New Single() {1, -1, 1},
            New Single() {-1, 1, -1}, New Single() {1, 1, -1}, New Single() {1, 1, 1},
            New Single() {-1, 1, -1}, New Single() {1, 1, 1}, New Single() {-1, 1, 1}}
        Dim v(36 * 3 - 1) As Single
        For i = 0 To 35
            v(i * 3) = c(i)(0) : v(i * 3 + 1) = c(i)(1) : v(i * 3 + 2) = c(i)(2)
        Next

        box_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tankShadowBox")
        box_vbo.Storage(v.Length * 4, v, BufferStorageFlags.None)
        box_vao = GLVertexArray.Create("tankShadowBoxVao")
        box_vao.VertexBuffer(0, box_vbo, IntPtr.Zero, 3 * 4)
        box_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        box_vao.AttribBinding(0, 0)
        box_vao.EnableAttrib(0)
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
        box_vbo?.Dispose() : box_vbo = Nothing
        box_vao?.Dispose() : box_vao = Nothing
    End Sub
End Class

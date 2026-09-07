Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

Imports OpenTK.Mathematics

''' <summary>
''' One depth cube per .campath lamp, baked once and sampled per frame in
''' deferred.frag. Without it every lamp lights through every wall - path_lights
''' had no visibility term at all, which is why a range 50 lamp lit 65% of the
''' frame instead of making a pool.
'''
''' A CUBE, not a downward cone. A lamp 6 m up with a 50 m range throws light
''' almost horizontally at the edge of its own radius: a 120 degree cone from
''' that height covers about 11 m of ground, so the outer 39 m of the pool would
''' still shine through walls. There is no cone wide enough - it is the whole
''' sphere or nothing.
'''
''' Baked once because nothing here moves. The lamps are authored in Path Studio
''' and the geometry is static, so the per-frame shadow re-render a normal
''' engine needs is not needed here; the cost is at load. Re-baking is per lamp,
''' so nudging one lamp costs one lamp's worth of work, not all of them.
'''
''' WHAT CASTS: terrain, static models and trees - the same three the sun bake
''' draws, through the same three shaders. Deliberately the same set. A lamp
''' that shadowed a different world from the sun would read as a bug in the
''' lamp, and there would be no cheap way to see which of the two was wrong.
''' </summary>
Public Class MapLampShadow
    Implements IDisposable

    ReadOnly scene As MapScene

    ''' <summary>
    ''' Edge of one cube face. Memory is 6 faces x 2 bytes x this squared PER
    ''' LAMP - 3 MiB each at 512, so 32 lamps is 96 MiB and one lamp is nothing.
    ''' Only as many layers as there are lamps are ever allocated.
    '''
    ''' 512 puts about 0.2 m on a texel at 50 m and 0.05 m at 12 m. A lamp pool
    ''' is soft edged by nature, so resolution buys less here than for the sun.
    ''' </summary>
    Public Shared FACE_SIZE As Integer = 512

    ''' <summary>
    ''' Near plane, metres. A perspective depth buffer spends most of its
    ''' precision just past the near plane, so this wants to be as far out as
    ''' the geometry allows - 0.5 m from the bulb is still inside the lamp post.
    ''' At 0.1 the far half of a 50 m range loses most of its resolution.
    ''' </summary>
    Public Shared NEAR_M As Single = 0.5F

    Public Shared MAX_LAMPS As Integer = 32

    ''' <summary>
    ''' Edge of each lamp's baked light VOLUME, in voxels.
    '''
    ''' A separate thing from the shadow cube and derived from it: the cube
    ''' answers "is this direction occluded", the volume answers "how much of
    ''' this lamp reaches this point in the air", which is fixed for a static
    ''' lamp in static geometry and so is worth baking once.
    '''
    ''' 64 over a 20 m range is ~0.6 m a voxel - coarse for a hard shadow, and
    ''' right for fog, which has no hard edges. R8, so 262 KiB a lamp.
    ''' </summary>
    Public Shared VOL_RES As Integer = 64

    Public fbo As GLFramebuffer
    Public depth_tex As GLTexture
    ''' <summary>Every lamp's light field, stacked along Z - lamp i occupies
    ''' z in [i*VOL_RES, (i+1)*VOL_RES). Sampled TRILINEAR, which is what makes
    ''' a marched shaft soft instead of stair-stepped.</summary>
    Public vol_tex As GLTexture
    ''' <summary>How many lamps have a cube in the array. Layer i is light i.</summary>
    Public layers As Integer
    Public ready As Boolean
    Public bake_ms As Long

    ' The six faces of a cube map, in GL's own order: +X -X +Y -Y +Z -Z.
    '
    ' The up vectors are not arbitrary, and not what a right handed LookAt would
    ' pick. Cube maps are sampled in a LEFT handed frame, so a face rendered
    ' with the obvious up comes out mirrored - and a mirrored shadow does not
    ' look obviously wrong, it just puts the occluder on the wrong side. These
    ' are the canonical six; verify() below is the check that they went in the
    ' right way round.
    Shared ReadOnly FACE_DIR() As Vector3 = {
        New Vector3(1.0F, 0.0F, 0.0F), New Vector3(-1.0F, 0.0F, 0.0F),
        New Vector3(0.0F, 1.0F, 0.0F), New Vector3(0.0F, -1.0F, 0.0F),
        New Vector3(0.0F, 0.0F, 1.0F), New Vector3(0.0F, 0.0F, -1.0F)}

    Shared ReadOnly FACE_UP() As Vector3 = {
        New Vector3(0.0F, -1.0F, 0.0F), New Vector3(0.0F, -1.0F, 0.0F),
        New Vector3(0.0F, 0.0F, 1.0F), New Vector3(0.0F, 0.0F, -1.0F),
        New Vector3(0.0F, -1.0F, 0.0F), New Vector3(0.0F, -1.0F, 0.0F)}

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    '''<summary>Bytes the cube array occupies for a given lamp count.</summary>
    Public Shared Function bytes_for(lamps As Integer) As Long
        Return CLng(FACE_SIZE) * CLng(FACE_SIZE) * 2L * 6L * CLng(Math.Max(lamps, 0))
    End Function

    ''' <summary>
    ''' Render every lamp's surroundings into its own cube. Call after the cam
    ''' path is loaded - the lamps come from it - and again whenever it reloads.
    ''' </summary>
    Public Sub Bake()
        ready = False

        Dim cp = scene.cam_path
        If cp Is Nothing OrElse Not cp.loaded OrElse cp.lights Is Nothing OrElse cp.lights.Length = 0 Then
            LogThis("lamp shadow: no lamps on this map - nothing baked")
            Return
        End If

        ' Cubes for the MAP lights only - they come first in lights(), so layer
        ' i is light i everywhere. Bulb lights are lit unshadowed; a map with
        ' 145 street lamps cannot carry 145 cubes.
        Dim n = Math.Min(cp.path_light_count(), MAX_LAMPS)
        If n = 0 Then
            LogThis("lamp shadow: no map lamps to bake ({0} bulb light(s) lit unshadowed)", cp.lights.Length)
            Return
        End If
        Dim clock = Stopwatch.StartNew()

        If depth_tex Is Nothing OrElse layers <> n Then
            Dispose_gl()
            layers = n
            create_target()
        End If

        ' Plain depth ordering, NOT the reversed-Z the main pass runs. Both of
        ' these are global state set once at startup, so they have to go back
        ' exactly as they were - leaving ClearDepth at 1.0 makes every later
        ' clear fail DepthFunc.Greater and the whole scene vanishes behind the
        ' sky. Same restore the sun bake does, for the same reason.
        GL.DepthFunc(DepthFunction.Less)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        ' Off, because WoT models are hollow shells with no bottom faces. With
        ' culling on, a lamp beside a building reads the INSIDE of its roof.
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.PolygonOffsetFill)
        ' Steeper than the sun's 1.5/4.0. A lamp sits metres from what it lights
        ' rather than kilometres away, so the same depth slope covers far fewer
        ' texels and acne shows at biases the sun never notices.
        GL.PolygonOffset(2.5F, 8.0F)

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, FACE_SIZE, FACE_SIZE)
        GL.ClearDepth(1.0)

        For i = 0 To n - 1
            Dim lp = lamp_world_pos(i)
            Dim far_m = Math.Max(cp.lights(i).range_m, 1.0F)

            For face = 0 To 5
                GL.NamedFramebufferTextureLayer(fbo.fbo_id, FramebufferAttachment.DepthAttachment,
                                                depth_tex.texture_id, 0, i * 6 + face)
                GL.Clear(ClearBufferMask.DepthBufferBit)

                Dim vp = face_view_proj(lp, face, far_m)
                draw_terrain(vp)
                draw_models(vp)
                draw_trees(vp)
            Next
        Next

        GL.Disable(EnableCap.PolygonOffsetFill)
        GL.Enable(EnableCap.CullFace)

        ' restore the reversed-Z state the rest of the engine assumes
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        clock.Stop()
        bake_ms = clock.ElapsedMilliseconds
        ready = True

        LogThis("lamp shadow: baked {0} lamp(s) x 6 faces at {1}x{1} 16 ({2:0.0} MiB) in {3} ms",
                n, FACE_SIZE, bytes_for(n) / (1024.0 * 1024.0), bake_ms)

        verify(0)

        bake_volumes(n)
    End Sub

    ''' <summary>
    ''' The lamp in WORLD space. The file stores Y as metres ABOVE THE TERRAIN -
    ''' Path Studio places on a 2D map and cannot know the ground - so it is
    ''' resolved here, and it MUST be resolved the same way modRender does for
    ''' the light itself. A bake at one height and a light at another shadows
    ''' the scene from a lamp that is not there.
    ''' </summary>
    Private Function lamp_world_pos(i As Integer) As Vector3
        Return scene.cam_path.world_pos(i)
    End Function

    ''' <summary>
    ''' View * projection for one cube face, already in 0..1 depth.
    '''
    ''' The 0..1 remap is NOT the one the sun bake uses. That one is
    '''     M33 *= 0.5 : M43 = (M43 + 1) * 0.5
    ''' which is right for an ORTHOGRAPHIC matrix, where w comes out as 1 and
    ''' z_ndc is a plain z*M33 + M43. A perspective matrix divides by w = -z, so
    ''' the substitution has to happen inside the quotient instead:
    '''     z01 = 0.5*z_ndc + 0.5 = (z*(0.5*M33 - 0.5) + 0.5*M43) / -z
    ''' Using the ortho form here yields depths that look entirely plausible -
    ''' monotonic, in range - and are wrong everywhere, which is the worst kind
    ''' of wrong to debug.
    '''
    ''' Worked through, the stored value is
    '''     z01 = (F - F*N/t) / (F - N)      t = distance along the major axis
    ''' and that is the expression path_lights inverts to build its reference.
    ''' </summary>
    Private Function face_view_proj(eye As Vector3, face As Integer, far_m As Single) As Matrix4
        Dim view = Matrix4.LookAt(eye, eye + FACE_DIR(face), FACE_UP(face))

        Dim proj = Matrix4.CreatePerspectiveFieldOfView(
            CSng(Math.PI * 0.5), 1.0F, NEAR_M, far_m)

        proj.M33 = proj.M33 * 0.5F - 0.5F
        proj.M43 = proj.M43 * 0.5F

        Return view * proj
    End Function

    ''' <summary>
    ''' Reads the texel straight down from a lamp and says what distance it
    ''' holds, next to the distance that has to be there.
    '''
    ''' This one number checks the whole chain at once - face order, the up
    ''' vectors' handedness, the perspective remap and the depth encoding. The
    ''' -Y face looks at the ground directly under the lamp, and the lamp's
    ''' authored Y IS its height above that ground, so measured and expected
    ''' must agree. They disagree loudly if any link is wrong, where a rendered
    ''' frame would only look slightly off.
    ''' </summary>
    Private Sub verify(i As Integer)
        If Not ready OrElse depth_tex Is Nothing OrElse i >= layers Then Return

        Dim px(0) As Single
        Dim mid = FACE_SIZE \ 2
        ' Face 3 is -Y. The z index into a cube array is lamp * 6 + face.
        GL.GetTextureSubImage(depth_tex.texture_id, 0,
                              mid, mid, i * 6 + 3, 1, 1, 1,
                              PixelFormat.DepthComponent, PixelType.Float,
                              4, px)

        Dim far_m = Math.Max(scene.cam_path.lights(i).range_m, 1.0F)
        Dim z = px(0)

        If z >= 0.9999F Then
            LogThis("lamp shadow: lamp {0} sees nothing below it - the ground did not draw into the -Y face", i)
            Return
        End If

        ' Invert z01 = (F - F*N/t) / (F - N)
        Dim denom = far_m - z * (far_m - NEAR_M)
        Dim measured = If(Math.Abs(denom) < 0.0001F, -1.0F, far_m * NEAR_M / denom)
        Dim expected = scene.cam_path.lights(i).pos.Y

        LogThis("lamp shadow: lamp {0} ground below reads {1:0.00} m, authored height {2:0.00} m (agreement = the cube is the right way round)",
                i, measured, expected)
    End Sub

    ' The three draws below are duplicated from MapSunShadow rather than shared
    ' with it. Factoring them out would mean editing the sun bake, and the sun
    ' bake is the one that currently works - the same trade the lamp BRDF makes
    ' against the sun's in deferred.frag.

    Private Sub draw_terrain(vp As Matrix4)
        If Not scene.TERRAIN_LOADED Then Return

        sunDepthTerrainShader.Use()
        GL.UniformMatrix4(sunDepthTerrainShader("sunViewProj"), False, vp)

        scene.terrain.all_chunks_vao.Bind()
        scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                                    New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
        Next

        sunDepthTerrainShader.StopUse()
    End Sub

    Private Sub draw_models(vp As Matrix4)
        If Not scene.MODELS_LOADED OrElse Not DONT_BLOCK_MODELS Then Return

        sunDepthModelShader.Use()
        GL.UniformMatrix4(sunDepthModelShader("sunViewProj"), False, vp)

        scene.static_models.allMapModels.Bind()
        scene.static_models.indirect_shadow_mapping.Bind(BufferTarget.DrawIndirectBuffer)

        ' Outland left out, same as the sun bake. Those are the distant cliffs
        ' ringing the arena; nothing 50 m from a street lamp is outland.
        Dim n = scene.static_models.indirectShadowMappingDrawCount -
                scene.static_models.indirectShadowOutlandDrawCount

        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                                     IntPtr.Zero, n, 0)

        sunDepthModelShader.StopUse()
    End Sub

    Private Sub draw_trees(vp As Matrix4)
        If Not scene.TREES_LOADED OrElse Not DONT_BLOCK_TREES Then Return
        scene.trees.sun_depth_pass(vp)
    End Sub

    ''' <summary>
    ''' Turn the cubes into a light field per lamp, with a compute pass.
    '''
    ''' One shadow lookup per voxel, once, instead of one per march step per
    ''' pixel per frame. The win that matters is not the arithmetic saved - it
    ''' is that a 3D texture FILTERS: the march reads a smoothly interpolated
    ''' visibility and the shaft edge comes out soft, where sampling the cube
    ''' per step gives a hard yes/no and the steps show as bands.
    ''' </summary>
    Private Sub bake_volumes(n As Integer)
        If depth_tex Is Nothing OrElse n <= 0 Then Return

        Dim clock = Stopwatch.StartNew()

        If vol_tex Is Nothing Then
            vol_tex = GLTexture.Create(TextureTarget.Texture3D, "LampLightVolume")
            vol_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            vol_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            ' ClampToEdge on every axis. A voxel outside the sphere is zero, so
            ' clamping repeats zero and a ray leaving the volume simply stops
            ' being lit - wrapping would light it from the far side instead.
            vol_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
            vol_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
            vol_tex.Parameter(TextureParameterName.TextureWrapR, TextureWrapMode.ClampToEdge)
            vol_tex.Storage3D(1, SizedInternalFormat.R8, VOL_RES, VOL_RES, VOL_RES * n)
        End If

        lampVolShader.Use()
        depth_tex.BindUnit(0)
        GL.BindImageTexture(0, vol_tex.texture_id, 0, True, 0,
                            TextureAccess.WriteOnly, SizedInternalFormat.R8)

        GL.Uniform1(lampVolShader("vres"), VOL_RES)
        GL.Uniform1(lampVolShader("lamp_shadow_near"), NEAR_M)
        GL.Uniform1(lampVolShader("lamp_shadow_bias"), LAMP_SHADOW_BIAS)

        Dim groups = (VOL_RES + 3) \ 4
        For i = 0 To n - 1
            GL.Uniform1(lampVolShader("lamp_index"), i)
            GL.Uniform1(lampVolShader("lamp_range"),
                        Math.Max(scene.cam_path.lights(i).range_m, 1.0F))
            GL.DispatchCompute(groups, groups, groups)
        Next

        ' The march samples this as a TEXTURE next frame, not as an image, so
        ' the texture-fetch barrier is the one that matters here.
        GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit Or
                         MemoryBarrierFlags.ShaderImageAccessBarrierBit)
        lampVolShader.StopUse()

        clock.Stop()
        LogThis("lamp shadow: light volumes {0}^3 x {1} lamp(s) ({2:0.0} MiB) in {3} ms",
                VOL_RES, n,
                CLng(VOL_RES) * VOL_RES * VOL_RES * n / (1024.0 * 1024.0),
                clock.ElapsedMilliseconds)

        report_volume(0)
    End Sub

    ''' <summary>
    ''' Say how much of a lamp's light field is actually SHADOWED.
    '''
    ''' This is the number that separates "the shafts are broken" from "there is
    ''' nothing here to cast one". A beam is the boundary between lit air and
    ''' unlit air, so if every voxel inside the sphere comes back lit there is no
    ''' boundary anywhere and no march, however tuned, will draw one. A lamp in
    ''' the open genuinely has no shaft to find - it needs an occluder between
    ''' itself and the air.
    '''
    ''' Counted over the inscribed SPHERE, not the cube: the cube's corners are
    ''' outside the range and always bake to zero, and letting them into the
    ''' average reports about 48% shadowed on a lamp with no occluders at all.
    ''' </summary>
    Private Sub report_volume(i As Integer)
        If vol_tex Is Nothing OrElse i >= layers Then Return

        Dim vox(VOL_RES * VOL_RES * VOL_RES - 1) As Byte
        GL.GetTextureSubImage(vol_tex.texture_id, 0,
                              0, 0, i * VOL_RES, VOL_RES, VOL_RES, VOL_RES,
                              PixelFormat.Red, PixelType.UnsignedByte,
                              vox.Length, vox)

        Dim inside = 0, lit = 0, part = 0
        Dim half = (VOL_RES - 1) * 0.5F
        For z = 0 To VOL_RES - 1
            For y = 0 To VOL_RES - 1
                For x = 0 To VOL_RES - 1
                    Dim dx = (x - half) / half, dy = (y - half) / half, dz = (z - half) / half
                    If dx * dx + dy * dy + dz * dz > 1.0F Then Continue For
                    inside += 1
                    Dim v = vox((z * VOL_RES + y) * VOL_RES + x)
                    If v > 240 Then
                        lit += 1
                    ElseIf v > 15 Then
                        part += 1
                    End If
                Next
            Next
        Next

        If inside = 0 Then Return
        LogThis("lamp shadow: lamp {0} light field - {1:0.0}% lit, {2:0.0}% partly, {3:0.0}% SHADOWED " &
                "(all lit = nothing to cast a shaft)",
                i, 100.0 * lit / inside, 100.0 * part / inside,
                100.0 * (inside - lit - part) / inside)
    End Sub

    Private Sub create_target()
        depth_tex = GLTexture.Create(TextureTarget.TextureCubeMapArray, "LampShadowDepth")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        depth_tex.Parameter(TextureParameterName.TextureWrapR, TextureWrapMode.ClampToEdge)
        ' Hardware compare, so a single fetch is already a 2x2 PCF average.
        depth_tex.Parameter(TextureParameterName.TextureCompareMode, CInt(TextureCompareMode.CompareRefToTexture))
        depth_tex.Parameter(TextureParameterName.TextureCompareFunc, CInt(All.Lequal))
        depth_tex.Storage3D(1, DirectCast(InternalFormat.DepthComponent16, SizedInternalFormat),
                            FACE_SIZE, FACE_SIZE, layers * 6)

        fbo = GLFramebuffer.Create("LampShadowFBO")
        ' The face is attached inside Bake; this only settles the read/draw state.
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.None)
        GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)
    End Sub

    Private Sub Dispose_gl()
        depth_tex?.Dispose()
        depth_tex = Nothing
        vol_tex?.Dispose()
        vol_tex = Nothing
        fbo?.Dispose()
        fbo = Nothing
        layers = 0
        ready = False
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose_gl()
        GC.SuppressFinalize(Me)
    End Sub
End Class

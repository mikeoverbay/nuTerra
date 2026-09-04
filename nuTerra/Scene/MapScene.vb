Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapScene
    Implements IDisposable

    'map pick Dictionary
    Public PICK_DICTIONARY As New Dictionary(Of UInteger, String)
    Public PICKED_STRING As String = ""
    Public PICKED_MODEL_INDEX As Integer

    'Draw Enable Flags. Items wont be rendered if these are False
    Public TERRAIN_LOADED As Boolean
    Public OUTLAND_LOADED As Boolean
    Public TREES_LOADED As Boolean
    Public ROADS_LOADED As Boolean
    Public DECALS_LOADED As Boolean
    Public MODELS_LOADED As Boolean
    Public BASES_LOADED As Boolean
    Public SKY_LOADED As Boolean
    Public WATER_LOADED As Boolean
    Public BASE_RINGS_LOADED As Boolean

    Public mouse_timer As New Timer

    ReadOnly mapName As String
    Public sky As New MapSky(Me)
    Public terrain As New MapTerrain(Me)
    Public static_models As New MapStaticModels(Me)
    Public water As New MapWater(Me)
    Public base_rings As New MapBaseRings(Me)
    Public mini_map As New MapMinimap(Me)
    Public fog As New MapFog(Me)
    Public particles As New MapParticles
    Public trees As New MapTrees(Me)
    Public roads As New MapRoads(Me)
    Public cursor As New MapCursor(Me)
    Public camera As New MapCamera(Me)
    Public decals As New MapDecals(Me)
    Public sun_shadow As New MapSunShadow(Me)
    Public flight_bake As New MapFlightBake(Me)
    Public cam_path As New MapCamPath
    Public CC_LUT_ID As GLTexture
    Public ENV_BRDF_LUT_ID As GLTexture

    Public shadow_mapping_matrix As GLBuffer

    Public Sub New(mapName As String)
        Me.mapName = mapName

        shadow_mapping_matrix = GLBuffer.Create(BufferTarget.UniformBuffer, "shadow_mapping_matrix")
        shadow_mapping_matrix.StorageNullData(
            4 * Marshal.SizeOf(Of Matrix4),
            BufferStorageFlags.DynamicStorageBit)
        shadow_mapping_matrix.BindBase(3)
    End Sub

    Public Sub DrawLightFrustum()
        GL_PUSH_GROUP("MapScene::DrawLightFrustum")

        GL.Disable(EnableCap.DepthTest)

        frustumShader.Use()

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.Points, 0, 1)

        frustumShader.StopUse()

        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Private Shared Function getFrustumCornersWorldSpace(proj As Matrix4, view As Matrix4) As List(Of Vector4)
        Dim inv = Matrix4.Invert(view * proj)

        Dim frustumCorners As New List(Of Vector4)
        For x = 0 To 1
            For y = 0 To 1
                For z = 0 To 1
                    Dim pt = New Vector4(2.0F * x - 1.0F, 2.0F * y - 1.0F, 2.0F * z - 1.0F, 1.0F) * inv
                    frustumCorners.Add(pt / pt.W)
                Next
            Next
        Next

        Return frustumCorners
    End Function

    ' https://docs.nvidia.com/gameworks/content/gameworkslibrary/graphicssamples/opengl_samples/cascadedshadowmapping.htm
    ' https://learnopengl.com/code_viewer_gh.php?code=src/8.guest/2021/2.csm/shadow_mapping.cpp
    '''<summary>
    ''' Light space matrix for one cascade.
    '''
    ''' The box is a fixed size and snapped to the shadow map's own texel grid.
    ''' Fitting it tightly to the frustum corners instead, which is the obvious
    ''' thing to do, makes the box change size and position every time the camera
    ''' turns; the depth samples then land on different texels from one frame to
    ''' the next and every shadow edge crawls. Sizing the box from a sphere round
    ''' the frustum makes it rotation invariant, and snapping its origin to whole
    ''' texels makes it translation invariant to within one texel.
    '''
    ''' The depth range is still measured from the corners, so casters behind the
    ''' split stay inside it.
    '''</summary>
    Private Function getLightSpaceMatrix(nearPlane As Single, farPlane As Single) As Matrix4
        Dim proj = Matrix4.CreatePerspectiveFieldOfView(
            FieldOfView,
            MainFBO.width / MainFBO.height,
            nearPlane, farPlane)

        Dim corners = getFrustumCornersWorldSpace(proj, camera.PerViewData.view)

        Dim center = Vector3.Zero
        For Each v In corners
            center += v.Xyz
        Next
        center /= corners.Count

        ' A sphere round the split: the same size whichever way the camera faces.
        Dim radius = 0.0F
        For Each v In corners
            radius = Math.Max(radius, (v.Xyz - center).Length)
        Next
        radius = CSng(Math.Ceiling(radius * 16.0F) / 16.0F)

        ' Straight up would make LookAt degenerate.
        Dim light_dir = LIGHT_POS.Normalized()
        Dim up = If(Math.Abs(light_dir.Y) > 0.99F, Vector3.UnitZ, Vector3.UnitY)

        ' Snap the centre to the texel grid of the map we are about to render.
        Dim snap_view = Matrix4.LookAt(light_dir + center, center, up)
        Dim texel = radius * 2.0F / ShadowMappingFBO.WIDTH
        Dim c = New Vector4(center, 1.0F) * snap_view
        c.X = CSng(Math.Floor(c.X / texel)) * texel
        c.Y = CSng(Math.Floor(c.Y / texel)) * texel
        center = (c * Matrix4.Invert(snap_view)).Xyz

        Dim light_view_matrix = Matrix4.LookAt(light_dir + center, center, up)

        ' Depth still comes from the corners so casters behind the split survive.
        Dim max = Vector3.NegativeInfinity
        Dim min = Vector3.PositiveInfinity
        For Each v In corners
            Dim trf = v * light_view_matrix
            min = Vector3.ComponentMin(min, trf.Xyz)
            max = Vector3.ComponentMax(max, trf.Xyz)
        Next

        Dim zMult = 10.0F
        If min.Z < 0 Then
            min.Z *= zMult
        Else
            min.Z /= zMult
        End If
        If max.Z < 0 Then
            max.Z /= zMult
        Else
            max.Z *= zMult
        End If

        Dim c_ls = New Vector4(center, 1.0F) * light_view_matrix
        Dim light_proj_matrix = Matrix4.CreateOrthographicOffCenter(
            c_ls.X - radius, c_ls.X + radius,
            c_ls.Y - radius, c_ls.Y + radius,
            min.Z, max.Z)

        ' Fix for reversed-z
        light_proj_matrix.M33 = 1.0F / (max.Z - min.Z)
        light_proj_matrix.M43 = max.Z / (max.Z - min.Z)

        Return light_view_matrix * light_proj_matrix
    End Function

    Public Sub ShadowMappingPass()
        GL_PUSH_GROUP("MapScene::ShadowMappingPass")

        ' Where one cascade hands over to the next. These MUST match
        ' cascadePlaneDistances in shaders/common.h - the shader picks the
        ' cascade with them, so if the two drift apart it samples the wrong map.
        '
        ' Doubling the maps to 4096 bought room to push the near cascade twice as
        ' far for the same sharpness. The old 20/200/700 was badly lopsided too:
        ' cascade 0 covered 20 m at roughly a centimetre per texel while cascade
        ' 2 covered 500 m at a quarter of a metre. Spread out, the near detail is
        ' unchanged and the middle distance is about three times finer.
        ' Halved from 40/150/500. The cascades now carry trees only, so they no
        ' longer have to reach far enough to cover terrain and buildings - the
        ' map-wide bake does that, at every distance. Pulling the splits in
        ' concentrates all four maps on the near field, where foliage detail is
        ' actually visible, and roughly doubles the texel density at every range.
        ' The far field past 250 m falls through to the bake, which is what it is
        ' for.
        Dim vp_cascade0 = getLightSpaceMatrix(My.Settings.near, 20.0F)
        Dim vp_cascade1 = getLightSpaceMatrix(20.0F, 75.0F)
        Dim vp_cascade2 = getLightSpaceMatrix(75.0F, 250.0F)
        Dim vp_cascade3 = getLightSpaceMatrix(250.0F, My.Settings.far)

        GL.NamedBufferSubData(shadow_mapping_matrix.buffer_id, IntPtr.Zero, Marshal.SizeOf(Of Matrix4), vp_cascade0)
        GL.NamedBufferSubData(shadow_mapping_matrix.buffer_id, New IntPtr(Marshal.SizeOf(Of Matrix4) * 1), Marshal.SizeOf(Of Matrix4), vp_cascade1)
        GL.NamedBufferSubData(shadow_mapping_matrix.buffer_id, New IntPtr(Marshal.SizeOf(Of Matrix4) * 2), Marshal.SizeOf(Of Matrix4), vp_cascade2)
        GL.NamedBufferSubData(shadow_mapping_matrix.buffer_id, New IntPtr(Marshal.SizeOf(Of Matrix4) * 3), Marshal.SizeOf(Of Matrix4), vp_cascade3)

        ShadowMappingFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, ShadowMappingFBO.WIDTH, ShadowMappingFBO.HEIGHT)
        GL.Clear(ClearBufferMask.DepthBufferBit)
        GL.DepthFunc(DepthFunction.Greater)

        GL.CullFace(CullFaceMode.Front)

        GL.Enable(EnableCap.PolygonOffsetFill)
        GL.PolygonOffset(1.1F, 4.0F)

        ' Models first, then trees. Both go in: a cascade that carries only
        ' trees drops every building shadow inside 250 m, which is the near field
        ' the cascades exist to serve.
        '
        ' Same guards the bake uses for each, so 'don't draw models' and 'don't
        ' draw trees' mean the same thing here as everywhere else.
        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            static_models.shadow_mapping_pass()
        End If

        If TREES_LOADED AndAlso DONT_BLOCK_TREES Then
            trees.shadow_pass()
        End If

        GL.Disable(EnableCap.PolygonOffsetFill)
        GL.CullFace(CullFaceMode.Back)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        sky.Dispose()
        terrain.Dispose()
        static_models.Dispose()
        water.Dispose()
        base_rings.Dispose()
        mini_map.Dispose()
        fog.Dispose()
        trees.Dispose()
        roads.Dispose()
        cursor.Dispose()
        camera.Dispose()
        decals.Dispose()

        ' gl buffers
        shadow_mapping_matrix.Dispose()

        CC_LUT_ID.Dispose()
        ENV_BRDF_LUT_ID.Dispose()

        mouse_timer.Dispose()

        PICK_DICTIONARY.Clear()
    End Sub

    Public Sub ExportToFile(path As String)
        'Dim scene As New Assimp.Scene
        'scene.RootNode = New Assimp.Node("Root")

        If TERRAIN_LOADED Then
            terrain.Export(path)
        End If

        ' dummy material
        'Dim dummy_material As New Assimp.Material
        'dummy_material.Name = "dummy_material"
        'dummy_material.ColorDiffuse = New Assimp.Color4D(1.0, 5.0, 5.0, 1.0)
        'scene.Materials.Add(dummy_material)

        'Dim exporter As New Assimp.AssimpContext
        'Debug.Assert(exporter.ExportFile(scene, filename + "dum.stl", format))
    End Sub
End Class

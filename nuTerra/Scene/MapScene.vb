Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapScene
    Implements IDisposable

    Public mouse_timer As New Timer

    ReadOnly mapName As String
    Public sky As New MapSky(Me)
    Public terrain As New MapTerrain(Me)
    Public static_models As New MapStaticModels(Me)
    Public water As New MapWater(Me)
    Public base_rings As New MapBaseRings(Me)
    Public mini_map As New MapMinimap(Me)
    Public fog As New MapFog(Me)
    Public trees As New MapTrees(Me)
    Public cursor As New MapCursor(Me)
    Public camera As New MapCamera(Me)

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

    Private Function getFrustumCornersWorldSpace(proj As Matrix4, view As Matrix4) As List(Of Vector4)
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

        Dim light_view_matrix = Matrix4.LookAt(LIGHT_POS.Normalized() + center, center, Vector3.UnitY)

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

        Dim light_proj_matrix = Matrix4.CreateOrthographicOffCenter(
            min.X, max.X, min.Y, max.Y, min.Z, max.Z)

        ' Fix for reversed-z
        light_proj_matrix.M33 = 1.0F / (max.Z - min.Z)
        light_proj_matrix.M43 = max.Z / (max.Z - min.Z)

        Return light_view_matrix * light_proj_matrix
    End Function

    Public Sub ShadowMappingPass()
        GL_PUSH_GROUP("MapScene::ShadowMappingPass")

        Dim vp_cascade0 = getLightSpaceMatrix(My.Settings.near, 20.0F)
        Dim vp_cascade1 = getLightSpaceMatrix(20.0F, 200.0F)
        Dim vp_cascade2 = getLightSpaceMatrix(200.0F, 700.0F)
        Dim vp_cascade3 = getLightSpaceMatrix(700.0F, My.Settings.far)

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

        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            static_models.shadow_mapping_pass()
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
        cursor.Dispose()
        camera.Dispose()

        ' gl buffers
        shadow_mapping_matrix.Dispose()

        CC_LUT_ID.Dispose()
        ENV_BRDF_LUT_ID.Dispose()

        mouse_timer.Dispose()
    End Sub
End Class

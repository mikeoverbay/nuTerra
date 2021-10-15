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
            Marshal.SizeOf(Of Matrix4),
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

    ' https://docs.nvidia.com/gameworks/content/gameworkslibrary/graphicssamples/opengl_samples/cascadedshadowmapping.htm
    Public Sub ShadowMappingPass()
        GL_PUSH_GROUP("MapScene::ShadowMappingPass")

        Dim dist = MathHelper.Clamp(4 * Vector3.Distance(camera.CAM_TARGET, camera.CAM_POSITION), 150, 1000)
        Dim light_proj_matrix = Matrix4.CreateOrthographic(dist, dist, ShadowMappingFBO.NEAR, ShadowMappingFBO.FAR)

        ' Fix for reversed-z
        light_proj_matrix.M33 = 1.0F / (ShadowMappingFBO.FAR - ShadowMappingFBO.NEAR)
        light_proj_matrix.M43 = ShadowMappingFBO.FAR / (ShadowMappingFBO.FAR - ShadowMappingFBO.NEAR)

        Dim cam_x0z As New Vector3(camera.CAM_TARGET.X, 0.0F, camera.CAM_TARGET.Z)
        Dim lp_norm = LIGHT_POS.Normalized() * dist
        Dim light_view_matrix = Matrix4.LookAt(lp_norm + cam_x0z, cam_x0z, Vector3.UnitY)
        Dim light_vp_matrix = light_view_matrix * light_proj_matrix
        GL.NamedBufferSubData(shadow_mapping_matrix.buffer_id, IntPtr.Zero, Marshal.SizeOf(light_vp_matrix), light_vp_matrix)

        ShadowMappingFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.ViewportIndexed(0, 0, 0, ShadowMappingFBO.WIDTH, ShadowMappingFBO.HEIGHT)
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

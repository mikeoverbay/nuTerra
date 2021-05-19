Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL4

Public Class MapScene
    Implements IDisposable

    ReadOnly mapName As String
    Public sky As New MapSky
    Public terrain As New MapTerrain
    Public static_models As New MapStaticModels
    Public water As New MapWater
    Public base_rings As New MapBaseRings
    Public mini_map As New MapMinimap
    Public fog As New MapFog
    Public trees As New MapTrees
    Public cursor As New MapCursor

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

        Dim dist = MathHelper.Clamp(4 * Vector3.Distance(CAM_TARGET, CAM_POSITION), 150, 1000)
        Dim light_proj_matrix = Matrix4.CreateOrthographic(dist, dist, ShadowMappingFBO.NEAR, ShadowMappingFBO.FAR)

        ' Fix for reversed-z
        light_proj_matrix.M33 = 1.0F / (ShadowMappingFBO.FAR - ShadowMappingFBO.NEAR)
        light_proj_matrix.M43 = ShadowMappingFBO.FAR / (ShadowMappingFBO.FAR - ShadowMappingFBO.NEAR)

        Dim cam_x0z As New Vector3(CAM_TARGET.X, 0.0F, CAM_TARGET.Z)
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

        ' gl buffers
        shadow_mapping_matrix.Dispose()
    End Sub
End Class

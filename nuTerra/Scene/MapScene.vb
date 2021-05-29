Imports System.Runtime.InteropServices
Imports OpenTK
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

    Public shadow_mapping_matrix As GLBuffer

    Public Sub New(mapName As String)
        Me.mapName = mapName

        mouse_timer.Interval = 10
        AddHandler mouse_timer.Tick, AddressOf check_postion_for_update
        mouse_timer.Start()

        shadow_mapping_matrix = GLBuffer.Create(BufferTarget.UniformBuffer, "shadow_mapping_matrix")
        shadow_mapping_matrix.StorageNullData(
            Marshal.SizeOf(Of Matrix4),
            BufferStorageFlags.DynamicStorageBit)
        shadow_mapping_matrix.BindBase(3)
    End Sub


    Public Sub check_postion_for_update()
        Dim halfPI = PI * 0.5F
        If camera.LOOK_AT_X <> camera.U_LOOK_AT_X Then
            camera.U_LOOK_AT_X = camera.LOOK_AT_X
        End If
        If camera.LOOK_AT_Y <> camera.U_LOOK_AT_Y Then
            camera.U_LOOK_AT_Y = camera.LOOK_AT_Y
        End If
        If camera.LOOK_AT_Z <> camera.U_LOOK_AT_Z Then
            camera.U_LOOK_AT_Z = camera.LOOK_AT_Z
        End If
        If camera.CAM_X_ANGLE <> camera.U_CAM_X_ANGLE Then
            camera.U_CAM_X_ANGLE = camera.CAM_X_ANGLE
        End If
        If camera.CAM_Y_ANGLE <> camera.U_CAM_Y_ANGLE Then
            If camera.CAM_Y_ANGLE > 1.3 Then
                camera.U_CAM_Y_ANGLE = 1.3
                camera.CAM_Y_ANGLE = camera.U_CAM_Y_ANGLE
            End If
            If camera.CAM_Y_ANGLE < -halfPI Then
                camera.U_CAM_Y_ANGLE = -halfPI + 0.001
                camera.CAM_Y_ANGLE = camera.U_CAM_Y_ANGLE
            End If
            camera.U_CAM_Y_ANGLE = camera.CAM_Y_ANGLE
        End If
        If camera.VIEW_RADIUS <> camera.U_VIEW_RADIUS Then
            camera.U_VIEW_RADIUS = camera.VIEW_RADIUS
        End If

        CURSOR_Y = get_Y_at_XZ(camera.U_LOOK_AT_X, camera.U_LOOK_AT_Z)

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

        mouse_timer.Dispose()
    End Sub
End Class

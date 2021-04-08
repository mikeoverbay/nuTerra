Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports System.ComponentModel

Module modOpenGL
    Public defaultVao As Integer
    Public FieldOfView As Single

    Public Function GetMaxGLVersion() As Tuple(Of Integer, Integer)
        Dim tmpControl = New GLControl()
        tmpControl.MakeCurrent()

        Dim majorVersion = GL.GetInteger(GetPName.MajorVersion)
        Dim minorVersion = GL.GetInteger(GetPName.MinorVersion)

        Return New Tuple(Of Integer, Integer)(majorVersion, minorVersion)
    End Function

    Public Class GLCapabilities
        Public Shared maxTextureSize As Integer
        Public Shared maxArrayTextureLayers As Integer
        Public Shared maxUniformBufferBindings As Integer
        Public Shared maxColorAttachments As Integer
        Public Shared maxAniso As Single
        Public Shared maxVertexOutputComponents As Integer
        Public Shared has_GL_NV_representative_fragment_test As Boolean
        Public Shared has_GL_NV_mesh_shader As Boolean
        Public Shared has_GL_NV_draw_texture As Boolean
        Public Shared has_GL_ARB_gl_spirv As Boolean

        Public Shared Sub Init(extensions As List(Of String))
            maxTextureSize = GL.GetInteger(GetPName.MaxTextureSize)
            maxArrayTextureLayers = GL.GetInteger(GetPName.MaxArrayTextureLayers)
            maxUniformBufferBindings = GL.GetInteger(GetPName.MaxUniformBufferBindings)
            maxColorAttachments = GL.GetInteger(GetPName.MaxColorAttachments)
            maxAniso = GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt)
            maxAniso = GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt)
            maxVertexOutputComponents = GL.GetInteger(GetPName.MaxVertexOutputComponents)

            ' useful extensions
            has_GL_NV_representative_fragment_test = extensions.Contains("GL_NV_representative_fragment_test")
            has_GL_NV_mesh_shader = extensions.Contains("GL_NV_mesh_shader")
            has_GL_NV_draw_texture = extensions.Contains("GL_NV_draw_texture")
            has_GL_ARB_gl_spirv = extensions.Contains("GL_ARB_gl_spirv")

            LogThis(String.Format("Max Texture Size = {0}", maxTextureSize))
            LogThis(String.Format("Max Array Texture Layers = {0}", maxArrayTextureLayers))
            LogThis(String.Format("Max Uniform Buffer Bindings = {0}", maxUniformBufferBindings))
            LogThis(String.Format("Max Color Attachments = {0}", maxColorAttachments))
            LogThis(String.Format("Max Texture Max Anisotropy = {0}", maxAniso))
            LogThis(String.Format("Max vertex output components = {0}", maxVertexOutputComponents))
            LogThis(String.Format("GL_NV_representative_fragment_test = {0}", has_GL_NV_representative_fragment_test))
            LogThis(String.Format("GL_NV_mesh_shader = {0}", has_GL_NV_mesh_shader))
            LogThis(String.Format("GL_NV_draw_texture = {0}", has_GL_NV_draw_texture))
            LogThis(String.Format("GL_ARB_gl_spirv = {0}", has_GL_ARB_gl_spirv))
        End Sub
    End Class

    <StructLayout(LayoutKind.Sequential)>
    Public Structure DrawElementsIndirectCommand
        Dim count As UInt32
        Dim instanceCount As UInt32
        Dim firstIndex As UInt32
        Dim baseVertex As UInt32
        Dim baseInstance As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure ModelInstance
        Dim matrix As Matrix4
        Dim cached_mvp As Matrix4
        Dim bmin As Vector3
        Dim lod_offset As UInt32
        Dim bmax As Vector3
        Dim lod_count As UInt32
        Dim batch_count As UInt32 ' hack!!!
        Dim reserverd1 As UInt32
        Dim reserverd2 As UInt32
        Dim reserverd3 As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure ModelLoD
        Dim draw_offset As UInt32
        Dim draw_count As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure CandidateDraw
        Dim model_id As UInt32
        Dim material_id As UInt32
        Dim count As UInt32
        Dim firstIndex As UInt32
        Dim baseVertex As UInt32
        Dim baseInstance As UInt32
        Dim lod_level As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure GLMaterial
        Dim g_atlasIndexes As Vector4
        Dim g_atlasSizes As Vector4
        Dim g_colorTint As Vector4
        Dim dirtParams As Vector4
        Dim dirtColor As Vector4
        Dim g_tile0Tint As Vector4
        Dim g_tile1Tint As Vector4
        Dim g_tile2Tint As Vector4
        Dim g_tileUVScale As Vector4
        Dim g_detailInfluences As Vector4
        Dim g_detailRejectTiling As Vector4
        Dim map1Handle As UInt64
        Dim map2Handle As UInt64
        Dim map3Handle As UInt64
        Dim map4Handle As UInt64
        Dim map5Handle As UInt64
        Dim map6Handle As UInt64
        Dim shader_type As UInt32
        Dim texAddressMode As UInt32
        Dim alphaReference As Single
        Dim g_useNormalPackDXT1 As UInt32
        Dim alphaTestEnable As UInt32
        Dim g_enableAO As UInt32
        Dim double_sided As UInt32
        'Dim pad0 As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure TPerViewData
        Public view As Matrix4
        Public projection As Matrix4
        Public viewProj As Matrix4
        Public invViewProj As Matrix4
        Public cameraPos As Vector3
        Public pad1 As UInt32
        Public resolution As Vector2
    End Structure
    Public PerViewData As New TPerViewData
    Public PerViewDataBuffer As GLBuffer

    <StructLayout(LayoutKind.Sequential)>
    Public Structure TCommonProperties
        Public waterColor As Vector3
        Public waterAlpha As Single
        Public fog_tint As Vector3
        Public light_count As UInt32
        Public sunColor As Vector3
        Public mapMaxHeight As Single
        Public ambientColorForward As Vector3
        Public mapMinHeight As Single
        Public map_size As Vector2
        Public MEAN As Single
        Public AMBIENT As Single
        Public BRIGHTNESS As Single
        Public SPECULAR As Single
        Public GRAY_LEVEL As Single
        Public GAMMA_LEVEL As Single
        Public fog_level As Single
        Public _start As Single
        Public _end As Single

        Public Sub update()
            light_count = Math.Max(LIGHTS.index - 1, 0)
            mapMaxHeight = MAX_MAP_HEIGHT
            mapMinHeight = MIN_MAP_HEIGHT
            MEAN = CSng(MEAN_MAP_HEIGHT)

            'Lighting settings
            AMBIENT = frmLightSettings.lighting_ambient
            BRIGHTNESS = frmLightSettings.lighting_terrain_texture
            SPECULAR = frmLightSettings.lighting_specular_level
            GRAY_LEVEL = frmLightSettings.lighting_gray_level
            GAMMA_LEVEL = frmLightSettings.lighting_gamma_level
            fog_level = frmLightSettings.lighting_fog_level * 100.0F

            GL.NamedBufferSubData(CommonPropertiesBuffer.buffer_id, IntPtr.Zero, Marshal.SizeOf(Me), Me)
        End Sub
    End Structure
    Public CommonProperties As New TCommonProperties
    Public CommonPropertiesBuffer As GLBuffer

    Public Sub Sun_Ortho_view(ByVal L As Single, ByVal R As Single, ByVal B As Single, ByVal T As Single)
        GL.Viewport(0, 0, FBO_ShadowBaker.depth_map_size, FBO_ShadowBaker.depth_map_size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(L, R, B, T, -3000.0F, 3000.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub

    Public Sub Ortho_main()
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, -30000.0F, 30000.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub

    Public Sub Ortho_MiniMap(ByVal square_size As Integer)
        GL.Viewport(0, 0, square_size, square_size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(MAP_BB_UR.X, MAP_BB_BL.X, -MAP_BB_UR.Y, -MAP_BB_BL.Y, -300.0F, 300.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub
    Public Sub Ortho_MiniMap_actual(ByVal square_size As Integer)
        GL.Viewport(0, 0, square_size, square_size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0, square_size, -square_size, 0.0, -300.0F, 300.0F)
        'PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0, square_size, 0.0, -square_size, 300.0F, -300.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub

    Public Function set_sun_view_matrix() As Matrix4

        Dim rotateY = Matrix4.CreateRotationY((LIGHT_ORBIT_ANGLE_Z) * 0.0174533)
        Dim rotateX = Matrix4.CreateRotationX(LIGHT_ORBIT_ANGLE_X * 0.0174533)

        Dim m As Matrix4 = rotateY * rotateX
        Return m
    End Function

    Public Sub set_prespective_view()

        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)

        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, -300.0F, 300.0F)
        Dim sin_x, cos_x, cos_y, sin_y As Single
        Dim cam_x, cam_y, cam_z As Single

        sin_x = Sin(U_CAM_X_ANGLE)
        cos_x = Cos(U_CAM_X_ANGLE)
        cos_y = Cos(U_CAM_Y_ANGLE)
        sin_y = Sin(U_CAM_Y_ANGLE)
        cam_y = sin_y * VIEW_RADIUS
        cam_x = cos_y * sin_x * VIEW_RADIUS
        cam_z = cos_y * cos_x * VIEW_RADIUS

        Dim LOOK_Y = CURSOR_Y + U_LOOK_AT_Y
        CAM_POSITION.X = cam_x + U_LOOK_AT_X
        CAM_POSITION.Y = cam_y + LOOK_Y
        CAM_POSITION.Z = cam_z + U_LOOK_AT_Z

        CAM_TARGET = New Vector3(U_LOOK_AT_X, LOOK_Y, U_LOOK_AT_Z)

        PerViewData.projection = Matrix4.CreatePerspectiveFieldOfView(
                                   FieldOfView,
                                   frmMain.glControl_main.ClientSize.Width / CSng(frmMain.glControl_main.ClientSize.Height),
                                   My.Settings.near, My.Settings.far)
#If True Then ' reverse depth
        PerViewData.projection.M33 *= -1
        PerViewData.projection.M33 -= 1
        PerViewData.projection.M43 *= -1
#End If
        PerViewData.cameraPos = CAM_POSITION
        PerViewData.view = Matrix4.LookAt(CAM_POSITION, CAM_TARGET, Vector3.UnitY)
        PerViewData.viewProj = PerViewData.view * PerViewData.projection
        PerViewData.invViewProj = Matrix4.Invert(PerViewData.viewProj)
        PerViewData.resolution.X = frmMain.glControl_main.ClientSize.Width
        PerViewData.resolution.Y = frmMain.glControl_main.ClientSize.Height
        GL.NamedBufferSubData(PerViewDataBuffer.buffer_id, IntPtr.Zero, Marshal.SizeOf(PerViewData), PerViewData)
    End Sub


    Public Sub draw_color_rectangle(rect As RectangleF, color As Color4)
        rect2dShader.Use()

        GL.Uniform4(rect2dShader("color"), color)
        GL.UniformMatrix4(rect2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(rect2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        'GL.BindVertexArray(0)

        rect2dShader.StopUse()
    End Sub

    Public Sub draw_image_rectangle(rect As RectangleF, image As GLTexture, atlas As Boolean)
        If USE_NV_DRAW_TEXTURE Then
            Dim h = frmMain.glControl_main.Height
            Dim x0 = rect.Left
            Dim x1 = rect.Right
            Dim y0 = h - rect.Top
            Dim y1 = h - rect.Bottom
            GL.NV.DrawTexture(image.texture_id, 0, x0, y0, x1, y1, 0, 0, 0, 1, 1)
        Else
            If atlas Then
                image2dArrayShader.Use()
                GL.Uniform1(image2dArrayShader("id"), 1)

                image.BindUnit(0)
                GL.Uniform1(image2dArrayShader("imageMap"), 0)
                GL.Uniform2(image2dArrayShader("uv_scale"), 1.0F, 1.0F)
                GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
                GL.Uniform4(image2dArrayShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

                GL.BindVertexArray(defaultVao)
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
                image2dArrayShader.StopUse()
            Else
                image2dShader.Use()
                image.BindUnit(0)
                GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)
                GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
                GL.Uniform4(image2dShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

                GL.BindVertexArray(defaultVao)
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
                image2dShader.StopUse()
            End If

            unbind_textures(0)
        End If
    End Sub

    Public Sub draw_image_rectangle_flipY(rect As RectangleF, image As GLTexture)
        If USE_NV_DRAW_TEXTURE Then
            Dim h = frmMain.glControl_main.Height
            Dim x0 = rect.Left
            Dim x1 = rect.Right
            Dim y0 = h - rect.Top
            Dim y1 = h - rect.Bottom
            GL.NV.DrawTexture(image.texture_id, 0, x0, y0, x1, y1, 0, 0, 0, 1, 1)
        Else
            image2dShader.Use()

            image.BindUnit(0)
            GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

            GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
            GL.Uniform4(image2dShader("rect"),
                        rect.Left,
                        -rect.Bottom,
                        rect.Right,
                        -rect.Top)

            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            'GL.BindVertexArray(0)

            image2dShader.StopUse()
            'unbind texture
            unbind_textures(0)
        End If
    End Sub

    Public Sub draw_image_array(rect As RectangleF, image As GLTexture, ByVal id As Integer)

        image2dArrayShader.Use()

        image.BindUnit(0)
        GL.Uniform1(image2dArrayShader("imageMap"), 0)
        GL.Uniform1(image2dArrayShader("id"), id)
        GL.Uniform2(image2dArrayShader("uv_scale"), 1.0F, 1.0F)

        GL.UniformMatrix4(image2dArrayShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dArrayShader("rect"),
                        rect.Left,
                        -rect.Bottom,
                        rect.Right,
                        -rect.Top)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        'GL.BindVertexArray(0)

        image2dShader.StopUse()
        'unbind texture
        unbind_textures(0)
    End Sub

    Private Function pack_10(x As Single) As UInt32
        Dim qx As Int32 = MathHelper.Clamp(CType(x * 511.0F, Int32), -512, 511)
        If qx < 0 Then
            Return (1 << 9) Or ((CType(-1 - qx, UInt32) Xor ((1 << 9) - 1)))
        Else
            Return qx
        End If
    End Function

    Public Function pack_2_10_10_10(unpacked As Vector3, Optional w As UInt32 = 0) As UInt32
        unpacked.Normalize()

        Dim packed_x As UInt32 = pack_10(unpacked.X)
        Dim packed_y As UInt32 = pack_10(unpacked.Y)
        Dim packed_z As UInt32 = pack_10(unpacked.Z)
        Return packed_x Or (packed_y << 10) Or (packed_z << 20) Or (w << 30)
    End Function

    Private debugOutputCallbackProc As DebugProc
    Private Sub DebugOutputCallback(source As DebugSource,
                                   type As DebugType,
                                   id As UInteger,
                                   severity As DebugSeverity,
                                   length As Integer,
                                   messagePtr As IntPtr,
                                   userParam As IntPtr)
        If source = DebugSource.DebugSourceApplication Then Return
        If id = 131185 Then Return
        If id = 1281 Then Return
        'If id = 1282 Then Return

        Dim message = Marshal.PtrToStringAnsi(messagePtr)

        Application.DoEvents()
        LogThis(String.Format("OpenGL error #{0}: {1}", id, message))
    End Sub

    Private stack_pos As Integer = 0

    <Conditional("DEBUG")>
    Public Sub GL_PUSH_GROUP(name As String)
        stack_pos += 1
        GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, stack_pos + 10, -1, name)
    End Sub

    <Conditional("DEBUG")>
    Public Sub GL_POP_GROUP()
        stack_pos -= 1
        GL.PopDebugGroup()
        If stack_pos < 0 Or stack_pos > 5 Then Stop
    End Sub

    Public Sub SetupDebugOutputCallback()
        GL.Enable(EnableCap.DebugOutput)
        GL.Enable(EnableCap.DebugOutputSynchronous)
        debugOutputCallbackProc = New DebugProc(AddressOf DebugOutputCallback)
        GL.DebugMessageCallback(debugOutputCallbackProc, IntPtr.Zero)
        GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DebugTypeError, DebugSeverityControl.DontCare, 0, 0, True)
    End Sub

    Public Function get_GL_error_string(ByVal e As ErrorCode) As String
        Return [Enum].GetName(GetType(ErrorCode), e)
    End Function
End Module

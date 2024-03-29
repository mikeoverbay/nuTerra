﻿Imports System.Runtime.InteropServices
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Module modOpenGL
    Public defaultVao As GLVertexArray
    Public FieldOfView As Single

    Public Class GLCapabilities
        Public Shared maxTextureSize As Integer
        Public Shared maxArrayTextureLayers As Integer
        Public Shared maxUniformBufferBindings As Integer
        Public Shared maxColorAttachments As Integer
        Public Shared maxAniso As Single
        Public Shared maxVertexOutputComponents As Integer

        Public Shared total_mem_mb As Integer

        Public Shared has_GL_NV_representative_fragment_test As Boolean
        Public Shared has_GL_NV_mesh_shader As Boolean
        Public Shared has_GL_NVX_gpu_memory_info As Boolean

        Public Shared ReadOnly Property memory_usage As Integer
            Get
                If has_GL_NVX_gpu_memory_info Then
                    Return total_mem_mb - GL.GetInteger(GL_GPU_MEM_INFO_CURRENT_AVAILABLE_MEM_NVX) \ 1024
                Else
                    Return Nothing
                End If
            End Get
        End Property

        Public Shared Sub Init(extensions As List(Of String))
            maxTextureSize = GL.GetInteger(GetPName.MaxTextureSize)
            maxArrayTextureLayers = GL.GetInteger(GetPName.MaxArrayTextureLayers)
            maxUniformBufferBindings = GL.GetInteger(GetPName.MaxUniformBufferBindings)
            maxColorAttachments = GL.GetInteger(GetPName.MaxColorAttachments)
            maxAniso = GL.GetFloat(OpenGL.ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt)
            maxVertexOutputComponents = GL.GetInteger(GetPName.MaxVertexOutputComponents)

            ' useful extensions
            has_GL_NV_representative_fragment_test = extensions.Contains("GL_NV_representative_fragment_test")
            has_GL_NV_mesh_shader = extensions.Contains("GL_NV_mesh_shader")
            has_GL_NVX_gpu_memory_info = extensions.Contains("GL_NVX_gpu_memory_info")

            If has_GL_NVX_gpu_memory_info Then
                Const GL_GPU_MEM_INFO_TOTAL_AVAILABLE_MEM_NVX As GetPName = &H9048
                total_mem_mb = GL.GetInteger(GL_GPU_MEM_INFO_TOTAL_AVAILABLE_MEM_NVX) \ 1024
            Else
                ' TODO: https://www.khronos.org/registry/OpenGL/extensions/AMD/WGL_AMD_gpu_association.txt
            End If

            LogThis("Max Texture Size = {0}", maxTextureSize)
            LogThis("Max Array Texture Layers = {0}", maxArrayTextureLayers)
            LogThis("Max Uniform Buffer Bindings = {0}", maxUniformBufferBindings)
            LogThis("Max Color Attachments = {0}", maxColorAttachments)
            LogThis("Max Texture Max Anisotropy = {0}", maxAniso)
            LogThis("Max vertex output components = {0}", maxVertexOutputComponents)

            LogThis("GL_NV_representative_fragment_test = {0}", has_GL_NV_representative_fragment_test)
            LogThis("GL_NV_mesh_shader = {0}", has_GL_NV_mesh_shader)
            LogThis("GL_NVX_gpu_memory_info = {0}", has_GL_NVX_gpu_memory_info)

            LogThis("total_mem_mb = {0}", total_mem_mb)
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
    Public Structure TCommonProperties
        Public waterColor As Vector3
        Public waterAlpha As Single
        Public fog_tint As Vector3
        Public _tess_level As Single
        Public sunColor As Vector3
        Public mapMaxHeight As Single
        Public ambientColorForward As Vector3
        Public mapMinHeight As Single
        Public map_size As Vector2
        Public MEAN As Single
        Public _AMBIENT As Single
        Public _BRIGHTNESS As Single
        Public _SPECULAR As Single
        Public _GRAY_LEVEL As Single
        Public _GAMMA_LEVEL As Single
        Public _FOG_LEVEL As Single
        Public blend_macro_influence As Single ' from space.bin/BWT2
        Public blend_global_threshold As Single ' from space.bin/BWT2

        Public VirtualTextureSize As Single
        Public AtlasScale As Single
        Public PageTableSize As Single

        Public USE_SHADOW_MAPPING As Integer
        Public _SHOW_TEST_TEXTURES As Integer

        Public Property AMBIENT As Single
            Get
                Return _AMBIENT
            End Get
            Set(value As Single)
                If _AMBIENT <> value Then
                    _AMBIENT = value
                    update()
                End If
            End Set
        End Property

        Public Property BRIGHTNESS As Single
            Get
                Return _BRIGHTNESS
            End Get
            Set(value As Single)
                If _BRIGHTNESS <> value Then
                    _BRIGHTNESS = value
                    update()
                End If
            End Set
        End Property

        Public Property SPECULAR As Single
            Get
                Return _SPECULAR
            End Get
            Set(value As Single)
                If _SPECULAR <> value Then
                    _SPECULAR = value
                    update()
                End If
            End Set
        End Property

        Public Property GRAY_LEVEL As Single
            Get
                Return _GRAY_LEVEL
            End Get
            Set(value As Single)
                If _GRAY_LEVEL <> value Then
                    _GRAY_LEVEL = value
                    update()
                End If
            End Set
        End Property

        Public Property GAMMA_LEVEL As Single
            Get
                Return _GAMMA_LEVEL
            End Get
            Set(value As Single)
                If _GAMMA_LEVEL <> value Then
                    _GAMMA_LEVEL = value
                    update()
                End If
            End Set
        End Property

        Public Property FOG_LEVEL As Single
            Get
                Return _FOG_LEVEL
            End Get
            Set(value As Single)
                If _FOG_LEVEL <> value Then
                    _FOG_LEVEL = value
                    update()
                End If
            End Set
        End Property

        Public Property tess_level As Single
            Get
                Return _tess_level
            End Get
            Set(value As Single)
                If _tess_level <> value Then
                    _tess_level = value
                    update()
                End If
            End Set
        End Property

        Public Property SHOW_TEST_TEXTURES As Boolean
            Get
                Return _SHOW_TEST_TEXTURES
            End Get
            Set(value As Boolean)
                If _SHOW_TEST_TEXTURES <> value Then
                    _SHOW_TEST_TEXTURES = value
                    update()
                    map_scene?.terrain.RebuildVTAtlas()
                End If
            End Set
        End Property


        Public Sub Init()
            'Lighting settings
            _AMBIENT = My.Settings.Ambient_level / 300.0!
            _BRIGHTNESS = My.Settings.Bright_level / 50.0!
            _SPECULAR = My.Settings.Specular_level / 100.0!
            _GRAY_LEVEL = 1.0 - (My.Settings.Gray_level / 100.0!)
            _GAMMA_LEVEL = My.Settings.Gamma_level / 100.0!
            _FOG_LEVEL = (My.Settings.Fog_level / 10000.0!) * 100.0F
            _tess_level = 1.0
        End Sub

        Public Sub update()
            mapMaxHeight = MAX_MAP_HEIGHT
            mapMinHeight = MIN_MAP_HEIGHT
            MEAN = CSng(MEAN_MAP_HEIGHT)

            GL.NamedBufferSubData(CommonPropertiesBuffer.buffer_id, IntPtr.Zero, Marshal.SizeOf(Me), Me)
        End Sub
    End Structure
    Public CommonProperties As New TCommonProperties
    Public CommonPropertiesBuffer As GLBuffer

    Public Sub Ortho_main()
        GL.Viewport(0, 0, MainFBO.width, MainFBO.height)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, MainFBO.width, -MainFBO.height, 0.0F, -30000.0F, 30000.0F)
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
        VIEWMATRIX = Matrix4.Identity
    End Sub

    Public Function set_sun_view_matrix() As Matrix4

        Dim rotateY = Matrix4.CreateRotationY((LIGHT_ORBIT_ANGLE_Z) * 0.0174533)
        Dim rotateX = Matrix4.CreateRotationX(LIGHT_ORBIT_ANGLE_X * 0.0174533)

        Dim m As Matrix4 = rotateY * rotateX
        Return m
    End Function



    Public Sub draw_color_rectangle(rect As RectangleF, color As Color4)
        rect2dShader.Use()

        GL.Uniform4(rect2dShader("color"), color)
        GL.UniformMatrix4(rect2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(rect2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        'GL.BindVertexArray(0)

        rect2dShader.StopUse()
    End Sub

    Public Sub draw_image_rectangle(rect As RectangleF, image As GLTexture)
        image2dShader.Use()
        image.BindUnit(0)
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        image2dShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Public Sub draw_image_rectangle_flipY(rect As RectangleF, image As GLTexture)
        image2dShader.Use()

        image.BindUnit(0)
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
                        rect.Left,
                        -rect.Bottom,
                        rect.Right,
                        -rect.Top)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        'GL.BindVertexArray(0)

        image2dShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
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
        If id = 131218 Then Return

        Dim message = Marshal.PtrToStringAnsi(messagePtr)

        LogThis("OpenGL error #{0}: {1}", id, message)
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

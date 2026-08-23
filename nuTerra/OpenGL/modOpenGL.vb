Imports System.Runtime.InteropServices
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
        ' PBS_tiled.fx needs 12: 3 tile sets x (albedoHeight, normalGlossSpec, metallicAO)
        ' plus blendMask, dirtMap and colorTex. Must stay in lockstep with
        ' MaterialProperties.maps[12] in shaders/common.h (std430).
        Dim map7Handle As UInt64
        Dim map8Handle As UInt64
        Dim map9Handle As UInt64
        Dim map10Handle As UInt64
        Dim map11Handle As UInt64
        Dim map12Handle As UInt64
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

        ' Appended at the end of the block so every existing std140 offset stays
        ' put. The three spares keep the block a multiple of 16 bytes, which
        ' std140 requires, and give the next few additions somewhere to go.
        Public _TONEMAP_EXPOSURE As Single
        Public _SUN_STRENGTH As Single
        Public _SUN_TINT As Single
        Public _AMBIENT_SAT As Single

        ' Terrain blend, from space.bin/BWT2. blend_height is how close a layer
        ' has to be to the tallest contender before it contributes at all - it is
        ' what makes a transition follow the height maps instead of cross-fading.
        Public _BLEND_HEIGHT As Single
        Public disabled_blend_height As Single
        Public _HEIGHT_CONTRAST As Single
        Public _pad_e As Single

        ' What the map asked for, kept so the panel can show it next to whatever
        ' the slider has been moved to. Not part of the UBO block.
        Public Shared blend_height_authored As Single

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

        '''<summary>
        ''' Gain of the tone curve in deferred.frag. The derivative of
        ''' 1 - exp(-x * E) at zero is exactly E, so this is the only control
        ''' over how fast the low end comes up and how soon highlights reach the
        ''' shoulder. It does not change contrast - that is the pow exponents.
        '''</summary>
        Public Property TONEMAP_EXPOSURE As Single
            Get
                Return _TONEMAP_EXPOSURE
            End Get
            Set(value As Single)
                If _TONEMAP_EXPOSURE <> value Then
                    _TONEMAP_EXPOSURE = value
                    update()
                End If
            End Set
        End Property

        '''<summary>
        ''' Multiplier on sunLightColor from environment.xml. The colour is used
        ''' at full chroma now - it used to be mixed 60% toward neutral grey,
        ''' which left a grey surface grey instead of picking up the warmth of
        ''' the light falling on it.
        '''</summary>
        Public Property SUN_STRENGTH As Single
            Get
                Return _SUN_STRENGTH
            End Get
            Set(value As Single)
                If _SUN_STRENGTH <> value Then
                    _SUN_STRENGTH = value
                    update()
                End If
            End Set
        End Property

        '''<summary>
        ''' How much of the map's sunLightColor tints the direct light. 0 is a
        ''' neutral white sun, 1 is the value from environment.xml at full chroma.
        '''</summary>
        Public Property SUN_TINT As Single
            Get
                Return _SUN_TINT
            End Get
            Set(value As Single)
                If _SUN_TINT <> value Then
                    _SUN_TINT = value
                    update()
                End If
            End Set
        End Property

        '''<summary>
        ''' How much of the SH probe's colour survives. 1 keeps the bake as it
        ''' is - Abbey's sh1 is [-0.13, 0.43, 0.99], so shadows go strongly blue
        ''' because sky fill is genuinely what lights them. 0 flattens the probe
        ''' to its own luminance, keeping the level and the directionality but
        ''' dropping the hue.
        '''</summary>
        Public Property AMBIENT_SAT As Single
            Get
                Return _AMBIENT_SAT
            End Get
            Set(value As Single)
                If _AMBIENT_SAT <> value Then
                    _AMBIENT_SAT = value
                    update()
                End If
            End Set
        End Property

        '''<summary>
        ''' Terrain height-blend width. Small values give a crisp edge that
        ''' follows the layer height maps, large values a soft cross-fade. Set
        ''' per map from BWT2/blendHeight when a map loads.
        '''</summary>
        Public Property BLEND_HEIGHT As Single
            Get
                Return _BLEND_HEIGHT
            End Get
            Set(value As Single)
                If _BLEND_HEIGHT <> value Then
                    _BLEND_HEIGHT = value
                    update()
                End If
            End Set
        End Property

        '''<summary>
        ''' Exponent on the terrain layer height before it enters the blend.
        ''' 1.0 is the game's own behaviour. Below 1 lifts mid heights toward 1,
        ''' so the splat dominates and the winning texture stops sitting so heavy
        ''' over a transition. Above 1 pushes them down and makes the boundary
        ''' follow the relief more sharply.
        '''</summary>
        Public Property HEIGHT_CONTRAST As Single
            Get
                Return _HEIGHT_CONTRAST
            End Get
            Set(value As Single)
                If _HEIGHT_CONTRAST <> value Then
                    _HEIGHT_CONTRAST = value
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
            ' Lighting settings. These are stored as the actual float the shader
            ' uses, not the old integer-in-hundredths form - that could not
            ' represent a gamma of 0.455 and was never written back, so every
            ' slider reverted on restart.
            ' Clamped to the slider range. A value saved while a slider was
            ' temporarily widened would otherwise load pinned at the maximum,
            ' where dragging can only ever reduce it and the control looks stuck.
            _AMBIENT = Math.Clamp(My.Settings.light_ambient, 0.0F, 0.4F)
            _BRIGHTNESS = Math.Clamp(My.Settings.light_bright, 0.0F, 2.0F)
            _SPECULAR = Math.Clamp(My.Settings.light_specular, 0.0F, 1.0F)
            _GRAY_LEVEL = Math.Clamp(My.Settings.light_gray, 0.0F, 1.0F)
            _GAMMA_LEVEL = Math.Clamp(My.Settings.light_gamma, 0.0F, 1.0F)
            _FOG_LEVEL = Math.Clamp(My.Settings.light_fog, 0.0F, 1.0F)
            _TONEMAP_EXPOSURE = Math.Clamp(My.Settings.light_tonemap_exposure, 0.5F, 4.0F)
            _SUN_STRENGTH = Math.Clamp(My.Settings.light_sun_strength, 0.0F, 3.0F)
            _SUN_TINT = Math.Clamp(My.Settings.light_sun_tint, 0.0F, 1.0F)
            _AMBIENT_SAT = Math.Clamp(My.Settings.light_ambient_sat, 0.0F, 1.0F)
            _HEIGHT_CONTRAST = Math.Clamp(My.Settings.terrain_height_contrast, 0.25F, 12.0F)
            USE_SH_AMBIENT = My.Settings.use_sh_ambient
            _tess_level = 1.0

            ' Shadows on. Init never touched this, so it sat at the Integer
            ' default of zero and every session started with them off until the
            ' checkbox was ticked.
            USE_SHADOW_MAPPING = 1
        End Sub

        ''' <summary>
        ''' Copies the live lighting values back into My.Settings so they survive
        ''' a restart. Called on shutdown, just before My.Settings.Save().
        ''' </summary>
        Public Sub SaveToSettings()
            My.Settings.light_ambient = _AMBIENT
            My.Settings.light_bright = _BRIGHTNESS
            My.Settings.light_specular = _SPECULAR
            My.Settings.light_gray = _GRAY_LEVEL
            My.Settings.light_gamma = _GAMMA_LEVEL
            My.Settings.light_fog = _FOG_LEVEL
            My.Settings.light_tonemap_exposure = _TONEMAP_EXPOSURE
            My.Settings.light_sun_strength = _SUN_STRENGTH
            My.Settings.light_sun_tint = _SUN_TINT
            My.Settings.light_ambient_sat = _AMBIENT_SAT
            My.Settings.terrain_height_contrast = _HEIGHT_CONTRAST
            My.Settings.use_sh_ambient = USE_SH_AMBIENT
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

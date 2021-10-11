Imports System.IO
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports ImGuiNET
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

Public Class Window
    Inherits GameWindow

    ' HACK
    Public Shared SCR_WIDTH As Integer = 1200
    Public Shared SCR_HEIGHT As Integer = 800
    Public Shared mouse_last_pos As Point

    Private _controller As ImGuiController

    Private Shared Function GetGLSettings() As NativeWindowSettings
        Dim setting As New NativeWindowSettings With {
            .Size = New Vector2i(SCR_WIDTH, SCR_HEIGHT),
            .API = Common.ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible,
            .DepthBits = 0,
            .AlphaBits = 0
        }
#If DEBUG Then
        setting.Flags = setting.Flags Or ContextFlags.Debug
#End If
        Return setting
    End Function

    Public Sub New()
        MyBase.New(
            New GameWindowSettings With {
                .IsMultiThreaded = False,
                .RenderFrequency = 0.0,
                .UpdateFrequency = 0.0
            }, GetGLSettings())
        Title = Application.ProductName
        VSync = VSyncMode.Off

        ' BEGIN HACK
        Dim appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)
        Dim bmpIcon = appIcon.ToBitmap()

        Dim data = bmpIcon.LockBits(New Rectangle(0, 0, bmpIcon.Width, bmpIcon.Height),
            Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb)

        Dim numbytes = data.Stride * bmpIcon.Height
        Dim bytes(numbytes) As Byte

        Marshal.Copy(data.Scan0, bytes, 0, numbytes)

        ' BGR TO RGB
        For i = 0 To numbytes - 1 Step 4
            Dim tmp = bytes(i)
            bytes(i) = bytes(i + 2)
            bytes(i + 2) = tmp
        Next

        Icon = New Input.WindowIcon(New Input.Image(bmpIcon.Width, bmpIcon.Height, bytes))

        bmpIcon.UnlockBits(data)

        ' END HACK
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
        'Check context:
        Dim majorVersion = GL.GetInteger(GetPName.MajorVersion)
        Dim minorVersion = GL.GetInteger(GetPName.MinorVersion)
        If majorVersion < 4 Or (majorVersion = 4 AndAlso minorVersion < 3) Then
            MsgBox("A graphics card and driver with support for OpenGL 4.3 or higher is required.")
            Application.Exit()
            Return
        End If

        Dim launch_timer As New Stopwatch

        '-----------------------------------------------------------------------------------------
        'need a work area on users disc
        TEMP_STORAGE = Path.Combine(Path.GetTempPath, "nuTerra")
        If Not Directory.Exists(TEMP_STORAGE) Then
            Directory.CreateDirectory(TEMP_STORAGE)
        End If
        LogThis("{0}ms Temp storage is located at: {1}", launch_timer.ElapsedMilliseconds, TEMP_STORAGE)

        LogThis("Vendor: {0}", GL.GetString(StringName.Vendor))
        LogThis("Renderer: {0}", GL.GetString(StringName.Renderer))
        LogThis("Version: {0}", GL.GetString(StringName.Version))
        LogThis("GLSL Version: {0}", GL.GetString(StringName.ShadingLanguageVersion))

        Dim extensions As New List(Of String)
        Dim numExt As Integer = GL.GetInteger(GetPName.NumExtensions)
        For i = 0 To numExt - 1
            extensions.Add(GL.GetString(StringNameIndexed.Extensions, i))
        Next

        Dim requied_extensions = {
            "GL_ARB_vertex_type_10f_11f_11f_rev",
            "GL_ARB_compute_variable_group_size",
            "GL_ARB_shading_language_include",
            "GL_ARB_bindless_texture",
            "GL_ARB_multi_draw_indirect", 'core since 4.3
            "GL_ARB_direct_state_access", 'core since 4.5
            "GL_ARB_clip_control", 'core since 4.5
            "GL_ARB_indirect_parameters", 'core since 4.6
            "GL_ARB_shader_draw_parameters", 'core since 4.6
            "GL_ARB_shader_atomic_counter_ops" 'core since 4.6
        }

        Dim unsupported_ext As New List(Of String)
        For Each ext In requied_extensions
            If Not extensions.Contains(ext) Then
                unsupported_ext.Add(ext)
            End If
        Next

        ' https://renderdoc.org/docs/getting_started/faq.html#can-i-tell-via-the-graphics-apis-if-renderdoc-Is-present-at-runtime
        Const GL_DEBUG_TOOL_EXT = &H6789
        Dim debug_tool = GL.IsEnabled(GL_DEBUG_TOOL_EXT)
        GL.GetError() ' Clear last error

        ' skip checks if we are in RenderDoc 
        If Not debug_tool Then
            If unsupported_ext.Count > 0 Then
                MsgBox(String.Format(
                       "A graphics card and driver with support for {0} is required.", String.Join(" ", unsupported_ext)))
                Application.Exit()
                Return
            End If
        End If

        '-----------------------------------------------------------------------------------------
        'Any relevant info the user could use.
        GLCapabilities.Init(extensions)
        '-----------------------------------------------------------------------------------------

        USE_REPRESENTATIVE_TEST = GLCapabilities.has_GL_NV_representative_fragment_test

#If DEBUG Then
        ' Just check
        Debug.Assert(extensions.Contains("GL_KHR_debug"))
        Debug.Assert(extensions.Contains("GL_ARB_debug_output"))

        If GL.GetInteger(GetPName.ContextFlags) And ContextFlagMask.ContextFlagDebugBit Then
            LogThis("Setup Debug Output Callback")
            SetupDebugOutputCallback()
        End If
#End If

        ' Set depth to [0..1] range instead of [-1..1]
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne)

        ' Enable depth clamping
        GL.Enable(EnableCap.DepthClamp)

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.ClearDepth(0.0F)

        '-----------------------------------------------------------------------------------------
        'Check if the game path is set
        If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
            MsgBox("Path to game is not set!" + vbCrLf +
                    "Lets set it now.", MsgBoxStyle.OkOnly, "Game Path not set")
            m_set_game_path()

            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("This application will be closed because game was not found!")
                Application.Exit()
                Return
            End If
        End If

        LogThis("{0}ms Game Path: {1}", launch_timer.ElapsedMilliseconds, My.Settings.GamePath)

        ' Create default VAO
        defaultVao = GLVertexArray.Create("defaultVao")

        make_cube() ' used for many draw functions

        CommonPropertiesBuffer = GLBuffer.Create(BufferTarget.UniformBuffer, "CommonProperties")
        CommonPropertiesBuffer.StorageNullData(
            Marshal.SizeOf(CommonProperties),
            BufferStorageFlags.DynamicStorageBit)
        CommonPropertiesBuffer.BindBase(2)

        CommonProperties.Init()
        FieldOfView = CSng(Math.PI) * (My.Settings.fov / 180.0F)

        'Get block state of things we want to block loading to speed things up for testing/debugging
        DONT_BLOCK_BASES = My.Settings.load_bases
        DONT_BLOCK_DECALS = My.Settings.load_decals
        DONT_BLOCK_MODELS = My.Settings.load_models
        DONT_BLOCK_SKY = My.Settings.load_sky
        DONT_BLOCK_TERRAIN = My.Settings.load_terrain
        DONT_BLOCK_OUTLAND = My.Settings.load_outland
        DONT_BLOCK_TREES = My.Settings.load_trees
        DONT_BLOCK_WATER = My.Settings.load_water

        'get directory of all shader files
        SHADER_PATHS = Directory.GetFiles(Application.StartupPath + "\shaders\", "*.*", SearchOption.AllDirectories)

        ShadowMappingFBO.FBO_Initialize()
        LogThis("{0}ms FBO ShadowMapping Created.", launch_timer.ElapsedMilliseconds)

        MiniMapFBO.FBO_Initialize(240) '<- default start up size
        LogThis("{0}ms FBO Mini Created.", launch_timer.ElapsedMilliseconds)

        build_shaders()
        LogThis("{0}ms Shaders Built.", launch_timer.ElapsedMilliseconds)

        load_assets()
        LogThis("{0}ms Assets Loaded.", launch_timer.ElapsedMilliseconds)

        MapMenuScreen.Invalidate()

        '-----------------------------------------------------------------------------------------
        LogThis("{0}ms Starting Update Thread", launch_timer.ElapsedMilliseconds)
        _STARTED = True ' I'm ready for update loops!

        SHOW_MAPS_SCREEN = True '<---- Un-rem to show map menu at startup.

        _controller = New ImGuiController(ClientSize.X, ClientSize.Y)
    End Sub

    Private Sub m_set_game_path()
        Dim FolderBrowserDialog1 As New FolderBrowserDialog

        'Sets the game path folder
try_again:
        If FolderBrowserDialog1.ShowDialog = DialogResult.OK Then
            My.Settings.GamePath = FolderBrowserDialog1.SelectedPath
            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("Wrong Folder Path!" + vbCrLf +
                       "You need to point at the World_of_Tanks folder!",
                        MsgBoxStyle.Exclamation, "Wrong Path!")
                GoTo try_again
            End If
        End If
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)

        SCR_WIDTH = Math.Max(1, ClientSize.X)
        SCR_HEIGHT = Math.Max(1, ClientSize.Y)

        If Not WindowState = WindowState.Minimized Then
            MainFBO.FBO_Initialize()
        End If

        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.Invalidate()
        End If

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)

        _controller.WindowResized(ClientSize.X, ClientSize.Y)

        ForceRender()
    End Sub

    Protected Overrides Sub OnRenderFrame(args As FrameEventArgs)
        MyBase.OnRenderFrame(args)

        DELTA_TIME = args.Time
        FPS_TIME = args.Time

        ForceRender(args.Time)
    End Sub

    Public Sub ForceRender(Optional time As Single = 0.0)
        draw_scene()

        _controller.Update(Me, CSng(time))

        SubmitUI()
        _controller.Render()

        SwapBuffers()
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyboardKeyEventArgs)
        MyBase.OnKeyDown(e)

        If _controller IsNot Nothing AndAlso ImGui.GetIO().WantCaptureKeyboard Then
            Return
        End If

        Select Case e.Key
            Case Keys.A
                WASD_VECTOR.X = -3.0F
            Case Keys.D
                WASD_VECTOR.X = 3.0F
            Case Keys.W
                WASD_VECTOR.Y = -3.0F
            Case Keys.S
                WASD_VECTOR.Y = 3.0F
        End Select
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyboardKeyEventArgs)
        MyBase.OnKeyUp(e)

        If _controller IsNot Nothing AndAlso ImGui.GetIO().WantCaptureKeyboard Then
            Return
        End If

        Z_MOVE = False
        MOVE_MOD = False
        Select Case e.Key
            Case Keys.A
                WASD_VECTOR.X = 0
            Case Keys.D
                WASD_VECTOR.X = 0
            Case Keys.W
                WASD_VECTOR.Y = 0
            Case Keys.S
                WASD_VECTOR.Y = 0
        End Select
    End Sub

    Private Sub WASD_movement()
        If WASD_VECTOR.X <> 0 Or WASD_VECTOR.Y <> 0 Then
            WASD_SPEED += DELTA_TIME
            If WASD_SPEED > 0.025F Then
                WASD_SPEED = 0F
                Dim MAX = -200.0F
                If MAX < map_scene.camera.VIEW_RADIUS Then
                    MAX = map_scene.camera.VIEW_RADIUS
                End If
                Dim ms As Single = 0.2F * MAX ' distance away changes speed.. THIS WORKS WELL!
                Dim t = WASD_VECTOR.X * ms * 0.003

                If WASD_VECTOR.X <> 0 Then
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (-Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                End If

                t = WASD_VECTOR.Y * ms * 0.003F

                If WASD_VECTOR.Y <> 0 Then
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                End If

            End If
        End If
    End Sub

    Private Sub load_assets()
        'setup text renderer
        Dim sp = Application.StartupPath
        '---------------------------------------------------------

        '---------------------------------------------------------
        ' Init packages
        ResMgr.Init(My.Settings.GamePath)

        '---------------------------------------------------------
        'Loads the textures for the map selection routines
        MapMenuScreen.make_map_pick_buttons()
        '---------------------------------------------------------

        '==========================================================
        DUMMY_TEXTURE_ID = make_dummy_texture()
        '==========================================================

        '---------------------------------------------------------
        'background screen image
        CHECKER_BOARD = load_png_image_from_file(Path.Combine(sp, "resources", "CheckerPatternPaper.png"), False, False)
        '---------------------------------------------------------
        'cursor texture
        '---------------------------------------------------------
        'MiniMap position/direction img
        DIRECTION_TEXTURE_ID = load_png_image_from_file(Path.Combine(sp, "resources", "direction.png"), True, False)
        '---------------------------------------------------------
        'load progress bar gradient image from the GUI package.
        PROGRESS_BAR_IMAGE_ID = load_png_image_from_file(Path.Combine(sp, "resources", "progress_bar.png"), False, True)

        '---------------------------------------------------------
        ' build Ascii characters texture.
        ASCII_ID = build_ascii_characters()

        '===========================================================================================
        ' needed for terrain atlas textures
        make_dummy_4_layer_atlas()
        '===========================================================================================

        'This gets the first GL texture, vertex array and vertex buffer IDs after the static IDs
        'ALL STATIC ITEMS NEED TO BE LOADED BEFORE THIS IS CALLED!!!
        get_start_ID_for_Components_Deletion()

    End Sub

    Protected Overrides Sub OnUpdateFrame(args As FrameEventArgs)
        MyBase.OnUpdateFrame(args)

        Dim io = ImGui.GetIO()
        If _controller IsNot Nothing AndAlso (io.WantCaptureKeyboard OrElse io.WantCaptureMouse) Then
            Return
        End If

        If Not IsFocused Then
            Return
        End If

        Dim input = KeyboardState
        Dim mouse = MouseState

        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.Scroll(mouse.ScrollDelta.Y)
        End If

        If mouse.IsButtonDown(MouseButton.Left) Then
            If MINI_MOUSE_CAPTURED Then
                'User clicked on the mini so lets move to that locations in world space
                map_scene.camera.LOOK_AT_X = MINI_WORLD_MOUSE_POSITION.X
                map_scene.camera.LOOK_AT_Z = MINI_WORLD_MOUSE_POSITION.Y
            End If

            If SHOW_MAPS_SCREEN Then
                If MapMenuScreen.SelectedMap Is Nothing Then
                    If MAP_LOADED Then
                        SHOW_MAPS_SCREEN = False
                        Return
                    End If
                Else
                    BLOCK_MOUSE = True
                    FINISH_MAPS = True
                    MAP_LOADED = False
                    Me.Title = MapMenuScreen.SelectedMap.realname
                    Return
                End If
            End If
        End If

        If mouse.IsButtonDown(MouseButton.Right) Then
            MOVE_CAM_Z = True
        End If

        If mouse.IsButtonDown(MouseButton.Middle) Then
            MOVE_MOD = True
            M_DOWN = True
        End If

        If mouse.IsButtonDown(MouseButton.Left) Then
            M_DOWN = True
        End If

        ' HACK!
        mouse_last_pos = New Point(mouse.Position.X, mouse.Position.Y)

        WASD_movement()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseMoveEventArgs)
        MyBase.OnMouseMove(e)

        Dim io = ImGui.GetIO()
        If _controller IsNot Nothing AndAlso (io.WantCaptureKeyboard OrElse io.WantCaptureMouse) Then
            Return
        End If

        If BLOCK_MOUSE Then Return
        M_MOUSE.X = e.X
        M_MOUSE.Y = e.Y
        'If check_menu_select() Then ' check if we are over a button
        '    Return
        'End If
        Dim dead As Integer = 5
        Dim t As Single
        Dim M_Speed As Single = My.Settings.speed
        If map_scene IsNot Nothing Then
            Dim ms As Single = 0.2F * map_scene.camera.VIEW_RADIUS ' distance away changes speed.. THIS WORKS WELL!
            If M_DOWN Then
                If e.X > (mouse_last_pos.X + dead) Then
                    If e.X - mouse_last_pos.X > 100 Then t = (1.0F * M_Speed)
                Else : t = CSng(Math.Sin((e.X - mouse_last_pos.X) / 100)) * M_Speed
                    If Not Z_MOVE Then
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_Z -= ((t * ms) * (-Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            map_scene.camera.CAM_X_ANGLE -= t
                        End If
                        If map_scene.camera.CAM_X_ANGLE > (2 * PI) Then map_scene.camera.CAM_X_ANGLE -= (2 * PI)
                    End If
                End If
                If e.X < (mouse_last_pos.X - dead) Then
                    If mouse_last_pos.X - e.X > 100 Then t = (M_Speed)
                Else : t = CSng(Math.Sin((mouse_last_pos.X - e.X) / 100)) * M_Speed
                    If Not Z_MOVE Then
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_X += ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_Z += ((t * ms) * (-Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            map_scene.camera.CAM_X_ANGLE += t
                        End If
                        If map_scene.camera.CAM_X_ANGLE < 0 Then map_scene.camera.CAM_X_ANGLE += (2 * PI)
                    End If
                End If
                ' ------- Y moves ----------------------------------
                If e.Y > (mouse_last_pos.Y + dead) Then
                    If e.Y - mouse_last_pos.Y > 100 Then t = (M_Speed)
                Else : t = CSng(Math.Sin((e.Y - mouse_last_pos.Y) / 100)) * M_Speed
                    If Z_MOVE Then
                        map_scene.camera.LOOK_AT_Y -= (t * ms)
                    Else
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_Z -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            If map_scene.camera.CAM_Y_ANGLE - t < -PI / 2.0 Then
                                map_scene.camera.CAM_Y_ANGLE = -PI / 2.0 + 0.001
                            Else
                                map_scene.camera.CAM_Y_ANGLE -= t
                            End If
                        End If
                        'If CAM_Y_ANGLE < -PI / 2.0 Then CAM_Y_ANGLE = -PI / 2.0 + 0.001
                    End If
                End If
                If e.Y < (mouse_last_pos.Y - dead) Then
                    If mouse_last_pos.Y - e.Y > 100 Then t = (M_Speed)
                Else : t = CSng(Math.Sin((mouse_last_pos.Y - e.Y) / 100)) * M_Speed
                    If Z_MOVE Then
                        map_scene.camera.LOOK_AT_Y += (t * ms)
                    Else
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_Z += ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_X += ((t * ms) * (Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            If map_scene.camera.CAM_Y_ANGLE + t > 1.3 Then
                                map_scene.camera.CAM_Y_ANGLE = 1.3
                            Else
                                map_scene.camera.CAM_Y_ANGLE += t
                            End If

                        End If
                        'If CAM_Y_ANGLE > 1.3 Then CAM_Y_ANGLE = 1.3
                    End If
                End If
                Return
            End If
            If MOVE_CAM_Z Then
                Dim vrad = map_scene.camera.VIEW_RADIUS
                If e.Y < (mouse_last_pos.Y - dead) Then
                    If e.Y - mouse_last_pos.Y > 100 Then t = (10)
                Else : t = CSng(Math.Sin((e.Y - mouse_last_pos.Y) / 100)) * 12 * My.Settings.speed
                    If vrad + (t * (vrad * 0.2)) < map_scene.camera.MAX_ZOOM_OUT Then
                        vrad = map_scene.camera.MAX_ZOOM_OUT
                    Else
                        vrad += (t * (vrad * 0.2))
                    End If
                End If
                If e.Y > (mouse_last_pos.Y + dead) Then
                    If mouse_last_pos.Y - e.Y > 100 Then t = (10)
                Else : t = CSng(Math.Sin((mouse_last_pos.Y - e.Y) / 100)) * 12 * My.Settings.speed
                    vrad -= (t * (vrad * 0.2))    ' zoom is factored in to Cam radius
                    If vrad > -0.01 Then vrad = -0.01
                End If
                If vrad > -0.1 Then vrad = -0.1
                map_scene.camera.VIEW_RADIUS = vrad
                Return
            End If
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseButtonEventArgs)
        MyBase.OnMouseUp(e)

        Dim io = ImGui.GetIO()
        If io.WantCaptureKeyboard OrElse io.WantCaptureMouse Then
            Return
        End If

        M_DOWN = False
        MOVE_CAM_Z = False
        MOVE_MOD = False
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseWheelEventArgs)
        MyBase.OnMouseWheel(e)

        _controller.MouseScroll(e.Offset)
    End Sub

    Protected Overrides Sub OnTextInput(e As TextInputEventArgs)
        MyBase.OnTextInput(e)

        _controller.PressChar(ChrW(e.Unicode))
    End Sub

    Private Sub SubmitUI()
        ImGui.SetNextWindowPos(New Numerics.Vector2(0, 0))
        If ImGui.Begin("Dummy Window 1", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings) Then
            If ImGui.Button("Load map") Then
                'Runs Map picking code.
                MapMenuScreen.Invalidate()
                SHOW_MAPS_SCREEN = True
            End If
            ImGui.End()
        End If

        ImGui.SetNextWindowPos(New Numerics.Vector2(80, 4))
        If ImGui.Begin("Dummy Window 2", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings) Then
            ImGui.Text(String.Format("VRAM usage: {0,-4}mb of {1}mb", GLCapabilities.memory_usage, GLCapabilities.total_mem_mb))
            ImGui.End()
        End If

        If ImGui.Begin("Settings") Then
            If ImGui.CollapsingHeader("Map") Then
                ImGui.Checkbox("Draw bases", DONT_BLOCK_BASES)
                ImGui.Checkbox("Draw decals", DONT_BLOCK_DECALS)
                ImGui.Checkbox("Draw models", DONT_BLOCK_MODELS)
                ImGui.Checkbox("Draw sky", DONT_BLOCK_SKY)
                ImGui.Checkbox("Draw terrain", DONT_BLOCK_TERRAIN)
                ImGui.Checkbox("Draw Outland", DONT_BLOCK_OUTLAND)
                ImGui.Checkbox("Draw trees", DONT_BLOCK_TREES)
                ImGui.Checkbox("Draw water", DONT_BLOCK_WATER)
            End If
            If ImGui.CollapsingHeader("Overlays") Then
                ImGui.Checkbox("Draw terrain wire", WIRE_TERRAIN)
                ImGui.Checkbox("Draw model wire", WIRE_MODELS)
                ImGui.Checkbox("Draw bounding boxes", SHOW_BOUNDING_BOXES)
                ImGui.Checkbox("Draw chunks", SHOW_CHUNKS)
                ImGui.Checkbox("Draw grid", SHOW_GRID)
                ImGui.Checkbox("Draw border", SHOW_BORDER)
                ImGui.Checkbox("Draw chunk ids", SHOW_CHUNK_IDs)
            End If
            If ImGui.CollapsingHeader("Culling") Then
                ImGui.Checkbox("Raster culling", USE_RASTER_CULLING)
            End If
            If ImGui.CollapsingHeader("Terrain") Then
                ImGui.Checkbox("Use tessellation", USE_TESSELLATION)
            End If
            If ImGui.CollapsingHeader("Lighting Settings") Then
                ImGui.SliderFloat("AMBIENT", CommonProperties.AMBIENT, 0.0, 1.0)
                ImGui.SliderFloat("BRIGHTNESS", CommonProperties.BRIGHTNESS, 0.0, 1.0)
                ImGui.SliderFloat("SPECULAR", CommonProperties.SPECULAR, 0.0, 1.0)
                ImGui.SliderFloat("GRAY_LEVEL", CommonProperties.GRAY_LEVEL, 0.0, 1.0)
                ImGui.SliderFloat("GAMMA_LEVEL", CommonProperties.GAMMA_LEVEL, 0.0, 1.0)
                ImGui.SliderFloat("fog_level", CommonProperties.fog_level, 0.0, 1.0)
                If ImGui.Button("Update") Then
                    CommonProperties.update()
                End If
            End If
            ImGui.Separator()
            If ImGui.Button(String.Format("Version {0}", Application.ProductVersion)) Then
                Using proc As New Process
                    proc.StartInfo.UseShellExecute = True
                    proc.StartInfo.FileName = "https://github.com/mikeoverbay/nuTerra/releases"
                    proc.Start()
                End Using
            End If
            If ImGui.Button("View Help") Then
                Using proc As New Process
                    proc.StartInfo.UseShellExecute = True
                    proc.StartInfo.FileName = Path.Combine(Application.StartupPath, "HTML", "index.html")
                    proc.Start()
                End Using
            End If
            ImGui.End()
        End If
    End Sub

End Class

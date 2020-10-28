﻿Imports System.Globalization
Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

'Imports Config = OpenTK.Configuration
'Imports Utilities = OpenTK.Platform.Utilities

Public Class frmMain
    Private fps_timer As New System.Diagnostics.Stopwatch
    Private game_clock As New System.Diagnostics.Stopwatch
    Private launch_timer As New System.Diagnostics.Stopwatch

    Dim MINIMIZED As Boolean

#Region "Form Events"

    Private Sub frmMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        _STARTED = False
        If frmGbufferViewer.Visible Then
            frmGbufferViewer.Visible = False
            frmGbufferViewer.Dispose()
        End If
        remove_map_data()
    End Sub

    Private Sub frmMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown

        Dim max_size As Single = 640

        Select Case e.KeyCode
            '-------------------------------
            'mini map size
            Case Keys.Oemplus
                If MINI_MAP_NEW_SIZE = MINI_MAP_SIZE Then
                    If MINI_MAP_NEW_SIZE < max_size Then
                        MINI_MAP_NEW_SIZE += 40
                        FBOmini.FBO_Initialize(MINI_MAP_NEW_SIZE)
                    End If
                End If
            Case Keys.OemMinus
                If MINI_MAP_NEW_SIZE = MINI_MAP_SIZE Then
                    If MINI_MAP_NEW_SIZE > 100 Then
                        MINI_MAP_NEW_SIZE -= 40
                        FBOmini.FBO_Initialize(MINI_MAP_NEW_SIZE)
                    End If
                End If
                '-------------------------------
                'wire modes
            Case Keys.D1
                WIRE_MODELS = WIRE_MODELS Xor True

            Case Keys.D2
                WIRE_TERRAIN = WIRE_TERRAIN Xor True

            Case Keys.F1
                SHOW_CURSOR = SHOW_CURSOR Xor True

                '-------------------------------
                'grid display
            Case Keys.F5
                SHOW_CHUNKS = SHOW_CHUNKS Xor 1

            Case Keys.F6
                SHOW_GRID = SHOW_GRID Xor 1

            Case Keys.F7
                SHOW_BORDER = SHOW_BORDER Xor 1

            Case Keys.F8
                SSAA_enable = SSAA_enable Xor True
                If SSAA_enable Then
                    SSAA_text = "SSAA On"
                Else
                    SSAA_text = "SSAA Off"
                End If
            Case Keys.F9
                SHOW_LOD_COLORS = SHOW_LOD_COLORS Xor 1
                '-------------------------------
            Case Keys.B
                SHOW_BOUNDING_BOXES = SHOW_BOUNDING_BOXES Xor True

            Case Keys.G
                If MAP_LOADED Then frmGbufferViewer.Show()

            Case Keys.E
                frmEditFrag.Show()

            Case Keys.I
                SHOW_CHUNK_IDs = SHOW_CHUNK_IDs Xor True

            Case Keys.L
                If Not frmLighting.Visible Then
                    m_light_settings.PerformClick()
                Else
                    frmLighting.Visible = False
                End If

            Case Keys.M
                DONT_HIDE_MINIMAP = DONT_HIDE_MINIMAP Xor True

            Case Keys.N
                ' 0 None, 1 by vertex, 2 by face
                NORMAL_DISPLAY_MODE += 1
                If NORMAL_DISPLAY_MODE > 2 Then
                    NORMAL_DISPLAY_MODE = 0
                End If

            Case Keys.O
                m_load_map.PerformClick()

            Case Keys.P
                PICK_MODELS = PICK_MODELS Xor True

            Case Keys.T
                SHOW_TEST_TEXTURES = SHOW_TEST_TEXTURES Xor 1

            Case Keys.V
                DONT_HIDE_HUD = DONT_HIDE_HUD Xor True
                '-------------------------------
                'Special Keys
            Case Keys.ControlKey
                Z_MOVE = True

            Case Keys.ShiftKey
                MOVE_MOD = True

            Case Keys.Space
                PAUSE_ORBIT = PAUSE_ORBIT Xor True

                 '-------------------------------
               'Navigation
            Case Keys.A
                WASD_VECTOR.X = -1
            Case Keys.D
                WASD_VECTOR.X = 1
            Case Keys.W
                WASD_VECTOR.Y = -1
            Case Keys.S
                WASD_VECTOR.Y = 1

        End Select
    End Sub

    Private Sub frmMain_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        Z_MOVE = False
        MOVE_MOD = False
        Select Case e.KeyCode
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

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles Me.Load
#If DEBUG Then
        ' Set to True on Debug builds
        Me.m_developer.Visible = True
#End If

        ' Init main gl-control
        Dim flags As GraphicsContextFlags
#If DEBUG Then
        flags = GraphicsContextFlags.ForwardCompatible Or GraphicsContextFlags.Debug
#Else
        flags = GraphicsContextFlags.ForwardCompatible
#End If

        Me.glControl_main = New OpenTK.GLControl(New GraphicsMode(ColorFormat.Empty, 0), 4, 5, flags)
        Me.glControl_main.VSync = False
        Me.Controls.Add(Me.glControl_main)

        '-----------------------------------------------------------------------------------------
        Me.Show()
        '-----------------------------------------------------------------------------------------

        ' we dont want menu events while the app is initializing :)
        MainMenuStrip.Enabled = False
        '-----------------------------------------------------------------------------------------
        'So numbers work in any nation I'm running in.
        Dim nonInvariantCulture As CultureInfo = New CultureInfo("en-US")
        nonInvariantCulture.NumberFormat.NumberDecimalSeparator = "."
        Thread.CurrentThread.CurrentCulture = nonInvariantCulture
        '-----------------------------------------------------------------------------------------
        Me.KeyPreview = True    'So I catch keyboard before despatching it
        '-----------------------------------------------------------------------------------------
        'get directory of all shader files
        SHADER_PATHS = Directory.GetFiles(Application.StartupPath + "\shaders\", "*.*", SearchOption.AllDirectories)
        '-----------------------------------------------------------------------------------------
        'Debugger.Break()
        'This timer allows the form to become visible before we initialize everything
        'It is disposed after its done its job.
        startup_delay_timer.Start()
    End Sub

    Private Sub frmMain_MouseDown(sender As Object, e As MouseEventArgs) Handles Me.MouseDown
    End Sub

    Public Sub resize_fbo_main()
        Dim ww, hh As Integer
        FBOm.get_glControl_size(ww, hh)
        If hh <> FBOm.SCR_WIDTH Or ww <> FBOm.SCR_HEIGHT Then
            If Not Me.WindowState = FormWindowState.Minimized Then
                FBOm.FBO_Initialize()
            End If
        End If

    End Sub

    Private Sub frmMain_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        If _STARTED Then
            resize_fbo_main()
            draw_scene()
        End If
        If Me.WindowState = FormWindowState.Minimized Then
            MINIMIZED = True
        Else
            MINIMIZED = False
        End If
    End Sub

    Private Sub frmMain_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        If _STARTED Then
            resize_fbo_main()
            draw_scene()
        End If
        'Dim s = Me.Size

    End Sub

    Private Sub startup_delay_timer_Tick(sender As Object, e As EventArgs) Handles startup_delay_timer.Tick
        startup_delay_timer.Enabled = False
        post_frmMain_loaded()
    End Sub

#End Region

#Region "FrmMain menu events"

    Private Sub m_light_settings_Click(sender As Object, e As EventArgs) Handles m_light_settings.Click
        'Opens light setting window
        If Not frmLighting.Visible Then
            frmLighting.Show()
        Else
            frmLighting.Hide()
        End If
    End Sub

    Private Sub m_load_map_Click(sender As Object, e As EventArgs) Handles m_load_map.Click
        'we are disabling this to speed up debugging of space.bin
#If 1 Then
        'Return
#End If
        'Runs Map picking code.
        glControl_main.MakeCurrent()
        SHOW_MAPS_SCREEN = True
        SELECTED_MAP_HIT = 0
    End Sub

    Private Sub m_set_game_path_Click(sender As Object, e As EventArgs) Handles m_set_game_path.Click
        'Sets the game path folder
try_again:
        If FolderBrowserDialog1.ShowDialog = Forms.DialogResult.OK Then
            My.Settings.GamePath = FolderBrowserDialog1.SelectedPath
            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("Wrong Folder Path!" + vbCrLf +
                       "You need to point at the World_of_Tanks folder!",
                        MsgBoxStyle.Exclamation, "Wrong Path!")
                GoTo try_again
            End If
        End If
    End Sub

    Private Sub m_help_Click(sender As Object, e As EventArgs) Handles m_help.Click
        'Opens the index.HTML help/info file in the users default web browser.
        Dim p = Application.StartupPath + "\HTML\index.html"
        Process.Start(p)
    End Sub

    Private Sub m_show_gbuffer_Click(sender As Object, e As EventArgs) Handles m_show_gbuffer.Click
        'Shows the Gbuffer Viwer.
        frmGbufferViewer.Visible = True
    End Sub

    Private Sub m_block_loading_Click(sender As Object, e As EventArgs) Handles m_block_loading.Click
        'Opens the window to chose what to block from loading.
        frmLoadOptions.Visible = True
    End Sub

    Private Sub m_shut_down_Click(sender As Object, e As EventArgs) Handles m_shut_down.Click
        'Closes the app.
        Me.Close()
    End Sub

    Private Sub m_developer_mode_Click(sender As Object, e As EventArgs) Handles m_developer_mode.Click
        'Makes the developer menu visible.
        If m_developer.Visible Then
            m_developer.Visible = False
        Else
            m_developer.Visible = True
        End If
    End Sub

    Private Sub m_Log_File_Click(sender As Object, e As EventArgs) Handles m_Log_File.Click
        frmShowText.Show()
        frmShowText.FastColoredTextBox1.Text = File.ReadAllText(Path.Combine(TEMP_STORAGE, "nuTerra_log.txt"))
    End Sub

#End Region

    '=================================================================================
    Private Sub post_frmMain_loaded()
        '-----------------------------------------------------------------------------------------
        launch_timer.Restart() 'log eplase times
        '-----------------------------------------------------------------------------------------
        glControl_main.MakeCurrent()

        'Check context:
        Dim majorVersion = GL.GetInteger(GetPName.MajorVersion)
        Dim minorVersion = GL.GetInteger(GetPName.MinorVersion)
        If majorVersion < 4 Or (majorVersion = 4 And minorVersion < 5) Then
            MsgBox("A graphics card and driver with support for OpenGL 4.5 or higher is required.")
            Application.Exit()
            Return
        End If

        '-----------------------------------------------------------------------------------------
        'need a work area on users disc
        TEMP_STORAGE = Path.Combine(Path.GetTempPath, "nuTerra")
        If Not Directory.Exists(TEMP_STORAGE) Then
            Directory.CreateDirectory(TEMP_STORAGE)
        End If
        LogThis(String.Format("{0}ms Temp storage is located at: {1}", launch_timer.ElapsedMilliseconds.ToString("0000"), TEMP_STORAGE))

        LogThis(String.Format("Vendor: {0}", GL.GetString(StringName.Vendor)))
        LogThis(String.Format("Renderer: {0}", GL.GetString(StringName.Renderer)))
        LogThis(String.Format("Version: {0}", GL.GetString(StringName.Version)))
        LogThis(String.Format("GLSL Version: {0}", GL.GetString(StringName.ShadingLanguageVersion)))

        Dim extensions As New List(Of String)
        Dim numExt As Integer = GL.GetInteger(GetPName.NumExtensions)
        For i = 0 To numExt - 1
            extensions.Add(GL.GetString(StringNameIndexed.Extensions, i))
        Next

        USE_NV_MESH_SHADER = extensions.Contains("GL_NV_mesh_shader")

        ' Requied extensions
        Debug.Assert(extensions.Contains("GL_ARB_vertex_type_10f_11f_11f_rev"))
        Debug.Assert(extensions.Contains("GL_ARB_shading_language_include"))
        Debug.Assert(extensions.Contains("GL_ARB_indirect_parameters"))
        Debug.Assert(extensions.Contains("GL_ARB_multi_draw_indirect")) 'core since 4.3
        Debug.Assert(extensions.Contains("GL_ARB_direct_state_access")) 'core since 4.5
        Debug.Assert(extensions.Contains("GL_ARB_clip_control")) 'core since 4.5

#If DEBUG Then
        ' Just check
        Debug.Assert(extensions.Contains("GL_KHR_debug"))
        Debug.Assert(extensions.Contains("GL_ARB_debug_output"))

        If GL.GetInteger(GetPName.ContextFlags) And ContextFlagMask.ContextFlagDebugBit Then
            LogThis("Setup Debug Output Callback")
            SetupDebugOutputCallback()
        End If
#End If
        '-----------------------------------------------------------------------------------------
        'Any relevant info the user could use.
        Dim maxTexSize = GL.GetInteger(GetPName.MaxTextureSize)
        LogThis(String.Format("Max Texture Size = {0}", maxTexSize))
        '-----------------------------------------------------------------------------------------

        ' Set depth to [0..1] range instead of [-1..1]
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne)

        ' Enable depth clamping
        GL.Enable(EnableCap.DepthClamp)

        '-----------------------------------------------------------------------------------------
        'Check if the game path is set
        If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
            MsgBox("Path to game is not set!" + vbCrLf +
                    "Lets set it now.", MsgBoxStyle.OkOnly, "Game Path not set")
            m_set_game_path.PerformClick()

            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("This application will be closed because game was not found!")
                Application.Exit()
                Return
            End If
        End If

        GAME_PATH = Path.Combine(My.Settings.GamePath, "res", "packages")
        LogThis(String.Format("{0}ms Packages Path: {1}", launch_timer.ElapsedMilliseconds.ToString("0000"), GAME_PATH))

        ' Create default VAO
        GL.CreateVertexArrays(1, defaultVao)
        GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, defaultVao, -1, "defaultVao")

        make_cube()

        GL.CreateBuffers(1, PerViewDataBuffer)
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, PerViewDataBuffer, -1, "PerView")
        GL.NamedBufferStorage(PerViewDataBuffer, Marshal.SizeOf(PerViewData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit)
        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 1, PerViewDataBuffer)

        FBOm.FBO_Initialize()
        LogThis(String.Format("{0}ms FBO Main Created.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        FBOmini.FBO_Initialize(240) '<- default start up size
        LogThis(String.Format("{0}ms FBO Mini Created.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        Il.ilInit()
        Ilu.iluInit()
        LogThis(String.Format("{0}ms DevIL Initialized.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        build_shaders()
        LogThis(String.Format("{0}ms Shaders Built.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        load_assets()
        LogThis(String.Format("{0}ms Assets Loaded.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        'Set camara start up position. This is mostly for testing.
        VIEW_RADIUS = -1000.0
        CAM_X_ANGLE = PI / 4
        CAM_Y_ANGLE = -PI / 4

        'Everything is setup/loaded to show the main window.
        'Dispose of the no longer used Panel1
        Panel1.Visible = False
        Me.Controls.Remove(Panel1)
        Panel1.Dispose()
        glControl_main.BringToFront()
        GC.Collect() 'Start a clean up of disposed items
        '-----------------------------------------------------------------------------------------
        'Must load and hide frmLighting to access its functions.
        frmLighting.StartPosition = FormStartPosition.CenterParent
        frmLighting.TopMost = False
        frmLighting.SendToBack()
        frmLighting.Show()
        frmLighting.Visible = False
        frmLighting.TopMost = True
        '-----------------------------------------------------------------------------------------
        'we are ready for user input so lets enable the menu
        MainMenuStrip.Enabled = True
        '-----------------------------------------------------------------------------------------
        LogThis(launch_timer.ElapsedMilliseconds.ToString("0000") + "ms " +
                "Starting Update Thread")
        _STARTED = True ' I'm ready for update loops!
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        launch_update_thread()
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        'This is temporary to speed up debugging
        '-----------------------------------------------------------------------------------------
        'load_map("19_monastery.pkg")
        'load_map("08_ruinberg.pkg")
        'load_map("14_siegfried_line.pkg")
        'load_map("29_el_hallouf.pkg")
        'load_map("31_airfield.pkg")
        'load_map("112_eiffel_tower_ctf.pkg")
    End Sub
    '=================================================================================

    ''' <summary>
    ''' Loads all assets nuTerra uses.
    ''' </summary>
    Private Sub load_assets()
        'setup text renderer
        Dim sp = Application.StartupPath
        '-----------------------------------------------------------------------------------------
        'needed to load image elements
        If File.Exists(Path.Combine(GAME_PATH, "gui.pkg")) Then
            'old WoT version
            Packages.GUI_PACKAGE = New Ionic.Zip.ZipFile(Path.Combine(GAME_PATH, "gui.pkg"))
        Else
            'new WoT version ~v1.10
            Packages.GUI_PACKAGE = New Ionic.Zip.ZipFile(Path.Combine(GAME_PATH, "gui-part1.pkg"))
            Packages.GUI_PACKAGE_PART2 = New Ionic.Zip.ZipFile(Path.Combine(GAME_PATH, "gui-part2.pkg"))
        End If

        '---------------------------------------------------------
        'Loads the textures for the map selection routines
        make_map_pick_buttons()
        '-----------------------------------------------------------------------------------------

        '---------------------------------------------------------
        'load the xml list of all item locations
        load_lookup_xml()
        '---------------------------------------------------------
        'loading screen image
        nuTERRA_BG_IMAGE =
            load_image_from_file(Il.IL_PNG,
            sp + "\resources\earth.png", False, True)
        '---------------------------------------------------------
        'cursor texture
        CURSOR_TEXTURE_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\Cursor.png", True, False)
        '---------------------------------------------------------
        'MiniMap position/direction img
        DIRECTION_TEXTURE_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\direction.png", True, False)
        '---------------------------------------------------------
        'MiniMap Letter Legends
        MINI_LETTERS_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\mini_letters.png", False, False)
        '---------------------------------------------------------
        'MiniMap Number Legends
        MINI_NUMBERS_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\mini_numbers.png", False, False)
        '---------------------------------------------------------
        'MiniMap vert trim
        MINI_TRIM_VERT_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\mini_trim_vert.png", False, False)
        '---------------------------------------------------------
        'MiniMap horz trim
        MINI_TRIM_HORZ_ID = load_image_from_file(Il.IL_PNG,
            sp + "\resources\mini_trim_horz.png", False, False)
        '---------------------------------------------------------
        'load progress bar gradient image from the GUI package.
        PROGRESS_BAR_IMAGE_ID =
            load_image_from_file(Il.IL_PNG,
            sp + "\resources\progress_bar.png", False, True)
        '---------------------------------------------------------
        'load Ascii characters image.
        ASCII_ID =
            load_image_from_file(Il.IL_PNG,
            sp + "\resources\ascii_characters.png", False, True)
        '---------------------------------------------------------
        'Test Textures
        For i = 0 To 7
            TEST_IDS(i) =
                load_image_from_file(Il.IL_PNG,
                sp + "\resources\TestTextures\tex_" + i.ToString + ".png", True, False)
        Next
        '---------------------------------------------------------        'Test Textures
#If False Then
        'This can be used to debug textureing
        checkerTest =
                load_image_from_file(Il.IL_PNG,
                sp + "\resources\checkerboard.png", True, False)
#End If
        '---------------------------------------------------------
        'need to create this texture.
        DrawMapPickText.TextRenderer(300, 30)

        'This gets the first GL texture, vertex array and vertex buffer IDs after the static IDs
        'ALL STATIC ITEMS NEED TO BE LOADED BEFORE THIS IS CALLED!!!
        get_start_ID_for_Components_Deletion()

    End Sub

#Region "Screen position and update"

    Private Sub launch_update_thread()
        fps_timer.Start()
        game_clock.Start()
        SHOW_MAPS_SCREEN = True '<---- Un-rem to show map menu at startup.
        'We wont use this timer again so lets remove it from memory
        startup_delay_timer.Dispose()
        closed_loop_updater()
    End Sub
    Private Sub WASD_movement()
        If WASD_VECTOR.X <> 0 Or WASD_VECTOR.Y <> 0 Then
            WASD_SPEED += DELTA_TIME
            If WASD_SPEED > 0.025 Then
                WASD_SPEED = 0
                Dim MAX = -200.0
                If MAX < VIEW_RADIUS Then
                    MAX = VIEW_RADIUS
                End If
                Dim ms As Single = 0.2F * MAX ' distance away changes speed.. THIS WORKS WELL!
                Dim t = WASD_VECTOR.X * ms * 0.003

                If WASD_VECTOR.X <> 0 Then
                    LOOK_AT_X -= ((t * ms) * (Cos(CAM_X_ANGLE)))
                    LOOK_AT_Z -= ((t * ms) * (-Sin(CAM_X_ANGLE)))
                End If

                t = WASD_VECTOR.Y * ms * 0.003

                If WASD_VECTOR.Y <> 0 Then
                    LOOK_AT_Z -= ((t * ms) * (Cos(CAM_X_ANGLE)))
                    LOOK_AT_X -= ((t * ms) * (Sin(CAM_X_ANGLE)))
                End If

            End If
        End If
    End Sub
    Private Sub closed_loop_updater()
        Dim trigger As Boolean = False
        Dim Time_before, Time_after As Long

        While _STARTED

            If MINIMIZED Then
                Application.DoEvents()
                Thread.Sleep(10)
            Else

                WASD_movement()
                '==============================================================
                If Not PAUSE_ORBIT Then
                    LIGHT_ORBIT_ANGLE_Z += (DELTA_TIME * 0.5)
                    If LIGHT_ORBIT_ANGLE_Z > PI * 2 Then LIGHT_ORBIT_ANGLE_Z -= PI * 2
                    LIGHT_POS(0) = Cos(LIGHT_ORBIT_ANGLE_Z) * LIGHT_RADIUS
                    LIGHT_POS(1) = Cos(LIGHT_ORBIT_ANGLE_X) * LIGHT_RADIUS
                    LIGHT_POS(2) = Sin(LIGHT_ORBIT_ANGLE_Z) * LIGHT_RADIUS
                End If
                CROSS_HAIR_TIME += (DELTA_TIME * 0.5)
                If CROSS_HAIR_TIME > 1.0F Then
                    CROSS_HAIR_TIME = 0.0F
                End If
                '==============================================================

                '==============================================================
                If fps_timer.ElapsedMilliseconds > 1000 Then
                    fps_timer.Restart()
                    FPS_TIME = FPS_COUNTER
                    FPS_COUNTER = 0
                End If
                '==============================================================

                '==============================================================
                check_postion_for_update()
                draw_scene()
                Application.DoEvents()
                '==============================================================

                '==============================================================
                'DELTA_TIME is elpased decimal seconds time. IE: 0.0003 seconds
                Time_after = game_clock.ElapsedTicks
                DELTA_TIME = CSng((Time_after - Time_before) / Stopwatch.Frequency)
                game_clock.Restart()
                Time_before = game_clock.ElapsedTicks
                '==============================================================
            End If
        End While
    End Sub

    Public Sub check_postion_for_update()
        Dim halfPI = PI * 0.5F
        If LOOK_AT_X <> U_LOOK_AT_X Then
            U_LOOK_AT_X = LOOK_AT_X
        End If
        If LOOK_AT_Y <> U_LOOK_AT_Y Then
            U_LOOK_AT_Y = LOOK_AT_Y
        End If
        If LOOK_AT_Z <> U_LOOK_AT_Z Then
            U_LOOK_AT_Z = LOOK_AT_Z
        End If
        If CAM_X_ANGLE <> U_CAM_X_ANGLE Then
            U_CAM_X_ANGLE = CAM_X_ANGLE
        End If
        If CAM_Y_ANGLE <> U_CAM_Y_ANGLE Then
            If CAM_Y_ANGLE > 1.3 Then
                U_CAM_Y_ANGLE = 1.3
                CAM_Y_ANGLE = U_CAM_Y_ANGLE
            End If
            If CAM_Y_ANGLE < -halfPI Then
                U_CAM_Y_ANGLE = -halfPI + 0.001
                CAM_Y_ANGLE = U_CAM_Y_ANGLE
            End If
            U_CAM_Y_ANGLE = CAM_Y_ANGLE
        End If
        If VIEW_RADIUS <> U_VIEW_RADIUS Then
            U_VIEW_RADIUS = VIEW_RADIUS
        End If

        CURSOR_Y = get_Y_at_XZ(U_LOOK_AT_X, U_LOOK_AT_Z)

    End Sub
#End Region


#Region "glControl_main events"

    Private Sub glControl_main_MouseDown(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseDown


        If BLOCK_MOUSE Then Return

        If MINI_MOUSE_CAPTURED Then
            'User clicked on the mini so lets move to that locations in world space
            LOOK_AT_X = MINI_WORLD_MOUSE_POSITION.X
            LOOK_AT_Z = MINI_WORLD_MOUSE_POSITION.Y
        End If

        If SHOW_MAPS_SCREEN Then
            If e.Button = Forms.MouseButtons.Left Then

                If SELECTED_MAP_HIT = 0 And MAP_LOADED Then
                    SHOW_MAPS_SCREEN = False
                    Application.DoEvents()
                    Return
                Else
                    Dim dx = SELECTED_MAP_HIT - 1 'deal with posible false hit
                    Try
                        Me.Text = "NuTerra : " + MapPickList(SELECTED_MAP_HIT - 1).realname

                    Catch ex As Exception

                    End Try

                    If dx < 0 Then Return
                    BLOCK_MOUSE = True
                    FINISH_MAPS = True
                    MOUSE.X = 0
                    MOUSE.Y = 0

                    Return
                End If
            End If
        End If
        MOUSE.X = e.X
        MOUSE.Y = e.Y
        If e.Button = Forms.MouseButtons.Right Then
            MOVE_CAM_Z = True
        End If
        If e.Button = Forms.MouseButtons.Middle Then
            MOVE_MOD = True
            M_DOWN = True
        End If
        If e.Button = Forms.MouseButtons.Left Then
            M_DOWN = True
        End If
    End Sub

    Private Sub glControl_main_MouseEnter(sender As Object, e As EventArgs) Handles glControl_main.MouseEnter

    End Sub

    Private Sub glControl_main_MouseUp(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseUp
        M_DOWN = False
        MOVE_CAM_Z = False
        MOVE_MOD = False
    End Sub

    Private Sub glControl_main_MouseMove(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseMove
        If BLOCK_MOUSE Then Return
        M_MOUSE.X = e.X
        M_MOUSE.Y = e.Y
        'If check_menu_select() Then ' check if we are over a button
        '    Return
        'End If
        Dim dead As Integer = 5
        Dim t As Single
        Dim M_Speed As Single = MOUSE_SPEED_GLOBAL
        Dim ms As Single = 0.2F * view_radius ' distance away changes speed.. THIS WORKS WELL!
        If M_DOWN Then
            If e.X > (MOUSE.X + dead) Then
                If e.X - MOUSE.X > 100 Then t = (1.0F * M_Speed)
            Else : t = CSng(Sin((e.X - MOUSE.X) / 100)) * M_Speed
                If Not Z_MOVE Then
                    If MOVE_MOD Then ' check for modifying flag
                        LOOK_AT_X -= ((t * ms) * (Cos(CAM_X_ANGLE)))
                        LOOK_AT_Z -= ((t * ms) * (-Sin(CAM_X_ANGLE)))
                    Else
                        CAM_X_ANGLE -= t
                    End If
                    If CAM_X_ANGLE > (2 * PI) Then CAM_X_ANGLE -= (2 * PI)
                    MOUSE.X = e.X
                End If
            End If
            If e.X < (MOUSE.X - dead) Then
                If MOUSE.X - e.X > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((MOUSE.X - e.X) / 100)) * M_Speed
                If Not Z_MOVE Then
                    If MOVE_MOD Then ' check for modifying flag
                        LOOK_AT_X += ((t * ms) * (Cos(CAM_X_ANGLE)))
                        LOOK_AT_Z += ((t * ms) * (-Sin(CAM_X_ANGLE)))
                    Else
                        CAM_X_ANGLE += t
                    End If
                    If CAM_X_ANGLE < 0 Then CAM_X_ANGLE += (2 * PI)
                    MOUSE.X = e.X
                End If
            End If
            ' ------- Y moves ----------------------------------
            If e.Y > (MOUSE.Y + dead) Then
                If e.Y - MOUSE.Y > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((e.Y - MOUSE.Y) / 100)) * M_Speed
                If Z_MOVE Then
                    LOOK_AT_Y -= (t * ms)
                Else
                    If MOVE_MOD Then ' check for modifying flag
                        LOOK_AT_Z -= ((t * ms) * (Cos(CAM_X_ANGLE)))
                        LOOK_AT_X -= ((t * ms) * (Sin(CAM_X_ANGLE)))
                    Else
                        If CAM_Y_ANGLE - t < -PI / 2.0 Then
                            CAM_Y_ANGLE = -PI / 2.0 + 0.001
                        Else
                            CAM_Y_ANGLE -= t
                        End If
                    End If
                    'If CAM_Y_ANGLE < -PI / 2.0 Then CAM_Y_ANGLE = -PI / 2.0 + 0.001
                End If
                MOUSE.Y = e.Y
            End If
            If e.Y < (MOUSE.Y - dead) Then
                If MOUSE.Y - e.Y > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((MOUSE.Y - e.Y) / 100)) * M_Speed
                If Z_MOVE Then
                    LOOK_AT_Y += (t * ms)
                Else
                    If MOVE_MOD Then ' check for modifying flag
                        LOOK_AT_Z += ((t * ms) * (Cos(CAM_X_ANGLE)))
                        LOOK_AT_X += ((t * ms) * (Sin(CAM_X_ANGLE)))
                    Else
                        If CAM_Y_ANGLE + t > 1.3 Then
                            CAM_Y_ANGLE = 1.3
                        Else
                            CAM_Y_ANGLE += t
                        End If

                    End If
                    'If CAM_Y_ANGLE > 1.3 Then CAM_Y_ANGLE = 1.3
                End If
                MOUSE.Y = e.Y
            End If
            'draw_scene()
            'Debug.WriteLine(Cam_X_angle.ToString("0.000") + " " + Cam_Y_angle.ToString("0.000"))
            Return
        End If
        If MOVE_CAM_Z Then
            If e.Y > (MOUSE.Y + dead) Then
                If e.Y - MOUSE.Y > 100 Then t = (10)
            Else : t = CSng(Sin((e.Y - MOUSE.Y) / 100)) * 12 * MOUSE_SPEED_GLOBAL
                view_radius += (t * (view_radius * 0.2))    ' zoom is factored in to Cam radius
                If VIEW_RADIUS < max_zoom_out Then
                    VIEW_RADIUS = max_zoom_out
                End If
                MOUSE.Y = e.Y
            End If
            If e.Y < (MOUSE.Y - dead) Then
                If MOUSE.Y - e.Y > 100 Then t = (10)
            Else : t = CSng(Sin((MOUSE.Y - e.Y) / 100)) * 12 * MOUSE_SPEED_GLOBAL
                view_radius -= (t * (view_radius * 0.2))    ' zoom is factored in to Cam radius
                If view_radius > -0.01 Then view_radius = -0.01
                MOUSE.Y = e.Y
            End If
            If view_radius > -0.1 Then view_radius = -0.1
            'draw_scene()
            Return
        End If
        MOUSE.X = e.X
        MOUSE.Y = e.Y

    End Sub

#End Region

    Private Sub map_loader_Tick(sender As Object, e As EventArgs) Handles map_loader.Tick
        map_loader.Enabled = False
        load_map(MAP_NAME_NO_PATH)
    End Sub

    Private Sub m_screen_capture_Click(sender As Object, e As EventArgs) Handles m_screen_capture.Click
        frmScreenCap.ShowDialog()
    End Sub

    Private Sub m_camera_options_Click(sender As Object, e As EventArgs) Handles m_camera_options.Click
        frmCameraOptions.Visible = True
    End Sub
End Class

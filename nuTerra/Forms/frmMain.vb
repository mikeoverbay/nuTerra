Imports System.Globalization
Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows
Imports OpenTK.Graphics
Imports OpenTK.Windowing.Common
Imports OpenTK.Graphics.OpenGL
Imports System.Reflection

Public Class frmMain
    '          SP2_Width = SplitContainer1.Panel2.Width
    Private Const WM_NCLBUTTONDBLCLK As Integer = &HA3
    Private Const HTCAPTION As Integer = &H2
    Private Const WM_SYSCOMMAND As Integer = &H112
    Private Const SC_MAXIMIZE As Integer = &HF030

    Protected Overrides Sub WndProc(ByRef m As Message)
        If (m.Msg = WM_SYSCOMMAND AndAlso m.WParam.ToInt32 = SC_MAXIMIZE) OrElse (m.Msg = WM_NCLBUTTONDBLCLK AndAlso m.WParam.ToInt32 = HTCAPTION) Then
            SP2_Width = SplitContainer1.Panel2.Width
            'm.Result = CType(0, IntPtr)
            'Return
        End If
        MyBase.WndProc(m)
    End Sub

    Dim last_state As FormWindowState
    Protected Overrides Sub OnClientSizeChanged(e As EventArgs)
        If last_state <> Me.WindowState Then
            last_state = Me.WindowState
            If _STARTED Then
                MainFBO.oldheight = -1
                resize_fbo_main()
            End If
        End If
        MyBase.OnClientSizeChanged(e)
    End Sub
    Private fps_timer As New System.Diagnostics.Stopwatch
    Private game_clock As New System.Diagnostics.Stopwatch
    Private launch_timer As New System.Diagnostics.Stopwatch
    Public SP2_Width As Integer
    Dim MINIMIZED As Boolean
    Public panel_2_occupied As Boolean
    Public PG_width As Integer
    Public FE_width As Integer

#Region "Form Events"

    Private Sub frmMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        _STARTED = False
        close_gracefully()
    End Sub
    Private Sub close_gracefully()
        'need to kill everything we created.
        'TODO
        'Need to delete all GL stuff too.
        close_megas()
        If frmGbufferViewer.Visible Then
            frmGbufferViewer.Visible = False
            frmGbufferViewer.Dispose()
        End If
        remove_map_data()
    End Sub
    Private Sub frmMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown

        ' If the Program editor is inserted in to frmMain,
        ' We have to stop key down events while typing in
        ' the editor's window.
        If frmProgramEditor IsNot Nothing AndAlso frmProgramEditor.CP_parent = Me.Handle Then
            If Not glControl_main.Focused Then
                Return
            End If
        End If

        'mini map max size
        Dim max_size As Single = 640

        Select Case e.KeyCode
            '-------------------------------
            'mini map size
            Case Keys.Oemplus
                If MINI_MAP_NEW_SIZE = MINI_MAP_SIZE Then
                    If MINI_MAP_NEW_SIZE < max_size Then
                        MINI_MAP_NEW_SIZE += 40
                        MiniMapFBO.FBO_Initialize(MINI_MAP_NEW_SIZE)
                    End If
                End If
            Case Keys.OemMinus
                If MINI_MAP_NEW_SIZE = MINI_MAP_SIZE Then
                    If MINI_MAP_NEW_SIZE > 100 Then
                        MINI_MAP_NEW_SIZE -= 40
                        MiniMapFBO.FBO_Initialize(MINI_MAP_NEW_SIZE)
                    End If
                End If
                '-------------------------------
                'wire modes
            Case Keys.D1
                WIRE_MODELS = WIRE_MODELS Xor True

            Case Keys.D2
                WIRE_TERRAIN = WIRE_TERRAIN Xor True

            Case Keys.D3
                WIRE_OUTLAND = WIRE_OUTLAND Xor True

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
                FXAA_enable = FXAA_enable Xor True
                If FXAA_enable Then
                    FXAA_text = "FXAA On"
                Else
                    FXAA_text = "FXAA Off"
                End If

                '-------------------------------
            Case Keys.B
                SHOW_BOUNDING_BOXES = SHOW_BOUNDING_BOXES Xor True

            Case Keys.E
                If frmProgramEditor.CP_parent = Me.Handle Then
                    Return
                End If
                frmProgramEditor.Show()

            Case Keys.G
                If MAP_LOADED Then frmGbufferViewer.Show()
                'inialize the sizes
                frmGbufferViewer.quater_scale.PerformClick()

            Case Keys.I
                SHOW_CHUNK_IDs = SHOW_CHUNK_IDs Xor True

            Case Keys.L
                If Not frmLightSettings.Visible Then
                    m_light_settings.PerformClick()
                Else
                    frmLightSettings.Visible = False
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
                If PICK_MODELS Then
                    modelShader.SetDefine("PICK_MODELS")
                    modelGlassShader.SetDefine("PICK_MODELS")
                Else
                    modelShader.UnsetDefine("PICK_MODELS")
                    modelGlassShader.UnsetDefine("PICK_MODELS")
                End If

            Case Keys.T
                SHOW_TEST_TEXTURES = SHOW_TEST_TEXTURES Xor True
                If SHOW_TEST_TEXTURES Then
                    t_mixerShader.SetDefine("SHOW_TEST_TEXTURES")
                Else
                    t_mixerShader.UnsetDefine("SHOW_TEST_TEXTURES")
                End If
                map_scene.terrain.RebuildVTAtlas()

            Case Keys.Y
                map_scene.terrain.RebuildVTAtlas()

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
                WASD_VECTOR.X = -3.0F
            Case Keys.D
                WASD_VECTOR.X = 3.0F
            Case Keys.W
                WASD_VECTOR.Y = -3.0F
            Case Keys.S
                WASD_VECTOR.Y = 3.0F

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
        Text = Application.ProductName
        m_appVersion.Text = "Version: " & Application.ProductVersion
        PropertyGrid1.SelectedObject = New SettingsPropertyGrid()

        If My.Settings.UpgradeRequired Then
            My.Settings.Upgrade()
            My.Settings.UpgradeRequired = False
            My.Settings.Save()
        End If

        ' Init main gl-control
        Dim glSettings As New OpenTK.WinForms.GLControlSettings With {
            .API = ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible
        }
#If DEBUG Then
        glSettings.Flags = glSettings.Flags Or ContextFlags.Debug
#End If

        Me.glControl_main = New OpenTK.WinForms.GLControl(glSettings)

        '-----------------------------------------------------------------------------------------
        Me.Show()
        Application.DoEvents()
        'size SplitContainer1
        SplitContainer1.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        SplitContainer1.Width = Me.ClientSize.Width
        SplitContainer1.Height = Me.ClientSize.Height - frmMainMenu.Height
        SplitContainer1.Location = New Point(0, frmMainMenu.Height)
        SplitContainer1.Panel2Collapsed = True
        '-----------------------------------------------------------------------------------------
        SplitContainer1.Panel1.Controls.Add(Me.glControl_main)
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
        PropertyGrid1.Parent = SplitContainer1.Panel2
        'Debugger.Break()
        'This timer allows the form to become visible before we initialize everything
        'It is disposed after its done its job.
        startup_delay_timer.Start()
    End Sub

    Private Sub frmMain_MouseDown(sender As Object, e As MouseEventArgs) Handles Me.MouseDown
    End Sub

    Public Sub resize_fbo_main()
        Dim ww, hh As Integer
        MainFBO.get_glControl_size(ww, hh)
        If Not Me.WindowState = FormWindowState.Minimized Then
            MainFBO.FBO_Initialize()
        End If
        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.Invalidate()
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

    End Sub

    Private Sub frmMain_ResizeBegin(sender As Object, e As EventArgs) Handles Me.ResizeBegin

    End Sub

    Private Sub frmMain_SizeChanged(sender As Object, e As EventArgs) Handles Me.SizeChanged
        If Not _STARTED Then Return
        'catch Excetion
        If Me.WindowState = FormWindowState.Minimized Then
            Return
        End If
        SplitContainer1.SplitterDistance = Math.Max(0, ClientSize.Width - SP2_Width - SplitContainer1.SplitterWidth)

        resize_fbo_main()
    End Sub

    Private Sub startup_delay_timer_Tick(sender As Object, e As EventArgs) Handles startup_delay_timer.Tick
        startup_delay_timer.Enabled = False
        post_frmMain_loaded()
    End Sub

#End Region

#Region "FrmMain menu events"

    Private Sub m_light_settings_Click(sender As Object, e As EventArgs) Handles m_light_settings.Click
        'Opens light setting window
        If Not frmLightSettings.Visible Then
            frmLightSettings.Show()
        Else
            frmLightSettings.Hide()
        End If
    End Sub

    Private Sub m_load_map_Click(sender As Object, e As EventArgs) Handles m_load_map.Click

        If Not MAP_LOADED Then
            Me.Text = Application.ProductName & " " & Application.ProductVersion
        End If

        'Runs Map picking code.
        MapMenuScreen.Invalidate()
        glControl_main.MakeCurrent()
        SHOW_MAPS_SCREEN = True
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

    Private Sub m_shut_down_Click(sender As Object, e As EventArgs) Handles m_shut_down.Click
        'Closes the app.
        Me.Close()
    End Sub

    Private Sub m_Log_File_Click(sender As Object, e As EventArgs) Handles m_Log_File.Click
        frmShowText.Show()
        frmShowText.FastColoredTextBox1.Text = File.ReadAllText(Path.Combine(TEMP_STORAGE, "nuTerra_log.txt"))
    End Sub

    Private Sub m_show_properties_Click(sender As Object, e As EventArgs) Handles m_show_properties.Click
        m_show_properties.Checked = Not m_show_properties.Checked
        If m_show_properties.Checked Then
            If panel_2_occupied Then
                frmProgramEditor.Container_panel.Hide()
            Else
                frmProgramEditor.Container_panel.Show()
            End If
            SplitContainer1.Panel2Collapsed = False
            PropertyGrid1.Show()
            SP2_Width = 225
            SplitContainer1.SplitterDistance = (ClientSize.Width - SP2_Width) - SplitContainer1.SplitterWidth
            SetLabelColumnWidth(PropertyGrid1, 120)
            PropertyGrid1.Invalidate()
            PG_width = SP2_Width
        Else
            PropertyGrid1.Hide()
            If panel_2_occupied Then
                SP2_Width = frmProgramEditor.Container_panel.Width
                SplitContainer1.Panel2Collapsed = False
                SplitContainer1.SplitterDistance = (ClientSize.Width - SP2_Width) - SplitContainer1.SplitterWidth
                frmProgramEditor.Container_panel.Show()
            Else
                SplitContainer1.Panel2Collapsed = True

            End If
        End If
        resize_fbo_main()
    End Sub

    Public Shared Sub SetLabelColumnWidth(ByVal grid As PropertyGrid, ByVal width As Integer)
        If grid Is Nothing Then Return
        Dim fi As FieldInfo = grid.[GetType]().GetField("gridView", BindingFlags.Instance Or BindingFlags.NonPublic)
        If fi Is Nothing Then Return
        Dim view As Control = TryCast(fi.GetValue(grid), Control)
        If view Is Nothing Then Return
        Dim mi As MethodInfo = view.[GetType]().GetMethod("MoveSplitterTo", BindingFlags.Instance Or BindingFlags.NonPublic)
        If mi Is Nothing Then Return
        mi.Invoke(view, New Object() {width})
    End Sub

    Private Sub m_appVersion_Click(sender As Object, e As EventArgs) Handles m_appVersion.Click
        System.Diagnostics.Process.Start("https://github.com/mikeoverbay/nuTerra/actions")
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
        If majorVersion < 4 Or (majorVersion = 4 AndAlso minorVersion < 3) Then
            MsgBox("A graphics card and driver with support for OpenGL 4.3 or higher is required.")
            Application.Exit()
            Return
        End If

        '-----------------------------------------------------------------------------------------
        'need a work area on users disc
        TEMP_STORAGE = Path.Combine(Path.GetTempPath, "nuTerra")
        If Not Directory.Exists(TEMP_STORAGE) Then
            Directory.CreateDirectory(TEMP_STORAGE)
        End If
        LogThis("{0}ms Temp storage is located at: {1}", launch_timer.ElapsedMilliseconds.ToString("0000"), TEMP_STORAGE)

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
            m_set_game_path.PerformClick()

            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("This application will be closed because game was not found!")
                Application.Exit()
                Return
            End If
        End If

        LogThis(String.Format("{0}ms Game Path: {1}", launch_timer.ElapsedMilliseconds.ToString("0000"), My.Settings.GamePath))

        ' Create default VAO
        defaultVao = GLVertexArray.Create("defaultVao")

        make_cube() ' used for many draw functions

        CommonPropertiesBuffer = GLBuffer.Create(BufferTarget.UniformBuffer, "CommonProperties")
        CommonPropertiesBuffer.StorageNullData(
            Marshal.SizeOf(CommonProperties),
            BufferStorageFlags.DynamicStorageBit)
        CommonPropertiesBuffer.BindBase(2)

        ShadowMappingFBO.FBO_Initialize()
        LogThis("{0}ms FBO ShadowMapping Created.", launch_timer.ElapsedMilliseconds.ToString("0000"))

        MainFBO.FBO_Initialize()
        LogThis(String.Format("{0}ms FBO Main Created.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        MiniMapFBO.FBO_Initialize(240) '<- default start up size
        LogThis(String.Format("{0}ms FBO Mini Created.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        build_shaders()
        LogThis(String.Format("{0}ms Shaders Built.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        load_assets()
        LogThis(String.Format("{0}ms Assets Loaded.", launch_timer.ElapsedMilliseconds.ToString("0000")))

        resize_fbo_main()
        MapMenuScreen.Invalidate()

        'Everything is setup/loaded to show the main window.
        'Dispose of the no longer used Panel1
        Panel1.Visible = False
        Me.Controls.Remove(Panel1)
        Panel1.Dispose()
        glControl_main.BringToFront()
        GC.Collect() 'Start a clean up of disposed items
        '-----------------------------------------------------------------------------------------
        'Must load and hide frmLighting to access its functions.
        frmLightSettings.StartPosition = FormStartPosition.CenterParent
        frmLightSettings.TopMost = False
        frmLightSettings.SendToBack()
        frmLightSettings.Show()
        frmLightSettings.Visible = False
        frmLightSettings.TopMost = True
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

    End Sub
    '=================================================================================

    ''' <summary>
    ''' Loads all assets nuTerra uses.
    ''' </summary>
    ''' 

    Private Sub load_assets()
        '---------------------------------------------------------
        'set up regex strings for the glsl editor.
        get_GLSL_filter_strings()
        '---------------------------------------------------------

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
        CHECKER_BOARD = load_png_image_from_file(Path.Combine(sp, "resources\CheckerPatternPaper.png"), False, False)
        '---------------------------------------------------------
        'cursor texture
        '---------------------------------------------------------
        'MiniMap position/direction img
        DIRECTION_TEXTURE_ID = load_png_image_from_file(Path.Combine(sp, "resources\direction.png"), True, False)
        '---------------------------------------------------------
        'load progress bar gradient image from the GUI package.
        PROGRESS_BAR_IMAGE_ID = load_png_image_from_file(Path.Combine(sp, "resources\progress_bar.png"), False, True)

        '---------------------------------------------------------
        ' build Ascii characters texture.
        ASCII_ID = build_ascii_characters()

        '===========================================================================================
        ' needed for terrain atlas textures
        make_dummy_4_layer_atlas()
        '===========================================================================================

#If False Then
        'This can be used to debug textureing
        checkerTest =
                load_image_from_file(Il.IL_PNG,
                sp + "\resources\checkerboard.png", True, False)
#End If
        '---------------------------------------------------------

        'This gets the first GL texture, vertex array and vertex buffer IDs after the static IDs
        'ALL STATIC ITEMS NEED TO BE LOADED BEFORE THIS IS CALLED!!!
        get_start_ID_for_Components_Deletion()

    End Sub

    Private Sub get_GLSL_filter_strings()
        Dim ts = IO.File.ReadAllText(Application.StartupPath + "\data\glsl_filtered_strings.txt")
        Dim f_list = ts.Split(ControlChars.CrLf.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
        set_GLSL_keywords(f_list)
    End Sub

    Private Sub set_GLSL_keywords(ByRef f_list() As String)
        GLSL_KEYWORDS = "\b("
        For Each s In f_list
            If InStr(s, "#") = 0 Then
                If s.Length > 2 Then
                    GLSL_KEYWORDS += s + "|"
                End If
            End If
        Next
        'this is needed because of the last | in the load loop!
        GLSL_KEYWORDS += "float)\b"
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
            If WASD_SPEED > 0.025F Then
                WASD_SPEED = 0F
                Dim MAX = -200.0F
                If MAX < map_scene.camera.VIEW_RADIUS Then
                    MAX = map_scene.camera.VIEW_RADIUS
                End If
                Dim ms As Single = 0.2F * MAX ' distance away changes speed.. THIS WORKS WELL!
                Dim t = WASD_VECTOR.X * ms * 0.003

                If WASD_VECTOR.X <> 0 Then
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (-Sin(map_scene.camera.CAM_X_ANGLE)))
                End If

                t = WASD_VECTOR.Y * ms * 0.003F

                If WASD_VECTOR.Y <> 0 Then
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Sin(map_scene.camera.CAM_X_ANGLE)))
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
                    'LIGHT_POS(1) = Sin(LIGHT_ORBIT_ANGLE_Z) * LIGHT_RADIUS
                    LIGHT_POS(2) = Sin(LIGHT_ORBIT_ANGLE_Z) * LIGHT_RADIUS
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

#End Region


#Region "glControl_main events"

    Private Sub glControl_main_MouseDown(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseDown


        If BLOCK_MOUSE Then Return

        If MINI_MOUSE_CAPTURED Then
            'User clicked on the mini so lets move to that locations in world space
            map_scene.camera.LOOK_AT_X = MINI_WORLD_MOUSE_POSITION.X
            map_scene.camera.LOOK_AT_Z = MINI_WORLD_MOUSE_POSITION.Y
        End If

        If SHOW_MAPS_SCREEN Then
            If e.Button = Forms.MouseButtons.Left Then
                If MapMenuScreen.SelectedMap Is Nothing Then
                    If MAP_LOADED Then
                        SHOW_MAPS_SCREEN = False
                        Return
                    End If
                Else
                    BLOCK_MOUSE = True
                    FINISH_MAPS = True
                    MAP_LOADED = False
                    Me.Text = MapMenuScreen.SelectedMap.realname
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

    Private Sub glControl_main_MouseWheel(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseWheel
        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.Scroll(e.Delta)
        End If
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
        Dim M_Speed As Single = My.Settings.speed
        If map_scene IsNot Nothing Then
            Dim ms As Single = 0.2F * map_scene.camera.VIEW_RADIUS ' distance away changes speed.. THIS WORKS WELL!
            If M_DOWN Then
                If e.X > (MOUSE.X + dead) Then
                    If e.X - MOUSE.X > 100 Then t = (1.0F * M_Speed)
                Else : t = CSng(Sin((e.X - MOUSE.X) / 100)) * M_Speed
                    If Not Z_MOVE Then
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_X -= ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_Z -= ((t * ms) * (-Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            map_scene.camera.CAM_X_ANGLE -= t
                        End If
                        If map_scene.camera.CAM_X_ANGLE > (2 * PI) Then map_scene.camera.CAM_X_ANGLE -= (2 * PI)
                        MOUSE.X = e.X
                    End If
                End If
                If e.X < (MOUSE.X - dead) Then
                    If MOUSE.X - e.X > 100 Then t = (M_Speed)
                Else : t = CSng(Sin((MOUSE.X - e.X) / 100)) * M_Speed
                    If Not Z_MOVE Then
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_X += ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_Z += ((t * ms) * (-Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            map_scene.camera.CAM_X_ANGLE += t
                        End If
                        If map_scene.camera.CAM_X_ANGLE < 0 Then map_scene.camera.CAM_X_ANGLE += (2 * PI)
                        MOUSE.X = e.X
                    End If
                End If
                ' ------- Y moves ----------------------------------
                If e.Y > (MOUSE.Y + dead) Then
                    If e.Y - MOUSE.Y > 100 Then t = (M_Speed)
                Else : t = CSng(Sin((e.Y - MOUSE.Y) / 100)) * M_Speed
                    If Z_MOVE Then
                        map_scene.camera.LOOK_AT_Y -= (t * ms)
                    Else
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_Z -= ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_X -= ((t * ms) * (Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            If map_scene.camera.CAM_Y_ANGLE - t < -PI / 2.0 Then
                                map_scene.camera.CAM_Y_ANGLE = -PI / 2.0 + 0.001
                            Else
                                map_scene.camera.CAM_Y_ANGLE -= t
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
                        map_scene.camera.LOOK_AT_Y += (t * ms)
                    Else
                        If MOVE_MOD Then ' check for modifying flag
                            map_scene.camera.LOOK_AT_Z += ((t * ms) * (Cos(map_scene.camera.CAM_X_ANGLE)))
                            map_scene.camera.LOOK_AT_X += ((t * ms) * (Sin(map_scene.camera.CAM_X_ANGLE)))
                        Else
                            If map_scene.camera.CAM_Y_ANGLE + t > 1.3 Then
                                map_scene.camera.CAM_Y_ANGLE = 1.3
                            Else
                                map_scene.camera.CAM_Y_ANGLE += t
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
                ' zoom is factored in to Cam radius
                Dim vrad = map_scene.camera.VIEW_RADIUS
                If e.Y < (MOUSE.Y - dead) Then
                    If e.Y - MOUSE.Y > 100 Then t = (10)
                Else : t = CSng(Sin((e.Y - MOUSE.Y) / 100)) * 12 * My.Settings.speed
                    If vrad + (t * (vrad * 0.2)) < map_scene.camera.MAX_ZOOM_OUT Then
                        vrad = map_scene.camera.MAX_ZOOM_OUT
                    Else
                        vrad += (t * (vrad * 0.2))
                    End If
                    MOUSE.Y = e.Y
                End If
                If e.Y > (MOUSE.Y + dead) Then
                    If MOUSE.Y - e.Y > 100 Then t = (10)
                Else : t = CSng(Sin((MOUSE.Y - e.Y) / 100)) * 12 * My.Settings.speed
                    vrad -= (t * (vrad * 0.2))    ' zoom is factored in to Cam radius
                    If vrad > -0.01 Then vrad = -0.01
                End If
                If vrad > -0.1 Then vrad = -0.1
                map_scene.camera.VIEW_RADIUS = vrad
                MOUSE.Y = e.Y
                Return
            End If
        End If

        MOUSE.X = e.X
        MOUSE.Y = e.Y

    End Sub

    Private Sub glControl_main_MouseDoubleClick(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseDoubleClick
        If PICK_MODELS AndAlso PICKED_MODEL_INDEX > 0 Then
            frmModelViewer.SplitContainer1.Panel1.Controls.Clear()

            If frmModelViewer.Model_Loaded Then
                frmModelViewer.Model_Loaded = False
                frmModelViewer.modelIndirectBuffer = Nothing
            End If

            'get the name we need to load
            Dim ar = PICKED_STRING.Split(":")
            Dim visual_path = ar(1).Trim.Replace(".primitives", ".visual_processed")
            'find the package and get the entry from that package as a zipEntry
            Dim visual = ResMgr.openXML(visual_path)
            If visual IsNot Nothing Then
                'This has to be visible for the text highlighting to work.
                If Not frmModelViewer.Visible Then
                    frmModelViewer.Visible = True
                End If

                'Put the visual string in the FCB on the frmModelViwer
                frmModelViewer.FastColoredTextBox1.Text = ""
                Application.DoEvents()
                'make sure the control sets the highlights
                frmModelViewer.FastColoredTextBox1.Text = visual.InnerXml.Replace("><", ">" + vbCrLf + ">")
                Application.DoEvents()
                frmModelViewer.MODEL_NAME_MODELVIEWER = visual_path.Replace("\", "/")
            Else
                LogThis("visual not found: {0}", visual_path)
            End If

            Dim mdlInstance As ModelInstance
            GL.GetNamedBufferSubData(map_scene.static_models.matrices.buffer_id,
                                     New IntPtr((PICKED_MODEL_INDEX - 1) * Marshal.SizeOf(mdlInstance)),
                                     Marshal.SizeOf(mdlInstance),
                                     mdlInstance)

            Dim mdlLod As ModelLoD
            GL.GetNamedBufferSubData(map_scene.static_models.lods.buffer_id,
                                     New IntPtr(mdlInstance.lod_offset * Marshal.SizeOf(mdlLod)),
                                     Marshal.SizeOf(mdlLod),
                                     mdlLod)

            frmModelViewer.modelDrawCount = mdlLod.draw_count
            Dim indirectCommands(mdlLod.draw_count - 1) As DrawElementsIndirectCommand
            For i = 0 To mdlLod.draw_count - 1
                Dim draw As CandidateDraw
                GL.GetNamedBufferSubData(map_scene.static_models.drawCandidates.buffer_id,
                                         New IntPtr((mdlLod.draw_offset + i) * Marshal.SizeOf(draw)),
                                         Marshal.SizeOf(draw),
                                         draw)

                With indirectCommands(i)
                    .count = draw.count
                    .instanceCount = 1
                    .firstIndex = draw.firstIndex
                    .baseVertex = draw.baseVertex
                    .baseInstance = draw.baseInstance
                End With

                ' Add checkbox
                Dim cb As New CheckBox
                cb.AutoSize = True
                cb.TextAlign = ContentAlignment.MiddleCenter
                cb.Checked = True
                cb.Text = String.Format("Primitive {0}, Poly Cnt: {1}", i, draw.count)
                cb.ForeColor = Color.Wheat
                cb.BackColor = Color.Transparent
                Dim k = frmModelViewer.SplitContainer1.Panel1.Controls.Count
                Dim h = cb.Height
                cb.Location = New Point(3, (k * (h - 3)) + 5)
                AddHandler cb.CheckedChanged, AddressOf frmModelViewer.update_model_indirect_buffer
                frmModelViewer.SplitContainer1.Panel1.Controls.Add(cb)
            Next

            frmModelViewer.modelIndirectBuffer = GLBuffer.Create(BufferTarget.DrawIndirectBuffer, "modelIndirectBuffer")
            frmModelViewer.modelIndirectBuffer.Storage(
                indirectCommands.Length * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                indirectCommands,
                BufferStorageFlags.DynamicStorageBit Or BufferStorageFlags.ClientStorageBit)
            frmModelViewer.Model_Loaded = True

        End If
    End Sub
#End Region

    Private Sub map_loader_Tick(sender As Object, e As EventArgs) Handles map_loader.Tick
        map_loader.Enabled = False
        load_map(MAP_NAME_NO_PATH)
    End Sub

    Private Sub m_screen_capture_Click(sender As Object, e As EventArgs) Handles m_screen_capture.Click
        frmScreenCap.ShowDialog()
    End Sub

    Private Sub SplitContainer1_SplitterMoved(sender As Object, e As SplitterEventArgs) Handles SplitContainer1.SplitterMoved
        If Not _STARTED Then Return
        If Not sp_moved Then Return
        sp_moved = False
        SP2_Width = SplitContainer1.Panel2.Width
        If panel_2_occupied Then
            FE_width = SP2_Width
        Else
            PG_width = SP2_Width
        End If
        MainFBO.oldWidth = -1
        resize_fbo_main()
    End Sub

    Dim sp_moved As Boolean
    Private Sub SplitContainer1_SplitterMoving(sender As Object, e As SplitterCancelEventArgs) Handles SplitContainer1.SplitterMoving
        If Not _STARTED Then Return


        sp_moved = True

    End Sub

End Class

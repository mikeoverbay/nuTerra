﻿

Imports System.Math
Imports System
Imports System.IO
Imports System.Globalization
Imports System.Threading
Imports System.Windows
Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
'Imports Config = OpenTK.Configuration
'Imports Utilities = OpenTK.Platform.Utilities

Public Class frmMain
    Private refresh_thread As New Thread(AddressOf updater)
    Private gametimer As New System.Diagnostics.Stopwatch
#Region "Form Events"

    Private Sub frmMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        _STARTED = False
        SYNCMUTEX.WaitOne()
        refresh_thread.Abort()
        'Need to add code to close down opengl and delete the resources.
    End Sub

    Private Sub frmMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Select Case True
            Case e.KeyCode = Keys.E
                frmEditFrag.Show()
            Case e.KeyCode = Keys.L
                m_light_settings.PerformClick()
            Case e.KeyCode = Keys.R
                make_randum_locations()


            Case e.KeyCode = Keys.ControlKey
                Z_MOVE = True
            Case e.KeyCode = Keys.ShiftKey
                MOVE_MOD = True
            Case e.KeyCode = Keys.Space
                If PAUSE_ORBIT Then
                    PAUSE_ORBIT = False
                Else
                    PAUSE_ORBIT = True
                End If
        End Select
    End Sub

    Private Sub frmMain_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        Z_MOVE = False
        MOVE_MOD = False
    End Sub

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles Me.Load
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
        'This timer allows the form to become visible before we initialize everything
        'It is disposed after its done its job.
        startup_delay_timer.Start()
    End Sub

    Private Sub post_frmMain_loaded()
        '-----------------------------------------------------------------------------------------
        'need a work area on users disc
        TEMP_STORAGE = Path.GetTempPath + "nuTerra\"
        If Not Directory.Exists(TEMP_STORAGE) Then
            Directory.CreateDirectory(TEMP_STORAGE)
        End If
        '-----------------------------------------------------------------------------------------
        'Check if the game path is set
        If Not Directory.Exists(My.Settings.GamePath + "\res") Then
            MsgBox("Path to game is not set!" + vbCrLf + _
                    "Lets set it now.", MsgBoxStyle.OkOnly, "Game Path not set")
            m_set_game_path.PerformClick()
        End If
        GAME_PATH = My.Settings.GamePath + "\res\packages\"
        '-----------------------------------------------------------------------------------------
        glControl_main.MakeCurrent()
        '-----------------------------------------------------------------------------------------
#If DEBUG Then
        'SetupDebugOutputCallback()
#End If
        '-----------------------------------------------------------------------------------------
        FBOm.FBO_Initialize()
        '-----------------------------------------------------------------------------------------
        Il.ilInit()
        Ilu.iluInit()
        '-----------------------------------------------------------------------------------------
        build_shaders()
        set_uniform_variables()
        '-----------------------------------------------------------------------------------------
        load_assets()
        '-----------------------------------------------------------------------------------------
        'Loads the list of destroyed object types.
        load_destructibles()
        '-----------------------------------------------------------------------------------------
        'Set camara start up position. This is mostly for testing.
        VIEW_RADIUS = -1000.0
        CAM_X_ANGLE = PI / 4
        CAM_Y_ANGLE = -PI / 4
        '-----------------------------------------------------------------------------------------
        set_light_pos() ' Set initial light position and get radius and angle.
        '-----------------------------------------------------------------------------------------
        'Everything is setup/loaded to show the main window.
        'Dispose of the no longer used Panel1
        Panel1.Visible = False
        Me.Controls.Remove(Panel1)
        Panel1.Dispose()
        glControl_main.BringToFront()
        GC.Collect() 'Start a clean up of disposed items
        '-----------------------------------------------------------------------------------------
        'Loads the textures for the map selection routines
        '!!!!! This is disabled to speed up testing for now!
#If 0 Then
        make_map_pick_buttons()
#End If
        '-----------------------------------------------------------------------------------------
        'Make a texture for rendering text on map pic textures
        DrawMapPickText.TextRenderer(120, 72)
        '-----------------------------------------------------------------------------------------
        'This gets the first texture ID after the static IDs
        'ALL STATIC TEXTURES NEED TO BE LOADED BEFORE THIS IS CALLED!!!
        get_start_ID_for_texture_Deletion()
        '-----------------------------------------------------------------------------------------
        'open up our huge virual memory file for storage.
        '(map size * map size)*((64 * 64) * 6 vertex per quad)
        triangle_holder.open((20 * 20) * (4096 * 6))
        '-----------------------------------------------------------------------------------------
        'Must load and hide frmLighting to access its functions.
        frmLighting.TopMost = False
        frmLighting.SendToBack()
        frmLighting.Show()
        frmLighting.Visible = False
        frmLighting.TopMost = True
        '-----------------------------------------------------------------------------------------
        'we are ready for user input so lets enable the menu
        MainMenuStrip.Enabled = True
        '-----------------------------------------------------------------------------------------
        _STARTED = True ' I'm ready for update loops!
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        'This is temporary to speed up debugging
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        launch_update_thread()
        '-----------------------------------------------------------------------------------------
        'load_map("19_monastery.pkg")
        'load_map("08_ruinberg.pkg")
        'load_map("14_siegfried_line.pkg")
        'load_map("29_el_hallouf.pkg")
        'load_map("31_airfield.pkg")
        load_map("112_eiffel_tower_ctf.pkg")

    End Sub

    Private Sub frmMain_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        If _STARTED Then
            If Not Me.WindowState = FormWindowState.Minimized Then
                FBOm.FBO_Initialize()
            End If
        End If
    End Sub

    Private Sub frmMain_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        If _STARTED Then
            FBOm.FBO_Initialize()
        End If
        Dim s = Me.Size

    End Sub

    Private Sub startup_delay_timer_Tick(sender As Object, e As EventArgs) Handles startup_delay_timer.Tick
        startup_delay_timer.Enabled = False
        post_frmMain_loaded()
    End Sub

#End Region

#Region "FrmMain menu events"

    Private Sub m_light_settings_Click(sender As Object, e As EventArgs) Handles m_light_settings.Click
        'Opens light setting window
        frmLighting.Show()
    End Sub

    Private Sub m_load_map_Click(sender As Object, e As EventArgs) Handles m_load_map.Click
        'we are disabling this to speed up debugging of space.bin
#If 1 Then
        Return
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
            If Not Directory.Exists(My.Settings.GamePath + "\res") Then
                MsgBox("Wrong Folder Path!" + vbCrLf + _
                       "You need to point at the World_of_Tanks folder!", _
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

#End Region
    ''' <summary>
    ''' Loads all assets nuTerra uses.
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub load_assets()
        'setup text renderer
        Dim sp = Application.StartupPath

        '---------------------------------------------------------
        'load the xml list of all item locations
        load_lookup_xml()
        '---------------------------------------------------------
        nuTERRA_BG_IMAGE =
            load_image_from_file(Il.IL_PNG,
            sp + "\resources\earth.png", False, True)
        '---------------------------------------------------------
        '---------------------------------------------------------


        'test junk. ----------------------------------------------
        make_randum_locations() ' randum box locals
        'load a test model
#If 1 Then
        get_X_model(sp + "\resources\moon.x", MOON)
        'color_id = load_image_from_file(Il.IL_PNG, sp + "\resources\phobosmirror.png", True, False)
        'normal_id = load_image_from_file(Il.IL_PNG, sp + "\resources\phobosmirror_NORM.png", True, False)
        'gmm_id = load_image_from_file(Il.IL_PNG, sp + "\resources\phobosmirror_NORM.png", True, False)
        N_MAP_TYPE = 0
#Else

        get_X_model(sp + "\resources\cube.x")
        color_id = load_image_from_file(Il.IL_DDS, sp + "\resources\PBS_Rock_05_AM.dds", True, False)
        normal_id = load_image_from_file(Il.IL_DDS, sp + "\resources\PBS_Rock_05_NM.dds", True, False)
        gmm_id = load_image_from_file(Il.IL_DDS, sp + "\resources\PBS_Rock_05_GMM.dds", True, False)
        N_MAP_TYPE = 1
#End If
    End Sub

#Region "Screen position and update"

    ''' <summary>
    ''' Sets up and starts the main render thread
    ''' </summary>
    ''' <remarks>
    ''' Also starts the gametimer stopwatch and
    ''' disposed the startup_delay timer.
    ''' </remarks>
    Private Sub launch_update_thread()
        gametimer.Start()
        refresh_thread.Priority = ThreadPriority.Highest
        refresh_thread.IsBackground = True
        refresh_thread.Name = "refresh_thread"
        refresh_thread.Start()
        'SHOW_MAPS = True
        'We wont use this timer again so lets remove it from memory
        startup_delay_timer.Dispose()
    End Sub

    ''' <summary>
    ''' The main render draw loop thread.
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub updater()

        While _STARTED
            If Not PAUSE_ORBIT Then
                LIGHT_ORBIT_ANGLE += LIGHT_SPEED
                If LIGHT_ORBIT_ANGLE > PI * 2 Then LIGHT_ORBIT_ANGLE -= PI * 2
                LIGHT_POS(0) = Cos(LIGHT_ORBIT_ANGLE) * LIGHT_RADIUS
                LIGHT_POS(1) = 300.0 'Cos(LIGHT_ORBIT_ANGLE) * LIGHT_RADIUS
                LIGHT_POS(2) = Sin(LIGHT_ORBIT_ANGLE) * LIGHT_RADIUS
            End If
            If gametimer.ElapsedMilliseconds > 1000 Then
                gametimer.Restart()
                FPS_TIME = FPS_COUNTER
                FPS_COUNTER = 0
            End If
            update_screen()
            Thread.Sleep(HOG_TIME) ' hog all the time :)
        End While
    End Sub

    ''' <summary>
    ''' cross thread Delegate
    ''' </summary>
    ''' <remarks></remarks>
    Private Delegate Sub update_screen_delegate()

    ''' <summary>
    ''' Used to call functions outside update thread.
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub update_screen()
        Try
            If Me.InvokeRequired And _STARTED Then
                Me.Invoke(New update_screen_delegate(AddressOf update_screen))
            Else
                check_postion_for_update()
                SYNCMUTEX.WaitOne()
                draw_scene()
                SYNCMUTEX.ReleaseMutex()
            End If
        Catch ex As Exception

        End Try
    End Sub
#End Region

    ''' <summary>
    ''' This is called by the update thread to see
    ''' if anything about the camera view has changed.
    ''' </summary>
    ''' <remarks></remarks>
    ''' 
    Private Sub check_postion_for_update()
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
            U_CAM_Y_ANGLE = CAM_Y_ANGLE
        End If
        If VIEW_RADIUS <> U_VIEW_RADIUS Then
            U_VIEW_RADIUS = VIEW_RADIUS
        End If
    End Sub

#Region "glControl_main events"

    Private Sub glControl_main_MouseDown(sender As Object, e As MouseEventArgs) Handles glControl_main.MouseDown
        If BLOCK_MOUSE Then Return

        If SHOW_MAPS_SCREEN Then
            If e.Button = Forms.MouseButtons.Left Then

                If SELECTED_MAP_HIT = 0 And MAP_LOADED Then
                    SHOW_MAPS_SCREEN = False
                    Application.DoEvents()
                    Return
                Else
                    Dim dx = SELECTED_MAP_HIT - 1 'deal with posible false hit
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
        Dim max_zoom_out As Single = -3500.0F 'must be negitive
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
                        CAM_Y_ANGLE -= t
                    End If
                    If CAM_Y_ANGLE < -PI / 2.0 Then CAM_Y_ANGLE = -PI / 2.0 + 0.001
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
                        CAM_Y_ANGLE += t
                    End If
                    If CAM_Y_ANGLE > 1.3 Then CAM_Y_ANGLE = 1.3
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

End Class

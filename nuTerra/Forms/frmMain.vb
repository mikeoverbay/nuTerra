

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
Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Public Class frmMain
    Private refresh_thread As New Thread(AddressOf updater)
    Private u_timer As New Stopwatch
#Region "From Events"

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
        'So numbers work in any nation I'm running in.
        Dim nonInvariantCulture As CultureInfo = New CultureInfo("en-US")
        nonInvariantCulture.NumberFormat.NumberDecimalSeparator = "."
        Thread.CurrentThread.CurrentCulture = nonInvariantCulture
        '-----------------------------------------------------------------------------------------
        Me.KeyPreview = True    'So I catch keyboard before despatching it
        '-----------------------------------------------------------------------------------------
        glControl_main.MakeCurrent()
        '-----------------------------------------------------------------------------------------

        Me.Show()
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
        'set camara start up position
        VIEW_RADIUS = -1000.0
        CAM_X_ANGLE = PI / 4
        CAM_Y_ANGLE = -PI / 4
        '-----------------------------------------------------------------------------------------
        set_light_pos() ' Set initial light position and get radius and angle.
        '-----------------------------------------------------------------------------------------
        _STARTED = True ' I'm ready to run!
        launch_update_thread()
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
    End Sub

#End Region

    Private Sub load_assets()
        'setup text renderer
        make_randum_locations() ' randum box locals
        Dim sp = Application.StartupPath

        '---------------------------------------------------------
        'load a test model
#If 0 Then
        get_X_model(sp + "\resources\moon.x")
        color_id = load_image_from_file(sp + "\resources\phobosmirror.png")
        normal_id = load_image_from_file(sp + "\resources\phobosmirror_NORM.png")
        gmm_id = load_image_from_file(sp + "\resources\phobosmirror_NORM.png")
#Else

        get_X_model(sp + "\resources\cube.x")
        color_id = load_image_from_file(sp + "\resources\PBS_Rock_05_AM.dds")
        normal_id = load_image_from_file(sp + "\resources\PBS_Rock_05_NM.dds")
        gmm_id = load_image_from_file(sp + "\resources\PBS_Rock_05_GMM.dds")
#End If
        '---------------------------------------------------------

    End Sub

#Region "Screen position and update"

    Private Sub launch_update_thread()
        u_timer.start()
        refresh_thread.Priority = ThreadPriority.Highest
        refresh_thread.IsBackground = True
        refresh_thread.Name = "refresh_thread"
        refresh_thread.Start()
    End Sub


    Private Sub updater()

        While _STARTED
            If u_timer.ElapsedMilliseconds > 60 Then
                If Not PAUSE_ORBIT Then
                    LIGHT_ORBIT_ANGLE += 0.03
                    If LIGHT_ORBIT_ANGLE > PI * 2 Then LIGHT_ORBIT_ANGLE -= PI * 2
                    LIGHT_POS(0) = Cos(LIGHT_ORBIT_ANGLE) * LIGHT_RADIUS
                    LIGHT_POS(2) = Sin(LIGHT_ORBIT_ANGLE) * LIGHT_RADIUS
                End If
                u_timer.Restart()
            End If
            update_screen()
            Thread.Sleep(3)
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


    Private Sub m_light_settings_Click(sender As Object, e As EventArgs) Handles m_light_settings.Click
        frmLighting.Show()
    End Sub
End Class

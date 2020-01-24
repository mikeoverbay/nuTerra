﻿

Imports System.Math
Imports System
Imports System.Globalization
Imports System.Threading

Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Public Class frmMain
    Private refresh_thread As New Thread(AddressOf updater)
#Region "From Events"

    Private Sub frmMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Select Case True
            Case e.KeyCode = Keys.E
                frmEditFrag.Show()
        End Select
    End Sub

    Private Sub frmMain_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp

    End Sub

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles Me.Load
        '-----------------------------------------------------------------------------------------
        'so numbers work in any nation this app is running.
        Dim nonInvariantCulture As CultureInfo = New CultureInfo("en-US")
        nonInvariantCulture.NumberFormat.NumberDecimalSeparator = "."
        Thread.CurrentThread.CurrentCulture = nonInvariantCulture
        '-----------------------------------------------------------------------------------------
        Me.KeyPreview = True    'so I catch keyboard before despatching it
        '-----------------------------------------------------------------------------------------

        'Dim winInfo = glControl_main.WindowInfo
        'glControl_main.Context.MakeCurrent(winInfo)
        Me.Show()
        FBOm.FBO_Initialize()

        '-----------------------------------------------------------------------------------------
        build_shaders()
        '-----------------------------------------------------------------------------------------
        _STARTED = True ' we are ready to run
        Delay_timer.Start() ' starts screen updates
    End Sub

    Private Sub frmMain_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        If _STARTED Then
            FBOm.FBO_Initialize()
        End If
    End Sub

    Private Sub frmMain_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        If _STARTED Then
            FBOm.FBO_Initialize()
        End If
    End Sub

#End Region

    Private Sub launch_update_thread()
        refresh_thread.Priority = ThreadPriority.Highest
        refresh_thread.IsBackground = True
        refresh_thread.Name = "refresh_thread"
        refresh_thread.Start()
    End Sub


    Private Sub updater()

        While _STARTED
            update_screen()
            Thread.Sleep(1)
        End While
    End Sub
    Private Delegate Sub update_screen_delegate()
    Private lockdrawing As New Object
    Public Sub update_screen()
        Try
            If Me.InvokeRequired And _STARTED Then
                SyncLock lockdrawing
                    Me.Invoke(New update_screen_delegate(AddressOf update_screen))
                End SyncLock
            Else
                draw_scene()
            End If
        Catch ex As Exception

        End Try
    End Sub

    Private Sub Delay_timer_Tick(sender As Object, e As EventArgs) Handles Delay_timer.Tick

        launch_update_thread()
        Delay_timer.Enabled = False
        Delay_timer.Dispose()
    End Sub
End Class

Imports System.Globalization
Imports System.Threading
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Windowing.Desktop

Class MainWindow
    Public Shared MaxOpenGlVersion As Version

    Private Function CheckOpenGLExtensions() As Boolean
        Dim extensions As New List(Of String)
        Dim numExt As Integer = GL.GetInteger(GetPName.NumExtensions)
        For i = 0 To numExt - 1
            extensions.Add(GL.GetString(StringNameIndexed.Extensions, i))
        Next

        Dim required_extensions() As String = {
            "GL_ARB_vertex_type_10f_11f_11f_rev",
            "GL_ARB_shading_language_include",
            "GL_ARB_indirect_parameters",
            "GL_ARB_multi_draw_indirect", 'core since 4.3
            "GL_ARB_direct_state_access", 'core since 4.5
            "GL_ARB_clip_control" 'core since 4.5
        }

        For Each ext_name In required_extensions
            If Not extensions.Contains(ext_name) Then
                MessageBox.Show(String.Format("{0} extension is required.", ext_name), "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return False
            End If
        Next

        Return True
    End Function

    Private Function GetMaxOpenGLVersionAndCheckExtensions() As Version
        Dim nws = NativeWindowSettings.Default
        nws.StartFocused = False
        nws.StartVisible = False

        For minor = 6 To 3 Step -1
            nws.APIVersion = New Version(4, minor)
            Try
                Dim glfwWindow = New NativeWindow(nws)
                If Not CheckOpenGLExtensions() Then
                    Return Nothing
                End If
                Return nws.APIVersion
            Catch ex As Exception
                ' Nothing to do
            End Try
        Next

        MessageBox.Show("A graphics card and driver with support for OpenGL 4.3 or higher is required.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
        Return Nothing
    End Function

    Public Sub New()
        MaxOpenGlVersion = GetMaxOpenGLVersionAndCheckExtensions()
        If MaxOpenGlVersion Is Nothing Then
            Close()
            Return
        End If

        ' So numbers work in any nation I'm running in.
        Dim nonInvariantCulture As New CultureInfo("en-US")
        Thread.CurrentThread.CurrentCulture = nonInvariantCulture

        ' TODO: move to Load Settings
        Dim GamePath = "E:\Games\World_of_Tanks_RU"
        If Not ResMgr.Init(GamePath) Then
            Close()
            Return
        End If

        InitializeComponent()
        ContentArea.Content = New MapGrid
    End Sub

    Private Sub Window_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.Key
            '-------------------------------
            'mini map size
            Case Key.OemPlus
            Case Key.OemMinus
            '-------------------------------
            'wire modes
            Case Key.D1
            Case Key.D2
            Case Key.F1
            '-------------------------------
            'grid display
            Case Key.F5
            Case Key.F6
            Case Key.F7
            Case Key.F8
            Case Key.F9
            Case Key.B
            Case Key.G
            Case Key.E
            Case Key.I
            Case Key.L
            Case Key.M
            Case Key.N
            Case Key.O
                If TypeOf (ContentArea.Content) IsNot MapGrid Then
                    ContentArea.Content = New MapGrid
                End If
            Case Key.P
            Case Key.T
            Case Key.V
            '-------------------------------
            'Special Keys
            Case Key.LeftCtrl
            Case Key.LeftShift
            Case Key.Space
            '-------------------------------
            'Navigation
            Case Key.A
            Case Key.D
            Case Key.W
            Case Key.S
        End Select
    End Sub

    Private Sub Window_KeyUp(sender As Object, e As KeyEventArgs)
        Select Case e.Key
            Case Key.A
            Case Key.D
            Case Key.W
            Case Key.S
        End Select
    End Sub

End Class

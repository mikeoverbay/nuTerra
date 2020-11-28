Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Windowing.Common
Imports OpenTK.Wpf

Class MapViewport

    Public Sub New(map_info As MapGrid.MapInfo)
        InitializeComponent()

#If DEBUG Then
        Dim flags = ContextFlags.Debug
#Else
        Dim flags = ContextFlags.Default
#End If

        Description.Text = String.Join("." & vbCrLf, map_info.MapDescription.Split(". "))
        Application.Current.MainWindow.Title = String.Format("nuTerra : {0}", map_info.MapName)

        Dim mainSettings = New GLWpfControlSettings With {
            .GraphicsContextFlags = flags,
            .GraphicsProfile = ContextProfile.Core,
            .MajorVersion = MainWindow.MaxOpenGlVersion.Major,
            .MinorVersion = MainWindow.MaxOpenGlVersion.Minor,
            .UseDeviceDpi = False,
            .RenderContinuously = True
        }
        OpenTkControl.Start(mainSettings)

        ' Set depth to [0..1] range instead of [-1..1]
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne)

        ' Enable depth clamping
        GL.Enable(EnableCap.DepthClamp)
    End Sub

    Private Sub OpenTkControl_OnRender(delta As TimeSpan)
        GL.ClearColor(OpenTK.Mathematics.Color4.Yellow)
        GL.Clear(ClearBufferMask.ColorBufferBit)
    End Sub

    Private Sub OpenTkControl_MouseDown(sender As Object, e As Windows.Input.MouseButtonEventArgs) Handles OpenTkControl.MouseDown
        If e.LeftButton Then
            If e.ClickCount = 2 Then
            Else
            End If
        End If
    End Sub

    Private Sub OpenTkControl_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles OpenTkControl.SizeChanged
        If ((e.WidthChanged Or e.HeightChanged) And (e.NewSize.Width > 0 And e.NewSize.Height > 0)) Then
            'TODO: update fbo
        End If
    End Sub
End Class

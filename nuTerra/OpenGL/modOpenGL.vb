

Imports System.Math
Imports System
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities


Module modOpenGL
    Public FieldOfView As Single = 60.0F
    Private projectionMatrix As Matrix4
    Private modelViewMatrix As Matrix4

    Public Main_Context As Integer

    Public Sub resize_glControl_main()
        Dim c = frmMain.glControl_main
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
    End Sub
    Public Sub Main_prospectiveView()
        resize_glControl_main()
        projectionMatrix = Matrix4.CreatePerspectiveFieldOfView( _
                                   CSng(Math.PI) * (FieldOfView / 180.0F), _
                                   frmMain.glControl_main.ClientSize.Width / CSng(frmMain.glControl_main.ClientSize.Height), _
                                   1.0F, 500.0F)

        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadMatrix(projectionMatrix)
    End Sub
    Public Sub Main_ortho()
        resize_glControl_main()
        projectionMatrix = Matrix4.Identity
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadIdentity()
        GL.Ortho(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, 300.0F, -300.0F)
        'GL.LoadIdentity()

    End Sub
End Module

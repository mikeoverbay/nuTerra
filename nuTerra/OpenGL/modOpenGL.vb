

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

    Public Main_Context As Integer

    Public Sub resize_glControl_main()
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
    End Sub

    Public Sub resize_glControl_utility()
        Dim position As New Point(frmMain.ClientSize.Width - 320, frmMain.ClientSize.Height - 320)
        frmMain.glControl_utility.Location = position
        frmMain.glControl_utility.Width = 320
        frmMain.glControl_utility.Height = 320
        GL.Viewport(0, 0, frmMain.glControl_utility.ClientSize.Width, frmMain.glControl_utility.ClientSize.Height)
    End Sub

    Public Sub Main_prospectiveView()
        resize_glControl_main()
        PROJECTIONMATRIX = Matrix4.CreatePerspectiveFieldOfView( _
                                   CSng(Math.PI) * (FieldOfView / 180.0F), _
                                   frmMain.glControl_main.ClientSize.Width / CSng(frmMain.glControl_main.ClientSize.Height), _
                                   1.0F, 500.0F)

        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadMatrix(PROJECTIONMATRIX)
    End Sub
    Public Sub Main_ortho_main()
        resize_glControl_main()
        PROJECTIONMATRIX = Matrix4.Identity
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadIdentity()
        GL.Ortho(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, 300.0F, -300.0F)
    End Sub
    Public Sub Main_ortho_utility()
        resize_glControl_utility()
        PROJECTIONMATRIX = Matrix4.Identity
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadIdentity()
        GL.Ortho(0.0F, frmMain.glControl_utility.Width, -frmMain.glControl_utility.Height, 0.0F, 300.0F, -300.0F)
    End Sub
End Module

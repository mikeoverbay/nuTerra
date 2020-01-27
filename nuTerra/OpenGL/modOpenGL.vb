

Imports System.Math
Imports System
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities


Module modOpenGL

    Public box_positions(4999) As vec3
    Public FieldOfView As Single = 60.0F

    Public Main_Context As Integer
    Public Sub make_randum_locations()
        Dim rn As New Random
        For i = 0 To 4999
            box_positions(i).x = (rn.NextDouble - 0.5) * 100.0F
            box_positions(i).y = (rn.NextDouble - 0.5) * 0.0F
            box_positions(i).z = (rn.NextDouble - 0.5) * 100.0F
        Next
    End Sub
    Public Sub resize_glControl_main()
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
    End Sub

    Public Sub resize_glControl_utility()
        Dim size As Integer = 180
        Dim position As New Point(frmMain.ClientSize.Width - size, frmMain.ClientSize.Height - size)
        frmMain.glControl_utility.Location = position
        frmMain.glControl_utility.Width = size
        frmMain.glControl_utility.Height = size
        GL.Viewport(0, 0, frmMain.glControl_utility.ClientSize.Width, frmMain.glControl_utility.ClientSize.Height)
    End Sub

    Public Sub Main_prospectiveView()
        resize_glControl_main()
        PROJECTIONMATRIX = Matrix4.CreatePerspectiveFieldOfView( _
                                   CSng(Math.PI) * (FieldOfView / 180.0F), _
                                   frmMain.glControl_main.ClientSize.Width / CSng(frmMain.glControl_main.ClientSize.Height), _
                                   0.5F, 2500.0F)

        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadMatrix(PROJECTIONMATRIX)
    End Sub
    Public Sub Ortho_main()
        resize_glControl_main()
        'PROJECTIONMATRIX = Matrix4.Identity
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadIdentity()
        GL.Ortho(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, -300.0F, 300.0F)
        GL.MatrixMode(MatrixMode.Modelview)
        GL.LoadIdentity()
    End Sub
    Public Sub Ortho_utility()
        resize_glControl_utility()
        PROJECTIONMATRIX = Matrix4.Identity
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadIdentity()
        GL.Ortho(0.0F, frmMain.glControl_utility.Width, -frmMain.glControl_utility.Height, 0.0F, -300.0F, 300.0F)
        GL.MatrixMode(MatrixMode.Modelview)
        GL.LoadIdentity()
    End Sub

    Public Sub set_prespective_view()
        Dim sin_x, cos_x, cos_y, sin_y As Single
        Dim cam_x, cam_y, cam_z As Single

        sin_x = Sin(U_CAM_X_ANGLE)
        cos_x = Cos(U_CAM_X_ANGLE)
        cos_y = Cos(U_CAM_Y_ANGLE)
        sin_y = Sin(U_CAM_Y_ANGLE)
        cam_y = Sin(U_CAM_Y_ANGLE) * VIEW_RADIUS
        cam_x = (sin_x - (1 - cos_y) * sin_x) * VIEW_RADIUS
        cam_z = (cos_x - (1 - cos_y) * cos_x) * VIEW_RADIUS

        CAM_POSITION.X = cam_x + U_LOOK_AT_X
        CAM_POSITION.Y = cam_y + U_LOOK_AT_Y
        CAM_POSITION.Z = cam_z + U_LOOK_AT_Z

        Dim target As Vector3 = New Vector3(U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z)
        Dim position As Vector3 = New Vector3(CAM_POSITION.X, CAM_POSITION.Y, CAM_POSITION.Z)
        Dim up As Vector3 = Vector3.UnitY

        Main_prospectiveView()

        MODELVIEWMATRIX = Matrix4.LookAt(position, target, up)
        GL.MatrixMode(MatrixMode.Modelview)
        GL.LoadMatrix(MODELVIEWMATRIX)

    End Sub

    Public Sub set_light_pos()
        LIGHT_POS(0) = 150.0F
        LIGHT_POS(1) = 30.0F
        LIGHT_POS(2) = 150.0F
        LIGHT_RADIUS = Sqrt(LIGHT_POS(0) ^ 2 + LIGHT_POS(2) ^ 2)
        LIGHT_ORBIT_ANGLE = Atan2(LIGHT_RADIUS / LIGHT_POS(2), LIGHT_RADIUS / LIGHT_POS(1))
    End Sub
End Module

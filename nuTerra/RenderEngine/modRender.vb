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

Module modRender
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Public Sub draw_scene()

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer


        '-------------------------------------------------------
        '1st glControl

        frmMain.glControl_main.MakeCurrent()

        set_prespective_view() ' sets camera and prespective view

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.UseProgram(shader_list.basic_shader)
        Dim x, y As Single
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle1
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 15.0F
            y = Sin(k + j) * 15.0F
            GL.Vertex3(0.0F, 0.0F, 0.0F)
            GL.Vertex3(x, 0.0F, y)
            GL.End()
            angle1 += 0.000001
            If angle1 > PI * 2 / 40 Then
                angle1 = 0
            End If
        Next

        GL.PolygonMode(MaterialFace.Front, PolygonMode.Line)
        GL.PushMatrix()
        GL.Scale(0.1F, 1.0F, 0.1F)

        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IBO)

        GL.EnableClientState(ArrayCap.VertexArray)
        GL.EnableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.EnableClientState(ArrayCap.IndexArray)


        GL.VertexPointer(3, VertexPointerType.Float, 32, 0)
        GL.VertexPointer(3, VertexPointerType.Float, 32, 12)
        GL.VertexPointer(2, VertexPointerType.Float, 32, 24)


        'GL.DrawArrays(PrimitiveType.Triangles, 0, indices.Length - 1)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 9)

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)

        GL.DisableClientState(ArrayCap.VertexArray)
        GL.DisableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.DisableClientState(ArrayCap.IndexArray)
        GL.PopMatrix()
        frmMain.glControl_main.SwapBuffers()


        frmMain.glControl_utility.Visible = False
#If 0 Then
       '-------------------------------------------------------
        '2nd glControl
        frmMain.glControl_utility.MakeCurrent()
        Main_ortho_utility()

        GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        Dim cx = frmMain.glControl_utility.Width / 2
        Dim cy = -frmMain.glControl_utility.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle2
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 150.0F + cx
            y = Sin(k + j) * 150.0F + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle2 += 0.00001
            If angle2 > PI * 2 / 40 Then
                angle2 = 0
            End If
        Next
        GL.UseProgram(0)
        frmMain.glControl_utility.SwapBuffers()
#End If

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
        SetCameraView(CAM_POSITION, target, up)

    End Sub
    Private Sub SetCameraView(ByRef position As Vector3, ByRef target As Vector3, ByRef up As Vector3)
        MODELVIEWMATRIX = Matrix4.LookAt(position, target, up)
        GL.MatrixMode(MatrixMode.Modelview)
        GL.LoadMatrix(MODELVIEWMATRIX)
    End Sub
End Module

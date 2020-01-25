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

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) ' Use default buffer
        FBOm.attach_C()
        '-------------------------------------------------------
        '1st glControl

        frmMain.glControl_main.MakeCurrent()

        set_prespective_view() ' sets camera and prespective view

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        '------------------------------------------------
        '------------------------------------------------
        'basic test pattern
        Dim x, y As Single
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle1
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 15.0F
            y = Sin(k + j) * 15.0F
            GL.Vertex3(0.0F, 0.0F, 0.0F)
            GL.Vertex3(x, 0.0F, y)
            GL.End()
            angle1 += 0.000005
            If angle1 > PI * 2 / 40 Then
                angle1 = 0
            End If
        Next
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, TextureEnvMode.Replace)

        GL.Enable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Lighting)
        GL.Disable(EnableCap.CullFace)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        '------------------------------------------------
        GL.UseProgram(shader_list.basic_shader) '<---- Shader Bind
        '------------------------------------------------
        'GL.Enable(EnableCap.Texture2D)
        GL.Uniform1(basic_text_id, 0)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, dial_face_ID) '<---------- Texture Bind
        'draw VBO IBO
        GL.PushMatrix()
        GL.Scale(0.1F, 0.1F, 0.1F)


        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO)


        GL.EnableClientState(ArrayCap.VertexArray)
        GL.EnableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.EnableClientState(ArrayCap.IndexArray)


        GL.VertexPointer(3, VertexPointerType.Float, 32, 0)
        GL.NormalPointer(NormalPointerType.Float, 32, 12)
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24)

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IBO)

        GL.DrawElements(PrimitiveType.Triangles, (indices.Length) * 3, DrawElementsType.UnsignedShort, 0)


        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)

        GL.DisableClientState(ArrayCap.VertexArray)
        GL.DisableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.DisableClientState(ArrayCap.IndexArray)

        GL.PopMatrix()
        '------------------------------------------------
        '------------------------------------------------

        'direct mode quad
        Dim WIDTH = 30.0F
        Dim HEIGHT = 30.0F




        GL.Begin(PrimitiveType.Quads)

        GL.TexCoord2(0.0F, 1.0F)
        GL.Vertex3(-WIDTH / 2, -0.1F, HEIGHT / 2)

        GL.TexCoord2(1.0F, 1.0F)
        GL.Vertex3(WIDTH / 2, -0.1F, HEIGHT / 2)

        GL.TexCoord2(1.0F, 0.0F)
        GL.Vertex3(WIDTH / 2, -0.1F, -HEIGHT / 2)

        GL.TexCoord2(0.0F, 0.0F)
        GL.Vertex3(-WIDTH / 2, -0.1F, -HEIGHT / 2)
        GL.End()

        GL.BindTexture(TextureTarget.Texture2D, 0) '<---- texture unbind

        frmMain.glControl_main.SwapBuffers()


#If 1 Then
        '-------------------------------------------------------
        frmMain.glControl_utility.Visible = True
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
        frmMain.glControl_utility.SwapBuffers()
#End If
        GL.UseProgram(0)

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

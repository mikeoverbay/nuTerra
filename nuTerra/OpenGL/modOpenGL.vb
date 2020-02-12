Imports System.Math
Imports System.Runtime.InteropServices
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL


Module modOpenGL
    Public defaultVao As Integer
    Public FieldOfView As Single = 60.0F

    Public Main_Context As Integer
    Public Sub resize_glControl_main()
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
        VIEW_PORT(0) = frmMain.glControl_main.ClientSize.Width
        VIEW_PORT(1) = frmMain.glControl_main.ClientSize.Height
    End Sub

    Public Sub resize_glControl_MiniMap(ByVal square_size As Integer)
        'puts this at the bottom right corner of the main window.
        'It needs be resizable just like the game.
        Dim position As New Point(frmMain.ClientSize.Width - square_size, frmMain.ClientSize.Height - square_size)
        frmMain.glControl_MiniMap.Location = position
        frmMain.glControl_MiniMap.Width = square_size
        frmMain.glControl_MiniMap.Height = square_size
        GL.Viewport(0, 0, frmMain.glControl_MiniMap.ClientSize.Width, frmMain.glControl_MiniMap.ClientSize.Height)
    End Sub

    Public Sub Ortho_main()
        resize_glControl_main()
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, frmMain.glControl_main.Width, -frmMain.glControl_main.Height, 0.0F, -300.0F, 300.0F)
        MODELVIEWMATRIX = Matrix4.Identity
    End Sub

    Public Sub Ortho_MiniMap(ByVal square_size As Integer)
        resize_glControl_MiniMap(square_size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, frmMain.glControl_MiniMap.Width, -frmMain.glControl_MiniMap.Height, 0.0F, -300.0F, 300.0F)
        MODELVIEWMATRIX = Matrix4.Identity

        'Set Legacy
        GL.MatrixMode(MatrixMode.Projection)
        GL.LoadMatrix(PROJECTIONMATRIX)
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
        cam_y = sin_y * VIEW_RADIUS
        cam_x = cos_y * sin_x * VIEW_RADIUS
        cam_z = cos_y * cos_x * VIEW_RADIUS

        CAM_POSITION.X = cam_x + U_LOOK_AT_X
        CAM_POSITION.Y = cam_y + U_LOOK_AT_Y
        CAM_POSITION.Z = cam_z + U_LOOK_AT_Z

        Dim target As New Vector3(U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z)
        Dim position As New Vector3(CAM_POSITION.X, CAM_POSITION.Y, CAM_POSITION.Z)
        Dim up As Vector3 = Vector3.UnitY

        PROJECTIONMATRIX = Matrix4.CreatePerspectiveFieldOfView(
                                   CSng(Math.PI) * (FieldOfView / 180.0F),
                                   frmMain.glControl_main.ClientSize.Width / CSng(frmMain.glControl_main.ClientSize.Height),
                                   PRESPECTIVE_NEAR, PRESPECTIVE_FAR)

        MODELVIEWMATRIX = Matrix4.LookAt(position, target, up)
        MODELVIEWMATRIX_Saved = MODELVIEWMATRIX
    End Sub

    Public Sub set_light_pos()
        LIGHT_POS(0) = 400.0F
        LIGHT_POS(1) = 200.0F
        LIGHT_POS(2) = 400.0F
        LIGHT_RADIUS = Sqrt(LIGHT_POS(0) ^ 2 + LIGHT_POS(2) ^ 2)
        LIGHT_ORBIT_ANGLE = Atan2(LIGHT_RADIUS / LIGHT_POS(2), LIGHT_RADIUS / LIGHT_POS(1))
    End Sub

    Public Sub draw_color_rectangle(rect As RectangleF, color As Color4)
        rect2dShader.Use()

        GL.Uniform4(rect2dShader("color"), color)
        GL.UniformMatrix4(rect2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(rect2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        GL.BindVertexArray(0)

        rect2dShader.StopUse()
    End Sub

    Public Sub draw_image_rectangle(rect As RectangleF, image As Integer)
        image2dShader.Use()

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, image)

        GL.Uniform1(image2dShader("imageMap"), 0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        GL.BindVertexArray(0)

        image2dShader.StopUse()
        'unbind texture
        GL.BindTexture(TextureTarget.Texture2D, 0)

    End Sub

    Public Sub DebugOutputCallback(source As DebugSource,
                                   type As DebugType,
                                   id As UInteger,
                                   severity As DebugSeverity,
                                   length As Integer,
                                   messagePtr As IntPtr,
                                   userParam As IntPtr)
        Dim message = Marshal.PtrToStringAnsi(messagePtr)
        Debug.Print("OpenGL error #{0}: {1}", id, message)
    End Sub

    Public Sub SetupDebugOutputCallback()
        GL.Enable(EnableCap.DebugOutput)
        GL.Enable(EnableCap.DebugOutputSynchronous)
        GL.DebugMessageCallback(New DebugProc(AddressOf DebugOutputCallback), IntPtr.Zero)
        GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DebugTypeError, DebugSeverityControl.DontCare, 0, 0, True)
    End Sub

    Public Function get_GL_error_string(ByVal e As ErrorCode) As String
        Return [Enum].GetName(GetType(ErrorCode), e)
    End Function
End Module

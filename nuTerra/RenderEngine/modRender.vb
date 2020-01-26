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
        Dim cx, cy As Single
        frmMain.glControl_main.MakeCurrent()

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) ' Use default buffer
        FBOm.attach_C()
        '-------------------------------------------------------
        '1st glControl

        set_prespective_view() ' <-- sets camera and prespective view

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        '------------------------------------------------
        '------------------------------------------------
        'GL States
        GL.Enable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Lighting)
        GL.Disable(EnableCap.CullFace)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
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



        '------------------------------------------------
        GL.UseProgram(shader_list.basic_shader) '<------------------------------- Shader Bind
        '------------------------------------------------
        'GL.Enable(EnableCap.Texture2D)
        GL.Uniform1(basic_text_id, 0)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, dial_face_ID) '<------------------------------- Texture Bind

        '------------------------------------------------
        'Draw Test VBO
        '------------------------------------------------
        GL.PushMatrix()
        GL.Scale(0.1F, 0.1F, 0.1F)
        '
        'Bind the main Array of data. This one uses packed data as:
        'Vertex : 3 floats
        'normal : 3 floats
        'UV Coords : 2 floats
        'A total of 8 floats or.. 32 bytes so the stride is 32.
        '
        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO)
        'Enable the data element types in the VBO (vertex, normal ... ).
        GL.EnableClientState(ArrayCap.VertexArray)
        GL.EnableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.EnableClientState(ArrayCap.IndexArray)
        '
        'We assign each element to the slots (gl_Normal, gl_Vertex, gl_textCoord) to the array data par ts.
        'The last 2 values are Stide and )ffset to start of next element.
        GL.VertexPointer(3, VertexPointerType.Float, 32, 0)         ' 3 floats next is at --> 12 
        GL.NormalPointer(NormalPointerType.Float, 32, 12)           ' 3 floats --> next is at 24
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24)    ' 2 floats --> None after
        '
        'WE bind the ElementArrayBuffer. This is where the indexing in to the VBO is stored.
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IBO)
        '
        'Draw everything now that its all bound and the states are set.
        GL.DrawElements(PrimitiveType.Triangles, (indices.Length) * 3, DrawElementsType.UnsignedShort, 0)
        '
        ' Unbind everything. 
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        'Disable states
        GL.DisableClientState(ArrayCap.VertexArray)
        GL.DisableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.DisableClientState(ArrayCap.IndexArray)
        '
        GL.PopMatrix()
        '------------------------------------------------
        'End Test VBO draw.
        '------------------------------------------------

        'direct mode quad also just for testing.
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

        GL.BindTexture(TextureTarget.Texture2D, 0) '<------------------------------- texture unbind

        GL.UseProgram(0)


#If 0 Then
        frmMain.glControl_utility.Visible = True
        '-------------------------------------------------------
        '2nd glControl
        frmMain.glControl_utility.MakeCurrent()
        Ortho_utility()

        GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        cx = frmMain.glControl_utility.Width / 2
        cy = -frmMain.glControl_utility.Height / 2
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
        frmMain.glControl_main.MakeCurrent()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        Ortho_main()

        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Texture2D)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)
        'GL.BindTexture(TextureTarget.Texture2D, dial_face_ID)
        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
        GL.Disable(EnableCap.Texture2D)
        GL.BindTexture(TextureTarget.Texture2D, 0)

        ' test render some text to see if it works
        Dim position = PointF.Empty
        'textRender.DrawText.TextRenderer(100, 100) '<--- reset when the FBO changes size!
        textRender.DrawText.clear(Color.FromArgb(0, 0, 0, 0))
        Dim ti = TimeOfDay.TimeOfDay
        textRender.DrawText.DrawString("Current Time:" + ti.ToString, mono, Brushes.White, position)
        frmMain.glControl_utility.Visible = False
        GL.Enable(EnableCap.Texture2D)
        GL.Enable(EnableCap.Blend)
        GL.Color4(1.0F, 1.0F, 1.0F, 0.0F)
        GL.BindTexture(TextureTarget.Texture2D, textRender.DrawText.Gettexture)
        GL.Begin(PrimitiveType.Quads)
        Dim he As Integer = 20
        GL.TexCoord2(0.0F, 1.0F) : GL.Vertex2(0.0F, -he)
        GL.TexCoord2(1.0F, 1.0F) : GL.Vertex2(FBOm.SCR_WIDTH, -he)
        GL.TexCoord2(1.0F, 0.0F) : GL.Vertex2(FBOm.SCR_WIDTH, 0.0F)
        GL.TexCoord2(0.0F, 0.0F) : GL.Vertex2(0.0F, 0.0F)

        GL.End()
        GL.Disable(EnableCap.Texture2D)



        frmMain.glControl_main.SwapBuffers()

    End Sub

    Private Sub draw_main_Quad(ByRef w As Integer, ByRef h As Integer)
        GL.Begin(PrimitiveType.Quads)
        'G_Buffer.getsize(w, h)
        '  CCW...
        '  1 ------ 4
        '  |        |
        '  |        |
        '  2 ------ 3
        '
        GL.TexCoord2(0.0F, 1.0F)
        GL.Vertex2(0.0F, 0.0F)

        GL.TexCoord2(0.0F, 0.0F)
        GL.Vertex2(0, -h)

        GL.TexCoord2(1.0F, 0.0F)
        GL.Vertex2(w, -h)

        GL.TexCoord2(1.0F, 1.0F)
        GL.Vertex2(w, 0.0F)
        GL.End()

    End Sub
End Module

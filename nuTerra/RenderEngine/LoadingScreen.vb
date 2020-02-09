Imports System.Runtime.InteropServices.Marshal
Imports OpenTK.Graphics.OpenGL


Module LoadingScreen
#Region "Variables"
    Public BG_MAX_VALUE As Double
    Public BG_VALUE As Double
    Public BG_TEXT As String
#End Region

    Public Sub draw_loading_screen()
        'This is important!
        frmMain.glControl_main.MakeCurrent()

        DrawMapPickText.TextRenderer(300, 30)
        'really dont need this but to be safe we set the default buffer!
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        'We are reusing this for the loading screen
        'and resize for our needs.

        Ortho_main()
        Dim ww = frmMain.glControl_main.ClientRectangle.Width
        'calculate scaling
        Dim w_MaxValue = frmMain.glControl_main.ClientRectangle.Width / BG_MAX_VALUE
        Dim w_Valuev = (BG_VALUE / BG_MAX_VALUE) * (ww)

        ' Clear the color buffer
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        Dim ls = (1920.0F - ww) / 2.0F

        ' Draw Terra Image
        draw_image_rectangle(New PointF(-ls, 0.0F),
                             New PointF(-ls + 1920.0F, 1080.0F),
                             nuTERRA_BG_IMAGE)

        ' Draw 'Loading Models...' text
        draw_text()

        '==============================================
        'Draw Bargraph ================================
        '==============================================

        Dim SP As New PointF
        Dim EP As New PointF

        Dim w = BG_MAX_VALUE
        Dim h = 20.0
        SP.X = 0.0F : SP.Y = 10.0F
        EP.X = frmMain.glControl_main.ClientRectangle.Width : EP.Y = 30
        EP.X = w_Valuev : EP.Y = 30

        GL.Color3(1.0F, 1.0F, 1.0F)
        draw_rectangle_LEGACY(SP, EP)

        ' Make it so!
        frmMain.glControl_main.SwapBuffers()
    End Sub

    Private Sub draw_rectangle_LEGACY(ByVal SP As PointF, ByVal EP As PointF)
        GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Begin(PrimitiveType.Quads)
        '  CCW...
        '  1 ------ 4
        '  |        |
        '  |        |
        '  2 ------ 3
        '
        GL.TexCoord2(0.0F, 1.0F)
        GL.Vertex2(SP.X, -SP.Y)

        GL.TexCoord2(0.0F, 0.0F)
        GL.Vertex2(SP.X, -EP.Y)

        GL.TexCoord2(1.0F, 0.0F)
        GL.Vertex2(EP.X, -EP.Y)

        GL.TexCoord2(1.0F, 1.0F)
        GL.Vertex2(EP.X, -SP.Y)
        GL.End()
    End Sub

    Private Sub draw_image_rectangle(ByVal SP As PointF, ByVal EP As PointF, ByVal image As Integer)
        '  CCW...
        '  1 ------ 4
        '  |        |
        '  |        |
        '  2 ------ 3
        '

        Dim rectVao As Integer
        GL.GenVertexArrays(1, rectVao)
        GL.BindVertexArray(rectVao)

        Dim rectBuffers(1) As Integer
        GL.GenBuffers(2, rectBuffers)

        Dim vertices As Single() = {
            SP.X, -SP.Y,
            SP.X, -EP.Y,
            EP.X, -EP.Y,
            EP.X, -SP.Y
            }

        Dim textCoords As Single() = {
            0.0F, 1.0F,
            0.0F, 0.0F,
            1.0F, 0.0F,
            1.0F, 1.0F
            }

        GL.BindBuffer(BufferTarget.ArrayBuffer, rectBuffers(0))
        GL.BufferData(BufferTarget.ArrayBuffer,
                      vertices.Length * SizeOf(GetType(Single)),
                      vertices,
                      BufferUsageHint.StaticDraw)

        ' vertices
        GL.VertexAttribPointer(0,
                               2,
                               VertexAttribPointerType.Float,
                               False,
                               0,
                               0)
        GL.EnableVertexAttribArray(0)


        GL.BindBuffer(BufferTarget.ArrayBuffer, rectBuffers(1))
        GL.BufferData(BufferTarget.ArrayBuffer,
                      textCoords.Length * SizeOf(GetType(Single)),
                      textCoords,
                      BufferUsageHint.StaticDraw)

        ' texcoords
        GL.VertexAttribPointer(1,
                               2,
                               VertexAttribPointerType.Float,
                               False,
                               0,
                               0)
        GL.EnableVertexAttribArray(1)


        GL.UseProgram(shader_list.image2d_shader)

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, image)
        GL.Uniform1(image2d_imageMap_id, 0)

        GL.UniformMatrix4(image2d_ProjectionMatrix_id, False, PROJECTIONMATRIX)

        GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4)

        'unbind texture
        GL.BindTexture(TextureTarget.Texture2D, 0)

        GL.UseProgram(0)

        GL.BindVertexArray(0)

        GL.DeleteVertexArrays(1, rectVao)
        GL.DeleteBuffers(2, rectBuffers)
    End Sub

    Private Sub draw_text()
        DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
        DrawMapPickText.DrawString(BG_TEXT, mono, Brushes.White, PointF.Empty)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        draw_image_rectangle(New PointF(0, 60), New PointF(300, 30), DrawMapPickText.Gettexture)

        GL.Disable(EnableCap.Blend)
    End Sub
End Module

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

    Private Sub draw_text()
        DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
        DrawMapPickText.DrawString(BG_TEXT, mono, Brushes.White, PointF.Empty)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        draw_image_rectangle(New PointF(0, 60), New PointF(300, 30), DrawMapPickText.Gettexture)

        GL.Disable(EnableCap.Blend)
    End Sub
End Module

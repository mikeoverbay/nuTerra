Imports System.IO
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Module LoadingScreen
#Region "Variables"
    Public BG_MAX_VALUE As Double
    Public BG_VALUE As Double
    Public BG_TEXT As String
    Public description_string As String
#End Region

    Public Sub draw_loading_screen()
        'really dont need this but to be safe we set the default buffer!
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        'and resize for our needs.

        Ortho_main()
        Dim ww = Window.SCR_WIDTH
        'calculate scaling
        Dim w_Valuev = (BG_VALUE / BG_MAX_VALUE) * (ww)

        ' Clear the color buffer
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        Dim ls = (1920.0F - ww) / 2.0F

        '---------------------------------------------------------
        'loading screen image
        If nuTERRA_BG_IMAGE Is Nothing Then
            nuTERRA_BG_IMAGE = TextureMgr.load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\earth.png"), False, True)
        End If

        ' Draw Terra Image
        draw_image_rectangle(New RectangleF(-ls, 0, 1920, 1080),
                             nuTERRA_BG_IMAGE)

        ' Draw 'Loading Models...' text
        draw_text(BG_TEXT, 5, 30, Color4.White, False, 0)

        'Draw Bargraph
        GL.Enable(EnableCap.Blend)
        draw_image_rectangle(New RectangleF(0.0F, 10.0F, w_Valuev, 20),
                             PROGRESS_BAR_IMAGE_ID)

        draw_text_Wrap(description_string, 10, 70,
                       Color4.Coral, False, False, 700.0)

        GL.Disable(EnableCap.Blend)
    End Sub

End Module

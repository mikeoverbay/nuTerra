﻿Imports OpenTK.Graphics.OpenGL

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
        Dim w_Valuev = (BG_VALUE / BG_MAX_VALUE) * (ww)

        ' Clear the color buffer
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        Dim ls = (1920.0F - ww) / 2.0F

        ' Draw Terra Image
        draw_image_rectangle(New RectangleF(-ls, 0, 1920, 1080),
                             nuTERRA_BG_IMAGE)

        ' Draw 'Loading Models...' text
        draw_text(BG_TEXT, 5, 30, OpenTK.Graphics.Color4.White, False, 0)

        'Draw Bargraph
        GL.Enable(EnableCap.Blend)
        draw_image_rectangle(New RectangleF(0.0F, 10.0F, w_Valuev, 20),
                             PROGRESS_BAR_IMAGE_ID)

        GL.Disable(EnableCap.Blend)
        ' Make it so!
        frmMain.glControl_main.SwapBuffers()
        GL.Flush()
    End Sub

End Module

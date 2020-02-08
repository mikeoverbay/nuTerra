#Region "Imports"
Imports System.Math
Imports System
Imports System.IO
Imports System.Globalization
Imports System.Threading
Imports System.Windows
Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
#End Region


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

        'GL States
        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Lighting)
        GL.Disable(EnableCap.CullFace)
        GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)
        GL.Disable(EnableCap.Blend)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.ActiveTexture(TextureUnit.Texture0)

        'clear the buffer
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        '==============================================
        'Draw Terra Image =============================
        '==============================================
        GL.Enable(EnableCap.Texture2D)
        Dim SP, EP As PointF
        Dim ls = (1920.0F - ww) / 2.0F
        SP.X = -ls : SP.Y = 0.0F
        EP.X = -ls + 1920.0F : EP.Y = 1080.0F
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.BindTexture(TextureTarget.Texture2D, nuTERRA_BG_IMAGE)
        draw_rectangle(SP, EP)

        '==============================================
        'Draw Text ====================================
        '==============================================
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        draw_text()
        'unbind texture
        GL.BindTexture(TextureTarget.Texture2D, 0)
        GL.Disable(EnableCap.Texture2D)
        '==============================================
        'Draw Bargraph ================================
        '==============================================
        GL.Color4(1.0F, 1.0F, 1.0F, 1.0F)

        GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)
        Dim w = BG_MAX_VALUE
        Dim h = 20.0
        SP.X = 0.0F : SP.Y = 10.0F
        EP.X = frmMain.glControl_main.ClientRectangle.Width : EP.Y = 30
        EP.X = w_Valuev : EP.Y = 30
        draw_rectangle(SP, EP)

        'Make it so!
        frmMain.glControl_main.SwapBuffers()

    End Sub
    Private Sub draw_rectangle(ByVal SP As PointF, ByVal EP As PointF)
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

        GL.Enable(EnableCap.AlphaTest)
        GL.AlphaFunc(AlphaFunction.Equal, 1.0)

        Dim position = PointF.Empty
        DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
        DrawMapPickText.DrawString(BG_TEXT, mono, Brushes.White, position)

        GL.BindTexture(TextureTarget.Texture2D, DrawMapPickText.Gettexture)
        GL.Begin(PrimitiveType.Quads)
        GL.TexCoord2(0.0F, 1.0F) : GL.Vertex2(0.0F, -60)
        GL.TexCoord2(1.0F, 1.0F) : GL.Vertex2(300, -60)
        GL.TexCoord2(1.0F, 0.0F) : GL.Vertex2(300, -30.0F)
        GL.TexCoord2(0.0F, 0.0F) : GL.Vertex2(0.0F, -30.0F)

        GL.End()

    End Sub
End Module

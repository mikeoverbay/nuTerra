Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module fbo_main
    Public FBOm As FBOm_
    Public mainFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    ''' <remarks></remarks>
    Public Class FBOm_
        Public SCR_WIDTH, SCR_HEIGHT As Int32
        Public gColor, gNormal, gGMF, gDepth, depthBufferTexture As Integer
        Private attach_Color_Normal_GMF_Depth() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0, _
                                            FramebufferAttachment.ColorAttachment1, _
                                            FramebufferAttachment.ColorAttachment2, _
                                            FramebufferAttachment.ColorAttachment3}

        Public Sub FBO_Initialize()
            delete_textures_and_fbo()
            get_mainFBO_size(SCR_WIDTH, SCR_HEIGHT)
            create_textures()
            If Not create_fbo() Then
                MsgBox("Failed to create main FBO" + vbCrLf + "I must down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                End
            End If
        End Sub
        Public Sub delete_textures_and_fbo()
            If mainFBO > 0 Then
                GL.DeleteFramebuffer(mainFBO)
            End If
            If gColor > 0 Then
                GL.DeleteTexture(gColor)
            End If
            If gNormal > 0 Then
                GL.DeleteTexture(gNormal)
            End If
            If gGMF > 0 Then
                GL.DeleteTexture(gGMF)
            End If
            If gDepth > 0 Then
                GL.DeleteTexture(gDepth)
            End If
            If depthBufferTexture > 0 Then
                GL.DeleteRenderbuffer(depthBufferTexture)
            End If
        End Sub
        Public Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            '4 color int : RGB and alpha
            gColor = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gColor)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Me.SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedInt, Nothing)
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gNormal ------------------------------------------------------------------------------------------
            '4 color int : normal in RGB : Height in A
            gNormal = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gNormal)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Me.SCR_WIDTH, Me.SCR_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedInt, Nothing)
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gGM_Flag ------------------------------------------------------------------------------------------
            '24 bit float
            gDepth = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gDepth)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgb, PixelType.Float, Nothing)
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gDepth ------------------------------------------------------------------------------------------
            '3 color int : GM in RG : Flag in b 
            gGMF = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gGMF)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgb, PixelType.UnsignedInt, Nothing)
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

        End Sub
        Public Function create_fbo() As Boolean
            _STOPGL = True 'stop rendering
            Threading.Thread.Sleep(50) ' give rendering a chance to stop.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

            'creat the FBO
            mainFBO = GL.GenFramebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)

            'create the FBOs depth buffer
            depthBufferTexture = GL.GenRenderbuffer
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBufferTexture)
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent32, Me.SCR_WIDTH, Me.SCR_HEIGHT)
            GL.FramebufferRenderbuffer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBufferTexture))

            'attach our render buffer textures.
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, gColor, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, gNormal, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3, gGMF, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment4, gDepth, 0)

            GL.DrawBuffers(4, attach_Color_Normal_GMF_Depth)
            Dim FBOHealth = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If
            Return True ' all good :)
        End Function
        Public Sub get_mainFBO_size(ByRef w As Integer, ByRef h As Integer)
            w = frmMain.glControl_main.Width
            h = frmMain.glControl_main.Height
            Return
        End Sub
    End Class


End Module

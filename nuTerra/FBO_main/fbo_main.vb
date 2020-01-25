
Imports System.Math
Imports System
Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module FBO_main
    Public mainFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    ''' <remarks></remarks>
    Public NotInheritable Class FBOm
        Public Shared SCR_WIDTH, SCR_HEIGHT As Int32
        Public Shared gColor, gNormal, gGMF, gDepth, depthBufferTexture As Integer
        Private Shared oldWidth As Integer = 1
        Private Shared oldHeigth As Integer = 1

        Private Shared attach_Color_Normal_GMF_Depth() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0, _
                                            FramebufferAttachment.ColorAttachment1, _
                                            FramebufferAttachment.ColorAttachment2, _
                                            FramebufferAttachment.ColorAttachment3 _
                                            }
        Public Shared attach_Color() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0 _
                                            }
        Public Shared attach_Normal() As Integer = { _
                                            FramebufferAttachment.ColorAttachment1 _
                                            }


        Public Shared Sub FBO_Initialize()
            SYNCMUTEX.WaitOne()
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

            ' Stop changing the size becuase of excessive window resize calls.
            get_glControl_main_size(SCR_WIDTH, SCR_HEIGHT)

            If oldWidth <> SCR_WIDTH And oldHeigth <> SCR_HEIGHT Then
                delete_textures_and_fbo()
                create_textures()
                If Not create_fbo() Then
                    MsgBox("Failed to create main FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                    End
                End If
                'set new size
                oldWidth = SCR_WIDTH
                oldHeigth = SCR_HEIGHT
            End If
            SYNCMUTEX.ReleaseMutex()
        End Sub
        Public Shared Sub delete_textures_and_fbo()
            'as the name says
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
            If mainFBO > 0 Then
                GL.DeleteFramebuffer(mainFBO)
            End If
            If depthBufferTexture > 0 Then
                GL.DeleteRenderbuffer(depthBufferTexture)
            End If
        End Sub

        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            '4 color int : RGB and alpha
            Dim er0 = GL.GetError
            gColor = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gColor)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, Nothing)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gNormal ------------------------------------------------------------------------------------------
            '4 color int : normal in RGB : Height in A
            Dim er1 = GL.GetError
            gNormal = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gNormal)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, Nothing)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gGM_Flag ------------------------------------------------------------------------------------------
            '24 bit float
            Dim er2 = GL.GetError
            gDepth = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gDepth)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Red, PixelType.Float, Nothing)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            ' gDepth ------------------------------------------------------------------------------------------
            '3 color int : GM in RG : Flag in b 
            Dim er3 = GL.GetError
            gGMF = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gGMF)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgb, PixelType.UnsignedByte, Nothing)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
            Dim er4 = GL.GetError

        End Sub

        Public Shared Function create_fbo() As Boolean

            'creat the FBO
            mainFBO = GL.GenFramebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)
            Dim er0 = GL.GetError

            'create the FBOs depth buffer
            depthBufferTexture = GL.GenRenderbuffer
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBufferTexture)
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent32, SCR_WIDTH, SCR_HEIGHT)
            GL.FramebufferRenderbuffer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBufferTexture)
            Dim er1 = GL.GetError
            'attach our render buffer textures.
            attach_CNGD()
            Dim FBOHealth = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            'set buffer target to default.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

            Return True ' No errors! all is good! :)
        End Function

        Public Shared Sub get_glControl_main_size(ByRef w As Integer, ByRef h As Integer)
            'returns the size of the render control
            'We must ensure that the window size is divisible by 2. GL doesn't like odd sized textures!
            frmMain.glControl_main.Width = frmMain.ClientSize.Width
            frmMain.glControl_main.Height = frmMain.ClientSize.Height - frmMain.MainMenuStrip.Height
            frmMain.glControl_main.Location = New System.Drawing.Point(0, frmMain.MainMenuStrip.Height + 1)
            Dim w1 = frmMain.glControl_main.Width
            Dim h1 = frmMain.glControl_main.Height
            w = w1 + (w1 Mod 2)
            h = h1 + (h1 Mod 2)
            frmMain.glControl_main.Width = w
            frmMain.glControl_main.Height = h
            Return
        End Sub

        Public Shared Sub attach_CNGD()
            'attach our render buffer textures.
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, gColor, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, gNormal, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, gGMF, 0)
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3, gDepth, 0)
            GL.DrawBuffers(4, attach_Color_Normal_GMF_Depth)
        End Sub

        Public Shared Sub attach_C()
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, gColor, 1)
            GL.DrawBuffers(1, attach_Color)
        End Sub

        Public Shared Sub attach_N()
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, gNormal, 1)
            GL.DrawBuffers(1, attach_Normal)
        End Sub

    End Class


End Module

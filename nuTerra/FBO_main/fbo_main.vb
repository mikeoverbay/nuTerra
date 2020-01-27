﻿
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

        Private Shared attach_Color_Normal_GMF() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0, _
                                            FramebufferAttachment.ColorAttachment1, _
                                            FramebufferAttachment.ColorAttachment2 _
                                            }
        Private Shared attach_Color_Normal() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0, _
                                            FramebufferAttachment.ColorAttachment1 _
                                            }
        Public Shared attach_Color() As Integer = { _
                                            FramebufferAttachment.ColorAttachment0 _
                                            }
        Public Shared attach_Normal() As Integer = { _
                                            FramebufferAttachment.ColorAttachment1 _
                                            }


        Public Shared Sub FBO_Initialize()
            SYNCMUTEX.WaitOne()

            ' Stop changing the size becuase of excessive window resize calls.
            get_glControl_main_size(SCR_WIDTH, SCR_HEIGHT)

            If oldWidth <> SCR_WIDTH Or oldHeigth <> SCR_HEIGHT Then
                delete_textures_and_fbo()

                GL.Enable(EnableCap.Texture2D)
                create_textures()

                If Not create_fbo() Then
                    MsgBox("Failed to create main FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                    End
                End If
                GL.Disable(EnableCap.Texture2D)
                'set new size
                oldWidth = SCR_WIDTH
                oldHeigth = SCR_HEIGHT
                'reset the size of the text header on the page
                textRender.DrawText.TextRenderer(SCR_WIDTH, 20)
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
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            ' gNormal ------------------------------------------------------------------------------------------
            '4 color int : normal in RGB : Height in A
            Dim er1 = GL.GetError
            gNormal = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gNormal)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            ' gGM_Flag ------------------------------------------------------------------------------------------
            '3 color int : GM in RG : Flag in b 
            Dim er3 = GL.GetError
            gGMF = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gGMF)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            Dim er4 = GL.GetError
            ' gDepth ------------------------------------------------------------------------------------------
            'DepthComponent32
            Dim er2 = GL.GetError
            gDepth = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gDepth)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32, SCR_WIDTH, SCR_HEIGHT, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            Dim er20 = GL.GetError
        End Sub

        Public Shared Function create_fbo() As Boolean

            'creat the FBO
            mainFBO = GL.GenFramebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)
            Dim er0 = GL.GetError


            'attach our render buffer textures.

            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, gColor, 0)
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment2, TextureTarget.Texture2D, gNormal, 0)
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment3, TextureTarget.Texture2D, gGMF, 0)
            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.DepthAttachmentExt, TextureTarget.Texture2D, gDepth, 0)


            attach_CNG()
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

        Public Shared Sub attach_CNG()
            'attach our render buffer textures.
            GL.DrawBuffers(4, attach_Color_Normal_GMF)
        End Sub
        Public Shared Sub attach_CN()
            'attach our render buffer textures.
            GL.DrawBuffers(3, attach_Color_Normal)
        End Sub

        Public Shared Sub attach_C()
            GL.DrawBuffers(1, attach_Color)
        End Sub

        Public Shared Sub attach_N()
            GL.DrawBuffers(1, attach_Normal)
        End Sub

        Public Shared Sub blit_depth_to_depth_texture()
            'Dim e1 = GL.GetError
            GL.ActiveTexture(TextureUnit.Texture0)
            GL.BindTexture(TextureTarget.Texture2D, gDepth)
            GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24Arb, 0, 0, SCR_WIDTH, SCR_WIDTH, 0)
            Dim e2 = GL.GetError
            Dim s = get_GL_error_string(e2)
            GL.BindTexture(TextureTarget.Texture2D, 0)

        End Sub
    End Class


End Module

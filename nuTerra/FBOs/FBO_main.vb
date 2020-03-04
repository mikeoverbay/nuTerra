﻿Imports OpenTK.Graphics.OpenGL

Module FBO_main
    Public mainFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBOm
        Public Shared SCR_WIDTH, SCR_HEIGHT As Int32
        Public Shared gColor, gNormal, gGMF, gDepth, depthBufferTexture, gPosition As Integer
        Public Shared oldWidth As Integer = 1
        Public Shared oldHeigth As Integer = 1
        ' color    = 0
        ' normal   = 1
        ' GMM      = 3
        ' Position = 4
        Private Shared attach_Color_Normal_GMF() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment1,
                                            FramebufferAttachment.ColorAttachment2,
                                            FramebufferAttachment.ColorAttachment3
                                            }
        Private Shared attach_Color_Normal() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment1,
                                            FramebufferAttachment.ColorAttachment3
                                            }
        Private Shared attach_Color() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0
                                            }
        Private Shared attach_Color_GMF() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment2
                                            }
        Private Shared attach_Normal() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment1
                                            }


        Public Shared Sub FBO_Initialize()
            frmMain.glControl_main.MakeCurrent()
            ' Stop changing the size becuase of excessive window resize calls.
            get_glControl_main_size(SCR_WIDTH, SCR_HEIGHT)

            If oldWidth <> SCR_WIDTH Or oldHeigth <> SCR_HEIGHT Then
                delete_textures_and_fbo()

                create_textures()

                If Not create_fbo() Then
                    MsgBox("Failed to create main FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                    End
                End If
                'set new size
                oldWidth = SCR_WIDTH
                oldHeigth = SCR_HEIGHT
                'reset the size of the text header on the page
                DrawText.TextRenderer(SCR_WIDTH, 20)

            End If
            make_test_texture()
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
            If gPosition > 0 Then
                GL.DeleteTexture(gPosition)
            End If
            If mainFBO > 0 Then
                GL.DeleteFramebuffer(mainFBO)
            End If
            If depthBufferTexture > 0 Then
                GL.DeleteRenderbuffer(depthBufferTexture)
            End If
            GL.Finish() '<-- Make sure they are gone!
        End Sub

        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            ' 4 color int : RGB and alpha
            GL.CreateTextures(TextureTarget.Texture2D, 1, gColor)
            GL.TextureParameter(gColor, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(gColor, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TextureParameter(gColor, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TextureParameter(gColor, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            GL.TextureStorage2D(gColor, 1, SizedInternalFormat.Rgba8, SCR_WIDTH, SCR_HEIGHT)

            ' gNormal ------------------------------------------------------------------------------------------
            ' 3 color 16f : normal in RGB
            GL.CreateTextures(TextureTarget.Texture2D, 1, gNormal)
            GL.TextureParameter(gNormal, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(gNormal, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TextureParameter(gNormal, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TextureParameter(gNormal, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            GL.TextureStorage2D(gNormal, 1, DirectCast(InternalFormat.R11fG11fB10f, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gGM_Flag ------------------------------------------------------------------------------------------
            ' 3 color int : GM in RG : Flag in b
            GL.CreateTextures(TextureTarget.Texture2D, 1, gGMF)
            GL.TextureParameter(gGMF, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(gGMF, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TextureParameter(gGMF, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TextureParameter(gGMF, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            GL.TextureStorage2D(gGMF, 1, DirectCast(InternalFormat.Rgb8, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gPosition ------------------------------------------------------------------------------------------
            ' RGB16F
            GL.CreateTextures(TextureTarget.Texture2D, 1, gPosition)
            GL.TextureParameter(gPosition, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(gPosition, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TextureParameter(gPosition, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TextureParameter(gPosition, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            GL.TextureStorage2D(gPosition, 1, DirectCast(InternalFormat.Rgb16f, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gDepth ------------------------------------------------------------------------------------------
            'DepthComponent24
            GL.CreateTextures(TextureTarget.Texture2D, 1, gDepth)
            GL.TextureParameter(gDepth, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(gDepth, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TextureParameter(gDepth, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TextureParameter(gDepth, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            GL.TextureStorage2D(gDepth, 1, DirectCast(PixelInternalFormat.DepthComponent24, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)
        End Sub

        Public Shared Function create_fbo() As Boolean
            GL.CreateFramebuffers(1, mainFBO)

            ' attach our render buffer textures.
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment0, gColor, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment1, gNormal, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment2, gGMF, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment3, gPosition, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, gDepth, 0)

            attach_CNGP()
            Dim FBOHealth = GL.CheckNamedFramebufferStatus(mainFBO, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function

        Public Shared Sub get_glControl_main_size(ByRef w As Integer, ByRef h As Integer)
            'returns the size of the render control
            'We must ensure that the window size is divisible by 2. GL doesn't like odd sized textures!

            'This has to be done this way because of the menu and even size buffer textures.
            'Just docking the control in fill causes problems.
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

        Public Shared Sub get_glControl_size(ByRef w As Integer, ByRef h As Integer)
            w = frmMain.glControl_main.Width
            h = frmMain.glControl_main.Height
        End Sub

        Public Shared Sub attach_CNGP()
            'attach our render buffer textures.
            GL.NamedFramebufferDrawBuffers(mainFBO, 4, attach_Color_Normal_GMF)
        End Sub

        Public Shared Sub attach_CNP()
            'attach our render buffer textures.
            GL.NamedFramebufferDrawBuffers(mainFBO, 3, attach_Color_Normal)
        End Sub

        Public Shared Sub attach_C()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Color)
        End Sub

        Public Shared Sub attach_C_no_Depth()
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, 0, 0)
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Color)
        End Sub

        Public Shared Sub attach_Depth()
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, gDepth, 0)
        End Sub

        Public Shared Sub attach_CF()
            GL.NamedFramebufferDrawBuffers(mainFBO, 2, attach_Color_GMF)
        End Sub

        Public Shared Sub attach_N()
            'This will be used to write to the normals during decal rendering. No depth needed.
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Normal)
        End Sub
    End Class


End Module
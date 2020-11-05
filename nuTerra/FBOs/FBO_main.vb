Imports OpenTK.Graphics.OpenGL

Module FBO_main
    Public mainFBO As Integer

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBOm
        Public Shared SCR_WIDTH, SCR_HEIGHT As Int32
        Public Shared gColor, gNormal, gGMF, gDepth, depthBufferTexture, gPosition, gPick As GLTexture
        Public Shared gAUX_Color As GLTexture
        Public Shared oldWidth As Integer = 1
        Public Shared oldHeigth As Integer = 1
        '========================
        ' Color Attachments
        ' color     = 0
        ' normal    = 1
        ' GMM       = 2
        ' Position  = 3
        ' Pick      = 4
        ' Aux_Color = 5
        '========================
        Private Shared attach_Color_Normal_GMF() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment1,
                                            FramebufferAttachment.ColorAttachment2,
                                            FramebufferAttachment.ColorAttachment3,
                                            FramebufferAttachment.ColorAttachment4
                                            }
        Private Shared attach_Color_Normal_GMF_aux() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment1,
                                            FramebufferAttachment.ColorAttachment2,
                                            FramebufferAttachment.ColorAttachment3,
                                            FramebufferAttachment.ColorAttachment4,
                                            FramebufferAttachment.ColorAttachment5
                                            }
        Private Shared attach_Normal_GMF_aux() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment1,
                                            FramebufferAttachment.ColorAttachment2,
                                            FramebufferAttachment.ColorAttachment3,
                                            FramebufferAttachment.ColorAttachment4,
                                            FramebufferAttachment.ColorAttachment5
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
        Private Shared attach_gPick() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment4
                                            }
        Private Shared attach_gAux_Color() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment5
                                            }
        Private Shared attach_gColor_and_gAux_Color() As DrawBuffersEnum = {
                                            FramebufferAttachment.ColorAttachment0,
                                            FramebufferAttachment.ColorAttachment5
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
            'make_test_texture()
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            ' as the name says
            If gColor IsNot Nothing Then gColor.Delete()
            If gAUX_Color IsNot Nothing Then gAUX_Color.Delete()
            If gNormal IsNot Nothing Then gNormal.Delete()
            If gGMF IsNot Nothing Then gGMF.Delete()
            If gDepth IsNot Nothing Then gDepth.Delete()
            If gPick IsNot Nothing Then gPick.Delete()
            If gPosition IsNot Nothing Then gPosition.Delete()
            If mainFBO > 0 Then GL.DeleteFramebuffer(mainFBO)
            If depthBufferTexture IsNot Nothing Then depthBufferTexture.Delete()
        End Sub

        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            ' 4 color int : RGB and alpha
            gColor = CreateTexture(TextureTarget.Texture2D, "gColor")
            gColor.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gColor.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gColor.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gColor.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gColor.Storage2D(1, SizedInternalFormat.Rgba8, SCR_WIDTH, SCR_HEIGHT)

            ' AUX_gColor -----------------------------------------------------------------------------------
            ' 4 color int : RGB and alpha
            gAUX_Color = CreateTexture(TextureTarget.Texture2D, "AUX_gColor")
            gAUX_Color.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gAUX_Color.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gAUX_Color.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gAUX_Color.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gAUX_Color.Storage2D(1, SizedInternalFormat.Rgba8, SCR_WIDTH, SCR_HEIGHT)

            ' gNormal ------------------------------------------------------------------------------------------
            ' 3 color 16f : normal in RGB
            gNormal = CreateTexture(TextureTarget.Texture2D, "gNormal")
            gNormal.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gNormal.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gNormal.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gNormal.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gNormal.Storage2D(1, DirectCast(InternalFormat.Rgb16f, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gGM_Flag ------------------------------------------------------------------------------------------
            ' 4 color int : GM in RG : Flag in b : Wetness in a
            gGMF = CreateTexture(TextureTarget.Texture2D, "gGMF")
            gGMF.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gGMF.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gGMF.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gGMF.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gGMF.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gPosition ------------------------------------------------------------------------------------------
            ' RGB16F
            gPosition = CreateTexture(TextureTarget.Texture2D, "gPosition")
            gPosition.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gPosition.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gPosition.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gPosition.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gPosition.Storage2D(1, DirectCast(InternalFormat.Rgb16f, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gDepth ------------------------------------------------------------------------------------------
            ' DepthComponent24
            gDepth = CreateTexture(TextureTarget.Texture2D, "gDepth")
            gDepth.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gDepth.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gDepth.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gDepth.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gDepth.Storage2D(1, DirectCast(PixelInternalFormat.DepthComponent24, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)

            ' gPick ------------------------------------------------------------------------------------------
            ' R16 uInt
            gPick = CreateTexture(TextureTarget.Texture2D, "gPick")
            gPick.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gPick.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gPick.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gPick.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gPick.Storage2D(1, DirectCast(PixelInternalFormat.R16ui, SizedInternalFormat), SCR_WIDTH, SCR_HEIGHT)
            Dim er = GL.GetError
        End Sub

        Public Shared Function create_fbo() As Boolean

            Dim maxColorAttachments As Integer
            GL.GetInteger(GetPName.MaxColorAttachments, maxColorAttachments)

            mainFBO = CreateFramebuffer("mainFBO")

            ' attach our render buffer textures.
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment1, gNormal.texture_id, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment2, gGMF.texture_id, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment3, gPosition.texture_id, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment4, gPick.texture_id, 0)
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.ColorAttachment5, gAUX_Color.texture_id, 0)

            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, gDepth.texture_id, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(mainFBO, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If
            attach_CNGP()
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
            GL.NamedFramebufferDrawBuffers(mainFBO, 5, attach_Color_Normal_GMF)
        End Sub

        Public Shared Sub attach_CNGPA()
            'attach our render buffer textures.
            GL.NamedFramebufferDrawBuffers(mainFBO, 6, attach_Color_Normal_GMF_aux)
        End Sub
        Public Shared Sub attach_NGPA()
            'attach our render buffer textures.
            GL.NamedFramebufferDrawBuffers(mainFBO, 5, attach_Normal_GMF_aux)
        End Sub

        Public Shared Sub attach_CNP()
            'attach our render buffer textures.
            GL.NamedFramebufferDrawBuffers(mainFBO, 3, attach_Color_Normal)
        End Sub

        Public Shared Sub attach_C()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Color)
        End Sub
        Public Shared Sub attach_AUX()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_gAux_Color)
        End Sub

        Public Shared Sub attach_C_no_Depth()
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, 0, 0)
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Color)
        End Sub

        Public Shared Sub attach_Depth()
            GL.NamedFramebufferTexture(mainFBO, FramebufferAttachment.DepthAttachment, gDepth.texture_id, 0)
        End Sub

        Public Shared Sub attach_CF()
            GL.NamedFramebufferDrawBuffers(mainFBO, 2, attach_Color_GMF)
        End Sub

        Public Shared Sub attach_Pick()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_gPick)
        End Sub

        Public Shared Sub attach_C_and_Aux()
            GL.NamedFramebufferDrawBuffers(mainFBO, 2, attach_gColor_and_gAux_Color)
        End Sub

        Public Shared Sub attach_N()
            'This will be used to write to the normals during decal rendering. No depth needed.
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, attach_Normal)
        End Sub
    End Class


End Module
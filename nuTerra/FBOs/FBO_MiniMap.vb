Imports OpenTK.Graphics.OpenGL

Module FBO_MiniMap
    Public miniFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBOmini
        Public Shared mini_size As Int32
        Private Shared old_mini_size As Integer = 1
        Public Shared gColor, screenTexture As GLTexture
        Public Shared at_both() As DrawBuffersEnum = {
                                 FramebufferAttachment.ColorAttachment0,
                                 FramebufferAttachment.ColorAttachment1
                                 }
        Public Shared at_gColor() As DrawBuffersEnum = {
                                 FramebufferAttachment.ColorAttachment0
                                 }
        Public Shared at_screenTexture() As DrawBuffersEnum = {
                                 FramebufferAttachment.ColorAttachment1
                                 }

        Public Shared Sub FBO_Initialize(ByVal size As Integer)
            mini_size = size
            frmMain.glControl_main.MakeCurrent()

            ' Stop changing the size becuase of excessive window resize calls.
            If mini_size <> old_mini_size Then

                delete_textures_and_fbo()

                create_textures()

                If Not create_fbo() Then
                    MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                    End
                End If
                'set new size
                old_mini_size = mini_size
                'reset the size of the text header on the page

            End If
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gColor IsNot Nothing Then gColor.Delete()
            If screenTexture IsNot Nothing Then screenTexture.Delete()
            If miniFBO > 0 Then
                GL.DeleteFramebuffer(miniFBO)
            End If
        End Sub
        Public Shared Sub attach_both()
            GL.NamedFramebufferDrawBuffers(mainFBO, 2, at_both)

        End Sub
        Public Shared Sub attach_gcolor()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, at_gColor)

        End Sub
        Public Shared Sub attach_screenTexture()
            GL.NamedFramebufferDrawBuffers(mainFBO, 1, at_screenTexture)
        End Sub
        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            '4 color int : RGB and alpha
            gColor = CreateTexture(TextureTarget.Texture2D, "gColor")
            gColor.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            gColor.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            gColor.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gColor.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gColor.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat), mini_size, mini_size)
            ' gColor2 ------------------------------------------------------------------------------------------
            '4 color int : RGB and alpha
            screenTexture = CreateTexture(TextureTarget.Texture2D, "screenTexture")
            screenTexture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            screenTexture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            screenTexture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            screenTexture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            screenTexture.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat), mini_size, mini_size)
        End Sub

        Public Shared Sub blit_to_screenTexture()
            GL.NamedFramebufferReadBuffer(miniFBO, ReadBufferMode.ColorAttachment0)
            GL.NamedFramebufferDrawBuffer(miniFBO, DrawBufferMode.ColorAttachment1)
            GL.BlitNamedFramebuffer(miniFBO, miniFBO,
                                    0, 0, mini_size, mini_size,
                                    0, 0, mini_size, mini_size,
                                    ClearBufferMask.ColorBufferBit,
                                    BlitFramebufferFilter.Nearest)
        End Sub

        Public Shared Sub blit_to_gBuffer()
            GL.NamedFramebufferReadBuffer(miniFBO, ReadBufferMode.ColorAttachment1)
            GL.NamedFramebufferDrawBuffer(miniFBO, DrawBufferMode.ColorAttachment0)
            GL.BlitNamedFramebuffer(miniFBO, miniFBO,
                                    0, 0, mini_size, mini_size,
                                    0, 0, mini_size, mini_size,
                                    ClearBufferMask.ColorBufferBit,
                                    BlitFramebufferFilter.Nearest)
        End Sub

        Public Shared Function create_fbo() As Boolean
            miniFBO = CreateFramebuffer("miniFBO")
            'attach our render buffer textures.

            GL.NamedFramebufferTexture(miniFBO, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)
            GL.NamedFramebufferTexture(miniFBO, FramebufferAttachment.ColorAttachment1, screenTexture.texture_id, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(miniFBO, FramebufferTarget.Framebuffer)
            attach_gcolor()

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function
    End Class


End Module
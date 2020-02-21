Imports OpenTK.Graphics.OpenGL

Module FBO_MiniMap
    Public miniFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBOmini
        Public Shared mini_size As Int32
        Private Shared old_mini_size As Integer = 1
        Public Shared gColor As Integer

        Private Shared attach_Color() As Integer = {
                                    FramebufferAttachment.ColorAttachment0
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
            If gColor > 0 Then
                GL.DeleteTexture(gColor)
            End If
            If miniFBO > 0 Then
                GL.DeleteFramebuffer(miniFBO)
            End If

            GL.Finish() '<-- Make sure they are gone!
        End Sub

        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            '4 color int : RGB and alpha
            Dim er0 = GL.GetError
            gColor = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, gColor)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, mini_size, mini_size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)

        End Sub

        Public Shared Function create_fbo() As Boolean

            'creat the FBO
            miniFBO = GL.GenFramebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, miniFBO)
            Dim er0 = GL.GetError


            'attach our render buffer textures.

            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, gColor, 0)

            attach_C()

            Dim FBOHealth = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            'set buffer target to default.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

            Return True ' No errors! all is good! :)
        End Function


        Public Shared Sub attach_C()
            GL.DrawBuffers(1, attach_Color)
        End Sub

    End Class


End Module
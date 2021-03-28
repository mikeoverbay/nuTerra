Imports OpenTK.Graphics.OpenGL

Module FBO_MiniMap
    Public miniFBO As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBOmini
        Public Shared mini_size As Int32
        Private Shared old_mini_size As Integer = 1
        Public Shared gColor As GLTexture

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
            If miniFBO > 0 Then
                GL.DeleteFramebuffer(miniFBO)
            End If
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
        End Sub

        Public Shared Function create_fbo() As Boolean
            miniFBO = CreateFramebuffer("miniFBO")
            'attach our render buffer textures.

            GL.NamedFramebufferTexture(miniFBO, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(miniFBO, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function
    End Class


End Module
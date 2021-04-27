Imports OpenTK.Graphics.OpenGL
Imports OpenTK

Module FBO_Mixer
    Public FBO_Mixer_ID As Integer = 0

    ' This is used to prerender the terrain.
    ' The gGmmArray is only here for future use. Decals if there are rendered during at this time.

    ''' <summary>
    ''' Creates the mix FBO
    ''' </summary>
    Public NotInheritable Class FBO_mixer_set
        Public Shared gColor, gNormal, gSpecular As GLTexture
        Private Shared width As Integer
        Private Shared height As Integer

        Public Shared Sub FBO_Initialize(_width As Integer, _height As Integer)
            width = _width
            height = _height

            frmMain.glControl_main.MakeCurrent()

            delete_textures_and_fbo()
            create_arraytextures()

            If Not create_fbo() Then
                MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                End
            End If

        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gColor IsNot Nothing Then gColor.Delete()
            If gNormal IsNot Nothing Then gNormal.Delete()
            If gSpecular IsNot Nothing Then gSpecular.Delete()
            If FBO_Mixer_ID > 0 Then GL.DeleteFramebuffer(FBO_Mixer_ID)
        End Sub

        Public Shared Sub create_arraytextures()
            ' gColor
            gColor = CreateTexture(TextureTarget.Texture2D, "FBO_mixer_gColor")
            gColor.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gColor.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gColor.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

            ' gNormal
            gNormal = CreateTexture(TextureTarget.Texture2D, "FBO_mixer_gNormal")
            gNormal.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gNormal.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gNormal.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

            ' gSpecular
            gSpecular = CreateTexture(TextureTarget.Texture2D, "FBO_mixer_gSpecular")
            gSpecular.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gSpecular.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gSpecular.Storage2D(1, SizedInternalFormat.R8, width, height)
        End Sub

        Public Shared Function create_fbo() As Boolean
            FBO_Mixer_ID = CreateFramebuffer("Mixer")

            'attach our textureArray to colorAttachment0, mip 0 and level 0
            GL.NamedFramebufferTexture(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment1, gNormal.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment2, gSpecular.texture_id, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_Mixer_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Dim bufs() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0, FramebufferAttachment.ColorAttachment1, FramebufferAttachment.ColorAttachment2}
            GL.NamedFramebufferDrawBuffers(FBO_Mixer_ID, 3, bufs)

            Return True
        End Function

    End Class

End Module

Imports OpenTK.Graphics.OpenGL4

Public Class VTMixerFBO
    Public Shared fbo As GLFramebuffer
    Public Shared ColorTex As GLTexture
    Public Shared NormalTex As GLTexture
    Public Shared SpecularTex As GLTexture
    Private Shared width As Integer
    Private Shared height As Integer

    Public Shared Sub FBO_Initialize(_width As Integer, _height As Integer)
        width = _width
        height = _height

        delete_textures_and_fbo()
        create_textures()

        If Not create_fbo() Then
            MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
            End
        End If
    End Sub

    Public Shared Sub delete_textures_and_fbo()
        'as the name says
        ColorTex?.Dispose()
        NormalTex?.Dispose()
        SpecularTex?.Dispose()
        fbo?.Dispose()
    End Sub

    Public Shared Sub create_textures()
        ColorTex = GLTexture.Create(TextureTarget.Texture2D, "VTMixerFBO_ColorTex")
        ColorTex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        ColorTex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        ColorTex.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

        NormalTex = GLTexture.Create(TextureTarget.Texture2D, "VTMixerFBO_NormalTex")
        NormalTex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        NormalTex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        NormalTex.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

        SpecularTex = GLTexture.Create(TextureTarget.Texture2D, "VTMixerFBO_SpecularTex")
        SpecularTex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        SpecularTex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        SpecularTex.Storage2D(1, SizedInternalFormat.R8, width, height)
    End Sub

    Public Shared Function create_fbo() As Boolean
        fbo = GLFramebuffer.Create("VTMixerFBO")

        fbo.Texture(FramebufferAttachment.ColorAttachment0, ColorTex, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment1, NormalTex, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment2, SpecularTex, 0)

        If Not fbo.IsComplete Then
            Return False
        End If

        Dim attachments() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0, FramebufferAttachment.ColorAttachment1, FramebufferAttachment.ColorAttachment2}
        fbo.DrawBuffers(3, attachments)
        Return True
    End Function
End Class

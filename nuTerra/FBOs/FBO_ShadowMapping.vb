Imports OpenTK.Graphics.OpenGL4

Public Class ShadowMappingFBO
    Public Shared fbo As GLFramebuffer
    Public Shared depth_tex As GLTexture

    Public Const WIDTH = 4096
    Public Const HEIGHT = 2048

    Public Shared ORTHO_WIDTH As Single = 800.0F
    Public Shared ORTHO_HEIGHT As Single = 400.0F
    Public Shared NEAR As Single = 300.0F
    Public Shared FAR As Single = 1400.0F
    Public Shared ENABLED As Boolean = False

    Public Shared Sub FBO_Initialize()
        frmMain.glControl_main.MakeCurrent()

        create_textures()

        If Not create_fbo() Then
            MsgBox("Failed to create ShadowMapping FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
            End
        End If
    End Sub

    Public Shared Sub create_textures()
        depth_tex = GLTexture.Create(TextureTarget.Texture2D, "depth_tex")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureCompareMode, TextureCompareMode.CompareRefToTexture)
        depth_tex.Parameter(TextureParameterName.TextureCompareFunc, DepthFunction.Lequal)
        depth_tex.Storage2D(1, DirectCast(PixelInternalFormat.DepthComponent32f, SizedInternalFormat), WIDTH, HEIGHT)
    End Sub

    Public Shared Function create_fbo() As Boolean
        fbo = GLFramebuffer.Create("ShadowMappingFBO")
        fbo.Texture(FramebufferAttachment.DepthAttachment, depth_tex, 0)

        If Not fbo.IsComplete Then
            Return False
        End If

        Return True ' No errors! all is good! :)
    End Function
End Class

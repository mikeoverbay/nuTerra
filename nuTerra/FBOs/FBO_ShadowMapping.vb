Imports OpenTK.Graphics.OpenGL4

Public Class ShadowMappingFBO
    Public Shared fbo As GLFramebuffer
    Public Shared depth_tex As GLTexture

    Public Const CASCADES = 4
    '''<summary>
    ''' 4096 square per cascade. Four of them at 32 bit depth is 268 MB, against
    ''' 67 MB at 2048 - affordable here, but it also quadruples the pixels the
    ''' shadow pass has to rasterise, which is why FRAME_STEP matters below.
    '''</summary>
    Public Const WIDTH = 4096
    Public Const HEIGHT = 4096

    '''<summary>
    ''' Frames between shadow map updates. This was briefly 1, on the strength of
    ''' a benchmark taken from an empty overhead view where it looked free; from
    ''' a real viewpoint it is not, and at 4096 it is four times less free.
    '''</summary>
    Public Shared FRAME_STEP As Integer = 4

    Public Shared Property Enabled As Boolean
        Get
            Return CommonProperties.USE_SHADOW_MAPPING
        End Get
        Set(value As Boolean)
            If CommonProperties.USE_SHADOW_MAPPING <> value Then
                CommonProperties.USE_SHADOW_MAPPING = value
                CommonProperties.update()
            End If
        End Set
    End Property

    Public Shared Sub FBO_Initialize()
        create_textures()

        If Not create_fbo() Then
            MsgBox("Failed to create ShadowMapping FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
            End
        End If
    End Sub

    Public Shared Sub create_textures()
        depth_tex = GLTexture.Create(TextureTarget.Texture2DArray, "depth_tex")
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureCompareMode, TextureCompareMode.CompareRefToTexture)
        depth_tex.Parameter(TextureParameterName.TextureCompareFunc, DepthFunction.Greater)
        depth_tex.Storage3D(1, DirectCast(PixelInternalFormat.DepthComponent32f, SizedInternalFormat), WIDTH, HEIGHT, CASCADES)
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

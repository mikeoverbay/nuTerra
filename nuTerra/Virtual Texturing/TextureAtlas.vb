Imports OpenTK.Graphics.OpenGL

Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo
    Public texture As GLTexture
    Public atlascount As Integer

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info
        Me.atlascount = atlascount

        If atlascount * atlascount > 2048 Then
            LogThis("NO!")
            Stop
        End If

        texture = CreateTexture(TextureTarget.Texture2DArray, "TextureAtlas")
        texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        texture.Storage3D(1, SizedInternalFormat.Rgba8, info.TileSize, info.TileSize, atlascount * atlascount)
    End Sub

	Public Sub uploadPage(pt As Point, data As Byte())
        GL.NamedFramebufferReadBuffer(FBO_Mixer_ID, ReadBufferMode.ColorAttachment0)
        GL.CopyTextureSubImage3D(texture.texture_id, 0, 0, 0, pt.X + pt.Y * atlascount, 0, 0, info.TileSize, info.TileSize)
    End Sub

	Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub
End Class

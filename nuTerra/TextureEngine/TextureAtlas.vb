Imports OpenTK.Graphics.OpenGL

Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo
    Public texture As GLTexture

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info

        texture = CreateTexture(TextureTarget.ProxyTexture2D, "TextureAtlas")
        texture.Storage2D(1, SizedInternalFormat.Rgba8, atlascount * info.PageSize, atlascount * info.PageSize)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub
End Class

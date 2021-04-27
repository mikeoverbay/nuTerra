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
        texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, atlascount * atlascount)
    End Sub

    Public Sub uploadPage(index As Integer, data As Byte())
        texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, data.Length, data)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub
End Class

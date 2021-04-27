Imports OpenTK.Graphics.OpenGL

Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo
    Public atlascount As Integer

    Public color_texture As GLTexture
    Public normal_texture As GLTexture

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info
        Me.atlascount = atlascount

        If atlascount * atlascount > 2048 Then
            LogThis("NO!")
            Stop
        End If

        color_texture = CreateTexture(TextureTarget.Texture2DArray, "ColorTextureAtlas")
        color_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, atlascount * atlascount)

        normal_texture = CreateTexture(TextureTarget.Texture2DArray, "NormalTextureAtlas")
        normal_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        normal_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        normal_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, atlascount * atlascount)
    End Sub

    Public Sub uploadPage(index As Integer, color_data As Byte(), normal_data As Byte())
        color_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, color_data.Length, color_data)
        normal_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, normal_data.Length, normal_data)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        color_texture.Delete()
        normal_texture.Delete()
    End Sub
End Class

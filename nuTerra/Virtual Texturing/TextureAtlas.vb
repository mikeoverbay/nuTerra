Imports OpenTK.Graphics.OpenGL

Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo
    Public atlascount As Integer

    Public color_texture As GLTexture
    Public normal_texture As GLTexture
    Public specular_texture As GLTexture

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info
        Me.atlascount = atlascount

        If atlascount * atlascount > 2048 Then
            LogThis("NO!")
            Stop
        End If

        ' RGB for albedo, A for wetness
        color_texture = CreateTexture(TextureTarget.Texture2DArray, "ColorTextureAtlas")
        color_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, atlascount * atlascount)

        ' RGB for normal, A for tessellation
        normal_texture = CreateTexture(TextureTarget.Texture2DArray, "NormalTextureAtlas")
        normal_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        normal_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        normal_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, atlascount * atlascount)

        specular_texture = CreateTexture(TextureTarget.Texture2DArray, "NormalTextureAtlas")
        specular_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        specular_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        specular_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        specular_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        specular_texture.Storage3D(1, SizedInternalFormat.R8, info.TileSize, info.TileSize, atlascount * atlascount)
    End Sub

    Public Sub uploadPage(index As Integer, color_data As Byte(), normal_data As Byte(), specular_data As Byte())
        color_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, color_data.Length, color_data)
        normal_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, normal_data.Length, normal_data)
        GL.TextureSubImage3D(specular_texture.texture_id, 0, 0, 0, index, info.TileSize, info.TileSize, 1, PixelFormat.Red, PixelType.Byte, specular_data)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        color_texture.Delete()
        normal_texture.Delete()
        specular_texture.Delete()
    End Sub
End Class

Imports OpenTK.Graphics.OpenGL4

Public Class TextureAtlas
    Implements IDisposable

    Private info As VirtualTextureInfo

    Public color_texture As GLTexture
    Public normal_texture As GLTexture
    Public specular_texture As GLTexture

    Public Sub New(info As VirtualTextureInfo, num_tiles As Integer)
        Me.info = info

        If num_tiles > 2048 Then
            LogThis("Texture2DArray doesn't support depth greater than 2048!")
            Stop
        End If

        ' DXT5 RGBA
        ' RGB for albedo, A for wetness
        color_texture = GLTexture.Create(TextureTarget.Texture2DArray, "ColorTextureAtlas")
        color_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, num_tiles)

        ' DXT5 RGBA
        ' RGB for normal, A for tessellation
        normal_texture = GLTexture.Create(TextureTarget.Texture2DArray, "NormalTextureAtlas")
        normal_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        normal_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        normal_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        normal_texture.Storage3D(1, InternalFormat.CompressedRgbaS3tcDxt5Ext, info.TileSize, info.TileSize, num_tiles)

        ' R8 w/o compression
        ' R for specular
        specular_texture = GLTexture.Create(TextureTarget.Texture2DArray, "SpecularTextureAtlas")
        specular_texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        specular_texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        specular_texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        specular_texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        specular_texture.Storage3D(1, SizedInternalFormat.R8, info.TileSize, info.TileSize, num_tiles)
    End Sub

    Public Sub uploadPage(index As Integer, color_data As Byte(), normal_data As Byte(), specular_data As Byte())
        ' UPLOAD TILES TO GPU (SHOULD WE USE PBO FOR ASYNC UPLOADING?)
        color_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, color_data.Length, color_data)
        normal_texture.CompressedSubImage3D(0, 0, 0, index, info.TileSize, info.TileSize, 1, InternalFormat.CompressedRgbaS3tcDxt5Ext, normal_data.Length, normal_data)
        GL.TextureSubImage3D(specular_texture.texture_id, 0, 0, 0, index, info.TileSize, info.TileSize, 1, PixelFormat.Red, PixelType.Byte, specular_data)

        ' MIP GENERATION (DISABLED NOW, DO WE NEED IT?)
        ' IT CAN BE USEFUL FOR TRILINEAR FILTERING
#If False Then
        ' GENERATE MIP FOR COLOR TILE
        Dim textureView = GL.GenTexture()
        GL.TextureView(textureView, TextureTarget.Texture2D, color_texture.texture_id, PixelInternalFormat.CompressedRgbaS3tcDxt5Ext, 0, 2, index, 1)
        GL.GenerateTextureMipmap(textureView)
        GL.DeleteTexture(textureView)

        ' GENERATE MIP FOR NORMAL TILE
        textureView = GL.GenTexture()
        GL.TextureView(textureView, TextureTarget.Texture2D, normal_texture.texture_id, PixelInternalFormat.CompressedRgbaS3tcDxt5Ext, 0, 2, index, 1)
        GL.GenerateTextureMipmap(textureView)
        GL.DeleteTexture(textureView)

        ' GENERATE MIP FOR SPECULAR TILE
        textureView = GL.GenTexture()
        GL.TextureView(textureView, TextureTarget.Texture2D, specular_texture.texture_id, PixelInternalFormat.R8, 0, 2, index, 1)
        GL.GenerateTextureMipmap(textureView)
        GL.DeleteTexture(textureView)
#End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        color_texture?.Dispose()
        normal_texture?.Dispose()
        specular_texture?.Dispose()
    End Sub
End Class

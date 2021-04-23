Imports OpenTK.Graphics.OpenGL

Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo
    Public texture As GLTexture

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info

        texture = CreateTexture(TextureTarget.Texture2D, "TextureAtlas")
        texture.Storage2D(1, SizedInternalFormat.Rgba8, atlascount * info.PageSize, atlascount * info.PageSize)
    End Sub

	Public Sub uploadPage(pt As Point, data As Byte())
        ' Copy the texture part to the actual atlas texture
        Dim pagesize = info.PageSize
        Dim xpos = pt.X * pagesize
        Dim ypos = pt.Y * pagesize
        texture.SubImage2D(0, xpos, ypos, pagesize, pagesize, PixelFormat.Rgba, PixelType.UnsignedByte, data)
    End Sub

	Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub
End Class

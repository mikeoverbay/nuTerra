Imports System.IO
Imports System.Text
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Just enough DDS to get a DXT1/DXT3/DXT5 texture onto the GPU. Mip chains in the
''' file are ignored; we upload level 0 and let the driver generate the rest.
''' </summary>
Public Module DdsLoader

    Public Function FromBytes(d() As Byte, label As String) As Integer
        If d Is Nothing OrElse d.Length < 128 Then Return 0
        If Encoding.ASCII.GetString(d, 0, 4) <> "DDS " Then Return 0

        Dim height = BitConverter.ToInt32(d, 12)
        Dim width = BitConverter.ToInt32(d, 16)
        Dim fourCC = Encoding.ASCII.GetString(d, 84, 4)

        Dim fmt As InternalFormat
        Dim blockBytes As Integer
        Select Case fourCC
            Case "DXT1"
                fmt = InternalFormat.CompressedRgbaS3tcDxt1Ext : blockBytes = 8
            Case "DXT3"
                fmt = InternalFormat.CompressedRgbaS3tcDxt3Ext : blockBytes = 16
            Case "DXT5"
                fmt = InternalFormat.CompressedRgbaS3tcDxt5Ext : blockBytes = 16
            Case Else
                Return 0 ' uncompressed and DX10 headers are not needed here
        End Select

        Dim size = ((width + 3) \ 4) * ((height + 3) \ 4) * blockBytes
        If 128 + size > d.Length Then Return 0

        Dim pixels(size - 1) As Byte
        Array.Copy(d, 128, pixels, 0, size)

        Dim tex As Integer
        GL.CreateTextures(TextureTarget.Texture2D, 1, tex)
        Dim levels = 1 + CInt(Math.Floor(Math.Log(Math.Max(width, height), 2)))
        GL.TextureStorage2D(tex, levels, CType(fmt, SizedInternalFormat), width, height)
        GL.CompressedTextureSubImage2D(tex, 0, 0, 0, width, height,
                                       CType(fmt, PixelFormat), size, pixels)
        GL.GenerateTextureMipmap(tex)
        GL.TextureParameter(tex, TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.LinearMipmapLinear))
        GL.TextureParameter(tex, TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Linear))
        GL.TextureParameter(tex, TextureParameterName.TextureWrapS, CInt(TextureWrapMode.Repeat))
        GL.TextureParameter(tex, TextureParameterName.TextureWrapT, CInt(TextureWrapMode.Repeat))
        Return tex
    End Function

    ''' <summary>A 2x2 white texture so untextured draws still show up.</summary>
    Public Function White() As Integer
        Dim tex As Integer
        GL.CreateTextures(TextureTarget.Texture2D, 1, tex)
        GL.TextureStorage2D(tex, 1, SizedInternalFormat.Rgba8, 2, 2)
        Dim px(15) As Byte
        For i = 0 To 15 : px(i) = 255 : Next
        GL.TextureSubImage2D(tex, 0, 0, 0, 2, 2, PixelFormat.Rgba, PixelType.UnsignedByte, px)
        GL.TextureParameter(tex, TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.Nearest))
        GL.TextureParameter(tex, TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Nearest))
        Return tex
    End Function
End Module

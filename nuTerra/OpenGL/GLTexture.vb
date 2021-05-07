Imports OpenTK.Graphics.OpenGL4

Public Class GLTexture
    Public Shared ALL_SIZE As Integer
    Public texture_id As Integer
    Public target As TextureTarget
    Private size As Integer

    Public Shared Function Create(target As TextureTarget, name As String) As GLTexture
        Dim tex_id As Integer
        GL.CreateTextures(target, 1, tex_id)
        LabelObject(ObjectLabelIdentifier.Texture, tex_id, name)
        Dim obj As New GLTexture With {.texture_id = tex_id, .target = target}
        Return obj
    End Function

    Public Sub GenerateMipmap()
        GL.GenerateTextureMipmap(texture_id)
        CheckGLError()
    End Sub

    Public Sub Delete()
        GL.DeleteTexture(texture_id)
        ALL_SIZE -= size
        CheckGLError()
    End Sub

    Public Sub BindUnit(unit As Integer)
        GL.BindTextureUnit(unit, texture_id)
        CheckGLError()
    End Sub

    Public Sub Parameter(pname As TextureParameterName, param As Single)
        GL.TextureParameter(texture_id, pname, param)
        CheckGLError()
    End Sub

    Public Sub Storage2D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer)
        GL.TextureStorage2D(texture_id, levels, iFormat, width, height)
        size = width * height
        ALL_SIZE += size
        CheckGLError()
    End Sub

    Public Sub Storage3D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer, depth As Integer)
        GL.TextureStorage3D(texture_id, levels, iFormat, width, height, depth)
        size = width * height * depth
        ALL_SIZE += size
        CheckGLError()
    End Sub

    Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels() As Byte)
        GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
        CheckGLError()
    End Sub

    Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels As IntPtr)
        GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
        CheckGLError()
    End Sub

    Public Sub CompressedSubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
        GL.CompressedTextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, imageSize, data)
        ' FAILED on lakeville:
        CheckGLError()
    End Sub

    Public Sub CompressedSubImage3D(level As Integer, xoffset As Integer, yoffset As Integer, zoffset As Integer, width As Integer, height As Integer, depth As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
        GL.CompressedTextureSubImage3D(texture_id, level, xoffset, yoffset, zoffset, width, height, depth, format, imageSize, data)
        CheckGLError()
    End Sub

#If DEBUG Then
    Public ReadOnly Property DEBUG_PARAMS As Dictionary(Of String, String)
        Get
            Dim result As New Dictionary(Of String, String)
            Dim params As New Dictionary(Of GetTextureParameter, System.Type)
            params(GetTextureParameter.TextureWrapS) = GetType(TextureWrapMode)
            params(GetTextureParameter.TextureWrapT) = GetType(TextureWrapMode)
            params(GetTextureParameter.TextureMinFilter) = GetType(TextureMinFilter)
            params(GetTextureParameter.TextureMagFilter) = GetType(TextureMagFilter)
            For Each param In params
                Dim val As Integer
                GL.GetTextureParameter(texture_id, param.Key, val)
                result(param.Key.ToString) = [Enum].Parse(param.Value, val).ToString
            Next
            Return result
        End Get
    End Property
#End If
End Class
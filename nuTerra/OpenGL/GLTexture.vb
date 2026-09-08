Imports OpenTK.Graphics.OpenGL4

Public Class GLTexture
    Implements IDisposable

    Public texture_id As Integer
    Public target As TextureTarget

    ''' <summary>
    ''' What Storage2D/3D actually allocated. Recorded because GL will not tell
    ''' us cheaply and because glTextureView needs it: a view MUST name a format
    ''' in the source's own class, and a caller passing that by hand gets it
    ''' wrong the moment the source's format changes under it. Ask the texture.
    ''' </summary>
    Public storage_format As SizedInternalFormat

    Public Sub New(texture_id As Integer, target As TextureTarget, name As String)
        Me.texture_id = texture_id
        Me.target = target
        LabelObject(ObjectLabelIdentifier.Texture, texture_id, name)
    End Sub

    Public Shared Function Create(target As TextureTarget, name As String) As GLTexture
        Dim tex_id As Integer
        GL.CreateTextures(target, 1, tex_id)
        If tex_id <> 0 Then
            Return New GLTexture(tex_id, target, name)
        End If
        Return Nothing
    End Function

    Public Sub GenerateMipmap()
        GL.GenerateTextureMipmap(texture_id)
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
        storage_format = iFormat
        GL.TextureStorage2D(texture_id, levels, iFormat, width, height)
        CheckGLError()
    End Sub

    Public Sub Storage3D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer, depth As Integer)
        storage_format = iFormat
        GL.TextureStorage3D(texture_id, levels, iFormat, width, height, depth)
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

    Public Sub SubImage3D(level As Integer, xoffset As Integer, yoffset As Integer, zoffset As Integer, width As Integer, height As Integer, depth As Integer, format As PixelFormat, type As PixelType, pixels() As Byte)
        GL.TextureSubImage3D(texture_id, level, xoffset, yoffset, zoffset, width, height, depth, format, type, pixels)
        CheckGLError()
    End Sub

    Public Sub CompressedSubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
        GL.CompressedTextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, imageSize, data)
        CheckGLError()
    End Sub

    Public Sub CompressedSubImage3D(level As Integer, xoffset As Integer, yoffset As Integer, zoffset As Integer, width As Integer, height As Integer, depth As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
        GL.CompressedTextureSubImage3D(texture_id, level, xoffset, yoffset, zoffset, width, height, depth, format, imageSize, data)
        CheckGLError()
    End Sub

    Public Sub CompressedSubImage3D(level As Integer, xoffset As Integer, yoffset As Integer, zoffset As Integer, width As Integer, height As Integer, depth As Integer, format As PixelFormat, imageSize As Integer, data As IntPtr)
        GL.CompressedTextureSubImage3D(texture_id, level, xoffset, yoffset, zoffset, width, height, depth, format, imageSize, data)
        CheckGLError()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim imgHandle = GL.Arb.GetTextureHandle(texture_id)
        If imgHandle > 0 Then
            If GL.Arb.IsTextureHandleResident(imgHandle) Then
                GL.Arb.MakeTextureHandleNonResident(imgHandle)
            End If
        End If
        GL.DeleteTexture(texture_id)
        CheckGLError()
    End Sub
End Class
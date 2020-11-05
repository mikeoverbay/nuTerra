Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL

#Const WITHOUT_DSA = False

Module modOpenGLAliases

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub TextureSubImage2D(target As TextureTarget, tex_id As Integer, level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels As IntPtr)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels)
        GL.BindTexture(target, 0)
#Else
        GL.TextureSubImage2D(tex_id, level, xoffset, yoffset, width, height, format, type, pixels)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub TextureSubImage2D(target As TextureTarget, tex_id As Integer, level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels() As Byte)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels)
        GL.BindTexture(target, 0)
#Else
        GL.TextureSubImage2D(tex_id, level, xoffset, yoffset, width, height, format, type, pixels)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub CompressedTextureSubImage2D(target As TextureTarget, tex_id As Integer, level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.CompressedTexSubImage2D(target, level, xoffset, yoffset, width, height, format, imageSize, data)
        GL.BindTexture(target, 0)
#Else
        GL.CompressedTextureSubImage2D(tex_id, level, xoffset, yoffset, width, height, format, imageSize, data)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub GenerateTextureMipmap(target As TextureTarget, tex_id As Integer)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.GenerateMipmap(target)
        GL.BindTexture(target, 0)
#Else
        GL.GenerateTextureMipmap(tex_id)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub TextureStorage2D(target As TextureTarget, tex_id As Integer, levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.TexStorage2D(target, levels, iFormat, width, height)
        GL.BindTexture(target, 0)
#Else
        GL.TextureStorage2D(tex_id, levels, iFormat, width, height)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub TextureParameter(target As TextureTarget, tex_id As Integer, pname As TextureParameterName, param As Single)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.TexParameter(target, pname, param)
        GL.BindTexture(target, 0)
#Else
        GL.TextureParameter(tex_id, pname, param)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub TextureParameter(target As TextureTarget, tex_id As Integer, pname As TextureParameterName, param As Integer)
#If WITHOUT_DSA Then
        GL.BindTexture(target, tex_id)
        GL.TexParameter(target, pname, param)
        GL.BindTexture(target, 0)
#Else
        GL.TextureParameter(tex_id, pname, param)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(target As BufferTarget, buffer As Integer, size As Integer, data() As dataType, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(target, buffer)
        ' GL 4.4+
        GL.BufferStorage(target, size, data, flags)
        GL.BindBuffer(target, 0)
#Else
        GL.NamedBufferStorage(buffer, size, data, flags)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(target As BufferTarget, buffer As Integer, size As Integer, data As dataType, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(target, buffer)
        ' GL 4.4+
        GL.BufferStorage(target, size, data, flags)
        GL.BindBuffer(target, 0)
#Else
        GL.NamedBufferStorage(buffer, size, data, flags)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorageNullData(target As BufferTarget, buffer As Integer, size As Integer, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(target, buffer)
        ' GL 4.4+
        GL.BufferStorage(target, size, IntPtr.Zero, flags)
        GL.BindBuffer(target, 0)
#Else
        GL.NamedBufferStorage(buffer, size, IntPtr.Zero, flags)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateVertexArray(name As String) As Integer
        Dim va_id As Integer
#If WITHOUT_DSA Then
        va_id = GL.GenVertexArray()
        GL.BindVertexArray(va_id)
        GL.BindVertexArray(0)
#Else
        GL.CreateVertexArrays(1, va_id)
#End If
        LabelObject(ObjectLabelIdentifier.VertexArray, va_id, name)
        Return va_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateBuffer(target As BufferTarget, name As String) As Integer
        Dim buf_id As Integer
#If WITHOUT_DSA Then
        buf_id = GL.GenBuffer()
        GL.BindBuffer(target, buf_id)
        GL.BindBuffer(target, 0)
#Else
        GL.CreateBuffers(1, buf_id)
#End If
        LabelObject(ObjectLabelIdentifier.Buffer, buf_id, name)
        Return buf_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateTexture(target As TextureTarget, name As String) As Integer
        Dim tex_id As Integer
#If WITHOUT_DSA Then
        tex_id = GL.GenTexture()
        GL.BindTexture(target, tex_id)
        GL.BindTexture(target, 0)
#Else
        GL.CreateTextures(target, 1, tex_id)
#End If
        LabelObject(ObjectLabelIdentifier.Texture, tex_id, name)
        Return tex_id
    End Function

    <Conditional("DEBUG")>
    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub LabelObject(objLabelIdent As ObjectLabelIdentifier, glObject As Integer, name As String)
        GL.ObjectLabel(objLabelIdent, glObject, name.Length, name)
    End Sub

End Module

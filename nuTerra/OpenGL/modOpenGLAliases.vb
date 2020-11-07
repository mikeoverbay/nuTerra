Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL

#Const WITHOUT_DSA = False

Public Module modOpenGLAliases
    Public Const GL_PARAMETER_BUFFER_ARB = DirectCast(33006, BufferTarget)

    Public Class GLBuffer
        Public buffer_id As Integer
        Public target As BufferTarget

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub BindBase(base As Integer)
            GL.BindBufferBase(DirectCast(target, BufferRangeTarget), base, buffer_id)
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Bind(bind_target As BufferTarget)
            GL.BindBuffer(bind_target, buffer_id)
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Delete()
            GL.DeleteBuffer(buffer_id)
            GL.Finish()
        End Sub
    End Class

    Public Class GLTexture
        Public texture_id As Integer
        Public target As TextureTarget

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub GenerateMipmap()
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.GenerateMipmap(target)
            GL.BindTexture(target, 0)
#Else
            GL.GenerateTextureMipmap(texture_id)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Delete()
            GL.DeleteTexture(texture_id)
            GL.Finish()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub BindUnit(unit As Integer)
#If WITHOUT_DSA Then
            GL.ActiveTexture(TextureUnit.Texture0 + unit)
            GL.BindTexture(target, texture_id)
#Else
            GL.BindTextureUnit(unit, texture_id)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Parameter(pname As TextureParameterName, param As Single)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexParameter(target, pname, param)
            GL.BindTexture(target, 0)
#Else
            GL.TextureParameter(texture_id, pname, param)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Parameter(pname As TextureParameterName, param As Integer)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexParameter(target, pname, param)
            GL.BindTexture(target, 0)
#Else
            GL.TextureParameter(texture_id, pname, param)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Storage2D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexStorage2D(target, levels, iFormat, width, height)
            GL.BindTexture(target, 0)
#Else
            GL.TextureStorage2D(texture_id, levels, iFormat, width, height)
#End If
        End Sub

        Public Sub Storage3D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer, depth As Integer)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexStorage3D(target, levels, iFormat, width, height, depth)
            GL.BindTexture(target, 0)
#Else
            GL.TextureStorage3D(texture_id, levels, iFormat, width, height, depth)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels() As Byte)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels)
            GL.BindTexture(target, 0)
#Else
            GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels As IntPtr)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels)
            GL.BindTexture(target, 0)
#Else
            GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub CompressedSubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.CompressedTexSubImage2D(target, level, xoffset, yoffset, width, height, format, imageSize, data)
            GL.BindTexture(target, 0)
#Else
            GL.CompressedTextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, imageSize, data)
#End If
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub CompressedSubImage3D(level As Integer, xoffset As Integer, yoffset As Integer, zoffset As Integer, width As Integer, height As Integer, depth As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
#If WITHOUT_DSA Then
            GL.BindTexture(target, texture_id)
            GL.CompressedTexSubImage3D(target, level, xoffset, yoffset, zoffset, width, height, depth, format, imageSize, data)
            GL.BindTexture(target, 0)
#Else
            GL.CompressedTextureSubImage3D(texture_id, level, xoffset, yoffset, zoffset, width, height, depth, format, imageSize, data)
#End If
        End Sub
    End Class

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateFramebuffer(name As String) As Integer
        Dim fbo_id As Integer
        GL.CreateFramebuffers(1, fbo_id)
        LabelObject(ObjectLabelIdentifier.Framebuffer, fbo_id, name)
        Return fbo_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(buffer As GLBuffer, size As Integer, data() As dataType, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(buffer.target, buffer.buffer_id)
        ' GL 4.4+
        GL.BufferStorage(buffer.target, size, data, flags)
        GL.BindBuffer(buffer.target, 0)
#Else
        GL.NamedBufferStorage(buffer.buffer_id, size, data, flags)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(buffer As GLBuffer, size As Integer, data As dataType, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(buffer.target, buffer.buffer_id)
        ' GL 4.4+
        GL.BufferStorage(buffer.target, size, data, flags)
        GL.BindBuffer(buffer.target, 0)
#Else
        GL.NamedBufferStorage(buffer.buffer_id, size, data, flags)
#End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorageNullData(buffer As GLBuffer, size As Integer, flags As BufferStorageFlags)
#If WITHOUT_DSA Then
        GL.BindBuffer(buffer.target, buffer.buffer_id)
        ' GL 4.4+
        GL.BufferStorage(buffer.target, size, IntPtr.Zero, flags)
        GL.BindBuffer(buffer.target, 0)
#Else
        GL.NamedBufferStorage(buffer.buffer_id, size, IntPtr.Zero, flags)
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
    Public Function CreateBuffer(target As BufferTarget, name As String) As GLBuffer
        Dim buf_id As Integer
#If WITHOUT_DSA Then
        buf_id = GL.GenBuffer()
        GL.BindBuffer(target, buf_id)
        GL.BindBuffer(target, 0)
#Else
        GL.CreateBuffers(1, buf_id)
#End If
        LabelObject(ObjectLabelIdentifier.Buffer, buf_id, name)
        Return New GLBuffer With {.buffer_id = buf_id, .target = target}
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateTexture(target As TextureTarget, name As String) As GLTexture
        Dim tex_id As Integer
#If WITHOUT_DSA Then
        tex_id = GL.GenTexture()
        GL.BindTexture(target, tex_id)
        GL.BindTexture(target, 0)
#Else
        GL.CreateTextures(target, 1, tex_id)
#End If
        LabelObject(ObjectLabelIdentifier.Texture, tex_id, name)
        Return New GLTexture With {.texture_id = tex_id, .target = target}
    End Function

    <Conditional("DEBUG")>
    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub LabelObject(objLabelIdent As ObjectLabelIdentifier, glObject As Integer, name As String)
        GL.ObjectLabel(objLabelIdent, glObject, name.Length, name)
    End Sub

    Public Sub unbind_textures(start As Integer)
        'doing this backwards leaves TEXTURE0 active :)
        For i = start To 0 Step -1
#If WITHOUT_DSA Then
            GL.ActiveTexture(TextureUnit.Texture0 + i)
            GL.BindTexture(TextureTarget.Texture2D, 0)
#Else
            GL.BindTextureUnit(i, 0)
#End If
        Next
    End Sub

End Module

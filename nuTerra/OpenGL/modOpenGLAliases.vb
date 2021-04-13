Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL

Public Module modOpenGLAliases
    Public Const GL_REPRESENTATIVE_FRAGMENT_TEST_NV As EnableCap = 37759

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Private Sub CheckGLError()
#If DEBUG Then
        Dim err_code = GL.GetError
        If err_code > 0 Then
            LogThis("GL Error " + err_code.ToString)
            'Stop
        End If
#End If
    End Sub

    Public Class GLBuffer
        Public buffer_id As Integer
        Public target As BufferTarget

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub BindBase(base As Integer)
            GL.BindBufferBase(DirectCast(target, BufferRangeTarget), base, buffer_id)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Bind(bind_target As BufferTarget)
            GL.BindBuffer(bind_target, buffer_id)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Delete()
            GL.DeleteBuffer(buffer_id)
            CheckGLError()
        End Sub
    End Class

    Public Class GLTexture
        Public texture_id As Integer
        Public target As TextureTarget

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub GenerateMipmap()
            GL.GenerateTextureMipmap(texture_id)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Delete()
            GL.DeleteTexture(texture_id)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub BindUnit(unit As Integer)
            GL.BindTextureUnit(unit, texture_id)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Parameter(pname As TextureParameterName, param As Single)
            GL.TextureParameter(texture_id, pname, param)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Parameter(pname As TextureParameterName, param As Integer)
            GL.TextureParameter(texture_id, pname, param)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub Storage2D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer)
            GL.TextureStorage2D(texture_id, levels, iFormat, width, height)
            CheckGLError()
        End Sub

        Public Sub Storage3D(levels As Integer, iFormat As SizedInternalFormat, width As Integer, height As Integer, depth As Integer)
            GL.TextureStorage3D(texture_id, levels, iFormat, width, height, depth)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels() As Byte)
            GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub SubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, type As PixelType, pixels As IntPtr)
            GL.TextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, type, pixels)
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Sub CompressedSubImage2D(level As Integer, xoffset As Integer, yoffset As Integer, width As Integer, height As Integer, format As PixelFormat, imageSize As Integer, data() As Byte)
            GL.CompressedTextureSubImage2D(texture_id, level, xoffset, yoffset, width, height, format, imageSize, data)
            ' FAILED on lakeville:
            CheckGLError()
        End Sub

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
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

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateFramebuffer(name As String) As Integer
        Dim fbo_id As Integer
        GL.CreateFramebuffers(1, fbo_id)
        LabelObject(ObjectLabelIdentifier.Framebuffer, fbo_id, name)
        Return fbo_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(buffer As GLBuffer, size As Integer, data() As dataType, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer.buffer_id, size, data, flags)
        CheckGLError()
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorage(Of dataType As Structure)(buffer As GLBuffer, size As Integer, data As dataType, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer.buffer_id, size, data, flags)
        CheckGLError()
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub BufferStorageNullData(buffer As GLBuffer, size As Integer, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer.buffer_id, size, IntPtr.Zero, flags)
        CheckGLError()
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateVertexArray(name As String) As Integer
        Dim va_id As Integer
        GL.CreateVertexArrays(1, va_id)
        LabelObject(ObjectLabelIdentifier.VertexArray, va_id, name)
        Return va_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateBuffer(target As BufferTarget, name As String) As GLBuffer
        Dim buf_id As Integer
        GL.CreateBuffers(1, buf_id)
        LabelObject(ObjectLabelIdentifier.Buffer, buf_id, name)
        Return New GLBuffer With {.buffer_id = buf_id, .target = target}
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateRenderbuffer(name As String) As Integer
        Dim buffer_id As Integer
        GL.CreateRenderbuffers(1, buffer_id)
        LabelObject(ObjectLabelIdentifier.Texture, buffer_id, name)
        Return buffer_id
    End Function

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function CreateTexture(target As TextureTarget, name As String) As GLTexture
        Dim tex_id As Integer
        GL.CreateTextures(target, 1, tex_id)
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
            GL.BindTextureUnit(i, 0)
        Next
    End Sub

End Module

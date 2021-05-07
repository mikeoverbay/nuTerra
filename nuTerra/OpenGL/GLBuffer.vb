Imports OpenTK.Graphics.OpenGL4

Public Class GLBuffer
    Public Shared ALL_SIZE As Integer
    Public buffer_id As Integer
    Public target As BufferTarget
    Private size As Integer

    Public Shared Function Create(target As BufferTarget, name As String) As GLBuffer
        Dim buf_id As Integer
        GL.CreateBuffers(1, buf_id)
        LabelObject(ObjectLabelIdentifier.Buffer, buf_id, name)
        Dim obj As New GLBuffer With {.buffer_id = buf_id, .target = target}
        Return obj
    End Function

    Public Sub BindBase(base As Integer)
        GL.BindBufferBase(DirectCast(target, BufferRangeTarget), base, buffer_id)
        CheckGLError()
    End Sub

    Public Sub Bind(bind_target As BufferTarget)
        GL.BindBuffer(bind_target, buffer_id)
        CheckGLError()
    End Sub

    Public Sub Delete()
        GL.DeleteBuffer(buffer_id)
        ALL_SIZE -= size
        CheckGLError()
    End Sub

    Public Sub Storage(Of dataType As Structure)(size As Integer, data() As dataType, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer_id, size, data, flags)
        Me.size = size
        ALL_SIZE += size
        CheckGLError()
    End Sub

    Public Sub Storage(Of dataType As Structure)(size As Integer, data As dataType, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer_id, size, data, flags)
        Me.size = size
        ALL_SIZE += size
        CheckGLError()
    End Sub

    Public Sub StorageNullData(size As Integer, flags As BufferStorageFlags)
        GL.NamedBufferStorage(buffer_id, size, IntPtr.Zero, flags)
        Me.size = size
        ALL_SIZE += size
        CheckGLError()
    End Sub
End Class

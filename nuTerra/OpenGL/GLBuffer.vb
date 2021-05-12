Imports OpenTK.Graphics.OpenGL4

Public Class GLBuffer
    Implements IDisposable

    Public Shared ALL_SIZE As Long
    Public buffer_id As Integer
    ReadOnly target As BufferTarget
    Private size As Integer

    Public Sub New(buffer_id As Integer, target As BufferTarget, name As String)
        Me.buffer_id = buffer_id
        Me.target = target
        LabelObject(ObjectLabelIdentifier.Buffer, buffer_id, name)
    End Sub

    Public Shared Function Create(target As BufferTarget, name As String) As GLBuffer
        Dim buf_id As Integer
        GL.CreateBuffers(1, buf_id)
        If buf_id <> 0 Then
            Return New GLBuffer(buf_id, target, name)
        End If
        Return Nothing
    End Function

    Public Sub BindBase(base As Integer)
        GL.BindBufferBase(DirectCast(target, BufferRangeTarget), base, buffer_id)
        CheckGLError()
    End Sub

    Public Sub Bind(bind_target As BufferTarget)
        GL.BindBuffer(bind_target, buffer_id)
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

    Public Sub Dispose() Implements IDisposable.Dispose
        GL.DeleteBuffer(buffer_id)
        ALL_SIZE -= size
        CheckGLError()
    End Sub
End Class

Imports OpenTK.Graphics.OpenGL4

Public Class GLVertexArray
    Implements IDisposable

    Private va_id As Integer

    Public Sub New(va_id As Integer, name As String)
        Me.va_id = va_id
        LabelObject(ObjectLabelIdentifier.VertexArray, va_id, name)
    End Sub

    Public Shared Function Create(name As String) As GLVertexArray
        Dim va_id As Integer
        GL.CreateVertexArrays(1, va_id)
        If va_id <> 0 Then
            Return New GLVertexArray(va_id, name)
        End If
        Return Nothing
    End Function

    Public Sub Bind()
        GL.BindVertexArray(va_id)
    End Sub

    Public Sub ElementBuffer(buffer As GLBuffer)
        GL.VertexArrayElementBuffer(va_id, buffer.buffer_id)
    End Sub

    Public Sub VertexBuffer(bindingindex As Integer, buffer As GLBuffer, offset As IntPtr, stride As Integer)
        GL.VertexArrayVertexBuffer(va_id, bindingindex, buffer.buffer_id, offset, stride)
    End Sub

    Public Sub AttribFormat(attribgindex As Integer, size As Integer, type As VertexAttribType, normalized As Boolean, relativeoffset As Integer)
        GL.VertexArrayAttribFormat(va_id, attribgindex, size, type, normalized, relativeoffset)
    End Sub

    Public Sub AttribBinding(attribgindex As Integer, bindingindex As Integer)
        GL.VertexArrayAttribBinding(va_id, attribgindex, bindingindex)
    End Sub

    Public Sub EnableAttrib(index As Integer)
        GL.EnableVertexArrayAttrib(va_id, index)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        GL.DeleteVertexArray(va_id)
        CheckGLError()
    End Sub
End Class

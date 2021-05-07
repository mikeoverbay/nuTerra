Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Public Class GLRenderbuffer
    Public Shared ALL_SIZE As Integer
    Public renderbuffer_id As Integer
    Private size As Integer

    Public Shared Function Create(name As String) As GLRenderbuffer
        Dim buffer_id As Integer
        GL.CreateRenderbuffers(1, buffer_id)
        LabelObject(ObjectLabelIdentifier.Renderbuffer, buffer_id, name)
        Dim obj As New GLRenderbuffer With {.renderbuffer_id = buffer_id}
        Return obj
    End Function

    Public Sub Delete()
        GL.DeleteRenderbuffer(renderbuffer_id)
        ALL_SIZE -= size
        CheckGLError()
    End Sub

    Public Sub Storage(internalformat As RenderbufferStorage, width As Integer, height As Integer)
        GL.NamedRenderbufferStorage(renderbuffer_id, internalformat, width, height)
        size = width * height * 4 ' FXIME
        ALL_SIZE += size
    End Sub
End Class

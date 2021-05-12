﻿Imports OpenTK.Graphics.OpenGL4

Public Class GLRenderbuffer
    Implements IDisposable

    Public Shared ALL_SIZE As Long
    Public renderbuffer_id As Integer
    Private size As Integer

    Public Sub New(renderbuffer_id As Integer, name As String)
        Me.renderbuffer_id = renderbuffer_id
        LabelObject(ObjectLabelIdentifier.Renderbuffer, renderbuffer_id, name)
    End Sub

    Public Shared Function Create(name As String) As GLRenderbuffer
        Dim buffer_id As Integer
        GL.CreateRenderbuffers(1, buffer_id)
        If buffer_id <> 0 Then
            Return New GLRenderbuffer(buffer_id, name)
        End If
        Return Nothing
    End Function

    Public Sub Storage(internalformat As RenderbufferStorage, width As Integer, height As Integer)
        GL.NamedRenderbufferStorage(renderbuffer_id, internalformat, width, height)
        size = width * height * 4 ' FXIME
        ALL_SIZE += size
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        GL.DeleteRenderbuffer(renderbuffer_id)
        ALL_SIZE -= size
        CheckGLError()
    End Sub
End Class

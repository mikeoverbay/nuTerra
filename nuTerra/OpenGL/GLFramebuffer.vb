Imports OpenTK.Graphics.OpenGL4

Public Class GLFramebuffer
    Public fbo_id As Integer

    Public Shared Function Create(name As String) As GLFramebuffer
        Dim fbo_id As Integer
        GL.CreateFramebuffers(1, fbo_id)
        LabelObject(ObjectLabelIdentifier.Framebuffer, fbo_id, name)
        Dim obj As New GLFramebuffer With {.fbo_id = fbo_id}
        Return obj
    End Function

    Public Sub Bind(target As FramebufferTarget)
        GL.BindFramebuffer(target, fbo_id)
    End Sub

    Public Sub DrawBuffers(n As Integer, bufs() As DrawBuffersEnum)
        GL.NamedFramebufferDrawBuffers(fbo_id, n, bufs)
    End Sub

    Public Sub Texture(attachment As FramebufferAttachment, texture As GLTexture, level As Integer)
        GL.NamedFramebufferTexture(fbo_id, attachment, If(texture Is Nothing, 0, texture.texture_id), level)
    End Sub

    Public Sub Renderbuffer(attachment As FramebufferAttachment, target As RenderbufferTarget, renderbuffer As GLRenderbuffer)
        GL.NamedFramebufferRenderbuffer(fbo_id, attachment, target, renderbuffer.renderbuffer_id)
    End Sub

    Public ReadOnly Property IsComplete
        Get
            Dim fboStatus = GL.CheckNamedFramebufferStatus(fbo_id, FramebufferTarget.Framebuffer)
            If fboStatus <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If
            Return True
        End Get
    End Property

    Public Sub Delete()
        GL.DeleteFramebuffer(fbo_id)
        CheckGLError()
    End Sub

    Public Sub ReadBuffer(src As ReadBufferMode)
        GL.NamedFramebufferReadBuffer(fbo_id, src)
    End Sub

    Public Sub DrawBuffer(buf As DrawBufferMode)
        GL.NamedFramebufferDrawBuffer(fbo_id, buf)
    End Sub
End Class

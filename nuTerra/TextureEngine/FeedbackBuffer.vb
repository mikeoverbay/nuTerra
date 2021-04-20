Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Public Class FeedbackBuffer
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer

    Public Requests() As Integer

    Public width As Integer
    Public height As Integer

    Public fbo As Integer
    ReadOnly rendertarget As Integer
    ReadOnly depthbuffer As Integer

    Public Sub New(info As VirtualTextureInfo, width As Integer, height As Integer)
        Me.info = info
        Me.width = width
        Me.height = height

        indexer = New PageIndexer(info)
        ReDim Requests(indexer.Count - 1)

        rendertarget = CreateRenderbuffer("FeedbackBuffer_rendertarget")
        GL.NamedRenderbufferStorage(rendertarget, RenderbufferStorage.Rgba32f, width, height)

        depthbuffer = CreateRenderbuffer("FeedbackBuffer_depthbuffer")
        GL.NamedRenderbufferStorage(depthbuffer, RenderbufferStorage.DepthComponent32f, width, height)

        fbo = CreateFramebuffer("FeedbackBuffer_fbo")

        GL.NamedFramebufferRenderbuffer(fbo, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, rendertarget)
        GL.NamedFramebufferRenderbuffer(fbo, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthbuffer)

        Dim FBOHealth = GL.CheckNamedFramebufferStatus(fbo, FramebufferTarget.Framebuffer)
        If FBOHealth <> FramebufferStatus.FramebufferComplete Then
            Stop
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        GL.DeleteFramebuffer(fbo)
        GL.DeleteRenderbuffer(rendertarget)
        GL.DeleteRenderbuffer(depthbuffer)
    End Sub

    Public Sub Download()
        ' Download New data
        Dim data(width * height - 1) As Vector4
        GL.GetTextureImage(rendertarget, 0, PixelFormat.Rgba, PixelType.Float, data.Length, data)

        For i = 0 To data.Length - 1
            If data(i).W >= 0.99F Then
                Dim request = New Page(data(i).X, data(i).Y, data(i).Z)
                AddRequestAndParents(request)
            End If
        Next
    End Sub

    Private Sub AddRequestAndParents(request As Page)
        Dim PageTableSizeLog2 = Math.Log(info.PageTableSize, 2)
        Dim count = PageTableSizeLog2 - request.Mip + 1

        For i = 0 To count - 1
            Dim xpos = request.X >> i
            Dim ypos = request.Y >> i

            Dim page = New Page(xpos, ypos, request.Mip + i)

            If Not indexer.IsValid(page) Then
                Return
            End If

            Requests(indexer(page)) += 1
        Next
    End Sub
End Class

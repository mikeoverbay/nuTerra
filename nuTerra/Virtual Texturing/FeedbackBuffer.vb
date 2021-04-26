Imports System.Runtime.InteropServices
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
    ReadOnly pboReadback As GLBuffer
    ReadOnly data() As FBColor

    Public Sub New(info As VirtualTextureInfo, width As Integer, height As Integer)
        Me.info = info
        Me.width = width
        Me.height = height
        ReDim data(width * height - 1)

        indexer = New PageIndexer(info)
        ReDim Requests(indexer.Count - 1)

        pboReadback = CreateBuffer(BufferTarget.PixelPackBuffer, "FeedbackBuffer_pboReadback")
        BufferStorageNullData(pboReadback, width * height * 3, BufferStorageFlags.None)

        rendertarget = CreateRenderbuffer("FeedbackBuffer_rendertarget")
        GL.NamedRenderbufferStorage(rendertarget, RenderbufferStorage.Rgb8, width, height)

        depthbuffer = CreateRenderbuffer("FeedbackBuffer_depthbuffer")
        GL.NamedRenderbufferStorage(depthbuffer, RenderbufferStorage.DepthComponent16, width, height)

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

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FBColor
        Public r As Byte
        Public g As Byte
        Public b As Byte
    End Structure

    Public Sub Download()
        ' Download New data
        GL.GetNamedBufferSubData(pboReadback.buffer_id, IntPtr.Zero, data.Length * 3, data)

        For i = 0 To data.Length - 1
            If data(i).b >= 1 Then
                Dim request = New Page(data(i).r, data(i).g, data(i).b - 1)
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

            Dim page As New Page(xpos, ypos, request.Mip + i)

            If Not indexer.IsValid(page) Then
                Return
            End If

            Requests(indexer(page)) += 1
        Next
    End Sub

    Public Sub copy()
        pboReadback.Bind(BufferTarget.PixelPackBuffer)
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero)
        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0)
    End Sub

    Public Sub clear()
        Array.Clear(Requests, 0, indexer.Count)
    End Sub
End Class

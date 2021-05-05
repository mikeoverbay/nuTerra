Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

Public Class FeedbackBuffer
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer

    Public Requests As New Dictionary(Of Page, Integer)(New PageEqualityComparer)

    Public width As Integer
    Public height As Integer

    Public fbo As Integer
    ReadOnly rendertarget As Integer
    ReadOnly depthbuffer As Integer
    ReadOnly pboReadback As GLBuffer

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FBColor
        Public r As UShort
        Public g As UShort
        Public b As UShort
    End Structure
    ReadOnly data() As FBColor

    Public Sub New(info As VirtualTextureInfo, width As Integer, height As Integer)
        Me.info = info
        Me.width = width
        Me.height = height
        ReDim data(width * height - 1)

        indexer = New PageIndexer(info)

        pboReadback = CreateBuffer(BufferTarget.PixelPackBuffer, "FeedbackBuffer_pboReadback")
        BufferStorageNullData(pboReadback, width * height * 6, BufferStorageFlags.ClientStorageBit)

        rendertarget = CreateRenderbuffer("FeedbackBuffer_rendertarget")
        GL.NamedRenderbufferStorage(rendertarget, RenderbufferStorage.Rgb16, width, height)

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

    Public Sub Download()
        ' Download New data
        GL.GetNamedBufferSubData(pboReadback.buffer_id, IntPtr.Zero, data.Length * 6, data)
        For i = 0 To data.Length - 1
            If data(i).b >= 1 Then
                AddRequestAndParents(data(i).r, data(i).g, data(i).b - 1)
            End If
        Next
    End Sub

    Private Sub AddRequestAndParents(X As Integer, Y As Integer, Mip As Integer)
        Dim PageTableSizeLog2 = Math.Log(info.PageTableSize, 2)
        Dim count = PageTableSizeLog2 - Mip + 1

        For i = 0 To count - 1
            Dim xpos = X >> i
            Dim ypos = Y >> i

            If Not indexer.IsValid(xpos, ypos, Mip + i) Then
                Return
            End If

            Dim page As New Page(xpos, ypos, Mip + i)

            Dim value As Integer
            If Requests.TryGetValue(page, value) Then
                Requests(page) = value + 1
            Else
                Requests(page) = 1
            End If
        Next
    End Sub

    Public Sub copy()
        pboReadback.Bind(BufferTarget.PixelPackBuffer)
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedShort, IntPtr.Zero)
        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0)
    End Sub

    Public Sub clear()
        Requests.Clear()
    End Sub
End Class

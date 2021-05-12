Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

Public Class FeedbackBuffer
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer

    Public Requests As New Dictionary(Of Page, Integer)(New PageEqualityComparer)

    Public width As Integer
    Public height As Integer

    Public fbo As GLFramebuffer
    ReadOnly rendertarget As GLRenderbuffer
    ReadOnly depthbuffer As GLRenderbuffer
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

        pboReadback = GLBuffer.Create(BufferTarget.PixelPackBuffer, "FeedbackBuffer_pboReadback")
        pboReadback.StorageNullData(width * height * 6, BufferStorageFlags.ClientStorageBit)

        rendertarget = GLRenderbuffer.Create("FeedbackBuffer_rendertarget")
        rendertarget.Storage(RenderbufferStorage.Rgb16, width, height)

        depthbuffer = GLRenderbuffer.Create("FeedbackBuffer_depthbuffer")
        depthbuffer.Storage(RenderbufferStorage.DepthComponent16, width, height)

        fbo = GLFramebuffer.Create("FeedbackBuffer_fbo")

        fbo.Renderbuffer(FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, rendertarget)
        fbo.Renderbuffer(FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthbuffer)

        If Not fbo.IsComplete Then
            Stop
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        rendertarget?.Dispose()
        depthbuffer?.Dispose()
        fbo?.Dispose()
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

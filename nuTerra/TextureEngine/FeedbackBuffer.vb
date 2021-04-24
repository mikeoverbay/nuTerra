﻿Imports System.Runtime.InteropServices
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
    ReadOnly rendertarget As GLTexture
    ReadOnly depthbuffer As Integer

    Public Sub New(info As VirtualTextureInfo, width As Integer, height As Integer)
        Me.info = info
        Me.width = width
        Me.height = height

        indexer = New PageIndexer(info)
        ReDim Requests(indexer.Count - 1)

        rendertarget = CreateTexture(TextureTarget.Texture2D, "FeedbackBuffer_rendertarget")
        rendertarget.Storage2D(1, SizedInternalFormat.Rgba16f, width, height)

        depthbuffer = CreateRenderbuffer("FeedbackBuffer_depthbuffer")
        GL.NamedRenderbufferStorage(depthbuffer, RenderbufferStorage.DepthComponent32f, width, height)

        fbo = CreateFramebuffer("FeedbackBuffer_fbo")

        GL.NamedFramebufferTexture(fbo, FramebufferAttachment.ColorAttachment0, rendertarget.texture_id, 0)
        GL.NamedFramebufferRenderbuffer(fbo, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthbuffer)

        Dim FBOHealth = GL.CheckNamedFramebufferStatus(fbo, FramebufferTarget.Framebuffer)
        If FBOHealth <> FramebufferStatus.FramebufferComplete Then
            Stop
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        GL.DeleteFramebuffer(fbo)
        rendertarget.Delete()
        GL.DeleteRenderbuffer(depthbuffer)
    End Sub

    <StructLayout(LayoutKind.Sequential)>
    Private Structure FBColor
        Public r As Half
        Public g As Half
        Public b As Half
        Public a As Half
    End Structure

    Public Sub Download()
        ' Download New data
        Dim data(width * height - 1) As FBColor
        GL.GetTextureImage(rendertarget.texture_id, 0, PixelFormat.Rgba, PixelType.HalfFloat, 8 * data.Length, data)

        For i = 0 To data.Length - 1
            If data(i).a >= 0.99F Then
                Dim request = New Page(data(i).r.ToSingle, data(i).g.ToSingle, data(i).b.ToSingle)
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

    Public Sub clear()
        Array.Clear(Requests, 0, indexer.Count)
    End Sub
End Class

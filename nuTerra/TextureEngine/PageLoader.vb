Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Public Class PageLoader
    Implements IDisposable

    Class ReadState
        Public Page As Page
        Public Data() As Byte
    End Class

    Const ChannelCount = 4
    Dim info As VirtualTextureInfo

    Public Event loadComplete(p As Page, data As Byte())

    Public Sub New(filename As String, indexer As PageIndexer, info As VirtualTextureInfo)
        Me.info = info

        FBO_mixer_set.FBO_Initialize(info.TileSize, info.TileSize)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose

    End Sub

    Public Sub Submit(request As Page)
        Dim state As New ReadState With {
            .Page = request
            }
        LoadPage(state)
        RaiseEvent loadComplete(state.Page, state.Data)
    End Sub

    Private Sub LoadPage(state As ReadState)
        Dim size = info.PageSize * info.PageSize * ChannelCount

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_Mixer_ID)
        GL.Viewport(0, 0, info.TileSize, info.TileSize)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)

        t_mixerShader.Use()

        Dim proj = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0, -50.0, 50.0F, -3000.0F, 3000.0F)
        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, proj)

        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)

        theMap.GLOBAL_AM_ID.BindUnit(21)

        For i = 0 To theMap.render_set.Length - 1
            If Not theMap.render_set(i).visible Then
                Continue For
            End If

            'draw chunk
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
        Next

        t_mixerShader.StopUse()
        unbind_textures(22)
    End Sub

    Private Sub CopyBorder(image As Byte())
        Dim pagesize = info.PageSize
        Dim bordersize = info.BorderSize

        For i = 0 To pagesize - 1
            Dim xindex = bordersize * pagesize + i
            image(xindex * ChannelCount + 0) = 0
            image(xindex * ChannelCount + 1) = 255
            image(xindex * ChannelCount + 2) = 0
            image(xindex * ChannelCount + 3) = 255

            Dim yindex = i * pagesize + bordersize
            image(yindex * ChannelCount + 0) = 0
            image(yindex * ChannelCount + 1) = 255
            image(yindex * ChannelCount + 2) = 0
            image(yindex * ChannelCount + 3) = 255
        Next
    End Sub

    Private Sub CopyColor(image As Byte(), request As Page)
        Dim colors As Byte(,) = {
                {0, 0, 255, 255},
                {0, 255, 255, 255},
                {255, 0, 0, 255},
                {255, 0, 255, 255},
                {255, 255, 0, 255},
                {64, 64, 192, 255},
                {64, 192, 64, 255},
                {64, 192, 192, 255},
                {192, 64, 64, 255},
                {192, 64, 192, 255},
                {192, 192, 64, 255},
                {0, 255, 0, 255}
            }

        Dim pagesize = info.PageSize

        For y = 0 To pagesize - 1
            For x = 0 To pagesize - 1
                image((y * pagesize + x) * ChannelCount + 0) = colors(request.Mip, 0)
                image((y * pagesize + x) * ChannelCount + 1) = colors(request.Mip, 1)
                image((y * pagesize + x) * ChannelCount + 2) = colors(request.Mip, 2)
                image((y * pagesize + x) * ChannelCount + 3) = colors(request.Mip, 3)
            Next
        Next
    End Sub
End Class

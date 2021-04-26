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
    Dim indexer As PageIndexer

    Public Event loadComplete(p As Page, data As Byte())

    Public Sub New(filename As String, indexer As PageIndexer, info As VirtualTextureInfo)
        Me.info = info
        Me.indexer = indexer

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
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_Mixer_ID)
        GL.Viewport(0, 0, info.TileSize, info.TileSize)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        t_mixerShader.Use()


        Dim perSize = Math.Pow(2, state.Page.Mip)

        Dim xMin As Integer = 100 * (theMap.bounds_minX) + 100
        Dim yMin As Integer = 100 * (theMap.bounds_minY)
        Dim _w = 100 * (theMap.bounds_maxX - theMap.bounds_minX + 1)
        Dim _h = 100 * (theMap.bounds_maxY - theMap.bounds_minY + 1)

        Dim x = xMin + state.Page.X / info.PageTableSize * perSize * _w
        Dim y = yMin + state.Page.Y / info.PageTableSize * perSize * _h

        Dim proj = Matrix4.CreateOrthographicOffCenter(
            x, x + _w / info.PageTableSize * perSize,
            y, y + _h / info.PageTableSize * perSize,
            -1, 1)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, proj)

        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)

        theMap.GLOBAL_AM_ID.BindUnit(0)

        For i = 0 To theMap.render_set.Length - 1
            With theMap.render_set(i)
                .layersStd140_ubo.BindBase(0)

                'AM maps
                theMap.render_set(i).layer.render_info(0).atlas_id.BindUnit(1)
                theMap.render_set(i).layer.render_info(1).atlas_id.BindUnit(2)
                theMap.render_set(i).layer.render_info(2).atlas_id.BindUnit(3)
                theMap.render_set(i).layer.render_info(3).atlas_id.BindUnit(4)
                theMap.render_set(i).layer.render_info(4).atlas_id.BindUnit(5)
                theMap.render_set(i).layer.render_info(5).atlas_id.BindUnit(6)
                theMap.render_set(i).layer.render_info(6).atlas_id.BindUnit(7)
                theMap.render_set(i).layer.render_info(7).atlas_id.BindUnit(8)

                'bind blend textures
                .TexLayers(0).Blend_id.BindUnit(9)
                .TexLayers(1).Blend_id.BindUnit(10)
                .TexLayers(2).Blend_id.BindUnit(11)
                .TexLayers(3).Blend_id.BindUnit(12)
            End With

            'draw chunk
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
        Next

        t_mixerShader.StopUse()
        unbind_textures(12)
    End Sub
End Class

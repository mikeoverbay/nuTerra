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
        Dim size = info.TileSize * info.TileSize * ChannelCount

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_Mixer_ID)
        GL.Viewport(0, 0, info.TileSize, info.TileSize)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        t_mixerShader.Use()

        Dim proj = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0, -50.0, 50.0F, -3000.0F, 3000.0F)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, proj)

        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)

        theMap.GLOBAL_AM_ID.BindUnit(0)

        For i = 0 To theMap.render_set.Length - 1
            If Not theMap.render_set(i).visible Then
                Continue For
            End If

            'draw chunk
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
        Next

        t_mixerShader.StopUse()
        unbind_textures(0)
    End Sub
End Class

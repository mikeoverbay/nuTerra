Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL4

Public Class PageLoader
    Implements IDisposable

    Class ReadState
        Public Page As Page
        Public ColorData() As Byte
        Public NormalData() As Byte
        Public SpecularData() As Byte
    End Class

    Const ChannelCount = 4
    Dim info As VirtualTextureInfo
    Dim indexer As PageIndexer
    Dim uncompData() As Byte
    Dim compDataColor() As Byte
    Dim compDataNormal() As Byte
    Dim specularData() As Byte

    Public Event loadComplete(p As Page, color_data As Byte(), normal_data As Byte(), specular_data As Byte())

    Public Sub New(indexer As PageIndexer, info As VirtualTextureInfo)
        Me.info = info
        ReDim uncompData((info.TileSize * info.TileSize * 4) - 1)
        ReDim specularData((info.TileSize * info.TileSize) - 1)
        ReDim compDataColor((((info.TileSize + 3) \ 4) * ((info.TileSize + 3) \ 4) * 16) - 1)
        ReDim compDataNormal((((info.TileSize + 3) \ 4) * ((info.TileSize + 3) \ 4) * 16) - 1)

        FBO_mixer_set.FBO_Initialize(info.TileSize, info.TileSize)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        uncompData = Nothing
        specularData = Nothing
        compDataColor = Nothing
        compDataNormal = Nothing
    End Sub

    Public Sub Submit(request As Page)
        Dim state As New ReadState With {
            .Page = request
            }
        LoadPage(state)
        RaiseEvent loadComplete(state.Page, state.ColorData, state.NormalData, state.SpecularData)
    End Sub

    Private Sub LoadPage(state As ReadState)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_mixer_set.fbo)
        GL.Viewport(0, 0, info.TileSize, info.TileSize)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        t_mixerShader.Use()

        Dim perSize = Math.Pow(2, state.Page.Mip)

        Dim xMin As Integer = 100 * b_x_min
        Dim yMin As Integer = 100 * (b_y_min - 1)
        Dim _w = 100 * (b_x_max - b_x_min + 1)
        Dim _h = 100 * (b_y_max - b_y_min + 1)

        Dim left = xMin + state.Page.X / info.PageTableSize * perSize * _w
        Dim bottom = yMin + state.Page.Y / info.PageTableSize * perSize * _h
        Dim right = left + _w / info.PageTableSize * perSize
        Dim top = bottom + _h / info.PageTableSize * perSize

        Dim proj = Matrix4.CreateOrthographicOffCenter(
            left, right,
            bottom, top,
            -1, 1)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, proj)
        GL.Uniform1(t_mixerShader("tile_mip_level"), CSng(state.Page.Mip) / 3)

        GL.Disable(EnableCap.DepthTest)
        GL.CullFace(CullFaceMode.Front)

        theMap.GLOBAL_AM_ID.BindUnit(0)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.v_data(i).BB_Min.X > right Then
                Continue For
            End If

            If theMap.v_data(i).BB_Max.X < left Then
                Continue For
            End If

            If theMap.v_data(i).BB_Min.Z > top Then
                Continue For
            End If

            If theMap.v_data(i).BB_Max.Z < bottom Then
                Continue For
            End If

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

        ' UNBIND
        unbind_textures(13)

        ' RESTORE STATE
        GL.CullFace(CullFaceMode.Back)

        GL.GetTextureImage(FBO_mixer_set.gColor.texture_id, 0, PixelFormat.Rgba, PixelType.UnsignedByte, uncompData.Length, uncompData)
        nuTerraCPP.Utils.CompressDXT5(uncompData, compDataColor, info.TileSize, info.TileSize)
        state.ColorData = compDataColor

        GL.GetTextureImage(FBO_mixer_set.gNormal.texture_id, 0, PixelFormat.Rgba, PixelType.UnsignedByte, uncompData.Length, uncompData)
        nuTerraCPP.Utils.CompressDXT5(uncompData, compDataNormal, info.TileSize, info.TileSize)
        state.NormalData = compDataNormal

        GL.GetTextureImage(FBO_mixer_set.gSpecular.texture_id, 0, PixelFormat.Red, PixelType.UnsignedByte, specularData.Length, specularData)
        state.SpecularData = specularData
    End Sub
End Class

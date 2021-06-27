Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL
Imports System.IO


Module MakeMega
    'these files MUST remain open while the map is loaded.
    Public megaHDL_AM As FileStream = Nothing 'albedo
    Public megaHDL_NM As FileStream = Nothing  'normal
    Public megaHDL_GMM As FileStream = Nothing  'gloss/metal

    'running index positions in VT Disc mem
    Dim am_vt_index As Integer
    Dim nm_vt_index As Integer
    Dim gmm_vt_index As Integer

    'running actual positions in VT Disc mem
    Dim am_vt_pointer As Long
    Dim nm_vt_pointer As Long
    Dim gmm_vt_pointer As Long

    Structure lut_data_buf
        Dim buff() As UInt32
    End Structure

    Dim lut_data(5) As lut_data_buf

    Dim scalers() = {1, 2, 4, 8, 16, 32}
    '================================================================================================================
    '================================================================================================================
    'The basic idea of this is:
    '1. Render to the mix fbo at a high resolution. 4096 x 4096. May need to be higher.
    '2. Create mips from this texture. Max of 5 total. Mip count may need adjusted.
    '3. Copy the texture as 128 x 128 tile blocks from the fbo texture to disc.
    '4. Update the LUT texture at the proper pixel with the index of the texture on disc.
    '5. DO this for each mip level also.
    'The LUT is a uint texture, 32 x 32 with 5 mip levels. Each pixel in the each Mip stores the location index
    'of the texture on disc.
    'The LUT we shall call megaLUT and it will reside in each render_set of theMAP structure.
    '================================================================================================================
    '================================================================================================================


    Public Function Build_Mega_Textures() As Boolean
        'running index positions in VT Disc mem
        am_vt_index = 0
        nm_vt_index = 0
        gmm_vt_index = 0

        'running actual positions in VT Disc mem
        am_vt_pointer = 0
        nm_vt_pointer = 0
        gmm_vt_pointer = 0

        '====================================
        Dim MAX_RES As Integer = 4096
        Dim Max_mip_level = 6
        '====================================
        prallocate_disc_space(MAX_RES)

        'setup FBO
        MegaFBO.max_mip_level = 1

        MegaFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)

        Dim chunk_count = theMap.render_set.Length - 1

        'Setup window physical size and ortho matrix
        GL.Viewport(0, 0, MAX_RES, MAX_RES)
        Dim proj = Matrix4.CreateOrthographicOffCenter(
            -50.0F, 50.0F, -50.0F, 50.0F, -1000.0F, 1000.0F)

        MegaMixerShader.Use()

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, proj)

        map_scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        map_scene.terrain.GLOBAL_AM_ID.BindUnit(0)
        chunk_count = 1
        For I = 0 To chunk_count

            MegaFBO.FBO_Initialize(MAX_RES, MAX_RES)
            GL.Viewport(0, 0, MAX_RES, MAX_RES)

            With theMap.render_set(I)
                .layersStd140_ubo.BindBase(0)
                'AM maps
                theMap.render_set(I).layer.render_info(0).atlas_id.BindUnit(1)
                theMap.render_set(I).layer.render_info(1).atlas_id.BindUnit(2)
                theMap.render_set(I).layer.render_info(2).atlas_id.BindUnit(3)
                theMap.render_set(I).layer.render_info(3).atlas_id.BindUnit(4)
                theMap.render_set(I).layer.render_info(4).atlas_id.BindUnit(5)
                theMap.render_set(I).layer.render_info(5).atlas_id.BindUnit(6)
                theMap.render_set(I).layer.render_info(6).atlas_id.BindUnit(7)
                theMap.render_set(I).layer.render_info(7).atlas_id.BindUnit(8)

                'bind blend textures
                .TexLayers(0).Blend_id.BindUnit(9)
                .TexLayers(1).Blend_id.BindUnit(10)
                .TexLayers(2).Blend_id.BindUnit(11)
                .TexLayers(3).Blend_id.BindUnit(12)
            End With
            'draw chunk
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(I * Marshal.SizeOf(Of DrawElementsIndirectCommand)))

            MegaFBO.gColor.GenerateMipmap()
            MegaFBO.gNormal.GenerateMipmap()
            MegaFBO.gGmm.GenerateMipmap()

            Dim status = True
            For level = 0 To 5
                status = status And slice_and_dice(MegaFBO.gColor, MAX_RES, lut_data, megaHDL_AM, am_vt_index, am_vt_pointer, level)
                'check for IO failure.
                If Not status Then
                    Return False
                End If
            Next


            For level = 0 To 5
                status = status And slice_and_dice(MegaFBO.gNormal, MAX_RES, lut_data, megaHDL_NM, nm_vt_index, nm_vt_pointer, level)
                'check for IO failure.
                If Not status Then
                    Return False
                End If
            Next

            For level = 0 To 5
                status = status And slice_and_dice(MegaFBO.gGmm, MAX_RES, lut_data, megaHDL_GMM, gmm_vt_index, gmm_vt_pointer, level)
                'check for IO failure.
                If Not status Then
                    Return False
                End If
            Next

            'build the lut from the data lut level arrays.
            theMap.render_set(I).mega_LUT = make_LUT(Max_mip_level)

        Next I

        MegaMixerShader.StopUse()

        ' UNBIND
        unbind_textures(13)

        GL.ReadBuffer(ReadBufferMode.Back)

        Return True
    End Function
    Private Function slice_and_dice(ByRef tex As GLTexture, ByVal res As Integer, ByRef lut_data() As lut_data_buf, disc_stream As FileStream,
                                    ByRef index As Integer, ByRef file_pointer As Long, ByRef level As Integer)
        res = res / scalers(level)
        Dim row_width As Integer = res / 128
        ReDim lut_data(level).buff((row_width * row_width) - 1)
        Dim lut_pnt As Integer = 0
        Dim tile_size = 128 * 128 * 4

        Dim buff_size = res * res * 4
        Dim buffer(buff_size - 1.0) As Byte


        GL.GetTextureImage(tex.texture_id, level, PixelFormat.Rgba, PixelType.UnsignedByte, buff_size, buffer)

        Try 'incase an error is thrown

            For j = 0 To row_width - 1
                For i = 0 To row_width - 1
                    'this would be a good time to compress the data.
                    Dim data As Byte() = blockCopy(res, i, j, buffer)
                    lut_data(level).buff(lut_pnt) = index
                    lut_pnt += 1
                    index += 1
                    disc_stream.Position = file_pointer
                    disc_stream.Write(data, 0, data.Length)
                    file_pointer = disc_stream.Position
                Next
            Next

        Catch ex As Exception
            Return False
        End Try

        Return True
    End Function
    Private Function blockCopy(ByVal size As Integer, ByVal x As Integer, ByVal y As Integer, ByRef buffer() As Byte) As Array
        Dim div = size / 128
        Dim block(65535) As Byte
        Dim stride = size * 4
        Dim pnt As Integer = 0
        Dim x_offset = x * 512
        For y_ = y To (y + div - 1)
            For x_ = x To (x + div - 1) * 4
                block(pnt) = buffer((x_ + x_offset) + (y_ * stride))
                pnt += 1
            Next
        Next
        Return block
    End Function

    Private Function make_LUT(ByRef max_mip_level As Integer) As GLTexture
        Dim er = GL.GetError
        Dim LUT = GLTexture.Create(TextureTarget.Texture2D, "Mega_LUT")
        LUT.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        LUT.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        LUT.Parameter(TextureParameterName.TextureMaxLevel, max_mip_level)
        LUT.Storage2D(1, SizedInternalFormat.R32ui, 32, 32)
        LUT.GenerateMipmap()
        er = GL.GetError

        GL.BindTexture(TextureTarget.Texture2D, LUT.texture_id)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 32, 32, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(0).buff)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 16, 16, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(1).buff)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 8, 8, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(2).buff)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 4, 4, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(3).buff)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 2, 2, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(4).buff)
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, lut_data(5).buff)
        GL.BindTexture(TextureTarget.Texture2D, 0)
        er = GL.GetError
        Return LUT
    End Function

    Public Sub close_megas()
        'Used when Terra is shut down
        If megaHDL_AM IsNot Nothing Then
            megaHDL_AM.Close()
        End If
        If megaHDL_NM IsNot Nothing Then
            megaHDL_NM.Close()
        End If
        If megaHDL_GMM IsNot Nothing Then
            megaHDL_GMM.Close()
        End If
    End Sub
    Public Sub prallocate_disc_space(ByVal MAX_RES)

        'Close if they are exist already. This atomatically deletes them per options.
        close_megas()

        Dim chunk_count = theMap.render_set.Length - 1
        'each chunk will be need:
        ' 4096 x 4096
        Dim L0 = MAX_RES
        Dim L1 = MAX_RES / 2
        Dim L2 = MAX_RES / 4
        Dim L3 = MAX_RES / 8
        Dim L4 = MAX_RES / 16
        Dim L5 = MAX_RES / 32
        ' Calculate disc space requirement 
        Dim total_space As Long = ((L0 + 3) / 4) * ((L0 + 3) / 4) * 16 * chunk_count
        total_space += ((L1 + 3) / 4) * ((L1 + 3) / 4) * 16 * chunk_count
        total_space += ((L2 + 3) / 4) * ((L2 + 3) / 4) * 16 * chunk_count
        total_space += ((L3 + 3) / 4) * ((L3 + 3) / 4) * 16 * chunk_count
        total_space += ((L4 + 3) / 4) * ((L4 + 3) / 4) * 16 * chunk_count
        total_space += ((L5 + 3) / 4) * ((L5 + 3) / 4) * 16 * chunk_count
        total_space = (MAX_RES / 1) ^ 2 * 4
        total_space = (MAX_RES / 2) ^ 2 * 4
        total_space = (MAX_RES / 4) ^ 2 * 4
        total_space = (MAX_RES / 8) ^ 2 * 4
        total_space = (MAX_RES / 16) ^ 2 * 4
        total_space = (MAX_RES / 32) ^ 2 * 4

        reserver_space(megaHDL_AM, total_space, "megaAM.bin")
        reserver_space(megaHDL_NM, total_space, "megaNM.bin")
        reserver_space(megaHDL_GMM, total_space, "megaGMM.bin")
    End Sub

    Private Sub reserver_space(ByRef f As FileStream, size As Long, filename As String)

        Dim buffer_size As Integer = 256 * 256 * 16 ' this can affect speed.. it will need tweaked later

        Dim options = IO.FileOptions.WriteThrough Or FileOptions.RandomAccess Or FileOptions.DeleteOnClose

        f = System.IO.File.Create(Path.Combine(TEMP_STORAGE, filename), buffer_size, options)
        f.Seek(size - 1, SeekOrigin.Begin)
        f.WriteByte(0)

    End Sub
End Module

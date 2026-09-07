Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK.Mathematics
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Module TerrainTextureFunctions
    Public max_on As UInt32
    Public min_on As UInt32
    Dim cur_layer_info_pnt As Integer = 0



    Public Sub get_layers(ByVal map As Integer)
        'There can be as many as 4 TexLayer sets.
        'Each contains the blend map and the 1 or 2 textures
        'that belong to that blend set.

        With theMap.render_set(map)
            ReDim .TexLayers(3)
            .layer_count = 0 'How many layer sets are there
            .TexLayers(0).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(1).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(2).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(3).Blend_id = DUMMY_TEXTURE_ID
        End With

        Get_layer_texture_data(map) ' get all the data

        ' we have the data so lets get the textures.
        get_layer_textures(map)

        theMap.render_set(map).horizon_id = build_horizon_texture(map)


    End Sub

    Private Sub get_layer_textures(ByVal map As Integer)

        For z = 0 To 7
            With theMap.render_set(map).layer.render_info(z)
                'finds and loads and returns the GL texture ID.
                If .texture_name = "" Then
                    'It is important to fill blank IDs with the dummy texture
                    .atlas_id = DUMMY_ATLAS
                    Continue For
                End If
                Dim id = TextureMgr.image_exists(.texture_name) 'Check if this has been loaded already.
                If id IsNot Nothing Then
                    .atlas_id = id
                    Continue For
                End If
                Dim yoffset As Integer = 0
                Dim xoffset As Integer = 0
                Dim tex_names(4) As String
                tex_names(0) = .texture_name
                tex_names(1) = .texture_name.Replace("_AM", "_NM")
                tex_names(2) = .texture_name.Replace("_AM", "_macro_AM")
                tex_names(3) = .texture_name.Replace("_AM", "_macro_NM")

                Dim atlas_tex As GLTexture = Nothing
                Dim fullWidth As Integer = 1024
                Dim fullHeight As Integer = 1024

                ' The slice index is i, not a separate counter. It used to be a
                ' running "layer" incremented at the bottom of the loop, which
                ' Continue For skipped - so one missing partner texture shifted
                ' every later one into the wrong slot: macro AM landing in the
                ' normal map slice, and one slice never written at all. An
                ' unwritten slice of a freshly allocated texture is undefined,
                ' which is what put red over the terrain on 36_fishing_bay.
                Dim written(3) As Boolean
                Dim slice_size As Integer = 0
                Dim slice_format As Integer = 0

                For i = 0 To 3

                    Dim dds_entry = ResMgr.Lookup(tex_names(i))

                    If dds_entry Is Nothing Then
                        LogThis("terrain atlas: {0} not found - slice {1} left blank", tex_names(i), i)
                        Continue For
                    End If

                    Dim dds_ms As New MemoryStream
                    dds_entry.Extract(dds_ms)

                    dds_ms.Position = 0
                    Dim er = GL.GetError
                    Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                        Dim dds_header = TextureMgr.get_dds_header(dds_br)
                        dds_ms.Position = 128

                        Dim format_info = dds_header.format_info

                        If i = 0 Then 'run once to get new atlas texture
                            'Calculate Max Mip Level based on width or height.. Which ever is larger.
                            Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(fullWidth, fullHeight), 2))
                            atlas_tex = get_atlas(numLevels, map, z, format_info.texture_format)
                        End If

                        Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                        Dim data = dds_br.ReadBytes(size)

                        slice_size = size
                        slice_format = CInt(format_info.texture_format)

                        er = GL.GetError
                        atlas_tex.CompressedSubImage3D(0, 0, 0, i, 1024, 1024, 1,
                                                DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                        er = GL.GetError
                        written(i) = True
                    End Using
                Next

                ' The AM is slice 0 and creates the atlas - without it there is
                ' nothing to fill, so fall back to the dummy for the whole layer.
                If atlas_tex Is Nothing Then
                    LogThis("terrain atlas: {0} has no usable AM - using the dummy", .texture_name)
                    .atlas_id = DUMMY_ATLAS
                    Continue For
                End If

                ' Anything we could not load gets zeroed rather than left as
                ' whatever the driver handed us.
                If slice_size > 0 Then
                    Dim blank(slice_size - 1) As Byte
                    For i = 0 To 3
                        If Not written(i) Then
                            atlas_tex.CompressedSubImage3D(0, 0, 0, i, 1024, 1024, 1,
                                                    DirectCast(slice_format, OpenGL.PixelFormat), slice_size, blank)
                        End If
                    Next
                End If

                atlas_tex.GenerateMipmap()
                .atlas_id = atlas_tex
                TextureMgr.add_image(.texture_name, .atlas_id)

            End With
        Next
        ' fill ubo

        With theMap.render_set(map)
            Dim layersBuffer As New LayersStd140
            layersBuffer.U1 = .TexLayers(0).uP1
            layersBuffer.U2 = .TexLayers(0).uP2

            layersBuffer.U3 = .TexLayers(1).uP1
            layersBuffer.U4 = .TexLayers(1).uP2

            layersBuffer.U5 = .TexLayers(2).uP1
            layersBuffer.U6 = .TexLayers(2).uP2

            layersBuffer.U7 = .TexLayers(3).uP1
            layersBuffer.U8 = .TexLayers(3).uP2

            layersBuffer.V1 = .TexLayers(0).vP1
            layersBuffer.V2 = .TexLayers(0).vP2

            layersBuffer.V3 = .TexLayers(1).vP1
            layersBuffer.V4 = .TexLayers(1).vP2

            layersBuffer.V5 = .TexLayers(2).vP1
            layersBuffer.V6 = .TexLayers(2).vP2

            layersBuffer.V7 = .TexLayers(3).vP1
            layersBuffer.V8 = .TexLayers(3).vP2

            layersBuffer.r1_1 = .TexLayers(0).r1
            layersBuffer.r1_2 = .TexLayers(0).r2_1
            layersBuffer.r1_3 = .TexLayers(1).r1
            layersBuffer.r1_4 = .TexLayers(1).r2_1
            layersBuffer.r1_5 = .TexLayers(2).r1
            layersBuffer.r1_6 = .TexLayers(2).r2_1
            layersBuffer.r1_7 = .TexLayers(3).r1
            layersBuffer.r1_8 = .TexLayers(3).r2_1

            layersBuffer.r2_1 = .TexLayers(0).r2
            layersBuffer.r2_2 = .TexLayers(0).r2_2
            layersBuffer.r2_3 = .TexLayers(1).r2
            layersBuffer.r2_4 = .TexLayers(1).r2_2
            layersBuffer.r2_5 = .TexLayers(2).r2
            layersBuffer.r2_6 = .TexLayers(2).r2_2
            layersBuffer.r2_7 = .TexLayers(3).r2
            layersBuffer.r2_8 = .TexLayers(3).r2_2

            layersBuffer.s1 = .TexLayers(0).scale_a
            layersBuffer.s2 = .TexLayers(0).scale_b
            layersBuffer.s3 = .TexLayers(1).scale_a
            layersBuffer.s4 = .TexLayers(1).scale_b
            layersBuffer.s5 = .TexLayers(2).scale_a
            layersBuffer.s6 = .TexLayers(2).scale_b
            layersBuffer.s7 = .TexLayers(3).scale_a
            layersBuffer.s8 = .TexLayers(3).scale_b



            .layersStd140_ubo = GLBuffer.Create(BufferTarget.UniformBuffer, String.Format("layersStd140_ubo_{0}", map))
            .layersStd140_ubo.Storage(
                Marshal.SizeOf(layersBuffer),
                layersBuffer,
                BufferStorageFlags.None)
        End With
    End Sub
    ''' <summary>
    ''' Expands terrain2/horizonshadows into a 128x128 R8 texture for one chunk.
    ''' The section header is "shd", width, height, bits-per-texel, planes - so
    ''' 128 x 128 at 4 bits, two texels per byte, low nibble first. 16 levels is
    ''' coarse, but this is a large scale terrain-on-terrain term.
    ''' </summary>
    Private Function build_horizon_texture(map As Integer) As GLTexture
        ' DISABLED - the layout is not cracked yet, and feeding it in produced a
        ' corduroy pattern across the terrain.
        '
        ' What is known: header is "shd", 128, 128, 4, 1, then 8192 bytes. The
        ' body is NOT 128x128 at 4 bits as the header suggests. Autocorrelation
        ' puts the record size at 8 bytes and the row at 256 bytes, so 32 x 32
        ' texels of 8 bytes each. Within a record the first two little-endian
        ' u16 are coherent terrain-like values in 0..2047 - two 11 bit fields,
        ' plausibly a pair of horizon angles - and the remaining four bytes do
        ' not read as anything spatially coherent.
        '
        ' Everything downstream of here is wired and works: the RG8 specular
        ' target and atlas, the bind at unit 13, the sample in t_mixer, and the
        ' fetch in TerrainLQ/HQ. Return a real texture here once the layout is
        ' understood and it lights up.
        Return Nothing

        Dim src = theMap.chunks(map).horizon_data
        If src Is Nothing OrElse src.Length < 32 Then Return Nothing

        Dim w = BitConverter.ToInt32(src, 4)
        Dim h = BitConverter.ToInt32(src, 8)
        Dim bits = BitConverter.ToInt32(src, 12)
        If w <= 0 OrElse h <= 0 OrElse bits <> 4 Then
            LogThis("horizonshadows: unexpected header {0}x{1} bits={2} - skipped", w, h, bits)
            Return Nothing
        End If

        Dim need = w * h \ 2
        If src.Length - 32 < need Then
            LogThis("horizonshadows: {0} body bytes, expected {1} - skipped", src.Length - 32, need)
            Return Nothing
        End If

        Dim pix(w * h - 1) As Byte
        For i = 0 To need - 1
            Dim b = src(32 + i)
            pix(i * 2) = CByte((b And &HF) * 17)
            pix(i * 2 + 1) = CByte((b >> 4) * 17)
        Next

        Dim t = GLTexture.Create(TextureTarget.Texture2D, String.Format("horizon_{0}", map))
        t.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        t.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        t.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        t.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        t.Storage2D(1, SizedInternalFormat.R8, w, h)
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1)
        t.SubImage2D(0, 0, 0, w, h, OpenGL.PixelFormat.Red, PixelType.UnsignedByte, pix)
        Return t
    End Function

    Private Function get_atlas(mipcount As Integer, map As Int32, z As Int32, format As SizedInternalFormat) As GLTexture
        Dim t = GLTexture.Create(TextureTarget.Texture2DArray, "tAtlas" + map.ToString + "_" + z.ToString)

        t.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), GLCapabilities.maxAniso) 'GLCapabilities.maxAniso

        t.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
        t.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        t.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
        t.Parameter(TextureParameterName.TextureBaseLevel, 0)
        t.Parameter(TextureParameterName.TextureMaxLevel, mipcount - 1)
        t.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        t.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
        t.Storage3D(mipcount, format, 1024, 1024, 4)
        Return t

    End Function

    Public Function Get_layer_texture_data(ByVal map As Integer) As Boolean

        cur_layer_info_pnt = 0

        '---------------------------------------------------------------------
        'lets get the layer render info first
        '---------------------------------------------------------------------
        Dim ms As New MemoryStream(theMap.chunks(map).layers_data)
        Dim br As New BinaryReader(ms)
        With theMap.render_set(map)

            'If map = 53 Then Stop

            Dim magic = br.ReadUInt32
            Dim map_count = br.ReadUInt32
            ReDim .layer.layer_section_size(7)
            ReDim Preserve .layer.render_info(7)
            For i = 0 To 7
                .layer.layer_section_size(i) = br.ReadUInt32

                If .layer.layer_section_size(i) > max_on Then max_on = .layer.layer_section_size(i)
                If .layer.layer_section_size(i) > 0 Then
                    If .layer.layer_section_size(i) < min_on Then min_on = .layer.layer_section_size(i)
                End If
                .layer.render_info(i) = New layer_render_info_entry_
                .layer.render_info(i).texture_name = ""
            Next
            'ReDim .layer.render_info(map_count)

            For i = 0 To map_count - 1
                br.ReadUInt32() 'magic
                .layer.render_info(i).width = br.ReadUInt32
                .layer.render_info(i).height = br.ReadUInt32
                .layer.render_info(i).count = br.ReadUInt32 ' always 8
                If .layer.render_info(i).count <> 8 Then Stop

                'texture projection transforms
                .layer.render_info(i).u.X = round_4(br.ReadSingle)
                .layer.render_info(i).u.Y = 0.0
                br.ReadSingle()
                .layer.render_info(i).u.Z = round_4(br.ReadSingle)
                .layer.render_info(i).u.W = br.ReadSingle

                .layer.render_info(i).v.X = round_4(br.ReadSingle)
                .layer.render_info(i).v.Y = 0.0
                br.ReadSingle()
                .layer.render_info(i).v.Z = round_4(br.ReadSingle)
                .layer.render_info(i).v.W = br.ReadSingle

                .layer.render_info(i).flags = br.ReadUInt32 'always 59
                If .layer.render_info(i).flags <> 59 Then Stop

                'not sure about these 3' Atlas offsets?
                .layer.render_info(i).v1.X = br.ReadSingle
                .layer.render_info(i).v1.Y = br.ReadSingle
                .layer.render_info(i).v1.Z = br.ReadSingle


                ' r1.x = tessellation height
                ' r2.y = terrain offset
                .layer.render_info(i).r1.X = br.ReadSingle
                .layer.render_info(i).r1.Y = br.ReadSingle
                .layer.render_info(i).r1.Z = br.ReadSingle
                .layer.render_info(i).r1.W = br.ReadSingle

                .layer.render_info(i).r2.X = br.ReadSingle
                .layer.render_info(i).r2.Y = br.ReadSingle
                .layer.render_info(i).r2.Z = br.ReadSingle
                .layer.render_info(i).r2.W = br.ReadSingle

                'not sure about these
                .layer.render_info(i).scale.X = br.ReadSingle
                .layer.render_info(i).scale.Y = br.ReadSingle
                .layer.render_info(i).scale.Z = br.ReadSingle
                .layer.render_info(i).scale.W = br.ReadSingle

                Dim bs = br.ReadUInt32
                Dim d = br.ReadBytes(bs)
                .layer.render_info(i).texture_name = Encoding.UTF8.GetString(d, 0, d.Length)

                br.ReadByte()

            Next
            ms.Dispose()

            '---------------------------------------------------------------------
            'lets get the textures and blend texture.
            '---------------------------------------------------------------------

            'Debug.WriteLine(map.ToString("000") + " -------------------------------------")

            Dim ms2 As New MemoryStream(theMap.chunks(map).blend_textures_data)
            ms2.Position = 0

            Dim br2 As New BinaryReader(ms2)

            Dim magic2 = br2.ReadUInt32()
            Dim version = br2.ReadUInt32
            Dim section_cnt = 4
            Dim sec_sizes(3) As UInt32
            For i = 0 To 3
                sec_sizes(i) = br2.ReadUInt32
            Next
            'ReDim .TexLayers(section_cnt)

            Dim lpnter As Integer = 1
            For i = 0 To 3
                Dim len = sec_sizes(i)
                If len > 0 Then

                    Dim mgc = br2.ReadUInt32
                    Dim ver = br2.ReadUInt32
                    .b_x_size = br2.ReadInt16
                    .b_y_size = br2.ReadInt16

                    Dim always19 = br2.ReadInt16
                    Debug.Assert(always19 = 19)
                    Dim tex_cnt = br2.ReadUInt16

                    br2.ReadUInt64() 'padding

                    'get first tex name
                    Dim bs = br2.ReadUInt32 'str length
                    br2.BaseStream.Position += CLng(bs)
                    'we skip these
                    'Dim d = br2.ReadBytes(bs)
                    '.TexLayers(i).AM_name1 = Encoding.UTF8.GetString(d, 0, d.Length)
                    '.TexLayers(i).NM_name1 = .TexLayers(i).AM_name1.Replace("AM.dds", "NM.dds")

                    If tex_cnt > 1 Then
                        'get 2nd tex name if it exist
                        bs = br2.ReadUInt32 'str length
                        br2.BaseStream.Position += CLng(bs)
                        'we skip these
                        'd = br2.ReadBytes(bs)
                        '.TexLayers(i).AM_name2 = Encoding.UTF8.GetString(d, 0, d.Length)
                        '.TexLayers(i).NM_name2 = .TexLayers(i).AM_name2.Replace("AM.dds", "NM.dds")

                    End If
                    'load blend texture
                    .TexLayers(i).Blend_id = TextureMgr.load_t2_texture_from_stream(br2, .b_x_size, .b_y_size)

                    .TexLayers(i).uP1 = .layer.render_info(cur_layer_info_pnt + 0).u
                    .TexLayers(i).vP1 = .layer.render_info(cur_layer_info_pnt + 0).v
                    .TexLayers(i).r1 = .layer.render_info(cur_layer_info_pnt + 0).r1
                    .TexLayers(i).r2 = .layer.render_info(cur_layer_info_pnt + 0).r2
                    .TexLayers(i).scale_a = .layer.render_info(cur_layer_info_pnt + 0).scale
                    'layer part 2
                    .TexLayers(i).uP2 = .layer.render_info(cur_layer_info_pnt + 1).u
                    .TexLayers(i).vP2 = .layer.render_info(cur_layer_info_pnt + 1).v
                    .TexLayers(i).r2_1 = .layer.render_info(cur_layer_info_pnt + 1).r1
                    .TexLayers(i).r2_2 = .layer.render_info(cur_layer_info_pnt + 1).r2

                    .TexLayers(i).scale_b = .layer.render_info(cur_layer_info_pnt + 1).scale

                    ' DEBUG: is there any rotation in the layer projections?
                    ' An axis aligned layer has U = (s, 0, 0, o) and
                    ' V = (0, 0, s, o) - a rotated one puts values in U.z and
                    ' V.x. |det| is the area scale, and atan2 gives the angle.
                    Dim uu = .TexLayers(i).uP1
                    Dim vv = .TexLayers(i).vP1
                    Dim ang = Math.Atan2(uu.Z, uu.X) * 180.0 / Math.PI
                    Dim det = uu.X * vv.Z - uu.Z * vv.X
                    LogThis("  layer {0}: U=({1:0.####} {2:0.####} {3:0.####} {4:0.####}) V=({5:0.####} {6:0.####} {7:0.####} {8:0.####}) angle={9:0.##} det={10:0.####}",
                            i, uu.X, uu.Y, uu.Z, uu.W, vv.X, vv.Y, vv.Z, vv.W, ang, det)

                    ' Candidates for the game's per-block layerMask, tileColor and
                    ' tileOffset - none of these three is identified yet. A mask
                    ' would read as 0/1 per layer; a colour as three 0..1 values;
                    ' an atlas offset as small fractions.
                    Dim sc = .TexLayers(i).scale_a
                    Dim v1 = .layer.render_info(cur_layer_info_pnt + 0).v1
                    Dim rr1 = .TexLayers(i).r1
                    Dim rr2 = .TexLayers(i).r2
                    LogThis("           scale=({0:0.####} {1:0.####} {2:0.####} {3:0.####}) v1=({4:0.####} {5:0.####} {6:0.####}) r1=({7:0.####} {8:0.####} {9:0.####} {10:0.####}) r2=({11:0.####} {12:0.####} {13:0.####} {14:0.####})",
                            sc.X, sc.Y, sc.Z, sc.W, v1.X, v1.Y, v1.Z,
                            rr1.X, rr1.Y, rr1.Z, rr1.W, rr2.X, rr2.Y, rr2.Z, rr2.W)

                    cur_layer_info_pnt += 2
                    .layer_count += 1
                End If
            Next

            ms2.Dispose()
        End With

        Return True
    End Function

    Private Function round_4(v As Single) As Single
        Return Math.Round(v, 2)
    End Function

    Public Sub make_dummy_4_layer_atlas()
        'makes dummy fill texture for terrain atlases

        Dim layer As Single
        Dim buffer = File.ReadAllBytes(Application.StartupPath + "\resources\blank12x12.dds")

        For i = 0 To 3

            Dim dds_ms As New MemoryStream(buffer)

            dds_ms.Position = 0

            Dim er = GL.GetError
            Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                Dim dds_header = TextureMgr.get_dds_header(dds_br)
                dds_ms.Position = 128

                Dim format_info = dds_header.format_info

                If i = 0 Then 'run once to get new atlas texture
                    layer = 0
                    'Calculate Max Mip Level based on width or height.. Which ever is larger.
                    DUMMY_ATLAS = GLTexture.Create(TextureTarget.Texture2DArray, "dummyAtlas")
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureBaseLevel, 0)
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureMaxLevel, 1)
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                    DUMMY_ATLAS.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                    DUMMY_ATLAS.Storage3D(2, format_info.texture_format, 12, 12, 4)
                End If

                Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                Dim data = dds_br.ReadBytes(size)

                er = GL.GetError
                DUMMY_ATLAS.CompressedSubImage3D(0, 0, 0, layer, 12, 12, 1,
                                            DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                er = GL.GetError
            End Using
            layer += 1
        Next
        DUMMY_ATLAS.GenerateMipmap()

    End Sub

End Module
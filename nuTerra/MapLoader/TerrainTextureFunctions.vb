Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK
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
                Dim id = image_exists(.texture_name) 'Check if this has been loaded already.
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

                Dim atlas_tex As New GLTexture
                Dim fullWidth As Integer = 1024
                Dim fullHeight As Integer = 1024
                Dim layer As Single
                Application.DoEvents() 'stop freezing the UI
                For i = 0 To 3

                    Dim dds_entry = ResMgr.Lookup(tex_names(i))

                    If dds_entry Is Nothing Then
                        Stop
                        Continue For
                    End If

                    Dim dds_ms As New MemoryStream
                    dds_entry.Extract(dds_ms)

                    dds_ms.Position = 0
                    Dim er = GL.GetError
                    Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                        Dim dds_header = get_dds_header(dds_br)
                        dds_ms.Position = 128

                        Dim format_info = dds_header.format_info

                        If i = 0 Then 'run once to get new atlas texture
                            layer = 0
                            'Calculate Max Mip Level based on width or height.. Which ever is larger.
                            Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(fullWidth, fullHeight), 2))
                            atlas_tex = get_atlas(numLevels, map, z, format_info.texture_format)
                        End If

                        Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                        Dim data = dds_br.ReadBytes(size)

                        er = GL.GetError
                        atlas_tex.CompressedSubImage3D(0, 0, 0, layer, 1024, 1024, 1,
                                                DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                        er = GL.GetError
                    End Using
                    layer += 1
                Next
                atlas_tex.GenerateMipmap()
                .atlas_id = atlas_tex
                add_image(.texture_name, .atlas_id)

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
    Private Function get_atlas(mipcount As Integer, map As Int32, z As Int32, format As SizedInternalFormat) As GLTexture
        Dim t = New GLTexture
        't.target = TextureTarget.Texture2DArray
        t = GLTexture.Create(TextureTarget.Texture2DArray, "tAtlas" + map.ToString + "_" + z.ToString)

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

            If _Write_texture_info Then
                sb.AppendLine("***********************************************")
                sb.AppendLine(String.Format("*********************************************** MAP ID {0}", map.ToString))
            End If
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
                    'just for reference
                    .TexLayers(i).AM_name1 = theMap.render_set(map).layer.render_info(lpnter).texture_name
                    .TexLayers(i).AM_name2 = ""
                    .TexLayers(i).NM_name1 = ""
                    .TexLayers(i).NM_name2 = ""

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
                    .TexLayers(i).Blend_id = load_t2_texture_from_stream(br2, .b_x_size, .b_y_size)

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
                    If _Write_texture_info Then

                        ' part 1 ======================================================
                        sb.AppendLine(String.Format("T{0} --------------------------------------------", lpnter.ToString))

                        Dim name = theMap.render_set(map).layer.render_info(lpnter - 1).texture_name
                        If name = "" Then
                            sb.AppendLine("-= EMPTY =-")
                        Else
                            sb.AppendLine(name)
                        End If
                        lpnter += 1
                        sb.Append(String.Format("{0,-8}", "U"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).u)

                        sb.Append(String.Format("{0,-8}", "V"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).v)

                        sb.Append(String.Format("{0,-8}", "V0"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).v1)

                        sb.Append(String.Format("{0,-8}", "V1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).r1)

                        sb.Append(String.Format("{0,-8}", "V2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).r2)

                        sb.Append(String.Format("{0,-8}", "V3"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).scale)

                        ' part 2 ======================================================
                        sb.AppendLine(String.Format("T{0} --------------------------------------------", lpnter.ToString))
                        name = theMap.render_set(map).layer.render_info(lpnter - 1).texture_name
                        If name = "" Then
                            sb.AppendLine("-= EMPTY =-")
                        Else
                            sb.AppendLine(name)
                        End If
                        lpnter += 1

                        sb.Append(String.Format("{0,-8}", "U"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).u)

                        sb.Append(String.Format("{0,-8}", "V"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).v)

                        sb.Append(String.Format("{0,-8}", "V0"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).v1)

                        sb.Append(String.Format("{0,-8}", "V1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).r1)

                        sb.Append(String.Format("{0,-8}", "V2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).r2)

                        sb.Append(String.Format("{0,-8}", "V3"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).scale)
                        '=============================================================
                    End If

                    cur_layer_info_pnt += 2
                    .layer_count += 1
                End If
            Next
            sb.AppendLine("")

            ms2.Dispose()
        End With

        Return True
    End Function

    Private Function round_4(v As Single) As Single
        Return Math.Round(v, 2)
    End Function

    Private Sub write_vec4(ByRef v As Vector4)
        sb.AppendLine(String.Format("{0,-8:F4} {1,-8:F4} {2,-8:F4} {3,-8:F4}",
                                 v.X.ToString, v.Y.ToString, v.Z.ToString, v.W.ToString))
    End Sub
    Public Sub make_dummy_4_layer_atlas()
        'makes dummy fill texture for terrain atlases

        Dim fullWidth As Integer = 12
        Dim fullHeight As Integer = 12
        Dim layer As Single
        Application.DoEvents() 'stop freezing the UI
        Dim buffer = File.ReadAllBytes(Application.StartupPath + "\resources\blank12x12.dds")

        For i = 0 To 3

            Dim dds_ms As New MemoryStream(buffer)

            dds_ms.Position = 0

            Dim er = GL.GetError
            Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                Dim dds_header = get_dds_header(dds_br)
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
        'GL.Clear(ClearBufferMask.ColorBufferBit)
        'draw_test_iamge(fullWidth / 2, fullHeight / 2, atlas_tex, True)
        'Stop
        'End If
        ' fill ubo

    End Sub

End Module
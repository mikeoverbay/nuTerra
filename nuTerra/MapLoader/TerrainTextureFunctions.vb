Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Ionic
Imports Ionic.Zip
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

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
        'It is important to fill blank IDs with the dummy texture
        'so the shader has VALID ID and nothing is added.
        'For i = 0 To 3
        '    With theMap.render_set(map).TexLayers(i)
        '        If .AM_name1 = "" Then
        '            .AM_id1 = DUMMY_TEXTURE_ID
        '            .NM_id1 = DUMMY_TEXTURE_ID
        '            .used_a = 0.0F
        '        Else
        '            .AM_id1 = find_and_trim(.AM_name1)
        '            .NM_id1 = find_and_trim(.NM_name1)
        '            .used_a = 1.0F
        '        End If
        '        If .AM_name2 = "" Then
        '            .AM_id2 = DUMMY_TEXTURE_ID
        '            .NM_id2 = DUMMY_TEXTURE_ID
        '            .used_b = 0.0F
        '        Else
        '            .AM_id2 = find_and_trim(.AM_name2)
        '            .NM_id2 = find_and_trim(.NM_name2)
        '            .used_b = 1.0F
        '        End If

        '    End With
        'Next
        For z = 0 To 7
            With theMap.render_set(map).layer.render_info(z)
                'finds and loads and returns the GL texture ID.
                If .texture_name = "" Then
                    .atlas_id = DUMMY_TEXTURE_ID
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
                    Dim coords = tex_names(i)

                    Dim dds_entry As ZipEntry = Nothing
                    dds_entry = search_pkgs(coords)

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
                'GL.Clear(ClearBufferMask.ColorBufferBit)
                'draw_test_iamge(fullWidth / 2, fullHeight / 2, atlas_tex, True)
                'Stop
                'End If
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



            .layersStd140_ubo = CreateBuffer(BufferTarget.UniformBuffer, String.Format("layersStd140_ubo_{0}", map))
            BufferStorage(.layersStd140_ubo,
                          Marshal.SizeOf(layersBuffer),
                          layersBuffer,
                          BufferStorageFlags.None)
        End With
    End Sub
    Private Function get_atlas(ByVal mipcount As Integer, map As Int32, z As Int32, format As SizedInternalFormat) As GLTexture
        Dim t = New GLTexture
        't.target = TextureTarget.Texture2DArray
        t = CreateTexture(TextureTarget.Texture2DArray, "tAtlas" + map.ToString + "_" + z.ToString)
        t.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
        t.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        t.Parameter(TextureParameterName.TextureBaseLevel, 0)
        t.Parameter(TextureParameterName.TextureMaxLevel, mipcount - 1)
        t.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        t.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
        t.Storage3D(mipcount - 1, format, 1024, 1024, 4)
        Return t

    End Function
    Private Sub draw_test_iamge(w As Integer, h As Integer, id As GLTexture, atlas As Boolean)

        Dim ww = frmMain.glControl_main.ClientRectangle.Width

        Dim ls = (1920.0F - ww) / 2.0F

        ' Draw Terra Image
        draw_image_rectangle(New RectangleF(0, 0, w, h), id, atlas)

        frmMain.glControl_main.SwapBuffers()
    End Sub


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
            For i = 0 To 7
                .layer.layer_section_size(i) = br.ReadUInt32

                If .layer.layer_section_size(i) > max_on Then max_on = .layer.layer_section_size(i)
                If .layer.layer_section_size(i) > 0 Then
                    If .layer.layer_section_size(i) < min_on Then min_on = .layer.layer_section_size(i)
                End If

            Next
            ReDim .layer.render_info(map_count)

            ReDim Preserve .layer.render_info(7)
            For i = 0 To map_count - 1
                .layer.render_info(i) = New layer_render_info_entry_
                br.ReadUInt32() 'magic
                .layer.render_info(i).width = br.ReadUInt32
                .layer.render_info(i).height = br.ReadUInt32
                .layer.render_info(i).count = br.ReadUInt32 ' always 8
                If .layer.render_info(i).count <> 8 Then Stop

                'texture projection transforms
                .layer.render_info(i).u.X = -round_4(br.ReadSingle)
                .layer.render_info(i).u.Y = 0.0
                br.ReadSingle()
                .layer.render_info(i).u.Z = round_4(br.ReadSingle)
                .layer.render_info(i).u.W = br.ReadSingle

                .layer.render_info(i).v.X = round_4(br.ReadSingle)
                .layer.render_info(i).v.Y = 0.0
                br.ReadSingle()
                .layer.render_info(i).v.Z = -round_4(br.ReadSingle)
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
            GC.Collect()

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
                sb.AppendLine(String.Format("MAP ID {0}", map.ToString))
            End If

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

                    .TexLayers(i).AM_name1 = ""
                    .TexLayers(i).AM_name2 = ""
                    .TexLayers(i).NM_name1 = ""
                    .TexLayers(i).NM_name2 = ""

                    'get first tex name
                    Dim bs = br2.ReadUInt32
                    Dim d = br2.ReadBytes(bs)

                    .TexLayers(i).AM_name1 = Encoding.UTF8.GetString(d, 0, d.Length)
                    .TexLayers(i).NM_name1 = .TexLayers(i).AM_name1.Replace("AM.dds", "NM.dds")

                    If tex_cnt > 1 Then
                        'get 2nd tex name if it exist
                        bs = br2.ReadUInt32
                        d = br2.ReadBytes(bs)

                        .TexLayers(i).AM_name2 = Encoding.UTF8.GetString(d, 0, d.Length)
                        .TexLayers(i).NM_name2 = .TexLayers(i).AM_name2.Replace("AM.dds", "NM.dds")

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
                        sb.AppendLine(String.Format("A {0} --------------------------------------------", cur_layer_info_pnt.ToString))

                        If .TexLayers(i).AM_name1 = "" Then
                            sb.AppendLine("-= EMPTY =-")
                        Else
                            sb.AppendLine(.TexLayers(i).AM_name1)
                        End If

                        sb.Append(String.Format("{0,-8}", "uP1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).u)

                        sb.Append(String.Format("{0,-8}", "vP1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).v)

                        sb.Append(String.Format("{0,-8}", "v1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).v1)

                        sb.Append(String.Format("{0,-8}", "r1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).r1)

                        sb.Append(String.Format("{0,-8}", "r2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).r2)

                        sb.Append(String.Format("{0,-8}", "Scale"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 0).scale)

                        ' part 2 ======================================================
                        sb.AppendLine(String.Format("B {0} --------------------------------------------", cur_layer_info_pnt + 1.ToString))
                        If .TexLayers(i).AM_name2 = "" Then
                            sb.AppendLine("-= EMPTY =-")
                        Else
                            sb.AppendLine(.TexLayers(i).AM_name2)
                        End If

                        sb.Append(String.Format("{0,-8}", "uP2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).u)

                        sb.Append(String.Format("{0,-8}", "vP2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).v)

                        sb.Append(String.Format("{0,-8}", "v1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).v1)

                        sb.Append(String.Format("{0,-8}", "r1"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).r1)

                        sb.Append(String.Format("{0,-8}", "r2"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).r2)

                        sb.Append(String.Format("{0,-8}", "Scale"))
                        write_vec4(.layer.render_info(cur_layer_info_pnt + 1).scale)
                        '=============================================================
                    End If

                    cur_layer_info_pnt += 2
                    .layer_count += 1
                End If
            Next
            sb.AppendLine("")

            ms2.Dispose()
            GC.Collect()
        End With

        Return True
    End Function

    Private Function round_4(ByVal v As Single)
        Return Math.Round(v, 2)
    End Function

    Private Sub write_vec4(ByRef v As Vector4)
        sb.AppendLine(String.Format("{0,-8:F4} {1,-8:F4} {2,-8:F4} {3,-8:F4}",
                                 v.X.ToString, v.Y.ToString, v.Z.ToString, v.W.ToString))
    End Sub
    'Public Function find_and_trim(ByRef fn1 As String) As GLTexture
    '    finds And loads And returns the GL texture ID.
    '    Dim id = image_exists(fn1) 'Check if this has been loaded already.
    '    If id IsNot Nothing Then
    '        Return id
    '    End If
    '    Dim entry As ZipEntry = search_pkgs(fn1)
    '    If entry IsNot Nothing Then
    '        Dim ms As New MemoryStream
    '        entry.Extract(ms)
    '        CHANGE THIS TO crop_DDS to use code below.
    '        id = load_dds_image_from_stream(ms, fn)
    '        id = load_terrain_texture_from_stream(ms, fn1)
    '        Return id
    '    End If
    '    Return Nothing
    'End Function
    'Public Function load_terrain_texture_from_stream(ms As MemoryStream, fn As String) As GLTexture
    '    'Check if this image has already been loaded.
    '    Dim image_id = image_exists(fn)
    '    If image_id IsNot Nothing Then
    '        Debug.WriteLine(fn)
    '        Return image_id
    '    End If
    '    Dim e1 = GL.GetError()

    '    ms.Position = 0
    '    Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
    '        Dim dds_header = get_dds_header(br)
    '        ms.Position = 128

    '        image_id = CreateTexture(TextureTarget.Texture2D, fn)

    '        'If image_id = 356 Then Stop
    '        Dim maxAniso As Single = 4 'GLCapabilities.maxAniso
    '        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(dds_header.width, dds_header.height), 2))

    '        Dim format_info = dds_header.format_info
    '        If dds_header.mipMapCount = 0 Or dds_header.mipMapCount = 1 Then
    '            image_id.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
    '            'image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
    '            image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
    '            image_id.Parameter(TextureParameterName.TextureMaxLevel, numLevels)
    '            image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
    '            image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
    '            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
    '            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
    '            image_id.Storage2D(numLevels, format_info.texture_format, dds_header.width, dds_header.height)

    '            Dim size As Integer
    '            If format_info.compressed Then
    '                size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
    '            Else
    '                size = dds_header.width * dds_header.height * format_info.components
    '            End If
    '            Dim data = br.ReadBytes(size)

    '            If format_info.compressed Then
    '                image_id.CompressedSubImage2D(0, 0, 0, dds_header.width, dds_header.height, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
    '            Else
    '                image_id.SubImage2D(0, 0, 0, dds_header.width, dds_header.height, format_info.pixel_format, format_info.pixel_type, data)
    '            End If

    '            'added 10/4/2020
    '            image_id.GenerateMipmap()

    '        Else


    '            image_id.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
    '            'image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
    '            image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
    '            image_id.Parameter(TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
    '            image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
    '            image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
    '            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
    '            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
    '            image_id.Storage2D(dds_header.mipMapCount, format_info.texture_format, dds_header.width, dds_header.height)

    '            Dim w = dds_header.width
    '            Dim h = dds_header.height
    '            Dim mipMapCount = dds_header.mipMapCount

    '            For i = 0 To dds_header.mipMapCount - 1
    '                If (w = 0 Or h = 0) Then
    '                    mipMapCount -= 1
    '                    Continue For
    '                End If

    '                Dim size As Integer
    '                If format_info.compressed Then
    '                    size = ((w + 3) \ 4) * ((h + 3) \ 4) * format_info.components
    '                Else
    '                    size = w * h * format_info.components
    '                End If
    '                Dim data = br.ReadBytes(size)

    '                If format_info.compressed Then
    '                    image_id.CompressedSubImage2D(i, 0, 0, w, h, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
    '                Else
    '                    image_id.SubImage2D(i, 0, 0, w, h, format_info.pixel_format, format_info.pixel_type, data)
    '                End If

    '                w /= 2
    '                h /= 2
    '            Next
    '            image_id.Parameter(TextureParameterName.TextureMaxLevel, mipMapCount - 1)
    '        End If

    '        Dim e2 = GL.GetError()
    '        If e2 > 0 Then
    '            Stop
    '        End If
    '    End Using
    '    If fn.Length = 0 Then Return image_id
    '    add_image(fn, image_id)
    '    Return image_id
    'End Function

    Private Function crop_DDS(ByRef ms As MemoryStream, ByRef fn As String) As GLTexture
        'File name is needed to add to our list of loaded textures

        ms.Position = 0

        GC.Collect()
        GC.WaitForFullGCComplete()

        Dim imgStore(ms.Length) As Byte
        ms.Read(imgStore, 0, ms.Length)

        Dim texID As UInt32
        texID = Ilu.iluGenImage()
        Il.ilBindImage(texID)

        Dim success = Il.ilGetError
        Il.ilLoadL(Il.IL_DDS, imgStore, ms.Length)
        success = Il.ilGetError

        Dim CROP As Boolean = True

        If success = Il.IL_NO_ERROR Then
            'Ilu.iluFlipImage()
            'Ilu.iluRotate(90.0F)
            Ilu.iluMirror()
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)
            Dim ul = CInt(width * 0.0625)
            Dim lr = CInt(width * 0.875)

            If CROP Then
                Ilu.iluCrop(ul, ul, 0, lr, lr, 1)
            End If

            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE)
            Dim result = Il.ilConvertImage(Il.IL_RGBA, Il.IL_UNSIGNED_BYTE)

            Dim image_id = CreateTexture(TextureTarget.Texture2D, fn)

            Dim maxAniso As Single = 3.0F

            image_id.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)

            image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)

            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)


            If CROP Then
                image_id.Storage2D(6, SizedInternalFormat.Rgba8, CInt(width * 0.875), CInt(height * 0.875))
                image_id.SubImage2D(0, 0, 0, CInt(width * 0.875), CInt(height * 0.875), OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())
            Else
                image_id.Storage2D(6, SizedInternalFormat.Rgba8, CInt(width), CInt(height))
                image_id.SubImage2D(0, 0, 0, CInt(width), CInt(height), OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())
            End If

            image_id.GenerateMipmap()

            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)

            add_image(fn, image_id)

            Return image_id
        Else
            MsgBox("Failed to load @ crop_DDS", MsgBoxStyle.Exclamation, "Shit!!")
        End If
        Return Nothing

    End Function
End Module

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Ionic
Imports Ionic.Zip
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module TerrainTextureFunctions
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
        For i = 0 To 3
            With theMap.render_set(map).TexLayers(i)
                If .AM_name1 = "" Then
                    .AM_id1 = DUMMY_TEXTURE_ID
                    .NM_id1 = DUMMY_TEXTURE_ID
                    .used_a = 0.0F
                Else
                    .AM_id1 = find_and_trim(.AM_name1)
                    .NM_id1 = find_and_trim(.NM_name1)
                    .used_a = 1.0F
                End If
                If .AM_name2 = "" Then
                    .AM_id2 = DUMMY_TEXTURE_ID
                    .NM_id2 = DUMMY_TEXTURE_ID
                    .used_b = 0.0F
                Else
                    .AM_id2 = find_and_trim(.AM_name2)
                    .NM_id2 = find_and_trim(.NM_name2)
                    .used_b = 1.0F
                End If

            End With
        Next

        ' fill ubo
        With theMap.render_set(map)
            Dim layersBuffer As New LayersStd140
            layersBuffer.layer0UT1 = .TexLayers(0).uP1
            layersBuffer.layer0UT2 = .TexLayers(0).uP2

            layersBuffer.layer0VT1 = .TexLayers(0).vP1
            layersBuffer.layer0VT2 = .TexLayers(0).vP2

            layersBuffer.layer1UT1 = .TexLayers(1).uP1
            layersBuffer.layer1UT2 = .TexLayers(1).uP2

            layersBuffer.layer1VT1 = .TexLayers(1).vP1
            layersBuffer.layer1VT2 = .TexLayers(1).vP2

            layersBuffer.layer2UT1 = .TexLayers(2).uP1
            layersBuffer.layer2UT2 = .TexLayers(2).uP2

            layersBuffer.layer2VT1 = .TexLayers(2).vP1
            layersBuffer.layer2VT2 = .TexLayers(2).vP2

            layersBuffer.layer3UT1 = .TexLayers(3).uP1
            layersBuffer.layer3UT2 = .TexLayers(3).uP2

            layersBuffer.layer3VT1 = .TexLayers(3).vP1
            layersBuffer.layer3VT2 = .TexLayers(3).vP2

            ' Used 1 = true, 0 = false
            layersBuffer.used_1 = .TexLayers(0).used_a
            layersBuffer.used_2 = .TexLayers(0).used_b

            layersBuffer.used_3 = .TexLayers(1).used_a
            layersBuffer.used_4 = .TexLayers(1).used_b

            layersBuffer.used_5 = .TexLayers(2).used_a
            layersBuffer.used_6 = .TexLayers(2).used_b

            layersBuffer.used_7 = .TexLayers(3).used_a
            layersBuffer.used_8 = .TexLayers(3).used_b

            .layersStd140_ubo = CreateBuffer(BufferTarget.UniformBuffer, String.Format("layersStd140_ubo_{0}", map))
            BufferStorage(.layersStd140_ubo,
                          Marshal.SizeOf(layersBuffer),
                          layersBuffer,
                          BufferStorageFlags.None)
        End With
    End Sub


    Public Function Get_layer_texture_data(ByVal map As Integer) As Boolean

        cur_layer_info_pnt = 0

        '---------------------------------------------------------------------
        'lets get the layer render info first
        '---------------------------------------------------------------------
        Dim ms As New MemoryStream(theMap.chunks(map).layers_data)
        Dim br As New BinaryReader(ms)
        With theMap.render_set(map)

            Dim magic = br.ReadUInt32
            Dim map_count = br.ReadUInt32
            ReDim .layer.used_on(7)
            For i = 0 To 7
                .layer.used_on(i) = br.ReadUInt32
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
                .layer.render_info(i).u.X = br.ReadSingle
                .layer.render_info(i).u.Y = br.ReadSingle
                .layer.render_info(i).u.Z = -br.ReadSingle
                .layer.render_info(i).u.W = br.ReadSingle

                .layer.render_info(i).v.X = -br.ReadSingle
                .layer.render_info(i).v.Y = br.ReadSingle
                .layer.render_info(i).v.Z = br.ReadSingle
                .layer.render_info(i).v.W = br.ReadSingle

                .layer.render_info(i).flags = br.ReadUInt32 'always 59
                If .layer.render_info(i).flags <> 59 Then Stop

                'not sure about these 3' Atlas offsets?
                .layer.render_info(i).v1.X = br.ReadSingle
                .layer.render_info(i).v1.Y = br.ReadSingle
                .layer.render_info(i).v1.Z = br.ReadSingle

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
            Dim section_cnt = br2.ReadUInt32
            section_cnt = 4
            Dim sec_sizes(3) As UInt32
            For i = 0 To 3
                sec_sizes(i) = br2.ReadUInt32
            Next
            'ReDim .TexLayers(section_cnt)
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

                    'If .TexLayers(i).AM_name1 <> .dom_tex_list(cur_layer_info_pnt) Then
                    '    Stop
                    'End If
                    'layer part 1
                    'scaleX = .sqrt((a * a) + (c * c));
                    Dim u = .layer.render_info(cur_layer_info_pnt + 0).u
                    Dim v = .layer.render_info(cur_layer_info_pnt + 0).v

                    Dim scaleX = Math.Sqrt(u.X * u.X + u.Z * u.Z)
                    Dim scaley = Math.Sqrt(v.X * v.X + v.Z * v.Z)
                    'Debug.Write("(" + cur_layer_info_pnt.ToString + ") ")
                    'Debug.WriteLine(scaleX.ToString + " ", scaley.ToString)

                    u = .layer.render_info(cur_layer_info_pnt + 0).u
                    v = .layer.render_info(cur_layer_info_pnt + 0).v

                    scaleX = Math.Sqrt(u.X * u.X + u.Z * u.Z)
                    scaley = Math.Sqrt(v.X * v.X + v.Z * v.Z)
                    'Debug.Write("(" + CInt(cur_layer_info_pnt + 1).ToString + ") ")
                    'Debug.WriteLine(scaleX.ToString + " ", scaley.ToString)


                    .TexLayers(i).uP1 = .layer.render_info(cur_layer_info_pnt + 0).u
                    .TexLayers(i).vP1 = .layer.render_info(cur_layer_info_pnt + 0).v
                    '.TexLayers(i).scale_a = .layer.render_info(cur_layer_info_pnt + 0).scale
                    'layer part 2
                    .TexLayers(i).uP2 = .layer.render_info(cur_layer_info_pnt + 1).u
                    .TexLayers(i).vP2 = .layer.render_info(cur_layer_info_pnt + 1).v
                    '.TexLayers(i).scale_b = .layer.render_info(cur_layer_info_pnt + 1).scale

                    cur_layer_info_pnt += 2
                    .layer_count += 1
                End If
            Next

            ms2.Dispose()
            GC.Collect()
        End With

        Return True
    End Function

    Public Function find_and_trim(ByRef fn As String) As GLTexture
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn) 'Check if this has been loaded already.
        If id IsNot Nothing Then
            Return id
        End If
        Dim entry As ZipEntry = search_pkgs(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'CHANGE THIS TO crop_DDS to use code below.
            'id = load_dds_image_from_stream(ms, fn)
            id = crop_DDS(ms, fn)
            Return id
        End If
        Return Nothing
    End Function

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
            'Ilu.iluMirror()
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
            'GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, maxAniso)

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

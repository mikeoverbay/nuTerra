
Imports System
Imports System.IO
Imports System.Text
Imports Ionic.Zip
Imports Tao.DevIl
Imports System.Xml
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports OpenTK.Graphics
Module TerrainTextureFunctions
    Dim cur_layer_info_pnt As Integer = 0

    Public Sub get_layers(ByVal map As Integer)
        'There can be as many as 4 TexLayer sets.
        'Each contains the blend map and the 1 or 2 textures
        'that belong to that blend set.

        With theMap.render_set(map)
            ReDim .TexLayers(3)
            .layers_mask = 0 'Controls mixing of the map blends
            .layer_count = 0 'How many layer sets are there
            .TexLayers(0).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(1).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(2).Blend_id = DUMMY_TEXTURE_ID
            .TexLayers(3).Blend_id = DUMMY_TEXTURE_ID
        End With
        Get_layer_texture_data(map) 'get all the data

        'we have the data so lets get the textures.
        get_layer_textures(map)
    End Sub


    Private Sub get_layer_textures(ByVal map As Integer)
        'It is important to fill blank IDs with the dummy texture
        'so the shader has VALID data even if its empty data.
        For i = 0 To theMap.render_set(map).layer_count
            With theMap.render_set(map).TexLayers(i)
                If .AM_name1 = "" Then
                    .AM_id1 = DUMMY_TEXTURE_ID
                    .NM_id1 = DUMMY_TEXTURE_ID
                Else
                    .AM_id1 = find_and_trim(.AM_name1)
                    .NM_id1 = find_and_trim(.NM_name1)
                End If
                If .AM_name2 = "" Then
                    .AM_id2 = DUMMY_TEXTURE_ID
                    .NM_id2 = DUMMY_TEXTURE_ID
                Else
                    .AM_id2 = find_and_trim(.AM_name2)
                    .NM_id2 = find_and_trim(.NM_name2)

                End If

            End With
        Next
        With theMap.render_set(map)

            Dim mask As Integer = 0
            If .TexLayers(0).AM_name1 <> "" Then
                mask = mask Or 1
            End If
            If .TexLayers(0).AM_name2 <> "" Then
                mask = mask Or 2
            End If
            If .TexLayers(1).AM_name1 <> "" Then
                mask = mask Or 4
            End If
            If .TexLayers(1).AM_name2 <> "" Then
                mask = mask Or 8
            End If
            If .TexLayers(2).AM_name1 <> "" Then
                mask = mask Or 16
            End If
            If .TexLayers(2).AM_name2 <> "" Then
                mask = mask Or 32
            End If
            If .TexLayers(3).AM_name1 <> "" Then
                mask = mask Or 64
            End If
            If .TexLayers(3).AM_name2 <> "" Then
                mask = mask Or 128
            End If
            .layers_mask = mask
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
                .layer.render_info(i).count = br.ReadUInt32

                .layer.render_info(i).u.R = br.ReadSingle
                .layer.render_info(i).u.G = br.ReadSingle
                .layer.render_info(i).u.B = br.ReadSingle
                .layer.render_info(i).u.A = br.ReadSingle

                .layer.render_info(i).v.R = br.ReadSingle
                .layer.render_info(i).v.G = br.ReadSingle
                .layer.render_info(i).v.B = br.ReadSingle
                .layer.render_info(i).v.A = br.ReadSingle

                .layer.render_info(i).flags = br.ReadUInt32

                'not sure about these 3
                .layer.render_info(i).v1.r = br.ReadSingle
                .layer.render_info(i).v1.g = br.ReadSingle
                .layer.render_info(i).v1.b = br.ReadSingle

                .layer.render_info(i).r1.R = br.ReadSingle
                .layer.render_info(i).r1.G = br.ReadSingle
                .layer.render_info(i).r1.B = br.ReadSingle
                .layer.render_info(i).r1.A = br.ReadSingle

                .layer.render_info(i).r2.R = br.ReadSingle
                .layer.render_info(i).r2.G = br.ReadSingle
                .layer.render_info(i).r2.B = br.ReadSingle
                .layer.render_info(i).r2.A = br.ReadSingle

                'not sure about these
                .layer.render_info(i).scale.R = br.ReadSingle
                .layer.render_info(i).scale.G = br.ReadSingle
                .layer.render_info(i).scale.B = br.ReadSingle
                .layer.render_info(i).scale.A = br.ReadSingle

                Dim bs = br.ReadUInt32
                Dim d = br.ReadBytes(bs)
                .layer.render_info(i).texture_name = Encoding.UTF8.GetString(d, 0, d.Length)
                br.ReadByte()

            Next
            ms.Dispose()
            GC.Collect()



            Dim ms2 As New MemoryStream(theMap.chunks(map).blend_textures_data)
            ms2.Position = 0
            If map = 7 Then
                'Stop
            End If
            Dim br2 As New BinaryReader(ms2)

            Dim magic2 = br2.ReadUInt32()
            Dim section_cnt = br2.ReadUInt32
            section_cnt = 4
            Dim sec_sizes(section_cnt - 1) As UInt32
            For i = 0 To section_cnt - 1
                sec_sizes(i) = br2.ReadUInt32
            Next
            ReDim .TexLayers(section_cnt)
            For i = 0 To section_cnt - 1
                .TexLayers(i) = New ids_
                Dim len = sec_sizes(i)
                If len > 0 Then
                    '.imageData(i).data = br.ReadBytes(len) 'need to just processe the data and not make a buffer

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
                    Dim bs = br2.ReadUInt32
                    Dim d = br2.ReadBytes(bs)
                    .TexLayers(i).AM_name1 = Encoding.UTF8.GetString(d, 0, d.Length)
                    .TexLayers(i).NM_name1 = .TexLayers(i).AM_name1.Replace("AM.dds", "NM.dds")
                    If tex_cnt > 1 Then
                        bs = br2.ReadUInt32
                        d = br2.ReadBytes(bs)
                        .TexLayers(i).AM_name2 = Encoding.UTF8.GetString(d, 0, d.Length)
                        .TexLayers(i).NM_name2 = .TexLayers(i).AM_name2.Replace("AM.dds", "NM.dds")
                    End If
                    .TexLayers(i).Blend_id = load_t2_texture_from_stream(br2, .b_x_size, .b_y_size)
                    .TexLayers(i).uP1 = .layer.render_info(cur_layer_info_pnt).u
                    .TexLayers(i).vP1 = .layer.render_info(cur_layer_info_pnt).v
                    .TexLayers(i).color1 = .layer.render_info(cur_layer_info_pnt).scale
                    .TexLayers(i).uP2 = .layer.render_info(cur_layer_info_pnt + 1).u
                    .TexLayers(i).vP2 = .layer.render_info(cur_layer_info_pnt + 1).v
                    .TexLayers(i).color2 = .layer.render_info(cur_layer_info_pnt + 1).scale
                    cur_layer_info_pnt += 2
                    'Select Case i
                    '    Case 1
                    '        .layers_mask = .layers_mask Or 1
                    '    Case 2
                    '        .layers_mask = .layers_mask Or 2
                    '    Case 3
                    '        .layers_mask = .layers_mask Or 4
                    '    Case 4
                    '        .layers_mask = .layers_mask Or 8

                    'End Select
                    .layer_count += 1
                End If
            Next
            ms2.Dispose()
            GC.Collect()
        End With

        Return True
    End Function


    Public Function find_and_trim(ByRef fn As String) As Integer
        fn = fn.Replace("\", "/") ' fix path issue
        'finds and loads and returns the GL texture ID.
        fn = fn.Replace(".png", ".dds")
        fn = fn.Replace(".atlas", ".atlas_processed")
        Dim id = image_exists(fn) 'check if this has been loaded.
        If id > 0 Then
            Return id
        End If
        Dim entry As ZipEntry = search_pkgs(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'CHANGE THIS TO crop_DDS to use code below.
            id = load_dds_image_from_stream(ms, fn)
            Return id
        End If
        Return -1 ' Didn't find it, return -1
    End Function

    Private Function crop_DDS(ByVal type As Integer, ByRef ms As MemoryStream, ByRef fn As String)
        Dim image_id As Integer

        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Debug.Assert(br.ReadChars(4) = "DDS ") ' file_code
            Debug.Assert(br.ReadUInt32() = 124) ' size of the header
            br.ReadUInt32() ' flags
            Dim height = br.ReadInt32()
            Dim width = br.ReadInt32()
            br.ReadUInt32() ' pitchOrLinearSize
            br.ReadUInt32() ' depth
            Dim mipMapCount = br.ReadInt32()
            br.ReadBytes(44) ' reserved1
            br.ReadUInt32() ' Size
            br.ReadUInt32() ' Flags
            Dim FourCC = br.ReadChars(4)
            br.ReadUInt32() ' RGBBitCount
            br.ReadUInt32() ' RBitMask
            br.ReadUInt32() ' GBitMask
            br.ReadUInt32() ' BBitMask
            br.ReadUInt32() ' ABitMask
            Dim caps = br.ReadUInt32()

            Select Case caps
                Case &H1000
                    Debug.Assert(mipMapCount = 0) ' Just Check
                Case &H401008
                    Debug.Assert(mipMapCount > 0) ' Just Check
                Case Else
                    Debug.Assert(False) ' Cubemap ?
            End Select

            Dim format As SizedInternalFormat
            Dim blockSize As Integer

            Select Case FourCC
                Case "DXT1"
                    format = InternalFormat.CompressedRgbaS3tcDxt1Ext
                    blockSize = 8
                Case "DXT3"
                    format = InternalFormat.CompressedRgbaS3tcDxt3Ext
                    blockSize = 16
                Case "DXT5"
                    format = InternalFormat.CompressedRgbaS3tcDxt5Ext
                    blockSize = 16
                Case Else ' DX10 ?
                    Debug.Assert(False, FourCC)
            End Select

            ms.Position = 128
            Dim size As Integer
            GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)
            Dim ULcorner = CInt(width * 0.0625F)
            Dim BRcorner = CInt(width * 0.875F)
            If mipMapCount = 0 Then
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.TextureStorage2D(image_id, 1, format, width, height)

                size = ((width + 3) \ 4) * ((height + 3) \ 4) * blockSize
                Dim data = br.ReadBytes(size)
                GL.CompressedTextureSubImage2D(image_id, 0, 0, 0, width, height, DirectCast(format, OpenGL.PixelFormat), Size, Data)
            Else
                size = ((width + 3) \ 4) * ((height + 3) \ 4) * blockSize
                Dim data = br.ReadBytes(size)
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, mipMapCount - 1)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.CompressedTextureSubImage2D(image_id, 0, ULcorner, ULcorner, BRcorner, BRcorner, DirectCast(format, OpenGL.PixelFormat), size, data)

                Dim w = width
                Dim h = height

                For i = 0 To mipMapCount - 1
                    If (w = 0 Or h = 0) Then
                        mipMapCount -= 1
                        Continue For
                    End If

                    size = ((w + 3) \ 4) * ((h + 3) \ 4) * blockSize
                    data = br.ReadBytes(size)
                    GL.CompressedTextureSubImage2D(image_id, i, 0, 0, w, h, DirectCast(format, OpenGL.PixelFormat), size, data)
                    w /= 2
                    h /= 2
                Next
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, mipMapCount - 1)
            End If
        End Using

        add_image(fn, image_id)
        Return image_id
    End Function
End Module

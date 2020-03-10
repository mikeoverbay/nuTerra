﻿
Imports System.Runtime.InteropServices
Imports System
Imports System.IO
Imports System.Text
Imports Ionic.Zip
Imports Tao.DevIl
Imports System.Xml
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports OpenTK.Graphics
Imports Ionic
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
        Get_layer_texture_data(map) 'get all the data

        'we have the data so lets get the textures.
        get_layer_textures(map)

        'get dom... for what I have no idea.
        get_dominate_texture(map)
    End Sub


    Private Sub get_layer_textures(ByVal map As Integer)
        'It is important to fill blank IDs with the dummy texture
        'so the shader has VALID data even if its empty data.
        For i = 0 To theMap.render_set(map).layer_count
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
                    .TexLayers(i).PBS_a = 0
                    .TexLayers(i).PBS_b = 0
                    Dim bs = br2.ReadUInt32
                    Dim d = br2.ReadBytes(bs)

                    .TexLayers(i).AM_name1 = Encoding.UTF8.GetString(d, 0, d.Length)
                    .TexLayers(i).NM_name1 = .TexLayers(i).AM_name1.Replace("AM.dds", "NM.dds")
                    If .TexLayers(i).NM_name1.Contains("PBS") Then
                        .TexLayers(i).PBS_a = 1
                    End If
                    If tex_cnt > 1 Then
                        bs = br2.ReadUInt32
                        d = br2.ReadBytes(bs)

                        .TexLayers(i).AM_name2 = Encoding.UTF8.GetString(d, 0, d.Length)
                        .TexLayers(i).NM_name2 = .TexLayers(i).AM_name2.Replace("AM.dds", "NM.dds")
                        If .TexLayers(i).NM_name2.Contains("PBS") Then
                            .TexLayers(i).PBS_b = 1
                        End If
                    End If
                    .TexLayers(i).Blend_id = load_t2_texture_from_stream(br2, .b_x_size, .b_y_size)
                    .TexLayers(i).uP1 = .layer.render_info(cur_layer_info_pnt).u
                    .TexLayers(i).vP1 = .layer.render_info(cur_layer_info_pnt).v

                    .TexLayers(i).uP2 = .layer.render_info(cur_layer_info_pnt + 1).u
                    .TexLayers(i).vP2 = .layer.render_info(cur_layer_info_pnt + 1).v

                    cur_layer_info_pnt += 2
                    .layer_count += 1
                End If
            Next
            ms2.Dispose()
            GC.Collect()
        End With

        Return True
    End Function

    Public Sub get_dominate_texture(ByVal map As Integer)

        Dim enc As New System.Text.ASCIIEncoding

        Dim ms As New MemoryStream(theMap.chunks(map).dominateTestures_data)
        ms.Position = 0
        Dim br As New BinaryReader(ms)

        Dim magic1 = br.ReadInt32
        Dim version = br.ReadInt32
        'unzip the data
        ms.Position = 0

        magic1 = br.ReadUInt32
        version = br.ReadUInt32

        Dim number_of_textures As Integer = br.ReadUInt32
        Dim texture_string_length As Integer = br.ReadUInt32

        Dim d_width As Integer = br.ReadUInt32
        Dim d_height As Integer = br.ReadUInt32
        br.ReadUInt64()
        ReDim theMap.render_set(map).dom_tex_list(number_of_textures)
        Dim s_buff(texture_string_length) As Byte
        For i = 0 To number_of_textures - 1
            s_buff = br.ReadBytes(texture_string_length)
            theMap.render_set(map).dom_tex_list(i) = enc.GetString(s_buff)
        Next



        Dim mg1 = br.ReadInt32
        Dim mg2 = br.ReadInt32
        Dim uncompressedsize = br.ReadInt32
        Dim buff(65536) As Byte
        Dim ps As New MemoryStream(buff)
        Dim count As UInteger = 0
        Dim total_read As Integer = 0
        Dim p_w As New StreamWriter(ps)

        Using Decompress As Zlib.ZlibStream = New Zlib.ZlibStream(ms, Zlib.CompressionMode.Decompress, False)
            Decompress.BufferSize = 65536
            Dim buffer(65536) As Byte
            Dim numRead As Integer
            numRead = Decompress.Read(buff, 0, buff.Length)
            total_read = numRead 'debug

        End Using

        Dim p_rd As New BinaryReader(ps)
        ReDim Preserve buff(total_read)
        Dim c_buff((total_read) * 4) As Byte

        Dim bb As New StringBuilder
        Dim cnt As Integer = 0
        Dim cnt2 As Integer = 0
        For i = 0 To total_read - 1
            'bb.Append(buff(i).ToString)
            c_buff(cnt + 0) = (buff(i) And 7) ' << 4
            c_buff(cnt + 1) = 0 '(buff(i) + 1 And 7) << 4
            c_buff(cnt + 2) = 0 '(buff(i) + 1 And 7) << 4
            c_buff(cnt + 3) = 255
            cnt += 4
            cnt2 += 1
            If cnt = 127 Then
                'bb.Append(vbCrLf)
            End If
        Next
        'done with these so dispose of them.
        p_rd.Close()
        ps.Dispose()
        br.Close()
        br.Dispose()
        ms.Dispose()

        Dim h, w As Integer
        w = d_width
        h = d_height
        Dim stride = (w / 2)
        count = 0
        'convert to 4 color data.

        '------------------------------------------------------------------
        'w = stride * 8 : h = h * 2
        'need point in to dbuff color buffer array
        Dim bufPtr As IntPtr = Marshal.AllocHGlobal(c_buff.Length - 1)
        Marshal.Copy(c_buff, 0, bufPtr, c_buff.Length - 1) ' copy dbuff to pufPtr's memory
        Dim texID = Ilu.iluGenImage() ' Generation of one image name
        Il.ilBindImage(texID) ' Binding of image name 
        Dim success = Il.ilGetError

        Il.ilTexImage(w, h, 1, 4, Il.IL_RGBA, Il.IL_UNSIGNED_BYTE, bufPtr) ' Create new image from pufPtr's data
        success = Il.ilGetError

        Marshal.FreeHGlobal(bufPtr) ' free this up

        If success = Il.IL_NO_ERROR Then
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)
            Dim f = Il.IL_FALSE
            Dim t = Il.IL_TRUE
            Ilu.iluMirror()

            GL.CreateTextures(TextureTarget.Texture2D, 1, theMap.render_set(map).dom_texture_id)

            GL.BindTexture(TextureTarget.Texture2D, theMap.render_set(map).dom_texture_id) ' bind the texture
            GL.TextureParameter(theMap.render_set(map).dom_texture_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TextureParameter(theMap.render_set(map).dom_texture_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)

            GL.TextureParameter(theMap.render_set(map).dom_texture_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TextureParameter(theMap.render_set(map).dom_texture_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TextureStorage2D(theMap.render_set(map).dom_texture_id, 1, SizedInternalFormat.Rgba8, width, height)
            GL.TextureSubImage2D(theMap.render_set(map).dom_texture_id, 0, 0, 0, width, height, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())

            GL.BindTexture(TextureTarget.Texture2D, 0) ' bind the texture
            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)
            'Stop
        Else
            MsgBox("Error Dom Texture! Il Error" + success.ToString, MsgBoxStyle.Exclamation, "Well Shit...")
        End If
    End Sub

    Public Function find_and_trim(ByRef fn As String) As Integer
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn) 'check if this has been loaded.
        If id > 0 Then
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
        Return -1 ' Didn't find it, return -1
    End Function

    Private Function crop_DDS(ByRef ms As MemoryStream, ByRef fn As String) As Integer
         'File name is needed to add to our list of loaded textures


        Dim image_id As Integer

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

        If success = Il.IL_NO_ERROR Then
            'Ilu.iluFlipImage()
            'Ilu.iluMirror()
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)
            Dim ul = CInt(width * 0.0625)
            Dim lr = CInt(width * 0.875)
            Ilu.iluCrop(ul, ul, 0, lr, lr, 1)
            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE)
            Dim result = Il.ilConvertImage(Il.IL_RGBA, Il.IL_UNSIGNED_BYTE)

            GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)

            Dim maxAniso As Single
            GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, maxAniso)

            GL.TextureParameter(image_id, DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)

            GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)

            GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TextureStorage2D(image_id, 6, SizedInternalFormat.Rgba8, CInt(width * 0.875), CInt(height * 0.875))
            GL.TextureSubImage2D(image_id, 0, 0, 0, CInt(width * 0.875), CInt(height * 0.875), OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())

            GL.GenerateTextureMipmap(image_id)

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

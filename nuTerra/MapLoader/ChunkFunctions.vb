Imports System.IO
Imports System.Math
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ChunkFunctions
    Dim b_x_min As Integer
    Dim b_x_max As Integer
    Dim b_y_min As Integer
    Dim b_y_max As Integer
    Public tl_, tr_, br_, bl_ As Vector3
    Public T_1, T_2, T_3, T_4 As Vector3
    Public Cursor_point As Vector3
    Public surface_normal As Vector3
    Public CURSOR_Y As Single
    Public normal_load_count As Integer

    Public Sub get_mesh(ByRef chunk As chunk_, ByRef v_data As terain_V_data_, ByRef r_set As chunk_render_data_)

        'good place as any to set bounding box
        v_data.BB_Max.X = chunk.location.X + 50
        v_data.BB_Min.X = chunk.location.X - 50
        v_data.BB_Max.Z = chunk.location.Y + 50
        v_data.BB_Min.Z = chunk.location.Y - 50
        get_translated_bb_terrain(v_data.BB, v_data)
        r_set.matrix = Matrix4.CreateTranslation(chunk.location.X, 0.0F, chunk.location.Y)

        ' 63 * 63 * 2  = 7938 indi count
        ' 64 * 64      = 4096 vert count
        Dim v_buff_XZ(4095) As Vector2
        Dim v_buff_Y(4095) As Single
        Dim h_buff(4095) As UInt32
        Dim uv_buff(4095) As Vector2
        Dim indicies(7937) As vect3_16
        Dim w As UInt32 = HEIGHTMAPSIZE 'bmp_w
        Dim h As UInt32 = HEIGHTMAPSIZE 'bmp_h
        Dim uvScale = (1.0# / 64.0#)
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / (64.0#)
        Dim stride = 64
        Dim cnt As UInt32 = 0
        For j = 0 To 62
            For i = 0 To 62
                indicies(cnt + 0).x = (i + 0) + ((j + 1) * stride) ' BL
                indicies(cnt + 0).y = (i + 1) + ((j + 0) * stride) ' TR
                indicies(cnt + 0).z = (i + 0) + ((j + 0) * stride) ' TL

                indicies(cnt + 1).x = (i + 0) + ((j + 1) * stride) ' BL
                indicies(cnt + 1).y = (i + 1) + ((j + 1) * stride) ' BR
                indicies(cnt + 1).z = (i + 1) + ((j + 0) * stride) ' TR
                cnt += 2
            Next
        Next

        cnt = 0
        For j = 0 To 62 Step 2
            For i = 0 To 63

                topleft.vert.X = (i) - w_
                topleft.H = v_data.heights((i), (j))
                topleft.vert.Y = (j) - h_
                topleft.uv.X = (i) * uvScale
                topleft.uv.Y = (j) * uvScale
                topleft.hole = v_data.holes(i, j)

                bottomleft.vert.X = (i) - w_
                bottomleft.H = v_data.heights((i), (j + 1))
                bottomleft.vert.Y = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                bottomleft.hole = v_data.holes(i, j + 1)

                '         I
                '  TL --------- TR
                '   |         . |
                '   |       .   |
                ' J |     .     | J
                '   |   .       |
                '   | .         |
                '   BL -------- BR
                '         I

                topleft.vert.X *= scale
                topleft.vert.Y *= scale

                bottomleft.vert.X *= scale
                bottomleft.vert.Y *= scale


                ' Fill the arrays
                v_buff_XZ(i + ((j + 1) * stride)) = bottomleft.vert
                v_buff_XZ(i + ((j + 0) * stride)) = topleft.vert

                v_buff_Y(i + ((j + 1) * stride)) = bottomleft.H
                v_buff_Y(i + ((j + 0) * stride)) = topleft.H

                h_buff(i + ((j + 1) * stride)) = bottomleft.hole
                h_buff(i + ((j + 0) * stride)) = topleft.hole

                uv_buff(i + ((j + 1) * stride)) = bottomleft.uv
                uv_buff(i + ((j + 0) * stride)) = topleft.uv

            Next
        Next
        Dim fill_buff As Boolean = False

        Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)

        ' SETUP ==================================================================
        'Gen VAO and VBO Ids
        GL.GenVertexArrays(1, r_set.VAO)
        GL.BindVertexArray(r_set.VAO)
        ReDim r_set.mBuffers(3)
        GL.GenBuffers(4, r_set.mBuffers)

        ' If the shared buffer is not defined, we need to do so.
        If theMap.vertex_vBuffer_id = 0 Then
            GL.GenBuffers(1, theMap.vertex_vBuffer_id)
            fill_buff = True
        End If

        ' VERTEX XZ ==================================================================
        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.vertex_vBuffer_id)
        'if the shared buffer is not defined, we need to fill the buffer now
        If fill_buff Then
            GL.BufferData(BufferTarget.ArrayBuffer,
                          v_buff_XZ.Length * 8,
                          v_buff_XZ, BufferUsageHint.StaticDraw)
        End If
        GL.VertexAttribPointer(0, 2,
                               VertexAttribPointerType.Float,
                               False, 8, 0)
        GL.EnableVertexAttribArray(0)

        ' POSITION Y ==================================================================
        GL.BindBuffer(BufferTarget.ArrayBuffer, r_set.mBuffers(1))
        GL.BufferData(BufferTarget.ArrayBuffer,
              v_buff_Y.Length * 4,
              v_buff_Y, BufferUsageHint.StaticDraw)

        GL.VertexAttribPointer(1, 1,
                            VertexAttribPointerType.Float,
                            False, 4, 0)
        GL.EnableVertexAttribArray(1)

        ' UV ==================================================================
        GL.BindBuffer(BufferTarget.ArrayBuffer, r_set.mBuffers(2))
        GL.BufferData(BufferTarget.ArrayBuffer,
              uv_buff.Length * 8,
              uv_buff, BufferUsageHint.StaticDraw)

        GL.VertexAttribPointer(2, 2,
                            VertexAttribPointerType.Float,
                            False, 8, 0)
        GL.EnableVertexAttribArray(2)

        ' NORMALS ==================================================================
        GL.BindBuffer(BufferTarget.ArrayBuffer, r_set.mBuffers(3))

        GL.BufferData(BufferTarget.ArrayBuffer,
              h_buff.Length * 4,
              h_buff, BufferUsageHint.StaticDraw)

        GL.VertexAttribPointer(3, 1,
                               VertexAttribPointerType.UnsignedInt,
                               False, 4, 0)
        GL.EnableVertexAttribArray(3)



        ' INDICES ==================================================================
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, r_set.mBuffers(0))
        GL.BufferData(BufferTarget.ElementArrayBuffer,
                          indicies.Length * 6,
                          indicies,
                          BufferUsageHint.StaticDraw)

        GL.BindVertexArray(0)
    End Sub

    Public Sub get_holes(ByRef c As chunk_, ByRef v As terain_V_data_)

        'Unpacks and creates hole data
        ReDim v.holes(63, 63)

        If Not c.has_holes Then
            Return
        End If

        Dim ms As New MemoryStream(c.holes_data)
        Dim br As New BinaryReader(ms)

        Dim magic1 = br.ReadInt32
        Dim magic2 = br.ReadInt32
        Dim uncompressedsize = br.ReadInt32
        Dim buff(uncompressedsize) As Byte
        Dim ps As New MemoryStream(buff)
        Dim count As UInteger = 0
        Dim total_read As Integer = 0
        'unzip the data
        Using Decompress As Zlib.ZlibStream = New Zlib.ZlibStream(ms, Zlib.CompressionMode.Decompress, False)
            Decompress.BufferSize = 65536
            Dim buffer(65536) As Byte
            Dim numRead As Integer
            numRead = Decompress.Read(buffer, 0, buffer.Length)
            total_read += numRead 'debug
            Do While numRead <> 0
                ps.Write(buffer, 0, numRead)
                numRead = Decompress.Read(buffer, 0, buffer.Length)
                total_read += numRead 'debug
            Loop
        End Using

        Dim p_rd As New BinaryReader(ps)
        ps.Position = 0
        magic1 = p_rd.ReadUInt32
        Dim w As UInt32 = p_rd.ReadUInt32 / 4
        Dim h As UInt32 = p_rd.ReadUInt32 / 2
        Dim version As UInt32 = p_rd.ReadUInt32
        Dim data(w * h) As Byte

        p_rd.Read(data, 0, w * h)

        Dim stride = 8
        count = 0
        If w = 8 Then ' nothing so retrun empty hole array
            ps.Dispose()
            ms.Dispose()
            Return

        End If
        'This will be used to punch holes
        'in the map to speed up rendering and allow for sub terrain items.
        'Each bit in the 8 bit grey scale 8 bit image is a hole.
        'We must bit shift >> 1 to get each value.
        For z1 = 0 To (h * 2) - 1
            For x1 = 0 To (stride) - 1
                Dim val = data((z1 * stride) + x1)
                For q = 0 To 7
                    Dim b = (1 And (val >> q))
                    If b > 0 Then b = 1
                    v.holes(63 - ((x1 * 8) + q), z1) = b
                Next
            Next
        Next

        c.holes_data = Nothing 'free memory
        ps.Dispose()
        ms.Dispose()

    End Sub

    Public Sub get_heights(ByRef c As chunk_, ByRef v As terain_V_data_)
        Dim r As New MemoryStream(c.heights_data)

        r.Position = 0
        ReDim v.BB(15)
        Dim f As New BinaryReader(r)
        Dim magic = f.ReadUInt32()
        Dim h_width = f.ReadUInt32
        Dim h_height = f.ReadUInt32
        Dim comp = f.ReadUInt32
        Dim version = f.ReadUInt32
        Dim h_min = f.ReadSingle
        Dim h_max = f.ReadSingle
        v.BB_Max.Y = h_max
        v.BB_Min.Y = h_min
        Dim crap = f.ReadUInt32
        Dim heaader = f.ReadUInt32
        Dim pos = r.Position


        Dim mapsize As UInt32
        Dim data(HEIGHTMAPSIZE * HEIGHTMAPSIZE * 4) As Byte
        Dim cnt As UInt32 = 0
        Using r
            r.Position = 36 'skip bigworld header stuff
            Dim rdr As New PngReader(r) ' create png from stream 's'
            Dim iInfo = rdr.ImgInfo
            mapsize = iInfo.Cols

            ReDim data(iInfo.Cols * iInfo.Cols * 4)
            Dim iline As ImageLine  ' create place to hold a scan line
            For i = 0 To iInfo.Cols - 1
                iline = rdr.GetRow(i)
                For j = 0 To iline.Scanline.Length - 1
                    'get the line and convert from word to byte and save in our buffer 'data'
                    Dim bytes() As Byte = BitConverter.GetBytes(iline.Scanline(j))
                    data(cnt) = iline.Scanline(j)
                    cnt += 1
                Next
            Next
            r.Close()
            r.Dispose()
        End Using
        Dim quantized As Single

        Dim ms As New MemoryStream(data, False)
        Dim br As New BinaryReader(ms)
        Dim sv, ev As Integer
        Dim ty As Integer
        If mapsize < 64 Then
            ReDim v.heights(64, 64)
            Dim div = 64 / (mapsize - 5)
            ReDim v.heights(64, 64)
            HEIGHTMAPSIZE = 64
            For j As UInt32 = 2 To mapsize - 4
                For i As UInt32 = 2 To mapsize - 4
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    sv = br.ReadInt32
                    ev = br.ReadInt32
                    For xp = (i - 2) * div To (((i + 1) - 2) * div)
                        Dim ii = (i - 2) * div
                        Dim xval As Single = (ev - sv) * ((xp - ii) / div)
                        v.heights(64 - xp, (j - 2) * div) = (xval + sv) * 0.001
                        ty = xp

                        ms.Position = (i * 4) + ((j + 1) * mapsize * 4)
                        ev = br.ReadInt32
                        For yp = (j - 2) * div To (((j + 1) - 2) * div)
                            Dim jj = (j - 2) * div
                            Dim yval As Single = (ev - sv) * ((yp - jj) / div)
                            v.heights(64 - xp, yp) = (yval + sv) * 0.001
                        Next
                    Next
                Next
            Next
        Else

            ReDim v.heights(HEIGHTMAPSIZE, HEIGHTMAPSIZE)
            For j As UInt32 = 3 To mapsize - 3
                For i As UInt32 = 3 To mapsize - 3
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    Dim tc = br.ReadInt32
                    quantized = tc * 0.001
                    v.heights(mapsize - i - 3, j - 3) = quantized
                Next
            Next
        End If

        Dim avg, y_max, y_min As Single
        For j As UInt32 = 0 To HEIGHTMAPSIZE - 1
            For i As UInt32 = 0 To HEIGHTMAPSIZE - 1
                avg += v.heights(i, j)
                If v.heights(i, j) < y_min Then
                    y_min = v.heights(i, j)
                End If
                If v.heights(i, j) > y_max Then
                    y_max = v.heights(i, j)
                End If
            Next
        Next
        c.heights_data = Nothing
        v.avg_heights = avg / (HEIGHTMAPSIZE ^ 2)
        br.Close()
        ms.Close()
        ms.Dispose()
        'End If
    End Sub


    Public Sub get_normals(ByRef c As chunk_, ByRef v As terain_V_data_,
                           ByRef render_set As chunk_render_data_, map As Integer)
        normal_load_count += 1

        Using br As New BinaryReader(New MemoryStream(c.normals_data))
            Dim header = br.ReadUInt32
            Dim version = br.ReadUInt32
            Dim x As UInt32 = br.ReadUInt16
            Dim y As UInt32 = br.ReadUInt16
            Dim unknown = br.ReadUInt32

            ' Just check
            Debug.Assert(header = 7172718) ' nrm
            Debug.Assert(version = 2)

            render_set.TerrainNormals_id = load_t2_normals_from_stream(br, "t2_normal_map", x, y)
        End Using

        Dim name = theMap.chunks(map).name
    End Sub

    Public Sub set_map_bs()
        b_x_max = -10000
        b_x_min = 10000
        b_y_max = -10000
        b_y_min = 10000
    End Sub

    Public Sub get_location_corner(ByRef c As chunk_)
        'This routine gets the maps location in the world grid from its name
        Dim x, y As Integer

        Dim a = c.name.ToCharArray
        If a(0) = "f" Then
            If AscW(a(3)) < 97 Then a(3) = ChrW(AscW(a(3)) + 39)
            x = AscW("f") - AscW(a(3))  '+ 1
            c.location.X = ((AscW("f") - AscW(a(3))) * 100.0) + 50.0
        Else
            If a(0) = "0" Then
                x = AscW(a(3)) - AscW("0") + 1
                c.location.X = ((AscW(a(3)) - AscW("0")) * -100.0) - 50.0
                x *= -1
            End If
        End If
        If a(4) = "f" Then
            If AscW(a(7)) < 97 Then a(7) = ChrW(AscW(a(7)) + 39)
            y = AscW("f") - AscW(a(7))  '+ 1
            c.location.Y = ((AscW("f") - AscW(a(7))) * -100.0) - 50
            y *= -1
        Else
            If a(4) = "0" Then
                y = AscW(a(7)) - AscW("0") + 1
                c.location.Y = ((AscW(a(7)) - AscW("0")) * 100.0) + 50
            End If
        End If
        If b_x_min > x Then b_x_min = x
        If b_x_max < x Then b_x_max = x
        If b_y_min > y Then b_y_min = y
        If b_y_max < y Then b_y_max = y

    End Sub

    Public Sub get_location(ByRef c As chunk_, ByVal map_id As Integer)
        'This routine gets the maps location in the world grid from its name
        Dim x, y As Integer

        Dim a = c.name.ToCharArray
        If a(0) = "f" Then
            If AscW(a(3)) < 97 Then a(3) = ChrW(AscW(a(3)) + 39)
            x = AscW("f") - AscW(a(3))  '+ 1
            c.location.X = ((AscW("f") - AscW(a(3))) * 100.0) + 50.0
        Else
            If a(0) = "0" Then
                x = AscW(a(3)) - AscW("0") + 1
                c.location.X = ((AscW(a(3)) - AscW("0")) * -100.0) - 50.0
                x *= -1
            End If
        End If
        If a(4) = "f" Then
            If AscW(a(7)) < 97 Then a(7) = ChrW(AscW(a(7)) + 39)
            y = AscW("f") - AscW(a(7))  '+ 1
            c.location.Y = ((AscW("f") - AscW(a(7))) * -100.0) - 50
            y *= -1
        Else
            If a(4) = "0" Then
                y = AscW(a(7)) - AscW("0") + 1
                c.location.Y = ((AscW(a(7)) - AscW("0")) * 100.0) + 50
            End If
        End If

        mapBoard(x + 10, y + 10).map_id = map_id
        mapBoard(x + 10, y + 10).location.X = c.location.X
        mapBoard(x + 10, y + 10).location.Y = c.location.Y
        mapBoard(x + 10, y + 10).abs_location.X = x
        mapBoard(x + 10, y + 10).abs_location.X = y
        mapBoard(x + 10, y + 10).occupied = True

    End Sub

    Private Sub get_translated_bb_terrain(ByRef BB() As Vector3, ByRef c As terain_V_data_)
        Dim v1, v2, v3, v4, v5, v6, v7, v8 As Vector3
        'created 8 corners
        With c
            v1.Z = .BB_Max.Z : v2.Z = .BB_Max.Z : v3.Z = .BB_Max.Z : v4.Z = .BB_Max.Z
            v5.Z = .BB_Min.Z : v6.Z = .BB_Min.Z : v7.Z = .BB_Min.Z : v8.Z = .BB_Min.Z

            v1.X = .BB_Min.X : v6.X = .BB_Min.X : v7.X = .BB_Min.X : v4.X = .BB_Min.X
            v5.X = .BB_Max.X : v8.X = .BB_Max.X : v3.X = .BB_Max.X : v2.X = .BB_Max.X

            v4.Y = .BB_Max.Y : v7.Y = .BB_Max.Y : v8.Y = .BB_Max.Y : v3.Y = .BB_Max.Y
            v6.Y = .BB_Min.Y : v5.Y = .BB_Min.Y : v1.Y = .BB_Min.Y : v2.Y = .BB_Min.Y
            'save the 8 corners
            .BB(0) = v1
            .BB(1) = v2
            .BB(2) = v3
            .BB(3) = v4
            .BB(4) = v5
            .BB(5) = v6
            .BB(6) = v7
            .BB(7) = v8
        End With


    End Sub

    Public Sub seam_map()
        Dim scale As Double = 100.0# / (64.0#)
        Dim uvinc As Double = 1.0# / 64.0#
        Dim u_start As Double = uvinc * 63.0#
        Dim almost1 As Double = 1.0

        Dim v_start As Double = u_start
        Dim v_end As Double = 1.0
        Dim y_pos As Integer = 0
        Dim x_pos As Integer = 0
        Dim yu, yl, xu, xl As Single
        Dim tl, bl, tr, br, cur_x, cur_y As Single
        Dim v_cnt As Integer = 0
        Dim buff(2) As vertex_data
        'Dim debug_string As String = ""

        BG_TEXT = "Creating Map Seams"
        BG_VALUE = 0
        BG_MAX_VALUE = 20 * 20
        Dim processed_count = 0
        Dim Valid_data As Boolean
        For mbX = 0 To 19

            For mbY = 0 To 19
                BG_VALUE = processed_count
                processed_count += 1
                Valid_data = False
                ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
                If mapBoard(mbX, mbY).occupied Then
                    ReDim buff(762) ' Max size.. some will be 1/2 this size
                    v_cnt = 0

                    yu = mapBoard(mbX, mbY).location.Y + 50
                    yl = yu - (1.0# * scale)
                    x_pos = 0.0#

                    If Not mapBoard(mbX, mbY + 1).occupied Then
                        GoTo endx
                    End If
                    Valid_data = True
                    u_start = 0

                    'HORIZONTAL SEAM
                    For x1 = mapBoard(mbX, mbY).location.X - 50 To _
                                                mapBoard(mbX, mbY).location.X + 50 - (scale * 2) Step 1 * scale

                        theMap.v_data(mapBoard(mbX, mbY).map_id).heights(x_pos, 64) =
                                    theMap.v_data(mapBoard(mbX, mbY + 1).map_id).heights(x_pos, 0)

                        topleft.vert.X = x1
                        topleft.vert.Y = yu
                        topleft.H = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).heights(x_pos, 0)
                        topleft.hole = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).holes(x_pos, 0)
                        topleft.uv.X = u_start
                        topleft.uv.Y = almost1

                        bottomleft.vert.X = x1
                        bottomleft.vert.Y = yl
                        bottomleft.H = theMap.v_data(mapBoard(mbX, mbY).map_id).heights(x_pos, 63)
                        bottomleft.hole = theMap.v_data(mapBoard(mbX, mbY).map_id).holes(x_pos, 63)
                        bottomleft.uv.X = u_start
                        bottomleft.uv.Y = almost1 - uvinc

                        topRight.vert.X = x1 + scale
                        topRight.vert.Y = yu
                        topRight.H = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).heights(x_pos + 1, 0)
                        topRight.hole = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).holes(x_pos + 1, 0)
                        topRight.uv.X = u_start + uvinc
                        topRight.uv.Y = almost1

                        bottomRight.vert.X = x1 + scale
                        bottomRight.vert.Y = yl
                        bottomRight.H = theMap.v_data(mapBoard(mbX, mbY).map_id).heights(x_pos + 1, 63)
                        bottomRight.hole = theMap.v_data(mapBoard(mbX, mbY).map_id).holes(x_pos + 1, 63)
                        bottomRight.uv.X = u_start + uvinc
                        bottomRight.uv.Y = almost1 - uvinc

                        'store in buffer
                        buff(v_cnt) = bottomleft : v_cnt += 1
                        buff(v_cnt) = topleft : v_cnt += 1
                        buff(v_cnt) = topRight : v_cnt += 1

                        buff(v_cnt) = bottomleft : v_cnt += 1
                        buff(v_cnt) = topRight : v_cnt += 1
                        buff(v_cnt) = bottomRight : v_cnt += 1

                        u_start += uvinc
                        x_pos += 1
                        cur_x = x1
                    Next
                    If Not mapBoard(mbX + 1, mbY).occupied Then
                        GoTo endx
                    End If
                    Valid_data = True

                    'CORNER
                    'these 3 positions was a pain to sort out :)
                    theMap.v_data(mapBoard(mbX, mbY).map_id).heights(64, 64) =
                        theMap.v_data(mapBoard(mbX + 1, mbY + 1).map_id).heights(0, 0) 'ok

                    theMap.v_data(mapBoard(mbX, mbY).map_id).heights(63, 64) =
                        theMap.v_data(mapBoard(mbX, mbY + 1).map_id).heights(63, 0) 'ok

                    theMap.v_data(mapBoard(mbX, mbY).map_id).heights(64, 63) =
                        theMap.v_data(mapBoard(mbX + 1, mbY).map_id).heights(0, 63) 'ok

                    topleft.vert.X = cur_x + scale
                    topleft.vert.Y = yl
                    topleft.H = theMap.v_data(mapBoard(mbX, mbY).map_id).heights(63, 63)
                    topleft.hole = theMap.v_data(mapBoard(mbX, mbY).map_id).holes(63, 63)
                    topleft.uv.X = almost1 - uvinc
                    topleft.uv.Y = almost1 - uvinc

                    topRight.vert.X = cur_x + (scale * 2)
                    topRight.vert.Y = yl
                    topRight.H = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).heights(0, 63)
                    topRight.hole = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).holes(0, 63)
                    topRight.uv.X = almost1
                    topRight.uv.Y = almost1 - uvinc

                    bottomleft.vert.X = cur_x + scale
                    bottomleft.vert.Y = yu
                    bottomleft.H = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).heights(63, 0)
                    bottomleft.hole = theMap.v_data(mapBoard(mbX, mbY + 1).map_id).holes(63, 0)
                    bottomleft.uv.X = almost1 - uvinc
                    bottomleft.uv.Y = almost1

                    bottomRight.vert.X = cur_x + (scale * 2)
                    bottomRight.vert.Y = yu
                    bottomRight.H = theMap.v_data(mapBoard(mbX + 1, mbY + 1).map_id).heights(0, 0)
                    bottomRight.hole = theMap.v_data(mapBoard(mbX + 1, mbY + 1).map_id).holes(0, 0)
                    bottomRight.uv.X = almost1
                    bottomRight.uv.Y = almost1

                    'store in buffer
                    buff(v_cnt) = topRight : v_cnt += 1
                    buff(v_cnt) = topleft : v_cnt += 1
                    buff(v_cnt) = bottomleft : v_cnt += 1

                    buff(v_cnt) = bottomleft : v_cnt += 1
                    buff(v_cnt) = bottomRight : v_cnt += 1
                    buff(v_cnt) = topRight : v_cnt += 1
endx:
                    If Not mapBoard(mbX + 1, mbY).occupied Then
                        GoTo endy
                    End If

                    Valid_data = True

                    xu = mapBoard(mbX, mbY).location.X + 50
                    xl = xu - (1 * scale)
                    cur_y = 0
                    y_pos = 0
                    v_start = 0
                    'VERTICAL SEAM
                    For y1 = mapBoard(mbX, mbY).location.Y - 50 To _
                              mapBoard(mbX, mbY).location.Y + 50 - (scale * 2) Step 1 * scale

                        theMap.v_data(mapBoard(mbX, mbY).map_id).heights(64, y_pos) =
                                theMap.v_data(mapBoard(mbX + 1, mbY).map_id).heights(0, y_pos + 1)

                        topleft.vert.X = xl
                        topleft.vert.Y = y1 + scale
                        topleft.H = theMap.v_data(mapBoard(mbX, mbY).map_id).heights(63, y_pos + 1)
                        topleft.hole = theMap.v_data(mapBoard(mbX, mbY).map_id).holes(63, y_pos + 1)
                        topleft.uv.X = almost1 - uvinc
                        topleft.uv.Y = v_start + uvinc

                        bottomleft.vert.X = xl
                        bottomleft.vert.Y = y1
                        bottomleft.H = theMap.v_data(mapBoard(mbX, mbY).map_id).heights(63, y_pos)
                        bottomleft.hole = theMap.v_data(mapBoard(mbX, mbY).map_id).holes(63, y_pos)
                        bottomleft.uv.X = almost1 - uvinc
                        bottomleft.uv.Y = v_start

                        topRight.vert.X = xu
                        topRight.vert.Y = y1 + scale
                        topRight.H = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).heights(0, y_pos + 1)
                        topRight.hole = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).holes(0, y_pos + 1)
                        topRight.uv.X = almost1
                        topRight.uv.Y = v_start + uvinc

                        bottomRight.vert.X = xu
                        bottomRight.vert.Y = y1
                        bottomRight.H = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).heights(0, y_pos)
                        bottomRight.hole = theMap.v_data(mapBoard(mbX + 1, mbY).map_id).holes(0, y_pos)
                        bottomRight.uv.X = almost1
                        bottomRight.uv.Y = v_start

                        'store in buffer
                        buff(v_cnt) = bottomleft : v_cnt += 1
                        buff(v_cnt) = topleft : v_cnt += 1
                        buff(v_cnt) = topRight : v_cnt += 1

                        buff(v_cnt) = bottomleft : v_cnt += 1
                        buff(v_cnt) = topRight : v_cnt += 1
                        buff(v_cnt) = bottomRight : v_cnt += 1
                        v_start += uvinc
                        y_pos += 1
                        cur_y = y1

                    Next
Endy:
                    If Valid_data Then
                        'Create this VAO ONLY if a seam was created!
                        '================================================================
                        '================================================================
                        '================================================================
                        'Create VBO
                        Dim MAP_ID = mapBoard(mbX, mbY).map_id

                        Dim v_buff_XZ(v_cnt - 1) As Vector2
                        Dim uv_buff(v_cnt - 1) As Vector2
                        Dim h_buff(v_cnt - 1) As UInt32
                        Dim v_buff_Y(v_cnt - 1) As Single

                        For i = 0 To v_cnt - 1
                            v_buff_XZ(i) = buff(i).vert
                            v_buff_Y(i) = buff(i).H
                            uv_buff(i) = buff(i).uv
                            h_buff(i) = buff(i).hole
                        Next

                        theMap.render_set(MAP_ID).S_tri_count = (v_cnt - 1) * 6
                        ' SETUP ==================================================================
                        'Gen VAO and VBO Ids
                        GL.GenVertexArrays(1, theMap.render_set(MAP_ID).S_VAO)
                        GL.BindVertexArray(theMap.render_set(MAP_ID).S_VAO)
                        ReDim theMap.render_set(MAP_ID).mBuffers(3)
                        GL.GenBuffers(4, theMap.render_set(MAP_ID).mBuffers)


                        ' VERTEX XZ ==================================================================
                        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.render_set(MAP_ID).mBuffers(0))
                        'if the shared buffer is not defined, we need to fill the buffer now
                        GL.BufferData(BufferTarget.ArrayBuffer,
                                          v_buff_XZ.Length * 8,
                                          v_buff_XZ, BufferUsageHint.StaticDraw)
                        GL.VertexAttribPointer(0, 2,
                                               VertexAttribPointerType.Float,
                                               False, 8, 0)
                        GL.EnableVertexAttribArray(0)

                        ' POSITION Y ==================================================================
                        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.render_set(MAP_ID).mBuffers(1))
                        GL.BufferData(BufferTarget.ArrayBuffer,
                              v_buff_Y.Length * 4,
                              v_buff_Y, BufferUsageHint.StaticDraw)

                        GL.VertexAttribPointer(1, 1,
                                            VertexAttribPointerType.Float,
                                            False, 4, 0)
                        GL.EnableVertexAttribArray(1)

                        ' UV ==================================================================
                        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.render_set(MAP_ID).mBuffers(2))
                        GL.BufferData(BufferTarget.ArrayBuffer,
                              uv_buff.Length * 8,
                              uv_buff, BufferUsageHint.StaticDraw)

                        GL.VertexAttribPointer(2, 2,
                                            VertexAttribPointerType.Float,
                                            False, 8, 0)
                        GL.EnableVertexAttribArray(2)

                        ' NORMALS ==================================================================
                        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.render_set(MAP_ID).mBuffers(3))

                        GL.BufferData(BufferTarget.ArrayBuffer,
                              h_buff.Length * 4,
                              h_buff, BufferUsageHint.StaticDraw)

                        GL.VertexAttribPointer(3, 1,
                                               VertexAttribPointerType.UnsignedInt,
                                               False, 4, 0)
                        GL.EnableVertexAttribArray(3)


                        GL.BindVertexArray(0)
                        '================================================================
                        '================================================================
                        '================================================================
                    End If
                End If
                draw_scene()
            Next 'mbY
        Next 'mbX

        'debug ascii mapping
        'debug_string += "   : "
        'For i = 0 To 19
        '    debug_string += i.ToString("00") + ": "
        'Next
        'debug_string += vbCrLf
        'For i = 0 To 19
        '    debug_string += i.ToString("00") + " : "
        '    For j = 0 To 19
        '        debug_string += mapBoard(j, i).map_id.ToString("000") + " "
        '    Next
        '    debug_string += vbCrLf
        'Next

    End Sub

    Public Function get_Y_at_XZ(ByVal Lx As Double, ByVal Lz As Double) As Single
        'If Not maploaded Then Return 100.0\
        If Not MAP_LOADED Or Not TERRAIN_LOADED Then
            Return 0
        End If
        If mapBoard Is Nothing Then Return 0.0F
        Dim tlx As Single = 100.0 / 64.0
        Dim tly As Single = 100.0 / 64.0
        Dim ts As Single = 64.0 / 100.0
        Dim tl, tr, br, bl, w As Vector3
        Dim xvp, yvp As Integer
        Dim ryp, rxp As Single
        'Dim mod_ = (MAP_SIDE_LENGTH) And 1
        For xo = 0 To 19
            For yo = 0 To 19
                If mapBoard(xo, yo).occupied Then

                    Dim px = mapBoard(xo, yo).location.X
                    If px - 50 < Lx And px + 50 >= Lx Then
                        xvp = xo
                        'Dim pz = mapBoard(xo, yo).location.Y
                        'If pz - 50 < Lz And pz + 50 >= Lz Then
                        '    yvp = yo
                        '    GoTo exit2
                        'End If
                        GoTo exit1
                    End If
                End If
            Next
        Next
exit1:
        For xo = 0 To 19
            For yo = 0 To 19
                If mapBoard(xo, yo).occupied Then
                    Dim pz = mapBoard(xo, yo).location.Y
                    If pz - 50 < Lz And pz + 50 >= Lz Then
                        yvp = yo
                        GoTo exit2
                    End If
                End If
            Next
        Next
exit2:

        'If maploaded Then
        '    Debug.Write("XP:" + xvp.ToString + "  ZP:" + yvp.ToString + vbCrLf)
        'End If
        'Dim msqrt = (MAP_SIDE_LENGTH / 2)

        Dim map = mapBoard(xvp, yvp).map_id
        'If maplist.Length - 1 < map Then
        '    Return eyeY
        'End If
        'If maplist(map).heights Is Nothing Then
        '    Return Z_Cursor
        'End If

        Dim vxp As Double = ((((Lx) / 100)) - Truncate((Truncate(Lx) / 100))) * 64.0
        Dim tx As Int32 = Round(Truncate(Lx / 100))
        Dim tz As Int32 = Round(Truncate(Lz / 100))
        If Lx < 0 Then
            tx += -1
        End If
        If Lz < 0 Then
            tz += -1
        End If
        Dim tx1 = (tx * 100)
        Dim tz1 = (tz * 100)

        Dim vyp As Double = ((((Lz) / 100)) - Truncate((Truncate(Lz) / 100))) * 64.0

        If vyp < 0.0 Then
            vyp = 64.0 + vyp
        End If
        If vxp < 0 Then
            vxp = 64.0 + vxp

        End If
        vxp = Round(vxp, 12)
        vyp = Round(vyp, 12)
        rxp = (Floor(vxp))
        rxp *= tlx
        ryp = Floor(vyp)
        ryp *= tlx
        'rxp = 64 + rxp
        w.X = (vxp * tlx)
        w.Y = (vyp * tlx)
        'vaid.x = w.X + maplist(map).location.x - 50.0
        'vaid.y = w.Y + maplist(map).location.y - 50.0
        Dim HX, HY, OX, OY As Integer
        HX = Floor(vxp)
        OX = 1
        HY = Floor(vyp)
        OY = 1
        'd_hx = HX
        'd_hy = HY
        Dim altitude As Single = 0.0
        'Try
        'look_point_Y = cp
        'w.Z = 1.0 'dont need this but who cares?
        If HX + OX > 64 Then
            Return 0
        End If
        tl.X = rxp
        tl.Y = ryp
        tl.Z = theMap.v_data(map).heights(HX, HY)

        tr.X = rxp + tlx
        tr.Y = ryp
        tr.Z = theMap.v_data(map).heights(HX + OX, HY)

        br.X = rxp + tlx
        br.Y = ryp + tlx
        br.Z = theMap.v_data(map).heights(HX + OX, HY + OY)

        bl.X = rxp
        bl.Y = ryp + tlx
        bl.Z = theMap.v_data(map).heights(HX, HY + OY)

        tr_ = tr
        br_ = br
        tl_ = tl
        bl_ = bl

        tr_.X += tx1
        br_.X += tx1
        tl_.X += tx1
        bl_.X += tx1

        tr_.Y += tz1
        br_.Y += tz1
        tl_.Y += tz1
        bl_.Y += tz1

        'for drawing the red square on the terrain
        T_1.X = tr.X + theMap.chunks(map).location.X - 50
        T_1.Y = tr.Y + theMap.chunks(map).location.Y - 50
        T_1.Z = tr.Z

        T_2.X = tl.X + theMap.chunks(map).location.X - 50
        T_2.Y = tl.Y + theMap.chunks(map).location.Y - 50
        T_2.Z = tl.Z

        T_3.X = br.X + theMap.chunks(map).location.X - 50
        T_3.Y = br.Y + theMap.chunks(map).location.Y - 50
        T_3.Z = br.Z

        T_4.X = bl.X + theMap.chunks(map).location.X - 50
        T_4.Y = bl.Y + theMap.chunks(map).location.Y - 50
        T_4.Z = bl.Z

        Dim agl = Atan2(w.Y - tr.Y, w.X - tr.X)
        If agl <= PI * 0.75 Then
            altitude = find_altitude(tr, bl, br, w)
            Return altitude
        End If
        If agl > PI * 0.75 Then
            altitude = find_altitude(tr, tl, bl, w)
            Return altitude
        End If
        'tb1.Update()
domath:
        Return altitude

        'Catch ex As Exception

        'End Try

    End Function

    Private Function find_altitude(ByVal p As Vector3,
                                   ByVal q As Vector3,
                                   ByVal r As Vector3,
                                   ByVal f As Vector3) As Double
        'This finds the height on the face of a triangle at point f.x, f.z
        p = p.Xzy ' flip yz
        q = q.Xzy ' flip yz
        r = r.Xzy ' flip yz
        f = f.Xzy ' flip yz

        Cursor_point.X = f.X
        Cursor_point.Z = f.Z
        'It returns that value as a double

        Dim nc As Vector3 = Vector3.Cross(p - r, q - r).Normalized()

        If p.Z = q.Z And q.Z = r.Z Then
            Return r.Y
        End If
        surface_normal.X = -nc.X
        surface_normal.Y = -nc.Z
        surface_normal.Z = -nc.Y
        'nc *= -1.0
        Dim k As Double
        k = (nc.X * (f.X - p.X)) + (nc.Z * (f.Z - q.Z))

        Dim y = ((k) / -nc.Y) + p.Y

        Cursor_point.Y = y
        Dim vx As Vector3 = r - f
        Dim vy = ((nc.Z * vx.Z) + (nc.X * vx.X)) / nc.Y
        y = r.Y + vy
        Return y
    End Function

End Module

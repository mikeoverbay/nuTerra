﻿Imports System.IO
Imports System.Runtime.InteropServices
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK
Imports OpenTK.Graphics.OpenGL4


Module ChunkFunctions

    Public Sub get_mesh(ByRef chunk As chunk_, ByRef v_data As terain_V_data_, ByRef r_set As chunk_render_data_)

        'good as place as any to set bounding box
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
        Dim n_buff(4095) As UInt32
        Dim uv_buff(4095) As Vector2
        Dim holes_buff(4095) As UInt32
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
                topleft.norm = pack_2_10_10_10(v_data.normals((i), (j)), 0)

                bottomleft.vert.X = (i) - w_
                bottomleft.H = v_data.heights((i), (j + 1))
                bottomleft.vert.Y = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                bottomleft.norm = pack_2_10_10_10(v_data.normals((i), (j + 1)), If(chunk.has_holes, v_data.holes(i, j), 0))

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

                n_buff(i + ((j + 1) * stride)) = bottomleft.norm
                n_buff(i + ((j + 0) * stride)) = topleft.norm

                uv_buff(i + ((j + 1) * stride)) = bottomleft.uv
                uv_buff(i + ((j + 0) * stride)) = topleft.uv

                'debug
                holes_buff(i + ((j + 0) * stride)) = v_data.holes(i, j)
                holes_buff(i + ((j + 1) * stride)) = v_data.holes(i, j)
            Next
        Next
        Dim fill_buff As Boolean = False

        Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)

        'Gen VAO id
        GL.GenVertexArrays(1, r_set.VAO)
        GL.BindVertexArray(r_set.VAO)

        ReDim r_set.mBuffers(4)

        ' If the reused buffer is not defined, we need to do so.
        If theMap.vertex_vBuffer_id = 0 Then
            GL.GenBuffers(1, theMap.vertex_vBuffer_id)
            fill_buff = True
        End If

        GL.GenBuffers(5, r_set.mBuffers)

        GL.BindBuffer(BufferTarget.ArrayBuffer, theMap.vertex_vBuffer_id)

        ' VERTEX XZ ==================================================================
        'if the reused vertex_vBuffer_id is not defined, we need to fill the buffer
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
              n_buff.Length * 4,
              n_buff, BufferUsageHint.StaticDraw)

        GL.VertexAttribPointer(3, 4,
                               VertexAttribPointerType.Int2101010Rev,
                               True, 4, 0)
        GL.EnableVertexAttribArray(3)

        ' COLOR for Debug ==================================================================
        GL.BindBuffer(BufferTarget.ArrayBuffer, r_set.mBuffers(4))

        GL.BufferData(BufferTarget.ArrayBuffer,
              holes_buff.Length * 4,
              holes_buff, BufferUsageHint.StaticDraw)

        GL.VertexAttribPointer(4, 1,
                               VertexAttribPointerType.Float,
                               True, 4, 0)
        GL.EnableVertexAttribArray(4)

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

        Dim stride = (w / 2)
        Dim dbuff((stride * 8) * (h * 2) * 4) As Byte ' make room
        count = 0

        'This will be used in a UV coord to discard
        'areas in the map to speed up rendering
        'ReDim v.holes(63, 63)

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
        v.avg_heights = avg / (HEIGHTMAPSIZE ^ 2)
        br.Close()
        ms.Close()
        ms.Dispose()
        'End If
    End Sub

    Public Sub get_normals(ByRef c As chunk_, ByRef v As terain_V_data_)

        Dim data((HEIGHTMAPSIZE * HEIGHTMAPSIZE * 2) + HEIGHTMAPSIZE) As SByte
        ReDim Preserve v.normals(HEIGHTMAPSIZE - 1, HEIGHTMAPSIZE - 1)
        Dim cnt As UInt32 = 0
        Dim i As UInt32 = 0
        Dim s As New MemoryStream(c.normals_data)
        s.Position = 0
        Dim br As New BinaryReader(s)
        Dim cols As Integer = 0
        Dim x, y As UInt32
        'Try
        s.Position = 0

        Dim header = br.ReadUInt32
        Dim version = br.ReadUInt32
        x = br.ReadUInt16
        y = br.ReadUInt16
        Dim unknown = br.ReadUInt32

        ReDim v.normals(63, 63)

        cnt = 0
        If x = 256 Then
            For j As Integer = 0 To 63
                For k As Integer = 0 To 63
                    br.ReadUInt16() 'read off un-used
                    Dim n As Vector3 = unpack16(br.ReadUInt16)
                    v.normals(k, j) = n
                Next
            Next
        End If
        If x = 128 Then
            For j As Integer = 0 To 63
                For k As Integer = 0 To 63
                    Dim n As Vector3 = unpack16(br.ReadUInt16)
                    v.normals(k, j) = n
                Next
            Next
        End If
        If x = 64 Then
            For j As Integer = 0 To 63 Step 2
                For k As Integer = 0 To 63 Step 2
                    Dim n As Vector3 = unpack16(br.ReadUInt16)
                    v.normals(k, j) = n
                    For i = 0 To 1
                        v.normals(k + 0, j + i) = n
                        v.normals(k + 1, j + i) = n
                    Next i
                Next k
            Next j
        End If
        If x = 32 Then
            For j As Integer = 0 To 63 Step 4
                For k As Integer = 0 To 63 Step 4
                    Dim n As Vector3 = unpack16(br.ReadUInt16)
                    v.normals(k, j) = n
                    For i = 0 To 3
                        v.normals(k + 0, j + i) = n
                        v.normals(k + 1, j + i) = n
                        v.normals(k + 2, j + i) = n
                        v.normals(k + 3, j + i) = n
                    Next i
                Next k
            Next j
        End If
        If x = 16 Then
            For j As Integer = 0 To 63 Step 8
                For k As Integer = 0 To 63 Step 8
                    Dim n As Vector3 = unpack16(br.ReadUInt16)
                    For i = 0 To 7
                        v.normals(k + 0, j + i) = n
                        v.normals(k + 1, j + i) = n
                        v.normals(k + 2, j + i) = n
                        v.normals(k + 3, j + i) = n
                        v.normals(k + 4, j + i) = n
                        v.normals(k + 5, j + i) = n
                        v.normals(k + 6, j + i) = n
                        v.normals(k + 7, j + i) = n
                    Next i
                Next k
            Next j
        End If

        s.Close()
        s.Dispose()

        Return
    End Sub
    Private Function unpack16(ByVal u16 As UInt16)
        Dim b = BitConverter.GetBytes(u16)
        Dim X = b(0)
        Dim Z = b(1)
        Dim divisor = 255.9F / 2.0F
        Dim subtractor = 1.0
        'Xs = (X / divisor) - subtractor
        'Zs = (Z / divisor) - subtractor
        Dim Xs = (X / 255) * 2.0 - 1.0
        Dim Zs = (Z / 255) * 2.0 - 1.0
        Dim Y As Single = Math.Sqrt(1.0 - (Xs * Xs - Zs * Zs))
        'nothing is working so lets jsut return a Y up normal
        Dim v As New Vector3(0.0, 1.0, 0.0)
        v.Normalize()
        Return v
    End Function
    Public Sub get_location(ByRef c As chunk_)
        'Creates the mapBoard array and figures out where each chunk is
        'located based on its name. 
        Dim x, y As Integer
        'Dim mod_ = (Sqrt(maplist.Length - 1)) And 1
        'Dim offset As Integer = Sqrt(maplist.Length - 1) / 2
        'If JUST_MAP_NAME.Contains("101_") Then
        '    mod_ = 0
        'End If
        'This routine gets the maps location in the world grid from its name
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
        Try
            'mapBoard(x + offset + mod_, y + offset + mod_) = map
            'mapBoard(x + offset + mod_, y + offset) = map
        Catch ex As Exception

        End Try
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

End Module

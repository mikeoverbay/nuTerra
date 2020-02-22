Imports System.IO
Imports System.Runtime.InteropServices
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK
Imports OpenTK.Graphics.OpenGL4


Module ChunkFunctions
    Private vertex_data_size As Integer = Marshal.SizeOf(GetType(vertex_data))

    Public Sub get_mesh(ByRef chunk As chunk_, ByRef v_data As terain_V_data_, ByRef r_set As chunk_render_data_)

        'good as place as any to set bounding box
        v_data.BB_Max.X = chunk.location.X + 50
        v_data.BB_Min.X = chunk.location.X - 50
        v_data.BB_Max.Z = chunk.location.Y + 50
        v_data.BB_Min.Z = chunk.location.Y - 50
        get_translated_bb_terrain(v_data.BB, v_data)
        r_set.matrix = Matrix4.CreateTranslation(chunk.location.X, 0.0F, chunk.location.Y)
        '(64 * 64 * 6) - 1  = 24575
        Dim v_buff(24575) As vertex_data
        Dim middle As New Vector3
        Dim w As UInt32 = HEIGHTMAPSIZE 'bmp_w
        Dim h As UInt32 = HEIGHTMAPSIZE 'bmp_h
        Dim uvScale = (1.0# / 64.0#)
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / (64.0#)
        Dim cnt As UInt32 = 0
        For j = 0 To w - 2
            For i = 0 To h - 2

                middle.X += (i - w_)
                middle.Y += (j - h_)
                middle.Z += (v_data.heights((i), (j)))

                topleft.vert.X = (i) - w_
                topleft.vert.Y = v_data.heights((i), (j))
                topleft.vert.Z = (j) - h_
                topleft.uv.X = (i) * uvScale
                topleft.uv.Y = (j) * uvScale
                topleft.norm = pack_2_10_10_10(v_data.normals((i), (j)), If(chunk.has_holes, v_data.holes(i, j), 0))

                topRight.vert.X = (i + 1) - w_
                topRight.vert.Y = v_data.heights((i + 1), (j))
                topRight.vert.Z = (j) - h_
                topRight.uv.X = (i + 1) * uvScale
                topRight.uv.Y = (j) * uvScale
                topRight.norm = pack_2_10_10_10(v_data.normals((i + 1), (j)), If(chunk.has_holes, v_data.holes(i, j), 0))

                bottomRight.vert.X = (i + 1) - w_
                bottomRight.vert.Y = v_data.heights((i + 1), (j + 1))
                bottomRight.vert.Z = (j + 1) - h_
                bottomRight.uv.X = (i + 1) * uvScale
                bottomRight.uv.Y = (j + 1) * uvScale
                bottomRight.norm = pack_2_10_10_10(v_data.normals((i + 1), (j + 1)), If(chunk.has_holes, v_data.holes(i, j), 0))

                bottomleft.vert.X = (i) - w_
                bottomleft.vert.Y = v_data.heights((i), (j + 1))
                bottomleft.vert.Z = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                bottomleft.norm = pack_2_10_10_10(v_data.normals((i), (j + 1)), If(chunk.has_holes, v_data.holes(i, j), 0))

                ' TL --------- TR
                '  |         . |
                '  |       .   |
                '  |     .     |
                '  |   .       |
                '  | .         |
                '  BL -------- BR

                bottomleft.vert.X *= scale
                bottomleft.vert.Z *= scale

                bottomRight.vert.X *= scale
                bottomRight.vert.Z *= scale

                topleft.vert.X *= scale
                topleft.vert.Z *= scale

                topRight.vert.X *= scale
                topRight.vert.Z *= scale

                v_buff(cnt + 0) = bottomleft
                v_buff(cnt + 1) = topRight
                v_buff(cnt + 2) = topleft

                v_buff(cnt + 3) = bottomleft
                v_buff(cnt + 4) = bottomRight
                v_buff(cnt + 5) = topRight
                cnt += 6
            Next
        Next

        'we can remove 2 vertices by adding a indices list!
        Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)
        If max_vertex_elements < v_buff.Length * vertex_data_size Then
            Stop ' not room for this big a vertex buffer
        End If

        'Gen VAO id
        GL.GenVertexArrays(1, r_set.VAO)
        GL.BindVertexArray(r_set.VAO)

        ReDim r_set.mBuffers(1)
        GL.GenBuffers(1, r_set.mBuffers)

        GL.BindBuffer(BufferTarget.ArrayBuffer, r_set.mBuffers(0))

        ' pos
        GL.VertexAttribPointer(0, 3,
                               VertexAttribPointerType.Float,
                               False, vertex_data_size, 0)
        GL.EnableVertexAttribArray(0)

        ' uv
        GL.VertexAttribPointer(1, 2,
                               VertexAttribPointerType.Float,
                               False, vertex_data_size, 12)
        GL.EnableVertexAttribArray(1)

        ' normals
        GL.VertexAttribPointer(2, 4,
                               VertexAttribPointerType.Int2101010Rev,
                               True, vertex_data_size, 20)
        GL.EnableVertexAttribArray(2)

        GL.BufferData(BufferTarget.ArrayBuffer,
                      v_buff.Length * vertex_data_size,
                      v_buff, BufferUsageHint.StaticDraw)

        GL.BindVertexArray(0)
    End Sub

    Public Sub get_holes(ByRef c As chunk_, ByRef v As terain_V_data_)

        'Unpacks and creates hole data
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
        ReDim v.holes((stride * 8) - 1, (h * 2) - 1)

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
            'ReDim bmp_data(64, 64)
            Dim div = 64 / (mapsize - 5)
            ReDim v.heights(63, 63)
            HEIGHTMAPSIZE = 64
            For j As UInt32 = 2 To mapsize - 4
                For i As UInt32 = 2 To mapsize - 4
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    sv = br.ReadInt32
                    ev = br.ReadInt32
                    For xp = (i - 2) * div To (((i + 1) - 2) * div)
                        Dim ii = (i - 2) * div
                        Dim xval As Single = (ev - sv) * ((xp - ii) / div)
                        'bmp_data(64 - xp, (j - 2) * div) = (xval + sv) * 0.001
                        v.heights(64 - xp, (j - 2) * div) = (xval + sv) * 0.001
                        ty = xp

                        ms.Position = (i * 4) + ((j + 1) * mapsize * 4)
                        ev = br.ReadInt32
                        For yp = (j - 2) * div To (((j + 1) - 2) * div)
                            Dim jj = (j - 2) * div
                            Dim yval As Single = (ev - sv) * ((yp - jj) / div)
                            'bmp_data(64 - xp, yp) = (yval + sv) * 0.001
                            v.heights(64 - xp, yp) = (yval + sv) * 0.001
                        Next
                    Next
                    ' Debug.Write(qtized & vbCrLf)
                Next
            Next

        Else

            'ReDim bmp_data(HEIGHTMAPSIZE, HEIGHTMAPSIZE)
            ReDim v.heights(HEIGHTMAPSIZE, HEIGHTMAPSIZE)
            For j As UInt32 = 3 To mapsize - 3
                For i As UInt32 = 3 To mapsize - 3
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    Dim tc = br.ReadInt32
                    quantized = tc * 0.001
                    ' Debug.Write(qtized & vbCrLf)
                    'bmp_data(mapsize - i - 3, j - 3) = qtized
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
        'This sub is no longer used.
        'I will leave it so others can see how the terrain normals are stored.
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

        If x * y <> 65536 Then
            MsgBox("Odd lodNormals file!!!", MsgBoxStyle.Exclamation, "Well Shit...")
        End If

        ReDim v.normals(63, 63)

        cnt = 0
        For j As Integer = 0 To 63
            For k As Integer = 0 To 63

                Dim n As Vector3 = unpackNormal_8_8_8(br.ReadUInt32)
                v.normals(k, j).X = n.X
                v.normals(k, j).Y = n.Y
                v.normals(k, j).Z = n.Z
            Next
            'Debug.Write(vbCrLf)
        Next
        s.Close()
        s.Dispose()

        Return
    End Sub

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

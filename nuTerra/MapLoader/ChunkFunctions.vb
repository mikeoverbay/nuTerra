Imports System.IO
Imports System.Math
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ChunkFunctions
    Public b_x_min As Integer
    Public b_x_max As Integer
    Public b_y_min As Integer
    Public b_y_max As Integer
    Public tl_, tr_, br_, bl_ As Vector3
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
        Dim b_size = 65 * 65
        Dim v_buff_XZ(b_size) As Vector2
        Dim v_buff_Y(b_size) As Single
        Dim h_buff(b_size) As UInt32
        Dim uv_buff(b_size) As Vector2
        'Dim indicies(7937) As vect3_16
        Dim indicies(8191) As vect3_16
        Dim w As Double = HEIGHTMAPSIZE + 1  'bmp_w
        Dim h As Double = HEIGHTMAPSIZE + 1  'bmp_h
        Dim uvScale = (1.0# / 64.0#)
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / (64.0#)
        Dim stride = 65
        Dim cnt As UInt32 = 0

        'we do not need to do this more than one time!
        If theMap.vertex_vBuffer_id = 0 Then
            For j = 0 To 63
                For i = 0 To 63
                    indicies(cnt + 0).x = (i + 0) + ((j + 1) * stride) ' BL
                    indicies(cnt + 0).y = (i + 1) + ((j + 0) * stride) ' TR
                    indicies(cnt + 0).z = (i + 0) + ((j + 0) * stride) ' TL

                    indicies(cnt + 1).x = (i + 0) + ((j + 1) * stride) ' BL
                    indicies(cnt + 1).y = (i + 1) + ((j + 1) * stride) ' BR
                    indicies(cnt + 1).z = (i + 1) + ((j + 0) * stride) ' TR
                    cnt += 2
                Next
            Next
        End If

        cnt = 0
        Dim hScaler = 1.0
        If HEIGHTMAPSIZE < 64 Then
            hScaler = 0.5
        End If
        For j = 0 To 63 Step 1
            For i = 0 To 64
                topleft.vert.X = (i) - w_
                topleft.H = v_data.heights(((i * hScaler) + 3), ((j * hScaler) + 2))
                topleft.vert.Y = (j) - h_
                topleft.uv.X = (i) * uvScale
                topleft.uv.Y = (j) * uvScale
                'topleft.hole = v_data.holes(i, j)

                bottomleft.vert.X = (i) - w_
                bottomleft.H = v_data.heights(((i * hScaler) + 3), ((j * hScaler) + 3))
                bottomleft.vert.Y = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                'bottomleft.hole = v_data.holes(i, j + 1)

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
        TOTAL_TRIANGLES_DRAWN += 8192 ' number of triangles per chunk
        Dim fill_buff As Boolean = False

        ' SETUP ==================================================================
        'Gen VAO and VBO Ids
        GL.CreateVertexArrays(1, r_set.VAO)
        ReDim r_set.mBuffers(2)
        GL.CreateBuffers(3, r_set.mBuffers)

        ' If the shared buffer is not defined, we need to do so.
        If theMap.vertex_vBuffer_id = 0 Then
            GL.CreateBuffers(1, theMap.vertex_vBuffer_id)
            GL.CreateBuffers(1, theMap.vertex_iBuffer_id)

            'if the shared buffer is not defined, we need to fill the buffer now
            GL.NamedBufferData(theMap.vertex_iBuffer_id, indicies.Length * 6, indicies, BufferUsageHint.StaticDraw)
            GL.NamedBufferData(theMap.vertex_vBuffer_id, v_buff_XZ.Length * 8, v_buff_XZ, BufferUsageHint.StaticDraw)
        End If

        ' VERTEX XZ ==================================================================
        If fill_buff Then
        End If
        GL.VertexArrayVertexBuffer(r_set.VAO, 0, theMap.vertex_vBuffer_id, IntPtr.Zero, 8)
        GL.VertexArrayAttribFormat(r_set.VAO, 0, 2, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(r_set.VAO, 0, 0)
        GL.EnableVertexArrayAttrib(r_set.VAO, 0)

        ' POSITION Y ==================================================================
        GL.NamedBufferData(r_set.mBuffers(0), v_buff_Y.Length * 4, v_buff_Y, BufferUsageHint.StaticDraw)

        GL.VertexArrayVertexBuffer(r_set.VAO, 1, r_set.mBuffers(0), IntPtr.Zero, 4)
        GL.VertexArrayAttribFormat(r_set.VAO, 1, 1, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(r_set.VAO, 1, 1)
        GL.EnableVertexArrayAttrib(r_set.VAO, 1)

        ' UV ==================================================================
        GL.NamedBufferData(r_set.mBuffers(1), uv_buff.Length * 8, uv_buff, BufferUsageHint.StaticDraw)

        GL.VertexArrayVertexBuffer(r_set.VAO, 2, r_set.mBuffers(1), IntPtr.Zero, 8)
        GL.VertexArrayAttribFormat(r_set.VAO, 2, 2, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(r_set.VAO, 2, 2)
        GL.EnableVertexArrayAttrib(r_set.VAO, 2)

        ' NORMALS ================================================================== 
        GL.NamedBufferData(r_set.mBuffers(2), h_buff.Length * 4, h_buff, BufferUsageHint.StaticDraw)

        GL.VertexArrayVertexBuffer(r_set.VAO, 3, r_set.mBuffers(2), IntPtr.Zero, 4)
        GL.VertexArrayAttribFormat(r_set.VAO, 3, 1, VertexAttribType.UnsignedInt, False, 0)
        GL.VertexArrayAttribBinding(r_set.VAO, 3, 3)
        GL.EnableVertexArrayAttrib(r_set.VAO, 3)

        ' INDICES ==================================================================
        GL.VertexArrayElementBuffer(r_set.VAO, theMap.vertex_iBuffer_id)
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
        HEIGHTMAPSIZE = mapsize
        If mapsize < 64 Then

            ReDim v.heights(mapsize, mapsize)
            For j As UInt32 = 0 To mapsize - 1
                For i As UInt32 = 0 To mapsize - 1
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    Dim tc = br.ReadInt32
                    quantized = tc * 0.001
                    v.heights(mapsize - i, j) = quantized
                Next
            Next
        Else

            ReDim v.heights(mapsize, mapsize)
            For j As UInt32 = 0 To mapsize - 1
                For i As UInt32 = 0 To mapsize - 1
                    ms.Position = (i * 4) + (j * mapsize * 4)
                    Dim tc = br.ReadInt32
                    quantized = tc * 0.001
                    v.heights(mapsize - i, j) = quantized
                Next
            Next
        End If

        Dim avg, y_max, y_min As Single
        For j As UInt32 = 0 To mapsize - 1
            For i As UInt32 = 0 To mapsize - 1
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
            Dim buffer(x * y) As Byte

            render_set.TerrainNormals_id = load_t2_normals_from_stream(br, x, y)
        End Using

        Dim name = theMap.chunks(map).name
    End Sub

    Public Sub set_map_bs()
        b_x_max = -10000
        b_x_min = 10000
        b_y_max = -10000
        b_y_min = 10000
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

        If b_x_min > x Then b_x_min = x
        If b_x_max < x Then b_x_max = x
        If b_y_min > y Then b_y_min = y
        If b_y_max < y Then b_y_max = y
        MAP_SIZE.X = b_x_max - b_x_min
        MAP_SIZE.Y = b_y_max - b_y_min

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

    Public Function get_Y_at_XZ(ByVal Lx As Double, ByVal Lz As Double) As Single
        'If Not maploaded Then Return 100.0\
        If Not MAP_LOADED Or Not TERRAIN_LOADED Then
            Return 0
        End If
        If mapBoard Is Nothing Then Return 0.0F
        Dim tlx As Single = 100.0 / 65.0
        Dim tly As Single = 100.0 / 65.0
        Dim ts As Single = 65.0 / 100.0
        Dim tl, tr, br, bl, w As Vector3
        Dim xvp, yvp As Integer
        Dim ryp, rxp As Single

        For xo = 0 To 19
            For yo = 0 To 19
                If mapBoard(xo, yo).occupied Then

                    Dim px = mapBoard(xo, yo).location.X
                    If px - 50 < Lx And px + 50 >= Lx Then
                        xvp = xo
                        Dim pz = mapBoard(xo, yo).location.Y
                        If pz - 50 < Lz And pz + 50 >= Lz Then
                            yvp = yo
                            GoTo exit2
                        End If
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

        Dim map = mapBoard(xvp, yvp).map_id

        Dim vxp As Double = ((((Lx) / 100)) - Truncate((Truncate(Lx) / 100))) * 65.0
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

        Dim vyp As Double = ((((Lz) / 100)) - Truncate((Truncate(Lz) / 100))) * 65.0

        If vyp < 0.0 Then
            vyp = 65.0 + vyp
        End If
        If vxp < 0 Then
            vxp = 65.0 + vxp

        End If
        vxp = Round(vxp, 12)
        vyp = Round(vyp, 12)
        rxp = (Floor(vxp))
        rxp *= tlx
        ryp = Floor(vyp)
        ryp *= tlx

        w.X = (vxp * tlx)
        w.Y = (vyp * tlx)

        Dim HX, HY, OX, OY As Integer
        HX = Floor(vxp)
        OX = 1
        HY = Floor(vyp)
        OY = 1
        If HEIGHTMAPSIZE < 64 Then
            HX *= 0.5 : HY *= 0.5
        End If
        Dim altitude As Single = 0.0

        If HX + OX > 65 Then
            Return 0
        End If
        tl.X = rxp
        tl.Y = ryp
        HX += 3
        HY += 2
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


        Dim agl = Atan2(w.Y - tr.Y, w.X - tr.X)
        If agl <= PI * 0.75 Then
            altitude = find_altitude(tr, bl, br, w)
            Return altitude
        End If
        If agl > PI * 0.75 Then
            altitude = find_altitude(tr, tl, bl, w)
            Return altitude
        End If
domath:
        Return altitude



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

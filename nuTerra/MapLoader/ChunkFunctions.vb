Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL
Imports GL4 = OpenTK.Graphics.OpenGL4

Module ChunkFunctions
    Public b_x_min As Single
    Public b_x_max As Single
    Public b_y_min As Single
    Public b_y_max As Single
    Public tl_, tr_, br_, bl_ As Vector3
    Public Cursor_point As Vector3
    Public surface_normal As Vector3
    Public CURSOR_Y As Single
    Public HX, HY, OX, OY As Integer
    Dim hole_size As Integer

    Public Sub get_outland_mesh(ByRef chunk As chunk_, ByRef v_data As terrain_V_data_, ByRef r_set As chunk_render_data_)

        'good place as any to set bounding box
        'unneeded
        'v_data.BB_Max.X = chunk.location.X + 50
        'v_data.BB_Min.X = chunk.location.X - 50
        'v_data.BB_Max.Z = chunk.location.Y + 50
        'v_data.BB_Min.Z = chunk.location.Y - 50

        'get_translated_bb_terrain(v_data.BB, v_data)
        r_set.matrix = Matrix4.Identity

        Dim size = MapTerrain.OUTLAND_GRID
        Dim indi_count = (size - 1) * (size - 1) * 2
        Dim vert_count = size * size
        ' 64 * 64 * 2  = 8192 indi count
        ' 65 * 65      = 4096 vert count
        Dim b_size = size * size - 1

        ReDim v_data.v_buff_XZ(b_size)
        ReDim v_data.v_buff_Y(b_size)
        ReDim v_data.uv_buff(b_size)
        ReDim v_data.n_buff(b_size)
        ReDim v_data.t_buff(b_size)

        ReDim v_data.indicies_32(indi_count - 1)

        Dim w As Double = size 'bmp_w
        Dim h As Double = size  'bmp_h
        Dim uvScale = 1.0# / size
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / (size - 1)
        Dim stride = size
        Dim cnt As UInt32 = 0

        'we need this for creating normals!
        'If theMap.vertex_vBuffer_id = 0 Then
        For j = 0 To size - 2
            For i = 0 To size - 2
                With v_data.indicies_32(cnt + 0)
                    .x = (i + 0) + ((j + 1) * stride) ' BL
                    .y = (i + 1) + ((j + 0) * stride) ' TR
                    .z = (i + 0) + ((j + 0) * stride) ' TL
                End With

                With v_data.indicies_32(cnt + 1)
                    .x = (i + 0) + ((j + 1) * stride) ' BL
                    .y = (i + 1) + ((j + 1) * stride) ' BR
                    .z = (i + 1) + ((j + 0) * stride) ' TR
                End With
                cnt += 2
            Next
        Next
        'End If

        For j As Single = 0 To size - 2
            For i As Single = 0 To size - 1
                topleft.vert.X = (i) - w_
                'topleft.H = v_data.heightsTBL((i + 3), (j + 2))
                topleft.vert.Y = (j) - h_
                topleft.uv.X = (i) * uvScale
                topleft.uv.Y = (j) * uvScale
                'topleft.hole = v_data.holes(topleft.uv.X * hole_size, topleft.uv.Y * hole_size)

                bottomleft.vert.X = (i) - w_
                'bottomleft.H = v_data.heightsTBL((i + 3), (j + 3))
                bottomleft.vert.Y = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                'topleft.hole = v_data.holes(topleft.uv.X * hole_size, topleft.uv.Y * hole_size)

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

                'center values
                topleft.vert.X += 0.04888F
                topleft.vert.Y += 0.04888F

                bottomleft.vert.X += 0.04888F
                bottomleft.vert.Y += 0.04888F

                ' Fill the arrays
                v_data.v_buff_XZ(i + ((j + 1) * stride)) = bottomleft.vert
                v_data.v_buff_XZ(i + ((j + 0) * stride)) = topleft.vert

                v_data.uv_buff(i + ((j + 1) * stride)) = bottomleft.uv
                v_data.uv_buff(i + ((j + 0) * stride)) = topleft.uv

            Next
        Next
        '=========================================================================
        'From : https://www.iquilezles.org/www/articles/normals/normals.htm
        'Create smoothed normals using IQ's method
        make_normals_indi32(v_data.indicies_32, v_data.v_buff_XZ, v_data.v_buff_Y, v_data.n_buff, v_data.t_buff, v_data.uv_buff)
        '=========================================================================


    End Sub
    Public Sub get_mesh(ByRef chunk As chunk_, ByRef v_data As terrain_V_data_, ByRef r_set As chunk_render_data_)

        'good place as any to set bounding box
        v_data.BB_Max.X = chunk.location.X + 50
        v_data.BB_Min.X = chunk.location.X - 50
        v_data.BB_Max.Z = chunk.location.Y + 50
        v_data.BB_Min.Z = chunk.location.Y - 50
        get_translated_bb_terrain(v_data.BB, v_data)
        r_set.matrix = Matrix4.CreateTranslation(chunk.location.X, 0.0F, chunk.location.Y)

        ' 64 * 64 * 2  = 8192 indi count
        ' 65 * 65      = 4096 vert count
        Dim b_size = 65 * 65 - 1

        ReDim v_data.v_buff_XZ(b_size)
        ReDim v_data.v_buff_Y(b_size)
        ReDim v_data.h_buff(b_size)
        ReDim v_data.uv_buff(b_size)
        ReDim v_data.n_buff(b_size)
        ReDim v_data.t_buff(b_size)
        ReDim v_data.indicies(8191)

        Dim w As Double = 64 + 1  'bmp_w
        Dim h As Double = 64 + 1  'bmp_h
        Dim uvScale = 1.0# / 64.0#
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / 64.0#
        Dim stride = 65
        Dim cnt As UInt32 = 0

        'we need this for creating normals!
        'If theMap.vertex_vBuffer_id = 0 Then
        For j = 0 To 63
            For i = 0 To 63
                With v_data.indicies(cnt + 0)
                    .x = (i + 0) + ((j + 1) * stride) ' BL
                    .y = (i + 1) + ((j + 0) * stride) ' TR
                    .z = (i + 0) + ((j + 0) * stride) ' TL
                End With

                With v_data.indicies(cnt + 1)
                    .x = (i + 0) + ((j + 1) * stride) ' BL
                    .y = (i + 1) + ((j + 1) * stride) ' BR
                    .z = (i + 1) + ((j + 0) * stride) ' TR
                End With
                cnt += 2
            Next
        Next
        'End If

        For j As Single = 0 To 63
            For i As Single = 0 To 64
                topleft.vert.X = (i) - w_
                topleft.H = v_data.heightsTBL((i + 3), (j + 2))
                topleft.vert.Y = (j) - h_
                topleft.uv.X = (i) * uvScale
                topleft.uv.Y = (j) * uvScale
                topleft.hole = v_data.holes(topleft.uv.X * hole_size, topleft.uv.Y * hole_size)

                bottomleft.vert.X = (i) - w_
                bottomleft.H = v_data.heightsTBL((i + 3), (j + 3))
                bottomleft.vert.Y = (j + 1) - h_
                bottomleft.uv.X = (i) * uvScale
                bottomleft.uv.Y = (j + 1) * uvScale
                topleft.hole = v_data.holes(topleft.uv.X * hole_size, topleft.uv.Y * hole_size)

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

                'this offsets the terrain geo to align textures with models.
                'ether .781 (100 /64)/2  = 0.78125 or (100/65)/2  = 0.76923
                topleft.vert.X += 0.78125F
                topleft.vert.Y += 0.78125F

                bottomleft.vert.X += 0.78125F
                bottomleft.vert.Y += 0.78125F

                ' Fill the arrays
                v_data.v_buff_XZ(i + ((j + 1) * stride)) = bottomleft.vert
                v_data.v_buff_XZ(i + ((j + 0) * stride)) = topleft.vert

                v_data.v_buff_Y(i + ((j + 1) * stride)) = bottomleft.H
                v_data.v_buff_Y(i + ((j + 0) * stride)) = topleft.H

                v_data.h_buff(i + ((j + 1) * stride)) = bottomleft.hole
                v_data.h_buff(i + ((j + 0) * stride)) = topleft.hole

                v_data.uv_buff(i + ((j + 1) * stride)) = bottomleft.uv
                v_data.uv_buff(i + ((j + 0) * stride)) = topleft.uv

            Next
        Next

        '=========================================================================
        'From : https://www.iquilezles.org/www/articles/normals/normals.htm
        'Create smoothed normals using IQ's method
        make_normals(v_data.indicies, v_data.v_buff_XZ, v_data.v_buff_Y, v_data.n_buff, v_data.t_buff, v_data.uv_buff)
        '=========================================================================


    End Sub

    Private Sub make_normals_indi32(ByRef indi() As vect3_32, ByRef XY() As Vector2, ByRef Z() As Single, ByRef n_buff() As Vector3, ByRef t_buff() As Vector3, ByRef UV() As Vector2)
        'generate and smooth normals. Amazing code by IQ.
        For i = 0 To indi.Length - 1
            Dim ia As UInt32 = indi(i).z
            Dim ib As UInt32 = indi(i).y
            Dim ic As UInt32 = indi(i).x

            Dim e1, e2 As Vector3

            e1.Xz = XY(ia) - XY(ib)
            e1.Y = Z(ia) - Z(ib)
            e2.Xz = XY(ic) - XY(ib)
            e2.Y = Z(ic) - Z(ib)
            Dim no = Vector3.Cross(e1, e2)
            no.Normalize()
            n_buff(ia) += no
            n_buff(ib) += no
            n_buff(ic) += no
        Next
        For i = 0 To indi.Length - 1
            Dim v0, V1, v2 As Vector3

            Dim ia As UInt32 = indi(i).z
            Dim ib As UInt32 = indi(i).y
            Dim ic As UInt32 = indi(i).x

            v0.Xz = XY(ia) : v0.Y = Z(ia)
            V1.Xz = XY(ib) : V1.Y = Z(ib)
            v2.Xz = XY(ic) : v2.Y = Z(ic)

            Dim uv0 = UV(ia)
            Dim uv1 = UV(ib)
            Dim uv2 = UV(ic)

            Dim deltaPos1 = V1 - v0
            Dim deltaPos2 = v2 - v0
            Dim deltaUV1 = uv1 - uv0
            Dim deltaUV2 = uv2 - uv1

            Dim r = 1.0F / (deltaUV1.X * deltaUV2.Y - deltaUV1.Y * deltaUV2.X)
            Dim tangent As Vector3 = (deltaPos1 * deltaUV2.Y - deltaPos2 * deltaUV1.Y) * r

            tangent.Normalize()

            t_buff(ia) = tangent
            t_buff(ib) = tangent
            t_buff(ic) = tangent

        Next

        For i = 0 To t_buff.Length - 1
            n_buff(i).Normalize()
        Next

    End Sub

    Private Sub make_normals(ByRef indi() As vect3_16, ByRef XY() As Vector2, ByRef Z() As Single, ByRef n_buff() As Vector3, ByRef t_buff() As Vector3, ByRef UV() As Vector2)
        'generate and smooth normals. Amazing code by IQ.
        For i = 0 To indi.Length - 1
            Dim ia As UInt16 = indi(i).z
            Dim ib As UInt16 = indi(i).y
            Dim ic As UInt16 = indi(i).x

            Dim e1, e2 As Vector3

            e1.Xz = XY(ia) - XY(ib)
            e1.Y = Z(ia) - Z(ib)
            e2.Xz = XY(ic) - XY(ib)
            e2.Y = Z(ic) - Z(ib)
            Dim no = Vector3.Cross(e1, e2)
            no.Normalize()
            n_buff(ia) += no
            n_buff(ib) += no
            n_buff(ic) += no
        Next
        For i = 0 To indi.Length - 1
            Dim v0, V1, v2 As Vector3

            Dim ia As UInt16 = indi(i).z
            Dim ib As UInt16 = indi(i).y
            Dim ic As UInt16 = indi(i).x

            v0.Xz = XY(ia) : v0.Y = Z(ia)
            V1.Xz = XY(ib) : V1.Y = Z(ib)
            v2.Xz = XY(ic) : v2.Y = Z(ic)

            Dim uv0 = UV(ia)
            Dim uv1 = UV(ib)
            Dim uv2 = UV(ic)

            Dim deltaPos1 = V1 - v0
            Dim deltaPos2 = v2 - v0
            Dim deltaUV1 = uv1 - uv0
            Dim deltaUV2 = uv2 - uv1

            Dim r = 1.0F / (deltaUV1.X * deltaUV2.Y - deltaUV1.Y * deltaUV2.X)
            Dim tangent As Vector3 = (deltaPos1 * deltaUV2.Y - deltaPos2 * deltaUV1.Y) * r

            tangent.Normalize()

            t_buff(ia) = tangent
            t_buff(ib) = tangent
            t_buff(ic) = tangent

        Next

        For i = 0 To t_buff.Length - 1
            n_buff(i).Normalize()
        Next

    End Sub

    Public Sub smooth_edges(ByVal Idx As Integer)

        Dim v1, v2, v3, v4 As Vector3
        With theMap.v_data(Idx)

            Dim mbX = theMap.chunks(Idx).mBoard_x
            Dim mbY = theMap.chunks(Idx).mBoard_y

            'corner
            If mapBoard(mbX + 1, mbY - 1).occupied Then
                Dim tr = mapBoard(mbX + 1, mbY - 1).map_id
                Dim tl = mapBoard(mbX, mbY - 1).map_id
                Dim br = mapBoard(mbX + 1, mbY).map_id

                Dim me_ = 64
                Dim you_tr = 64 * 65
                Dim you_tl = 65 * 65 - 1
                Dim you_br = 0
                v1 = .n_buff(me_) '<-- me
                v2 = theMap.v_data(tr).n_buff(you_tr)
                v3 = theMap.v_data(tl).n_buff(you_tl)
                v4 = theMap.v_data(br).n_buff(you_br)
                v1 = (v1 + v2 + v3 + v4) / 4.0F
                theMap.v_data(tr).n_buff(you_tr) = v1
                theMap.v_data(tl).n_buff(you_tl) = v1
                theMap.v_data(br).n_buff(you_br) = v1
                .n_buff(me_) = v1

                v1 = .t_buff(me_) '<-- me
                v2 = theMap.v_data(tr).t_buff(you_tr)
                v3 = theMap.v_data(tl).t_buff(you_tl)
                v4 = theMap.v_data(br).t_buff(you_br)
                'v1 = (v1 + v2 + v3 + v4) / 4.0F
                theMap.v_data(tr).t_buff(you_tr) = v1
                theMap.v_data(tl).t_buff(you_tl) = v1
                theMap.v_data(br).t_buff(you_br) = v1
                .t_buff(me_) = v1

            End If

            'top edge
            If mapBoard(mbX, mbY - 1).occupied Then
                Dim other = mapBoard(mbX, mbY - 1).map_id
                For x = 0 To 64
                    Dim me_ = x
                    Dim you_ = x + (65 * 64)

                    v1 = .n_buff(me_) '<-- me
                    v2 = theMap.v_data(other).n_buff(you_)
                    v1 = (v1 + v2) / 2.0F
                    .n_buff(me_) = v1
                    theMap.v_data(other).n_buff(you_) = v1

                    v1 = .t_buff(me_) '<-- me
                    v2 = theMap.v_data(other).t_buff(you_)
                    'v1 = (v1 + v2) / 2.0
                    .t_buff(me_) = v1
                    theMap.v_data(other).t_buff(you_) = v1

                Next
            End If
            'front edge
            If mapBoard(mbX + 1, mbY).occupied Then
                Dim other = mapBoard(mbX + 1, mbY).map_id
                For y = 0 To 64
                    Dim me_ = y * 65 + 64
                    Dim you_ = y * 65
                    v1 = .n_buff(me_) '<-- me
                    v2 = theMap.v_data(other).n_buff(you_)
                    v1 = (v1 + v2) / 2.0F
                    .n_buff(me_) = v1
                    theMap.v_data(other).n_buff(you_) = v1

                    v1 = .t_buff(me_) '<-- me
                    v1 = theMap.v_data(other).t_buff(you_)
                    'v1 = (v1 + v2) / 2.0F
                    .t_buff(me_) = v1
                    theMap.v_data(other).t_buff(you_) = v1

                Next
            End If

        End With


    End Sub

    <StructLayout(LayoutKind.Sequential)>
    Structure TerrainVertex
        Public xyz As Vector3
        Public uv As Vector2
        Public packed_noraml As UInt32
        Public tangents As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure OutlandVertex
        Public xy As Vector2
        Public uv As Vector2
        Public packed_noraml As UInt32
        Public tangents As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Structure TerrainChunkInfo
        Public modelMatrix As Matrix4
        Public g_uv_offset As Vector2
        Public pad1 As UInt32
        Public pad2 As UInt32
    End Structure

    ''' <summary>
    ''' Index buffer for one outland cascade as a RING: the full grid minus
    ''' every quad that falls entirely inside the hole rect (world XZ). The game
    ''' ships prebuilt ring meshes - the near cascade has the playfield cut out,
    ''' the far cascade has the near cascade cut out - because the coarse outland
    ''' surface tracks the playfield only approximately and would poke through
    ''' it. Quads crossing the hole edge are kept, so the ring always tucks a
    ''' little way under what covers it.
    ''' scale/center are the same values the draw uniforms use.
    '''
    ''' Triangles come out grouped in OUTLAND_CULL_BLOCK^2-quad blocks (same
    ''' triangles, block-major order), and blocks() records each non-empty
    ''' block's index range plus the world-XZ bounds of the quads it actually
    ''' emitted - so a block straddling the hole gets a tight box. Draw_outland
    ''' frustum-tests these instead of drawing the whole ring.
    ''' </summary>
    Public Function build_outland_ring_indices(scale As Vector2, center As Vector2,
                                               hole_min As Vector2, hole_max As Vector2,
                                               ByRef blocks() As MapTerrain.OutlandBlock) As vect3_32()
        Dim size = MapTerrain.OUTLAND_GRID
        Dim half = size \ 2
        Dim stride = size
        Dim ms = 100.0F / (size - 1)
        Dim bq = MapTerrain.OUTLAND_CULL_BLOCK
        Dim nblocks = (size - 2 + bq) \ bq   ' ceil((size-1) quads / bq)
        Dim list As New List(Of vect3_32)((size - 1) * (size - 1) * 2)
        Dim block_list As New List(Of MapTerrain.OutlandBlock)(nblocks * nblocks)

        For bj = 0 To nblocks - 1
            For bi = 0 To nblocks - 1
                Dim first = list.Count * 3
                Dim mn As New Vector2(Single.MaxValue, Single.MaxValue)
                Dim mx As New Vector2(Single.MinValue, Single.MinValue)

                For j = bj * bq To Math.Min((bj + 1) * bq, size - 1) - 1
                    For i = bi * bq To Math.Min((bi + 1) * bq, size - 1) - 1
                        Dim x0 = ((i - half) * ms + 0.04888F) * scale.X + center.X
                        Dim x1 = ((i + 1 - half) * ms + 0.04888F) * scale.X + center.X
                        Dim z0 = ((j - half) * ms + 0.04888F) * scale.Y + center.Y
                        Dim z1 = ((j + 1 - half) * ms + 0.04888F) * scale.Y + center.Y

                        If Math.Min(x0, x1) >= hole_min.X AndAlso Math.Max(x0, x1) <= hole_max.X AndAlso
                           Math.Min(z0, z1) >= hole_min.Y AndAlso Math.Max(z0, z1) <= hole_max.Y Then
                            Continue For
                        End If

                        mn.X = Math.Min(mn.X, Math.Min(x0, x1))
                        mn.Y = Math.Min(mn.Y, Math.Min(z0, z1))
                        mx.X = Math.Max(mx.X, Math.Max(x0, x1))
                        mx.Y = Math.Max(mx.Y, Math.Max(z0, z1))

                        list.Add(New vect3_32 With {
                            .x = CUInt((i + 0) + ((j + 1) * stride)),
                            .y = CUInt((i + 1) + ((j + 0) * stride)),
                            .z = CUInt((i + 0) + ((j + 0) * stride))})
                        list.Add(New vect3_32 With {
                            .x = CUInt((i + 0) + ((j + 1) * stride)),
                            .y = CUInt((i + 1) + ((j + 1) * stride)),
                            .z = CUInt((i + 1) + ((j + 0) * stride))})
                    Next
                Next

                ' Blocks fully swallowed by the hole emit nothing - drop them.
                If list.Count * 3 > first Then
                    block_list.Add(New MapTerrain.OutlandBlock With {
                        .first_index = CUInt(first),
                        .index_count = CUInt(list.Count * 3 - first),
                        .min_xz = mn,
                        .max_xz = mx})
                End If
            Next
        Next
        blocks = block_list.ToArray()
        Return list.ToArray()
    End Function

    Public Sub build_outland_vao()
        map_scene.terrain.outland_vertices_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "outland_vertices")

        Dim vcount = theMap.outland_Vdata.v_buff_XZ.Length
        Dim vsize = Marshal.SizeOf(Of OutlandVertex)

        With theMap.outland_Vdata
            Dim vertices(.v_buff_XZ.Length - 1) As OutlandVertex
            For j = 0 To .v_buff_XZ.Length - 1
                vertices(j).xy = .v_buff_XZ(j)
                vertices(j).uv = .uv_buff(j)
                vertices(j).packed_noraml = pack_2_10_10_10(.n_buff(j), 0)
                vertices(j).tangents = pack_2_10_10_10(.t_buff(j))
            Next
            map_scene.terrain.outland_vertices_buffer.Storage(vcount * vsize, vertices, BufferStorageFlags.DynamicStorageBit)

            .indicies = Nothing
            .v_buff_XZ = Nothing
            .uv_buff = Nothing
            .n_buff = Nothing
            .t_buff = Nothing
        End With

        ' Ring index buffers, one per cascade. The near ring's hole is the
        ' playfield terrain footprint (measured in create_outland, which also
        ' centres the whole outland on it); the far ring's hole is the near
        ' cascade's drawn footprint.
        Dim near_tris = build_outland_ring_indices(theMap.near_scale, theMap.center_offset,
                                                   theMap.terrain_footprint_min, theMap.terrain_footprint_max,
                                                   map_scene.terrain.outland_near_blocks)
        map_scene.terrain.outland_near_index_count = near_tris.Length * 3
        map_scene.terrain.outland_indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "outland_indices")
        map_scene.terrain.outland_indices_buffer.Storage(near_tris.Length * 12, near_tris, BufferStorageFlags.None)

        map_scene.terrain.outland_vao = make_outland_vao("outland_vao", vsize, map_scene.terrain.outland_indices_buffer)

        map_scene.terrain.outland_near_indirect = make_outland_indirect("outland_near_indirect",
                                                                        map_scene.terrain.outland_near_blocks)
        ReDim map_scene.terrain.outland_near_cmds(map_scene.terrain.outland_near_blocks.Length - 1)

        If map_scene.terrain.CASCADE_LEVELS = 2 Then
            Dim near_half As New Vector2(theMap.near_scale.X * 50.0F, theMap.near_scale.Y * 50.0F)
            Dim far_tris = build_outland_ring_indices(theMap.far_scale, theMap.center_offset,
                                                      theMap.center_offset - near_half, theMap.center_offset + near_half,
                                                      map_scene.terrain.outland_far_blocks)
            map_scene.terrain.outland_far_index_count = far_tris.Length * 3
            map_scene.terrain.outland_far_indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "outland_far_indices")
            map_scene.terrain.outland_far_indices_buffer.Storage(far_tris.Length * 12, far_tris, BufferStorageFlags.None)

            map_scene.terrain.outland_far_vao = make_outland_vao("outland_far_vao", vsize, map_scene.terrain.outland_far_indices_buffer)

            map_scene.terrain.outland_far_indirect = make_outland_indirect("outland_far_indirect",
                                                                           map_scene.terrain.outland_far_blocks)
            ReDim map_scene.terrain.outland_far_cmds(map_scene.terrain.outland_far_blocks.Length - 1)
        End If

        LogThis("outland cull blocks: near {0} far {1}",
                map_scene.terrain.outland_near_blocks.Length,
                If(map_scene.terrain.outland_far_blocks Is Nothing, 0, map_scene.terrain.outland_far_blocks.Length))
    End Sub

    ''' <summary>
    ''' Indirect command buffer sized for one cascade's cull blocks, filled by
    ''' Draw_outland each frame with the frustum survivors (SubData writes only
    ''' - never read back).
    ''' </summary>
    Private Function make_outland_indirect(name As String, blocks() As MapTerrain.OutlandBlock) As GLBuffer
        Dim buf = GLBuffer.Create(BufferTarget.DrawIndirectBuffer, name)
        buf.StorageNullData(blocks.Length * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                            BufferStorageFlags.DynamicStorageBit)
        Return buf
    End Function

    Private Function make_outland_vao(name As String, vsize As Integer, indices As GLBuffer) As GLVertexArray
        Dim vao = GLVertexArray.Create(name)

        ' VERTEX XZ
        vao.VertexBuffer(0, map_scene.terrain.outland_vertices_buffer, IntPtr.Zero, vsize)
        vao.AttribFormat(0, 2, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)

        ' UV
        vao.VertexBuffer(1, map_scene.terrain.outland_vertices_buffer, New IntPtr(8), vsize)
        vao.AttribFormat(1, 2, VertexAttribType.Float, False, 0)
        vao.AttribBinding(1, 1)
        vao.EnableAttrib(1)

        ' NORMALS AND HOLES
        vao.VertexBuffer(2, map_scene.terrain.outland_vertices_buffer, New IntPtr(16), vsize)
        vao.AttribFormat(2, 4, VertexAttribType.Int2101010Rev, True, 0)
        vao.AttribBinding(2, 2)
        vao.EnableAttrib(2)

        ' Tangents
        vao.VertexBuffer(3, map_scene.terrain.outland_vertices_buffer, New IntPtr(20), vsize)
        vao.AttribFormat(3, 4, VertexAttribType.Int2101010Rev, True, 0)
        vao.AttribBinding(3, 3)
        vao.EnableAttrib(3)

        vao.ElementBuffer(indices)
        Return vao
    End Function

    Public Sub build_Terrain_VAO()
        Dim mapsize As New Vector2(MAP_SIZE.X + 1, MAP_SIZE.Y + 1)

        CommonProperties.waterColor = Map_wetness.waterColor
        CommonProperties.waterAlpha = Map_wetness.waterAlpha
        CommonProperties.map_size.X = 1.0 / mapsize.X
        CommonProperties.map_size.Y = 1.0 / mapsize.Y
        CommonProperties.update()

        Dim terrainMatrices(theMap.chunks.Length - 1) As TerrainChunkInfo
        Dim terrainIndirect(theMap.chunks.Length - 1) As DrawElementsIndirectCommand

        map_scene.terrain.all_chunks_vao = GLVertexArray.Create("allTerrainChunks")

        map_scene.terrain.vertices_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "terrain_vertices")
        map_scene.terrain.indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "terrain_indices")

        Dim vcount = theMap.v_data(0).v_buff_XZ.Length * theMap.chunks.Length
        Dim vsize = Marshal.SizeOf(Of TerrainVertex)

        map_scene.terrain.vertices_buffer.StorageNullData(vcount * vsize, BufferStorageFlags.DynamicStorageBit)
        map_scene.terrain.indices_buffer.Storage(theMap.v_data(0).indicies.Length * 6, theMap.v_data(0).indicies, BufferStorageFlags.None)

        For i = 0 To theMap.chunks.Length - 1
            With theMap.v_data(i)
                Debug.Assert(.n_buff.Length = .h_buff.Length)

                terrainIndirect(i).count = 24576
                terrainIndirect(i).instanceCount = 1
                terrainIndirect(i).firstIndex = 0
                terrainIndirect(i).baseVertex = i * .v_buff_XZ.Length
                terrainIndirect(i).baseInstance = i

                terrainMatrices(i).modelMatrix = theMap.render_set(i).matrix
                terrainMatrices(i).g_uv_offset = Vector2.Divide((((theMap.chunks(i).location.Xy - New Vector2(50.0)) / 100.0) - New Vector2(b_x_min, b_y_max)), mapsize)
                terrainMatrices(i).g_uv_offset.Y += 1.0

                Dim vertices(.n_buff.Length - 1) As TerrainVertex
                For j = 0 To .n_buff.Length - 1
                    vertices(j).xyz.Xz = .v_buff_XZ(j)
                    vertices(j).xyz.Y = .v_buff_Y(j)
                    vertices(j).uv = .uv_buff(j)
                    vertices(j).packed_noraml = pack_2_10_10_10(.n_buff(j), .h_buff(j))
                    vertices(j).tangents = pack_2_10_10_10(.t_buff(j))
                Next

                GL.NamedBufferSubData(map_scene.terrain.vertices_buffer.buffer_id,
                                      New IntPtr(i * vertices.Length * vsize),
                                      vertices.Length * vsize,
                                      vertices)

                .indicies = Nothing
                .v_buff_XZ = Nothing
                .uv_buff = Nothing
                .v_buff_Y = Nothing
                .n_buff = Nothing
                .h_buff = Nothing
                .t_buff = Nothing
            End With
        Next

        ' VERTEX XYZ
        map_scene.terrain.all_chunks_vao.VertexBuffer(0, map_scene.terrain.vertices_buffer, IntPtr.Zero, vsize)
        map_scene.terrain.all_chunks_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        map_scene.terrain.all_chunks_vao.AttribBinding(0, 0)
        map_scene.terrain.all_chunks_vao.EnableAttrib(0)

        ' UV
        map_scene.terrain.all_chunks_vao.VertexBuffer(1, map_scene.terrain.vertices_buffer, New IntPtr(12), vsize)
        map_scene.terrain.all_chunks_vao.AttribFormat(1, 2, VertexAttribType.Float, False, 0)
        map_scene.terrain.all_chunks_vao.AttribBinding(1, 1)
        map_scene.terrain.all_chunks_vao.EnableAttrib(1)

        ' NORMALS AND HOLES
        map_scene.terrain.all_chunks_vao.VertexBuffer(2, map_scene.terrain.vertices_buffer, New IntPtr(20), vsize)
        map_scene.terrain.all_chunks_vao.AttribFormat(2, 4, VertexAttribType.Int2101010Rev, True, 0)
        map_scene.terrain.all_chunks_vao.AttribBinding(2, 2)
        map_scene.terrain.all_chunks_vao.EnableAttrib(2)

        ' Tangents
        map_scene.terrain.all_chunks_vao.VertexBuffer(3, map_scene.terrain.vertices_buffer, New IntPtr(24), vsize)
        map_scene.terrain.all_chunks_vao.AttribFormat(3, 4, VertexAttribType.Int2101010Rev, True, 0)
        map_scene.terrain.all_chunks_vao.AttribBinding(3, 3)
        map_scene.terrain.all_chunks_vao.EnableAttrib(3)

        map_scene.terrain.all_chunks_vao.ElementBuffer(map_scene.terrain.indices_buffer)

        map_scene.terrain.indirect_buffer = GLBuffer.Create(BufferTarget.DrawIndirectBuffer, "terrain_indirect")
        map_scene.terrain.indirect_buffer.Storage(terrainIndirect.Length * Marshal.SizeOf(Of DrawElementsIndirectCommand), terrainIndirect, BufferStorageFlags.None)

        map_scene.terrain.matrices = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "terrain_matrices")
        map_scene.terrain.matrices.Storage(terrainMatrices.Length * Marshal.SizeOf(Of TerrainChunkInfo), terrainMatrices, BufferStorageFlags.None)
        map_scene.terrain.matrices.BindBase(10)
    End Sub

    Public Sub get_holes(ByRef c As chunk_, ByRef v As terrain_V_data_)

        'Unpacks and creates hole data
        ReDim v.holes(63, 63)
        hole_size = 63

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
        If w = 8 Then ' nothing so return empty hole array
            ps.Dispose()
            ms.Dispose()
            Return

        End If
        hole_size = h * 2 - 1
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

    Public Sub get_heights(ByRef c As chunk_, ByRef v As terrain_V_data_)
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
        Dim data(h_width * h_height * 4 - 1) As Byte
        Dim cnt As UInt32 = 0
        Using r
            r.Position = 36 'skip bigworld header stuff
            Dim rdr As New PngReader(r) ' create png from stream 's'
            Dim iInfo = rdr.ImgInfo
            mapsize = iInfo.Cols

            ReDim data(iInfo.Cols * iInfo.Cols * 4 - 1)
            Dim iline As ImageLine  ' create place to hold a scan line
            For i = 0 To iInfo.Cols - 1
                iline = rdr.ReadRow(i)
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
        HEIGHTMAPSIZE = mapsize


        ReDim v.heightsTBL(69, 69)
        ReDim v.heights(mapsize, mapsize)
        For j As UInt32 = 0 To mapsize - 1
            For i As UInt32 = 0 To mapsize - 1
                ms.Position = (i * 4) + (j * mapsize * 4)
                Dim tc = br.ReadInt32
                quantized = tc * 0.001
                v.heights(mapsize - i, j) = quantized
                v.heightsTBL(mapsize - i, j) = quantized
            Next
        Next

        'going to average the hights if there is only 37 x 37
        'DO NOT TOUCH THIS CODE MIKE!!!
        'We must shift the column to the left to allow for averaging.
        If mapsize < 69 Then
            For j = 0 To 36
                For i = 0 To 37
                    v.heights(j, i) = v.heights(j + 1, i)
                Next
            Next
            Dim xx, yy As Integer
            yy = 0
            For j = 1 To 68
                xx = 0
                For i = 0 To 68
                    Dim aa = v.heights(i * 0.5 + 0, j * 0.5 + 0)
                    Dim bb = v.heights(i * 0.5 + 1, j * 0.5 + 0)

                    Dim cc = v.heights(i * 0.5 + 0, j * 0.5 + 1)
                    Dim dd = v.heights(i * 0.5 + 1, j * 0.5 + 1)

                    v.heightsTBL(xx, yy) = (aa + bb + cc + dd) / 4.0F
                    xx += 1
                Next
                yy += 1
            Next
        End If


        ' This Is important!
        ' DONT DELETE THIS
        Dim y_max, y_min As Single
        y_min = 1000.0F
        For j As UInt32 = 1 To mapsize - 1
            For i As UInt32 = 1 To mapsize - 1

                MEAN_MAP_HEIGHT += v.heights(i, j) '<---- this is important. DONT DELETE THIS

                TOTAL_HEIGHT_COUNT += 1

                If v.heights(i, j) < y_min Then
                    y_min = v.heights(i, j)
                End If
                If v.heights(i, j) > y_max Then
                    y_max = v.heights(i, j)
                End If
            Next
        Next
        c.heights_data = Nothing
        v.avg_heights = (y_max + y_min) / 2.0F ' used for fog

        MAX_MAP_HEIGHT = Max(MAX_MAP_HEIGHT, y_max)
        MIN_MAP_HEIGHT = Min(MIN_MAP_HEIGHT, y_min)

        v.max_height = MAX_MAP_HEIGHT
        v.min_height = MIN_MAP_HEIGHT
        br.Close()
        ms.Close()
        ms.Dispose()
        'End If
    End Sub

    Public Sub set_map_bs()
        MAX_MAP_HEIGHT = Single.MinValue
        MIN_MAP_HEIGHT = Single.MaxValue
        b_x_max = Single.MinValue
        b_x_min = Single.MaxValue
        b_y_max = Single.MinValue
        b_y_min = Single.MaxValue
    End Sub

    Public Sub get_location(ByRef c As chunk_, map_id As Integer)
        'This routine gets the maps location in the world grid from its name
        Dim x = -Convert.ToInt16(c.name.Substring(0, 4), 16) - 1
        Dim y = Convert.ToInt16(c.name.Substring(4, 4), 16) + 1

        c.location.X = (x * 100.0) + 50.0
        c.location.Y = (y * 100.0) - 50.0

        Const center = MAP_BOARD_SIZE \ 2
        c.mBoard_x = x + center
        c.mBoard_y = y + center

        With mapBoard(c.mBoard_x, c.mBoard_y)
            .map_id = map_id
            .location = c.location.Xy
            .occupied = True
        End With

        b_x_min = Min(b_x_min, x)
        b_x_max = Max(b_x_max, x)
        b_y_min = Min(b_y_min, y)
        b_y_max = Max(b_y_max, y)

        MAP_SIZE.X = b_x_max - b_x_min
        MAP_SIZE.Y = b_y_max - b_y_min
    End Sub

    Private Sub get_translated_bb_terrain(ByRef BB() As Vector3, ByRef c As terrain_V_data_)
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

    ''' <summary>
    ''' get_Y_at_XZ without the mapBoard scan: the board cell is computed
    ''' directly from the world position, then the same bilinear/triangle
    ''' sample runs on the chunk height table. Local state only - safe to call
    ''' a few hundred thousand times at load (the scanning original is far
    ''' slower per call; bulk lookups through it are what froze the load).
    ''' </summary>
    Public Function get_Y_at_XZ_fast(ByVal Lx As Double, ByVal Lz As Double) As Single
        If mapBoard Is Nothing Then Return 0.0F

        Lx += 0.01
        Lz += 0.01

        ' chunk x covers (x*100, x*100+100]; chunk z covers (y*100-100, y*100]
        Dim cx = CInt(Math.Ceiling(Lx / 100.0)) - 1
        Dim cy = CInt(Math.Ceiling(Lz / 100.0))
        Const centre = MAP_BOARD_SIZE \ 2
        Dim bx = cx + centre
        Dim by = cy + centre
        If bx < 0 OrElse by < 0 OrElse bx >= MAP_BOARD_SIZE OrElse by >= MAP_BOARD_SIZE Then Return 0.0F
        If Not mapBoard(bx, by).occupied Then Return 0.0F
        Dim map = mapBoard(bx, by).map_id

        Dim tlx As Single = 100.0 / 65.0
        Dim vxp As Double = ((((Lx) / 100)) - Truncate((Truncate(Lx) / 100))) * 65.0
        Dim vyp As Double = ((((Lz) / 100)) - Truncate((Truncate(Lz) / 100))) * 65.0
        If vyp < 0.0 Then vyp = 65.0 + vyp
        If vxp < 0 Then vxp = 65.0 + vxp
        vxp = Round(vxp, 12)
        vyp = Round(vyp, 12)

        Dim rxp As Single = Floor(vxp) * tlx
        Dim ryp As Single = Floor(vyp) * tlx

        Dim w, tl, tr, br, bl As Vector3
        w.X = (vxp * tlx)
        w.Y = (vyp * tlx)

        Dim hx = CInt(Floor(vxp))
        Dim hy = CInt(Floor(vyp))
        If hx + 1 > 65 Then Return 0
        hx += 3
        hy += 2

        tl.X = rxp : tl.Y = ryp
        tl.Z = theMap.v_data(map).heightsTBL(hx, hy)
        tr.X = rxp + tlx : tr.Y = ryp
        tr.Z = theMap.v_data(map).heightsTBL(hx + 1, hy)
        br.X = rxp + tlx : br.Y = ryp + tlx
        br.Z = theMap.v_data(map).heightsTBL(hx + 1, hy + 1)
        bl.X = rxp : bl.Y = ryp + tlx
        bl.Z = theMap.v_data(map).heightsTBL(hx, hy + 1)

        Dim agl = Atan2(w.Y - tr.Y, w.X - tr.X)
        If agl <= PI * 0.75 Then
            Return find_altitude(tr, bl, br, w)
        End If
        Return find_altitude(tr, tl, bl, w)
    End Function

    ''' <summary>
    ''' The data weld: rewrites the near cascade's heightmap texels in and
    ''' around the terrain footprint with the terrain's own surface height, so
    ''' the outland lands on the terrain edge BY DATA - at any mesh density.
    ''' Inside the footprint the sheet tucks under the terrain (small lip);
    ''' exactly at the footprint line it matches the terrain; over
    ''' OUTLAND_WELD_BAND metres outside it blends back to the authored
    ''' outland. Values are stored +1.5 to cancel the VS's seam sink.
    ''' Runs after MAP_LOADED (needs the chunk height tables).
    ''' </summary>
    Public Sub patch_outland_heightmap()
        Dim tex = map_scene.terrain.OUTLAND_height_MAP
        If tex Is Nothing Then Return

        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim w, h As Integer
        GL4.GL.GetTextureLevelParameter(tex.texture_id, 0, GL4.GetTextureParameter.TextureWidth, w)
        GL4.GL.GetTextureLevelParameter(tex.texture_id, 0, GL4.GetTextureParameter.TextureHeight, h)
        Dim px(w * h - 1) As UShort
        GL4.GL.GetTextureImage(tex.texture_id, 0, GL4.PixelFormat.Red, GL4.PixelType.UnsignedShort, px.Length * 2, px)

        ' sanity: the fast lookup must agree with the scanning original
        Dim worst As Single = 0
        For i = 0 To 8
            Dim sxw = theMap.terrain_footprint_min.X + 10 + i * (theMap.terrain_footprint_max.X - theMap.terrain_footprint_min.X - 20) / 8.0F
            Dim szw = theMap.terrain_footprint_min.Y + 10 + i * (theMap.terrain_footprint_max.Y - theMap.terrain_footprint_min.Y - 20) / 8.0F
            worst = Math.Max(worst, Math.Abs(get_Y_at_XZ_fast(sxw, szw) - get_Y_at_XZ(sxw, szw)))
        Next

        Dim fmin = theMap.terrain_footprint_min
        Dim fmax = theMap.terrain_footprint_max
        Dim band = MapTerrain.OUTLAND_WELD_BAND
        Dim y_range = theMap.near_y_height
        Dim y_off = theMap.near_y_offset
        Dim n = 0

        ' ---- orientation audit --------------------------------------------
        ' Scores the four possible heightmap mirrorings against the terrain's
        ' own edge heights, 128 samples around the footprint boundary. The
        ' winner should be "mirror-both" (the -uv REPEAT convention the
        ' shaders use); if another orientation wins the mapping is wrong for
        ' this map and the seam builds cliffs.
        Dim gsize_a = CSng(MapTerrain.OUTLAND_GRID)
        Dim ghalf_a = gsize_a / 2.0F
        Dim gms_a = 100.0F / (gsize_a - 1.0F)
        Dim onames = {"mirror-both", "no-mirror", "mirror-x-only", "mirror-z-only"}
        Dim edge_err(3) As Double
        Dim edge_max(3) As Double
        For o = 0 To 3
            Dim errsum As Double = 0
            Dim ns = 0
            If o = 0 Then
                For k = 0 To 3
                    edge_err(k) = 0 : edge_max(k) = 0
                Next
            End If
            For k = 0 To 127
                Dim t = (k Mod 32) / 31.0F
                Dim wx, wz As Single
                Select Case k \ 32
                    Case 0 : wx = fmin.X + t * (fmax.X - fmin.X) : wz = fmin.Y + 1.0F
                    Case 1 : wx = fmin.X + t * (fmax.X - fmin.X) : wz = fmax.Y - 1.0F
                    Case 2 : wx = fmin.X + 1.0F : wz = fmin.Y + t * (fmax.Y - fmin.Y)
                    Case Else : wx = fmax.X - 1.0F : wz = fmin.Y + t * (fmax.Y - fmin.Y)
                End Select
                Dim ty_ = get_Y_at_XZ_fast(wx, wz)
                ' world -> mesh -> vertex uv
                Dim mxa = (wx - theMap.center_offset.X) / theMap.near_scale.X
                Dim mza = (wz - theMap.center_offset.Y) / theMap.near_scale.Y
                Dim ua = ((mxa - 0.04888F) / gms_a + ghalf_a) / gsize_a
                Dim va = ((mza - 0.04888F) / gms_a + ghalf_a) / gsize_a
                ' orientation variants of the texture lookup
                Dim tu = Math.Clamp(If(o = 0 OrElse o = 2, 1.0F - ua, ua), 0.0F, 1.0F)
                Dim tv = Math.Clamp(If(o = 0 OrElse o = 3, 1.0F - va, va), 0.0F, 1.0F)
                Dim fx = tu * w - 0.5F
                Dim fy = tv * h - 0.5F
                Dim xi = CInt(Math.Floor(fx)) : Dim yi = CInt(Math.Floor(fy))
                Dim ax = fx - xi : Dim ay = fy - yi
                Dim x1i = ((xi + 1) Mod w + w) Mod w : Dim y1i = ((yi + 1) Mod h + h) Mod h
                xi = ((xi Mod w) + w) Mod w : yi = ((yi Mod h) + h) Mod h
                Dim hs = (px(yi * w + xi) * (1 - ax) + px(yi * w + x1i) * ax) * (1 - ay) +
                         (px(y1i * w + xi) * (1 - ax) + px(y1i * w + x1i) * ax) * ay
                Dim oy = CSng(hs / 65535.0F * y_range + y_off - 1.5F)
                Dim d_ = Math.Abs(oy - ty_)
                errsum += d_
                If o = 0 Then
                    edge_err(k \ 32) += d_
                    edge_max(k \ 32) = Math.Max(edge_max(k \ 32), d_)
                End If
                ns += 1
            Next
            Console.WriteLine("outland orientation {0}: mean seam error {1:0.0} m", onames(o), errsum / ns)
        Next
        Console.WriteLine("outland seam by edge (current orientation): N mean {0:0.0} max {1:0.0} | S mean {2:0.0} max {3:0.0} | W mean {4:0.0} max {5:0.0} | E mean {6:0.0} max {7:0.0}",
                          edge_err(0) / 32, edge_max(0), edge_err(1) / 32, edge_max(1),
                          edge_err(2) / 32, edge_max(2), edge_err(3) / 32, edge_max(3))

        ' Adaptive blend band: bridging the seam's worst mismatch inside the
        ' default 45 m band builds near-vertical smeared walls on alpine maps
        ' (lakeville: 210 m spikes). Spread the blend over ~2.5x the worst
        ' mismatch instead, capped so farm maps keep their tight seam.
        Dim gmax = Math.Max(Math.Max(edge_max(0), edge_max(1)), Math.Max(edge_max(2), edge_max(3)))
        band = Math.Clamp(CSng(gmax) * 2.5F, MapTerrain.OUTLAND_WELD_BAND, 400.0F)
        Console.WriteLine("outland weld band: {0:0} m (worst seam mismatch {1:0.0} m)", band, gmax)

        Dim gsize = CSng(MapTerrain.OUTLAND_GRID)
        Dim ghalf = gsize / 2.0F
        Dim gms = 100.0F / (gsize - 1.0F)

        ' Exact terrain minimum per heightmap texel. Point-sampling the terrain
        ' at texel centres (or any sparse pattern) misses narrow ravines - the
        ' first attempt still left midpoints up to 8.5 m above the gully floor
        ' on prohorovka. Instead walk every terrain board vertex (the 100/64 m
        ' grid is anchored at the footprint corner, so stepping it hits the
        ' vertices exactly) and min it into every texel whose bilinear support
        ' contains it (radius one texel). Each texel below the min of its
        ' support puts the whole interpolated sheet below the terrain,
        ' triangulation included.
        Const BOARD_STEP As Single = 100.0F / 64.0F
        Dim min_map(w * h - 1) As Single
        For i = 0 To min_map.Length - 1
            min_map(i) = Single.MaxValue
        Next
        Dim nbx = CInt(Math.Floor((fmax.X - fmin.X) / BOARD_STEP)) + 1
        Dim nbz = CInt(Math.Floor((fmax.Y - fmin.Y) / BOARD_STEP)) + 1
        For iz = 0 To nbz - 1
            Dim bz = fmin.Y + iz * BOARD_STEP
            Dim szw = Math.Clamp(bz, fmin.Y + 0.5F, fmax.Y - 0.5F)
            Dim vv = (((bz - theMap.center_offset.Y) / theMap.near_scale.Y - 0.04888F) / gms + ghalf) / gsize
            Dim fy = (1.0F - vv) * h - 0.5F
            Dim ty0 = Math.Max(0, CInt(Math.Ceiling(fy - 1.0F)))
            Dim ty1 = Math.Min(h - 1, CInt(Math.Floor(fy + 1.0F)))
            For ix = 0 To nbx - 1
                Dim bx = fmin.X + ix * BOARD_STEP
                Dim sxw = Math.Clamp(bx, fmin.X + 0.5F, fmax.X - 0.5F)
                Dim vy = get_Y_at_XZ_fast(sxw, szw)
                Dim uu = (((bx - theMap.center_offset.X) / theMap.near_scale.X - 0.04888F) / gms + ghalf) / gsize
                Dim fx = (1.0F - uu) * w - 0.5F
                Dim tx0 = Math.Max(0, CInt(Math.Ceiling(fx - 1.0F)))
                Dim tx1 = Math.Min(w - 1, CInt(Math.Floor(fx + 1.0F)))
                For tyi = ty0 To ty1
                    For txi = tx0 To tx1
                        If vy < min_map(tyi * w + txi) Then min_map(tyi * w + txi) = vy
                    Next
                Next
            Next
        Next

        ' The cascade's authored Y range can sit ABOVE the terrain's deepest
        ' spots (prohorovka's river gorge bottoms out ~9 m below the authored
        ' floor): the tuck encode then clamps at 0, the sheet rides its floor
        ' and cuts through the valley - no weld target can fix an unencodable
        ' height, which is exactly what the crossing audit kept catching at
        ' +8.46 m with pxL=0. Extend the encoded floor below the terrain
        ' minimum and re-encode the whole map into the new frame; the draw
        ' reads y_offset/range from theMap every frame so the shader follows.
        Dim terrain_min As Single = Single.MaxValue
        For i = 0 To min_map.Length - 1
            If min_map(i) < terrain_min Then terrain_min = min_map(i)
        Next
        Dim need_floor = terrain_min - 2.0F
        If terrain_min < Single.MaxValue AndAlso need_floor < y_off Then
            Dim top = y_off + y_range
            Dim new_range = top - need_floor
            For i = 0 To px.Length - 1
                Dim wy = px(i) / 65535.0F * y_range + y_off
                px(i) = CUShort(Math.Clamp((wy - need_floor) / new_range, 0.0F, 1.0F) * 65535.0F)
            Next
            Console.WriteLine("outland Y floor extended: {0:0.0} -> {1:0.0} m (terrain min {2:0.0})",
                              y_off, need_floor, terrain_min)
            y_off = need_floor
            y_range = new_range
            theMap.near_y_offset = y_off
            theMap.near_y_height = y_range
        End If

        For ty = 0 To h - 1
            ' texel centre -> vertex uv -> mesh xy -> world (inverting the
            ' shader's -uv REPEAT sampling and the grid's affine uv map;
            ' MUST use the same OUTLAND_GRID affine the mesh is built with)
            Dim v = 1.0F - (ty + 0.5F) / h
            Dim my = ((v * gsize) - ghalf) * gms + 0.04888F
            Dim world_z = my * theMap.near_scale.Y + theMap.center_offset.Y
            Dim dz = Math.Max(fmin.Y - world_z, world_z - fmax.Y)
            If dz > band Then Continue For

            For tx = 0 To w - 1
                Dim u = 1.0F - (tx + 0.5F) / w
                Dim mx = ((u * gsize) - ghalf) * gms + 0.04888F
                Dim world_x = mx * theMap.near_scale.X + theMap.center_offset.X
                Dim dx = Math.Max(fmin.X - world_x, world_x - fmax.X)
                Dim d = Math.Max(dx, dz)
                If d > band Then Continue For

                Dim cxw = Math.Clamp(world_x, fmin.X + 0.5F, fmax.X - 0.5F)
                Dim czw = Math.Clamp(world_z, fmin.Y + 0.5F, fmax.Y - 0.5F)
                Dim terrain_y = get_Y_at_XZ_fast(cxw, czw)

                Dim target As Single
                If d <= 0.0F Then
                    ' Tucked band. The sheet must stay under the terrain
                    ' EVERYWHERE inside the footprint, not just at texel
                    ' centres: between centres the terrain dips into trenches
                    ' and ravines while the sheet's interpolation floats
                    ' across and pokes out - those pixels win the depth test
                    ' honestly in either draw order, and the winner flips as
                    ' the camera settles: the far-field shading flicker.
                    ' min_map holds the exact terrain minimum over this
                    ' texel's support; the lip keeps a small floor even at
                    ' the footprint line.
                    Dim tmin = terrain_y
                    Dim mm = min_map(ty * w + tx)
                    If mm < Single.MaxValue Then tmin = Math.Min(tmin, mm)
                    Dim lip = 0.15F + 0.6F * Math.Clamp(-d / 10.0F, 0.0F, 1.0F)
                    target = tmin - lip
                Else
                    Dim t = d / band
                    t = t * t * (3.0F - 2.0F * t)
                    Dim authored = px(ty * w + tx) / 65535.0F * y_range + y_off - 1.5F
                    target = terrain_y * (1.0F - t) + authored * t
                End If

                ' +1.5 cancels the VS seam sink so `target` is what renders
                Dim enc = (target + 1.5F - y_off) / y_range
                px(ty * w + tx) = CUShort(Math.Clamp(enc, 0.0F, 1.0F) * 65535.0F)
                n += 1
            Next
        Next

        GL4.GL.TextureSubImage2D(tex.texture_id, 0, 0, 0, w, h, GL4.PixelFormat.Red, GL4.PixelType.UnsignedShort, px)

        LogThis("outland heightmap patch: {0} of {1}x{2} texels welded in {3} ms (fast-lookup check: {4:0.000} m)",
                n, w, h, sw.ElapsedMilliseconds, worst)
        Console.WriteLine("outland heightmap patch: {0} texels in {1} ms (check {2:0.000} m)", n, sw.ElapsedMilliseconds, worst)

        ' ---- crossing audit -----------------------------------------------
        ' Does the PATCHED sheet still rise above the terrain anywhere between
        ' texel centres? Checks the bilinear midpoint of each interior texel
        ' pair against the terrain there. Any positive count here is pixels
        ' that can z-fight the playfield.
        Dim cross_n = 0
        Dim cross_max As Single = 0
        For ty = 0 To h - 1
            Dim v = 1.0F - (ty + 0.5F) / h
            Dim my = ((v * gsize) - ghalf) * gms + 0.04888F
            Dim world_z = my * theMap.near_scale.Y + theMap.center_offset.Y
            If world_z < fmin.Y + 1.0F OrElse world_z > fmax.Y - 1.0F Then Continue For
            For tx = 0 To w - 2
                Dim u = 1.0F - (tx + 1.0F) / w   ' midpoint between tx and tx+1
                Dim mx = ((u * gsize) - ghalf) * gms + 0.04888F
                Dim world_x = mx * theMap.near_scale.X + theMap.center_offset.X
                If world_x < fmin.X + 1.0F OrElse world_x > fmax.X - 1.0F Then Continue For
                Dim sheet = (CSng(px(ty * w + tx)) + CSng(px(ty * w + tx + 1))) * 0.5F / 65535.0F * y_range + y_off - 1.5F
                Dim excess = sheet - get_Y_at_XZ_fast(world_x, world_z)
                If excess > 0.0F Then
                    cross_n += 1
                    cross_max = Math.Max(cross_max, excess)
                    If cross_n <= 5 Then
                        Console.WriteLine("  crossing: tx={0} ty={1} world=({2:0.0},{3:0.0}) sheet={4:0.00} terrain={5:0.00} pxL={6} pxR={7} minmapL={8:0.00} minmapR={9:0.00}",
                                          tx, ty, world_x, world_z, sheet, get_Y_at_XZ_fast(world_x, world_z),
                                          px(ty * w + tx), px(ty * w + tx + 1),
                                          min_map(ty * w + tx), min_map(ty * w + tx + 1))
                    End If
                End If
            Next
        Next
        Console.WriteLine("outland crossing audit: {0} midpoints above terrain (worst +{1:0.00} m)", cross_n, cross_max)

        ' Raw 16-bit dump of the PATCHED near heightmap for offline mesh work
        ' (the PNG dumps are 8-bit - ~6 m steps, useless for geometry).
        ' Header: i32 w, i32 h, f32 y_off, f32 y_range, f32 scale.xy,
        ' f32 center.xy, then w*h u16 rows.
        Try
            Dim rawdir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra")
            IO.Directory.CreateDirectory(rawdir)
            Using bw As New IO.BinaryWriter(IO.File.Create(IO.Path.Combine(rawdir, "outland_height_near_patched.raw")))
                bw.Write(w) : bw.Write(h)
                bw.Write(y_off) : bw.Write(y_range)
                bw.Write(theMap.near_scale.X) : bw.Write(theMap.near_scale.Y)
                bw.Write(theMap.center_offset.X) : bw.Write(theMap.center_offset.Y)
                For Each v16 In px
                    bw.Write(v16)
                Next
            End Using
        Catch
            ' a locked temp file must never break the load
        End Try

        ' ---- second ring: weld the FAR cascade to the near one --------------
        ' The far heightmap is coarse and authored independently; at the near
        ' cascade's outer edge the two can disagree by 100-200 m on alpine
        ' maps, which drew a smeared wall around the whole near cascade. Same
        ' data-weld: far texels near the near-cascade rect are dragged to the
        ' near cascade's RENDERED height (post-patch), blending back to the
        ' far cascade's own data over an adaptive band.
        If map_scene.terrain.CASCADE_LEVELS <> 2 OrElse map_scene.terrain.OUTLAND_height_CASCADE_MAP Is Nothing Then
            kick_outland_decimation(px, w, h, Nothing, 0, 0)
            Return
        End If

        Dim tex2 = map_scene.terrain.OUTLAND_height_CASCADE_MAP
        Dim w2, h2 As Integer
        GL4.GL.GetTextureLevelParameter(tex2.texture_id, 0, GL4.GetTextureParameter.TextureWidth, w2)
        GL4.GL.GetTextureLevelParameter(tex2.texture_id, 0, GL4.GetTextureParameter.TextureHeight, h2)
        Dim px2(w2 * h2 - 1) As UShort
        GL4.GL.GetTextureImage(tex2.texture_id, 0, GL4.PixelFormat.Red, GL4.PixelType.UnsignedShort, px2.Length * 2, px2)
        Dim yr2 = theMap.far_y_height
        Dim yo2 = theMap.far_y_offset

        ' the near cascade's drawn world rect (mesh spans -ghalf*gms..+ghalf*gms)
        Dim mesh_lo = (0.0F - ghalf) * gms + 0.04888F
        Dim mesh_hi = (gsize - 1.0F - ghalf) * gms + 0.04888F
        Dim nmin As New Vector2(theMap.center_offset.X + mesh_lo * theMap.near_scale.X,
                                theMap.center_offset.Y + mesh_lo * theMap.near_scale.Y)
        Dim nmax As New Vector2(theMap.center_offset.X + mesh_hi * theMap.near_scale.X,
                                theMap.center_offset.Y + mesh_hi * theMap.near_scale.Y)

        ' audit the ring - all four far-map orientations, in case the far
        ' cascade is registered differently - then set the blend band from
        ' the winning orientation's worst mismatch
        Dim ring_max As Double = 0
        For o = 0 To 3
            Dim omax As Double = 0
            Dim osum As Double = 0
            For k = 0 To 63
                Dim t = (k Mod 16) / 15.0F
                Dim wx, wz As Single
                Select Case k \ 16
                    Case 0 : wx = nmin.X + t * (nmax.X - nmin.X) : wz = nmin.Y + 2.0F
                    Case 1 : wx = nmin.X + t * (nmax.X - nmin.X) : wz = nmax.Y - 2.0F
                    Case 2 : wx = nmin.X + 2.0F : wz = nmin.Y + t * (nmax.Y - nmin.Y)
                    Case Else : wx = nmax.X - 2.0F : wz = nmin.Y + t * (nmax.Y - nmin.Y)
                End Select
                Dim nh = sample_outland_px(px, w, h, wx, wz, theMap.near_scale, y_range, y_off)
                Dim fh = sample_outland_px_oriented(px2, w2, h2, wx, wz, theMap.far_scale, yr2, yo2, o)
                omax = Math.Max(omax, Math.Abs(nh - fh))
                osum += Math.Abs(nh - fh)
            Next
            Console.WriteLine("outland far ring orientation {0}: mean {1:0.0} max {2:0.0} m",
                              {"mirror-both", "no-mirror", "mirror-x", "mirror-z"}(o), osum / 64.0, omax)
            If o = 0 Then ring_max = omax
        Next
        Dim band2 = Math.Clamp(CSng(ring_max) * 2.5F, 150.0F, 1200.0F)

        ' calibration probe: all three surfaces must roughly agree at centre
        Dim cxp = theMap.center_offset.X
        Dim czp = theMap.center_offset.Y
        Console.WriteLine("outland centre probe: terrain {0:0.0}  near {1:0.0}  far {2:0.0}  (far y_off {3:0.0} range {4:0.0})",
                          get_Y_at_XZ_fast(cxp, czp),
                          sample_outland_px(px, w, h, cxp, czp, theMap.near_scale, y_range, y_off),
                          sample_outland_px(px2, w2, h2, cxp, czp, theMap.far_scale, yr2, yo2),
                          yo2, yr2)

        Dim n2 = 0
        For ty = 0 To h2 - 1
            Dim v = 1.0F - (ty + 0.5F) / h2
            Dim mz2 = ((v * gsize) - ghalf) * gms + 0.04888F
            Dim wz2 = mz2 * theMap.far_scale.Y + theMap.center_offset.Y
            Dim dz2 = Math.Max(nmin.Y - wz2, wz2 - nmax.Y)
            If dz2 > band2 Then Continue For
            For tx = 0 To w2 - 1
                Dim u = 1.0F - (tx + 0.5F) / w2
                Dim mx2 = ((u * gsize) - ghalf) * gms + 0.04888F
                Dim wx2 = mx2 * theMap.far_scale.X + theMap.center_offset.X
                Dim dx2 = Math.Max(nmin.X - wx2, wx2 - nmax.X)
                Dim d2 = Math.Max(dx2, dz2)
                If d2 > band2 Then Continue For

                Dim cx2 = Math.Clamp(wx2, nmin.X + 1.0F, nmax.X - 1.0F)
                Dim cz2 = Math.Clamp(wz2, nmin.Y + 1.0F, nmax.Y - 1.0F)
                Dim near_h = sample_outland_px(px, w, h, cx2, cz2, theMap.near_scale, y_range, y_off)

                Dim target2 As Single
                If d2 <= 0.0F Then
                    Dim lip2 = 1.0F * Math.Clamp(-d2 / 60.0F, 0.0F, 1.0F)
                    target2 = near_h - lip2
                Else
                    Dim t2 = d2 / band2
                    t2 = t2 * t2 * (3.0F - 2.0F * t2)
                    Dim authored2 = CSng(px2(ty * w2 + tx) / 65535.0F * yr2 + yo2 - 1.5F)
                    target2 = near_h * (1.0F - t2) + authored2 * t2
                End If

                Dim enc2 = (target2 + 1.5F - yo2) / yr2
                px2(ty * w2 + tx) = CUShort(Math.Clamp(enc2, 0.0F, 1.0F) * 65535.0F)
                n2 += 1
            Next
        Next

        GL4.GL.TextureSubImage2D(tex2.texture_id, 0, 0, 0, w2, h2, GL4.PixelFormat.Red, GL4.PixelType.UnsignedShort, px2)
        Console.WriteLine("outland far-cascade weld: {0} texels, band {1:0} m (ring mismatch {2:0.0} m)", n2, band2, ring_max)

        dump_heightmap_png(px, w, h, "outland_height_near_patched")
        dump_heightmap_png(px2, w2, h2, "outland_height_far_patched")

        ' Both welds are final - hand the patched grids to the background
        ' decimator. The full-res grid keeps drawing until it finishes.
        kick_outland_decimation(px, w, h, px2, w2, h2)
    End Sub

    ''' <summary>Debug: 8-bit visualisation of a heightmap to %TEMP%\nuTerra.</summary>
    Private Sub dump_heightmap_png(px As UShort(), w As Integer, h As Integer, name As String)
        Try
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra")
            IO.Directory.CreateDirectory(dir)
            Using bmp As New Drawing.Bitmap(w, h, Drawing.Imaging.PixelFormat.Format32bppArgb)
                Dim bd = bmp.LockBits(New Drawing.Rectangle(0, 0, w, h), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb)
                Dim rowbuf(w * 4 - 1) As Byte
                For y = 0 To h - 1
                    For x = 0 To w - 1
                        Dim g = CByte(px(y * w + x) >> 8)
                        rowbuf(x * 4 + 0) = g
                        rowbuf(x * 4 + 1) = g
                        rowbuf(x * 4 + 2) = g
                        rowbuf(x * 4 + 3) = 255
                    Next
                    Marshal.Copy(rowbuf, 0, bd.Scan0 + y * bd.Stride, w * 4)
                Next
                bmp.UnlockBits(bd)
                bmp.Save(IO.Path.Combine(dir, name + ".png"), Drawing.Imaging.ImageFormat.Png)
            End Using
        Catch ex As Exception
            Console.WriteLine("heightmap dump failed: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>sample_outland_px under one of the four mirror orientations -
    ''' audit use only (0 = mirror-both, the shader's own convention).</summary>
    Private Function sample_outland_px_oriented(px As UShort(), w As Integer, h As Integer,
                                                wx As Single, wz As Single,
                                                scale As Vector2, y_range As Single, y_off As Single,
                                                o As Integer) As Single
        Dim gsize = CSng(MapTerrain.OUTLAND_GRID)
        Dim ghalf = gsize / 2.0F
        Dim gms = 100.0F / (gsize - 1.0F)
        Dim mx = (wx - theMap.center_offset.X) / scale.X
        Dim mz = (wz - theMap.center_offset.Y) / scale.Y
        Dim u = ((mx - 0.04888F) / gms + ghalf) / gsize
        Dim v = ((mz - 0.04888F) / gms + ghalf) / gsize
        Dim tu = Math.Clamp(If(o = 0 OrElse o = 2, 1.0F - u, u), 0.0F, 1.0F)
        Dim tv = Math.Clamp(If(o = 0 OrElse o = 3, 1.0F - v, v), 0.0F, 1.0F)
        Dim fx = tu * w - 0.5F
        Dim fy = tv * h - 0.5F
        Dim x0 = CInt(Math.Floor(fx)) : Dim y0 = CInt(Math.Floor(fy))
        Dim ax = fx - x0 : Dim ay = fy - y0
        Dim x1 = Math.Clamp(x0 + 1, 0, w - 1) : Dim y1 = Math.Clamp(y0 + 1, 0, h - 1)
        x0 = Math.Clamp(x0, 0, w - 1) : y0 = Math.Clamp(y0, 0, h - 1)
        Dim s = (px(y0 * w + x0) * (1 - ax) + px(y0 * w + x1) * ax) * (1 - ay) +
                (px(y1 * w + x0) * (1 - ax) + px(y1 * w + x1) * ax) * ay
        Return CSng(s / 65535.0F * y_range + y_off - 1.5F)
    End Function

    ''' <summary>RENDERED height of a cascade heightmap at a world position -
    ''' the same -uv REPEAT sampling and -1.5 sink the vertex shader applies.</summary>
    Public Function sample_outland_px(px As UShort(), w As Integer, h As Integer,
                                      wx As Single, wz As Single,
                                      scale As Vector2, y_range As Single, y_off As Single) As Single
        Dim gsize = CSng(MapTerrain.OUTLAND_GRID)
        Dim ghalf = gsize / 2.0F
        Dim gms = 100.0F / (gsize - 1.0F)
        Dim mx = (wx - theMap.center_offset.X) / scale.X
        Dim mz = (wz - theMap.center_offset.Y) / scale.Y
        Dim u = ((mx - 0.04888F) / gms + ghalf) / gsize
        Dim v = ((mz - 0.04888F) / gms + ghalf) / gsize
        Dim tu = Math.Clamp(1.0F - u, 0.0F, 1.0F)
        Dim tv = Math.Clamp(1.0F - v, 0.0F, 1.0F)
        Dim fx = tu * w - 0.5F
        Dim fy = tv * h - 0.5F
        Dim x0 = CInt(Math.Floor(fx)) : Dim y0 = CInt(Math.Floor(fy))
        Dim ax = fx - x0 : Dim ay = fy - y0
        Dim x1 = Math.Clamp(x0 + 1, 0, w - 1) : Dim y1 = Math.Clamp(y0 + 1, 0, h - 1)
        x0 = Math.Clamp(x0, 0, w - 1) : y0 = Math.Clamp(y0, 0, h - 1)
        Dim s = (px(y0 * w + x0) * (1 - ax) + px(y0 * w + x1) * ax) * (1 - ay) +
                (px(y1 * w + x0) * (1 - ax) + px(y1 * w + x1) * ax) * ay
        Return CSng(s / 65535.0F * y_range + y_off - 1.5F)
    End Function

    Public Function get_Y_at_XZ(ByVal Lx As Double, ByVal Lz As Double) As Single

        If Not MAP_LOADED Or Not map_scene.TERRAIN_LOADED Then
            Return 0
        End If
        If mapBoard Is Nothing Then Return 0.0F
        Dim tlx As Single = 100.0 / 65.0
        Dim tl, tr, br, bl, w As Vector3
        Dim xvp, yvp As Integer
        Dim ryp, rxp As Single

        'not sure why we need this offset
        Lx += 0.01
        Lz += 0.01

        For xo = 0 To MAP_BOARD_SIZE - 1
            For yo = 0 To MAP_BOARD_SIZE - 1
                If mapBoard(xo, yo).occupied Then

                    Dim px = mapBoard(xo, yo).location.X
                    If px - 50 < Lx AndAlso px + 50 >= Lx Then
                        xvp = xo
                        Dim pz = mapBoard(xo, yo).location.Y
                        If pz - 50 < Lz AndAlso pz + 50 >= Lz Then
                            yvp = yo
                            GoTo exit2
                        End If
                        GoTo exit1
                    End If
                End If
            Next
        Next
exit1:
        For xo = 0 To MAP_BOARD_SIZE - 1
            For yo = 0 To MAP_BOARD_SIZE - 1
                If mapBoard(xo, yo).occupied Then
                    Dim pz = mapBoard(xo, yo).location.Y
                    If pz - 50 < Lz AndAlso pz + 50 >= Lz Then
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

        HX = Floor(vxp)
        OX = 1
        HY = Floor(vyp)
        OY = 1
        If HEIGHTMAPSIZE < 64 Then
        End If
        Dim altitude As Single = 0.0

        If HX + OX > 65 Then
            Return 0
        End If
        tl.X = rxp
        tl.Y = ryp
        HX += 3
        HY += 2
        tl.Z = theMap.v_data(map).heightsTBL(HX, HY)

        tr.X = rxp + tlx
        tr.Y = ryp
        tr.Z = theMap.v_data(map).heightsTBL(HX + OX, HY)

        br.X = rxp + tlx
        br.Y = ryp + tlx
        br.Z = theMap.v_data(map).heightsTBL(HX + OX, HY + OY)

        bl.X = rxp
        bl.Y = ryp + tlx
        bl.Z = theMap.v_data(map).heightsTBL(HX, HY + OY)

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

        If p.Z = q.Z AndAlso q.Z = r.Z Then
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



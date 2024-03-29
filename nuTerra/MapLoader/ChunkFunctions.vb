﻿Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL

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

        Dim size = 256
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

    Public Sub build_outland_vao()
        map_scene.terrain.outland_vao = GLVertexArray.Create("outland_vao")

        map_scene.terrain.outland_vertices_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "outland_vertices")
        map_scene.terrain.outland_indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "outland_indices")

        Dim vcount = theMap.outland_Vdata.v_buff_XZ.Length
        Dim vsize = Marshal.SizeOf(Of OutlandVertex)

        map_scene.terrain.outland_indices_buffer.Storage(theMap.outland_Vdata.indicies_32.Length * 12, theMap.outland_Vdata.indicies_32, BufferStorageFlags.None)

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

        ' VERTEX XZ
        map_scene.terrain.outland_vao.VertexBuffer(0, map_scene.terrain.outland_vertices_buffer, IntPtr.Zero, vsize)
        map_scene.terrain.outland_vao.AttribFormat(0, 2, VertexAttribType.Float, False, 0)
        map_scene.terrain.outland_vao.AttribBinding(0, 0)
        map_scene.terrain.outland_vao.EnableAttrib(0)

        ' UV
        map_scene.terrain.outland_vao.VertexBuffer(1, map_scene.terrain.outland_vertices_buffer, New IntPtr(8), vsize)
        map_scene.terrain.outland_vao.AttribFormat(1, 2, VertexAttribType.Float, False, 0)
        map_scene.terrain.outland_vao.AttribBinding(1, 1)
        map_scene.terrain.outland_vao.EnableAttrib(1)

        ' NORMALS AND HOLES
        map_scene.terrain.outland_vao.VertexBuffer(2, map_scene.terrain.outland_vertices_buffer, New IntPtr(16), vsize)
        map_scene.terrain.outland_vao.AttribFormat(2, 4, VertexAttribType.Int2101010Rev, True, 0)
        map_scene.terrain.outland_vao.AttribBinding(2, 2)
        map_scene.terrain.outland_vao.EnableAttrib(2)

        ' Tangents
        map_scene.terrain.outland_vao.VertexBuffer(3, map_scene.terrain.outland_vertices_buffer, New IntPtr(20), vsize)
        map_scene.terrain.outland_vao.AttribFormat(3, 4, VertexAttribType.Int2101010Rev, True, 0)
        map_scene.terrain.outland_vao.AttribBinding(3, 3)
        map_scene.terrain.outland_vao.EnableAttrib(3)


        map_scene.terrain.outland_vao.ElementBuffer(map_scene.terrain.outland_indices_buffer)
    End Sub

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

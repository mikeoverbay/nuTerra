﻿Imports System.IO
Imports System.Math
Imports Hjg.Pngcs
Imports Ionic
Imports OpenTK
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

    Public Sub get_mesh(ByRef chunk As chunk_, ByRef v_data As terain_V_data_, ByRef r_set As chunk_render_data_)

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
        ReDim v_data.v_buff_XZ_morph(b_size)

        ReDim v_data.v_buff_Y(b_size)
        ReDim v_data.v_buff_Y_morph(b_size)

        ReDim v_data.h_buff(b_size)
        ReDim v_data.uv_buff(b_size)
        ReDim v_data.n_buff(b_size)
        ReDim v_data.n_buff_morph(b_size)
        ReDim v_data.t_buff(b_size)
        ReDim v_data.t_buff_morph(b_size)
        ReDim v_data.indicies(8191)

        Dim w As Double = 64 + 1  'bmp_w
        Dim h As Double = 64 + 1  'bmp_h
        Dim uvScale = (1.0# / 64.0#)
        Dim w_ = w / 2.0#
        Dim h_ = h / 2.0#
        Dim scale = 100.0 / (64.0#)
        Dim stride = 65
        Dim cnt As UInt32 = 0

        'we need this for creating normals!
        'If theMap.vertex_vBuffer_id = 0 Then
        For j = 0 To 63
            For i = 0 To 63
                v_data.indicies(cnt + 0).x = (i + 0) + ((j + 1) * stride) ' BL
                v_data.indicies(cnt + 0).y = (i + 1) + ((j + 0) * stride) ' TR
                v_data.indicies(cnt + 0).z = (i + 0) + ((j + 0) * stride) ' TL

                v_data.indicies(cnt + 1).x = (i + 0) + ((j + 1) * stride) ' BL
                v_data.indicies(cnt + 1).y = (i + 1) + ((j + 1) * stride) ' BR
                v_data.indicies(cnt + 1).z = (i + 1) + ((j + 0) * stride) ' TR
                cnt += 2
            Next
        Next
        'End If

        cnt = 0

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

                topleft.vert.X += 0.793F
                topleft.vert.Y += 0.793F

                'this offsets the terrain geo to align textures with models.
                bottomleft.vert.X += 0.793F
                bottomleft.vert.Y += 0.793F

                ' Fill the arrays
                v_data.v_buff_XZ(i + ((j + 1) * stride)) = bottomleft.vert
                v_data.v_buff_XZ(i + ((j + 0) * stride)) = topleft.vert

                v_data.v_buff_Y(i + ((j + 1) * stride)) = bottomleft.H
                v_data.v_buff_Y(i + ((j + 0) * stride)) = topleft.H

                v_data.h_buff(i + ((j + 1) * stride)) = bottomleft.hole
                v_data.h_buff(i + ((j + 0) * stride)) = topleft.hole

                v_data.uv_buff(i + ((j + 1) * stride)) = bottomleft.uv
                v_data.uv_buff(i + ((j + 0) * stride)) = topleft.uv

                ' Fill the morph arrays. We duplicate the vaules in 2 locations.
                v_data.v_buff_XZ_morph(i + ((j + 1) * stride)) = bottomleft.vert
                v_data.v_buff_XZ_morph(i + ((j + 0) * stride)) = topleft.vert

                v_data.v_buff_Y_morph(i + ((j + 1) * stride)) = bottomleft.H
                v_data.v_buff_Y_morph(i + ((j + 0) * stride)) = topleft.H

            Next
        Next

        '=========================================================================
        'From : https://www.iquilezles.org/www/articles/normals/normals.htm
        'Create smoothed normals using IQ's method
        make_normals_tangents(v_data.indicies, v_data.v_buff_XZ, v_data.v_buff_Y, v_data.n_buff, v_data.t_buff, v_data.n_buff_morph, v_data.t_buff_morph, v_data.uv_buff)
        '=========================================================================


    End Sub

    Public Sub douplicate_1st_to_2nd_sng(ByRef buff() As Single)

        For x = 0 To 65 * 64 Step 65
            For y = 1 To 64 Step 2
                buff(y + x) = buff(y + x + 1)
            Next
        Next
        For x = 0 To 64
            For y = 1 To 64 Step 2
                buff(x + (y * 65)) = buff(x + (y * 65 + 65))
            Next
        Next


    End Sub
    Public Sub douplicate_1st_to_2nd_vec2(ByRef buff() As Vector2)

        For x = 0 To 65 * 64 Step 65
            For y = 1 To 64 Step 2
                buff(y + x) = buff(y + x + 1)
            Next
        Next
        For x = 0 To 64
            For y = 1 To 64 Step 2
                buff(x + (y * 65)) = buff(x + (y * 65 + 65))
            Next
        Next

    End Sub
    Public Sub douplicate_1st_to_2nd_vec3(ByRef buff() As Vector3)

        For x = 0 To 65 * 64 Step 65
            For y = 1 To 64 Step 2
                buff(y + x) = buff(y + x + 1)
            Next
        Next
        For x = 0 To 64
            For y = 1 To 64 Step 2
                buff(x + (y * 65)) = buff(x + (y * 65 + 65))
            Next
        Next
    End Sub

    Private Sub make_normals_tangents(ByRef indi() As vect3_16, ByRef XY() As Vector2, ByRef Z() As Single,
                                      ByRef n_buff_morph() As Vector3, ByRef t_buff_morph() As Vector3,
                                      ByRef n_buff() As Vector3, ByRef t_buff() As Vector3,
                                      ByRef UV() As Vector2)
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
            n_buff_morph(ia) = n_buff(ia)
            n_buff_morph(ib) = n_buff(ib)
            n_buff_morph(ic) = n_buff(ic)
        Next
        For i = 0 To indi.Length - 1
            Dim v0, V1, v2 As Vector3

            Dim ia As UInt16 = indi(i).z
            Dim ib As UInt16 = indi(i).y
            Dim ic As UInt16 = indi(i).x

            v0.Xz = XY(ia) : v0.Y = Z(ia)
            V1.Xz = XY(ib) : V1.Y = Z(ib)
            v2.Xz = XY(ic) : v2.Y = Z(ic)
            'v0 += New Vector3(50.0, 0.0, 50.0)
            'V1 += New Vector3(50.0, 0.0, 50.0)
            'v2 += New Vector3(50.0, 0.0, 50.0)
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

            t_buff(ia) += tangent
            t_buff(ib) += tangent
            t_buff(ic) += tangent

            t_buff_morph(ia) += t_buff(ia)
            t_buff_morph(ib) += t_buff(ib)
            t_buff_morph(ic) += t_buff(ic)

        Next
        Return
        'not needed?
        For i = 0 To t_buff.Length - 1
            t_buff(i).Normalize()
            n_buff(i).Normalize()
            t_buff_morph(i).Normalize()
            n_buff_morph(i).Normalize()
        Next
    End Sub

    Public Sub smooth_seams(ByVal Idx As Integer)

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
                v1 = theMap.v_data(tr).n_buff(you_tr)
                v2 = theMap.v_data(tl).n_buff(you_tl)
                v3 = theMap.v_data(br).n_buff(you_br)
                v4 = .n_buff(me_) '<-- me
                v1 = (v1 + v2 + v3 + v4) / 4.0F
                theMap.v_data(tr).n_buff(you_tr) = v1
                theMap.v_data(tl).n_buff(you_tl) = v1
                theMap.v_data(br).n_buff(you_br) = v1
                .n_buff(me_) = v1

                theMap.v_data(tr).n_buff_morph(you_tr) = v1
                theMap.v_data(tl).n_buff_morph(you_tl) = v1
                theMap.v_data(br).n_buff_morph(you_br) = v1
                .n_buff_morph(me_) = v1
                '====================================
                v1 = theMap.v_data(tr).t_buff(you_tr)
                v2 = theMap.v_data(tl).t_buff(you_tl)
                v3 = theMap.v_data(br).t_buff(you_br)
                v4 = .t_buff(me_) '<-- me
                v1 = (v1 + v2 + v3 + v4) / 4.0F
                theMap.v_data(tr).t_buff(you_tr) = v1
                theMap.v_data(tl).t_buff(you_tl) = v1
                theMap.v_data(br).t_buff(you_br) = v1
                .t_buff(me_) = v1

                theMap.v_data(tr).t_buff_morph(you_tr) = v1
                theMap.v_data(tl).t_buff_morph(you_tl) = v1
                theMap.v_data(br).t_buff_morph(you_br) = v1
                .t_buff_morph(me_) = v1

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

                    .n_buff_morph(me_) = v1
                    theMap.v_data(other).n_buff_morph(you_) = v1
                    '====================================

                    v1 = .t_buff(me_) '<-- me
                    v2 = theMap.v_data(other).t_buff(you_)
                    v1 = (v1 + v2) / 2.0
                    .t_buff(me_) = v1
                    theMap.v_data(other).t_buff(you_) = v1

                    .t_buff_morph(me_) = v1
                    theMap.v_data(other).t_buff_morph(you_) = v1

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

                    .n_buff_morph(me_) = v1
                    theMap.v_data(other).n_buff_morph(you_) = v1
                    '====================================
                    v1 = .t_buff(me_) '<-- me
                    v2 = theMap.v_data(other).t_buff(you_)
                    v1 = (v1 + v2) / 2.0F
                    .t_buff(me_) = v1
                    theMap.v_data(other).t_buff(you_) = v1

                    .t_buff_morph(me_) = v1
                    theMap.v_data(other).t_buff_morph(you_) = v1

                Next
            End If

        End With


    End Sub

    Private Sub convert_low_z_sng(ByRef inBuff() As Single, ByRef OutBuff() As Single)
        Dim c, r As Integer
        For y = 0 To 65 * 63 Step 65 * 2
            r = 0
            For x = 0 To 64 Step 2
                OutBuff(r + c) = inBuff(x + y)
                r += 1
            Next
            OutBuff(r + c + 1) = inBuff(64 + y)
            c += 33
        Next
        r = 1056
        For i = 4095 To 4095 + 64 Step 2
            OutBuff(r) = inBuff(i)
            r += 1
        Next
    End Sub
    Private Sub convert_low_z_vec2(ByRef inBuff() As Vector2, ByRef OutBuff() As Vector2)
        Dim c, r As Integer
        For y = 0 To 65 * 63 Step 65 * 2
            r = 0
            For x = 0 To 64 Step 2
                OutBuff(r + c) = inBuff(x + y)
                r += 1
            Next
            OutBuff(r + c + 1) = inBuff(64 + y)
            c += 33
        Next
        r = 1056
        For i = 4095 To 4095 + 64 Step 2
            OutBuff(r) = inBuff(i)
            r += 1
        Next
    End Sub
    Private Sub create_LQ_indies()
        Dim cnt As Integer = 0
        Dim stride As Integer = 33
        For j = 0 To 31
            For i = 0 To 31
                indicies(cnt + 0).x = (i + 0) + ((j + 1) * stride) ' BL
                indicies(cnt + 0).y = (i + 1) + ((j + 0) * stride) ' TR
                indicies(cnt + 0).z = (i + 0) + ((j + 0) * stride) ' TL

                indicies(cnt + 1).x = (i + 0) + ((j + 1) * stride) ' BL
                indicies(cnt + 1).y = (i + 1) + ((j + 1) * stride) ' BR
                indicies(cnt + 1).z = (i + 1) + ((j + 0) * stride) ' TR
                cnt += 2
            Next
        Next
        ReDim Preserve indicies(cnt - 1)

    End Sub

    Private Sub convert_low_z_vec3(ByRef inBuff() As Vector3, ByRef OutBuff() As Vector3)
        Dim c, r As Integer
        For y = 0 To 65 * 63 Step 65 * 2
            r = 0
            For x = 0 To 64 Step 2
                OutBuff(r + c) = inBuff(x + y)
                r += 1
            Next
            OutBuff(r + c + 1) = inBuff(64 + y)
            c += 33
        Next
        r = 1056
        For i = 4095 To 4095 + 64 Step 2
            OutBuff(r) = inBuff(i)
            r += 1
        Next
    End Sub

    Dim quater_size As Integer = (33 * 33) - 1
    Dim indicies(2178) As vect3_16
    Dim uv_buff(quater_size) As Vector2
    Dim v_buff_XZ(quater_size) As Vector2

    Public Sub build_Terrain_LQ_VAO(ByVal i As Integer)
        ' ===== LW VAO Creator =====

        ' SETUP ==================================================================

        Dim v_buff_Y(quater_size) As Single
        Dim normal(quater_size) As Vector3
        Dim tangent(quater_size) As Vector3
        Dim indie_count As Integer
        If i = 0 Then 'only need to create these once!!!
            create_LQ_indies()
            convert_low_z_vec2(theMap.v_data(i).v_buff_XZ_morph, v_buff_XZ)
        End If

        convert_low_z_sng(theMap.v_data(i).v_buff_Y_morph, v_buff_Y)
        convert_low_z_vec3(theMap.v_data(i).n_buff_morph, normal)
        convert_low_z_vec3(theMap.v_data(i).t_buff_morph, tangent)

        '=========================================================================

        With theMap.v_data(i)

            'Gen VAO and VBO Ids
            GL.CreateVertexArrays(1, theMap.render_set(i).LQ_VAO)
            ReDim theMap.render_set(i).LQ_mBuffers(3)
            GL.CreateBuffers(3, theMap.render_set(i).LQ_mBuffers)

            ' If the shared buffer is not defined, we need to do so.
            If theMap.LQ_vertex_vBuffer_id = 0 Then
                GL.CreateBuffers(1, theMap.LQ_vertex_vBuffer_id)
                GL.CreateBuffers(1, theMap.LQ_vertex_iBuffer_id)

                'if the shared buffer is not defined, we need to fill the buffer now
                GL.NamedBufferStorage(theMap.LQ_vertex_iBuffer_id, indicies.Length * 6, indicies, BufferStorageFlags.None)
                GL.NamedBufferStorage(theMap.LQ_vertex_vBuffer_id, v_buff_XZ.Length * 8, v_buff_XZ, BufferStorageFlags.None)
            End If

            ' VERTEX XZ ==================================================================
            GL.VertexArrayVertexBuffer(theMap.render_set(i).LQ_VAO, 0, theMap.LQ_vertex_vBuffer_id, IntPtr.Zero, 8)
            GL.VertexArrayAttribFormat(theMap.render_set(i).LQ_VAO, 0, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).LQ_VAO, 0, 0)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).LQ_VAO, 0)

            ' POSITION Y ==================================================================
            GL.NamedBufferStorage(theMap.render_set(i).LQ_mBuffers(0), v_buff_Y.Length * 4, v_buff_Y, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).LQ_VAO, 1, theMap.render_set(i).LQ_mBuffers(0), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).LQ_VAO, 1, 1, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).LQ_VAO, 1, 1)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).LQ_VAO, 1)


            ' NORMALS AND HOLES ======================================================== 
            Dim packed(normal.Length - 1) As UInteger
            For j = 0 To normal.Length - 1
                packed(j) = pack_2_10_10_10(normal(j), 0)
            Next
            GL.NamedBufferStorage(theMap.render_set(i).LQ_mBuffers(1), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).LQ_VAO, 3, theMap.render_set(i).LQ_mBuffers(1), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).LQ_VAO, 3, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).LQ_VAO, 3, 3)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).LQ_VAO, 3)

            ' Tangents ========================================================
            For j = 0 To tangent.Length - 1
                packed(j) = pack_2_10_10_10(tangent(j), 0.0)
            Next
            GL.NamedBufferStorage(theMap.render_set(i).LQ_mBuffers(2), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).LQ_VAO, 4, theMap.render_set(i).LQ_mBuffers(2), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).LQ_VAO, 4, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).LQ_VAO, 4, 4)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).LQ_VAO, 4)

            ' INDICES ==================================================================
            GL.VertexArrayElementBuffer(theMap.render_set(i).LQ_VAO, theMap.LQ_vertex_iBuffer_id)



            .indicies = Nothing
            .v_buff_XZ_morph = Nothing
            .v_buff_Y_morph = Nothing
            .n_buff_morph = Nothing
            .t_buff_morph = Nothing
            .uv_buff = Nothing

        End With
    End Sub
    Public Sub build_Terrain_VAO(ByVal i As Integer)
        ' SETUP ==================================================================
        With theMap.v_data(i)

            'Gen VAO and VBO Ids
            GL.CreateVertexArrays(1, theMap.render_set(i).VAO)
            ReDim theMap.render_set(i).mBuffers(5)
            GL.CreateBuffers(6, theMap.render_set(i).mBuffers)

            ' If the shared buffer is not defined, we need to do so.
            If theMap.vertex_vBuffer_id = 0 Then
                GL.CreateBuffers(1, theMap.vertex_vBuffer_id)
                GL.CreateBuffers(1, theMap.vertex_vBuffer_morph_id)
                GL.CreateBuffers(1, theMap.vertex_iBuffer_id)

                'if the shared buffer is not defined, we need to fill the buffer now
                GL.NamedBufferStorage(theMap.vertex_iBuffer_id, .indicies.Length * 6, .indicies, BufferStorageFlags.None)
                GL.NamedBufferStorage(theMap.vertex_vBuffer_id, .v_buff_XZ.Length * 8, .v_buff_XZ, BufferStorageFlags.None)
                GL.NamedBufferStorage(theMap.vertex_vBuffer_morph_id, .v_buff_XZ_morph.Length * 8, .v_buff_XZ_morph, BufferStorageFlags.None)
            End If

            ' VERTEX XZ ==================================================================
            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 0, theMap.vertex_vBuffer_id, IntPtr.Zero, 8)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 0, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 0, 0)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 0)

            ' POSITION Y ==================================================================
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(0), .v_buff_Y.Length * 4, .v_buff_Y, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 1, theMap.render_set(i).mBuffers(0), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 1, 1, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 1, 1)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 1)

            ' NORMALS AND HOLES ======================================================== 
            Dim packed(.n_buff.Length - 1) As UInteger
            For j = 0 To .n_buff.Length - 1
                packed(j) = pack_2_10_10_10(.n_buff(j), .h_buff(j))
            Next
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(1), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 2, theMap.render_set(i).mBuffers(1), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 2, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 2, 2)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 2)

            ' Tangents ========================================================
            For j = 0 To .n_buff.Length - 1
                packed(j) = pack_2_10_10_10(.t_buff(j), 0.0)
            Next
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(2), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 3, theMap.render_set(i).mBuffers(2), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 3, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 3, 3)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 3)

            ' VERTEX XZ Morph ============================================================
            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 4, theMap.vertex_vBuffer_morph_id, IntPtr.Zero, 8)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 4, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 4, 4)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 4)

            ' POSITION Y Morph =============================================================
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(3), .v_buff_Y_morph.Length * 4, .v_buff_Y_morph, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 5, theMap.render_set(i).mBuffers(3), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 5, 1, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 5, 5)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 5)

            ' normals morph ========================================================
            For j = 0 To .n_buff.Length - 1
                packed(j) = pack_2_10_10_10(.n_buff_morph(j), 0.0)
            Next
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(4), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 6, theMap.render_set(i).mBuffers(4), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 6, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 6, 6)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 6)

            ' Tangents morphs =================================================
            For j = 0 To .n_buff.Length - 1
                packed(j) = pack_2_10_10_10(.t_buff_morph(j), 0.0)
            Next
            GL.NamedBufferStorage(theMap.render_set(i).mBuffers(5), packed.Length * 4, packed, BufferStorageFlags.None)

            GL.VertexArrayVertexBuffer(theMap.render_set(i).VAO, 7, theMap.render_set(i).mBuffers(5), IntPtr.Zero, 4)
            GL.VertexArrayAttribFormat(theMap.render_set(i).VAO, 7, 4, VertexAttribType.Int2101010Rev, True, 0)
            GL.VertexArrayAttribBinding(theMap.render_set(i).VAO, 7, 7)
            GL.EnableVertexArrayAttrib(theMap.render_set(i).VAO, 7)

            ' INDICES ==================================================================
            GL.VertexArrayElementBuffer(theMap.render_set(i).VAO, theMap.vertex_iBuffer_id)

            .v_buff_XZ = Nothing
            .v_buff_Y = Nothing
            .n_buff = Nothing
            .h_buff = Nothing
            .t_buff = Nothing

        End With
    End Sub

    Public Sub get_holes(ByRef c As chunk_, ByRef v As terain_V_data_)

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
        Dim data(h_width * h_height * 4) As Byte
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
            xx = 0 : yy = 0
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


        'need to find a use for this :)
        Dim avg, y_max, y_min As Single
        y_min = 1000.0F
        For j As UInt32 = 1 To mapsize - 1
            For i As UInt32 = 1 To mapsize - 1
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
        v.avg_heights = (y_max + y_min) / 2.0F
        br.Close()
        ms.Close()
        ms.Dispose()
        'End If
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
        c.mBoard_x = x + 10
        c.mBoard_y = y + 10

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

        'not sure why we need this offset
        Lx += 0.01
        Lz += 0.01

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

Imports System.Linq
Imports System.Threading.Tasks
Imports OpenTK.Mathematics

''' <summary>
''' Background threshold decimation of the outland ring meshes.
'''
''' Prototyped offline first (the previous in-engine attempt froze the load
''' and was reverted whole): per cull block, greedy edge collapse driven by an
''' error THRESHOLD - collapse any edge whose area-normalized quadric error at
''' the surviving vertex stays under eps^2 - with subset placement (a collapse
''' only ever picks one of the two original endpoints, never a new position).
''' That means surviving vertices are always original grid vertices, so the
''' result is ONLY a new index buffer over the existing shared vertex buffer;
''' the VS keeps sampling heights exactly as before. Offline numbers on
''' prohorovka's patched heightmap: 91-94% triangle reduction at eps 0.25 with
''' max vertical error under 0.9 m.
'''
''' Freeze rules keep every invariant the rest of the renderer relies on:
'''  - cull-block border vertices never move, so per-block index ranges and
'''    the block frustum cull survive, and blocks stay crack-free against
'''    their neighbours;
'''  - vertices in or near the terrain footprint (the weld band) never move,
'''    so the sheet-under-terrain guarantee from patch_outland_heightmap and
'''    its crossing audit is untouched.
'''
''' Runs as a Task kicked at the end of patch_outland_heightmap; Draw_outland
''' picks the finished result up on the GL thread and swaps buffers. The full
''' grid draws until then - the load path never blocks.
''' </summary>
Module OutlandDecimator

    ''' <summary>One cascade's finished result, waiting for the GL thread.</summary>
    Public Class DecimatedCascade
        Public tris() As vect3_32
        Public blocks() As MapTerrain.OutlandBlock
        Public source_tris As Integer
        Public ms As Long
        Public gen As Integer
    End Class

    Public pending_lock As New Object
    Public pending_near As DecimatedCascade
    Public pending_far As DecimatedCascade
    ' Bumped on every kick (one per map load). A task that finishes after the
    ' owner loaded a different map carries a stale gen and its result is
    ' dropped instead of being swapped onto the wrong map's buffers.
    Public current_gen As Integer

    ''' <summary>
    ''' Kick the background decimation for both cascades. px/px2 are the
    ''' PATCHED height grids (the weld must already have run - the collapse
    ''' error is measured on what actually renders).
    ''' </summary>
    Public Sub kick_outland_decimation(px() As UShort, w As Integer, h As Integer,
                                       px2() As UShort, w2 As Integer, h2 As Integer)
        If Not OUTLAND_DECIMATE Then Return

        ' snapshot everything the task needs - theMap fields can be touched by
        ' the UI thread while we run
        Dim near_scale = theMap.near_scale
        Dim far_scale = theMap.far_scale
        Dim center = theMap.center_offset
        Dim fmin = theMap.terrain_footprint_min
        Dim fmax = theMap.terrain_footprint_max
        Dim near_yr = theMap.near_y_height
        Dim near_yo = theMap.near_y_offset
        Dim far_yr = theMap.far_y_height
        Dim far_yo = theMap.far_y_offset
        Dim has_far = map_scene.terrain.CASCADE_LEVELS = 2 AndAlso px2 IsNot Nothing
        current_gen += 1
        Dim g = current_gen

        Task.Run(Sub()
                     Try
                         Dim near_result = decimate_cascade(px, w, h, near_scale, center,
                                                            fmin, fmax, near_yr, near_yo,
                                                            OUTLAND_DECIMATE_EPS)
                         near_result.gen = g
                         SyncLock pending_lock
                             pending_near = near_result
                         End SyncLock

                         If has_far Then
                             Dim near_half As New Vector2(near_scale.X * 50.0F, near_scale.Y * 50.0F)
                             ' the far cascade's texels are coarser; a looser
                             ' eps there is still far below a pixel
                             Dim far_result = decimate_cascade(px2, w2, h2, far_scale, center,
                                                               center - near_half, center + near_half,
                                                               far_yr, far_yo,
                                                               OUTLAND_DECIMATE_EPS * 2.0F)
                             far_result.gen = g
                             SyncLock pending_lock
                                 pending_far = far_result
                             End SyncLock
                         End If
                     Catch ex As Exception
                         LogThis("outland decimation FAILED (full grid stays): {0}", ex.Message)
                     End Try
                 End Sub)
    End Sub

    ''' <summary>
    ''' Decimate one cascade: regenerate the same ring triangles and blocks the
    ''' builder made, then collapse each block independently in parallel.
    ''' </summary>
    Private Function decimate_cascade(px() As UShort, w As Integer, h As Integer,
                                      scale As Vector2, center As Vector2,
                                      hole_min As Vector2, hole_max As Vector2,
                                      y_range As Single, y_off As Single,
                                      eps As Single) As DecimatedCascade
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim gsize = MapTerrain.OUTLAND_GRID
        Dim blocks() As MapTerrain.OutlandBlock = Nothing
        Dim tris = build_outland_ring_indices(scale, center, hole_min, hole_max, blocks)

        ' vertex world positions, heights sampled the way the VS samples them
        Dim half = gsize \ 2
        Dim ms_ = 100.0F / (gsize - 1)
        Dim pos(gsize * gsize - 1) As Vector3
        Dim have(gsize * gsize - 1) As Boolean
        For Each t In tris
            have(CInt(t.x)) = True : have(CInt(t.y)) = True : have(CInt(t.z)) = True
        Next
        For j = 0 To gsize - 1
            For i = 0 To gsize - 1
                Dim vid = j * gsize + i
                If Not have(vid) Then Continue For
                Dim wx = ((i - half) * ms_ + 0.04888F) * scale.X + center.X
                Dim wz = ((j - half) * ms_ + 0.04888F) * scale.Y + center.Y
                pos(vid) = New Vector3(wx, sample_outland_px(px, w, h, wx, wz, scale, y_range, y_off), wz)
            Next
        Next

        ' freeze: cull-block borders (multiples of OUTLAND_CULL_BLOCK), the
        ' grid edge, and everything in/near the hole rect (the weld band) so
        ' the under-terrain tuck survives verbatim
        Dim bq = MapTerrain.OUTLAND_CULL_BLOCK
        Dim margin = 2.0F * ms_ * Math.Max(Math.Abs(scale.X), Math.Abs(scale.Y))
        Dim out_tris As New List(Of vect3_32)(tris.Length)
        Dim out_blocks As New List(Of MapTerrain.OutlandBlock)(blocks.Length)
        Dim per_block(blocks.Length - 1) As List(Of vect3_32)

        Parallel.For(0, blocks.Length,
            Sub(bi)
                Dim blk = blocks(bi)
                Dim first_tri = CInt(blk.first_index \ 3)
                Dim tri_count = CInt(blk.index_count \ 3)
                Dim local_tris(tri_count - 1) As vect3_32
                Array.Copy(tris, first_tri, local_tris, 0, tri_count)
                per_block(bi) = collapse_block(local_tris, pos, gsize, bq,
                                               hole_min, hole_max, margin, eps)
            End Sub)

        For bi = 0 To blocks.Length - 1
            Dim lt = per_block(bi)
            If lt.Count = 0 Then Continue For
            Dim nb = blocks(bi)
            nb.first_index = CUInt(out_tris.Count * 3)
            nb.index_count = CUInt(lt.Count * 3)
            out_blocks.Add(nb)
            out_tris.AddRange(lt)
        Next

        Return New DecimatedCascade With {
            .tris = out_tris.ToArray(),
            .blocks = out_blocks.ToArray(),
            .source_tris = tris.Length,
            .ms = sw.ElapsedMilliseconds}
    End Function

    ''' <summary>
    ''' Threshold collapse of one block's triangles. Guards: frozen vertices
    ''' (block border by grid index, weld band by world position) are never
    ''' removed; a collapse that flips any surviving face's normal is refused.
    ''' </summary>
    Private Function collapse_block(tris() As vect3_32, pos() As Vector3,
                                    gsize As Integer, bq As Integer,
                                    hole_min As Vector2, hole_max As Vector2,
                                    band_margin As Single, eps As Single) As List(Of vect3_32)
        ' local vertex table
        Dim map As New Dictionary(Of UInteger, Integer)
        Dim verts As New List(Of UInteger)
        Dim faces As New List(Of Integer())       ' local ids; Nothing = dead
        For Each t In tris
            Dim f(2) As Integer
            Dim src = {t.x, t.y, t.z}
            For k = 0 To 2
                Dim gid = src(k)
                Dim li As Integer
                If Not map.TryGetValue(gid, li) Then
                    li = verts.Count
                    map(gid) = li
                    verts.Add(gid)
                End If
                f(k) = li
            Next
            faces.Add(f)
        Next
        Dim nv = verts.Count

        Dim frozen(nv - 1) As Boolean
        Dim P(nv - 1) As Vector3
        For li = 0 To nv - 1
            Dim gid = CInt(verts(li))
            Dim gi = gid Mod gsize
            Dim gj = gid \ gsize
            P(li) = pos(gid)
            ' block border / grid edge
            If gi Mod bq = 0 OrElse gj Mod bq = 0 OrElse gi = gsize - 1 OrElse gj = gsize - 1 Then
                frozen(li) = True
            End If
            ' weld band: anything in or hugging the terrain footprint
            If P(li).X >= hole_min.X - band_margin AndAlso P(li).X <= hole_max.X + band_margin AndAlso
               P(li).Z >= hole_min.Y - band_margin AndAlso P(li).Z <= hole_max.Y + band_margin Then
                frozen(li) = True
            End If
        Next

        ' quadrics as symmetric 4x4 (10 doubles), plus accumulated area
        Dim Q(nv - 1)() As Double
        Dim A(nv - 1) As Double
        For li = 0 To nv - 1
            Q(li) = New Double(9) {}
        Next
        Dim vf(nv - 1) As List(Of Integer)
        For li = 0 To nv - 1
            vf(li) = New List(Of Integer)
        Next
        For fi = 0 To faces.Count - 1
            Dim f = faces(fi)
            vf(f(0)).Add(fi) : vf(f(1)).Add(fi) : vf(f(2)).Add(fi)
            Dim n = Vector3.Cross(P(f(1)) - P(f(0)), P(f(2)) - P(f(0)))
            Dim a2 = n.Length
            If a2 < 0.000000001F Then Continue For
            n /= a2
            Dim area = 0.5 * a2
            Dim d = -Vector3.Dot(n, P(f(0)))
            add_quadric(Q(f(0)), n, d, area) : A(f(0)) += area
            add_quadric(Q(f(1)), n, d, area) : A(f(1)) += area
            add_quadric(Q(f(2)), n, d, area) : A(f(2)) += area
        Next

        Dim eps2 = CDbl(eps) * eps
        Dim alive(nv - 1) As Boolean
        For li = 0 To nv - 1
            alive(li) = True
        Next

        For pass = 1 To 30
            Dim touched(nv - 1) As Boolean
            Dim collapsed = 0
            ' walk current edges via faces
            For fi = 0 To faces.Count - 1
                Dim f = faces(fi)
                If f Is Nothing Then Continue For
                For e = 0 To 2
                    Dim va = f(e)
                    Dim vb = f((e + 1) Mod 3)
                    If touched(va) OrElse touched(vb) Then Continue For
                    If frozen(va) AndAlso frozen(vb) Then Continue For

                    ' subset placement: survivor must be an existing vertex,
                    ' and a frozen endpoint must be the survivor
                    Dim keep = va, kill = vb
                    Dim best_err = Double.MaxValue
                    If Not frozen(vb) Then
                        Dim err = eval_quadric(Q(va), Q(vb), A(va) + A(vb), P(va))
                        If err < best_err Then best_err = err : keep = va : kill = vb
                    End If
                    If Not frozen(va) Then
                        Dim err = eval_quadric(Q(va), Q(vb), A(va) + A(vb), P(vb))
                        If err < best_err Then best_err = err : keep = vb : kill = va
                    End If
                    If best_err > eps2 Then Continue For

                    ' flip guard over the killed vertex's surviving fan
                    Dim ok = True
                    For Each ofi In vf(kill)
                        Dim g = faces(ofi)
                        If g Is Nothing OrElse g.Contains(keep) Then Continue For
                        Dim q0 = P(g(0)) : Dim q1 = P(g(1)) : Dim q2 = P(g(2))
                        Dim n_old = Vector3.Cross(q1 - q0, q2 - q0)
                        Dim r0 = If(g(0) = kill, P(keep), q0)
                        Dim r1 = If(g(1) = kill, P(keep), q1)
                        Dim r2 = If(g(2) = kill, P(keep), q2)
                        Dim n_new = Vector3.Cross(r1 - r0, r2 - r0)
                        Dim lo = n_old.Length : Dim ln = n_new.Length
                        If ln < 0.0000000001F OrElse
                           (lo > 0.0000000001F AndAlso Vector3.Dot(n_old, n_new) / (lo * ln) < 0.2F) Then
                            ok = False
                            Exit For
                        End If
                    Next
                    If Not ok Then Continue For

                    ' collapse kill -> keep
                    For Each ofi In vf(kill).ToArray()
                        Dim g = faces(ofi)
                        If g Is Nothing Then Continue For
                        If g.Contains(keep) Then
                            faces(ofi) = Nothing   ' degenerate, dies
                        Else
                            For k = 0 To 2
                                If g(k) = kill Then g(k) = keep
                            Next
                            vf(keep).Add(ofi)
                        End If
                    Next
                    vf(kill).Clear()
                    For k = 0 To 9
                        Q(keep)(k) += Q(kill)(k)
                    Next
                    A(keep) += A(kill)
                    alive(kill) = False
                    touched(keep) = True
                    touched(kill) = True
                    collapsed += 1
                    Exit For   ' this face changed; move to the next face
                Next
            Next
            If collapsed = 0 Then Exit For
        Next

        Dim out_list As New List(Of vect3_32)
        For Each f In faces
            If f Is Nothing Then Continue For
            out_list.Add(New vect3_32 With {
                .x = verts(f(0)), .y = verts(f(1)), .z = verts(f(2))})
        Next
        Return out_list
    End Function

    ' symmetric 4x4 quadric, packed [xx xy xz xw yy yz yw zz zw ww]
    Private Sub add_quadric(q() As Double, n As Vector3, d As Double, weight As Double)
        Dim px = CDbl(n.X) : Dim py = CDbl(n.Y) : Dim pz = CDbl(n.Z)
        q(0) += weight * px * px : q(1) += weight * px * py : q(2) += weight * px * pz : q(3) += weight * px * d
        q(4) += weight * py * py : q(5) += weight * py * pz : q(6) += weight * py * d
        q(7) += weight * pz * pz : q(8) += weight * pz * d
        q(9) += weight * d * d
    End Sub

    Private Function eval_quadric(qa() As Double, qb() As Double, area As Double, p As Vector3) As Double
        Dim x = CDbl(p.X) : Dim y = CDbl(p.Y) : Dim z = CDbl(p.Z)
        Dim s = (qa(0) + qb(0)) * x * x + 2 * (qa(1) + qb(1)) * x * y + 2 * (qa(2) + qb(2)) * x * z + 2 * (qa(3) + qb(3)) * x +
                (qa(4) + qb(4)) * y * y + 2 * (qa(5) + qb(5)) * y * z + 2 * (qa(6) + qb(6)) * y +
                (qa(7) + qb(7)) * z * z + 2 * (qa(8) + qb(8)) * z +
                (qa(9) + qb(9))
        Return s / Math.Max(area, 0.000000001)
    End Function
End Module

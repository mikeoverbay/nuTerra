Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' The buildings: one mesh per model, drawn once per placement, shaded by
''' what the model IS.
'''
''' Added 2026-09-16 by nuTerra work, stage 3 of docs/brain_testing_plan.md.
''' </summary>
Module BrainModels

    ''' <summary>One model's geometry on the GPU, plus what it is.</summary>
    Private Structure Mesh
        Public vao As Integer
        Public vbo As Integer
        Public ibo As Integer
        Public indexCount As Integer
        Public kind As Byte
        ''' <summary>Local-space bounds, for the view-space clip.</summary>
        Public bbMin As Vector3
        Public bbMax As Vector3
        Public ok As Boolean
    End Structure

    Private meshes() As Mesh
    Private shader As BrainShader

    ''' <summary>Instances that survived the clip last frame, for the HUD and
    ''' for proving the cull actually culls.</summary>
    Public Drawn As Integer = 0
    Public Total As Integer = 0

    Public Sub Init()
        shader = New BrainShader("model")
    End Sub

    ''' <summary>
    ''' Build a mesh for every model the space declares.
    '''
    ''' LOD 0 ONLY. nuTerra keeps the whole LOD chain and picks by distance;
    ''' this app has no distance shading to speak of, and a harness that
    ''' silently swapped a building for its low-poly twin would make a
    ''' collision question depend on where the camera was standing.
    ''' </summary>
    Public Sub Build()
        Total = 0
        If MAP_MODELS Is Nothing OrElse MAP_MODELS.Length = 0 Then Return
        ReDim meshes(MAP_MODELS.Length - 1)

        Dim sw = Stopwatch.StartNew()
        Dim built = 0, skipped = 0, verts = 0L, tris = 0L
        Dim tally(7) As Integer

        For i = 0 To MAP_MODELS.Length - 1
            Dim lods = MAP_MODELS(i).modelLods
            If lods Is Nothing OrElse lods.Length = 0 Then skipped += 1 : Continue For
            Dim lod = lods(0)
            If lod Is Nothing OrElse lod.junk Then skipped += 1 : Continue For

            ' The geometry is not in memory until this is asked for.
            If Not get_primitive(lod) Then skipped += 1 : Continue For
            If lod.render_sets Is Nothing OrElse lod.render_sets.Count = 0 Then
                skipped += 1 : Continue For
            End If

            Dim m = build_one(lod)
            If Not m.ok Then skipped += 1 : Continue For

            ' The kind comes from the asset PATH, through the same classifier
            ' the flight bake uses - one table, one answer. See ModelKind.
            '
            ' render_sets(0).verts_name, NOT primitive_name, and that is the
            ' bake's own choice at MapLoader.vb:259. primitive_name is a
            ' section name; verts_name carries the content/... path the
            ' classifier's keywords are written against. Reading the wrong one
            ' classified all 376 models as "other" - which LOOKS like a working
            ' classifier finding nothing it knows.
            Dim asset = ""
            Try
                asset = If(lod.render_sets(0).verts_name, "")
            Catch
            End Try
            m.kind = ModelKind.classify(asset.Replace("\", "/").ToLowerInvariant())
            meshes(i) = m
            tally(m.kind And 7) += 1
            built += 1
            verts += m.indexCount
            tris += m.indexCount \ 3
        Next

        Dim parts As New List(Of String)
        For k = 0 To 7
            If tally(k) > 0 Then parts.Add(String.Format("{0} {1}", ModelKind.KIND_NAMES(k), tally(k)))
        Next
        LogThis("brain: {0} model mesh(es) in {1} ms, {2} skipped, {3:N0} triangles - {4}",
                built, sw.ElapsedMilliseconds, skipped, tris, String.Join(", ", parts))

        If MODEL_INDEX_LIST IsNot Nothing Then Total = MODEL_INDEX_LIST.Length
        LogThis("brain: {0:N0} placement(s) on the map", Total)
    End Sub

    ''' <summary>
    ''' One model's render sets concatenated into a single VAO.
    '''
    ''' nuTerra keeps them separate because each carries its own material and
    ''' textures. There are no materials here, so they merge - one buffer and
    ''' one draw per model instead of one per render set, which for a building
    ''' with six material groups is six times fewer calls.
    ''' </summary>
    Private Function build_one(lod As base_model_holder_) As Mesh
        Dim m As New Mesh
        Dim allV As New List(Of ModelVertex)
        Dim allI As New List(Of UInteger)
        Dim lo As New Vector3(Single.MaxValue), hi As New Vector3(Single.MinValue)

        For Each rs In lod.render_sets
            If rs?.buffers Is Nothing Then Continue For

            ' NO_DRAW IS NOT DECORATION. The map ships geometry that exists but
            ' is never rendered - boundary volumes, collision shells, occluder
            ' proxies - and it is flagged rather than absent. nuTerra's own
            ' batch loop tests both these flags before it counts a draw.
            '
            ' Skipping them was not optional: without it two map-boundary slabs
            ' stood across the whole play field in purple, which is exactly
            ' what an invisible wall looks like when you draw it.
            If rs.no_draw Then Continue For
            If rs.primitiveGroups IsNot Nothing AndAlso rs.primitiveGroups.Count > 0 Then
                Dim any = False
                For Each pg In rs.primitiveGroups.Values
                    If Not pg.no_draw Then any = True : Exit For
                Next
                If Not any Then Continue For
            End If

            Dim vb = rs.buffers.vertexBuffer
            Dim ib = rs.buffers.index_buffer32
            If vb Is Nothing OrElse ib Is Nothing OrElse vb.Length = 0 Then Continue For

            Dim base_v = CUInt(allV.Count)
            For Each v In vb
                allV.Add(v)
                lo = Vector3.ComponentMin(lo, v.pos)
                hi = Vector3.ComponentMax(hi, v.pos)
            Next
            ' index_buffer32 holds TRIANGLES - three indices each - which is
            ' why its element count is a third of the index count.
            For Each t In ib
                allI.Add(base_v + t.x)
                allI.Add(base_v + t.y)
                allI.Add(base_v + t.z)
            Next
        Next

        If allV.Count = 0 OrElse allI.Count = 0 Then Return m

        Dim vsize = Marshal.SizeOf(Of ModelVertex)()
        m.vao = GL.GenVertexArray()
        GL.BindVertexArray(m.vao)

        m.vbo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.vbo)
        Dim va = allV.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, va.Length * vsize, va, BufferUsageHint.StaticDraw)

        m.ibo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.ibo)
        Dim ia = allI.ToArray()
        GL.BufferData(BufferTarget.ElementArrayBuffer, ia.Length * 4, ia, BufferUsageHint.StaticDraw)

        ' position at 0, normal at 12 as four HALF floats - ModelVertex is
        ' pos(12) normal(8) tangent(8) binormal(8) uv(8). Only the first two
        ' are wanted: no textures means no uv, and no lighting model beyond a
        ' key light means no tangent frame.
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, vsize, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.HalfFloat, True, vsize, 12)

        GL.BindVertexArray(0)

        m.indexCount = ia.Length
        m.bbMin = lo
        m.bbMax = hi
        m.ok = True
        Return m
    End Function

    ''' <summary>
    ''' Draw every placement the view can see.
    '''
    ''' VIEW-SPACE CLIPPING, which is what the owner asked for. Each instance's
    ''' local box is transformed and tested against the frustum before anything
    ''' is issued. It is done on the CPU rather than through nuTerra's cull.comp
    ''' because that compute pass needs the shader system and the indirect
    ''' batching that come with MapStaticModels - 1,054 lines - and this app
    ''' draws a few thousand instances, not a few hundred thousand.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4)
        Drawn = 0
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If meshes Is Nothing OrElse MODEL_INDEX_LIST Is Nothing Then Return

        Dim planes = BrainFrustum.FromViewProj(viewProj)

        shader.Use()
        shader.SetMat4("viewProj", viewProj)

        Dim boundVao = -1
        For Each inst In MODEL_INDEX_LIST
            If inst.model_index < 0 OrElse inst.model_index >= meshes.Length Then Continue For
            Dim m = meshes(inst.model_index)
            If Not m.ok Then Continue For

            Dim mat = inst.matrix
            If Not BrainFrustum.BoxVisible(planes, mat, m.bbMin, m.bbMax) Then Continue For

            If m.vao <> boundVao Then
                GL.BindVertexArray(m.vao)
                boundVao = m.vao
            End If
            shader.SetMat4("model", mat)
            shader.SetVec3("kindColour", kind_colour(m.kind))
            GL.DrawElements(PrimitiveType.Triangles, m.indexCount,
                            DrawElementsType.UnsignedInt, 0)
            Drawn += 1
        Next
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>One placement's world-space footprint and what it is.</summary>
    Public Structure Footprint
        Public ok As Boolean
        Public kind As Byte
        Public minX, maxX, minZ, maxZ As Single
        ''' <summary>The four lower corners in world XZ, IN ORDER round the
        ''' quad. The AABB above is only their extent - rasterising THIS is
        ''' what stops a rotated fence claiming its bounding square.</summary>
        Public c0, c1, c2, c3 As Vector2
    End Structure

    ''' <summary>
    ''' The world XZ box a placement covers, for the nav grid.
    '''
    ''' All four lower corners are transformed and their extent taken, not
    ''' the min and max: a rotated box's min/max are not the transform of its
    ''' min/max, and on a WoT map most placements carry a rotation. Only the
    ''' lower corners, because a footprint is what the ground sees - an
    ''' overhanging roof is not something a tank drives into.
    ''' </summary>
    Public Function WorldFootprint(inst As MODEL_INDEX_LIST_) As Footprint
        Dim f As New Footprint
        If meshes Is Nothing Then Return f
        If inst.model_index < 0 OrElse inst.model_index >= meshes.Length Then Return f
        Dim m = meshes(inst.model_index)
        If Not m.ok Then Return f

        Dim mat = inst.matrix
        Dim lo = m.bbMin, hi = m.bbMax
        f.minX = Single.MaxValue : f.maxX = Single.MinValue
        f.minZ = Single.MaxValue : f.maxZ = Single.MinValue
        ' ROUND the quad, not across it: 00, 10, 11, 01. A diagonal ordering
        ' makes a bow-tie, and every inside test against it fails.
        Dim corners = {New Vector3(lo.X, lo.Y, lo.Z), New Vector3(hi.X, lo.Y, lo.Z),
                       New Vector3(hi.X, lo.Y, hi.Z), New Vector3(lo.X, lo.Y, hi.Z)}
        Dim w(3) As Vector2
        For i = 0 To 3
            Dim r = New Vector4(corners(i), 1.0F) * mat
            w(i) = New Vector2(r.X, r.Z)
            f.minX = Math.Min(f.minX, r.X) : f.maxX = Math.Max(f.maxX, r.X)
            f.minZ = Math.Min(f.minZ, r.Z) : f.maxZ = Math.Max(f.maxZ, r.Z)
        Next
        f.c0 = w(0) : f.c1 = w(1) : f.c2 = w(2) : f.c3 = w(3)
        f.kind = m.kind
        f.ok = True
        Return f
    End Function

    ''' <summary>The kind's colour, 0..1. ModelKind.KIND_RGB is the one table -
    ''' it is Path Studio's legend and the owner has been reading it since
    ''' before this app existed. Index 0 is terrain and unused there, so a
    ''' model that somehow classifies as terrain gets "other".</summary>
    Private Function kind_colour(k As Byte) As Vector3
        Dim i = k And 7
        Dim rgb = ModelKind.KIND_RGB(i)
        If rgb Is Nothing Then rgb = ModelKind.KIND_RGB(ModelKind.KIND_OTHER)
        Return New Vector3(rgb(0) / 255.0F, rgb(1) / 255.0F, rgb(2) / 255.0F)
    End Function

End Module

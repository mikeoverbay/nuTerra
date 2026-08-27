Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

'''<summary>
'''SpeedTree placement and geometry.
'''
'''Each distinct species is decoded once with <see cref="SrtFile"/> and its LOD0
'''geometry uploaded a single time. Every placement is then one instance of that
'''geometry, so a map with thousands of trees costs a few megabytes rather than
'''re-emitting the same trunk for each one.
'''
'''Drawing is one call per species part - a handful of species times a handful of
'''parts, so a few dozen calls in total.
'''</summary>
Public Class MapTrees
    Implements IDisposable

    ReadOnly scene As MapScene

    <StructLayout(LayoutKind.Sequential)>
    Private Structure TreeVertex
        Public pos As Vector3
        Public normal As Vector3
        Public uv As Vector2
        '''<summary>Bark or leaf atlas. Per vertex, so one species draws in one call.</summary>
        Public texHandle As UInt64
    End Structure

    '''<summary>One species' decoded geometry and its two textures.</summary>
    Private Class Species
        Public srt As SrtFile
        '''<summary>Leaves or fronds, whichever this species uses.</summary>
        Public leaf_handle As UInt64
        '''<summary>Trunk and branch bark. Ground cover has none.</summary>
        Public bark_handle As UInt64
        '''<summary>Handles for the textures the file names, keyed by name.</summary>
        Public declared As New Dictionary(Of String, UInt64)(StringComparer.OrdinalIgnoreCase)
        '''<summary>Where this species' vertices start in the shared buffer.</summary>
        Public base_vertex As Integer
        Public instances As New List(Of Matrix4)
        '''<summary>The bounding box the .srt header declares, put the right way round.</summary>
        Public cull_min As Vector3
        Public cull_max As Vector3
        Public valid As Boolean
        '''<summary>The .srt path, for the picker.</summary>
        Public path As String
    End Class

    '''<summary>
    ''' One species' draw state: an index range per LOD over the shared buffers,
    ''' and per frame, one contiguous run of visible instances per LOD.
    '''
    ''' The geometry for every LOD is packed at load; which range an instance
    ''' draws with is decided per frame in cull() from its distance to the
    ''' camera. Before this, every tree drew its LOD0 at every distance - a
    ''' forest of full-detail trees at 800 m, which is what made the Trees row
    ''' the red one in the stats panel.
    '''</summary>
    Private Class Part
        '''<summary>Indices per LOD - count and byte offset into the shared
        ''' index buffer. An authored LOD with no renderable geometry inherits
        ''' the previous one's range, so lookup never has to special-case.</summary>
        Public lod_index_count() As Integer
        Public lod_index_offset() As Integer
        Public lod_count As Integer
        '''<summary>Species .srt path, shown by the picker.</summary>
        Public name As String

        ''' <summary>
        ''' The asset's own LOD profile, header offset 0x30: full detail up to
        ''' hi, lowest 3D LOD from lo outward, linear in between. Using the
        ''' authored numbers means a species tuned to degrade early does so.
        ''' </summary>
        Public lod_hi As Single
        Public lod_lo As Single

        Public base_vertex As Integer
        '''<summary>Where this species sits in the full, unculled instance buffer.</summary>
        Public base_instance As Integer
        Public instance_count As Integer

        ' World space bounds of each placement, worked out once at load.
        Public mats() As Matrix4
        Public mins() As Vector3
        Public maxs() As Vector3

        ' Rewritten every frame into the compacted buffers, one run per LOD.
        Public visible_base() As Integer
        Public visible_count() As Integer
    End Class

    '''<summary>Per-instance LOD pick, scratch for cull()'s two passes. 255 = culled.</summary>
    Private bucket() As Byte

    Private vertices_buffer As GLBuffer
    Private indices_buffer As GLBuffer
    '''<summary>Every placement, in species order. Used by the shadow pass.</summary>
    Private instance_buffer As GLBuffer
    '''<summary>Just the placements that survived the frustum, rewritten each frame.</summary>
    Private visible_buffer As GLBuffer
    Private visible As Matrix4()
    Private vao As GLVertexArray
    Private parts As New List(Of Part)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub Build()
        ' Clear first: a map with no trees would otherwise keep showing the
        ' previous map's counts in the overlay.
        TREES_DRAWN = 0
        TREES_TOTAL = 0

        If cSpTr.trees Is Nothing OrElse cSpTr.trees.count = 0 Then
            Return
        End If

        Dim total = CInt(cSpTr.trees.count)

        BG_TEXT = "Loading Trees..."
        BG_VALUE = 0
        BG_MAX_VALUE = total
        main_window.ForceRender()

        Dim species As New Dictionary(Of UInt32, Species)
        Dim placed = 0

        For k = 0 To total - 1
            If k Mod 512 = 0 Then
                BG_VALUE = k
                main_window.ForceRender()
            End If

            Dim inst = cSpTr.trees.data(k)
            Dim sp As Species = Nothing
            If Not species.TryGetValue(inst.spt_fnv, sp) Then
                sp = load_species(cBWST.find_str(inst.spt_fnv))
                species(inst.spt_fnv) = sp
            End If
            If Not sp.valid Then Continue For

            ' Row-vector convention, same as the rest of the renderer, and the
            ' whole world is mirrored in x for display - so post-multiply by a
            ' scale of -1 in x rather than only negating the origin.
            sp.instances.Add(inst.transform * Matrix4.CreateScale(-1.0F, 1.0F, 1.0F))
            placed += 1
        Next

        Dim drawable = species.Values.Where(Function(s) s.valid AndAlso s.instances.Count > 0).ToList()
        LogThis("Trees: {0} instances, {1} species, {2} usable", total, species.Count, drawable.Count)
        If drawable.Count = 0 Then
            Return
        End If

        upload(drawable)
        If parts.Count = 0 Then
            Return
        End If

        TREES_TOTAL = placed
        scene.TREES_LOADED = True
        LogThis("Trees: {0} placed, {1} source triangles, {2} draw calls",
                placed, source_triangles(), parts.Count)
    End Sub

    Private Function source_triangles() As Integer
        ' LOD0 only - what a tree costs at close range, which is the number the
        ' load-time log line has always meant.
        Dim t = 0
        For Each p In parts
            t += p.lod_index_count(0) \ 3
        Next
        Return t
    End Function

    '''<summary>Decodes one .srt and resolves its textures.</summary>
    Private Shared Function load_species(srt_path As String) As Species
        Dim sp As New Species With {.valid = False, .path = srt_path}
        If String.IsNullOrEmpty(srt_path) Then Return sp

        Dim entry = ResMgr.Lookup(srt_path)
        If entry Is Nothing Then Return sp

        Try
            Using ms As New MemoryStream
                entry.Extract(ms)
                sp.srt = SrtFile.FromBytes(ms.ToArray(), srt_path)
            End Using
        Catch ex As Exception
            LogThis("Trees: {0} failed to read - {1}", srt_path, ex.Message)
            Return sp
        End Try

        If Not sp.srt.Solved Then
            LogThis("Trees: {0} not decoded - {1}", srt_path, sp.srt.Notes)
            Return sp
        End If

        Dim dir = Path.GetDirectoryName(srt_path).Replace("\"c, "/"c)
        sp.leaf_handle = load_handle(dir, sp.srt.FoliageTexture)
        If sp.leaf_handle = 0UL Then Return sp

        sp.bark_handle = load_handle(dir, sp.srt.BarkTexture)
        If sp.bark_handle = 0UL Then sp.bark_handle = sp.leaf_handle

        ' Most assets declare a texture per draw call. Load exactly those; the
        ' two atlases above are only the fallback for the ones that do not.
        For Each dc In sp.srt.DrawCalls
            If dc.DiffuseTexture <> "" AndAlso Not sp.declared.ContainsKey(dc.DiffuseTexture) Then
                Dim h = load_handle(dir, dc.DiffuseTexture)
                If h <> 0UL Then sp.declared(dc.DiffuseTexture) = h
            End If
        Next

        measure_species(sp)
        sp.valid = True
        Return sp
    End Function

    Private Shared Function load_handle(dir As String, leaf As String) As UInt64
        If String.IsNullOrEmpty(leaf) Then Return 0UL
        Dim tex = TextureMgr.find_and_load_texture_from_pkgs(dir & "/" & leaf)
        If tex Is Nothing Then Return 0UL
        Dim h = CULng(GL.Arb.GetTextureHandle(tex.texture_id))
        If Not GL.Arb.IsTextureHandleResident(h) Then
            GL.Arb.MakeTextureHandleResident(h)
        End If
        Return h
    End Function

    '''<summary>
    '''Packs every species' LOD0 geometry into one vertex and one index buffer,
    '''and every placement into one instance buffer, then records the draw calls.
    '''</summary>
    Private Sub upload(list As List(Of Species))
        Dim verts As New List(Of TreeVertex)
        Dim indices As New List(Of UInteger)
        Dim transforms As New List(Of Matrix4)

        For Each sp In list
            sp.base_vertex = verts.Count
            Dim base_instance = transforms.Count
            transforms.AddRange(sp.instances)

            ' Every LOD goes in, each recording its own index range. Vertex
            ' numbering restarts per draw call, so each one is shifted as it
            ' goes in and the whole species shares one base vertex. The atlas
            ' rides along per vertex, which lets the parts of one LOD merge
            ' into one call.
            Dim n_lods = Math.Max(sp.srt.LodCount, 1)
            Dim lod_count(n_lods - 1) As Integer
            Dim lod_offset(n_lods - 1) As Integer

            For lod = 0 To n_lods - 1
                lod_offset(lod) = indices.Count * 4

                For Each dc In sp.srt.DrawCalls
                    If dc.Lod <> lod OrElse Not dc.Renderable Then Continue For

                    Dim handle As UInt64
                    If dc.DiffuseTexture = "" OrElse Not sp.declared.TryGetValue(dc.DiffuseTexture, handle) Then
                        handle = If(dc.Kind = SrtFile.PartKind.Skin, sp.bark_handle, sp.leaf_handle)
                    End If
                    Dim local_base = CUInt(verts.Count - sp.base_vertex)

                    For v = 0 To dc.VertexCount - 1
                        verts.Add(New TreeVertex With {
                            .pos = New Vector3(dc.Positions(v * 3), dc.Positions(v * 3 + 1), dc.Positions(v * 3 + 2)),
                            .normal = New Vector3(dc.Normals(v * 3), dc.Normals(v * 3 + 1), dc.Normals(v * 3 + 2)),
                            .uv = New Vector2(dc.TexCoords(v * 2), dc.TexCoords(v * 2 + 1)),
                            .texHandle = handle})
                    Next
                    For i = 0 To dc.IndexCount - 1
                        indices.Add(dc.Indices(i) + local_base)
                    Next
                Next

                lod_count(lod) = indices.Count - (lod_offset(lod) \ 4)

                ' An authored LOD with nothing renderable in it inherits the
                ' previous range, so distance lookup never lands on a hole.
                If lod_count(lod) = 0 AndAlso lod > 0 Then
                    lod_count(lod) = lod_count(lod - 1)
                    lod_offset(lod) = lod_offset(lod - 1)
                End If
            Next

            If lod_count(0) = 0 Then Continue For

            ' The asset's own LOD profile: full detail out to hi, lowest LOD
            ' from lo. Some files carry zeros or nonsense here - fall back to
            ' something sane rather than promoting every tree to LOD0 forever.
            Dim hi = sp.srt.LodDistances(0)
            Dim lo = sp.srt.LodDistances(1)
            If Not (hi > 0.0F AndAlso lo > hi) Then
                hi = 40.0F
                lo = 250.0F
            End If

            ' Put each placement's box into world space once, so the per frame
            ' test is the same axis aligned one the models use.
            Dim mats = sp.instances.ToArray()
            Dim mins(mats.Length - 1) As Vector3
            Dim maxs(mats.Length - 1) As Vector3
            For k = 0 To mats.Length - 1
                world_box(sp.cull_min, sp.cull_max, mats(k), mins(k), maxs(k))
            Next

            parts.Add(New Part With {
                .lod_index_count = lod_count,
                .lod_index_offset = lod_offset,
                .lod_count = n_lods,
                .name = sp.path,
                .lod_hi = hi,
                .lod_lo = lo,
                .base_vertex = sp.base_vertex,
                .base_instance = base_instance,
                .instance_count = mats.Length,
                .mats = mats, .mins = mins, .maxs = maxs,
                .visible_base = New Integer(n_lods - 1) {},
                .visible_count = New Integer(n_lods - 1) {}})
        Next

        If verts.Count = 0 OrElse parts.Count = 0 Then
            parts.Clear()
            Return
        End If

        Dim varr = verts.ToArray()
        Dim iarr = indices.ToArray()
        Dim tarr = transforms.ToArray()
        Dim stride = Marshal.SizeOf(Of TreeVertex)
        Const MAT4_SIZE = 64

        vertices_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "treeVerts")
        vertices_buffer.Storage(varr.Length * stride, varr, BufferStorageFlags.None)

        indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "treeIndices")
        indices_buffer.Storage(iarr.Length * 4, iarr, BufferStorageFlags.None)

        instance_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "treeInstances")
        instance_buffer.Storage(tarr.Length * MAT4_SIZE, tarr, BufferStorageFlags.None)

        visible_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "treeInstancesVisible")
        visible_buffer.Storage(tarr.Length * MAT4_SIZE, tarr, BufferStorageFlags.DynamicStorageBit)
        ReDim visible(tarr.Length - 1)

        vao = GLVertexArray.Create("treesVao")
        vao.VertexBuffer(0, vertices_buffer, IntPtr.Zero, stride)
        vao.VertexBuffer(1, instance_buffer, IntPtr.Zero, MAT4_SIZE)
        vao.ElementBuffer(indices_buffer)

        'pos
        vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)

        'normal
        vao.AttribFormat(1, 3, VertexAttribType.Float, False, 12)
        vao.AttribBinding(1, 0)
        vao.EnableAttrib(1)

        'uv
        vao.AttribFormat(2, 2, VertexAttribType.Float, False, 24)
        vao.AttribBinding(2, 0)
        vao.EnableAttrib(2)

        'bindless atlas handle
        vao.AttribIFormat(3, 2, VertexAttribType.UnsignedInt, 32)
        vao.AttribBinding(3, 0)
        vao.EnableAttrib(3)

        ' per-instance transform, one mat4 as four vec4 slots
        For row = 0 To 3
            vao.AttribFormat(4 + row, 4, VertexAttribType.Float, False, row * 16)
            vao.AttribBinding(4 + row, 1)
            vao.EnableAttrib(4 + row)
        Next
        vao.BindingDivisor(1, 1)
    End Sub

    '''<summary>
    ''' The species' own bounding box, straight out of the .srt header at 0x14.
    '''
    ''' It already encloses everything the asset draws with a little to spare -
    ''' apple_7m_apples declares -2.652 -1.010 -5.002 .. 4.318 8.136 3.174 around
    ''' geometry that spans -2.650 -0.792 -5.000 .. 4.316 7.918 3.172 - so there
    ''' is nothing to measure. Some files store the z pair inverted, which is why
    ''' each axis is put back in order rather than taken as given.
    '''
    ''' There is no per draw call box anywhere in the header; this is the only
    ''' bound the format carries.
    '''</summary>
    Private Shared Sub measure_species(sp As Species)
        Dim lo, hi As Vector3
        lo.X = Math.Min(sp.srt.BoundsMin(0), sp.srt.BoundsMax(0))
        lo.Y = Math.Min(sp.srt.BoundsMin(1), sp.srt.BoundsMax(1))
        lo.Z = Math.Min(sp.srt.BoundsMin(2), sp.srt.BoundsMax(2))
        hi.X = Math.Max(sp.srt.BoundsMin(0), sp.srt.BoundsMax(0))
        hi.Y = Math.Max(sp.srt.BoundsMin(1), sp.srt.BoundsMax(1))
        hi.Z = Math.Max(sp.srt.BoundsMin(2), sp.srt.BoundsMax(2))
        sp.cull_min = lo
        sp.cull_max = hi
    End Sub

    '''<summary>
    ''' Corners of an object space box through a transform, back out as an axis
    ''' aligned box in world space.
    '''</summary>
    Private Shared Sub world_box(lo As Vector3, hi As Vector3, m As Matrix4,
                                 ByRef out_min As Vector3, ByRef out_max As Vector3)
        out_min = New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        out_max = New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        For c = 0 To 7
            Dim corner As New Vector3(If((c And 1) = 0, lo.X, hi.X),
                                      If((c And 2) = 0, lo.Y, hi.Y),
                                      If((c And 4) = 0, lo.Z, hi.Z))
            Dim w = Vector3.TransformPosition(corner, m)
            out_min = Vector3.ComponentMin(out_min, w)
            out_max = Vector3.ComponentMax(out_max, w)
        Next
    End Sub

    '''<summary>
    ''' Keeps only the placements the camera can see, packed so each (species,
    ''' LOD) pair is one contiguous run and so one instanced draw.
    '''
    ''' Two passes per part: the first frustum-tests each instance and picks its
    ''' LOD from distance to the camera, the second writes the matrices into
    ''' runs laid out from the counts. The pick is remembered in a byte per
    ''' instance between the passes so the distance maths runs once.
    '''
    ''' The mapping honours the asset's authored profile: LOD0 out to lod_hi,
    ''' the last LOD from lod_lo onward, the rest spread linearly between. There
    ''' is no far cull - past lod_lo a tree keeps drawing its cheapest LOD,
    ''' because trees blinking out of a hillside reads as a bug, not a saving.
    '''</summary>
    Private Function cull() As Integer
        Dim cam = scene.camera.PerViewData.cameraPos

        ' Scratch sized to the largest species, reused across frames.
        Dim biggest = 0
        For Each p In parts
            biggest = Math.Max(biggest, p.mats.Length)
        Next
        If bucket Is Nothing OrElse bucket.Length < biggest Then
            ReDim bucket(biggest - 1)
        End If

        Dim written = 0
        For Each p In parts
            Dim span = Math.Max(p.lod_lo - p.lod_hi, 1.0F)

            For l = 0 To p.lod_count - 1
                p.visible_count(l) = 0
            Next

            ' Pass 1: visibility and LOD choice.
            For k = 0 To p.mats.Length - 1
                If BoxInFrustum(p.mins(k), p.maxs(k)) Then
                    ' Row-vector convention: translation lives in Row3.
                    Dim d = (p.mats(k).Row3.Xyz - cam).Length
                    Dim f = (d - p.lod_hi) / span
                    Dim l = Math.Clamp(CInt(Math.Floor(f * p.lod_count)), 0, p.lod_count - 1)
                    bucket(k) = CByte(l)
                    p.visible_count(l) += 1
                Else
                    bucket(k) = Byte.MaxValue
                End If
            Next

            ' Lay the runs out back to back, then reuse visible_base as the
            ' write cursor for pass 2 and restore it afterwards.
            For l = 0 To p.lod_count - 1
                p.visible_base(l) = written
                written += p.visible_count(l)
            Next

            For k = 0 To p.mats.Length - 1
                If bucket(k) <> Byte.MaxValue Then
                    visible(p.visible_base(bucket(k))) = p.mats(k)
                    p.visible_base(bucket(k)) += 1
                End If
            Next

            For l = 0 To p.lod_count - 1
                p.visible_base(l) -= p.visible_count(l)
            Next
        Next
        Return written
    End Function
    '''<summary>Species name for a picked tree-band id (0-based part index).</summary>
    Public Function pick_name(part_index As Integer) As String
        If part_index < 0 OrElse part_index >= parts.Count Then Return "tree ?"
        Return parts(part_index).name
    End Function

    Public Sub draw()
        If Not scene.TREES_LOADED OrElse vao Is Nothing Then
            Return
        End If

        GL_PUSH_GROUP("draw_trees")

        Dim shown = cull()
        TREES_DRAWN = shown

        ' The LOD split, for the stats panel. Cheap, and it is the direct check
        ' that distance is actually demoting geometry rather than everything
        ' still landing in bucket zero.
        Dim tally(7) As Integer
        Dim deepest = 1
        For Each p In parts
            deepest = Math.Max(deepest, p.lod_count)
            For l = 0 To p.lod_count - 1
                tally(l) += p.visible_count(l)
            Next
        Next
        TREES_LOD_TEXT = String.Join("/"c, tally.Take(deepest))
        If shown > 0 Then
            visible_buffer.SubData(IntPtr.Zero, shown * 64, visible)

            treeShader.Use()

            ' Leaf cards are two sided, and the x mirror reverses winding anyway.
            GL.Disable(EnableCap.CullFace)

            ' Depth writes have to be turned back on. draw_models leaves the mask
            ' off - it does its own depth pre-pass and does not need it - and
            ' trees run straight after. Without this the foliage tests against
            ' the depth buffer but never writes to it, so terrain and buildings
            ' occlude trees correctly while trees do not occlude each other, and
            ' cards inside one canopy draw in whatever order they happen to be
            ' in. That reads as a transparency sorting problem and is not one:
            ' the shader alpha tests rather than blends, so once the depth is
            ' written the result is order independent and needs no sorting.
            GL.DepthMask(True)

            vao.VertexBuffer(1, visible_buffer, IntPtr.Zero, 64)
            vao.Bind()
            ' One draw per (species, LOD) with anything in it - still a few
            ' dozen calls, but the far instances now carry far geometry.
            Dim part_idx = 0
            For Each p In parts
                ' Picker: one id per species, in the tree band above the model
                ' ids. Whatever LOD is on screen is what gets picked - the far
                ' billboards included, which keeps the pick pass free.
                If ModelPicker.Enabled Then
                    GL.Uniform1(treeShader("pick_id"), CUInt(ModelPicker.TREE_PICK_BASE + part_idx + 1))
                End If
                For l = 0 To p.lod_count - 1
                    If p.visible_count(l) = 0 Then Continue For
                    GL.DrawElementsInstancedBaseVertexBaseInstance(
                        PrimitiveType.Triangles, p.lod_index_count(l), DrawElementsType.UnsignedInt,
                        New IntPtr(p.lod_index_offset(l)), p.visible_count(l), p.base_vertex, p.visible_base(l))
                Next
                part_idx += 1
            Next

            treeShader.StopUse()
            GL.Enable(EnableCap.CullFace)
            ' leave the mask as we found it
            GL.DepthMask(False)
        End If

        GL_POP_GROUP()
    End Sub

    '''<summary>
    ''' Depth only pass into the cascades.
    '''
    ''' Every placement is drawn, culled by nothing. That is deliberate and it
    ''' is what the models do - indirect_shadow_mapping is built once at load
    ''' and MultiDrawElementsIndirect walks all of it every time. A caster does
    ''' not have to be on screen for its shadow to be, so culling the shadow
    ''' pass against the camera frustum makes shadows wink out as their tree
    ''' leaves the view.
    '''
    ''' The fragment stage alpha tests against the leaf atlas, so what lands in
    ''' the shadow map is the outline of the leaves rather than of the cards.
    '''</summary>
    Public Sub shadow_pass()
        If Not scene.TREES_LOADED OrElse vao Is Nothing Then
            Return
        End If

        GL_PUSH_GROUP("trees_shadow_pass")

        TREES_CASTING = TREES_TOTAL

        treeDepthShader.Use()
        GL.Disable(EnableCap.CullFace)

        vao.VertexBuffer(1, instance_buffer, IntPtr.Zero, 64)
        vao.Bind()
        ' Cheapest LOD: a cascade texel is far coarser than the LOD0/LODn
        ' difference, so the extra geometry never reached the map anyway.
        For Each p In parts
            Dim l = p.lod_count - 1
            GL.DrawElementsInstancedBaseVertexBaseInstance(
                PrimitiveType.Triangles, p.lod_index_count(l), DrawElementsType.UnsignedInt,
                New IntPtr(p.lod_index_offset(l)), p.instance_count, p.base_vertex, p.base_instance)
        Next

        treeDepthShader.StopUse()
        GL.Enable(EnableCap.CullFace)

        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Depth only pass into the map-wide sun shadow bake.
    '''
    ''' Every placement is drawn, culled by nothing - same reasoning as
    ''' shadow_pass: a caster does not have to be on screen for its shadow to be.
    ''' It matters more here, because the bake happens once at map load from a
    ''' camera that has nothing to do with where the player ends up standing.
    '''
    ''' Unlike shadow_pass this goes through one ortho projection instead of a
    ''' geometry stage fanning out to four cascades, so it takes sunViewProj
    ''' directly. The fragment stage still alpha tests against the leaf atlas, so
    ''' what lands in the bake is the outline of the leaves and not of the cards.
    ''' </summary>
    Public Sub sun_depth_pass(sun_view_proj As Matrix4)
        If Not scene.TREES_LOADED OrElse vao Is Nothing Then
            Return
        End If

        GL_PUSH_GROUP("trees_sun_depth_pass")

        sunDepthTreeShader.Use()
        GL.UniformMatrix4(sunDepthTreeShader("sunViewProj"), False, sun_view_proj)

        ' Leaf cards are two sided, and the x mirror reverses winding anyway.
        GL.Disable(EnableCap.CullFace)

        vao.VertexBuffer(1, instance_buffer, IntPtr.Zero, 64)
        vao.Bind()
        ' Full-detail LOD0: this runs once per bake, so the cost is nothing and
        ' the leaf cutout in the shadow is as good as the asset can give.
        For Each p In parts
            GL.DrawElementsInstancedBaseVertexBaseInstance(
                PrimitiveType.Triangles, p.lod_index_count(0), DrawElementsType.UnsignedInt,
                New IntPtr(p.lod_index_offset(0)), p.instance_count, p.base_vertex, p.base_instance)
        Next

        sunDepthTreeShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        vertices_buffer?.Dispose()
        indices_buffer?.Dispose()
        instance_buffer?.Dispose()
        visible_buffer?.Dispose()
        vao?.Dispose()
    End Sub
End Class

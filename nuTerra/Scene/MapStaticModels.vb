Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Class MapStaticModels
    Implements IDisposable

    ReadOnly scene As MapScene

    ' Get data from gpu: opaque, double-sided, glass, volumetric FX
    Public numAfterFrustum(3) As Integer

    ' OpenGL buffers used to draw all map models
    ' For map models only!
    Public materials As GLBuffer
    Public parameters As GLBuffer
    Public parameters_temp As GLBuffer
    Public matrices As GLBuffer
    Public drawCandidates As GLBuffer
    Public verts As GLBuffer
    Public vertsUV2 As GLBuffer
    Public prims As GLBuffer
    Public indirect As GLBuffer
    Public indirect_glass As GLBuffer
    Public indirect_dbl_sided As GLBuffer
    Public indirect_fx As GLBuffer
    Public vertsColour As GLBuffer
    Public indirect_shadow_mapping As GLBuffer
    Public lods As GLBuffer

    ' For cull-raster only!
    Public visibles As GLBuffer
    Public visibles_dbl_sided As GLBuffer

    Public allMapModels As GLVertexArray

    Public numModelInstances As Integer
    Public indirectDrawCount As Integer
    Public indirectShadowMappingDrawCount As Integer

    ' World origin of every candidate draw's instance, kept CPU-side by the
    ' loader. draw_fx sorts its bucket back-to-front with these each frame,
    ' indexed by the command's baseInstance (= candidate id).
    Public candidate_origins As Vector3()

    ' Candidate id -> model instance id, so Snapshot can name what is in the
    ' FX bucket (those draws never touch the pick buffer, so the model
    ' picker cannot identify them).
    Public candidate_model_ids As UInteger()

    ' Candidate id -> material id. Kept CPU-side because drawCommands is erased
    ' right after its buffer upload (MapLoader.vb:356), but the FX composite
    ' class cannot be derived until load_materials has run.
    Public candidate_material_ids As UInteger()

    ''' <summary>
    ''' Candidate id -> FX composite class, fixed at load. True means this draw's
    ''' MODEL INSTANCE carries an additive volumetric material: volumetric.frag
    ''' takes the mat.alphaTestEnable branch and emits (rgb*a, 0), which under
    ''' this pass's premultiplied One / OneMinusSrcAlpha reduces to dst + src -
    ''' it adds light and attenuates nothing.
    '''
    ''' Alpha materials emit (rgb*a, a) and DO attenuate, which is why they have
    ''' to composite FIRST. That is the whole "fire after smoke" rule.
    '''
    ''' Per INSTANCE, not per draw: every draw of one instance shares a
    ''' candidate_origin, so they already compare equal on distance and tie-break
    ''' on baseInstance = authored prim-group order. Classing per draw would
    ''' split a mesh that mixes an alpha layer with additive layers and silently
    ''' reorder the authored layer stack inside content that is known to work.
    ''' </summary>
    Public candidate_fx_additive As Boolean()

    ' Host-memory staging for the FX sort readback - reading indirect_fx
    ' directly made the driver demote it to host memory (perf warning
    ' #131186 every frame). Same pattern as parameters_temp: GPU-copy the
    ' commands here, read from here, so indirect_fx itself stays in VRAM.
    Public indirect_fx_staging As GLBuffer
    Public Const FX_SORT_MAX As Integer = 4096

    ' draw_fx sort scratch, grown on demand so steady state allocates nothing
    Private fx_cmds As DrawElementsIndirectCommand()
    Private fx_cmds_sorted As DrawElementsIndirectCommand()
    Private fx_order As Integer()
    Private fx_dist As Single()
    ' Composite class per sorted entry: 0 = alpha (smoke), 1 = additive (fire).
    Private fx_class As Integer()
    Private fx_sort_overflow_logged As Boolean
    ' Separate latch. Sharing one flag with the overflow above meant whichever
    ' condition fired first silenced the other for the rest of the session.
    Private fx_sort_range_logged As Boolean

    ' Sort hysteresis: a draw keeps its stored distance until the real one
    ' drifts past this, so near-equidistant plumes do not swap order back and
    ' forth while the camera orbits - each swap re-composites the overlap,
    ' which reads as flicker during rotation. 10 m is nothing at plume scale.
    Private Const FX_SORT_HYSTERESIS As Single = 10.0F
    Private ReadOnly fx_stored_dist As New Dictionary(Of UInteger, Single)

    ' Instruments for Snapshot: how often the drawn FX order actually changed.
    Public fx_sort_order_changes As Integer
    Private fx_prev_order As UInteger()
    Private fx_prev_order_count As Integer

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>
    ''' Appends every static model instance to an already-open binary STL stream.
    ''' Only LOD 0 is emitted, so each instance appears once at full detail.
    '''
    ''' Positions are mirrored in X and written with Y/Z swapped, matching
    ''' MapTerrain.Export, so the models line up with the terrain in the same file.
    ''' </summary>
    ''' <param name="bw">Writer positioned at the end of the facet list.</param>
    ''' <param name="total_face_count">Running facet total, incremented per triangle.</param>
    Public Sub AppendToStl(bw As BinaryWriter, ByRef total_face_count As UInteger)
        If verts Is Nothing OrElse prims Is Nothing OrElse matrices Is Nothing OrElse drawCandidates Is Nothing Then
            Return
        End If

        Dim vertex_size = Marshal.SizeOf(Of ModelVertex)
        Dim tri_size = Marshal.SizeOf(Of vect3_32)

        Dim num_verts = CInt(verts.size / vertex_size)
        Dim num_tris = CInt(prims.size / tri_size)

        Dim vertsData(num_verts - 1) As ModelVertex
        GL.GetNamedBufferSubData(verts.buffer_id, IntPtr.Zero, verts.size, vertsData)

        Dim trisData(num_tris - 1) As vect3_32
        GL.GetNamedBufferSubData(prims.buffer_id, IntPtr.Zero, prims.size, trisData)

        Dim instData(numModelInstances - 1) As ModelInstance
        GL.GetNamedBufferSubData(matrices.buffer_id, IntPtr.Zero, matrices.size, instData)

        Dim drawData(indirectDrawCount - 1) As CandidateDraw
        GL.GetNamedBufferSubData(drawCandidates.buffer_id, IntPtr.Zero, drawCandidates.size, drawData)

        ' Clip to the terrain chunk footprint. These are the same bounds
        ' check_map_border uses, expressed in STL file space: X is the mirrored
        ' world X and Y is the world Z (see to_stl_space).
        Dim clip_min_x = (-b_x_max - 1.0F) * 100.0F
        Dim clip_max_x = -b_x_min * 100.0F
        Dim clip_min_y = (b_y_min - 1.0F) * 100.0F
        Dim clip_max_y = b_y_max * 100.0F
        LogThis("Model clip box: X {0} .. {1}, Y {2} .. {3}", clip_min_x, clip_max_x, clip_min_y, clip_max_y)

        BG_VALUE = 0
        BG_MAX_VALUE = indirectDrawCount
        BG_TEXT = "Exporting Models..."

        Dim written As UInteger = 0
        Dim skipped As Integer = 0
        Dim clipped As Integer = 0

        For d = 0 To indirectDrawCount - 1
            Dim dc = drawData(d)

            ' highest detail only, otherwise every instance is emitted once per LOD
            If dc.lod_level <> 0UI Then
                Continue For
            End If
            If dc.model_id >= numModelInstances Then
                Continue For
            End If

            ' OpenTK stores row-major but the SSBO is read column-major, so the
            ' shader's "matrix * vertex" is a transposed multiply on the CPU.
            Dim m = instData(dc.model_id).matrix
            Dim mat = New Assimp.Matrix4x4(
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44)

            ' count and firstIndex are in indices; the prim buffer holds triples
            Dim first_tri = CInt(dc.firstIndex \ 3UI)
            Dim tri_count = CInt(dc.count \ 3UI)

            For t = 0 To tri_count - 1
                Dim idx = first_tri + t
                If idx < 0 OrElse idx >= num_tris Then
                    skipped += 1
                    Continue For
                End If

                Dim tri = trisData(idx)
                Dim i1 = CInt(dc.baseVertex + tri.x)
                Dim i2 = CInt(dc.baseVertex + tri.y)
                Dim i3 = CInt(dc.baseVertex + tri.z)

                If i1 < 0 OrElse i2 < 0 OrElse i3 < 0 OrElse
                   i1 >= num_verts OrElse i2 >= num_verts OrElse i3 >= num_verts Then
                    skipped += 1
                    Continue For
                End If

                Dim w1 = to_stl_space(mat, vertsData(i1).pos)
                Dim w2 = to_stl_space(mat, vertsData(i2).pos)
                Dim w3 = to_stl_space(mat, vertsData(i3).pos)

                ' Drop anything reaching outside the chunk footprint. A triangle is
                ' kept only if all three corners are inside, so nothing protrudes
                ' past the terrain edge.
                If outside_clip(w1, clip_min_x, clip_max_x, clip_min_y, clip_max_y) OrElse
                   outside_clip(w2, clip_min_x, clip_max_x, clip_min_y, clip_max_y) OrElse
                   outside_clip(w3, clip_min_x, clip_max_x, clip_min_y, clip_max_y) Then
                    clipped += 1
                    Continue For
                End If

                Dim no = Assimp.Vector3D.Cross(w2 - w1, w3 - w1)
                If no.LengthSquared() > 0.0F Then
                    no.Normalize()
                Else
                    skipped += 1
                    Continue For
                End If

                bw.Write(no.X) : bw.Write(no.Y) : bw.Write(no.Z)
                bw.Write(w1.X) : bw.Write(w1.Y) : bw.Write(w1.Z)
                bw.Write(w2.X) : bw.Write(w2.Y) : bw.Write(w2.Z)
                bw.Write(w3.X) : bw.Write(w3.Y) : bw.Write(w3.Z)
                bw.Write(CUShort(0))

                written += 1UI
            Next

            If d Mod 4096 = 0 Then
                BG_VALUE = d
                main_window.ForceRender()
            End If
        Next

        total_face_count += written
        LogThis("Model export: {0} triangles written, {1} clipped outside map, {2} skipped, from {3} draws", written, clipped, skipped, indirectDrawCount)
    End Sub

    ''' <summary>
    ''' Model space to STL file space: transform to world, mirror X (the exporter
    ''' works in a mirrored X, same as MapTerrain.Export), then swap Y and Z.
    ''' </summary>
    ' True when a point lies outside the terrain footprint in STL file space.
    Private Shared Function outside_clip(p As Assimp.Vector3D,
                                         min_x As Single, max_x As Single,
                                         min_y As Single, max_y As Single) As Boolean
        Return p.X < min_x OrElse p.X > max_x OrElse p.Y < min_y OrElse p.Y > max_y
    End Function

    Private Shared Function to_stl_space(mat As Assimp.Matrix4x4, pos As Vector3) As Assimp.Vector3D
        Dim w = mat * New Assimp.Vector3D(pos.X, pos.Y, pos.Z)
        Return New Assimp.Vector3D(-w.X, w.Z, w.Y)
    End Function

    Public Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        parameters.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, numAfterFrustum.Length * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        cullShader.Use()

        GL.Uniform1(cullShader("numModelInstances"), numModelInstances)

        Dim numGroups = (numModelInstances + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
        GL.Arb.DispatchComputeGroupSize(numGroups, 1, 1, WORK_GROUP_SIZE, 1, 1)

        ' Command: the indirect draws source these buffers. BufferUpdate:
        ' draw_fx reads the fx bucket back with GetNamedBufferSubData to
        ' depth-sort it before drawing.
        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit Or MemoryBarrierFlags.BufferUpdateBarrierBit)

        cullShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub model_cull_raster_pass()
        GL_PUSH_GROUP("model_cull_raster_pass")

        GL.ColorMask(False, False, False, False)
        ' we need this because the depth has been writen already.
        GL.DepthFunc(DepthFunction.Gequal)
        GL.DepthMask(False)

        'clear
        visibles.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, numAfterFrustum(0) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)
        visibles_dbl_sided.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, numAfterFrustum(1) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        defaultVao.Bind()

        If USE_REPRESENTATIVE_TEST Then
            GL.Enable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        cullRasterShader.Use()
        GL.Uniform1(cullRasterShader("numAfterFrustum"), numAfterFrustum(0))
        GL.DrawArrays(PrimitiveType.Points, 0, numAfterFrustum(0) + numAfterFrustum(1))
        cullRasterShader.StopUse()

        If USE_REPRESENTATIVE_TEST Then
            GL.Disable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit)

        cullInvalidateShader.Use()
        GL.Uniform1(cullInvalidateShader("numAfterFrustum"), numAfterFrustum(0))
        GL.Uniform1(cullInvalidateShader("numAfterFrustumDblSided"), numAfterFrustum(1))

        Dim numGroups = (Math.Max(numAfterFrustum(0), numAfterFrustum(1)) + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
        GL.Arb.DispatchComputeGroupSize(numGroups, 1, 1, WORK_GROUP_SIZE, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullInvalidateShader.StopUse()

        GL.DepthMask(True)
        GL.ColorMask(True, True, True, True)

        GL_POP_GROUP()
    End Sub

    Public Sub shadow_mapping_pass()
        GL_PUSH_GROUP("MapStaticModels::shadow_mapping_pass")

        mDepthWrite_light.Use()

        GL.Enable(EnableCap.CullFace)

        allMapModels.Bind()

        indirect_shadow_mapping.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, indirectShadowMappingDrawCount, 0)

        mDepthWrite_light.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub model_depth_pass()
        'This is just to depth pass write to allow early z reject and stop
        ' wetness from showing through the models.
        GL_PUSH_GROUP("model_depth_pass")

        '------------------------------------------------
        mDepthWriteShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.ColorMask(False, False, False, False)
        GL.Enable(EnableCap.CullFace)

        allMapModels.Bind()

        indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(1), 0)

        mDepthWriteShader.StopUse()
        GL.ColorMask(True, True, True, True)

        GL.Enable(EnableCap.CullFace)

        GL_POP_GROUP()
    End Sub

    Public Sub draw_models()
        GL_PUSH_GROUP("draw_models")

        ' we need this because the depth has been writen already.
        GL.DepthFunc(DepthFunction.Equal)
        GL.DepthMask(False)

        'SOLID FILL
        MainFBO.attach_CNGP()

        ' Element i selects the subroutine for shader_type i. Element 11
        ' (FX_volumetric) never draws in this pass - cull routes it to the
        ' FX bucket - but GL wants every element assigned, so it gets the
        ' FX_unsupported function (9). Element 12 = FX_PBS_tiled_global,
        ' whose function carries layout index 11.
        ' Element 13 = FX_glow, whose function carries layout index 12.
        Dim indices = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 9, 11, 12}
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        'assign subroutines
        GL.UniformSubroutines(ShaderType.FragmentShader, indices.Length, indices)

        GL.Enable(EnableCap.CullFace)

        allMapModels.Bind()

        indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(1), 0)

        modelShader.StopUse()

        GL.DepthFunc(DepthFunction.Greater)

        MainFBO.attach_CNGPA()
        GL.DepthMask(True)

        '------------------------------------------------
        modelGlassShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        indirect_glass.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(2), 0)

        modelGlassShader.StopUse()

        MainFBO.attach_CNGP()
        GL.DepthMask(False)

        If WIRE_MODELS Then
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)

            MainFBO.attach_CF()
            normalShader.Use()

            GL.Uniform1(normalShader("prj_length"), 0.3F)
            GL.Uniform1(normalShader("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(normalShader("show_wireframe"), CInt(WIRE_MODELS))

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(2), 0)

            indirect.Bind(BufferTarget.DrawIndirectBuffer)
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(0), 0)
            normalShader.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        End If

        If SHOW_BOUNDING_BOXES Then
            GL.Disable(EnableCap.DepthTest)

            boxShader.Use()
            GL.Uniform1(boxShader("box_filter"), CInt(If(BOXES_VOLUMETRIC_ONLY, 1, 0)))

            defaultVao.Bind()
            GL.DrawArrays(PrimitiveType.Points, 0, numModelInstances)

            boxShader.StopUse()
        End If

        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Volumetric GFX meshes - smoke columns, flame sheets - forward over the
    ''' lit frame, after water. Translucent: depth-tested against the scene
    ''' (reversed-Z Greater), no depth write, no cull (the materials are all
    ''' double sided), standard alpha blend. The shader is a transcription of
    ''' the game's volumetric_effect_vtx fxo.
    ''' </summary>
    ''' <summary>
    ''' Reorders the FX indirect bucket back-to-front by instance origin.
    ''' The cull shader emits FX draws in atomic-counter order, which varies
    ''' frame to frame - with order-dependent "over" blending, overlapping
    ''' smoke flickered where the plumes crossed. Sorting fixes the flicker
    ''' (the order is now deterministic) and composites separate plumes in
    ''' the right depth order. Draws of one instance tie-break on candidate
    ''' id so their relative order is frame-stable too.
    ''' </summary>
    Private Sub sort_fx_draws(count As Integer)
        If count < 2 OrElse candidate_origins Is Nothing OrElse indirect_fx_staging Is Nothing Then Return

        If count > FX_SORT_MAX Then
            If Not fx_sort_overflow_logged Then
                LogThis("draw_fx sort: {0} draws exceeds FX_SORT_MAX {1} - drawing unsorted", count, FX_SORT_MAX)
                fx_sort_overflow_logged = True
            End If
            Return
        End If

        If fx_cmds Is Nothing OrElse fx_cmds.Length < count Then
            ReDim fx_cmds(count - 1)
            ReDim fx_cmds_sorted(count - 1)
            ReDim fx_order(count - 1)
            ReDim fx_dist(count - 1)
            ReDim fx_class(count - 1)
        End If

        Dim byte_count = count * Marshal.SizeOf(Of DrawElementsIndirectCommand)
        GL.CopyNamedBufferSubData(indirect_fx.buffer_id, indirect_fx_staging.buffer_id, IntPtr.Zero, IntPtr.Zero, byte_count)
        GL.GetNamedBufferSubData(indirect_fx_staging.buffer_id, IntPtr.Zero, byte_count, fx_cmds)

        Dim cam = scene.camera.CAM_POSITION
        For i = 0 To count - 1
            Dim candidate = CInt(fx_cmds(i).baseInstance)
            If candidate >= candidate_origins.Length Then
                ' Latched like the overflow above - this runs per frame and
                ' would spam for as long as the condition holds. Its OWN latch:
                ' sharing the overflow flag hid whichever fault came second.
                If Not fx_sort_range_logged Then
                    LogThis("draw_fx sort: candidate {0} out of range {1} - drawing unsorted", candidate, candidate_origins.Length)
                    fx_sort_range_logged = True
                End If
                Return
            End If
            fx_order(i) = i

            ' Hysteresis: sort on the stored distance, refreshed only when the
            ' true distance drifts past the window. Keeps near-ties from
            ' oscillating while the orbit camera moves.
            Dim d = (candidate_origins(candidate) - cam).Length
            Dim stored As Single
            If Not fx_stored_dist.TryGetValue(fx_cmds(i).baseInstance, stored) OrElse
               Math.Abs(d - stored) > FX_SORT_HYSTERESIS Then
                stored = d
                fx_stored_dist(fx_cmds(i).baseInstance) = d
            End If
            fx_dist(i) = stored

            ' Composite class. Deliberately NOT part of the early-return guard
            ' above: a missing array degrades to class 0 for everything, which is
            ' exactly today's behaviour. Turning it into a Return instead would
            ' leave the bucket in the cull shader's nondeterministic atomic
            ' order, which is the flicker this sort exists to kill.
            fx_class(i) = 0
            If candidate_fx_additive IsNot Nothing AndAlso
               candidate < candidate_fx_additive.Length AndAlso
               candidate_fx_additive(candidate) Then
                fx_class(i) = 1
            End If
        Next

        Array.Sort(fx_order, 0, count, Comparer(Of Integer).Create(
            Function(a, b)
                ' PRIMARY KEY: composite class. Alpha (0) before additive (1), so
                ' smoke can never attenuate fire. Additive draws are
                ' order-independent - they add light and attenuate nothing - so
                ' moving them last changes no other draw's result.
                Dim k = fx_class(a).CompareTo(fx_class(b))
                If k <> 0 Then Return k
                ' Within a class, unchanged: farthest first, tie-break on the
                ' authored prim-group order.
                Dim c = fx_dist(b).CompareTo(fx_dist(a)) ' farthest first
                If c <> 0 Then Return c
                Return fx_cmds(a).baseInstance.CompareTo(fx_cmds(b).baseInstance)
            End Function))

        For i = 0 To count - 1
            fx_cmds_sorted(i) = fx_cmds(fx_order(i))
        Next
        GL.NamedBufferSubData(indirect_fx.buffer_id, IntPtr.Zero, byte_count, fx_cmds_sorted)

        ' Instrument: count frames where the drawn sequence differs from the
        ' previous frame's. Snapshot prints and resets it - churn here while
        ' the camera moves is what overlap flicker looks like in numbers.
        If fx_prev_order Is Nothing OrElse fx_prev_order.Length < count Then
            ReDim Preserve fx_prev_order(count - 1)
            fx_prev_order_count = -1 ' force one "change" on growth
        End If
        Dim changed = count <> fx_prev_order_count
        If Not changed Then
            For i = 0 To count - 1
                If fx_cmds_sorted(i).baseInstance <> fx_prev_order(i) Then
                    changed = True
                    Exit For
                End If
            Next
        End If
        If changed Then
            fx_sort_order_changes += 1
            For i = 0 To count - 1
                fx_prev_order(i) = fx_cmds_sorted(i).baseInstance
            Next
            fx_prev_order_count = count
        End If
    End Sub

    ''' <summary>
    ''' Names what the FX bucket holds right now. These draws never touch
    ''' the pick buffer, so the model picker cannot identify them - Snapshot
    ''' calls this instead.
    ''' </summary>
    Public Sub LogFxBucket()
        Dim count = Math.Min(numAfterFrustum(3), FX_SORT_MAX)
        If count = 0 OrElse candidate_model_ids Is Nothing OrElse indirect_fx_staging Is Nothing Then Return

        If fx_cmds Is Nothing OrElse fx_cmds.Length < count Then ReDim fx_cmds(count - 1)
        Dim byte_count = count * Marshal.SizeOf(Of DrawElementsIndirectCommand)
        GL.CopyNamedBufferSubData(indirect_fx.buffer_id, indirect_fx_staging.buffer_id, IntPtr.Zero, IntPtr.Zero, byte_count)
        GL.GetNamedBufferSubData(indirect_fx_staging.buffer_id, IntPtr.Zero, byte_count, fx_cmds)

        Dim by_model As New Dictionary(Of UInteger, Integer)
        For i = 0 To count - 1
            Dim cand = CInt(fx_cmds(i).baseInstance)
            If cand >= candidate_model_ids.Length Then Continue For
            Dim mid = candidate_model_ids(cand)
            by_model(mid) = If(by_model.ContainsKey(mid), by_model(mid) + 1, 1)
        Next
        For Each kv In by_model
            Dim name As String = Nothing
            scene.PICK_DICTIONARY.TryGetValue(kv.Key, name)
            LogThis("    fx in view: {0} draw(s)  instance {1}  {2}", kv.Value, kv.Key, If(name, "?"))
        Next
    End Sub

    Public Sub draw_fx()
        If numAfterFrustum(3) = 0 Then Return

        GL_PUSH_GROUP("draw_fx")

        sort_fx_draws(numAfterFrustum(3))

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        ' Premultiplied, so the shader can serve alpha AND additive materials
        ' in one multidraw: alpha outputs (rgb*a, a), additive (rgb*a, 0).
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        volumetricShader.Use()
        GL.Uniform1(volumetricShader("fx_time"), FX_TIME)
        ' Scene position for the soft-particle fade. gPosition is view space,
        ' despite every writer calling it world. Sampling it while it is still
        ' ATTACHED to the bound framebuffer is a feedback loop - the driver
        ' raised GL_INVALID_OPERATION - so detach it for the pass and put it
        ' back afterwards. Being merely absent from the draw buffers is not
        ' enough.
        MainFBO.fbo.Texture(FramebufferAttachment.ColorAttachment3, Nothing, 0)
        MainFBO.gPosition.BindUnit(3)

        ' Same ambient probe the deferred pass uses, so the smoke and the
        ' ground beneath it are lit from one source.
        Dim sh_flat(26) As Single
        For i = 0 To 8
            sh_flat(i * 3 + 0) = SH_AMBIENT(i).X
            sh_flat(i * 3 + 1) = SH_AMBIENT(i).Y
            sh_flat(i * 3 + 2) = SH_AMBIENT(i).Z
        Next
        GL.Uniform3(volumetricShader("sh_ambient"), 9, sh_flat)
        GL.Uniform1(volumetricShader("sh_enabled"),
                    CInt(If(USE_SH_AMBIENT AndAlso SH_AMBIENT_LOADED, 1, 0)))

        ' The baked probe FIELD, the same one deferred.frag folds into the
        ' ground's ambient. Mirror of the block in modRender.draw_deferred with
        ' the shader swapped, and it MUST stay a mirror: if these values ever
        ' diverge from the ones the deferred shader gets, the smoke and the
        ' ground under it are lit by two different fields and this has no point.
        '
        ' Two deliberate divergences, both argued in volumetric.vert:
        '   sh_grid_enabled - also gated on USE_SH_GRID_FX (default False).
        '   sh_grid_offset  - sent as SH_GRID_OFFSET_FX, which is 0. The 1.5 m
        '                     normal push is tuned for wall SURFACES; a smoke
        '                     card's normals span far too wide an arc, so
        '                     inheriting it scatters neighbouring lookups by a
        '                     whole probe cell and splits one column.
        '
        ' The BIND is not gated on the FX toggle. Fire and smoke are one
        ' MultiDrawElementsIndirect through one program, so a declared sampler3D
        ' against an unbound unit 11 is an incomplete-texture condition for the
        ' WHOLE draw, fire included. Bind whenever the texture exists; the only
        ' remaining unbound case is a map with no grid at all, where the
        ' deferred pass sits in exactly the same state today.
        Dim fx_grid_on = USE_SH_GRID AndAlso USE_SH_GRID_FX AndAlso
                         SH_GRID_LOADED AndAlso SH_GRID_ID IsNot Nothing
        If SH_GRID_ID IsNot Nothing Then SH_GRID_ID.BindUnit(11)
        If fx_grid_on Then
            ' The shader computes uv = world.xz * scale - offset. Our world is
            ' mirrored in x for display and the bake is not, so x runs
            ' backwards - scale_x is NEGATIVE and the x offset is built from
            ' centre PLUS half size. Copied from the deferred upload rather
            ' than re-derived.
            '   z : uv = (w - min)/size  -> scale =  1/size, offset =  min/size
            '   x : uv = (max - w)/size  -> scale = -1/size, offset = -max/size
            Dim scale_z = 1.0F / SH_GRID_SIZE.Z
            Dim offset_z = (SH_GRID_CENTRE.Z - SH_GRID_SIZE.Z * 0.5F) * scale_z
            Dim scale_x = -1.0F / SH_GRID_SIZE.X
            Dim offset_x = -(SH_GRID_CENTRE.X + SH_GRID_SIZE.X * 0.5F) / SH_GRID_SIZE.X

            GL.Uniform4(volumetricShader("sh_grid_uv"), offset_x, offset_z, scale_x, scale_z)
            GL.Uniform1(volumetricShader("sh_grid_fade"), 1.0F / Math.Max(SH_GRID_FADE, 0.001F))
            GL.Uniform1(volumetricShader("sh_grid_offset"), SH_GRID_OFFSET_FX)
            GL.Uniform1(volumetricShader("sh_grid_mix"), SH_GRID_MIX)
            ' There is no SH_GRID_EDGE global - it is derived, so the
            ' expression has to be recomputed here, not read.
            GL.Uniform1(volumetricShader("sh_grid_edge"),
                        2.0F * SH_GRID_SPACING / Math.Max(SH_GRID_SIZE.X, 1.0F))

            Static grid_sh_flat(26) As Single
            For i = 0 To 8
                grid_sh_flat(i * 3 + 0) = SH_GRID_SH9(i).X
                grid_sh_flat(i * 3 + 1) = SH_GRID_SH9(i).Y
                grid_sh_flat(i * 3 + 2) = SH_GRID_SH9(i).Z
            Next
            GL.Uniform3(volumetricShader("sh_grid_sh9"), 9, grid_sh_flat)
        End If
        ' UNCONDITIONAL, outside the If, exactly as the deferred upload does it.
        ' volumetricShader is a separate program object with its own uniform
        ' state, and draw_fx early-returns above when nothing is in view, so
        ' loading a grid-less map after a grid map would otherwise leave
        ' sh_grid_enabled = 1 and a stale sh_grid_uv against an unbound unit 11.
        GL.Uniform1(volumetricShader("sh_grid_enabled"), CInt(If(fx_grid_on, 1, 0)))

        allMapModels.Bind()
        indirect_fx.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, numAfterFrustum(3), 0)

        ' Put gPosition back before anything downstream expects to write it.
        MainFBO.fbo.Texture(FramebufferAttachment.ColorAttachment3, MainFBO.gPosition, 0)

        volumetricShader.StopUse()

        GL.Disable(EnableCap.Blend)
        ' BlendFunc is global state - put the app's conventional func back so
        ' later passes that enable blend without setting one (minimap trims,
        ' text) do not inherit the premultiplied pair.
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.DepthMask(True)
        ' The post-water stretch of the frame runs with the depth test OFF -
        ' leaving it on here made the FXAA fullscreen quad fail the reversed-Z
        ' test against the cleared depth buffer, which wiped the whole frame
        ' to the clear colour whenever an FX was on screen.
        GL.Disable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Public Sub glassPass()
        GL_PUSH_GROUP("perform_GlassPass")

        'GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)

        'GL.ReadBuffer(ReadBufferMode.Back)

        glassPassShader.Use()
        GL.UniformMatrix4(glassPassShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        MainFBO.gColor.BindUnit(0)
        MainFBO.gAUX_Color.BindUnit(1)

        'draw full screen quad
        GL.Uniform4(glassPassShader("rect"), 0.0F, CSng(-MainFBO.height), CSng(MainFBO.width), 0.0F)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        glassPassShader.StopUse()

        ' UNBIND
        unbind_textures(2)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        materials?.Dispose()
        parameters?.Dispose()
        parameters_temp?.Dispose()
        matrices?.Dispose()
        drawCandidates?.Dispose()
        verts?.Dispose()
        vertsUV2?.Dispose()
        prims?.Dispose()
        indirect?.Dispose()
        indirect_glass?.Dispose()
        indirect_fx?.Dispose()
        indirect_fx_staging?.Dispose()
        candidate_origins = Nothing
        fx_cmds = Nothing
        fx_cmds_sorted = Nothing
        fx_order = Nothing
        fx_dist = Nothing
        fx_stored_dist.Clear()
        fx_prev_order = Nothing
        vertsColour?.Dispose()
        indirect_dbl_sided?.Dispose()
        indirect_shadow_mapping?.Dispose()
        lods?.Dispose()

        visibles?.Dispose()
        visibles_dbl_sided?.Dispose()

        allMapModels?.Dispose()
    End Sub
End Class

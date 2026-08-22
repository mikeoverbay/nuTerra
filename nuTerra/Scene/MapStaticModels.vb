Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Class MapStaticModels
    Implements IDisposable

    ReadOnly scene As MapScene

    ' Get data from gpu
    Public numAfterFrustum(2) As Integer

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
    Public indirect_shadow_mapping As GLBuffer
    Public lods As GLBuffer

    ' For cull-raster only!
    Public visibles As GLBuffer
    Public visibles_dbl_sided As GLBuffer

    Public allMapModels As GLVertexArray

    Public numModelInstances As Integer
    Public indirectDrawCount As Integer
    Public indirectShadowMappingDrawCount As Integer

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

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

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

        Dim indices = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10}
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

            defaultVao.Bind()
            GL.DrawArrays(PrimitiveType.Points, 0, numModelInstances)

            boxShader.StopUse()
        End If

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
        indirect_dbl_sided?.Dispose()
        indirect_shadow_mapping?.Dispose()
        lods?.Dispose()

        visibles?.Dispose()
        visibles_dbl_sided?.Dispose()

        allMapModels?.Dispose()
    End Sub
End Class

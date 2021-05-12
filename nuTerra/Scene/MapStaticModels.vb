Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

Public Class MapStaticModels
    Implements IDisposable

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
    Public lods As GLBuffer

    ' For cull-raster only!
    Public visibles As GLBuffer
    Public visibles_dbl_sided As GLBuffer

    Public allMapModels As GLVertexArray

    Public numModelInstances As Integer
    Public indirectDrawCount As Integer

    Public Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        parameters.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, 3 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

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
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(1), 0)

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

        Dim indices = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        'assign subroutines
        GL.UniformSubroutines(ShaderType.FragmentShader, indices.Length, indices)

        GL.Enable(EnableCap.CullFace)

        map_scene.static_models.allMapModels.Bind()

        map_scene.static_models.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        map_scene.static_models.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(1), 0)

        modelShader.StopUse()

        GL.DepthFunc(DepthFunction.Greater)

        MainFBO.attach_CNGPA()
        GL.DepthMask(True)

        '------------------------------------------------
        modelGlassShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        map_scene.static_models.indirect_glass.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(2), 0)

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

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(2), 0)

            map_scene.static_models.indirect.Bind(BufferTarget.DrawIndirectBuffer)
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(0), 0)
            normalShader.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        End If

        If SHOW_BOUNDING_BOXES Then
            GL.Disable(EnableCap.DepthTest)

            boxShader.Use()

            defaultVao.Bind()
            GL.DrawArrays(PrimitiveType.Points, 0, map_scene.static_models.numModelInstances)

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
        GL.Uniform4(glassPassShader("rect"), 0.0F, CSng(-MainFBO.SCR_HEIGHT), CSng(MainFBO.SCR_WIDTH), 0.0F)

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
        lods?.Dispose()

        visibles?.Dispose()
        visibles_dbl_sided?.Dispose()

        allMapModels?.Dispose()
    End Sub
End Class

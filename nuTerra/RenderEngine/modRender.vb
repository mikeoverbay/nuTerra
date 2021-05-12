Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Module modRender
    Dim temp_timer As New Stopwatch
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Public uv_location As New Vector2

    Dim colors() As Color4 = {
        Color4.Red,
        Color4.Green,
        Color4.Blue,
        Color4.Yellow,
        Color4.Purple,
        Color4.Orange,
        Color4.Coral,
        Color4.Silver
        }

    Dim tags() As String = {
        "Texture 1",
        "Texture 2",
        "Texture 3",
        "Texture 4",
        "Texture 5",
        "Texture 6",
        "Texture 7",
        "Texture 8"
        }


    Public Sub draw_scene()
        '===========================================================================
        ' FLAG INFO
        ' 0  = No shading
        ' 64  = model 
        ' 128 = terrain
        ' 255 = sky dome. We will want to control brightness
        ' more as they are added
        '===========================================================================
        'house keeping
        FRAME_TIMER.Restart()
        '===========================================================================

        '===========================================================================

        GL.FrontFace(FrontFaceDirection.Ccw)
        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.gl_pick_map()
            Return
        End If
        If SHOW_LOADING_SCREEN Then
            draw_loading_screen()
            Return
        End If
        '===========================================================================

        '===========================================================================
        set_prespective_view() ' <-- sets camera and prespective view ==============
        '===========================================================================

        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            '=======================================================================
            frustum_cull() '========================================================
            '=======================================================================
        End If

        '===========================================================================
        If TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            ExtractFrustum()
            cull_terrain()

            map_scene.terrain.terrain_vt_pass()
        End If
        '===========================================================================

        '===========================================================================
        MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, frmMain.glControl_main.ClientSize.Width, frmMain.glControl_main.ClientSize.Height)
        '===========================================================================

        '===========================================================================
        MainFBO.attach_CNGPA() 'clear ALL gTextures!
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        '===========================================================================
        MainFBO.attach_C()
        map_scene.sky.draw_sky()

        '===========================================================================
        'GL States 
        GL.DepthFunc(DepthFunction.Greater)
        '===========================================================================

        'Model depth pass only
        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            GL.CopyNamedBufferSubData(map_scene.static_models.parameters.buffer_id, map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, IntPtr.Zero, 3 * Marshal.SizeOf(Of Integer))
            GL.GetNamedBufferSubData(map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, 3 * Marshal.SizeOf(Of Integer), map_scene.static_models.numAfterFrustum)

            '=======================================================================
            model_depth_pass() '=========================================================
            '=======================================================================

            If USE_RASTER_CULLING Then
                model_cull_raster_pass()
            End If
        End If

        MainFBO.attach_CNGPA()

        If TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            '=======================================================================
            draw_terrain() '========================================================
            '=======================================================================
            If (SHOW_BORDER Or SHOW_CHUNKS Or SHOW_GRID) Then draw_terrain_grids()
            '=======================================================================
            If SHOW_CURSOR Then
                'setup for projection before drawing
                MainFBO.attach_C_no_Depth()
                GL.DepthMask(False)
                GL.FrontFace(FrontFaceDirection.Cw)
                GL.Enable(EnableCap.CullFace)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
                '=======================================================================
                draw_map_cursor() '=================================
                '=======================================================================
                'restore settings after projected objects are drawn
                GL.DepthMask(True)
                GL.Disable(EnableCap.CullFace)
                MainFBO.attach_Depth()
                GL.FrontFace(FrontFaceDirection.Ccw)
            End If
        End If

        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            '=======================================================================
            draw_models() '=========================================================
            '=======================================================================
        End If
        '===========================================================================

        GL.DepthFunc(DepthFunction.Less)
        '===========================================================================
        If PICK_MODELS AndAlso MODELS_LOADED Then PickModel()
        '===========================================================================

        '===========================================================================
        '================== Deferred Rendering, HUD and MINI MAP ===================
        '===========================================================================


        '===========================================================================
        'Before we destory the gColor texture using it for other functions.
        If frmGbufferViewer IsNot Nothing Then
            If frmGbufferViewer.Visible AndAlso frmGbufferViewer.Viewer_Image_ID = 2 Then
                frmGbufferViewer.update_screen()
            End If
        End If


        '===========================================================================
        '===========================================================================
        '===========================================================================
        '===========================================================================
        Ortho_main()
        '===========================================================================
        '===========================================================================
        '===========================================================================
        '===========================================================================



        GL.Disable(EnableCap.DepthTest)

        MainFBO.attach_C2()

        render_deferred_buffers()
        'gAux_color to gColor;
        MainFBO.attach_C1_and_C2()
        copy_gColor_2_to_gColor()


        '===========================================================================
        'DEFAUL BUFFER ATTACH!!!
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        '===========================================================================

        If FXAA_enable Then
            perform_SSAA_Pass()
            copy_default_to_gColor()
        End If

        '===========================================================================
        'hopefully, this will look like glass :)
        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            glassPass()
        End If

        '===========================================================================

        'ortho projection decals
#If True Then

        MainFBO.attach_C()


        If TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            GL.Disable(EnableCap.DepthTest)

            copy_default_to_gColor()
            GL.DepthMask(False)
            'GL.FrontFace(FrontFaceDirection.Cw)
            GL.Enable(EnableCap.Blend)
            GL.Enable(EnableCap.CullFace)

            map_scene.base_rings.draw_base_rings_deferred()

            'hopefully, this will look like FOG :)
            GL.Disable(EnableCap.Blend)
            copy_default_to_gColor()
            map_scene.fog.global_fog()

            GL.Disable(EnableCap.DepthTest)
            GL.DepthMask(True)
            GL.Disable(EnableCap.CullFace)
            GL.FrontFace(FrontFaceDirection.Ccw)
        End If
#End If

        '===========================================================================
        If DONT_HIDE_HUD Then
            '===========================================================================
            'color_correct()
            '===========================================================================
            render_HUD() '==============================================================
            '===========================================================================

            '===========================================================================
            'This has to be called last. It changes the PROJECTMATRIX and VIEWMATRIX
            If DONT_HIDE_MINIMAP Then map_scene.mini_map.draw_mini_map() '===========================================================
            '===========================================================================
        End If
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
        If _STARTED Then frmMain.glControl_main.SwapBuffers() '=====================
        '===========================================================================
        If frmGbufferViewer IsNot Nothing Then
            If frmGbufferViewer.Visible AndAlso frmGbufferViewer.Viewer_Image_ID <> 2 Then
                frmGbufferViewer.update_screen()
            End If
        End If

        If frmModelViewer IsNot Nothing Then
            If frmModelViewer.Visible Then
                frmModelViewer.draw_model_view()
            End If
        End If

        FPS_COUNTER += 1
    End Sub

    Public map_center As Vector3
    Public scale As Vector3

    '=============================================================================================
    Private Sub render_deferred_buffers()
        GL_PUSH_GROUP("render_deferred_buffers")
        '===========================================================================
        ' Test our deferred shader =================================================
        '===========================================================================
        deferredShader.Use()

        MainFBO.gColor.BindUnit(0)
        MainFBO.gNormal.BindUnit(1)
        MainFBO.gGMF.BindUnit(2)
        MainFBO.gPosition.BindUnit(3)
        CUBE_TEXTURE_ID.BindUnit(4)
        CC_LUT_ID.BindUnit(5)
        ENV_BRDF_LUT_ID?.BindUnit(6)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT) 'render Gbuffer lighting

        ' UNBIND
        unbind_textures(7)

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub
    '=============================================================================================
    Private Sub color_keys()
        If Not SHOW_TEST_TEXTURES Then Return

        draw_image_rectangle(New RectangleF(0.0F, 79.0F, 100.0F, 19.0F * 8.0F),
                                            DUMMY_TEXTURE_ID)

        For i = 0 To 7
            draw_text(tags(i), 5.0F, 81.0F + (i * 19.0F), colors(i), False, 0)
        Next
    End Sub

    Private Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        map_scene.static_models.parameters.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, 3 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        cullShader.Use()

        GL.Uniform1(cullShader("numModelInstances"), map_scene.static_models.numModelInstances)

        Dim numGroups = (map_scene.static_models.numModelInstances + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
        GL.Arb.DispatchComputeGroupSize(numGroups, 1, 1, WORK_GROUP_SIZE, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(MainFBO.gColor.texture_id, 0, 0, 0, 0, 0, MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT)
    End Sub

    Private Sub copy_gColor_2_to_gColor()
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment6)
        MainFBO.fbo.DrawBuffer(DrawBufferMode.ColorAttachment0)
        GL.BlitNamedFramebuffer(
            MainFBO.fbo.fbo_id,
            MainFBO.fbo.fbo_id,
            0, 0, MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT,
            0, 0, MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest)
    End Sub

    Private Sub draw_terrain()
        GL_PUSH_GROUP("draw_terrain")

        ' EANABLE FACE CULLING
        GL.Enable(EnableCap.CullFace)

        ' BIND LQ SHADER
        TerrainLQShader.Use()

        ' BIND VT TEXTURES
        map_scene.terrain.vt.Bind()

        ' BIND TERRAIN VAO
        map_scene.terrain.all_chunks_vao.Bind()

        ' BIND TERRAIN INDIRECT BUFFER
        map_scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.LQ Then
                ' CALC NORMAL MATRIX FOR CHUNK
                GL.UniformMatrix3(TerrainLQShader("normalMatrix"), False, New Matrix3(PerViewData.view * theMap.render_set(i).matrix))

                ' DRAW CHUNK INDIRECT
                GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
            End If
        Next

        ' UNBIND SHADER
        TerrainLQShader.StopUse()

        If USE_TESSELLATION Then
            GL_PUSH_GROUP("draw_terrain: tessellation")

            ' BIND HQ SHADER
            TerrainHQShader.Use()

            For i = 0 To theMap.render_set.Length - 1
                If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.HQ Then
                    ' CALC NORMAL MATRIX FOR CHUNK
                    GL.UniformMatrix3(TerrainHQShader("normalMatrix"), False, New Matrix3(PerViewData.view * theMap.render_set(i).matrix))

                    ' DRAW CHUNK INDIRECT
                    GL.DrawElementsIndirect(PrimitiveType.Patches, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                End If
            Next

            ' UNBIND SHADER
            TerrainHQShader.StopUse()

            GL_POP_GROUP()
        End If

        ' RESTORE STATE
        GL.Disable(EnableCap.CullFace)

        If WIRE_TERRAIN Then
            GL_PUSH_GROUP("draw_terrain: wire")

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            MainFBO.attach_CF()

            TerrainNormals.Use()

            GL.Uniform1(TerrainNormals("prj_length"), 0.5F)
            GL.Uniform1(TerrainNormals("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(TerrainNormals("show_wireframe"), CInt(WIRE_TERRAIN))

            For i = 0 To theMap.render_set.Length - 1
                If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality <> TerrainQuality.HQ Then
                    GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                End If
            Next

            TerrainNormals.StopUse()

            If USE_TESSELLATION Then
                TerrainNormalsHQ.Use()

                GL.Uniform1(TerrainNormalsHQ("prj_length"), 0.2F)
                GL.Uniform1(TerrainNormalsHQ("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
                GL.Uniform1(TerrainNormalsHQ("show_wireframe"), CInt(WIRE_TERRAIN))

                For i = 0 To theMap.render_set.Length - 1
                    If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.HQ Then
                        GL.DrawElementsIndirect(PrimitiveType.Patches, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                    End If
                Next

                TerrainNormalsHQ.StopUse()
            End If

            ' RESTORE STATE
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

            GL_POP_GROUP()
        End If

        ' UNBIND VT TEXTURES
        map_scene.terrain.vt.Unbind()

        GL_POP_GROUP()
    End Sub

    Private Sub model_depth_pass()
        'This is just to depth pass write to allow early z reject and stop
        ' wetness from showing through the models.
        GL_PUSH_GROUP("model_depth_pass")

        '------------------------------------------------
        mDepthWriteShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.ColorMask(False, False, False, False)
        GL.Enable(EnableCap.CullFace)

        map_scene.static_models.allMapModels.Bind()

        map_scene.static_models.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        map_scene.static_models.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, map_scene.static_models.numAfterFrustum(1), 0)

        mDepthWriteShader.StopUse()
        GL.ColorMask(True, True, True, True)

        GL.Enable(EnableCap.CullFace)

        GL_POP_GROUP()
    End Sub

    Private Sub model_cull_raster_pass()
        GL_PUSH_GROUP("model_cull_raster_pass")

        GL.ColorMask(False, False, False, False)
        ' we need this because the depth has been writen already.
        GL.DepthFunc(DepthFunction.Gequal)
        GL.DepthMask(False)

        'clear
        map_scene.static_models.visibles.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, map_scene.static_models.numAfterFrustum(0) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)
        map_scene.static_models.visibles_dbl_sided.ClearSubData(PixelInternalFormat.R32ui, IntPtr.Zero, map_scene.static_models.numAfterFrustum(1) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        defaultVao.Bind()

        If USE_REPRESENTATIVE_TEST Then
            GL.Enable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        cullRasterShader.Use()
        GL.Uniform1(cullRasterShader("numAfterFrustum"), map_scene.static_models.numAfterFrustum(0))
        GL.DrawArrays(PrimitiveType.Points, 0, map_scene.static_models.numAfterFrustum(0) + map_scene.static_models.numAfterFrustum(1))
        cullRasterShader.StopUse()

        If USE_REPRESENTATIVE_TEST Then
            GL.Disable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit)

        cullInvalidateShader.Use()
        GL.Uniform1(cullInvalidateShader("numAfterFrustum"), map_scene.static_models.numAfterFrustum(0))
        GL.Uniform1(cullInvalidateShader("numAfterFrustumDblSided"), map_scene.static_models.numAfterFrustum(1))

        Dim numGroups = (Math.Max(map_scene.static_models.numAfterFrustum(0), map_scene.static_models.numAfterFrustum(1)) + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
        GL.Arb.DispatchComputeGroupSize(numGroups, 1, 1, WORK_GROUP_SIZE, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullInvalidateShader.StopUse()

        GL.DepthMask(True)
        GL.ColorMask(True, True, True, True)

        GL_POP_GROUP()
    End Sub

    Private Sub draw_models()
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

    Private Sub draw_terrain_grids()
        GL_PUSH_GROUP("draw_terrain_grids")

        MainFBO.attach_C()
        'GL.DepthMask(False)
        GL.Enable(EnableCap.DepthTest)
        TerrainGrids.Use()
        GL.Uniform2(TerrainGrids("bb_tr"), MAP_BB_UR.X, MAP_BB_UR.Y)
        GL.Uniform2(TerrainGrids("bb_bl"), MAP_BB_BL.X, MAP_BB_BL.Y)
        GL.Uniform1(TerrainGrids("g_size"), PLAYER_FIELD_CELL_SIZE)

        GL.Uniform1(TerrainGrids("show_border"), CInt(SHOW_BORDER))
        GL.Uniform1(TerrainGrids("show_chunks"), CInt(SHOW_CHUNKS))
        GL.Uniform1(TerrainGrids("show_grid"), CInt(SHOW_GRID))

        MainFBO.gGMF.BindUnit(0)

        map_scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)
        map_scene.terrain.all_chunks_vao.Bind()

        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, IntPtr.Zero, theMap.render_set.Length, 0)
        TerrainGrids.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Private Sub perform_SSAA_Pass()

        GL_PUSH_GROUP("perform_SSAA_Pass")

        FXAAShader.Use()

        GL.Uniform1(FXAAShader("pass_through"), CInt(FXAA_enable))

        GL.UniformMatrix4(FXAAShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform2(FXAAShader("viewportSize"), CSng(MainFBO.SCR_WIDTH), CSng(MainFBO.SCR_HEIGHT))

        MainFBO.gColor.BindUnit(0)

        'draw full screen quad
        GL.Uniform4(FXAAShader("rect"), 0.0F, CSng(-MainFBO.SCR_HEIGHT), CSng(MainFBO.SCR_WIDTH), 0.0F)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FXAAShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL_POP_GROUP()
    End Sub

    Private Sub glassPass()
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

    Private Sub draw_terrain_ids()
        GL_PUSH_GROUP("draw_terrain_ids")

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible Then ' Dont do math on no-visible chunks

                Dim v As Vector4
                v.Y = theMap.v_data(i).avg_heights
                v.W = 1.0

                Dim sp = UnProject_Chunk(v, theMap.render_set(i).matrix)

                If sp.Z > 0.0F Then
                    Dim s = theMap.chunks(i).name + ":" + i.ToString("000")
                    draw_text(s, sp.X, sp.Y, Color4.Yellow, True, 1)
                    s = String.Format("{0}, {1}", theMap.render_set(i).matrix.Row3(0), theMap.render_set(i).matrix.Row3(2))
                    draw_text(s, sp.X, sp.Y - 19, Color4.Yellow, True, 1)

                End If
            End If
        Next

        GL_POP_GROUP()
    End Sub

    Private Sub render_HUD()
        GL_PUSH_GROUP("render_HUD")

        temp_timer.Restart()
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        ' Text Rendering ===========================================================
        'save this.. we may want to use it for debug with a different source for the values.
        'Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")

        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds

        Dim txt = String.Format("FPS: {0,-3} | Draw time in Milliseconds: {1,-2} | VRAM usage: {2,-4}mb of {3}mb", FPS_TIME, elapsed, GLCapabilities.memory_usage, GLCapabilities.total_mem_mb)
        'debug shit
        'txt = String.Format("mouse {0} {1}", MINI_WORLD_MOUSE_POSITION.X.ToString, MINI_WORLD_MOUSE_POSITION.Y.ToString)
        'txt = String.Format("HX {0} : HY {1}", HX, HY)
        draw_text(txt, 5.0F, 5.0F, Color4.Cyan, False, 1)
        draw_text(PICKED_STRING, 5.0F, 24.0F, Color4.Yellow, False, 1)

        color_keys()

        'draw status of SSAA
        draw_text(FXAA_text, 5.0F, 62.0F, Color4.Yellow, False, 1)
        Dim temp_time = temp_timer.ElapsedMilliseconds

        ' Draw Terrain IDs =========================================================
        If SHOW_CHUNK_IDs AndAlso DONT_BLOCK_TERRAIN Then
            draw_terrain_ids()
        End If
        '===========================================================================

        GL_POP_GROUP()
    End Sub

    Private Sub draw_map_cursor()
        GL_PUSH_GROUP("draw_map_cursor")

        DecalProject.Use()

        GL.Uniform3(DecalProject("color_in"), 0.4F, 0.3F, 0.3F)

        CURSOR_TEXTURE_ID.BindUnit(0)
        MainFBO.gDepth.BindUnit(1)
        MainFBO.gGMF.BindUnit(2)

        ' Track the terrain at Y
        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, CURSOR_Y, U_LOOK_AT_Z)
        Dim model_S = Matrix4.CreateScale(25.0F, 50.0F, 25.0F)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        'GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DecalProject("DecalMatrix"), False, rotate * model_S * model_X)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DecalProject.StopUse()

        ' UNBIND
        unbind_textures(3)

        GL_POP_GROUP()
    End Sub

#Region "miniMap"


#End Region

    Private Sub draw_main_Quad(w As Integer, h As Integer)
        GL.Uniform4(deferredShader("rect"), 0.0F, CSng(-h), CSng(w), 0.0F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
    End Sub

    Public Function cube_point_intersection(ByRef rot As Matrix4, ByRef scale As Matrix4, ByRef translate As Matrix4, ByRef point As Vector3) As Boolean
        'rotate * scale * translate
        'point in world space to check if its in out side of the cube
        'based on a 1 x 1 x 1 cube

        ' get translate
        Dim trans As Vector4 = translate.Row3
        trans.Normalize()
        Dim p = New Vector4(point, 0.0)
        p.Normalize()
        p = p * scale * rot + trans

        Dim VTL As New Vector4(0.5, 0.5, 0.5, 1.0)
        Dim VBR As New Vector4(-0.5, -0.5, -0.5, 1.0)
        VTL = VTL * scale + trans
        VBR = VBR * scale + trans

        If VTL.X <= p.X Or VBR.X >= p.X Then Return False
        If VTL.Y <= p.Z Or VBR.Y >= p.Z Then Return False
        If VTL.Z >= p.Y Or VBR.Z >= p.Y Then Return False

        Return True
    End Function

    Public Sub draw_text(ByRef text As String,
                         ByVal locX As Single,
                         ByVal locY As Single,
                         ByRef color As Color4,
                         ByRef center As Boolean,
                         ByRef mask As Integer)
        ' text, loc X, loc Y, color, Center text at X location,
        ' mask 1 = drak background.

        '=======================================================================
        'draw text at location.
        '=======================================================================
        'setup
        If text Is Nothing Then Return

        GL.Enable(EnableCap.Blend)
        TextRenderShader.Use()
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(TextRenderShader("divisor"), 162.0F) 'atlas size
        ASCII_ID.BindUnit(0)
        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        GL.Uniform4(TextRenderShader("color"), color)
        GL.Uniform1(TextRenderShader("mask"), mask)
        '=======================================================================
        'draw text
        Dim cntr = 0
        If center Then
            cntr = text.Length * 10.0F / 2.0F
        End If
        Dim cnt As Integer = 0
        defaultVao.Bind()
        For Each l In text
            Dim idx = ASCII_CHARACTERS.IndexOf(l) + 1
            Dim tp = (locX + cnt * 10.0) - cntr
            GL.Uniform1(TextRenderShader("index"), CSng(idx))
            Dim rect As New RectangleF(tp, locY, 10.0F, 15.0F)
            GL.Uniform4(TextRenderShader("rect"),
                      rect.Left,
                      -rect.Top,
                      rect.Right,
                      -rect.Bottom)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            cnt += 1
        Next
        GL.Disable(EnableCap.Blend)
        TextRenderShader.StopUse()
        GL.BindTextureUnit(0, 0)

    End Sub

    Public Sub draw_text_Wrap(ByRef text As String,
                         ByVal locX As Single,
                         ByVal locY As Single,
                         ByRef color As Color4,
                         ByRef center As Boolean,
                         ByRef mask As Integer,
                         ByRef wrapWidth As Integer)
        ' text, loc X, loc Y, color, Center text at X location,
        ' mask 1 = drak background.
        ' Width = target size in charaters to wrap at.

        '=======================================================================
        'draw text at location.
        '=======================================================================
        'setup
        If text Is Nothing Then Return

        GL.Enable(EnableCap.Blend)
        TextRenderShader.Use()
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(TextRenderShader("divisor"), 162.0F) 'atlas size
        ASCII_ID.BindUnit(0)
        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        GL.Uniform4(TextRenderShader("color"), color)
        GL.Uniform1(TextRenderShader("mask"), mask)
        '=======================================================================
        'draw text
        Dim cntr = 0
        If center Then
            cntr = text.Length * 10.0F / 2.0F
        End If
        Dim cnt As Integer = 0
        defaultVao.Bind()
        For Each l In text
            Dim idx = ASCII_CHARACTERS.IndexOf(l) + 1
            Dim tp = (locX + cnt * 10.0) - cntr
            If tp > wrapWidth AndAlso idx = 0 Then
                cnt = -1
                locY += 19
            End If
            GL.Uniform1(TextRenderShader("index"), CSng(idx))
            Dim rect As New RectangleF(tp, locY, 10.0F, 15.0F)
            GL.Uniform4(TextRenderShader("rect"),
                      rect.Left,
                      -rect.Top,
                      rect.Right,
                      -rect.Bottom)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            cnt += 1
        Next
        GL.Disable(EnableCap.Blend)
        TextRenderShader.StopUse()
        GL.BindTextureUnit(0, 0)
    End Sub
End Module

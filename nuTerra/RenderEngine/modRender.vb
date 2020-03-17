Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Dim temp_timer As New Stopwatch
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single

    Public Sub draw_scene()
        '===========================================================================
        ' FLAG INFO
        ' 0  = No shading
        ' 64  = model 
        ' 
        ' 255 = sky dome. We will want to control brightness
        ' more as they are added
        '===========================================================================
        'house keeping
        FRAME_TIMER.Restart()
        '===========================================================================

        frmMain.glControl_main.MakeCurrent()
        '===========================================================================
        If SHOW_MAPS_SCREEN Then
            gl_pick_map(MOUSE.X, MOUSE.Y)
            Return
        End If
        If SHOW_LOADING_SCREEN Then
            draw_loading_screen()
            Return
        End If
        '===========================================================================

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) '================
        '===========================================================================

        '===========================================================================
        set_prespective_view() ' <-- sets camera and prespective view ==============
        '===========================================================================

        If MODELS_LOADED Then
            '=======================================================================
            frustum_cull() '========================================================
            '=======================================================================
        End If

        '===========================================================================
        FBOm.attach_CNGP() 'clear ALL gTextures!
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        '===========================================================================
        Draw_SkyDome() '============================================================
        '===========================================================================

        '===========================================================================
        'GL States 
        GL.Enable(EnableCap.DepthTest)
        '===========================================================================

        '===========================================================================
        Draw_Light_Orb() '==========================================================
        '===========================================================================
        FBOm.attach_CNGP()

        If TERRAIN_LOADED Then
            '=======================================================================
            draw_terrain() '========================================================
            '=======================================================================
        End If

        '===========================================================================
        draw_terrain_grids() '======================================================
        '===========================================================================


        'setup for projection before drawing
        FBOm.attach_C_no_Depth()
        GL.DepthMask(False)
        GL.FrontFace(FrontFaceDirection.Cw)
        GL.Enable(EnableCap.Blend)
        GL.Enable(EnableCap.CullFace)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        '===========================================================================
        draw_map_cursor() '=========================================================
        '===========================================================================

        '===========================================================================
        draw_terrain_base_rings() '=================================================
        '===========================================================================
        'restore settings after projected objects are drawn
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL.Disable(EnableCap.CullFace)
        FBOm.attach_Depth()
        GL.FrontFace(FrontFaceDirection.Ccw)

        If MODELS_LOADED Then
            '=======================================================================
            draw_models() '=========================================================
            '=======================================================================

            '=======================================================================
            draw_overlays() '=======================================================
            '=======================================================================
        End If

        '===========================================================================
        draw_cross_hair() '=========================================================
        '===========================================================================

        '===========================================================================
        '================== Deferred Rendering, HUD and MINI MAP ===================
        '===========================================================================

        'We can now switch to the default hardware buffer.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        'house keeping
        '===========================================================================
        GL.Disable(EnableCap.DepthTest)
        GL.Clear(ClearBufferMask.ColorBufferBit)
        '===========================================================================

        Ortho_main()

        '===========================================================================
        render_deferred_buffers() '=================================================
        '===========================================================================

        '===========================================================================
        'render_test_compute() '====================================================
        '===========================================================================

        '===========================================================================
        render_HUD() '==============================================================
        '===========================================================================

        '===========================================================================
        'This has to be called last. It changes the PROJECTMATRIX and VIEWMATRIX
        draw_mini_map() '===========================================================
        '===========================================================================

        '===========================================================================
        If _STARTED Then frmMain.glControl_main.SwapBuffers() '=====================
        '===========================================================================
        If frmGbufferViewer IsNot Nothing Then
            If frmGbufferViewer.Visible Then
                frmGbufferViewer.update_screen()
            End If
        End If

        FPS_COUNTER += 1

    End Sub

    Private Sub frustum_cull()
        cullShader.Use()

        GL.UniformMatrix4(cullShader("projection"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(cullShader("view"), False, VIEWMATRIX)

        ' TODO: pass visbox here
        GL.Uniform3(cullShader("ObjectExtent"), 0.5F, 0.5F, 0.5F)

        GL.Enable(EnableCap.RasterizerDiscard)

        For Each batch In MODEL_BATCH_LIST
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, batch.culledInstanceDataBO)
            GL.BindVertexArray(batch.cullVA)

            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points)
            GL.BeginQuery(QueryTarget.PrimitivesGenerated, batch.culledQuery)
            GL.DrawArrays(PrimitiveType.Points, 0, batch.count)
            GL.EndQuery(QueryTarget.PrimitivesGenerated)
            GL.EndTransformFeedback()

            GL.Flush()
        Next

        GL.Disable(EnableCap.RasterizerDiscard)
        cullShader.StopUse()
    End Sub

    Private Sub render_test_compute()

        Dim maxComputeWorkGroupCount As Integer
        Dim maxComputeWorkGroupsize As Integer

        GL.GetInteger(DirectCast(All.MaxComputeWorkGroupCount, GetIndexedPName), 0, maxComputeWorkGroupCount)
        GL.GetInteger(DirectCast(All.MaxComputeWorkGroupSize, GetIndexedPName), 0, maxComputeWorkGroupsize)

        Dim er0 = GL.GetError

        testShader.Use()
        GL.DispatchCompute(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT, 1)
        testShader.StopUse()

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit)
        Dim er1 = GL.GetError

        draw_image_rectangle(New RectangleF(0.0F, 0.0F, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT),
                             TEST_TEXTURE_ID)

        Dim er3 = GL.GetError

    End Sub

    Private Sub draw_terrain()
        If WIRE_TERRAIN Then
            GL.PolygonOffset(1.2, 0.2)
            GL.Enable(EnableCap.PolygonOffsetFill) '<-- Needed for wire overlay
        End If
        '==========================
        'debug
        FBOm.attach_C()
        GL.Enable(EnableCap.Blend)
        '==========================

        GL.Enable(EnableCap.CullFace)

        '------------------------------------------------
        TerrainShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        'shit load of textures to bind
        GL.Uniform1(TerrainShader("layer_1T1"), 0)
        GL.Uniform1(TerrainShader("layer_2T1"), 1)
        GL.Uniform1(TerrainShader("layer_3T1"), 2)
        GL.Uniform1(TerrainShader("layer_4T1"), 3)

        GL.Uniform1(TerrainShader("layer_1T2"), 4)
        GL.Uniform1(TerrainShader("layer_2T2"), 5)
        GL.Uniform1(TerrainShader("layer_3T2"), 6)
        GL.Uniform1(TerrainShader("layer_4T2"), 7)

        GL.Uniform1(TerrainShader("n_layer_1T1"), 8)
        GL.Uniform1(TerrainShader("n_layer_2T1"), 9)
        GL.Uniform1(TerrainShader("n_layer_3T1"), 10)
        GL.Uniform1(TerrainShader("n_layer_4T1"), 11)

        GL.Uniform1(TerrainShader("n_layer_1T2"), 12)
        GL.Uniform1(TerrainShader("n_layer_2T2"), 13)
        GL.Uniform1(TerrainShader("n_layer_3T2"), 14)
        GL.Uniform1(TerrainShader("n_layer_4T2"), 15)


        GL.Uniform1(TerrainShader("mixtexture1"), 16)
        GL.Uniform1(TerrainShader("mixtexture2"), 17)
        GL.Uniform1(TerrainShader("mixtexture3"), 18)
        GL.Uniform1(TerrainShader("mixtexture4"), 19)


        GL.Uniform1(TerrainShader("colorMap"), 20)
        GL.Uniform1(TerrainShader("normalMap"), 21)
        GL.Uniform1(TerrainShader("domTexture"), 22)


        GL.Uniform1(TerrainShader("tex_0"), 23)
        GL.Uniform1(TerrainShader("tex_1"), 24)

        GL.Uniform1(TerrainShader("tex_2"), 25)
        GL.Uniform1(TerrainShader("tex_3"), 26)

        GL.Uniform1(TerrainShader("tex_4"), 27)
        GL.Uniform1(TerrainShader("tex_5"), 28)

        GL.Uniform1(TerrainShader("tex_6"), 29)
        GL.Uniform1(TerrainShader("tex_7"), 30)

        GL.BindTextureUnit(20, theMap.GLOBAL_AM_ID) '<----------------- Texture Bind
        GL.BindTextureUnit(21, m_normal_id)


        GL.Uniform1(TerrainShader("nMap_type"), N_MAP_TYPE)
        GL.Uniform2(TerrainShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(TerrainShader("map_center"), -b_x_min, b_y_max)
        GL.Uniform3(TerrainShader("cam_position"), CAM_POSITION.X, CAM_POSITION.Y, CAM_POSITION.Z)

        GL.UniformMatrix4(TerrainShader("projMatrix"), False, PROJECTIONMATRIX)

        For i = 0 To theMap.render_set.Length - 1
            GL.UniformMatrix4(TerrainShader("modelMatrix"), False, theMap.render_set(i).matrix)
            GL.UniformMatrix4(TerrainShader("viewMatrix"), False, VIEWMATRIX)

            GL.UniformMatrix3(TerrainShader("normalMatrix"), True, Matrix3.Invert(New Matrix3(VIEWMATRIX * theMap.render_set(i).matrix)))
            GL.Uniform2(TerrainShader("me_location"), theMap.chunks(i).location.X, theMap.chunks(i).location.Y)

            'debug shit
            GL.BindTextureUnit(22, theMap.render_set(i).dom_texture_id) '<----------------- Texture Bind

            'bind all the data for this chunk
            With theMap.render_set(i)
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, .layersStd140_ubo)

                'AM maps
                GL.BindTextureUnit(0, .TexLayers(0).AM_id1)
                GL.BindTextureUnit(1, .TexLayers(1).AM_id1)
                GL.BindTextureUnit(2, .TexLayers(2).AM_id1)
                GL.BindTextureUnit(3, .TexLayers(3).AM_id1)

                GL.BindTextureUnit(4, .TexLayers(0).AM_id2)
                GL.BindTextureUnit(5, .TexLayers(1).AM_id2)
                GL.BindTextureUnit(6, .TexLayers(2).AM_id2)
                GL.BindTextureUnit(7, .TexLayers(3).AM_id2)
                'NM maps
                GL.BindTextureUnit(8, .TexLayers(0).NM_id1)
                GL.BindTextureUnit(9, .TexLayers(1).NM_id1)
                GL.BindTextureUnit(10, .TexLayers(2).NM_id1)
                GL.BindTextureUnit(11, .TexLayers(3).NM_id1)

                GL.BindTextureUnit(12, .TexLayers(0).NM_id2)
                GL.BindTextureUnit(13, .TexLayers(1).NM_id2)
                GL.BindTextureUnit(14, .TexLayers(2).NM_id2)
                GL.BindTextureUnit(15, .TexLayers(3).NM_id2)
                'bind blend textures
                GL.BindTextureUnit(16, .TexLayers(0).Blend_id)
                GL.BindTextureUnit(17, .TexLayers(1).Blend_id)
                GL.BindTextureUnit(18, .TexLayers(2).Blend_id)
                GL.BindTextureUnit(19, .TexLayers(3).Blend_id)

                'test textures so we cab see the mapping
                GL.BindTextureUnit(23, TEST_IDS(0))
                GL.BindTextureUnit(24, TEST_IDS(1))
                GL.BindTextureUnit(25, TEST_IDS(2))
                GL.BindTextureUnit(26, TEST_IDS(3))
                GL.BindTextureUnit(27, TEST_IDS(4))
                GL.BindTextureUnit(28, TEST_IDS(5))
                GL.BindTextureUnit(29, TEST_IDS(6))
                GL.BindTextureUnit(30, TEST_IDS(7))

            End With


            'draw chunk
            GL.BindVertexArray(theMap.render_set(i).VAO)
            GL.DrawElements(PrimitiveType.Triangles,
                24576,
                DrawElementsType.UnsignedShort, 0)
        Next

        TerrainShader.StopUse()

        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        unbind_textures(30)

        If WIRE_TERRAIN Then


            'Must have this Identity to use the terrain normal view shader.
            Dim viewM = Matrix4.Identity * VIEWMATRIX

            GL.Disable(EnableCap.PolygonOffsetFill)

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            FBOm.attach_CF()

            TerrainNormals.Use()


            GL.Uniform1(TerrainNormals("prj_length"), 1.0F)
            GL.Uniform1(TerrainNormals("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(TerrainNormals("show_wireframe"), CInt(WIRE_TERRAIN))

            GL.UniformMatrix4(TerrainNormals("projection"), False, PROJECTIONMATRIX)
            GL.UniformMatrix4(TerrainNormals("view"), False, VIEWMATRIX)

            For i = 0 To theMap.render_set.Length - 1

                Dim model = theMap.render_set(i).matrix

                GL.UniformMatrix4(TerrainNormals("model"), False, model)

                GL.BindVertexArray(theMap.render_set(i).VAO)

                'draw chunk wire
                GL.DrawElements(PrimitiveType.Triangles,
                        24576,
                        DrawElementsType.UnsignedShort, 0)

            Next

            TerrainNormals.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)


        End If
    End Sub

    Private Sub draw_models()
        'SOLID FILL
        FBOm.attach_CNGP()
        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
            GL.PolygonOffset(1.2, 0.2)
            GL.Enable(EnableCap.PolygonOffsetFill) '<-- Needed for wire overlay
        End If
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.BindTextureUnit(0, m_color_id)
        GL.Uniform1(modelShader("colorMap"), 0)
        GL.BindTextureUnit(1, m_normal_id)
        GL.Uniform1(modelShader("normalMap"), 1)
        GL.BindTextureUnit(2, m_gmm_id)
        GL.Uniform1(modelShader("GMF_Map"), 2)
        GL.Uniform1(modelShader("nMap_type"), N_MAP_TYPE)

        GL.UniformMatrix4(modelShader("projection"), False, PROJECTIONMATRIX)

        GL.Enable(EnableCap.CullFace)
        TOTAL_TRIANGLES_DRAWN = 0
        PRIMS_CULLED = 0

        For Each batch In MODEL_BATCH_LIST
            Dim model = MAP_MODELS(batch.model_id).mdl

            If model.junk Then
                Continue For
            End If

            For Each renderSet In model.render_sets
                If renderSet.no_draw Then
                    Continue For
                End If

                'Debug.Assert(renderSet.primitiveGroups.Count > 0)

                GL.GetQueryObject(batch.culledQuery, GetQueryObjectParam.QueryResult, batch.visibleCount)
                'Debug.Assert(batch.visibleCount <= batch.count)

                PRIMS_CULLED += batch.count - batch.visibleCount

                If batch.visibleCount = 0 Then
                    Continue For
                End If

                GL.BindVertexArray(renderSet.mdl_VAO)

                Dim triType = If(renderSet.indexSize = 2, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                For Each primGroup In renderSet.primitiveGroups.Values
                    If primGroup.no_draw Then
                        Continue For
                    End If

                    TOTAL_TRIANGLES_DRAWN += primGroup.nPrimitives * batch.visibleCount
                    'setup materials here

                    GL.DrawElementsInstanced(PrimitiveType.Triangles,
                                             primGroup.nPrimitives * 3,
                                             triType,
                                             New IntPtr(primGroup.startIndex * renderSet.indexSize),
                                             batch.visibleCount)
                Next
            Next
        Next
        GL.Disable(EnableCap.CullFace)

        modelShader.StopUse()
        unbind_textures(2) ' unbind all the used texture slots

        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
            GL.Disable(EnableCap.PolygonOffsetFill) '<-- Needed for wire overlay
        End If
    End Sub

    Private Sub draw_terrain_grids()
        If (SHOW_BORDER + SHOW_CHUNKS + SHOW_GRID) = 0 Then
            Return
        End If

        FBOm.attach_CF()
        GL.DepthMask(False)
        GL.Disable(EnableCap.DepthTest)
        TerrainGrids.Use()
        GL.Uniform2(TerrainGrids("bb_tr"), MAP_BB_UR.X, MAP_BB_UR.Y)
        GL.Uniform2(TerrainGrids("bb_bl"), MAP_BB_BL.X, MAP_BB_BL.Y)
        GL.Uniform1(TerrainGrids("g_size"), PLAYER_FIELD_CELL_SIZE)

        GL.Uniform1(TerrainGrids("show_border"), SHOW_BORDER)
        GL.Uniform1(TerrainGrids("show_chunks"), SHOW_CHUNKS)
        GL.Uniform1(TerrainGrids("show_grid"), SHOW_GRID)

        GL.UniformMatrix4(TerrainGrids("projection"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(TerrainGrids("view"), False, VIEWMATRIX)

        For i = 0 To theMap.render_set.Length - 1
            GL.UniformMatrix4(TerrainGrids("model"), False, theMap.render_set(i).matrix)

            'draw chunk
            GL.BindVertexArray(theMap.render_set(i).VAO)
            GL.DrawElements(PrimitiveType.Triangles,
                24576,
                DrawElementsType.UnsignedShort, 0)
        Next
        TerrainGrids.StopUse()

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)

    End Sub

    Private Sub render_deferred_buffers()
        '===========================================================================
        ' Test our deferred shader =================================================
        '===========================================================================

        deferredShader.Use()

        'set up uniforms
        GL.BindTextureUnit(0, FBOm.gColor)
        GL.Uniform1(deferredShader("gColor"), 0)
        GL.BindTextureUnit(1, FBOm.gNormal)
        GL.Uniform1(deferredShader("gNormal"), 1)
        GL.BindTextureUnit(2, FBOm.gGMF)
        GL.Uniform1(deferredShader("gGMF"), 2) ' ignore this for now
        GL.BindTextureUnit(3, FBOm.gPosition)
        GL.Uniform1(deferredShader("gPosition"), 3)
        ' GL.BindTextureUnit(4, FBOm.gDepth)
        'GL.Uniform1(deferredShader("gDepth"), 4)

        'Lighting settings
        GL.Uniform1(deferredShader("AMBIENT"), frmLighting.lighting_ambient)
        GL.Uniform1(deferredShader("BRIGHTNESS"), frmLighting.lighting_terrain_texture)
        GL.Uniform1(deferredShader("SPECULAR"), frmLighting.lighting_specular_level)
        GL.Uniform1(deferredShader("GRAY_LEVEL"), frmLighting.lighting_gray_level)
        GL.Uniform1(deferredShader("GAMMA_LEVEL"), frmLighting.lighting_gamma_level)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, MODELVIEWMATRIX_Saved)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(4) ' unbind all the used texture slots

        deferredShader.StopUse()
    End Sub

    ''' <summary>
    ''' renders all 2D things in ortho mode
    ''' </summary>
    ''' 
    Private Sub render_HUD()
        temp_timer.Restart()
        '===========================================================================
        ' Text Rendering ===========================================================
        '===========================================================================

        Dim position = PointF.Empty
        DrawText.clear(Color.FromArgb(0, 0, 0, 255))

        'save this.. we may want to use it for debug with a different source for the values.
        'Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")
        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds
        Dim tr = TOTAL_TRIANGLES_DRAWN

        Dim txt = String.Format("Culled: {0} | FPS: {1} | Triangles drawn per frame: {2} | Draw time in Milliseconds: {3}", PRIMS_CULLED, FPS_TIME, tr, elapsed)
        'debug shit
        'txt = String.Format("mouse {0} {1}", MINI_WORLD_MOUSE_POSITION.X.ToString, MINI_WORLD_MOUSE_POSITION.Y.ToString)
        'txt = String.Format("HX {0} : HY {1}", HX, HY)
        DrawText.DrawString(txt, mono, Brushes.White, position)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        draw_image_rectangle(New RectangleF(0, 0, FBOm.SCR_WIDTH, 20), DrawText.Gettexture)

        GL.Disable(EnableCap.Blend)
        Dim temp_time = temp_timer.ElapsedMilliseconds
        Dim aa As Integer = 0
        '===========================================================================
        ' Text Rendering End =======================================================
        '===========================================================================

    End Sub

#Region "miniMap"

    Private Sub draw_mini_map()
        'check if we have the mini map loaded.
        If theMap.MINI_MAP_ID = 0 Then
            Return
        End If
        GL.DepthMask(False)
        GL.Disable(EnableCap.DepthTest)

        ' Animate map growth
        'need to control this so it is not affected by frame rate!
        Dim s = CInt(150 * DELTA_TIME)
        If MINI_MAP_SIZE <> MINI_MAP_NEW_SIZE Then
            If MINI_MAP_SIZE < MINI_MAP_NEW_SIZE Then
                MINI_MAP_SIZE += s
                If MINI_MAP_SIZE > MINI_MAP_NEW_SIZE Then
                    MINI_MAP_SIZE = MINI_MAP_NEW_SIZE
                End If
            Else
                If MINI_MAP_SIZE > MINI_MAP_NEW_SIZE Then
                    MINI_MAP_SIZE -= s
                    If MINI_MAP_SIZE < MINI_MAP_NEW_SIZE Then
                        MINI_MAP_SIZE = MINI_MAP_NEW_SIZE
                    End If
                End If
            End If
            'sized changed so we must resize the FBOmini
            FBOmini.FBO_Initialize(MINI_MAP_SIZE)
        Else
        End If

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, miniFBO) '================
        '===========================================================================

        Ortho_MiniMap(MINI_MAP_SIZE)

        GL.ClearColor(0.0, 0.0, 0.5, 0.0)
        GL.Clear(ClearBufferMask.ColorBufferBit)
        Draw_mini()

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
        '===========================================================================
        Ortho_main()
        Dim size = frmMain.glControl_main.Size
        Dim cx = size.Width - MINI_MAP_SIZE
        Dim cy = size.Height - MINI_MAP_SIZE
        draw_image_rectangle(New RectangleF(cx, cy,
                                                MINI_MAP_SIZE, MINI_MAP_SIZE),
                                                FBOmini.gColor)

        '=======================================================================
        'draw mini map legends
        '=======================================================================
        'setup
        GL.Enable(EnableCap.Blend)
        miniLegends.Use()
        GL.Uniform1(miniLegends("imageMap"), 0)
        GL.UniformMatrix4(miniLegends("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(miniLegends("divisor"), 1.0F) 'tile factor
        GL.Uniform1(miniLegends("index"), 0.0F)

        '=======================================================================
        'draw horz trim
        GL.BindTextureUnit(0, MINI_TRIM_HORZ_ID)
        Dim rect As New RectangleF(cx - 12, cy - 12, 640 + 12, 16.0F)
        GL.Uniform4(miniLegends("rect"),
                  rect.Left,
                  -rect.Top,
                  rect.Right,
                  -rect.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'draw vert trim
        GL.BindTextureUnit(0, MINI_TRIM_VERT_ID)
        rect = New RectangleF(cx - 12, cy - 12, 16.0F, 640 + 12.0F)
        GL.Uniform4(miniLegends("rect"),
                 rect.Left,
                 -rect.Top,
                 rect.Right,
                 -rect.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        '=======================================================================

        'row
        '=======================================================================
        GL.Uniform1(miniLegends("divisor"), 10.0F) 'tile factor

        GL.Uniform1(miniLegends("col_row"), 1) 'draw row
        GL.BindTextureUnit(0, MINI_NUMBERS_ID)

        Dim index! = 0
        Dim cnt! = 10.0F
        Dim step_s! = MINI_MAP_SIZE / 10.0F
        For xp = cx To cx + MINI_MAP_SIZE Step step_s
            GL.Uniform1(miniLegends("index"), index)

            rect = New RectangleF(xp + (step_s / 2.0F) - 8, cy - 11, 16.0F, 10.0F)
            GL.Uniform4(miniLegends("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
            Application.DoEvents()
        Next
        'column
        '=======================================================================
        index = 0
        GL.Uniform1(miniLegends("col_row"), 0) 'draw row
        GL.BindTextureUnit(0, MINI_LETTERS_ID)

        cnt! = 10.0F
        step_s! = MINI_MAP_SIZE / 10.0F
        For yp = cy To cy + MINI_MAP_SIZE Step step_s
            GL.Uniform1(miniLegends("index"), index)

            rect = New RectangleF(cx - 9, yp + (step_s / 2) - 6, 8.0F, 12.0F)
            GL.Uniform4(miniLegends("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
            Application.DoEvents()
        Next
        miniLegends.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

    End Sub
    Private Sub Draw_mini()

        '======================================================
        'Draw all the shit on top of this image
        draw_minimap_texture()
        '======================================================

        GL.Enable(EnableCap.Blend)

        '======================================================
        draw_mini_base_rings()
        '======================================================

        '======================================================
        draw_mini_base_ids()
        '======================================================

        '======================================================
        draw_mini_grids_lines()
        '======================================================

        '======================================================
        draw_mini_position()
        '======================================================

        GL.Disable(EnableCap.Blend)

        '======================================================
        get_world_Position_In_Minimap_Window(M_POS)
        '======================================================

    End Sub

    Private Sub draw_minimap_texture()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        draw_image_rectangle(New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y,
                                           -w, -h),
                                            theMap.MINI_MAP_ID)
    End Sub

    Private Sub draw_mini_base_ids()


        'need to scale with the map
        Dim i_size = 30.0F

        Dim pos_t1 As New RectangleF(-TEAM_1.X + i_size, -TEAM_1.Z - i_size, -i_size * 2, i_size * 2)
        Dim pos_t2 As New RectangleF(-TEAM_2.X + i_size, -TEAM_2.Z - i_size, -i_size * 2, i_size * 2)

        image2dShader.Use()

        GL.Uniform1(image2dShader("imageMap"), 0)

        'Icon 1
        GL.BindTextureUnit(0, TEAM_1_ICON_ID)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t1.Left,
            pos_t1.Top,
            pos_t1.Right,
            pos_t1.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Icon 2
        GL.BindTextureUnit(0, TEAM_2_ICON_ID)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t2.Left,
            pos_t2.Top,
            pos_t2.Right,
            pos_t2.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Reset
        GL.BindTextureUnit(0, 0)
        image2dShader.StopUse()

    End Sub


    Private Sub draw_mini_base_rings()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        'draw base rings
        MiniMapRingsShader.Use()
        'constants
        Dim er0 = GL.GetError
        GL.UniformMatrix4(MiniMapRingsShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(MiniMapRingsShader("radius"), 50.0F)
        GL.Uniform1(MiniMapRingsShader("thickness"), 2.5F)

        Dim m_size = New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)

        GL.Uniform4(MiniMapRingsShader("rect"),
            m_size.Left,
            -m_size.Top,
            m_size.Right,
            -m_size.Bottom)

        GL.Uniform2(MiniMapRingsShader("center"), TEAM_2.X, TEAM_2.Z)
        GL.Uniform4(MiniMapRingsShader("color"), OpenTK.Graphics.Color4.DarkRed)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        GL.Uniform2(MiniMapRingsShader("center"), TEAM_1.X, TEAM_1.Z)
        GL.Uniform4(MiniMapRingsShader("color"), OpenTK.Graphics.Color4.DarkGreen)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        MiniMapRingsShader.StopUse()

    End Sub

    Private Sub draw_mini_position()

        image2dShader.Use()

        GL.Uniform1(image2dShader("imageMap"), 0)
        Dim i_size = 32
        Dim pos As New RectangleF(-i_size, -i_size, i_size * 2, i_size * 2)

        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, -U_LOOK_AT_Z, 0.0F)
        Dim model_R = Matrix4.CreateRotationZ(U_CAM_X_ANGLE)
        Dim modelMatrix = model_R * model_X

        GL.BindTextureUnit(0, DIRECTION_TEXTURE_ID)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, modelMatrix * PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos.Left,
            pos.Top,
            pos.Right,
            pos.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        image2dShader.StopUse()

        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)

        'draw ring around pointer 
        MiniMapRingsShader.Use()
        'constants
        Dim er0 = GL.GetError
        GL.UniformMatrix4(MiniMapRingsShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(MiniMapRingsShader("radius"), 40.0F)
        GL.Uniform1(MiniMapRingsShader("thickness"), 3.0F)
        Dim er3 = GL.GetError

        Dim m_size = New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)

        Dim er1 = GL.GetError
        GL.Uniform4(MiniMapRingsShader("rect"),
            m_size.Left,
            -m_size.Top,
            m_size.Right,
            -m_size.Bottom)

        GL.Uniform2(MiniMapRingsShader("center"), -U_LOOK_AT_X, U_LOOK_AT_Z)
        GL.Uniform4(MiniMapRingsShader("color"), OpenTK.Graphics.Color4.White)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)


        MiniMapRingsShader.StopUse()

    End Sub

    Private Sub draw_mini_grids_lines()

        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)

        coloredline2dShader.Use()

        Dim co As OpenTK.Graphics.Color4
        co = OpenTK.Graphics.Color4.GhostWhite
        co.A = 0.5F ' tone down the brightness some

        GL.UniformMatrix4(coloredline2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(coloredline2dShader("color"), co)
        'vertical lines
        Dim step_size! = w / 10

        For x = MAP_BB_BL.X + step_size! To MAP_BB_UR.X - step_size! Step step_size!
            Dim pos As New RectangleF(x - 0.5, MAP_BB_BL.Y, 0.0F, h)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)

            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next
        'horizonal lines
        For y = MAP_BB_BL.Y + step_size! To MAP_BB_UR.Y - step_size! Step step_size!
            Dim pos As New RectangleF(MAP_BB_BL.X - 0.5, y, w, 0.0F)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)
            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next

        coloredline2dShader.StopUse()

    End Sub

    Private Sub get_world_Position_In_Minimap_Window(ByRef pos As Vector2)
        MINI_MOUSE_CAPTURED = False

        Dim left = FBOm.SCR_WIDTH - MINI_MAP_SIZE
        Dim top = FBOm.SCR_HEIGHT - MINI_MAP_SIZE
        'Are we over the minimap?
        If M_MOUSE.X < left Then Return
        If M_MOUSE.Y < top Then Return


        pos.X = ((M_MOUSE.X - left) / MINI_MAP_SIZE) * 2.0 - 1.0
        pos.Y = ((M_MOUSE.Y - top) / MINI_MAP_SIZE) * 2.0 - 1.0
        Dim pos_v = New Vector4(pos.X, pos.Y, 0.0F, 0.0F)
        Dim world = UnProject(pos_v)
        MINI_WORLD_MOUSE_POSITION.X = world.X
        MINI_WORLD_MOUSE_POSITION.Y = -world.Y
        MINI_MOUSE_CAPTURED = True
        Return
    End Sub
#End Region

    Private Sub Draw_SkyDome()
        If Not TERRAIN_LOADED Then Return
        FBOm.attach_CNGP()
        SkyDomeShader.Use()
        GL.Enable(EnableCap.CullFace)
        Dim model = Matrix4.CreateTranslation(CAM_POSITION.X, CAM_POSITION.Y + 0, CAM_POSITION.Z)
        GL.UniformMatrix4(SkyDomeShader("mvp"), False, model * VIEWMATRIX * PROJECTIONMATRIX)
        GL.Uniform1(SkyDomeShader("imageMap"), 0)

        GL.BindTextureUnit(0, theMap.Sky_Texture_Id)

        GL.BindVertexArray(theMap.skybox_mdl.mdl_VAO)
        GL.DrawElements(PrimitiveType.Triangles,
                        theMap.skybox_mdl.indice_count * 3,
                        DrawElementsType.UnsignedShort,
                        0)
        SkyDomeShader.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.Disable(EnableCap.CullFace)
    End Sub

    Private Sub draw_overlays()
        If WIRE_MODELS Then
            GL.Disable(EnableCap.PolygonOffsetFill)

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            FBOm.attach_CF()

            normalShader.Use()

            GL.UniformMatrix4(normalShader("projection"), False, PROJECTIONMATRIX)

            GL.Uniform1(normalShader("prj_length"), 0.1F)
            GL.Uniform1(normalShader("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(normalShader("show_wireframe"), CInt(WIRE_MODELS))

            For Each batch In MODEL_BATCH_LIST
                Dim model = MAP_MODELS(batch.model_id).mdl

                If model.junk Then
                    Continue For
                End If

                For Each renderSet In model.render_sets
                    If renderSet.no_draw Then
                        Continue For
                    End If

                    If batch.visibleCount = 0 Then
                        Continue For
                    End If

                    GL.BindVertexArray(renderSet.mdl_VAO)
                    Dim triType = If(renderSet.indexSize = 2, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                    For Each primGroup In renderSet.primitiveGroups.Values
                        If primGroup.no_draw Then
                            Continue For
                        End If

                        GL.DrawElementsInstanced(PrimitiveType.Triangles,
                                                 primGroup.nPrimitives * 3,
                                                 triType,
                                                 New IntPtr(primGroup.startIndex * renderSet.indexSize),
                                                 batch.visibleCount)
                    Next
                Next
            Next
            normalShader.StopUse()
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        End If
    End Sub

    Private Sub Draw_Light_Orb()
        'Dont draw if not told to
        If Not frmMain.m_show_light_pos.Checked Then
            Return
        End If
        FBOm.attach_CF()

        Dim model = Matrix4.CreateTranslation(LIGHT_POS.X, LIGHT_POS.Y, LIGHT_POS.Z)

        Dim scale_ As Single = 30.0
        Dim sMat = Matrix4.CreateScale(scale_)

        Dim MVPM = sMat * model * VIEWMATRIX * PROJECTIONMATRIX
        colorOnlyShader.Use()

        GL.Uniform3(colorOnlyShader("color"), 1.0F, 1.0F, 0.0F)

        GL.UniformMatrix4(colorOnlyShader("ProjectionMatrix"), False, MVPM)

        GL.BindVertexArray(MOON.mdl_VAO)
        GL.DrawElements(PrimitiveType.Triangles,
                        MOON.indice_count * 3,
                        DrawElementsType.UnsignedShort,
                        0)
        ' GL.BindVertexArray(0)

        colorOnlyShader.StopUse()
    End Sub

    Private Sub draw_map_cursor()

        If Not SHOW_CURSOR Then Return

        DecalProject.Use()


        GL.Uniform3(DecalProject("color_in"), 0.4F, 0.3F, 0.3F)

        GL.Uniform1(DecalProject("depthMap"), 0)
        GL.Uniform1(DecalProject("gFlag"), 1)
        GL.Uniform1(DecalProject("colorMap"), 2)

        GL.BindTextureUnit(0, FBOm.gDepth)
        GL.BindTextureUnit(1, FBOm.gGMF)
        GL.BindTextureUnit(2, CURSOR_TEXTURE_ID)

        ' Track the terrain at Y
        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, CURSOR_Y, U_LOOK_AT_Z)
        Dim model_S = Matrix4.CreateScale(25.0F, 50.0F, 25.0F)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DecalProject("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(DecalProject("ViewMatrix"), False, VIEWMATRIX)
        GL.UniformMatrix4(DecalProject("DecalMatrix"), False, rotate * model_S * model_X)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36)

        DecalProject.StopUse()
        unbind_textures(2)
    End Sub

    Private Sub draw_cross_hair()
        If MOVE_MOD Or Z_MOVE Then
            If MOVE_MOD And Not Z_MOVE Then
                frmMain.glControl_main.Cursor = Cursors.SizeAll
            End If
            If Z_MOVE Then
                frmMain.glControl_main.Cursor = Cursors.SizeNS
            End If
            FBOm.attach_CF()
            ObjectRenderers.draw_cross_hair()
        Else
            frmMain.glControl_main.Cursor = Cursors.Default
        End If
        '==============================================================


    End Sub

    Private Sub draw_terrain_base_rings()

        FBOm.attach_C_no_Depth()

        BaseRingProjector.Use()

        GL.Uniform1(BaseRingProjector("depthMap"), 0)
        GL.BindTextureUnit(0, FBOm.gDepth)

        'constants
        GL.UniformMatrix4(BaseRingProjector("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(BaseRingProjector("ViewMatrix"), False, VIEWMATRIX)
        GL.Uniform1(BaseRingProjector("radius"), 50.0F)
        GL.Uniform1(BaseRingProjector("thickness"), 2.0F)
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        Dim scale = Matrix4.CreateScale(120.0F, 25.0F, 120.0F)

        ' base 1 ring
        Dim model_X = Matrix4.CreateTranslation(-TEAM_1.X, T1_Y, TEAM_1.Z)
        GL.Uniform3(BaseRingProjector("ring_center"), -TEAM_1.X, TEAM_1.Y, TEAM_1.Z)
        GL.UniformMatrix4(BaseRingProjector("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjector("color"), OpenTK.Graphics.Color4.Green)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36)

        'base 2 ring
        model_X = Matrix4.CreateTranslation(-TEAM_2.X, T2_Y, TEAM_2.Z)
        GL.Uniform3(BaseRingProjector("ring_center"), -TEAM_2.X, TEAM_2.Y, TEAM_2.Z)
        GL.UniformMatrix4(BaseRingProjector("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjector("color"), OpenTK.Graphics.Color4.Red)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36)

        BaseRingProjector.StopUse()
        GL.BindTextureUnit(0, 0)


    End Sub

    ''' <summary>
    ''' Unbinds textures from last used to zero
    ''' </summary>
    Private Sub unbind_textures(ByVal start As Integer)
        'doing this backwards leaves TEXTURE0 active :)
        For i = start To 0 Step -1
            GL.BindTextureUnit(i, 0)
        Next
    End Sub

    Private Sub draw_main_Quad(w As Integer, h As Integer)
        GL.Uniform4(deferredShader("rect"), 0.0F, CSng(-h), CSng(w), 0.0F)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        ' GL.BindVertexArray(0)
    End Sub

End Module

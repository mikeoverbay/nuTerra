Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Dim temp_timer As New Stopwatch
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Private cull_timer As New Stopwatch
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

        'frmMain.glControl_main.MakeCurrent()
        frmMain.glControl_main.Context.MakeCurrent(frmMain.glControl_main.WindowInfo)

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

        '===========================================================================
        CULLED_COUNT = 0
        cull_timer.Restart()
        If TERRAIN_LOADED And DONT_BLOCK_TERRAIN Then
            ExtractFrustum()
            cull_terrain()
        End If
        '===========================================================================

        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            '=======================================================================
            frustum_cull() '========================================================
            '=======================================================================
        End If
        cull_timer.Stop()

        '===========================================================================
        FBOm.attach_CNGPA() 'clear ALL gTextures!
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        If TERRAIN_LOADED And DONT_BLOCK_SKY Then Draw_SkyDome()
        '===========================================================================
        'GL States 
        GL.Enable(EnableCap.DepthTest)
        '===========================================================================

        FBOm.attach_CNGP()

        If TERRAIN_LOADED And DONT_BLOCK_TERRAIN Then
            '=======================================================================
            draw_terrain() '========================================================
            '=======================================================================
            If (SHOW_BORDER + SHOW_CHUNKS + SHOW_GRID) > 0 Then draw_terrain_grids()
            '=======================================================================
            'setup for projection before drawing
            FBOm.attach_C_no_Depth()
            GL.DepthMask(False)
            GL.FrontFace(FrontFaceDirection.Cw)
            GL.Enable(EnableCap.Blend)
            GL.Enable(EnableCap.CullFace)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
            '=======================================================================
            If SHOW_CURSOR Then draw_map_cursor() '=================================
            '=======================================================================

            '=======================================================================
            draw_terrain_base_rings() '=============================================
            '=======================================================================
            'restore settings after projected objects are drawn
            GL.Disable(EnableCap.Blend)
            GL.DepthMask(True)
            GL.Disable(EnableCap.CullFace)
            FBOm.attach_Depth()
            GL.FrontFace(FrontFaceDirection.Ccw)
        End If


        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            '=======================================================================
            draw_models() '=========================================================
            '=======================================================================
        End If

        '===========================================================================
        'draw sun
        FBOm.attach_C()
        If TERRAIN_LOADED Then draw_sun()
        '===========================================================================

        '===========================================================================
        If PICK_MODELS And MODELS_LOADED Then PickModel()
        '===========================================================================

        '===========================================================================
        '================== Deferred Rendering, HUD and MINI MAP ===================
        '===========================================================================


        '===========================================================================
        'Before we destory the gColor texture using it for other functions.
        If frmGbufferViewer IsNot Nothing Then
            If frmGbufferViewer.Visible And frmGbufferViewer.Viewer_Image_ID = 2 Then
                frmGbufferViewer.update_screen()
            End If
        End If
        '===========================================================================

        '===========================================================================
        Ortho_main()
        FBOm.attach_CNGP()
        'final render. Either direct to default or use SSAA process.
        GL.Disable(EnableCap.DepthTest)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        If SSAA_enable Then
            GL.ClearColor(0.0, 0.5, 0.0, 0.0)
            GL.Clear(ClearBufferMask.ColorBufferBit)
            render_deferred_buffers()
            copy_default_to_gColor()
            perform_SSAA_Pass()

        Else
            render_deferred_buffers()
        End If
        '===========================================================================
        'hopefully, this will look like glass :)
        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            copy_default_to_gColor()
            glassPass()
        End If
        If DONT_HIDE_HUD Then
            '===========================================================================
            'color_correct()
            '===========================================================================
            render_HUD() '==============================================================
            '===========================================================================

            '===========================================================================
            'This has to be called last. It changes the PROJECTMATRIX and VIEWMATRIX
            If DONT_HIDE_MINIMAP Then draw_mini_map() '===========================================================
            '===========================================================================
        End If
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

        '===========================================================================
        If _STARTED Then frmMain.glControl_main.SwapBuffers() '=====================
        '===========================================================================
        If frmGbufferViewer IsNot Nothing Then
            If frmGbufferViewer.Visible And frmGbufferViewer.Viewer_Image_ID <> 2 Then
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

    Private Sub draw_sun()

        FBOm.attach_C()

        'test only
        'Dim matrix = Matrix4.CreateTranslation(New Vector3(0F, 100.0F, 0F))
        GL.Enable(EnableCap.DepthTest)
        Dim matrix = Matrix4.CreateTranslation(New Vector3(LIGHT_POS(0) + CAM_POSITION.X, LIGHT_POS(1) + CAM_POSITION.Y, LIGHT_POS(2) + CAM_POSITION.Z))


        FF_BillboardShader.Use()
        GL.Uniform1(FF_BillboardShader("colorMap"), 0)
        GL.UniformMatrix4(FF_BillboardShader("matrix"), False, matrix)
        GL.Uniform3(FF_BillboardShader("color"), SUN_RENDER_COLOR.X / 100.0F, SUN_RENDER_COLOR.Y / 100.0F, SUN_RENDER_COLOR.Z / 100.0F)
        GL.Uniform1(FF_BillboardShader("scale"), SUN_SCALE * 10)
        GL.BindTextureUnit(0, SUN_TEXTURE_ID)

        GL.Uniform4(FF_BillboardShader("rect"), -0.5F, -0.5F, 0.5F, 0.5F)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FF_BillboardShader.StopUse()

    End Sub

    Private Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        GL.ClearNamedBufferSubData(MapGL.Buffers.parameters, PixelInternalFormat.R32ui, IntPtr.Zero, 2 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        cullShader.Use()

        GL.Uniform1(cullShader("numModelInstances"), MapGL.numModelInstances)

        Dim workGroupSize = 16
        Dim numGroups = (MapGL.numModelInstances + workGroupSize - 1) \ workGroupSize
        GL.DispatchCompute(numGroups, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullShader.StopUse()

        GL_POP_GROUP()
    End Sub
    Private Sub color_correct()
        copy_default_to_gColor()

        colorCorrectShader.Use()
        GL.UniformMatrix4(colorCorrectShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform1(colorCorrectShader("colorMap"), 0)
        GL.Uniform1(colorCorrectShader("lut"), 1)

        GL.BindTextureUnit(0, FBOm.gColor)
        GL.BindTextureUnit(1, CC_LUT_ID)

        'draw full screen quad
        GL.Uniform4(colorCorrectShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        colorCorrectShader.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.BindTextureUnit(1, 0)

    End Sub
    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(FBOm.gColor, 0, 0, 0, 0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
    End Sub

    Private Sub draw_terrain()
        GL_PUSH_GROUP("draw_terrain")

        '==========================
        'debug
        'FBOm.attach_C()
        'GL.Enable(EnableCap.Blend)
        '==========================
        TERRAIN_TRIS_DRAWN = 0
        GL.Enable(EnableCap.CullFace)

        '=======================================================================================
        'First, find out what chunks are to be drawn as LQ global_AM texturing only.
        '=======================================================================================
        For i = 0 To theMap.render_set.Length - 1
            Dim l1 = Abs(theMap.chunks(i).location.X - CAM_POSITION.X) 'x
            Dim l2 = Abs(theMap.v_data(i).avg_heights - CAM_POSITION.Y) 'y
            Dim l3 = Abs(theMap.chunks(i).location.Y - CAM_POSITION.Z) 'z
            Dim v As New Vector3(l1, l2, l3)
            Dim l = v.Length
            If l > 300.0F Then 'This value is the distance at which the chunk drawing is swapped.
                theMap.render_set(i).LQ = True
            Else
                theMap.render_set(i).LQ = False
            End If
        Next

        '=======================================================================================
        'Draw visible LQ chunks
        '=======================================================================================
        '------------------------------------------------
        TerrainLQShader.Use()  '<------------ Shader Bind
        '------------------------------------------------
        ' Set this texture to 0 to test LQ/HQ transitions
        GL.BindTextureUnit(0, theMap.GLOBAL_AM_ID) '<----------------- Texture Bind
        GL.BindTextureUnit(1, m_normal_id)

        GL.Uniform2(TerrainLQShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(TerrainLQShader("map_center"), -b_x_min, b_y_max)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk

                GL.UniformMatrix4(TerrainLQShader("modelMatrix"), False, theMap.render_set(i).matrix)

                GL.UniformMatrix3(TerrainLQShader("normalMatrix"), True, Matrix3.Invert(New Matrix3(PerViewData.view * theMap.render_set(i).matrix))) 'NormalMatrix
                GL.Uniform2(TerrainLQShader("me_location"), theMap.chunks(i).location.X, theMap.chunks(i).location.Y)

                'draw chunk
                GL.BindVertexArray(theMap.render_set(i).VAO)
                GL.DrawElements(PrimitiveType.Triangles,
                    24576,
                    DrawElementsType.UnsignedShort, 0)
            End If

        Next
        TerrainLQShader.StopUse()
        unbind_textures(2)
        '=======================================================================================
        'draw visible HZ terrain
        '=======================================================================================
        '------------------------------------------------
        TerrainShader.Use()  '<-------------- Shader Bind
        '------------------------------------------------

        'shit load of textures to bind

        GL.BindTextureUnit(21, TEST_IDS(0))
        GL.BindTextureUnit(22, TEST_IDS(1))
        GL.BindTextureUnit(23, TEST_IDS(2))
        GL.BindTextureUnit(24, TEST_IDS(3))
        GL.BindTextureUnit(25, TEST_IDS(4))
        GL.BindTextureUnit(26, TEST_IDS(5))
        GL.BindTextureUnit(27, TEST_IDS(6))
        GL.BindTextureUnit(28, TEST_IDS(7))

        GL.BindTextureUnit(29, theMap.GLOBAL_AM_ID) '<----------------- Texture Bind
        GL.BindTextureUnit(30, m_normal_id)
        'water BS
        GL.BindTextureUnit(31, ripple_textures(RIPPLE_FRAME_NUMBER))
        GL.BindTextureUnit(32, ripple_mask_texture)

        GL.Uniform3(TerrainShader("waterColor"),
                        Map_wetness.waterColor.X,
                        Map_wetness.waterColor.Y,
                        Map_wetness.waterColor.Z)

        GL.Uniform1(TerrainShader("waterAlpha"), Map_wetness.waterAlpha)
        GL.Uniform1(TerrainShader("waveUVScale"), Map_wetness.waveUVScale)
        GL.Uniform1(TerrainShader("waveStrength"), Map_wetness.waveStrength)
        GL.Uniform1(TerrainShader("waveMaskUVScale"), Map_wetness.waveMaskUVScale)

        GL.Uniform1(TerrainShader("rippple_mask_time"), RIPPLE_MASK_TIME)

        GL.Uniform2(TerrainShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(TerrainShader("map_center"), -b_x_min, b_y_max)

        GL.Uniform1(TerrainShader("show_test"), SHOW_TEST_TEXTURES)

        'Dim max_binding As Integer = GL.GetInteger(GetPName.MaxUniformBufferBindings)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And Not theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk

                GL.UniformMatrix4(TerrainShader("modelMatrix"), False, theMap.render_set(i).matrix)

                GL.UniformMatrix3(TerrainShader("normalMatrix"), True, Matrix3.Invert(New Matrix3(PerViewData.view * theMap.render_set(i).matrix))) 'NormalMatrix
                GL.Uniform2(TerrainShader("me_location"), theMap.chunks(i).location.X, theMap.chunks(i).location.Y) 'me_location

                'bind all the data for this chunk
                With theMap.render_set(i)
                    GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, .layersStd140_ubo)

                    'debug shit
                    'GL.BindTextureUnit(31, .dom_texture_id) '<----------------- Texture Bind

                    'AM maps
                    GL.BindTextureUnit(1, .TexLayers(0).AM_id1)
                    GL.BindTextureUnit(2, .TexLayers(1).AM_id1)
                    GL.BindTextureUnit(3, .TexLayers(2).AM_id1)
                    GL.BindTextureUnit(4, .TexLayers(3).AM_id1)

                    GL.BindTextureUnit(5, .TexLayers(0).AM_id2)
                    GL.BindTextureUnit(6, .TexLayers(1).AM_id2)
                    GL.BindTextureUnit(7, .TexLayers(2).AM_id2)
                    GL.BindTextureUnit(8, .TexLayers(3).AM_id2)
                    'NM maps
                    GL.BindTextureUnit(9, .TexLayers(0).NM_id1)
                    GL.BindTextureUnit(10, .TexLayers(1).NM_id1)
                    GL.BindTextureUnit(11, .TexLayers(2).NM_id1)
                    GL.BindTextureUnit(12, .TexLayers(3).NM_id1)

                    GL.BindTextureUnit(13, .TexLayers(0).NM_id2)
                    GL.BindTextureUnit(14, .TexLayers(1).NM_id2)
                    GL.BindTextureUnit(15, .TexLayers(2).NM_id2)
                    GL.BindTextureUnit(16, .TexLayers(3).NM_id2)
                    'bind blend textures
                    GL.BindTextureUnit(17, .TexLayers(0).Blend_id)
                    GL.BindTextureUnit(18, .TexLayers(1).Blend_id)
                    GL.BindTextureUnit(19, .TexLayers(2).Blend_id)
                    GL.BindTextureUnit(20, .TexLayers(3).Blend_id)

                    'draw chunk
                    GL.BindVertexArray(.VAO)
                    GL.DrawElements(PrimitiveType.Triangles,
                        24576,
                        DrawElementsType.UnsignedShort, 0)
                End With
            End If
        Next

        TerrainShader.StopUse()

        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        unbind_textures(30)

        If WIRE_TERRAIN Then

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            FBOm.attach_CF()

            TerrainNormals.Use()

            GL.Uniform1(TerrainNormals("prj_length"), 0.5F)
            GL.Uniform1(TerrainNormals("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(TerrainNormals("show_wireframe"), CInt(WIRE_TERRAIN))

            For i = 0 To theMap.render_set.Length - 1

                If theMap.render_set(i).visible Then

                    Dim model = theMap.render_set(i).matrix

                    GL.UniformMatrix4(TerrainNormals("model"), False, model)

                    GL.BindVertexArray(theMap.render_set(i).VAO)

                    'draw chunk wire
                    GL.DrawElements(PrimitiveType.Triangles,
                            24576,
                            DrawElementsType.UnsignedShort, 0)

                End If
            Next

            TerrainNormals.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        End If

        GL_POP_GROUP()
    End Sub

    Private Sub draw_models()
        GL_PUSH_GROUP("draw_models")

        'SOLID FILL
        FBOm.attach_CNGP()

        Dim indices = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        ' Color highlighting of LOD levels if enabled.
        GL.Uniform1(modelShader("show_Lods"), SHOW_LOD_COLORS)

        'assign subroutines
        GL.UniformSubroutines(ShaderType.FragmentShader, indices.Length, indices)

        GL.Enable(EnableCap.CullFace)

        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, MapGL.Buffers.indirect)
        GL.BindBuffer(DirectCast(33006, BufferTarget), MapGL.Buffers.parameters)
        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, IntPtr.Zero, MapGL.indirectDrawCount, 0)

        modelShader.StopUse()

        FBOm.attach_CNGPA()
        '------------------------------------------------
        modelGlassShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        GL.Disable(EnableCap.CullFace)

        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, MapGL.Buffers.indirect_glass)
        GL.BindBuffer(DirectCast(33006, BufferTarget), MapGL.Buffers.parameters)
        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, New IntPtr(4), MapGL.indirectDrawCount, 0)

        modelGlassShader.StopUse()

        FBOm.attach_CNGP()
        GL.DepthMask(False)

        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)

            FBOm.attach_CF()
            normalShader.Use()

            GL.Uniform1(normalShader("prj_length"), 0.3F)
            GL.Uniform1(normalShader("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(normalShader("show_wireframe"), CInt(WIRE_MODELS))

            GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, New IntPtr(4), MapGL.indirectDrawCount, 0)

            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, MapGL.Buffers.indirect)
            GL.BindBuffer(DirectCast(33006, BufferTarget), MapGL.Buffers.parameters)
            GL.BindVertexArray(MapGL.VertexArrays.allMapModels)
            GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, IntPtr.Zero, MapGL.indirectDrawCount, 0)
            normalShader.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        End If

        If SHOW_BOUNDING_BOXES Then
            GL.Disable(EnableCap.DepthTest)

            boxShader.Use()

            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.Points, 0, MapGL.numModelInstances)

            boxShader.StopUse()
        End If

        GL_POP_GROUP()
    End Sub

    Private Sub draw_terrain_grids()
        GL_PUSH_GROUP("draw_terrain_grids")

        FBOm.attach_C()
        'GL.DepthMask(False)
        GL.Enable(EnableCap.DepthTest)
        TerrainGrids.Use()
        GL.Uniform2(TerrainGrids("bb_tr"), MAP_BB_UR.X, MAP_BB_UR.Y)
        GL.Uniform2(TerrainGrids("bb_bl"), MAP_BB_BL.X, MAP_BB_BL.Y)
        GL.Uniform1(TerrainGrids("g_size"), PLAYER_FIELD_CELL_SIZE)

        GL.Uniform1(TerrainGrids("show_border"), SHOW_BORDER)
        GL.Uniform1(TerrainGrids("show_chunks"), SHOW_CHUNKS)
        GL.Uniform1(TerrainGrids("show_grid"), SHOW_GRID)

        GL.Uniform1(TerrainGrids("gGMF"), 0)

        GL.BindTextureUnit(0, FBOm.gGMF)



        For i = 0 To theMap.render_set.Length - 1
            GL.UniformMatrix4(TerrainGrids("model"), False, theMap.render_set(i).matrix)

            'draw chunk
            GL.BindVertexArray(theMap.render_set(i).VAO)
            GL.DrawElements(PrimitiveType.Triangles,
                24576,
                DrawElementsType.UnsignedShort, 0)
        Next
        TerrainGrids.StopUse()

        GL.BindTextureUnit(0, 0)

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Private Sub render_deferred_buffers()
        GL_PUSH_GROUP("render_deferred_buffers")
        '===========================================================================
        ' Test our deferred shader =================================================
        '===========================================================================
        deferredShader.Use()

        'set up uniforms
        GL.Uniform1(deferredShader("gColor"), 0)
        GL.Uniform1(deferredShader("gNormal"), 1)
        GL.Uniform1(deferredShader("gGMF"), 2) ' ignore this for now
        GL.Uniform1(deferredShader("gPosition"), 3)
        GL.Uniform1(deferredShader("cubeMap"), 4)
        GL.Uniform1(deferredShader("lut"), 5)
        GL.Uniform1(deferredShader("env_brdf_lut"), 6)

        GL.BindTextureUnit(0, FBOm.gColor)
        GL.BindTextureUnit(1, FBOm.gNormal)
        GL.BindTextureUnit(2, FBOm.gGMF)
        GL.BindTextureUnit(3, FBOm.gPosition)
        GL.BindTextureUnit(4, CUBE_TEXTURE_ID)
        GL.BindTextureUnit(5, CC_LUT_ID)
        GL.BindTextureUnit(6, ENV_BRDF_LUT)



        GL.Uniform3(deferredShader("sunColor"), SUNCOLOR.X, SUNCOLOR.Y, SUNCOLOR.Z)
        GL.Uniform3(deferredShader("ambientColorForward"), AMBIENTSUNCOLOR.X, AMBIENTSUNCOLOR.Y, AMBIENTSUNCOLOR.Z)

        ' GL.BindTextureUnit(4, FBOm.gDepth)
        'GL.Uniform1(deferredShader("gDepth"), 4)

        'Lighting settings
        GL.Uniform1(deferredShader("AMBIENT"), frmLighting.lighting_ambient)
        GL.Uniform1(deferredShader("BRIGHTNESS"), frmLighting.lighting_terrain_texture)
        GL.Uniform1(deferredShader("SPECULAR"), frmLighting.lighting_specular_level)
        GL.Uniform1(deferredShader("GRAY_LEVEL"), frmLighting.lighting_gray_level)
        GL.Uniform1(deferredShader("GAMMA_LEVEL"), frmLighting.lighting_gamma_level)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(6) ' unbind all the used texture slots

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Private Sub perform_SSAA_Pass()

        GL_PUSH_GROUP("perform_SSAA_Pass")

        Dim e = GL.GetError
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        FXAAShader.Use()
        GL.UniformMatrix4(FXAAShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform2(FXAAShader("viewportSize"), CSng(FBOm.SCR_WIDTH), CSng(FBOm.SCR_HEIGHT))

        GL.BindTextureUnit(0, FBOm.gColor)
        'GL.BindTextureUnit(0, FBOm.gAUX_Color)

        'draw full screen quad
        GL.Uniform4(FXAAShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FXAAShader.StopUse()
        GL.BindTextureUnit(0, 0)

        GL_POP_GROUP()
    End Sub

    Private Sub glassPass()
        GL_PUSH_GROUP("perform_GlassPass")

        'GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)

        'GL.ReadBuffer(ReadBufferMode.Back)
        GL.GetError()
        copy_default_to_gColor()
        Dim e = GL.GetError

        glassPassShader.Use()
        GL.UniformMatrix4(glassPassShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        'GL.Uniform2(glassPassShader("viewportSize"), CSng(FBOm.SCR_WIDTH), CSng(FBOm.SCR_HEIGHT))

        GL.Uniform1(glassPassShader("colorMap"), 0)
        GL.Uniform1(glassPassShader("glassMap"), 1)

        GL.BindTextureUnit(0, FBOm.gColor)
        GL.BindTextureUnit(1, FBOm.gAUX_Color)

        'draw full screen quad
        GL.Uniform4(glassPassShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        glassPassShader.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.BindTextureUnit(1, 0)

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
                    draw_text(s, sp.X, sp.Y, OpenTK.Graphics.Color4.Yellow, True, 1)
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
        'GL.Finish()
        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds

        'sum triangles drawn
        Dim tr = TERRAIN_TRIS_DRAWN

        Dim cull_t = cull_timer.ElapsedMilliseconds
        Dim txt = String.Format("Culled: {0} | FPS: {1} | Triangles drawn per frame: {2} | Draw time in Milliseconds: {3}", CULLED_COUNT, FPS_TIME, tr, elapsed)
        Dim txt2 = String.Format("Cull Time: {0}", cull_t)
        'debug shit
        'txt = String.Format("mouse {0} {1}", MINI_WORLD_MOUSE_POSITION.X.ToString, MINI_WORLD_MOUSE_POSITION.Y.ToString)
        'txt = String.Format("HX {0} : HY {1}", HX, HY)
        draw_text(txt, 5.0F, 5.0F, OpenTK.Graphics.Color4.Cyan, False, 1)
        draw_text(txt2, 5.0F, 24.0F, OpenTK.Graphics.Color4.Cyan, False, 1)
        draw_text(PICKED_STRING, 5.0F, 43.0F, OpenTK.Graphics.Color4.Yellow, False, 1)

        'draw status of SSAA
        draw_text(SSAA_text, 5.0F, 62.0F, OpenTK.Graphics.Color4.Yellow, False, 1)
        Dim temp_time = temp_timer.ElapsedMilliseconds
        Dim aa As Integer = 0

        ' Draw Terrain IDs =========================================================
        If SHOW_CHUNK_IDs Then
            draw_terrain_ids()
        End If
        '===========================================================================

        GL_POP_GROUP()
    End Sub

    Public Sub Draw_SkyDome()
        GL_PUSH_GROUP("Draw_SkyDome")

        FBOm.attach_CNGP()

        SkyDomeShader.Use()

        GL.Enable(EnableCap.CullFace)

        GL.BindTextureUnit(0, theMap.Sky_Texture_Id)

        GL.BindVertexArray(theMap.skybox_mdl.vao)
        GL.DrawElements(PrimitiveType.Triangles, theMap.skybox_mdl.indices_count * 3, DrawElementsType.UnsignedShort, 0)

        SkyDomeShader.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.Disable(EnableCap.CullFace)

        GL_POP_GROUP()
    End Sub

    Private Sub draw_map_cursor()
        GL_PUSH_GROUP("draw_map_cursor")

        DecalProject.Use()

        GL.Uniform3(DecalProject("color_in"), 0.4F, 0.3F, 0.3F)

        GL.Uniform1(DecalProject("depthMap"), 0)
        GL.Uniform1(DecalProject("gFlag"), 1)
        GL.Uniform1(DecalProject("colorMap"), 2)
        GL.Uniform1(DecalProject("gGMF"), 3)

        GL.BindTextureUnit(0, FBOm.gDepth)
        GL.BindTextureUnit(1, FBOm.gGMF)
        GL.BindTextureUnit(2, CURSOR_TEXTURE_ID)
        GL.BindTextureUnit(3, FBOm.gGMF)

        ' Track the terrain at Y
        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, CURSOR_Y, U_LOOK_AT_Z)
        Dim model_S = Matrix4.CreateScale(25.0F, 50.0F, 25.0F)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DecalProject("DecalMatrix"), False, rotate * model_S * model_X)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DecalProject.StopUse()
        unbind_textures(3)

        GL_POP_GROUP()
    End Sub

    Private Sub draw_terrain_base_rings()

        GL_PUSH_GROUP("draw_terrain_base_rings")

        BaseRingProjector.Use()

        GL.Uniform1(BaseRingProjector("depthMap"), 0)
        GL.Uniform1(BaseRingProjector("gGMF"), 1)
        GL.BindTextureUnit(0, FBOm.gDepth)
        GL.BindTextureUnit(1, FBOm.gGMF)

        'constants
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
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        'base 2 ring
        model_X = Matrix4.CreateTranslation(-TEAM_2.X, T2_Y, TEAM_2.Z)
        GL.Uniform3(BaseRingProjector("ring_center"), -TEAM_2.X, TEAM_2.Y, TEAM_2.Z)
        GL.UniformMatrix4(BaseRingProjector("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjector("color"), OpenTK.Graphics.Color4.Red)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        BaseRingProjector.StopUse()
        unbind_textures(2)

        GL_POP_GROUP()
    End Sub

#Region "miniMap"

    Private Sub draw_mini_map()
        'check if we have the mini map loaded.
        If theMap.MINI_MAP_ID = 0 Then
            Return
        End If

        GL_PUSH_GROUP("draw_mini_map")

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
        TextRenderShader.Use()
        GL.Uniform4(TextRenderShader("color"), OpenTK.Graphics.Color4.White)
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(TextRenderShader("divisor"), 1.0F) 'atlas size
        GL.Uniform1(TextRenderShader("index"), 0.0F)
        GL.Uniform1(TextRenderShader("mask"), 0)

        '=======================================================================
        'draw horz trim
        GL.BindTextureUnit(0, MINI_TRIM_HORZ_ID)
        Dim rect As New RectangleF(cx - 12, cy - 12, 640 + 12, 16.0F)
        GL.Uniform4(TextRenderShader("rect"),
                  rect.Left,
                  -rect.Top,
                  rect.Right,
                  -rect.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'draw vert trim
        GL.BindTextureUnit(0, MINI_TRIM_VERT_ID)
        rect = New RectangleF(cx - 12, cy - 12, 16.0F, 640 + 12.0F)
        GL.Uniform4(TextRenderShader("rect"),
                 rect.Left,
                 -rect.Top,
                 rect.Right,
                 -rect.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        '=======================================================================

        'row
        '=======================================================================
        GL.Uniform1(TextRenderShader("divisor"), 10.0F) 'atlas size

        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        GL.BindTextureUnit(0, MINI_NUMBERS_ID)

        Dim index! = 0
        Dim cnt! = 10.0F
        Dim step_s! = MINI_MAP_SIZE / 10.0F
        GL.BindVertexArray(defaultVao)
        For xp = cx To cx + MINI_MAP_SIZE Step step_s
            GL.Uniform1(TextRenderShader("index"), index)

            rect = New RectangleF(xp + (step_s / 2.0F) - 8, cy - 11, 16.0F, 10.0F)
            GL.Uniform4(TextRenderShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
            Application.DoEvents()
        Next
        'column
        '=======================================================================
        index = 0
        GL.Uniform1(TextRenderShader("col_row"), 0) 'draw row
        GL.BindTextureUnit(0, MINI_LETTERS_ID)

        cnt! = 10.0F
        step_s! = MINI_MAP_SIZE / 10.0F
        GL.BindVertexArray(defaultVao)
        For yp = cy To cy + MINI_MAP_SIZE Step step_s
            GL.Uniform1(TextRenderShader("index"), index)

            rect = New RectangleF(cx - 9, yp + (step_s / 2) - 6, 8.0F, 12.0F)
            GL.Uniform4(TextRenderShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
            Application.DoEvents()
        Next
        TextRenderShader.StopUse()
        GL.BindTextureUnit(0, 0)

        GL_POP_GROUP()
    End Sub

    Private Sub Draw_mini()
        GL_PUSH_GROUP("Draw_mini")

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

        GL_POP_GROUP()
    End Sub

    Private Sub draw_minimap_texture()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        Dim rect As New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)
        image2dShader.Use()

        GL.BindTextureUnit(0, theMap.MINI_MAP_ID)
        GL.Uniform1(image2dShader("imageMap"), 0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        image2dShader.StopUse()
        'unbind texture
        GL.BindTextureUnit(0, 0)
    End Sub

    Private Sub draw_mini_base_ids()
        GL_PUSH_GROUP("draw_mini_base_ids")

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

        GL_POP_GROUP()
    End Sub

    Private Sub draw_mini_base_rings()
        GL_PUSH_GROUP("draw_mini_base_rings")

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

        GL_POP_GROUP()
    End Sub

    Private Sub draw_mini_position()
        GL_PUSH_GROUP("draw_mini_position")

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

        GL_POP_GROUP()
    End Sub

    Private Sub draw_mini_grids_lines()
        GL_PUSH_GROUP("draw_mini_grids_lines")

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

        GL_POP_GROUP()
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
    End Sub

    Public Sub draw_text(ByRef text As String,
                         ByVal locX As Single,
                         ByVal locY As Single,
                         ByRef color As OpenTK.Graphics.Color4,
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
        GL.Uniform1(TextRenderShader("divisor"), 95.0F) 'atlas size
        GL.BindTextureUnit(0, ASCII_ID)
        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        GL.Uniform4(TextRenderShader("color"), color)
        GL.Uniform1(TextRenderShader("mask"), mask)
        '=======================================================================
        'draw text
        Dim cntr = 0
        If center Then
            cntr = text.Length * 10.0F / 2.0F
        End If
        Dim ar = text.ToArray
        Dim cnt As Integer = 0
        GL.BindVertexArray(defaultVao)
        For Each l In ar
            Dim idx = CSng(Asc(l) - 32)
            Dim tp = (locX + cnt * 10.0) - cntr
            GL.Uniform1(TextRenderShader("index"), idx)
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

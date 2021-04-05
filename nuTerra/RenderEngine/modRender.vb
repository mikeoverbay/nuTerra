Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Dim temp_timer As New Stopwatch
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Private uv_location As New Vector2

    Dim colors() As Graphics.Color4 = {
        Graphics.Color4.Red,
        Graphics.Color4.Green,
        Graphics.Color4.Blue,
        Graphics.Color4.Yellow,
        Graphics.Color4.Purple,
        Graphics.Color4.Orange,
        Graphics.Color4.Coral,
        Graphics.Color4.Silver
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
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) '================
        '===========================================================================

        '===========================================================================
        set_prespective_view() ' <-- sets camera and prespective view ==============
        '===========================================================================

        '===========================================================================
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

        '===========================================================================
        FBOm.attach_CNGPA() 'clear ALL gTextures!
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.ClearDepth(0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        '===========================================================================
        FBOm.attach_C()
        If DONT_BLOCK_SKY Then
            GL.Disable(EnableCap.DepthTest)
            Draw_SkyDome()
            draw_sun()
            GL.Enable(EnableCap.DepthTest)
        End If

        '===========================================================================
        'GL States 
        GL.DepthFunc(DepthFunction.Greater)
        '===========================================================================

        'Model depth pass only
        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            GL.GetNamedBufferSubData(MapGL.Buffers.parameters.buffer_id, IntPtr.Zero, 3 * Marshal.SizeOf(Of Integer), MapGL.numAfterFrustum)

            '=======================================================================
            model_depth_pass() '=========================================================
            '=======================================================================

            If USE_RASTER_CULLING Then
                model_cull_raster_pass()
            End If
        End If

        FBOm.attach_CNGPA()

        If TERRAIN_LOADED And DONT_BLOCK_TERRAIN Then
            '=======================================================================
            draw_terrain() '========================================================
            '=======================================================================
            If (SHOW_BORDER Or SHOW_CHUNKS Or SHOW_GRID) Then draw_terrain_grids()
            '=======================================================================
            'setup for projection before drawing
            FBOm.attach_C_no_Depth()
            GL.DepthMask(False)
            GL.FrontFace(FrontFaceDirection.Cw)
            GL.Enable(EnableCap.CullFace)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
            '=======================================================================
            If SHOW_CURSOR Then draw_map_cursor() '=================================
            '=======================================================================
            'restore settings after projected objects are drawn
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
        If TERRAIN_LOADED Then
            FBOm.attach_C()
            GL.Enable(EnableCap.Blend)
            GL.Enable(EnableCap.DepthTest)
            GL.DepthMask(False)
            For i = 0 To Test_Emiters.Length - 1
                'Test_Emiters(i).execute()
            Next

            GL.Disable(EnableCap.Blend)
            GL.Enable(EnableCap.DepthTest)
            GL.DepthMask(True)
        End If


        GL.DepthFunc(DepthFunction.Less)
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
        '===========================================================================
        '===========================================================================
        Ortho_main()
        '===========================================================================
        '===========================================================================
        '===========================================================================
        '===========================================================================



        GL.Disable(EnableCap.DepthTest)

        FBOm.attach_C2()

        render_deferred_buffers()
        'gAux_color to gColor;
        FBOm.attach_C1_and_C2()
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
        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            glassPass()
        End If

        '===========================================================================

        'ortho projection decals
#If True Then

        FBOm.attach_C()


        If TERRAIN_LOADED And DONT_BLOCK_TERRAIN Then
            GL.Disable(EnableCap.DepthTest)

            copy_default_to_gColor()
            GL.DepthMask(False)
            'GL.FrontFace(FrontFaceDirection.Cw)
            GL.Enable(EnableCap.Blend)
            GL.Enable(EnableCap.CullFace)

            draw_base_rings_deferred()

            'hopefully, this will look like FOG :)
            GL.Disable(EnableCap.Blend)
            copy_default_to_gColor()
            global_fog()

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
            If DONT_HIDE_MINIMAP Then draw_mini_map() '===========================================================
            '===========================================================================
        End If
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
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

    Dim map_center As Vector3
    Dim scale As Vector3

    '=============================================================================================
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

        GL.Uniform1(deferredShader("light_count"), LIGHTS.index - 1)
        GL.Uniform1(deferredShader("mapMaxHeight"), MAX_MAP_HEIGHT)
        GL.Uniform1(deferredShader("mapMinHeight"), MIN_MAP_HEIGHT)
        GL.Uniform1(deferredShader("MEAN"), CSng(MEAN_MAP_HEIGHT))

        GL.Uniform3(deferredShader("fog_tint"), FOG_COLOR.X, FOG_COLOR.Y, FOG_COLOR.Z)

        GL.Uniform3(deferredShader("waterColor"),
                        Map_wetness.waterColor.X,
                        Map_wetness.waterColor.Y,
                        Map_wetness.waterColor.Z)


        FBOm.gColor.BindUnit(0)
        FBOm.gNormal.BindUnit(1)
        FBOm.gGMF.BindUnit(2)
        FBOm.gPosition.BindUnit(3)
        CUBE_TEXTURE_ID.BindUnit(4)
        CC_LUT_ID.BindUnit(5)

        If ENV_BRDF_LUT_ID IsNot Nothing Then ENV_BRDF_LUT_ID.BindUnit(6)

        GL.Uniform3(deferredShader("sunColor"), SUNCOLOR.X, SUNCOLOR.Y, SUNCOLOR.Z)
        GL.Uniform3(deferredShader("ambientColorForward"), AMBIENTSUNCOLOR.X, AMBIENTSUNCOLOR.Y, AMBIENTSUNCOLOR.Z)

        ' GL.BindTextureUnit(4, FBOm.gDepth)
        'GL.Uniform1(deferredShader("gDepth"), 4)

        'Lighting settings
        GL.Uniform1(deferredShader("AMBIENT"), frmLightSettings.lighting_ambient)
        GL.Uniform1(deferredShader("BRIGHTNESS"), frmLightSettings.lighting_terrain_texture)
        GL.Uniform1(deferredShader("SPECULAR"), frmLightSettings.lighting_specular_level)
        GL.Uniform1(deferredShader("GRAY_LEVEL"), frmLightSettings.lighting_gray_level)
        GL.Uniform1(deferredShader("GAMMA_LEVEL"), frmLightSettings.lighting_gamma_level)
        GL.Uniform1(deferredShader("fog_level"), frmLightSettings.lighting_fog_level * 100.0F)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(6) ' unbind all the used texture slots

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub
    '=============================================================================================
    Private Sub color_keys()

        If SHOW_TEST_TEXTURES = 0 Then Return

        draw_image_rectangle(New RectangleF(0.0F, 79.0F, 100.0F, 19.0F * 8.0F),
                                            DUMMY_TEXTURE_ID, False)

        If SHOW_TEST_TEXTURES > 0.0F Then
            For i = 0 To 7
                draw_text(tags(i), 5.0F, 81.0F + (i * 19.0F), colors(i), False, 0)
            Next
        End If

    End Sub
    Private Sub global_fog()
        GL_PUSH_GROUP("perform_Fog_Noise_pass")

        Dim s = 0.03F * DELTA_TIME ' <---- How fast the fog moves

        'this is in the game data somewhere!
        Dim move_vector = New Vector2(0.3, 0.7) ' <----  Direction the fog moves

        uv_location += move_vector * s '<----  do the math;

        DeferredFogShader.Use()

        GL.Uniform3(DeferredFogShader("fog_tint"), FOG_COLOR.X, FOG_COLOR.Y, FOG_COLOR.Z)
        GL.Uniform1(DeferredFogShader("uv_scale"), 4.0F)
        GL.Uniform2(DeferredFogShader("move_vector"), uv_location.X, uv_location.Y)
        GL.Uniform3(DeferredFogShader("fog_tint"), FOG_COLOR.X, FOG_COLOR.Y, FOG_COLOR.Z)
        GL.Uniform1(DeferredFogShader("fog_level"), frmLightSettings.lighting_fog_level * 100.0F)

        Dim ff = frmLightSettings.lighting_fog_level * 100.0

        NOISE_id.BindUnit(0)
        FBOm.gDepth.BindUnit(1)
        FBOm.gPosition.BindUnit(2)
        FBOm.gColor.BindUnit(3)
        'FBOm.gColor_2.BindUnit(4)

        map_center.X = 100.0F * (theMap.bounds_minX + theMap.bounds_maxX) / 2.0F
        map_center.Y = 1.0F
        map_center.Z = 100.0F * (theMap.bounds_minY + theMap.bounds_maxY) / 2.0F
        map_center.X += 50.0F
        map_center.Z += 50.0F

        scale.X = 100.0F * (Abs(theMap.bounds_minX) + Abs(theMap.bounds_maxX) + 1.0F)
        scale.Y = 1000.0F
        scale.Z = 100.0F * (Abs(theMap.bounds_minY) + Abs(theMap.bounds_maxY) + 1.0F)

        'scale *= 0.1
        Dim model_X = Matrix4.CreateTranslation(map_center)
        Dim model_S = Matrix4.CreateScale(scale)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        'GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DeferredFogShader("DecalMatrix"), False, rotate * model_S * model_X)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DeferredFogShader.StopUse()

        unbind_textures(2)

        GL_POP_GROUP()
    End Sub

    Private Sub draw_base_rings_deferred()
        If Not BASE_RINGS_LOADED Then
            Return
        End If
        GL_PUSH_GROUP("draw_terrain_base_rings_deferred")

        BaseRingProjectorDeferred.Use()

        GL.Disable(EnableCap.CullFace)
        'GL.Uniform1(BaseRingProjectorDeferred("depthMap"), 0)
        'GL.Uniform1(BaseRingProjectorDeferred("gGMF"), 1)
        'GL.Uniform1(BaseRingProjectorDeferred("gPosition"), 2)
        FBOm.gDepth.BindUnit(0)
        FBOm.gGMF.BindUnit(1)
        FBOm.gPosition.BindUnit(2)

        'constants
        GL.Uniform1(BaseRingProjectorDeferred("radius"), 50.0F)
        GL.Uniform1(BaseRingProjectorDeferred("thickness"), 2.0F)
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        Dim scale = Matrix4.CreateScale(120.0F, 25.0F, 120.0F)

        GL.Uniform1(BaseRingProjectorDeferred("BRIGHTNESS"), frmLightSettings.lighting_terrain_texture)

        ' base 1 ring

        Dim model_X = Matrix4.CreateTranslation(-TEAM_1.X, T1_Y, TEAM_1.Z)

        GL.Uniform3(BaseRingProjectorDeferred("ring_center"), -TEAM_1.X, TEAM_1.Y, TEAM_1.Z)
        GL.UniformMatrix4(BaseRingProjectorDeferred("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjectorDeferred("color"), New Graphics.Color4(0.0F, 128.0F, 0.0F, 0.5F))

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        'base 2 ring
        model_X = Matrix4.CreateTranslation(-TEAM_2.X, T2_Y, TEAM_2.Z)

        'check in side of cube
        If cube_point_intersection(rotate, scale, model_X, CAM_POSITION) Then
            GL.Uniform1(BaseRingProjectorDeferred("front"), CInt(True))
        Else
            GL.Uniform1(BaseRingProjectorDeferred("front"), CInt(False))
        End If

        GL.Uniform3(BaseRingProjectorDeferred("ring_center"), -TEAM_2.X, TEAM_2.Y, TEAM_2.Z)
        GL.UniformMatrix4(BaseRingProjectorDeferred("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjectorDeferred("color"), New Graphics.Color4(128.0F, 0.0F, 0.0F, 0.5F))

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        BaseRingProjectorDeferred.StopUse()
        unbind_textures(2)

        GL_POP_GROUP()
    End Sub

    Private Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        GL.ClearNamedBufferSubData(MapGL.Buffers.parameters.buffer_id, PixelInternalFormat.R32ui, IntPtr.Zero, 3 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        cullShader.Use()

        GL.Uniform1(cullShader("numModelInstances"), MapGL.numModelInstances)

        Dim numGroups = (MapGL.numModelInstances + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
        GL.Arb.DispatchComputeGroupSize(numGroups, 1, 1, WORK_GROUP_SIZE, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(FBOm.gColor.texture_id, 0, 0, 0, 0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
    End Sub

    Private Sub copy_gColor_2_to_gColor()
        GL.ReadBuffer(ReadBufferMode.ColorAttachment6)
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0)
        GL.BlitFramebuffer(0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                                0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                                ClearBufferMask.ColorBufferBit,
                                BlitFramebufferFilter.Nearest)
        'Dim er = GL.GetError
        'Dim er1 = GL.GetError

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

        theMap.GLOBAL_AM_ID.BindUnit(0)
        FBO_mixer_set.gColorArray.BindUnit(1)
        FBO_mixer_set.gNormalArray.BindUnit(2)
        FBO_mixer_set.gGmmArray.BindUnit(3)

        GL.Uniform3(TerrainLQShader("waterColor"),
                        Map_wetness.waterColor.X,
                        Map_wetness.waterColor.Y,
                        Map_wetness.waterColor.Z)

        GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)
        MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk
                'draw chunk
                GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
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


        theMap.GLOBAL_AM_ID.BindUnit(21)

        FBO_mixer_set.gColorArray.BindUnit(22)
        FBO_mixer_set.gNormalArray.BindUnit(23)
        FBO_mixer_set.gGmmArray.BindUnit(24)

        'water BS
        GL.Uniform3(TerrainShader("waterColor"),
                        Map_wetness.waterColor.X,
                        Map_wetness.waterColor.Y,
                        Map_wetness.waterColor.Z)

        GL.Uniform1(TerrainShader("test"), SHOW_TEST_TEXTURES)

        GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)
        MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And Not theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk

                'bind all the data for this chunk
                With theMap.render_set(i)
                    'AM maps
                    theMap.render_set(i).layer.render_info(0).atlas_id.BindUnit(1)
                    theMap.render_set(i).layer.render_info(1).atlas_id.BindUnit(2)
                    theMap.render_set(i).layer.render_info(2).atlas_id.BindUnit(3)
                    theMap.render_set(i).layer.render_info(3).atlas_id.BindUnit(4)
                    theMap.render_set(i).layer.render_info(4).atlas_id.BindUnit(5)
                    theMap.render_set(i).layer.render_info(5).atlas_id.BindUnit(6)
                    theMap.render_set(i).layer.render_info(6).atlas_id.BindUnit(7)
                    theMap.render_set(i).layer.render_info(7).atlas_id.BindUnit(8)

                    'bind blend textures
                    .TexLayers(0).Blend_id.BindUnit(17)
                    .TexLayers(1).Blend_id.BindUnit(18)
                    .TexLayers(2).Blend_id.BindUnit(19)
                    .TexLayers(3).Blend_id.BindUnit(20)

                    'draw chunk
                    GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
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

            GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)
            MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, IntPtr.Zero, MapGL.numTerrainChunks, 0)

            TerrainNormals.StopUse()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        End If

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

        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)

        MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        MapGL.Buffers.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(1), 0)

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
        GL.ClearNamedBufferSubData(MapGL.Buffers.visibles.buffer_id, PixelInternalFormat.R32ui, IntPtr.Zero, MapGL.numAfterFrustum(0) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)
        GL.ClearNamedBufferSubData(MapGL.Buffers.visibles_dbl_sided.buffer_id, PixelInternalFormat.R32ui, IntPtr.Zero, MapGL.numAfterFrustum(1) * Marshal.SizeOf(Of Integer), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        GL.BindVertexArray(defaultVao)

        If USE_REPRESENTATIVE_TEST Then
            GL.Enable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        cullRasterShader.Use()
        GL.Uniform1(cullRasterShader("numAfterFrustum"), MapGL.numAfterFrustum(0))
        GL.DrawArrays(PrimitiveType.Points, 0, MapGL.numAfterFrustum(0) + MapGL.numAfterFrustum(1))
        cullRasterShader.StopUse()

        If USE_REPRESENTATIVE_TEST Then
            GL.Disable(GL_REPRESENTATIVE_FRAGMENT_TEST_NV)
        End If

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit)

        cullInvalidateShader.Use()
        GL.Uniform1(cullInvalidateShader("numAfterFrustum"), MapGL.numAfterFrustum(0))
        GL.Uniform1(cullInvalidateShader("numAfterFrustumDblSided"), MapGL.numAfterFrustum(1))

        Dim numGroups = (Math.Max(MapGL.numAfterFrustum(0), MapGL.numAfterFrustum(1)) + WORK_GROUP_SIZE - 1) \ WORK_GROUP_SIZE
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
        FBOm.attach_CNGP()

        Dim indices = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        'assign subroutines
        GL.UniformSubroutines(ShaderType.FragmentShader, indices.Length, indices)

        GL.Enable(EnableCap.CullFace)

        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)

        MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(0), 0)

        GL.Disable(EnableCap.CullFace)

        MapGL.Buffers.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(1), 0)

        modelShader.StopUse()

        GL.DepthFunc(DepthFunction.Greater)

        FBOm.attach_CNGPA()
        GL.DepthMask(True)

        '------------------------------------------------
        modelGlassShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        MapGL.Buffers.indirect_glass.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(2), 0)

        modelGlassShader.StopUse()

        FBOm.attach_CNGP()
        GL.DepthMask(False)

        If WIRE_MODELS Then
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)

            FBOm.attach_CF()
            normalShader.Use()

            GL.Uniform1(normalShader("prj_length"), 0.3F)
            GL.Uniform1(normalShader("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(normalShader("show_wireframe"), CInt(WIRE_MODELS))

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(2), 0)

            MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, MapGL.numAfterFrustum(0), 0)
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

        'restore depth function

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

        GL.Uniform1(TerrainGrids("show_border"), CInt(SHOW_BORDER))
        GL.Uniform1(TerrainGrids("show_chunks"), CInt(SHOW_CHUNKS))
        GL.Uniform1(TerrainGrids("show_grid"), CInt(SHOW_GRID))

        FBOm.gGMF.BindUnit(0)

        GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)
        MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)

        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, IntPtr.Zero, MapGL.numTerrainChunks, 0)
        TerrainGrids.StopUse()

        unbind_textures(0)

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Private Sub perform_SSAA_Pass()

        GL_PUSH_GROUP("perform_SSAA_Pass")

        Dim e = GL.GetError

        FXAAShader.Use()

        GL.Uniform1(FXAAShader("pass_through"), CInt(FXAA_enable))

        GL.UniformMatrix4(FXAAShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform2(FXAAShader("viewportSize"), CSng(FBOm.SCR_WIDTH), CSng(FBOm.SCR_HEIGHT))

        FBOm.gColor.BindUnit(0)

        'draw full screen quad
        GL.Uniform4(FXAAShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FXAAShader.StopUse()
        unbind_textures(0)

        GL_POP_GROUP()
    End Sub

    Private Sub glassPass()
        GL_PUSH_GROUP("perform_GlassPass")

        'GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO)

        'GL.ReadBuffer(ReadBufferMode.Back)
        GL.GetError()
        Dim e = GL.GetError

        glassPassShader.Use()
        GL.UniformMatrix4(glassPassShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        'GL.Uniform2(glassPassShader("viewportSize"), CSng(FBOm.SCR_WIDTH), CSng(FBOm.SCR_HEIGHT))

        GL.Uniform1(glassPassShader("BRIGHTNESS"), frmLightSettings.lighting_terrain_texture)

        FBOm.gColor.BindUnit(0)
        FBOm.gAUX_Color.BindUnit(1)

        'draw full screen quad
        GL.Uniform4(glassPassShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        glassPassShader.StopUse()
        unbind_textures(1)

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
                    s = String.Format("{0}, {1}", theMap.render_set(i).matrix.Row3(0), theMap.render_set(i).matrix.Row3(2))
                    draw_text(s, sp.X, sp.Y - 19, OpenTK.Graphics.Color4.Yellow, True, 1)

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

        'sum triangles drawn
        Dim tr = TERRAIN_TRIS_DRAWN

        Dim txt = String.Format("FPS: {0} | Draw time in Milliseconds: {1}", FPS_TIME, elapsed)
        'debug shit
        'txt = String.Format("mouse {0} {1}", MINI_WORLD_MOUSE_POSITION.X.ToString, MINI_WORLD_MOUSE_POSITION.Y.ToString)
        'txt = String.Format("HX {0} : HY {1}", HX, HY)
        draw_text(txt, 5.0F, 5.0F, Graphics.Color4.Cyan, False, 1)
        draw_text(PICKED_STRING, 5.0F, 24.0F, Graphics.Color4.Yellow, False, 1)

        color_keys()

        'draw status of SSAA
        draw_text(FXAA_text, 5.0F, 62.0F, Graphics.Color4.Yellow, False, 1)
        Dim temp_time = temp_timer.ElapsedMilliseconds
        Dim aa As Integer = 0

        ' Draw Terrain IDs =========================================================
        If SHOW_CHUNK_IDs And DONT_BLOCK_TERRAIN Then
            draw_terrain_ids()
        End If
        '===========================================================================

        GL_POP_GROUP()
    End Sub

    Public Sub Draw_SkyDome()
        GL_PUSH_GROUP("Draw_SkyDome")

        GL.DepthMask(False)
        FBOm.attach_CNGP()

        SkyDomeShader.Use()

        GL.Enable(EnableCap.CullFace)

        theMap.Sky_Texture_Id.BindUnit(0)

        GL.BindVertexArray(theMap.skybox_mdl.vao)
        GL.DrawElements(PrimitiveType.Triangles, theMap.skybox_mdl.indices_count * 3, DrawElementsType.UnsignedShort, 0)

        SkyDomeShader.StopUse()
        unbind_textures(0)

        GL.DepthMask(True)

        GL_POP_GROUP()
    End Sub

    Private Sub draw_sun()

        FBOm.attach_C()

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        Dim matrix = Matrix4.CreateTranslation(New Vector3(LIGHT_POS(0), LIGHT_POS(1), LIGHT_POS(2)))

        FF_BillboardShader.Use()
        GL.Uniform1(FF_BillboardShader("colorMap"), 0)
        GL.UniformMatrix4(FF_BillboardShader("matrix"), False, matrix)
        'GL.Uniform3(FF_BillboardShader("color"), SUN_RENDER_COLOR.X / 100.0F, SUN_RENDER_COLOR.Y / 100.0F, SUN_RENDER_COLOR.Z / 100.0F)
        GL.Uniform3(FF_BillboardShader("color"), 1.0F, 1.0F, 1.0F)
        GL.Uniform1(FF_BillboardShader("scale"), SUN_SCALE * 6)

        SUN_TEXTURE_ID.BindUnit(0)

        GL.Uniform4(FF_BillboardShader("rect"), -0.5F, -0.5F, 0.5F, 0.5F)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FF_BillboardShader.StopUse()

        GL.Disable(EnableCap.Blend)

        unbind_textures(0)

    End Sub

    Private Sub draw_map_cursor()
        GL_PUSH_GROUP("draw_map_cursor")

        DecalProject.Use()

        GL.Uniform3(DecalProject("color_in"), 0.4F, 0.3F, 0.3F)

        CURSOR_TEXTURE_ID.BindUnit(0)
        FBOm.gDepth.BindUnit(1)
        FBOm.gGMF.BindUnit(2)

        ' Track the terrain at Y
        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, CURSOR_Y, U_LOOK_AT_Z)
        Dim model_S = Matrix4.CreateScale(25.0F, 50.0F, 25.0F)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        'GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DecalProject("DecalMatrix"), False, rotate * model_S * model_X)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DecalProject.StopUse()
        unbind_textures(3)

        GL_POP_GROUP()
    End Sub

#Region "miniMap"

    Private Sub draw_mini_map()
        'check if we have the mini map loaded.
        If theMap.MINI_MAP_ID Is Nothing Then
            Return
        End If
        GL_PUSH_GROUP("draw_mini_map")

        GL.DepthMask(False)
        GL.Disable(EnableCap.DepthTest)

        '===========================================================================
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
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, miniFBO) '================
            Ortho_MiniMap(MINI_MAP_SIZE)
            FBOmini.attach_gcolor()
            'render to gcolor and blit it to the screeenTexture buffer
            GL.ClearColor(0.0, 0.0, 0.5, 0.0)
            GL.Clear(ClearBufferMask.ColorBufferBit)
            Draw_mini()
            draw_mini_position()
        Else
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, miniFBO) '================
            Ortho_MiniMap(MINI_MAP_SIZE)
            FBOmini.attach_gcolor()
            draw_mini_position()
        End If

        get_world_Position_In_Minimap_Window(M_POS)
        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
        '===========================================================================
        Ortho_main()
        Dim size = frmMain.glControl_main.Size
        Dim cx = size.Width - MINI_MAP_SIZE
        Dim cy = size.Height - MINI_MAP_SIZE
        draw_image_rectangle(New RectangleF(cx, cy,
                                                MINI_MAP_SIZE, MINI_MAP_SIZE),
                                                FBOmini.gColor, False)

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
        MINI_TRIM_HORZ_ID.BindUnit(0)
        Dim rect As New RectangleF(cx - 12, cy - 12, 640 + 12, 16.0F)
        GL.Uniform4(TextRenderShader("rect"),
                  rect.Left,
                  -rect.Top,
                  rect.Right,
                  -rect.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'draw vert trim
        MINI_TRIM_VERT_ID.BindUnit(0)
        rect = New RectangleF(cx - 12, cy - 12, 16.0F, 640 + 12.0F)
        GL.Uniform4(TextRenderShader("rect"),
                 rect.Left,
                 -rect.Top,
                 rect.Right,
                 -rect.Bottom)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        '=======================================================================

        'row
        '=======================================================================
        GL.Uniform1(TextRenderShader("divisor"), 10.0F) 'atlas size

        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        MINI_NUMBERS_ID.BindUnit(0)

        Dim index! = 0
        Dim cnt! = 10.0F
        Dim step_s! = MINI_MAP_SIZE / 10.0F
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
        MINI_LETTERS_ID.BindUnit(0)

        cnt! = 10.0F
        step_s! = MINI_MAP_SIZE / 10.0F
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
        If BASE_RINGS_LOADED Then
            draw_mini_base_rings()
        End If
        '======================================================

        '======================================================
        If BASE_RINGS_LOADED Then
            draw_mini_base_ids()
        End If
        '======================================================

        '======================================================
        draw_mini_grids_lines()
        '======================================================

        'now, bilt this to screenTexture
        FBOmini.attach_both()
        FBOmini.blit_to_screenTexture()
        FBOmini.attach_gcolor()


        GL_POP_GROUP()
    End Sub

    Private Sub draw_minimap_texture()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        Dim rect As New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)
        image2dShader.Use()
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        theMap.MINI_MAP_ID.BindUnit(0)
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
        unbind_textures(0)
    End Sub

    Private Sub draw_mini_base_ids()
        GL_PUSH_GROUP("draw_mini_base_ids")

        'need to scale with the map
        Dim i_size = 30.0F

        Dim pos_t1 As New RectangleF(-TEAM_1.X + i_size, -TEAM_1.Z - i_size, -i_size * 2, i_size * 2)
        Dim pos_t2 As New RectangleF(-TEAM_2.X + i_size, -TEAM_2.Z - i_size, -i_size * 2, i_size * 2)

        image2dShader.Use()

        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        'Icon 1
        TEAM_1_ICON_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t1.Left,
            pos_t1.Top,
            pos_t1.Right,
            pos_t1.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Icon 2
        TEAM_2_ICON_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t2.Left,
            pos_t2.Top,
            pos_t2.Right,
            pos_t2.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Reset
        unbind_textures(0)
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
        GL.Enable(EnableCap.Blend)
        GL_PUSH_GROUP("draw_mini_position")

        FBOmini.attach_both()
        FBOmini.blit_to_gBuffer() ' copy prerendered to screenTexture
        FBOmini.attach_gcolor()
        'GoTo skip

        image2dShader.Use()
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        Dim i_size = 32
        Dim pos As New RectangleF(-i_size, -i_size, i_size * 2, i_size * 2)

        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, -U_LOOK_AT_Z, 0.0F)
        Dim model_R = Matrix4.CreateRotationZ(U_CAM_X_ANGLE)
        Dim modelMatrix = model_R * model_X

        DIRECTION_TEXTURE_ID.BindUnit(0)
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
skip:
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

    Private Sub draw_main_Quad(w As Integer, h As Integer)
        GL.Uniform4(deferredShader("rect"), 0.0F, CSng(-h), CSng(w), 0.0F)
        GL.BindVertexArray(defaultVao)
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
        If VTL.Z >= p.y Or VBR.Z >= p.y Then Return False

        Return True
    End Function

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
        GL.BindVertexArray(defaultVao)
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
                         ByRef color As OpenTK.Graphics.Color4,
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
        GL.BindVertexArray(defaultVao)
        For Each l In text
            Dim idx = ASCII_CHARACTERS.IndexOf(l) + 1
            Dim tp = (locX + cnt * 10.0) - cntr
            If tp > wrapWidth And idx = 0 Then
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

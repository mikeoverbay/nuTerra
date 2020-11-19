Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Dim temp_timer As New Stopwatch
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Private cull_timer As New Stopwatch
    Private uv_location As New Vector2
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
        GL.ClearDepth(0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        '===========================================================================
        'draw sun
        FBOm.attach_C()
        'GL.FrontFace(FrontFaceDirection.Ccw)
        GL.Disable(EnableCap.DepthTest)
        If TERRAIN_LOADED And DONT_BLOCK_SKY Then Draw_SkyDome()
        If TERRAIN_LOADED And DONT_BLOCK_SKY Then draw_sun()
        '===========================================================================
        'GL States 
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        '===========================================================================

        'Model depth pass only
        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            '=======================================================================
            FBOm.attach_C()
            model_depth_pass() '=========================================================
            '=======================================================================
        End If

        FBOm.attach_CNGPA()

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
            'restore settings after projected objects are drawn
            GL.Disable(EnableCap.Blend)
            GL.DepthMask(True)
            GL.Disable(EnableCap.CullFace)
            FBOm.attach_Depth()
            GL.FrontFace(FrontFaceDirection.Ccw)
        End If


        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            '=======================================================================
            FBOm.attach_CNGP()
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
        'final render. Either direct to default or use SSAA process.

        FBOm.attach_C1_and_C2()

        render_deferred_buffers()
        '                        BlitFramebufferFilter.Linear)
        copy_color_2_to_gColor()


        '===========================================================================
        'DEFAUL BUFFER ATTACH!!!
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        '===========================================================================

        perform_SSAA_Pass()

        '===========================================================================
        'hopefully, this will look like glass :)
        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            copy_default_to_gColor()
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
            global_noise()

            GL.Disable(EnableCap.DepthTest)
            GL.DepthMask(True)
            GL.Disable(EnableCap.CullFace)
            GL.FrontFace(FrontFaceDirection.Ccw)
        End If
#End If
        '===========================================================================

        '===========================================================================
        'If MODELS_LOADED And DONT_BLOCK_MODELS Then
        '    copy_default_to_gColor()
        '    GL.DepthMask(False)
        '    GL.Disable(EnableCap.DepthTest)
        '    FBOm.attach_C_no_Depth()
        '    GL.DepthMask(False)
        '    GL.FrontFace(FrontFaceDirection.Cw)
        '    GL.Enable(EnableCap.Blend)
        '    GL.Enable(EnableCap.CullFace)
        'End If

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
    Dim rotate As Matrix4 = Nothing
    Dim model_S As Matrix4 = Nothing
    Dim model_X As Matrix4 = Nothing
    '=============================================================================================
    Private Sub global_noise()
        GL_PUSH_GROUP("perform_Fog_Noise_pass")

        Dim s = 0.03F * DELTA_TIME ' <---- How fast the fog moves

        'this is in the game data somewhere!
        Dim move_vector = New Vector2(0.3, 0.7) ' <----  Direction the fog moves

        uv_location += move_vector * s '<----  do the math;

        DeferredDecalProjectShader.Use()

        GL.Uniform3(DeferredDecalProjectShader("fog_tint"), FOG_COLOR.X, FOG_COLOR.Y, FOG_COLOR.Z)
        GL.Uniform1(DeferredDecalProjectShader("uv_scale"), 4.0F)
        GL.Uniform2(DeferredDecalProjectShader("move_vector"), uv_location.X, uv_location.Y)

        NOISE_id.BindUnit(0)
        FBOm.gDepth.BindUnit(1)
        FBOm.gPosition.BindUnit(2)
        FBOm.gColor.BindUnit(3)
        FBOm.gColor_2.BindUnit(4)

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

        GL.UniformMatrix4(DeferredDecalProjectShader("DecalMatrix"), False, rotate * model_S * model_X)

        GL.BindVertexArray(CUBE_VAO)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DeferredDecalProjectShader.StopUse()

        unbind_textures(2)

        GL_POP_GROUP()
    End Sub
    '=============================================================================================
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
    Private Sub fog_pass()
        GL_PUSH_GROUP("perform_Fog_Pass")

        Dim fog_color As New Vector3(0.5, 0.5, 0.8)
        GL.Disable(EnableCap.DepthTest)

        gBufferFogShader.Use()
        GL.DepthMask(False)

        GL.UniformMatrix4(gBufferFogShader("ProjectionMatrix"), False, PROJECTIONMATRIX)


        GL.Uniform1(gBufferFogShader("gPosition"), 0)
        GL.Uniform1(gBufferFogShader("gColor"), 1)
        GL.Uniform1(gBufferFogShader("gGMF"), 2)

        GL.Uniform3(gBufferFogShader("fog_color_in"), fog_color.X, fog_color.Y, fog_color.Z)
        GL.Uniform1(gBufferFogShader("Fog_density"), 0.2)
        GL.Uniform1(gBufferFogShader("viewDistance"), 3000)



        GL.Uniform1(gBufferFogShader("AMBIENT"), frmLightSettings.lighting_ambient)
        GL.Uniform1(gBufferFogShader("FOG_LEVEL"), frmLightSettings.lighting_fog_level)

        FBOm.gPosition.BindUnit(0)
        FBOm.gColor.BindUnit(1)
        FBOm.gGMF.BindUnit(2)

        'draw full screen quad
        GL.Uniform4(glassPassShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        gBufferFogShader.StopUse()
        unbind_textures(2)

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

        GL.Uniform1(deferredShader("light_count"), LIGHTS.index - 1)
        GL.Uniform1(deferredShader("mapMaxHeight"), MAX_MAP_HEIGHT)
        GL.Uniform1(deferredShader("mapMinHeight"), MIN_MAP_HEIGHT)
        GL.Uniform1(deferredShader("MEAN"), CSng(MEAN_MAP_HEIGHT))

        GL.Uniform3(deferredShader("fog_tint"), FOG_COLOR.X, FOG_COLOR.Y, FOG_COLOR.Z)

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

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(6) ' unbind all the used texture slots

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub
    '=============================================================================================

    Private Sub draw_sun()

        FBOm.attach_C()

        'test only
        'Dim matrix = Matrix4.CreateTranslation(New Vector3(0F, 100.0F, 0F))
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
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

    Private Sub frustum_cull()
        GL_PUSH_GROUP("frustum_cull")

        'clear atomic counter
        GL.ClearNamedBufferSubData(MapGL.Buffers.parameters.buffer_id, PixelInternalFormat.R32ui, IntPtr.Zero, 3 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

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

        FBOm.gColor.BindUnit(0)
        CC_LUT_ID.BindUnit(1)

        'draw full screen quad
        GL.Uniform4(colorCorrectShader("rect"), 0.0F, CSng(-FBOm.SCR_HEIGHT), CSng(FBOm.SCR_WIDTH), 0.0F)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        colorCorrectShader.StopUse()
        unbind_textures(1)

    End Sub

    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(FBOm.gColor.texture_id, 0, 0, 0, 0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
    End Sub

    Private Sub copy_color_2_to_gColor()
        GL.ReadBuffer(ReadBufferMode.ColorAttachment6)
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0)
        GL.BlitFramebuffer(0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                                0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                                0,
                                BlitFramebufferFilter.Linear)
        Dim er = GL.GetError
        Dim er1 = GL.GetError

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

        GL.Uniform1(TerrainLQShader("waterAlpha"), Map_wetness.waterAlpha)

        GL.Uniform2(TerrainLQShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(TerrainLQShader("map_center"), -b_x_min, b_y_max)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk

                GL.Uniform1(TerrainLQShader("map_id"), CSng(i))


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


        theMap.GLOBAL_AM_ID.BindUnit(21)

        FBO_mixer_set.gColorArray.BindUnit(22)
        FBO_mixer_set.gNormalArray.BindUnit(23)
        FBO_mixer_set.gGmmArray.BindUnit(24)

        'water BS
        GL.Uniform3(TerrainShader("waterColor"),
                        Map_wetness.waterColor.X,
                        Map_wetness.waterColor.Y,
                        Map_wetness.waterColor.Z)

        GL.Uniform1(TerrainShader("waterAlpha"), Map_wetness.waterAlpha)

        GL.Uniform2(TerrainShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(TerrainShader("map_center"), -b_x_min, b_y_max)

        GL.Uniform1(TerrainShader("show_test"), SHOW_TEST_TEXTURES)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible And Not theMap.render_set(i).LQ Then
                TERRAIN_TRIS_DRAWN += 8192 ' number of triangles per chunk

                GL.Uniform1(TerrainShader("map_id"), CSng(i))

                GL.UniformMatrix4(TerrainShader("modelMatrix"), False, theMap.render_set(i).matrix)

                GL.UniformMatrix3(TerrainShader("normalMatrix"), True, Matrix3.Invert(New Matrix3(PerViewData.view * theMap.render_set(i).matrix))) 'NormalMatrix
                GL.Uniform2(TerrainShader("me_location"), theMap.chunks(i).location.X, theMap.chunks(i).location.Y) 'me_location

                'bind all the data for this chunk
                With theMap.render_set(i)
                    .layersStd140_ubo.BindBase(0)

                    'AM maps
                    .TexLayers(0).AM_id1.BindUnit(1)
                    .TexLayers(1).AM_id1.BindUnit(2)
                    .TexLayers(2).AM_id1.BindUnit(3)
                    .TexLayers(3).AM_id1.BindUnit(4)

                    .TexLayers(0).AM_id2.BindUnit(5)
                    .TexLayers(1).AM_id2.BindUnit(6)
                    .TexLayers(2).AM_id2.BindUnit(7)
                    .TexLayers(3).AM_id2.BindUnit(8)

                    'NM maps
                    .TexLayers(0).NM_id1.BindUnit(9)
                    .TexLayers(1).NM_id1.BindUnit(10)
                    .TexLayers(2).NM_id1.BindUnit(11)
                    .TexLayers(3).NM_id1.BindUnit(12)

                    .TexLayers(0).NM_id2.BindUnit(13)
                    .TexLayers(1).NM_id2.BindUnit(14)
                    .TexLayers(2).NM_id2.BindUnit(15)
                    .TexLayers(3).NM_id2.BindUnit(16)

                    'bind blend textures
                    .TexLayers(0).Blend_id.BindUnit(17)
                    .TexLayers(1).Blend_id.BindUnit(18)
                    .TexLayers(2).Blend_id.BindUnit(19)
                    .TexLayers(3).Blend_id.BindUnit(20)

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

    Private Sub model_depth_pass()
        'This is just to depth pass write to allow early z reject and stop
        ' wetness from showing through the models.
        GL_PUSH_GROUP("model_depth_pass")

        '------------------------------------------------
        mDepthWriteShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.ColorMask(False, False, False, False)
        GL.Enable(EnableCap.CullFace)

        MapGL.Buffers.parameters.Bind(GL_PARAMETER_BUFFER_ARB)
        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)

        MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, IntPtr.Zero, MapGL.indirectDrawCount, 0)

        GL.Disable(EnableCap.CullFace)

        MapGL.Buffers.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, New IntPtr(8), MapGL.indirectDrawCount, 0)

        mDepthWriteShader.StopUse()
        GL.ColorMask(True, True, True, True)

        GL.Enable(EnableCap.CullFace)

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
        ' Color highlighting of LOD levels if enabled.
        GL.Uniform1(modelShader("show_Lods"), SHOW_LOD_COLORS)

        'assign subroutines
        GL.UniformSubroutines(ShaderType.FragmentShader, indices.Length, indices)

        GL.Enable(EnableCap.CullFace)

        MapGL.Buffers.parameters.Bind(GL_PARAMETER_BUFFER_ARB)

        GL.BindVertexArray(MapGL.VertexArrays.allMapModels)

        MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, IntPtr.Zero, MapGL.indirectDrawCount, 0)

        GL.Disable(EnableCap.CullFace)

        MapGL.Buffers.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, New IntPtr(8), MapGL.indirectDrawCount, 0)

        modelShader.StopUse()

        GL.DepthFunc(DepthFunction.Greater)

        FBOm.attach_CNGPA()
        GL.DepthMask(True)

        '------------------------------------------------
        modelGlassShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------

        MapGL.Buffers.indirect_glass.Bind(BufferTarget.DrawIndirectBuffer)
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

            MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
            MapGL.Buffers.parameters.Bind(GL_PARAMETER_BUFFER_ARB)
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

        GL.Uniform1(TerrainGrids("show_border"), SHOW_BORDER)
        GL.Uniform1(TerrainGrids("show_chunks"), SHOW_CHUNKS)
        GL.Uniform1(TerrainGrids("show_grid"), SHOW_GRID)

        GL.Uniform1(TerrainGrids("gGMF"), 0)

        FBOm.gGMF.BindUnit(0)

        For i = 0 To theMap.render_set.Length - 1
            GL.UniformMatrix4(TerrainGrids("model"), False, theMap.render_set(i).matrix)

            'draw chunk
            GL.BindVertexArray(theMap.render_set(i).VAO)
            GL.DrawElements(PrimitiveType.Triangles,
                24576,
                DrawElementsType.UnsignedShort, 0)
        Next
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

        GL.Uniform1(FXAAShader("pass_through"), CInt(SSAA_enable))

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
        'GL.Enable(EnableCap.Blend)
        'GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        FBOm.attach_CNGP()

        SkyDomeShader.Use()

        GL.Enable(EnableCap.CullFace)

        theMap.Sky_Texture_Id.BindUnit(0)

        GL.BindVertexArray(theMap.skybox_mdl.vao)
        GL.DrawElements(PrimitiveType.Triangles, theMap.skybox_mdl.indices_count * 3, DrawElementsType.UnsignedShort, 0)

        SkyDomeShader.StopUse()
        unbind_textures(0)
        GL.Disable(EnableCap.CullFace)
        'GL.Disable(EnableCap.Blend)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)

        GL_POP_GROUP()
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

    Private Sub draw_terrain_base_rings()

        If Not BASE_RINGS_LOADED Then
            Return
        End If

        GL_PUSH_GROUP("draw_terrain_base_rings")

        BaseRingProjector.Use()

        GL.Uniform1(BaseRingProjector("depthMap"), 0)
        GL.Uniform1(BaseRingProjector("gGMF"), 1)
        FBOm.gDepth.BindUnit(0)
        FBOm.gGMF.BindUnit(1)

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
        If theMap.MINI_MAP_ID Is Nothing Then
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
        GL.BindVertexArray(defaultVao)
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
        MINI_LETTERS_ID.BindUnit(0)

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

        theMap.MINI_MAP_ID.BindUnit(0)
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
        unbind_textures(0)
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
        GL_PUSH_GROUP("draw_mini_position")

        image2dShader.Use()

        GL.Uniform1(image2dShader("imageMap"), 0)
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
        GL.Uniform1(TextRenderShader("divisor"), 95.0F) 'atlas size
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
        GL.Uniform1(TextRenderShader("divisor"), 95.0F) 'atlas size
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
        Dim ar = text.ToArray
        Dim cnt As Integer = 0
        GL.BindVertexArray(defaultVao)
        Dim wrap As Boolean = False
        For Each l In ar
            Dim idx = CSng(Asc(l) - 32)
            Dim tp = (locX + cnt * 10.0) - cntr
            If tp > wrapWidth And idx = 0 Then
                cnt = -1
                locY += 19
            End If
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

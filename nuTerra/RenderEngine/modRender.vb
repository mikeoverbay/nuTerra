Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Public Sub draw_scene()

        '===========================================================================
        ' FLAG INFO
        ' 0  = No shading
        ' 64  = model 
        ' 255 = sky dome. We will want to control brightness
        ' more as they are added
        '===========================================================================
        'house keeping
        FRAME_TIMER.Restart()
        '===========================================================================

        frmMain.glControl_main.MakeCurrent()
        '===========================================================================
        HOG_TIME = 20 ' <- this probably needs to be set lower when we are done. 3?
        If SHOW_MAPS_SCREEN Then
            gl_pick_map(MOUSE.X, MOUSE.Y)
            HOG_TIME = 16
            Return
        End If
        If SHOW_LOADING_SCREEN Then
            draw_loading_screen()
            HOG_TIME = 16
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
        frustum_cull()
        '===========================================================================

        '===========================================================================
        FBOm.attach_CNGP() 'clear ALL gTextures!
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        '===========================================================================

        '===========================================================================
        Draw_SkyDome() '
        '===========================================================================

        '===========================================================================
        'GL States
        GL.Enable(EnableCap.DepthTest)
        '===========================================================================

        '===========================================================================
        Draw_Light_Orb() '==========================================================
        '===========================================================================

        '===========================================================================
        draw_cross_hair() '=========================================================
        '===========================================================================

        FBOm.attach_CNGP()
        Dim er0 = GL.GetError

        If MODELS_LOADED Then
            '===========================================================================
            draw_models() '=============================================================
            '===========================================================================

            '===========================================================================
            draw_overlays() '===========================================================
            '===========================================================================
        End If

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
        'render_test_compute() '=================================================
        '===========================================================================


        '===========================================================================
        render_HUD() '==============================================================
        '===========================================================================

        '===========================================================================
        draw_mini_map() '===========================================================
        '===========================================================================

        '===========================================================================
        frmMain.glControl_main.SwapBuffers() '======================================
        '===========================================================================

        If frmGbufferViewer.Visible Then
            frmGbufferViewer.update_screen()
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

    Private Sub draw_models()
        'SOLID FILL
        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
            GL.PolygonOffset(1.2, 0.2)
            GL.Enable(EnableCap.PolygonOffsetFill) '<-- Needed for wire overlay
        End If
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.Uniform1(modelShader("colorMap"), 0)
        GL.Uniform1(modelShader("normalMap"), 1)
        GL.Uniform1(modelShader("GMF_Map"), 2)
        GL.Uniform1(modelShader("nMap_type"), N_MAP_TYPE)

        GL.UniformMatrix4(modelShader("projection"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(modelShader("view"), False, VIEWMATRIX)

        GL.ActiveTexture(TextureUnit.Texture0 + 0)
        GL.BindTexture(TextureTarget.Texture2D, m_color_id) '<----------------- Texture Bind
        GL.ActiveTexture(TextureUnit.Texture0 + 1)
        GL.BindTexture(TextureTarget.Texture2D, m_normal_id)
        GL.ActiveTexture(TextureUnit.Texture0 + 2)
        GL.BindTexture(TextureTarget.Texture2D, m_gmm_id)

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

    Dim temp_timer As New Stopwatch

    Private Sub render_deferred_buffers()
        '===========================================================================
        ' Test our deferred shader =================================================
        '===========================================================================

        deferredShader.Use()

        'set up uniforms
        GL.Uniform1(deferredShader("gColor"), 0)
        GL.Uniform1(deferredShader("gNormal"), 1)
        'GL.Uniform1(deferredShader("gGMF"), 2) ' ignore this for now
        GL.Uniform1(deferredShader("gPosition"), 3)
        GL.Uniform1(deferredShader("gDepth"), 4)

        'Lighting settings
        GL.Uniform1(deferredShader("AMBIENT"), frmLighting.lighting_ambient)
        GL.Uniform1(deferredShader("BRIGHTNESS"), frmLighting.lighting_terrain_texture)
        GL.Uniform1(deferredShader("SPECULAR"), frmLighting.lighting_specular_level)
        GL.Uniform1(deferredShader("GRAY_LEVEL"), frmLighting.lighting_gray_level)
        GL.Uniform1(deferredShader("GAMMA_LEVEL"), frmLighting.lighting_gamma_level)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, MODELVIEWMATRIX_Saved)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)

        GL.ActiveTexture(TextureUnit.Texture1)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gNormal)

        GL.ActiveTexture(TextureUnit.Texture2)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gGMF)

        GL.ActiveTexture(TextureUnit.Texture3)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gPosition)

        GL.ActiveTexture(TextureUnit.Texture4)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gDepth)


        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(4) ' unbind all the used texture slots

        deferredShader.StopUse()
    End Sub

    ''' <summary>
    ''' renders all 2D things in ortho mode
    ''' </summary>
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
        txt = String.Format("mouse {0} {1}", MINI_WORLD_MOUSE_POSITION.X.ToString, MINI_WORLD_MOUSE_POSITION.Y.ToString)
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
        If MINI_MAP_SIZE <> MINI_MAP_NEW_SIZE Then
            If MINI_MAP_SIZE < MINI_MAP_NEW_SIZE Then
                MINI_MAP_SIZE += 10
            Else
                MINI_MAP_SIZE -= 10
            End If
            FBOmini.FBO_Initialize(MINI_MAP_SIZE)
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

        draw_image_rectangle(New RectangleF(size.Width - MINI_MAP_SIZE, size.Height - MINI_MAP_SIZE,
                                            MINI_MAP_SIZE, MINI_MAP_SIZE),
                                            FBOmini.gColor)



        GL.DepthMask(True)

    End Sub
    Private Sub Draw_mini()

        '======================================================
        'Draw all the shit on top of this image
        draw_minimap_texture()
        '======================================================

        '======================================================
        draw_base_rings()
        '======================================================

        '======================================================
        draw_base_ids()
        '======================================================

        '======================================================
        draw_grids_lines()
        '======================================================

        '======================================================
        get_world_Position_In_Minimap_Window(M_POS)
        '======================================================

    End Sub

    Private Sub draw_base_ids()

        GL.Enable(EnableCap.Blend) 'transparent Icons

        'need to scale with the map
        Dim i_size = 30.0F

        Dim pos_t1 As New RectangleF(TEAM_1.X - i_size, -TEAM_1.Z - i_size, i_size * 2, i_size * 2)
        Dim pos_t2 As New RectangleF(TEAM_2.X - i_size, -TEAM_2.Z - i_size, i_size * 2, i_size * 2)

        image2dShader.Use()

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.Uniform1(image2dShader("imageMap"), 0)

        'Icon 1
        GL.BindTexture(TextureTarget.Texture2D, TEAM_1_ICON_ID)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t1.Left,
            pos_t1.Top,
            pos_t1.Right,
            pos_t1.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Icon 2
        GL.BindTexture(TextureTarget.Texture2D, TEAM_2_ICON_ID)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t2.Left,
            pos_t2.Top,
            pos_t2.Right,
            pos_t2.Bottom)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Reset
        GL.BindTexture(TextureTarget.Texture2D, 0)
        image2dShader.StopUse()
        GL.Disable(EnableCap.Blend)

    End Sub

    Private Sub draw_minimap_texture()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        draw_image_rectangle(New RectangleF(MAP_BB_BL.X, MAP_BB_UR.Y + 0.5,
                                           w, -h),
                                            theMap.MINI_MAP_ID)
    End Sub

    Private Sub draw_base_rings()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        'draw base rings
        MiniMapRingsShader.Use()
        'constants
        Dim er0 = GL.GetError
        GL.UniformMatrix4(MiniMapRingsShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(MiniMapRingsShader("radius"), 50.0F)
        GL.Uniform1(MiniMapRingsShader("thickness"), 2.0F)
        Dim er3 = GL.GetError

        Dim m_size = New RectangleF(MAP_BB_BL.X, MAP_BB_UR.Y, w, -h)

        Dim er1 = GL.GetError
        GL.Uniform4(MiniMapRingsShader("rect"),
            m_size.Left,
            -m_size.Top,
            m_size.Right,
            -m_size.Bottom)

        GL.Uniform2(MiniMapRingsShader("center"), -TEAM_2.X, TEAM_2.Z)
        GL.Uniform4(MiniMapRingsShader("color"), OpenTK.Graphics.Color4.DarkRed)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        GL.Uniform2(MiniMapRingsShader("center"), -TEAM_1.X, TEAM_1.Z)
        GL.Uniform4(MiniMapRingsShader("color"), OpenTK.Graphics.Color4.DarkGreen)

        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        MiniMapRingsShader.StopUse()

    End Sub

    Private Sub draw_grids_lines()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        GL.Enable(EnableCap.Blend) 'so the lines are not so bold
        coloredline2dShader.Use()

        Dim co As OpenTK.Graphics.Color4
        co = OpenTK.Graphics.Color4.GhostWhite
        co.A = 0.5F

        GL.UniformMatrix4(coloredline2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(coloredline2dShader("color"), co)
        For x = MAP_BB_BL.X To MAP_BB_UR.X - 100.0F Step 100.0F
            Dim pos As New RectangleF(x - 0.78, MAP_BB_BL.Y, 0.0F, h)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)

            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next
        For y = MAP_BB_BL.Y To MAP_BB_UR.Y - 100 Step 100.0F
            Dim pos As New RectangleF(MAP_BB_BL.X - 0.78, y, w, 0.0F)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)
            GL.BindVertexArray(defaultVao)
            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next
        GL.Disable(EnableCap.Blend)
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
        MINI_MOUSE_CAPTURED = True
        If pos.X <= 0.0F Then
            MINI_WORLD_MOUSE_POSITION.X = -pos.X * MAP_BB_UR.X
        Else
            MINI_WORLD_MOUSE_POSITION.X = pos.X * MAP_BB_BL.X
        End If
        If pos.Y >= 0.0F Then
            MINI_WORLD_MOUSE_POSITION.Y = -pos.Y * MAP_BB_UR.Y
        Else
            MINI_WORLD_MOUSE_POSITION.Y = pos.Y * MAP_BB_BL.Y
        End If
        Return
    End Sub
#End Region

    Private Sub Draw_SkyDome()

        GL.DepthMask(False)
        FBOm.attach_CF()
        SkyDomeShader.Use()
        GL.FrontFace(FrontFaceDirection.Cw)
        Dim model = Matrix4.CreateTranslation(CAM_POSITION.X, CAM_POSITION.Y + 3, CAM_POSITION.Z)
        GL.UniformMatrix4(SkyDomeShader("model"), False, model)
        GL.UniformMatrix4(SkyDomeShader("view"), False, VIEWMATRIX)
        GL.UniformMatrix4(SkyDomeShader("projection"), False, PROJECTIONMATRIX)
        GL.Uniform1(SkyDomeShader("imageMap"), 0)

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, theMap.Sky_Texture_Id)

        GL.BindVertexArray(theMap.skybox_mdl.mdl_VAO)
        GL.DrawElements(PrimitiveType.Triangles,
                        theMap.skybox_mdl.indice_count * 3,
                        DrawElementsType.UnsignedShort,
                        0)
        SkyDomeShader.StopUse()
        GL.BindTexture(TextureTarget.Texture2D, 0)
        GL.FrontFace(FrontFaceDirection.Ccw)
        GL.DepthMask(True)

    End Sub

    Private Sub draw_overlays()
        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            FBOm.attach_CF()

            normalShader.Use()

            GL.UniformMatrix4(normalShader("projection"), False, PROJECTIONMATRIX)
            GL.UniformMatrix4(normalShader("view"), False, VIEWMATRIX)

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

    End Sub
    ''' <summary>
    ''' Unbinds textures from last used to zero
    ''' </summary>
    Private Sub unbind_textures(ByVal start As Integer)
        'doing this backwards leaves TEXTURE0 active :)
        For i = start To 0 Step -1
            GL.ActiveTexture(TextureUnit.Texture0 + i)
            GL.BindTexture(TextureTarget.Texture2D, 0)
        Next
    End Sub

    Private Sub draw_main_Quad(w As Integer, h As Integer)
        GL.Uniform4(deferredShader("rect"), 0.0F, CSng(-h), CSng(w), 0.0F)
        GL.BindVertexArray(defaultVao)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        ' GL.BindVertexArray(0)
    End Sub

End Module

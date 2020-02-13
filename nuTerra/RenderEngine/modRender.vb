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
        ' more as they are added
        '===========================================================================
        'house keeping
        FRAME_TIMER.Restart()
        TOTAL_TRIANGLES_DRAWN = 0
        '===========================================================================

        frmMain.glControl_main.MakeCurrent()
        '===========================================================================
        HOG_TIME = 5 ' <- this probably needs to be set lower when we are done. 3?
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

#If 0 Then ' TODO: gpu culling
        cullShader.Use()

        GL.UniformMatrix4(cullShader("projection"), False, PROJECTIONMATRIX)
        GL.UniformMatrix4(cullShader("view"), False, VIEWMATRIX)

        GL.Enable(EnableCap.RasterizerDiscard)

        For Each batch In MODEL_BATCH_LIST
            Dim model = MAP_MODELS(batch.model_id).mdl

            GL.BindVertexArray(model.render_sets(0).mdl_VAO)

            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points)
            GL.BeginQuery(QueryTarget.PrimitivesGenerated, batch.culledQuery)
            GL.DrawArrays(PrimitiveType.Points, 0, batch.count)
            GL.EndQuery(QueryTarget.PrimitivesGenerated)
            GL.EndTransformFeedback()
        Next

        GL.Disable(EnableCap.RasterizerDiscard)
        cullShader.StopUse()
#End If

        '===========================================================================

        '===========================================================================
        FBOm.attach_CNGP() 'clear ALL gTextures!
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
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

        '===========================================================================
        render_deferred_buffers() '=================================================
        '===========================================================================

        '===========================================================================
        render_HUD() '==============================================================
        '===========================================================================

        '===========================================================================
        frmMain.glControl_main.SwapBuffers() '======================================
        '===========================================================================

        '===========================================================================
        'draw_mini_map() '===========================================================
        '===========================================================================

        If frmGbufferViewer.Visible Then
            frmGbufferViewer.update_screen()
        End If

        FPS_COUNTER += 1

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

        For Each batch In MODEL_BATCH_LIST
            Dim model = MAP_MODELS(batch.model_id).mdl

            If model.junk Then
                Continue For
            End If

            For Each renderSet In model.render_sets
                If renderSet.no_draw Then
                    Continue For
                End If

                'Stop
                Dim triType = If(renderSet.indexSize = 2, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                For Each primGroup In renderSet.primitiveGroups.Values
                    'setup materials here

                    GL.BindVertexArray(renderSet.mdl_VAO)
                    GL.DrawElementsInstanced(PrimitiveType.Triangles,
                                             primGroup.nPrimitives * 3,
                                             triType,
                                             New IntPtr(primGroup.startIndex * renderSet.indexSize),
                                             batch.count)
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
        'ortho for the win
        Ortho_main()

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
        '===========================================================================
        ' Text Rendering ===========================================================
        '===========================================================================

        Dim position = PointF.Empty
        DrawText.clear(Color.FromArgb(0, 0, 0, 255))

        'save this.. we may want to use it for debug with a different source for the values.
        'Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")
        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds
        Dim tr = TOTAL_TRIANGLES_DRAWN * LOOP_COUNT

        Dim txt = String.Format("Culled: {0} | FPS: {1} | Triangles drawn per frame: {2} | Draw time in Milliseconds: {3}", 0, FPS_TIME, tr, elapsed)
        DrawText.DrawString(txt, mono, Brushes.White, position)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        draw_image_rectangle(New RectangleF(0, 0, FBOm.SCR_WIDTH, 20), DrawText.Gettexture)

        GL.Disable(EnableCap.Blend)

        '===========================================================================
        ' Text Rendering End =======================================================
        '===========================================================================

    End Sub

    Private Sub draw_mini_map()
#If 1 Then
        frmMain.glControl_MiniMap.Visible = True
        frmMain.glControl_MiniMap.BringToFront()
        '-------------------------------------------------------
        '2nd glControl
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer
        Dim size As Integer = 100
        frmMain.glControl_MiniMap.MakeCurrent()
        Ortho_MiniMap(size) ' <--- set size of the square in lower right corner.

        GL.ClearColor(0.5F, 0.2F, 0.2F, 1.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)
        GL.Disable(EnableCap.DepthTest)

        Dim cx, cy, x, y As Single

        cx = frmMain.glControl_MiniMap.Width / 2
        cy = -frmMain.glControl_MiniMap.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle2
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * size / 2 + cx
            y = Sin(k + j) * size / 2 + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle2 += 0.0001
            If angle2 > PI * 2 / 40 Then
                angle2 = 0
            End If
        Next
        frmMain.glControl_MiniMap.SwapBuffers()
#Else
        frmMain.glControl_MiniMap.Visible = False
#End If
    End Sub

    Private Sub draw_overlays()
        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
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

                    GL.BindVertexArray(renderSet.mdl_VAO)
                    Dim triType = If(renderSet.indexSize = 2, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                    For Each primGroup In renderSet.primitiveGroups.Values
                        GL.DrawElementsInstanced(PrimitiveType.Triangles,
                                                 primGroup.nPrimitives * 3,
                                                 triType,
                                                 New IntPtr(primGroup.startIndex * renderSet.indexSize),
                                                 batch.count)
                    Next
                Next
            Next
            normalShader.StopUse()
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
        GL.BindVertexArray(0)

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
        GL.BindVertexArray(0)
    End Sub

End Module

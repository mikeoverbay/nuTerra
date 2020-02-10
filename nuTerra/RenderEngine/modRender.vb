Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modRender
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Public Sub draw_scene()

        FRAME_TIMER.Restart()

        frmMain.glControl_main.MakeCurrent()

        HOG_TIME = 5
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

        TOTAL_TRIANGLES_DRAWN = 0


        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) ' Use FBO_main buffer
        FBOm.attach_CNGP()
        '-------------------------------------------------------

        set_prespective_view() ' <-- sets camera and prespective view
        'after camera is set
        CULLED_COUNT = 0
        ExtractFrustum()
        If MODELS_LOADED Then
            check_models_visible()
        End If

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        '------------------------------------------------
        '------------------------------------------------
        'GL States
        GL.Enable(EnableCap.DepthTest)
        GL.Enable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)
        GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)
        '------------------------------------------------
        '------------------------------------------------

        '------------------------------------------------
        '------------------------------------------------
        'Draw temp light positon.
        FBOm.attach_C()
        Dim v As New Vector3
        v.X = LIGHT_POS(0) : v.Y = LIGHT_POS(1) : v.Z = LIGHT_POS(2)
        'unremming this screws up the VertexAttribPointers 
        draw_one_damn_moon(v)
        '------------------------------------------------
        '------------------------------------------------
        'Draw the cross hair if we are moving the look_at location
        If MOVE_MOD Or Z_MOVE Then
            If MOVE_MOD And Not Z_MOVE Then
                frmMain.glControl_main.Cursor = Cursors.SizeAll
            End If
            If Z_MOVE Then
                frmMain.glControl_main.Cursor = Cursors.SizeNS
            End If
            FBOm.attach_C()
            draw_cross_hair()
        Else
            frmMain.glControl_main.Cursor = Cursors.Default
        End If
        '------------------------------------------------
        '------------------------------------------------
        FBOm.attach_CNGP()

        '===========================================================================
        'draw the test MDL model using VAO =========================================
        '===========================================================================
        'SOLID FILL
        If WIRE_MODELS Or NORMAL_DISPLAY_MODE > 0 Then
            GL.PolygonOffset(1, 1)
            GL.Enable(EnableCap.PolygonOffsetFill) '<-- Needed for wire overlay
        End If
        '------------------------------------------------
        modelShader.Use()  '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.Uniform1(modelShader("colorMap"), 0)
        GL.Uniform1(modelShader("normalMap"), 1)
        GL.Uniform1(modelShader("GMF_Map"), 2)
        GL.Uniform1(modelShader("nMap_type"), N_MAP_TYPE)


        GL.ActiveTexture(TextureUnit.Texture0 + 0)
        GL.BindTexture(TextureTarget.Texture2D, m_color_id) '<----------------- Texture Bind
        GL.ActiveTexture(TextureUnit.Texture0 + 1)
        GL.BindTexture(TextureTarget.Texture2D, m_normal_id)
        GL.ActiveTexture(TextureUnit.Texture0 + 2)
        GL.BindTexture(TextureTarget.Texture2D, m_gmm_id)

        For z = 0 To MODEL_INDEX_LIST.Length - 2
            Dim idx = MODEL_INDEX_LIST(z).model_index
            Dim model = MAP_MODELS(idx).mdl(0)

            If Not model.junk And Not MODEL_INDEX_LIST(z).Culled Then
                TOTAL_TRIANGLES_DRAWN += model.POLY_COUNT

                Dim modelMatrix = MODEL_INDEX_LIST(z).matrix
                Dim MVM = modelMatrix * MODELVIEWMATRIX
                Dim MVPM = MVM * PROJECTIONMATRIX
                ' need an inverse of the modelmatrix
                Dim normalMatrix As New Matrix3(Matrix4.Invert(MVM))

                GL.UniformMatrix4(modelShader("modelMatrix"), False, MVM)
                GL.UniformMatrix4(modelShader("modelViewProjection"), False, MVPM)
                GL.UniformMatrix3(modelShader("modelNormalMatrix"), True, normalMatrix)

                Dim triType = If(model.USHORTS, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                Dim triSize = If(model.USHORTS, SizeOf(GetType(vect3_16)), SizeOf(GetType(vect3_32)))

                GL.BindVertexArray(model.mdl_VAO)
                For i = 0 To model.primitive_count - 1
                    Dim offset As New IntPtr(model.entries(i).startIndex * triSize)
                    GL.DrawElements(PrimitiveType.Triangles,
                                    model.entries(i).numIndices,
                                    triType,
                                    offset)
                Next
            End If
        Next

        GL.BindVertexArray(0)

        modelShader.StopUse()
        unbind_textures(2) ' unbind all the used texture slots

        '===========================================================================
        ' WIRE OVERLAY =============================================================
        '===========================================================================
        If WIRE_MODELS Then
            FBOm.attach_C()

            GL.Disable(EnableCap.PolygonOffsetFill)
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Line)

            colorOnlyShader.Use()

            GL.Uniform3(colorOnlyShader("color"), 1.0F, 1.0F, 0.0F)


            For z = 0 To MODEL_INDEX_LIST.Length - 2
                Dim idx = MODEL_INDEX_LIST(z).model_index
                Dim model = MAP_MODELS(idx).mdl(0)

                If Not model.junk And Not MODEL_INDEX_LIST(z).Culled Then

                    Dim modelMatrix = MODEL_INDEX_LIST(z).matrix
                    Dim MVM = modelMatrix * MODELVIEWMATRIX
                    Dim MVPM = MVM * PROJECTIONMATRIX
                    GL.UniformMatrix4(colorOnlyShader("ModelMatrix"), False, MVM)
                    GL.UniformMatrix4(colorOnlyShader("ProjectionMatrix"), False, MVPM)

                    Dim triType = If(model.USHORTS, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                    Dim triSize = If(model.USHORTS, SizeOf(GetType(vect3_16)), SizeOf(GetType(vect3_32)))

                    GL.BindVertexArray(model.mdl_VAO)
                    For i = 0 To model.primitive_count - 1
                        Dim offset As New IntPtr(model.entries(i).startIndex * triSize)
                        GL.DrawElements(PrimitiveType.Triangles,
                                        model.entries(i).numIndices,
                                        triType,
                                        offset)
                    Next

                End If

            Next
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)

            colorOnlyShader.StopUse()
        End If

        '===========================================================================
        ' TBN VISUALIZER ===========================================================
        '===========================================================================
        If NORMAL_DISPLAY_MODE > 0 Then
            FBOm.attach_C()
            GL.Disable(EnableCap.PolygonOffsetFill)

            normalShader.Use()

            For z = 0 To MODEL_INDEX_LIST.Length - 2
                Dim idx = MODEL_INDEX_LIST(z).model_index
                Dim model = MAP_MODELS(idx).mdl(0)

                If Not model.junk And Not MODEL_INDEX_LIST(z).Culled Then

                    Dim modelMatrix = MODEL_INDEX_LIST(z).matrix
                    Dim MVM = modelMatrix * MODELVIEWMATRIX
                    Dim MVPM = MVM * PROJECTIONMATRIX

                    GL.UniformMatrix4(normalShader("MVPM"), False, MVPM)

                    GL.Uniform1(normalShader("prj_length"), 0.1F)
                    GL.Uniform1(normalShader("mode"), NORMAL_DISPLAY_MODE) '0 none, 1 by face, 2 by vertex

                    GL.BindVertexArray(MAP_MODELS(idx).mdl(0).mdl_VAO)

                    Dim er0 = GL.GetError
                    Dim triType = If(model.USHORTS, DrawElementsType.UnsignedShort, DrawElementsType.UnsignedInt)
                    Dim triSize = If(model.USHORTS, SizeOf(GetType(vect3_16)), SizeOf(GetType(vect3_32)))

                    GL.BindVertexArray(model.mdl_VAO)
                    For i = 0 To model.primitive_count - 1
                        Dim offset As New IntPtr(model.entries(i).startIndex * triSize)
                        GL.DrawElements(PrimitiveType.Triangles,
                                        model.entries(i).numIndices,
                                        triType,
                                        offset)
                    Next
                End If

            Next
            GL.BindVertexArray(0)
            normalShader.StopUse()

        End If


        '===========================================================================
        '===========================================================================
        'Draws a full screen quad to render FBO textures. ==========================
        '===========================================================================
        '===========================================================================

        'We can now switch to the default hardware buffer.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        'house keeping
        GL.Disable(EnableCap.Blend)

        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.Disable(EnableCap.DepthTest)

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

        'ortho for the win
        Ortho_main()

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform3(deferredShader("LightPos"), LIGHT_POS(0), LIGHT_POS(1), LIGHT_POS(2))

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

        unbind_textures(3) ' unbind all the used texture slots

        deferredShader.StopUse()

        '===========================================================================
        ' Text Rendering ===========================================================
        '===========================================================================

        Dim position = PointF.Empty
        DrawText.clear(Color.FromArgb(0, 0, 0, 255))


        'save this.. we may want to use it for debug with a different source for the values.
        'Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")
        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds
        Dim tr = TOTAL_TRIANGLES_DRAWN * LOOP_COUNT
        Dim txt = String.Format("Culled: {0} | FPS: {1} | Triangles drawn per frame: {2} | Draw time in Milliseconds: {3}", CULLED_COUNT, FPS_TIME, tr, elapsed)
        DrawText.DrawString(txt, mono, Brushes.White, position)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        Dim he As Integer = 20
        draw_image_rectangle(New PointF(0, he), New PointF(FBOm.SCR_WIDTH, 0), DrawText.Gettexture)

        GL.Disable(EnableCap.Blend)


        '===========================================================================
        ' Text Rendering End =======================================================
        '===========================================================================

        frmMain.glControl_main.SwapBuffers() '<---------------------- Buffer Swap
        FPS_COUNTER += 1

        'draw_maps()
        If frmGbufferViewer.Visible Then
            frmGbufferViewer.update_screen()
        End If
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
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

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

    ''' <summary>
    ''' Unbinds textures from last used to zero
    ''' </summary>
    ''' <param name="start"></param>
    ''' <remarks></remarks>
    Private Sub unbind_textures(ByVal start As Integer)
        'doing this backwards leaves TEXTURE0 active :)
        For i = start To 0 Step -1
            GL.ActiveTexture(TextureUnit.Texture0 + i)
            GL.BindTexture(TextureTarget.Texture2D, 0)
        Next
    End Sub

    Private Sub draw_main_Quad_LEGACY(w As Integer, h As Integer)
        GL.Begin(PrimitiveType.Quads)
        '  CCW...
        '  1 ------ 4
        '  |        |
        '  |        |
        '  2 ------ 3
        '
        GL.TexCoord2(0.0F, 1.0F)
        GL.Vertex2(0.0F, 0.0F)

        GL.TexCoord2(0.0F, 0.0F)
        GL.Vertex2(0, -h)

        GL.TexCoord2(1.0F, 0.0F)
        GL.Vertex2(w, -h)

        GL.TexCoord2(1.0F, 1.0F)
        GL.Vertex2(w, 0.0F)
        GL.End()
    End Sub

    Private Sub draw_main_Quad(w As Integer, h As Integer)
        Dim rectVao As Integer
        GL.GenVertexArrays(1, rectVao)
        GL.BindVertexArray(rectVao)

        Dim rectBuffers(1) As Integer
        GL.GenBuffers(2, rectBuffers)

        Dim vertices As Single() = {
            0.0F, 0.0F,
            0.0F, -h,
            w, -h,
            w, 0.0F
            }

        Dim textCoords As Single() = {
            0.0F, 1.0F,
            0.0F, 0.0F,
            1.0F, 0.0F,
            1.0F, 1.0F
            }

        GL.BindBuffer(BufferTarget.ArrayBuffer, rectBuffers(0))
        GL.BufferData(BufferTarget.ArrayBuffer,
                      vertices.Length * SizeOf(GetType(Single)),
                      vertices,
                      BufferUsageHint.StaticDraw)

        ' vertices
        GL.VertexAttribPointer(0,
                               2,
                               VertexAttribPointerType.Float,
                               False,
                               0,
                               0)
        GL.EnableVertexAttribArray(0)


        GL.BindBuffer(BufferTarget.ArrayBuffer, rectBuffers(1))
        GL.BufferData(BufferTarget.ArrayBuffer,
                      textCoords.Length * SizeOf(GetType(Single)),
                      textCoords,
                      BufferUsageHint.StaticDraw)

        ' texcoords
        GL.VertexAttribPointer(1,
                               2,
                               VertexAttribPointerType.Float,
                               False,
                               0,
                               0)
        GL.EnableVertexAttribArray(1)

        GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4)

        GL.BindVertexArray(0)

        GL.DeleteVertexArrays(1, rectVao)
        GL.DeleteBuffers(2, rectBuffers)
    End Sub

End Module

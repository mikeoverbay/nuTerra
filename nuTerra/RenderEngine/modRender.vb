Imports System.Math
Imports System
Imports System.Globalization
Imports System.Threading

Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module modRender
    Public PI As Single = 3.14159274F
    Public angle1, angle2 As Single
    Public Sub draw_scene()

        FRAME_TIMER.Restart()

        frmMain.glControl_main.MakeCurrent()

        If SHOW_MAPS Then
            gl_pick_map(MOUSE.X, MOUSE.Y)
            Return
        End If

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) ' Use FBO_main buffer
        FBOm.attach_CNG()
        '-------------------------------------------------------

        set_prespective_view() ' <-- sets camera and prespective view

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        '------------------------------------------------
        '------------------------------------------------
        'GL States
        GL.Enable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Lighting)
        GL.Enable(EnableCap.CullFace)
        GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill)
        GL.Disable(EnableCap.Blend)
        '------------------------------------------------
        '------------------------------------------------


        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, TextureEnvMode.Replace)
        '------------------------------------------------
        '------------------------------------------------
        'Draw temp light positon.
        FBOm.attach_C()
        Dim v As New vec3
        v.x = LIGHT_POS(0) : v.y = LIGHT_POS(1) : v.z = LIGHT_POS(2)
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
        FBOm.attach_CNG()


#If 1 Then '<----- set to 1 to draw using VAO DrawElements. 0 to draw using display lists
        '===========================================================================
        '===========================================================================
        'draw the test MDL model using VAO
        '------------------------------------------------
        GL.UseProgram(shader_list.MDL_shader) '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.Uniform1(MDL_textureMap_id, 0)
        GL.Uniform1(MDL_normalMap_id, 1)
        GL.Uniform1(MDL_GMF_id, 2)

        GL.Uniform1(MDL_nMap_type_id, N_MAP_TYPE)

        GL.ActiveTexture(TextureUnit.Texture0 + 0)
        GL.BindTexture(TextureTarget.Texture2D, m_color_id) '<------------------------------- Texture Bind
        GL.ActiveTexture(TextureUnit.Texture0 + 1)
        GL.BindTexture(TextureTarget.Texture2D, m_normal_id)
        GL.ActiveTexture(TextureUnit.Texture0 + 2)
        GL.BindTexture(TextureTarget.Texture2D, m_gmm_id)

        Dim er1 = GL.GetError

        GL.BindVertexArray(mdl(0).mdl_VAO)

        For z = 0 To LOOP_COUNT  'set in modGlobalVars.vb
            Dim ox = box_positions(z).x
            Dim oy = box_positions(z).y
            Dim oz = box_positions(z).z

            Dim model = Matrix4.CreateTranslation(ox, oy, oz)

            Dim scale_ As Single = 5.0
            Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)
            Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX
            GL.UniformMatrix4(MDL_modelMatrix_id, False, sMat * model * MODELVIEWMATRIX)
            GL.UniformMatrix4(MDL_modelViewProjection_id, False, MVPM)

            ' need an inverse of the modelmatrix
            Dim MVM = sMat * model * MODELVIEWMATRIX
            Dim normalMatrix As New Matrix3(Matrix4.Invert(MVM))

            GL.UniformMatrix3(MDL_modelNormalMatrix_id, True, normalMatrix)


            For i = 0 To mdl(0).primitive_count - 1
                If mdl(0).USHORTS Then
                    GL.DrawElements(PrimitiveType.Triangles, _
                                    (mdl(0).entries(i).numIndices), _
                                    DrawElementsType.UnsignedShort, mdl(0).index_buffer16((mdl(0).entries(i).startIndex)))
                Else
                    GL.DrawElements(PrimitiveType.Triangles, _
                                    (mdl(0).entries(i).numIndices), _
                                    DrawElementsType.UnsignedInt, mdl(0).index_buffer32((mdl(0).entries(i).startIndex)))
                End If

            Next

            Dim er = GL.GetError
        Next
        GL.BindVertexArray(mdl(0).mdl_VAO)

        GL.UseProgram(0)
        unbind_textures(2) ' unbind all the used texture slots

#Else
        '===========================================================================
        '===========================================================================
        'draw the test display lists
        '------------------------------------------------
        GL.UseProgram(shader_list.testList_shader) '<------------------------------- Shader Bind
        '------------------------------------------------
        GL.Uniform1(testList_textureMap_id, 0)
        GL.Uniform1(testList_normalMap_id, 1)
        GL.Uniform1(testList_GMF_id, 2)

        GL.Uniform1(testList_nMap_type_id, N_MAP_TYPE)

        GL.ActiveTexture(TextureUnit.Texture0 + 0)
        GL.BindTexture(TextureTarget.Texture2D, m_color_id) '<------------------------------- Texture Bind
        GL.ActiveTexture(TextureUnit.Texture0 + 1)
        GL.BindTexture(TextureTarget.Texture2D, m_normal_id)
        GL.ActiveTexture(TextureUnit.Texture0 + 2)
        GL.BindTexture(TextureTarget.Texture2D, m_gmm_id)

        Dim er1 = GL.GetError

        For z = 1 To LOOP_COUNT
            Dim ox = box_positions(z).x
            Dim oy = box_positions(z).y
            Dim oz = box_positions(z).z

            Dim model = Matrix4.CreateTranslation(ox, oy, oz)

            Dim scale_ As Single = 5.0
            Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)
            Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX

            GL.UniformMatrix4(testList_modelMatrix_id, False, sMat * model * MODELVIEWMATRIX)
            GL.UniformMatrix4(testList_modelViewProjection_id, False, MVPM)

            ' need an inverse of the modelmatrix
            Dim MVM = sMat * model * MODELVIEWMATRIX
            Dim normalMatrix As New Matrix3(Matrix4.Invert(MVM))
            GL.UniformMatrix3(testList_modelNormalMatrix_id, True, normalMatrix)


            For i = 0 To mdl(0).primitive_count - 1

                GL.CallList(mdl(0).entries(i).list_id)
            Next



            Dim er = GL.GetError
        Next

        GL.UseProgram(0)
        unbind_textures(2) ' unbind all the used texture slots

#End If
        '===========================================================================
        '===========================================================================
        'Draws a full screen quad to render FBO textures.
        '===========================================================================
        '===========================================================================

        'We can now switch to the default hardware buffer.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        'ortho for the win
        Ortho_main()

        'house keeping
        GL.Disable(EnableCap.Blend)

        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.Disable(EnableCap.DepthTest)

        GL.Enable(EnableCap.Texture2D)

        '===========================================================================
        ' Test our deferred shader =================================================
        '===========================================================================

        GL.UseProgram(shader_list.Deferred_shader)

        'set up uniforms
        GL.Uniform1(deferred_gColor_id, 0)
        GL.Uniform1(deferred_gNormal_id, 1)
        GL.Uniform1(deferred_gGMF_id, 2) ' ignore this for now
        GL.Uniform1(deferred_gDepth_id, 3) ' ignore this for now
        GL.UniformMatrix4(deferred_ModelMatrix, False, MODELVIEWMATRIX)
        GL.UniformMatrix4(deferred_ProjectionMatrix, False, PROJECTIONMATRIX)

        GL.Uniform3(deferred_lightPos, LIGHT_POS(0), LIGHT_POS(1), LIGHT_POS(2))
        GL.Uniform2(deferred_ViewPort, VIEW_PORT(0), VIEW_PORT(1))

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)

        GL.ActiveTexture(TextureUnit.Texture1)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gNormal)

        GL.ActiveTexture(TextureUnit.Texture2)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gGMF)

        GL.ActiveTexture(TextureUnit.Texture3)
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gDepth)




        draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT) 'render Gbuffer lighting

        unbind_textures(3) ' unbind all the used texture slots

        GL.UseProgram(0)

        ' test render some text to see if it works
        Dim position = PointF.Empty
        'textRender.DrawText.TextRenderer(100, 100) '<--- reset when the FBO changes size!
        DrawText.clear(Color.FromArgb(0, 0, 0, 255))
        Dim ti = TimeOfDay.TimeOfDay
        Dim tr = total_triangles_drawn * LOOP_COUNT

        Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")
        Dim tri_count As String = "  Triangles drawn per frame :" + tr.ToString

        Dim elapsed = FRAME_TIMER.ElapsedMilliseconds
        Dim elapsed_str As String = "  Draw time in Milliseconds :" + elapsed.ToString

        Dim fps As String = "FPS:" + FPS_TIME.ToString
        DrawText.DrawString(fps + tri_count + elapsed_str, mono, Brushes.White, position)

        GL.Enable(EnableCap.Texture2D)
        GL.Enable(EnableCap.AlphaTest)
        GL.AlphaFunc(AlphaFunction.Equal, 1.0)
        GL.Color4(1.0F, 1.0F, 1.0F, 0.0F)

        GL.BindTexture(TextureTarget.Texture2D, DrawText.Gettexture)
        GL.Begin(PrimitiveType.Quads)
        Dim he As Integer = 20
        GL.TexCoord2(0.0F, 1.0F) : GL.Vertex2(0.0F, -he)
        GL.TexCoord2(1.0F, 1.0F) : GL.Vertex2(FBOm.SCR_WIDTH, -he)
        GL.TexCoord2(1.0F, 0.0F) : GL.Vertex2(FBOm.SCR_WIDTH, 0.0F)
        GL.TexCoord2(0.0F, 0.0F) : GL.Vertex2(0.0F, 0.0F)

        GL.End()
        GL.Disable(EnableCap.Texture2D)



        frmMain.glControl_main.SwapBuffers()
        FPS_COUNTER += 1

        'draw_maps()
        If frmGbufferViewer.Visible Then
            frmGbufferViewer.update_screen()
        End If
#If 0 Then
        frmMain.glControl_utility.Visible = True
        '-------------------------------------------------------
        '2nd glControl
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        frmMain.glControl_utility.MakeCurrent()
        Ortho_utility()

        GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        Dim cx, cy, x, y As Single

        cx = frmMain.glControl_utility.Width / 2
        cy = -frmMain.glControl_utility.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle2
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 150.0F + cx
            y = Sin(k + j) * 150.0F + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle2 += 0.00001
            If angle2 > PI * 2 / 40 Then
                angle2 = 0
            End If
        Next
        frmMain.glControl_utility.SwapBuffers()
#Else
        frmMain.glControl_utility.Visible = False
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
    Private Sub draw_main_Quad(ByRef w As Integer, ByRef h As Integer)
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
    Private Sub draw_one_damn_moon(ByVal location As vec3)



        '
        'repeat drawing the elements now that the states are set..
        Dim model = Matrix4.CreateTranslation(location.x, location.y, location.z)

        Dim scale_ As Single = 60.0
        Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)

        Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX

        GL.UseProgram(shader_list.colorOnly_shader)

        GL.Uniform3(colorOnly_color_id, 1.0F, 0.0F, 0.0F)

        GL.UniformMatrix4(colorOnly_PrjMatrix_id, False, MVPM)

        GL.BindVertexArray(MOON.mdl_VAO)

        GL.DrawElements(PrimitiveType.Triangles, (MOON.indice_count * 3), DrawElementsType.UnsignedShort, MOON.index_buffer16)
        GL.UseProgram(0)

        GL.BindVertexArray(0)
        'GL.BindVertexArray(0)
        'Disable states
    End Sub
    Private Sub draw_cross_hair()
        Dim scale_ As Single = 60.0
        Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)

        Dim MVPM = MODELVIEWMATRIX * PROJECTIONMATRIX

        GL.UseProgram(shader_list.colorOnly_shader)

        GL.Uniform3(colorOnly_color_id, 1.0F, 1.0F, 1.0F)
        GL.UniformMatrix4(colorOnly_PrjMatrix_id, False, MVPM)

        'I wasnt going to use direct mode but for now, this is simple
        Dim l As Single = 1000.0F
        GL.Color4(1.0F, 1.0F, 1.0F, 1.0F)
        GL.Begin(PrimitiveType.Lines)
        'left right
        GL.Vertex3(U_LOOK_AT_X - l, U_LOOK_AT_Y, U_LOOK_AT_Z)
        GL.Vertex3(U_LOOK_AT_X + l, U_LOOK_AT_Y, U_LOOK_AT_Z)
        'forward back
        GL.Vertex3(U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z - l)
        GL.Vertex3(U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z + l)
        'up down
        GL.Vertex3(U_LOOK_AT_X, U_LOOK_AT_Y + l, U_LOOK_AT_Z)
        GL.Vertex3(U_LOOK_AT_X, U_LOOK_AT_Y - l, U_LOOK_AT_Z)
        GL.End()

        GL.UseProgram(0)
    End Sub
End Module

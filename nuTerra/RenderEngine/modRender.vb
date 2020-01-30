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
        Dim cx, cy As Single
        frmMain.glControl_main.MakeCurrent()

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFBO) ' Use default buffer
        FBOm.attach_CNG()
        '-------------------------------------------------------
        '1st glControl

        set_prespective_view() ' <-- sets camera and prespective view

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)


        '------------------------------------------------
        '------------------------------------------------
        'GL States
        GL.Enable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Lighting)
        GL.Enable(EnableCap.CullFace)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Disable(EnableCap.Blend)
        '------------------------------------------------
        '------------------------------------------------


        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, TextureEnvMode.Replace)
        '------------------------------------------------
        '------------------------------------------------
        'Draw temp light positon.
        FBOm.attach_CN()
        Dim v As New vec3
        v.x = LIGHT_POS(0) : v.y = LIGHT_POS(1) : v.z = LIGHT_POS(2)
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


        'FBOm.attach_CNG()

        '------------------------------------------------
        GL.UseProgram(shader_list.gWriter_shader) '<------------------------------- Shader Bind
        'GL.UseProgram(shader_list.basic_shader) '<------------------------------- Shader Bind
        '------------------------------------------------
        'GL.Enable(EnableCap.Texture2D)
        GL.Uniform1(gWriter_textureMap_id, 0)
        GL.Uniform1(gWriter_normalMap_id, 1)
        GL.Uniform1(gWriter_GMF_id, 2)

        GL.Uniform1(gWriter_nMap_type, N_MAP_TYPE)

        GL.ActiveTexture(TextureUnit.Texture0 + 0)
        GL.BindTexture(TextureTarget.Texture2D, color_id) '<------------------------------- Texture Bind
        GL.ActiveTexture(TextureUnit.Texture0 + 1)
        GL.BindTexture(TextureTarget.Texture2D, normal_id)
        GL.ActiveTexture(TextureUnit.Texture0 + 2)
        GL.BindTexture(TextureTarget.Texture2D, gmm_id)

        '------------------------------------------------
        'Draw Test VBO
        '------------------------------------------------
        'Bind the main Array of data. This one uses packed data as:
        'Vertex : 3 floats
        'normal : 3 floats
        'UV Coords : 2 floats
        'A total of 8 floats or.. 32 bytes so the stride is 32.
        '
        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO)
        'Enable the data element types in the VBO (vertex, normal ... ).
        GL.EnableClientState(ArrayCap.VertexArray)
        GL.EnableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.EnableClientState(ArrayCap.IndexArray)
        '
        'We assign each element to the slots (gl_Normal, gl_Vertex, gl_textCoord) to the array data parts.
        'The last 2 values are Stide and )ffset to start of next element.
        GL.VertexPointer(3, VertexPointerType.Float, 32, 0)         ' 3 floats next is at --> 12 
        GL.NormalPointer(NormalPointerType.Float, 32, 12)           ' 3 floats --> next is at 24
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24)    ' 2 floats --> None after
        '
        'WE bind the ElementArrayBuffer. This is where the indexing in to the VBO is stored.
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IBO)
        '
        'repeat drawing the elements now that the states are set..
        For i = 0 To 999 ' draw 1,000 boxes
            Dim ox = box_positions(i).x
            Dim oy = box_positions(i).y
            Dim oz = box_positions(i).z

            Dim model = Matrix4.CreateTranslation(ox, oy, oz)

            Dim scale_ As Single = 20.0
            Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)
            Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX

            GL.UniformMatrix4(gWriter_ModelMatrix, False, sMat * model * MODELVIEWMATRIX)
            GL.UniformMatrix4(gWriter_ProjectionMatrix_id, False, MVPM)


            GL.DrawElements(PrimitiveType.Triangles, (indices.Length) * 3, DrawElementsType.UnsignedShort, 0)
        Next

        '
        ' Unbind everything. 
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        'Disable states
        GL.DisableClientState(ArrayCap.VertexArray)
        GL.DisableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.DisableClientState(ArrayCap.IndexArray)
        '
        '------------------------------------------------
        'End Test VBO draw.
        '------------------------------------------------
        'unbind textures!
        unbind_textures(2) ' unbind all the used texture slots



#If 0 Then

        'direct mode quad also just for testing.
        Dim WIDTH = 30.0F
        Dim HEIGHT = 30.0F

        GL.Begin(PrimitiveType.Quads)

        GL.TexCoord2(0.0F, 1.0F)
        GL.Vertex3(-WIDTH / 2, -0.1F, HEIGHT / 2)

        GL.TexCoord2(1.0F, 1.0F)
        GL.Vertex3(WIDTH / 2, -0.1F, HEIGHT / 2)

        GL.TexCoord2(1.0F, 0.0F)
        GL.Vertex3(WIDTH / 2, -0.1F, -HEIGHT / 2)

        GL.TexCoord2(0.0F, 0.0F)
        GL.Vertex3(-WIDTH / 2, -0.1F, -HEIGHT / 2)
        GL.End()

        GL.BindTexture(TextureTarget.Texture2D, 0) '<------------------------------- texture unbind
#End If

        GL.UseProgram(0)
        '===========================================================================
        '===========================================================================
        'Draws a full screen quad to render FBO textures.
        '===========================================================================
        '===========================================================================

        'We can now switch to the default hardware buffer.
        frmMain.glControl_main.MakeCurrent()
        ' Use default buffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        'ortho for the win
        Ortho_main()

        'house keeping
        GL.Disable(EnableCap.Blend)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        GL.Disable(EnableCap.DepthTest)

        GL.Enable(EnableCap.Texture2D)

        '===========================================================================
        ' test our deferred shader
        '===========================================================================

#If 1 Then '<------ set this to 0 to just light the boxes and test their transforms
        GL.UseProgram(shader_list.Deferred_shader)
#Else
        GL.UseProgram(shader_list.basic_shader)
#End If

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

        ''GL.BindTexture(TextureTarget.Texture2D, dial_face_ID)
        'draw_main_Quad(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
        'GL.Disable(EnableCap.Texture2D)
        'GL.BindTexture(TextureTarget.Texture2D, 0)

        ' test render some text to see if it works
        Dim position = PointF.Empty
        'textRender.DrawText.TextRenderer(100, 100) '<--- reset when the FBO changes size!
        textRender.DrawText.clear(Color.FromArgb(0, 0, 0, 255))
        Dim ti = TimeOfDay.TimeOfDay
        Dim pos_str As String = " Light Position X, Y, Z: " + LIGHT_POS(0).ToString("00.0000") + ", " + LIGHT_POS(1).ToString("00.0000") + ", " + LIGHT_POS(2).ToString("00.000")
        textRender.DrawText.DrawString("Current Time:" + ti.ToString + pos_str, mono, Brushes.White, position)

        GL.Enable(EnableCap.Texture2D)
        GL.Enable(EnableCap.AlphaTest)
        GL.AlphaFunc(AlphaFunction.Equal, 1.0)
        GL.Color4(1.0F, 1.0F, 1.0F, 0.0F)

        GL.BindTexture(TextureTarget.Texture2D, textRender.DrawText.Gettexture)
        GL.Begin(PrimitiveType.Quads)
        Dim he As Integer = 20
        GL.TexCoord2(0.0F, 1.0F) : GL.Vertex2(0.0F, -he)
        GL.TexCoord2(1.0F, 1.0F) : GL.Vertex2(FBOm.SCR_WIDTH, -he)
        GL.TexCoord2(1.0F, 0.0F) : GL.Vertex2(FBOm.SCR_WIDTH, 0.0F)
        GL.TexCoord2(0.0F, 0.0F) : GL.Vertex2(0.0F, 0.0F)

        GL.End()
        GL.Disable(EnableCap.Texture2D)



        frmMain.glControl_main.SwapBuffers()
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
        'G_Buffer.getsize(w, h)
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


        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO)
        'Enable the data element types in the VBO (vertex, normal ... ).
        GL.EnableClientState(ArrayCap.VertexArray)
        GL.EnableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.EnableClientState(ArrayCap.IndexArray)
        '
        'We assign each element to the slots (gl_Normal, gl_Vertex, gl_textCoord) to the array data par ts.
        'The last 2 values are Stide and )ffset to start of next element.
        GL.VertexPointer(3, VertexPointerType.Float, 32, 0)         ' 3 floats next is at --> 12 
        GL.NormalPointer(NormalPointerType.Float, 32, 12)           ' 3 floats --> next is at 24
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24)    ' 2 floats --> None after
        '
        'WE bind the ElementArrayBuffer. This is where the indexing in to the VBO is stored.
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IBO)
        '
        'repeat drawing the elements now that the states are set..
        Dim model = Matrix4.CreateTranslation(location.x, location.y, location.z)

        Dim scale_ As Single = 60.0
        Dim sMat = Matrix4.CreateScale(scale_, scale_, scale_)

        Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX

        GL.UseProgram(shader_list.colorOnly_shader)

        GL.Uniform3(colorOnly_color_id, 1.0F, 0.0F, 0.0F)
        GL.UniformMatrix4(colorOnly_ModelMatrix_id, False, sMat * model * MODELVIEWMATRIX)
        GL.UniformMatrix4(colorOnly_PrjMatrix_id, False, MVPM)

        GL.DrawElements(PrimitiveType.Triangles, (indices.Length) * 3, DrawElementsType.UnsignedShort, 0)
        GL.UseProgram(0)

        ' Unbind everything. 
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        'Disable states
        GL.DisableClientState(ArrayCap.VertexArray)
        GL.DisableClientState(ArrayCap.NormalArray)
        GL.EnableClientState(ArrayCap.TextureCoordArray)
        GL.DisableClientState(ArrayCap.IndexArray)

    End Sub
    Private Sub draw_cross_hair()
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
    End Sub
End Module

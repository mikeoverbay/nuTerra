Imports System.Runtime.InteropServices
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Module modRender
    Public PI As Single = 3.14159274F

    Public map_center As Vector3
    Public scale As Vector3


    Public Sub draw_scene()
        '===========================================================================
        ' FLAG INFO
        ' 0  = No shading
        ' 64  = model 
        ' 128 = terrain
        ' 255 = sky dome. We will want to control brightness
        ' more as they are added
        '===========================================================================

        '===========================================================================

        GL.FrontFace(FrontFaceDirection.Ccw)
        If SHOW_MAPS_SCREEN Then
            Return
        End If
        If SHOW_LOADING_SCREEN Then
            draw_loading_screen()
            Return
        End If
        '===========================================================================

        '===========================================================================
        map_scene.camera.set_prespective_view() ' <-- sets camera and prespective view ==============
        '===========================================================================

        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            '=======================================================================
            map_scene.static_models.frustum_cull() '========================================================
            '=======================================================================
        End If

        '===========================================================================
        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            ExtractFrustum()
            cull_terrain()

            map_scene.terrain.terrain_vt_pass()
        End If
        '===========================================================================

        '===========================================================================
        MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, MainFBO.width, MainFBO.height)
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
        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            GL.CopyNamedBufferSubData(map_scene.static_models.parameters.buffer_id, map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, IntPtr.Zero, map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer))
            GL.GetNamedBufferSubData(map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer), map_scene.static_models.numAfterFrustum)

            map_scene.static_models.model_depth_pass()

            If USE_RASTER_CULLING Then
                map_scene.static_models.model_cull_raster_pass()
            End If
        End If

        If ShadowMappingFBO.Enabled AndAlso FPS_COUNTER Mod ShadowMappingFBO.FRAME_STEP = 0 Then
            map_scene.ShadowMappingPass()

            ' restore main FBO
            MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
            GL.Viewport(0, 0, MainFBO.width, MainFBO.height)
        End If

        MainFBO.attach_CNGPA()

        If DONT_BLOCK_OUTLAND AndAlso map_scene.OUTLAND_LOADED Then
            MainFBO.attach_CNGPA()
            'GL.Disable(EnableCap.DepthTest) 'just so we can see all of it
            map_scene.terrain.Draw_outland()
            GL.Enable(EnableCap.DepthTest)
        End If
        MainFBO.attach_CNGPA()

        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            map_scene.terrain.draw_terrain()

            If (SHOW_BORDER Or SHOW_CHUNKS Or SHOW_GRID) Then map_scene.terrain.draw_terrain_grids()
            '=======================================================================
            If SHOW_CURSOR Then
                'setup for projection before drawing
                MainFBO.attach_C_no_Depth()
                GL.DepthMask(False)
                GL.FrontFace(FrontFaceDirection.Cw)
                GL.Enable(EnableCap.CullFace)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
                '=======================================================================
                map_scene.cursor.draw_map_cursor() '=================================
                '=======================================================================
                'restore settings after projected objects are drawn
                GL.DepthMask(True)
                GL.Disable(EnableCap.CullFace)
                MainFBO.attach_Depth()
                GL.FrontFace(FrontFaceDirection.Ccw)
            End If
        End If

        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            map_scene.static_models.draw_models()
        End If

        'If ShadowMappingFBO.Enabled Then
        'map_scene.DrawLightFrustum()
        'End If

        GL.DepthFunc(DepthFunction.Less)
        '===========================================================================
        If PICK_MODELS AndAlso map_scene.MODELS_LOADED Then PickModel()
        '===========================================================================

        '===========================================================================
        '================== Deferred Rendering, HUD and MINI MAP ===================
        '===========================================================================


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
        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            map_scene.static_models.glassPass()
        End If

        '===========================================================================

        'ortho projection decals
#If True Then

        MainFBO.attach_C()


        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
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
            'This has to be called last. It changes the PROJECTMATRIX and VIEWMATRIX
            If DONT_HIDE_MINIMAP Then map_scene.mini_map.draw_mini_map() '===========================================================
            '===========================================================================
        End If
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
    End Sub

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
        map_scene.sky.CUBE_TEXTURE_ID.BindUnit(4)
        map_scene.CC_LUT_ID.BindUnit(5)
        map_scene.ENV_BRDF_LUT_ID?.BindUnit(6)
        ShadowMappingFBO.depth_tex.BindUnit(7)

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, map_scene.camera.PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        draw_main_Quad(MainFBO.width, MainFBO.height) 'render Gbuffer lighting

        ' UNBIND
        unbind_textures(7)

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(MainFBO.gColor.texture_id, 0, 0, 0, 0, 0, MainFBO.width, MainFBO.height)
    End Sub

    Private Sub copy_gColor_2_to_gColor()
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment6)
        MainFBO.fbo.DrawBuffer(DrawBufferMode.ColorAttachment0)
        GL.BlitNamedFramebuffer(
            MainFBO.fbo.fbo_id,
            MainFBO.fbo.fbo_id,
            0, 0, MainFBO.width, MainFBO.height,
            0, 0, MainFBO.width, MainFBO.height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest)
    End Sub

    Private Sub perform_SSAA_Pass()

        GL_PUSH_GROUP("perform_SSAA_Pass")

        FXAAShader.Use()

        GL.Uniform1(FXAAShader("pass_through"), CInt(FXAA_enable))

        GL.UniformMatrix4(FXAAShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        GL.Uniform2(FXAAShader("viewportSize"), CSng(MainFBO.width), CSng(MainFBO.height))

        MainFBO.gColor.BindUnit(0)

        'draw full screen quad
        GL.Uniform4(FXAAShader("rect"), 0.0F, CSng(-MainFBO.height), CSng(MainFBO.width), 0.0F)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FXAAShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL_POP_GROUP()
    End Sub

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

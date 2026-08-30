Imports System.Runtime.InteropServices
Imports ImGuiNET
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Module modRender
    Public PI As Single = 3.14159274F

    Public map_center As Vector3
    Public scale As Vector3


    Public Sub draw_scene()
        ' Flip the query slot before anything issues one.
        modGpuTimers.NewFrame()

        '===========================================================================
        ' FLAG INFO
        ' 0  = No shading
        ' 64  = model 
        ' 128 = terrain
        ' 255 = sky dome. We will want to control brightness
        ' more as they are added
        '===========================================================================

        GL.FrontFace(FrontFaceDirection.Ccw)
        If SHOW_MAPS_SCREEN OrElse SHOW_LOADING_SCREEN Then
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer
            Ortho_main()
            If SHOW_MAPS_SCREEN Then
                draw_image_rectangle(New RectangleF(0, 0, MainFBO.width, MainFBO.height), MAP_SELECT_BACKGROUND_ID)
            Else
                Dim ls = (1920.0F - MainFBO.width) / 2.0F
                draw_image_rectangle(New RectangleF(-ls, 0, 1920, 1080), nuTERRA_BG_IMAGE)
            End If
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

            modGpuTimers.Begin("VT pages")
            map_scene.terrain.terrain_vt_pass()
            modGpuTimers.Finish()
        End If
        '===========================================================================

        '===========================================================================
        MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, MainFBO.width, MainFBO.height)
        '===========================================================================

        '===========================================================================
        MainFBO.attach_CSNGP()
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
        MainFBO.attach_CNGPA() 'clear ALL gTextures!
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        '===========================================================================

        '===========================================================================
        MainFBO.attach_C()
        modGpuTimers.Begin("Sky")
        map_scene.sky.draw_sky()
        modGpuTimers.Finish()

        '===========================================================================
        'GL States 
        GL.DepthFunc(DepthFunction.Greater)
        '===========================================================================

        'Model depth pass only
        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            GL.CopyNamedBufferSubData(map_scene.static_models.parameters.buffer_id, map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, IntPtr.Zero, map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer))
            GL.GetNamedBufferSubData(map_scene.static_models.parameters_temp.buffer_id, IntPtr.Zero, map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer), map_scene.static_models.numAfterFrustum)

            modGpuTimers.Begin("Model depth")
            map_scene.static_models.model_depth_pass()
            modGpuTimers.Finish()

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

        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            MainFBO.attach_CSNGP()


            modGpuTimers.Begin("Terrain")
            map_scene.terrain.draw_terrain()
            modGpuTimers.Finish()

            MainFBO.attach_CNGPA()
        End If

        ' The outland draws AFTER the playfield terrain, deliberately. The
        ' heightmap weld makes the sheet FLUSH with the terrain at the
        ' footprint line, and with the strict Greater depth test two writers
        ' at equal depth flip winners as the camera moves sub-pixel - the
        ' outland's bake albedo and its own normals shading through
        ' intermittently was the "settling-in" lighting flicker on the far
        ' terrain. Drawing terrain first makes it win every tie by
        ' construction (and early-Z discards the tucked-under outland free).
        If DONT_BLOCK_OUTLAND AndAlso map_scene.OUTLAND_LOADED Then
            MainFBO.attach_CNGPA()
            modGpuTimers.Begin("Outland")
            map_scene.terrain.Draw_outland()
            modGpuTimers.Finish()
            GL.Enable(EnableCap.DepthTest)
        End If
        MainFBO.attach_CNGPA()

        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then

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

        ' Roads are no longer drawn here. They are mixed into the virtual
        ' texture pages instead, in PageLoader, so by the time the terrain is
        ' drawn they are already part of it.

        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            modGpuTimers.Begin("Models")
            map_scene.static_models.draw_models()
            modGpuTimers.Finish()
        End If

        If map_scene.TREES_LOADED AndAlso DONT_BLOCK_TREES Then
            modGpuTimers.Begin("Trees")
            map_scene.trees.draw()
            modGpuTimers.Finish()
        End If

        'If ShadowMappingFBO.Enabled Then
        'map_scene.DrawLightFrustum()
        'End If

        GL.DepthFunc(DepthFunction.Less)
        '===========================================================================
        If ModelPicker.Enabled AndAlso map_scene.MODELS_LOADED Then ModelPicker.PickModel()
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


        'ortho projection decals

        If map_scene.DECALS_LOADED AndAlso DONT_BLOCK_DECALS Then
            modGpuTimers.Begin("Decals")
            map_scene.decals.draw_decals()
            modGpuTimers.Finish()
        End If

        GL.Disable(EnableCap.DepthTest)

        MainFBO.attach_C2()

        ' The shader fork. "show probe field" swaps the whole lighting program
        ' for the inspector rather than adding a branch inside deferred.frag,
        ' so the real lighting path has no knowledge of the probe grid at all.
        modGpuTimers.Begin("Deferred")
        If SH_GRID_DEBUG AndAlso SH_GRID_LOADED AndAlso SH_GRID_ID IsNot Nothing Then
            render_probe_field()
        Else
            render_deferred_buffers()
        End If
        modGpuTimers.Finish()
        'gAux_color to gColor;
        MainFBO.attach_C1_and_C2()
        copy_gColor_2_to_gColor()

        ' Screen space reflections, after the resolve because they reflect the
        ' LIT frame - reflecting G-buffer albedo would put unlit building faces
        ' in the water. Reads gColor, writes gColor_2, then the same blit puts
        ' it back; gColor cannot be both source and target of one pass.
        If SSR_ENABLED AndAlso map_scene.TERRAIN_LOADED Then
            modGpuTimers.Begin("SSR")
            render_ssr()
            MainFBO.attach_C1_and_C2()
            copy_gColor_2_to_gColor()
            modGpuTimers.Finish()
        End If

        ' Water, forward over the lit frame. After SSR so puddle reflections sit
        ' under it, before the default-framebuffer switch so it still has the
        ' scene depth to test against. MainFBO is still bound; route colour to
        ' gColor and let the pass manage its own depth/blend state.
        If map_scene.WATER_LOADED AndAlso DONT_BLOCK_WATER Then
            modGpuTimers.Begin("Water")

            ' Water reads the lit frame (for SSR reflections of terrain and
            ' models) while blending over it - one texture cannot be both, so
            ' it draws into gColor_2 against a fresh copy and blits back. Same
            ' dance the SSR pass does, for the same reason.
            copy_gColor_to_gColor_2()
            MainFBO.attach_C2()
            map_scene.water.draw()
            MainFBO.attach_C1_and_C2()
            copy_gColor_2_to_gColor()

            modGpuTimers.Finish()
        End If

        ' Volumetric GFX meshes - smoke, flames - forward over the lit frame,
        ' after water so a column rising out of a lake sits over it. Straight
        ' into gColor: unlike water and SSR the pass never samples the frame,
        ' so no copy dance is needed.
        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            modGpuTimers.Begin("FX")
            ' Bind explicitly. attach_C only names the draw buffer (DSA); it
            ' does not bind, so this pass was landing in whatever framebuffer
            ' the previous one left bound. It went unnoticed because the
            ' debug readback bound MainFBO as a side effect on exactly the
            ' frame anyone was measuring.
            MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
            MainFBO.attach_C()
            ' Colour only - the depth buffer must survive or the FX loses its
            ' depth test against the scene and cards show through terrain.
            If BLACK_BEFORE_FX Then
                GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F)
                GL.Clear(ClearBufferMask.ColorBufferBit)
            End If
            ' Read the target either side of the pass. Screenshots go through
            ' the tonemap and the LUT, so a value read off one is not what the
            ' shader wrote and cannot be reasoned about numerically; this is
            ' the raw buffer, and it answers the only question that matters -
            ' did the FX pass change any pixel, and by how much.
            Dim fx_before As Byte() = Nothing
            If FX_DIFF_THIS_FRAME Then fx_before = grab_colour_buffer()
            map_scene.static_models.draw_fx()
            ' Card particles. OFF by default: the simulation and both readers
            ' are verified, but issuing this draw blacks the whole frame for a
            ' reason not yet isolated - it is not the VAO (restoring defaultVao
            ' afterwards does not help) and not the gPosition feedback loop
            ' (removing that sample does not help either). The particle itself
            ' is correct at that point: one card, half-size 0.7 m, at the house.
            If PARTICLES_ENABLED Then map_scene.particles.Draw(map_scene.camera.CAM_POSITION)

            If FX_DIFF_THIS_FRAME Then report_fx_diff(fx_before)
            trace_gcolor("draw_fx")
            modGpuTimers.Finish()
        End If


        '===========================================================================
        'DEFAUL BUFFER ATTACH!!!
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        '===========================================================================

        trace_gcolor("default fb clear")

        If FXAA_enable Then
            perform_SSAA_Pass()
            copy_default_to_gColor()
        End If
        trace_gcolor("FXAA (on=" & FXAA_enable.ToString() & ")")

        '===========================================================================
        'hopefully, this will look like glass :)
        If map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            map_scene.static_models.glassPass()
        End If

        '===========================================================================


#If True Then

        MainFBO.attach_C()


        If map_scene.TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            GL.Disable(EnableCap.DepthTest)

            copy_default_to_gColor()
            trace_gcolor("copy_default_to_gColor")
            GL.DepthMask(False)
            'GL.FrontFace(FrontFaceDirection.Cw)
            GL.Enable(EnableCap.Blend)
            GL.Enable(EnableCap.CullFace)

            map_scene.base_rings.draw_base_rings_deferred()
            trace_gcolor("base_rings")

            'hopefully, this will look like FOG :)
            GL.Disable(EnableCap.Blend)
            copy_default_to_gColor()
            trace_gcolor("copy_default (pre-fog)")
            ' Fog against a blacked scene drives every FX pixel to zero, which
            ' makes the isolated view useless. Skip it while isolating.
            If Not BLACK_BEFORE_FX Then map_scene.fog.global_fog()
            trace_gcolor("global_fog")

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
            ' Before the minimap: draw_mini_map changes PROJECTMATRIX/VIEWMATRIX
            ' on its way out, and this needs the 2D projection still standing.
            If SHOW_SUN_SHADOW_VIEWER Then
                If ImGui.Begin("Shadow Map Viewer", SHOW_SUN_SHADOW_VIEWER) Then
                    Dim sz = Math.Min(ImGui.GetWindowWidth() - 16, ImGui.GetWindowHeight() - 40)
                    Dim wx = (b_x_max - b_x_min + 1.0F) * 100.0F
                    Dim wz = (b_y_max - b_y_min + 1.0F) * 100.0F
                    Dim aspect = wz / wx
                    Dim w = sz, h = sz * aspect
                    If h > sz Then
                        h = sz
                        w = sz / aspect
                    End If

                    Dim pos = ImGui.GetCursorScreenPos()
                    map_scene.sun_shadow.DebugDraw(New RectangleF(pos.X, pos.Y, w, h))
                    ImGui.Dummy(New System.Numerics.Vector2(w, h))
                    ImGui.End()
                End If
            End If

            If DONT_HIDE_MINIMAP Then map_scene.mini_map.draw_mini_map() '===========================================================
            '===========================================================================
        End If
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)

        trace_gcolor("end of frame")
        FX_DIFF_THIS_FRAME = False

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
    End Sub

    ''' <summary>
    ''' RGBA8 copy of the currently attached colour buffer, straight off the GPU.
    ''' </summary>
    Private Function grab_colour_buffer() As Byte()
        Dim w = MainFBO.width, h = MainFBO.height
        Dim buf(w * h * 4 - 1) As Byte
        ' Name the read buffer explicitly. ReadPixels otherwise takes whatever
        ' the FBO's read buffer happens to be, which is not the attachment the
        ' FX pass draws into - it returned an unchanging image and reported
        ' "wrote 0 pixels" even for a shader outputting solid magenta.
        ' gColor is ColorAttachment0.
        ' Save and RESTORE the binding. Leaving MainFBO bound sent the rest of
        ' the frame's draws to the wrong framebuffer and produced a run of
        ' GL_INVALID_OPERATION "the required buffer is missing" - the harness
        ' was corrupting the very frame it measured.
        Dim prev_fbo = GL.GetInteger(GetPName.FramebufferBinding)
        MainFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0)
        GL.ReadPixels(0, 0, w, h, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, buf)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prev_fbo)
        Return buf
    End Function

    ' Byte offsets of the pixels the FX pass actually changed this frame, so the
    ' later stages can be judged on the FX alone. Whole-frame counts are useless
    ' for that: in a normal render the scene lights every pixel and swamps them.
    Private fx_pixels As Integer() = Nothing

    Private Sub trace_gcolor(label As String)
        If Not FX_DIFF_THIS_FRAME OrElse fx_pixels Is Nothing Then Return
        Dim buf = grab_colour_buffer()
        Dim nz = 0, mx = 0, sum As Long = 0
        For Each i In fx_pixels
            Dim v = Math.Max(CInt(buf(i)), Math.Max(CInt(buf(i + 1)), CInt(buf(i + 2))))
            If v > 0 Then nz += 1
            sum += v
            If v > mx Then mx = v
        Next
        LogThis("    FX pixels after {0,-24} still lit={1,5} of {2}  max={3,3}/255  mean={4:0.0}",
                label, nz, fx_pixels.Length, mx, sum / CDbl(Math.Max(1, fx_pixels.Length)))
    End Sub

    Private Sub report_fx_diff(before As Byte())
        If before Is Nothing Then Return
        Dim after = grab_colour_buffer()
        Dim changed = 0, max_delta = 0, sum_delta As Long = 0
        Dim hits As New List(Of Integer)
        For i = 0 To before.Length - 1 Step 4
            Dim d = Math.Max(Math.Abs(CInt(after(i)) - CInt(before(i))),
                    Math.Max(Math.Abs(CInt(after(i + 1)) - CInt(before(i + 1))),
                             Math.Abs(CInt(after(i + 2)) - CInt(before(i + 2)))))
            If d > 0 Then
                changed += 1
                sum_delta += d
                hits.Add(i)
                If d > max_delta Then max_delta = d
            End If
        Next
        fx_pixels = hits.ToArray()
        Dim total = before.Length \ 4
        LogThis("  FX pass wrote {0} of {1} pixels ({2:0.000}%)  max delta={3}/255  mean delta={4:0.0}",
                changed, total, 100.0 * changed / total, max_delta,
                If(changed > 0, sum_delta / CDbl(changed), 0.0))
        dump_fx_pass(after)
    End Sub

    ''' <summary>
    ''' Save gColor as it stands immediately after the FX pass. The screen is
    ''' not a usable record of this: the SSAA round trip through the 8-bit back
    ''' buffer crushes small values (a measured 23/255 came back as 1/255), so
    ''' a faint effect that the pass really drew never reaches a screenshot.
    ''' </summary>
    Private Sub dump_fx_pass(buf As Byte())
        Try
            Dim w = MainFBO.width, h = MainFBO.height
            Using bmp As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
                Dim d = bmp.LockBits(New Rectangle(0, 0, w, h),
                                     Imaging.ImageLockMode.WriteOnly,
                                     Imaging.PixelFormat.Format32bppArgb)
                Dim row(w * 4 - 1) As Byte
                For y = 0 To h - 1
                    ' GL origin is bottom-left, GDI+ top-left: flip, and swap
                    ' RGBA to the BGRA that Format32bppArgb expects.
                    Dim src = (h - 1 - y) * w * 4
                    For x = 0 To w - 1
                        row(x * 4 + 0) = buf(src + x * 4 + 2)
                        row(x * 4 + 1) = buf(src + x * 4 + 1)
                        row(x * 4 + 2) = buf(src + x * 4 + 0)
                        row(x * 4 + 3) = 255
                    Next
                    Marshal.Copy(row, 0, IntPtr.Add(d.Scan0, y * d.Stride), row.Length)
                Next
                bmp.UnlockBits(d)
                Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra")
                IO.Directory.CreateDirectory(dir)
                bmp.Save(IO.Path.Combine(dir, "fx_pass.png"), Imaging.ImageFormat.Png)
            End Using
            LogThis("  wrote fx_pass.png (gColor straight after the FX pass)")
        Catch ex As Exception
            LogThis("  fx_pass.png failed: {0}", ex.Message)
        End Try
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

        ' Map-wide baked sun shadow. The cascades carry trees only, so this is
        ' what shadows terrain and static models - it has to be here rather than
        ' folded into the terrain albedo at page-bake time, or it reaches neither
        ' the models nor the ambient/direct split correctly.
        If map_scene.sun_shadow.ready AndAlso map_scene.sun_shadow.depth_tex IsNot Nothing Then
            map_scene.sun_shadow.depth_tex.BindUnit(8)
            GL.UniformMatrix4(deferredShader("sunViewProj"), False, map_scene.sun_shadow.sun_view_proj)

            ' 1 = PCF over the depth map, 2 = moment shadow map. The moment path
            ' needs its texture to actually exist - msm_ready tracks what the
            ' last bake built, not what the checkbox currently says.
            If map_scene.sun_shadow.msm_ready AndAlso map_scene.sun_shadow.moment_tex IsNot Nothing Then
                map_scene.sun_shadow.moment_tex.BindUnit(9)
                GL.Uniform1(deferredShader("has_sun_shadow"), 2)
                GL.Uniform1(deferredShader("msm_moment_bias"), MSM_MOMENT_BIAS)
            Else
                GL.Uniform1(deferredShader("has_sun_shadow"), 1)
            End If

            ' Shared by both paths, so an A/B compares the filtering only.
            GL.Uniform1(deferredShader("shadow_penumbra_lo"), SHADOW_PENUMBRA_LO)
            GL.Uniform1(deferredShader("shadow_penumbra_hi"), SHADOW_PENUMBRA_HI)
        Else
            GL.Uniform1(deferredShader("has_sun_shadow"), 0)
        End If

        GL.UniformMatrix4(deferredShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        Dim lp = Transform_vertex_by_Matrix4(LIGHT_POS, map_scene.camera.PerViewData.view)

        GL.Uniform3(deferredShader("LightPos"), lp.X, lp.Y, lp.Z)

        ' SH ambient. Nine RGB coefficients go up as a flat float array; when the
        ' map has no probe sh0 is white and the rest zero, which evaluates back to
        ' the flat constant the shader used before.
        Static sh_flat(26) As Single
        For i = 0 To 8
            sh_flat(i * 3 + 0) = SH_AMBIENT(i).X
            sh_flat(i * 3 + 1) = SH_AMBIENT(i).Y
            sh_flat(i * 3 + 2) = SH_AMBIENT(i).Z
        Next
        GL.Uniform3(deferredShader("sh_ambient"), 9, sh_flat)
        GL.Uniform1(deferredShader("sh_enabled"),
                    CInt(If(USE_SH_AMBIENT AndAlso SH_AMBIENT_LOADED, 1, 0)))

        ' The baked probe FIELD. Uploaded and sampled, but deferred.frag does
        ' not fold it into the lighting yet - only the debug view reads it.
        Dim grid_on = USE_SH_GRID AndAlso SH_GRID_LOADED AndAlso SH_GRID_ID IsNot Nothing
        If grid_on Then
            SH_GRID_ID.BindUnit(11)

            ' The shader computes uv = world.xz * scale - offset. Our world is
            ' mirrored in x for display and the bake is not, so x runs backwards:
            '   z : uv = (w - min)/size  -> scale =  1/size, offset =  min/size
            '   x : uv = (max - w)/size  -> scale = -1/size, offset = -max/size
            Dim scale_z = 1.0F / SH_GRID_SIZE.Z
            Dim offset_z = (SH_GRID_CENTRE.Z - SH_GRID_SIZE.Z * 0.5F) * scale_z
            Dim scale_x = -1.0F / SH_GRID_SIZE.X
            Dim offset_x = -(SH_GRID_CENTRE.X + SH_GRID_SIZE.X * 0.5F) / SH_GRID_SIZE.X

            GL.Uniform4(deferredShader("sh_grid_uv"), offset_x, offset_z, scale_x, scale_z)
            GL.Uniform1(deferredShader("sh_grid_fade"), 1.0F / Math.Max(SH_GRID_FADE, 0.001F))
            GL.Uniform1(deferredShader("sh_grid_offset"), SH_GRID_OFFSET)
            GL.Uniform1(deferredShader("sh_grid_mix"), SH_GRID_MIX)
            ' Ease the box edge over a couple of probes instead of switching -
            ' the grid stops well inside the outland and a hard test would draw
            ' a ring across the terrain there.
            GL.Uniform1(deferredShader("sh_grid_edge"),
                        2.0F * SH_GRID_SPACING / Math.Max(SH_GRID_SIZE.X, 1.0F))

            Static grid_sh_flat(26) As Single
            For i = 0 To 8
                grid_sh_flat(i * 3 + 0) = SH_GRID_SH9(i).X
                grid_sh_flat(i * 3 + 1) = SH_GRID_SH9(i).Y
                grid_sh_flat(i * 3 + 2) = SH_GRID_SH9(i).Z
            Next
            GL.Uniform3(deferredShader("sh_grid_sh9"), 9, grid_sh_flat)
        End If
        GL.Uniform1(deferredShader("sh_grid_enabled"), CInt(If(grid_on, 1, 0)))
        GL.Uniform1(deferredShader("sh_grid_debug"),
                    CInt(If(grid_on AndAlso SH_GRID_DEBUG, 1, 0)))

        draw_main_Quad(MainFBO.width, MainFBO.height) 'render Gbuffer lighting

        ' UNBIND
        unbind_textures(12)

        deferredShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Private Sub copy_default_to_gColor()
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.CopyTextureSubImage2D(MainFBO.gColor.texture_id, 0, 0, 0, 0, 0, MainFBO.width, MainFBO.height)
    End Sub

    Private Sub render_ssr()
        GL_PUSH_GROUP("render_ssr")

        MainFBO.attach_C2()

        ssrShader.Use()

        MainFBO.gColor.BindUnit(0)      ' the resolved frame - what gets reflected
        MainFBO.gNormal.BindUnit(1)
        MainFBO.gGMF.BindUnit(2)        ' .a is the wetness mask
        MainFBO.gPosition.BindUnit(3)   ' view space

        GL.Uniform1(ssrShader("ssr_intensity"), SSR_INTENSITY)
        GL.Uniform1(ssrShader("ssr_steps"), SSR_STEPS)
        GL.Uniform1(ssrShader("ssr_thickness"), SSR_THICKNESS)
        GL.Uniform1(ssrShader("ssr_stride"), SSR_STRIDE)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        ssrShader.StopUse()
        unbind_textures(4)

        GL_POP_GROUP()
    End Sub

    Private Sub copy_gColor_to_gColor_2()
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment0)
        MainFBO.fbo.DrawBuffer(DrawBufferMode.ColorAttachment6)
        GL.BlitNamedFramebuffer(
            MainFBO.fbo.fbo_id,
            MainFBO.fbo.fbo_id,
            0, 0, MainFBO.width, MainFBO.height,
            0, 0, MainFBO.width, MainFBO.height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest)
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
        draw_main_Quad(deferredShader, w, h)
    End Sub

    ''' <summary>
    ''' The same full-screen quad against whichever program is bound - the probe
    ''' field view is a second program that needs an identical draw.
    ''' </summary>
    Private Sub draw_main_Quad(shader As Shader, w As Integer, h As Integer)
        GL.Uniform4(shader("rect"), 0.0F, CSng(-h), CSng(w), 0.0F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
    End Sub

    ''' <summary>
    ''' Probe field inspector - a COMPLETE replacement for the deferred pass,
    ''' selected by the "show probe field" checkbox.
    '''
    ''' Forking at the program level rather than branching inside deferred.frag
    ''' is the whole point: the lighting shader never references the grid, so
    ''' turning this view on cannot change how the scene actually renders.
    ''' </summary>
    Private Sub render_probe_field()
        GL_PUSH_GROUP("render_probe_field")

        probeFieldShader.Use()

        MainFBO.gColor.BindUnit(0)
        MainFBO.gNormal.BindUnit(1)
        MainFBO.gGMF.BindUnit(2)
        MainFBO.gPosition.BindUnit(3)
        SH_GRID_ID.BindUnit(11)

        ' Same world mapping the loader established: our world is mirrored in x
        ' for display and the bake is not, so x runs backwards.
        Dim scale_z = 1.0F / SH_GRID_SIZE.Z
        Dim offset_z = (SH_GRID_CENTRE.Z - SH_GRID_SIZE.Z * 0.5F) * scale_z
        Dim scale_x = -1.0F / SH_GRID_SIZE.X
        Dim offset_x = -(SH_GRID_CENTRE.X + SH_GRID_SIZE.X * 0.5F) / SH_GRID_SIZE.X

        GL.Uniform4(probeFieldShader("sh_grid_uv"), offset_x, offset_z, scale_x, scale_z)
        GL.Uniform1(probeFieldShader("sh_grid_fade"), 1.0F / Math.Max(SH_GRID_FADE, 0.001F))
        GL.Uniform1(probeFieldShader("sh_grid_offset"), SH_GRID_OFFSET)
        GL.Uniform1(probeFieldShader("probe_exposure"), SH_GRID_EXPOSURE)
        GL.Uniform1(probeFieldShader("probe_show_grid"), CInt(If(SH_GRID_SHOW_LATTICE, 1, 0)))

        Static grid_sh_flat(26) As Single
        For i = 0 To 8
            grid_sh_flat(i * 3 + 0) = SH_GRID_SH9(i).X
            grid_sh_flat(i * 3 + 1) = SH_GRID_SH9(i).Y
            grid_sh_flat(i * 3 + 2) = SH_GRID_SH9(i).Z
        Next
        GL.Uniform3(probeFieldShader("sh_grid_sh9"), 9, grid_sh_flat)

        GL.UniformMatrix4(probeFieldShader("ProjectionMatrix"), False, PROJECTIONMATRIX)

        draw_main_Quad(probeFieldShader, MainFBO.width, MainFBO.height)

        unbind_textures(12)
        probeFieldShader.StopUse()

        GL_POP_GROUP()
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

End Module

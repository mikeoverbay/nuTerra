Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime.InteropServices

Module LQ_Texture_creator
    Public Sub bake_terrain_shadows()

        Dim biasMatrix = New Matrix4 With {
            .Row0 = New Vector4(0.5F, 0.0F, 0.0F, 0.0F),
            .Row1 = New Vector4(0.0F, 0.5F, 0.0F, 0.0F),
            .Row2 = New Vector4(0.0F, 0.0F, 0.5F, 0.0F),
            .Row3 = New Vector4(0.5F, 0.5F, 0.5F, 1.0F)
        }
        '===========================================================================
        'setup Fbo
        Dim quailty As Integer = 512 '<-- adjusts size of the mask textures

        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(quailty, quailty), 2))

        FBO_ShadowBaker.LayerCount = theMap.render_set.Length

        FBO_ShadowBaker.mipCount = 2 'numLevels

        FBO_ShadowBaker.depth_map_size = 1024 ' Depth map size

        FBO_ShadowBaker.FBO_Initialize(New Point(quailty, quailty))

        If Not FBO_ShadowBaker.FBO_Make_Ready_For_Shadow_writes() Then
            Stop
        End If

        '===========================================================================
        If Not DONT_BLOCK_TERRAIN Then
            Return
        End If
        Dim LIGHT_ANGLE_Z = 360 - LIGHT_ORBIT_ANGLE_Z
        LIGHT_ANGLE_Z += 180.0F

        LIGHT_POS(0) = Math.Sin(LIGHT_ANGLE_Z * 0.0174533)
        LIGHT_POS(1) = Math.Sin(LIGHT_ORBIT_ANGLE_X * 0.0174533)
        LIGHT_POS(2) = Math.Cos(LIGHT_ANGLE_Z * 0.0174533)

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_ShadowBaker_ID) '=====
        '===========================================================================
        Dim map_id As Integer = 62

        Dim loc As New Point


        Dim sunMatrix = set_sun_view_matrix()

        '===========================================================================
        'set states
        GL.DepthFunc(DepthFunction.Less)
        GL.ClearDepth(1.0F)

        GL.Enable(EnableCap.DepthTest)
        GL.Enable(EnableCap.CullFace)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.FrontFace(FrontFaceDirection.Ccw)
        '===========================================================================

        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            clear_culling_and_Lod()
        End If

        '===========================================================================
        'loop each chunk
        For id = 0 To theMap.render_set.Length - 1
            'For id = 48 To 48
            GL.ClearColor(0.0F, 0.0F, 1.0F, 1.0)

            FBO_ShadowBaker.FBO_Make_Ready_For_Shadow_writes()

            GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

            With theMap.render_set(id)

                loc.X = .matrix.Row3.X
                loc.Y = .matrix.Row3.Z
                Dim loc_z As Single = (theMap.v_data(id).min_height + theMap.v_data(id).max_height) / 2.0F

                Dim eye As New Vector3(-LIGHT_POS(0), LIGHT_POS(1), LIGHT_POS(2))

                Dim at As New Vector3(loc.X, loc_z, loc.Y)
                'Dim at As New Vector3(0.0F, 0.0F, 0.0F)

                Dim SUN_ROTATION = Matrix4.LookAt(eye, at, New Vector3(0.0F, 1.0F, 0.0))
                'Dim SUN_ROTATION.Row3.Xyz += theMap.render_set(id).matrix.Row3.Xyz

                Dim rv = New Vector4(50, loc_z, 50, 1.0)

                'set up the ortho window for each chunk
                Sun_Ortho_view(-75.0 + loc.X, 75.0 + loc.X, -75.0 + loc.Y, 75.0 + loc.Y)
                Sun_Ortho_view(-100.0, 100.0, -100.0, 100.0)

                GL.FrontFace(FrontFaceDirection.Cw)
                GL.Enable(EnableCap.CullFace)
                '################################################################
                'save this shadow matrix for use later
                .shadowMatrix = biasMatrix * SUN_ROTATION

                terrainDepthShader.Use()

                'test only
                GL.Uniform1(terrainDepthShader("map_id"), id)

                'this chunk is for testing. It can be drawn with the others once everything is proved out.
                GL.UniformMatrix4(terrainDepthShader("Ortho_Project"), False, PROJECTIONMATRIX)
                GL.UniformMatrix4(terrainDepthShader("modelMat"), False, theMap.render_set(id).matrix)
                GL.UniformMatrix4(terrainDepthShader("cameraMat"), False, SUN_ROTATION)

                'draw chunk at this othro projection
                GL.BindVertexArray(.VAO)
                GL.DrawElements(PrimitiveType.Triangles,
                                24576,
                                DrawElementsType.UnsignedShort, 0)

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

                For i = 0 To theMap.render_set.Length - 1
                    'draw all terrain chucks to capture shadow from other chunks but the currrent one
                    If i <> id Then
                        GL.Uniform1(terrainDepthShader("map_id"), i)

                        GL.UniformMatrix4(terrainDepthShader("Ortho_Project"), False, PROJECTIONMATRIX)
                        GL.UniformMatrix4(terrainDepthShader("modelMat"), False, theMap.render_set(i).matrix)
                        GL.UniformMatrix4(terrainDepthShader("cameraMat"), False, SUN_ROTATION)

                        GL.BindVertexArray(theMap.render_set(i).VAO)
                        GL.DrawElements(PrimitiveType.Triangles,
                                24576,
                                DrawElementsType.UnsignedShort, 0)
                    End If
                Next
                terrainDepthShader.StopUse()

                '==================================================================
                'draw all buildings . we will need to draw these 2 times. Once with back side and once with forward facing side.
                If MODELS_LOADED And DONT_BLOCK_MODELS Then


                    modelDepthShader.Use()

                    GL.UniformMatrix4(terrainDepthShader("Ortho_Project"), False, PROJECTIONMATRIX)

                    MapGL.Buffers.parameters.Bind(GL_PARAMETER_BUFFER_ARB)

                    GL.BindVertexArray(MapGL.VertexArrays.allMapModels)

                    MapGL.Buffers.indirect.Bind(BufferTarget.DrawIndirectBuffer)
                    GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, IntPtr.Zero, MapGL.indirectDrawCount, 0)

                    GL.Disable(EnableCap.CullFace)

                    MapGL.Buffers.indirect_dbl_sided.Bind(BufferTarget.DrawIndirectBuffer)
                    GL.MultiDrawElementsIndirectCount(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, New IntPtr(8), MapGL.indirectDrawCount, 0)

                    modelDepthShader.StopUse()
                End If
                '==================================================================
                'now that we have the shadow depth texture, we can create the shadow mask.
                FBO_ShadowBaker.FBO_Make_Ready_For_mask_writes(id)
                ortho(FBO_ShadowBaker.texture_size.X)

                GL.ClearColor(0.0F, 0.0F, 1.0F, 1.0)
                GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)
                GL.Enable(EnableCap.CullFace)
                GL.FrontFace(FrontFaceDirection.Cw)

                terrainMaskShader.Use()

                FBO_ShadowBaker.shadow_map.BindUnit(0)

                GL.UniformMatrix4(terrainMaskShader("Ortho_Project"), False, PROJECTIONMATRIX)
                GL.UniformMatrix4(terrainMaskShader("shadowProjection"), False, theMap.render_set(id).shadowMatrix)
                GL.UniformMatrix4(terrainMaskShader("model"), False, theMap.render_set(id).matrix)

                GL.BindVertexArray(.VAO)
                GL.DrawElements(PrimitiveType.Triangles,
                                24576,
                                DrawElementsType.UnsignedShort, 0)


                terrainMaskShader.StopUse()

                'test rendering just to visualize.
                render_test_depth(FBO_ShadowBaker.shadow_map)
                render_test_array(id)
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_ShadowBaker_ID) '=====

            End With
        Next


    End Sub
    Private Sub render_test_depth(ByVal map As GLTexture)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)


        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '======================
        '===========================================================================
        '===========================================================================
        Ortho_main()
        '===========================================================================
        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)
        Dim r As New Rectangle(0F, 0F, FBO_ShadowBaker.depth_map_size, FBO_ShadowBaker.depth_map_size)
        draw_image_rectangle_flipY(r, map)


    End Sub
    Private Sub render_test_array(ByVal id As Integer)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)


        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '======================
        '===========================================================================
        '===========================================================================
        Ortho_main()
        '===========================================================================
        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)
        Dim r As New Rectangle(frmMain.glControl_main.Width / 2.0F, 0F, FBO_ShadowBaker.depth_map_size, FBO_ShadowBaker.depth_map_size)
        draw_image_array(r, FBO_ShadowBaker.gBakerColorArray, id)

        frmMain.glControl_main.SwapBuffers()

    End Sub
    Private Sub clear_culling_and_Lod()
        GL_PUSH_GROUP("cull_Lod_Clear")

        'clear atomic counter
        GL.ClearNamedBufferSubData(MapGL.Buffers.parameters.buffer_id, PixelInternalFormat.R32ui, IntPtr.Zero, 3 * Marshal.SizeOf(Of UInt32), PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero)

        cullLodClearShader.Use()

        GL.Uniform1(cullLodClearShader("numModelInstances"), MapGL.numModelInstances)

        Dim workGroupSize = 16
        Dim numGroups = (MapGL.numModelInstances + workGroupSize - 1) \ workGroupSize
        GL.DispatchCompute(numGroups, 1, 1)

        GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit)

        cullLodClearShader.StopUse()

        GL_POP_GROUP()

    End Sub

    Public Sub make_LQ_textures()

        ' call to bake shadows
        'bake_terrain_shadows()

        Dim quailty As Integer = 360 '<-- adjusts size of the texture

        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(quailty, quailty), 2))

        FBO_mixer_set.LayerCount = theMap.render_set.Length

        FBO_mixer_set.mipCount = numLevels

        FBO_mixer_set.FBO_Initialize(New Point(quailty, quailty))

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_Mixer_ID)

        ortho(quailty)

        For map = 0 To theMap.render_set.Length - 1
            create_layer(map)
        Next

        FBO_mixer_set.make_mips()
    End Sub
    Private Sub create_layer(ByVal map As Integer)

        FBO_mixer_set.attach_array_layer(map)

        t_mixerShader.Use()

        'TerrainShader.Use()  '<-------------- Shader Bind
        '------------------------------------------------
        theMap.GLOBAL_AM_ID.BindUnit(21)

        'pre created shadow masks
        'FBO_ShadowBaker.gBakerColorArray.BindUnit(23)

        GL.Uniform1(t_mixerShader("map_id"), map)

        GL.Uniform2(t_mixerShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(t_mixerShader("map_center"), -b_x_min, b_y_max)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, PROJECTIONMATRIX)

        GL.Uniform2(t_mixerShader("me_location"), theMap.chunks(map).location.X, theMap.chunks(map).location.Y) 'me_location

        'bind all the data for this chunk
        With theMap.render_set(map)
            .layersStd140_ubo.BindBase(0)
            Dim i = map
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
            GL.BindVertexArray(.VAO)
            GL.DrawElements(PrimitiveType.Triangles,
                    24576,
                    DrawElementsType.UnsignedShort, 0)
        End With

        t_mixerShader.StopUse()

        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        unbind_textures(22)



    End Sub


    Public Sub ortho(ByVal size As Integer)
        GL.Viewport(0, 0, size, size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0, -50.0, 50.0F, -3000.0F, 3000.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub
End Module

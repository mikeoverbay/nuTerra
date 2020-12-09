Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime.InteropServices

Module LQ_Texture_creator

    Public Sub bake_terrain_shadows()

        Dim quailty As Integer = 512 '<-- adjusts size of the mask textures

        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(quailty, quailty), 2))

        FBO_ShadowBaker.LayerCount = theMap.render_set.Length

        FBO_ShadowBaker.mipCount = numLevels

        FBO_ShadowBaker.depth_map_size = 1024 ' Depth map size

        FBO_ShadowBaker.FBO_Initialize(New Point(quailty, quailty))

        If Not FBO_ShadowBaker.FBO_Make_Ready_For_Shadow_writes() Then
            Stop
        End If
        If Not DONT_BLOCK_TERRAIN Then
            Return
        End If
        'frmMain.glControl_main.Context.MakeCurrent(frmMain.glControl_main.WindowInfo)
        GL.Clear(ClearBufferMask.DepthBufferBit)

        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_ShadowBaker_ID) '=====
        '===========================================================================
        Dim map_id As Integer = 62
        Dim loc As New Point

        GL.DepthFunc(DepthFunction.Less)
        GL.ClearDepth(1.0F)
        GL.ClearColor(1.0F, 1.0F, 1.0F, 1.0)

        Dim sunMatrix = set_sun_view_matrix()

        GL.Enable(EnableCap.DepthTest)
        GL.Enable(EnableCap.CullFace)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.FrontFace(FrontFaceDirection.Ccw)

        If MODELS_LOADED And DONT_BLOCK_MODELS Then
            clear_culling_and_Lod()
        End If

        For id = 0 To theMap.render_set.Length - 1
            'For id = 48 To 48

            FBO_ShadowBaker.FBO_Make_Ready_For_Shadow_writes()

            GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

            With theMap.render_set(id)

                loc.X = .matrix.Row3.X
                loc.Y = .matrix.Row3.Z
                Dim loc_z As Single = (theMap.v_data(id).min_height + theMap.v_data(id).max_height) / 2.0F

                Dim eye As New Vector3(LIGHT_POS.X, LIGHT_POS.Y, LIGHT_POS.Z)
                Dim at As New Vector3(loc.X, loc_z, loc.Y)
                SUN_CAMERA = Matrix4.LookAt(eye, at, New Vector3(0.0F, 1.0F, 0.0))
                Dim rv = New Vector4(50, loc_z, 50, 1.0)

                'set up the ortho window for each chunk
                Sun_Ortho_view(-75.0, 75.0, -115.0, 35.0, 0.0F, 0.0F)


                'save this shadow matrix for use later
                .shadowMatrix = .matrix * SUN_CAMERA * PROJECTIONMATRIX

                terrainDepthShader.Use()

                'test only
                GL.Uniform1(terrainDepthShader("map_id"), id)

                'this chunk is for testing. It can be drawn with the others once everything is proved out.
                GL.UniformMatrix4(terrainDepthShader("Ortho_Project"), False, .matrix * SUN_CAMERA * PROJECTIONMATRIX)
                'draw chunk at this othro projection
                GL.BindVertexArray(.VAO)
                GL.DrawElements(PrimitiveType.Triangles,
                                24576,
                                DrawElementsType.UnsignedShort, 0)

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

                For i = 0 To theMap.render_set.Length - 1
                    'draw all terrain chucks to capture shadow from other chunks
                    If i <> id Then
                        GL.Uniform1(terrainDepthShader("map_id"), i)

                        GL.UniformMatrix4(terrainDepthShader("Ortho_Project"), False, theMap.render_set(i).matrix * SUN_CAMERA * PROJECTIONMATRIX)

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

                    GL.UniformMatrix4(modelDepthShader("Ortho_Project"), False, SUN_CAMERA * PROJECTIONMATRIX)

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
                FBO_ShadowBaker.FBO_Make_Ready_For_mask_writes()
                FBO_ShadowBaker.attach_array_layer(id)
                ortho(FBO_ShadowBaker.texture_size.X)

                terrainMaskShader.Use()

                GL.UniformMatrix4(terrainMaskShader("Ortho_Project"), False, PROJECTIONMATRIX)
                GL.UniformMatrix4(terrainMaskShader("shadowProjection"), False, .shadowMatrix)

                GL.BindVertexArray(.VAO)
                GL.DrawElements(PrimitiveType.Triangles,
                                24576,
                                DrawElementsType.UnsignedShort, 0)


                terrainMaskShader.StopUse()


            End With
        Next

        '    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)


        ''===========================================================================
        'GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '======================
        ''===========================================================================
        ''===========================================================================
        'Ortho_main()
        ''===========================================================================
        'GL.Disable(EnableCap.DepthTest)
        'GL.Disable(EnableCap.CullFace)
        'Dim r As New Rectangle(0F, 0F, FBO_ShadowBaker.depth_map_size, FBO_ShadowBaker.depth_map_size)
        'draw_image_rectangle_flipY(r, FBO_ShadowBaker.shadow_map)

        ''frmMain.glControl_main.SwapBuffers()

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

        bake_terrain_shadows()

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
        JITTER_TEXTURE_ID.BindUnit(22)

        'pre created shadow masks
        FBO_ShadowBaker.gBakerColorArray.BindUnit(23)


        GL.Uniform2(t_mixerShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(t_mixerShader("map_center"), -b_x_min, b_y_max)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, PROJECTIONMATRIX)

        GL.Uniform2(t_mixerShader("me_location"), theMap.chunks(map).location.X, theMap.chunks(map).location.Y) 'me_location

        'bind all the data for this chunk
        With theMap.render_set(map)
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

        t_mixerShader.StopUse()

        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        unbind_textures(22)



    End Sub


    Public Sub ortho(ByVal size As Integer)
        GL.Viewport(0, 0, size, size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0, -50.0, 50.0F, -300.0F, 300.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub
End Module

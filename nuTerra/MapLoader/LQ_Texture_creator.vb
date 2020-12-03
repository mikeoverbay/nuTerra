Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module LQ_Texture_creator

    Public Sub bake_terrain_shadows()
        Dim quailty As Integer = 512 '<-- adjusts size of the shadow textures
        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(quailty, quailty), 2))
        FBO_shadowBaker.layerCount = theMap.render_set.Length
        FBO_ShadowBaker.mipCount = numLevels
        FBO_ShadowBaker.FBO_Initialize(New Point(quailty, quailty))

        If Not FBO_ShadowBaker.FBO_Make_Ready_For_Shadow_writes() Then
            Stop
        End If
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

        'water BS
        'GL.Uniform3(t_mixerShader("waterColor"),
        '                Map_wetness.waterColor.X,
        '                Map_wetness.waterColor.Y,
        '                Map_wetness.waterColor.Z)

        'GL.Uniform1(t_mixerShader("waterAlpha"), Map_wetness.waterAlpha)

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


    Private Sub ortho(ByVal size As Integer)
        GL.Viewport(0, 0, size, size)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0, -50.0, 50.0F, -300.0F, 300.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub
End Module

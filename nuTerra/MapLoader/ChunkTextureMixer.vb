Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Ionic
Imports Ionic.Zip
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports OpenTK
Imports Tao.DevIl

Module ChunkTextureMixer

    Public Sub make_pre_mixed_terrain()

        Dim layerCount = theMap.chunks.Length - 1

        '------------------------------------------------
        'initailize fbo
        FBO_mixer_set.LayerCount = layerCount
        FBO_mixer_set.mipCount = 3
        FBO_mixer_set.FBO_Initialize(New Point(1024, 1024))

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO_Mixer_ID)
        fbo_mixer_ortho(New Point(1024, 1024))
        '------------------------------------------------

        '------------------------------------------------
        mixTerrainShader.Use()  '<-------------- Shader Bind
        '------------------------------------------------

        'shit load of textures to bind

        TEST_IDS(0).BindUnit(0)
        TEST_IDS(1).BindUnit(1)
        TEST_IDS(2).BindUnit(2)
        TEST_IDS(3).BindUnit(3)
        TEST_IDS(4).BindUnit(4)
        TEST_IDS(5).BindUnit(5)
        TEST_IDS(6).BindUnit(6)
        TEST_IDS(7).BindUnit(7)

        theMap.GLOBAL_AM_ID.BindUnit(29) '<----------------- Texture Bind
        m_normal_id.BindUnit(30)
        'water BS

        GL.Uniform2(mixTerrainShader("map_size"), MAP_SIZE.X + 1, MAP_SIZE.Y + 1)
        GL.Uniform2(mixTerrainShader("map_center"), -b_x_min, b_y_max)

        GL.Uniform1(mixTerrainShader("show_test"), SHOW_TEST_TEXTURES)

        'Dim max_binding As Integer = GL.GetInteger(GetPName.MaxUniformBufferBindings)

        For i = 0 To theMap.render_set.Length - 1

            FBO_mixer_set.attach_array_layer(i)

            GL.ClearColor(Color.OliveDrab)
            GL.Clear(ClearBufferMask.ColorBufferBit)

            GL.UniformMatrix4(mixTerrainShader("Ortho_Project"), False, PROJECTIONMATRIX)

            ' It will be important to create the normals using the location on the map.
            GL.UniformMatrix3(mixTerrainShader("normalMatrix"), True, Matrix3.Invert(New Matrix3(theMap.render_set(i).matrix))) 'NormalMatrix

            GL.Uniform2(mixTerrainShader("me_location"), theMap.chunks(i).location.X, theMap.chunks(i).location.Y) 'me_location

            'bind all the data for this chunk
            With theMap.render_set(i)
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, .layersStd140_ubo)

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
        Next

        mixTerrainShader.StopUse()

        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        unbind_textures(30)
        FBO_mixer_set.make_mips()

    End Sub
    Private Sub fbo_mixer_ortho(ByVal b_size As Point)
        GL.Viewport(0, 0, b_size.X, b_size.Y)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(-50.0F, 50.0F, 50.0F, -50.0F, -300.0F, 300.0F)
        VIEWMATRIX = Matrix4.Identity
    End Sub
End Module

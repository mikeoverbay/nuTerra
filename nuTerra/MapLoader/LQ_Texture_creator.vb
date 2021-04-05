Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime.InteropServices

Module LQ_Texture_creator
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

        GL.BindVertexArray(MapGL.VertexArrays.allTerrainChunks)
        MapGL.Buffers.terrain_indirect.Bind(BufferTarget.DrawIndirectBuffer)

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

        GL.Uniform3(t_mixerShader("waterColor"), Map_wetness.waterColor.X, Map_wetness.waterColor.Y, Map_wetness.waterColor.Z)

        GL.UniformMatrix4(t_mixerShader("Ortho_Project"), False, PROJECTIONMATRIX)

        'bind all the data for this chunk
        With theMap.render_set(map)
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
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
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

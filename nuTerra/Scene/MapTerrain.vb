Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Public Class MapTerrain
    Implements IDisposable
    Public outland_vertices_buffer As GLBuffer
    Public outland_indices_buffer As GLBuffer
    Public outland_vao As GLVertexArray


    Public matrices As GLBuffer
    Public indirect_buffer As GLBuffer
    Public vertices_buffer As GLBuffer
    Public indices_buffer As GLBuffer
    Public all_chunks_vao As GLVertexArray

    Public vt As VirtualTexture
    Public vtInfo As VirtualTextureInfo
    Public feedback As FeedbackBuffer

    Public GLOBAL_AM_ID As GLTexture

    Public Sub terrain_vt_pass()
        GL_PUSH_GROUP("terrain_vt_pass")

        feedback.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, feedback.width, feedback.height)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        GL.Enable(EnableCap.CullFace)

        TerrainVTMIPShader.Use()

        GL.Uniform1(TerrainVTMIPShader("MipBias"), CSng(vt.MipBias))

        indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)
        all_chunks_vao.Bind()

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible Then
                GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
            End If
        Next

        TerrainVTMIPShader.StopUse()

        feedback.Download()
        vt.Update(feedback.Requests)

        feedback.clear()
        feedback.copy()

        GL_POP_GROUP()
    End Sub

    Public Sub RebuildVTAtlas()
        LogThis("REBUILD ATLAS")

        vtInfo = New VirtualTextureInfo With {
            .TileSize = TILE_SIZE,
            .VirtualTextureSize = TILE_SIZE * VT_NUM_PAGES
            }

        vt?.Dispose()
        vt = New VirtualTexture(vtInfo, NUM_TILES, UPLOADS_PER_FRAME)

        feedback?.Dispose()
        feedback = New FeedbackBuffer(vtInfo, FEEDBACK_WIDTH, FEEDBACK_HEIGHT)

        CommonProperties.VirtualTextureSize = vtInfo.VirtualTextureSize
        CommonProperties.AtlasScale = 1.0F / (vtInfo.VirtualTextureSize / vtInfo.TileSize)
        CommonProperties.PageTableSize = vtInfo.PageTableSize
        CommonProperties.update()
    End Sub

    Public Sub Draw_outland()
        GL_PUSH_GROUP("draw_outland")
        ' EANABLE FACE CULLING
        GL.Disable(EnableCap.CullFace)

        '=========================================================
        ' Cascade near
        outlandShader.Use()

        OUTLAND_height_MAP.BindUnit(1)
        OUTLAND_NORMAL_MAP.BindUnit(2)
        OUTLAND_TILE.BindUnit(3)
        'there is something odd with textures when there is far outland terrain.
        OUTLAND_TILES(7).BindUnit(4)
        OUTLAND_TILES(6).BindUnit(5)
        OUTLAND_TILES(5).BindUnit(6)
        OUTLAND_TILES(4).BindUnit(7)

        GL.Uniform1(outlandShader("tile_scale"), OUTLAND_TILE_SCALE / 10.0F)

        GL.Uniform1(outlandShader("y_range"), theMap.near_y_height)
        GL.Uniform1(outlandShader("y_offset"), theMap.near_y_offset)

        GL.Uniform2(outlandShader("scale"), theMap.near_scale.X, theMap.near_scale.Y)
        GL.Uniform2(outlandShader("center_offset"), theMap.center_offset.X, theMap.center_offset.Y)

        GL.UniformMatrix4(outlandShader("nMatrix"), False, PerViewData.invView)

        outland_vao.Bind()

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        GL.DrawElements(PrimitiveType.Triangles, theMap.outland_Vdata.indicies_32.Length * 3, DrawElementsType.UnsignedInt, IntPtr.Zero)
        unbind_textures(7)

        '=========================================================
        ' Cascade far
        If CASCADE_LEVELS = 2 Then

            OUTLAND_height_CASCADE_MAP.BindUnit(1)
            OUTLAND_NORMAL_CASCADE_MAP.BindUnit(2)
            OUTLAND_TILE_CASCADE.BindUnit(3)

            OUTLAND_TILES(0).BindUnit(4)
            OUTLAND_TILES(1).BindUnit(5)
            OUTLAND_TILES(2).BindUnit(6)
            OUTLAND_TILES(3).BindUnit(7)

            GL.Uniform1(outlandShader("tile_scale"), OUTLAND_TILE_SCALE_CASCADE / 10.0F)

            GL.Uniform1(outlandShader("y_range"), theMap.far_y_height)
            GL.Uniform1(outlandShader("y_offset"), theMap.far_y_offset)

            GL.Uniform2(outlandShader("scale"), theMap.far_scale.X, theMap.far_scale.Y)
            GL.Uniform2(outlandShader("center_offset"), theMap.center_offset.X, theMap.center_offset.Y)

            GL.UniformMatrix4(outlandShader("nMatrix"), False, PerViewData.invView)

            outland_vao.Bind()

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

            GL.DrawElements(PrimitiveType.Triangles, theMap.outland_Vdata.indicies_32.Length * 3, DrawElementsType.UnsignedInt, IntPtr.Zero)

        End If


        outlandShader.StopUse()
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        unbind_textures(7)
        GL_POP_GROUP()
    End Sub

    Public Sub draw_terrain()
        GL_PUSH_GROUP("draw_terrain")

        ' EANABLE FACE CULLING
        GL.Enable(EnableCap.CullFace)

        ' BIND LQ SHADER
        TerrainLQShader.Use()

        ' BIND VT TEXTURES
        vt.Bind()

        ' BIND TERRAIN VAO
        all_chunks_vao.Bind()

        ' BIND TERRAIN INDIRECT BUFFER
        indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.LQ Then
                ' CALC NORMAL MATRIX FOR CHUNK
                GL.UniformMatrix3(TerrainLQShader("normalMatrix"), False, New Matrix3(PerViewData.view * theMap.render_set(i).matrix))

                ' DRAW CHUNK INDIRECT
                GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
            End If
        Next

        ' UNBIND SHADER
        TerrainLQShader.StopUse()

        If USE_TESSELLATION Then
            GL_PUSH_GROUP("draw_terrain: tessellation")

            ' BIND HQ SHADER
            TerrainHQShader.Use()

            For i = 0 To theMap.render_set.Length - 1
                If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.HQ Then
                    ' CALC NORMAL MATRIX FOR CHUNK
                    GL.UniformMatrix3(TerrainHQShader("normalMatrix"), False, New Matrix3(PerViewData.view * theMap.render_set(i).matrix))

                    ' DRAW CHUNK INDIRECT
                    GL.DrawElementsIndirect(PrimitiveType.Patches, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                End If
            Next

            ' UNBIND SHADER
            TerrainHQShader.StopUse()

            GL_POP_GROUP()
        End If

        ' RESTORE STATE
        GL.Disable(EnableCap.CullFace)

        If WIRE_TERRAIN Then
            GL_PUSH_GROUP("draw_terrain: wire")

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
            MainFBO.attach_CF()

            TerrainNormals.Use()

            GL.Uniform1(TerrainNormals("prj_length"), 0.5F)
            GL.Uniform1(TerrainNormals("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
            GL.Uniform1(TerrainNormals("show_wireframe"), CInt(WIRE_TERRAIN))

            For i = 0 To theMap.render_set.Length - 1
                If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality <> TerrainQuality.HQ Then
                    GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                End If
            Next

            TerrainNormals.StopUse()

            If USE_TESSELLATION Then
                TerrainNormalsHQ.Use()

                GL.Uniform1(TerrainNormalsHQ("prj_length"), 0.2F)
                GL.Uniform1(TerrainNormalsHQ("mode"), NORMAL_DISPLAY_MODE) ' 0 none, 1 by face, 2 by vertex
                GL.Uniform1(TerrainNormalsHQ("show_wireframe"), CInt(WIRE_TERRAIN))

                For i = 0 To theMap.render_set.Length - 1
                    If theMap.render_set(i).visible AndAlso theMap.render_set(i).quality = TerrainQuality.HQ Then
                        GL.DrawElementsIndirect(PrimitiveType.Patches, DrawElementsType.UnsignedShort, New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
                    End If
                Next

                TerrainNormalsHQ.StopUse()
            End If

            ' RESTORE STATE
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

            GL_POP_GROUP()
        End If

        ' UNBIND VT TEXTURES
        vt.Unbind()

        GL_POP_GROUP()
    End Sub

    Public Sub draw_terrain_grids()
        GL_PUSH_GROUP("draw_terrain_grids")

        MainFBO.attach_C()
        'GL.DepthMask(False)
        GL.Enable(EnableCap.DepthTest)
        TerrainGrids.Use()
        GL.Uniform2(TerrainGrids("bb_tr"), MAP_BB_UR.X, MAP_BB_UR.Y)
        GL.Uniform2(TerrainGrids("bb_bl"), MAP_BB_BL.X, MAP_BB_BL.Y)
        GL.Uniform1(TerrainGrids("g_size"), PLAYER_FIELD_CELL_SIZE)

        GL.Uniform1(TerrainGrids("show_border"), CInt(SHOW_BORDER))
        GL.Uniform1(TerrainGrids("show_chunks"), CInt(SHOW_CHUNKS))
        GL.Uniform1(TerrainGrids("show_grid"), CInt(SHOW_GRID))

        MainFBO.gGMF.BindUnit(0)

        indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)
        all_chunks_vao.Bind()

        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort, IntPtr.Zero, theMap.render_set.Length, 0)
        TerrainGrids.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Public Sub draw_terrain_ids()
        GL_PUSH_GROUP("draw_terrain_ids")

        For i = 0 To theMap.render_set.Length - 1
            If theMap.render_set(i).visible Then ' Dont do math on no-visible chunks

                Dim v As Vector4
                v.Y = theMap.v_data(i).avg_heights
                v.W = 1.0

                Dim sp = UnProject_Chunk(v, theMap.render_set(i).matrix)

                If sp.Z > 0.0F Then
                    Dim s = theMap.chunks(i).name + ":" + i.ToString("000")
                    draw_text(s, sp.X, sp.Y, Color4.Yellow, True, 1)
                    s = String.Format("{0}, {1}", theMap.render_set(i).matrix.Row3(0), theMap.render_set(i).matrix.Row3(2))
                    draw_text(s, sp.X, sp.Y - 19, Color4.Yellow, True, 1)

                End If
            End If
        Next

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        matrices?.Dispose()
        indirect_buffer?.Dispose()
        vertices_buffer?.Dispose()
        indices_buffer?.Dispose()
        all_chunks_vao?.Dispose()

        vt?.Dispose()
        feedback?.Dispose()

        GLOBAL_AM_ID?.Dispose()
    End Sub
End Class

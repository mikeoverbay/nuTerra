Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports ImGuiNET
Imports System.IO
Public Class MapTerrain
    Implements IDisposable

    ReadOnly scene As MapScene

    Public outland_vertices_buffer As GLBuffer
    Public outland_indices_buffer As GLBuffer
    Public outland_vao As GLVertexArray
    ' The far cascade is its own ring mesh (hole = near cascade footprint), so
    ' it has its own index buffer and VAO over the shared vertex buffer.
    Public outland_far_indices_buffer As GLBuffer
    Public outland_far_vao As GLVertexArray
    Public outland_near_index_count As Integer
    Public outland_far_index_count As Integer

    Public matrices As GLBuffer
    Public indirect_buffer As GLBuffer
    Public vertices_buffer As GLBuffer
    Public indices_buffer As GLBuffer
    Public all_chunks_vao As GLVertexArray

    Public vt As VirtualTexture
    Public vtInfo As VirtualTextureInfo
    Public feedback As FeedbackBuffer

    Public GLOBAL_AM_ID As GLTexture

    'outland texture ids
    Public OUTLAND_NORMAL_MAP As GLTexture
    Public OUTLAND_NORMAL_CASCADE_MAP As GLTexture
    Public OUTLAND_TILE As GLTexture
    Public OUTLAND_TILE_CASCADE As GLTexture
    Public OUTLAND_height_MAP As GLTexture
    Public OUTLAND_height_CASCADE_MAP As GLTexture
    Public OUTLAND_TILES() As GLTexture
    Public CASCADE_LEVELS As Integer = 0
    Public OUTLAND_TILE_SCALE As Single
    Public OUTLAND_TILE_SCALE_CASCADE As Single

    ' Baked at load by bake_outland_albedo - the game ships no outland albedo,
    ' its engine bakes tilemap x tile set into one texture per cascade and
    ' PBS_ext_outland only ever samples the result. The tile set and tilemaps
    ' are not touched again after this bake.
    Public OUTLAND_ALBEDO As GLTexture
    Public OUTLAND_ALBEDO_CASCADE As GLTexture
    ' 1x1 neutral (0.5, 0.5, 0.5, 0.5) stand-in: makes the game's detail
    ' combine an exact no-op until a real detail albedo is chosen.
    Public OUTLAND_DETAIL As GLTexture

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

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

        OUTLAND_ALBEDO.BindUnit(0)
        OUTLAND_height_MAP.BindUnit(1)
        OUTLAND_NORMAL_MAP.BindUnit(2)
        OUTLAND_DETAIL.BindUnit(3)

        ' the game hardcodes 64 detail repeats over the cascade
        GL.Uniform1(outlandShader("detail_tiles"), 64.0F)

        GL.Uniform1(outlandShader("y_range"), theMap.near_y_height)
        GL.Uniform1(outlandShader("y_offset"), theMap.near_y_offset)

        GL.Uniform2(outlandShader("scale"), theMap.near_scale.X, theMap.near_scale.Y)
        GL.Uniform2(outlandShader("center_offset"), theMap.center_offset.X, theMap.center_offset.Y)

        outland_vao.Bind()

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        GL.DrawElements(PrimitiveType.Triangles, outland_near_index_count, DrawElementsType.UnsignedInt, IntPtr.Zero)

        '=========================================================
        ' Cascade far
        If CASCADE_LEVELS = 2 Then

            OUTLAND_ALBEDO_CASCADE.BindUnit(0)
            OUTLAND_height_CASCADE_MAP.BindUnit(1)
            OUTLAND_NORMAL_CASCADE_MAP.BindUnit(2)

            GL.Uniform1(outlandShader("y_range"), theMap.far_y_height)
            GL.Uniform1(outlandShader("y_offset"), theMap.far_y_offset)

            GL.Uniform2(outlandShader("scale"), theMap.far_scale.X, theMap.far_scale.Y)
            GL.Uniform2(outlandShader("center_offset"), theMap.center_offset.X, theMap.center_offset.Y)

            outland_far_vao.Bind()

            GL.DrawElements(PrimitiveType.Triangles, outland_far_index_count, DrawElementsType.UnsignedInt, IntPtr.Zero)

        End If


        outlandShader.StopUse()

        unbind_textures(3)
        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Load-time bake of one albedo per outland cascade, the way the game's
    ''' engine does it - PBS_ext_outland never sees tiles or tilemap.
    '''
    ''' A tilemap texel (RGBA4 nibbles) is two layers, not four weights:
    ''' r/g = tile indices, b/a = their weights (see outland_bake_accum.frag).
    ''' One additive fullscreen pass per tile accumulates rgb = tile * w, a = w
    ''' into RGBA16F; a resolve pass divides by total weight into the final
    ''' mipped RGBA8. Per-tile passes exist because GLSL cannot index a sampler
    ''' array by a value fetched from a texture.
    ''' </summary>
    Public Sub bake_outland_albedo()
        GL_PUSH_GROUP("bake_outland_albedo")

        If OUTLAND_DETAIL Is Nothing Then
            OUTLAND_DETAIL = GLTexture.Create(TextureTarget.Texture2D, "outland_detail_neutral")
            OUTLAND_DETAIL.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            OUTLAND_DETAIL.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            OUTLAND_DETAIL.Storage2D(1, SizedInternalFormat.Rgba8, 1, 1)
            OUTLAND_DETAIL.SubImage2D(0, 0, 0, 1, 1, OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, New Byte() {128, 128, 128, 128})
        End If

        Dim span_near = theMap.outland_bounds_max.X - theMap.outland_bounds_min.X
        OUTLAND_ALBEDO = bake_one_outland_cascade("outland_albedo_near", OUTLAND_TILE, span_near, OUTLAND_TILE_SCALE)

        If CASCADE_LEVELS = 2 Then
            Dim span_far = theMap.outland_Cascade_bounds_max.X - theMap.outland_Cascade_bounds_min.X
            OUTLAND_ALBEDO_CASCADE = bake_one_outland_cascade("outland_albedo_far", OUTLAND_TILE_CASCADE, span_far, OUTLAND_TILE_SCALE_CASCADE)
        End If

        ' Restore what the frame loop assumes (GL globals leak between passes).
        GL.Disable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        GL.Enable(EnableCap.CullFace)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        GL_POP_GROUP()
    End Sub

    Private Function bake_one_outland_cascade(name As String, tile_map As GLTexture, span As Single, tileScale As Single) As GLTexture
        Const SIZE As Integer = 2048
        Const MIPS As Integer = 12 ' log2(2048) + 1

        ' tileScale is metres per tile repeat (101_dday authors 20/20,
        ' 05_prohorovka 90/900 - only m-per-repeat stays sane at 42 km).
        Dim tile_repeats = span / Math.Max(tileScale, 0.001F)

        Dim accum = GLTexture.Create(TextureTarget.Texture2D, name + "_accum")
        accum.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        accum.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        accum.Storage2D(1, SizedInternalFormat.Rgba16f, SIZE, SIZE)

        Dim final = GLTexture.Create(TextureTarget.Texture2D, name)
        final.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
        final.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        final.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        final.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
        final.Storage2D(MIPS, SizedInternalFormat.Rgba8, SIZE, SIZE)

        Dim fbo = GLFramebuffer.Create(name + "_fbo")
        GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)

        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Viewport(0, 0, SIZE, SIZE)

        ' ---- accumulate: one additive pass per tile -------------------------
        fbo.Texture(FramebufferAttachment.ColorAttachment0, accum, 0)
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.ColorAttachment0)
        fbo.Bind(FramebufferTarget.Framebuffer)

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One)

        outlandBakeAccumShader.Use()
        tile_map.BindUnit(0)
        GL.Uniform1(outlandBakeAccumShader("tile_repeats"), tile_repeats)

        defaultVao.Bind()
        For i = 0 To OUTLAND_TILES.Length - 1
            OUTLAND_TILES(i).BindUnit(1)
            GL.Uniform1(outlandBakeAccumShader("tile_index"), i)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        Next
        outlandBakeAccumShader.StopUse()

        GL.Disable(EnableCap.Blend)

        ' ---- resolve: divide by total weight --------------------------------
        fbo.Texture(FramebufferAttachment.ColorAttachment0, final, 0)
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.ColorAttachment0)
        fbo.Bind(FramebufferTarget.Framebuffer)

        outlandBakeResolveShader.Use()
        accum.BindUnit(0)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        outlandBakeResolveShader.StopUse()

        GL.GenerateTextureMipmap(final.texture_id)

        unbind_textures(1)
        fbo.Dispose()
        accum.Dispose()

        Console.WriteLine("outland bake {0}: span {1:0} m, tileScale {2:0.#} m -> {3:0.#} repeats, {4} tiles, err={5}",
                          name, span, tileScale, tile_repeats, OUTLAND_TILES.Length, GL.GetError())
        If DUMP_OUTLAND_BAKES Then dump_outland_bake(final, SIZE, name)
        Return final
    End Function

    ''' <summary>Write each baked outland albedo to %TEMP%\nuTerra\*.png at load,
    ''' for eyeballing the bake without a debugger. Cheap; flip off when trusted.</summary>
    Private Const DUMP_OUTLAND_BAKES As Boolean = True

    ''' <summary>Debug dump of a baked outland albedo to %TEMP%\nuTerra\*.png.</summary>
    Private Sub dump_outland_bake(tex As GLTexture, size As Integer, name As String)
        Try
            dump_outland_bake_core(tex, size, name)
            Console.WriteLine("outland bake dump written: {0}", name)
        Catch ex As Exception
            Console.WriteLine("outland bake dump FAILED: {0}: {1}", name, ex.Message)
        End Try
    End Sub

    Private Sub dump_outland_bake_core(tex As GLTexture, size As Integer, name As String)
        Dim px(size * size * 4 - 1) As Byte
        GL.GetTextureImage(tex.texture_id, 0, OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, px.Length, px)
        ' RGBA -> BGRA for GDI+
        For i = 0 To px.Length - 4 Step 4
            Dim r = px(i)
            px(i) = px(i + 2)
            px(i + 2) = r
        Next
        Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra")
        IO.Directory.CreateDirectory(dir)
        Using bmp As New Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb)
            Dim bd = bmp.LockBits(New Drawing.Rectangle(0, 0, size, size), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb)
            Marshal.Copy(px, 0, bd.Scan0, px.Length)
            bmp.UnlockBits(bd)
            bmp.Save(IO.Path.Combine(dir, name + ".png"), Drawing.Imaging.ImageFormat.Png)
        End Using
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
                GL.UniformMatrix3(TerrainLQShader("normalMatrix"), False, New Matrix3(scene.camera.PerViewData.view * theMap.render_set(i).matrix))

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
                    GL.UniformMatrix3(TerrainHQShader("normalMatrix"), False, New Matrix3(scene.camera.PerViewData.view * theMap.render_set(i).matrix))

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
        Dim list = ImGui.GetBackgroundDrawList()
        Dim col = ImGui.GetColorU32(New Numerics.Vector4(1.0, 1.0, 0, 1.0))

        For i = 0 To theMap.render_set.Length - 1
            If Not theMap.render_set(i).visible Then
                ' Dont do math on no-visible chunks
                Continue For
            End If

            Dim v As Vector4
            v.Y = theMap.v_data(i).avg_heights
            v.W = 1.0

            Dim sp = UnProject_Chunk(v, theMap.render_set(i).matrix)

            If sp.Z > 0.0F Then
                Dim s = theMap.chunks(i).name + ":" + i.ToString("000")
                list.AddText(New Numerics.Vector2(sp.X, sp.Y), col, s)
                s = String.Format("{0}, {1}", theMap.render_set(i).matrix.Row3(0), theMap.render_set(i).matrix.Row3(2))
                list.AddText(New Numerics.Vector2(sp.X, sp.Y - 19), col, s)
            End If
        Next
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

        If map_scene.OUTLAND_LOADED Then
            OUTLAND_NORMAL_MAP?.Dispose()
            OUTLAND_NORMAL_CASCADE_MAP?.Dispose()
            OUTLAND_TILE?.Dispose()
            OUTLAND_TILE_CASCADE?.Dispose()
            OUTLAND_height_MAP?.Dispose()
            OUTLAND_height_CASCADE_MAP?.Dispose()
            For Each it In OUTLAND_TILES
                it.Dispose()
            Next
            OUTLAND_ALBEDO?.Dispose()
            OUTLAND_ALBEDO_CASCADE?.Dispose()
            OUTLAND_DETAIL?.Dispose()
            outland_vertices_buffer?.Dispose()
            outland_indices_buffer?.Dispose()
            outland_far_indices_buffer?.Dispose()
            outland_vao?.Dispose()
            outland_far_vao?.Dispose()
        End If
    End Sub

    Private Function make_surface_normal(ByVal e1 As Assimp.Vector3D, e2 As Assimp.Vector3D) As Assimp.Vector3D
        Dim no As Assimp.Vector3D

        no = Assimp.Vector3D.Cross(e1, e2)
        If no.LengthSquared() > 0.0F Then
            no.Normalize()
        End If
        Return no
    End Function
    Private Function check_map_border(v1 As Assimp.Vector3D) As Boolean
        Select Case True
            Case v1.X = (-b_x_max - 1.0F) * 100.0F
                Return True
            Case v1.X = -b_x_min * 100.0F
                Return True
            Case v1.Z = b_y_max * 100.0F
                Return True
            Case v1.Z = (b_y_min - 1.0F) * 100.0F
                Return True
        End Select
        Return False
    End Function
    Public Sub Export(ByRef path As String)
        Dim num_chunks = theMap.render_set.Length
        Dim num_verts = vertices_buffer.size / Marshal.SizeOf(Of TerrainVertex)
        Dim num_indices = indices_buffer.size / Marshal.SizeOf(Of UShort)
        Dim num_verts_in_chunk = num_verts / num_chunks

        Dim indirectData(num_chunks - 1) As DrawElementsIndirectCommand
        GL.GetNamedBufferSubData(indirect_buffer.buffer_id, IntPtr.Zero, indirect_buffer.size, indirectData)

        Dim verticesData(num_verts - 1) As TerrainVertex
        GL.GetNamedBufferSubData(vertices_buffer.buffer_id, IntPtr.Zero, vertices_buffer.size, verticesData)

        Dim indicesData(num_indices - 1) As UShort
        GL.GetNamedBufferSubData(indices_buffer.buffer_id, IntPtr.Zero, indices_buffer.size, indicesData)

        Dim matricesData(num_chunks - 1) As TerrainChunkInfo
        GL.GetNamedBufferSubData(matrices.buffer_id, IntPtr.Zero, matrices.size, matricesData)

        Dim a, b, c, k As Single

        Dim scene As New Assimp.Scene

        scene.RootNode = New Assimp.Node("Root")
        If Not Directory.Exists(path + MAP_NAME_NO_PATH) Then
            Directory.CreateDirectory(path + MAP_NAME_NO_PATH)
        End If
        path += MAP_NAME_NO_PATH + "/"
        If File.Exists(path + MAP_NAME_NO_PATH + ".stl") Then
            File.Delete(path + MAP_NAME_NO_PATH + ".stl")
        End If
        Dim file_h = IO.File.OpenWrite(path + MAP_NAME_NO_PATH + ".stl")

        'top and bottom face count
        Dim total_stl_face_count As UInt32 = 16384 * num_chunks
        'add wall count for out side chunks
        Dim header_size As UInt32 = 80
        Dim face_count_size As UInt32 = 4
        Dim ent_size As UInt32 = 12 * 4 + 2 '1 normal, 3 vertex, 1 uint16
        Dim complete_map_data((total_stl_face_count * ent_size) + header_size + face_count_size) As Byte
        Dim h_bw As New BinaryWriter(file_h)
        'move to first face location
        For ii As Byte = 0 To 79
            h_bw.Write(CByte(0))
        Next
        h_bw.Write(total_stl_face_count)

        BG_MAX_VALUE = num_chunks
        BG_VALUE = 0
        BG_TEXT = "Exporting Map..."


        '16896 faces per chunk with sides
        Dim face_type(16896 * num_chunks) As Boolean
        Dim type_counter As Integer = 0
        For i = 0 To num_chunks - 1

            '=============================================================================
            'Doing it the direct way in a binary stl
            If IO.File.Exists(path + MAP_NAME_NO_PATH + "_C" + i.ToString + ".stl") Then
                IO.File.Delete(path + MAP_NAME_NO_PATH + "_C" + i.ToString + ".stl")

            End If
            Dim file_ = IO.File.OpenWrite(path + MAP_NAME_NO_PATH + "_C" + i.ToString + ".stl")
            Dim bw As New BinaryWriter(file_)
            'write 80 bytes for ingored header
            For ii As Byte = 0 To 79
                bw.Write(CByte(0))
            Next
            '=============================================================================


            Dim chunk_mesh As New Assimp.Mesh(Assimp.PrimitiveType.Triangle)

            Dim baseVertex = indirectData(i).baseVertex

            '******************************************************
            'X has to be flipped including the matrix X traslation!
            '******************************************************

            For j = 0 To num_verts_in_chunk - 1
                Dim v = verticesData(baseVertex + j)
                chunk_mesh.Vertices.Add(New Assimp.Vector3D(-v.xyz.X, v.xyz.Y, v.xyz.Z))
                Dim f = New Assimp.Vector3D(-v.xyz.X, v.xyz.Y, v.xyz.Z)
                chunk_mesh.TextureCoordinateChannels(0).Add(New Assimp.Vector3D(-v.uv.X, v.uv.Y, 0.0F))
                ' TODO: export normals
            Next
            'create bottom mesh
            For j = 0 To num_verts_in_chunk - 1
                Dim v = verticesData(baseVertex + j)
                chunk_mesh.Vertices.Add(New Assimp.Vector3D(-v.xyz.X, MIN_MAP_HEIGHT - 2.0F, v.xyz.Z))
                chunk_mesh.TextureCoordinateChannels(0).Add(New Assimp.Vector3D(-v.uv.X, v.uv.Y, 0.0F))
                ' TODO: export normals
            Next
            'need to add each wall

            For j = 0 To num_indices - 1 Step 3
                a = indicesData(j)
                b = indicesData(j + 1)
                c = indicesData(j + 2)
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = False
                type_counter += 1
            Next
            'add faces for bottom mesh
            For j = 0 To num_indices - 1 Step 3
                b = indicesData(j) + (num_verts_in_chunk)
                a = indicesData(j + 1) + (num_verts_in_chunk)
                c = indicesData(j + 2) + (num_verts_in_chunk)
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = False
                type_counter += 1
            Next


            'add faces north wall
            For j = 0 To (64 * 6) - 1 Step 6
                't1
                b = indicesData(j + 1)
                a = indicesData(j + 2)
                c = indicesData(j + 1) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
                't2
                b = indicesData(j + 1) + 4225
                a = indicesData(j + 2)
                c = indicesData(j + 2) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
            Next

            Dim stride As Integer = (64 * 6)

            'add faces south wall
            For j = 0 To (64 * 6) - 1 Step 6
                't1
                k = (62 * 65 * 6) + 12 'p * stride
                b = indicesData(k + j + 0) + 4225
                a = indicesData(k + j + 4)
                c = indicesData(k + j + 4) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
                't2
                a = indicesData(k + j + 0) + 4225
                b = indicesData(k + j + 4)
                c = indicesData(k + j + 0)
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
            Next

            'add faces east wall
            For j = 0 To (63 * 384) - 1 Step 384
                't1
                b = indicesData(j + 2 + 0) + 4225
                a = indicesData(j + 2 + 0)
                c = indicesData(j + 2 + 384) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
                't2
                b = indicesData(j + 2 + 0)
                a = indicesData(j + 2 + 384)
                c = indicesData(j + 2 + 384) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
            Next
            'hack to add last 2 faces.
            't1
            k = 24192 - 384
            b = indicesData(k + 3 + 0) + 4225
            a = indicesData(k + 0 + 0)
            c = indicesData(k + 0 + 384) + 4225
            chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
            face_type(type_counter) = True
            type_counter += 1
            't2
            b = indicesData(k + 3 + 0)
            a = indicesData(k + 0 + 384)
            c = indicesData(k + 0 + 384) + 4225
            chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
            face_type(type_counter) = True
            type_counter += 1


            'add faces west wall
            For j = 378 To (63 * 384) - 1 Step 384
                't1
                a = indicesData(j + 5 + 0) + 4225
                b = indicesData(j + 5 + 0)
                c = indicesData(j + 5 + 384) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
                't2
                a = indicesData(j + 5 + 0)
                b = indicesData(j + 5 + 384)
                c = indicesData(j + 5 + 384) + 4225
                chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
                face_type(type_counter) = True
                type_counter += 1
            Next
            'hack to add last 2 faces.
            't1
            k = 24576 - 6
            b = indicesData(k + 4 + 0) + 4225
            a = indicesData(k + 4 + 0)
            c = indicesData(k + 1) + 4225
            chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
            face_type(type_counter) = True
            type_counter += 1
            't2
            a = indicesData(k + 4)
            b = indicesData(k + 1) + 4225
            c = indicesData(k + 1)
            chunk_mesh.Faces.Add(New Assimp.Face({a, b, c}))
            face_type(type_counter) = True
            type_counter += 1

            Dim m = matricesData(i).modelMatrix
            Dim mat = New Assimp.Matrix4x4(
                m.M11, m.M21, m.M31, -m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44)


            Dim faces = chunk_mesh.Faces
            Dim verts = chunk_mesh.Vertices
            Dim normals As New List(Of Assimp.Vector3D)

            '=====================================================================
            bw.Write(CUInt(faces.Count)) 'top  bottom walls

            For j = 0 To faces.Count - 1
                'reverse winding
                Dim a1 = faces(j).Indices(0)
                Dim a2 = faces(j).Indices(1)
                Dim a3 = faces(j).Indices(2)

                'transform vertices
                Dim v1 = mat * verts(a1)
                Dim v2 = mat * verts(a2)
                Dim v3 = mat * verts(a3)

                ' The vertex writes below swap Y and Z, which mirrors the coordinate
                ' system, so a normal computed from v1/v2/v3 would not match the
                ' winding actually stored in the file. Build the written positions
                ' first and take the normal from those.
                Dim p1 = New Assimp.Vector3D(v1.X, v1.Z, v1.Y)
                Dim p2 = New Assimp.Vector3D(v2.X, v2.Z, v2.Y)
                Dim p3 = New Assimp.Vector3D(v3.X, v3.Z, v3.Y)

                ' cross of two EDGES, not of two positions
                Dim no = make_surface_normal(p2 - p1, p3 - p1)
                'normals.Add(no)
                'normals.Add(no)
                'normals.Add(no)
                '------------- each chunk
                'write normal
                bw.Write(no.X)
                bw.Write(no.Y)
                bw.Write(no.Z)
                'write vertex 1
                bw.Write(v1.X)
                bw.Write(v1.Z)
                bw.Write(v1.Y)
                'write vertex 1
                bw.Write(v2.X)
                bw.Write(v2.Z)
                bw.Write(v2.Y)
                'write vertex 1
                bw.Write(v3.X)
                bw.Write(v3.Z)
                bw.Write(v3.Y)
                'write atribute. 0 is fine but can be a color for each face
                bw.Write(CUShort(0))
                '------------- entire map
                'only if map boarder
                Dim test = check_map_border(v1)
                test = test Or check_map_border(v2)
                test = test Or check_map_border(v3)
                'if this is a top or bottom mesh face, we must add it!
                If face_type(j) And test Then
                    total_stl_face_count += 1
                    'write normal
                    h_bw.Write(no.X)
                    h_bw.Write(no.Y)
                    h_bw.Write(no.Z)
                    'write vertex 1
                    h_bw.Write(v1.X)
                    h_bw.Write(v1.Z)
                    h_bw.Write(v1.Y)
                    'write vertex 1
                    h_bw.Write(v2.X)
                    h_bw.Write(v2.Z)
                    h_bw.Write(v2.Y)
                    'write vertex 1
                    h_bw.Write(v3.X)
                    h_bw.Write(v3.Z)
                    h_bw.Write(v3.Y)
                    'write atribute. 0 is fine but can be a color for each face
                    h_bw.Write(CUShort(0))
                End If
                'we must write top and bottom faces
                If Not face_type(j) Then
                    'write normal
                    h_bw.Write(no.X)
                    h_bw.Write(no.Y)
                    h_bw.Write(no.Z)
                    'write vertex 1
                    h_bw.Write(v1.X)
                    h_bw.Write(v1.Z)
                    h_bw.Write(v1.Y)
                    'write vertex 1
                    h_bw.Write(v2.X)
                    h_bw.Write(v2.Z)
                    h_bw.Write(v2.Y)
                    'write vertex 1
                    h_bw.Write(v3.X)
                    h_bw.Write(v3.Z)
                    h_bw.Write(v3.Y)
                    'write atribute. 0 is fine but can be a color for each face
                    h_bw.Write(CUShort(0))
                End If
            Next
            'this stl is complete so close the file
            file_.Close()
            '=====================================================================




            'chunk_mesh.MaterialIndex = 0
            'chunk_mesh.Normals.AddRange(normals)
            'scene.Meshes.Add(chunk_mesh)

            'Dim node = New Assimp.Node(String.Format("chunk_{0}", i), scene.RootNode)
            'scene.RootNode.Children.Add(node)
            'node.MeshIndices.Add(i)

            ''matrix - un-needed for now
            'm = matricesData(i).modelMatrix
            'node.Transform = New Assimp.Matrix4x4(
            '    m.M11, m.M21, m.M31, m.M41,
            '    m.M12, m.M22, m.M32, m.M42,
            '    m.M13, m.M23, m.M33, m.M43,
            '    m.M14, m.M24, m.M34, m.M44)

            ''unity matrix for now
            ''node.Transform = New Assimp.Matrix4x4(
            ''    1.0, 0.0, 0.0, 0.0,
            ''    0.0, 1.0, 0.0, 0.0,
            ''    0.0, 0.0, 1.0, 0.0,
            ''    0.0, 0.0, 0.0, 0.0)

            'Dim dummy_material As New Assimp.Material
            'dummy_material.Name = "dummy_material"
            'dummy_material.ColorDiffuse = New Assimp.Color4D(1.0, 0.5, 0.5, 1.0)
            'dummy_material.Reflectivity = 0.5
            'dummy_material.ColorAmbient = New Assimp.Color4D(0.15, 0.15, 0.15, 1.0)
            'scene.Materials.Add(dummy_material)

            BG_VALUE = i
            main_window.ForceRender()
        Next
        ' Static models go into the combined map only, never the per-chunk files.
        If Me.scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            Me.scene.static_models.AppendToStl(h_bw, total_stl_face_count)
        End If

        h_bw.BaseStream.Position = 80
        h_bw.Write(total_stl_face_count) ' write total faces
        file_h.Close()

        'Dim exporter As New Assimp.AssimpContext
        'Debug.Assert(exporter.ExportFile(scene, path + MAP_NAME_NO_PATH + ".obj", "obj"))
    End Sub
End Class

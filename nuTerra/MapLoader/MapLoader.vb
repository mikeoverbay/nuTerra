Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports OpenTK.Mathematics

Module MapLoader
    Public HEIGHTMAPSIZE As Integer = 64

    '-----------------------------------
    'This stores all models used on a map
    Public MAP_MODELS() As mdl_

    Public Structure mdl_
        Public modelLods() As base_model_holder_
        Public visibilityBounds As Matrix2x3
    End Structure


    '============================================================================
    Public Sub load_map(map_name As String)
        If MAP_LOADED AndAlso MAP_NAME_NO_PATH = map_name Then
            SHOW_MAPS_SCREEN = False
            Return
        End If

        MAP_LOADED = False
        SHOW_MAPS_SCREEN = False
        BG_MAX_VALUE = 0

        SHOW_LOADING_SCREEN = True

        'First we need to remove the loaded data.
        map_scene?.Dispose()
        TextureMgr.ClearCache()

        MAP_NAME_NO_PATH = map_name
        map_scene = New MapScene(map_name)

        ' Clear the previous map's tuning. These settings live in module state
        ' that outlives a map, and modMapSettings.Load only applies the keys a
        ' file actually has - so without this an absent key inherited the last
        ' map's value instead of falling back to the default. Map data from
        ' get_environment_info and space.bin is applied after this and still
        ' wins; the saved file is applied last and overrides both.
        modMapSettings.ResetToDefaults()

        '===============================================================
        'Open the space.bin file. If it fails, it closes all packages and lets the user know.
        If Not get_spaceBin(map_name) Then
            MsgBox("Failed to load Space.Bin from the map package.", MsgBoxStyle.Exclamation, "Space.bin!")
            Return
        End If

        get_environment_info(map_name)
        map_scene.sky.SUN_TEXTURE_ID = TextureMgr.load_dds_image_from_file("sol.dds")
        map_scene.CC_LUT_ID = TextureMgr.openDDS(theMap.lut_path)
        map_scene.ENV_BRDF_LUT_ID = TextureMgr.openDDS("system/maps/env_brdf_lut.dds")


        '===============================================================

#Region "load models"

        If DONT_BLOCK_MODELS Then

            ' Setup Bar graph
            BG_TEXT = "Loading Models..."
            BG_MAX_VALUE = MAP_MODELS.Length - 1
            BG_VALUE = 0

            For i = 0 To MAP_MODELS.Length - 1
                BG_VALUE = i
                For Each model In MAP_MODELS(i).modelLods
                    If Not model.junk Then
                        Dim good = get_primitive(model)
                        If Not good Then
                            Application.Exit()
                            Return
                        End If
                    End If
                Next
                If i Mod 10 = 0 Then
                    main_window.ForceRender()
                End If
            Next

            '----------------------------------------------------------------
            ' calc instances
            map_scene.static_models.numModelInstances = 0
            map_scene.static_models.indirectDrawCount = 0
            map_scene.static_models.indirectShadowMappingDrawCount = 0
            Dim numVerts = 0
            Dim numPrims = 0
            Dim numLods = 0
            For Each batch In MODEL_BATCH_LIST
                Dim MAX_LOD_ID = MAP_MODELS(batch.model_id).modelLods.Length - 1
                Dim SHADOW_MAP_LOD = Math.Min(1, MAX_LOD_ID)
                For lod_id = 0 To MAX_LOD_ID
                    Dim lod = MAP_MODELS(batch.model_id).modelLods(lod_id)

                    If lod.junk Then
                        Continue For
                    End If

                    Dim skip = True
                    For Each renderSet In lod.render_sets
                        If renderSet.no_draw Then
                            Continue For
                        End If
                        For Each primGroup In renderSet.primitiveGroups.Values
                            If primGroup.no_draw Then
                                Continue For
                            End If
                            map_scene.static_models.indirectDrawCount += batch.count
                            If lod_id = SHADOW_MAP_LOD Then map_scene.static_models.indirectShadowMappingDrawCount += 1
                            skip = False
                        Next
                        numVerts += renderSet.buffers.vertexBuffer.Length
                        numPrims += renderSet.buffers.index_buffer32.Length
                    Next

                    If skip Then Continue For

                    numLods += batch.count
                    If lod_id = 0 Then map_scene.static_models.numModelInstances += batch.count
                Next
            Next

            '----------------------------------------------------------------
            ' setup instances
            Dim drawCommands(map_scene.static_models.indirectDrawCount - 1) As CandidateDraw
            Dim shadowMappingDrawCommands(map_scene.static_models.indirectShadowMappingDrawCount - 1) As DrawElementsIndirectCommand

            Dim vertex_size = Marshal.SizeOf(Of ModelVertex)()
            Dim tri_size = Marshal.SizeOf(Of vect3_32)()
            Dim uv2_size = Marshal.SizeOf(Of Vector2)()

            map_scene.static_models.verts = GLBuffer.Create(BufferTarget.ArrayBuffer, "verts")
            map_scene.static_models.verts.StorageNullData(
                                  numVerts * vertex_size,
                                  BufferStorageFlags.DynamicStorageBit)

            map_scene.static_models.prims = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "prims")
            map_scene.static_models.prims.StorageNullData(
                                  numPrims * tri_size,
                                  BufferStorageFlags.DynamicStorageBit)

            map_scene.static_models.vertsUV2 = GLBuffer.Create(BufferTarget.ArrayBuffer, "vertsUV2")
            map_scene.static_models.vertsUV2.StorageNullData(
                                  numVerts * uv2_size,
                                  BufferStorageFlags.DynamicStorageBit)

            ' Vertex colours, RGBA8. Only meshes with a "colour" stream author
            ' this; everything else must read WHITE, not zero - the volumetric
            ' alpha math is (texA + vertexA * fade - 1) * gain, so a zero
            ' default makes every colour-less volumetric mesh invisible.
            map_scene.static_models.vertsColour = GLBuffer.Create(BufferTarget.ArrayBuffer, "vertsColour")
            map_scene.static_models.vertsColour.StorageNullData(
                                  numVerts * 4,
                                  BufferStorageFlags.DynamicStorageBit)
            Dim white_default(numVerts - 1) As UInt32
            For wi = 0 To numVerts - 1
                white_default(wi) = &HFFFFFFFFUI
            Next
            GL.NamedBufferSubData(map_scene.static_models.vertsColour.buffer_id, IntPtr.Zero, numVerts * 4, white_default)
            Erase white_default

            Dim matrices(map_scene.static_models.numModelInstances - 1) As ModelInstance
            Dim lods(numLods - 1) As ModelLoD
            Dim cmdId = 0
            Dim shadow_cmdId = 0
            Dim vLast = 0
            Dim iLast = 0
            Dim mLast = 0
            Dim lodLast = 0
            Dim baseVert = 0
            For Each batch In MODEL_BATCH_LIST
                Dim skip = True
                Dim savedLodOffset = lodLast
                ' Audit trail: one entry per lods() row group actually appended
                ' for this batch, holding that lod's emitted prim-group count.
                Dim lodRows As New List(Of Integer)

                Dim MAX_LOD_ID = MAP_MODELS(batch.model_id).modelLods.Length - 1
                Dim SHADOW_MAP_LOD = Math.Min(1, MAX_LOD_ID)
                For lod_id = 0 To MAX_LOD_ID
                    Dim lod = MAP_MODELS(batch.model_id).modelLods(lod_id)

                    If lod.junk Then
                        Continue For
                    End If

                    Dim savedCmdId = cmdId

                    For Each renderSet In lod.render_sets
                        If renderSet.no_draw Then
                            Continue For
                        End If
                        For Each primGroup In renderSet.primitiveGroups.Values
                            If primGroup.no_draw Then
                                Continue For
                            End If
                            With drawCommands(cmdId)
                                .model_id = mLast
                                .material_id = primGroup.material_id
                                .count = primGroup.nPrimitives * 3
                                .firstIndex = iLast * 3 + primGroup.startIndex
                                .baseVertex = baseVert
                                .baseInstance = cmdId
                                .lod_level = lod_id
                            End With
                            If lod_id = SHADOW_MAP_LOD Then
                                With shadowMappingDrawCommands(shadow_cmdId)
                                    .baseVertex = drawCommands(cmdId).baseVertex
                                    .firstIndex = drawCommands(cmdId).firstIndex
                                    .instanceCount = batch.count
                                    .count = drawCommands(cmdId).count
                                    .baseInstance = cmdId
                                End With
                                shadow_cmdId += 1
                            End If
                            cmdId += 1
                            skip = False
                        Next

                        baseVert += renderSet.numVertices

                        GL.NamedBufferSubData(map_scene.static_models.verts.buffer_id, New IntPtr(vLast * vertex_size), renderSet.buffers.vertexBuffer.Length * vertex_size, renderSet.buffers.vertexBuffer)
                        GL.NamedBufferSubData(map_scene.static_models.prims.buffer_id, New IntPtr(iLast * tri_size), renderSet.buffers.index_buffer32.Length * tri_size, renderSet.buffers.index_buffer32)

                        If renderSet.buffers.uv2 IsNot Nothing Then
                            GL.NamedBufferSubData(map_scene.static_models.vertsUV2.buffer_id, New IntPtr(vLast * uv2_size), renderSet.buffers.uv2.Length * uv2_size, renderSet.buffers.uv2)
                            Erase renderSet.buffers.uv2
                        End If

                        If renderSet.buffers.colour IsNot Nothing Then
                            GL.NamedBufferSubData(map_scene.static_models.vertsColour.buffer_id, New IntPtr(vLast * 4), renderSet.buffers.colour.Length * 4, renderSet.buffers.colour)
                            Erase renderSet.buffers.colour
                        End If

                        vLast += renderSet.buffers.vertexBuffer.Length
                        iLast += renderSet.buffers.index_buffer32.Length

                        Erase renderSet.buffers.vertexBuffer
                        Erase renderSet.buffers.index_buffer32
                    Next

                    If Not skip Then
                        Dim countPrimGroups = cmdId - savedCmdId
                        For i = 1 To batch.count - 1
                            For j = 0 To countPrimGroups - 1
                                With drawCommands(cmdId)
                                    .model_id = mLast + i
                                    .material_id = drawCommands(savedCmdId + j).material_id
                                    .count = drawCommands(savedCmdId + j).count
                                    .firstIndex = drawCommands(savedCmdId + j).firstIndex
                                    .baseVertex = drawCommands(savedCmdId + j).baseVertex
                                    .baseInstance = cmdId
                                    .lod_level = lod_id
                                End With
                                cmdId += 1
                            Next
                        Next
                        For i = 0 To batch.count - 1
                            With lods(lodLast)
                                .draw_offset = savedCmdId + i * countPrimGroups
                                .draw_count = countPrimGroups
                            End With
                            lodLast += 1
                        Next
                        lodRows.Add(countPrimGroups)
                    End If
                Next

                If skip Then Continue For

                ' LOD-table audit. cull.comp picks a lods() row by camera
                ' distance (bands 50/100/150 m) and clamps to lod_count-1.
                ' A row with draw_count 0 makes every instance VANISH in that
                ' band; fewer rows than authored lods sends the far bands past
                ' this batch's rows into a neighbour's. Outland models are
                ' always viewed from beyond 150 m, so they live in the last
                ' band permanently.
                If lodRows.Count < MAX_LOD_ID + 1 OrElse lodRows.Contains(0) Then
                    LogThis("  lod audit: {0}  authored {1} rows {2} counts [{3}] x{4}",
                            Path.GetDirectoryName(MAP_MODELS(batch.model_id).modelLods(0).render_sets(0).verts_name),
                            MAX_LOD_ID + 1, lodRows.Count,
                            String.Join(",", lodRows), batch.count)
                End If

                ' Hoisted: it was recomputed per instance for PICK_DICTIONARY,
                ' and the bounding-box filter needs it too.
                Dim model_dir = Path.GetDirectoryName(MAP_MODELS(batch.model_id).modelLods(0).render_sets(0).verts_name)
                Dim is_volumetric As UInt32 = If(VOLUMETRIC_MODEL_DIRS.Contains(model_dir), 1UI, 0UI)

                For i = 0 To batch.count - 1
                    With matrices(mLast + i)
                        .matrix = MODEL_INDEX_LIST(batch.offset + i).matrix
                        .bmin.X = -MAP_MODELS(batch.model_id).visibilityBounds.Row1.X 'make negative because of GL rendering!
                        .bmin.Yz = MAP_MODELS(batch.model_id).visibilityBounds.Row0.Yz
                        .bmax.X = -MAP_MODELS(batch.model_id).visibilityBounds.Row0.X 'make negative because of GL rendering!
                        .bmax.Yz = MAP_MODELS(batch.model_id).visibilityBounds.Row1.Yz
                        .lod_offset = savedLodOffset + i
                        .lod_count = MAP_MODELS(batch.model_id).modelLods.Length
                        .batch_count = batch.count
                        ' Marks a GFX/volumetric instance so the bounding-box
                        ' overlay can show only those.
                        .reserverd1 = is_volumetric
                    End With
                    map_scene.PICK_DICTIONARY(mLast + i) = model_dir
                Next
                mLast += batch.count
            Next

            map_scene.static_models.parameters_temp = GLBuffer.Create(BufferTarget.CopyWriteBuffer, "parameters_temp")
            map_scene.static_models.parameters_temp.StorageNullData(
                map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer),
                BufferStorageFlags.ClientStorageBit)

            map_scene.static_models.parameters = GLBuffer.Create(BufferTarget.AtomicCounterBuffer, "parameters")
            map_scene.static_models.parameters.StorageNullData(
                map_scene.static_models.numAfterFrustum.Length * Marshal.SizeOf(Of Integer),
                BufferStorageFlags.None)
            map_scene.static_models.parameters.BindBase(0)

            map_scene.static_models.visibles = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "visibles")
            map_scene.static_models.visibles.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of Integer),
                BufferStorageFlags.DynamicStorageBit)
            map_scene.static_models.visibles.BindBase(8)

            map_scene.static_models.visibles_dbl_sided = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "visibles_dbl_sided")
            map_scene.static_models.visibles_dbl_sided.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of Integer),
                BufferStorageFlags.DynamicStorageBit)
            map_scene.static_models.visibles_dbl_sided.BindBase(9)

            ' CPU copy of every candidate draw's instance origin - draw_fx
            ' depth-sorts its bucket with these each frame (the cull shader's
            ' atomic emission order is nondeterministic, which flickered
            ' overlapping smoke). Indexed by candidate id (= baseInstance).
            ReDim map_scene.static_models.candidate_origins(map_scene.static_models.indirectDrawCount - 1)
            ReDim map_scene.static_models.candidate_model_ids(map_scene.static_models.indirectDrawCount - 1)
            ReDim map_scene.static_models.candidate_material_ids(map_scene.static_models.indirectDrawCount - 1)
            For i = 0 To map_scene.static_models.indirectDrawCount - 1
                map_scene.static_models.candidate_origins(i) = matrices(drawCommands(i).model_id).matrix.ExtractTranslation()
                map_scene.static_models.candidate_model_ids(i) = drawCommands(i).model_id
                ' Kept for the FX composite class, which load_materials derives
                ' later - drawCommands is erased a few lines below.
                map_scene.static_models.candidate_material_ids(i) = drawCommands(i).material_id
            Next

            map_scene.static_models.drawCandidates = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "drawCandidates")
            map_scene.static_models.drawCandidates.Storage(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of CandidateDraw),
                drawCommands,
                BufferStorageFlags.None)
            map_scene.static_models.drawCandidates.BindBase(1)
            Erase drawCommands

            map_scene.static_models.indirect = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "indirect")
            map_scene.static_models.indirect.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                BufferStorageFlags.None)
            map_scene.static_models.indirect.BindBase(2)

            map_scene.static_models.indirect_glass = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "indirect_glass")
            map_scene.static_models.indirect_glass.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                BufferStorageFlags.None)
            map_scene.static_models.indirect_glass.BindBase(5)

            map_scene.static_models.indirect_dbl_sided = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "indirect_dbl_sided")
            map_scene.static_models.indirect_dbl_sided.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                BufferStorageFlags.None)
            map_scene.static_models.indirect_dbl_sided.BindBase(6)

            ' Volumetric FX bucket - the cull shader routes shader_type 11 here.
            ' DynamicStorage because draw_fx writes the bucket back CPU-side
            ' after depth-sorting it.
            map_scene.static_models.indirect_fx = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "indirect_fx")
            map_scene.static_models.indirect_fx.StorageNullData(
                map_scene.static_models.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                BufferStorageFlags.DynamicStorageBit)
            map_scene.static_models.indirect_fx.BindBase(7)

            ' Host staging for the FX depth sort's readback (parameters_temp
            ' pattern) - keeps indirect_fx itself resident in VRAM.
            map_scene.static_models.indirect_fx_staging = GLBuffer.Create(BufferTarget.CopyWriteBuffer, "indirect_fx_staging")
            map_scene.static_models.indirect_fx_staging.StorageNullData(
                MapStaticModels.FX_SORT_MAX * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                BufferStorageFlags.ClientStorageBit)

            map_scene.static_models.indirect_shadow_mapping = GLBuffer.Create(BufferTarget.DrawIndirectBuffer, "indirect_shadow_mapping")
            map_scene.static_models.indirect_shadow_mapping.Storage(
                shadowMappingDrawCommands.Length * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                shadowMappingDrawCommands,
                BufferStorageFlags.None)
            Erase shadowMappingDrawCommands

            map_scene.static_models.matrices = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "matrices")
            map_scene.static_models.matrices.Storage(
                matrices.Length * Marshal.SizeOf(Of ModelInstance),
                matrices,
                BufferStorageFlags.None)
            map_scene.static_models.matrices.BindBase(0)
            Erase matrices

            map_scene.static_models.lods = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "lods")
            map_scene.static_models.lods.Storage(
                lods.Length * Marshal.SizeOf(Of ModelLoD),
                lods,
                BufferStorageFlags.None)
            map_scene.static_models.lods.BindBase(4)
            Erase lods

            map_scene.static_models.allMapModels = GLVertexArray.Create("allMapModels")

            'pos
            map_scene.static_models.allMapModels.VertexBuffer(0, map_scene.static_models.verts, New IntPtr(0), Marshal.SizeOf(Of ModelVertex))
            map_scene.static_models.allMapModels.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(0, 0)
            map_scene.static_models.allMapModels.EnableAttrib(0)

            'normal
            map_scene.static_models.allMapModels.VertexBuffer(1, map_scene.static_models.verts, New IntPtr(12), Marshal.SizeOf(Of ModelVertex))
            map_scene.static_models.allMapModels.AttribFormat(1, 4, VertexAttribType.HalfFloat, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(1, 1)
            map_scene.static_models.allMapModels.EnableAttrib(1)

            'tangent
            map_scene.static_models.allMapModels.VertexBuffer(2, map_scene.static_models.verts, New IntPtr(20), Marshal.SizeOf(Of ModelVertex))
            map_scene.static_models.allMapModels.AttribFormat(2, 4, VertexAttribType.HalfFloat, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(2, 2)
            map_scene.static_models.allMapModels.EnableAttrib(2)

            'binormal
            map_scene.static_models.allMapModels.VertexBuffer(3, map_scene.static_models.verts, New IntPtr(28), Marshal.SizeOf(Of ModelVertex))
            map_scene.static_models.allMapModels.AttribFormat(3, 4, VertexAttribType.HalfFloat, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(3, 3)
            map_scene.static_models.allMapModels.EnableAttrib(3)

            'uv
            map_scene.static_models.allMapModels.VertexBuffer(4, map_scene.static_models.verts, New IntPtr(36), Marshal.SizeOf(Of ModelVertex))
            map_scene.static_models.allMapModels.AttribFormat(4, 2, VertexAttribType.Float, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(4, 4)
            map_scene.static_models.allMapModels.EnableAttrib(4)

            'uv2
            map_scene.static_models.allMapModels.VertexBuffer(5, map_scene.static_models.vertsUV2, IntPtr.Zero, Marshal.SizeOf(Of Vector2))
            map_scene.static_models.allMapModels.AttribFormat(5, 2, VertexAttribType.Float, False, 0)
            map_scene.static_models.allMapModels.AttribBinding(5, 5)
            map_scene.static_models.allMapModels.EnableAttrib(5)

            'vertex colour (RGBA8, normalized) - volumetric FX meshes only
            map_scene.static_models.allMapModels.VertexBuffer(6, map_scene.static_models.vertsColour, IntPtr.Zero, 4)
            map_scene.static_models.allMapModels.AttribFormat(6, 4, VertexAttribType.UnsignedByte, True, 0)
            map_scene.static_models.allMapModels.AttribBinding(6, 6)
            map_scene.static_models.allMapModels.EnableAttrib(6)

            map_scene.static_models.allMapModels.ElementBuffer(map_scene.static_models.prims)

            load_materials()

            Erase MAP_MODELS

            map_scene.MODELS_LOADED = True
        End If ' block DONT_BLOCK_MODELS laoded
#End Region
        '===============================================================


        '===============================================================
        'As it says.. create the terrain
        If DONT_BLOCK_TERRAIN Then
            Create_Terrain()
            PLAYER_FIELD_CELL_SIZE = Math.Abs(MAP_BB_BL.X - MAP_BB_UR.X) / 10.0F

            map_scene.TERRAIN_LOADED = True
        End If 'DONT_BLOCK_TERRAIN
        If DONT_BLOCK_OUTLAND Then
            create_outland()
            map_scene.OUTLAND_LOADED = True
        End If

        If DONT_BLOCK_DECALS Then
            build_decals()
        End If

        If DONT_BLOCK_TREES Then
            map_scene.trees.Build()
        End If

        map_scene.roads.Build(map_name)

        ' Water surface mesh out of BWWa. Needs nothing but the parsed section
        ' and a GL context, so anywhere after ReadSpaceBinData works.
        map_scene.water.Build()

        '===============================================================
        'load cube map for PBS_ext lighting,
        'It must happend after terrain load to get the path.
        map_scene.sky.load_cube_and_cube_map()
        '===============================================================
        'test load of maga textures
        '===============================================================
        '===============================================================

        map_scene.terrain.RebuildVTAtlas()


        '==========================================================
        'remove data now that its unneeded now.
        cBWT2 = Nothing
        cBWST = Nothing
        cWGSD = Nothing

        ' Particles need ResMgr and the GL context, so they load last.
        If map_scene IsNot Nothing Then map_scene.particles.Load()

        MAP_LOADED = True

        ' A camera handed in on the command line, applied once the map is up
        ' (loading resets the view, so it cannot be set any earlier). Cleared
        ' after use so loading a second map by hand does not snap back to it.
        If STARTUP_CAM IsNot Nothing AndAlso map_scene IsNot Nothing Then
            With map_scene.camera
                .VIEW_RADIUS = STARTUP_CAM(0)
                .CAM_X_ANGLE = STARTUP_CAM(1)
                .CAM_Y_ANGLE = STARTUP_CAM(2)
                .LOOK_AT_X = STARTUP_CAM(3)
                .LOOK_AT_Y = STARTUP_CAM(4)
                .LOOK_AT_Z = STARTUP_CAM(5)
            End With
            LogThis("startup camera applied: {0:0.####},{1:0.####},{2:0.####},{3:0.####},{4:0.####},{5:0.####}  freezefx={6}",
                    STARTUP_CAM(0), STARTUP_CAM(1), STARTUP_CAM(2),
                    STARTUP_CAM(3), STARTUP_CAM(4), STARTUP_CAM(5), FREEZE_FX)
            STARTUP_CAM = Nothing
        End If

        ' Data weld: stitch the near cascade's heightmap onto the terrain edge.
        ' Must come after MAP_LOADED (needs the chunk height tables).
        If DONT_BLOCK_OUTLAND AndAlso map_scene.OUTLAND_LOADED Then
            patch_outland_heightmap()
        End If

        ' Per-map render settings, applied last so they win over whatever the
        ' environment.xml defaults and the global settings put in place.
        modMapSettings.Load(map_name)
        ' Baseline for the on-exit save, taken whether or not a file existed.
        modMapSettings.Snapshot(map_name)

        '===================================================
        ' Set sun location from map data. This MUST come before the bake below:
        ' the bake's whole camera is derived from LIGHT_POS, and until this runs
        ' LIGHT_POS is either zero (first map of the session) or the previous
        ' map's sun. Zero normalizes to NaN, which makes the entire view matrix
        ' NaN, which makes every vertex NaN - nothing rasterizes and the depth
        ' map comes back exactly as it was cleared. That reads as "the bake is
        ' broken" and is really just an ordering bug.
        '
        ' set_light_pos is not idempotent - it flips LIGHT_ORBIT_ANGLE_Z off its
        ' own previous value - so it is moved here, never called twice.
        set_light_pos() 'for light rotation animation
        '===================================================

        ' One map-wide depth render from the sun. Fills what the cascades cannot:
        ' terrain never casts into them, and they stop at 500 m.
        '
        ' Off by default - see BAKED_SHADOW_ENABLED. This has to come AFTER the
        ' settings load, because that is what decides the flag; baking before it
        ' would read last map's answer. If it stays off, ready is False, the
        ' t_mixer block is skipped entirely and the taps cost nothing.
        If BAKED_SHADOW_ENABLED Then
            map_scene.sun_shadow.Bake()
        End If

        '===============================================================
        'We need to get the Y location of the rings and stop drawing overly tall cubes.
        'It only needs to happen once!
        If map_scene.BASE_RINGS_LOADED Then
            T1_Y = get_Y_at_XZ(-TEAM_1.X, TEAM_1.Z)
            T2_Y = get_Y_at_XZ(-TEAM_2.X, TEAM_2.Z)
        End If

        CommonProperties.update()

        '===============================================================
        MINI_MAP_SIZE += 1 ' force a redraw of the entire minimap
        '===============================================================

        If EXPORT_STL_MAP Then
            If Not Directory.Exists("C:\wot_maps") Then
                Directory.CreateDirectory("C:\wot_maps")
            End If
            'map_scene.ExportToFile("./map_scene.dae", "collada")
            map_scene.ExportToFile("C:\wot_maps\")
        End If

        map_scene.camera.check_postion_for_update() ' need to initialize cursor altitude
        SHOW_LOADING_SCREEN = False
        'LOOK_AT_X = 0.001
        'LOOK_AT_Z = 0.001
    End Sub

    Private Sub build_decals()
        BG_VALUE = 0
        BG_MAX_VALUE = cWGSD.decalEntries.Length - 1
        BG_TEXT = "Building Decals.."

        map_scene.decals.all_decals = New List(Of DecalGLInfo)
        Dim i As Int16 = 0

        For Each decal In cWGSD.decalEntries
            BG_VALUE = i
            main_window.ForceRender()

            Dim decal_item As New DecalGLInfo

            decal_item.influence = CUInt(decal.influenceType)

            decal_item.priority = decal.priority
            decal_item.load_index = CInt(i)

            decal_item.visibility = decal.visibility_mask >> 16 And &HFFFF

            decal_item.v1 = decal.v1 And &HFFFF

            decal_item.v2 = decal.v2 And &HFFFF

            'If decal_item.v2 > &HFF00 Then
            '    decal_item.v2 = &HFFFF - (decal_item.v2 And &HFFFF) + 32
            'End If

            'Debug.WriteLine("inf: " + decal_item.influence.ToString +
            '                "  mat: " + decal_item.material_type.ToString +
            '                "  vis: " + decal_item.visibility.ToString +
            '                "  v1: " + decal_item.v1.ToString +
            '                "  v2: " + decal_item.v2.ToString +
            '                "  id: " + i.ToString)


            'Debug.WriteLine("materialType: " + decal.materialType.ToString)

            decal_item.offset = decal.offsets.Xz 'XY?
            decal_item.scale = decal.uv_wrapping

            If decal_item.offset.X > 0 Then
                'Stop
            End If
            If decal_item.offset.Y > 0 Then
                Stop
            End If
            decal_item.matrix = decal.transform

            'Flip some row values to convert from DirectX to Opengl
            decal_item.matrix.M12 *= -1.0
            decal_item.matrix.M13 *= -1.0
            decal_item.matrix.M21 *= -1.0
            decal_item.matrix.M31 *= -1.0
            decal_item.matrix.M41 *= -1.0

            Dim md = decal_item.matrix.Determinant
            If md < 0 Then
                decal_item.winding = FrontFaceDirection.Cw
            Else
                decal_item.winding = FrontFaceDirection.Ccw
            End If


            ' materialType 8 is the authored wet type - the pooled-water path.
            ' Surveyed across 21 maps it is exactly the set of W_*_wetness
            ' decals, it never combines with another value, and only 0, 1 and 8
            ' occur at all.
            '
            ' This was INFERRED from a decal having no diffuse texture. The
            ' guess is right almost everywhere, because a wetness decal normally
            ' carries its texture in the add slot - but 23_westfeld authors one
            ' of its 40 with the same W_puddle_01_wetness.dds in the DIFFUSE
            ' slot, and the guess rendered that one as ordinary albedo.
            decal_item.wet = If(decal.materialType = 8, CUInt(1), CUInt(0))

            ' Independent of the wet flag: a decal with no diffuse drives itself
            ' from the add texture instead and writes no colour, only gloss and
            ' a normal.
            Dim diff_fname = cBWST.find_str(decal.diff_tex_fnv)
            Dim normal_fname = cBWST.find_str(decal.bump_tex_fnv)

            Dim colour_fname = diff_fname
            If colour_fname.Length = 0 Then
                colour_fname = cBWST.find_str(decal.add_tex_fnv)
            End If

            If colour_fname.Length > 0 Then
                decal_item.color_tex = TextureMgr.OpenDDS(colour_fname)


                If normal_fname = "" Then
                    decal_item.normal_tex = TextureMgr.load_png_image_from_file("Ref_normalMap.png", True, False)
                Else
                    decal_item.normal_tex = TextureMgr.OpenDDS(normal_fname)
                End If

                map_scene.decals.all_decals.Add(decal_item)
            End If
            i += 1
        Next

        ' Wet decals are the pooled-water path, and how many a map has is the
        ' first thing worth knowing when it does not appear. Read from the
        ' authored materialType now, not guessed.
        '
        ' Six of the 21 maps surveyed carry any: 29_el_hallouf 47, 23_westfeld
        ' 40, 19_monastery 36, 01_karelia 18, 34_redshire 14, 11_murovanka 8.
        ' The other fifteen - 101_dday and 08_ruinberg among them - author NONE.
        ' A zero here is normal and is not evidence of a broken classifier.
        Dim wet_decals = 0
        For Each d_ In map_scene.decals.all_decals
            If d_.wet = CUInt(1) Then wet_decals += 1
        Next
        LogThis("decals: {0} total, {1} wet (materialType 8)",
                map_scene.decals.all_decals.Count, wet_decals)

        ' Authored draw order, finally applied. The WGSD record carries a
        ' priority per decal and draw_decals composites in list order with Blend
        ' on and DepthMask(False) (MapDecals.vb:47-48), so the order of this
        ' list IS the order they stack. Ascending, which is the intent already
        ' encoded in DECAL_INDEX_LIST_.CompareTo: higher priority draws later,
        ' and therefore on top.
        '
        ' Until now the priority was read, copied into DECAL_INDEX_LIST, sorted
        ' there - and thrown away, because DECAL_INDEX_LIST is never read by
        ' anything. This list, built from cWGSD.decalEntries in raw file order,
        ' is what actually reaches the GPU.
        map_scene.decals.all_decals.Sort(
            Function(a, b)
                Dim c = a.priority.CompareTo(b.priority)
                If c <> 0 Then Return c
                Return a.load_index.CompareTo(b.load_index)
            End Function)



        map_scene.DECALS_LOADED = True
    End Sub

    Public Sub set_light_pos()
        LIGHT_RADIUS = MAP_SIZE.Length * 100.0
        LIGHT_ORBIT_ANGLE_Z = 360 - LIGHT_ORBIT_ANGLE_Z
        LIGHT_ORBIT_ANGLE_Z += 180.0F

        ' Set initial light position and get radius and angle.
        LIGHT_POS(0) = Math.Sin(LIGHT_ORBIT_ANGLE_Z * 0.0174533) * LIGHT_RADIUS
        LIGHT_POS(1) = Math.Sin(LIGHT_ORBIT_ANGLE_X * 0.0174533) * LIGHT_RADIUS
        LIGHT_POS(2) = Math.Cos(LIGHT_ORBIT_ANGLE_Z * 0.0174533) * LIGHT_RADIUS

        LIGHT_POS.X = LIGHT_POS(0)
        LIGHT_POS.Y = LIGHT_POS(1)
        LIGHT_POS.Z = LIGHT_POS(2)
        LIGHT_ORBIT_ANGLE = LIGHT_ORBIT_ANGLE_Z
        LIGHT_POS(0) = Math.Sin(LIGHT_ORBIT_ANGLE_Z * 0.0174533) * LIGHT_RADIUS
        LIGHT_POS(1) = Math.Sin(LIGHT_ORBIT_ANGLE_X * 0.0174533) * LIGHT_RADIUS
        LIGHT_POS(2) = Math.Cos(LIGHT_ORBIT_ANGLE_Z * 0.0174533) * LIGHT_RADIUS

    End Sub

    Private Structure AtlasCoords
        Implements IComparable(Of AtlasCoords)
        Dim x0 As Int32
        Dim x1 As Int32
        Dim y0 As Int32
        Dim y1 As Int32
        Dim path As String

        Public Function CompareTo(other As AtlasCoords) As Integer Implements IComparable(Of AtlasCoords).CompareTo
            If y0 > other.y0 Then Return 1
            If y0 = other.y0 AndAlso x0 > other.x0 Then Return 1
            Return -1
        End Function
    End Structure

    Private Sub AddAtlas(atlasPath As String,
                         atlasPaths As Dictionary(Of String, HashSet(Of Integer)),
                         ddsAtlasSizes As Dictionary(Of String, Vector2),
                         indexes() As Integer,
                         size As Vector2)
        If atlasPaths.ContainsKey(atlasPath) Then
            For Each i In indexes
                atlasPaths(atlasPath).Add(i)
                atlasPaths(atlasPath).Add(i)
                atlasPaths(atlasPath).Add(i)
            Next
        Else
            atlasPaths(atlasPath) = New HashSet(Of Integer)(indexes)
            If atlasPath.EndsWith(".dds") Then
                ddsAtlasSizes(atlasPath) = size
            End If
        End If
    End Sub

    'Load materials
    Private Sub load_materials()
        Dim texturePaths As New HashSet(Of String)
        Dim atlasPaths As New Dictionary(Of String, HashSet(Of Integer))
        Dim ddsAtlasSizes As New Dictionary(Of String, Vector2)

        For Each mat In materials.Values
            Select Case mat.shader_type
                Case ShaderTypes.FX_PBS_ext
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.metallicGlossMap)

                Case ShaderTypes.FX_PBS_ext_dual
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.diffuseMap2)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.metallicGlossMap)

                Case ShaderTypes.FX_PBS_ext_detail
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.metallicGlossMap)
                    If mat.props.g_detailMap IsNot Nothing Then
                        texturePaths.Add(mat.props.g_detailMap)
                    End If

                Case ShaderTypes.FX_volumetric
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.distortionMap)

                Case ShaderTypes.FX_PBS_tiled_atlas
                    AddAtlas(mat.props.atlasAlbedoHeight,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    Debug.Assert(mat.props.atlasBlend.EndsWith(".png"))
                    mat.props.atlasBlend = mat.props.atlasBlend.Replace(".png", ".dds") 'hack!!!
                    AddAtlas(mat.props.atlasBlend,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.W},
                             mat.props.g_atlasSizes.Zw)
                    AddAtlas(mat.props.atlasNormalGlossSpec,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    AddAtlas(mat.props.atlasMetallicAO,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    If mat.props.dirtMap IsNot Nothing Then
                        texturePaths.Add(mat.props.dirtMap)
                    End If

                Case ShaderTypes.FX_PBS_tiled_atlas_global
                    AddAtlas(mat.props.atlasAlbedoHeight,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    Debug.Assert(mat.props.atlasBlend.EndsWith(".png"))
                    mat.props.atlasBlend = mat.props.atlasBlend.Replace(".png", ".dds") 'hack!!!
                    AddAtlas(mat.props.atlasBlend,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.W},
                             mat.props.g_atlasSizes.Zw)
                    AddAtlas(mat.props.atlasNormalGlossSpec,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    AddAtlas(mat.props.atlasMetallicAO,
                             atlasPaths,
                             ddsAtlasSizes,
                             {mat.props.g_atlasIndexes.X,
                             mat.props.g_atlasIndexes.Y,
                             mat.props.g_atlasIndexes.Z},
                             mat.props.g_atlasSizes.Xy)
                    If mat.props.dirtMap IsNot Nothing Then
                        texturePaths.Add(mat.props.dirtMap)
                    End If
                    texturePaths.Add(mat.props.globalTex)

                Case ShaderTypes.FX_PBS_tiled
                    texturePaths.Add(mat.props.albedoHeightTile0)
                    texturePaths.Add(mat.props.normalGlossSpecTile0)
                    texturePaths.Add(mat.props.metallicAOTile0)
                    texturePaths.Add(mat.props.albedoHeightTile1)
                    texturePaths.Add(mat.props.normalGlossSpecTile1)
                    texturePaths.Add(mat.props.metallicAOTile1)
                    texturePaths.Add(mat.props.albedoHeightTile2)
                    texturePaths.Add(mat.props.normalGlossSpecTile2)
                    texturePaths.Add(mat.props.metallicAOTile2)
                    texturePaths.Add(mat.props.blendMask)
                    texturePaths.Add(mat.props.colorTex)
                    If mat.props.dirtMap IsNot Nothing Then
                        texturePaths.Add(mat.props.dirtMap)
                    End If

                Case ShaderTypes.FX_PBS_tiled_global
                    ' No normalGlossSpec/metallicAO tiles on purpose - the
                    ' game's techniques never sample them for this fx.
                    texturePaths.Add(mat.props.albedoHeightTile0)
                    texturePaths.Add(mat.props.albedoHeightTile1)
                    texturePaths.Add(mat.props.albedoHeightTile2)
                    texturePaths.Add(mat.props.blendMask)
                    texturePaths.Add(mat.props.colorTex)
                    texturePaths.Add(mat.props.globalTex)
                    If mat.props.dirtMap IsNot Nothing Then
                        texturePaths.Add(mat.props.dirtMap)
                    End If

                Case ShaderTypes.FX_PBS_glass
                    texturePaths.Add(mat.props.dirtAlbedoMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.glassMap)

                Case ShaderTypes.FX_PBS_ext_repaint
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.metallicGlossMap)

                Case ShaderTypes.FX_lightonly_alpha, ShaderTypes.FX_glow
                    texturePaths.Add(mat.props.diffuseMap)

                Case Else
                    'Stop
            End Select
        Next

        'load atlases
        'Set bargraph up
        BG_TEXT = "Loading Model Materials..."
        BG_VALUE = 0
        BG_MAX_VALUE = texturePaths.Count
        main_window.ForceRender()

        Dim textureHandles As New Dictionary(Of String, UInt64)
        Dim atlasIndexRemaper As New Dictionary(Of String, Dictionary(Of Integer, Integer))
        For Each atlasPathAndUsage In atlasPaths
            Dim unique As New HashSet(Of Integer)(atlasPathAndUsage.Value)
            Dim old2new_indexes As New Dictionary(Of Integer, Integer)
            Dim handle As Long
            Dim atlas_tex = GLTexture.Create(TextureTarget.Texture2DArray, atlasPathAndUsage.Key)

            If atlasPathAndUsage.Key.EndsWith(".dds") Then
                Dim atlasSize = ddsAtlasSizes(atlasPathAndUsage.Key)
                Dim dds_entry = ResMgr.LookupHD(atlasPathAndUsage.Key)
                If dds_entry Is Nothing Then
                    Stop
                    Continue For
                End If

                Dim dds_ms As New MemoryStream
                dds_entry.Extract(dds_ms)

                dds_ms.Position = 0
                Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                    Dim dds_header = TextureMgr.get_dds_header(dds_br)
                    dds_ms.Position = 128

                    Dim format_info = dds_header.format_info

                    Dim tmp_tex = GLTexture.Create(TextureTarget.Texture2D, "tmpTex")
                    tmp_tex.Parameter(TextureParameterName.TextureBaseLevel, 0)
                    tmp_tex.Parameter(TextureParameterName.TextureMaxLevel, 0)
                    tmp_tex.Storage2D(1, format_info.texture_format, dds_header.width, dds_header.height)

                    If format_info.compressed Then
                        Dim srcImgSize = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                        Dim srcImgData = dds_br.ReadBytes(srcImgSize)
                        tmp_tex.CompressedSubImage2D(0, 0, 0, dds_header.width, dds_header.height, format_info.texture_format, srcImgSize, srcImgData)
                    Else
                        Stop
                    End If

                    Dim tileWidth = dds_header.width \ CInt(atlasSize.X)
                    Dim tileHeight = dds_header.height \ CInt(atlasSize.Y)

                    Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(tileWidth, tileHeight), 2))
                    If atlasPathAndUsage.Key.EndsWith("_blend.dds") Then
                        numLevels = 1
                    End If

                    atlas_tex = GLTexture.Create(TextureTarget.Texture2DArray, atlasPathAndUsage.Key)
                    atlas_tex.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), 4)
                    atlas_tex.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
                    atlas_tex.Parameter(TextureParameterName.TextureBaseLevel, 0)
                    atlas_tex.Parameter(TextureParameterName.TextureMaxLevel, numLevels - 1)
                    atlas_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                    atlas_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                    atlas_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                    atlas_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                    atlas_tex.Storage3D(numLevels, format_info.texture_format, tileWidth, tileHeight, unique.Count)

                    Dim i = 0
                    For Each old_id In unique
                        old2new_indexes(old_id) = i
                        Dim x = old_id Mod CInt(atlasSize.X)
                        Dim y = old_id \ CInt(atlasSize.X)

                        GL.CopyImageSubData(tmp_tex.texture_id,
                                            ImageTarget.Texture2D,
                                            0,
                                            x * tileWidth,
                                            y * tileHeight,
                                            0,
                                            atlas_tex.texture_id,
                                            ImageTarget.Texture2DArray,
                                            0,
                                            0,
                                            0,
                                            i,
                                            tileWidth,
                                            tileHeight,
                                            1)
                        i += 1
                    Next

                    tmp_tex.Dispose()
                End Using

                atlas_tex.GenerateMipmap()

                handle = GL.Arb.GetTextureHandle(atlas_tex.texture_id)
                GL.Arb.MakeTextureHandleResident(handle)

                textureHandles(atlasPathAndUsage.Key) = handle
                atlasIndexRemaper(atlasPathAndUsage.Key) = old2new_indexes
                Continue For
            End If

            If Not atlasPathAndUsage.Key.EndsWith(".atlas") Then
                Stop
                texturePaths.Add(atlasPathAndUsage.Key)
                Continue For
            End If

            Dim entry = ResMgr.Lookup(atlasPathAndUsage.Key + "_processed")
            If entry Is Nothing Then
                Stop
                Continue For
            End If

            'update bargraph
            BG_VALUE += 1
            If BG_VALUE Mod 100 = 0 Then
                main_window.ForceRender()
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)
            ms.Position = 0

            Dim atlasParts As New List(Of AtlasCoords)

            Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
                Dim version = br.ReadInt32
                Debug.Assert(version = 1)

                Dim atlas_width = br.ReadInt32
                Dim atlas_height = br.ReadInt32

                Dim unused1 = br.ReadUInt32
                Debug.Assert({0, 1}.Contains(unused1)) 'boolean flag, compression?

                ' Older clients embedded the atlas bitmap here as a "BCVT" chunk.
                ' Current ones drop it and start the coordinate table straight
                ' after the header, so only skip a chunk that is actually there.
                Dim entries_start = ms.Position
                If ms.Length - ms.Position >= 4 AndAlso New String(br.ReadChars(4)) = "BCVT" Then
                    Dim unused2 = br.ReadUInt32
                    Debug.Assert(unused2 = 1)

                    Dim dds_chunk_size = br.ReadUInt64
                    ms.Position += dds_chunk_size
                Else
                    ms.Position = entries_start
                End If

                Dim i = 0
                While br.BaseStream.Position < br.BaseStream.Length - 1
                    Dim coords As New AtlasCoords
                    coords.x0 = br.ReadInt32
                    coords.x1 = br.ReadInt32
                    coords.y0 = br.ReadInt32
                    coords.y1 = br.ReadInt32

                    coords.path = ""
                    Dim tmpChar = br.ReadChar
                    While tmpChar <> vbNullChar
                        coords.path += tmpChar
                        tmpChar = br.ReadChar
                    End While

                    coords.path = coords.path.Replace(".png", ".dds")
                    If unique.Contains(i) Then
                        old2new_indexes(i) = atlasParts.Count
                        atlasParts.Add(coords)
                    Else
                        '
                        ' HACK HACK HACK!!!!!
                        '
                        If atlasPathAndUsage.Key = "content/buildings/00_atlases/eu_castleruins_atlas_mao.atlas" Then
                            If i = 5 Then
                                old2new_indexes(9) = atlasParts.Count
                                atlasParts.Add(coords)
                            End If
                        End If
                    End If

                    i += 1
                End While
            End Using

            For i = 0 To atlasParts.Count - 1
                Dim coords = atlasParts(i)

                Dim dds_entry = ResMgr.LookupHD(coords.path)
                If dds_entry Is Nothing Then
                    Stop
                    Continue For
                End If

                Dim dds_ms As New MemoryStream
                dds_entry.Extract(dds_ms)

                dds_ms.Position = 0
                Using dds_br As New BinaryReader(dds_ms, System.Text.Encoding.ASCII)
                    Dim dds_header = TextureMgr.get_dds_header(dds_br)
                    dds_ms.Position = 128

                    Dim format_info = dds_header.format_info

                    If i = 0 Then 'run once
                        'Calculate Max Mip Level based on width or height.. Which ever is larger.
                        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(dds_header.width, dds_header.height), 2))

                        atlas_tex.Storage3D(numLevels, format_info.texture_format, dds_header.width, dds_header.height, atlasParts.Count)

                        atlas_tex.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), 4)
                        atlas_tex.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
                        atlas_tex.Parameter(TextureParameterName.TextureBaseLevel, 0)
                        atlas_tex.Parameter(TextureParameterName.TextureMaxLevel, numLevels - 1)
                        atlas_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                        atlas_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                        atlas_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                        atlas_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                    End If

                    Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                    Dim data = dds_br.ReadBytes(size)

                    atlas_tex.CompressedSubImage3D(0, 0, 0, i, dds_header.width, dds_header.height, 1,
                                                DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                End Using
            Next
            atlas_tex.GenerateMipmap()

            handle = GL.Arb.GetTextureHandle(atlas_tex.texture_id)
            GL.Arb.MakeTextureHandleResident(handle)

            textureHandles(atlasPathAndUsage.Key) = handle
            atlasIndexRemaper(atlasPathAndUsage.Key) = old2new_indexes
        Next

        'load textures
        For Each texturePath In texturePaths
            main_window.ForceRender()
            Dim old_texturePath = texturePath
            If Not texturePath.EndsWith(".dds") Then
                'Stop
                texturePath = texturePath.Replace(".png", ".dds") ' hack
                'Continue For
            End If
            'dont load images that are already created!
            Dim image_id = TextureMgr.image_exists(texturePath)
            If image_id IsNot Nothing Then
                'Debug.WriteLine(texturePath)
                Dim hndl = GL.Arb.GetTextureHandle(image_id.texture_id)
                ' A bindless handle has to be resident before it can be sampled.
                ' Without this every cache hit samples as solid white.
                If Not GL.Arb.IsTextureHandleResident(hndl) Then
                    GL.Arb.MakeTextureHandleResident(hndl)
                End If
                ' key on the original path, same as the load path below
                textureHandles(old_texturePath) = hndl
                Continue For
            End If

            Dim entry = ResMgr.LookupHD(texturePath)
            If entry Is Nothing Then
                Stop
                Continue For
            End If

            'update bargraph
            BG_VALUE += 1

            Dim ms As New MemoryStream
            entry.Extract(ms)

            Dim tex = TextureMgr.load_dds_image_from_stream(ms, texturePath)

            Dim handle = GL.Arb.GetTextureHandle(tex.texture_id)
            GL.Arb.MakeTextureHandleResident(handle)

            textureHandles(old_texturePath) = handle
        Next

        Dim materialsData(materials.Count - 1) As GLMaterial
        For Each mat In materials.Values
            With materialsData(mat.id)
                .shader_type = mat.shader_type
                Select Case mat.shader_type
                    Case ShaderTypes.FX_PBS_ext
                        Dim props As MaterialProps_PBS_ext = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .map2Handle = textureHandles(props.normalMap)
                        .map3Handle = textureHandles(props.metallicGlossMap)
                        .g_useNormalPackDXT1 = If(props.g_useNormalPackDXT1, 1, 0)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_colorTint = props.g_colorTint
                        .g_enableAO = If(props.g_enableAO, 1, 0)
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_PBS_ext_dual
                        Dim props As MaterialProps_PBS_ext_dual = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .map2Handle = textureHandles(props.normalMap)
                        .map3Handle = textureHandles(props.metallicGlossMap)
                        .map4Handle = textureHandles(props.diffuseMap2)
                        .g_useNormalPackDXT1 = If(props.g_useNormalPackDXT1, 1, 0)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_colorTint = props.g_colorTint
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_PBS_ext_detail
                        Dim props As MaterialProps_PBS_ext_detail = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .map2Handle = textureHandles(props.normalMap)
                        .map3Handle = textureHandles(props.metallicGlossMap)
                        If props.g_detailMap IsNot Nothing Then
                            .map4Handle = textureHandles(props.g_detailMap)
                        End If
                        .g_enableAO = If(props.g_enableAO, 1, 0)
                        .g_useNormalPackDXT1 = If(props.g_useNormalPackDXT1, 1, 0)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_colorTint = props.g_colorTint
                        .g_detailInfluences = props.g_detailInfluences
                        .g_detailRejectTiling = props.g_detailRejectTiling
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_volumetric
                        ' Slot mapping - volumetric.frag/vert read these back
                        ' out of the same generic fields, keep in lockstep:
                        '   g_colorTint        = TintlColor
                        '   dirtParams         = diffuseUVSpeedAlphaOffset (w carries alphaFreshnelEnable)
                        '   dirtColor          = distortion_UV_Speed_Amount
                        '   g_tile0Tint        = lightMultipliers
                        '   g_tile1Tint        = selfIllumLight
                        '   g_tile2Tint        = FreshnelColor
                        '   g_tileUVScale      = alphaFadeAmountFresnel
                        '   g_atlasIndexes.xy  = fadeMinDistance / fadeMaxDistance
                        '   alphaTestEnable    = additive compositing (alphaAdditiveEnable OR destBlend=ONE)
                        '   g_enableAO         = enableLighting
                        Dim vprops As MaterialProps_volumetric = mat.props
                        LogThis("volumetric material {0}: diffuse={1} ({2}) distortion={3} ({4}) additive={5} lit={6} fresnelVariant={7}",
                                mat.id,
                                vprops.diffuseMap, If(textureHandles.ContainsKey(vprops.diffuseMap), "ok", "MISSING"),
                                vprops.distortionMap, If(textureHandles.ContainsKey(vprops.distortionMap), "ok", "MISSING"),
                                vprops.alphaAdditiveEnable OrElse vprops.destBlend = 2, vprops.enableLighting,
                                vprops.alphaFreshnelEnable)
                        ' Distance fade window. A backdrop sheet authors a real
                        ' range and is therefore INVISIBLE closer than fadeMin -
                        ' which is the whole answer to "where is the smoke".
                        ' TintlColor lands in g_colorTint and multiplies the
                        ' whole litColor INCLUDING alpha, so a zero .w makes
                        ' litColor.a zero and the remap collapses to
                        ' sat(texA - 1) = 0 - invisible, with no other symptom.
                        LogThis("    TintlColor={0}  selfIllum={1}  vertAlphaPath: fresnel={2}",
                                vprops.TintlColor, vprops.selfIllumLight,
                                vprops.alphaFreshnelEnable)
                        LogThis("    fadeMin={0} fadeMax={1}  lightMul.x gain={2}",
                                vprops.fadeMinDistance, vprops.fadeMaxDistance,
                                vprops.lightMultipliers.X)
                        ' The alpha-shaping knobs, because an authored-but-odd
                        ' set here is what makes a sheet invisible (vista_smoke
                        ' taught that the hard way).
                        LogThis("    fade/trimAmount/fresExp/fresAlpha={0}  alphaOffset={1}  fresnelColor={2}  lightMul={3}",
                                vprops.alphaFadeAmountFresnel,
                                vprops.diffuseUVSpeedAlphaOffset.W,
                                vprops.FreshnelColor,
                                vprops.lightMultipliers)
                        LogThis("    softFactor={0}", vprops.softFactor)
                        LogThis("    alphaTrim(alphaAdditiveEnable)={0}  destBlend={1}  -> variant={2}",
                                vprops.alphaAdditiveEnable, vprops.destBlend,
                                If(vprops.alphaAdditiveEnable, "trim/cutout", "multiply/soft"))
                        If Not textureHandles.ContainsKey(vprops.diffuseMap) OrElse
                           Not textureHandles.ContainsKey(vprops.distortionMap) Then
                            ' Sampling an invalid bindless handle is undefined
                            ' behaviour that can take unrelated draws with it -
                            ' demote to unsupported so the cull never routes it.
                            .shader_type = ShaderTypes.FX_unsupported
                            Exit Select
                        End If
                        .map1Handle = textureHandles(vprops.diffuseMap)
                        .map2Handle = textureHandles(vprops.distortionMap)
                        .g_colorTint = vprops.TintlColor
                        ' dirtParams.w is unused by the transcription's UV
                        ' animation, so it carries the variant selector down
                        ' to volumetric.vert/frag.
                        Dim uvsp = vprops.diffuseUVSpeedAlphaOffset
                        uvsp.W = If(vprops.alphaFreshnelEnable, 1, 0)
                        .dirtParams = uvsp
                        .dirtColor = vprops.distortion_UV_Speed_Amount
                        .g_tile0Tint = vprops.lightMultipliers
                        .g_tile1Tint = vprops.selfIllumLight
                        .g_tile2Tint = vprops.FreshnelColor
                        .g_tileUVScale = vprops.alphaFadeAmountFresnel
                        ' .z carries ALPHA TRIM. The fxo's own annotations name
                        ' the bools in order - enableLighting / alphaFreshnelEnable
                        ' / alphaAdditiveEnable / g_useTime - against the labels
                        ' "Enable Lighting" / "Useb Alpha Freshnel" / "Use Alpha
                        ' Trim" / "Use Time". So alphaAdditiveEnable is the ALPHA
                        ' TRIM switch, and it is what picks the fxo's two pixel
                        ' variants: trim on = sat((texA + vertA - 1) * amount)
                        ' (blob 8, the fire cutout), trim off = sat(texA * vertA)
                        ' (blob 9, soft smoke). Selecting on alphaFreshnelEnable
                        ' instead sent Abbey's smoke down the trim path, where
                        ' vertA <= 0.29 makes texA + vertA - 1 negative almost
                        ' everywhere: measured 24 lit pixels against 571.
                        ' .w = softFactor, the soft-particle fade distance.
                        .g_atlasIndexes = New Vector4(vprops.fadeMinDistance, vprops.fadeMaxDistance,
                                                      If(vprops.alphaAdditiveEnable, 1, 0),
                                                      vprops.softFactor)
                        ' destBlend 2 = D3DBLEND_ONE: the material composites
                        ' additively even without alphaAdditiveEnable. Both
                        ' end up as output (rgb*a, 0) under the premultiplied
                        ' pass blend, which is exactly src*a + dest.
                        .alphaTestEnable = If(vprops.alphaAdditiveEnable OrElse vprops.destBlend = 2, 1, 0)
                        .g_enableAO = If(vprops.enableLighting, 1, 0)
                        .double_sided = If(vprops.doubleSided, 1, 0)

                    Case ShaderTypes.FX_PBS_tiled_atlas
                        Dim props As MaterialProps_PBS_tiled_atlas = mat.props
                        .map1Handle = textureHandles(props.atlasAlbedoHeight)
                        .map2Handle = textureHandles(props.atlasNormalGlossSpec)
                        .map3Handle = textureHandles(props.atlasMetallicAO)
                        .map4Handle = textureHandles(props.atlasBlend)
                        If props.dirtMap IsNot Nothing Then
                            .map5Handle = textureHandles(props.dirtMap)
                        End If

                        '.alphaReference = props.alphaReference / 255.0
                        '.alphaTestEnable = mat.props.alphaTestEnable
                        .g_atlasIndexes.X = atlasIndexRemaper(props.atlasAlbedoHeight)(props.g_atlasIndexes.X)
                        .g_atlasIndexes.Y = atlasIndexRemaper(props.atlasNormalGlossSpec)(props.g_atlasIndexes.Y)
                        .g_atlasIndexes.Z = atlasIndexRemaper(props.atlasMetallicAO)(props.g_atlasIndexes.Z)
                        .g_atlasIndexes.W = atlasIndexRemaper(props.atlasBlend)(props.g_atlasIndexes.W)
                        .dirtColor = props.dirtColor
                        .dirtParams = props.dirtParams
                        .g_tile0Tint = props.g_tile0Tint
                        .g_tile1Tint = props.g_tile2Tint
                        .g_tile2Tint = props.g_tile2Tint
                        .g_tileUVScale = props.g_tileUVScale
                        .double_sided = 0

                    Case ShaderTypes.FX_PBS_tiled_atlas_global
                        Dim props As MaterialProps_PBS_atlas_global = mat.props
                        .map1Handle = textureHandles(props.atlasAlbedoHeight)
                        .map2Handle = textureHandles(props.atlasNormalGlossSpec)
                        .map3Handle = textureHandles(props.atlasMetallicAO)
                        .map4Handle = textureHandles(props.atlasBlend)
                        If props.dirtMap IsNot Nothing Then
                            .map5Handle = textureHandles(props.dirtMap)
                        End If
                        .map6Handle = textureHandles(props.globalTex)

                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_atlasIndexes.X = atlasIndexRemaper(props.atlasAlbedoHeight)(props.g_atlasIndexes.X)
                        .g_atlasIndexes.Y = atlasIndexRemaper(props.atlasNormalGlossSpec)(props.g_atlasIndexes.Y)
                        .g_atlasIndexes.Z = atlasIndexRemaper(props.atlasMetallicAO)(props.g_atlasIndexes.Z)
                        .g_atlasIndexes.W = atlasIndexRemaper(props.atlasBlend)(props.g_atlasIndexes.W)
                        .dirtColor = props.dirtColor
                        .dirtParams = props.dirtParams
                        .g_tile0Tint = props.g_tile0Tint
                        .g_tile1Tint = props.g_tile2Tint
                        .g_tile2Tint = props.g_tile2Tint
                        .g_tileUVScale = props.g_tileUVScale
                        .double_sided = 0

                    Case ShaderTypes.FX_PBS_tiled
                        Dim props As MaterialProps_PBS_tiled = mat.props
                        ' tile 0 / 1 / 2, each albedoHeight + normalGlossSpec + metallicAO
                        .map1Handle = textureHandles(props.albedoHeightTile0)
                        .map2Handle = textureHandles(props.normalGlossSpecTile0)
                        .map3Handle = textureHandles(props.metallicAOTile0)
                        .map4Handle = textureHandles(props.albedoHeightTile1)
                        .map5Handle = textureHandles(props.normalGlossSpecTile1)
                        .map6Handle = textureHandles(props.metallicAOTile1)
                        .map7Handle = textureHandles(props.albedoHeightTile2)
                        .map8Handle = textureHandles(props.normalGlossSpecTile2)
                        .map9Handle = textureHandles(props.metallicAOTile2)
                        .map10Handle = textureHandles(props.blendMask)
                        If props.dirtMap IsNot Nothing Then
                            .map11Handle = textureHandles(props.dirtMap)
                        End If
                        .map12Handle = textureHandles(props.colorTex)
                        .g_tile0Tint = props.g_tile0Tint
                        .g_tile1Tint = props.g_tile1Tint
                        .g_tile2Tint = props.g_tile2Tint
                        .dirtColor = props.g_dirtColor
                        .dirtParams = props.g_dirtColorParams
                        .g_detailInfluences = props.g_fakeShadowsAndDetailParams
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_PBS_tiled_global
                        ' Slot mapping, in lockstep with
                        ' FX_PBS_tiled_global_entry in model.frag:
                        '   maps[0..2]    = albedoHeightTile0/1/2
                        '   maps[3]       = blendMask   (A = baked AO)
                        '   maps[4]       = dirtMap
                        '   maps[5]       = colorTex    (GCM)
                        '   maps[6]       = globalTex   (GNM, B*2 = baked shadow)
                        '   g_tileUVScale = g_tileUVScale
                        '   g_tile0Tint   = g_dirtColorParams (yzw = per-tile GCM luminance weights)
                        '   g_tile1Tint   = g_tintParams      (yzw = per-tile GCM chroma weights)
                        '   g_tile2Tint   = g_dirtColor       (w = dirt curve strength)
                        Dim props As MaterialProps_PBS_tiled_global = mat.props
                        .map1Handle = textureHandles(props.albedoHeightTile0)
                        .map2Handle = textureHandles(props.albedoHeightTile1)
                        .map3Handle = textureHandles(props.albedoHeightTile2)
                        .map4Handle = textureHandles(props.blendMask)
                        If props.dirtMap IsNot Nothing Then
                            .map5Handle = textureHandles(props.dirtMap)
                        End If
                        .map6Handle = textureHandles(props.colorTex)
                        .map7Handle = textureHandles(props.globalTex)
                        .g_tileUVScale = props.g_tileUVScale
                        .g_tile0Tint = props.g_dirtColorParams
                        .g_tile1Tint = props.g_tintParams
                        .g_tile2Tint = props.g_dirtColor
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_PBS_glass
                        Dim props As MaterialProps_PBS_glass = mat.props
                        If props.dirtAlbedoMap IsNot Nothing Then
                            .map1Handle = textureHandles(props.dirtAlbedoMap)
                        End If
                        .map2Handle = textureHandles(props.normalMap)
                        .map3Handle = textureHandles(props.glassMap)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_colorTint = props.g_filterColor
                        .texAddressMode = props.texAddressMode
                        .double_sided = 0

                    Case ShaderTypes.FX_PBS_ext_repaint
                        Dim props As MaterialProps_PBS_ext_repaint = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .map2Handle = textureHandles(props.normalMap)
                        .map3Handle = textureHandles(props.metallicGlossMap)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .g_tile0Tint = props.g_baseColor
                        .g_tile1Tint = props.g_repaintColor
                        .g_enableAO = If(props.g_enableAO, 1, 0)
                        .double_sided = If(props.doubleSided, 1, 0)

                    Case ShaderTypes.FX_lightonly_alpha
                        Dim props As MaterialProps_lightonly_alpha = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .double_sided = If(props.doubleSided, 1, 0)
                        ' These cards carry no normal map - the depth passes
                        ' must take the cutout alpha from diffuse.a, not the
                        ' PBS normal-map red channel.
                        .alphaFromDiffuse = 1

                    Case ShaderTypes.FX_glow
                        Dim props As MaterialProps_lightonly_alpha = mat.props
                        .map1Handle = textureHandles(props.diffuseMap)
                        .alphaReference = props.alphaReference / 255.0
                        .alphaTestEnable = If(props.alphaTestEnable, 1, 0)
                        .double_sided = If(props.doubleSided, 1, 0)
                        .alphaFromDiffuse = 1
                        ' The emissive multiplier rides in g_colorTint, which
                        ' nothing else on this path uses. The compiled shader
                        ' multiplies by (selfIllumination + 1), so 0 is a
                        ' no-op and Abbey's burnt grass is a x16.
                        Dim illum = props.selfIllumination + 1.0F
                        .g_colorTint = New Vector4(illum, illum, illum, 1.0F)

                    Case Else
                        'Stop
                End Select
            End With
        Next

        materials = Nothing

        ' This buffer is read as MaterialProperties in shaders/common.h under std430.
        ' 10 vec4 (160) + 12 uvec2 (96) + 8 x 4-byte scalars (32) = 288 exactly
        ' (alphaFromDiffuse took the old tail padding). Add a field here and you
        ' must add it to common.h too, or every material reads garbage.
        Debug.Assert(Marshal.SizeOf(Of GLMaterial) = 288, "GLMaterial no longer matches MaterialProperties in common.h")

        map_scene.static_models.materials = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "materials")
        map_scene.static_models.materials.Storage(
            materialsData.Length * Marshal.SizeOf(Of GLMaterial),
            materialsData,
            BufferStorageFlags.None)
        map_scene.static_models.materials.BindBase(3)

        ' ---- FX composite class, derived once, read by the sort every frame ----
        ' additive = the volumetric fragment shader takes the mat.alphaTestEnable
        ' branch (volumetric.frag:120) and emits (rgb*a, 0). Under the FX pass
        ' blend One / OneMinusSrcAlpha with src.a = 0 that is dst + src: it adds
        ' light and attenuates nothing, so it is safe to composite LAST and it is
        ' the only class that can be moved without changing anything else.
        '
        ' GATED ON FX_volumetric. On every other shader type the alphaTestEnable
        ' slot is a real alpha TEST, so reading it unguarded would misclassify
        ' ordinary opaque geometry as fire.
        Dim mat_fx_additive(materialsData.Length - 1) As Boolean
        For mi = 0 To materialsData.Length - 1
            mat_fx_additive(mi) = (materialsData(mi).shader_type = CUInt(ShaderTypes.FX_volumetric) AndAlso
                                   materialsData(mi).alphaTestEnable = 1)
        Next

        With map_scene.static_models
            If .candidate_material_ids IsNot Nothing AndAlso .candidate_model_ids IsNot Nothing Then
                ' Fold to per-INSTANCE first: an instance counts as additive if
                ' ANY of its draws is. That keeps every mesh contiguous in the
                ' sorted bucket instead of splitting its authored layer stack.
                Dim inst_additive As New Dictionary(Of UInteger, Boolean)
                For i = 0 To .candidate_material_ids.Length - 1
                    Dim mid_ = .candidate_model_ids(i)
                    Dim add_ = False
                    If .candidate_material_ids(i) < CUInt(mat_fx_additive.Length) Then
                        add_ = mat_fx_additive(CInt(.candidate_material_ids(i)))
                    End If
                    Dim cur_ As Boolean
                    If inst_additive.TryGetValue(mid_, cur_) Then
                        inst_additive(mid_) = cur_ OrElse add_
                    Else
                        inst_additive(mid_) = add_
                    End If
                Next

                ReDim .candidate_fx_additive(.candidate_material_ids.Length - 1)
                Dim n_add = 0
                For i = 0 To .candidate_material_ids.Length - 1
                    .candidate_fx_additive(i) = inst_additive(.candidate_model_ids(i))
                    If .candidate_fx_additive(i) Then n_add += 1
                Next
                LogThis("FX composite class: {0} of {1} candidates additive (fire), rest alpha (smoke)",
                        n_add, .candidate_fx_additive.Length)
            End If
        End With
    End Sub

    ''' <summary>
    ''' What the particle readers found. Logged rather than drawn for now: the
    ''' 32-bit effect id in each placement is not resolved to a file yet, so a
    ''' placement cannot be matched to its .vfxbin in general.
    ''' </summary>
    Private Sub report_pfx_placements()
        If PFX_PLACEMENTS Is Nothing OrElse PFX_PLACEMENTS.Count = 0 Then
            LogThis("particles: no BWPs placements")
            Return
        End If
        Dim ids As New Dictionary(Of UInteger, Integer)
        For Each p In PFX_PLACEMENTS
            If ids.ContainsKey(p.effectId) Then ids(p.effectId) += 1 Else ids(p.effectId) = 1
        Next
        LogThis("particles: {0} placement(s), {1} distinct effect id(s)", PFX_PLACEMENTS.Count, ids.Count)
        For Each kv In ids
            LogThis("   effect {0:x8} x{1}", kv.Key, kv.Value)
        Next
        Dim shown = 0
        For Each p In PFX_PLACEMENTS
            If shown >= 6 Then Exit For
            Dim t = p.transform.Row3
            LogThis("   at ({0:0.##}, {1:0.##}, {2:0.##})  effect {3:x8}", t.X, t.Y, t.Z, p.effectId)
            shown += 1
        Next

        ' Verify the .vfxbin reader in-engine against the offline decode.
        For Each nm In {"Big", "Med", "Small", "Ash_black"}
            Dim rel = String.Format(
                "particles/content_deferred/PFX/Environment/Buildings/Bld_19_01_Vhouse_05_Smoke_{0}.vfxbin", nm)
            Dim entry = ResMgr.Lookup(rel)
            If entry Is Nothing Then
                LogThis("   pfx {0}: NOT FOUND", nm)
                Continue For
            End If
            Using ms As New MemoryStream
                entry.Extract(ms)
                Dim eff = modParticles.LoadVfx(ms.ToArray(), rel)
                If eff Is Nothing Then
                    LogThis("   pfx {0}: parse failed", nm)
                    Continue For
                End If
                LogThis("   pfx {0}: {1} emitter(s)", nm, eff.emitters.Count)
                For Each em In eff.emitters
                    LogThis("      {0,-12} rate={1,-5:0.##} box=({2:0.##},{3:0.##},{4:0.##}) spread={5:0.#}deg size={6:0.###}..{7:0.###} life={8:0.##}..{9:0.##} atlas={10}x{11}@{12:0.#}",
                            em.name, em.rate, em.boxHalf.X, em.boxHalf.Y, em.boxHalf.Z,
                            em.spread * 57.2958F, em.sizeMin, em.sizeMax,
                            em.lifeMin, em.lifeMax, em.atlasCols, em.atlasRows, em.atlasFps)
                    If em.sizeTrack IsNot Nothing Then
                        LogThis("         size x {0} -> {1}   colour keys={2}",
                                em.sizeTrack.Sample(0.0F, 0), em.sizeTrack.Sample(1.0F, 0),
                                If(em.colourTrack Is Nothing, 0, em.colourTrack.times.Length))
                    End If
                    LogThis("         tex    {0}", em.diffuse)
                Next
            End Using
        Next
    End Sub

    Private Function get_spaceBin(ABS_NAME As String) As Boolean
        Dim space_bin_file = ResMgr.Lookup(String.Format("spaces/{0}/space.bin", ABS_NAME))
        Dim ms As New MemoryStream
        space_bin_file.Extract(ms)
        If ms IsNot Nothing Then
            ' Particle effect placements (BWPs). Read BEFORE ReadSpaceBinData,
            ' which closes the stream on its way out.
            PFX_PLACEMENTS = modParticles.LoadPlacements(ms)
            report_pfx_placements()

            If Not ReadSpaceBinData(ms) Then
                MsgBox("Error decoding Space.bin", MsgBoxStyle.Exclamation, "File Error...")
                Return False
            End If
        Else
            MsgBox("Unable to load Space.bin from package", MsgBoxStyle.Exclamation, "File Error...")
            Return False
        End If
        Return True
    End Function


End Module

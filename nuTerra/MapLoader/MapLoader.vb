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
                    End If
                Next

                If skip Then Continue For

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
                    End With
                    map_scene.PICK_DICTIONARY(mLast + i) = Path.GetDirectoryName(MAP_MODELS(batch.model_id).modelLods(0).render_sets(0).verts_name)
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

        MAP_LOADED = True

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
        '===================================================
        ' Set sun location from map data
        set_light_pos() 'for light rotation animation
        '===================================================
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

            decal_item.color_only = 0
            decal_item.normal_only = 0


            Dim flag = decal.v1 ' And decal.v2
            Dim m = flag ' - &HFFFFFFFF

            decal_item.color_only = m And &HFFFF
            decal_item.material_type = CSng(decal.materialType)
            Debug.WriteLine("flag: " + decal_item.color_only.ToString + " id: " + i.ToString + " Mask: " + decal_item.color_only.ToString)


            'Debug.WriteLine("materialType: " + decal.materialType.ToString)

            decal_item.offset = decal.offsets.Xz 'XY?
            decal_item.scale = decal.uv_wrapping

            If decal_item.offset.X > 1 Then
                Stop
            End If
            If decal_item.offset.Y > 0 Then
            End If
            decal_item.matrix = decal.transform

            'Flip some row values to convert from DirectX to Opengl
            decal_item.matrix.M12 *= -1.0
            decal_item.matrix.M13 *= -1.0
            decal_item.matrix.M21 *= -1.0
            decal_item.matrix.M31 *= -1.0
            decal_item.matrix.M41 *= -1.0


            Dim diff_fname = cBWST.find_str(decal.diff_tex_fnv)
            If diff_fname.Length > 0 Then
                decal_item.color_tex = TextureMgr.OpenDDS(diff_fname)

                Dim normal_fname = cBWST.find_str(decal.bump_tex_fnv)
                decal_item.normal_tex = TextureMgr.OpenDDS(normal_fname)

                map_scene.decals.all_decals.Add(decal_item)
            Else
                ' Nothing to do?
            End If
            i += 1
        Next

        map_scene.DECALS_LOADED = True
    End Sub

    Public Sub set_light_pos()
        LIGHT_RADIUS = MAP_SIZE.Length * 100.0
        'LIGHT_ORBIT_ANGLE_Z += 180.0
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

                Case ShaderTypes.FX_PBS_glass
                    texturePaths.Add(mat.props.dirtAlbedoMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.glassMap)

                Case ShaderTypes.FX_PBS_ext_repaint
                    texturePaths.Add(mat.props.diffuseMap)
                    texturePaths.Add(mat.props.normalMap)
                    texturePaths.Add(mat.props.metallicGlossMap)

                Case ShaderTypes.FX_lightonly_alpha
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
                Dim dds_entry = ResMgr.Lookup(atlasPathAndUsage.Key.Replace(".dds", "_hd.dds"))
                If dds_entry Is Nothing Then
                    dds_entry = ResMgr.Lookup(atlasPathAndUsage.Key)
                    If dds_entry Is Nothing Then
                        Stop
                        Continue For
                    End If
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
                Dim magic = br.ReadChars(4)
                Debug.Assert(magic = "BCVT")
                Dim unused2 = br.ReadUInt32
                Debug.Assert(unused2 = 1)

                Dim dds_chunk_size = br.ReadUInt64
                ms.Position += dds_chunk_size

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

                Dim dds_entry = ResMgr.Lookup(coords.path.Replace(".dds", "_hd.dds"))
                If dds_entry Is Nothing Then
                    dds_entry = ResMgr.Lookup(coords.path)
                    If dds_entry Is Nothing Then
                        Stop
                        Continue For
                    End If
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
                textureHandles(texturePath) = hndl
                Continue For
            End If

            Dim entry = ResMgr.Lookup(texturePath.Replace(".dds", "_hd.dds"))
            If entry Is Nothing Then
                entry = ResMgr.Lookup(texturePath)
            End If
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

                    Case Else
                        'Stop
                End Select
            End With
        Next

        materials = Nothing

        map_scene.static_models.materials = GLBuffer.Create(BufferTarget.ShaderStorageBuffer, "materials")
        map_scene.static_models.materials.Storage(
            materialsData.Length * Marshal.SizeOf(Of GLMaterial),
            materialsData,
            BufferStorageFlags.None)
        map_scene.static_models.materials.BindBase(3)
    End Sub

    Private Function get_spaceBin(ABS_NAME As String) As Boolean
        Dim space_bin_file = ResMgr.Lookup(String.Format("spaces/{0}/space.bin", ABS_NAME))
        Dim ms As New MemoryStream
        space_bin_file.Extract(ms)
        If ms IsNot Nothing Then
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

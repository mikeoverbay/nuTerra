Imports System.IO
Imports System.Runtime
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Module MapLoader
    Public LODMAPSIZE As Integer = 256
    Public AOMAPSIZE As Integer = 256
    Public HEIGHTMAPSIZE As Integer = 64
    Public NORMALMAPSIZE As Integer = 256
    Public HOLEMAPSIZE As Integer = 64
    Public SHADOWMAPSIZE As Integer = 64
    Public BLENDMAPSIZE As Integer = 256

    '-----------------------------------
    'This stores all models used on a map
    Public MAP_MODELS() As mdl_

    NotInheritable Class MapGL
        ' Get data from gpu
        Public Shared numAfterFrustum(2) As Integer

        ''' <summary>
        ''' OpenGL buffers used to draw all map models
        ''' </summary>
        NotInheritable Class Buffers
            ' For map models only!
            Public Shared materials As GLBuffer
            Public Shared parameters As GLBuffer
            Public Shared parameters_temp As GLBuffer
            Public Shared matrices As GLBuffer
            Public Shared drawCandidates As GLBuffer
            Public Shared verts As GLBuffer
            Public Shared vertsUV2 As GLBuffer
            Public Shared prims As GLBuffer
            Public Shared indirect As GLBuffer
            Public Shared indirect_glass As GLBuffer
            Public Shared indirect_dbl_sided As GLBuffer
            Public Shared lods As GLBuffer

            ' For terrain only!
            Public Shared terrain_matrices As GLBuffer
            Public Shared terrain_indirect As GLBuffer
            Public Shared terrain_vertices As GLBuffer
            Public Shared terrain_indices As GLBuffer

            ' For cull-raster only!
            Public Shared visibles As GLBuffer
            Public Shared visibles_dbl_sided As GLBuffer
        End Class

        NotInheritable Class VertexArrays
            Public Shared allMapModels As Integer
            Public Shared allTerrainChunks As Integer
        End Class

        Public Shared numModelInstances As Integer
        Public Shared indirectDrawCount As Integer
    End Class

    Public Structure mdl_
        Public modelLods() As base_model_holder_
        Public visibilityBounds As Matrix2x3
    End Structure

#Region "utility functions"

#End Region

    '============================================================================
    Public Sub load_map(map_name As String)
        'disable main menu
        frmMain.MainMenuStrip.Enabled = False

        MAP_LOADED = False
        SHOW_MAPS_SCREEN = False
        BG_MAX_VALUE = 0

        'SHOW_CURSOR = True

        SHOW_LOADING_SCREEN = True
        'For now, we are going to hard wire this name
        'and call this at startup so skip having to select a menu

        'First we need to remove the loaded data.
        '===============================================================
        remove_map_data() '=============================================
        '===============================================================
        'House Keeping

        '===============================================================
        'get the light settings for this map.
        frmLightSettings.Init()
        '===============================================================

        '===============================================================
        'Set draw enable flags
        TERRAIN_LOADED = False
        TREES_LOADED = False
        DECALS_LOADED = False
        MODELS_LOADED = False
        BASES_LOADED = False
        SKY_LOADED = False
        WATER_LOADED = False
        '===============================================================


        '===============================================================

        '===============================================================
        'load sand texture. This will need to be set by the maps vars at some point.
        'm_normal_id = find_and_load_texture_from_pkgs("maps/landscape/detail/sand_NM.dds")
        '===============================================================

        '===============================================================
        'Open the space.bin file. If it fails, it closes all packages and lets the user know.
        If Not get_spaceBin(map_name) Then
            MsgBox("Failed to load Space.Bin from the map package.", MsgBoxStyle.Exclamation, "Space.bin!")
            'Enabled main menu
            frmMain.MainMenuStrip.Enabled = True
            Return
        End If
        '===============================================================
        'need this for all rendering states
        get_environment_info(map_name)
        '===============================================================
        SUN_TEXTURE_ID = load_dds_image_from_file(Path.Combine(Application.StartupPath, "resources\sol.dds"))
        'Dim entry = search_pkgs(SUN_TEXTURE_PATH)
        'If entry IsNot Nothing Then
        '    Dim ms As New MemoryStream
        '    entry.Extract(ms)
        'End If
        Dim entry = ResMgr.Lookup(theMap.lut_path)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            CC_LUT_ID = load_dds_image_from_stream(ms, theMap.lut_path)
        End If
        'get env_brdf
        entry = ResMgr.Lookup("system/maps/env_brdf_lut.dds")
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            ENV_BRDF_LUT_ID = load_dds_image_from_stream(ms, "system/maps/env_brdf_lut.dds")
        End If

        '===============================================================
        'load ripple textures
#If False Then
        Dim rtc = Map_wetness.waveTextureCount - 1
        ReDim RIPPLE_TEXTURES(rtc)
        For i = 0 To rtc
            Dim r_path = Map_wetness.waveTexture + i.ToString("000") + ".dds"
            RIPPLE_TEXTURES(i) = find_and_load_texture_from_pkgs(r_path)
        Next
        RIPPLE_MASK_TEXTURE = find_and_load_texture_from_pkgs(Map_wetness.waveMaskTexture)
#End If
        '===============================================================

#Region "load models"

        If DONT_BLOCK_MODELS Then

            ' Setup Bar graph
            BG_TEXT = "Loading Models..."
            BG_MAX_VALUE = MAP_MODELS.Length - 1
            BG_VALUE = 0
            draw_scene()

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
                    Application.DoEvents() '<-- Give some time to this app's UI
                    draw_scene()
                End If
            Next

            '----------------------------------------------------------------
            ' calc instances
            MapGL.numModelInstances = 0
            MapGL.indirectDrawCount = 0
            Dim numVerts = 0
            Dim numPrims = 0
            Dim numLods = 0
            For Each batch In MODEL_BATCH_LIST
                For lod_id = 0 To MAP_MODELS(batch.model_id).modelLods.Count - 1
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
                            MapGL.indirectDrawCount += batch.count
                            skip = False
                        Next
                        numVerts += renderSet.buffers.vertexBuffer.Length
                        numPrims += renderSet.buffers.index_buffer32.Length
                    Next

                    If skip Then Continue For

                    numLods += batch.count
                    If lod_id = 0 Then MapGL.numModelInstances += batch.count
                Next
            Next

            '----------------------------------------------------------------
            ' setup instances
            Dim drawCommands(MapGL.indirectDrawCount - 1) As CandidateDraw

            Dim vertex_size = Marshal.SizeOf(Of ModelVertex)()
            Dim tri_size = Marshal.SizeOf(Of vect3_32)()
            Dim uv2_size = Marshal.SizeOf(Of Vector2)()

            MapGL.Buffers.verts = CreateBuffer(BufferTarget.ArrayBuffer, "verts")
            BufferStorageNullData(MapGL.Buffers.verts,
                                  numVerts * vertex_size,
                                  BufferStorageFlags.DynamicStorageBit)

            MapGL.Buffers.prims = CreateBuffer(BufferTarget.ElementArrayBuffer, "prims")
            BufferStorageNullData(MapGL.Buffers.prims,
                                  numPrims * tri_size,
                                  BufferStorageFlags.DynamicStorageBit)

            MapGL.Buffers.vertsUV2 = CreateBuffer(BufferTarget.ArrayBuffer, "vertsUV2")
            BufferStorageNullData(MapGL.Buffers.vertsUV2,
                                  numVerts * uv2_size,
                                  BufferStorageFlags.DynamicStorageBit)

            Dim matrices(MapGL.numModelInstances - 1) As ModelInstance
            Dim lods(numLods - 1) As ModelLoD
            Dim cmdId = 0
            Dim vLast = 0
            Dim iLast = 0
            Dim mLast = 0
            Dim lodLast = 0
            Dim baseVert = 0
            For Each batch In MODEL_BATCH_LIST
                Dim skip = True
                Dim savedLodOffset = lodLast

                For lod_id = 0 To MAP_MODELS(batch.model_id).modelLods.Count - 1
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
                            cmdId += 1
                            skip = False
                        Next

                        baseVert += renderSet.numVertices

                        GL.NamedBufferSubData(MapGL.Buffers.verts.buffer_id, New IntPtr(vLast * vertex_size), renderSet.buffers.vertexBuffer.Count * vertex_size, renderSet.buffers.vertexBuffer)
                        GL.NamedBufferSubData(MapGL.Buffers.prims.buffer_id, New IntPtr(iLast * tri_size), renderSet.buffers.index_buffer32.Count * tri_size, renderSet.buffers.index_buffer32)

                        If renderSet.buffers.uv2 IsNot Nothing Then
                            GL.NamedBufferSubData(MapGL.Buffers.vertsUV2.buffer_id, New IntPtr(vLast * uv2_size), renderSet.buffers.uv2.Count * uv2_size, renderSet.buffers.uv2)
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
                        .lod_count = MAP_MODELS(batch.model_id).modelLods.Count
                        .batch_count = batch.count
                    End With
                    PICK_DICTIONARY(mLast + i) = Path.GetDirectoryName(MAP_MODELS(batch.model_id).modelLods(0).render_sets(0).verts_name)
                Next
                mLast += batch.count
            Next

            MapGL.Buffers.parameters_temp = CreateBuffer(BufferTarget.CopyWriteBuffer, "parameters_temp")
            BufferStorageNullData(MapGL.Buffers.parameters_temp,
                                  3 * Marshal.SizeOf(Of Integer),
                                  BufferStorageFlags.ClientStorageBit)

            MapGL.Buffers.parameters = CreateBuffer(BufferTarget.AtomicCounterBuffer, "parameters")
            BufferStorageNullData(MapGL.Buffers.parameters,
                                  3 * Marshal.SizeOf(Of Integer),
                                  BufferStorageFlags.None)
            MapGL.Buffers.parameters.BindBase(0)

            MapGL.Buffers.visibles = CreateBuffer(BufferTarget.ShaderStorageBuffer, "visibles")
            BufferStorageNullData(MapGL.Buffers.visibles,
                                  MapGL.indirectDrawCount * Marshal.SizeOf(Of Integer),
                                  BufferStorageFlags.DynamicStorageBit)
            MapGL.Buffers.visibles.BindBase(8)

            MapGL.Buffers.visibles_dbl_sided = CreateBuffer(BufferTarget.ShaderStorageBuffer, "visibles_dbl_sided")
            BufferStorageNullData(MapGL.Buffers.visibles_dbl_sided,
                                  MapGL.indirectDrawCount * Marshal.SizeOf(Of Integer),
                                  BufferStorageFlags.DynamicStorageBit)
            MapGL.Buffers.visibles_dbl_sided.BindBase(9)

            MapGL.Buffers.drawCandidates = CreateBuffer(BufferTarget.ShaderStorageBuffer, "drawCandidates")
            BufferStorage(MapGL.Buffers.drawCandidates,
                          MapGL.indirectDrawCount * Marshal.SizeOf(Of CandidateDraw),
                          drawCommands,
                          BufferStorageFlags.None)
            MapGL.Buffers.drawCandidates.BindBase(1)
            Erase drawCommands

            MapGL.Buffers.indirect = CreateBuffer(BufferTarget.ShaderStorageBuffer, "indirect")
            BufferStorageNullData(MapGL.Buffers.indirect,
                                  MapGL.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                                  BufferStorageFlags.None)
            MapGL.Buffers.indirect.BindBase(2)

            MapGL.Buffers.indirect_glass = CreateBuffer(BufferTarget.ShaderStorageBuffer, "indirect_glass")
            BufferStorageNullData(MapGL.Buffers.indirect_glass,
                                  MapGL.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                                  BufferStorageFlags.None)
            MapGL.Buffers.indirect_glass.BindBase(5)

            MapGL.Buffers.indirect_dbl_sided = CreateBuffer(BufferTarget.ShaderStorageBuffer, "indirect_dbl_sided")
            BufferStorageNullData(MapGL.Buffers.indirect_dbl_sided,
                                  MapGL.indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand),
                                  BufferStorageFlags.None)
            MapGL.Buffers.indirect_dbl_sided.BindBase(6)

            MapGL.Buffers.matrices = CreateBuffer(BufferTarget.ShaderStorageBuffer, "matrices")
            BufferStorage(MapGL.Buffers.matrices,
                          matrices.Length * Marshal.SizeOf(Of ModelInstance),
                          matrices,
                          BufferStorageFlags.None)
            MapGL.Buffers.matrices.BindBase(0)
            Erase matrices

            MapGL.Buffers.lods = CreateBuffer(BufferTarget.ShaderStorageBuffer, "lods")
            BufferStorage(MapGL.Buffers.lods,
                          lods.Length * Marshal.SizeOf(Of ModelLoD),
                          lods,
                          BufferStorageFlags.None)
            MapGL.Buffers.lods.BindBase(4)
            Erase lods

            MapGL.VertexArrays.allMapModels = CreateVertexArray("allMapModels")

            'pos
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 0, MapGL.Buffers.verts.buffer_id, New IntPtr(0), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 0, 3, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 0, 0)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 0)

            'normal
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 1, MapGL.Buffers.verts.buffer_id, New IntPtr(12), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 1, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 1, 1)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 1)

            'tangent
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 2, MapGL.Buffers.verts.buffer_id, New IntPtr(20), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 2, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 2, 2)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 2)

            'binormal
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 3, MapGL.Buffers.verts.buffer_id, New IntPtr(28), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 3, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 3, 3)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 3)

            'uv
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 4, MapGL.Buffers.verts.buffer_id, New IntPtr(36), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 4, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 4, 4)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 4)

            'uv2
            GL.VertexArrayVertexBuffer(MapGL.VertexArrays.allMapModels, 5, MapGL.Buffers.vertsUV2.buffer_id, IntPtr.Zero, Marshal.SizeOf(Of Vector2))
            GL.VertexArrayAttribFormat(MapGL.VertexArrays.allMapModels, 5, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(MapGL.VertexArrays.allMapModels, 5, 5)
            GL.EnableVertexArrayAttrib(MapGL.VertexArrays.allMapModels, 5)

            GL.VertexArrayElementBuffer(MapGL.VertexArrays.allMapModels, MapGL.Buffers.prims.buffer_id)

            load_materials()

            Erase MAP_MODELS

            MODELS_LOADED = True
        End If ' block DONT_BLOCK_MODELS laoded
#End Region
        '===============================================================


        '===============================================================
        'As it says.. create the terrain
        If DONT_BLOCK_TERRAIN Then
            Create_Terrain()
            PLAYER_FIELD_CELL_SIZE = Math.Abs(MAP_BB_BL.X - MAP_BB_UR.X) / 10.0F

#If False Then
            If Not Build_Mega_Textures() Then
                MsgBox("failed to create Virtual Textures on Disc Drive!" + vbCrLf +
                   "I will close now...", MsgBoxStyle.Exclamation, "Not a good day!")
                End
            End If
#End If
            TERRAIN_LOADED = True
        End If 'DONT_BLOCK_TERRAIN
        '===============================================================
        'load cube map for PBS_ext lighting,
        'It must happend after terrain load to get the path.
        load_cube_and_cube_map()
        '===============================================================
        'test load of maga textures
        '===============================================================
        '===============================================================

        RebuildVTAtlas()

        MAP_LOADED = True

        '===============================================================
        'We need to get the Y location of the rings and stop drawing overly tall cubes.
        'It only needs to happen once!
        If BASE_RINGS_LOADED Then
            T1_Y = get_Y_at_XZ(-TEAM_1.X, TEAM_1.Z)
            T2_Y = get_Y_at_XZ(-TEAM_2.X, TEAM_2.Z)
        End If

        '===============================================================
        'load some test emitters
#If False Then

        ReDim Test_Emiters(500) '<--- emitter count
        ReDim sort_lists(Test_Emiters.Length)
        For i = 0 To 500
            Test_Emiters(i) = New Explosion_type_1

            Test_Emiters(i).total_frames = 91
            Test_Emiters(i).row_length = 46

            Test_Emiters(i).update_time = 35
            Test_Emiters(i).particle_count = 6 '<-- Partices for this emitter.

            Test_Emiters(i).fixed_expand_speed = True

            Test_Emiters(i).note = "This better F'in work!"

            Dim v = get_random_vector3(400) '<--- Spread out will be 1/2 this value

            v.Y = get_Y_at_XZ(v.X, v.Z)
            Test_Emiters(i).start_location = v

            Test_Emiters(i).Scatter_factor = 0.5F ' each quad randomly moves by per loop
            Test_Emiters(i).z_speed = 2.0

            '  the quad may never get this big if Expand_speed is too low a value 
            Test_Emiters(i).max_expand_size = 50.0F

            Test_Emiters(i).expand_start_size = 10.0F 'initial size 

            '(30 - 2) / 91 frames = 0.307692.. prefect grow rate..
            'It can be larger BUT the quad will be frozen at max_expand_size for the remaining frames
            'Too small and the quad will never reach max_expanded size.
            'It will reach max size before the frames are done using this valune
            Test_Emiters(i).Expand_speed = 1.5F

            ReDim sort_lists(i).list(Test_Emiters(i).particle_count)
            Test_Emiters(i).birth_speed = (Test_Emiters(i).total_frames * Test_Emiters(i).update_time) / Test_Emiters(i).particle_count '<-- best if this = 1000. particle count
            '(total_frames * update speed)/paricle_count = time to cycle.
            'divid this by paricle count to get smooot birth rate


            Test_Emiters(i).continuous = True ' repeat or not


            Test_Emiters(i).initialize()
        Next
#End If

        CommonProperties.update()

        '===============================================================
        MINI_MAP_SIZE += 1 ' force a redraw of the entire minimap
        '===============================================================

        SHOW_LOADING_SCREEN = False
        'LOOK_AT_X = 0.001
        'LOOK_AT_Z = 0.001
        '===================================================
        ' Set sun location from map data
        set_light_pos() 'for light rotation animation
        '===================================================

        frmMain.check_postion_for_update() ' need to initialize cursor altitude

        'Enable main menu
        frmMain.MainMenuStrip.Enabled = True

    End Sub

    Public Sub RebuildVTAtlas()
        LogThis("REBUILD ATLAS")

        vtInfo = New VirtualTextureInfo With {
            .TileSize = TILE_SIZE,
            .VirtualTextureSize = TILE_SIZE * VT_NUM_PAGES
            }

        If vt IsNot Nothing Then vt.Dispose()
        vt = New VirtualTexture(vtInfo, NUM_TILES, UPLOADS_PER_FRAME)

        If feedback IsNot Nothing Then feedback.Dispose()
        feedback = New FeedbackBuffer(vtInfo, FEEDBACK_WIDTH, FEEDBACK_HEIGHT)

        CommonProperties.VirtualTextureSize = vtInfo.VirtualTextureSize
        CommonProperties.AtlasScale = 1.0F / (vtInfo.VirtualTextureSize / vtInfo.TileSize)
        CommonProperties.PageTableSize = vtInfo.PageTableSize
        CommonProperties.update()
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
        Dim x0 As Int32
        Dim x1 As Int32
        Dim y0 As Int32
        Dim y1 As Int32
        Dim path As String
    End Structure

    'Load materials
    Private Sub load_materials()
        Dim texturePaths As New HashSet(Of String)
        Dim atlasPaths As New HashSet(Of String)


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
                    atlasPaths.Add(mat.props.atlasAlbedoHeight)
                    Debug.Assert(mat.props.atlasBlend.EndsWith(".png"))
                    mat.props.atlasBlend = mat.props.atlasBlend.Replace(".png", ".dds") 'hack!!!
                    texturePaths.Add(mat.props.atlasBlend)
                    atlasPaths.Add(mat.props.atlasNormalGlossSpec)
                    atlasPaths.Add(mat.props.atlasMetallicAO)
                    If mat.props.dirtMap IsNot Nothing Then
                        atlasPaths.Add(mat.props.dirtMap)
                    End If

                Case ShaderTypes.FX_PBS_tiled_atlas_global
                    atlasPaths.Add(mat.props.atlasAlbedoHeight)
                    Debug.Assert(mat.props.atlasBlend.EndsWith(".png"))
                    mat.props.atlasBlend = mat.props.atlasBlend.Replace(".png", ".dds") 'hack!!!
                    texturePaths.Add(mat.props.atlasBlend)
                    atlasPaths.Add(mat.props.atlasNormalGlossSpec)
                    atlasPaths.Add(mat.props.atlasMetallicAO)
                    atlasPaths.Add(mat.props.atlasAlbedoHeight)
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
        draw_scene()

        Dim textureHandles As New Dictionary(Of String, UInt64)
        For Each atlasPath In atlasPaths
            If atlasPath.EndsWith(".dds") Then
                texturePaths.Add(atlasPath)
                Continue For
            End If

            If Not atlasPath.EndsWith(".atlas") Then
                'Stop
                texturePaths.Add(atlasPath)
                Continue For
            End If

            Dim entry = ResMgr.Lookup(atlasPath + "_processed")
            If entry Is Nothing Then
                Stop
                Continue For
            End If

            'update bargraph
            BG_VALUE += 1
            If BG_VALUE Mod 100 = 0 Then
                Application.DoEvents() 'stop freezing the UI
                draw_scene()
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)
            ms.Position = 0

            Dim atlasParts As New List(Of AtlasCoords)
            Dim uniqueX0 As New HashSet(Of Integer)
            Dim uniqueY0 As New HashSet(Of Integer)

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

                While br.BaseStream.Position < br.BaseStream.Length - 1
                    Dim coords As New AtlasCoords
                    coords.x0 = br.ReadInt32
                    coords.x1 = br.ReadInt32
                    coords.y0 = br.ReadInt32
                    coords.y1 = br.ReadInt32

                    'hack for now
                    uniqueX0.Add(coords.x0)
                    uniqueY0.Add(coords.y0)

                    coords.path = ""
                    Dim tmpChar = br.ReadChar
                    While tmpChar <> vbNullChar
                        coords.path += tmpChar
                        tmpChar = br.ReadChar
                    End While

                    coords.path = coords.path.Replace(".png", ".dds")
                    atlasParts.Add(coords)
                End While
            End Using

            Dim atlas_tex As New GLTexture
            Dim fullWidth As Integer
            Dim fullHeight As Integer
            Dim multiplierX, multiplierY As Single
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
                    Dim dds_header = get_dds_header(dds_br)
                    dds_ms.Position = 128

                    Dim format_info = dds_header.format_info

                    If i = 0 Then 'run once

                        ' gets size of atlas
                        fullWidth = uniqueX0.Count * dds_header.width
                        fullHeight = uniqueY0.Count * dds_header.height

                        multiplierX = dds_header.width / (coords.x1 - coords.x0)
                        multiplierY = dds_header.height / (coords.y1 - coords.y0)

                        'Calculate Max Mip Level based on width or height.. Which ever is larger.
                        Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(fullWidth, fullHeight), 2))

                        atlas_tex = CreateTexture(TextureTarget.Texture2D, atlasPath)
                        atlas_tex.Storage2D(numLevels, format_info.texture_format, fullWidth, fullHeight)

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

                    Dim xoffset = CInt(coords.x0 * multiplierX)
                    Dim yoffset = CInt(coords.y0 * multiplierY)
                    'Dim er = GL.GetError
                    atlas_tex.CompressedSubImage2D(0, xoffset, yoffset, dds_header.width, dds_header.height,
                                                DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                    'er = GL.GetError
                    'If er > 0 Then
                    '    Debug.WriteLine("error!")
                    'End If
                End Using
            Next
            atlas_tex.GenerateMipmap()
            'If atlasPath.ToLower.Contains("Tirpiz_atlas_AM".ToLower) Then
            '    GL.Clear(ClearBufferMask.ColorBufferBit)
            '    draw_test_iamge(fullWidth / 2, fullHeight / 2, atlas_tex)
            '    Stop
            'End If

            Dim handle = GL.Arb.GetTextureHandle(atlas_tex.texture_id)
            GL.Arb.MakeTextureHandleResident(handle)

            textureHandles(atlasPath) = handle
        Next

        'load textures
        For Each texturePath In texturePaths
            Application.DoEvents() 'stop freezing the UI
            Dim old_texturePath = texturePath
            If Not texturePath.EndsWith(".dds") Then
                'Stop
                texturePath = texturePath.Replace(".png", ".dds") ' hack
                'Continue For
            End If
            'dont load images that are already created!
            Dim image_id = image_exists(texturePath)
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
            If BG_VALUE Mod 50 = 0 Then
                draw_scene()
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)

            Dim tex = load_dds_image_from_stream(ms, texturePath)

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
                        .g_atlasIndexes = props.g_atlasIndexes
                        .g_atlasSizes = props.g_atlasSizes
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
                        .g_atlasIndexes = props.g_atlasIndexes
                        .g_atlasSizes = props.g_atlasSizes
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
            Application.DoEvents() 'stop freezing the UI

        Next

        materials = Nothing

        MapGL.Buffers.materials = CreateBuffer(BufferTarget.ShaderStorageBuffer, "materials")
        BufferStorage(MapGL.Buffers.materials,
                      materialsData.Length * Marshal.SizeOf(Of GLMaterial),
                      materialsData,
                      BufferStorageFlags.None)
        MapGL.Buffers.materials.BindBase(3)
    End Sub

    Private Sub draw_test_iamge(w As Integer, h As Integer, id As GLTexture)

        Dim ww = frmMain.glControl_main.ClientRectangle.Width

        Dim ls = (1920.0F - ww) / 2.0F

        ' Draw Terra Image
        draw_image_rectangle(New RectangleF(0, 0, w, h), id)

        frmMain.glControl_main.SwapBuffers()
    End Sub

    Private Function get_spaceBin(ABS_NAME As String) As Boolean
        Dim space_bin_file = ResMgr.Lookup(String.Format("spaces/{0}/space.bin", ABS_NAME))
        Dim ms As New MemoryStream
        space_bin_file.Extract(ms)
        If ms IsNot Nothing Then
            If Not ReadSpaceBinData(ms) Then
                space_bin_file = Nothing
                MsgBox("Error decoding Space.bin", MsgBoxStyle.Exclamation, "File Error...")
                Return False
            End If
            space_bin_file = Nothing
        Else
            space_bin_file = Nothing
            MsgBox("Unable to load Space.bin from package", MsgBoxStyle.Exclamation, "File Error...")
            Return False
        End If
        Return True
    End Function

    Public Sub remove_map_data()
        'Used to delete all images and display lists.

        MapMenuScreen.Invalidate()

        PICK_DICTIONARY.Clear()
        'Remove map related textures. Keep Static Textures!
        Dim img_id = GL.GenTexture()
        For i = FIRST_UNUSED_TEXTURE To img_id
            Dim imgHandle = GL.Arb.GetTextureHandle(i)
            If imgHandle > 0 Then 'trap error
                If GL.Arb.IsTextureHandleResident(imgHandle) Then
                    GL.Arb.MakeTextureHandleNonResident(imgHandle)
                End If
            End If
            GL.DeleteTexture(i)
        Next

        'delete VBOs
        Dim Lvb As Integer
        GL.GenBuffers(1, Lvb)
        For i = FIRST_UNUSED_V_BUFFER To Lvb
            GL.DeleteBuffer(i)
        Next

        'delete VAOs
        Dim Lvbo As Integer
        GL.GenVertexArrays(1, Lvbo)
        For i = FIRST_UNUSED_VB_OBJECT To Lvbo
            GL.DeleteVertexArray(i)
        Next

        theMap.MINI_MAP_ID = Nothing
        theMap.chunks = Nothing

        GC.Collect()
        GC.WaitForFullGCComplete()

        'Clear texture cache so we dont returned non-existent textures.
        imgTbl.Clear()

        vt = Nothing
        vtInfo = Nothing
        feedback = Nothing

        GC.Collect()
        GC.WaitForFullGCComplete()
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce
    End Sub


End Module

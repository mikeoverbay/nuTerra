﻿Imports System.IO
Imports System.Runtime.InteropServices
Imports Ionic.Zip
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module MapLoader
    'putting these GLobals here because they are tightly related to MapLoader
    Public SHARED_PART_1 As ZipFile
    Public SHARED_PART_2 As ZipFile
    Public SAND_BOX_PART_1 As ZipFile
    Public SAND_BOX_PART_2 As ZipFile

    Public SHARED_PART_1_HD As ZipFile
    Public SHARED_PART_2_HD As ZipFile
    Public SAND_BOX_PART_1_HD As ZipFile
    Public SAND_BOX_PART_2_HD As ZipFile

    Public MAP_PACKAGE As ZipFile
    Public MAP_PACKAGE_HD As ZipFile
    Public MAP_PARTICLES As ZipFile
    Public GUI_PACKAGE As ZipFile
    Public GUI_PACKAGE_PART2 As ZipFile
    'stores what .PKG a model, visual, primtive, atlas_processed or texture is located.
    Public PKG_DATA_TABLE As New DataTable("items")

    Public LODMAPSIZE As Integer = 256
    Public AOMAPSIZE As Integer = 256
    Public HEIGHTMAPSIZE As Integer = 64
    Public NORMALMAPSIZE As Integer = 256
    Public HOLEMAPSIZE As Integer = 64
    Public SHADOWMAPSIZE As Integer = 64
    Public BLENDMAPSIZE As Integer = 256

    '-----------------------------------
    'This stores all models used on a map
    Public MAP_MODELS(1) As mdl_
    'GL-buffers
    Public textureHandleBuffer As Integer
    Public parametersBuffer As Integer
    Public matricesBuffer As Integer
    Public indirectDrawCount As Integer
    Public drawCandidatesBuffer As Integer
    Public indirectBuffer As Integer
    Public vertsBuffer As Integer
    Public vertsUV2Buffer As Integer
    Public primsBuffer As Integer
    Public vertexArray As Integer

    Public Structure mdl_
        Public mdl As base_model_holder_
        Public visibilityBounds As Matrix2x3
    End Structure

#Region "utility functions"

    Public Sub load_lookup_xml()
        PKG_DATA_TABLE.Clear()
        PKG_DATA_TABLE.Columns.Add("filename", GetType(String))
        PKG_DATA_TABLE.Columns.Add("package", GetType(String))
        PKG_DATA_TABLE.ReadXml(Application.StartupPath + "\data\TheItemList.xml")
    End Sub

    Public Function search_pkgs(ByVal filename As String) As ZipEntry
        Dim entry As ZipEntry = Nothing
        If HD_EXISTS And USE_HD_TEXTURES Then
            'look in HD shared package files
            'check map pkg first
            entry = MAP_PACKAGE_HD(filename)
            If entry Is Nothing Then
                entry = SHARED_PART_1_HD(filename)
                If entry Is Nothing Then
                    entry = SHARED_PART_2_HD(filename)
                    If entry Is Nothing Then
                        entry = SAND_BOX_PART_1_HD(filename)
                        If entry Is Nothing Then
                            entry = SAND_BOX_PART_2_HD(filename)
                        End If
                    End If
                End If
            End If
            If entry IsNot Nothing Then
                Return entry
            End If
        End If
        'look in SD shared package files
        'check map pkg first
        entry = MAP_PACKAGE(filename)
        If entry Is Nothing Then
            entry = SHARED_PART_1(filename)
            If entry Is Nothing Then
                entry = SHARED_PART_2(filename)
                If entry Is Nothing Then
                    entry = SAND_BOX_PART_1(filename)
                    If entry Is Nothing Then
                        entry = SAND_BOX_PART_2(filename)
                        If entry Is Nothing Then
                            entry = MAP_PARTICLES(filename)
                        End If
                    End If
                End If
            End If
        End If
        If entry IsNot Nothing Then
            Return entry
        End If

        entry = GUI_PACKAGE(filename)
        If entry IsNot Nothing Then
            Return entry
        End If
        entry = GUI_PACKAGE_PART2(filename)
        If entry IsNot Nothing Then
            Return entry
        End If
        'We still have not found it so lets search the XML datatable.
        Dim pn = search_xml(filename)
        If pn = "" Then
            'Stop ' didnt find it
            Return Nothing
        End If
        Using zip As ZipFile = ZipFile.Read(Path.Combine(GAME_PATH, pn))
            entry = zip(filename)
            If entry Is Nothing Then
                Return Nothing
            End If
            Return entry
        End Using
    End Function

    Public Function search_xml(ByVal filename) As String
        'Searches the PKG_DATA_TABLE xml item to get the package its located in.
        If filename.Length = 0 Then
            Return ""
        End If
        Dim q = From d In PKG_DATA_TABLE.AsEnumerable
                Where d.Field(Of String)("filename").Contains(filename)
                Select
                pkg = d.Field(Of String)("package"),
                file = d.Field(Of String)("filename")

        If q.Count = 0 Then
            Return ""
        End If
        For Each item In q
            If item.file.Contains("lod0") Then
                Return item.pkg
            End If
        Next
        'If we land here, the file we are looking for
        'is not in a LOD folder so return the only item found.
        Return q(0).pkg

    End Function

    Private Sub open_packages()
        'Opens the shared and selected map packages.
        'Can we put these in virtual memory files? Is there a reason?

        'Check if there is HD content on the users disc.
        HD_EXISTS = File.Exists(Path.Combine(GAME_PATH, "shared_content_hd-part1.pkg"))
        If HD_EXISTS Then
            MAP_PACKAGE_HD = ZipFile.Read(Path.Combine(GAME_PATH, MAP_NAME_NO_PATH.Replace(".pkg", "_hd.pkg")))
            SHARED_PART_1_HD = New ZipFile(Path.Combine(GAME_PATH, "shared_content_hd-part1.pkg"))
            SHARED_PART_2_HD = New ZipFile(Path.Combine(GAME_PATH, "shared_content_hd-part2.pkg"))
            SAND_BOX_PART_1_HD = New ZipFile(Path.Combine(GAME_PATH, "shared_content_sandbox_hd-part1.pkg"))
            SAND_BOX_PART_2_HD = New ZipFile(Path.Combine(GAME_PATH, "shared_content_sandbox_hd-part2.pkg"))
        End If

        'open map pkg file
        MAP_PACKAGE = New ZipFile(Path.Combine(GAME_PATH, MAP_NAME_NO_PATH))

        SHARED_PART_1 = New ZipFile(Path.Combine(GAME_PATH, "shared_content-part1.pkg"))
        SHARED_PART_2 = New ZipFile(Path.Combine(GAME_PATH, "shared_content-part2.pkg"))
        SAND_BOX_PART_1 = New ZipFile(Path.Combine(GAME_PATH, "shared_content_sandbox-part1.pkg"))
        SAND_BOX_PART_2 = New ZipFile(Path.Combine(GAME_PATH, "shared_content_sandbox-part2.pkg"))

        MAP_PARTICLES = New ZipFile(Path.Combine(GAME_PATH, "particles.pkg"))
    End Sub

    Public Sub close_shared_packages()
        'Disposes of the loaded packages.
        If HD_EXISTS Then
            SHARED_PART_1_HD.Dispose()
            SHARED_PART_2_HD.Dispose()
            SAND_BOX_PART_1_HD.Dispose()
            SAND_BOX_PART_2_HD.Dispose()

            SHARED_PART_1_HD = Nothing
            SHARED_PART_2_HD = Nothing
            SAND_BOX_PART_1_HD = Nothing
            SAND_BOX_PART_2_HD = Nothing
        End If

        MAP_PACKAGE.Dispose()
        SHARED_PART_1.Dispose()
        SHARED_PART_2.Dispose()
        SAND_BOX_PART_1.Dispose()
        SAND_BOX_PART_2.Dispose()
        MAP_PARTICLES.Dispose()

        MAP_PACKAGE = Nothing
        SHARED_PART_1 = Nothing
        SHARED_PART_2 = Nothing
        SAND_BOX_PART_1 = Nothing
        SAND_BOX_PART_2 = Nothing
        MAP_PARTICLES = Nothing

        'Tell the grabage collector to clean up
        GC.Collect()
        'Wait for the garbage collector to finish cleaning up
        GC.WaitForFullGCComplete()
    End Sub

#End Region

    Public Sub load_map(ByVal package_name As String)

        MAP_LOADED = False
        SHOW_MAPS_SCREEN = False
        BG_MAX_VALUE = 0

        'SHOW_CURSOR = True

        SHOW_LOADING_SCREEN = True
        'For now, we are going to hard wire this name
        'and call this at startup so skip having to select a menu
        MAP_NAME_NO_PATH = package_name
        Dim ABS_NAME = MAP_NAME_NO_PATH.Replace(".pkg", "")

        'First we need to remove the loaded data.
        '===============================================================
        remove_map_data() '=============================================
        '===============================================================
        'House Keeping
        TOTAL_TRIANGLES_DRAWN = 0
        make_cube()
        '===============================================================
        'get the light settings for this map.
        frmLighting.get_light_settings()
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
        'Get block state of things we want to block loading to speed things up for testing/debugging
        DONT_BLOCK_BASES = My.Settings.load_bases
        DONT_BLOCK_DECALS = My.Settings.load_decals
        DONT_BLOCK_MODELS = My.Settings.load_models
        DONT_BLOCK_SKY = My.Settings.load_sky
        DONT_BLOCK_TERRAIN = My.Settings.load_terrain
        DONT_BLOCK_TREES = My.Settings.load_trees
        DONT_BLOCK_WATER = My.Settings.load_water
        '===============================================================

        '===============================================================
        'we need to load the packages. This also opens the Map pkg we selected.
        open_packages()
        '===============================================================

        '===============================================================
        'load test textures
        N_MAP_TYPE = 1 ' has to be set for the ANM Green alpha normal maps.
        m_normal_id = find_and_load_texture_from_pkgs("maps/landscape/detail/sand_NM.dds")
        m_color_id = load_image_from_file(Il.IL_PNG, Application.StartupPath + "\resources\ref_colorMap.png", True, False)
        m_gmm_id = find_and_load_texture_from_pkgs("content/Buildings/bld_19_04_Ampitheratre/bld_19_04_Ampitheratre_GMM.dds")
        '===============================================================

        '===============================================================
        'Open the space.bin file. If it fails, it closes all packages and lets the user know.
        If Not get_spaceBin(ABS_NAME) Then
            MsgBox("Failed to load Space.Bin from the map package.", MsgBoxStyle.Exclamation, "Space.bin!")
            Return
        End If
        '===============================================================

        '===============================================================
        If DONT_BLOCK_MODELS Then

            ' Setup Bar graph
            BG_TEXT = "Loading Models..."
            BG_MAX_VALUE = MAP_MODELS.Length - 1

            For i = 0 To MAP_MODELS.Length - 1
                BG_VALUE = i
                If Not MAP_MODELS(i).mdl.junk Then
                    Application.DoEvents() '<-- Give some time to this app's UI
                    Dim good = get_primitive(MAP_MODELS(i).mdl)
                End If
                draw_scene()
            Next

            '----------------------------------------------------------------
            ' calc instances
            Dim numMatrices = 0
            Dim numVerts = 0
            Dim numPrims = 0
            indirectDrawCount = 0
            For Each batch In MODEL_BATCH_LIST
                Dim model = MAP_MODELS(batch.model_id).mdl

                If model.junk Then
                    Continue For
                End If

                Dim skip = True
                For Each renderSet In model.render_sets
                    If renderSet.no_draw Then
                        Continue For
                    End If
                    For Each primGroup In renderSet.primitiveGroups.Values
                        If primGroup.no_draw Then
                            Continue For
                        End If
                        indirectDrawCount += batch.count
                        skip = False
                    Next
                    numVerts += renderSet.buffers.vertexBuffer.Length
                    numPrims += renderSet.buffers.index_buffer32.Length
                Next
                If Not skip Then
                    numMatrices += batch.count
                End If
            Next

            '----------------------------------------------------------------
            ' setup instances
            Dim drawCommands(indirectDrawCount - 1) As CandidateDraw
            Dim vBuffer(numVerts - 1) As ModelVertex
            Dim uv2Buffer(numVerts - 1) As Vector2
            Dim iBuffer(numPrims - 1) As vect3_32
            Dim matrices(numMatrices - 1) As Matrix4
            Dim cmdId = 0
            Dim vLast = 0
            Dim iLast = 0
            Dim mLast = 0
            Dim baseVert = 0
            For Each batch In MODEL_BATCH_LIST
                Dim model = MAP_MODELS(batch.model_id).mdl

                If model.junk Then
                    Continue For
                End If

                Dim skip = True
                For Each renderSet In model.render_sets
                    If renderSet.no_draw Then
                        Continue For
                    End If
                    For Each primGroup In renderSet.primitiveGroups.Values
                        If primGroup.no_draw Then
                            Continue For
                        End If
                        For i = 0 To batch.count - 1
                            With drawCommands(cmdId)
                                .visibilityBox1 = MAP_MODELS(batch.model_id).visibilityBounds.Row0
                                .visibilityBox1.X *= -1 'make negative because of GL rendering!
                                .model_id = mLast + i
                                .visibilityBox2 = MAP_MODELS(batch.model_id).visibilityBounds.Row1
                                .visibilityBox2.X *= -1 'make negative because of GL rendering!
                                .material_id = primGroup.material_id
                                .count = primGroup.nPrimitives * 3
                                .firstIndex = iLast * 3 + primGroup.startIndex
                                .baseVertex = baseVert
                                .baseInstance = cmdId
                            End With
                            cmdId += 1
                        Next
                        skip = False
                    Next

                    baseVert += renderSet.numVertices

                    renderSet.buffers.vertexBuffer.CopyTo(vBuffer, vLast)
                    renderSet.buffers.index_buffer32.CopyTo(iBuffer, iLast)
                    If renderSet.buffers.uv2 IsNot Nothing Then
                        renderSet.buffers.uv2.CopyTo(uv2Buffer, vLast)
                        Erase renderSet.buffers.uv2
                    End If

                    vLast += renderSet.buffers.vertexBuffer.Length
                    iLast += renderSet.buffers.index_buffer32.Length

                    Erase renderSet.buffers.vertexBuffer
                    Erase renderSet.buffers.index_buffer32
                Next

                If Not skip Then
                    For i = 0 To batch.count - 1
                        matrices(mLast + i) = MODEL_INDEX_LIST(batch.offset + i).matrix
                    Next
                    mLast += batch.count
                End If
            Next

            GL.CreateBuffers(1, parametersBuffer)
            GL.NamedBufferStorage(parametersBuffer, 256, IntPtr.Zero, BufferStorageFlags.None)

            GL.CreateBuffers(1, drawCandidatesBuffer)
            GL.NamedBufferStorage(drawCandidatesBuffer, indirectDrawCount * Marshal.SizeOf(Of CandidateDraw)(), drawCommands, BufferStorageFlags.None)
            Erase drawCommands

            GL.CreateBuffers(1, indirectBuffer)
            GL.NamedBufferStorage(indirectBuffer, indirectDrawCount * Marshal.SizeOf(Of DrawElementsIndirectCommand)(), IntPtr.Zero, BufferStorageFlags.None)

            GL.CreateBuffers(1, matricesBuffer)
            GL.NamedBufferStorage(matricesBuffer, matrices.Length * Marshal.SizeOf(Of Matrix4)(), matrices, BufferStorageFlags.None)
            Erase matrices

            GL.CreateBuffers(1, vertsBuffer)
            GL.NamedBufferStorage(vertsBuffer, vBuffer.Length * Marshal.SizeOf(Of ModelVertex)(), vBuffer, BufferStorageFlags.None)
            Erase vBuffer

            GL.CreateBuffers(1, vertsUV2Buffer)
            GL.NamedBufferStorage(vertsUV2Buffer, uv2Buffer.Length * Marshal.SizeOf(Of Vector2)(), uv2Buffer, BufferStorageFlags.None)
            Erase uv2Buffer

            GL.CreateBuffers(1, primsBuffer)
            GL.NamedBufferStorage(primsBuffer, iBuffer.Length * Marshal.SizeOf(Of vect3_32)(), iBuffer, BufferStorageFlags.None)
            Erase iBuffer

            GL.CreateVertexArrays(1, vertexArray)

            'pos
            GL.VertexArrayVertexBuffer(vertexArray, 0, vertsBuffer, New IntPtr(0), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(vertexArray, 0, 3, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 0, 0)
            GL.EnableVertexArrayAttrib(vertexArray, 0)

            'normal
            GL.VertexArrayVertexBuffer(vertexArray, 1, vertsBuffer, New IntPtr(12), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(vertexArray, 1, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 1, 1)
            GL.EnableVertexArrayAttrib(vertexArray, 1)

            'tangent
            GL.VertexArrayVertexBuffer(vertexArray, 2, vertsBuffer, New IntPtr(20), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(vertexArray, 2, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 2, 2)
            GL.EnableVertexArrayAttrib(vertexArray, 2)

            'binormal
            GL.VertexArrayVertexBuffer(vertexArray, 3, vertsBuffer, New IntPtr(28), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(vertexArray, 3, 4, VertexAttribType.HalfFloat, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 3, 3)
            GL.EnableVertexArrayAttrib(vertexArray, 3)

            'uv
            GL.VertexArrayVertexBuffer(vertexArray, 4, vertsBuffer, New IntPtr(36), Marshal.SizeOf(Of ModelVertex))
            GL.VertexArrayAttribFormat(vertexArray, 4, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 4, 4)
            GL.EnableVertexArrayAttrib(vertexArray, 4)

            'uv2
            GL.VertexArrayVertexBuffer(vertexArray, 5, vertsUV2Buffer, IntPtr.Zero, Marshal.SizeOf(Of Vector2))
            GL.VertexArrayAttribFormat(vertexArray, 5, 2, VertexAttribType.Float, False, 0)
            GL.VertexArrayAttribBinding(vertexArray, 5, 5)
            GL.EnableVertexArrayAttrib(vertexArray, 5)

            GL.VertexArrayElementBuffer(vertexArray, primsBuffer)

            load_materials()

            MODELS_LOADED = True
        End If ' block DONT_BLOCK_MODELS laoded
        '===============================================================


        '===============================================================
        'As it says.. create the terrain
        If DONT_BLOCK_TERRAIN Then

            Create_Terrain()
            TERRAIN_LOADED = True
            PLAYER_FIELD_CELL_SIZE = Math.Abs(MAP_BB_BL.X - MAP_BB_UR.X) / 10.0F
            'TO DO and there is lots
        End If 'DONT_BLOCK_TERRAIN
        '===============================================================


        '=======================================================
        'Stop Here for now =====================================
        '=======================================================

        MAP_LOADED = True

        '===============================================================
        'We need to get the Y location of the rings and stop drawing overly tall cubes.
        'It only needs to happen once!
        T1_Y = get_Y_at_XZ(-TEAM_1.X, TEAM_1.Z)
        T2_Y = get_Y_at_XZ(-TEAM_2.X, TEAM_2.Z)
        '===============================================================


        SHOW_LOADING_SCREEN = False
        'LOOK_AT_X = 0.001
        'LOOK_AT_Z = 0.001
        frmMain.check_postion_for_update() ' need to initialize crusor altitude

        'Maintains constant grow shrink regardless of frame rate.

        ' close packages
        close_shared_packages()
    End Sub

    Private Structure AtlasCoords
        Dim x0 As Int32
        Dim x1 As Int32
        Dim y0 As Int32
        Dim y1 As Int32
        Dim path As String
    End Structure

    Private Structure AtlasCfg
        Dim width As Int32
        Dim height As Int32
        Dim atlas_tex As Integer
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

                Case ShaderTypes.FX_PBS_tiled_atlas
                    atlasPaths.Add(mat.props.atlasAlbedoHeight)
                    Debug.Assert(mat.props.atlasBlend.EndsWith(".png"))
                    mat.props.atlasBlend = mat.props.atlasBlend.Replace(".png", ".dds") 'hack!!!
                    texturePaths.Add(mat.props.atlasBlend)
                    atlasPaths.Add(mat.props.atlasNormalGlossSpec)
                    atlasPaths.Add(mat.props.atlasMetallicAO)
                    atlasPaths.Add(mat.props.dirtMap)

                Case ShaderTypes.FX_lightonly_alpha
                    texturePaths.Add(mat.props.diffuseMap)

                Case Else
                    'Stop
            End Select
        Next

        'load atlases
        Dim textureHandles As New Dictionary(Of String, UInt64)
        For Each atlasPath In atlasPaths
            If atlasPath.EndsWith(".dds") Then
                texturePaths.Add(atlasPath)
                Continue For
            End If

            If Not atlasPath.EndsWith(".atlas") Then
                'Stop
                Continue For
            End If

            Dim entry As ZipEntry = search_pkgs(atlasPath + "_processed")
            If entry Is Nothing Then
                Stop
                Continue For
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)

            ms.Position = 0
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
                Dim dds_header_pos = br.BaseStream.Position
                Dim dds_header = get_dds_header(br)
                ms.Position = dds_header_pos + 128

                Dim atlas_tex As Integer
                GL.CreateTextures(TextureTarget.Texture2D, 1, atlas_tex)
                Debug.Assert(dds_header.mipMapCount = 0)
                GL.TextureStorage2D(atlas_tex, 1, DirectCast(dds_header.gl_format, SizedInternalFormat), dds_header.width, dds_header.height)

                Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * dds_header.gl_block_size
                Dim data = br.ReadBytes(size)
                GL.CompressedTextureSubImage2D(atlas_tex, 0, 0, 0, dds_header.width, dds_header.height, DirectCast(dds_header.gl_format, OpenGL.PixelFormat), size, data)

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
                End While

                Dim handle = GL.Arb.GetTextureHandle(atlas_tex)
                GL.Arb.MakeTextureHandleResident(handle)

                textureHandles(atlasPath) = handle
            End Using
        Next

        'load textures
        For Each texturePath In texturePaths
            If Not texturePath.EndsWith(".dds") Then
                Stop
                Continue For
            End If

            Dim entry As ZipEntry = search_pkgs(texturePath)
            If entry Is Nothing Then
                Stop
                Continue For
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)

            Dim tex = load_dds_image_from_stream(ms, texturePath)

            Dim handle = GL.Arb.GetTextureHandle(tex)
            GL.Arb.MakeTextureHandleResident(handle)

            textureHandles(texturePath) = handle
        Next

        Dim materialsData(materials.Count - 1) As GLMaterial
        For Each mat In materials.Values
            With materialsData(mat.id)
                .shader_type = mat.shader_type
                Select Case mat.shader_type
                    Case ShaderTypes.FX_PBS_ext
                        .map1Handle = textureHandles(mat.props.diffuseMap)
                        .map2Handle = textureHandles(mat.props.normalMap)
                        .map3Handle = textureHandles(mat.props.metallicGlossMap)
                        .g_useNormalPackDXT1 = mat.props.g_useNormalPackDXT1
                        .alphaReference = mat.props.alphaReference / 255.0
                        .alphaTestEnable = mat.props.alphaTestEnable
                        .g_colorTint = mat.props.g_colorTint
                        .g_useColorTint = mat.props.g_useTintColor

                    Case ShaderTypes.FX_PBS_ext_dual
                        .map1Handle = textureHandles(mat.props.diffuseMap)
                        .map2Handle = textureHandles(mat.props.normalMap)
                        .map3Handle = textureHandles(mat.props.metallicGlossMap)
                        .map4Handle = textureHandles(mat.props.diffuseMap2)
                        .g_useNormalPackDXT1 = mat.props.g_useNormalPackDXT1
                        .alphaReference = mat.props.alphaReference / 255.0
                        .alphaTestEnable = mat.props.alphaTestEnable
                        '.g_colorTint = mat.props.g_colorTint
                        '.g_useColorTint = mat.props.g_useTintColor

                    Case ShaderTypes.FX_PBS_ext_detail
                        .map1Handle = textureHandles(mat.props.diffuseMap)
                        .map2Handle = textureHandles(mat.props.normalMap)
                        .map3Handle = textureHandles(mat.props.metallicGlossMap)
                        .g_useNormalPackDXT1 = mat.props.g_useNormalPackDXT1
                        .alphaReference = mat.props.alphaReference / 255.0
                        .alphaTestEnable = mat.props.alphaTestEnable
                        .g_colorTint = mat.props.g_colorTint
                        .g_useColorTint = mat.props.g_useTintColor

                    Case ShaderTypes.FX_PBS_tiled_atlas
                        .map1Handle = textureHandles(mat.props.atlasAlbedoHeight)
                        .map2Handle = textureHandles(mat.props.atlasBlend)
                        .map3Handle = textureHandles(mat.props.atlasNormalGlossSpec)
                        .map4Handle = textureHandles(mat.props.atlasMetallicAO)
                        .g_atlasIndexes = mat.props.g_atlasIndexes
                        .g_atlasSizes = mat.props.g_atlasSizes
                        If mat.props.dirtMap <> "unused" Then
                            '.map5Handle = textureHandles(mat.props.dirtMap)

                        End If
                        .dirtColor = mat.props.dirtColor
                        .dirtParams = mat.props.dirtParams
                        .g_tile0Tint = mat.props.g_tile0Tint
                        .g_tile1Tint = mat.props.g_tile2Tint
                        .g_tile1Tint = mat.props.g_tile2Tint

                    Case ShaderTypes.FX_lightonly_alpha
                        .map1Handle = textureHandles(mat.props.diffuseMap)

                    Case Else
                        'Stop
                End Select
            End With
        Next

        materials = Nothing

        GL.CreateBuffers(1, textureHandleBuffer)
        GL.NamedBufferStorage(textureHandleBuffer, materialsData.Length * Marshal.SizeOf(Of GLMaterial), materialsData, BufferStorageFlags.None)
    End Sub

    Private Function get_spaceBin(ByVal ABS_NAME As String) As Boolean
        Dim space_bin_file As Ionic.Zip.ZipEntry =
            MAP_PACKAGE(Path.Combine("spaces", ABS_NAME, "space.bin"))
        If space_bin_file IsNot Nothing Then
            ' This is all new code -------------------
            Try

                If File.Exists(Path.Combine(TEMP_STORAGE, space_bin_file.FileName)) Then
                    File.Delete(Path.Combine(TEMP_STORAGE, space_bin_file.FileName))
                End If
                space_bin_file.Extract(TEMP_STORAGE, ExtractExistingFileAction.OverwriteSilently)

            Catch ex As Exception

            End Try
            If Not ReadSpaceBinData(space_bin_file.FileName) Then
                space_bin_file = Nothing
                MsgBox("Error decoding Space.bin", MsgBoxStyle.Exclamation, "File Error...")
                close_shared_packages()
                Return False
            End If
            space_bin_file = Nothing
        Else
            space_bin_file = Nothing
            MsgBox("Unable to load Space.bin", MsgBoxStyle.Exclamation, "File Error...")
            close_shared_packages()
            Return False
        End If
        'first, we clear out the previous map data
        Return True
    End Function

    Public Sub remove_map_data()
        'Used to delete all images and display lists.

        'Remove map related textures. Keep Static Textures!
        Dim img_id = GL.GenTexture
        For i = FIRST_UNUSED_TEXTURE To img_id
            GL.DeleteTexture(i)
            GL.Finish() ' make sure we are done before moving on
        Next

        'delete VBOs
        Dim Lvb As Integer
        GL.GenBuffers(1, Lvb)
        For i = FIRST_UNUSED_V_BUFFER To Lvb
            GL.DeleteBuffer(i)
        Next
        GL.Finish() ' make sure we are done before moving on

        'delete VAOs
        Dim Lvbo As Integer
        GL.GenVertexArrays(1, Lvbo)
        For i = FIRST_UNUSED_VB_OBJECT To Lvbo
            GL.DeleteVertexArray(i)
        Next
        GL.Finish() ' make sure we are done before moving on

        theMap.MINI_MAP_ID = 0
        theMap.chunks = Nothing
        theMap.vertex_vBuffer_id = 0

        GC.Collect()
        GC.WaitForFullGCComplete()

        'Clear texture cache so we dont returned non-existent textures.
        imgTbl.Clear()


    End Sub


End Module

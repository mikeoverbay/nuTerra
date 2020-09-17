Imports System.IO
Imports System.Runtime.InteropServices.Marshal
Imports Ionic.Zip
Imports OpenTK
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

    Public DESTRUCTABLE_DATA_TABLE As DataTable

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
    Public Structure mdl_
        Public mdl As base_model_holder_
        Public visibilityBounds As Matrix3x2
    End Structure

    ' Just for loading a model to test.
    Public mdl() As base_model_holder_


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
        If File.Exists(Path.Combine(GAME_PATH, "shared_content_hd-part1.pkg")) Then
            HD_EXISTS = True
        Else
            HD_EXISTS = False
        End If
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
            ' setup instances
            For Each batch In MODEL_BATCH_LIST
                Dim model = MAP_MODELS(batch.model_id).mdl

                If model.junk Then
                    Continue For
                End If

                Dim modelMatrices(batch.count - 1) As Matrix4
                For i = 0 To batch.count - 1
                    modelMatrices(i) = MODEL_INDEX_LIST(batch.offset + i).matrix
                Next

                GL.CreateVertexArrays(1, batch.cullVA)
                GL.CreateBuffers(1, batch.instanceDataBO)

                GL.NamedBufferData(batch.instanceDataBO, batch.count * SizeOf(GetType(Matrix4)), modelMatrices, BufferUsageHint.StaticDraw)
                GL.VertexArrayVertexBuffer(batch.cullVA, 0, batch.instanceDataBO, New IntPtr(0 * 16), 4 * 16)
                GL.VertexArrayVertexBuffer(batch.cullVA, 1, batch.instanceDataBO, New IntPtr(1 * 16), 4 * 16)
                GL.VertexArrayVertexBuffer(batch.cullVA, 2, batch.instanceDataBO, New IntPtr(2 * 16), 4 * 16)
                GL.VertexArrayVertexBuffer(batch.cullVA, 3, batch.instanceDataBO, New IntPtr(3 * 16), 4 * 16)

                GL.VertexArrayAttribFormat(batch.cullVA, 0, 4, VertexAttribType.Float, False, 0)
                GL.VertexArrayAttribFormat(batch.cullVA, 1, 4, VertexAttribType.Float, False, 0)
                GL.VertexArrayAttribFormat(batch.cullVA, 2, 4, VertexAttribType.Float, False, 0)
                GL.VertexArrayAttribFormat(batch.cullVA, 3, 4, VertexAttribType.Float, False, 0)

                GL.EnableVertexArrayAttrib(batch.cullVA, 0)
                GL.EnableVertexArrayAttrib(batch.cullVA, 1)
                GL.EnableVertexArrayAttrib(batch.cullVA, 2)
                GL.EnableVertexArrayAttrib(batch.cullVA, 3)

                GL.CreateBuffers(1, batch.culledInstanceDataBO)
                GL.NamedBufferData(batch.culledInstanceDataBO, batch.count * SizeOf(GetType(Matrix4)), IntPtr.Zero, BufferUsageHint.DynamicCopy)

                GL.CreateQueries(QueryTarget.PrimitivesGenerated, 1, batch.culledQuery)

                For Each renderSet In model.render_sets
                    If renderSet.no_draw Then
                        Continue For
                    End If

                    GL.VertexArrayVertexBuffer(renderSet.mdl_VAO, 6, batch.culledInstanceDataBO, New IntPtr(0 * 16), 4 * 16)
                    GL.VertexArrayVertexBuffer(renderSet.mdl_VAO, 7, batch.culledInstanceDataBO, New IntPtr(1 * 16), 4 * 16)
                    GL.VertexArrayVertexBuffer(renderSet.mdl_VAO, 8, batch.culledInstanceDataBO, New IntPtr(2 * 16), 4 * 16)
                    GL.VertexArrayVertexBuffer(renderSet.mdl_VAO, 9, batch.culledInstanceDataBO, New IntPtr(3 * 16), 4 * 16)

                    GL.VertexArrayAttribFormat(renderSet.mdl_VAO, 6, 4, VertexAttribType.Float, False, 0)
                    GL.VertexArrayAttribFormat(renderSet.mdl_VAO, 7, 4, VertexAttribType.Float, False, 0)
                    GL.VertexArrayAttribFormat(renderSet.mdl_VAO, 8, 4, VertexAttribType.Float, False, 0)
                    GL.VertexArrayAttribFormat(renderSet.mdl_VAO, 9, 4, VertexAttribType.Float, False, 0)

                    GL.EnableVertexArrayAttrib(renderSet.mdl_VAO, 6)
                    GL.EnableVertexArrayAttrib(renderSet.mdl_VAO, 7)
                    GL.EnableVertexArrayAttrib(renderSet.mdl_VAO, 8)
                    GL.EnableVertexArrayAttrib(renderSet.mdl_VAO, 9)

                    GL.VertexArrayBindingDivisor(renderSet.mdl_VAO, 6, 1)
                    GL.VertexArrayBindingDivisor(renderSet.mdl_VAO, 7, 1)
                    GL.VertexArrayBindingDivisor(renderSet.mdl_VAO, 8, 1)
                    GL.VertexArrayBindingDivisor(renderSet.mdl_VAO, 9, 1)
                Next
            Next

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

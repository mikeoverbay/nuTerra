Imports System.IO
Imports Ionic.Zip
Imports OpenTK.Graphics.OpenGL


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

    'stores what .PKG a model, visual, primtive, atlas_processed or texture is located.
    Public PKG_DATA_TABLE As New DataTable("items")

    Public DESTRUCTABLE_DATA_TABLE As DataTable
    Public SKYDOMENAME As String = ""

    Dim contents As New List(Of String)

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
        Public mdl() As base_model_holder_
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
                    End If
                End If
            End If
        End If
        If entry IsNot Nothing Then
            Return entry
        End If
        'We still have not found it so lets search the XML datatable.
        Dim pn = search_xml(filename)
        If pn = "" Then
            'Stop ' didnt find it
            Return Nothing
        End If
        Using zip As ZipFile = ZipFile.Read(GAME_PATH + pn)
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
        Dim q = From d In PKG_DATA_TABLE.AsEnumerable _
                Where d.Field(Of String)("filename").ToLower.Contains(filename.ToLower) _
                Select _
                pkg = d.Field(Of String)("package"), _
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
        If File.Exists(GAME_PATH + "shared_content_hd-part1.pkg") Then
            HD_EXISTS = True
        Else
            HD_EXISTS = False
        End If
        If HD_EXISTS Then
            MAP_PACKAGE_HD = ZipFile.Read(GAME_PATH + MAP_NAME_NO_PATH.Replace(".pkg", "_hd.pkg"))
            SHARED_PART_1_HD = New ZipFile(GAME_PATH + "shared_content_hd-part1.pkg")
            SHARED_PART_2_HD = New ZipFile(GAME_PATH + "shared_content_hd-part2.pkg")
            SAND_BOX_PART_1_HD = New ZipFile(GAME_PATH + "shared_content_sandbox_hd-part1.pkg")
            SAND_BOX_PART_2_HD = New ZipFile(GAME_PATH + "shared_content_sandbox_hd-part2.pkg")
        End If
        'open map pkg file
        MAP_PACKAGE = New ZipFile(GAME_PATH + MAP_NAME_NO_PATH)

        SHARED_PART_1 = New ZipFile(GAME_PATH + "shared_content-part1.pkg")
        SHARED_PART_2 = New ZipFile(GAME_PATH + "shared_content-part2.pkg")
        SAND_BOX_PART_1 = New ZipFile(GAME_PATH + "shared_content_sandbox-part1.pkg")
        SAND_BOX_PART_2 = New ZipFile(GAME_PATH + "shared_content_sandbox-part2.pkg")

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

        MAP_PACKAGE = Nothing
        SHARED_PART_1 = Nothing
        SHARED_PART_2 = Nothing
        SAND_BOX_PART_1 = Nothing
        SAND_BOX_PART_2 = Nothing

        'Tell the grabage collector to clean up
        GC.Collect()
        'Wait for the garbage collector to finish cleaning up
        GC.WaitForFullGCComplete()
    End Sub

#End Region

    Public Sub load_map(ByVal package_name As String)
        SHOW_MAPS_SCREEN = False
        SHOW_LOADING_SCREEN = True
        'For now, we are going to hard wire this name
        'and call this at startup so skip having to select a menu
        MAP_NAME_NO_PATH = package_name
        Dim ABS_NAME = MAP_NAME_NO_PATH.Replace(".pkg", "")
        'First we need to remove the loaded data.
        remove_map_data()
        TOTAL_TRIANGLES_DRAWN = 0
        'get the light settings for this map.
        frmLighting.get_light_settings()

        'Set draw enable flags
        TERRAIN_LOADED = False
        TREES_LOADED = False
        DECALS_LOADED = False
        MODELS_LOADED = False
        BASES_LOADED = False
        SKY_LOADED = False
        WATER_LOADED = False
        '------------------------------------------------------------------------------------------------
        'we need to load the packages. This also opens the Map pkg we selected.
        open_packages()

        '
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        N_MAP_TYPE = 1 ' has to be set for the ANM Green alpha normal maps.
        '---------------------------------------------------------
        ReDim mdl(1)
        mdl(0) = New base_model_holder_
        m_color_id = find_and_load_texture_from_pkgs("content/Buildings/bld_19_04_Ampitheratre/bld_19_04_Ampitheratre_AM.dds")
        m_normal_id = find_and_load_texture_from_pkgs("content/Buildings/bld_19_04_Ampitheratre/bld_19_04_Ampitheratre_ANM.dds")
        m_gmm_id = find_and_load_texture_from_pkgs("content/Buildings/bld_19_04_Ampitheratre/bld_19_04_Ampitheratre_GMM.dds")
        'get_primitive("content/Buildings/bld_19_04_Ampitheratre/normal/lod0/bld_19_04_Ampitheratre.model", mdl)
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        'test load a model
        'get_primitive("content/Buildings/hd_bld_EU_013_RailroadStation/normal/lod0/hd_bld_EU_013_RailroadStation_02.model", mdl)
        'get_primitive("content/MilitaryEnvironment/hd_mle_SU_005_Mi24A/normal/lod0/hd_mle_SU_005_Mi24A_02.model", mdl)
        'get_primitive("content/Buildings/bld_19_02_Monastery/normal/lod0/bld_19_02_Monastery_05_Chapel.model", mdl)
        'Return

        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        'Open the space.bin file. If it fails, it closes all packages and lets the use know.
        If Not get_spaceBin(ABS_NAME) Then
            MsgBox("Failed to load Space.Bin from the map package.", MsgBoxStyle.Exclamation, "Space.bin!")
            Return
        End If
        '------------------------------------------------------------------------------------------------
        ' Setup Bar graph
        BG_TEXT = "Loading Models..."
        BG_MAX_VALUE = MAP_MODELS.Length - 1

        For i = 0 To MAP_MODELS.Length - 1
            BG_VALUE = i
            If MAP_MODELS(i).mdl(0).primitive_name IsNot Nothing Then
                Application.DoEvents() '<-- Give some time to this app's UI
                Dim good = get_primitive(MAP_MODELS(i).mdl(0).primitive_name.Replace("primitives", "model"), MAP_MODELS(i).mdl)
            End If
        Next
        MODELS_LOADED = True
        'Get a list of all items in the MAP_package
        '=======================================================
        'Stop Here for now =====================================
        '=======================================================

#If 0 Then
        Dim cnt As Integer = 0
        For Each e As ZipEntry In MAP_PACKAGE
            contents.Add(e.FileName)
            cnt += 1
            Application.DoEvents()
        Next
        '------------------------------------------------------------------------------------------------
        ' get settings xml.. this sets map sizes and such
        Dim st As Ionic.Zip.ZipEntry = MAP_PACKAGE("spaces/" & ABS_NAME & "/space.settings")
        Dim settings As New MemoryStream
        st.Extract(settings)
        openXml_stream(settings, ABS_NAME)

        getMapSizes(ABS_NAME) ' this also gets the skydome full path
        '------------------------------------------------------------------------------------------------
#End If
        SHOW_LOADING_SCREEN = False

        ' close packages
        close_shared_packages()
    End Sub

    Private Function get_spaceBin(ByVal ABS_NAME As String) As Boolean
        Dim space_bin_file As Ionic.Zip.ZipEntry = _
            MAP_PACKAGE("spaces/" & ABS_NAME & "/space.bin")
        If space_bin_file IsNot Nothing Then
            ' This is all new code -------------------
            Try

                If File.Exists(TEMP_STORAGE + space_bin_file.FileName) Then
                    File.Delete(TEMP_STORAGE + space_bin_file.FileName)
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

        'remove map loaded textures
        Dim LAST_TEXTURE = GL.GenTexture  'get last texture created.
        Dim t_count = FIRST_UNUSED_TEXTURE - LAST_TEXTURE
        GL.DeleteTextures(t_count, FIRST_UNUSED_TEXTURE)
        GL.Finish() ' make sure we are done before moving on
        Try


            For i = 0 To MAP_MODELS.Length - 1
                If MAP_MODELS(i).mdl IsNot Nothing Then
                    GL.DeleteBuffer(MAP_MODELS(i).mdl(0).mdl_VAO)
                    MAP_MODELS(i).mdl(0).flush()
                    ReDim MAP_MODELS(i).mdl(0).index_buffer16(0)
                    ReDim MAP_MODELS(i).mdl(0).index_buffer32(0)

                End If
            Next
        Catch ex As Exception

        End Try
    End Sub


End Module

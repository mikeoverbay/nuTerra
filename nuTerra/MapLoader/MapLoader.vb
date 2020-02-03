
Imports System.IO
Imports OpenTK.Graphics.OpenGL
Imports Ionic.Zip


Module MapLoader
    'putting these GLobals here because they are tightly related to MapLoader
    Public shared_part_1 As ZipFile
    Public shared_part_2 As ZipFile
    Public sand_box_part_1 As ZipFile
    Public sand_box_part_2 As ZipFile

    Public shared_part_1_hd As ZipFile
    Public shared_part_2_hd As ZipFile
    Public sand_box_part_1_hd As ZipFile
    Public sand_box_part_2_hd As ZipFile

    Public MAP_package As ZipFile

    Public DATA_TABLE As New DataTable("items")
    Public skyDomeName As String = ""

    Dim contents As New List(Of String)

    Public lodMapSize As Integer = 256
    Public aoMapSize As Integer = 256
    Public heightMapSize As Integer = 64
    Public normalMapSize As Integer = 256
    Public holeMapSize As Integer = 64
    Public shadowMapSize As Integer = 64
    Public blendMapsize As Integer = 256

    Public mdl(0) As base_model_holder_


#Region "utility functions"

    Public Sub load_lookup_xml()
        DATA_TABLE.Clear()
        DATA_TABLE.Columns.Add("filename", GetType(String))
        DATA_TABLE.Columns.Add("package", GetType(String))
        DATA_TABLE.ReadXml(Application.StartupPath + "\data\TheItemList.xml")
    End Sub
    Public Function search_pkgs(ByVal filename As String) As ZipEntry
        Dim entry As ZipEntry
        If HD_EXISTS And USE_HD_TEXTURES Then
            'look in HD shared package files
            entry = shared_part_1_hd(filename)
            If entry Is Nothing Then
                entry = shared_part_2_hd(filename)
                If entry Is Nothing Then
                    entry = sand_box_part_1_hd(filename)
                    If entry Is Nothing Then
                        entry = sand_box_part_2_hd(filename)
                    End If
                End If
            End If
            If entry IsNot Nothing Then
                Return entry
            End If
        End If
        'look in SD shared package files
        entry = MAP_package(filename)
        If entry Is Nothing Then
            entry = shared_part_1(filename)
            If entry Is Nothing Then
                entry = shared_part_2(filename)
                If entry Is Nothing Then
                    entry = sand_box_part_1(filename)
                    If entry Is Nothing Then
                        entry = sand_box_part_2(filename)
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
            Stop ' didnt find it
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
        'Searches the DATA_TABLE xml item to get the package its located in.
        If filename.Length = 0 Then
            Return ""
        End If
        Dim q = From d In DATA_TABLE.AsEnumerable _
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
            shared_part_1_hd = New ZipFile
            shared_part_2_hd = New ZipFile
            sand_box_part_1_hd = New ZipFile
            sand_box_part_2_hd = New ZipFile

            shared_part_1_hd = ZipFile.Read(GAME_PATH + "shared_content_hd-part1.pkg")
            shared_part_2_hd = ZipFile.Read(GAME_PATH + "shared_content_hd-part2.pkg")
            sand_box_part_1_hd = ZipFile.Read(GAME_PATH + "shared_content_sandbox_hd-part1.pkg")
            sand_box_part_2_hd = ZipFile.Read(GAME_PATH + "shared_content_sandbox_hd-part2.pkg")
        End If
        'open map pkg file
        MAP_package = New ZipFile
        MAP_package = ZipFile.Read(GAME_PATH + MAP_NAME_NO_PATH)

        shared_part_1 = New ZipFile
        shared_part_2 = New ZipFile
        sand_box_part_1 = New ZipFile
        sand_box_part_2 = New ZipFile

        shared_part_1 = ZipFile.Read(GAME_PATH + "shared_content-part1.pkg")
        shared_part_2 = ZipFile.Read(GAME_PATH + "shared_content-part2.pkg")
        sand_box_part_1 = ZipFile.Read(GAME_PATH + "shared_content_sandbox-part1.pkg")
        sand_box_part_2 = ZipFile.Read(GAME_PATH + "shared_content_sandbox-part2.pkg")

    End Sub

    Public Sub close_shared_packages()
        'Disposes of the loaded packages.
        If HD_EXISTS Then
            shared_part_1_hd.Dispose()
            shared_part_2_hd.Dispose()
            sand_box_part_1_hd.Dispose()
            sand_box_part_2_hd.Dispose()
        End If

        MAP_package.Dispose()

        shared_part_1.Dispose()
        shared_part_2.Dispose()
        sand_box_part_1.Dispose()
        sand_box_part_1.Dispose()

        'Tell the grabage collector to clean up
        GC.Collect()
        'Wait for the garbage collector to finish cleaning up
        GC.WaitForFullGCComplete()
    End Sub

#End Region

    Public Sub load_map(ByVal package_name As String)
        'For now, we are going to hard wire this name
        'and call this at startup so skip having to select a menu
        MAP_NAME_NO_PATH = package_name
        Dim ABS_NAME = MAP_NAME_NO_PATH.Replace(".pkg", "")
        'First we need to remove the loaded data.
        remove_map_data()

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
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        '------------------------------------------------------------------------------------------------
        'test load a model
        get_primitive("content/Buildings/hd_bld_EU_013_RailroadStation/normal/lod0/hd_bld_EU_013_RailroadStation_02.model", mdl)
        'get_primitive("content/MilitaryEnvironment/hd_mle_SU_005_Mi24A/normal/lod0/hd_mle_SU_005_Mi24A_02.model", mdl)
        'get_primitive("content/Buildings/bld_19_02_Monastery/normal/lod0/bld_19_02_Monastery_05_Chapel.model", mdl)
        Return
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
        'Get a list of all items in the MAP_package
        Dim cnt As Integer = 0
        For Each e As ZipEntry In MAP_package
            contents.Add(e.FileName)
            cnt += 1
            Application.DoEvents()
        Next
        '------------------------------------------------------------------------------------------------
        ' get settings xml.. this sets map sizes and such
        Dim st As Ionic.Zip.ZipEntry = MAP_package("spaces/" & ABS_NAME & "/space.settings")
        Dim settings As New MemoryStream
        st.Extract(settings)
        openXml_stream(settings, ABS_NAME)

        getMapSizes(ABS_NAME) ' this also gets the skydome full path
        '------------------------------------------------------------------------------------------------

    End Sub

    Private Function get_spaceBin(ByVal ABS_NAME As String) As Boolean
        Dim space_bin_file As Ionic.Zip.ZipEntry = _
            MAP_package("spaces/" & ABS_NAME & "/space.bin")
        If space_bin_file IsNot Nothing Then
            ' This is all new code -------------------
            space_bin_file.Extract(TEMP_STORAGE, ExtractExistingFileAction.OverwriteSilently)
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
        Dim LAST_TEXTURE = GL.GenTexture - 1 'get last texture created.
        Dim t_count = FIRST_UNUSED_TEXTURE - LAST_TEXTURE
        GL.DeleteTextures(t_count, FIRST_UNUSED_TEXTURE)
        GL.Finish() ' make sure we are done before moving on

    End Sub


End Module

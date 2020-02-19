Imports System.IO
Imports Ionic.Zip
Imports Tao.DevIl
Imports System.Xml

Module TerrainBuilder
    '=======================================================================
    'move this to Modules/modTypeStructures.vb when we are done debugging
    Public NotInheritable Class theMap
        Public Shared chunks = Nothing
        Public Shared MINI_MAP_ID As Integer

    End Class
    Public Structure chunk_
        Public name As String
        Public cdata() As Byte
        Public heights_data() As Byte
        Public blend_textures_data() As Byte
        Public dominateTestures_data() As Byte
        Public holes_data() As Byte
        Public layers_data() As Byte
        Public normals_data() As Byte
    End Structure
    '=======================================================================
    Public Sub Create_Terrain()
        get_all_chunk_file_data()
    End Sub
    Public Sub get_all_chunk_file_data()
        'Reads and stores the contents of each cdata_processed
        Dim ABS_NAME = Path.GetFileNameWithoutExtension(MAP_NAME_NO_PATH)
        'Get the settings for this map

        get_team_locations_and_field_BB(ABS_NAME)

        'get minimap
        Dim mm = MAP_PACKAGE("spaces/" + ABS_NAME + "/mmap.dds")
        Dim mss As New MemoryStream
        mm.Extract(mss)
        theMap.MINI_MAP_ID = load_image_from_stream(Il.IL_DDS, mss, mm.FileName, False, False)
        mss.Dispose()
        '==========================================================
        'getting mini map team cons here

        TEAM_1_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_1.png")
        TEAM_2_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_2.png")

        '==========================================================


        Dim chunks(15 * 15) As chunk_ 'I don't expect any maps larger than 225 chunks
        Dim cnt As Integer = 0
        For Each entry In MAP_PACKAGE.Entries

            'find cdata chunks
            If entry.FileName.Contains("cdata") Then

                chunks(cnt).name = Path.GetFileNameWithoutExtension(entry.FileName)

                Dim ms As New MemoryStream
                entry.Extract(ms)

                ReDim chunks(cnt).cdata(ms.Length)
                ms.Position = 0
                Dim br As New BinaryReader(ms)
                chunks(cnt).cdata = br.ReadBytes(ms.Length)

                Dim cms As New MemoryStream(chunks(cnt).cdata)
                cms.Position = 0
                Using t2 As Ionic.Zip.ZipFile = Ionic.Zip.ZipFile.Read(cms)

                    Dim stream = New MemoryStream
                    br = New BinaryReader(stream)

                    Dim blend = t2("terrain2/blend_textures")
                    blend.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).blend_textures_data = br.ReadBytes(stream.Length)

                    Dim dominate = t2("terrain2/dominanttextures")
                    dominate.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).dominateTestures_data = br.ReadBytes(stream.Length)

                    Dim heights = t2("terrain2/heights")
                    heights.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).heights_data = br.ReadBytes(stream.Length)

                    Dim layers = t2("terrain2/layers")
                    layers.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).layers_data = br.ReadBytes(stream.Length)

                    Dim normals = t2("terrain2/normals")
                    normals.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).normals_data = br.ReadBytes(stream.Length)
                End Using
                chunks(cnt).cdata = Nothing ' Free up memory
                cnt += 1

            End If
        Next
        ReDim Preserve chunks(cnt - 1)

    End Sub

    Private Sub getSkyDome(ByVal abs_name As String)
        'Dim terrain As New DataTable
        Dim ms As New MemoryStream
        Dim f As ZipEntry = MAP_PACKAGE("spaces/" + abs_name + "/environments/environments.xml")
        If f IsNot Nothing Then
            f.Extract(ms)
            openXml_stream(ms, abs_name)
        Else

        End If
        Dim ds As DataSet = xmldataset.Copy
        Dim te As DataTable = ds.Tables("map_" + abs_name)
        Dim q = From row In te Select ename = row.Field(Of String)("environment")

        Dim e_path = "spaces/" + abs_name + "/environments/" + q(0).Replace(".", "-") + "/skyDome/skyBox.model"
        SKYDOMENAME = e_path.Replace("\", "/")
  

        ds.Dispose()
        te.Dispose()
    End Sub

    Private Function get_team_locations_and_field_BB(ByRef name As String) As Boolean
        Dim ar = name.Split(".")
        Dim script_pkg = Ionic.Zip.ZipFile.Read(GAME_PATH & "scripts.pkg")
        Dim script As Ionic.Zip.ZipEntry = script_pkg("scripts\arena_defs\" & name & ".xml")

        Dim ms As New MemoryStream
        script.Extract(ms)

        ms.Position = 0
        openXml_stream(ms, "")
        script_pkg.Dispose()

        ms.Dispose()
        Dim t As DataSet = xmldataset.Copy
        Dim bb As DataTable = t.Tables("boundingbox")
        Dim t1 As DataTable = t.Tables("team1")
        Dim t2 As DataTable = t.Tables("team2")
        Dim s1 As String = t1.Rows(0).Item(0)
        Dim s2 As String = t2.Rows(0).Item(0)
        Dim bb_bl As String = bb.Rows(0).Item(0)
        Dim bb_ur As String = bb.Rows(0).Item(1)
        If s1.Length = 1 Then
            s1 = t1.Rows(1).Item(2)
            s2 = t1.Rows(1).Item(2)
        End If
        t.Dispose()
        t1.Dispose()
        t2.Dispose()
        bb.Dispose()
        ar = s1.Split(" ")
        TEAM_1.X = ar(0)
        TEAM_1.Y = 0.0
        TEAM_1.Z = ar(1)
        ar = s2.Split(" ")
        TEAM_2.X = ar(0)
        TEAM_2.Y = 0.0
        TEAM_2.Z = ar(1)
        ar = bb_bl.Split(" ")
        Dim scaler As Single = 1.0  'this is debug testing for minimap scale issues.
        MAP_BB_UR.X = -ar(0) * scaler
        MAP_BB_BL.Y = ar(1) * scaler
        ar = bb_ur.Split(" ")
        MAP_BB_BL.X = -ar(0) * scaler
        MAP_BB_UR.Y = ar(1) * scaler
        If MAP_BB_UR.Y > 1000 Or MAP_BB_BL.X < -1000 Then
            Dim mmscale = 0.1
            MAP_BB_UR.X *= mmscale
            MAP_BB_BL.Y *= mmscale
            MAP_BB_BL.X *= mmscale
            MAP_BB_UR.Y *= mmscale

        End If
        ' I dont remeber why I did this but I will find out.. Probably the seaming.
        MAP_BB_BL.X -= 0.78
        MAP_BB_BL.Y -= 0.78
        MAP_BB_UR.X -= 0.78
        MAP_BB_UR.Y -= 0.78
        'Stop
        Return True
    End Function

End Module

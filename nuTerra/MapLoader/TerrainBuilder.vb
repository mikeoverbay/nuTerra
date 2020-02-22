﻿Imports System.IO
Imports Ionic.Zip
Imports Tao.DevIl
Imports System.Xml
Imports OpenTK

Module TerrainBuilder
    '=======================================================================
    'move this to Modules/modTypeStructures.vb when we are done debugging
    Public topRight As New vertex_data
    Public topleft As New vertex_data
    Public bottomRight As New vertex_data
    Public bottomleft As New vertex_data
    Public Structure vertex_data
        Public vert As Vector3
        Public uv As Vector2
        Public norm As UInt32
        Public hole As Single
    End Structure
    Public Structure grid_sec
        Public bmap As System.Drawing.Bitmap
        Public calllist_Id As Int32
        Public normMapID As Int32
        Public HZ_normMapID As Int32
        Public colorMapId As Int32
        Public HZ_colorMapID As Int32
        Public dominateId As Int32
        Public location As vector3
        Public heights(,) As Single
        Public holes(,) As Single
        Public heights_avg As Single
        Public seamCallId As Integer
        Public has_holes As Integer
        Public BB_Max As Vector3
        Public BB_Min As Vector3
        Public BB() As Vector3
        Public visible As Boolean
    End Structure


    Public NotInheritable Class theMap
        Public Shared chunks() As chunk_
        Public Shared v_data() As terain_V_data_
        Public Shared render_set() As chunk_render_data_

        Public Shared MINI_MAP_ID As Integer

        Public Shared skybox_mdl As New base_model_holder_
        Public Shared Sky_Texture_Id As Integer
        Public Shared skybox_path As String
    End Class
    Public Structure chunk_
        Public cdata() As Byte
        Public heights_data() As Byte
        Public blend_textures_data() As Byte
        Public dominateTestures_data() As Byte
        Public holes_data() As Byte
        Public layers_data() As Byte
        Public normals_data() As Byte

        Public location As Vector2
        Public has_holes As Boolean
        Public name As String
    End Structure
    Public Structure terain_V_data_
        Public holes(,) As Single
        Public heights(,) As Single
        Public avg_heights As Single
        Public normals(,) As Vector3
        Public max_height As Single
        Public min_height As Single
        Public BB_Max As Vector3
        Public BB_Min As Vector3
        Public BB() As Vector3
    End Structure
    Public Structure chunk_render_data_
        Public mBuffers() As Integer
        Public VAO As Integer
        Public matrix As Matrix4
        ' Texture IDs and such below
    End Structure
    '=======================================================================
    Public Sub Create_Terrain()

        get_all_chunk_file_data()

        For I = 0 To theMap.chunks.Length - 1
            get_location(theMap.chunks(I))
            get_holes(theMap.chunks(I), theMap.v_data(I))
            get_heights(theMap.chunks(I), theMap.v_data(I))
            get_normals(theMap.chunks(I), theMap.v_data(I))
            get_mesh(theMap.chunks(I), theMap.v_data(I), theMap.render_set(I))
        Next

    End Sub

    Public Sub get_all_chunk_file_data()
        'Reads and stores the contents of each cdata_processed
        Dim ABS_NAME = Path.GetFileNameWithoutExtension(MAP_NAME_NO_PATH)
        'Get the settings for this map

        get_team_locations_and_field_BB(ABS_NAME)
        get_Sky_Dome(ABS_NAME)
        'get minimap
        Dim mm = MAP_PACKAGE("spaces/" + ABS_NAME + "/mmap.dds")
        Dim mss As New MemoryStream
        mm.Extract(mss)
        theMap.MINI_MAP_ID = load_image_from_stream(Il.IL_DDS, mss, mm.FileName, False, False)
        mss.Dispose()
        '==========================================================
        'getting mini map team icons here

        TEAM_1_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_1.png")
        TEAM_2_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_2.png")

        '==========================================================
        'I don't expect any maps larger than 225 chunks
        Dim Expected_max_chunk_count As Integer = 15 * 15
        ReDim theMap.chunks(Expected_max_chunk_count)
        ReDim theMap.v_data(Expected_max_chunk_count)
        ReDim theMap.render_set(Expected_max_chunk_count)

        Dim cnt As Integer = 0
        For Each entry In MAP_PACKAGE.Entries

            'find cdata chunks
            If entry.FileName.Contains("cdata") Then
                '-- make room
                theMap.v_data(cnt) = New terain_V_data_
                theMap.chunks(cnt) = New chunk_
                theMap.render_set(cnt) = New chunk_render_data_

                theMap.chunks(cnt).name = Path.GetFileNameWithoutExtension(entry.FileName)

                Dim ms As New MemoryStream
                entry.Extract(ms)

                ReDim theMap.chunks(cnt).cdata(ms.Length)
                ms.Position = 0
                Dim br As New BinaryReader(ms)
                theMap.chunks(cnt).cdata = br.ReadBytes(ms.Length)

                Dim cms As New MemoryStream(theMap.chunks(cnt).cdata)
                cms.Position = 0
                Using t2 As Ionic.Zip.ZipFile = Ionic.Zip.ZipFile.Read(cms)

                    Dim stream = New MemoryStream
                    br = New BinaryReader(stream)

                    Dim blend = t2("terrain2/blend_textures")
                    stream = New MemoryStream
                    blend.Extract(stream)
                    stream.Position = 0
                    theMap.chunks(cnt).blend_textures_data = br.ReadBytes(stream.Length)

                    Dim dominate = t2("terrain2/dominanttextures")
                    stream = New MemoryStream
                    dominate.Extract(stream)
                    stream.Position = 0
                    br = New BinaryReader(stream)
                    theMap.chunks(cnt).dominateTestures_data = br.ReadBytes(stream.Length)

                    Dim heights = t2("terrain2/heights")
                    stream = New MemoryStream
                    heights.Extract(stream)
                    stream.Position = 0
                    br = New BinaryReader(stream)
                    theMap.chunks(cnt).heights_data = br.ReadBytes(stream.Length)

                    Dim layers = t2("terrain2/layers")
                    stream = New MemoryStream
                    layers.Extract(stream)
                    stream.Position = 0
                    br = New BinaryReader(stream)
                    theMap.chunks(cnt).layers_data = br.ReadBytes(stream.Length)

                    Dim normals = t2("terrain2/normals")
                    stream = New MemoryStream
                    normals.Extract(stream)
                    stream.Position = 0
                    br = New BinaryReader(stream)
                    theMap.chunks(cnt).normals_data = br.ReadBytes(stream.Length)

                    Dim holes = t2("terrain2/holes")
                    If holes IsNot Nothing Then
                        theMap.chunks(cnt).has_holes = True
                        stream = New MemoryStream
                        holes.Extract(stream)
                        stream.Position = 0
                        br = New BinaryReader(stream)
                        theMap.chunks(cnt).holes_data = br.ReadBytes(stream.Length)
                    Else
                        theMap.chunks(cnt).has_holes = False
                    End If
                End Using
                theMap.chunks(cnt).cdata = Nothing ' Free up memory now that its processed

                cnt += 1

            End If
        Next

        ReDim Preserve theMap.chunks(cnt - 1)
        ReDim Preserve theMap.v_data(cnt - 1)
        ReDim Preserve theMap.render_set(cnt - 1)

    End Sub

    Private Sub get_Sky_Dome(ByVal abs_name As String)
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

        theMap.skybox_path = "spaces/" + abs_name + "/environments/" + q(0).Replace(".", "-") + "/skyDome/forward/skyBox.visual_processed"
        theMap.skybox_mdl = New base_model_holder_
        get_X_model(Application.StartupPath + "\resources\skyDome.x", theMap.skybox_mdl)

        Dim entry = MAP_PACKAGE(theMap.skybox_path)
        If entry Is Nothing Then
            MsgBox("Cant find sky box visual_processed", MsgBoxStyle.Exclamation, "Oh no!")
            Return
        End If
        ms = New MemoryStream
        entry.Extract(ms)
        openXml_stream(ms, Path.GetFileName(theMap.skybox_path))
        theMap.Sky_Texture_Id = get_diffuse_texture_id_from_visual()
        If theMap.Sky_Texture_Id = -1 Then
            MsgBox("could not find Sky Box Texture", MsgBoxStyle.Exclamation, "Shit!")
        End If
        'clean up
        ms.Dispose()
        ds.Dispose()
        te.Dispose()
    End Sub

    Public Function get_diffuse_texture_id_from_visual() As Integer
        Dim theString = TheXML_String
        Dim in_pos = InStr(1, theString, "diffuseMap")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, theString, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, theString, "</Texture>")
            Dim newS As String = ""
            newS = Mid(theString, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            Return find_and_load_texture_from_pkgs(newS)
        End If
        Return -1
    End Function
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

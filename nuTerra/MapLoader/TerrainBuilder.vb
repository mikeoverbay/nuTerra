Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK
Imports Tao.DevIl
Imports System.Text

Module TerrainBuilder
    Public sb As New StringBuilder
    Public _Write_texture_info As Boolean = True

#Region "Terrain Storage"
    Public Map_wetness As wetness_
    Public Structure wetness_
        Public waterColor As Vector3
        Public waterAlpha As Single
        Public waveTexture As String
        Public waveTextureCount As Integer
        Public waveAnimationSpeed As Single
        Public waveUVScale As Single
        Public waveSpeed As Single
        Public waveStrength As Single
        Public waveMaskTexture As String
        Public waveMaskUVScale As Single
        Public waveMaskSpeed As Single
    End Structure
    Public mapBoard(20, 20) As map_entry_
    Public Structure map_entry_
        Public location As Vector2
        Public map_id As Integer
        Public abs_location As Point
        Public occupied As Boolean
    End Structure

    Public topRight As New vertex_data
    Public topleft As New vertex_data
    Public bottomRight As New vertex_data
    Public bottomleft As New vertex_data
    Public Structure vertex_data
        Public vert As Vector2
        Public H As Single
        Public uv As Vector2
        Public hole As UInt32
    End Structure

    Public Structure grid_sec
        Public bmap As System.Drawing.Bitmap
        Public calllist_Id As Int32
        Public normMapID As Int32
        Public HZ_normMapID As Int32
        Public colorMapId As Int32
        Public HZ_colorMapID As Int32
        Public dominateId As Int32
        Public location As Vector3
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
        '------------------------
        Public Shared MINI_MAP_ID As GLTexture
        Public Shared GLOBAL_AM_ID As GLTexture

        Public Shared skybox_mdl As XModel
        Public Shared Sky_Texture_Id As GLTexture
        Public Shared skybox_path As String
        Public Shared lut_path As String
        '------------------------
        Public Shared chunk_size As Single ' space.settings/chunkSize or 100.0 by default
        Public Shared bounds_minX As Int32 ' play area bounds?
        Public Shared bounds_maxX As Int32 '
        Public Shared bounds_minY As Int32 '
        Public Shared bounds_maxY As Int32 '
        '------------------------
        Public Shared normal_map As String
        Public Shared global_map As String ' global_AM.dds
        Public Shared noise_texture As String ' noiseTexture
        '------------------------
        Public Shared vertex_vBuffer_id As GLBuffer
        Public Shared vertex_iBuffer_id As GLBuffer
        Public Shared vertex_uvBuffer_id As GLBuffer
        Public Shared vertex_TangentBuffer_id As GLBuffer
        Public Shared indices_count As Integer = 7938 * 3
        '------------------------

    End Class
    Public Structure chunk_
        Public cdata() As Byte
        Public heights_data() As Byte
        Public blend_textures_data() As Byte
        Public dominateTestures_data() As Byte
        Public holes_data() As Byte
        Public layers_data() As Byte
        Public normals_data() As Byte

        Public location As Vector4

        Public mBoard_x As Int16
        Public mBoard_y As Int16
        Public has_holes As Boolean
        Public name As String
    End Structure

    Public Structure terain_V_data_
        Public holes(,) As UInt32
        Public heights(,) As Single
        Public heightsTBL(,) As Single
        Public avg_heights As Single
        Public normals(,) As Vector3
        Public max_height As Single
        Public min_height As Single
        Public BB_Max As Vector3
        Public BB_Min As Vector3
        Public BB() As Vector3

        Dim v_buff_XZ() As Vector2
        Dim v_buff_Y() As Single
        Dim indicies() As vect3_16

        Public h_buff() As UInt32
        Public uv_buff() As Vector2
        Public n_buff() As Vector3
        Public t_buff() As Vector3

    End Structure

    Public Structure chunk_render_data_
        Public mBuffers() As Integer
        Public VAO As Integer
        Public matrix As Matrix4
        Public shadowMatrix As Matrix4
        '-------------------------------
        ' Texture IDs and such below
        Public layersStd140_ubo As GLBuffer
        Public TexLayers() As ids_
        Public layer As layer_render_info_
        Public b_x_size, b_y_size As Integer
        Public layer_count As Integer
        Public dom_tex_list() As String
        Public dom_id As Integer
        Public visible As Boolean ' frustum clipped flag
        Public LQ As Boolean 'draw global_am only flag
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure LayersStd140
        Public U1 As Vector4
        Public U2 As Vector4
        Public U3 As Vector4
        Public U4 As Vector4

        Public U5 As Vector4
        Public U6 As Vector4
        Public U7 As Vector4
        Public U8 As Vector4

        Public V1 As Vector4
        Public V2 As Vector4
        Public V3 As Vector4
        Public V4 As Vector4

        Public V5 As Vector4
        Public V6 As Vector4
        Public V7 As Vector4
        Public V8 As Vector4

        Public r1_1 As Vector4
        Public r1_2 As Vector4
        Public r1_3 As Vector4
        Public r1_4 As Vector4
        Public r1_5 As Vector4
        Public r1_6 As Vector4
        Public r1_7 As Vector4
        Public r1_8 As Vector4

        Public r2_1 As Vector4
        Public r2_2 As Vector4
        Public r2_3 As Vector4
        Public r2_4 As Vector4
        Public r2_5 As Vector4
        Public r2_6 As Vector4
        Public r2_7 As Vector4
        Public r2_8 As Vector4

        Public s1 As Vector4
        Public s2 As Vector4
        Public s3 As Vector4
        Public s4 As Vector4
        Public s5 As Vector4
        Public s6 As Vector4
        Public s7 As Vector4
        Public s8 As Vector4

    End Structure

    Public Structure ids_
        Public Blend_id As GLTexture
        Public AM_name1, NM_name1 As String
        Public AM_id1, NM_id1 As GLTexture
        Public AM_name2, NM_name2 As String
        Public AM_id2, NM_id2 As GLTexture
        Public uP1, uP2, vP1, vP2 As Vector4
        Public used_a, used_b As Single
        Public scale_a, scale_b As Vector4
        Public r1, r2 As Vector4
        Public r2_1, r2_2 As Vector4
    End Structure
    Public Structure layer_render_info_
        Public layer_section_size() As UInt32
        Public render_info() As layer_render_info_entry_
    End Structure
    Public Structure layer_render_info_entry_
        Public texture_name As String
        Public width As Integer
        Public height As Integer
        Public count As Integer
        Public u As Vector4
        Public v As Vector4
        Public flags As UInt32
        Dim v1 As Vector4 ' unknown?
        Public r1 As Vector4
        Public r2 As Vector4
        Public scale As Vector4
    End Structure
    Public Structure imageData
        Public data() As Byte
    End Structure
#End Region

    '=======================================================================
    Public Sub Create_Terrain()
        Dim SWT As New Stopwatch
#If DEBUG Then
        'clear debug window
        clear_output()
#End If

        SWT.Start()
        ReDim mapBoard(20, 20) 'clear it

        MAX_MAP_HEIGHT = -1000.0F
        MIN_MAP_HEIGHT = 2000.0F
        TOTAL_HEIGHT_COUNT = 0

        get_all_chunk_file_data()
        BG_TEXT = "Loading Terrain..."
        BG_MAX_VALUE = theMap.chunks.Length - 1
        BG_VALUE = 0
        set_map_bs() 'presets max min values
        For I = 0 To theMap.chunks.Length - 1
            get_location(theMap.chunks(I), I)
            get_holes(theMap.chunks(I), theMap.v_data(I))
            get_heights(theMap.chunks(I), theMap.v_data(I))
            get_mesh(theMap.chunks(I), theMap.v_data(I), theMap.render_set(I))
            BG_VALUE = I
            draw_scene()
            Application.DoEvents()
        Next
        'needed for fog rendering
        MEAN_MAP_HEIGHT /= TOTAL_HEIGHT_COUNT

        LogThis(String.Format("Load Terrain Data & build mesh: {0}", SWT.ElapsedMilliseconds.ToString))
        SWT.Restart()

        BG_VALUE = 0
        BG_TEXT = "Smoothing Terrain Normals..."
        For i = 0 To theMap.chunks.Length - 1
            smooth_edges(i)
            BG_VALUE = i
            draw_scene()
            Application.DoEvents()
        Next

        LogThis(String.Format("Smooth Seams: {0}", SWT.ElapsedMilliseconds.ToString))
        SWT.Restart()

        sb.Clear()

        BG_VALUE = 0
        BG_TEXT = "Reading Terrain Texture data..."
        For i = 0 To theMap.chunks.Length - 1
            get_layers(i)
            BG_VALUE = i
            draw_scene()
            Application.DoEvents()
        Next
        If _Write_texture_info Then
            LogThis("Saved Texture transform data")
            File.WriteAllText(TEMP_STORAGE + "\" + MAP_NAME_NO_PATH + "_Tex_info.txt", sb.ToString)
        End If
        LogThis(String.Format("Get Layers data and textures: {0}", SWT.ElapsedMilliseconds.ToString))
        SWT.Restart()


        'we need to find a way to package the terrains texture info so we can use instance rendering
        BG_VALUE = 0
        BG_TEXT = "Building render VAOs..."
        For i = 0 To theMap.chunks.Length - 1
            build_Terrain_VAO(i)
            BG_VALUE = i
            draw_scene()
            Application.DoEvents()
        Next
        LogThis(String.Format("Build VAO: {0}", SWT.ElapsedMilliseconds.ToString))
        SWT.Stop()

    End Sub

    '=======================================================================
    Public Sub get_all_chunk_file_data()
        'Reads and stores the contents of each cdata_processed
        Dim ABS_NAME = Path.GetFileNameWithoutExtension(MAP_NAME_NO_PATH)

        GC.Collect()
        GC.WaitForFullGCComplete()
        '==========================================================
        'Get the settings for this map
        BASE_RINGS_LOADED = False
        If get_team_locations_and_field_BB(ABS_NAME) Then
            BASE_RINGS_LOADED = True
        End If

        '==========================================================
        'get minimap
        Dim mm = Packages.MAP_PACKAGE("spaces/" + ABS_NAME + "/mmap.dds")
        Dim mss As New MemoryStream
        mm.Extract(mss)
        theMap.MINI_MAP_ID = load_image_from_stream(Il.IL_DDS, mss, "spaces/" + ABS_NAME + "/mmap.dds", False, False)
        mss.Dispose()
        'get global_am
        Dim gmm = Packages.MAP_PACKAGE("spaces/" + ABS_NAME + "/global_am.dds")
        Dim gmss As New MemoryStream
        gmm.Extract(gmss)
        theMap.GLOBAL_AM_ID = load_image_from_stream(Il.IL_DDS, gmss, "", False, False)
        gmss.Dispose()
        GC.Collect()

        '==========================================================
        'getting mini map team icons here
        TEAM_1_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_1.png")
        TEAM_2_ICON_ID = find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_2.png")
        '==========================================================

        'I don't expect any maps larger than 225 chunks
        Dim Expected_max_chunk_count As Integer = 24 * 24
        ReDim theMap.chunks(Expected_max_chunk_count)
        ReDim theMap.v_data(Expected_max_chunk_count)
        ReDim theMap.render_set(Expected_max_chunk_count)

        Dim cnt As Integer = 0
        With cBWT2.settings
            theMap.chunk_size = .chunk_size
            theMap.bounds_maxX = .bounds_maxX
            theMap.bounds_maxY = .bounds_maxY
            theMap.bounds_minX = .bounds_minX
            theMap.bounds_minY = .bounds_minY

            theMap.global_map = .global_map
            theMap.normal_map = .normal_map
            theMap.noise_texture = .noise_texture
        End With
        For i = 0 To cBWT2.cdatas.count - 1
            With cBWT2.cdatas.data(i)
                Dim chunk_name As String = .resource
                Dim loc_x = .loc_x
                Dim loc_y = .loc_y

                '-- make room
                theMap.v_data(cnt) = New terain_V_data_
                theMap.chunks(cnt) = New chunk_
                theMap.render_set(cnt) = New chunk_render_data_

                Dim s = Left(chunk_name, chunk_name.IndexOf("/"))

                theMap.chunks(cnt).name = Path.GetFileNameWithoutExtension(s)

                Dim entry = Packages.MAP_PACKAGE(Path.Combine("spaces", ABS_NAME, s))
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
                    br = New BinaryReader(stream)
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

                    'Dim normals = t2("terrain2/normals")
                    'stream = New MemoryStream
                    'normals.Extract(stream)
                    'stream.Position = 0
                    'br = New BinaryReader(stream)
                    'theMap.chunks(cnt).normals_data = br.ReadBytes(stream.Length)

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

            End With
        Next
        cBWT2 = Nothing
        cBWST = Nothing

        ReDim Preserve theMap.chunks(cnt - 1)
        ReDim Preserve theMap.v_data(cnt - 1)
        ReDim Preserve theMap.render_set(cnt - 1)
    End Sub

    Public Sub get_environment_info(abs_name As String)
        'Dim terrain As New DataTable
        Dim ms As New MemoryStream
        Dim f = Packages.MAP_PACKAGE("spaces/" + abs_name + "/environments/environments.xml")
        If f IsNot Nothing Then
            f.Extract(ms)
            openXml_stream(ms, abs_name)
        End If
        Dim q As EnumerableRowCollection(Of String) = Nothing
        Dim ds As DataSet = xmldataset.Copy
        Dim te As DataTable = ds.Tables("File_" + abs_name)

        If te.Columns("environment") IsNot Nothing Then
            q = From row In te Select ename = row.Field(Of String)("environment")
        End If

        If te.Columns("activeEnvironment") IsNot Nothing Then
            q = From row In te Select ename = row.Field(Of String)("activeEnvironment")
        End If


        '===========================================================================
        'get skybox and cube texture paths
        theMap.skybox_path = "spaces/" + abs_name + "/environments/" + q(0).Replace(".", "-") + "/skyDome/forward/skyBox.visual_processed"
        theMap.skybox_mdl = get_X_model(Application.StartupPath + "\resources\skyDome.x")

        CUBE_TEXTURE_PATH = "spaces/" + abs_name + "/environments/" + q(0).Replace(".", "-") + "/probes/global/pmrem.dds"

        Dim entry = Packages.MAP_PACKAGE(theMap.skybox_path)
        If entry Is Nothing Then
            MsgBox("Cant find sky box visual_processed", MsgBoxStyle.Exclamation, "Oh no!")
            Return
        End If
        ms = New MemoryStream
        entry.Extract(ms)
        openXml_stream(ms, Path.GetFileName(theMap.skybox_path))
        theMap.Sky_Texture_Id = get_diffuse_texture_id_from_visual()
        If theMap.Sky_Texture_Id Is Nothing Then
            MsgBox("could not find Sky Box Texture", MsgBoxStyle.Exclamation, "Shit!")
        End If
        Dim envPath = "spaces/" + abs_name + "/environments/" + q(0).Replace(".", "-") + "/environment.xml"
        entry = Packages.MAP_PACKAGE(envPath)

        '===========================================================================
        'get sun information and time of day.
        If entry IsNot Nothing Then
            ms = New MemoryStream
            entry.Extract(ms)
            openXml_stream(ms, Path.GetFileName(envPath))
            Dim day_light As DataTable = xmldataset.Tables("day_night_cycle")
            Dim q2 = From row In day_light Select
                        sunColor = row.Field(Of String)("sunLightColor"),
                        ambientSunColor = row.Field(Of String)("ambientColorForward"),
                        time_ = row.Field(Of String)("starttime"),
                        sun_scale = row.Field(Of String)("sunScaleForward"),
                        sun_path = row.Field(Of String)("sunTextureForward"),
                        sun_color = row.Field(Of String)("sunColorForward")


            SUNCOLOR = vector3_from_string(q2(0).sunColor)
            AMBIENTSUNCOLOR = vector3_from_string(q2(0).ambientSunColor)
            TIME_OF_DAY = Convert.ToSingle(q2(0).time_)
            SUN_SCALE = Convert.ToSingle(q2(0).sun_scale)
            If SUN_SCALE = 0 Then
                SUN_SCALE = 5.0F
            End If
            SUN_TEXTURE_PATH = q2(0).sun_path
            'default
            If SUN_TEXTURE_PATH = "" Then
                SUN_TEXTURE_PATH = "system/maps/PSF2_ldr.dds"
            End If
            SUN_RENDER_COLOR = vector3_from_string(q2(0).sun_color)

            'sun rotation/location
            Dim forward As DataTable = xmldataset.Tables("forward")
            Dim q3 = From row In forward Select
                        rotateX = row.Field(Of String)("angle"),
                        rotateZ = row.Field(Of String)("angleZ")
            LIGHT_ORBIT_ANGLE_X = q3(1).rotateX
            LIGHT_ORBIT_ANGLE_Z = q3(1).rotateZ
            If LIGHT_ORBIT_ANGLE_X = 0 Then
                LIGHT_ORBIT_ANGLE_X = q3(0).rotateX
                LIGHT_ORBIT_ANGLE_Z = q3(0).rotateZ

            End If
            '===========================================================================
            'fog_info
            Dim forward_tbl = xmldataset.Tables("forward")
            Dim fog_color_s = forward_tbl.Rows(0).Field(Of String)("color")
            If fog_color_s IsNot Nothing Then
                FOG_COLOR = vector3_from_string(fog_color_s)
            Else
                FOG_COLOR = New Vector3(0.75F, 0.75, 0.85F)
            End If
            '===========================================================================

            ' get color correction lut
            Dim colorCorrection As DataTable = xmldataset.Tables("colorCorrection")
            Dim q4 = From row In colorCorrection Select
                        map = row.Field(Of String)("map")
            theMap.lut_path = q4(0)

            Dim Wetness As DataTable = xmldataset.Tables("Wetness")
            Dim q5 = From row In Wetness Select
                waterColor = row.Field(Of String)("waterColor"),
                waterAlpha = row.Field(Of String)("waterAlpha"),
                waveTexture = row.Field(Of String)("waveTexture"),
                waveTextureCount = row.Field(Of String)("waveTextureCount"),
                waveAnimationSpeed = row.Field(Of String)("waveAnimationSpeed"),
                waveUVScale = row.Field(Of String)("waveUVScale"),
                waveSpeed = row.Field(Of String)("waveSpeed"),
                waveStrength = row.Field(Of String)("waveStrength"),
                waveMaskTexture = row.Field(Of String)("waveMaskTexture"),
                waveMaskUVScale = row.Field(Of String)("waveMaskUVScale"),
                waveMaskSpeed = row.Field(Of String)("waveMaskSpeed")
            For Each w In q5

                Map_wetness.waterColor = vector3_from_string(w.waterColor)
                Map_wetness.waterAlpha = Convert.ToSingle(w.waterAlpha)
                Map_wetness.waveTexture = w.waveTexture
                Map_wetness.waveTextureCount = Convert.ToInt32(w.waveTextureCount)
                Map_wetness.waveAnimationSpeed = Convert.ToSingle(w.waveAnimationSpeed)
                Map_wetness.waveUVScale = Convert.ToSingle(w.waveUVScale)
                Map_wetness.waveSpeed = Convert.ToSingle(w.waveSpeed)
                Map_wetness.waveStrength = Convert.ToSingle(w.waveStrength)
                Map_wetness.waveMaskTexture = w.waveMaskTexture
                Map_wetness.waveMaskUVScale = Convert.ToSingle(w.waveMaskUVScale)
                Map_wetness.waveMaskSpeed = Convert.ToSingle(w.waveMaskSpeed)
            Next

        End If
    End Sub
    Private Function vector3_from_string(ByRef s As String) As Vector3
        Dim v As New Vector3
        's = s.Replace("  ", " ")
        Dim a = s.Split(" ")
        v.X = Convert.ToSingle(a(0))
        v.Y = Convert.ToSingle(a(1))
        v.Z = Convert.ToSingle(a(2))
        Return v
    End Function

    Public Function get_diffuse_texture_id_from_visual() As GLTexture
        Dim theString = TheXML_String
        Dim in_pos = InStr(1, theString, "diffuseMap")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, theString, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, theString, "</Texture>")
            Dim newS As String = ""
            newS = Mid(theString, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            Return find_and_load_texture_from_pkgs(newS)
        End If
        Return Nothing
    End Function

    Private Function get_team_locations_and_field_BB(ByRef name As String) As Boolean
        Dim ar = name.Split(".")
        Dim script_pkg = Ionic.Zip.ZipFile.Read(Path.Combine(GAME_PATH, "scripts.pkg"))
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
        If t1 Is Nothing Then
            Return False
        End If
        Dim t2 As DataTable = t.Tables("team2")
        Dim s1 As String = t1.Rows(0).Item(0)
        Dim s2 As String = t2.Rows(0).Item(0)
        Dim bb_bl As String = bb.Rows(0).Item(0)
        Dim bb_ur As String = bb.Rows(0).Item(1)
        If s1.Length = 1 Then
            s1 = t1.Rows(1).Item(2)
            s2 = t2.Rows(1).Item(2)
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


        Return True
    End Function


End Module

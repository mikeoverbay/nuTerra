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

    Public Const MAP_BOARD_SIZE = 34
    Public mapBoard(,) As map_entry_

    Public Structure map_entry_
        Public location As Vector2
        Public map_id As Integer
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
        Public atlas_id As GLTexture
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
        ReDim mapBoard(MAP_BOARD_SIZE, MAP_BOARD_SIZE) 'clear it

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
        BASE_RINGS_LOADED = get_team_locations_and_field_BB(ABS_NAME)

        '==========================================================
        'get minimap
        Dim mm = ResMgr.Lookup(String.Format("spaces/{0}/mmap.dds", ABS_NAME))
        Dim mss As New MemoryStream
        mm.Extract(mss)
        theMap.MINI_MAP_ID = load_image_from_stream(Il.IL_DDS, mss, "spaces/" + ABS_NAME + "/mmap.dds", False, False)
        mss.Dispose()
        'get global_am
        Dim gmm = ResMgr.Lookup(String.Format("spaces/{0}/global_am.dds", ABS_NAME))
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

        'I don't expect any maps larger than 1024 chunks (208_bf_epic_normandy)
        Dim Expected_max_chunk_count As Integer = 32 * 32
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

                Dim s = Left(chunk_name, chunk_name.LastIndexOf("/"))

                theMap.chunks(cnt).name = Path.GetFileNameWithoutExtension(s)

                Dim entry = ResMgr.Lookup(String.Format("spaces/{0}/{1}", ABS_NAME, s))
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
        Dim environments_xml = ResMgr.openXML(String.Format("spaces/{0}/environments/environments.xml", abs_name))
        Dim activeEnvironment = environments_xml("activeEnvironment").InnerText.Replace(".", "-")

        ' get skybox and cube texture paths
        theMap.skybox_mdl = get_X_model(Application.StartupPath + "\resources\skyDome.x")
        CUBE_TEXTURE_PATH = "spaces/" + abs_name + "/environments/" + activeEnvironment + "/probes/global/pmrem.dds"

        Dim skyBox_visual_path = String.Format("spaces/{0}/environments/{1}/skyDome/forward/skyBox.visual_processed", abs_name, activeEnvironment)
        Dim skyBox_visual = ResMgr.openXML(skyBox_visual_path)
        Dim skyBox_diffuseMap = skyBox_visual.SelectSingleNode("renderSet/geometry/primitiveGroup/material/property[contains(text(), 'diffuseMap')]/Texture").InnerText
        theMap.Sky_Texture_Id = find_and_load_texture_from_pkgs(skyBox_diffuseMap)

        ' get sun information and time of day.
        Dim active_environment_xml = ResMgr.openXML(String.Format("spaces/{0}/environments/{1}/environment.xml", abs_name, activeEnvironment))
        Dim day_night_cycle_node = active_environment_xml("day_night_cycle")

        SUNCOLOR = vector3_from_string(day_night_cycle_node("sunLightColor").InnerText)
        AMBIENTSUNCOLOR = vector3_from_string(day_night_cycle_node("ambientColorForward").InnerText)
        TIME_OF_DAY = Convert.ToSingle(day_night_cycle_node("starttime").InnerText)
        SUN_SCALE = Convert.ToSingle(day_night_cycle_node("sunScaleForward").InnerText)
        SUN_TEXTURE_PATH = day_night_cycle_node("sunTextureForward").InnerText
        SUN_RENDER_COLOR = vector3_from_string(day_night_cycle_node("sunColorForward").InnerText)

        If SUN_SCALE = 0 Then
            SUN_SCALE = 5.0F
        End If

        'default
        If SUN_TEXTURE_PATH = "" Then
            SUN_TEXTURE_PATH = "system/maps/PSF2_ldr.dds"
        End If

        'sun rotation/location
        LIGHT_ORBIT_ANGLE_X = day_night_cycle_node("forward")("angle").InnerText
        LIGHT_ORBIT_ANGLE_Z = day_night_cycle_node("forward")("angleZ").InnerText

        ' fog_info
        Dim fog_color_node = active_environment_xml.SelectSingleNode("Fog/forward/color")
        If fog_color_node IsNot Nothing Then
            FOG_COLOR = vector3_from_string(fog_color_node.InnerText)
        Else
            FOG_COLOR = New Vector3(0.75F, 0.75, 0.85F)
        End If

        ' get color correction lut
        theMap.lut_path = active_environment_xml.SelectSingleNode("HDR/colorCorrection/map").InnerText

        Dim wetness_node = active_environment_xml("Wetness")
        With Map_wetness
            .waterColor = vector3_from_string(wetness_node("waterColor").InnerText)
            .waterAlpha = Convert.ToSingle(wetness_node("waterAlpha").InnerText)
            .waveTexture = wetness_node("waveTexture").InnerText
            .waveTextureCount = Convert.ToInt32(wetness_node("waveTextureCount").InnerText)
            .waveAnimationSpeed = Convert.ToSingle(wetness_node("waveAnimationSpeed").InnerText)
            .waveUVScale = Convert.ToSingle(wetness_node("waveUVScale").InnerText)
            .waveSpeed = Convert.ToSingle(wetness_node("waveSpeed").InnerText)
            .waveStrength = Convert.ToSingle(wetness_node("waveStrength").InnerText)
            .waveMaskTexture = wetness_node("waveMaskTexture").InnerText
            .waveMaskUVScale = Convert.ToSingle(wetness_node("waveMaskUVScale").InnerText)
            .waveMaskSpeed = Convert.ToSingle(wetness_node("waveMaskSpeed").InnerText)
        End With
    End Sub

    Private Function vector3_from_string(s As String) As Vector3
        Dim a = s.Split(" ")
        Return New Vector3(
            Convert.ToSingle(a(0)),
            Convert.ToSingle(a(1)),
            Convert.ToSingle(a(2))
        )
    End Function

    Private Function get_team_locations_and_field_BB(name As String) As Boolean
        Dim arena_xml = ResMgr.openXML(String.Format("scripts/arena_defs/{0}.xml", name))

        Dim bb_bottomLeft = arena_xml("boundingBox")("bottomLeft").InnerText.Split(" ")
        Dim bb_upperRight = arena_xml("boundingBox")("upperRight").InnerText.Split(" ")

        Dim scaler As Single = 1.0  'this is debug testing for minimap scale issues.
        MAP_BB_UR.X = -bb_bottomLeft(0) * scaler
        MAP_BB_BL.Y = bb_bottomLeft(1) * scaler
        MAP_BB_BL.X = -bb_upperRight(0) * scaler
        MAP_BB_UR.Y = bb_upperRight(1) * scaler

        If MAP_BB_UR.Y > 1000 Or MAP_BB_BL.X < -1000 Then
            Dim mmscale = 0.1
            MAP_BB_UR.X *= mmscale
            MAP_BB_BL.Y *= mmscale
            MAP_BB_BL.X *= mmscale
            MAP_BB_UR.Y *= mmscale
        End If

        Dim ctf_teamBasePositions_node = arena_xml.SelectSingleNode("gameplayTypes/ctf/teamBasePositions")
        If ctf_teamBasePositions_node Is Nothing Then
            Return False
        End If

        Dim team1_pos = ctf_teamBasePositions_node("team1").ChildNodes(1).InnerText.Split(" ") ' position1 or position2
        Dim team2_pos = ctf_teamBasePositions_node("team2").ChildNodes(1).InnerText.Split(" ") ' position1 or position2
        TEAM_1.X = team1_pos(0)
        TEAM_1.Y = 0.0
        TEAM_1.Z = team1_pos(1)
        TEAM_2.X = team2_pos(0)
        TEAM_2.Y = 0.0
        TEAM_2.Z = team2_pos(1)
        Return True
    End Function
End Module

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Module TerrainBuilder

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

    Public NotInheritable Class theMap
        Public Shared chunks() As chunk_
        Public Shared v_data() As terrain_V_data_
        Public Shared render_set() As chunk_render_data_
        '------------------------
        Public Shared outland_Vdata As terrain_V_data_
        Public Shared outland_render_set As chunk_render_data_
        Public Shared outland_chunk As chunk_
        '------------------------
        Public Shared lut_path As String
        '------------------------
        Public Shared chunk_size As Single ' space.settings/chunkSize or 100.0 by default
        Public Shared bounds_minX As Int32 ' play area bounds?
        Public Shared bounds_maxX As Int32 '
        Public Shared bounds_minY As Int32 '
        Public Shared bounds_maxY As Int32 '

        Public Shared outland_bounds_max As Vector3
        Public Shared outland_bounds_min As Vector3

        Public Shared outland_Cascade_bounds_max As Vector3
        Public Shared outland_Cascade_bounds_min As Vector3

        Public Shared outland_locations() As Vector2
        Public Shared outland_cascade_locations() As Vector2
        Public Shared center_offset As Vector2
        ' Actual chunk-grid footprint in world coords, measured from the placed
        ' chunk locations. This - not the settings bounds - is what the outland
        ' centres on and what the near ring's hole is cut to.
        Public Shared terrain_footprint_min As Vector2
        Public Shared terrain_footprint_max As Vector2
        Public Shared near_scale As Vector2
        Public Shared far_scale As Vector2
        Public Shared near_y_height As Single
        Public Shared far_y_height As Single
        Public Shared near_y_offset As Single
        Public Shared far_y_offset As Single
    End Class

    Public Structure chunk_
        Public cdata() As Byte
        Public heights_data() As Byte
        Public blend_textures_data() As Byte
        Public dominateTestures_data() As Byte
        Public holes_data() As Byte
        Public layers_data() As Byte
        Public normals_data() As Byte
        '''<summary>terrain2/horizonshadows - 128x128, 4 bits per texel.</summary>
        Public horizon_data() As Byte

        Public location As Vector4

        Public mBoard_x As Int16
        Public mBoard_y As Int16
        Public has_holes As Boolean
        Public name As String
    End Structure

    Public Structure terrain_V_data_
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
        Dim indicies_32() As vect3_32

        Public h_buff() As UInt32
        Public uv_buff() As Vector2
        Public n_buff() As Vector3
        Public t_buff() As Vector3

    End Structure

    Enum TerrainQuality
        LQ
        HQ
    End Enum

    Public Structure chunk_render_data_
        Implements IDisposable

        Public mega_LUT As GLTexture
        Public matrix As Matrix4
        '-------------------------------
        ' Texture IDs and such below
        Public layersStd140_ubo As GLBuffer
        Public TexLayers() As ids_
        Public layer As layer_render_info_
        Public b_x_size, b_y_size As Integer
        Public layer_count As Integer
        '''<summary>Baked horizon shadow for this chunk, or Nothing.</summary>
        Public horizon_id As GLTexture
        Public visible As Boolean ' frustum clipped flag
        Public quality As TerrainQuality

        Public Sub Dispose() Implements IDisposable.Dispose
            mega_LUT?.Dispose()
            layersStd140_ubo?.Dispose()
        End Sub
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
        Implements IDisposable

        Public Blend_id As GLTexture
        Public AM_id1 As GLTexture
        Public NM_id1 As GLTexture
        Public AM_id2 As GLTexture
        Public NM_id2 As GLTexture
        Public uP1, uP2, vP1, vP2 As Vector4
        Public used_a, used_b As Single
        Public scale_a, scale_b As Vector4
        Public r1, r2 As Vector4
        Public r2_1, r2_2 As Vector4

        Public Sub Dispose() Implements IDisposable.Dispose
            Blend_id?.Dispose()
            AM_id1?.Dispose()
            NM_id1?.Dispose()
            AM_id2?.Dispose()
            NM_id2?.Dispose()
        End Sub
    End Structure

    Public Structure layer_render_info_
        Public layer_section_size() As UInt32
        Public render_info() As layer_render_info_entry_
    End Structure

    Public Class layer_render_info_entry_
        Implements IDisposable

        Public atlas_id As GLTexture
        Public texture_name As String
        Public width As Integer
        Public height As Integer
        Public count As Integer
        Public u As Vector4
        Public v As Vector4
        Public flags As UInt32
        Public v1 As Vector4 ' unknown?
        Public r1 As Vector4
        Public r2 As Vector4
        Public scale As Vector4

        Public Sub Dispose() Implements IDisposable.Dispose
            atlas_id?.Dispose()
        End Sub
    End Class
#End Region

    '=======================================================================
    Public Sub Create_Terrain()
        Dim SWT As New Stopwatch

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
            If I Mod 50 = 0 Then
                main_window.ForceRender()
            End If
        Next
        'needed for fog rendering
        MEAN_MAP_HEIGHT /= TOTAL_HEIGHT_COUNT

        LogThis("Load Terrain Data & build mesh: {0}", SWT.ElapsedMilliseconds)
        SWT.Restart()

        BG_VALUE = 0
        BG_TEXT = "Smoothing Terrain Normals..."

        For i = 0 To theMap.chunks.Length - 1
            smooth_edges(i)
            BG_VALUE = i
            If i Mod 50 = 0 Then
                main_window.ForceRender()
            End If
        Next

        LogThis("Smooth Seams: {0}", SWT.ElapsedMilliseconds)
        SWT.Restart()

        BG_VALUE = 0
        BG_TEXT = "Reading Terrain Texture data..."
        For i = 0 To theMap.chunks.Length - 1
            get_layers(i)
            BG_VALUE = i
            If i Mod 50 = 0 Then
                main_window.ForceRender()
            End If
        Next

        LogThis("Get Layers data and textures: {0}", SWT.ElapsedMilliseconds)
        SWT.Restart()


        'we need to find a way to package the terrains texture info so we can use instance rendering
        BG_VALUE = 0
        BG_TEXT = "Building render VAOs..."
        main_window.ForceRender()

        build_Terrain_VAO()

        LogThis("Build VAO: {0}", SWT.ElapsedMilliseconds)
        SWT.Stop()
    End Sub

    '=======================================================================
    Public Sub create_outland()
        ' TODO
        ' Build the mesh.. Size to be tweaked later. Currently 1024 x 1024 . 1 to 1 texture size
        get_outland_mesh(theMap.outland_chunk, theMap.outland_Vdata, theMap.outland_render_set)

        ' Centre the outland on the ACTUAL chunk footprint. The cascades are
        ' authored centred on the terrain in game world; the old settings-based
        ' centre ((max+min)/2*100 = (-50,-50) on a -7..6 grid) sat half a chunk
        ' off on both axes, and nuTerra's chunk Z convention (y*100-50 centres)
        ' shifts the whole frame another 50 m - together the outland was 50/100 m
        ' out of register with the terrain edge (measured on 05_prohorovka and
        ' 101_dday: footprint X -700..700, Z -800..600, cascades authored (0,0)).
        theMap.terrain_footprint_min = New Vector2(Single.MaxValue, Single.MaxValue)
        theMap.terrain_footprint_max = New Vector2(Single.MinValue, Single.MinValue)
        For Each c In theMap.chunks
            theMap.terrain_footprint_min.X = Math.Min(theMap.terrain_footprint_min.X, c.location.X - 50.0F)
            theMap.terrain_footprint_min.Y = Math.Min(theMap.terrain_footprint_min.Y, c.location.Y - 50.0F)
            theMap.terrain_footprint_max.X = Math.Max(theMap.terrain_footprint_max.X, c.location.X + 50.0F)
            theMap.terrain_footprint_max.Y = Math.Max(theMap.terrain_footprint_max.Y, c.location.Y + 50.0F)
        Next
        theMap.center_offset = (theMap.terrain_footprint_min + theMap.terrain_footprint_max) / 2.0F

        Console.WriteLine("outland placement: footprint x {0:0}..{1:0} z {2:0}..{3:0}  centre ({4:0},{5:0})  cascade0 bb x {6:0}..{7:0} z {8:0}..{9:0}",
                          theMap.terrain_footprint_min.X, theMap.terrain_footprint_max.X,
                          theMap.terrain_footprint_min.Y, theMap.terrain_footprint_max.Y,
                          theMap.center_offset.X, theMap.center_offset.Y,
                          theMap.outland_bounds_min.X, theMap.outland_bounds_max.X,
                          theMap.outland_bounds_min.Z, theMap.outland_bounds_max.Z)

        ' The grid mesh is scaled to 100 units across in get_outland_mesh, so
        ' world-units-per-mesh-unit is span/100 - the cascade lands exactly on
        ' its authored BB.
        theMap.near_scale.X = (theMap.outland_bounds_max.X - theMap.outland_bounds_min.X) / 100.0
        theMap.near_scale.Y = (theMap.outland_bounds_max.Z - theMap.outland_bounds_min.Z) / 100.0

        theMap.far_scale.X = (theMap.outland_Cascade_bounds_max.X - theMap.outland_Cascade_bounds_min.X) / 100.0
        theMap.far_scale.Y = (theMap.outland_Cascade_bounds_max.Z - theMap.outland_Cascade_bounds_min.Z) / 100.0

        theMap.near_y_height = theMap.outland_bounds_max.Y - theMap.outland_bounds_min.Y
        theMap.far_y_height = theMap.outland_Cascade_bounds_max.Y - theMap.outland_Cascade_bounds_min.Y
        theMap.near_y_offset = theMap.outland_bounds_min.Y
        theMap.far_y_offset = theMap.outland_Cascade_bounds_min.Y
        'this does not need to be indirect?
        build_outland_vao()

        ' The game's engine bakes tilemap x tile set into one albedo per
        ' cascade at load and PBS_ext_outland only samples the result -
        ' textures are already in (get_all_chunk_file_data runs before us).
        map_scene.terrain.bake_outland_albedo()
    End Sub

    '=======================================================================
    Public Sub get_all_chunk_file_data()
        ' Reads and stores the contents of each cdata_processed
        Dim ABS_NAME = Path.GetFileNameWithoutExtension(MAP_NAME_NO_PATH)

        GC.Collect()
        GC.WaitForFullGCComplete()
        '==========================================================
        ' Get the settings for this map
        map_scene.BASE_RINGS_LOADED = get_team_locations_and_field_BB(ABS_NAME)

        '==========================================================
        'get minimap
        Dim mm = ResMgr.Lookup(String.Format("spaces/{0}/mmap.dds", ABS_NAME))
        If mm IsNot Nothing Then
            Using mss As New MemoryStream
                mm.Extract(mss)
                map_scene.mini_map.MINI_MAP_ID = TextureMgr.load_dds_image_from_stream(mss, "spaces/" + ABS_NAME + "/mmap.dds")
            End Using
        End If

        ' get global_am
        Dim gmm = ResMgr.Lookup(String.Format("spaces/{0}/global_am.dds", ABS_NAME))
        Dim gmss As New MemoryStream
        gmm.Extract(gmss)
        map_scene.terrain.GLOBAL_AM_ID = TextureMgr.load_dds_image_from_stream(gmss, "")
        gmss.Dispose()
        GC.Collect()

        '==========================================================
        ' getting mini map team icons here
        map_scene.mini_map.TEAM_1_ICON_ID = TextureMgr.find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_1.png")
        map_scene.mini_map.TEAM_2_ICON_ID = TextureMgr.find_and_load_UI_texture_from_pkgs("gui/maps/icons/library/icon_2.png")
        '==========================================================

        'I don't expect any maps larger than 1024 chunks (208_bf_epic_normandy)
        Dim Expected_max_chunk_count As Integer = 32 * 32
        ReDim theMap.chunks(Expected_max_chunk_count)
        ReDim theMap.v_data(Expected_max_chunk_count)
        ReDim theMap.render_set(Expected_max_chunk_count)

        '==========================================================
        ' get map bounds
        With cBWT2.settings
            theMap.chunk_size = .chunk_size
            theMap.bounds_maxX = .bounds_maxX
            theMap.bounds_maxY = .bounds_maxY
            theMap.bounds_minX = .bounds_minX
            theMap.bounds_minY = .bounds_minY
        End With

        '==========================================================
        ' get outland bounds

        If cBWT2.cascades.count <> 0 Then
            With cBWT2.cascades.data(0)
                theMap.outland_bounds_max = .outland_bb_max
                theMap.outland_bounds_min = .outland_BB_min
            End With
            If cBWT2.cascades.count = 2 Then
                With cBWT2.cascades.data(1)
                    theMap.outland_Cascade_bounds_max = .outland_bb_max
                    theMap.outland_Cascade_bounds_min = .outland_BB_min
                End With
            End If
        End If

        '==========================================================
        ' get map data for each chunk
        Dim cnt As Integer = 0
        For i = 0 To cBWT2.cdatas.count - 1
            With cBWT2.cdatas.data(i)
                Dim chunk_name As String = .resource
                Dim loc_x = .loc_x
                Dim loc_y = .loc_y

                '-- make room
                theMap.v_data(cnt) = New terrain_V_data_
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

                    ' Baked terrain self shadowing. Header is "shd", 128 x 128,
                    ' 4 bits per texel - it stores a horizon angle, not a shadow
                    ' image, which is why it survives the day/night cycle.
                    Dim horizon = t2("terrain2/horizonshadows")
                    If horizon IsNot Nothing Then
                        stream = New MemoryStream
                        horizon.Extract(stream)
                        stream.Position = 0
                        br = New BinaryReader(stream)
                        theMap.chunks(cnt).horizon_data = br.ReadBytes(stream.Length)
                    End If

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

        If DONT_BLOCK_OUTLAND Then
            '==========================================================
            ' get outland textures
            map_scene.terrain.CASCADE_LEVELS = cBWT2.cascades.count
            map_scene.terrain.OUTLAND_TILE_SCALE = cBWT2.cascades.data(0).tileScale
            map_scene.terrain.OUTLAND_TILE = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(0).tile_map, False) ' unorm 16
            map_scene.terrain.OUTLAND_NORMAL_MAP = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(0).normal_map, Nothing) ' dds
            map_scene.terrain.OUTLAND_height_MAP = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(0).height_map, True) ' float 16
            If cBWT2.cascades.count = 2 Then
                map_scene.terrain.OUTLAND_TILE_SCALE_CASCADE = cBWT2.cascades.data(1).tileScale
                map_scene.terrain.OUTLAND_TILE_CASCADE = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(1).tile_map, False) ' unorm 16
                map_scene.terrain.OUTLAND_NORMAL_CASCADE_MAP = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(1).normal_map, Nothing) ' dds
                map_scene.terrain.OUTLAND_height_CASCADE_MAP = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(cBWT2.cascades.data(1).height_map, True) ' float 16
            End If

            ReDim map_scene.terrain.OUTLAND_TILES(cBWT2.tiles_fnv.count - 1)
            For i = 0 To cBWT2.tiles_fnv.count - 1
                map_scene.terrain.OUTLAND_TILES(i) = TextureMgr.find_and_load_texture_from_pkgs(cBWST.find_str(cBWT2.tiles_fnv.data(i))) ' dds
            Next
        End If

        ReDim Preserve theMap.chunks(cnt - 1)
        ReDim Preserve theMap.v_data(cnt - 1)
        ReDim Preserve theMap.render_set(cnt - 1)
    End Sub

    Public Sub get_environment_info(abs_name As String)
        Dim environments_xml = ResMgr.openXML(String.Format("spaces/{0}/environments/environments.xml", abs_name))
        Dim activeEnvironment = environments_xml("activeEnvironment").InnerText.Replace(".", "-")

        ' get skybox and cube texture paths
        map_scene.sky.skybox_mdl = XModel.load_from_file("skyDome.x")
        map_scene.sky.CUBE_TEXTURE_PATH = "spaces/" + abs_name + "/environments/" + activeEnvironment + "/probes/global/pmrem.dds"

        Dim skyBox_visual_path = String.Format("spaces/{0}/environments/{1}/skyDome/forward/skyBox.visual_processed", abs_name, activeEnvironment)
        Dim skyBox_visual = ResMgr.openXML(skyBox_visual_path)
        Dim skyBox_diffuseMap = skyBox_visual.SelectSingleNode("renderSet/geometry/primitiveGroup/material/property[contains(text(), 'diffuseMap')]/Texture").InnerText
        map_scene.sky.texture = TextureMgr.find_and_load_texture_from_pkgs(skyBox_diffuseMap)

        ' Diffuse ambient. The probe next to the pmrem holds 9 L2 spherical
        ' harmonic coefficients baked from the same environment, so ambient can
        ' vary with surface orientation instead of being one flat colour.
        load_sh_ambient(abs_name, activeEnvironment)

        ' The baked probe FIELD beside it. Loaded and uploaded, but nothing in
        ' the lighting reads it yet - see load_sh_grid.
        load_sh_grid(abs_name, activeEnvironment)

        ' get sun information and time of day.
        Dim active_environment_xml = ResMgr.openXML(String.Format("spaces/{0}/environments/{1}/environment.xml", abs_name, activeEnvironment))
        Dim day_night_cycle_node = active_environment_xml("day_night_cycle")

        CommonProperties.sunColor = vector3_from_string(day_night_cycle_node("sunLightColor").InnerText)
        CommonProperties.ambientColorForward = vector3_from_string(day_night_cycle_node("ambientColorForward").InnerText)
        TIME_OF_DAY = Convert.ToSingle(day_night_cycle_node("starttime").InnerText)
        SUN_SCALE = Convert.ToSingle(day_night_cycle_node("sunScaleForward").InnerText)
        map_scene.sky.SUN_TEXTURE_PATH = day_night_cycle_node("sunTextureForward").InnerText

        If SUN_SCALE = 0 Then
            SUN_SCALE = 5.0F
        End If

        'default
        If map_scene.sky.SUN_TEXTURE_PATH = "" Then
            map_scene.sky.SUN_TEXTURE_PATH = "system/maps/PSF2_ldr.dds"
        End If

        'sun rotation/location
        LIGHT_ORBIT_ANGLE_X = day_night_cycle_node("forward")("angle").InnerText
        LIGHT_ORBIT_ANGLE_Z = day_night_cycle_node("forward")("angleZ").InnerText

        ' fog_info
        Dim fog_color_node = active_environment_xml.SelectSingleNode("Fog/forward/color")
        If fog_color_node IsNot Nothing Then
            CommonProperties.fog_tint = vector3_from_string(fog_color_node.InnerText)
        Else
            CommonProperties.fog_tint = New Vector3(0.75F, 0.75, 0.85F)
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

    ''' <summary>
    ''' Reads probes/global/rem_sh.xml - a packed section holding sh0..sh8 as RGB
    ''' triples, plus dominant_vector and max_lum. Falls back to a flat white DC
    ''' term if the probe is missing, which evaluates to the old constant ambient
    ''' so a map without probes still lights.
    ''' </summary>
    Private Sub load_sh_ambient(abs_name As String, activeEnvironment As String)
        SH_AMBIENT_LOADED = False
        SH_MAX_LUM = 1.0F
        reset_sh_ambient()

        Try
            Dim path = String.Format("spaces/{0}/environments/{1}/probes/global/rem_sh.xml",
                                     abs_name, activeEnvironment)
            Dim sh_xml = ResMgr.openXML(path)
            If sh_xml Is Nothing Then
                LogThis("no SH probe at {0} - ambient stays flat", path)
                Return
            End If

            For i = 0 To 8
                Dim node = sh_xml("sh" & i.ToString())
                If node Is Nothing Then
                    LogThis("SH probe {0} has no sh{1} - ambient stays flat", path, i)
                    reset_sh_ambient()
                    Return
                End If
                SH_AMBIENT(i) = vector3_from_string(node.InnerText)
            Next

            Dim lum = sh_xml("max_lum")
            If lum IsNot Nothing Then
                SH_MAX_LUM = Convert.ToSingle(lum.InnerText)
            End If

            SH_AMBIENT_LOADED = True
            LogThis("SH ambient loaded: sh0 = {0} {1} {2}, max_lum = {3}",
                    SH_AMBIENT(0).X, SH_AMBIENT(0).Y, SH_AMBIENT(0).Z, SH_MAX_LUM)
        Catch ex As Exception
            LogThis("SH probe failed to load: {0}", ex.Message)
            reset_sh_ambient()
        End Try
    End Sub

    Private Sub reset_sh_ambient()
        SH_AMBIENT(0) = New Vector3(1.0F, 1.0F, 1.0F)
        For i = 1 To 8
            SH_AMBIENT(i) = Vector3.Zero
        Next
    End Sub

    ''' <summary>
    ''' Reads probes/sh_grid - the baked probe FIELD, an RGBA16F volume texture
    ''' with 8 slices. Seven carry one probe's packed SH9; slice 6's alpha is
    ''' that probe's reference height; slice 7 is padding. Abbey ships 280x280
    ''' probes over the 1400 m WGSH box, so exactly 5 m apart.
    '''
    ''' Where load_sh_ambient gives one probe for the whole map, this varies
    ''' with POSITION as well as normal, because the bake saw the buildings.
    '''
    ''' NOTHING IN THE LIGHTING READS THIS YET. It is loaded, uploaded and
    ''' sampled by deferred.frag into a local variable that is deliberately not
    ''' folded into the ambient term - the "show probe field" view is there to
    ''' confirm it is live and correctly placed before any of that happens.
    '''
    ''' The general DDS loader has no volume path, so the read is bespoke.
    ''' </summary>
    Private Sub load_sh_grid(abs_name As String, activeEnvironment As String)
        SH_GRID_ID?.Dispose()
        SH_GRID_ID = Nothing
        SH_GRID_LOADED = False
        reset_sh_grid_probe()

        ' The box has to be known first - without WGSH there is no way to map
        ' the texture onto the world.
        If Not WGSH_LOADED Then
            LogThis("no WGSH box - probe grid skipped")
            Return
        End If

        Try
            Dim folder = String.Format("spaces/{0}/environments/{1}/probes/sh_grid",
                                       abs_name, activeEnvironment)

            ' Named with a content hash the caller cannot know, so match the
            ' suffix - the environment folder holds exactly one.
            Dim entry = ResMgr.LookupBySuffix(folder, "_sh_grid.dds")
            If entry Is Nothing Then
                LogThis("no probe grid in {0} - ambient stays on the global probe", folder)
                Return
            End If

            Dim ms As New MemoryStream
            entry.Extract(ms)
            ms.Position = 0

            Dim w As Integer, h As Integer, d As Integer
            Dim pixels() As Byte
            Using br As New BinaryReader(ms)
                Dim dds_header = TextureMgr.get_dds_header(br)
                w = dds_header.width
                h = dds_header.height
                d = Math.Max(dds_header.depth, 1)

                ' RGBA16F only - that is what every shipped grid is, and a
                ' silent mis-read here would look like a lighting bug later.
                If dds_header.FourCC <> "q" & vbNullChar & vbNullChar & vbNullChar Then
                    LogThis("probe grid is not RGBA16F (fourCC '{0}') - skipped", dds_header.FourCC)
                    Return
                End If

                Dim expected = w * h * d * 8
                ms.Position = 128
                pixels = br.ReadBytes(expected)
                If pixels.Length <> expected Then
                    LogThis("probe grid truncated: {0} of {1} bytes - skipped", pixels.Length, expected)
                    Return
                End If
            End Using

            Dim tex = GLTexture.Create(TextureTarget.Texture3D, "sh_grid")
            tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            ' Clamp on every axis. Wrapping would fetch the far side of the map
            ' at the edges, and on the slice axis it would blend two different
            ' coefficient vectors together.
            tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
            tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
            tex.Parameter(TextureParameterName.TextureWrapR, TextureWrapMode.ClampToEdge)
            tex.Storage3D(1, SizedInternalFormat.Rgba16f, w, h, d)
            tex.SubImage3D(0, 0, 0, 0, w, h, d, PixelFormat.Rgba, PixelType.HalfFloat, pixels)

            SH_GRID_ID = tex
            SH_GRID_SPACING = SH_GRID_SIZE.X / Math.Max(w, 1)

            ' The field's OWN fallback probe, from the rem_sh.xml beside it -
            ' NOT probes/global. On Abbey the global one is about 1.8x brighter,
            ' and fading out to it banded the top of every wall.
            load_sh_grid_probe(folder)

            SH_GRID_LOADED = True
            LogThis("SH probe grid: {0}x{1}x{2}, {3:0.00} m spacing over a {4:0.#} m box",
                    w, h, d, SH_GRID_SPACING, SH_GRID_SIZE.X)
        Catch ex As Exception
            LogThis("probe grid failed to load: {0}", ex.Message)
            SH_GRID_ID?.Dispose()
            SH_GRID_ID = Nothing
            SH_GRID_LOADED = False
        End Try
    End Sub

    ''' <summary>The grid's companion probe - same folder, same hash prefix.</summary>
    Private Sub load_sh_grid_probe(folder As String)
        Dim entry = ResMgr.LookupBySuffix(folder, "_rem_sh.xml")
        If entry Is Nothing Then
            LogThis("probe grid has no companion rem_sh.xml - falling back to the global probe")
            SH_GRID_SH9 = DirectCast(SH_AMBIENT.Clone(), Vector3())
            Return
        End If

        Dim sh_xml = ResMgr.openXML(entry)
        If sh_xml Is Nothing Then
            SH_GRID_SH9 = DirectCast(SH_AMBIENT.Clone(), Vector3())
            Return
        End If

        For i = 0 To 8
            Dim node = sh_xml("sh" & i.ToString())
            If node Is Nothing Then
                LogThis("companion probe has no sh{0} - falling back to the global probe", i)
                SH_GRID_SH9 = DirectCast(SH_AMBIENT.Clone(), Vector3())
                Return
            End If
            SH_GRID_SH9(i) = vector3_from_string(node.InnerText)
        Next

        LogThis("  grid probe sh0 = {0:0.000} {1:0.000} {2:0.000}  (global sh0 = {3:0.000} {4:0.000} {5:0.000})",
                SH_GRID_SH9(0).X, SH_GRID_SH9(0).Y, SH_GRID_SH9(0).Z,
                SH_AMBIENT(0).X, SH_AMBIENT(0).Y, SH_AMBIENT(0).Z)
    End Sub

    Private Sub reset_sh_grid_probe()
        SH_GRID_SH9(0) = New Vector3(1.0F, 1.0F, 1.0F)
        For i = 1 To 8
            SH_GRID_SH9(i) = Vector3.Zero
        Next
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

        MAP_BB_UR.X = -Convert.ToSingle(bb_bottomLeft(0))
        MAP_BB_BL.Y = Convert.ToSingle(bb_bottomLeft(1))
        MAP_BB_BL.X = -Convert.ToSingle(bb_upperRight(0))
        MAP_BB_UR.Y = Convert.ToSingle(bb_upperRight(1))

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

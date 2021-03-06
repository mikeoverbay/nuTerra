﻿Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL

Public Class SettingsPropertyGrid
    Const MIN_FOV = 1
    Const MAX_FOV = 179.0

    Const MIN_NEAR = 0.0
    Const MAX_NEAR = 1000.0

    Const MIN_FAR = 0.0
    Const MAX_FAR = 100000.0

    Const MIN_SPEED = 0.0
    Const MAX_SPEED = 10000.0

    Public Sub New()
        CommonProperties.tess_level = 1.0
        FieldOfView = CSng(Math.PI) * (My.Settings.fov / 180.0F)

        'Get block state of things we want to block loading to speed things up for testing/debugging
        DONT_BLOCK_BASES = My.Settings.load_bases
        DONT_BLOCK_DECALS = My.Settings.load_decals
        DONT_BLOCK_MODELS = My.Settings.load_models
        DONT_BLOCK_SKY = My.Settings.load_sky
        DONT_BLOCK_TERRAIN = My.Settings.load_terrain
        DONT_BLOCK_OUTLAND = My.Settings.load_outland
        DONT_BLOCK_TREES = My.Settings.load_trees
        DONT_BLOCK_WATER = My.Settings.load_water
    End Sub

    <DisplayName("FoV"), Category("Camera")>
    Public Property Camera_FoV As Single
        Set(value As Single)
            If MIN_FOV <= value AndAlso value <= MAX_FOV Then
                My.Settings.fov = value
                FieldOfView = CSng(Math.PI) * (value / 180.0F)
            End If
        End Set
        Get
            Return My.Settings.fov
        End Get
    End Property

    <DisplayName("Near"), Category("Camera")>
    Public Property Camera_Near As Single
        Set(value As Single)
            If MIN_NEAR <= value AndAlso value <= MAX_NEAR Then
                My.Settings.near = value
            End If
        End Set
        Get
            Return My.Settings.near
        End Get
    End Property

    <DisplayName("Far"), Category("Camera")>
    Public Property Camera_Far As Single
        Set(value As Single)
            If MIN_FAR <= value AndAlso value <= MAX_FAR Then
                My.Settings.far = value
            End If
        End Set
        Get
            Return My.Settings.far
        End Get
    End Property

    <DisplayName("Speed"), Category("Camera")>
    Public Property Camera_Speed As Single
        Set(value As Single)
            If MIN_SPEED <= value AndAlso value <= MAX_SPEED Then
                My.Settings.speed = value
            End If
        End Set
        Get
            Return My.Settings.speed
        End Get
    End Property

    <DisplayName("Max Zoom Out"), Category("Camera")>
    Public Property Camera_max_zoom_out As Single
        Set(value As Single)
            map_scene.camera.MAX_ZOOM_OUT = value
        End Set
        Get
            Return map_scene?.camera.MAX_ZOOM_OUT
        End Get
    End Property

    <DisplayName("Position"), Category("Camera")>
    Public ReadOnly Property Camera_position As String
        Get
            Return map_scene?.camera.CAM_POSITION.ToString.Replace("(", "").Replace(")", "")
        End Get
    End Property

    <DisplayName("Target"), Category("Camera")>
    Public ReadOnly Property Camera_target As String
        Get
            Return map_scene?.camera.CAM_TARGET.ToString.Replace("(", "").Replace(")", "")
        End Get
    End Property

    <DisplayName("Use tessellation"), Category("Terrain")>
    Public Property Terrain_use_tessellation As Boolean
        Set(value As Boolean)
            USE_TESSELLATION = value
        End Set
        Get
            Return USE_TESSELLATION
        End Get
    End Property

    <DisplayName("Tessellation level"), Category("Terrain")>
    Public Property Terrain_tess_level As Single
        Set(value As Single)
            CommonProperties.tess_level = value
            CommonProperties.update()
        End Set
        Get
            Return CommonProperties.tess_level
        End Get
    End Property


    <DisplayName("Map Icon Scale"), Category("User Interface")>
    Public Property UI_map_icon_scale As Single
        Set(value As Single)
            If value > 0.0 Then
                My.Settings.UI_map_icon_scale = value
                MapMenuScreen.Invalidate()
            End If
        End Set
        Get
            Return My.Settings.UI_map_icon_scale
        End Get
    End Property

    <DisplayName("Global Mip Bias"), Category("Open GL")>
    Public Property OPENGL_global_mip_bias As Single
        Set(value As Single)
            If -15.0 <= value AndAlso value <= 15.0 Then
                GLOBAL_MIP_BIAS = value
            End If
        End Set
        Get
            Return GLOBAL_MIP_BIAS
        End Get
    End Property

    <DisplayName("Work Group Size"), Category("Open GL")>
    Public Property OPENGL_work_group_size As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 1024 Then
                WORK_GROUP_SIZE = value
            End If
        End Set
        Get
            Return WORK_GROUP_SIZE
        End Get
    End Property

    <DisplayName("Feedback width"), Category("VT")>
    Public Property VT_feedback_width As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 128 Then
                FEEDBACK_WIDTH = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return FEEDBACK_WIDTH
        End Get
    End Property

    <DisplayName("Feedback height"), Category("VT")>
    Public Property VT_feedback_height As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 128 Then
                FEEDBACK_HEIGHT = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return FEEDBACK_HEIGHT
        End Get
    End Property

    <DisplayName("Tile Size"), Category("VT")>
    Public Property VT_tile_zise As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 8192 Then
                TILE_SIZE = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return TILE_SIZE
        End Get
    End Property

    <DisplayName("Num pages"), Category("VT")>
    Public Property VT_num_pages_ As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 4096 Then
                VT_NUM_PAGES = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return VT_NUM_PAGES
        End Get
    End Property

    <DisplayName("Num tiles"), Category("VT")>
    Public Property VT_num_tiles_ As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 2048 Then
                NUM_TILES = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return NUM_TILES
        End Get
    End Property

    <DisplayName("Uploads per frame"), Category("VT")>
    Public Property VT_uploads_per_frame_ As Integer
        Set(value As Integer)
            If 1 <= value AndAlso value <= 64 Then
                UPLOADS_PER_FRAME = value
                map_scene.terrain.RebuildVTAtlas()
            End If
        End Set
        Get
            Return UPLOADS_PER_FRAME
        End Get
    End Property

    <DisplayName("Draw terrain"), Category("Map")>
    Public Property MAP_draw_terrain As Boolean
        Set(value As Boolean)
            My.Settings.load_terrain = value
            DONT_BLOCK_TERRAIN = value
        End Set
        Get
            Return DONT_BLOCK_TERRAIN
        End Get
    End Property

    <DisplayName("Draw Outland"), Category("Map")>
    Public Property MAP_draw_outland As Boolean
        Set(value As Boolean)
            My.Settings.load_outland = value
            DONT_BLOCK_OUTLAND = value
        End Set
        Get
            Return DONT_BLOCK_OUTLAND
        End Get
    End Property

    <DisplayName("Draw models"), Category("Map")>
    Public Property MAP_draw_models As Boolean
        Set(value As Boolean)
            My.Settings.load_models = value
            DONT_BLOCK_MODELS = value
        End Set
        Get
            Return DONT_BLOCK_MODELS
        End Get
    End Property

    <DisplayName("Draw bases"), Category("Map")>
    Public Property MAP_draw_bases As Boolean
        Set(value As Boolean)
            My.Settings.load_bases = value
            DONT_BLOCK_BASES = value
        End Set
        Get
            Return DONT_BLOCK_BASES
        End Get
    End Property

    <DisplayName("Draw decals"), Category("Map")>
    Public Property MAP_draw_decals As Boolean
        Set(value As Boolean)
            My.Settings.load_decals = value
            DONT_BLOCK_DECALS = value
        End Set
        Get
            Return DONT_BLOCK_DECALS
        End Get
    End Property

    <DisplayName("Draw sky"), Category("Map")>
    Public Property MAP_draw_sky As Boolean
        Set(value As Boolean)
            My.Settings.load_sky = value
            DONT_BLOCK_SKY = value
        End Set
        Get
            Return DONT_BLOCK_SKY
        End Get
    End Property

    <DisplayName("Draw trees"), Category("Map")>
    Public Property MAP_draw_trees As Boolean
        Set(value As Boolean)
            My.Settings.load_trees = value
            DONT_BLOCK_TREES = value
        End Set
        Get
            Return DONT_BLOCK_TREES
        End Get
    End Property

    <DisplayName("Draw water"), Category("Map")>
    Public Property MAP_draw_water As Boolean
        Set(value As Boolean)
            My.Settings.load_water = value
            DONT_BLOCK_WATER = value
        End Set
        Get
            Return DONT_BLOCK_WATER
        End Get
    End Property

    <DisplayName("Draw terrain wire"), Category("Overlays")>
    Public Property OVERLAYS_draw_terrain_wire As Boolean
        Set(value As Boolean)
            WIRE_TERRAIN = value
        End Set
        Get
            Return WIRE_TERRAIN
        End Get
    End Property

    <DisplayName("Draw model wire"), Category("Overlays")>
    Public Property OVERLAYS_draw_model_wire As Boolean
        Set(value As Boolean)
            WIRE_MODELS = value
        End Set
        Get
            Return WIRE_MODELS
        End Get
    End Property

    <DisplayName("Draw normals (0 none, 1 by face, 2 by vertex)"), Category("Overlays")>
    Public Property OVERLAYS_draw_normals As Integer
        Set(value As Integer)
            NORMAL_DISPLAY_MODE = value
        End Set
        Get
            Return NORMAL_DISPLAY_MODE
        End Get
    End Property

    <DisplayName("Draw bounding boxes"), Category("Overlays")>
    Public Property OVERLAYS_draw_model_boxes As Boolean
        Set(value As Boolean)
            SHOW_BOUNDING_BOXES = value
        End Set
        Get
            Return SHOW_BOUNDING_BOXES
        End Get
    End Property

    <DisplayName("Draw chunks"), Category("Overlays")>
    Public Property OVERLAYS_draw_chunks As Boolean
        Set(value As Boolean)
            SHOW_CHUNKS = value
        End Set
        Get
            Return SHOW_CHUNKS
        End Get
    End Property

    <DisplayName("Draw grid"), Category("Overlays")>
    Public Property OVERLAYS_draw_grid As Boolean
        Set(value As Boolean)
            SHOW_GRID = value
        End Set
        Get
            Return SHOW_GRID
        End Get
    End Property

    <DisplayName("Draw border"), Category("Overlays")>
    Public Property OVERLAYS_draw_border As Boolean
        Set(value As Boolean)
            SHOW_BORDER = value
        End Set
        Get
            Return SHOW_BORDER
        End Get
    End Property

    <DisplayName("Draw colored lods"), Category("Overlays")>
    Public Property OVERLAYS_colored_lods As Boolean
        Set(value As Boolean)
            SHOW_LOD_COLORS = value
            If SHOW_LOD_COLORS Then
                modelShader.SetDefine("SHOW_LOD_COLORS")
            Else
                modelShader.UnsetDefine("SHOW_LOD_COLORS")
            End If
        End Set
        Get
            Return SHOW_LOD_COLORS
        End Get
    End Property

    <DisplayName("Draw chunk ids"), Category("Overlays")>
    Public Property OVERLAYS_chunk_ids As Boolean
        Set(value As Boolean)
            SHOW_CHUNK_IDs = value
        End Set
        Get
            Return SHOW_CHUNK_IDs
        End Get
    End Property

    <DisplayName("Draw test textures"), Category("Overlays")>
    Public Property OVERLAYS_test_textures As Boolean
        Set(value As Boolean)
            SHOW_TEST_TEXTURES = value
            If SHOW_TEST_TEXTURES Then
                t_mixerShader.SetDefine("SHOW_TEST_TEXTURES")
            Else
                t_mixerShader.UnsetDefine("SHOW_TEST_TEXTURES")
            End If
        End Set
        Get
            Return SHOW_TEST_TEXTURES
        End Get
    End Property

    <DisplayName("Raster culling"), Category("Culling")>
    Public Property CULLING_raster_culling As Boolean
        Set(value As Boolean)
            USE_RASTER_CULLING = value
        End Set
        Get
            Return USE_RASTER_CULLING
        End Get
    End Property

#If False Then
    ' TODO
    <DisplayName("Textures (Kb)"), Category("Statictics")>
    Public ReadOnly Property STAT_textures As Integer
        Get
            Return GLTexture.ALL_SIZE / 1024
        End Get
    End Property

    ' TODO
    <DisplayName("Renderbuffers (Kb)"), Category("Statictics")>
    Public ReadOnly Property STAT_renderbuffers As Integer
        Get
            Return GLRenderbuffer.ALL_SIZE / 1024
        End Get
    End Property
#End If

    <DisplayName("Buffers (Kb)"), Category("Statictics")>
    Public ReadOnly Property STAT_buffers As Integer
        Get
            Return GLBuffer.ALL_SIZE / 1024
        End Get
    End Property


    <DisplayName("Enabled"), Category("Shadow Mapping")>
    Public Property SHADOW_MAPPING_enabled As Boolean
        Set(value As Boolean)
            ShadowMappingFBO.ENABLED = value
            If value Then
                deferredShader.SetDefine("SHADOW_MAPPING")
                cullShader.SetDefine("SHADOW_MAPPING")
            Else
                deferredShader.UnsetDefine("SHADOW_MAPPING")
                cullShader.UnsetDefine("SHADOW_MAPPING")
            End If
        End Set
        Get
            Return ShadowMappingFBO.ENABLED
        End Get
    End Property

    <DisplayName("zNear"), Category("Shadow Mapping")>
    Public Property SHADOW_MAPPING_near As Single
        Set(value As Single)
            ShadowMappingFBO.NEAR = value
        End Set
        Get
            Return ShadowMappingFBO.NEAR
        End Get
    End Property

    <DisplayName("zFar"), Category("Shadow Mapping")>
    Public Property SHADOW_MAPPING_far As Single
        Set(value As Single)
            ShadowMappingFBO.FAR = value
        End Set
        Get
            Return ShadowMappingFBO.FAR
        End Get
    End Property
End Class

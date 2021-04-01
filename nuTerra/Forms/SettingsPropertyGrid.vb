Imports System.ComponentModel
Imports System.Text.RegularExpressions

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
        PerViewData._start = 75
        PerViewData._end = 200
        FieldOfView = CSng(Math.PI) * (My.Settings.fov / 180.0F)

        'Get block state of things we want to block loading to speed things up for testing/debugging
        DONT_BLOCK_BASES = My.Settings.load_bases
        DONT_BLOCK_DECALS = My.Settings.load_decals
        DONT_BLOCK_MODELS = My.Settings.load_models
        DONT_BLOCK_SKY = My.Settings.load_sky
        DONT_BLOCK_TERRAIN = My.Settings.load_terrain
        DONT_BLOCK_TREES = My.Settings.load_trees
        DONT_BLOCK_WATER = My.Settings.load_water
    End Sub

    <DisplayName("FoV"), Category("Camera")>
    Public Property Camera_FoV As Single
        Set(value As Single)
            If MIN_FOV <= value And value <= MAX_FOV Then
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
            If MIN_NEAR <= value And value <= MAX_NEAR Then
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
            If MIN_FAR <= value And value <= MAX_FAR Then
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
            If MIN_SPEED <= value And value <= MAX_SPEED Then
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
            MAX_ZOOM_OUT = value
        End Set
        Get
            Return MAX_ZOOM_OUT
        End Get
    End Property

    <DisplayName("Position"), Category("Camera")>
    Public ReadOnly Property Camera_position As String
        Get
            Return CAM_POSITION.ToString.Replace("(", "").Replace(")", "")
        End Get
    End Property

    <DisplayName("Target"), Category("Camera")>
    Public ReadOnly Property Camera_target As String
        Get
            Return CAM_TARGET.ToString.Replace("(", "").Replace(")", "")
        End Get
    End Property

    <DisplayName("Start"), Category("Terrain")>
    Public Property Terrain_Start As Single
        Set(value As Single)
            PerViewData._start = value
        End Set
        Get
            Return PerViewData._start
        End Get
    End Property

    <DisplayName("End"), Category("Terrain")>
    Public Property Terrain_End As Single
        Set(value As Single)
            PerViewData._end = value
        End Set
        Get
            Return PerViewData._end
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
            If -4.0 <= value And value <= 4.0 Then
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
            If 1 <= value And value <= 1024 Then
                WORK_GROUP_SIZE = value
            End If
        End Set
        Get
            Return WORK_GROUP_SIZE
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
    Public Property OVERLAYS_test_textures As Single
        Set(value As Single)
            SHOW_TEST_TEXTURES = value
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
End Class

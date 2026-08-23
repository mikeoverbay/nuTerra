Imports System.Text
Imports OpenTK.Mathematics

Module modGlobalVars
    Public map_scene As MapScene

    Public BG_MAX_VALUE As Integer
    Public BG_VALUE As Integer
    Public BG_TEXT As String

    ' VT params
    Public UPLOADS_PER_FRAME As Integer = 1
    Public NUM_TILES As Integer = 1280
    Public VT_NUM_PAGES As Integer = 1024
    Public TILE_SIZE As Integer = 256
    Public FEEDBACK_WIDTH As Integer = 32
    Public FEEDBACK_HEIGHT As Integer = 32

    ' https://www.khronos.org/registry/OpenGL/extensions/NV/NV_representative_fragment_test.txt
    Public USE_REPRESENTATIVE_TEST As Boolean

    ' https://github.com/nvpro-samples/gl_occlusion_culling
    Public USE_RASTER_CULLING As Boolean = True

    Public WORK_GROUP_SIZE As Integer = 32

    Public USE_TESSELLATION As Boolean = False

    'Shading
    Public DUMMY_ATLAS As GLTexture
    Public FXAA_enable As Boolean = True
    Public FXAA_text As String = "FXAA On"
    Public TIME_OF_DAY As Single
    Public SUN_SCALE As Single
    Public RIPPLE_FRAME_NUMBER As Integer
    Public RIPPLE_MASK_TIME As Single
    Public MAX_MAP_HEIGHT As Single
    Public MIN_MAP_HEIGHT As Single
    Public MEAN_MAP_HEIGHT As Double
    Public TOTAL_HEIGHT_COUNT As Integer

    '============================================================
    ' this setting tweaks the mip biasing!
    Public GLOBAL_MIP_BIAS As Single = -0.75

    '============================================================
    'Render related
    Public T1_Y As Single
    Public T2_Y As Single
    Public DELTA_TIME As Single
    Public NORMAL_DISPLAY_MODE As Integer ' 0 None, 1 by vertex, 2 by face
    Public SHOW_BOUNDING_BOXES As Boolean
    Public LOOP_COUNT As Integer = 200
    Public FPS_COUNTER As Integer
    Public FPS_TIME As Integer
    Public DONT_HIDE_HUD As Boolean = True
    Public DONT_HIDE_MINIMAP As Boolean = True
    Public SHOW_LOD_COLORS As Boolean
    'ascii characters
    Public ASCII_ID As GLTexture
    'wire flags
    Public WIRE_MODELS As Boolean
    Public WIRE_DECALS As Boolean
    Public WIRE_TERRAIN As Boolean
    Public WIRE_OUTLAND As Boolean
    'grid display
    Public SHOW_CHUNKS As Boolean
    Public SHOW_GRID As Boolean
    Public SHOW_BORDER As Boolean
    Public SHOW_CHUNK_IDs As Boolean
    'models
    Public DIRECTION_TEXTURE_ID As GLTexture
    Public MINI_WORLD_MOUSE_POSITION As Vector2
    Public MINI_MOUSE_CAPTURED As Boolean
    '============================================================
    ' background images
    Public nuTERRA_BG_IMAGE As GLTexture
    Public CHECKER_BOARD As GLTexture
    '============================================================
    Public LIGHT_POS As Vector3
    Public LIGHT_RADIUS As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_X As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_Z As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE As Single
    Public PAUSE_ORBIT As Boolean = True
    Public LIGHT_SPEED As Single = 0.02F


    '============================================================
    'mouse camera related
    Public MOVE_CAM_Z, M_DOWN, MOVE_MOD, Z_MOVE, M_SPIN As Boolean ' mouse control booleans
    Public WASD_SPEED As Single = 0
    Public WASD_VECTOR As Point
    Public M_MOUSE As New Point
    Public SHOW_CURSOR As Integer
    '============================================================
    Public PROJECTIONMATRIX As New Matrix4
    Public VIEWMATRIX As New Matrix4
    '============================================================
    'Map related
    Public PLAYER_FIELD_CELL_SIZE As Single
    Public MAP_SIZE As Vector2
    Public MINI_MAP_SIZE As Integer = 240
    Public MINI_MAP_NEW_SIZE As Integer = 240
    Public MAP_NAME_NO_PATH As String = ""
    Public STARTUP_MAP As String ' optional map name passed on the command line
    Public MAP_LOADED As Boolean = False 'Rendering/settings clause
    Public TEMP_STORAGE As String 'Work are on users SSD/HDD
    Public DUMMY_TEXTURE_ID As GLTexture 'texture id 
    Public MAP_SELECT_BACKGROUND_ID As GLTexture 'texture id 
    '
    Public SHOW_MAPS_SCREEN As Boolean = False 'show pick menu screen
    Public SHOW_LOADING_SCREEN As Boolean = False 'show loading screen flag
    Public BLOCK_MOUSE As Boolean 'pick menu flag
    Public FINISH_MAPS As Boolean 'pick menu flag
    '
    Public EXPORT_STL_MAP As Boolean

    'Block loading flags. They are used for skipping loading of data.
    Public DONT_BLOCK_TERRAIN As Boolean
    Public DONT_BLOCK_OUTLAND As Boolean
    Public DONT_BLOCK_TREES As Boolean
    '''<summary>Tree placements that survived the frustum this frame, and in total.</summary>
    Public TREES_DRAWN As Integer
    Public TREES_TOTAL As Integer
    '''<summary>Tree placements drawn into the shadow map this update.</summary>
    Public TREES_CASTING As Integer
    Public DONT_BLOCK_DECALS As Boolean
    '''<summary>Fade decals out at the box edge when their texture runs to its border.</summary>
    ''' <summary>
    ''' L2 spherical harmonics for the map's diffuse ambient, 9 RGB coefficients,
    ''' read from environments/&lt;env&gt;/probes/global/rem_sh.xml. The game lights
    ''' ambient with these instead of a flat colour, which is what gives surfaces
    ''' form in shade - a wall facing the sky picks up blue, one facing the ground
    ''' picks up warm bounce. Identity fallback is a flat grey so a map without
    ''' probes still renders.
    ''' </summary>
    Public SH_AMBIENT(8) As Vector3
    ''' <summary>Brightest luminance in the probe, from rem_sh.xml/max_lum.</summary>
    Public SH_MAX_LUM As Single = 1.0F
    ''' <summary>True once rem_sh.xml has been read for the current map.</summary>
    Public SH_AMBIENT_LOADED As Boolean
    ''' <summary>Use the SH probe for ambient instead of the flat constant.</summary>
    Public USE_SH_AMBIENT As Boolean = True

    '''<summary>Show a quad at every GFX_model placement - the particle scaffolding.</summary>
    Public SHOW_GFX_MARKERS As Boolean = True

    Public DECAL_EDGE_FADE As Boolean = True
    Public DONT_BLOCK_MODELS As Boolean = False
    Public DONT_BLOCK_BASES As Boolean
    Public DONT_BLOCK_SKY As Boolean
    Public DONT_BLOCK_WATER As Boolean
    '---------------------
    Public WATER_LINE As Single
    '---------------------
    Public TEAM_1 As Vector3
    Public TEAM_2 As Vector3
    Public MAP_BB_UR As Vector2
    Public MAP_BB_BL As Vector2

End Module

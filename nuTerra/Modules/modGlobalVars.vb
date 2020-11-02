Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK

Module modGlobalVars

    '=================================================================================
    'map pick Dictionary
    Public PICK_DICTIONARY As New Dictionary(Of UInteger, String)
    Public PICKED_STRING As String = ""
    Public PICKED_MODEL_INDEX As Integer
    '=================================================================================
    Public Sub clear_output()
        Try
            Dim dte = Marshal.GetActiveObject("VisualStudio.DTE.17.0") 'change to version of visual studio
            dte.ExecuteCommand("Edit.ClearOutputWindow")
        Catch
        End Try
    End Sub
    '=================================================================================
    Public checkerTest As Integer
    'Define these in CAP TEXT
    Public TEST_IDS(7) As Integer
    Public M_POS As Vector2
    '============================================================
    Public nuTerra_LOG As New StringBuilder ' for logging
    '============================================================

    ' https://www.khronos.org/registry/OpenGL/extensions/NV/NV_mesh_shader.txt
    Public USE_NV_MESH_SHADER As Boolean
    ' https://www.khronos.org/registry/OpenGL/extensions/NV/NV_draw_texture.txt
    Public USE_NV_DRAW_TEXTURE As Boolean
    Public USE_SPIRV_SHADERS As Boolean

    'Shading
    Public SUNCOLOR As Vector3
    Public AMBIENTSUNCOLOR As Vector3
    Public SSAA_enable As Boolean = True
    Public SSAA_text As String = "SSAA On"
    Public TIME_OF_DAY As Single
    Public SUN_SCALE As Single
    Public SUN_TEXTURE_PATH As String
    Public SUN_TEXTURE_ID As Integer
    Public SUN_RENDER_COLOR As Vector3
    Public CC_LUT_ID As Integer

    Public RIPPLE_FRAME_NUMBER As Integer
    Public RIPPLE_TEXTURES() As Integer
    Public RIPPLE_MASK_TEXTURE As Integer
    Public RIPPLE_MASK_TIME As Single
    Public ENV_BRDF_LUT As Integer
    '============================================================
    ' this setting tweaks the mip biasing!
    Public GLOBAL_MIP_BIAS As Single = -0.75F
    '============================================================
    'temp test texture ids
    Public color_id, normal_id, gmm_id As Integer
    Public m_color_id, m_normal_id, m_gmm_id As Integer
    '============================================================
    'Render related
    Public T1_Y As Single
    Public T2_Y As Single
    Public DELTA_TIME As Single
    Public NORMAL_DISPLAY_MODE As Integer ' 0 None, 1 by vertex, 2 by face
    Public SHOW_BOUNDING_BOXES As Boolean
    Public FRAME_TIMER As New Stopwatch
    Public LOOP_COUNT As Integer = 200
    Public FPS_COUNTER As Integer
    Public FPS_TIME As Integer
    Public PICK_MODELS As Boolean = False
    Public DONT_HIDE_HUD As Boolean = True
    Public DONT_HIDE_MINIMAP As Boolean = True
    Public SHOW_LOD_COLORS As Integer
    'ascii characters
    Public ASCII_ID As Integer
    'wire flags
    Public WIRE_MODELS As Boolean
    Public WIRE_DECALS As Boolean
    Public WIRE_TERRAIN As Boolean
    'grid display
    Public SHOW_CHUNKS As Integer
    Public SHOW_GRID As Integer
    Public SHOW_BORDER As Integer
    Public SHOW_CHUNK_IDs As Integer
    Public SHOW_TEST_TEXTURES As Integer = 0 'show test textures on terrain flag. default Off.
    'models
    Public CURSOR_TEXTURE_ID As Integer
    Public DIRECTION_TEXTURE_ID As Integer
    Public CROSS_HAIR_TIME As Single = 0.0F ' animation time 0-1
    Public PROGRESS_BAR_IMAGE_ID As Integer
    Public MINI_WORLD_MOUSE_POSITION As Vector2
    Public MINI_MOUSE_CAPTURED As Boolean
    Public MINI_NUMBERS_ID As Integer
    Public MINI_LETTERS_ID As Integer
    Public MINI_TRIM_VERT_ID As Integer
    Public MINI_TRIM_HORZ_ID As Integer
    Public CUBE_TEXTURE_ID As Integer
    Public CUBE_TEXTURE_PATH As String
    '============================================================
    'load screen background image
    Public nuTERRA_BG_IMAGE As Integer
    '============================================================
    Public N_MAP_TYPE As Integer '<------------- temp value
    '============================================================
    Public LIGHT_POS As Vector3
    Public LIGHT_RADIUS As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_X As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_Z As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE As Single
    Public PAUSE_ORBIT As Boolean = True
    Public LIGHT_SPEED As Single = 0.02F
    '============================================================
    Public TERRAIN_TRIS_DRAWN As UInt32
    '============================================================
    Public _STARTED As Boolean 'Signals UI initialization is complete

    'mouse camera related
    Public MOVE_CAM_Z, M_DOWN, MOVE_MOD, Z_MOVE, M_SPIN As Boolean ' mouse control booleans
    Public WASD As Boolean
    Public WASD_SPEED As Single = 0
    Public WASD_VECTOR As Point
    Public M_MOUSE, MOUSE As New Point
    Public VIEW_RADIUS, CAM_X_ANGLE, CAM_Y_ANGLE As Single
    Public LOOK_AT_X, LOOK_AT_Y, LOOK_AT_Z As Single
    Public U_VIEW_RADIUS, U_CAM_X_ANGLE, U_CAM_Y_ANGLE As Single
    Public U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z As Single
    Public MOUSE_SPEED_GLOBAL As Single = 0.8
    Public CAM_POSITION As Vector3
    Public MAX_ZOOM_OUT As Single = -2000.0F 'must be negitive
    Public SHOW_CURSOR As Integer
    '============================================================
    Public PROJECTIONMATRIX As New Matrix4
    Public VIEWMATRIX As New Matrix4
    Public PRESPECTIVE_NEAR As Single = 0.1F
    Public PRESPECTIVE_FAR As Single = 3000.0F
    '============================================================
    'Map related
    Public PLAYER_FIELD_CELL_SIZE As Single
    Public MAP_SIZE As Vector2
    Public MINI_MAP_SIZE As Integer = 240
    Public MINI_MAP_NEW_SIZE As Integer = 240
    Public MAP_NAME_NO_PATH As String = ""
    Public MAP_LOADED As Boolean = False 'Rendering/settings clause
    Public TEMP_STORAGE As String 'Work are on users SSD/HDD
    Public GAME_PATH As String 'Points directly to "world_of_tanks\res\packages\"
    Public FIRST_UNUSED_TEXTURE As Integer 'Used for deltion of textures. holds starting texture
    Public FIRST_UNUSED_VB_OBJECT As Integer 'Used for deltion of VBO
    Public FIRST_UNUSED_V_BUFFER As Integer 'Used for deltion of V Bufffers
    Public DUMMY_TEXTURE_ID As Integer 'texture id 
    Public MAP_SELECT_BACKGROUND_ID As Integer 'texture id 
    Public TEXT_OVERLAY_MAP_PICK As Integer 'texture id for text on icons
    '
    Public SHOW_MAPS_SCREEN As Boolean = False 'show pick menu screen
    Public SHOW_LOADING_SCREEN As Boolean = False 'show loading screen flag
    Public SELECTED_MAP_HIT = 0 'pick menu flag
    Public BLOCK_MOUSE As Boolean 'pick menu flag
    Public FINISH_MAPS As Boolean 'pick menu flag
    Public USE_HD_TEXTURES As Boolean = True 'Lets the map loader know if we want to try and find HD textures.
    Public HD_EXISTS As Boolean 'Flag that the user has HD files in the packages folder.
    '
    'Draw Enable Flags. Items wont be rendered if these are False
    Public TERRAIN_LOADED As Boolean
    Public TREES_LOADED As Boolean
    Public DECALS_LOADED As Boolean
    Public MODELS_LOADED As Boolean
    Public BASES_LOADED As Boolean
    Public SKY_LOADED As Boolean
    Public WATER_LOADED As Boolean

    'Block loading flags. They are used for skipping loading of data.
    Public DONT_BLOCK_TERRAIN As Boolean
    Public DONT_BLOCK_TREES As Boolean
    Public DONT_BLOCK_DECALS As Boolean
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
    Public TEAM_1_ICON_ID As Integer
    Public TEAM_2_ICON_ID As Integer

End Module

Imports System.Threading
Imports OpenTK
Module modGlobalVars
    'Define these in CAP TEXT

    '---------------------
    'temp test texture ids
    Public color_id, normal_id, gmm_id As Integer
    Public m_color_id, m_normal_id, m_gmm_id As Integer
    '---------------------
    'Render related
    Public FRAME_TIMER As New Stopwatch
    Public LOOP_COUNT As Integer = 150
    Public FPS_COUNTER As Integer
    Public FPS_TIME As Integer
    '---------------------
    Public N_MAP_TYPE As Integer
    '---------------------
    Public LIGHT_POS(3) As Single
    Public LIGHT_RADIUS As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE As Single 'Used when orbiting the light
    Public PAUSE_ORBIT As Boolean
    Public LIGHT_SPEED As Single = 0.01F
    '---------------------
    Public total_triangles_drawn As UInt32
    '---------------------
    Public _STARTED As Boolean 'Signals UI initialization is complete
    '---------------------
    Public SYNCMUTEX As New Mutex 'Used to stop rendering during FBO and shader rebuilds
    '---------------------
    'mouse camera related
    Public MOVE_CAM_Z, M_DOWN, MOVE_MOD, Z_MOVE As Boolean ' mouse control booleans
    Public M_MOUSE, MOUSE As New Point
    Public VIEW_RADIUS, CAM_X_ANGLE, CAM_Y_ANGLE As Single
    Public LOOK_AT_X, LOOK_AT_Y, LOOK_AT_Z As Single
    Public U_VIEW_RADIUS, U_CAM_X_ANGLE, U_CAM_Y_ANGLE As Single
    Public U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z As Single
    Public MOUSE_SPEED_GLOBAL As Single = 0.8
    Public CAM_POSITION As Vector3
    '---------------------
    Public PROJECTIONMATRIX As New Matrix4
    Public MODELVIEWMATRIX As New Matrix4
    Public VIEW_PORT(1) As Single
    '---------------------
    'Map related
    Public MAP_NAME_NO_PATH As String = ""
    Public MAP_LOADED As Boolean = False 'Rendering/settings clause
    Public TEMP_STORAGE As String 'Work are on users SSD/HDD
    Public GAME_PATH As String 'Points directly to "world_of_tanks\res\packages\"
    Public FIRST_UNUSED_TEXTURE As Integer 'Used for deltion of textures. holds starting texture
    Public DUMMY_TEXTURE_ID As Integer 'texture id 
    Public MAP_SELECT_BACKGROUND_ID As Integer 'texture id 
    Public TEXT_OVERLAY_MAP_PICK As Integer 'texture id for text on icons
    '
    Public SHOW_MAPS = False 'pick menu flag
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
    Public TERRAIN_BLOCK_LOADING As Boolean
    Public TREES_BLOCK_LOADING As Boolean
    Public DECALS_BLOCK_LOADING As Boolean
    Public MODELS_BLOCK_LOADING As Boolean
    Public BASES_BLOCK_LOADING As Boolean
    Public SKY_BLOCK_LOADING As Boolean
    Public WATER_BLOCK_LOADING As Boolean
    '---------------------
    Public WATER_LINE As Single
    '---------------------

End Module

﻿Imports System.Threading
Imports OpenTK
Module modGlobalVars
    'Define these in CAP TEXT

    '---------------------
    'temp test texture ids
    Public color_id, normal_id, gmm_id As Integer
    '---------------------
    Public N_MAP_TYPE As Integer
    '---------------------
    Public LIGHT_POS(3) As Single
    Public LIGHT_RADIUS As Single ' Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE As Single ' Used when orbiting the light
    Public PAUSE_ORBIT As Boolean
    Public LIGHT_SPEED As Single = 0.01F
    '---------------------
    Public MAP_NAME_NO_PATH As String = ""
    Public MAP_LOADED As Boolean = False 'Rendering/settings clause
    '---------------------
    Public _STARTED As Boolean ' signals initialization is complete
    '---------------------
    Public SYNCMUTEX As New Mutex ' used to stop rendering during FBO and shader rebuilds
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
    Public TEMP_STORAGE As String
    Public GAME_PATH As String
    Public FIRST_UNUSED_TEXTURE As Integer
    Public DUMMY_TEXTURE_ID As Integer
    Public MAP_SELECT_BACKGROUND_ID As Integer
    Public SHOW_MAPS = False
    Public SELECTED_MAP_HIT = 0
    Public BLOCK_MOUSE As Boolean
    Public FINISH_MAPS As Boolean
    Public TEXT_OVERLAY_MAP_PICK As Integer
    '---------------------
    Public WATER_LINE As Single
    '---------------------

End Module

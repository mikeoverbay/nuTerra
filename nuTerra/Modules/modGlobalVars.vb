Imports System.Threading
Module modGlobalVars
    '---------------------
    Public _STARTED As Boolean ' signafies initialization is complete
    '---------------------
    Public SyncMutex As New Mutex ' used to stop rendering during FBO and shader rebuilds
    '---------------------
    Public MOVE_CAM_Z, M_DOWN, MOVE_MOD As Boolean ' mouse control booleans
    '---------------------

End Module

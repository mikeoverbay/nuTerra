Imports System.Threading
Module modGlobalVars
    '---------------------
    Public _STARTED As Boolean ' signafies initialization is complete
    '---------------------
    Public SynchMutex As New Mutex ' used to stop rendering during FBO and shader rebuilds
End Module

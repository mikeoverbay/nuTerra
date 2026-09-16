''' <summary>
''' Where the World of Tanks install is, asked ONE way.
'''
''' Callers used to read My.Settings.GamePath directly, which ties them to a
''' settings store - and that store is keyed to the EXECUTABLE'S PATH, so a
''' second app built from these same source files reads a different
''' user.config that has never heard of this machine's game folder.
'''
''' Brain Testing links several of these files and supplies its OWN GAME_PATH
''' that reads the sidecar at %LOCALAPPDATA%\nuTerra\game_path.txt - the file
''' Window.write_game_path_sidecar already keeps for exactly this reason,
''' "somewhere every build can find it". One name, two backings, and neither
''' app has to know about the other's.
'''
''' Added 2026-09-15 by nuTerra work, stage 1 of docs\brain_testing_plan.md.
''' </summary>
Module modGamePath

    ''' <summary>The game folder, or "" when one has never been chosen. Read
    ''' only: Window still OWNS setting it, because picking a folder is a
    ''' dialog and that belongs with the window.</summary>
    Public Function GAME_PATH() As String
        Dim p = My.Settings.GamePath
        If p Is Nothing Then Return ""
        Return p
    End Function

End Module

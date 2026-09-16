Imports System.IO

''' <summary>
''' Brain Testing's answer to GAME_PATH - the SIDECAR, not a settings store.
'''
''' nuTerra keeps the game folder in two places: My.Settings, and a plain text
''' file at %LOCALAPPDATA%\nuTerra\game_path.txt. The sidecar exists because
''' My.Settings is keyed to the EXECUTABLE'S PATH, so a build landing in a
''' different folder reads a user.config that has never seen the game - and
''' Upgrade() cannot rescue it, because a different path is a different
''' identity, not an earlier version of the same one.
'''
''' That makes the sidecar exactly right for a SECOND APP. BrainTesting.exe is
''' a different path by definition, so a settings store of its own would start
''' empty and prompt the owner for a folder he has already chosen once. Reading
''' the sidecar means Brain Testing is pointed at the game the moment nuTerra
''' has been run once, and never asks.
'''
''' VALIDATED, not trusted: a sidecar naming a moved or deleted install is
''' ignored rather than believed, the same test Window.adopt_game_path_sidecar
''' makes - a folder with no res\ in it is not a game.
'''
''' Added 2026-09-15 by nuTerra work, stage 1 of docs\brain_testing_plan.md.
''' </summary>
Module BrainGamePath

    Private cached As String = Nothing

    ''' <summary>The game folder, or "" if the sidecar is missing or stale.
    ''' Cached after the first read: this is asked once per loader and the
    ''' answer cannot change while the app is up.</summary>
    Public Function GAME_PATH() As String
        If cached IsNot Nothing Then Return cached
        cached = read_sidecar()
        Return cached
    End Function

    Private Function read_sidecar() As String
        Dim f = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nuTerra", "game_path.txt")
        Try
            If Not File.Exists(f) Then
                Console.WriteLine(
                    "no game path: {0} does not exist. Run nuTerra once and point it at the game.", f)
                Return ""
            End If
            ' TRIMMED OF ITS BOM as well as its whitespace. The file is written
            ' with File.WriteAllText, which prepends a UTF-8 BOM, and a path
            ' beginning with an invisible U+FEFF fails Directory.Exists with a
            ' message that names the folder and looks correct.
            Dim p = File.ReadAllText(f).Trim().Trim(ChrW(&HFEFF))
            If p = "" OrElse Not Directory.Exists(Path.Combine(p, "res")) Then
                Console.WriteLine("game path sidecar names {0}, which has no res\ - ignoring it", p)
                Return ""
            End If
            Console.WriteLine("game path: {0}", p)
            Return p
        Catch ex As Exception
            Console.WriteLine("could not read the game path sidecar - {0}", ex.Message)
            Return ""
        End Try
    End Function

End Module

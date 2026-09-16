''' <summary>
''' The world: the game's packages, and what can be read out of them.
'''
''' At stage 1 it opens the packages and names the spaces. The terrain meshes
''' and the buildings arrive in stages 2 and 3 - see
''' docs/brain_testing_plan.md.
'''
''' Added 2026-09-15 by nuTerra work.
''' </summary>
Module BrainWorld

    ''' <summary>Every installed space, once ResMgr has been opened.</summary>
    Public Spaces As List(Of String) = New List(Of String)

    ''' <summary>True once the packages are open and readable.</summary>
    Public Ready As Boolean = False

    ''' <summary>
    ''' Open the game's packages.
    '''
    ''' Returns False rather than throwing when there is no game to open: a
    ''' harness that cannot find the install should say so plainly and leave
    ''' the window standing, not die inside a loader with a stack trace about
    ''' a missing pkg.
    ''' </summary>
    Public Function Init() As Boolean
        Dim wot = GAME_PATH()
        If wot = "" Then
            LogThis("no game path - the world cannot be opened")
            Return False
        End If

        Dim sw = Stopwatch.StartNew()
        Try
            ResMgr.Init(wot)
        Catch ex As Exception
            LogThis("packages failed to open: {0}", ex.Message)
            Return False
        End Try

        Spaces = ResMgr.SpaceNames()
        LogThis("packages open in {0} ms, {1} space(s) installed",
                sw.ElapsedMilliseconds, Spaces.Count)

        If Spaces.Count = 0 Then
            ' Open but empty is a DIFFERENT failure from not opening, and
            ' worth its own line: it means the path is a folder with a res\ in
            ' it that is not a game, which the sidecar test cannot catch.
            LogThis("...but no spaces were found. Is {0} really the game?", wot)
            Return False
        End If

        Ready = True
        Return True
    End Function

    ''' <summary>
    ''' Is this the name of an installed space?
    '''
    ''' EXACT, not a prefix and not a substring. "19_monastery" must not be
    ''' answered by "19_monastery_winter", and a bare "19" must not match
    ''' anything at all - a map name is a structured token and gets a
    ''' structured test. This repository has paid four times in one day for
    ''' the other kind.
    ''' </summary>
    Public Function HasSpace(name As String) As Boolean
        If name Is Nothing Then Return False
        For Each s In Spaces
            If String.Equals(s, name, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ''' <summary>The installed spaces whose names begin with this, for telling
    ''' the owner what he might have meant when a name misses.</summary>
    Public Function NearMisses(name As String, limit As Integer) As List(Of String)
        Dim hits As New List(Of String)
        If name Is Nothing OrElse name = "" Then Return hits
        ' The first chunk before an underscore - "19" out of "19_monastery" -
        ' so a typo in the tail still finds the right neighbourhood.
        Dim head = name.Split("_"c)(0)
        For Each s In Spaces
            If s.StartsWith(head, StringComparison.OrdinalIgnoreCase) Then
                hits.Add(s)
                If hits.Count >= limit Then Exit For
            End If
        Next
        Return hits
    End Function

End Module

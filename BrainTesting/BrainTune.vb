''' <summary>
''' KNOBS THE COMMAND LINE CAN TURN, so a search does not need a compiler.
'''
''' "find best by trying different logic" - the owner, 2026-09-18.
'''
''' A trial that needs a rebuild costs thirty seconds of compiler and carries a
''' real chance of not compiling at all, which ends a sweep at 3am with nobody
''' watching. A trial that only needs a different argument costs nothing and
''' cannot fail to build. So every value worth searching over comes through
''' here instead of being a Const.
'''
''' `tune=commit=12,blockonly=1` on the command line. Unknown names are kept
''' rather than rejected - a knob that has not been wired up yet is a typo the
''' sweep log will show, and refusing to start is a worse answer than running
''' with a default.
'''
''' DEFAULTS LIVE AT THE CALL SITE, not here. Get(name, fallback) means the
''' code that uses a number still says what it wants when nobody is tuning,
''' which keeps the file readable on its own.
'''
''' Added 2026-09-18 by Tank AI work.
''' </summary>
Module BrainTune

    Private ReadOnly knobs As New Dictionary(Of String, Single)(
        StringComparer.OrdinalIgnoreCase)

    ''' <summary>What was asked for, verbatim, for the scorecard to echo. A
    ''' sweep with thirty rows in it is unreadable unless every row says what
    ''' it was.</summary>
    Public Spec As String = ""

    ''' <summary>
    ''' WHERE A SWEEP LEAVES ITS KNOBS. Read once at startup if it exists.
    '''
    ''' A file rather than an argument because `tune=a=1,b=2` never survived
    ''' the trip through the shell - it was recognised by the parser and
    ''' arrived empty, and half an hour of a measured hour went into asking
    ''' why. A file has no quoting rules and the sweep can read back exactly
    ''' what the run was given, which an argument cannot.
    ''' </summary>
    Public Const FILE_ As String =
        "C:/nuTerra_shared/tank_logs/sweep/tune.txt"

    Public Sub LoadFile()
        Try
            If Not IO.File.Exists(FILE_) Then Return
            Parse(IO.File.ReadAllText(FILE_).Trim())
        Catch ex As Exception
            LogThis("brain: could not read the tune file - {0}", ex.Message)
        End Try
    End Sub

    Public Sub Parse(spec As String)
        Spec = spec
        knobs.Clear()
        If spec Is Nothing OrElse spec.Length = 0 Then Return
        For Each pair In spec.Split(","c)
            Dim eq = pair.IndexOf("="c)
            If eq <= 0 Then Continue For
            Dim name = pair.Substring(0, eq).Trim()
            Dim v As Single
            If Single.TryParse(pair.Substring(eq + 1).Trim(),
                               Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, v) Then
                knobs(name) = v
            End If
        Next
        If knobs.Count > 0 Then
            LogThis("brain: tuning {0} knob(s) - {1}", knobs.Count, spec)
        End If
    End Sub

    Public Function Get_(name As String, fallback As Single) As Single
        Dim v As Single
        If knobs.TryGetValue(name, v) Then Return v
        Return fallback
    End Function

    ''' <summary>A knob read as a switch. Anything above a half is on.</summary>
    Public Function On_(name As String, fallback As Boolean) As Boolean
        Return Get_(name, If(fallback, 1.0F, 0.0F)) > 0.5F
    End Function

End Module

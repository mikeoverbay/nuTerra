Imports System.IO
Imports OpenTK.Mathematics

''' <summary>
''' WHAT WON LAST TIME, in this situation. THE BOOK, NOT THE DRIVER.
'''
''' NAMING, because the first pair of names was confusing and the owner said
''' so: everything in this app that is a PART OF THE APP is Brain-something
''' - BrainNav, BrainRadar, BrainGoal, BrainMemory. Everything that is a
''' BRAIN, in the IBrain sense, is something-Brain: NullBrain, SeekBrain,
''' LearnBrain. So this file is what is known, and LearnBrain is what drives
''' and writes to it.
'''
''' "you make your own candidates. I want it to learn as it tries." - the
''' owner, 2026-09-16.
'''
''' THE SHAPE, and why it is this one. A tank that is stuck has a small number
''' of things it can do and no way to reason about which is right - that was
''' the conclusion of the whole algorithm survey earlier this week. So it tries
''' one, scores what happened, and remembers the score against the SITUATION it
''' was in. Next time that situation comes up it reaches for what worked.
'''
''' That is a bandit, one per situation, and it is deliberately not something
''' cleverer. A policy gradient or a planner needs a model of the world; this
''' needs only the ability to tell two situations apart and to score an
''' outcome, both of which we already have.
'''
''' THE SITUATION KEY IS COARSE ON PURPOSE. Five sectors across the front arc,
''' four distance bands each, and eight buckets of bearing to the goal. That is
''' 8,192 possible keys and a real run touches a few dozen - which is the point:
''' a key so precise that every frame is unique learns nothing, because no
''' situation ever repeats. Coarse keys collide, and colliding is what lets one
''' attempt inform the next.
'''
''' SCORING, from the owner: time and travel to reach the goal, and a move that
''' runs more than MAX_LEG_M scores ZERO. That cap is doing real work - without
''' it "drive half way round the map" wins on a technicality, and every move
''' stops being a local decision.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Module BrainMemory

    ''' <summary>A single move may not run further than this. Over it, the
    ''' attempt scores zero however well it ended - the owner's rule.</summary>
    Public Const MAX_LEG_M As Single = 40.0F

    ''' <summary>How often to try something other than the best known move.
    ''' Without this the first lucky answer is the only answer ever tried.</summary>
    Public Const EXPLORE As Single = 0.25F

    Public Const STORE As String = "C:\nuTerra_shared\tank_ai_work\brain\learned.csv"

    ''' <summary>
    ''' The candidates. Mine to choose, and chosen to be the things a tank can
    ''' actually do rather than the things an algorithm would like it to.
    '''
    ''' A move is a relative heading and whether to back up first. Everything
    ''' else - how far, when to stop - is the same for all of them, so two
    ''' moves differ in exactly one way and the score means something.
    ''' </summary>
    Public Structure Move
        Public name As String
        Public turnDeg As Single
        Public reverse As Boolean
    End Structure

    Public ReadOnly MOVES As Move() = {
        New Move With {.name = "ahead", .turnDeg = 0.0F},
        New Move With {.name = "r15", .turnDeg = 15.0F},
        New Move With {.name = "l15", .turnDeg = -15.0F},
        New Move With {.name = "r30", .turnDeg = 30.0F},
        New Move With {.name = "l30", .turnDeg = -30.0F},
        New Move With {.name = "r45", .turnDeg = 45.0F},
        New Move With {.name = "l45", .turnDeg = -45.0F},
        New Move With {.name = "r60", .turnDeg = 60.0F},
        New Move With {.name = "l60", .turnDeg = -60.0F},
        New Move With {.name = "r90", .turnDeg = 90.0F},
        New Move With {.name = "l90", .turnDeg = -90.0F},
        New Move With {.name = "back-r45", .turnDeg = 45.0F, .reverse = True},
        New Move With {.name = "back-l45", .turnDeg = -45.0F, .reverse = True}
    }

    ''' <summary>What one move is worth, where it has been tried.</summary>
    Public Class Record
        Public tries As Integer = 0
        Public scored As Integer = 0        ' tries that produced a real score
        Public zeros As Integer = 0         ' over the leg cap: thrown out
        Public total As Double = 0.0
        Public best As Double = Double.MinValue

        ''' <summary>Over the SCORED tries only.
        '''
        ''' "it can just repeat and thow out scores of zero" - the owner. A leg
        ''' that ran past the cap did not measure the move, it measured the
        ''' cap, and averaging that in drags a good move down for a reason
        ''' that has nothing to do with the move.
        '''
        ''' The try is still COUNTED, because a move that always overruns has
        ''' to stop being offered as untried - otherwise it is picked first,
        ''' forever, and never learns anything.
        ''' </summary>
        Public ReadOnly Property mean As Double
            Get
                Return If(scored = 0, 0.0, total / scored)
            End Get
        End Property
    End Class

    ' situation key -> move name -> record
    Private ReadOnly table As New Dictionary(Of String, Dictionary(Of String, Record))
    Private ReadOnly rng As New Random(12345)     ' seeded: two runs compare

    ''' <summary>
    ''' The situation, as a short string.
    '''
    ''' Front arc only. What is behind a tank does not change which way it
    ''' should go round what is in front of it, and folding the rear arc in
    ''' would double the key length for a distinction nothing acts on.
    ''' </summary>
    Public Function Situation(hits As BrainRadar.Hit(), bearingToGoal As Single) As String
        If hits Is Nothing OrElse hits.Length = 0 Then Return "none"
        Dim front As New List(Of BrainRadar.Hit)
        For Each q In hits
            If q.front Then front.Add(q)
        Next
        If front.Count = 0 Then Return "none"

        Dim sb As New Text.StringBuilder(8)
        Dim per = Math.Max(1, front.Count \ 5)
        For s = 0 To 4
            Dim lo = s * per
            Dim hi = Math.Min(front.Count, lo + per) - 1
            Dim nearest = BrainRadar.REACH_M
            For i = lo To hi
                nearest = Math.Min(nearest, front(i).dist)
            Next
            ' Four bands. 5 m is inside braking distance, 10 m is a decision,
            ' 20 m is the reach - anything past it is simply open.
            If nearest < 5.0F Then
                sb.Append("X"c)
            ElseIf nearest < 10.0F Then
                sb.Append("n"c)
            ElseIf nearest < BrainRadar.REACH_M Then
                sb.Append("f"c)
            Else
                sb.Append("."c)
            End If
        Next

        ' Where the goal is, in eight 45 degree buckets. A situation is only
        ' the same situation if the way OUT is in the same place.
        Dim b = CInt(Math.Floor(((bearingToGoal + Math.PI) / (Math.PI * 2.0)) * 8.0)) Mod 8
        If b < 0 Then b += 8
        sb.Append("|"c)
        sb.Append(b.ToString())
        Return sb.ToString()
    End Function

    ''' <summary>Which move to try here. Best known, or something new.</summary>
    Public Function Choose(key As String) As Move
        Dim row As Dictionary(Of String, Record) = Nothing
        If Not table.TryGetValue(key, row) Then
            table(key) = New Dictionary(Of String, Record)
            row = table(key)
        End If

        ' ANYTHING UNTRIED FIRST. A move with no score is worth more than a
        ' second sample of one that has one - there is no information in
        ' repeating yourself until everything has been seen once.
        Dim untried As New List(Of Move)
        For Each m In MOVES
            If Not row.ContainsKey(m.name) Then untried.Add(m)
        Next
        If untried.Count > 0 Then Return untried(rng.Next(untried.Count))

        If rng.NextDouble() < EXPLORE Then Return MOVES(rng.Next(MOVES.Length))

        Dim bestMove = MOVES(0)
        Dim bestVal = Double.MinValue
        For Each m In MOVES
            Dim r As Record = Nothing
            If Not row.TryGetValue(m.name, r) Then Continue For
            If r.mean > bestVal Then bestVal = r.mean : bestMove = m
        Next
        Return bestMove
    End Function

    ''' <summary>
    ''' Score an attempt and remember it.
    '''
    ''' gained   metres of straight-line progress toward the goal, signed
    ''' travelled how far the hull actually drove
    ''' seconds  how long it took
    '''
    ''' The score is progress per metre driven, docked for time. Progress alone
    ''' rewards a move that wanders there; distance alone rewards standing
    ''' still. A leg over MAX_LEG_M scores zero whatever it achieved.
    ''' </summary>
    Public Function Score(gained As Single, travelled As Single, seconds As Single) As Double
        If travelled > MAX_LEG_M Then Return 0.0
        If travelled <= 0.01F Then Return -1.0          ' went nowhere: worse than useless
        Dim efficiency = gained / travelled             ' 1.0 is straight at it
        Return efficiency - 0.02 * seconds
    End Function

    Public Sub Remember(key As String, moveName As String, score As Double)
        Dim row As Dictionary(Of String, Record) = Nothing
        If Not table.TryGetValue(key, row) Then
            row = New Dictionary(Of String, Record)
            table(key) = row
        End If
        Dim r As Record = Nothing
        If Not row.TryGetValue(moveName, r) Then
            r = New Record()
            row(moveName) = r
        End If
        r.tries += 1
        If score = 0.0 Then
            ' THROWN OUT, not averaged in. Counted, so it is not untried.
            r.zeros += 1
            Return
        End If
        r.scored += 1
        r.total += score
        If score > r.best Then r.best = score
    End Sub

    Public ReadOnly Property Situations As Integer
        Get
            Return table.Count
        End Get
    End Property

    Public ReadOnly Property Attempts As Integer
        Get
            Dim n = 0
            For Each row In table.Values
                For Each r In row.Values
                    n += r.tries
                Next
            Next
            Return n
        End Get
    End Property

    ''' <summary>The best move for a situation, for the panel to show.</summary>
    Public Function BestFor(key As String) As String
        Dim row As Dictionary(Of String, Record) = Nothing
        If Not table.TryGetValue(key, row) OrElse row.Count = 0 Then Return "-"
        Dim nm = "-"
        Dim v = Double.MinValue
        For Each kv In row
            If kv.Value.mean > v Then v = kv.Value.mean : nm = kv.Key
        Next
        Return String.Format("{0} {1:0.00}", nm, v)
    End Function

    ''' <summary>
    ''' Keep it. A night of attempts is worth nothing if it dies with the
    ''' process, and this is the file the scoring gets read out of afterwards.
    ''' </summary>
    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(STORE))
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("situation,move,tries,scored,over_cap,mean,best")
            Dim inv = Globalization.CultureInfo.InvariantCulture
            For Each kv In table
                For Each mv In kv.Value
                    sb.AppendLine(String.Format(inv, "{0},{1},{2},{3},{4},{5:0.0000},{6:0.0000}",
                                                kv.Key, mv.Key, mv.Value.tries,
                                                mv.Value.scored, mv.Value.zeros,
                                                mv.Value.mean, mv.Value.best))
                Next
            Next
            File.WriteAllText(STORE, sb.ToString())
            LogThis("brain: learned {0} situation(s), {1} attempt(s) -> {2}",
                    table.Count, Attempts, STORE)
        Catch ex As Exception
            LogThis("brain: could not save what was learned - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>Pick up where the last run left off.</summary>
    Public Sub Load()
        Try
            If Not File.Exists(STORE) Then Return
            Dim inv = Globalization.CultureInfo.InvariantCulture
            Dim n = 0
            For Each line In File.ReadAllLines(STORE)
                Dim f = line.Split(","c)
                If f.Length < 7 OrElse f(0) = "situation" Then Continue For
                Dim tries, scored, zeros As Integer
                Dim mean, best As Double
                If Not Integer.TryParse(f(2), tries) Then Continue For
                If Not Integer.TryParse(f(3), scored) Then Continue For
                If Not Integer.TryParse(f(4), zeros) Then Continue For
                If Not Double.TryParse(f(5), Globalization.NumberStyles.Float, inv, mean) Then Continue For
                If Not Double.TryParse(f(6), Globalization.NumberStyles.Float, inv, best) Then Continue For
                If Not table.ContainsKey(f(0)) Then table(f(0)) = New Dictionary(Of String, Record)
                table(f(0))(f(1)) = New Record With {.tries = tries,
                                                     .scored = scored,
                                                     .zeros = zeros,
                                                     .total = mean * scored,
                                                     .best = best}
                n += 1
            Next
            LogThis("brain: loaded {0} learned row(s) over {1} situation(s)", n, table.Count)
        Catch ex As Exception
            LogThis("brain: could not load what was learned - {0}", ex.Message)
        End Try
    End Sub

End Module

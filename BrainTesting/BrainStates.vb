Imports OpenTK.Mathematics

''' <summary>
''' HOW MANY SITUATIONS DOES THE TANK ACTUALLY MEET?
'''
''' "can we store the relative possible combinations of hits? How many could
'''  there be" then "can we use that to flow thru the course?" - the owner,
''' 2026-09-24.
'''
''' THE COMBINATORICS SAY NO AND THE WORLD MIGHT SAY YES. 120 rays hit-or-miss
''' is 2^120; the front sixty alone is 1.15e18. Quantised to 8 sectors of 4
''' buckets it is 65,536 shapes, and with a desired-direction sector on top,
''' 524,288 states - a 512 KB table, unfillable by hand.
'''
''' But scans are not random. Chains are contiguous and angle-ordered, and a
''' hull only visits so many kinds of place. So the number that decides
''' whether a lookup table is a real idea is not the size of the space, it is
''' how much of it is ever OCCUPIED - and that is a measurement, not an
''' argument. This file takes it.
'''
''' MEASURING ONLY. Nothing steers on this and nothing should until the count
''' comes back: a table worth hand-tuning and a table needing machine learning
''' are different projects, and the difference is one number.
'''
''' THE ENCODING, and each choice is a claim that could be wrong:
'''
'''   drive, NOT dist. `dist` is where a half-metre line stops; `drive` is the
'''   same walk at the body's radius, which folds the terrain in. A bowl that
'''   reads as twenty clear metres on dist is a trap on drive - and a state
'''   built from dist would call the trap and the open road the same situation.
'''
'''   THE FRONT ARC ONLY. A full 360 encoding squares the space, and what is
'''   behind the tank does not decide where it drives next. 8 sectors over
'''   180 degrees is 22.5 degrees each, about six rays.
'''
'''   THE NEAREST RETURN IN A SECTOR WINS. A sector is "how close is the
'''   nearest thing this way", which is the question the driving asks.
''' </summary>
Module BrainStates

    ''' <summary>Off unless `states` is on the command line. It costs a
    ''' dictionary insert a tick, which is nothing, but a diagnostic that runs
    ''' when nobody asked is a diagnostic nobody reads.</summary>
    Public ON_ As Boolean = False

    Private Const SECTORS As Integer = 8
    Private Const DIRS As Integer = 8

    ''' <summary>Metres. Under NEAR is "touching", under FAR is "near",
    ''' anything further that still returned is "far", and a miss is "open".
    ''' NEAR is about a hull length; FAR is the range a turn has to fit in.</summary>
    Private Const NEAR_M As Single = 5.0F
    Private Const FAR_M As Single = 20.0F

    ''' <summary>shape -> times seen, and the full state -> times seen. Both,
    ''' because they answer different questions: the shapes are what a human
    ''' would have to look at, the states are what a table would have to
    ''' hold.</summary>
    Private ReadOnly shapes As New Dictionary(Of Integer, Integer)
    Private ReadOnly states As New Dictionary(Of Integer, Integer)
    Private ticks As Long = 0

    ''' <summary>
    ''' One tick's situation, folded into an integer.
    '''
    ''' Two bits a sector, low sector first, then three bits of desired
    ''' direction on top. 8*2 + 3 = 19 bits, so it fits an Integer with room
    ''' to spare and can be printed as one number.
    ''' </summary>
    Public Sub Note(hits As BrainRadar.Hit(), wantRel As Single)
        If Not ON_ OrElse hits Is Nothing Then Return

        ' Nearest return per sector, in metres, starting at "nothing here".
        Dim near_(SECTORS - 1) As Single
        For i = 0 To SECTORS - 1
            near_(i) = Single.MaxValue
        Next

        For Each q In hits
            If Not q.front Then Continue For
            ' angle is relative to the nose and the front arc is +/- pi/2, so
            ' this lands 0..SECTORS-1 with -90 deg in sector 0.
            Dim t = (q.angle + CSng(Math.PI) * 0.5F) / CSng(Math.PI)
            If t < 0.0F OrElse t >= 1.0F Then Continue For
            Dim s = CInt(Math.Floor(t * SECTORS))
            If s < 0 OrElse s >= SECTORS Then Continue For
            ' A MISS IS NOT A ZERO. A ray that found nothing carries a dist of
            ' REACH_M, and taking the minimum blindly would let a miss beat a
            ' real return and report open ground in front of a wall.
            If Not q.found Then Continue For
            If q.drive < near_(s) Then near_(s) = q.drive
        Next

        Dim shape = 0
        For i = 0 To SECTORS - 1
            Dim b As Integer
            If near_(i) = Single.MaxValue Then
                b = 3                      ' open - nothing returned this way
            ElseIf near_(i) < NEAR_M Then
                b = 0                      ' touching
            ElseIf near_(i) < FAR_M Then
                b = 1                      ' near
            Else
                b = 2                      ' far
            End If
            shape = shape Or (b << (i * 2))
        Next

        ' Desired direction, wrapped to 0..2pi then cut into DIRS sectors.
        Dim a = wantRel
        While a < 0.0F
            a += CSng(Math.PI * 2.0)
        End While
        While a >= CSng(Math.PI * 2.0)
            a -= CSng(Math.PI * 2.0)
        End While
        Dim d = CInt(Math.Floor(a / CSng(Math.PI * 2.0) * DIRS)) Mod DIRS

        Dim st = shape Or (d << (SECTORS * 2))

        ticks += 1
        shapes(shape) = If(shapes.ContainsKey(shape), shapes(shape), 0) + 1
        states(st) = If(states.ContainsKey(st), states(st), 0) + 1
    End Sub

    ''' <summary>
    ''' What was seen, written where it can be read rather than logged where
    ''' it scrolls past. Appends, so several runs accumulate into one answer -
    ''' the question is about the WORLD, not about one run.
    ''' </summary>
    Public Sub Dump(map As String)
        If Not ON_ OrElse ticks = 0 Then Return
        Try
            IO.Directory.CreateDirectory(BrainPanel.OUT_DIR)
            Dim p = IO.Path.Combine(BrainPanel.OUT_DIR, map & "_states.txt")
            Dim sb As New Text.StringBuilder()
            sb.AppendLine(String.Format(
                "# {0}  ticks {1:N0}  distinct shapes {2:N0} of 65,536  " &
                "distinct states {3:N0} of 524,288",
                DateTime.Now.ToString("s"), ticks, shapes.Count, states.Count))
            sb.AppendLine(String.Format(
                "# occupancy: shapes {0:0.000}%  states {1:0.000}%",
                100.0 * shapes.Count / 65536.0,
                100.0 * states.Count / 524288.0))
            sb.AppendLine("# sector buckets: 0 touching(<5m) 1 near(<20m) 2 far 3 open")
            sb.AppendLine("# shape, times seen, sectors left-to-right")
            Dim ranked = New List(Of KeyValuePair(Of Integer, Integer))(shapes)
            ranked.Sort(Function(x, y) y.Value.CompareTo(x.Value))
            Dim shown = 0
            For Each kv In ranked
                Dim row As New Text.StringBuilder()
                For i = 0 To SECTORS - 1
                    row.Append((kv.Key >> (i * 2)) And 3)
                Next
                sb.AppendLine(String.Format("{0},{1},{2}", kv.Key, kv.Value, row))
                shown += 1
                If shown >= 200 Then Exit For
            Next
            If ranked.Count > shown Then
                sb.AppendLine(String.Format("# ... and {0:N0} more shapes",
                                            ranked.Count - shown))
            End If
            IO.File.AppendAllText(p, sb.ToString())
            LogThis("brain: states - {0:N0} ticks, {1:N0} distinct shapes, " &
                    "{2:N0} distinct states -> {3}",
                    ticks, shapes.Count, states.Count, p)
        Catch ex As Exception
            LogThis("brain: states would not write - {0}", ex.Message)
        End Try
    End Sub

End Module

Imports OpenTK.Mathematics

''' <summary>
''' WHAT THE BRAIN DID, COUNTED AND SAID OUT LOUD - and kept out of the brain.
'''
''' "lets move all heads up shit out of the AI body" - the owner, 2026-09-17.
'''
''' RangeBrain had grown a second job. Beside the driving there were seven
''' counters, a double-tap detector, a once-a-second heartbeat with
''' twenty-two format arguments, and a diagnostic that binary-searches a
''' clearance radius - none of which decides anything. A tank that drove
''' perfectly and a tank that reported perfectly were the same 1,629 lines,
''' and every change to one was a chance to break the other.
'''
''' SO THE SPLIT IS BY WHETHER IT STEERS. Everything here is read by a person
''' and by nothing else: take it all out and the hull drives exactly the same
''' route. That is the test for whether a thing belongs in this file.
'''
''' THE COUNTERS EARNED THEIR PLACE and are not decoration. Two suspicions
''' about why the hull stopped - the rear rays triggering a reversal, and a
''' probe repeating from one spot - were unfalsifiable by watching, and both
''' came back zero, which is how the real cause got found somewhere else
''' entirely. The hit rate printed beside a miss rate is what exposed a cache
''' answering nothing. A number nobody can see is a number nobody can check.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainReport

    ' ---- what the run has done ------------------------------------------
    Public Probes As Integer = 0
    Public DblTaps As Integer = 0
    Public RearTrigs As Integer = 0
    Public RearLastDeg As Single = 0.0F
    Public Doors As Integer = 0
    Public Traps As Integer = 0

    ''' <summary>The last opening the vote looked at, and what straight on was
    ''' worth at the same moment. Printed because "it never turned" and "it
    ''' never found anything to turn to" look identical from outside and have
    ''' completely different fixes.</summary>
    Public BestScore As Single = -1.0F
    Public AheadScore As Single = -1.0F
    Public BestDeg As Single = 0.0F

    ' ---- HOW LONG WE WERE FORCED TO SLOW DOWN ----------------------------
    '
    ' "heck how long we are forced to decelerate as a box on our score card.
    '  also how long at what speed. if we drop to a craw, we are not looking
    '  ahead" - the owner, 2026-09-17.
    '
    ' THIS IS AN ANTICIPATION METRIC WEARING A SPEEDOMETER. Nothing makes the
    ' hull crawl except arriving somewhere it should have seen coming: creep()
    ' brakes on the room the BODY has, so time at the floor is time spent
    ' close to something that was in the scan twenty metres earlier. A route
    ' that never crawls was planned; a route that crawls for a third of its
    ' length was reacted to.
    '
    ' Banded by THROTTLE rather than by speed, because the question is what the
    ' brain ASKED for. A hull doing 1 m/s because it just started moving is
    ' not the same failure as one doing 1 m/s because the brain refuses to
    ' give it more, and only the command tells them apart.
    Public SecFull As Single = 0.0F       ' 0.95+     nothing in the way
    Public SecCruise As Single = 0.0F     ' 0.60-0.95 braking, in good time
    Public SecSlow As Single = 0.0F       ' 0.36-0.60 late
    Public SecCrawl As Single = 0.0F      ' at the creep floor - too late
    Public SecBack As Single = 0.0F       ' reversing
    Public SecStill As Single = 0.0F      ' commanded nothing at all

    ''' <summary>The longest UNBROKEN crawl. A total of twelve seconds spread
    ''' over a route is braking; twelve seconds in one stretch is a hull
    ''' feeling its way along something it never planned around, and the mean
    ''' cannot tell the two apart.</summary>
    Public CrawlWorst As Single = 0.0F
    Private crawlRun As Single = 0.0F
    Private metres As Single = 0.0F

    ''' <summary>Called every tick with what the brain asked for and what the
    ''' world gave.</summary>
    Public Sub Motion(dt As Single, throttle As Single, speed As Single)
        metres += Math.Abs(speed) * dt
        If throttle < -0.01F Then
            SecBack += dt
            crawlRun = 0.0F
        ElseIf throttle <= 0.01F Then
            SecStill += dt
            crawlRun = 0.0F
        ElseIf throttle <= 0.36F Then
            SecCrawl += dt
            crawlRun += dt
            If crawlRun > CrawlWorst Then CrawlWorst = crawlRun
        ElseIf throttle < 0.6F Then
            SecSlow += dt
            crawlRun = 0.0F
        ElseIf throttle < 0.95F Then
            SecCruise += dt
            crawlRun = 0.0F
        Else
            SecFull += dt
            crawlRun = 0.0F
        End If
    End Sub

    ''' <summary>The box. Percentages of the run spent in each band, the worst
    ''' unbroken crawl, and the mean speed over everything.</summary>
    Public Function MotionBox() As String
        Dim t = SecFull + SecCruise + SecSlow + SecCrawl + SecBack + SecStill
        If t <= 0.01F Then Return "motion: -"
        Return String.Format("motion: full {0:0}% cruise {1:0}% slow {2:0}% " &
                             "CRAWL {3:0}% back {4:0}% still {5:0}% | worst crawl " &
                             "{6:0.0} s | mean {7:0.0} m/s",
                             100.0F * SecFull / t, 100.0F * SecCruise / t,
                             100.0F * SecSlow / t, 100.0F * SecCrawl / t,
                             100.0F * SecBack / t, 100.0F * SecStill / t,
                             CrawlWorst, metres / t)
    End Function

    ''' <summary>Where the run began and how far the goal was then.
    ''' Captured at Start, because the scorecard's only honest question is
    ''' how much of THE GAP IT WAS GIVEN it managed to shut - and by the
    ''' end the opening position is long gone.</summary>
    Public StartPos As Vector2
    Public StartGap As Single = -1.0F

    ''' <summary>Arrivals, nearest approach, and the time lost to going
    ''' slowly. Counted as the run happens, because a goal that respawns
    ''' on arrival makes any end-of-run distance meaningless.</summary>
    Public Goals As Integer = 0
    Public ClosestGap As Single = -1.0F
    Public Stalls As Integer = 0
    Public StallSecs As Single = 0.0F
    ''' <summary>The longest single stall, and where it happened. If every
    ''' trial dies at the same coordinates it is not a tuning problem, it
    ''' is one piece of ground - and no knob will move it.</summary>
    Public StallWorst As Single = 0.0F
    Public StuckAt As Vector2
    ''' <summary>Seconds since the closest approach improved. The measure
    ''' of a run that has stopped being worth watching.</summary>
    Public SinceGain As Single = 0.0F
    Private atGoal As Boolean = False
    Private stallRun As Single = 0.0F
    Private stallOpen As Boolean = False

    ''' <summary>
    ''' One tick of progress. Arrival uses HYSTERESIS - inside five metres
    ''' to count, outside eight to re-arm - or a hull parked on the line
    ''' scores an arrival every frame it jitters across it. The same
    ''' lesson as the wedge clock and the backing clock: nothing keyed on
    ''' an instant can settle.
    '''
    ''' A STALL IS HALF A SECOND UNDER A METRE A SECOND. Not a single slow
    ''' frame - that is a gear change, not a think - and counted as
    ''' EPISODES as well as seconds, because one four second think and
    ''' eight half second ones are the same percentage and completely
    ''' different problems.
    ''' </summary>
    Public Sub NoteProgress(gap As Single, speed As Single, dt As Single,
                            whereNow As Vector2)
        If ClosestGap < 0.0F OrElse gap < ClosestGap - 0.5F Then
            ClosestGap = gap
            SinceGain = 0.0F
        Else
            SinceGain += dt
        End If
        If gap <= 5.0F Then
            If Not atGoal Then
                Goals += 1
                atGoal = True
            End If
        ElseIf gap > 8.0F Then
            atGoal = False
        End If

        If Math.Abs(speed) < 1.0F Then
            stallRun += dt
            StallSecs += dt
            If stallRun >= 0.5F AndAlso Not stallOpen Then
                Stalls += 1
                stallOpen = True
            End If
            If stallRun > StallWorst Then
                StallWorst = stallRun
                StuckAt = whereNow
            End If
        Else
            stallRun = 0.0F
            stallOpen = False
        End If
    End Sub

    ''' <summary>
    ''' One line, the same way every time, so two runs can be read side by
    ''' side.
    '''
    ''' closed% is the measure, not metres driven: a hull can drive a long
    ''' way round in a circle. Metres driven appears beside it as `path`,
    ''' because two brains that shut the same gap are not equal if one of
    ''' them took twice the road to do it.
    ''' </summary>
    ''' <summary>
    ''' ONE NUMBER, so a search can say better or worse without a human.
    '''
    '''   goals   x 100   arriving is the job, and nothing else counts until
    '''                   it has happened at least once.
    '''   closed  x 0.5   credit for ground genuinely shut, measured at the
    '''                   CLOSEST approach - not the end, or a run that
    '''                   arrived and drove on scores as if it never went.
    '''   pace    x 1     "slow as little as we can", literally.
    '''   deg/m   x -8    "smooth turns". Full lock at cruise is about
    '''                   2.2 deg/m, so eight points a degree makes the
    '''                   difference between smooth and fighting worth
    '''                   roughly twenty points - real, not decisive.
    '''   stalls  x -2    each separate stop-and-think.
    ''' </summary>
    Public Function Score() As Single
        Dim t = SecFull + SecCruise + SecSlow + SecCrawl + SecBack + SecStill
        Dim pace = If(t > 0.01F, 100.0F * (SecFull + SecCruise) / t, 0.0F)
        Dim closed = 0.0F
        If StartGap > 0.1F AndAlso ClosestGap >= 0.0F Then
            closed = Math.Clamp(100.0F * (StartGap - ClosestGap) / StartGap,
                                0.0F, 100.0F)
        End If
        Return 100.0F * Goals + 0.5F * closed + pace -
               8.0F * BrainTrail.TurnPerMetre() - 2.0F * Stalls
    End Function

    ''' <summary>One CSV row. A sweep reads a file; scraping a console is
    ''' one encoding away from lying about a number.</summary>
    Public Function ScoreRow(brain As String, secs As Single,
                             nowPos As Vector2, goal As Vector2) As String
        Dim t = SecFull + SecCruise + SecSlow + SecCrawl + SecBack + SecStill
        Dim pace = If(t > 0.01F, 100.0F * (SecFull + SecCruise) / t, 0.0F)
        Dim top = BrainNodes.TopActs(2)
        Return String.Format(Globalization.CultureInfo.InvariantCulture,
            "{0:0.00},{1},""{2}"",{3},{4:0.0},{5},{6:0.0},{7:0.0}," &
            "{8:0.0},{9:0.000},{10:0.0},{11:0.0},{12:0.0},{13:0.0},{14:0.0}," &
            "{15:0.0},""{16}"",{17},""{18}"",{19},{20:0.0},{21:0.0},{22:0.0}",
            Score(), brain, BrainTune.Spec, Goals, pace, Stalls, StallSecs,
            If(t > 0.01F, metres / t, 0.0F), BrainTrail.TurnDeg,
            BrainTrail.TurnPerMetre(), ClosestGap, (goal - nowPos).Length,
            metres, StartGap, secs,
            BrainNodes.Flips / Math.Max(1.0F, secs),
            top(0), top(1), top(2), top(3),
            StallWorst, StuckAt.X, StuckAt.Y)
    End Function

    Public ReadOnly ROW_HEADER As String =
        "score,brain,spec,goals,pace,stalls,stall_s,mean_ms,turn_deg," &
        "turn_per_m,closest,gap,path,start_gap,secs,flips_s," &
        "top1,top1_pct,top2,top2_pct,worst_stall,stuck_x,stuck_z"

    Public Function Scorecard(brain As String, secs As Single,
                              nowPos As Vector2, goal As Vector2,
                              arriveM As Single) As String
        Dim t = SecFull + SecCruise + SecSlow + SecCrawl + SecBack + SecStill
        Dim pace = If(t > 0.01F, 100.0F * (SecFull + SecCruise) / t, 0.0F)
        Return String.Format(
            "SCORE {0,-6} {1,3:0}s | goals {2} | pace {3,3:0}% | stalls {4,3} " &
            "({5,5:0.0}s) | mean {6,4:0.0} m/s | closest {7,6:0.0} m | " &
            "gap {8,6:0.0} m | path {9,6:0.0} m | turn {10,6:0} deg " &
            "({11,5:0.00} deg/m) | SCORE {12,6:0.0}",
            brain, secs, Goals, pace, Stalls, StallSecs,
            If(t > 0.01F, metres / t, 0.0F), ClosestGap,
            (goal - nowPos).Length, metres,
            BrainTrail.TurnDeg, BrainTrail.TurnPerMetre(), Score())
    End Function

    ''' <summary>The last why and throttle the brain produced, so a display can
    ''' read them without reaching into whichever IBrain happens to be
    ''' installed. The card over the tank wants them every frame; the brain
    ''' produces them once a tick.</summary>
    Public LastWhy As String = "-"
    Public LastThrottle As Single = 0.0F
    ''' <summary>Last steer command, -1..1. Published so the TURN RADIUS
    ''' can be read off the screen: radius = v / (steer * TURN_RATE). A
    ''' radius is the thing being complained about and it was the one
    ''' number nothing displayed.</summary>
    Public LastSteer As Single = 0.0F
    Public LastSpeed As Single = 0.0F

    Private probeFrom As Vector2
    Private probedOnce As Boolean = False
    Private beat As Single = 0.0F

    ''' <summary>A fresh run starts from nothing, or the second scenario of an
    ''' evening inherits the first one's tally and every comparison is off by
    ''' however long the app has been open.</summary>
    Public Sub Reset()
        Goals = 0
        ClosestGap = -1.0F
        StallWorst = 0.0F
        SinceGain = 0.0F
        BrainNodes.ResetCounts()
        Stalls = 0
        StallSecs = 0.0F
        atGoal = False
        stallRun = 0.0F
        stallOpen = False
        Probes = 0
        DblTaps = 0
        RearTrigs = 0
        RearLastDeg = 0.0F
        Doors = 0
        Traps = 0
        probedOnce = False
        beat = 0.0F
        SecFull = 0.0F
        SecCruise = 0.0F
        SecSlow = 0.0F
        SecCrawl = 0.0F
        SecBack = 0.0F
        SecStill = 0.0F
        CrawlWorst = 0.0F
        crawlRun = 0.0F
        metres = 0.0F
    End Sub

    ''' <summary>
    ''' RECORD A PROBE, AND WHETHER IT IS THE SECOND FROM THE SAME SPOT.
    '''
    ''' A metre is the threshold because a probe is a decision, and if the hull
    ''' has not moved a metre since the last one it is asking the same question
    ''' from the same place and will get the same answer. Entering the decision
    ''' twice from one spot is a loop, not progress, and it reads on screen as
    ''' a tank jiggling in place.
    ''' </summary>
    Public Sub Probe(pos As Vector2, what As String)
        Probes += 1
        If probedOnce AndAlso (pos - probeFrom).Length < 1.0F Then
            DblTaps += 1
            LogThis("brain: DOUBLE TAP - probe {0} ({1}) is {2:0.0} m from probe {3}. " &
                    "Nothing was driven in between.",
                    Probes, what, (pos - probeFrom).Length, Probes - 1)
        End If
        probeFrom = pos
        probedOnce = True
    End Sub

    ''' <summary>The rear won a reversal. The BEARING is recorded, not just the
    ''' fact: the rear arc is 120 degrees wide and a ray 55 degrees off the tail
    ''' looks sideways, so an outer winner is a bug and a middle one is an
    ''' answer. That distinction cost a run to find.</summary>
    Public Sub RearTrigger(offTailRad As Single, why As String)
        RearTrigs += 1
        RearLastDeg = MathHelper.RadiansToDegrees(offTailRad)
        LogThis("brain: REAR TRIGGER {0} - {1}, winning ray {2:0} deg off the tail. " &
                "Reversing rather than turning round.", RearTrigs, why, RearLastDeg)
    End Sub

    Public Sub Door(widthM As Single, aheadM As Single, needM As Single)
        Doors += 1
        LogThis("brain: DOOR {0} at {1:0.0} m ahead - {2:0.0} m wide, hull needs {3:0.0}. " &
                "Going through.", Doors, aheadM, widthM, needM)
    End Sub

    Public Sub Trap(col As Integer, row As Integer)
        Traps += 1
        LogThis("brain: TRAP {0} - every front ray blocked and the hull will not move. " &
                "Square ({1},{2}) marked once we are clear of it.", Traps, col, row)
    End Sub

    ''' <summary>
    ''' ONCE A SECOND, THE WHOLE STATE OF IT.
    '''
    ''' Two silent stops in one evening were silent because the paths that stop
    ''' do not log, and a hull standing still looks identical whatever it is
    ''' thinking. The cost of this line is that it is long; the cost of not
    ''' having it was hours.
    ''' </summary>
    Public Sub Heartbeat(dt As Single, state As String, why As String,
                         throttle As Single, speed As Single, rangeM As Single,
                         surf As BrainRadar.Surface)
        LastWhy = why
        LastThrottle = throttle
        LastSpeed = speed
        Motion(dt, throttle, speed)
        beat += dt
        If beat < 1.0F Then Return
        beat = 0.0F
        LogThis("brain: " & MotionBox())
        LogThis("brain: [{0}] {1} | thr {2:0.00} speed {3:0.0} range {4:0.0} " &
                "| surf: {5} (valid {6}, one {7}, turns {8}, face {9:0} deg) " &
                "| probes {10} (dbl {11}) rear-trig {12}{13} doors {14} " &
                "traps {19} (marked {20}) " &
                "| gap {21:0.0} @ {22:0} deg vs ahead {23:0.0} " &
                "| ai {15:0.00} ms (ground {16:0.00} ms, {17} queries, " &
                "{18} cells)",
                state, why, throttle, speed, rangeM,
                surf.verdict, surf.valid, surf.oneSurface, surf.turns,
                MathHelper.RadiansToDegrees(surf.normalRad),
                Probes, DblTaps, RearTrigs,
                If(RearTrigs > 0, String.Format(" (last {0:0} deg)", RearLastDeg), ""),
                Doors, BrainSim.TickMs, BrainNav.GroundMs,
                BrainNav.GroundMisses, BrainNav.CellTests,
                Traps, BrainNav.Learned, BestScore, BestDeg, AheadScore)
    End Sub

    ''' <summary>
    ''' THE WIDEST BODY THAT WOULD GET ONE METRE THIS WAY. Diagnostic only -
    ''' nothing steers on it.
    '''
    ''' Standable takes a RADIUS and tests an axis-aligned box of that half
    ''' size, so the rays clear a 1 m box while the hull asks for a 3.9 m one.
    ''' When those disagree the only useful question is BY HOW MUCH: a lane
    ''' that fits 1.2 m but not 1.94 is a real lane the margin is refusing, and
    ''' one that fits 0.2 m has a wall in it. That number is what turned "boxed
    ''' in" from a mystery into an arithmetic error in one reading.
    ''' </summary>
    Public Function WidestClear(pos As Vector2, headingRad As Single,
                                upTo As Single) As Single
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Dim px = pos.X + dx, pz = pos.Y + dz
        Dim r = upTo
        While r > 0.1F
            If BrainNav.Standable(px, pz, r) Then Return r
            r -= 0.1F
        End While
        Return 0.0F
    End Function

End Module

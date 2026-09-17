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

    ''' <summary>The last why and throttle the brain produced, so a display can
    ''' read them without reaching into whichever IBrain happens to be
    ''' installed. The card over the tank wants them every frame; the brain
    ''' produces them once a tick.</summary>
    Public LastWhy As String = "-"
    Public LastThrottle As Single = 0.0F
    Public LastSpeed As Single = 0.0F

    Private probeFrom As Vector2
    Private probedOnce As Boolean = False
    Private beat As Single = 0.0F

    ''' <summary>A fresh run starts from nothing, or the second scenario of an
    ''' evening inherits the first one's tally and every comparison is off by
    ''' however long the app has been open.</summary>
    Public Sub Reset()
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
                "traps {20} (marked {21}) " &
                "| gap {22:0.0} @ {23:0} deg vs ahead {24:0.0} " &
                "| ai {15:0.00} ms (ground {16:0.00} ms, {17} new / {18} cached, " &
                "{19} cells)",
                state, why, throttle, speed, rangeM,
                surf.verdict, surf.valid, surf.oneSurface, surf.turns,
                MathHelper.RadiansToDegrees(surf.normalRad),
                Probes, DblTaps, RearTrigs,
                If(RearTrigs > 0, String.Format(" (last {0:0} deg)", RearLastDeg), ""),
                Doors, BrainSim.TickMs, BrainNav.GroundMs,
                BrainNav.GroundMisses, BrainNav.CellHeightHits, BrainNav.CellTests,
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

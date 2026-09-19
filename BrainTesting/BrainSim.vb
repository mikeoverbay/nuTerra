Imports OpenTK.Mathematics

''' <summary>
''' The loop: gather the world, step the brain, apply what it says, write the
''' black box.
'''
''' THE SIM STARTS AT LAUNCH, and what it DOES is Tank AI's. The owner,
''' 2026-09-16: "We need to just start the sim at start but that need to be
''' under Tank AI's control." So this file owns the loop and the bookkeeping -
''' when to step, what to hand over, what to record - and owns none of the
''' driving. Install a brain and it drives; install none and NullBrain parks
''' everything, which is what ships.
'''
''' Nothing here decides where a tank should go. That is the line, and the
''' reason the loop can be measured before a single driving decision exists.
'''
''' Added 2026-09-16 by nuTerra work, stage 6 of docs/brain_testing_plan.md.
''' </summary>
Module BrainSim

    ''' <summary>The installed brain. NullBrain until something replaces it.
    ''' Public so Tank AI's code can set it without touching this file.</summary>
    Private drivingName As String = ""

    ''' <summary>
    ''' WHO IS DRIVING, said once each time it changes.
    '''
    ''' A run's log has to name the brain that produced it. Two scorecards
    ''' that do not say which brain they came from are not a comparison,
    ''' they are two numbers - and the switch is a checkbox, so getting it
    ''' wrong leaves no trace anywhere else.
    ''' </summary>
    Private refuseIn As Single = 0.0F

    ''' <summary>
    ''' WHY THE HULL DID NOT MOVE.
    '''
    ''' The refusal above is silent, and it is the end of every stuck run: the
    ''' brain commands full throttle, the speed reads zero, and nothing says
    ''' what failed. The numbers are the answer, so this prints them rather
    ''' than a count - but twice a second, because one a frame is sixty a
    ''' second and that is a window nobody can read.
    '''
    ''' It also asks whether a SMALLER box would have passed. That one extra
    ''' question separates the two cases, and they have nothing in common:
    ''' either the hull is somewhere tighter than its own drive box, which no
    ''' amount of brain work fixes, or the destination is fine for a hull and
    ''' we are asking for more room than we need.
    ''' </summary>
    ''' <summary>
    ''' THE WORLD SAID NO, and how many times in a row.
    '''
    ''' The refusal used to be a log line and nothing else, so a brain
    ''' could command the same impossible move on every tick of a seventy
    ''' second run and never learn anything from it. Measured: thr 0.10,
    ''' speed 0.0, for the last eighteen seconds of a run.
    ''' </summary>
    Public Refused As Boolean = False
    Public RefusedRun As Integer = 0

    Private Sub say_refused(h As BrainHull, want As Vector2, dt As Single)
        Refused = True
        RefusedRun += 1
        refuseIn -= dt
        If refuseIn > 0.0F Then Return
        refuseIn = 0.5F

        Dim r = h.DriveRadius
        Dim half = BrainNav.Standable(want.X, want.Y, r * 0.5F)
        Dim here = BrainNav.Standable(h.pos.X, h.pos.Y, r)
        LogThis("brain: MOVE REFUSED at ({0:0.0}, {1:0.0}) -> ({2:0.0}, {3:0.0}) " &
                "r {4:0.00} | half-r {5} | standing where we are {6}",
                h.pos.X, h.pos.Y, want.X, want.Y, r, half, here)
    End Sub

    Public Sub NoteDriver()
        Dim n = If(Brain Is Nothing, "none", Brain.Name)
        If n = drivingName Then Return
        drivingName = n
        LogThis("brain: driving with the {0} brain", n)
    End Sub

    Public Brain As IBrain = New NullBrain()

    Public Running As Boolean = False
    Private stepping As Boolean = False
    Public Frame As Integer = 0

    ''' <summary>How long the BRAIN took last tick, smoothed, in
    ''' milliseconds.
    '''
    ''' Measured around Brain.Tick alone - not the gather, not the apply, not
    ''' the log. Those are the harness and they cost what they cost; this is
    ''' the number that says whether the THINKING is affordable, which is the
    ''' question when a brain starts casting thirty rays a frame.
    ''' </summary>
    Public TickMs As Double = 0.0

    ''' <summary>What the brain last said each hull was doing. The panel
    ''' shows it so the reason a tank is not moving is on screen rather than
    ''' only in the log.</summary>
    Public LastWhy As String() = Nothing
    Private ReadOnly think As New Stopwatch()

    ''' <summary>How fast a hull moves at full throttle, and how fast it turns.
    ''' Deliberately crude: this is the WORLD applying a brain's numbers, not a
    ''' vehicle model. When the driving needs real physics it belongs in the
    ''' tank lane, not here.</summary>
    Private Const TOP_SPEED As Single = 12.0F      ' m/s

    ''' <summary>
    ''' HOW FAST A HULL SWINGS AT FULL STEER. Halved from 0.9 on the owner's
    ''' call - "it works but we need to not turn so sharp" - so 26 deg/s
    ''' rather than 52.
    '''
    ''' It is the TURN RADIUS that changed, not just the look of it. Steer and
    ''' throttle are independent here, so at 12 m/s the tightest circle the
    ''' hull could cut went from 13 m to 27 m: the brain's turns now describe
    ''' arcs a tank could actually hold, instead of pivots that happen to be
    ''' moving forward.
    '''
    ''' The probe turns take twice as long, which costs nothing - they are
    ''' taken at zero throttle and gated on ALIGNED, not on the clock. TEST_S
    ''' runs down during the swing but is only read after the hull is aligned,
    ''' where PEEK_SAMPLES holds the decision anyway.
    ''' </summary>
    ''' <summary>Radians a second at full steer - about 26 deg/s.
    ''' PUBLIC because a brain that plans a turn should plan it against
    ''' the rate it will actually get. The look-ahead distance is derived
    ''' from this, and a second copy of the number in the brain would be a
    ''' second thing to forget to change.</summary>
    Public Const TURN_RATE As Single = 0.45F      ' rad/s at full steer

    Private speeds() As Single

    Public Sub Start()
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then
            LogThis("brain: no hulls - the sim has nothing to run")
            Return
        End If
        Frame = 0
        ReDim speeds(BrainTanks.Bodies.Count - 1)
        ' A fresh run starts from nothing, or the second brain of a duel
        ' inherits the first one's tally and the comparison is a fiction.
        BrainReport.Reset()
        BrainTrail.Reset()

        ' THE OPENING GAP, before anything moves. A scorecard written at the
        ' end cannot ask this - by then the start is wherever the hull
        ' happens to be standing.
        If BrainTanks.Bodies.Count > BrainRadar.HULL Then
            BrainReport.StartPos = BrainTanks.Bodies(BrainRadar.HULL).spawn
            BrainReport.StartGap = If(BrainGoal.HasTarget,
                                      (BrainGoal.Target - BrainReport.StartPos).Length,
                                      -1.0F)
        End If
        BrainLog.StartRun(MAP_NAME_NO_PATH, Brain.Name)
        Brain.Start(Gather(0.0F))
        Running = True
        LogThis("brain: sim started with brain '{0}', {1} hull(s)",
                Brain.Name, BrainTanks.Bodies.Count)
    End Sub

    ''' <summary>
    ''' ONE TICK, then stop again. For looking at a single decision.
    '''
    ''' Tick refuses to run when the sim is halted, which is right - but it
    ''' means a stopped sim cannot be nudged forward, and a single decision
    ''' is the only thing small enough to actually study. dt is a nominal
    ''' frame rather than the real one, so stepping is repeatable.
    ''' </summary>
    Public Sub StepOnce()
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return
        Dim wasRunning = Running
        stepping = True
        If Not wasRunning Then
            If speeds Is Nothing OrElse speeds.Length <> BrainTanks.Bodies.Count Then
                ReDim speeds(BrainTanks.Bodies.Count - 1)
            End If
            Running = True
        End If
        Tick(1.0F / 60.0F)
        If Not wasRunning Then Running = False
        stepping = False
        journal()
    End Sub

    ''' <summary>
    ''' ONE LINE PER HAND-STEPPED TICK.
    '''
    ''' The owner steps through a turn and can see what went wrong; this
    ''' session cannot see his screen. Without a record the conversation
    ''' becomes him describing a picture and me guessing at numbers, which
    ''' has cost hours tonight.
    '''
    ''' Everything a turn is decided by, on one line: what won the tick and
    ''' why, the throttle and steer commanded against the speed actually
    ''' delivered - a gap between those two IS the world refusing - the
    ''' radius that comes out of them, and the point being chased.
    '''
    ''' Only hand steps. Sixty lines a second is noise; a stepped tick is a
    ''' decision somebody chose to look at.
    ''' </summary>
    Private Sub journal()
        Try
            Dim pos As Vector2, hdg As Single
            If BrainTanks.Bodies IsNot Nothing AndAlso
               BrainTanks.Bodies.Count > BrainRadar.HULL Then
                pos = BrainTanks.Bodies(BrainRadar.HULL).spawn
                hdg = BrainTanks.Bodies(BrainRadar.HULL).headingRad
            End If
            Dim st = Math.Abs(BrainReport.LastSteer)
            Dim v = Math.Abs(BrainReport.LastSpeed)
            Dim rad = "  -  "
            If st > 0.01F AndAlso v > 0.05F Then
                rad = (v / (st * TURN_RATE)).ToString("0.0") & " m"
            End If
            Dim act = "-"
            If BrainNodes.ActedNode >= 0 Then
                act = BrainNodes.NodeKind(BrainNodes.ActedNode) & "#" &
                      BrainNodes.ActedNode.ToString()
            End If
            Dim aim = "none"
            Dim gb = TryCast(Brain, GraphBrain)
            If gb IsNot Nothing AndAlso gb.PlanOn Then
                aim = String.Format(Globalization.CultureInfo.InvariantCulture,
                                    "{0:0.0},{1:0.0}", gb.PlanAim.X, gb.PlanAim.Y)
            End If
            Dim line = String.Format(Globalization.CultureInfo.InvariantCulture,
                "tick {0,4} | pos {1,7:0.0},{2,7:0.0} hdg {3,4:0} | {4,-20} | " &
                "thr {5,5:0.00} steer {6,6:+0.00;-0.00} speed {7,5:0.0} | radius {8,7} | " &
                "aim {9,-16} | {10}",
                Frame, pos.X, pos.Y, MathHelper.RadiansToDegrees(hdg),
                act, BrainReport.LastThrottle, BrainReport.LastSteer,
                BrainReport.LastSpeed, rad, aim, BrainReport.LastWhy)
            LogThis("brain: {0}", line)
            IO.Directory.CreateDirectory("C:/nuTerra_shared/tank_logs/sweep")
            IO.File.AppendAllText("C:/nuTerra_shared/tank_logs/sweep/steps.txt",
                                  line & Environment.NewLine)
        Catch ex As Exception
            LogThis("brain: step journal - {0}", ex.Message)
        End Try
    End Sub

    Public Sub Halt()
        If Not Running Then Return
        Running = False
        BrainLog.Close()
        LogThis("brain: sim stopped at frame {0}", Frame)
    End Sub

    ''' <summary>
    ''' One step. Called from the render loop.
    '''
    ''' dt IS CLAMPED, and that is not tidiness. A stall - a shader recompile,
    ''' the window being dragged - hands the next frame a dt of several
    ''' seconds, and a hull integrated across that teleports through whatever
    ''' was in front of it. A brain would then be scored on a collision the
    ''' world invented.
    ''' </summary>
    Public Sub Tick(dt As Single)
        If Not Running OrElse Brain Is Nothing Then Return
        dt = Math.Clamp(dt, 0.0F, 0.1F)
        If dt <= 0.0F Then Return

        ' The ground table is good for this tick only.
        BrainNav.NewTick()

        Dim inp = Gather(dt)
        Dim outp As BrainOutput
        Try
            think.Restart()
            NoteDriver()
        outp = Brain.Tick(inp)
            think.Stop()
            Dim ms = think.Elapsed.TotalMilliseconds
            TickMs = If(TickMs = 0.0, ms, TickMs * 0.9 + ms * 0.1)
        Catch ex As Exception
            ' A brain that throws stops the SIM, not the app. The owner is
            ' looking at a window; losing it to someone's null reference tells
            ' him nothing and costs him the state he was watching.
            '
            ' AND WHERE. A message on its own names the failure and not
            ' the place - "Index must be greater than or equal to zero"
            ' fits every array and every String.Format in the project
            ' equally well. The top of the stack is the difference
            ' between a fix and an afternoon of grep.
            LogThis("brain: '{0}' threw - {1}: {2}. Sim stopped.",
                    Brain.Name, ex.GetType().Name, ex.Message)
            For Each fr In top_frames(ex, 6)
                LogThis("brain:     {0}", fr)
            Next
            Halt()
            Return
        End Try

        LastWhy = outp.why
        Apply(inp, outp, dt)
        BrainLog.Note(inp, outp, Frame * CDbl(dt))
        ' SCORED AS IT HAPPENS. A goal that respawns on arrival cannot be
        ' scored at the whistle - by then the distance is to a goal the run
        ' never saw the start of.
        If BrainGoal.HasTarget AndAlso BrainTanks.Bodies IsNot Nothing AndAlso
           BrainTanks.Bodies.Count > BrainRadar.HULL Then
            Dim me_ = BrainTanks.Bodies(BrainRadar.HULL)
            BrainReport.NoteProgress((BrainGoal.Target - me_.spawn).Length,
                                     speeds(BrainRadar.HULL), dt, me_.spawn)
            BrainTrail.Note(me_.spawn, me_.headingRad)
        End If
        Frame += 1
        ' Asked for a fixed number of ticks: stop with the last one still
        ' drawn, rather than running on and overwriting the picture.
        ' Only the FREE-RUNNING sim stops itself at the limit. A deliberate
        ' step has already decided it wants one more.
        If TICK_LIMIT > 0 AndAlso Frame >= TICK_LIMIT AndAlso Not stepping Then
            LogThis("brain: stopping after {0} tick(s) - the picture stays",
                    Frame)
            Halt()
        End If
    End Sub

    ''' <summary>The first few stack frames, trimmed. Enough to name the
    ''' line without printing the whole harness underneath it.</summary>
    Private Function top_frames(ex As Exception, n As Integer) As List(Of String)
        Dim got As New List(Of String)
        Dim st = ex.StackTrace
        If st Is Nothing Then Return got
        For Each ln In st.Split(New String() {Environment.NewLine},
                                StringSplitOptions.RemoveEmptyEntries)
            got.Add(ln.Trim())
            If got.Count >= n Then Exit For
        Next
        Return got
    End Function

    ''' <summary>The world, as the brain sees it.</summary>
    Private Function Gather(dt As Single) As BrainInput
        Dim inp As New BrainInput With {.dt = dt, .frame = Frame}
        Dim n = BrainTanks.Bodies.Count
        ReDim inp.hulls(n - 1)
        For i = 0 To n - 1
            Dim b = BrainTanks.Bodies(i)
            inp.hulls(i) = New BrainHull With {
                .id = b.id, .team = b.team, .tag = b.tag,
                .pos = b.spawn, .y = b.y,
                .headingRad = b.headingRad,
                .speed = If(speeds Is Nothing, 0.0F, speeds(i)),
                .halfX = b.half.X, .halfZ = b.half.Z,
                .turretYawDeg = b.turretYawDeg, .gunPitchDeg = b.gunPitchDeg}

            ' The gun, from the vehicle's own def. PitchRangeAt is sampled at
            ' the CURRENT yaw, so a brain gets the limits where the turret
            ' actually is rather than the curve it came from.
            If b.vehicle IsNot Nothing Then
                inp.hulls(i).yawMinDeg = b.vehicle.yawMin
                inp.hulls(i).yawMaxDeg = b.vehicle.yawMax
                inp.hulls(i).yawRateDegS = b.vehicle.yawRate
                inp.hulls(i).pitchRateDegS = b.vehicle.pitchRate
                Dim pr = b.vehicle.PitchRangeAt(b.turretYawDeg)
                inp.hulls(i).pitchLowDeg = pr.X
                inp.hulls(i).pitchHighDeg = pr.Y
                inp.hulls(i).muzzleLocal = b.vehicle.muzzleLocal
                inp.hulls(i).hasMuzzle = b.vehicle.hasMuzzle
            End If
        Next
        Return inp
    End Function

    ''' <summary>
    ''' Move the hulls the brain's numbers say to move.
    '''
    ''' THROTTLE AND STEER ONLY - no teleporting a hull to a waypoint. If a
    ''' brain cannot drive somewhere with the controls a tank has, that is a
    ''' finding, and hiding it behind a position write is how the current
    ''' driving hides its own.
    '''
    ''' A hull that would end up somewhere unstandable simply does not move
    ''' this step. Crude, and correct in the way that matters: it stops where
    ''' the wall is, so the brain sees the wall.
    ''' </summary>
    Private Sub Apply(inp As BrainInput, outp As BrainOutput, dt As Single)
        For i = 0 To inp.hulls.Length - 1
            Dim b = BrainTanks.Bodies(i)

            Dim thr = clamp1(get_at(outp.throttle, i))
            Dim str = clamp1(get_at(outp.steer, i))

            b.headingRad += str * TURN_RATE * dt
            speeds(i) = thr * TOP_SPEED

            Dim step_m = speeds(i) * dt
            If step_m <> 0.0F Then
                Dim fwd As New Vector2(CSng(Math.Sin(b.headingRad)), CSng(Math.Cos(b.headingRad)))
                Dim want = b.spawn + fwd * step_m
                ' THE DRIVING RADIUS, not the rotating one. This line asked
                ' for a clear circle the size of the hull's DIAGONAL before it
                ' would let it move a metre forward, which on these hulls is an
                ' 8.5 m circle for a 3.4 m tank - and near any wall there is no
                ' such circle, so the hull could not move at all.
                If BrainNav.Standable(want.X, want.Y, inp.hulls(i).DriveRadius) Then
                    b.spawn = want
                    ' It moved. Whatever was refusing has stopped.
                    RefusedRun = 0
                Else
                    speeds(i) = 0.0F
                    say_refused(inp.hulls(i), want, dt)
                End If
            End If

            b.y = BrainNav.Ground(b.spawn.X, b.spawn.Y)
            BrainTanks.Bodies(i) = b
        Next
    End Sub

    Private Function get_at(a As Single(), i As Integer) As Single
        If a Is Nothing OrElse i >= a.Length Then Return 0.0F
        Return a(i)
    End Function

    Private Function clamp1(v As Single) As Single
        If Single.IsNaN(v) Then Return 0.0F
        Return Math.Max(-1.0F, Math.Min(1.0F, v))
    End Function

End Module

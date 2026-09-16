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
    Public Brain As IBrain = New NullBrain()

    Public Running As Boolean = False
    Public Frame As Integer = 0

    ''' <summary>How fast a hull moves at full throttle, and how fast it turns.
    ''' Deliberately crude: this is the WORLD applying a brain's numbers, not a
    ''' vehicle model. When the driving needs real physics it belongs in the
    ''' tank lane, not here.</summary>
    Private Const TOP_SPEED As Single = 12.0F      ' m/s
    Private Const TURN_RATE As Single = 0.9F       ' rad/s at full steer

    Private speeds() As Single

    Public Sub Start()
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then
            LogThis("brain: no hulls - the sim has nothing to run")
            Return
        End If
        Frame = 0
        ReDim speeds(BrainTanks.Bodies.Count - 1)

        BrainLog.StartRun(MAP_NAME_NO_PATH, Brain.Name)
        Brain.Start(Gather(0.0F))
        Running = True
        LogThis("brain: sim started with brain '{0}', {1} hull(s)",
                Brain.Name, BrainTanks.Bodies.Count)
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

        Dim inp = Gather(dt)
        Dim outp As BrainOutput
        Try
            outp = Brain.Tick(inp)
        Catch ex As Exception
            ' A brain that throws stops the SIM, not the app. The owner is
            ' looking at a window; losing it to someone's null reference tells
            ' him nothing and costs him the state he was watching.
            LogThis("brain: '{0}' threw - {1}. Sim stopped.", Brain.Name, ex.Message)
            Halt()
            Return
        End Try

        Apply(inp, outp, dt)
        BrainLog.Note(inp, outp, Frame * CDbl(dt))
        Frame += 1
    End Sub

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
                If BrainNav.Standable(want.X, want.Y, inp.hulls(i).FitRadius) Then
                    b.spawn = want
                Else
                    speeds(i) = 0.0F
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

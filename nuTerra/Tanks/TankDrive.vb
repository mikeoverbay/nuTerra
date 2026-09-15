Imports OpenTK.Mathematics

''' <summary>Why a tank is standing still. Four causes that look identical
''' from outside and want completely different fixes.</summary>
Public Enum StopWhy
    Moving = 0
    ''' <summary>Swinging the hull round toward the goal. Not a problem.</summary>
    Turning = 1
    ''' <summary>Pointed the right way and still not moving - the one that is
    ''' always wrong.</summary>
    Aligned = 2
    ''' <summary>The navigation grid says the ground ahead is shut.</summary>
    Ground = 3
    ''' <summary>Another hull is in the way.</summary>
    Traffic = 4
    ''' <summary>Backing out of somewhere it could not get through.</summary>
    Reversing = 5
End Enum

''' <summary>
''' The tank's own driving brain. Terrain recovery is a state transition, not
''' a one-frame steering trick: after two failed static attempts the hull scans,
''' optionally backs out, turns to the best escape ray, then returns to Forward.
''' </summary>
Public Enum TankBrainState
    Forward = 0
    TerrainScan = 1
    TerrainReverse = 2
    TerrainTurn = 3
End Enum

Public Structure TankBrainScanRay
    Public origin As Vector2
    Public hit As Vector2
    Public distanceM As Single
    Public isWinner As Boolean
End Structure

''' <summary>
''' One tank's driving: where it is going, how fast, and what it does when the
''' way is shut.
'''
''' THIS IS STEERING, NOT PATHFINDING, and the difference is deliberate. A tank
''' here picks somewhere open, turns toward it and drives, and gives up on the
''' goal when something stops it. That handles open ground, woods and the space
''' between buildings; it will nose into a courtyard with one entrance and take
''' a few tries to leave. Route planning over the grid is the next piece and
''' slots in at pick_goal - everything else stays.
'''
''' PER TANK, WHICH THE SHUTTLE WAS NOT. What this replaces was a single shared
''' distance that slid all thirty hulls along their own headings together: fine
''' for checking that a track band scrolls at the right rate, and the reason
''' every tank on the map moved as one object.
'''
''' DETERMINISTIC, SO A CAPTURE REPEATS. Every step is taken against
''' ANIM_DELTA rather than the frame time, so two runs of the same build put
''' the same tank in the same place on the same frame - the only reason a still
''' of thirty moving vehicles can be compared with another still.
'''
''' This used to say "every random choice comes from a generator seeded off the
''' tank's own id". There are no random choices left here: the seeded generator
''' went with the random goal picker on 2026-09-12 and the routes come from the
''' catalogue, which is deterministic in its own right.
''' </summary>
Public Class TankDrive

    ''' <summary>Where this tank is trying to get to, in world XZ.</summary>
    Public goal As Vector2
    Public hasGoal As Boolean

    ''' <summary>Said once per hull, so a parked fleet explains itself in the
    ''' log without repeating it sixty times a second.</summary>
    Private noRouteLogged As Boolean

    ''' <summary>Set by TankRenderer.HandOutRoutes. Until it is true a hull
    ''' without a route has not been offered one yet, and saying so is noise
    ''' rather than news.</summary>
    Public Shared RoutesHandedOut As Boolean

    ''' <summary>The route this hull was handed at load, as world waypoints,
    ''' or Nothing to wander. See PickGoal.</summary>
    Public path As List(Of Vector2)

    ''' <summary>Which waypoint it is driving at.</summary>
    Public pathAt As Integer = 0

    ''' <summary>Set once, when the last waypoint is reached. This is the race
    ''' result: the first hull of a side to set it got its team to the enemy
    ''' base.</summary>
    Public arrived As Boolean = False

    ''' <summary>Set when the route this hull was given has stopped being
    ''' usable. The driver cannot re-plan - it has no idea where the bases are -
    ''' so it raises this and MapTanks cuts a fresh route from where the hull
    ''' now stands, using the current baked TankNav.</summary>
    Public wantsReplan As Boolean = False

    ''' <summary>Which way round an obstacle this hull committed to, +1 or -1,
    ''' and how long that choice still holds. Re-deciding every frame is how a
    ''' hull oscillates in the mouth of a gap.</summary>
    Public skirtSide As Integer = 0
    Public skirtS As Single = 0.0F

    ''' <summary>Where the SIM sent this hull, kept apart from `goal`.
    '''
    ''' THESE MUST BE TWO DIFFERENT THINGS. `goal` is where the hull is
    ''' steering RIGHT NOW, and skirting an obstacle or passing another tank
    ''' moves it to a tangent a few metres away. The first version of the sim
    ''' wrote the start point straight into `goal` every frame, which put it
    ''' back the instant either manoeuvre set it - so a hull could never
    ''' complete a way round anything. The destination persists here and
    ''' `goal` is free to be the next few metres.</summary>
    Public simTarget As Vector2
    Public hasSimTarget As Boolean = False

    ''' <summary>Seconds left of a go-around. While it runs the hull keeps
    ''' the tangent it turned onto instead of re-aiming at its
    ''' destination.</summary>
    Public passS As Single = 0.0F

    ''' <summary>How far the tangent sweep opens, and in what steps. Twelve
    ''' rings of 12 degrees reaches 144 degrees either side - past square to the
    ''' obstacle, which is as far as skirting can sensibly go before the way
    ''' round is genuinely behind you.</summary>
    Private Const SKIRT_RINGS As Integer = 12
    Private Const SKIRT_STEP_RAD As Single = 0.2094F
    Private Const SKIRT_HOLD_S As Single = 1.5F

    ''' <summary>The go-around: how far right to turn, how far to drive that
    ''' way, and how long to hold it before re-aiming.
    '''
    ''' RIGHT, ALWAYS RIGHT - the owner's rule: "if both turn right to their
    ''' heading, they should pass." That is the whole reason it works. Two
    ''' hulls meeting head-on each choose the same hand, so they swing apart
    ''' rather than mirroring each other into the same gap. A cleverer rule
    ''' that picked the roomier side per tank would have both of them pick
    ''' the same side of the road and meet again there.
    '''
    ''' Fifty degrees is enough to clear a hull's width in a couple of
    ''' lengths without turning the drive into a detour.</summary>
    Private Const PASS_TURN_RAD As Single = 0.9F
    Private Const PASS_M As Single = 12.0F
    Private Const PASS_HOLD_S As Single = 1.2F

    ''' <summary>Metres a second, ramped rather than set - a hull that reaches
    ''' its top speed in one frame reads as a slide, and the track band is
    ''' driven off distance so it would scroll in a step too.</summary>
    Public speed As Single

    ''' <summary>How long this tank has been unable to move. Drives the retry,
    ''' and tells a tank wedged against something from one merely waiting for
    ''' another to pass.</summary>
    Public stuckS As Single

    ''' <summary>Seconds on the current goal. A tank that has been driving at
    ''' one spot for a long time without arriving is going around something it
    ''' cannot get past, and is better off choosing again.</summary>
    Public goalS As Single

    ''' <summary>How long the way ahead has been shut. Paced apart from stuckS
    ''' because they answer different questions: this one decides when to try a
    ''' different goal, that one decides when the tank is genuinely wedged.
    ''' </summary>
    Public blockedS As Single

    ''' <summary>Seconds left of backing out. A tank wedged against something
    ''' cannot fix that by turning in place; it has to give itself room first.
    ''' </summary>
    Public reverseS As Single

    ''' <summary>Why this tank is not moving, this frame. Counted in the fleet
    ''' report: "ten of thirty moving" says a fleet is sluggish and nothing
    ''' about which of four quite different causes to go and fix.</summary>
    Public stopReason As StopWhy

    ' TWO STATIC ATTEMPTS THE SAME WAY MEANS THAT WAY IS NOT WORKING.
    ' This is per TankDrive/per hull. Traffic never counts; it resets the streak.
    ' On the second matching terrain attempt the brain enters its recovery state
    ' machine instead of freezing the tank.
    Public Const SAME_WAY_ATTEMPT_LIMIT As Integer = 2
    Public attemptCount As Integer = 0
    Public brainState As TankBrainState = TankBrainState.Forward

    Private lastAttemptDir As Integer = 99       ' +1 right, -1 left, 0 reverse
    Private lastAttemptTarget As Vector2
    Private haveLastAttempt As Boolean = False
    Private lastAttemptReason As String = ""
    Private attemptEpochSeen As Integer = -1

    ' TERRAIN ESCAPE BRAIN. Scan only the forward 90-degree fan, from
    ' -45 through +45 degrees relative to the hull heading, in ten-degree steps.
    ' The heading whose ray stays clear farthest is remembered
    ' while the tank backs and turns; it is not recomputed every frame.
    Private Const ESCAPE_SWEEP_MIN_DEG As Integer = -45
    Private Const ESCAPE_SWEEP_MAX_DEG As Integer = 45
    Private Const ESCAPE_SWEEP_STEP_DEG As Integer = 10
    Private Const DEG_TO_RAD As Single = 0.0174532925F
    Private Const ESCAPE_TURN_EPS_RAD As Single = 0.034906585F    ' 2 degrees
    Private Const ESCAPE_BACK_M As Single = 4.5F
    Private escapeHeading As Single = 0.0F
    Private escapeRayM As Single = 0.0F
    Private escapeBackRemainingM As Single = 0.0F

    ' Visible copy of the last 10-degree terrain scan. The renderer reads this
    ' directly from the tank's brain, so the picture is the exact scan that made
    ' the decision rather than a second diagnostic scan that could disagree.
    Public ReadOnly brainScanRays As New List(Of TankBrainScanRay)
    Private _brainScanVisible As Boolean = False

    ' REROLL INVALIDATES THE OLD BRAIN IMMEDIATELY. Advance also checks this
    ' epoch, but Reset Sim stops driving before it calls Reroll; without this
    ' guard the renderer could keep showing the previous recovery fan until the
    ' next run. The first read after the epoch changes clears the stale scan and
    ' restores the brain to Forward.
    Public ReadOnly Property brainScanVisible As Boolean
        Get
            If attemptEpochSeen <> TankSim.ATTEMPT_EPOCH Then
                ResetAttemptMemory()
                attemptEpochSeen = TankSim.ATTEMPT_EPOCH
            End If
            Return _brainScanVisible
        End Get
    End Property

    Private Sub ResetAttemptStreak()
        attemptCount = 0
        lastAttemptDir = 99
        lastAttemptTarget = Vector2.Zero
        haveLastAttempt = False
        lastAttemptReason = ""
    End Sub

    Private Sub ResetAttemptMemory()
        ResetAttemptStreak()
        brainState = TankBrainState.Forward
        escapeHeading = 0.0F
        escapeRayM = 0.0F
        escapeBackRemainingM = 0.0F
        brainScanRays.Clear()
        _brainScanVisible = False
    End Sub

    ''' <summary>
    ''' Another tank is temporary traffic, not proof that this route direction
    ''' failed. Break the consecutive static-obstacle streak immediately. Log
    ''' only when there was a streak to clear so a traffic jam does not print
    ''' sixty reset lines a second. This does NOT cancel an already-armed terrain
    ''' escape state; it only clears the retry memory.
    ''' </summary>
    Private Sub ResetAttemptsForTraffic(inst As TankInstance)
        If attemptCount > 0 OrElse haveLastAttempt Then
            LogThis("tank ai: ATTEMPT RESET tank={0} team={1} count={2} blocker=TANK",
                    inst.id, inst.team.ToString(), attemptCount)
        End If
        ResetAttemptStreak()
    End Sub

    ''' <summary>
    ''' Record ONE committed STATIC recovery, not one blocked frame. Consecutive
    ''' means the same SIM waypoint and the same direction. The second matching
    ''' attempt arms the terrain-escape brain; the caller returns immediately and
    ''' TerrainScan runs on the next Advance with the authoritative nav grid.
    ''' </summary>
    Private Function RegisterRecoveryAttempt(inst As TankInstance,
                                             dir As Integer,
                                             ring As Integer,
                                             reason As String) As Boolean
        If Not TankSim.SIM_RUN Then Return False

        Dim target = If(hasSimTarget, simTarget, goal)
        Dim sameTarget = haveLastAttempt AndAlso
                         (target - lastAttemptTarget).Length <= 0.5F
        Dim sameWay = sameTarget AndAlso lastAttemptDir = dir

        If sameWay Then
            attemptCount += 1
        Else
            attemptCount = 1
        End If

        lastAttemptDir = dir
        lastAttemptTarget = target
        lastAttemptReason = reason
        haveLastAttempt = True

        If attemptCount < SAME_WAY_ATTEMPT_LIMIT Then Return False

        brainState = TankBrainState.TerrainScan
        speed = 0.0F
        reverseS = 0.0F
        passS = 0.0F
        skirtS = 0.0F
        skirtSide = 0

        Dim way = If(dir > 0, "RIGHT", If(dir < 0, "LEFT", "REVERSE"))
        LogThis("tank ai: TERRAIN ESCAPE ARMED tank={0} team={1} count={2} way={3} ring={4} reason={5} target=({6:0.0},{7:0.0})",
                inst.id, inst.team.ToString(), attemptCount, way, ring, reason,
                target.X, target.Y)
        Return True
    End Function

    ''' <summary>
    ''' How far a sensor ray travels before it hits an impassable nav square.
    ''' No arbitrary local range is used:
    ''' the ray may cross the whole nav grid, and OFFMAP eventually ends it.
    ''' </summary>
    Private Shared Function NavRayDistance(nav As TankNav,
                                           pos As Vector2,
                                           heading As Single) As Single
        If nav Is Nothing OrElse Not nav.ready Then Return 0.0F

        Dim d As New Vector2(CSng(Math.Sin(heading)), CSng(Math.Cos(heading)))
        Dim stepM = Math.Max(0.25F, nav.cell_m * 0.5F)
        Dim maxM = nav.cell_m * TankNav.SIZE * 1.5F
        Dim dist = stepM

        ' THIS IS A SENSOR RAY, NOT A HULL-FIT TEST. Read the nav square itself.
        ' If we use CanStand(..., HULL_R) here, a tank already tight to a wall can
        ' report every direction blocked at zero because its 4.5 m footprint still
        ' overlaps that wall. The ray asks where the static obstruction actually is.
        While dist <= maxM
            Dim q = pos + d * dist
            Dim cx As Integer, cz As Integer
            nav.CellOf(q.X, q.Y, cx, cz)

            If Not nav.InBounds(cx, cz) Then Return dist

            Dim flags = nav.cell(cz * TankNav.SIZE + cx)
            If (flags And TankNav.IMPASSABLE) <> 0 Then Return dist

            dist += stepM
        End While

        Return maxM
    End Function

    ''' <summary>
    ''' Run a non-Forward brain state. True means the recovery state owns this
    ''' frame and normal path/traffic steering must not run underneath it.
    ''' </summary>
    Private Function RunBrainState(inst As TankInstance,
                                   nav As TankNav,
                                   others As List(Of TankInstance),
                                   pos As Vector2,
                                   dt As Single) As Boolean
        Select Case brainState
            Case TankBrainState.Forward
                Return False

            Case TankBrainState.TerrainScan
                speed = 0.0F
                reverseS = 0.0F
                passS = 0.0F
                skirtS = 0.0F
                skirtSide = 0
                stopReason = StopWhy.Ground

                Dim bestM As Single = -1.0F
                Dim bestOffset As Integer = 0
                Dim bestIndex As Integer = -1
                brainScanRays.Clear()

                ' FORWARD FAN ONLY. -45 through +45 relative to the current
                ' hull heading, ten degrees at a time. Store the exact ray and
                ' first nav-hit point the brain saw so the renderer shows the
                ' same decision the brain made.
                For offsetDeg = ESCAPE_SWEEP_MIN_DEG To ESCAPE_SWEEP_MAX_DEG Step ESCAPE_SWEEP_STEP_DEG
                    Dim h = WrapPi(inst.headingRad + offsetDeg * DEG_TO_RAD)
                    Dim rayM = NavRayDistance(nav, pos, h)
                    Dim d As New Vector2(CSng(Math.Sin(h)), CSng(Math.Cos(h)))
                    Dim q = pos + d * rayM

                    brainScanRays.Add(New TankBrainScanRay With {
                        .origin = pos,
                        .hit = q,
                        .distanceM = rayM,
                        .isWinner = False
                    })

                    If rayM > bestM + 0.001F OrElse
                       (Math.Abs(rayM - bestM) <= 0.001F AndAlso
                        Math.Abs(offsetDeg) < Math.Abs(bestOffset)) Then
                        bestM = rayM
                        bestOffset = offsetDeg
                        bestIndex = brainScanRays.Count - 1
                        escapeHeading = h
                    End If
                Next

                If bestIndex >= 0 Then
                    Dim winner = brainScanRays(bestIndex)
                    winner.isWinner = True
                    brainScanRays(bestIndex) = winner
                End If
                _brainScanVisible = (brainScanRays.Count > 0)
                escapeRayM = Math.Max(0.0F, bestM)

                ' Check the whole backup segment against static ground and the
                ' rear tank rays. If either is blocked, skip Reverse and turn in
                ' place; otherwise back one hull radius before turning.
                Dim scanBack As New Vector2(-CSng(Math.Sin(inst.headingRad)),
                                            -CSng(Math.Cos(inst.headingRad)))
                Dim backTarget = pos + scanBack * ESCAPE_BACK_M
                Dim rearTraffic = TankSim.SIM_RUN AndAlso TankSim.RearBlocked(inst, others)
                Dim rearGround = Not NavSegmentClear(nav, pos, backTarget, TankDriveTune.HULL_R)

                If Not rearTraffic AndAlso Not rearGround Then
                    escapeBackRemainingM = ESCAPE_BACK_M
                    brainState = TankBrainState.TerrainReverse
                Else
                    escapeBackRemainingM = 0.0F
                    If rearTraffic Then ResetAttemptsForTraffic(inst)
                    brainState = TankBrainState.TerrainTurn
                End If

                LogThis("tank ai: TERRAIN SCAN tank={0} team={1} bestOffset={2}deg clear={3:0.0}m back={4}",
                        inst.id, inst.team.ToString(), bestOffset, escapeRayM,
                        If(brainState = TankBrainState.TerrainReverse, "YES", "NO"))

                ' NO DIAGNOSTIC PAUSE. The scan is retained for drawing while
                ' this recovery runs, then cleared when the turn completes or the
                ' SIM is reset. Continue directly into reverse/turn on later frames.
                Return True

            Case TankBrainState.TerrainReverse
                stopReason = StopWhy.Reversing
                speed = 0.0F
                reverseS = 0.0F
                passS = 0.0F
                skirtS = 0.0F
                skirtSide = 0

                If escapeBackRemainingM <= 0.001F Then
                    brainState = TankBrainState.TerrainTurn
                    Return True
                End If

                ' Re-check every reverse step. A tank can move behind us after
                ' the scan, and dynamic traffic must never be backed into.
                If TankSim.SIM_RUN AndAlso TankSim.RearBlocked(inst, others) Then
                    ResetAttemptsForTraffic(inst)
                    escapeBackRemainingM = 0.0F
                    brainState = TankBrainState.TerrainTurn
                    LogThis("tank ai: TERRAIN BACK interrupted tank={0} blocker=TANK -> TURN",
                            inst.id)
                    Return True
                End If

                Dim reverseDir As New Vector2(-CSng(Math.Sin(inst.headingRad)),
                                              -CSng(Math.Cos(inst.headingRad)))
                Dim bstep = Math.Min(TankDriveTune.REVERSE_MS * dt, escapeBackRemainingM)
                Dim bnxt = pos + reverseDir * bstep

                If Not nav.CanStand(bnxt.X, bnxt.Y, TankDriveTune.HULL_R) Then
                    escapeBackRemainingM = 0.0F
                    brainState = TankBrainState.TerrainTurn
                    LogThis("tank ai: TERRAIN BACK stopped tank={0} blocker=NAV -> TURN",
                            inst.id)
                    Return True
                End If

                inst.position = New Vector3(bnxt.X,
                                            get_Y_at_XZ_fast(bnxt.X, bnxt.Y),
                                            bnxt.Y)
                inst.trackDistance += bstep
                escapeBackRemainingM -= bstep

                If escapeBackRemainingM <= 0.001F Then
                    escapeBackRemainingM = 0.0F
                    brainState = TankBrainState.TerrainTurn
                    LogThis("tank ai: TERRAIN BACK complete tank={0} -> TURN", inst.id)
                End If
                Return True

            Case TankBrainState.TerrainTurn
                stopReason = StopWhy.Turning
                speed = 0.0F
                reverseS = 0.0F
                passS = 0.0F
                skirtS = 0.0F
                skirtSide = 0

                Dim dh = WrapPi(escapeHeading - inst.headingRad)
                Dim maxTurn = TankDriveTune.TURN_RATE_RAD * dt

                If Math.Abs(dh) <= ESCAPE_TURN_EPS_RAD Then
                    inst.headingRad = escapeHeading

                    ' The escape is finished. Clear every stale recovery timer and
                    ' retry count, and hand the tank back to normal forward/path
                    ' driving on the next frame.
                    ResetAttemptStreak()
                    brainState = TankBrainState.Forward
                    brainScanRays.Clear()
                    _brainScanVisible = False
                    stuckS = 0.0F
                    blockedS = 0.0F
                    goalS = 0.0F
                    speed = 0.0F

                    LogThis("tank ai: TERRAIN TURN complete tank={0} heading={1:0.0}deg clear={2:0.0}m -> FORWARD",
                            inst.id, escapeHeading * CSng(180.0 / Math.PI), escapeRayM)
                    Return True
                End If

                inst.headingRad = WrapPi(inst.headingRad +
                                         Math.Max(-maxTurn, Math.Min(maxTurn, dh)))
                Return True
        End Select

        brainState = TankBrainState.Forward
        Return False
    End Function

    ''' <summary>
    ''' Hard rule for the three forward rays. If FL + FRONT + FR are ALL
    ''' blocked, do not try another turn angle. The forward fan has already
    ''' proved that the whole nose is shut, so turning through those same
    ''' steps is exactly what causes the left/right oscillation.
    '''
    ''' Try backing up instead. If the rear rays or the ground immediately
    ''' behind are blocked too, stand still and return. Because no timer or
    ''' alternate goal is started in that case, Advance reaches this test again
    ''' next frame and keeps checking until either front or rear opens.
    ''' </summary>
    Private Function HandleAllThreeFrontBlocked(inst As TankInstance,
                                                nav As TankNav,
                                                others As List(Of TankInstance),
                                                pos As Vector2,
                                                dt As Single,
                                                blockers As Boolean(),
                                                countStaticAttempt As Boolean) As Boolean
        If blockers Is Nothing OrElse blockers.Length <= TankSim.R_FRONT Then Return False

        Dim allFront = blockers(TankSim.R_FL) AndAlso
                       blockers(TankSim.R_FRONT) AndAlso
                       blockers(TankSim.R_FR)
        If Not allFront Then Return False

        ' Stop any committed turn/pass. Three-of-three overrides steering.
        passS = 0.0F
        skirtS = 0.0F
        skirtSide = 0
        speed = 0.0F

        ' Is there somewhere to back into RIGHT NOW? Rear rays protect against
        ' tanks; CanStand protects against the static/nav map.
        Dim rearTraffic = TankSim.SIM_RUN AndAlso TankSim.RearBlocked(inst, others)
        Dim back As New Vector2(-CSng(Math.Sin(inst.headingRad)),
                                -CSng(Math.Cos(inst.headingRad)))
        Dim bstep = TankDriveTune.REVERSE_MS * dt
        Dim bnxt = pos + back * bstep
        Dim rearGround = Not nav.CanStand(bnxt.X, bnxt.Y, TankDriveTune.HULL_R)

        If rearTraffic OrElse rearGround Then
            ' Boxed in: WAIT. Do not turn, do not choose another goal, do not
            ' start reverse. Next frame checks the same rays again.
            reverseS = 0.0F
            stopReason = If(rearTraffic, StopWhy.Traffic, StopWhy.Ground)
            Return True
        End If

        ' Rear is open: back out and give the nose room. Count this only when
        ' the caller got here because the NAV/GROUND was blocked. The same
        ' three-ray shape can be caused by another tank in the traffic branch,
        ' and moving traffic must never advance the static-obstacle retry count.
        If countStaticAttempt AndAlso
           RegisterRecoveryAttempt(inst, 0, 0, "three-front reverse") Then
            stopReason = StopWhy.Ground
            Return True
        End If
        reverseS = TankDriveTune.REVERSE_S
        stopReason = StopWhy.Reversing
        stuckS = 0.0F
        blockedS = 0.0F
        Return True
    End Function

    ''' <summary>
    ''' Drive one tank for one step.
    ''' </summary>
    Public Sub Advance(inst As TankInstance, nav As TankNav,
                       others As List(Of TankInstance), dt As Single)
        If dt <= 0.0F Then Return

        ' Honour the SIM's manual/global pause before advancing this tank.
        ' Terrain recovery no longer raises this flag itself.
        If TankSim.SIM_RUN AndAlso TankSim.SIM_PAUSED Then Return

        ' A SIM reset starts a fresh attempt history for every existing drive.
        If attemptEpochSeen <> TankSim.ATTEMPT_EPOCH Then
            ResetAttemptMemory()
            attemptEpochSeen = TankSim.ATTEMPT_EPOCH
        End If

        Dim pos As New Vector2(inst.position.X, inst.position.Z)

        ' THE BRAIN STATE OWNS THE FRAME. Terrain recovery must finish its scan,
        ' optional reverse and turn before ordinary path steering can write a
        ' different goal underneath it.
        If RunBrainState(inst, nav, others, pos, dt) Then Return

        goalS += dt
        If skirtS > 0.0F Then
            skirtS -= dt
            If skirtS <= 0.0F Then skirtSide = 0
        End If
        If passS > 0.0F Then passS -= dt
        ' THE SIM OWNS THE GOAL WHILE IT RUNS, and this is where that has to
        ' be said or it does not hold. The sim assigns a start point each
        ' frame, and then this ran anyway: PickGoal fires the moment a hull is
        ' within ARRIVE_M, or after GOAL_PATIENCE_S, or whenever the way ahead
        ' is shut - and it takes the next waypoint of the route catalogue
        ' instead. So every tank was handed a start and then immediately sent
        ' somewhere else by its own corridor, which looked exactly like the
        ' assignment never arriving. It arrived; it was overwritten.
        '
        ' Arrival still ends the drive - hasGoal stays set and the hull stops
        ' on the point rather than picking a new one.
        If TankSim.SIM_RUN Then
            ' BACK ONTO THE DESTINATION, but only once whatever the hull was
            ' doing has finished. Re-aiming during a skirt or a go-around is
            ' what cancels it - the tangent is abandoned on the frame after it
            ' is chosen and the hull turns straight back into what it was
            ' avoiding.
            If hasSimTarget AndAlso skirtS <= 0.0F AndAlso passS <= 0.0F Then
                If Not hasGoal OrElse (goal - simTarget).Length > 0.5F Then
                    goal = simTarget
                    hasGoal = True
                End If
            End If
            If hasGoal AndAlso (goal - pos).Length < TankDriveTune.ARRIVE_M Then
                arrived = True
            End If
        ElseIf skirtS <= 0.0F AndAlso
               (Not hasGoal OrElse (goal - pos).Length < TankDriveTune.ARRIVE_M OrElse
                goalS > TankDriveTune.GOAL_PATIENCE_S) Then
            PickGoal(inst, nav, pos)
        End If
        If Not hasGoal Then Return

        ' ---- backing out ---------------------------------------------------
        If reverseS > 0.0F Then
            stopReason = StopWhy.Reversing
            reverseS -= dt
            Dim back As New Vector2(-CSng(Math.Sin(inst.headingRad)),
                                    -CSng(Math.Cos(inst.headingRad)))
            Dim bstep = TankDriveTune.REVERSE_MS * dt
            Dim bnxt = pos + back * bstep
            ' No alignment test and no turning: reverse is a straight line out
            ' of wherever the nose got to. If behind is shut too then the tank
            ' is boxed, and the goal timer will move it on.
            Dim reverseTrafficClear =
                If(TankSim.SIM_RUN,
                   Not TankSim.RearBlocked(inst, others),
                   Not Crowded(inst, others, bnxt))

            If nav.CanStand(bnxt.X, bnxt.Y, TankDriveTune.HULL_R) AndAlso
               reverseTrafficClear Then
                inst.position = New Vector3(bnxt.X, get_Y_at_XZ_fast(bnxt.X, bnxt.Y), bnxt.Y)
                inst.trackDistance += bstep
            Else
                reverseS = 0.0F
            End If
            speed = 0.0F
            Return
        End If

        ' ---- turn ----------------------------------------------------------
        Dim to_goal = goal - pos
        If to_goal.LengthSquared < 1.0E-6F Then Return

        ' Forward is (sin h, cos h) - the convention world_matrix's
        ' CreateRotationY already uses, so heading 0 faces +Z.
        Dim want = CSng(Math.Atan2(to_goal.X, to_goal.Y))
        Dim dh = WrapPi(want - inst.headingRad)
        Dim maxTurn = TankDriveTune.TURN_RATE_RAD * dt
        inst.headingRad = WrapPi(inst.headingRad +
                                 Math.Max(-maxTurn, Math.Min(maxTurn, dh)))

        ' ---- speed ---------------------------------------------------------
        ' TURN FIRST, THEN DRIVE. Rolling forward while still swinging round
        ' makes a tank arc into whatever it was turning away from, and the
        ' arc is widest exactly when the turn is largest - which is when it
        ' has just been blocked.
        Dim aligned = Math.Abs(dh) < TankDriveTune.DRIVE_CONE_RAD
        Dim wanted = If(aligned, TankDriveTune.SPEED_MS, 0.0F)
        Dim rate = If(wanted > speed, TankDriveTune.ACCEL_MS2, TankDriveTune.BRAKE_MS2) * dt
        speed += Math.Max(-rate, Math.Min(rate, wanted - speed))
        If speed < 0.0F Then speed = 0.0F

        If speed <= 0.001F Then
            ' TURNING IS NOT BEING STUCK. A hull swinging through 180 degrees
            ' at TURN_RATE_RAD takes five seconds and stands still for all of
            ' them; counting that as stuck repicks the goal a third of the way
            ' round, which changes the target heading and starts the turn
            ' again. Only a tank that is pointed the right way and still not
            ' moving has a problem.
            stopReason = If(aligned, StopWhy.Aligned, StopWhy.Turning)
            If aligned Then
                stuckS += dt
                If stuckS > TankDriveTune.STUCK_S Then PickGoal(inst, nav, pos)
            End If
            Return
        End If

        ' ---- move ----------------------------------------------------------
        Dim fwd As New Vector2(CSng(Math.Sin(inst.headingRad)),
                               CSng(Math.Cos(inst.headingRad)))
        Dim stride = speed * dt
        Dim nxt = pos + fwd * stride

        If Not nav.CanStand(nxt.X, nxt.Y, TankDriveTune.HULL_R) Then

            ' SKIRT IT BEFORE GIVING UP ON THE DIRECTION.
            '
            ' The owner, watching a hull re-plan onto the same route three times
            ' running: "i think you missed draw the tangent expanding ring at
            ' each collision point. It gives up easy on direction."
            '
            ' He is right, and the re-plan loop is the proof: a blocked hull was
            ' abandoning its heading, asking for a new route, and being handed
            ' the same one - because nothing about the MAP had changed, only
            ' this hull's position against one obstacle. What it needed was to
            ' go ROUND the thing, which is a steering problem and not a
            ' planning one.
            '
            ' So: open a ring at the collision point and take the first tangent
            ' that clears. Sweep out from the blocked heading in widening steps,
            ' trying each side alternately, and steer down the first one a hull
            ' fits through. A near-miss costs a few degrees; a wall costs more;
            ' only a dead end runs out of ring, and that is the case where
            ' giving up on the direction is the right answer rather than the
            ' easy one.
            '
            ' CHOOSE THE SIDE FROM THE CURRENT RAYS, THEN COMMIT TO IT.
            '
            ' Truth table:
            '   FL / LEFT blocked  -> turn RIGHT  (+ heading)
            '   FR / RIGHT blocked -> turn LEFT   (- heading)
            '   FRONT + one side   -> turn away from that side
            '
            ' Multiple blockers mean a stronger first turn using the same
            ' 45-degree spacing as the rays:
            '   1 blocker = 45 degrees
            '   2 blockers = 90 degrees
            '   3 blockers = 135 degrees
            ' We already know the smaller angles point into blocked space, so
            ' testing them again causes oscillation.
            '
            ' DO NOT alternate sides inside the ring loop. Once a side is chosen
            ' it stays chosen for this skirt attempt.
            Dim blockers = TankSim.RayBlockers(inst, others, nav)

            ' THREE FORWARD BLOCKS: do not turn through known-blocked headings.
            ' Try reverse; if the rear is blocked too, wait and keep checking.
            If HandleAllThreeFrontBlocked(inst, nav, others, pos, dt, blockers, True) Then Return

            If skirtSide = 0 Then
                Dim leftPressure = 0
                Dim rightPressure = 0

                If blockers(TankSim.R_FL) Then leftPressure += 1
                If blockers(TankSim.R_LEFT) Then leftPressure += 1

                If blockers(TankSim.R_FR) Then rightPressure += 1
                If blockers(TankSim.R_RIGHT) Then rightPressure += 1

                If leftPressure > rightPressure Then
                    skirtSide = 1             ' obstacle left -> turn right
                ElseIf rightPressure > leftPressure Then
                    skirtSide = -1            ' obstacle right -> turn left
                ElseIf blockers(TankSim.R_FRONT) Then
                    ' Straight ahead is blocked and side pressure is tied.
                    ' Prefer the side whose side ray is actually clear.
                    If blockers(TankSim.R_RIGHT) AndAlso
                       Not blockers(TankSim.R_LEFT) Then
                        skirtSide = -1
                    ElseIf blockers(TankSim.R_LEFT) AndAlso
                           Not blockers(TankSim.R_RIGHT) Then
                        skirtSide = 1
                    ElseIf blockers(TankSim.R_FL) AndAlso
                           Not blockers(TankSim.R_FR) Then
                        skirtSide = 1
                    ElseIf blockers(TankSim.R_FR) AndAlso
                           Not blockers(TankSim.R_FL) Then
                        skirtSide = -1
                    Else
                        skirtSide = 1          ' true tie: right-hand rule
                    End If
                ElseIf blockers(TankSim.R_FL) Then
                    skirtSide = 1
                ElseIf blockers(TankSim.R_FR) Then
                    skirtSide = -1
                ElseIf blockers(TankSim.R_LEFT) Then
                    skirtSide = 1
                ElseIf blockers(TankSim.R_RIGHT) Then
                    skirtSide = -1
                Else
                    skirtSide = 1
                End If
            End If

            ' Count blockers on the side we are turning AWAY from, plus FRONT.
            ' This is the "two blocks = two turns" rule.
            Dim turnSteps = 0
            If blockers(TankSim.R_FRONT) Then turnSteps += 1

            If skirtSide > 0 Then
                If blockers(TankSim.R_FL) Then turnSteps += 1
                If blockers(TankSim.R_LEFT) Then turnSteps += 1
            Else
                If blockers(TankSim.R_FR) Then turnSteps += 1
                If blockers(TankSim.R_RIGHT) Then turnSteps += 1
            End If

            If turnSteps < 1 Then turnSteps = 1
            If turnSteps > SKIRT_RINGS Then turnSteps = SKIRT_RINGS

            Dim skirted = False
            Dim newSkirtAttempt = (skirtS <= 0.0F)
            Dim chosenSkirtRing = 0
            For ring = turnSteps To SKIRT_RINGS
                Dim ang = SKIRT_STEP_RAD * ring
                Dim h = inst.headingRad + ang * skirtSide
                Dim dir As New Vector2(CSng(Math.Sin(h)), CSng(Math.Cos(h)))
                Dim probe2 = pos + dir * stride

                If nav.CanStand(probe2.X, probe2.Y, TankDriveTune.HULL_R) Then
                    chosenSkirtRing = ring
                    If newSkirtAttempt AndAlso
                       RegisterRecoveryAttempt(inst, skirtSide, chosenSkirtRing, "ground skirt") Then
                        stopReason = StopWhy.Ground
                        Return
                    End If
                    goal = pos + dir * TankDriveTune.ARRIVE_M * 2.0F
                    hasGoal = True
                    skirtS = SKIRT_HOLD_S
                    skirted = True
                    Exit For
                End If
            Next
            If skirted Then
                stopReason = StopWhy.Turning
                speed = 0.0F
                blockedS = 0.0F
                Return
            End If

            stopReason = StopWhy.Ground
            speed = 0.0F
            stuckS += dt

            ' COMMIT TO THE TURN BEFORE ASKING AGAIN. Repicking on every
            ' blocked frame was what deadlocked the first fleet: back when the
            ' goal was a random throw, a fresh goal each frame meant a fresh
            ' desired heading each frame, so the turn was a jitter about the
            ' average and the tank never completed the swing that would take it
            ' away from the wall.
            '
            ' The throw is gone and PickGoal now re-aims at the SAME waypoint,
            ' so the heading no longer jumps - but the delay still earns its
            ' place: it is the time the hull needs to actually come round, and
            ' asking again sooner just burns the frames it should be turning
            ' in.
            blockedS += dt
            If blockedS > TankDriveTune.REPICK_S Then
                blockedS = 0.0F
                ' A HULL ON A ROUTE THAT IS BLOCKED DOES NOT WANT A NEW
                ' WAYPOINT, IT WANTS A NEW ROUTE. Re-aiming at the same point is
                ' right for a moment's obstruction and useless against a wall.
                ' The baked TankNav is the authoritative static no-go map.
                If path IsNot Nothing Then wantsReplan = True
                PickGoal(inst, nav, pos)
            End If

            ' NO RUNTIME PINNING. The baked height/nav map is the complete
            ' static no-go source. If the hull remains wedged, recovery may back
            ' it out, but it must never rewrite the navigation map.
            If stuckS > TankDriveTune.WEDGED_S Then
                stuckS = 0.0F
                ' NOT BACKWARDS INTO SOMEBODY. The rear rays say whether there
                ' is traffic behind us. Wedged against terrain reverses; wedged
                ' against a neighbour waits for it to move.
                If TankSim.SIM_RUN AndAlso TankSim.RearBlocked(inst, others) Then
                    stopReason = StopWhy.Traffic
                    speed = 0.0F
                    Return
                End If
                reverseS = TankDriveTune.REVERSE_S
            End If
            Return
        End If

        ' OTHER TANKS ARE DYNAMIC TRAFFIC, not map data. Waiting/avoidance is
        ' handled by the rays; the static navigation map is never rewritten.
        ' THE RAYS DECIDE, not a circle round the nose.
        '
        ' Crowded asks "is any hull within SEPARATION_M and forward of my
        ' beam", which is an eight-metre disc and says nothing about WHERE in
        ' it. The rays are the hull's own eight, cast from its corners and
        ' sides, and they answer the two questions this actually needs: is
        ' something in front of me, and is the side I am about to swing into
        ' clear. That is what they were made for.
        Dim rayBlocked = TankSim.SIM_RUN AndAlso TankSim.BlockedAhead(inst, others)

        ' Under the SIM the rays are the collision sensor. Do NOT OR the old
        ' 8 m Crowded() disc back in here or a tank alongside/in the forward
        ' half-plane can stop this hull even though no relevant ray is close.
        Dim trafficBlocked =
            If(TankSim.SIM_RUN,
               rayBlocked,
               Crowded(inst, others, nxt))

        If trafficBlocked Then
            ' A TANK IS NOT A FAILED STATIC ROUTE ATTEMPT. It may move on the
            ' next frame, so every traffic loop breaks the consecutive attempt
            ' streak. Only nav/ground obstruction is allowed to reach two and
            ' trigger the diagnostic freeze.
            If TankSim.SIM_RUN Then ResetAttemptsForTraffic(inst)

            ' GO ROUND TO THE RIGHT rather than stand and wait.
            '
            ' Waiting is correct when the other hull is passing THROUGH; it is
            ' a deadlock when both are trying to reach the same place, which is
            ' most of a sim run - two tanks nose to nose each waiting for the
            ' other to clear, neither of them moving, both of them counting up
            ' a stuck timer.
            '
            ' The turn is committed to for PASS_HOLD_S so the hull actually
            ' gets round rather than re-deciding every frame in the same spot,
            ' and it is only taken if the ground that way can be stood on and
            ' is not itself occupied - otherwise going round is just a second
            ' way to get stuck.
            If passS <= 0.0F Then
                Dim spot As Vector2
                Dim haveWay = False

                If TankSim.SIM_RUN Then
                    ' ONE TRUTH TABLE FOR THE THREE FORWARD RAYS.
                    '
                    ' Do NOT call ClearWay here. That used to search the clear
                    ' rays again from scratch and could choose the first turn
                    ' step even when CENTER had already proved that step bad.
                    ' That is the oscillation we already fixed once.
                    '
                    '   C only       -> RIGHT 1 step  (right-hand rule)
                    '   C + L        -> RIGHT 2 steps
                    '   C + R        -> LEFT  2 steps
                    '   C + L + R    -> REVERSE; if rear blocked, WAIT
                    '   L only       -> RIGHT 1 step
                    '   R only       -> LEFT  1 step
                    '
                    ' The count is the FIRST ring to try. If C+L is blocked,
                    ' ring 1 is known bad, so begin at ring 2. Never retry it.
                    Dim blockers = TankSim.RayBlockers(inst, others, nav)

                    ' THREE FORWARD BLOCKS override the turn table: back up.
                    ' If the rear is blocked too, wait motionless and re-check
                    ' on the next frame.
                    If HandleAllThreeFrontBlocked(inst, nav, others, pos, dt, blockers, False) Then Return

                    Dim c = blockers(TankSim.R_FRONT)
                    Dim l = blockers(TankSim.R_FL)
                    Dim r = blockers(TankSim.R_FR)

                    Dim turnSide As Integer = 1     ' + = right, - = left
                    Dim turnSteps As Integer = 1

                    If c Then
                        If l Then
                            turnSide = 1
                            turnSteps = 2
                        ElseIf r Then
                            turnSide = -1
                            turnSteps = 2
                        Else
                            turnSide = 1
                            turnSteps = 1
                        End If
                    ElseIf l AndAlso r Then
                        ' Both corners blocked with CENTER clear: keep the
                        ' right-hand tie rule and skip the first known-bad side.
                        turnSide = 1
                        turnSteps = 2
                    ElseIf l Then
                        turnSide = 1
                        turnSteps = 1
                    ElseIf r Then
                        turnSide = -1
                        turnSteps = 1
                    Else
                        turnSide = 1
                        turnSteps = 1
                    End If

                    If turnSteps > SKIRT_RINGS Then turnSteps = SKIRT_RINGS

                    ' Commit to ONE side and sweep outward from the first ring
                    ' not already disproved by the blocked forward rays.
                    For ring = turnSteps To SKIRT_RINGS
                        Dim ang = SKIRT_STEP_RAD * ring
                        Dim h = inst.headingRad + ang * turnSide
                        Dim dir As New Vector2(CSng(Math.Sin(h)), CSng(Math.Cos(h)))
                        spot = pos + dir * PASS_M

                        If NavSegmentClear(nav, pos, spot, TankDriveTune.HULL_R) Then
                            haveWay = True
                            Exit For
                        End If
                    Next
                Else
                    ' Outside the SIM there are no authoritative ray blockers;
                    ' keep the old fixed right-hand pass behaviour.
                    Dim ph = inst.headingRad + PASS_TURN_RAD
                    Dim pd As New Vector2(CSng(Math.Sin(ph)), CSng(Math.Cos(ph)))
                    spot = pos + pd * PASS_M
                    haveWay = Not Crowded(inst, others, spot)
                End If

                If haveWay Then
                    ' This manoeuvre is around ANOTHER TANK. The traffic branch
                    ' already reset the static attempt streak above; do not
                    ' register the pass as an obstacle retry.
                    goal = spot
                    hasGoal = True
                    passS = PASS_HOLD_S
                    stopReason = StopWhy.Turning
                    speed = 0.0F
                    stuckS = 0.0F
                    Return
                End If
            End If
            stopReason = StopWhy.Traffic
            speed = 0.0F
            stuckS += dt
            ' Under the sim the destination is not this hull's to change.
            If stuckS > TankDriveTune.STUCK_S AndAlso Not TankSim.SIM_RUN Then
                PickGoal(inst, nav, pos)
            End If
            Return
        End If

        stopReason = StopWhy.Moving
        stuckS = 0.0F
        blockedS = 0.0F
        inst.position = New Vector3(nxt.X, get_Y_at_XZ_fast(nxt.X, nxt.Y), nxt.Y)
        inst.trackDistance += stride
    End Sub

    ''' <summary>
    ''' The next waypoint of this hull's route, or nothing at all.
    '''
    ''' THE CATALOGUE IS THE ONLY SOURCE OF GOALS. Nothing is searched here and
    ''' nothing is invented: the route was built at load, this hull was handed
    ''' one of its corridors, and this walks along it.
    '''
    ''' It used to fall back on throwing random goals into a 60-220 m ring when
    ''' there was no route. That is gone - "remove all the random path seeking
    ''' code ... we have ray studio now" - and a hull without a corridor now
    ''' parks instead of wandering. Called on arrival, on a stall and when the
    ''' way ahead shuts, so it must stay cheap and must not advance the
    ''' waypoint except on arrival.
    ''' </summary>
    Private Sub PickGoal(inst As TankInstance, nav As TankNav, pos As Vector2)
        hasGoal = False
        goalS = 0.0F

        ' FOLLOW THE ROUTE IF IT HAS ONE. The catalogue was built at load and
        ' this hull was handed one of its corridors; nothing is searched here.
        '
        ' The waypoint only advances on ARRIVAL, never on a repick. PickGoal is
        ' also called when the way ahead is shut and when a turn has stalled,
        ' and advancing there would skip the waypoint the hull could not reach
        ' and aim it at the next one - which is a shortcut through whatever it
        ' just failed to get past. Re-aiming at the SAME waypoint makes it turn
        ' and try again, which is what being blocked should cost.
        If path IsNot Nothing AndAlso path.Count > 0 Then
            If pathAt < path.Count AndAlso
               (path(pathAt) - pos).Length < TankDriveTune.ARRIVE_M Then
                pathAt += 1
            End If
            If pathAt >= path.Count Then
                If Not arrived Then
                    arrived = True
                    LogThis("tank ai: ARRIVED - team {0} {1} reached the enemy base",
                            If(inst.team = TankTeam.Green, 1, 2), inst.label)
                End If
                Return
            End If
            goal = path(pathAt)
            hasGoal = True
            Return
        End If

        ' AND WITHOUT A ROUTE, IT STAYS PUT. The owner, 2026-09-12: "remove
        ' all the random path seeking code ... we have ray studio now."
        '
        ' What used to be here threw up to 24 random goals into a 60-220 m ring
        ' and drove at the first one that could be stood on. It was never
        ' navigation - it was a hull wandering until it happened to be
        ' somewhere - and it flattered every measurement of the real planner by
        ' keeping the fleet in motion whether or not a route existed.
        '
        ' So a hull with no corridor now parks, visibly, and says so once. That
        ' is the honest state: the catalogue is the only thing that moves a
        ' tank, and a tank standing still means it was never given a route.
        If RoutesHandedOut AndAlso Not noRouteLogged Then
            noRouteLogged = True
            LogThis("tank ai: team {0} {1} has no route - parked. " &
                    "The random goal picker was removed; the catalogue is the " &
                    "only source of goals now.",
                    If(inst.team = TankTeam.Green, 1, 2), inst.label)
        End If
    End Sub

    ''' <summary>
    ''' Is another hull in the way of where this one is about to stand?
    '''
    ''' AHEAD ONLY. Testing a full circle means a tank is held up by one that
    ''' is behind it and receding, which in a fifteen-strong spawn cluster is
    ''' most of them - the group locks itself in place, every tank waiting for
    ''' a neighbour it is already driving away from. What matters is what the
    ''' nose is about to reach.
    '''
    ''' Thirty tanks is 900 of these a frame, which is nothing.
    ''' </summary>
    Private Shared Function Crowded(self As TankInstance,
                                    others As List(Of TankInstance),
                                    p As Vector2) As Boolean
        Dim r2 = TankDriveTune.SEPARATION_M * TankDriveTune.SEPARATION_M
        Dim fx = CSng(Math.Sin(self.headingRad))
        Dim fz = CSng(Math.Cos(self.headingRad))
        For Each o In others
            If o Is self Then Continue For
            Dim dx = o.position.X - p.X
            Dim dz = o.position.Z - p.Y
            If dx * dx + dz * dz >= r2 Then Continue For
            ' Behind the hull's beam, so it is being left rather than met.
            If dx * fx + dz * fz < 0.0F Then Continue For
            Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' Final ground guard for an escape target. The endpoint alone is not enough:
    ''' a target can be clear while a wall lies between the tank and that target.
    ''' Test the whole segment using the same hull-radius CanStand test used by
    ''' normal movement.
    ''' </summary>
    Private Shared Function NavSegmentClear(nav As TankNav,
                                            a As Vector2,
                                            b As Vector2,
                                            hullR As Single) As Boolean
        If nav Is Nothing OrElse Not nav.ready Then Return True

        Dim v = b - a
        Dim length = v.Length
        If length <= 0.001F Then Return nav.CanStand(b.X, b.Y, hullR)

        Dim d = v / length
        Dim stepM = Math.Max(0.25F, nav.cell_m * 0.5F)
        Dim steps = Math.Max(1, CInt(Math.Ceiling(length / stepM)))

        ' Skip the exact current position; the tank is already standing there.
        For s = 1 To steps
            Dim dist = Math.Min(length, CSng(s) * stepM)
            Dim q = a + d * dist
            If Not nav.CanStand(q.X, q.Y, hullR) Then Return False
        Next

        Return True
    End Function

    ''' <summary>An angle folded back into -pi..pi. Without it the shortest way
    ''' round from 179 degrees to -179 looks like 358 degrees of turning.
    ''' </summary>
    Public Shared Function WrapPi(a As Single) As Single
        Const TAU As Single = CSng(Math.PI * 2.0)
        a = CSng(a - Math.Floor((a + Math.PI) / TAU) * TAU)
        Return a
    End Function
End Class

''' <summary>
''' What a driving tank does, in numbers.
'''
''' Separate from the class so all of it can be read at once and tuned in one
''' place; these are the knobs that decide whether thirty tanks look like a
''' battle or like a screensaver.
''' </summary>
Public Module TankDriveTune
    ''' <summary>Top speed. A tier 10 medium does about 50 km/h, which is
    ''' 14 m/s; this is deliberately under that because the map is 1000 m
    ''' across and a tank crossing it in seventy seconds reads as a car.
    ''' </summary>
    Public SPEED_MS As Single = 7.0F

    Public ACCEL_MS2 As Single = 3.0F
    Public BRAKE_MS2 As Single = 8.0F

    ''' <summary>Radians a second of hull yaw, about 57 degrees. A tracked
    ''' vehicle turning its whole hull, not a turret - but neutral steer is
    ''' quick, and at half this a full reversal took five seconds during which
    ''' the tank stood still and looked broken.</summary>
    Public TURN_RATE_RAD As Single = 1.0F

    ''' <summary>How closely a tank must be facing its goal before it will
    ''' drive at it. Wide enough to keep rolling through a gentle correction,
    ''' tight enough that a reversal is done standing still.</summary>
    Public DRIVE_CONE_RAD As Single = 0.5F

    ''' <summary>Clearance a hull needs, its own half-length plus margin. The
    ''' same 4.5 m the placement test uses, so a tank cannot drive somewhere it
    ''' would not have been allowed to spawn.</summary>
    Public HULL_R As Single = 4.5F

    ''' <summary>How close counts as arrived.</summary>
    Public ARRIVE_M As Single = 6.0F

    ''' <summary>Seconds on one goal before giving up on it. A tank still
    ''' trying after this is circling something it cannot pass.</summary>
    Public GOAL_PATIENCE_S As Single = 45.0F

    ''' <summary>Seconds held at a standstill before choosing again.</summary>
    Public STUCK_S As Single = 1.5F

    ''' <summary>Seconds of being blocked before asking for the goal again.
    ''' Long enough for the hull to have swung a useful part of the way round at
    ''' TURN_RATE_RAD - about 40 degrees - so the tank commits to a direction.
    ''' It re-aims at the same waypoint now rather than at a fresh random
    ''' throw, so this is a turning budget, not a jitter guard.</summary>
    Public REPICK_S As Single = 1.2F

    ''' <summary>
    ''' Seconds continuously wedged before the recovery reverse is attempted.
    ''' This no longer creates or records any map obstacle.
    ''' </summary>
    Public WEDGED_S As Single = 5.0F

    ''' <summary>How long, and how fast, a wedged tank backs out.</summary>
    Public REVERSE_S As Single = 1.5F
    Public REVERSE_MS As Single = 3.0F

    ''' <summary>Centre to centre spacing two hulls keep. A hull is about 7 m
    ''' long, so this leaves them near enough to look like a formation and far
    ''' enough not to interpenetrate.</summary>
    Public SEPARATION_M As Single = 8.0F
End Module

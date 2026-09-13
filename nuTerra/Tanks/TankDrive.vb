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
    ''' now stands, using the pins it has learned since.</summary>
    Public wantsReplan As Boolean = False

    ''' <summary>Which way round an obstacle this hull committed to, +1 or -1,
    ''' and how long that choice still holds. Re-deciding every frame is how a
    ''' hull oscillates in the mouth of a gap.</summary>
    Public skirtSide As Integer = 0
    Public skirtS As Single = 0.0F

    ''' <summary>How far the tangent sweep opens, and in what steps. Twelve
    ''' rings of 12 degrees reaches 144 degrees either side - past square to the
    ''' obstacle, which is as far as skirting can sensibly go before the way
    ''' round is genuinely behind you.</summary>
    Private Const SKIRT_RINGS As Integer = 12
    Private Const SKIRT_STEP_RAD As Single = 0.2094F
    Private Const SKIRT_HOLD_S As Single = 1.5F

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

    ''' <summary>Seconds left of backing out. A tank that has pinned the thing
    ''' in front of it still has that thing in front of it, and turning on the
    ''' spot against a wall does not help - it has to give itself room first.
    ''' </summary>
    Public reverseS As Single

    ''' <summary>Why this tank is not moving, this frame. Counted in the fleet
    ''' report: "ten of thirty moving" says a fleet is sluggish and nothing
    ''' about which of four quite different causes to go and fix.</summary>
    Public stopReason As StopWhy

    ''' <summary>
    ''' Drive one tank for one step.
    ''' </summary>
    Public Sub Advance(inst As TankInstance, nav As TankNav,
                       others As List(Of TankInstance), dt As Single)
        If dt <= 0.0F Then Return

        Dim pos As New Vector2(inst.position.X, inst.position.Z)

        goalS += dt
        If skirtS > 0.0F Then
            skirtS -= dt
            If skirtS <= 0.0F Then skirtSide = 0
        End If
        If skirtS <= 0.0F AndAlso
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
            If nav.CanStand(bnxt.X, bnxt.Y, TankDriveTune.HULL_R) AndAlso
               Not Crowded(inst, others, bnxt) Then
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
            ' THE SIDE IS REMEMBERED once chosen. Picking afresh each frame is
            ' how a hull ends up oscillating in the mouth of a gap - left looks
            ' best, it turns, right looks best, it turns back - and skirting an
            ' obstacle means committing to one way round it until it is passed.
            Dim skirted = False
            If skirtSide = 0 Then skirtSide = 1
            For ring = 1 To SKIRT_RINGS
                Dim ang = SKIRT_STEP_RAD * ring
                For pass = 0 To 1
                    ' The remembered side first, the other second.
                    Dim sgn = If(pass = 0, skirtSide, -skirtSide)
                    Dim h = inst.headingRad + ang * sgn
                    Dim probe2 = pos + New Vector2(CSng(Math.Sin(h)), CSng(Math.Cos(h))) * stride
                    If nav.CanStand(probe2.X, probe2.Y, TankDriveTune.HULL_R) Then
                        goal = pos + New Vector2(CSng(Math.Sin(h)), CSng(Math.Cos(h))) *
                                     TankDriveTune.ARRIVE_M * 2.0F
                        hasGoal = True
                        skirtSide = sgn
                        skirtS = SKIRT_HOLD_S
                        skirted = True
                        Exit For
                    End If
                Next
                If skirted Then Exit For
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
                ' right for a moment's obstruction and useless against a wall:
                ' the route was cut before this hull learned what is here, and
                ' every pin it has dropped since is knowledge the plan does not
                ' have. Wandering off a dart would lose the base entirely.
                If path IsNot Nothing Then wantsReplan = True
                PickGoal(inst, nav, pos)
            End If

            ' PINNING IS FOR BEING WEDGED, NOT FOR TOUCHING. A pin is
            ' permanent and it is written to disk, so pinning every wall a
            ' tank brushes would fill the map with them - the first run laid
            ' down several hundred in under a minute doing exactly that. Only
            ' a tank that has failed to move for a sustained stretch has
            ' learned anything worth keeping.
            If stuckS > TankDriveTune.PIN_S Then
                Dim probe = pos + fwd * (TankDriveTune.HULL_R + nav.cell_m)
                nav.Pin(probe.X, probe.Y)
                stuckS = 0.0F
                reverseS = TankDriveTune.REVERSE_S
            End If
            Return
        End If

        ' OTHER TANKS ARE NOT PINNED. They move; pinning one would leave a
        ' permanent hole in the map where a tank happened to pause. Waiting is
        ' the right answer, and the stuck timer eventually sends this one
        ' somewhere else if the other never clears.
        If Crowded(inst, others, nxt) Then
            stopReason = StopWhy.Traffic
            speed = 0.0F
            stuckS += dt
            If stuckS > TankDriveTune.STUCK_S Then PickGoal(inst, nav, pos)
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
        If Not noRouteLogged Then
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

    ''' <summary>Seconds wedged before the map is told about it. Deliberately
    ''' several times REPICK_S: a pin is permanent and persisted, so it should
    ''' record a tank that could not get out, never one that brushed a wall.
    ''' </summary>
    Public PIN_S As Single = 5.0F

    ''' <summary>How long, and how fast, a wedged tank backs out.</summary>
    Public REVERSE_S As Single = 1.5F
    Public REVERSE_MS As Single = 3.0F

    ''' <summary>Centre to centre spacing two hulls keep. A hull is about 7 m
    ''' long, so this leaves them near enough to look like a formation and far
    ''' enough not to interpenetrate.</summary>
    Public SEPARATION_M As Single = 8.0F
End Module

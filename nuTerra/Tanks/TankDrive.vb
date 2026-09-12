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
''' SEEDED, SO A CAPTURE REPEATS. Every random choice comes from a generator
''' seeded off the tank's own id, and every step is taken against ANIM_DELTA
''' rather than the frame time. Two runs of the same build therefore put the
''' same tank in the same place on the same frame, which is the only reason a
''' still of thirty moving vehicles can be compared with another still.
''' </summary>
Public Class TankDrive

    ''' <summary>Where this tank is trying to get to, in world XZ.</summary>
    Public goal As Vector2
    Public hasGoal As Boolean

    ''' <summary>The route this hull was handed at load, as world waypoints,
    ''' or Nothing to wander. See PickGoal.</summary>
    Public path As List(Of Vector2)

    ''' <summary>Which waypoint it is driving at.</summary>
    Public pathAt As Integer = 0

    ''' <summary>Set once, when the last waypoint is reached. This is the race
    ''' result: the first hull of a side to set it got its team to the enemy
    ''' base.</summary>
    Public arrived As Boolean = False

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

    Public rng As Random

    ''' <summary>
    ''' Drive one tank for one step.
    ''' </summary>
    Public Sub Advance(inst As TankInstance, nav As TankNav,
                       others As List(Of TankInstance), dt As Single)
        If dt <= 0.0F Then Return
        If rng Is Nothing Then rng = New Random(&H7A2B0000 Xor inst.id)

        Dim pos As New Vector2(inst.position.X, inst.position.Z)

        goalS += dt
        If Not hasGoal OrElse (goal - pos).Length < TankDriveTune.ARRIVE_M OrElse
           goalS > TankDriveTune.GOAL_PATIENCE_S Then
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
            stopReason = StopWhy.Ground
            speed = 0.0F
            stuckS += dt

            ' COMMIT TO THE NEW GOAL BEFORE ASKING FOR ANOTHER. Repicking on
            ' every blocked frame was what deadlocked the first fleet: a fresh
            ' random goal each frame means a fresh desired heading each frame,
            ' so the turn is a jitter about the average and the tank never
            ' completes the turn that would take it away from the wall. It
            ' needs long enough to actually swing round.
            blockedS += dt
            If blockedS > TankDriveTune.REPICK_S Then
                blockedS = 0.0F
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
    ''' Somewhere open to head for.
    '''
    ''' A RING, NOT A DISC. Sampling a uniform disc puts most of the candidates
    ''' close to the tank, so it shuffles about instead of crossing ground. The
    ''' minimum radius is what makes it travel.
    '''
    ''' Gives up after a bounded number of tries rather than searching: a tank
    ''' boxed in badly enough that two dozen throws all miss is a tank that
    ''' should sit still this frame and be asked again next frame, not one that
    ''' should spend the frame proving it is stuck.
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

        For attempt = 1 To TankDriveTune.GOAL_TRIES
            Dim a = rng.NextDouble() * Math.PI * 2.0
            Dim r = TankDriveTune.GOAL_MIN_M +
                    rng.NextDouble() * (TankDriveTune.GOAL_MAX_M - TankDriveTune.GOAL_MIN_M)
            Dim gx = pos.X + CSng(Math.Cos(a) * r)
            Dim gz = pos.Y + CSng(Math.Sin(a) * r)
            If nav.CanStand(gx, gz, TankDriveTune.HULL_R) Then
                goal = New Vector2(gx, gz)
                hasGoal = True
                ' stuckS is NOT cleared here. A new goal is not progress, and
                ' zeroing it on every pick is what made the first fleet report
                ' zero stuck tanks while not one of them was moving.
                Return
            End If
        Next
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

    ''' <summary>Goals are thrown into this ring around the tank.</summary>
    Public GOAL_MIN_M As Single = 60.0F
    Public GOAL_MAX_M As Single = 220.0F
    Public GOAL_TRIES As Integer = 24

    ''' <summary>Seconds on one goal before giving up on it. A tank still
    ''' trying after this is circling something it cannot pass.</summary>
    Public GOAL_PATIENCE_S As Single = 45.0F

    ''' <summary>Seconds held at a standstill before choosing again.</summary>
    Public STUCK_S As Single = 1.5F

    ''' <summary>Seconds of being blocked before trying a different goal. Long
    ''' enough for the hull to have swung a useful part of the way round at
    ''' TURN_RATE_RAD - about 40 degrees - so the tank commits to a direction
    ''' instead of jittering between fresh random ones.</summary>
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

Imports OpenTK.Mathematics

''' <summary>
''' GO ROUND BY RANGE. The owner's algorithm, 2026-09-16:
'''
'''   "we do this by range and we want to go around based on range. we want to
'''    look to the out side 2 rays for a longer one. that may be away around.
'''    we turn and we check again and see if that pans out with the new scan.
'''    if we pick up a wall that goes across the scanner, we are not going that
'''    way. unless the rear says its worse the other. (we turn tank so we use
'''    rear and not turn the tank completely around."
'''
''' NOT A CANDIDATE LIST. LearnBrain picks a move from thirteen and scores what
''' happened; this one READS the scan and turns toward what it measured. The
''' difference matters: a candidate list needs a hundred attempts before it
''' knows anything, and this knows on the first scan - because the longest ray
''' IS the answer to "which way is more open", and no amount of trying things
''' at random discovers it faster than looking.
'''
''' THE OUTSIDE RAYS, NOT THE LONGEST ANYWHERE. A long ray near the nose is the
''' gap you are already driving at; it tells you nothing about going ROUND.
''' The outermost pair each side is where an edge shows up first, which is why
''' those are the ones worth comparing.
'''
''' PANS OUT OR IT DOES NOT. Turning toward a long ray is a HYPOTHESIS: that
''' the opening continues once the hull faces it. The scan after the turn is
''' the test. Committing to a turn without re-testing is how a tank drives
''' confidently into the wall behind the gap it saw.
'''
''' A WALL ACROSS THE SCANNER ends it. When the returns are one flat surface
''' spanning the arc, there is no way through in front, and turning a few more
''' degrees toward its slightly longer end is just walking the wall. Then the
''' REAR arc is consulted - and if behind is better, the hull REVERSES. It does
''' not spin 180: a tank has a back, using it costs no turning at all, and a
''' full turn in a space tight enough to be stuck in is how a hull wedges.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Public Class RangeBrain
    Implements IBrain

    Public Shared DRIVER As Integer = 0

    Private Const ARRIVE_M As Single = 5.0F
    Private Shared ReadOnly FULL_LOCK As Single = MathHelper.DegreesToRadians(20.0F)
    Private Shared ReadOnly TURN_FIRST As Single = MathHelper.DegreesToRadians(50.0F)

    ''' <summary>Close enough that the nose cannot come round in time.</summary>
    Private Const BLOCK_M As Single = 5.0F

    ''' <summary>How much longer an outside ray has to be before it counts as
    ''' a way round rather than noise. Half a cell is the staircase; this is
    ''' comfortably past it.</summary>
    Private Const BETTER_M As Single = 3.0F

    ''' <summary>Start easing off this far out. See creep().</summary>
    Private Const SLOW_FROM_M As Single = 15.0F

    ''' <summary>
    ''' A wall whose normal is inside this of the nose is SQUARE ON, and its
    ''' tangent is useless.
    '''
    ''' "this is a legit case we are scanning square to the map" - the owner,
    ''' and he is right: a wall built on the grid and met head-on genuinely
    ''' HAS a normal near zero. The fit is not failing.
    '''
    ''' But tangent = normal +/- 90 then orders a hard sideways turn, which
    ''' tells you nothing about which END of the wall is nearer. Nose-on to a
    ''' flat face the angle carries no information at all - both ways along it
    ''' look identical to the fit, and only the RANGES break the tie.
    ''' </summary>
    Private Shared ReadOnly SQUARE_ON As Single = MathHelper.DegreesToRadians(20.0F)

    ''' <summary>A turn is given this long to pan out before it is judged.</summary>
    Private Const TEST_S As Single = 1.2F

    ''' <summary>Give up widening after this many rays past the heading the
    ''' probe liked - about 51 degrees at the scan's 8.6 spacing. Past that it
    ''' is not a tight gap, it is the wrong way round.
    '''
    ''' A fixed-degree step used to live here too. It is gone: the widen walks
    ''' the scan's own bearings now, so the step size IS the ray spacing and
    ''' there is nothing left to choose.</summary>
    Private Const WIDEN_MAX As Integer = 6

    ''' <summary>Reverse for this long once the rear wins.</summary>
    Private Const BACK_S As Single = 2.0F

    Private Enum St
        Seek = 0
        Turning        ' swinging toward a longer ray, not yet re-scanned
        Follow         ' the turn panned out: hold it until the goal opens up
        Backing
    End Enum

    Private state As St = St.Seek
    Private wantHeading As Single
    Private testFor As Single
    Private backFor As Single
    Private lastOpen As Single

    ' ---- wall following -------------------------------------------------
    '
    ' "It would probably walk right around with this logic if we moved tried
    '  to go to home and use the radar to keep us around the same distance.
    '  move turn to target or until we touch the right ray. If we are turning
    '  left it is the left ray." - the owner.
    '
    ' HOLD A DISTANCE, NOT A HEADING. Holding the heading the turn found walks
    ' straight off the end of a wall that curves, and straight into one that
    ' turns in. Holding the DISTANCE on the side ray is what makes a hull go
    ' round a shape it has never seen - it is the same rule at every point of
    ' the perimeter, so the shape does not have to be known.
    '
    ' THE SIDE IS THE SIDE WE TURNED. Turn left and the wall ends up on the
    ' right, so the RIGHT ray holds it - and the owner has it the other way
    ' round in his message because he is naming the ray he turned toward. What
    ' matters is that it is ONE side for the whole manoeuvre: swapping sides
    ' halfway is how a hull ends up in a corner it has already been round.
    Private followLeft As Boolean      ' turning left, so the wall is on the right
    Private holdAt As Single           ' the distance to keep
    Private lostFor As Single          ' how long the side ray has seen nothing

    Public Why As String = "-"

    ' A heartbeat, once a second while the sim runs. Two silent stops
    ' tonight were silent because the paths that stop do not log - and a
    ' hull standing still looks the same whatever it is thinking.
    Private beat As Single = 0.0F

    ' ---- THE RECORD -----------------------------------------------------
    '
    ' "i think the rear ray may have stopped us. run as is but record if the
    '  rear ray was a trigger" / "we dbl tapped a probe again then"
    '
    ' INSTRUMENTATION ONLY. Nothing below changes what the brain does - two
    ' suspicions were raised about WHY it stopped, and neither can be settled
    ' by watching it. A hull standing still looks identical whichever of them
    ' is true.
    '
    ' THE REAR IS ONLY EVER ONE DECISION: rearBest > ahead + BETTER_M sends it
    ' to Backing. Worth watching now because the sweep went to 120 degrees and
    ' the REAR arc widened with it - there are rays 55 degrees off the tail
    ' looking sideways, and one of those reading deep beats the nose and orders
    ' a reversal that has nothing to do with what is behind. So the winning
    ' ray's bearing is recorded, not just that the rear won: an outer one is
    ' the bug, a middle one is a real answer.
    Private rearTrigs As Integer = 0
    Private rearLastDeg As Single = 0.0F

    ' A PROBE THAT DID NOT MOVE FIRST. Entering Turning twice from the same
    ' spot means the first probe taught it nothing - it looked, settled, and
    ' came straight back to look again. That is a loop, not progress, and it
    ' reads on screen as a tank jiggling in place.
    Private probes As Integer = 0
    Private dblTaps As Integer = 0
    Private probeFrom As Vector2
    Private probedOnce As Boolean = False

    ''' <summary>
    ''' HOW LONG THE WORLD HAS BEEN REFUSING TO MOVE US.
    '''
    ''' BrainSim.Apply tests the next position against the hull's FIT radius
    ''' and, if it does not fit, sets the speed to zero and says nothing. So
    ''' a brain can hold full throttle against a wall for ever, steering
    ''' neatly, believing it is driving. It was doing exactly that: the side
    ''' ray held 5 m, the heading was right, and the hull had not moved in
    ''' thirty seconds.
    '''
    ''' The rays cannot see this. A ray is a line from the hull centre; the
    ''' thing that refuses the step is the hull's own WIDTH against geometry
    ''' no ray happens to pass through. Comparing intent against speed is the
    ''' only way to find out, and it costs one subtraction.
    ''' </summary>
    Private wedgedFor As Single = 0.0F

    ''' <summary>What we asked for last tick, to compare against what
    ''' happened.</summary>
    Private lastThrottle As Single = 0.0F

    ''' <summary>
    ''' LOOK AHEAD AND TURN EARLY, ONCE THREE SAMPLES AGREE.
    '''
    ''' "look ahead and turn before the side with deeper rays after we get 3
    ''' samples."
    '''
    ''' Everything until now decided at the LAST moment - blocked at 5 m,
    ''' nose-on, with no room to do anything but stop and pivot. By then the
    ''' hull's own width has removed most of the options, which is why the fit
    ''' probe kept refusing both sides.
    '''
    ''' Deciding at 15 m instead means the turn is a lane change rather than a
    ''' pivot. THREE AGREEING SAMPLES is what stops it twitching: one scan can
    ''' favour a side because of a single ray clipping a corner, and reacting
    ''' to that is how a hull weaves. Three in a row is a wall, not a glint.
    ''' </summary>
    Private Const LOOK_AHEAD_M As Single = 15.0F
    Private Const VOTES_NEEDED As Integer = 3
    Private Const VOTE_EVERY_S As Single = 0.25F
    Private votes As Integer = 0
    Private voteLeft As Boolean
    Private voteAt As Single = 0.0F

    ''' <summary>
    ''' TURN, TAKE TWO SAMPLES, THEN TURN BACK UNTIL THE BODY IS CLEAR.
    '''
    ''' "it oscillates when it turns to check out side prob. maybe turn and
    ''' get 2 samples and turn back until body is clear."
    '''
    ''' The oscillation is a decision being re-made every tick. Turning toward
    ''' a side changes the scan, the new scan favours the other side, and the
    ''' hull saws between them without ever driving anywhere.
    '''
    ''' So the look becomes a DELIBERATE ACT with an end: swing over, take two
    ''' scans once actually facing that way - one can be a single ray clipping
    ''' a corner - and then stop looking. The decision is not revisited while
    ''' the look is in progress.
    '''
    ''' And then turn BACK. Having looked 41 degrees off to see round
    ''' something, driving 41 degrees off is further from the goal than it
    ''' needs to be - so it sweeps back toward the goal and stops at the LAST
    ''' heading the hull still clears. That is the owner's "until body is
    ''' clear", and it is also what keeps the turn a lane change instead of a
    ''' detour.
    ''' </summary>
    ''' <summary>
    ''' ACTUALLY FACING IT. Not TURN_FIRST.
    '''
    ''' The samples used to start counting as soon as the heading error fell
    ''' under TURN_FIRST - fifty degrees - which is most of the way through a
    ''' forty degree turn. So it judged "did it pan out" while still pointing
    ''' at the wall, saw no improvement, called the turn a failure and went
    ''' back to seeking. Then it re-voted, turned again, and judged again.
    ''' That was the wobble: a turn abandoned before it happened, forever.
    ''' </summary>
    Private Shared ReadOnly ALIGNED As Single = MathHelper.DegreesToRadians(8.0F)

    Private Const PEEK_SAMPLES As Integer = 2
    Private peeks As Integer = 0

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            Return "range"
        End Get
    End Property

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        state = St.Seek
        LogThis("brain: RangeBrain on hull {0} - turning by range, not by guess",
                DRIVER)
    End Sub

    Public Function Tick(inp As BrainInput) As BrainOutput Implements IBrain.Tick
        Dim n = If(inp.hulls Is Nothing, 0, inp.hulls.Length)
        Dim o = BrainOutput.ForHulls(n)
        For i = 0 To n - 1
            o.why(i) = "parked"
        Next
        If n = 0 OrElse DRIVER >= n Then Return o
        If Not BrainGoal.HasTarget Then
            o.why(DRIVER) = "no goal"
            Return o
        End If

        Dim h = inp.hulls(DRIVER)
        Dim goal = BrainGoal.Target
        o.target(DRIVER) = goal

        Dim toGoal = goal - h.pos
        Dim range = toGoal.Length
        If range <= ARRIVE_M Then
            Why = "arrived"
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' The bearing to the goal, needed by more than the seeking block -
        ' the turn-back sweep asks how far it can rotate toward it.
        Dim wantGoal = CSng(Math.Atan2(toGoal.X, toGoal.Y))

        Dim hits = BrainRadar.Scan(h.pos, h.headingRad)
        Dim surf = BrainRadar.FitSurface(hits)

        ' WEDGED: we asked to move and the world did not move us.
        If lastThrottle > 0.1F AndAlso Math.Abs(h.speed) < 0.2F Then
            wedgedFor += inp.dt
        Else
            wedgedFor = 0.0F
        End If
        If wedgedFor > 0.8F AndAlso state <> St.Backing Then
            LogThis("brain: WEDGED - throttle {0:0.00} but speed {1:0.00} for {2:0.0} s. " &
                    "Backing out.", lastThrottle, h.speed, wedgedFor)
            state = St.Backing
            backFor = BACK_S
            wedgedFor = 0.0F
            holdAt = 0.0F
            Why = "wedged - backing out"
            o.throttle(DRIVER) = -0.6F
            o.why(DRIVER) = Why
            lastThrottle = -0.6F
            Return o
        End If

        beat += inp.dt
        If beat >= 1.0F Then
            beat = 0.0F
            LogThis("brain: [{0}] {1} | thr {2:0.00} speed {3:0.0} range {4:0.0} " &
                    "| surf: {5} (valid {6}, one {7}, turns {8}, face {9:0} deg) " &
                    "| probes {10} (dbl {11}) rear-trig {12}{13}",
                    state.ToString(), Why, lastThrottle, h.speed, range,
                    surf.verdict, surf.valid, surf.oneSurface, surf.turns,
                    MathHelper.RadiansToDegrees(surf.normalRad),
                    probes, dblTaps, rearTrigs,
                    If(rearTrigs > 0, String.Format(" (last {0:0} deg)", rearLastDeg), ""))
        End If

        ' ---- what the scan says --------------------------------------------
        Dim front As New List(Of BrainRadar.Hit)
        Dim rear As New List(Of BrainRadar.Hit)
        For Each q In hits
            If q.front Then front.Add(q) Else rear.Add(q)
        Next
        If front.Count < 4 Then
            o.why(DRIVER) = "no scan"
            Return o
        End If

        ' Straight ahead: the middle of the front arc.
        Dim mid = front.Count \ 2
        Dim ahead = Math.Min(front(mid).dist, Math.Min(front(mid - 1).dist,
                                                      front(Math.Min(mid + 1, front.Count - 1)).dist))

        ' ---- backing out ---------------------------------------------------
        If state = St.Backing Then
            backFor -= inp.dt
            If backFor > 0.0F Then
                o.throttle(DRIVER) = -0.6F
                Why = "backing - rear was better"
                o.why(DRIVER) = Why
                Return o
            End If

            ' ---- THE STEP THAT WAS MISSING ----------------------------------
            '
            ' "it doesnt move forward after it backs up to clear. it is
            '  missing that step" - "it probes, it touches out side, it turns
            '  back AND MOVES FOWARD and repete" - the owner.
            '
            ' Backing used to hand straight back to Seek, and Seek steers at
            ' the GOAL - which is back through the thing it just reversed away
            ' from. So the room the reverse bought was spent driving into the
            ' same spot again, and the cycle never closed.
            '
            ' The reverse is not the manoeuvre. It is the SPACE TO TURN IN, and
            ' nothing was turning. So: take the deepest ray this scan has,
            ' point at it, and hand to Turning - which swings on the spot, takes
            ' its two confirming samples, sweeps back toward the goal as far as
            ' the rays still allow, and then DRIVES. Probe, touch the outside,
            ' turn back, move forward, repeat.
            Dim deep = front(0)
            For Each q In front
                If q.dist > deep.dist Then deep = q
            Next
            wantHeading = wrap_pi(h.headingRad + deep.angle)
            ' Anything is an improvement on wedged, so the confirming test
            ' cannot reject the only heading we have left.
            lastOpen = 0.0F
            testFor = TEST_S
            peeks = 0
            followLeft = (deep.angle < 0.0F)
            holdAt = 0.0F
            lostFor = 0.0F
            state = St.Turning
            note_probe(h.pos, "after backing")
            Why = String.Format("backed up - turning to {0:0} deg ({1:0.0} m deep)",
                                MathHelper.RadiansToDegrees(deep.angle), deep.dist)
            LogThis("brain: {0}", Why)
            o.why(DRIVER) = Why
            lastThrottle = 0.0F
            Return o
        End If

        ' ---- a turn under test ---------------------------------------------
        If state = St.Turning Then
            Dim err = wrap_pi(wantHeading - h.headingRad)
            o.steer(DRIVER) = Math.Clamp(err / FULL_LOCK, -1.0F, 1.0F)
            testFor -= inp.dt
            If Math.Abs(err) > ALIGNED Then
                Why = String.Format("turning to look ({0:0} deg to go)",
                                    MathHelper.RadiansToDegrees(Math.Abs(err)))
                o.why(DRIVER) = Why
                Return o
            End If
            ' Facing it now. TWO SAMPLES before anything is believed.
            peeks += 1
            If peeks < PEEK_SAMPLES Then
                Why = "looking (" & peeks.ToString() & " of " &
                      PEEK_SAMPLES.ToString() & ")"
                o.why(DRIVER) = Why
                lastThrottle = o.throttle(DRIVER)
                Return o
            End If

            If testFor <= 0.0F OrElse peeks >= PEEK_SAMPLES Then
                If ahead > lastOpen + BETTER_M OrElse ahead > BLOCK_M * 2.0F Then
                    Why = "it panned out"
                    ' TURN BACK UNTIL THE BODY IS CLEAR. Sweep from the heading
                    ' we looked along, back toward the goal, and keep the LAST
                    ' one the hull still clears. Looking 41 degrees off does not
                    ' mean driving 41 degrees off.
                    ' UNTIL THE RAYS CLEAR, not until the nav probe says so.
                    ' "i meant the rays cleared" - and the rays are the thing
                    ' the hull can actually see; the probe answers a different
                    ' question and answers it about ground, not about what is
                    ' in the way.
                    Dim settled = wantHeading
                    Dim need = Math.Max(SLOW_FROM_M, h.FitRadius * 3.0F)
                    Dim steps = 8
                    For k = 1 To steps
                        Dim f = k / CSng(steps)
                        Dim test = wrap_pi(wantHeading +
                                           wrap_pi(wantGoal - wantHeading) * f)
                        ' What does the scan see along that heading?
                        If ray_toward(front, wrap_pi(test - h.headingRad)) >= need Then
                            settled = test
                        Else
                            Exit For
                        End If
                    Next
                    If Math.Abs(wrap_pi(settled - wantHeading)) > 0.01F Then
                        LogThis("brain: turned back {0:0} deg toward the goal and still clear",
                                MathHelper.RadiansToDegrees(Math.Abs(wrap_pi(settled - wantHeading))))
                    End If

                    ' ---- WILL THE BODY CLEAR WHERE THE PROBE HIT? -----------
                    '
                    ' "make sure we are checking if we can clear the body at
                    '  where the probe hit. and turn away more if needed."
                    '
                    ' Everything above this line judges by RAYS, and a ray is a
                    ' line while the hull is 3.4 m wide. Worse, the sweep only
                    ' ever gives ground TOWARD the goal - so a heading the rays
                    ' like and the body cannot take had no way to be widened.
                    ' That is the difference between finding a solution and
                    ' finding a good one: it was squeezing down the tightest
                    ' line the rays would allow, every time.
                    '
                    ' So test the settled heading with the rule the SIM will
                    ' apply to it - Standable at the fit radius, stepped along
                    ' the way it would drive - and if it fails, turn further
                    ' AWAY, the same way it already turned, a ray at a time.
                    ' TURN BY ANGLES WE CAN GRAB. "look at a way to find where
                    ' we are in relation to the points and turn by angles we
                    ' can grab" - the owner.
                    '
                    ' The widen used to step a flat nine degrees, which is a
                    ' bearing NOTHING WAS MEASURED ALONG: between two rays
                    ' there is no reading at all, so it was asking will_clear
                    ' about a direction the scan had never looked. Walk the
                    ' scan's own bearings outward instead - nearest first, away
                    ' from the goal - and every heading it commits to is one it
                    ' has a return for.
                    Dim awaySign = If(followLeft, -1.0F, 1.0F)
                    Dim cands As New List(Of Single)
                    For Each q In front
                        Dim cand = wrap_pi(h.headingRad + q.angle)
                        If wrap_pi(cand - settled) * awaySign > 0.0F Then cands.Add(cand)
                    Next
                    cands.Sort(Function(u, v) (wrap_pi(u - settled) * awaySign).
                                              CompareTo(wrap_pi(v - settled) * awaySign))

                    Dim widened = 0
                    Do While widened < WIDEN_MAX AndAlso widened < cands.Count AndAlso
                             Not will_clear(h.pos, settled, h.FitRadius, need)
                        settled = cands(widened)
                        widened += 1
                    Loop

                    If widened > 0 Then
                        If will_clear(h.pos, settled, h.FitRadius, need) Then
                            LogThis("brain: body would not clear - grabbed the ray {0:0} deg " &
                                    "further {1}, which does",
                                    MathHelper.RadiansToDegrees(Math.Abs(wrap_pi(settled - wantHeading))),
                                    If(followLeft, "left", "right"))
                        Else
                            ' NOWHERE IN 54 DEGREES. The probe was right that
                            ' the rays got longer and wrong that the hull could
                            ' follow them. Do not drive it - go back and decide
                            ' from this scan, which is the branch that can pick
                            ' the other side or the rear.
                            Why = "rays opened but the body will not fit"
                            state = St.Seek
                            LogThis("brain: {0} - {1} ray(s) of widening and still blocked",
                                    Why, widened)
                            o.throttle(DRIVER) = 0.0F
                            o.why(DRIVER) = Why
                            lastThrottle = 0.0F
                            Return o
                        End If
                    End If

                    wantHeading = settled
                    ' HOLD IT. Going straight back to seeking is what made it
                    ' oscillate: Seek steers at the GOAL, the goal is through
                    ' the wall, so it turned along the wall and immediately
                    ' turned back into it, over and over.
                    state = St.Follow
                Else
                    Why = "it did not pan out"
                    state = St.Seek
                End If
                LogThis("brain: turn tested - open was {0:0.0} m, now {1:0.0} m: {2}",
                        lastOpen, ahead, Why)
            End If
            ' STOPPED WHILE IT TURNS. "i want it to stop when it finds the
            ' wall and turn to that angle."
            '
            ' It used to roll at 0.4 through the last 50 degrees and through
            ' the test, which meant the confirming scan was taken several
            ' metres from where the decision was made - so it was not testing
            ' the thing it decided on. Turning on the spot makes the second
            ' scan comparable with the first.
            o.throttle(DRIVER) = 0.0F
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' ---- seeking --------------------------------------------------------
        Dim goalErr = wrap_pi(wantGoal - h.headingRad)

        ' ---- following the line that worked ---------------------------------
        '
        ' Drive the heading the turn found, and only give it up when the way
        ' to the GOAL is actually open - which is a question the scan can
        ' answer: how far is the ray pointing nearest the goal.
        If state = St.Follow Then
            ' HOW FAR THE BODY CAN GO, not how far the ray can see.
            '
            ' The record caught this: "[Follow] along wall, 6.0 m (want 11.5)
            ' | thr 1.00 speed 12.0" and then "WEDGED - throttle 1.00 but speed
            ' 0.00". Full throttle, the front rays reading over 15 m clear, and
            ' the hull could not move at all.
            '
            ' Follow was the one state with no body check left in it. It drove
            ' on creep(ahead), and `ahead` is a RAY distance measured at
            ' TRACE_R - a thin probe - while BrainSim will only step the hull
            ' where Standable clears at its FIT radius. Running alongside a
            ' wall is exactly where those two disagree: the rays point down the
            ' gap and the corner of the hull is already in it.
            Dim bodyAhead = body_ahead(h.pos, h.headingRad, h.FitRadius, ahead)
            If bodyAhead <= BLOCK_M Then
                ' Something new in front. Decide again from this scan.
                state = St.Seek
            Else
                Dim toward = ray_toward(front, goalErr)
                If toward > BLOCK_M * 3.0F AndAlso Math.Abs(goalErr) < TURN_FIRST Then
                    Why = "goal is open again"
                    state = St.Seek
                    LogThis("brain: back on the goal - {0:0.0} m clear toward it", toward)
                Else
                    ' THE SIDE RAY. The outermost one on the side the wall is,
                    ' which is the opposite side to the way we turned.
                    Dim side = If(followLeft, front(front.Count - 1), front(0))
                    Dim seen = side.found

                    If holdAt <= 0.0F AndAlso seen Then holdAt = side.dist

                    Dim steer2 As Single
                    If seen Then
                        lostFor = 0.0F
                        ' Too far from the wall, steer toward it; too close,
                        ' steer away. One proportional term, because a wall is
                        ' not a thing you need a controller for.
                        Dim errD = side.dist - holdAt
                        Dim k = Math.Clamp(errD / 4.0F, -1.0F, 1.0F)
                        steer2 = If(followLeft, k, -k)
                        Why = String.Format("along wall, {0:0.0} m (want {1:0.0})",
                                            side.dist, holdAt)
                    Else
                        ' LOST IT - the wall ended. That is the corner we were
                        ' driving to find, so turn INTO it and keep going round
                        ' rather than sailing off across open ground.
                        lostFor += inp.dt
                        steer2 = If(followLeft, 1.0F, -1.0F)
                        Why = "wall ended - going round it"
                        If lostFor > 3.0F Then
                            state = St.Seek
                            LogThis("brain: wall is gone - back to the goal")
                        End If
                    End If

                    o.steer(DRIVER) = steer2
                    ' Braked on what the BODY has, so it crawls into a
                    ' narrowing gap instead of arriving at it at 12 m/s.
                    o.throttle(DRIVER) = creep(bodyAhead)
                    o.why(DRIVER) = Why
                    lastThrottle = o.throttle(DRIVER)
                    Return o
                End If
            End If
        End If

        If ahead > BLOCK_M Then
            ' ---- looking ahead, before it becomes a problem ----------------
            If ahead < LOOK_AHEAD_M Then
                voteAt += inp.dt
                If voteAt >= VOTE_EVERY_S Then
                    voteAt = 0.0F
                    Dim lb = Math.Max(front(0).dist, front(1).dist)
                    Dim rb = Math.Max(front(front.Count - 1).dist,
                                      front(front.Count - 2).dist)
                    Dim thisLeft = (lb >= rb)
                    If votes > 0 AndAlso thisLeft = voteLeft Then
                        votes += 1
                    Else
                        votes = 1
                        voteLeft = thisLeft
                    End If

                    If votes >= VOTES_NEEDED Then
                        ' Three in a row. Commit while there is still room to
                        ' turn in - and only if the hull actually clears it.
                        Dim deepest = front(0)
                        For Each q In front
                            Dim ours = If(voteLeft, q.angle < 0.0F, q.angle > 0.0F)
                            If ours AndAlso q.dist > deepest.dist Then deepest = q
                        Next
                        Dim head2 = wrap_pi(h.headingRad + deepest.angle)
                        If will_clear(h.pos, head2, h.FitRadius,
                                      Math.Max(6.0F, h.FitRadius * 1.5F)) Then
                            wantHeading = head2
                            lastOpen = ahead
                            testFor = TEST_S
                            followLeft = voteLeft
                            holdAt = 0.0F
                            lostFor = 0.0F
                            votes = 0
                            peeks = 0
                            state = St.Turning
                            note_probe(h.pos, "early turn")
                            Why = String.Format("early turn {0} at {1:0.0} m ({2:0} deg, {3:0.0} m deep)",
                                                If(voteLeft, "left", "right"), ahead,
                                                MathHelper.RadiansToDegrees(deepest.angle),
                                                deepest.dist)
                            LogThis("brain: {0} - three samples agreed", Why)
                            o.why(DRIVER) = Why
                            lastThrottle = o.throttle(DRIVER)
                            Return o
                        Else
                            votes = 0
                        End If
                    End If
                End If
            Else
                votes = 0
            End If

            o.steer(DRIVER) = Math.Clamp(goalErr / FULL_LOCK, -1.0F, 1.0F)
            If Math.Abs(goalErr) > TURN_FIRST Then
                Why = "turning"
            Else
                Dim thr = creep(ahead)
                If range < 20.0F Then thr = Math.Min(thr, Math.Max(0.25F, range / 20.0F))
                o.throttle(DRIVER) = thr
                Why = "seeking"
            End If
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' ---- blocked: decide by RANGE ---------------------------------------
        '
        ' THE OUTSIDE TWO EACH SIDE. Where an edge shows itself first.
        Dim leftBest = Math.Max(front(0).dist, front(1).dist)
        Dim rightBest = Math.Max(front(front.Count - 1).dist, front(front.Count - 2).dist)
        Dim bestOut = Math.Max(leftBest, rightBest)

        ' A WALL RIGHT ACROSS THE SCANNER. One surface, spanning, and no end
        ' of it is meaningfully longer than the middle - there is nothing to
        ' turn toward.
        Dim wallAcross = surf.valid AndAlso surf.oneSurface AndAlso
                         (bestOut < ahead + BETTER_M)
        ' will_clear below may promote this to True when no side fits.

        If bestOut > ahead + BETTER_M AndAlso Not wallAcross Then
            Dim goLeft = (leftBest >= rightBest)

            ' TEST THE SIDE BEFORE COMMITTING TO IT. The longer ray is only a
            ' candidate; the hull has to fit down it. If the better-looking
            ' side will not clear, take the other one - and if neither will,
            ' fall through to the rear, which is what a wall across does.
            Dim probe = Math.Max(8.0F, h.FitRadius * 2.0F)
            Dim headL = wrap_pi(h.headingRad + front(0).angle)
            Dim headR = wrap_pi(h.headingRad + front(front.Count - 1).angle)
            Dim okL = will_clear(h.pos, headL, h.FitRadius, probe)
            Dim okR = will_clear(h.pos, headR, h.FitRadius, probe)
            If goLeft AndAlso Not okL AndAlso okR Then
                goLeft = False
                LogThis("brain: left looked longer but the hull will not clear it - going right")
            ElseIf Not goLeft AndAlso Not okR AndAlso okL Then
                goLeft = True
                LogThis("brain: right looked longer but the hull will not clear it - going left")
            ElseIf Not okL AndAlso Not okR Then
                ' NEITHER FITS. Do not turn into either of them - fall through
                ' to the rear, which is what a wall across the scanner does.
                ' Setting the flag alone would not have stopped the turn below;
                ' this has to leave the block.
                LogThis("brain: neither side will clear the hull ({0:0.0} m fit) - " &
                        "treating as a wall", h.FitRadius)
                wallAcross = True
            End If

            If Not wallAcross Then
            Dim pick = If(goLeft, front(0), front(front.Count - 1))

            ' THE RAY NEXT TO THE WALL. "we need to check the ray next to the
            ' wall it found. if it hit nothing, we should turn to match the
            ' angle of that wall."
            '
            ' Walk in from the open side until the returns start hitting. The
            ' last miss before that is the EDGE, and the hit beside it is the
            ' wall's end. If there is a clean miss there, the way round is not
            ' the bearing of the longest ray - that points diagonally at the
            ' wall's end and drives into it. It is the wall's OWN angle, so the
            ' hull runs alongside and past it.
            Dim edgeMiss = -1
            If goLeft Then
                For k = 0 To front.Count - 1
                    If front(k).found Then Exit For
                    edgeMiss = k
                Next
            Else
                For k = front.Count - 1 To 0 Step -1
                    If front(k).found Then Exit For
                    edgeMiss = k
                Next
            End If

            Dim byWall = False
            Dim squareOn = surf.valid AndAlso Math.Abs(surf.normalRad) < SQUARE_ON

            If squareOn Then
                ' SQUARE ON: THE RAYS DECIDE, NOT THE ANGLE. Turn toward the
                ' deeper side - the owner's override. The deepest ray on that
                ' side is the bearing, because it is the one that has actually
                ' found its way past the end.
                Dim deepest = pick
                For Each q In front
                    Dim onOurSide = If(goLeft, q.angle < 0.0F, q.angle > 0.0F)
                    If onOurSide AndAlso q.dist > deepest.dist Then deepest = q
                Next
                wantHeading = wrap_pi(h.headingRad + deepest.angle)
                Why = String.Format("square on - deeper {0} at {1:0} deg, {2:0.0} m",
                                    If(goLeft, "left", "right"),
                                    MathHelper.RadiansToDegrees(deepest.angle),
                                    deepest.dist)
            ElseIf edgeMiss >= 0 AndAlso surf.valid AndAlso surf.oneSurface Then
                ' Angled wall with a clean edge: run along its face.
                Dim tangent = surf.normalRad + If(goLeft, -MathHelper.PiOver2,
                                                           MathHelper.PiOver2)
                wantHeading = wrap_pi(h.headingRad + tangent)
                byWall = True
            Else
                wantHeading = wrap_pi(h.headingRad + pick.angle)
            End If

            lastOpen = ahead
            testFor = TEST_S
            followLeft = goLeft
            holdAt = 0.0F           ' taken from the first side reading
            lostFor = 0.0F
            peeks = 0
            state = St.Turning
            note_probe(h.pos, "blocked")
            If Not squareOn Then
                Why = If(byWall, "along the wall ", "trying ") &
                      If(goLeft, "left", "right") &
                      String.Format(" ({0:0.0} m vs {1:0.0})", bestOut, ahead)
            End If
            LogThis("brain: blocked at {0:0.0} m - {1}{2}", ahead, Why,
                    If(byWall, String.Format(", wall face {0:0} deg",
                                             MathHelper.RadiansToDegrees(surf.normalRad)),
                       ", no clean edge - using the long ray"))
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
            End If
        End If

        ' ---- the front is shut. IS THE REAR BETTER? --------------------------
        Dim rearBest = 0.0F
        Dim rearWin As Single = 0.0F        ' the winning ray's bearing off the TAIL
        For Each q In rear
            If q.dist > rearBest Then
                rearBest = q.dist
                ' Rear bearings come back near +/-PI; off-the-tail is what a
                ' person can read, and what says whether it is a sideways ray.
                rearWin = wrap_pi(q.angle - CSng(Math.PI))
            End If
        Next
        If rearBest > ahead + BETTER_M Then
            state = St.Backing
            backFor = BACK_S
            rearTrigs += 1
            rearLastDeg = MathHelper.RadiansToDegrees(rearWin)
            Why = String.Format("rear is better ({0:0.0} m vs {1:0.0})", rearBest, ahead)
            LogThis("brain: REAR TRIGGER {0} - {1}, winning ray {2:0} deg off the tail. " &
                    "Reversing rather than turning round.",
                    rearTrigs, Why, rearLastDeg)
            o.throttle(DRIVER) = -0.6F
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' BOXED IN USED TO MEAN STOPPED FOREVER. No throttle was set here and
        ' no state changed, so the hull sat still for the rest of the run with
        ' nothing in the log to say why - which read exactly like the turn
        ' having failed to release the brakes.
        '
        ' Nothing ahead, nothing to either side, and the rear no better does
        ' not mean there is no way out: it means the hull cannot SEE one from
        ' here. Backing up moves the sensor, which is the only thing that
        ' changes the answer.
        state = St.Backing
        backFor = BACK_S
        o.throttle(DRIVER) = -0.6F
        Why = "boxed in - backing up to see more"
        LogThis("brain: boxed in at {0:0.0} m ahead, rear {1:0.0} m - reversing to " &
                "move the sensor", ahead, rearBest)
        o.why(DRIVER) = Why
        lastThrottle = o.throttle(DRIVER)
        Return o
    End Function

    ''' <summary>
    ''' HOW FAR THE HULL ITSELF CAN GO THIS WAY, up to what the rays saw.
    '''
    ''' will_clear answers yes or no over a fixed distance; this answers HOW
    ''' FAR, which is what a throttle needs. Same rule either way - Standable
    ''' at the fit radius, the rule BrainSim will apply to every step.
    ''' </summary>
    Private Shared Function body_ahead(pos As Vector2, headingRad As Single,
                                       fitR As Single, limit As Single) As Single
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Dim t = 1.0F
        While t <= limit
            If Not BrainNav.Standable(pos.X + dx * t, pos.Y + dz * t, fitR) Then
                Return t - 1.0F
            End If
            t += 1.0F
        End While
        Return limit
    End Function

    ''' <summary>
    ''' RECORD A PROBE, AND WHETHER IT IS THE SECOND FROM THE SAME SPOT.
    '''
    ''' Called at every entry into Turning. A metre is the threshold because a
    ''' probe is taken at zero throttle - if the hull has not moved a metre
    ''' since the last one, it is asking the same question from the same place
    ''' and will get the same answer.
    ''' </summary>
    Private Sub note_probe(pos As Vector2, what As String)
        probes += 1
        If probedOnce AndAlso (pos - probeFrom).Length < 1.0F Then
            dblTaps += 1
            LogThis("brain: DOUBLE TAP - probe {0} ({1}) is {2:0.0} m from probe {3}. " &
                    "Nothing was driven in between.",
                    probes, what, (pos - probeFrom).Length, probes - 1)
        End If
        probeFrom = pos
        probedOnce = True
    End Sub

    ''' <summary>How far the scan sees in the direction of the goal - the ray
    ''' whose bearing is nearest to it. This is what says whether giving up the
    ''' wall is safe yet.</summary>
    Private Shared Function ray_toward(front As List(Of BrainRadar.Hit),
                                       bearing As Single) As Single
        Dim best = 0.0F
        Dim gap = Single.MaxValue
        For Each q In front
            Dim d = Math.Abs(wrap_pi(q.angle - bearing))
            If d < gap Then gap = d : best = q.dist
        Next
        Return best
    End Function

    ''' <summary>
    ''' How hard to drive with this much room ahead.
    '''
    ''' "it needs to slow down faster." The old ramp held a FLOOR of 0.3 -
    ''' 3.6 m/s at 12 m/s top speed - all the way down to the 5 m block
    ''' distance, so the hull arrived at every wall still doing walking pace
    ''' and then cut to nothing.
    '''
    ''' This starts backing off at 15 m instead of 10, SQUARES the fraction
    ''' so it comes off fast and then eases, and floors at 0.12 - a crawl
    ''' rather than a walk. The sim has no inertia (speed = throttle x top),
    ''' so this curve IS the braking.
    ''' </summary>
    ''' <summary>
    ''' WILL THE HULL ACTUALLY CLEAR IT GOING THAT WAY?
    '''
    ''' "it needs to check the ray on the ray it hit when trying to turn and
    ''' see if it will clear it moving foward."
    '''
    ''' A RAY IS A LINE; A TANK IS 3.4 m WIDE. A ray reading 20 m down a gap
    ''' two metres across is telling the truth and still describing somewhere
    ''' the hull cannot go. That mismatch is what wedged it: the heading was
    ''' right, the ray was long, and BrainSim refused every step because the
    ''' FIT radius did not clear.
    '''
    ''' So the candidate heading is tested with the SAME rule the sim will
    ''' apply to it - Standable at the hull's own fit radius - stepped along
    ''' the direction it would drive. A turn is only worth making if the hull
    ''' can follow it.
    ''' </summary>
    Private Shared Function will_clear(pos As Vector2, headingRad As Single,
                                       fitR As Single, metres As Single) As Boolean
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Dim t = 1.0F
        While t <= metres
            If Not BrainNav.Standable(pos.X + dx * t, pos.Y + dz * t, fitR) Then
                Return False
            End If
            t += 1.0F
        End While
        Return True
    End Function

    Private Shared Function creep(ahead As Single) As Single
        If ahead >= SLOW_FROM_M Then Return 1.0F
        Dim f = (ahead - BLOCK_M) / (SLOW_FROM_M - BLOCK_M)
        If f < 0.0F Then f = 0.0F
        Return Math.Max(0.12F, f * f)
    End Function

    Private Shared Function wrap_pi(a As Single) As Single
        While a > Math.PI
            a -= CSng(Math.PI * 2.0)
        End While
        While a < -Math.PI
            a += CSng(Math.PI * 2.0)
        End While
        Return a
    End Function

End Class

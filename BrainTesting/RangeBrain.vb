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
    ''' <summary>Where the braking curve starts. Raised with the horizon: at
    ''' 12 m/s a hull covers this in two seconds, so braking that begins at
    ''' fifteen metres is braking that begins too late to be gentle - which is
    ''' how a curve meant to ease off became a curve that slams.</summary>
    Private Const SLOW_FROM_M As Single = 25.0F

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

    ''' <summary>What the wedge entry brakes against on its one frame, before
    ''' St.Backing measures the room behind for itself. A wedged hull has by
    ''' definition just failed to move, so it gets the crawl.</summary>
    Private Const BACK_ROOM_M As Single = 4.0F

    Private Enum St
        Seek = 0
        Turning        ' swinging toward a longer ray, not yet re-scanned
        Follow         ' the turn panned out: hold it until the goal opens up
        Door           ' hits both sides, open in the middle: line up and go through
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
    ' ---- the doorway ----------------------------------------------------
    '
    ' "now we need to deal with left and right hits and center being open.
    '  find center and aim if we can fit. if we cant back up using same logic
    '  as going forward. go around or go though."
    '
    ' A DOOR IS NOT A WALL WITH A LONG RAY BESIDE IT. The blocked branch asks
    ' which SIDE is deeper and turns that way - and nose-on to a gateway both
    ' sides are equally shut, so it would pick one at random and drive round a
    ' building it could have gone through. The gap is a different shape in the
    ' scan and it deserves a different answer: returns on the left, returns on
    ' the right, and a run of rays in the middle that go straight past both.
    '
    ' THE CENTRE IS AIMED AT AS A PLACE, not as a bearing. A bearing is only
    ' true from where it was measured, and a hull lining up on a gateway is
    ' moving the whole time. The world point between the two jambs stays put.
    Private doorAt As Vector2
    Private doorW As Single = 0.0F
    Private doorFor As Single = 0.0F

    ''' <summary>
    ''' HOW MUCH OF THE REAR ARC GETS A VOTE ON REVERSING.
    '''
    ''' Sixty degrees, so thirty either side of dead astern. The rear sweep is
    ''' 120 degrees wide like the front, which puts rays 55 degrees off the
    ''' tail looking SIDEWAYS - and a tank reverses along its own axis, so
    ''' those describe ground it will never back into.
    '''
    ''' The record caught it on the first run that went 120: "REAR TRIGGER 1 -
    ''' rear is better (20.0 m vs 4.5), winning ray -56 deg off the tail." A
    ''' ray at the very edge of the arc saw nothing for its full 20 m, beat the
    ''' 4.5 m ahead, and ordered a reversal about ground that was beside the
    ''' hull rather than behind it. Range went 83 m to 130 m and never came
    ''' back. Same mistake as letting the outer FRONT rays into the surface
    ''' fit, one arc round.
    ''' </summary>
    Private Const REAR_ARC_DEG As Single = 60.0F

    ''' <summary>
    ''' HOW FAR OFF THE NOSE A RAY CAN BE AND STILL BE SOMEWHERE TO DRIVE.
    '''
    ''' The scan went to a full circle so nothing is blind abeam, and that
    ''' immediately broke the side choice - which had always read the OUTERMOST
    ''' forward rays as front(0) and front(last). Those meant 55 degrees while
    ''' front was a 120-degree arc. With front as the whole forward hemisphere
    ''' they mean 88, and a ray pointing almost sideways reads its full 20 m
    ''' across open ground and wins every vote: "early turn left at 12.1 m
    ''' (-88 deg, 20.0 m deep)", 2,493 double taps in seventy seconds.
    '''
    ''' A ray at 88 degrees is not a way forward, it is a description of the
    ''' ground beside the tank. Turning to it is a pivot, not a manoeuvre. So
    ''' SEEING is 360 degrees and STEERING is this cone - which is what the
    ''' code meant all along, written as an array index instead of as a
    ''' bearing, and therefore silently redefined the moment the array changed.
    ''' </summary>
    Private Shared ReadOnly STEER_ARC As Single = MathHelper.DegreesToRadians(60.0F)

    ''' <summary>How much further an open ray must read than the nearest thing
    ''' in the scan before it counts as going PAST it rather than at it.</summary>
    Private Const DOOR_OPEN_M As Single = 4.0F

    ''' <summary>Clearance wanted beyond the hull, over the whole opening. Half
    ''' a metre each side: a gateway taken at a crawl does not need a lane.</summary>
    Private Const DOOR_MARGIN_M As Single = 1.0F

    ''' <summary>Keep driving at a door this long after losing sight of it -
    ''' the jambs leave the arc before the hull is through them.</summary>
    Private Const DOOR_S As Single = 4.0F

    ' ---- the endurance harness -------------------------------------------
    '
    ' "i want to change how I place a target / you do it after it makes it.
    '  random heading 300m distance"
    '
    ' ARRIVING IS NOT THE END OF THE RUN, it is the start of the next one. One
    ' hand-placed look-at tests one route, and every bug tonight came from a
    ' route nobody thought to try - the low spot, the wall the body would not
    ' clear, the gateway that has still never been met. A goal thrown 300 m out
    ' on a random bearing tries routes nobody chose.
    '
    ' SEEDED, so the sequence of goals is the same every run. The seam's rule
    ' is that a brain gets no clock and no unseeded RNG, and it is the right
    ' rule: two runs from identical spawns have to be comparable, or a change
    ' cannot be measured against the run before it.
    Private ReadOnly rng As New Random(20260917)
    Private Const GOAL_SEED As Integer = 20260917
    Private Const NEXT_GOAL_M As Single = 300.0F
    Private goalsMade As Integer = 0

    ' ---- the trap -------------------------------------------------------
    '
    ' "look for cases where all front rays hit something and we trying to turn
    '  and move failed, we need to backup using rear scans. but first we are
    '  going to mark that blk we are on as a 1 in the data so we don't drive
    '  in to it again"
    '
    ' WHAT MAKES IT A TRAP AND NOT A WALL. A wall is every front ray finding
    ' something; that happens constantly and the blocked branch answers it by
    ' turning. A TRAP is every front ray finding something AND the turning
    ' having already failed - the hull asked to move, the world refused, and
    ' the scan offers nowhere better. Both halves are required, or every wall
    ' on the map gets marked and the map fills up with things that were only
    ' ever in the way.
    '
    ' MARKED ON THE WAY OUT, NOT ON THE WAY IN, and that is not tidiness. The
    ' square the hull stands on is inside its own footprint, so setting the bit
    ' while standing there makes Standable refuse every neighbouring
    ' destination too - the mark would wall the tank into the trap it was meant
    ' to escape. The cell is remembered, the reverse runs, and the bit is set
    ' once the hull is clear of it.
    Private trapCol As Integer = -1
    Private trapRow As Integer = -1
    Private trapAt As Vector2



    ' A PROBE THAT DID NOT MOVE FIRST. Entering Turning twice from the same
    ' spot means the first probe taught it nothing - it looked, settled, and
    ' came straight back to look again. That is a loop, not progress, and it
    ' reads on screen as a tank jiggling in place.

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
    ''' <summary>How far the corridor planks reach. Matched to the new
    ''' horizon: a trigger that fires at fifteen metres while the scan sees
    ''' forty is throwing away the warning the scan just bought.</summary>
    Private Const LOOK_AHEAD_M As Single = 30.0F
    Private Const VOTES_NEEDED As Integer = 3
    ' DECIDE FASTER. Three votes a quarter-second apart is 0.75 s to commit,
    ' and at 12 m/s that is nine metres driven while thinking - most of the
    ' room the early turn exists to preserve.
    '
    ' Three samples was the cure for wobble on a twenty-ray scan, where a
    ' single reading genuinely could not be trusted. The sweep is 120 rays now
    ' and every bearing is measured every tick, so the CONFIRMATION is still
    ' worth having and the WAIT is not. Same three samples, 0.3 s.
    Private Const VOTE_EVERY_S As Single = 0.1F
    Private votes As Integer = 0
    Private voteBear As Single = 0.0F
    Private voteScore As Single = 0.0F
    Private voteWidth As Single = 0.0F
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
        If first.hulls IsNot Nothing AndAlso first.hulls.Length > DRIVER Then
            Dim hv = first.hulls(DRIVER)
            ' ON SCREEN, because these two numbers decide whether the hull can
            ' move at all and neither of them was ever printed.
            LogThis("brain: hull {0:0.0} x {1:0.0} m - drive radius {2:0.00} m " &
                    "(needs a {3:0.0} m lane), rotate radius {4:0.00} m",
                    hv.halfX * 2.0F, hv.halfZ * 2.0F, hv.DriveRadius,
                    hv.DriveRadius * 2.0F, hv.FitRadius)
        End If
        BrainReport.Reset()
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
            throw_goal(h)
            Return o
        End If

        ' The bearing to the goal, needed by more than the seeking block -
        ' the turn-back sweep asks how far it can rotate toward it.
        Dim wantGoal = CSng(Math.Atan2(toGoal.X, toGoal.Y))

        Dim hits = BrainRadar.Scan(h.pos, h.headingRad, h.DriveRadius)
        Dim surf = BrainRadar.FitSurface(hits)

        ' WEDGED: we asked to move and the world did not move us.
        If lastThrottle > 0.1F AndAlso Math.Abs(h.speed) < 0.2F Then
            wedgedFor += inp.dt
        Else
            wedgedFor = 0.0F
        End If
        If wedgedFor > 0.8F AndAlso state <> St.Backing Then
            ' IS THIS A TRAP? Every front ray finding something, on top of the
            ' hull having just failed to move, is the case the owner named.
            Dim allShut = True
            For Each q In hits
                If q.front AndAlso Not q.found Then
                    allShut = False
                    Exit For
                End If
            Next
            If allShut AndAlso trapCol < 0 Then
                trapCol = BrainNav.ColOf(h.pos.X)
                trapRow = BrainNav.RowOf(h.pos.Y)
                trapAt = h.pos
                BrainReport.Trap(trapCol, trapRow)
            End If
            LogThis("brain: WEDGED - throttle {0:0.00} but speed {1:0.00} for {2:0.0} s. " &
                    "Backing out.", lastThrottle, h.speed, wedgedFor)
            state = St.Backing
            backFor = BACK_S
            wedgedFor = 0.0F
            holdAt = 0.0F
            Why = "wedged - backing out"
            ' A nudge, not a retreat - see the reverse curve in St.Backing.
            o.throttle(DRIVER) = -creep(BACK_ROOM_M)
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        BrainReport.Heartbeat(inp.dt, state.ToString(), Why, lastThrottle,
                              h.speed, range, surf)

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

        ' THE RAYS THAT ARE CANDIDATE HEADINGS, in angle order like front. The
        ' whole hemisphere is kept for SEEING - the door finder, the surface
        ' fit and the gap hunt all read `front` - but anything that answers
        ' "which way do we go" reads this instead.
        Dim steer As New List(Of BrainRadar.Hit)
        For Each q In front
            If Math.Abs(q.angle) <= STEER_ARC Then steer.Add(q)
        Next
        If steer.Count < 4 Then
            o.why(DRIVER) = "no steerable scan"
            Return o
        End If

        ' Straight ahead: the middle of the front arc.
        Dim mid = front.Count \ 2
        Dim ahead = Math.Min(front(mid).dist, Math.Min(front(mid - 1).dist,
                                                      front(Math.Min(mid + 1, front.Count - 1)).dist))

        ' AND WHAT THE BODY HAS, which is the number anything about DRIVING has
        ' to be decided on. `ahead` is three rays - lines, measured at TRACE_R -
        ' and every state that braked or gated on it drove the hull into things
        ' the rays could see past. Computed once, here, so no state can go back
        ' to asking the cheaper question.
        '
        ' USE IT EVERYWHERE OR NOWHERE. Gating on the body while the branch
        ' below still compared against `ahead` was worse than either: it
        ' entered "blocked" with five real metres of room and then measured
        ' every alternative against a 20 m ray, so nothing could ever beat
        ' straight on and every path fell through to "boxed in". The record
        ' said it plainly - "rays 20.0 m ahead but the BODY has 5.0 m" - while
        ' the hull sat there reversing to move a sensor that was working.
        Dim bodyAhead = body_ahead(h.pos, h.headingRad, h.DriveRadius, ahead)

        ' SCAN MODE OR PLANK MODE, never both - AND SET BEFORE ANY STATE CAN
        ' RETURN.
        '
        ' This was written further down, next to the corridor cast, and was
        ' therefore dead: St.Door returns a hundred lines above it, so the one
        ' assignment to SCANNING could only ever run while the state was NOT
        ' Door. It evaluated (state = St.Door) exclusively in the cases where
        ' that is false, so the flag was permanently False and the mode never
        ' engaged at all.
        '
        ' A flag whose only assignment sits below an early return is not a flag.
        ' Here, every path through the tick passes it.
        BrainRadar.SCANNING = (state = St.Door)

        ' ---- backing out ---------------------------------------------------
        If state = St.Backing Then
            backFor -= inp.dt

            ' THE MARK, once the hull is out of its own footprint. Held until
            ' here so the bit cannot wall the tank into the square it is
            ' reversing out of.
            If trapCol >= 0 AndAlso
               (h.pos - trapAt).Length > h.DriveRadius + BrainNav.CellSize Then
                If BrainNav.MarkBlocked(trapCol, trapRow) Then
                    LogThis("brain: square ({0},{1}) marked blocked - {2:0.0} m clear of it " &
                            "now, {3} learned this run. IN MEMORY ONLY, the .blk on disk is " &
                            "nuTerra's and is not touched.",
                            trapCol, trapRow, (h.pos - trapAt).Length, BrainNav.Learned)
                Else
                    LogThis("brain: square ({0},{1}) was already blocked - the hull was " &
                            "wedged against something the map already knew about",
                            trapCol, trapRow)
                End If
                trapCol = -1
                trapRow = -1
            End If

            ' WHICH WAY BACK, BY THE REAR SCAN. "we need to backup using rear
            ' scans" - so the reverse is steered rather than straight: take the
            ' rear ray whose BODY distance is deepest and swing the tail toward
            ' it. Every ray carries a drive distance since the scan folded the
            ' body test in, so this is the room the hull has and not the room
            ' the line has.
            '
            ' STEERING IS INVERTED IN REVERSE. A tank backing up swings its
            ' TAIL opposite to the way the steer points, so aiming the tail at
            ' a bearing means steering away from it. Backwards, the hull
            ' reverses confidently into the nearest wall.
            Dim bestRear As Single = 0.0F
            Dim rearBear As Single = 0.0F
            For Each q In rear
                Dim reach = If(q.drive > 0.0F, q.drive, q.dist)
                If reach > bestRear Then
                    bestRear = reach
                    rearBear = wrap_pi(q.angle - CSng(Math.PI))
                End If
            Next
            If bestRear > 0.0F AndAlso Math.Abs(rearBear) > ALIGNED Then
                o.steer(DRIVER) = Math.Clamp(-rearBear / FULL_LOCK, -1.0F, 1.0F)
            End If

            ' THE SAME LOGIC GOING BACKWARDS. "if we cant back up using same
            ' logic as going forward."
            '
            ' Reversing was the one move made blind - a fixed throttle for a
            ' fixed time, with nothing asking whether there was anywhere to
            ' reverse INTO. A hull that backs into the wall behind it has not
            ' bought itself any room, and the wedge detector cannot see it
            ' because the wedge detector only watches forward throttle.
            Dim behind = body_ahead(h.pos, wrap_pi(h.headingRad + CSng(Math.PI)),
                                    h.DriveRadius, 8.0F)
            If behind < 2.0F Then
                backFor = 0.0F
                LogThis("brain: nothing behind either - {0:0.0} m of room, stopping the " &
                        "reverse", behind)
            End If

            If backFor > 0.0F Then
                ' THE FORWARD CURVE, BACKWARDS. "using same logic as going
                ' forward" - so literally creep(), on the room the body has
                ' behind it, with the sign flipped.
                '
                ' It used to be a flat -0.6, which at 12 m/s top speed over the
                ' two-second BACK_S is FOURTEEN METRES of reverse. The record
                ' shows what that cost: range to the goal went 83, 86, 94, 97,
                ' 102, 107 ... 130, gaining seven or eight metres of separation
                ' per wedge and never getting them back. A backup is meant to
                ' buy enough room to turn in - three metres or so - not to
                ' retreat half the map.
                o.throttle(DRIVER) = -creep(behind)
                Why = String.Format("backing toward {0:0} deg off the tail ({1:0.0} m clear)",
                                    MathHelper.RadiansToDegrees(rearBear), bestRear)
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
            Dim deep = steer(0)
            For Each q In steer
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
            BrainReport.Probe(h.pos, "after backing")
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
                If bodyAhead > lastOpen + BETTER_M OrElse bodyAhead > BLOCK_M * 2.0F Then
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
                        ' WHAT THE SCAN SEES **AND** WHAT THE HULL FITS.
                        '
                        ' Ray-only, this loop handed the whole turn straight
                        ' back: "turning to -13 deg" then "turned back 13 deg
                        ' toward the goal and still clear" - which is the goal
                        ' heading, the one that had just wedged it. The rays
                        ' were telling the truth and describing somewhere the
                        ' hull cannot go, so the widen below had to undo the
                        ' sweep every single time, and when it could not, Seek
                        ' drove back into the wall. Fifteen wedges in 55 s.
                        If ray_toward(front, wrap_pi(test - h.headingRad)) >= need AndAlso
                           will_clear(h.pos, test, h.DriveRadius, need) Then
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
                             Not will_clear(h.pos, settled, h.DriveRadius, need)
                        settled = cands(widened)
                        widened += 1
                    Loop

                    If widened > 0 Then
                        If will_clear(h.pos, settled, h.DriveRadius, need) Then
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
                LogThis("brain: turn tested - body had {0:0.0} m, now {1:0.0} m: {2}",
                        lastOpen, bodyAhead, Why)
            End If
            ' IT NO LONGER STOPS TO LOOK, AND THE SWEEP IS WHY.
            '
            ' This pivoted at zero throttle because the scan was two 120-degree
            ' arcs: the hull could not see the heading it was considering until
            ' it pointed at it, so the turn WAS the measurement and taking it on
            ' the move meant confirming from somewhere else. That was true and
            ' it stopped being true when the sweep went to 360 - the rays
            ' already hold every bearing, every tick, including the one being
            ' turned toward.
            '
            ' So the stop bought nothing and cost the entire start-stop gait.
            ' It steers to the heading while driving, braked on what the BODY
            ' has ahead like everything else does.
            o.throttle(DRIVER) = creep(bodyAhead)
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' ---- going through it ------------------------------------------------
        If state = St.Door Then
            Dim d2 = find_door(front, h.pos, h.headingRad, h.DriveRadius)
            If d2.found AndAlso d2.fits Then
                doorAt = d2.at
                doorW = d2.width
                doorFor = DOOR_S
            Else
                ' The jambs leave the arc before the hull is between them, so
                ' losing sight of a door is the NORMAL end of one. Carry on to
                ' the point it was last seen at.
                doorFor -= inp.dt
            End If

            Dim toDoor = doorAt - h.pos
            Dim doorLeft = toDoor.Length
            If doorLeft < 2.0F OrElse doorFor <= 0.0F Then
                state = St.Seek
                LogThis("brain: through the door ({0:0.0} m of it left, {1:0.0} m wide)",
                        doorLeft, doorW)
            Else
                Dim wantD = CSng(Math.Atan2(toDoor.X, toDoor.Y))
                Dim dErr = wrap_pi(wantD - h.headingRad)
                o.steer(DRIVER) = Math.Clamp(dErr / FULL_LOCK, -1.0F, 1.0F)
                If Math.Abs(dErr) > SQUARE_ON Then
                    ' LINE UP WHILE MOVING. This stood still, and the motion
                    ' box caught what that cost: HALF the run commanding zero
                    ' throttle, with eleven doors in seventy seconds. The hull
                    ' spent more time pointing at gaps than going through them.
                    '
                    ' The reason for stopping was real - a gateway entered on a
                    ' diagonal is a gateway the hull's corner catches - but
                    ' stopping was never what fixed that, ARRIVING SQUARE is,
                    ' and a hull that closes the angle while it approaches
                    ' arrives just as square as one that pivoted first. It has
                    ' the whole run-up to do it in.
                    '
                    ' Braked on the room the BODY has, and harder the further
                    ' off the bearing is, so a big correction still slows down
                    ' without stopping: at 45 degrees out this is a third of
                    ' the throttle, at 10 it is nearly all of it.
                    Dim lineRoom = body_ahead(h.pos, h.headingRad, h.DriveRadius,
                                              Math.Min(ahead, doorLeft))
                    Dim square = 1.0F - Math.Min(1.0F, Math.Abs(dErr) / FULL_LOCK)
                    o.throttle(DRIVER) = creep(lineRoom) * Math.Max(0.3F, square)
                    Why = String.Format("lining up on the door ({0:0} deg off, thr {1:0.00})",
                                        MathHelper.RadiansToDegrees(Math.Abs(dErr)),
                                        o.throttle(DRIVER))
                Else
                    Dim room = body_ahead(h.pos, h.headingRad, h.DriveRadius,
                                          Math.Min(ahead, doorLeft + 4.0F))
                    ' At a walk. The margin is half a metre a side and the
                    ' braking curve cannot save a hull that is already in the
                    ' jamb.
                    o.throttle(DRIVER) = Math.Min(creep(room), 0.35F)
                    Why = String.Format("through the door ({0:0.0} m wide, {1:0.0} m to go)",
                                        doorW, doorLeft)
                End If
                o.why(DRIVER) = Why
                lastThrottle = o.throttle(DRIVER)
                Return o
            End If
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
                    ' THE BEAM, and this one deliberately stays on `front`. The
                    ' wall being followed is abeam, and holding a distance off
                    ' a ray 55 degrees forward of it was regulating against the
                    ' nearest thing that could be seen rather than against the
                    ' wall. The full sweep is what makes this honest.
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

        ' ---- RULE ONE: CAN WE CLEAR? ---------------------------------------
        '
        ' "rule one can we clear is first. objects left or right of our tank
        '  only count. shot block of rays the width of our tank, straight out
        '  from corners... if one hits, we trigger scanning. we scan and look
        '  for the widest way out of the hits. we scan, find center of cord and
        '  aim for it. turn and to it." - the owner, 2026-09-17.
        '
        ' A BLOCK OF PARALLEL PLANKS, not a fan. The fan answers what is out
        ' there; this answers whether the box the hull is about to sweep
        ' contains anything, which is the only question that decides whether to
        ' keep the throttle down. A ray down a two-metre gap reporting twenty
        ' clear metres was the fan standing in for this test.
        '
        ' AND IT IS THE TRIGGER, not a running commentary. The gap analysis
        ' used to run every tick inside the seek branch, where straight on
        ' correctly won every time - so it never fired once in a whole run, and
        ' by the time an opening was genuinely better the hull had left that
        ' branch for the blocked one. Something has to HAPPEN before it is
        ' worth asking which way out is widest, and a plank touching a wall is
        ' that something.
        Dim lane As BrainRadar.Lane
        If Not BrainRadar.SCANNING Then
            lane = BrainRadar.Corridor(h.pos, h.headingRad, h.DriveRadius,
                                       LOOK_AHEAD_M)
        End If
        If lane.hit Then
            ' THE WIDEST WAY THE CORRIDOR ACTUALLY CLEARED. WidestGap picked
            ' by chord alone, which is how the hull kept committing to gaps it
            ' could not fit: a chord between jambs at different ranges is a
            ' diagonal past a corner, not a doorway. BrainRadar.WAYS has
            ' already cast the hull's own planks at each candidate, so `fits`
            ' is a test rather than an inference.
            Dim gapW As Single = 0.0F
            Dim gapAt As Vector2 = Vector2.Zero
            For Each wy In BrainRadar.WAYS
                If wy.fits AndAlso wy.chord > gapW Then
                    gapW = wy.chord
                    gapAt = wy.mid
                End If
            Next
            If gapW > 0.0F Then
                doorAt = gapAt
                doorW = gapW
                doorFor = DOOR_S
                state = St.Door
                Why = String.Format("lane blocked at {0:0.0} m ({1} of {2} planks{3}) - " &
                                    "widest way out is {4:0.0} m",
                                    lane.dist, lane.blocked, lane.planks,
                                    If(lane.leftHit AndAlso lane.rightHit, ", both sides",
                                       If(lane.leftHit, ", left", ", right")),
                                    gapW)
                BrainReport.Door(gapW, lane.dist, h.DriveRadius * 2.0F)
                o.why(DRIVER) = Why
                lastThrottle = o.throttle(DRIVER)
                Return o
            End If
        End If

        If bodyAhead > BLOCK_M Then
            ' ---- looking ahead, before it becomes a problem ----------------
            ' NO DISTANCE GATE. "we look for a gap. we turn to the gap NOW!
            ' not after we are so close we cant move."
            '
            ' This only looked for an opening once something was inside 15 m,
            ' which is too late BY ARITHMETIC and not by feel: the hull turns
            ' at 26 degrees a second, so a 40-degree turn takes a second and a
            ' half, and a second and a half at 12 m/s is EIGHTEEN METRES. The
            ' decision was gated on being closer to the obstruction than the
            ' manoeuvre needs to complete, so every turn began already short of
            ' room and the hull arrived at the gap still swinging.
            '
            ' The scan reaches 20 m and holds every bearing, so the opening is
            ' visible long before it matters. Look every tick; the score decides
            ' whether there is anything worth turning for, which is a better
            ' gate than proximity because it is about the ROUTE rather than
            ' about how frightened the hull should be.
            If True Then
                voteAt += inp.dt
                If voteAt >= VOTE_EVERY_S Then
                    voteAt = 0.0F

                    ' AS SOON AS THERE IS DATA THAT WOULD MAKE US TURN. This
                    ' runs while the way ahead is merely NARROWING, not once it
                    ' is shut - which is the difference between choosing an
                    ' opening and being forced into one.
                    Dim ops = find_openings(steer, goalErr, h.DriveRadius * 2.0F)
                    Dim best As Opening
                    Dim haveBest = False
                    For Each g In ops
                        If Not haveBest OrElse g.score > best.score Then
                            best = g
                            haveBest = True
                        End If
                    Next

                    ' STRAIGHT ON IS A CANDIDATE TOO, and it has to win on the
                    ' same number or the hull swerves at every doorway it
                    ' passes. Its progress is what the body has ahead, projected
                    ' on the goal exactly like any other opening's.
                    Dim aheadScore = bodyAhead * CSng(Math.Cos(goalErr))

                    ' ON THE HEARTBEAT. Whether the vote is finding nothing or
                    ' finding something and rejecting it are different bugs
                    ' that look the same from outside the window.
                    BrainReport.AheadScore = aheadScore
                    BrainReport.BestScore = If(haveBest, best.score, -1.0F)
                    BrainReport.BestDeg = If(haveBest,
                        MathHelper.RadiansToDegrees(best.bearing), 0.0F)

                    ' AND IT HAS TO BE A TURN. An opening centred on the nose
                    ' is the way we are already pointing; committing to it
                    ' enters Turning, spends a probe and arrives where it
                    ' started.
                    If haveBest AndAlso Math.Abs(best.bearing) > ALIGNED AndAlso
                       best.score > aheadScore + BETTER_M Then
                        Dim thisLeft = (best.bearing < 0.0F)
                        ' The SAME opening three times, not merely the same
                        ' side: a bearing that drifts across the arc is a
                        ' different decision wearing the same answer.
                        If votes > 0 AndAlso thisLeft = voteLeft AndAlso
                           Math.Abs(wrap_pi(best.bearing - voteBear)) < SQUARE_ON Then
                            votes += 1
                        Else
                            votes = 1
                            voteLeft = thisLeft
                        End If
                        voteBear = best.bearing
                        voteScore = best.score
                        voteWidth = best.width
                    Else
                        votes = 0
                    End If

                    If votes >= VOTES_NEEDED Then
                        ' Three in a row on the SAME opening. Commit while
                        ' there is still room to turn in - and only if the hull
                        ' actually clears it.
                        Dim head2 = wrap_pi(h.headingRad + voteBear)
                        If will_clear(h.pos, head2, h.DriveRadius,
                                      Math.Max(6.0F, h.FitRadius * 1.5F)) Then
                            wantHeading = head2
                            lastOpen = bodyAhead
                            testFor = TEST_S
                            followLeft = voteLeft
                            holdAt = 0.0F
                            lostFor = 0.0F
                            votes = 0
                            peeks = 0
                            state = St.Turning
                            BrainReport.Probe(h.pos, "early turn")
                            Why = String.Format("opening {0} at {1:0} deg - {2:0.0} m wide, " &
                                                "{3:0.0} m of progress against {4:0.0} straight on",
                                                If(voteLeft, "left", "right"),
                                                MathHelper.RadiansToDegrees(voteBear),
                                                voteWidth, voteScore,
                                                bodyAhead * CSng(Math.Cos(goalErr)))
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
                Dim thr = creep(bodyAhead)
                If range < 20.0F Then thr = Math.Min(thr, Math.Max(0.25F, range / 20.0F))
                o.throttle(DRIVER) = thr
                Why = "seeking"
            End If
            o.why(DRIVER) = Why
            lastThrottle = o.throttle(DRIVER)
            Return o
        End If

        ' ---- blocked: IS IT A DOOR? -----------------------------------------
        '
        ' Asked FIRST, because going through beats going round whenever the
        ' hull fits: the side logic below would see both sides equally shut and
        ' pick one at random, which is how it drove round a building it could
        ' have driven through.
        Dim gap = find_door(front, h.pos, h.headingRad, h.DriveRadius)
        If gap.found Then
            If gap.fits Then
                doorAt = gap.at
                doorW = gap.width
                doorFor = DOOR_S
                state = St.Door
                Why = String.Format("door {0:0.0} m wide - going through", gap.width)
                BrainReport.Door(gap.width, ahead, h.DriveRadius * 2.0F + DOOR_MARGIN_M)
                o.why(DRIVER) = Why
                lastThrottle = o.throttle(DRIVER)
                Return o
            Else
                ' TOO NARROW. Say so and fall through to going round - the gap
                ' is real, the hull is just wider than it. This is the line
                ' that tells the two failures apart in the record.
                LogThis("brain: door at {0:0.0} m is only {1:0.0} m wide, hull needs " &
                        "{2:0.0} - going round instead",
                        ahead, gap.width, h.DriveRadius * 2.0F + DOOR_MARGIN_M)
            End If
        End If

        ' ---- blocked: decide by RANGE ---------------------------------------
        '
        ' THE OUTSIDE TWO EACH SIDE. Where an edge shows itself first.
        Dim leftBest = Math.Max(steer(0).dist, steer(1).dist)
        Dim rightBest = Math.Max(steer(steer.Count - 1).dist, steer(steer.Count - 2).dist)
        Dim bestOut = Math.Max(leftBest, rightBest)

        ' A WALL RIGHT ACROSS THE SCANNER. One surface, spanning, and no end
        ' of it is meaningfully longer than the middle - there is nothing to
        ' turn toward.
        Dim wallAcross = surf.valid AndAlso surf.oneSurface AndAlso
                         (bestOut < bodyAhead + BETTER_M)
        ' will_clear below may promote this to True when no side fits.

        If bestOut > bodyAhead + BETTER_M AndAlso Not wallAcross Then
            Dim goLeft = (leftBest >= rightBest)

            ' TEST THE SIDE BEFORE COMMITTING TO IT. The longer ray is only a
            ' candidate; the hull has to fit down it. If the better-looking
            ' side will not clear, take the other one - and if neither will,
            ' fall through to the rear, which is what a wall across does.
            Dim probe = Math.Max(8.0F, h.FitRadius * 2.0F)
            Dim headL = wrap_pi(h.headingRad + steer(0).angle)
            Dim headR = wrap_pi(h.headingRad + steer(steer.Count - 1).angle)
            Dim okL = will_clear(h.pos, headL, h.DriveRadius, probe)
            Dim okR = will_clear(h.pos, headR, h.DriveRadius, probe)
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
            Dim pick = If(goLeft, steer(0), steer(steer.Count - 1))

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
                For k = 0 To steer.Count - 1
                    If steer(k).found Then Exit For
                    edgeMiss = k
                Next
            Else
                For k = steer.Count - 1 To 0 Step -1
                    If steer(k).found Then Exit For
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
                For Each q In steer
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

            lastOpen = bodyAhead
            testFor = TEST_S
            followLeft = goLeft
            holdAt = 0.0F           ' taken from the first side reading
            lostFor = 0.0F
            peeks = 0
            state = St.Turning
            BrainReport.Probe(h.pos, "blocked")
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
        Dim tailCone = MathHelper.DegreesToRadians(REAR_ARC_DEG * 0.5F)
        For Each q In rear
            ' Rear bearings come back near +/-PI; off-the-tail is what a person
            ' can read, and what says whether it is a sideways ray.
            Dim off = wrap_pi(q.angle - CSng(Math.PI))
            If Math.Abs(off) > tailCone Then Continue For
            If q.dist > rearBest Then
                rearBest = q.dist
                rearWin = off
            End If
        Next

        ' AND THE BODY HAS TO HAVE THE ROOM. "if we cant back up using same
        ' logic as going forward" - the rays behind are lines like the rays in
        ' front, and the hull reversing is the same 3.3 m wide box it is going
        ' forward. Asked HERE and not only once the reverse is running, so a
        ' reversal that cannot happen is never entered.
        Dim roomBack = body_ahead(h.pos, wrap_pi(h.headingRad + CSng(Math.PI)),
                                  h.DriveRadius, 8.0F)

        If rearBest > bodyAhead + BETTER_M AndAlso roomBack < 2.0F Then
            LogThis("brain: rear rays say {0:0.0} m but the body has {1:0.0} m behind it - " &
                    "not reversing", rearBest, roomBack)
        ElseIf rearBest > bodyAhead + BETTER_M Then
            state = St.Backing
            backFor = BACK_S
            Why = String.Format("rear is better ({0:0.0} m vs {1:0.0} of body room)",
                                rearBest, bodyAhead)
            BrainReport.RearTrigger(rearWin, Why)
            o.throttle(DRIVER) = -creep(roomBack)
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
        o.throttle(DRIVER) = -creep(roomBack)
        Why = "boxed in - backing up to see more"
        LogThis("brain: boxed in - rays {0:0.0} m ahead but the BODY has {1:0.0} m " &
                "(needs {2:0.00} radius, widest that fits here is {3:0.00}), rear {4:0.0} m",
                ahead, bodyAhead, h.DriveRadius,
                BrainReport.WidestClear(h.pos, h.headingRad, h.DriveRadius), rearBest)
        o.why(DRIVER) = Why
        lastThrottle = o.throttle(DRIVER)
        Return o
    End Function

    ''' <summary>
    ''' EVERY OPENING IN THE SWEEP, SCORED BY WHAT IT IS WORTH.
    '''
    ''' "the logic would dictate we look for the opening gaps and weight them.
    '''  is it more direct to target or not? we need to make this decision as
    '''  soon as we have data that makes us turn"
    '''
    ''' ONE RULE INSTEAD OF THREE. The side choice asked "is left or right
    ''' deeper", the door check asked "is there a bounded gap ahead", and the
    ''' early turn asked "which outer ray is longest" - three answers to one
    ''' question, none of which knew where the goal was. A gap 80 degrees off
    ''' with 20 m behind it beat a gap 10 degrees off with 18 m, every time,
    ''' because nothing in the comparison mentioned the target.
    '''
    ''' THE SCORE IS METRES OF PROGRESS, which is why there are no weights to
    ''' tune. How far the BODY can drive down a gap, times the cosine of the
    ''' angle between that bearing and the goal, is the distance it would
    ''' actually close - the projection of the move onto the direction that
    ''' matters. A gap at 60 degrees keeps half its reach; one at 90 keeps
    ''' none; one behind scores negative and is dropped. Wider and straighter
    ''' win by arithmetic rather than by a constant somebody picked.
    '''
    ''' The reach is the gap's own rays' DRIVE distance, not their range, so a
    ''' gap the rays see through and the hull cannot take scores what it is
    ''' worth: nothing.
    ''' </summary>
    Private Structure Opening
        Public bearing As Single      ' relative to the nose
        Public width As Single        ' metres across, jamb to jamb
        Public reach As Single        ' how far the BODY gets down it
        Public score As Single        ' metres of progress toward the goal
        Public bounded As Boolean     ' a jamb each side - a doorway, not an edge
    End Structure

    ''' <summary>
    ''' Walk the sweep for runs of rays the hull can drive down, and score each.
    '''
    ''' A RUN IS AN OPENING when consecutive rays all clear the body. Bounded
    ''' by returns each side it is a doorway; running off the end of the arc it
    ''' is the way round something, which is just as useful and scores the same
    ''' way. The distinction is kept because a doorway is worth crawling and an
    ''' open edge is worth driving.
    ''' </summary>
    Private Shared Function find_openings(rays As List(Of BrainRadar.Hit),
                                          goalErr As Single,
                                          need As Single) As List(Of Opening)
        Dim outp As New List(Of Opening)
        Dim n = rays.Count
        Dim k = 0
        While k < n
            If rays(k).drive < need Then
                k += 1
                Continue While
            End If
            Dim j = k
            While j < n AndAlso rays(j).drive >= need
                j += 1
            End While

            Dim g As Opening
            Dim a0 = rays(k).angle, a1 = rays(j - 1).angle
            g.bearing = wrap_pi((a0 + a1) * 0.5F)

            ' REACH ALONG THE MIDDLE, AND THE WORST OF THREE - not the best of
            ' the whole run. Taking the deepest ray anywhere in the opening
            ' measured the luckiest line through it, while straight-on is
            ' measured as the WORST of its three centre rays. Best case against
            ' worst case, so any opening containing the nose beat the nose:
            ' "opening right at 0 deg, 20.0 m of progress against 9.0 straight
            ' on" - the same direction, scored twice, differently.
            '
            ' Measured the way `ahead` is measured, the two are comparable and
            ' a turn has to earn itself.
            Dim mid = k
            Dim bestGap = Single.MaxValue
            For q = k To j - 1
                Dim d = Math.Abs(wrap_pi(rays(q).angle - g.bearing))
                If d < bestGap Then bestGap = d : mid = q
            Next
            g.reach = rays(mid).drive
            If mid > k Then g.reach = Math.Min(g.reach, rays(mid - 1).drive)
            If mid < j - 1 Then g.reach = Math.Min(g.reach, rays(mid + 1).drive)
            g.bounded = (k > 0) AndAlso (j < n)
            ' WIDTH FROM THE ARC IT SUBTENDS, at the distance it is seen. Two
            ' rays 3 degrees apart at 10 m are half a metre apart; the same two
            ' at 2 m are a hand's breadth. A gap has to be measured where it
            ' is, not counted in rays.
            ' The ray spacing, from the two constants rather than a field -
            ' a module ReadOnly here reads as zero on the first Scan of a run,
            ' which is the bug that made the radar draw nothing.
            Dim step_rad = MathHelper.DegreesToRadians(BrainRadar.ARC_DEG / BrainRadar.RAYS)
            Dim span = Math.Abs(wrap_pi(a1 - a0)) + step_rad
            ' Measured at the reach we are claiming for it, which is now the
            ' middle's, so the width and the score describe the same line
            ' through the opening rather than two different ones.
            g.width = 2.0F * Math.Min(g.reach, BrainRadar.REACH_M) *
                      CSng(Math.Sin(Math.Min(span, CSng(Math.PI)) * 0.5F))

            ' METRES OF PROGRESS: the reach projected onto the goal direction.
            Dim toward = CSng(Math.Cos(wrap_pi(g.bearing - goalErr)))
            g.score = g.reach * toward
            If g.score > 0.0F Then outp.Add(g)
            k = j
        End While
        Return outp
    End Function

    Private Structure Gap
        Public found As Boolean
        Public fits As Boolean
        Public width As Single      ' metres between the jambs
        Public at As Vector2        ' world point in the middle of the opening
    End Structure

    ''' <summary>
    ''' HITS BOTH SIDES, OPEN IN THE MIDDLE.
    '''
    ''' Walk the front arc for the widest run of rays that go PAST the nearest
    ''' thing in the scan, with a return on both sides of the run. Those two
    ''' returns are the jambs; the distance between where they landed is the
    ''' opening, measured rather than inferred from an angle.
    '''
    ''' THE RUN MUST BE BOUNDED AT BOTH ENDS. An open run that reaches the edge
    ''' of the arc is not a door - it is the scan running out, which is the
    ''' ordinary "go round the end of it" case the side logic already handles.
    ''' Requiring a jamb each side is the whole difference between the two.
    ''' </summary>
    Private Shared Function find_door(front As List(Of BrainRadar.Hit),
                                      pos As Vector2, headingRad As Single,
                                      fitR As Single) As Gap
        Dim g As Gap
        Dim near = BrainRadar.REACH_M
        For Each q In front
            If q.found Then near = Math.Min(near, q.dist)
        Next
        If near >= BrainRadar.REACH_M Then Return g    ' nothing there at all

        Dim n = front.Count
        Dim isOpen(n - 1) As Boolean
        For k = 0 To n - 1
            isOpen(k) = (Not front(k).found) OrElse front(k).dist > near + DOOR_OPEN_M
        Next

        Dim bi0 = -1, bi1 = -1
        Dim k2 = 1
        While k2 < n - 1
            If isOpen(k2) AndAlso Not isOpen(k2 - 1) Then
                Dim j = k2
                While j < n - 1 AndAlso isOpen(j)
                    j += 1
                End While
                ' j is the first closed ray after the run - a jamb, because the
                ' loop stopped before the arc's edge.
                If Not isOpen(j) Then
                    If bi0 < 0 OrElse (j - k2) > (bi1 - bi0) Then
                        bi0 = k2
                        bi1 = j - 1
                    End If
                End If
                k2 = j
            Else
                k2 += 1
            End If
        End While
        If bi0 < 0 Then Return g

        ' THE SAME NINETY DEGREE RULE THE SCOPE DRAWS BY. An opening whose two
        ' jambs are more than a right angle apart is not a doorway to go
        ' through, it is open ground with something on either flank - and its
        ' "centre" is the average of two unrelated walls rather than a place
        ' anyone chose. Going through a gap and crossing open ground are
        ' different manoeuvres; only the first one has a middle worth aiming
        ' at. The second is handled by seeking off the wall, as before.
        '
        ' Ray steps, so it is integer and exact and does not have to reason
        ' about the wrap.
        Dim jambSteps = (bi1 + 1) - (bi0 - 1)
        If jambSteps * (BrainRadar.ARC_DEG / BrainRadar.RAYS) > 90.0F Then Return g

        g.found = True
        Dim jambL = front(bi0 - 1).at
        Dim jambR = front(bi1 + 1).at
        g.width = (jambL - jambR).Length
        ' The middle of the two jambs, pushed a little past the line of them so
        ' the hull aims THROUGH the opening and not at the plane of it.
        Dim mid = (jambL + jambR) * 0.5F
        Dim reach = (mid - pos).Length
        Dim dir = If(reach > 0.01F, (mid - pos) / reach, New Vector2(0.0F, 1.0F))
        g.at = mid + dir * 2.0F

        Dim bear = CSng(Math.Atan2(dir.X, dir.Y))
        g.fits = (g.width >= fitR * 2.0F + DOOR_MARGIN_M) AndAlso
                 will_clear(pos, bear, fitR, reach + 2.0F)
        Return g
    End Function

    ''' <summary>
    ''' THROW THE NEXT GOAL 300 m OUT ON A RANDOM BEARING.
    '''
    ''' IT HAS TO BE SOMEWHERE A TANK COULD STAND. A goal inside a building or
    ''' off the edge of the map is not a hard route, it is an impossible one,
    ''' and the hull would grind at the nearest wall until the run ended with
    ''' nothing learned. So bearings are tried until one lands on standable
    ''' ground, and the distance is walked in if the map is not big enough that
    ''' way - 300 m from a corner of a 1400 m map points off it more often than
    ''' not.
    ''' </summary>
    Private Sub throw_goal(h As BrainHull)
        Dim tries = 0
        For Each want In New Single() {NEXT_GOAL_M, 220.0F, 150.0F, 90.0F}
            For k = 1 To 16
                tries += 1
                Dim a = CSng(rng.NextDouble() * Math.PI * 2.0)
                Dim p = h.pos + New Vector2(CSng(Math.Sin(a)), CSng(Math.Cos(a))) * want
                If BrainNav.Standable(p.X, p.Y, h.DriveRadius) Then
                    BrainGoal.Target = p
                    BrainGoal.HasTarget = True
                    ' PIN IT. Follow rides the camera and would drag the goal
                    ' back under the owner's view on the very next frame.
                    BrainGoal.Follow = False
                    goalsMade += 1
                    state = St.Seek
                    holdAt = 0.0F
                    lostFor = 0.0F
                    votes = 0
                    LogThis("brain: GOAL {0} - arrived, throwing the next one {1:0} m " &
                            "at {2:0} deg ({3} bearing(s) tried). Seed {4}.",
                            goalsMade, want, MathHelper.RadiansToDegrees(a), tries,
                            GOAL_SEED)
                    Return
                End If
            Next
        Next
        LogThis("brain: arrived, but {0} bearings found nowhere standable to go next", tries)
    End Sub

    ''' <summary>
    ''' HOW FAR THE HULL ITSELF CAN GO THIS WAY, up to what the rays saw.
    '''
    ''' will_clear answers yes or no over a fixed distance; this answers HOW
    ''' FAR, which is what a throttle needs. Same rule either way - Standable
    ''' at the fit radius, the rule BrainSim will apply to every step.
    ''' </summary>
    Private Shared Function body_ahead(pos As Vector2, headingRad As Single,
                                       fitR As Single, limit As Single) As Single
        ' HALF A CELL, AND THE FIRST STRIDE IS TESTED.
        '
        ' This walked at 1.0 m and started at t = 1.0, so the metre directly in
        ' front of the hull was never tested at all and any lip narrower than a
        ' metre was stepped clean over. BrainNav.Clear has said why since the
        ' day it was written - "a full cell can jump a one-cell wall
        ' diagonally, which is the classic way a line test misses a fence" -
        ' and this function ignored it and re-made the mistake. The sim moves
        ' at 0.19 m a frame, so it hits everything this jumped.
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Dim stride = Math.Max(0.25F, BrainNav.CellSize * 0.5F)
        Dim t = stride
        Dim last = 0.0F
        While t <= limit
            If Not BrainNav.Standable(pos.X + dx * t, pos.Y + dz * t, fitR) Then
                Return last
            End If
            last = t
            t += stride
        End While
        Return limit
    End Function

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
        ' BrainNav.Clear ALREADY DOES THIS, at half a cell, for the reason its
        ' own comment gives. This walked at 1.0 m instead and hopped exactly
        ' what that stride exists to catch. One rule, one place.
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Return BrainNav.Clear(pos, New Vector2(pos.X + dx * metres,
                                               pos.Y + dz * metres), fitR)
    End Function

    Private Shared Function creep(ahead As Single) As Single
        If ahead >= SLOW_FROM_M Then Return 1.0F
        Dim f = (ahead - BLOCK_M) / (SLOW_FROM_M - BLOCK_M)
        If f < 0.0F Then f = 0.0F
        ' NO CRAWLING. "we need to stop creeping" - the floor was 0.12, which
        ' is 1.4 m/s, and the squared curve sits on that floor for most of the
        ' last ten metres. The hull spent the interesting part of every
        ' approach at walking pace, rescanning ground it had already scanned.
        '
        ' The floor exists so a blocked hull keeps steerage. 0.35 is 4.2 m/s,
        ' which is steerage without the dawdle, and the braking is still the
        ' squared term - it comes off just as hard, it simply stops lower down.
        Return Math.Max(0.35F, f * f)
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

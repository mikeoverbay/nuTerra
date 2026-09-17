Imports OpenTK.Mathematics

''' <summary>
''' THE BOARD DRIVES THE TANK.
'''
''' "we need to make the node control the path" - the owner, 2026-09-17.
'''
''' RangeBrain is an ordered chain of rules in VB; this walks the SAME shape as
''' a graph and steers from it. Nothing here re-decides anything - the board is
''' the decision, and this is the thing that reads it.
'''
''' IT DOES NOT REPLACE RangeBrain, it sits beside it. Both implement IBrain and
''' the panel picks one, so the working brain is still there to be scored
''' against - which is the only way to tell whether the graph drives as well as
''' the code it was drawn from. Deleting the incumbent first would have thrown
''' away the measuring stick.
'''
''' HOW A TICK RUNS. Flow is PUSHED from the Tick node: a Priority tries its
''' outs a, b, c, d in order and stops at the first that acts, a Gate passes
''' flow on only if its `when` is true, and an act node steers and says it
''' acted. Data is PULLED the other way: when a node needs an input it follows
''' the wire backwards and evaluates whatever is on the end. Push for control,
''' pull for values - mixing those two up is how graph evaluators end up
''' recomputing the same scan eight times a frame.
'''
''' A GATE WITH NOTHING ON ITS `when` IS ALWAYS TRUE. That is not a default
''' chosen for convenience: it is what the last rung of an else-if ladder IS,
''' and the board has one - the boxed-in Reverse hanging off the last Priority
''' with no test in front of it.
'''
''' THE DEPTH LIMIT IS NOT PARANOIA. Nothing stops a wire being dragged into a
''' cycle, and a cycle here is a stack overflow that kills the process while the
''' tank is driving. Sixty-four is far past anything the board needs.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Public Class GraphBrain
    Implements IBrain

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            Return "graph"
        End Get
    End Property

    Private Enum GSt
        Seek = 0
        Turning
        Follow
        Door
        Backing
    End Enum

    Private Const ARRIVE_M As Single = 5.0F
    Private Const BLOCK_M As Single = 5.0F
    Private Const BETTER_M As Single = 3.0F
    Private Const REAR_ARC_DEG As Single = 60.0F
    Private Const LOOK_AHEAD_M As Single = 30.0F
    Private Const CREEP As Single = 0.35F
    Private Const MAX_DEPTH As Integer = 64
    Private Const VOTES_NEEDED As Integer = 2

    ' ---- what this tick is working with ----------------------------------
    Private h As BrainHull
    Private goal As Vector2
    Private hits As BrainRadar.Hit()
    Private lane As BrainRadar.Lane
    Private laneDone As Boolean
    Private dt As Single

    Private thr As Single
    Private steerOut As Single
    Private why As String

    Private state As GSt = GSt.Seek
    Private wedgedFor As Single
    Private voteBearing As Single
    Private votes As Integer
    Private depth As Integer

    Private ready As Boolean = False
    Private goalsMade As Integer = 0

    ''' <summary>Its own, so a graph run and a RangeBrain run do not pull from
    ''' the same sequence and quietly get different goals.</summary>
    Private ReadOnly rng As New Random(20260917)

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        ' The board has to exist before it can be walked. EnsureSomething loads
        ' the saved graph or lays out the current AI.
        BrainNodes.EnsureSomething()
        state = GSt.Seek
        wedgedFor = 0.0F
        votes = 0
        LogThis("brain: graph brain started")
    End Sub

    Public Function Tick(inp As BrainInput) As BrainOutput Implements IBrain.Tick
        Dim n = If(inp.hulls Is Nothing, 0, inp.hulls.Length)
        Dim o = BrainOutput.ForHulls(n)
        For i = 0 To n - 1
            o.why(i) = "parked"
        Next
        Dim me_ = BrainRadar.HULL
        If n = 0 OrElse me_ >= n Then Return o

        If Not BrainGoal.HasTarget Then
            o.why(me_) = "no goal"
            Return o
        End If

        ' SELF-STARTING. Switching brain from the panel swaps the instance

        ' without anyone calling Start, so the board is made sure of here too

        ' rather than trusting a lifecycle that has two ways in.

        If Not ready Then

            BrainNodes.EnsureSomething()

            ready = True

        End If



        h = inp.hulls(me_)
        goal = BrainGoal.Target
        dt = Math.Max(0.0001F, inp.dt)
        o.target(me_) = goal

        ' ONE SCAN A TICK, kept here rather than in the Ray Scan node. Several
        ' nodes ask for hits and the pull would run the scan once each - and a
        ' scan is the most expensive thing the brain does.
        hits = BrainRadar.Scan(h.pos, h.headingRad, 0.0F)
        laneDone = False

        ' The wedge watch is a TIMER, not a reading, which is why it is here and
        ' not behind a wire. Asking "is the speed low" on one frame answers a
        ' different question from "has it been low for most of a second".
        ' EITHER DIRECTION. Counting only forward throttle meant a tank
        ' reversing into something was wedged with nothing watching, and
        ' the one state that commands reverse was the one the watch could
        ' not see into.
        If Math.Abs(thr) > 0.1F AndAlso Math.Abs(h.speed) < 0.2F Then
            wedgedFor += dt
        Else
            wedgedFor = 0.0F
        End If

        thr = 0.0F
        steerOut = 0.0F
        why = "the board did nothing"
        depth = 0
        BrainNodes.TraceBegin()

        ' SET EVERY TICK, not latched. RangeBrain's SCANNING is (state = Door)
        ' and is re-evaluated each tick; the graph's Rescan act can raise it on
        ' top for the tick it fires. Left to the act alone it was False until
        ' the first rescan and stuck True forever afterwards - wrong before and
        ' wrong after, which is why the scope showed nothing.
        BrainRadar.SCANNING = (state = GSt.Door)

        Dim tickNode = BrainNodes.FirstOfKind("Tick")
        If tickNode < 0 Then
            why = "no Tick node on the board"
        Else
            Dim first = BrainNodes.Downstream(tickNode, "out")
            If first < 0 Then
                why = "the Tick node goes nowhere"
            ElseIf Not run(first) Then
                why = "every rule on the board declined"
            End If
        End If

        thr = Math.Max(-1.0F, Math.Min(1.0F, thr))
        steerOut = Math.Max(-1.0F, Math.Min(1.0F, steerOut))

        ' THE SAME REPORT RangeBrain FILES, and for the same reason it does.
        ' Everything that watches a run - the heartbeat, the motion box, the
        ' card over the hull - reads BrainReport, not the brain. A brain that
        ' drives without filing this drives invisibly, and two scorecards where
        ' one of them is blank are not a comparison.
        BrainNodes.TraceLogIfChanged()
        BrainReport.Heartbeat(dt, state.ToString(), why, thr, h.speed,
                              (goal - h.pos).Length, BrainRadar.FitSurface(hits))

        o.throttle(me_) = thr
        o.steer(me_) = steerOut
        o.why(me_) = why
        Return o
    End Function

    ' ======================================================================
    '  FLOW, pushed
    ' ======================================================================

    Private Function run(id As Integer) As Boolean
        If id < 0 Then Return False
        BrainNodes.TraceVisit(id)
        depth += 1
        If depth > MAX_DEPTH Then
            why = "the board loops back on itself"
            depth -= 1
            Return True
        End If
        Dim did = False
        Select Case BrainNodes.NodeKind(id)
            Case "Priority"
                For Each p In {"a", "b", "c", "d"}
                    If run(BrainNodes.Downstream(id, p)) Then
                        did = True
                        Exit For
                    End If
                Next
            Case "Sequence"
                For Each p In {"a", "b", "c"}
                    If run(BrainNodes.Downstream(id, p)) Then did = True
                Next
            Case "Gate"
                ' AN UNWIRED `when` DECLINES. It reads backwards - surely a
                ' gate with no condition is open? - and the other way round is
                ' how the board came to be dead: rule 1 was a Gate nobody had
                ' finished wiring, it fired every tick, Stop acted, and because
                ' a Priority stops at the first child that acts it disabled the
                ' fifty-three nodes behind it. A rule still being drawn should
                ' do nothing, not everything.
                '
                ' An unconditional rung does not need a Gate at all: wire the
                ' Priority straight to the act, which is what the boxed-in
                ' Reverse at the bottom of the board does.
                If as_bool(pulled(id, "when"), False) Then
                    did = run(BrainNodes.Downstream(id, "out"))
                End If
            Case Else
                did = act(id)
                If did Then BrainNodes.TraceAct(id)
        End Select
        depth -= 1
        Return did
    End Function

    Private Function act(id As Integer) As Boolean
        Dim k = BrainNodes.NodeKind(id)
        Select Case k
            Case "Stop"
                thr = 0.0F : steerOut = 0.0F
                why = "stopped"
            Case "New Goal"
                throw_goal()
            Case "Rescan"
                ' SCANNING IS A SIDE EFFECT, NOT A DECISION. This raises the
                ' flag and reports that it did NOT act, so the chain carries
                ' on to the rules that actually steer.
                '
                ' As a terminal act it ended the tick every time a corridor
                ' plank touched anything - which on a real map is most of the
                ' time - and the tank sat still scanning with clear road
                ' beside it. RangeBrain does not stop there either: its
                ' lane-blocked rule looks for the widest fitting way out and
                ' only commits if it finds one, otherwise it falls through.
                BrainRadar.SCANNING = True
                Return False
            Case "Mark Trap"
                ' THE CELL AHEAD, NOT THE ONE UNDERNEATH. The sim refuses to
                ' move a hull to an unstandable place (BrainSim: "simply does
                ' not move"), so marking the ground the tank is parked on
                ' freezes it for good - every destination within a drive
                ' radius of that cell then fails, in every direction. It sat
                ' commanding reverse at zero speed until the run was killed.
                '
                ' What deserves the mark is what it could not get INTO.
                Dim ahead_ = h.pos + New Vector2(CSng(Math.Sin(h.headingRad)),
                                                 CSng(Math.Cos(h.headingRad))) * h.DriveRadius
                Dim c = BrainNav.ColOf(ahead_.X), r = BrainNav.RowOf(ahead_.Y)
                If BrainNav.MarkBlocked(c, r) Then
                    why = "marked the block ahead a trap"
                Else
                    why = "already a trap"
                End If
            Case "Set Seek", "Set Turning", "Set Follow", "Set Door", "Set Backing"
                state = CType([Enum].Parse(GetType(GSt), k.Substring(4)), GSt)
                why = "state " & k.Substring(4).ToLowerInvariant()
            Case "Reverse"
                Dim b = as_num(pulled(id, "bearing"), rear_bearing())
                ' THE ERROR FROM STRAIGHT BACK, not from straight ahead. The
                ' rear ray is about 180 degrees off the nose, so steering
                ' toward it directly is full lock every time: the tank
                ' pivoted on the spot, never moved, and wedged itself in a
                ' loop of Set Backing / Reverse that the node trace made
                ' obvious in one line.
                '
                ' Negated because the hull is going backwards - the same
                ' track command swings the nose the other way.
                Dim offTail = wrap_pi(b - CSng(Math.PI))
                steerOut = -turn_to(offTail)
                thr = -CREEP
                why = String.Format("backing, {0:0} deg off the tail",
                                    MathHelper.RadiansToDegrees(offTail))
            Case "Turn To"
                Dim b = as_num(pulled(id, "bearing"), 0.0F)
                steerOut = turn_to(b)
                thr = 0.0F
                why = String.Format("turning to {0:0} deg",
                                    MathHelper.RadiansToDegrees(b))
            Case "Drive Heading"
                Dim b = as_num(pulled(id, "bearing"), goal_bearing())
                Dim t = as_num(pulled(id, "throttle"), 0.0F)
                steerOut = turn_to(b)
                thr = If(t > 0.0F, t, throttle_for(body_ahead()))
                why = String.Format("driving {0:0} deg, {1:0.0} m clear",
                                    MathHelper.RadiansToDegrees(b), body_ahead())
            Case "Drive To Point", "Through Door"
                Dim w = pulled(id, "way")
                If w Is Nothing Then Return False
                Dim way = CType(w, BrainRadar.Way)
                steerOut = turn_to(way.bearing)
                thr = throttle_for(body_ahead())
                why = String.Format("{0} - {1:0.0} m wide at {2:0} deg",
                                    If(k = "Through Door", "through the door", "heading for the gap"),
                                    way.chord, MathHelper.RadiansToDegrees(way.bearing))
            Case "Follow Wall"
                Dim sd = as_num(pulled(id, "side"), 0.0F)
                ' Steer a little AWAY from the wall it is following, not along
                ' it: along means into it the moment the wall bends in.
                steerOut = If(sd < 0.0F, 0.25F, -0.25F)
                thr = throttle_for(body_ahead())
                why = "following the wall"
            Case Else
                ' Not an act - a sense or a test reached through a flow pin by
                ' mistake. Say so once rather than steering on nothing.
                Return False
        End Select
        Return True
    End Function

    ' ======================================================================
    '  DATA, pulled
    ' ======================================================================

    ''' <summary>Follow the wire into this pin and evaluate what is on the far
    ''' end. Nothing when the pin is unwired - every caller has a default,
    ''' because an unwired pin is a design in progress, not an error.</summary>
    Private Function pulled(id As Integer, inName As String) As Object
        Dim src = BrainNodes.UpstreamNode(id, inName)
        If src < 0 Then Return Nothing
        Return value_of(src, BrainNodes.UpstreamPin(id, inName))
    End Function

    Private Function value_of(id As Integer, outName As String) As Object
        depth += 1
        If depth > MAX_DEPTH Then
            depth -= 1
            Return Nothing
        End If
        Dim v As Object = Nothing
        Dim kind = BrainNodes.NodeKind(id)
        Select Case kind
            Case "Goal"
                v = If(outName = "range", CObj((goal - h.pos).Length), CObj(goal_bearing()))
            Case "Ray Scan"
                v = If(outName = "hits", CObj(hits), CObj(ahead()))
            Case "Rear Scan"
                v = If(outName = "bearing", CObj(rear_bearing()), CObj(rear_deepest()))
            Case "Body Ahead"
                v = CObj(body_ahead())
            Case "Speed"
                v = CObj(h.speed)
            Case "Corridor"
                Dim l = corridor()
                Select Case outName
                    Case "clear" : v = CObj(Not l.hit)
                    Case "dist" : v = CObj(l.dist)
                    Case Else : v = CObj(If(l.leftHit, -1.0F, If(l.rightHit, 1.0F, 0.0F)))
                End Select
            Case "Gaps"
                v = CObj(BrainRadar.WAYS)
            Case "Arrived"
                v = CObj(as_num(pulled(id, "range"), (goal - h.pos).Length) <= ARRIVE_M)
            Case "Is Wedged"
                '' NOT WHILE ALREADY BACKING - RangeBrain gates this the same way
                '' (state <> St.Backing) and the reason is structural: the wedge
                '' rule sits ABOVE the state rules in the chain, so a wedge that
                '' stays true while reversing wins the priority every other tick,
                '' Set Backing fires instead of Reverse, and the throttle is only
                '' commanded half the time. The tank never builds speed, so the
                '' wedge never clears. It reverses forever without moving.
                v = CObj(wedgedFor > 0.8F AndAlso state <> GSt.Backing)
            Case "Too Few Rays"
                ' The RULE is "rescan when there are fewer than four returns",
                ' so the test has to be the shortage, not the sufficiency. As
                ' "Enough Rays" it was wired to the rescan gate and inverted
                ' it: the board would have rescanned whenever the scan was
                ' healthy and pressed on when it was blind.
                v = CObj(front_count() < 4)
            Case "No Goal"
                v = CObj(Not BrainGoal.HasTarget)
            Case "Has Way"
                v = CObj(pulled(id, "way") IsNot Nothing)
            Case "Will Clear"
                Dim b = as_num(pulled(id, "bearing"), goal_bearing())
                Dim m = as_num(pulled(id, "metres"), LOOK_AHEAD_M)
                v = CObj(will_clear(b, m))
            Case "Is Clear"
                v = CObj(as_num(pulled(id, "dist"), 0.0F) > BLOCK_M)
            Case "Nearer Than"
                v = CObj(as_num(pulled(id, "metres"), 999.0F) < BLOCK_M)
            Case "Plank Hit"
                Dim c = pulled(id, "clear")
                v = CObj(Not as_bool(c, Not corridor().hit))
            Case "Rear Better"
                v = CObj(rear_deepest() > as_num(pulled(id, "metres"), body_ahead()) + BETTER_M)
            Case "Is Seek" : v = CObj(state = GSt.Seek)
            Case "Is Backing" : v = CObj(state = GSt.Backing)
            Case "Is Turning" : v = CObj(state = GSt.Turning)
            Case "Is Door" : v = CObj(state = GSt.Door)
            Case "Is Follow" : v = CObj(state = GSt.Follow)
            Case "Widest"
                v = widest(False)
            Case "Door Gap"
                v = widest(True)
            Case "Best Progress"
                v = best_progress()
            Case "Deeper Side"
                v = CObj(deeper_side())
            Case "Vote"
                v = vote(pulled(id, "way"))
        End Select
        ' A test's answer is the interesting half of a walk - the reason it
        ' turned where it did. Recorded here rather than at every call site so
        ' no test can be added later and quietly not report.
        If TypeOf v Is Boolean Then BrainNodes.TraceTest(id, CBool(v))
        depth -= 1
        Return v
    End Function

    ' ---- the measurements -------------------------------------------------

    Private Function goal_bearing() As Single
        Dim d = goal - h.pos
        Return wrap_pi(CSng(Math.Atan2(d.X, d.Y)) - h.headingRad)
    End Function

    Private Function corridor() As BrainRadar.Lane
        If Not laneDone Then
            ' record:=True. This IS the corridor the board steers on, so the
            ' planks drawn on the scope should be the ones it actually used.
            ' False here is what kept the scope blank: Corridor only fills
            ' LANE_OFF/LEN/HIT and raises LANE_ACTIVE when it is recording,
            ' and the scope will not draw returns unless one of those or
            ' SCANNING is up.
            lane = BrainRadar.Corridor(h.pos, h.headingRad, h.halfX + 0.3F,
                                       LOOK_AHEAD_M, True)
            laneDone = True
        End If
        Return lane
    End Function

    Private Function body_ahead() As Single
        Return corridor().dist
    End Function

    Private Function ahead() As Single
        Dim best = BrainRadar.REACH_M
        If hits Is Nothing Then Return best
        Dim near = Single.MaxValue
        For Each q In hits
            If Not q.found Then Continue For
            If Math.Abs(q.angle) > 0.2F Then Continue For
            If q.dist < near Then near = q.dist
        Next
        Return If(near = Single.MaxValue, best, near)
    End Function

    Private Function front_count() As Integer
        If hits Is Nothing Then Return 0
        Dim c = 0
        For Each q In hits
            '' EVERY forward ray, found or not. This counts whether the SCAN
            '' happened, not whether it hit anything - RangeBrain builds the same
            '' list with If q.front Then, and nothing else. Requiring q.found as
            '' well meant open ground read as a failed scan, and the board sat
            '' rescanning forever with clear road in front of it.
            If q.front Then c += 1
        Next
        Return c
    End Function

    ''' <summary>The deepest ray in a cone off the TAIL. Gated to 60 degrees
    ''' because a ray 56 degrees off the tail once voted for reversing and the
    ''' tank backed into what it had just driven round.</summary>
    Private Function rear_deepest() As Single
        Dim best = 0.0F
        If hits Is Nothing Then Return best
        Dim half = MathHelper.DegreesToRadians(REAR_ARC_DEG * 0.5F)
        For Each q In hits
            If Math.Abs(wrap_pi(q.angle - CSng(Math.PI))) > half Then Continue For
            If q.dist > best Then best = q.dist
        Next
        Return best
    End Function

    Private Function rear_bearing() As Single
        Dim best = 0.0F, at = CSng(Math.PI)
        If hits Is Nothing Then Return at
        Dim half = MathHelper.DegreesToRadians(REAR_ARC_DEG * 0.5F)
        For Each q In hits
            If Math.Abs(wrap_pi(q.angle - CSng(Math.PI))) > half Then Continue For
            If q.dist > best Then
                best = q.dist
                at = q.angle
            End If
        Next
        Return at
    End Function

    Private Function deeper_side() As Single
        Dim best = -1.0F, at = 0.0F
        If hits Is Nothing Then Return at
        For Each q In hits
            If Not q.front Then Continue For
            If q.dist > best Then
                best = q.dist
                at = q.angle
            End If
        Next
        Return at
    End Function

    ''' <summary>The widest way, optionally only ones the hull actually fits
    ''' through. Nothing when there is none, so the Gate above it declines.</summary>
    Private Function widest(mustFit As Boolean) As Object
        Dim best As BrainRadar.Way = Nothing
        Dim got = False
        For Each w In BrainRadar.WAYS
            If mustFit AndAlso Not w.fits Then Continue For
            If Not got OrElse w.chord > best.chord Then
                best = w
                got = True
            End If
        Next
        If Not got Then Return Nothing
        Return CObj(best)
    End Function

    ''' <summary>
    ''' The way that buys the most PROGRESS, not the most width.
    '''
    ''' reach times the cosine of how far its bearing is off the goal - metres
    ''' actually gained toward where we are going. This is the node the drawing
    ''' showed dangling: RangeBrain has this maths and never reaches it, and
    ''' wiring it here is the first time it has ever been able to run.
    ''' </summary>
    Private Function best_progress() As Object
        Dim best As BrainRadar.Way = Nothing
        Dim bestScore = -Single.MaxValue
        Dim got = False
        Dim want = goal_bearing()
        For Each w In BrainRadar.WAYS
            If Not w.fits Then Continue For
            Dim score = w.chord * CSng(Math.Cos(wrap_pi(w.bearing - want)))
            If score > bestScore Then
                bestScore = score
                best = w
                got = True
            End If
        Next
        If Not got Then Return Nothing
        Return CObj(best)
    End Function

    ''' <summary>
    ''' Hold a heading steady before committing to it.
    '''
    ''' Passes a way through only once it has picked much the same bearing on
    ''' consecutive ticks. One scan can put a gap where a fencepost briefly was
    ''' not, and a tank that turns on a single frame's opinion spends the run
    ''' wagging between two of them.
    ''' </summary>
    Private Function vote(w As Object) As Object
        If w Is Nothing Then
            votes = 0
            Return Nothing
        End If
        Dim way = CType(w, BrainRadar.Way)
        If Math.Abs(wrap_pi(way.bearing - voteBearing)) < 0.26F Then
            votes += 1
        Else
            votes = 1
            voteBearing = way.bearing
        End If
        If votes < VOTES_NEEDED Then Return Nothing
        Return CObj(way)
    End Function

    Private Function will_clear(bearing As Single, metres As Single) As Boolean
        Dim head = h.headingRad + bearing
        Dim far = h.pos + New Vector2(CSng(Math.Sin(head)) * metres,
                                      CSng(Math.Cos(head)) * metres)
        Return BrainNav.Clear(h.pos, far, h.DriveRadius)
    End Function

    ' ---- steering ---------------------------------------------------------

    ''' <summary>Steer toward a bearing off the nose. Proportional and clamped:
    ''' the sharp-turn complaint earlier in the project was a gain of 1.0 on
    ''' this, which spins on the spot at the smallest error.</summary>
    Private Function turn_to(bearing As Single) As Single
        Return Math.Max(-1.0F, Math.Min(1.0F, wrap_pi(bearing) * 0.6F))
    End Function

    ''' <summary>Throttle from room. Full when there is room, easing off as the
    ''' wall comes up, never below a creep - a brain that stops entirely stops
    ''' gathering the scans that would get it out.</summary>
    Private Function throttle_for(clearM As Single) As Single
        If clearM >= 25.0F Then Return 1.0F
        Return Math.Max(CREEP, Math.Min(1.0F, clearM / 25.0F))
    End Function

    ' ---- coercion ---------------------------------------------------------

    Private Function as_num(v As Object, fallback As Single) As Single
        If v Is Nothing Then Return fallback
        If TypeOf v Is Single Then Return CSng(v)
        If TypeOf v Is Boolean Then Return If(CBool(v), 1.0F, 0.0F)
        Return fallback
    End Function

    Private Function as_bool(v As Object, fallback As Boolean) As Boolean
        If v Is Nothing Then Return fallback
        If TypeOf v Is Boolean Then Return CBool(v)
        If TypeOf v Is Single Then Return CSng(v) <> 0.0F
        Return fallback
    End Function

    ''' <summary>
    ''' Somewhere new to go, and somewhere the hull could actually stand.
    '''
    ''' NOT the camera's look-at, which is what this used to do through
    ''' PlaceAtLookAt: with the chase cam on, the camera looks at the tank, so
    ''' arriving threw the next goal onto the tank and it arrived again. Range
    ''' 0.0, every tick, forever.
    '''
    ''' Far first, then closer. A long goal is a better test of the brain, but
    ''' on a map with water and cliffs there may be nowhere standable that far
    ''' out - so it falls back rather than giving up and parking.
    ''' </summary>
    Private Sub throw_goal()
        Dim tries = 0
        For Each want In New Single() {300.0F, 220.0F, 150.0F, 90.0F}
            For k = 1 To 16
                tries += 1
                Dim a = CSng(rng.NextDouble() * Math.PI * 2.0)
                Dim p = h.pos + New Vector2(CSng(Math.Sin(a)), CSng(Math.Cos(a))) * want
                If BrainNav.Standable(p.X, p.Y, h.DriveRadius) Then
                    BrainGoal.Target = p
                    BrainGoal.HasTarget = True
                    ' PINNED. Follow rides the camera and would drag the goal
                    ' back under the view on the very next frame.
                    BrainGoal.Follow = False
                    goalsMade += 1
                    state = GSt.Seek
                    votes = 0
                    why = String.Format("arrived - goal {0}, {1:0} m out", goalsMade, want)
                    LogThis("brain: GOAL {0} - {1:0} m at {2:0} deg after {3} tries",
                            goalsMade, want, MathHelper.RadiansToDegrees(a), tries)
                    Return
                End If
            Next
        Next
        why = "arrived, but nowhere standable to go next"
        LogThis("brain: arrived, but {0} bearings found nowhere standable", tries)
    End Sub

    Private Shared Function wrap_pi(a As Single) As Single
        Dim x = a
        While x > Math.PI
            x -= CSng(Math.PI * 2.0)
        End While
        While x < -Math.PI
            x += CSng(Math.PI * 2.0)
        End While
        Return x
    End Function

End Class

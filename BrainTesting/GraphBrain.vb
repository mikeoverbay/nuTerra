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
    ''' <summary>
    ''' Metres a chosen side is owed before the other may be reconsidered.
    '''
    ''' DEFAULTS TO NOTHING. "this should be one scan per move" - the owner,
    ''' and he is right: the commitment was mine, added to stop the sides
    ''' swapping every tick, and it treats a stale decision as better than a
    ''' fresh one. The scan is the truth; holding a bearing across twelve
    ''' metres of new scans means driving on what the world looked like
    ''' twelve metres ago.
    '''
    ''' The swapping it was there to stop is a real problem, but the answer
    ''' to it is that the choice should not be marginal in the first place -
    ''' pick the open direction nearest the goal and it is stable because
    ''' the goal is stable. Kept as a knob so the question can be measured
    ''' rather than argued.
    ''' </summary>
    Private Const COMMIT_M As Single = 12.0F

    Private Const BLOCK_M As Single = 5.0F
    Private Const BETTER_M As Single = 3.0F
    Private Const REAR_ARC_DEG As Single = 60.0F
    Private Const LOOK_AHEAD_M As Single = 30.0F
    Private Const CREEP As Single = 0.35F
    Private Const MAX_DEPTH As Integer = 64
    Private Const VOTES_NEEDED As Integer = 2

    ''' <summary>Radians a second the hull swings at full steer. Mirrors
    ''' BrainSim's TURN_RATE, which is Private there.</summary>
    Private Const SIM_TURN_RATE As Single = 0.4538F       ' 26 deg/s

    ' ---- what this tick is working with ----------------------------------
    Private h As BrainHull
    Private goal As Vector2
    Private hits As BrainRadar.Hit()
    Private lane As BrainRadar.Lane
    Private laneDone As Boolean
    Private roundDone As Boolean
    Private roundVal As Object = Nothing
    ''' <summary>-1 left, +1 right, 0 free. Which way round we said we
    ''' were going, and how much road we still owe that promise.</summary>
    ''' <summary>Seconds left before another reversal may begin. A
    ''' minimum dwell OUT of the state, to go with the minimum dwell in
    ''' it - without both, leaving is just the first half of
    ''' re-entering.</summary>
    Private backCool As Single = 0.0F
    Private wasBacking As Boolean = False

    ''' <summary>Did a detour win the last tick. Reset each tick by the
    ''' rung that takes the wheel, so it means "last decision", not
    ''' "ever".</summary>
    Private wasAround As Boolean = False

    ''' <summary>The move already chosen: a WORLD heading and how much
    ''' road it still has. Kept in world terms because a plan stored off
    ''' the nose runs away from you as you turn toward it.</summary>
    Private planHeading As Single = 0.0F
    Private planLeft As Single = 0.0F
    Private planOn As Boolean = False
    Private planAt As Single = 0.0F
    ''' <summary>Sweeps taken this run, against ticks - the saving, measured
    ''' rather than assumed.</summary>
    Public scans As Integer = 0
    Public walkRan As Integer = 0

    Private roundSide As Integer = 0
    Private roundHold As Single = 0.0F

    ' ---- the view from up the road --------------------------------
    ''' <summary>The two sweeps merged, in the hull's frame.</summary>
    Private aheadHits As BrainRadar.Hit()
    ''' <summary>Doors found on the merged cloud, forward only.</summary>
    Private aheadDoors As New List(Of BrainRadar.Way)
    Private aheadDone As Boolean
    ''' <summary>What the two viewpoints agree and disagree about.</summary>
    Private aheadPx As BrainRadar.Parallax
    ''' <summary>The distance actually used this tick.</summary>
    Private aheadL As Single
    ''' <summary>The best opening the hull could actually reach, and
    ''' where. Zero and False when there is none - which is a real
    ''' answer, not a missing one.</summary>
    Private aheadGain As Single
    Private aheadBear As Single
    Private aheadReach As Boolean
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

    ''' <summary>What we asked for LAST tick. This tick's throttle is
    ''' cleared before any node runs, so a node asking whether we are stuck
    ''' has to look at what was commanded, not at what is commanded.</summary>
    Private lastThr As Single = 0.0F

    ''' <summary>Seconds spent in Backing. Reset whenever the state is not
    ''' Backing, so it measures THIS attempt rather than the sum of
    ''' every attempt in the run.</summary>
    Private backingFor As Single = 0.0F

    ''' <summary>Seconds of asking to move and not moving. A clock rather
    ''' than an instant, for the same reason the wedge watch is one: a
    ''' single frame of scuffing a wall should not throw away the heading
    ''' we are committed to.</summary>
    Private stuckFor As Single = 0.0F

    ' ---- the way we are committed to, and how it is going ---------
    Private heldWay As BrainRadar.Way
    Private holding As Boolean = False
    Private heldFrom As Vector2
    Private heldFor As Single = 0.0F
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

    ''' <summary>
    ''' Back to how it started, without rebuilding it.
    '''
    ''' Everything here is something the brain BELIEVES rather than something
    ''' it can see: which state it is in, how long it has been backing, which
    ''' gap it committed to. Put the tank back at the start without clearing
    ''' these and the next run begins mid-thought - reversing out of something
    ''' that is no longer there, holding a way that is now behind it.
    '''
    ''' What is NOT cleared: the goal, which Reset deals with, and the board,
    ''' which is the design and has nothing to do with a run.
    ''' </summary>
    Public Sub ResetState()
        state = GSt.Seek
        wedgedFor = 0.0F
        stuckFor = 0.0F
        backingFor = 0.0F
        heldFor = 0.0F
        holding = False
        votes = 0
        lastThr = 0.0F
        thr = 0.0F
        steerOut = 0.0F
        why = "reset"
        BrainNodes.TraceBegin()
        LogThis("brain: graph brain state cleared")
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
        ' HOW FAR THE RAYS GO, from the Ray Scan node. Set before the scan
        ' because REACH_M is read inside it - and it is one value for the whole
        ' app, so a second Ray Scan node on a board would be the last one to
        ' set it winning, which is at least predictable.
        Dim sn2 = BrainNodes.FirstOfKind("Ray Scan")
        If sn2 >= 0 Then BrainRadar.REACH_M = BrainNodes.Setting(sn2, "reach", 40.0F)
        ' ---- DID THE WORLD ACCEPT THE LAST MOVE --------------------------
        '
        ' A refused move proves the plan is undriveable, whatever Corridor
        ' thinks of it - so drop the plan and scan again from here rather
        ' than spending the remaining metres pushing at it.
        If BrainSim.Refused Then
            planOn = False
            BrainSim.Refused = False
        End If

        ' ONE SCAN PER DECISION, not one per tick.
        '
        ' "its also scanning every turn move. we decided we didn't need to do
        '  that because we already found the solution" - so while a plan is
        ' being executed the sweep is skipped entirely and the last one stands.
        ' The plan's own validity is checked with Corridor, which walks cells
        ' directly and needs no sweep, so dropping a dead plan still works.
        '
        ' Scan, decide, turn and move, scan again. The sweep is the most
        ' expensive thing the brain does and it was being paid for on every
        ' tick of a move that had already been decided.
        If Not planOn OrElse hits Is Nothing Then
            hits = BrainRadar.Scan(h.pos, h.headingRad, h.DriveRadius)
            scans += 1
        End If
        laneDone = False
        aheadDone = False
        roundDone = False

        ' THE COOLDOWN CLOCK. Started the moment a reversal ends, so the
        ' next one cannot begin from the same spot on the same reading.
        Dim backingNow = (state = GSt.Backing)
        If wasBacking AndAlso Not backingNow Then
            backCool = BrainTune.Get_("backcool", 0.0F)
        End If
        wasBacking = backingNow
        If backCool > 0.0F Then backCool -= dt
        door_census()

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

        ' The clock on this backing attempt, kept here because a timer is not
        ' something a wire can carry.
        If state = GSt.Backing Then
            backingFor += dt
        Else
            backingFor = 0.0F
        End If

        If Math.Abs(thr) > 0.1F AndAlso Math.Abs(h.speed) < 0.2F Then
            stuckFor += dt
        Else
            stuckFor = 0.0F
        End If

        lastThr = thr
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
                    Dim nxt = BrainNodes.Downstream(id, p)
                    If run(nxt) Then
                        BrainNodes.LitFlow(id, p, nxt)
                        did = True
                        Exit For
                    End If
                Next
            Case "Sequence"
                For Each p In {"a", "b", "c"}
                    Dim nxt = BrainNodes.Downstream(id, p)
                    If run(nxt) Then
                        BrainNodes.LitFlow(id, p, nxt)
                        did = True
                    End If
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
                If as_bool(pulled(id, "True"), False) Then
                    Dim nxt = BrainNodes.Downstream(id, "out")
                    did = run(nxt)
                    If did Then BrainNodes.LitFlow(id, "out", nxt)
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
            Case "Scanning"
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
                why = String.Format("backing {1:0.00}s, {0:0} deg off the tail",
                                    MathHelper.RadiansToDegrees(offTail), backingFor)
            Case "Turn To"
                Dim b = as_num(pulled(id, "bearing"), 0.0F)
                steerOut = turn_to(b)
                thr = 0.0F
                why = String.Format("turning to {0:0} deg",
                                    MathHelper.RadiansToDegrees(b))
            Case "Drive Heading"
                Dim b = as_num(pulled(id, "bearing"), goal_bearing())
                Dim t = as_num(pulled(id, "throttle"), 0.0F)
                Dim room = body_ahead()
                ' REFUSED SEVERAL TIMES RUNNING: stop asking. Forward is not
                ' available here whatever the clearance says, so pivot toward
                ' the bearing instead - the one command the sim never refuses
                ' - and let the next scan find a way out from a new heading.
                If BrainSim.RefusedRun > 3 Then
                    steerOut = If(Math.Abs(wrap_pi(b)) < 0.05F, 1.0F,
                                  CSng(Math.Sign(wrap_pi(b))))
                    thr = 0.0F
                    why = String.Format("pivoting - forward refused {0}x",
                                        BrainSim.RefusedRun)
                    Return True
                End If
                ' Throttle and steer from the SAME arithmetic - the rate the
                ' clearance demands. Steering with one rule and throttling
                ' with another is how the hull ended up creeping forward at a
                ' quarter lock into something a metre away.
                thr = If(t > 0.0F, t, throttle_for_turn(b, room))
                steerOut = steer_to_clear(b, room, thr)
                why = String.Format("driving {0:0} deg, {1:0.0} m clear, thr {2:0.00}",
                                    MathHelper.RadiansToDegrees(b), room, thr)
            Case "Through Door"
                Dim w = pulled(id, "door")
                If w Is Nothing Then Return False
                Dim way = CType(w, BrainRadar.Way)
                steerOut = turn_to(way.bearing)
                thr = throttle_for(body_ahead())
                ' THE MISSING WORD. This read "{0} - {1:0.0} m wide at {2:0} deg"
                ' with two arguments, so String.Format threw on the very first
                ' tick a door was taken - which stopped the sim at frame 0 and
                ' made the graph brain look like it simply did not drive.
                ' `fits` is what {0} was always for: a door the hull clears and
                ' one it is squeezing through are not the same decision.
                why = String.Format("{0} - {1:0.0} m wide at {2:0} deg",
                                    If(way.fits, "door", "tight door"),
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
        ' Every value the walk actually asked for. Without this the data half
        ' of a decision leaves no mark at all - a pull does not touch the flow.
        BrainNodes.LitData(id, inName)
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
                    Case "metres" : v = CObj(l.dist)
                    Case Else : v = CObj(If(l.leftHit, -1.0F, If(l.rightHit, 1.0F, 0.0F)))
                End Select
            Case "Doors"
                v = CObj(BrainRadar.WAYS)
            Case "Look Ahead"
                look_ahead(BrainNodes.Setting(id, "metres", 0.0F))
                Select Case outName
                    Case "doors" : v = CObj(aheadDoors)
                    Case "gain" : v = CObj(aheadGain)
                    Case "confidence" : v = CObj(aheadPx.agree)
                    Case Else : v = CObj(aheadHits)
                End Select
            Case "Way Out"
                ' SAME SAMPLE. look_ahead caches for the tick, so a board
                ' with both nodes on it pays for one extra sweep, not two -
                ' and both read the same instant, which matters more.
                look_ahead(BrainNodes.Setting(id, "metres", 0.0F))
                Select Case outName
                    Case "gain" : v = CObj(aheadGain)
                    Case "confidence" : v = CObj(aheadPx.agree)
                    Case Else
                        ' NOTHING, not zero, when there is no way out.
                        ' Zero is a bearing - straight ahead - and handing
                        ' that to a steer is a lie that drives. An empty pin
                        ' is what a Gate declines on, so the rung below gets
                        ' its turn instead.
                        If aheadReach AndAlso
                           aheadGain >= BrainNodes.Setting(id, "min gain", 6.0F) Then
                            v = CObj(aheadBear)
                        End If
                End Select
            Case "Arrived"
                v = CObj(as_num(pulled(id, "range"), (goal - h.pos).Length) <=
                         BrainNodes.Setting(id, "metres", ARRIVE_M))
            Case "Backed Enough"
                ' Long enough. RangeBrain backs for a set time and then looks
                ' again rather than waiting for the world to open up, and a
                ' state whose exit needs the world to improve is one that can be
                ' entered and never left.
                ' 1.2 s of creep is about a metre of reversing, which changes
                ' nothing about the view - so the rung that put us here is
                ' true again immediately and the loop closes.
                v = CObj(backingFor > BrainTune.Get_("backsecs",
                             BrainNodes.Setting(id, "seconds", 1.2F)))
            Case "Not Moving"
                ' ASKED FOR MOVEMENT AND DID NOT GET IT. Not simply "the speed is
                ' low" - the branch this gates is Turn To, which commands zero
                ' throttle, so a bare speed test was true BECAUSE of what it had
                ' just caused. The tank span on the spot for the rest of every
                ' run, held there by its own answer.
                '
                ' Last tick's throttle, because this tick's is cleared before any
                ' node is evaluated.
                v = CObj(stuckFor > 0.25F)
            Case "Is Wedged"
                '' NOT WHILE ALREADY BACKING - RangeBrain gates this the same way
                '' (state <> St.Backing) and the reason is structural: the wedge
                '' rule sits ABOVE the state rules in the chain, so a wedge that
                '' stays true while reversing wins the priority every other tick,
                '' Set Backing fires instead of Reverse, and the throttle is only
                '' commanded half the time. The tank never builds speed, so the
                '' wedge never clears. It reverses forever without moving.
                v = CObj(wedgedFor > 0.8F AndAlso state <> GSt.Backing)
            Case "Hit Count"
                ' Returns, not rays: how many of them FOUND something. The
                ' whole sweep, not just ahead - "three hits on radar" is about
                ' what the radar can see, and it sees all the way round.
                Dim seen = 0
                If hits IsNot Nothing Then
                    For Each q In hits
                        If q.found Then seen += 1
                    Next
                End If
                v = CObj(seen >= CInt(BrainNodes.Setting(id, "count", 3.0F)))
            Case "Too Few Rays"
                ' The RULE is "rescan when there are fewer than four returns",
                ' so the test has to be the shortage, not the sufficiency. As
                ' "Enough Rays" it was wired to the rescan gate and inverted
                ' it: the board would have rescanned whenever the scan was
                ' healthy and pressed on when it was blind.
                v = CObj(front_count() < 4)
            Case "No Goal"
                v = CObj(Not BrainGoal.HasTarget)
            Case "Has Door"
                v = CObj(pulled(id, "door") IsNot Nothing)
            Case "Can Pivot To"
                ' Unwired bearing means a half turn - the worst case, and the
                ' answer to "can this thing turn round at all".
                v = CObj(can_pivot(as_num(pulled(id, "bearing"), CSng(Math.PI)),
                                   BrainNodes.Setting(id, "margin", 0.3F)))
            Case "Path Clear"
                ' THE PLANK, not a line. Its own help says "the hull fits along
                ' this bearing for this far" and it was tracing a one-cell line
                ' through the nav grid, which fits where the hull does not.
                ' "open plank has priority" - so the rung that decides whether
                ' to just drive at the goal has to ask the same question the
                ' tank's body will ask a moment later.
                Dim b = as_num(pulled(id, "bearing"), goal_bearing())
                Dim m = as_num(pulled(id, "metres"), LOOK_AHEAD_M)
                ' A DEAD BAND ON THE CHOICE, not a timer on it.
                '
                ' Going round something swings the nose off the goal, which makes
                ' the goal look open, which aims back at the obstacle, which
                ' blocks it again. Two rungs that are each correct, and each
                ' one's action creates the other's condition: 9043 against 7070
                ' decisions in seventy seconds, 15 deg/m, forty metres covered.
                '
                ' So while we are going round, the way to the goal has to be
                ' open by a MARGIN before it wins the tick back - not merely
                ' open. Sensing stays fresh every scan, which is right; the
                ' DECISION gets the hysteresis, which is what was missing.
                If wasAround Then m *= BrainTune.Get_("backon", 1.0F)
                v = CObj(box_clear(b, m))
            Case "Enough Room"
                v = CObj(as_num(pulled(id, "metres"), 0.0F) >
                         BrainTune.Get_("room",
                             BrainNodes.Setting(id, "metres", BLOCK_M)))
            Case "Nearer Than"
                v = CObj(as_num(pulled(id, "metres"), 999.0F) < BLOCK_M)
            Case "Plank Hit"
                Dim c = pulled(id, "clear")
                v = CObj(Not as_bool(c, Not corridor().hit))
            Case "Rear Better"
                ' AND THE FRONT HAS TO BE BLOCKED. Deeper behind than ahead is
                ' true constantly in close country, and on its own it had the
                ' board reversing with clear road in front of it - drive,
                ' reverse, drive, reverse, twice a tick for the whole run.
                ' Backing up is for when forward is not an option.
                ' rear=0 turns reversing off entirely, which is a real
                ' question: it has never once been shown to help.
                ' LAST RESORT, and now it has to prove it. Asking the
                ' go-around first - cached for the tick, so this costs
                ' nothing when the rung above already asked - means Rear
                ' Better can only be true when every turn has been tried
                ' and none of them works.
                If Not roundDone Then
                    roundDone = True
                    roundVal = round_the_end(goal_bearing(), 0.6F)
                End If
                v = CObj(roundVal Is Nothing AndAlso
                         BrainTune.On_("rear", True) AndAlso
                         backCool <= 0.0F AndAlso
                         body_ahead() < BrainTune.Get_("blockat", BLOCK_M) AndAlso
                         rear_deepest() > body_ahead() +
                                          BrainTune.Get_("better", BETTER_M))
            Case "Is Seek" : v = CObj(state = GSt.Seek)
            Case "Is Backing" : v = CObj(state = GSt.Backing)
            Case "Is Turning" : v = CObj(state = GSt.Turning)
            Case "Is Door" : v = CObj(state = GSt.Door)
            Case "Is Follow" : v = CObj(state = GSt.Follow)
            Case "Round The End"
                If Not roundDone Then
                    roundDone = True
                    roundVal = round_the_end(
                        as_num(pulled(id, "bearing"), goal_bearing()),
                        BrainNodes.Setting(id, "margin", 0.6F))
                End If
                ' Remember that we took a detour, so the goal rung has to clear
                ' a higher bar next tick to take the wheel back.
                If outName = "bearing" AndAlso roundVal IsNot Nothing Then wasAround = True
                v = If(outName = "True", CObj(roundVal IsNot Nothing), roundVal)
            Case "Widest Door"
                v = widest()
            Case "Best Door"
                v = best_progress()
            Case "Deepest Ray"
                v = CObj(deeper_side())
            Case "Confirm"
                v = vote(pulled(id, "door"))
            Case "Commit"
                v = commit(pulled(id, "door"),
                           BrainNodes.Setting(id, "patience", 0.6F),
                           BrainNodes.Setting(id, "gained", 1.0F))
            Case "Door Bearing"
                Dim wb = pulled(id, "door")
                If wb IsNot Nothing Then v = CObj(CType(wb, BrainRadar.Way).bearing)
        End Select
        ' A test's answer is the interesting half of a walk - the reason it
        ' turned where it did. Recorded here rather than at every call site so
        ' no test can be added later and quietly not report.
        If TypeOf v Is Boolean Then BrainNodes.TraceTest(id, CBool(v))
        BrainNodes.TraceValue(id, shown(v))
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
            ' FROM THE CORRIDOR NODE'S OWN SETTINGS. Looked up by kind rather
            ' than passed in, because this is cached per tick and several
            ' callers want it - the first one through should not get to decide
            ' how long the planks are.
            Dim cn = BrainNodes.FirstOfKind("Corridor")
            Dim reach = BrainNodes.Setting(cn, "length", LOOK_AHEAD_M)
            Dim margin = BrainNodes.Setting(cn, "margin", 0.3F)
            lane = BrainRadar.Corridor(h.pos, h.headingRad, h.halfX + margin,
                                       reach, True)
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
    ''' <summary>
    ''' The widest opening the hull FITS THROUGH, any direction.
    '''
    ''' No longer optional. This took a mustFit flag and the caller that mattered
    ''' passed False - so the rung whose whole job is "find any gap we fit
    ''' through" was handed the widest gap whether we fit or not, and could
    ''' commit the tank to an opening narrower than itself.
    '''
    ''' A way the hull cannot pass is not a way. `fits` is already measured at
    ''' scan time by casting the hull's own corridor at the opening, so there is
    ''' nothing to weigh up here.
    ''' </summary>
    Private Function widest() As Object
        Dim best As BrainRadar.Way = Nothing
        Dim got = False
        For Each w In BrainRadar.WAYS
            If Not w.fits Then Continue For
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
            ' IN FRONT, ROUGHLY. A way at 104 degrees off the nose is not a way
            ' through, it is a three-point turn - and taking those is what had
            ' the tank driving sideways past openings and coming back.
            If Math.Abs(wrap_pi(w.bearing)) > 1.4F Then Continue For
            Dim score = w.chord * CSng(Math.Cos(wrap_pi(w.bearing - want)))
            ' AND IT HAS TO GAIN GROUND. A negative score is a way that leads
            ' away from the goal; offering the least bad of those is how a
            ' local decision becomes a tour of the map.
            If score <= 0.0F Then Continue For
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

    ''' <summary>
    ''' HOLD A WAY UNTIL IT STOPS WORKING.
    '''
    ''' "plank stops scanning until we find the next path point that gets us
    '''  moving again. if we having not moved forward we still can't get out."
    '''
    ''' So the test of a chosen way is not whether it still looks good - it is
    ''' whether the tank GOT ANYWHERE. While ground is being made this keeps
    ''' handing back the same way, which keeps Has Way true, which is what
    ''' stops the plank branch re-scanning something already decided.
    '''
    ''' It lets go when a second has passed with less than a metre gained. That
    ''' is the owner's condition: not moving forward means we still cannot get
    ''' out, and the way we picked is not the way.
    '''
    ''' A grace period first, because a heavy hull does not accelerate
    ''' instantly and judging a decision on the frame after it was made throws
    ''' away every decision.
    ''' </summary>
    Private Function commit(offered As Object, patience As Single,
                            needGain As Single) As Object
        If holding Then
            ' ONLY WHILE WE ARE ASKING TO MOVE. Turning on the spot gains no
            ' ground by design - since badly misaligned means zero throttle,
            ' every turn was being abandoned before it finished, the bearing
            ' jumped to a new way, and the next turn started over. Turning
            ' toward a way is not failing to reach it.
            If Math.Abs(lastThr) > 0.1F Then heldFor += dt
            Dim gained = (h.pos - heldFrom).Length
            If heldFor < patience OrElse gained > needGain Then
                ' Working, or too early to say. Either way, keep going and do
                ' not look for anything else.
                If gained > needGain Then
                    heldFrom = h.pos
                    heldFor = 0.0F
                End If
                Return CObj(heldWay)
            End If
            ' A second of not getting anywhere. Drop it and let the board look
            ' again - which is what turns the scan back on.
            holding = False
        End If

        If offered Is Nothing Then Return Nothing
        heldWay = CType(offered, BrainRadar.Way)
        holding = True
        heldFrom = h.pos
        heldFor = 0.0F
        Return CObj(heldWay)
    End Function

    ''' <summary>
    ''' Is there room to TURN to face that bearing, from right here.
    '''
    ''' A rectangle turning sweeps its CORNERS, and a corner sits at the
    ''' half-diagonal - 4.0 m on this hull against 1.65 m of half-width. That
    ''' is the whole reason a 3.3 m tank drives down a 5 m corridor and can
    ''' never turn round in it. Nothing about the width tells you; the diagonal
    ''' does.
    '''
    ''' Only the corners' OWN ARCS have to be clear, not the whole circle.
    ''' Turning thirty degrees sweeps four thirty-degree bands. Asking for the
    ''' full circle would refuse every small correction in a corridor the hull
    ''' is perfectly able to make.
    ''' </summary>
    Private Function can_pivot(bearing As Single, margin As Single) As Boolean
        If hits Is Nothing Then Return True
        Dim swing = wrap_pi(bearing)
        If Math.Abs(swing) < 0.02F Then Return True

        ' Where the four corners are now, as angles off the nose.
        Dim a0 = CSng(Math.Atan2(h.halfX, h.halfZ))
        Dim corners = {a0, -a0, CSng(Math.PI) - a0, a0 - CSng(Math.PI)}

        ' How far out a corner reaches, plus whatever margin is asked for.
        Dim need = CSng(Math.Sqrt(h.halfX * h.halfX + h.halfZ * h.halfZ)) + margin

        For Each q In hits
            If Not q.found Then Continue For
            If q.dist >= need Then Continue For
            ' This return is closer than a corner reaches. It only matters if a
            ' corner actually sweeps across it on the way round.
            For Each c In corners
                Dim off = wrap_pi(q.angle - c)
                If swing > 0.0F Then
                    If off >= -0.02F AndAlso off <= swing Then Return False
                Else
                    If off <= 0.02F AndAlso off >= swing Then Return False
                End If
            Next
        Next
        Return True
    End Function

    ''' <summary>
    ''' Scan from a point up the road, and keep only what is genuinely NEW.
    '''
    ''' Three ways a look-ahead lies to you, and they are all the same mistake -
    ''' mistaking a change of viewpoint for a change in the world:
    '''
    ''' OUR OWN WAKE. Rays from ahead sweep the full circle and return off
    ''' everything behind the sample point, which is ground we have just driven
    ''' over. A door built from two of those is a doorway we came through. Only
    ''' the forward half is kept, and only doors further down the heading than
    ''' the hull is now.
    '''
    ''' STANDING NOWHERE. A sample point inside a wall returns something that
    ''' looks exactly like a very small room. It has to be somewhere the hull
    ''' could stand, down a lane that is clear - which is also what makes it
    ''' somewhere we will actually be.
    '''
    ''' THE GLOBAL LIST. BrainRadar.WAYS is one list that Scan clears and
    ''' refills, so this scan would silently replace the near one. The doors are
    ''' copied out and the near scan is run again to put it back.
    ''' </summary>
    Private Sub look_ahead(metres As Single)
        If aheadDone Then Return
        aheadDone = True
        ' The near view is the honest fallback for every early return below:
        ' a board reading `hits` should get the real sweep, not an empty one,
        ' when the second viewpoint turns out to be unreachable.
        aheadHits = hits
        aheadDoors.Clear()
        aheadPx = New BrainRadar.Parallax()
        aheadGain = 0.0F
        aheadBear = 0.0F
        aheadReach = False

        ' ZERO MEANS THE TURN BUDGET. A fixed twenty metres was a round
        ' number chosen before there was a reason; this is the distance the
        ' decision actually has.
        Dim L = If(metres > 0.0F, metres, turn_budget(goal_bearing()))
        aheadL = L

        ' REACHABLE, or we are planning from a place we will never stand.
        Dim ln = corridor()
        If ln.hit AndAlso ln.dist < L Then Return
        Dim fwd = New Vector2(CSng(Math.Sin(h.headingRad)), CSng(Math.Cos(h.headingRad)))
        Dim at = h.pos + fwd * L
        ' And somewhere the hull could stand. A sample point inside a wall
        ' returns something that looks exactly like a very small room.
        If Not BrainNav.Standable(at.X, at.Y, h.DriveRadius) Then Return
        If hits Is Nothing Then Return

        ' record:=False - LAST and WAYS stay the NEAR view's. Everything else
        ' on the board reads those, and a sense node must not move the world
        ' it is sensing. This used to cost a third sweep to undo.
        Dim far = BrainRadar.Scan(at, h.headingRad, h.DriveRadius, False)

        aheadPx = BrainRadar.Compare(hits, far, L)
        aheadBear = pick_out(at, far)

        ' ONE CLOUD, THEN THE ORDINARY DOOR FINDER ON IT. Not a second door
        ' rule - the same one, fed better data. Forward only, and past the
        ' hull, or we find the doorway we came through.
        aheadHits = BrainRadar.Union(hits, far, h.pos, h.headingRad, h.DriveRadius)
        For Each w In BrainRadar.BuildWays(aheadHits, h.pos, h.headingRad, h.DriveRadius)
            If Math.Abs(wrap_pi(w.bearing)) > 1.4F Then Continue For
            If Vector2.Dot(w.mid - h.pos, fwd) <= 0.0F Then Continue For
            aheadDoors.Add(w)
        Next
    End Sub

    ''' <summary>
    ''' HOW FAR WE TRAVEL WHILE MAKING THAT TURN.
    '''
    ''' Twenty-six degrees a second at twelve metres a second means a forty
    ''' degree swing eats about eighteen metres of road. So the moment a
    ''' forty degree turn has to be DECIDED is eighteen metres back - and
    ''' that is exactly where the second sweep should be taken from, because
    ''' it is where we will be standing when the decision falls due.
    '''
    ''' Off the sim's own TURN_RATE rather than a copy, so the plan is made
    ''' against the rate the hull will really get.
    '''
    ''' Floored at a walking pace so a stopped tank still looks somewhere,
    ''' and capped well inside the radar's reach - a sample point beyond
    ''' what we can see is a guess dressed as a measurement.
    ''' </summary>
    Private Function turn_budget(bearing As Single) As Single
        Dim swing = Math.Abs(wrap_pi(bearing))
        Dim v = Math.Max(3.0F, Math.Abs(h.speed))
        Dim m = swing / BrainSim.TURN_RATE * v
        Return Math.Clamp(m, 6.0F, BrainRadar.REACH_M * 0.6F)
    End Function

    ''' <summary>
    ''' The best opening we could ACTUALLY GO TO, or zero if there is none.
    '''
    ''' Compare says where the view opens. It does not say whether the hull
    ''' can get there, and that is deliberate - it is not a radar question.
    ''' Asked from HERE the answer is nearly always no, because the very
    ''' thing that makes a direction an opening is something in the way
    ''' between here and it. Asked from the SAMPLE POINT, which is where the
    ''' turn actually gets made, it is a fair question with a useful answer.
    '''
    ''' Best first, three tries. Past that the gains are small and each try
    ''' is a cell walk.
    ''' </summary>
    Private Function pick_out(at As Vector2, far As BrainRadar.Hit()) As Single
        aheadGain = 0.0F
        aheadReach = False
        If aheadPx.opened Is Nothing OrElse far Is Nothing Then Return 0.0F

        Dim taken As New List(Of Integer)
        For attempt = 1 To 3
            Dim best = -1
            For i = 0 To Math.Min(aheadPx.opened.Length, far.Length) - 1
                If taken.Contains(i) Then Continue For
                If aheadPx.opened(i) <= 0.5F Then Continue For
                If best < 0 OrElse aheadPx.opened(i) > aheadPx.opened(best) Then best = i
            Next
            If best < 0 Then Exit For
            taken.Add(best)

            Dim a = wrap_pi(far(best).angle)
            Dim head = h.headingRad + a
            ' Far enough to prove it is a way through, not so far that a bend
            ' beyond it can veto an opening that is really there.
            Dim d = Math.Min(If(far(best).found, far(best).dist,
                                BrainRadar.REACH_M), 14.0F)
            Dim toward = at + New Vector2(CSng(Math.Sin(head)) * d,
                                          CSng(Math.Cos(head)) * d)
            If BrainNav.Clear(at, toward, h.DriveRadius) Then
                aheadGain = aheadPx.opened(best)
                aheadReach = True
                Return a
            End If
        Next
        Return 0.0F
    End Function

    ''' <summary>
    ''' TEMPORARY: which filter is eating the doors.
    '''
    ''' Has Door reads false on every tick of every run, and three different
    ''' things would produce exactly that: no ways built at all, ways too
    ''' narrow for the hull, or ways the hull cannot reach from where it is
    ''' standing. They are three different problems with three different
    ''' fixes and one symptom, so guessing between them is how an evening
    ''' goes.
    '''
    ''' Ten lines a run, one a second, counts only.
    ''' </summary>
    Private Sub door_census()
        If doorSaid >= 10 Then Return
        If doorClock.IsRunning AndAlso doorClock.Elapsed.TotalSeconds < 1.0 Then Return
        doorClock.Restart()
        doorSaid += 1

        Dim fit = 0, wideAny = 0.0F, wideFit = 0.0F, narrow = 0
        Dim need = h.DriveRadius * 2.0F
        For Each w In BrainRadar.WAYS
            If w.chord > wideAny Then wideAny = w.chord
            If w.chord < need Then narrow += 1
            If w.fits Then
                fit += 1
                If w.chord > wideFit Then wideFit = w.chord
            End If
        Next

        Dim found = 0, edges = 0
        If hits IsNot Nothing Then
            For i = 0 To hits.Length - 1
                If hits(i).found Then
                    found += 1
                    If Not hits(i).linked Then edges += 1
                End If
            Next
        End If

        LogThis("brain: doors - cand {9}, too-far {10}, narrow {11}, " &
                "too-near {12}, no-j {13}, MADE {16} (first i={14} j={15}) | returns {0}, " &
                "edges {1} | ways {2} " &
                "(narrow {3}, fit {4}) | widest any {5:0.0} m, widest fitting " &
                "{6:0.0} m, need {7:0.0} m | body ahead {8:0.0} m",
                found, edges, BrainRadar.WAYS.Count, narrow, fit,
                wideAny, wideFit, need, body_ahead(),
                BrainRadar.WAY_CAND, BrainRadar.WAY_FAR,
                BrainRadar.WAY_NARROW, BrainRadar.WAY_NEAR,
                BrainRadar.WAY_NOJ, BrainRadar.WAY_FIRST_I, BrainRadar.WAY_FIRST_J,
                BrainRadar.WAY_MADE)
    End Sub

    Private doorSaid As Integer = 0
    Private ReadOnly doorClock As New Stopwatch()

    ''' <summary>
    ''' STEER PAST THE EDGE OF WHAT IS IN THE WAY.
    '''
    ''' A door needs two jambs and will not exist until the hull can see
    ''' both sides of one. In the open there is no far side - there is an
    ''' object with edges - so the board sat looking at a scope full of
    ''' returns with nothing it was willing to steer at. Rounding an end
    ''' needs ONE edge and some room past it, and that exists from the
    ''' very first return.
    '''
    ''' IT IS ALLOWED TO BE WRONG. Aim past the edge, drive, see more of
    ''' the thing, aim again. The chain grows as the hull closes and the
    ''' bearing follows it. A decision revised sixty times a second only
    ''' has to be roughly right and stable, which is a much easier thing
    ''' to build than one that has to be correct first time.
    '''
    ''' THE SIDE NEAREST THE GOAL, not the roomier side. Picking room is
    ''' how a hull goes the long way round a hedge it could have rounded
    ''' in ten metres. The other edge is tried only if the first will not
    ''' clear.
    ''' </summary>
    ''' <summary>
    ''' WALK THE CHAINS AND FIND THE WAY PAST - the owner's rule, exactly.
    '''
    ''' A run of consecutive returns IS an object. Where the run stops is
    ''' where the object stops. So: take each end of each chain, aim past it
    ''' by the hull's own half width - the smallest turn that clears it - and
    ''' ask whether the plank is open on the far side. If it is, that is the
    ''' way round. If it is not, try the other end, then the next chain.
    '''
    ''' AND IT FINISHES. When every end of every chain has been tried and
    ''' none of them clears, there is no way past and the board can stop
    ''' looking - "we are dead and there is no point in looking any more".
    ''' My previous version swept bearings every ten degrees, which can find
    ''' a direction but never finds the way PAST anything: it does not know
    ''' what it is going round, so it cannot tell open ground from the one
    ''' gap in a wall, and it can never conclude there is no way through. It
    ''' just returns the least bad bearing, forever.
    '''
    ''' Ordered by cost against the goal, so of two ways past one object it
    ''' takes the one that loses less ground.
    ''' </summary>
    Private Function way_past(goalB As Single) As Object
        Trapped = False
        walkRan += 1
        If hits Is Nothing Then Return Nothing

        ' ---- THE PLAN WE ALREADY HAVE --------------------------------
        '
        ' "we already had the path and only need to turn and make the move
        '  and than scan again". Held in WORLD heading, so turning toward it
        ' does not move it - a plan stored as a bearing off the nose is a
        ' plan that runs away as you turn.
        '
        ' While it holds, the whole walk is skipped: nineteen plank casts a
        ' tick, and a decision re-made at a heading the last one did not see.
        Dim step_ = Math.Abs(h.speed) * dt
        If planOn Then
            planLeft -= step_
            Dim rel = wrap_pi(planHeading - h.headingRad)
            If planLeft > 0.0F AndAlso box_verdict(rel, 6.0F, planAt) <> 2 Then
                Return CObj(rel)
            End If
            ' Spent, or the way shut. Look again from here.
            planOn = False
        End If

        BrainWalkView.Begin()

        Dim need = BrainTune.Get_("outm", 12.0F)
        Dim margin = BrainTune.Get_("pivmargin", 0.3F)
        Dim reach = BrainRadar.REACH_M

        ' The ends of every chain of returns in the forward half. Index order
        ' is angle order - the radar guarantees it and the brain relies on it.
        Dim ends As New List(Of Integer)
        For i = 0 To hits.Length - 1
            If Not hits(i).found Then Continue For
            ' ENDS ALL ROUND, not just ahead. An object beside the hull has
            ' an end too, and that end is exactly what you go round. The
            ' forward filter belongs on the BEARING we might steer, which is
            ' checked below - not on which objects we are allowed to notice.
            ' With it here the walk found ONE end on a scan holding twenty-two
            ' returns.
            Dim before_ = (i - 1 + hits.Length) Mod hits.Length
            Dim after_ = (i + 1) Mod hits.Length
            ' An end is a return whose neighbour on one side found nothing:
            ' that is where the object stops and open ground begins.
            If Not hits(before_).found OrElse Not hits(after_).found Then
                ends.Add(i)
                BrainWalkView.EndPoint(hits(i).at)
            End If
            ' The chain itself: `linked` is the radar's own answer to
            ' whether two neighbouring returns are one object or two.
            If hits(i).linked Then BrainWalkView.Chain(hits(i).at, hits(after_).at)
        Next
        ' ---- AND EVERY GAP BETWEEN HITS, whether the hull fits or not ----
        '
        ' "missing hits we can't fit between" - and it was. The walk only ever
        ' marked the ENDS of objects, so the spaces between them - which is
        ' where a door either is or is not - were invisible. A gap narrower
        ' than the hull is the owner's third case, "no way between hits", and
        ' it is the commonest thing on this map: a scan of twenty-two returns
        ' has twenty-one gaps in it and the picture showed none of them.
        '
        ' Red where the hull will not fit between two returns at all. Green
        ' where it fits AND the plank through the middle is open - a real
        ' door. Amber where it fits but the way through is not clear, which is
        ' a gap you can see through and not drive through.
        Dim fitW = h.DriveRadius * 2.0F
        For i = 0 To hits.Length - 1
            If Not hits(i).found Then Continue For
            Dim j = -1
            For k = 1 To hits.Length - 1
                Dim m2 = (i + k) Mod hits.Length
                If hits(m2).found Then
                    j = m2
                    Exit For
                End If
            Next
            If j < 0 OrElse j = i Then Continue For
            Dim chord = (hits(i).at - hits(j).at).Length
            Dim mid2 = (hits(i).at + hits(j).at) * 0.5F
            If chord < fitW Then
                BrainWalkView.Mark(mid2, 2)
            Else
                ' Wide enough on paper. Is it open for the box?
                Dim to2 = mid2 - h.pos
                Dim r2 = to2.Length
                If r2 > 0.5F Then
                    Dim b2 = wrap_pi(CSng(Math.Atan2(to2.X, to2.Y)) - h.headingRad)
                    Dim at2 As Single = 0.0F
                    BrainWalkView.Mark(mid2, box_verdict(b2, r2 + 2.0F, at2))
                End If
            End If
        Next

        If ends.Count = 0 Then Return Nothing

        ' Aim past each end by the hull's half width at that range, on the
        ' side the object is NOT. Both signs are tried - "we try the other
        ' side of that point".
        Dim bestB = 0.0F, bestOff = Single.MaxValue
        Dim got = False, tried = 0

        ' THE GOAL IS JUST ANOTHER CANDIDATE. "we are always looking for the
        ' green" - so straight at the goal is not a separate rule with its own
        ' rung, it is the candidate that costs nothing, and it wins whenever it
        ' is green because no other bearing can beat an offset of zero.
        '
        ' Two rungs - drive at the goal, and go round - were fighting for the
        ' tick, and each one's action created the other's condition: going
        ' round swings the nose off the goal, which makes the goal look open,
        ' which aims back into the obstacle. 9043 against 7070 decisions in
        ' seventy seconds. One rule cannot argue with itself.
        Dim gAt As Single = 0.0F
        ' OPEN, and with room to move into. A graze passes the verdict but
        ' at one metre there is nothing to carry on through - and being
        ' already aligned with the goal means no steer either, so the hull
        ' sits at thr 0 steer 0 pointed at a wall.
        ' NOT halfZ + margin. Corridor casts from the hull's front CORNERS, so
        ' body_ahead is already the clearance beyond the tank - adding half
        ' the hull length again counted it twice and made the standoff 5.0 m.
        ' With exactly 5.0 m of clear road the throttle computed zero and the
        ' hull sat still: "5.0 m clear, thr 0.00".
        Dim standoff = BrainTune.Get_("standoff", 1.5F)
        If box_verdict(goalB, Math.Min(need, reach), gAt) = 0 AndAlso
           gAt > standoff + 0.5F Then
            bestB = goalB
            bestOff = 0.0F
            got = True
        End If
        For Each i In ends
            Dim d = Math.Max(2.0F, hits(i).dist)
            Dim swing = CSng(Math.Atan2(h.halfX + margin, d))
            For Each sgn In {-1.0F, 1.0F}
                Dim b = wrap_pi(hits(i).angle + sgn * swing)
                If Math.Abs(b) > 1.5F Then Continue For
                tried += 1
                If Not can_pivot(b, margin) Then
                    ' Cannot even swing to face it - the corners foul.
                    BrainWalkView.Mark(hits(i).at, 2)
                    Continue For
                End If

                ' The far side, for the HULL, not a line - and which kind
                ' of answer it gives, marked where the plank actually stops.
                Dim atM As Single = 0.0F
                Dim verdict = box_verdict(b, Math.Min(need, reach), atM)
                Dim hd2 = h.headingRad + b
                Dim where_ = h.pos + New Vector2(CSng(Math.Sin(hd2)) * atM,
                                                CSng(Math.Cos(hd2)) * atM)
                BrainWalkView.Mark(where_, verdict)
                ' A graze is a GO: the hull clears it by carrying on.
                If verdict = 2 Then Continue For
                Dim off = Math.Abs(wrap_pi(b - goalB))
                If off < bestOff Then
                    bestOff = off
                    bestB = b
                    got = True
                End If
            Next
        Next

        If turnSay < 6 Then
            turnSay += 1
            LogThis("brain: chain walk - ends {0}, tried {1}, picked {2:0} deg " &
                    "(goal {3:0} deg){4}", ends.Count, tried,
                    MathHelper.RadiansToDegrees(bestB),
                    MathHelper.RadiansToDegrees(goalB),
                    If(got, "", "  - TRAPPED"))
        End If

        If got Then
            ' Remember it in world terms and run it for a set distance
            ' before asking again.
            planHeading = h.headingRad + bestB
            planLeft = BrainTune.Get_("planm", 10.0F)
            planOn = True

            ' Drawn from the hull out along the bearing it will steer, so a
            ' wrong choice shows as a line pointing at the wrong thing.
            Dim hd = h.headingRad + bestB
            BrainWalkView.Pick(h.pos,
                h.pos + New Vector2(CSng(Math.Sin(hd)) * need,
                                    CSng(Math.Cos(hd)) * need))
        End If

        If Not got Then
            ' Every end of every chain, both sides, and nothing clears. There
            ' is no way round from here and looking again next tick will find
            ' the same thing from the same place.
            Trapped = True
            Return Nothing
        End If
        Return CObj(bestB)
    End Function

    ''' <summary>Every way past has been tried and none of them clears.
    ''' Not a mood - a finished search.</summary>
    Public Trapped As Boolean = False

    Private turnSay As Integer = 0

    Private Function round_the_end(goalB As Single, margin As Single) As Object
        If hits Is Nothing Then Return Nothing

        ' ONLY GO ROUND SOMETHING THAT IS IN THE WAY.
        '
        ' Without this the rung fires whenever ANYTHING is in the forward
        ' band, in the way or not - so the hull rounds an obstacle, commits,
        ' meets the next one, rounds that, and orbits. Measured: 1616 degrees
        ' of heading over 337 metres, about four and a half laps, perfectly
        ' smoothly. The trail showed it as a circle before the number did.
        '
        ' If the road to the goal is open, there is nothing to go round and
        ' the rung declines, which hands the tick to the one that drives at
        ' the goal.
        If BrainTune.On_("blockonly", True) Then
            If will_clear(goalB, BrainTune.Get_("blockm", 16.0F)) Then Return Nothing
        End If

        ' The chain in the way: forward returns near enough to matter, and
        ' close enough to the lane we would actually drive. A tree thirty
        ' metres off the shoulder is not what we are going round.
        Dim lo = Single.MaxValue, hi = -Single.MaxValue
        Dim nearest = Single.MaxValue
        Dim any = False
        Dim band = h.halfX + margin + BrainTune.Get_("band", 2.5F)
        For Each q In hits
            If Not q.found Then Continue For
            Dim a = wrap_pi(q.angle)
            If Math.Abs(a) > 1.2F Then Continue For
            If q.dist > BrainRadar.REACH_M * 0.6F Then Continue For
            If Math.Abs(CSng(Math.Sin(a)) * q.dist) > band Then Continue For
            any = True
            If a < lo Then lo = a
            If a > hi Then hi = a
            If q.dist < nearest Then nearest = q.dist
        Next
        If Not any Then Return Nothing

        ' How far off the edge we have to aim to take the hull past it. At
        ' two metres that is a big angle, at twenty it is a small one, which
        ' is why it comes off the range rather than being a constant.
        Dim swing = CSng(Math.Atan2(h.halfX + margin, Math.Max(2.0F, nearest)))
        Dim leftB = wrap_pi(lo - swing)
        Dim rightB = wrap_pi(hi + swing)

        ' ---- AND THEN COMMIT TO A SIDE -------------------------------
        '
        ' "its like going around a blind corner. we dont know whats there but
        '  we have to turn so don't wait. check if we can and if we can't turn."
        '
        ' Choosing the better side EVERY TICK is what put the hull in a loop of
        ' forward and back: the two sides are near enough equal in the middle,
        ' so a centimetre of movement swaps the answer, the nose swings the
        ' other way, and that centimetre comes back. The same knife edge as the
        ' backing threshold, one level up.
        '
        ' So the side is chosen ONCE and then owed COMMIT_M metres of road. The
        ' bearing is still recomputed every tick - it has to be, or it could
        ' not track the edge as the hull closes on it and the chain grows -
        ' but WHICH EDGE is not up for reconsideration until the debt is paid.
        '
        ' The commitment breaks early for exactly one reason: the side we chose
        ' stopped being drivable. That is the "if we can't" half - do not sit
        ' there insisting on a turn the ground will not give.
        Dim probe = Math.Min(nearest + 6.0F, 18.0F)
        roundHold -= Math.Abs(h.speed) * dt

        If roundSide <> 0 AndAlso roundHold > 0.0F Then
            Dim held = If(roundSide < 0, leftB, rightB)
            If box_clear(held, probe) Then Return CObj(held)
            ' It shut. Drop it and choose again this tick rather than next.
            roundSide = 0
        End If

        Dim leftFirst = Math.Abs(wrap_pi(leftB - goalB)) <=
                        Math.Abs(wrap_pi(rightB - goalB))
        Dim first_ = If(leftFirst, leftB, rightB)
        Dim other_ = If(leftFirst, rightB, leftB)

        If box_clear(first_, probe) Then
            roundSide = If(leftFirst, -1, 1)
            roundHold = BrainTune.Get_("commit", 0.0F)
            Return CObj(first_)
        End If
        If box_clear(other_, probe) Then
            roundSide = If(leftFirst, 1, -1)
            roundHold = BrainTune.Get_("commit", 0.0F)
            Return CObj(other_)
        End If
        roundSide = 0
        ' The edges of what we can see did not offer anything. Before giving
        ' the tick to Reverse, ask the harder question: is there ANY bearing
        ' the hull can turn to and then move along.
        Return way_past(goalB)
    End Function

    Private Function will_clear(bearing As Single, metres As Single) As Boolean
        Dim head = h.headingRad + bearing
        Dim far = h.pos + New Vector2(CSng(Math.Sin(head)) * metres,
                                      CSng(Math.Cos(head)) * metres)
        Return BrainNav.Clear(h.pos, far, h.DriveRadius)
    End Function

    ''' <summary>
    ''' WILL THE HULL'S OWN BOX GO THAT WAY - the test the tank actually
    ''' drives on.
    '''
    ''' will_clear traces a LINE through the nav grid. Corridor sweeps the
    ''' HULL. A line fits through places the box does not, and at the spot
    ''' every run parked the difference was decisive: the board approved a
    ''' bearing two degrees off the nose with an obstacle one metre ahead,
    ''' held a third of throttle against it, and never moved again.
    '''
    ''' A rung that decides where to go must use the same test as the thing
    ''' that stops us, or it is answering a different question about a
    ''' different world.
    ''' </summary>
    ''' <summary>
    ''' WHAT KIND OF BLOCKED, and how far out it starts.
    '''
    ''' 0 open, 1 grazing - one edge touched, the hull carries on through
    ''' it - 2 shut across the width. The three answers need three
    ''' different moves, and `hit` collapsed them into one.
    '''
    ''' Lane already carried all of this and only `hit` was ever read.
    ''' </summary>
    Private Function box_verdict(bearing As Single, metres As Single,
                                 ByRef atM As Single) As Integer
        Dim lane = BrainRadar.Corridor(h.pos, h.headingRad + bearing,
                                       h.DriveRadius, metres, False)
        atM = lane.dist
        If Not lane.hit Then Return 0
        ' One edge, and most of the width still open, is something the
        ' hull passes alongside rather than something in the way.
        Dim bothSides = lane.leftHit AndAlso lane.rightHit
        Dim mostShut = lane.planks > 0 AndAlso
                       lane.blocked * 3 > lane.planks
        If bothSides OrElse mostShut Then Return 2
        Return 1
    End Function

    Private Function box_clear(bearing As Single, metres As Single) As Boolean
        Dim lane = BrainRadar.Corridor(h.pos, h.headingRad + bearing,
                                       h.DriveRadius, metres, False)
        Return Not lane.hit
    End Function

    ' ---- steering ---------------------------------------------------------

    ''' <summary>Steer toward a bearing off the nose. Proportional and clamped:
    ''' the sharp-turn complaint earlier in the project was a gain of 1.0 on
    ''' this, which spins on the spot at the smallest error.</summary>
    Private Function turn_to(bearing As Single) As Single
        ' SHARPER WHILE SCANNING. At the cruising gain a forty degree error asks
        ' for 42% of the turn rate, which is a leisurely swing to be making when
        ' something is already touching the planks. The gentle gain is still
        ' what ordinary driving gets - "we need to not turn so sharp" was about
        ' cruising, not about getting out of the way.
        ' THE SMOOTHNESS KNOB, and the one that goes straight at deg/m. A
        ' high gain answers a small error with a big swing, which is how a
        ' hull ends up at full lock correcting two degrees.
        Dim gain = If(BrainRadar.SCANNING,
                      BrainTune.Get_("scangain", 1.8F),
                      BrainTune.Get_("gain", 0.6F))
        Return Math.Max(-1.0F, Math.Min(1.0F, wrap_pi(bearing) * gain))
    End Function

    ''' <summary>
    ''' How fast we can go and still finish the turn in the room we have.
    '''
    ''' The hull swings at a fixed rate, so a heading change takes a known
    ''' TIME, and at a given speed that time is a known DISTANCE. If the swing
    ''' would carry us further than the room ahead, slow by exactly that ratio -
    ''' and no further. With room to spare this does not slow at all, which is
    ''' the point: slowing is for finding a way out, not a reflex.
    '''
    ''' The two figures mirror BrainSim's TOP_SPEED and TURN_RATE, which are
    ''' Private there. If they move, this goes stale quietly - the symptom
    ''' would be arriving at gaps still swinging.
    ''' </summary>
    ''' <summary>
    ''' THE SPEED THIS TURN CAN BE TAKEN AT, or nothing if none can.
    '''
    ''' To clear something `room` ahead we must swing theta before reaching
    ''' it. At speed v that is room/v seconds, so the turn demands
    '''
    '''     omega_required = theta * v / room
    '''
    ''' and the hull delivers at most TURN_RATE. Rearranged, that is a
    ''' SPEED rather than a verdict:
    '''
    '''     v_max = TURN_RATE * room / theta
    '''
    ''' so there is nothing to tune. Either a speed exists that clears the
    ''' turn - drive at it - or none does, and we stop and pivot, which is
    ''' the one command the sim never refuses.
    '''
    ''' This replaces a fixed fifty degree pivot threshold that ignored how
    ''' much room there was, and a tail that scaled the throttle down for a
    ''' turn that would not fit and then floored it at CREEP. The comment
    ''' above that floor said "still 0.35 into a wall" and it was right: a
    ''' 24 degree turn with 1 m of room needed 11.2 m of arc, had 1 m, and
    ''' crept forward regardless.
    ''' </summary>
    ''' <summary>
    ''' THE WIDEST TURNING CIRCLE THAT STILL MISSES THE SQUARES.
    '''
    ''' "not tight enough turn. We have squares at each ray to get around."
    '''
    ''' A tank turning traces a circle of radius v / TURN_RATE, and at twelve
    ''' metres a second that circle is TWENTY-SEVEN METRES across. No amount
    ''' of lock takes it round a square four metres away - the only thing
    ''' that tightens a turn is going slower. So the squares set the speed.
    '''
    ''' Turning right, the circle's centre is at (r, 0) in the hull's frame.
    ''' A return at (x, z) is cleared when it lies more than r + w from that
    ''' centre, w being half the hull plus a margin:
    '''
    '''     (x - r)^2 + z^2  >  (r + w)^2
    '''     x^2 + z^2 - w^2  >  2r(x + w)
    '''     r  <  (x^2 + z^2 - w^2) / (2(x + w))
    '''
    ''' Turning left is the mirror, so one sign flip covers both. The
    ''' tightest constraint over every return is the circle we are allowed,
    ''' and v = r * TURN_RATE is the speed that draws it.
    '''
    ''' A return whose numerator is negative is already inside the swept
    ''' width - no circle clears it at any speed, and the answer is to stop
    ''' and pivot rather than to go slowly into it.
    ''' </summary>
    Private Function speed_for_squares(dir_ As Single) As Single
        If hits Is Nothing Then Return Single.MaxValue
        Dim w = h.halfX + BrainTune.Get_("sqmargin", 0.5F)
        Dim rMin = Single.MaxValue
        For Each q In hits
            If Not q.found Then Continue For
            ' Only what is ahead of the axle can be driven into while turning.
            Dim z = CSng(Math.Cos(q.angle)) * q.dist
            If z <= 0.0F Then Continue For
            If q.dist > BrainRadar.REACH_M * 0.5F Then Continue For
            Dim x = CSng(Math.Sin(q.angle)) * q.dist
            Dim den = 2.0F * (x * dir_ + w)
            If den <= 0.01F Then Continue For        ' the other side of the turn
            Dim num = x * x + z * z - w * w
            If num <= 0.0F Then Return 0.0F          ' already inside the sweep
            Dim r = num / den
            If r < rMin Then rMin = r
        Next
        If rMin = Single.MaxValue Then Return Single.MaxValue
        Return rMin * SIM_TURN_RATE
    End Function

    Private Function throttle_for_turn(bearing As Single, room As Single) As Single
        Const SIM_TOP As Single = 12.0F                  ' m/s at full throttle
        Dim swing = Math.Abs(wrap_pi(bearing))
        If swing < 0.05F Then Return throttle_for(room)

        ' Nothing ahead to clear: the turn has all the room it needs.
        Dim r = Math.Max(0.1F, room)
        If r >= BrainRadar.REACH_M Then Return throttle_for(room)

        ' The swing has to happen before we arrive...
        Dim vMax = SIM_TURN_RATE * r / swing
        ' ...and the circle it traces has to miss the squares on the way round.
        ' The second is usually the binding one: the arc limit says how soon,
        ' this says how tight, and a tank only turns tight by going slowly.
        vMax = Math.Min(vMax, speed_for_squares(CSng(Math.Sign(wrap_pi(bearing)))))
        ' Below a walking pace the turn is not being taken at speed, it is
        ' being taken standing still. Say so rather than creeping into it.
        If vMax < BrainTune.Get_("pivotunder", 1.5F) Then Return 0.0F

        Return Math.Min(throttle_for(room), vMax / SIM_TOP)
    End Function

    ''' <summary>
    ''' FULL LOCK, BECAUSE LOCK IS THE RATE AND SPEED IS THE RADIUS.
    '''
    ''' This sim turns the hull at a fixed rate that does not care how fast
    ''' it is going - headingRad += str * TURN_RATE * dt. So
    '''
    '''     radius = v / (str * TURN_RATE)
    '''
    ''' and the tightest possible turn at any speed is FULL lock. Slowing
    ''' down tightens the circle; easing the lock widens it again.
    '''
    ''' The previous version returned omega_required / TURN_RATE - a
    ''' FRACTION of lock - while speed_for_squares had already worked out the
    ''' speed on the assumption of full lock. So the throttle was set for a
    ''' tight turn and the steering then declined to make it, widening every
    ''' circle by exactly that fraction. Partial lock at low speed is the
    ''' worst of both: slow AND wide.
    '''
    ''' The only taper is the last nine degrees, so the nose settles instead
    ''' of hunting across the line.
    ''' </summary>
    Private Function steer_to_clear(bearing As Single, room As Single,
                                    thr As Single) As Single
        Dim swing = wrap_pi(bearing)
        If Math.Abs(swing) < 0.02F Then Return 0.0F
        Dim ease = BrainTune.Get_("ease", 0.16F)
        Dim frac = Math.Min(1.0F, Math.Abs(swing) / Math.Max(0.02F, ease))
        Return Math.Sign(swing) * frac
    End Function

    ''' <summary>Throttle from room. Full when there is room, easing off as the
    ''' wall comes up, never below a creep - a brain that stops entirely stops
    ''' gathering the scans that would get it out.</summary>
    ''' <summary>
    ''' NEAR ZERO WHEN THERE IS A BLOCK AT THE NOSE.
    '''
    ''' This read Math.Max(CREEP, clearM / 25) - so a block ONE METRE ahead
    ''' computed 0.04 and was then floored to 0.35, which is four metres a
    ''' second into it. The third place tonight where a correct calculation
    ''' was thrown away by a floor underneath it, and the third time that
    ''' floor was the bug.
    '''
    ''' The floor was there to stop the hull stalling at zero throttle. That
    ''' is what pivoting is for, and the chain walk now always has a bearing
    ''' to pivot toward - so the reason for it has gone.
    '''
    ''' Zero inside the standoff, which is the hull's own half length plus a
    ''' margin: a tank whose nose is against something has no forward speed
    ''' available to it, whatever the throttle says.
    ''' </summary>
    Private Function throttle_for(clearM As Single) As Single
        Const FULL_AT As Single = 25.0F
        ' NOT halfZ + margin. Corridor casts from the hull's front CORNERS, so
        ' body_ahead is already the clearance beyond the tank - adding half
        ' the hull length again counted it twice and made the standoff 5.0 m.
        ' With exactly 5.0 m of clear road the throttle computed zero and the
        ' hull sat still: "5.0 m clear, thr 0.00".
        Dim standoff = BrainTune.Get_("standoff", 1.5F)
        If clearM <= standoff Then Return 0.0F
        Return Math.Min(1.0F, (clearM - standoff) / FULL_AT)
    End Function

    ' ---- coercion ---------------------------------------------------------

    ''' <summary>A value in one short string, for the node to wear. Units
    ''' where there are any - a bare number on a board of fifty-nine nodes is
    ''' a number you have to go and look up.</summary>
    Private Function shown(v As Object) As String
        If v Is Nothing Then Return "-"
        If TypeOf v Is Boolean Then Return If(CBool(v), "True", "False")
        If TypeOf v Is Single Then Return CSng(v).ToString("0.0")
        If TypeOf v Is BrainRadar.Way Then
            Dim w = CType(v, BrainRadar.Way)
            Return String.Format("{0:0.0} m @ {1:0}", w.chord,
                                 MathHelper.RadiansToDegrees(w.bearing))
        End If
        If TypeOf v Is List(Of BrainRadar.Way) Then
            Return CType(v, List(Of BrainRadar.Way)).Count.ToString() & " ways"
        End If
        If TypeOf v Is BrainRadar.Hit() Then
            Return CType(v, BrainRadar.Hit()).Length.ToString() & " rays"
        End If
        Return "?"
    End Function

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
                    If Not BrainGoal.TryMove(p) Then Return
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

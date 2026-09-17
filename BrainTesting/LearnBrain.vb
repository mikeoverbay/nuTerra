Imports OpenTK.Mathematics

''' <summary>
''' DRIVES AT THE GOAL, AND LEARNS HOW TO GET ROUND WHAT IS IN THE WAY.
'''
''' "seek look at point from tank start. you make your own candidates. I want
''' it to learn as it tries." - the owner, 2026-09-16.
'''
''' TWO STATES AND NOTHING ELSE.
'''
'''   SEEKING     point at the goal and drive. The way is open; there is
'''               nothing to decide.
'''   MANOEUVRE   something is in the way. Pick a move from BrainMemory, commit
'''               to it, and drive it to the end.
'''
''' THE COMMITMENT IS THE WHOLE DESIGN. A brain that re-decides every frame
''' never finishes a manoeuvre and never learns anything, because no attempt is
''' ever carried out far enough to be worth scoring. That is the failure the
''' app's old driver had - a jam branch that re-entered itself sixty times a
''' second - and it is why a leg here runs until it ENDS: the cap, a fresh
''' block, or a stall.
'''
''' SCORED ON WHAT THE LEG ACHIEVED, not on what happened afterwards. Credit
''' for the whole journey would reward the last move of a lucky run and punish
''' the first move of an unlucky one, and neither is a fact about the move.
''' Progress toward the goal per metre driven, docked for time, zero past 40 m.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Public Class LearnBrain
    Implements IBrain

    Public Shared DRIVER As Integer = 0

    Private Const ARRIVE_M As Single = 5.0F
    Private Shared ReadOnly FULL_LOCK As Single = MathHelper.DegreesToRadians(20.0F)
    Private Shared ReadOnly TURN_FIRST As Single = MathHelper.DegreesToRadians(50.0F)

    ''' <summary>Close enough that the nose cannot come round in time. Not a
    ''' braking distance - see SeekBrain for why those are different.</summary>
    Private Const BLOCK_M As Single = 5.0F

    ''' <summary>Stalled: barely moving for this long. A tank that is turning
    ''' on the spot is not stalled, which is why this counts DISTANCE and not
    ''' speed.</summary>
    Private Const STALL_S As Single = 1.5F
    Private Const STALL_M As Single = 0.4F

    ''' <summary>A leg that has run this long has failed whatever it is
    ''' doing.</summary>
    Private Const LEG_TIMEOUT_S As Single = 20.0F

    ''' <summary>
    ''' GIVE UP ON A MOVE THAT IS LOSING GROUND.
    '''
    ''' "40M travel limit remember? we can have it drive of to space" - the
    ''' owner. The cap ended the leg at 40 m and scored it zero, which is
    ''' right, but it let the hull drive the whole 40 m in the wrong
    ''' direction first: two legs in the first run lost 23 m and 38 m of
    ''' ground before the cap caught them.
    '''
    ''' Going backwards is sometimes correct - round an obstacle is rarely a
    ''' straight line - so this is not zero. It is the distance past which a
    ''' move has stopped being a detour and started being a departure.
    ''' </summary>
    Private Const LOST_GROUND_M As Single = 15.0F

    Private Enum St
        Seeking = 0
        Manoeuvre
    End Enum

    Private state As St = St.Seeking

    ' the leg in progress
    Private legKey As String = ""
    Private legMove As BrainMemory.Move
    Private legHeading As Single = 0.0F
    Private legFrom As Vector2
    Private legRange0 As Single
    Private legTravel As Single
    Private legSeconds As Single
    Private lastPos As Vector2
    Private stillFor As Single
    Private stillFrom As Vector2

    ' ---- the ATTEMPT: everything since first contact --------------------
    '
    ' "if we cant find the edge of the blocker or score in 40 M total travel
    ' stop." - the owner. The 40 m is a BUDGET for getting past one obstacle,
    ' not an allowance per move: three legs of 39 m are not three cheap
    ' attempts, they are 117 m of driving round in the dark.
    '
    ' It opens at first contact and closes when the hull is past the thing -
    ' nearer the goal than it was when it stopped - or when the budget is
    ' spent, whichever comes first. Spent means STOP: the tank has proved it
    ' cannot solve this one, and the honest thing is to say so rather than
    ' wander until something else interrupts.
    Private attemptOn As Boolean = False
    Private attemptTravel As Single
    Private attemptRange0 As Single
    Private gaveUp As Boolean = False

    ''' <summary>Past the obstacle: this much nearer than when it stopped.</summary>
    Private Const PAST_IT_M As Single = 5.0F

    Public Legs As Integer = 0
    Public LastScore As Double = 0.0
    Public LastMove As String = "-"
    Public LastKey As String = ""

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            Return "learn"
        End Get
    End Property

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        state = St.Seeking
        Legs = 0
        attemptOn = False
        gaveUp = False
        BrainMemory.Load()
        If first.hulls IsNot Nothing AndAlso first.hulls.Length > DRIVER Then
            lastPos = first.hulls(DRIVER).pos
            stillFrom = lastPos
        End If
        stillFor = 0.0F
        LogThis("brain: LearnBrain on hull {0} - {1} situation(s) already known",
                DRIVER, BrainMemory.Situations)
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

        Dim moved = (h.pos - lastPos).Length
        lastPos = h.pos
        legTravel += moved
        legSeconds += inp.dt
        If attemptOn Then attemptTravel += moved

        ' STALLED IS ABOUT GROUND COVERED, not about speed. A hull turning on
        ' the spot has speed and is going nowhere.
        If (h.pos - stillFrom).Length < STALL_M Then
            stillFor += inp.dt
        Else
            stillFor = 0.0F
            stillFrom = h.pos
        End If

        Dim toGoal = goal - h.pos
        Dim range = toGoal.Length

        ' THE BUDGET. Checked before anything else acts on it, so a spent
        ' attempt cannot start one more leg on its way out.
        If attemptOn Then
            If range <= attemptRange0 - PAST_IT_M Then
                LogThis("brain: past it - {0:0.0} m of budget used, {1:0.0} m nearer",
                        attemptTravel, attemptRange0 - range)
                attemptOn = False
            ElseIf attemptTravel >= BrainMemory.MAX_LEG_M Then
                If state = St.Manoeuvre Then finish_leg(range)
                attemptOn = False
                gaveUp = True
                LogThis("brain: GAVE UP - {0:0.0} m spent without getting past it",
                        attemptTravel)
            End If
        End If
        If gaveUp Then
            o.why(DRIVER) = "gave up"
            Return o
        End If

        If range <= ARRIVE_M Then
            If state = St.Manoeuvre Then finish_leg(range)
            o.why(DRIVER) = "arrived"
            Return o
        End If

        ' THE FULL SCAN, every tick. A two-stage version was tried - nine rays
        ' while driving, the full sweep on contact - and it was reverted: the
        ' coarse scan became the last scan, which is what the scope and the
        ' surface fit read, so the picture went sparse and the fit fell below
        ' its minimum and reported nothing. The saving was 0.4 ms of a 16 ms
        ' frame, which was never worth the thing it cost.
        Dim hits = BrainRadar.Scan(h.pos, h.headingRad)
        Dim nearFront = BrainRadar.REACH_M
        For Each q In hits
            If Not q.front OrElse Not q.found Then Continue For
            If Math.Abs(q.angle) > MathHelper.DegreesToRadians(30.0F) Then Continue For
            nearFront = Math.Min(nearFront, q.dist)
        Next

        Dim wantGoal = CSng(Math.Atan2(toGoal.X, toGoal.Y))

        If state = St.Manoeuvre Then
            Dim lost = range - legRange0
            Dim ended = (legTravel >= BrainMemory.MAX_LEG_M) OrElse
                        (legSeconds >= LEG_TIMEOUT_S) OrElse
                        (stillFor >= STALL_S) OrElse
                        (lost >= LOST_GROUND_M) OrElse
                        (nearFront <= BLOCK_M AndAlso legTravel > 2.0F)
            If ended Then
                finish_leg(range)
            Else
                Dim err = wrap_pi(legHeading - h.headingRad)
                o.steer(DRIVER) = Math.Clamp(err / FULL_LOCK, -1.0F, 1.0F)
                If Math.Abs(err) > TURN_FIRST Then
                    o.why(DRIVER) = "turn " & legMove.name
                Else
                    o.throttle(DRIVER) = If(legMove.reverse, -0.6F, 1.0F)
                    o.why(DRIVER) = "try " & legMove.name
                End If
                Return o
            End If
        End If

        ' ---- seeking --------------------------------------------------------
        ' STUCK IS NOT ONLY "SOMETHING IN FRONT". A hull can be wedged on a
        ' kerb, hung on a corner, or grinding along a wall with its front rays
        ' clear - and the old test only started a manoeuvre when the RADAR said
        ' blocked. So it would stall, go back to seeking, drive into the same
        ' thing, and stall again, forever, with nothing recorded.
        '
        ' "we need this to not stop when it gets stuck." So: not moving for
        ' STALL_S is enough on its own to try something, whatever the rays say.
        Dim stuck = (stillFor >= STALL_S)
        If nearFront <= BLOCK_M OrElse stuck Then
            ' FIRST CONTACT. Record where we are, choose something, commit.
            If Not attemptOn Then
                attemptOn = True
                attemptTravel = 0.0F
                attemptRange0 = range
            End If
            Dim bearing = wrap_pi(wantGoal - h.headingRad)
            legKey = BrainMemory.Situation(hits, bearing)
            legMove = BrainMemory.Choose(legKey)
            legHeading = wrap_pi(h.headingRad + MathHelper.DegreesToRadians(legMove.turnDeg))
            legFrom = h.pos
            legRange0 = range
            legTravel = 0.0F
            legSeconds = 0.0F
            stillFor = 0.0F
            stillFrom = h.pos
            state = St.Manoeuvre
            LastKey = legKey
            LastMove = legMove.name
            o.why(DRIVER) = If(stuck, "stuck, try ", "try ") & legMove.name
            Return o
        End If

        Dim e2 = wrap_pi(wantGoal - h.headingRad)
        o.steer(DRIVER) = Math.Clamp(e2 / FULL_LOCK, -1.0F, 1.0F)
        If Math.Abs(e2) > TURN_FIRST Then
            o.why(DRIVER) = "turning"
        Else
            Dim thr = 1.0F
            If nearFront < 10.0F Then
                thr = Math.Max(0.3F, (nearFront - BLOCK_M) / (10.0F - BLOCK_M))
            End If
            If range < 20.0F Then thr = Math.Min(thr, Math.Max(0.25F, range / 20.0F))
            o.throttle(DRIVER) = thr
            o.why(DRIVER) = "seeking"
        End If
        Return o
    End Function

    ''' <summary>Score what the leg achieved and put it in the book.</summary>
    Private Sub finish_leg(rangeNow As Single)
        Dim gained = legRange0 - rangeNow
        LastScore = BrainMemory.Score(gained, legTravel, legSeconds)
        BrainMemory.Remember(legKey, legMove.name, LastScore)
        Legs += 1
        LogThis("brain: leg {0} [{1}] {2} - gained {3:0.0} m over {4:0.0} m in {5:0.0} s -> {6:0.000}",
                Legs, legKey, legMove.name, gained, legTravel, legSeconds, LastScore)
        state = St.Seeking
        stillFor = 0.0F
    End Sub

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

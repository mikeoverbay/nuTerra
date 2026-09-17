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

    ''' <summary>A turn is given this long to pan out before it is judged.</summary>
    Private Const TEST_S As Single = 1.2F

    ''' <summary>Reverse for this long once the rear wins.</summary>
    Private Const BACK_S As Single = 2.0F

    Private Enum St
        Seek = 0
        Turning        ' swinging toward a longer ray, not yet re-scanned
        Backing
    End Enum

    Private state As St = St.Seek
    Private wantHeading As Single
    Private testFor As Single
    Private backFor As Single
    Private lastOpen As Single

    Public Why As String = "-"

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
            Return o
        End If

        Dim hits = BrainRadar.Scan(h.pos, h.headingRad)
        Dim surf = BrainRadar.FitSurface(hits)

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
            state = St.Seek
        End If

        ' ---- a turn under test ---------------------------------------------
        If state = St.Turning Then
            Dim err = wrap_pi(wantHeading - h.headingRad)
            o.steer(DRIVER) = Math.Clamp(err / FULL_LOCK, -1.0F, 1.0F)
            testFor -= inp.dt
            If Math.Abs(err) > TURN_FIRST Then
                Why = "turning to look"
                o.why(DRIVER) = Why
                Return o
            End If
            ' Facing it now. DID IT PAN OUT? The new scan is the test.
            If testFor <= 0.0F Then
                If ahead > lastOpen + BETTER_M OrElse ahead > BLOCK_M * 2.0F Then
                    Why = "it panned out"
                    state = St.Seek
                Else
                    Why = "it did not pan out"
                    state = St.Seek
                End If
                LogThis("brain: turn tested - open was {0:0.0} m, now {1:0.0} m: {2}",
                        lastOpen, ahead, Why)
            End If
            o.throttle(DRIVER) = 0.4F
            o.why(DRIVER) = Why
            Return o
        End If

        ' ---- seeking --------------------------------------------------------
        Dim wantGoal = CSng(Math.Atan2(toGoal.X, toGoal.Y))
        Dim goalErr = wrap_pi(wantGoal - h.headingRad)

        If ahead > BLOCK_M Then
            o.steer(DRIVER) = Math.Clamp(goalErr / FULL_LOCK, -1.0F, 1.0F)
            If Math.Abs(goalErr) > TURN_FIRST Then
                Why = "turning"
            Else
                Dim thr = 1.0F
                If ahead < 10.0F Then thr = Math.Max(0.3F, (ahead - BLOCK_M) / (10.0F - BLOCK_M))
                If range < 20.0F Then thr = Math.Min(thr, Math.Max(0.25F, range / 20.0F))
                o.throttle(DRIVER) = thr
                Why = "seeking"
            End If
            o.why(DRIVER) = Why
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

        If Not wallAcross AndAlso bestOut > ahead + BETTER_M Then
            Dim goLeft = (leftBest >= rightBest)
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
            If edgeMiss >= 0 AndAlso surf.valid AndAlso surf.oneSurface Then
                ' Tangent of the fitted wall, on the side we are going. The
                ' normal points at the wall; a quarter turn from it runs along
                ' it, and the sign picks which way along.
                Dim tangent = surf.normalRad + If(goLeft, -MathHelper.PiOver2,
                                                           MathHelper.PiOver2)
                wantHeading = wrap_pi(h.headingRad + tangent)
                byWall = True
            Else
                wantHeading = wrap_pi(h.headingRad + pick.angle)
            End If

            lastOpen = ahead
            testFor = TEST_S
            state = St.Turning
            Why = If(byWall, "along the wall ", "trying ") &
                  If(goLeft, "left", "right") &
                  String.Format(" ({0:0.0} m vs {1:0.0})", bestOut, ahead)
            LogThis("brain: blocked at {0:0.0} m - {1}{2}", ahead, Why,
                    If(byWall, String.Format(", wall face {0:0} deg",
                                             MathHelper.RadiansToDegrees(surf.normalRad)),
                       ", no clean edge - using the long ray"))
            o.why(DRIVER) = Why
            Return o
        End If

        ' ---- the front is shut. IS THE REAR BETTER? --------------------------
        Dim rearBest = 0.0F
        For Each q In rear
            If q.dist > rearBest Then rearBest = q.dist
        Next
        If rearBest > ahead + BETTER_M Then
            state = St.Backing
            backFor = BACK_S
            Why = String.Format("rear is better ({0:0.0} m vs {1:0.0})", rearBest, ahead)
            LogThis("brain: wall across the scanner - {0}, reversing rather than turning round",
                    Why)
            o.throttle(DRIVER) = -0.6F
            o.why(DRIVER) = Why
            Return o
        End If

        Why = "boxed in"
        o.why(DRIVER) = Why
        Return o
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

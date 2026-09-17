Imports OpenTK.Mathematics

''' <summary>
''' THE FIRST REAL BRAIN. One tank drives at the crosshair.
'''
''' "i want a go here crosshair i can place with enter at the look at point. I
''' want the tank to seek it" - the owner, 2026-09-16.
'''
''' DELIBERATELY THE SMALLEST THING THAT COUNTS. One hull, one point, no route
''' and no recovery. It steers at the goal, eases off for what is in front of
''' it, and stops when it cannot go on. Everything cleverer has to beat this,
''' and a baseline nobody can beat is how a week gets spent tuning something
''' that was never the problem.
'''
''' GROUND AND HULLS ARE DIFFERENT QUESTIONS, and this is the one lesson
''' carried over from the afternoon's Python version rather than rediscovered.
''' Measured there: with one limit for both, 21 of 30 hulls stopped dead with
''' ground a median 8.50 m ahead, against routes whose waypoints were 100%
''' standable. The route was fine and the rays were fine; reading them
''' together was not.
'''
''' The braking distance belongs to things that MIGHT MOVE. Terrain only stops
''' a hull when it is close enough to actually hit, because the forward rays
''' look down the nose and a tank turning towards a goal points its nose at
''' whatever is on the outside of the turn. Separating the two took median
''' travel from 76 m to 401 m in the same 180 seconds.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Public Class SeekBrain
    Implements IBrain

    ''' <summary>Which hull drives. One tank, as asked - the rest park.</summary>
    Public Shared DRIVER As Integer = 0

    ''' <summary>Stop this close: the goal is a place, not a pixel.</summary>
    Private Const ARRIVE_M As Single = 5.0F

    ''' <summary>Full lock at this heading error, easing to none as it lines
    ''' up - so it does not saw about the bearing.</summary>
    Private Shared ReadOnly FULL_LOCK As Single = MathHelper.DegreesToRadians(20.0F)

    ''' <summary>Past this much error, turn on the spot. A tank that drives and
    ''' turns at once leaves the line on the outside of every corner.</summary>
    Private Shared ReadOnly TURN_FIRST As Single = MathHelper.DegreesToRadians(50.0F)

    ''' <summary>Close enough to hit before the nose can come round. Half a
    ''' hull plus a margin - NOT a braking distance.</summary>
    Private Const GROUND_STOP_M As Single = 4.5F
    Private Const GROUND_SLOW_M As Single = 10.0F

    ''' <summary>A hull ahead stops this one at the full braking distance:
    ''' v^2 / 2a is 7.6 m at 11 m/s, plus the nose.</summary>
    Private Const HULL_STOP_M As Single = 9.0F
    Private Const HULL_CLEAR_M As Single = 18.0F

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            Return "seek"
        End Get
    End Property

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        LogThis("brain: SeekBrain driving hull {0} of {1}; the rest park",
                DRIVER, If(first.hulls Is Nothing, 0, first.hulls.Length))
    End Sub

    Public Function Tick(inp As BrainInput) As BrainOutput Implements IBrain.Tick
        Dim n = If(inp.hulls Is Nothing, 0, inp.hulls.Length)
        Dim o = BrainOutput.ForHulls(n)
        For i = 0 To n - 1
            o.why(i) = "parked"
        Next
        If n = 0 OrElse DRIVER < 0 OrElse DRIVER >= n Then Return o
        If Not BrainGoal.HasTarget Then
            o.why(DRIVER) = "no goal"
            Return o
        End If

        Dim h = inp.hulls(DRIVER)
        Dim goal = BrainGoal.Target
        o.target(DRIVER) = goal
        o.row(DRIVER) = 0

        Dim to_goal = goal - h.pos
        Dim range = to_goal.Length
        If range <= ARRIVE_M Then
            o.why(DRIVER) = "arrived"
            Return o
        End If

        ' STEER AT IT. Bearing in the hull's frame, eased so a small error is a
        ' small correction.
        Dim want = CSng(Math.Atan2(to_goal.X, to_goal.Y))
        Dim err = wrap_pi(want - h.headingRad)
        Dim steer = Math.Clamp(err / FULL_LOCK, -1.0F, 1.0F)
        o.steer(DRIVER) = steer

        If Math.Abs(err) > TURN_FIRST Then
            o.why(DRIVER) = "turning"
            Return o
        End If

        ' WHAT IS IN FRONT, from the radar this hull already carries - the same
        ' rays that are drawn, so what stops it is what you can see stopping it.
        Dim hits = BrainRadar.Scan(h.pos, h.headingRad)
        Dim near_ground = BrainRadar.REACH_M
        For Each q In hits
            If Not q.front OrElse Not q.found Then Continue For
            ' Only the rays looking roughly where it is going. A ray 60 degrees
            ' off the nose finds the verge, not the way ahead.
            If Math.Abs(q.angle) > MathHelper.DegreesToRadians(30.0F) Then Continue For
            near_ground = Math.Min(near_ground, q.dist)
        Next

        If near_ground <= GROUND_STOP_M Then
            o.why(DRIVER) = "wall ahead"
            Return o
        End If

        Dim throttle = 1.0F
        If near_ground < GROUND_SLOW_M Then
            Dim f = (near_ground - GROUND_STOP_M) / (GROUND_SLOW_M - GROUND_STOP_M)
            throttle = Math.Max(0.3F, Math.Min(1.0F, f))
        End If
        ' And ease down as it arrives, so it stops ON the cross rather than
        ' driving through it and turning back.
        If range < 20.0F Then
            throttle = Math.Min(throttle, Math.Max(0.25F, range / 20.0F))
        End If

        o.throttle(DRIVER) = throttle
        o.why(DRIVER) = "seeking"
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

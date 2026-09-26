Imports OpenTK.Mathematics

''' <summary>
''' THE OTHER TANKS. Where they are, and whether one is in the way.
'''
''' "now we need to be aware of other tanks. lets get team mate hit reignition
''' working. we need 2 tanks to collide to test." - the owner, 2026-09-25.
'''
''' NOTHING IN THIS APP KNEW ABOUT ANOTHER HULL UNTIL NOW. BrainNav.Standable
''' reads the baked grid and nothing else, so BrainSim.Apply let five tanks drive
''' straight through one another and stack on the same square. The rays had the
''' same blind spot: a teammate parked across a doorway read as open sky.
'''
''' THE HULL BOUNDING BOX ONLY - "need the tank hulls bounding box only", and
''' Body.half is already exactly that: "Half-extents of the HULL in metres: X
''' across, Y up, Z along. The game's own boundingBox, via TankRoster." No
''' turret, no gun, no barrel sweeping a circle nobody can drive through.
'''
''' TWO DIMENSIONS, NOT THREE. BrainPick already has a RayBox for the mouse, and
''' it is the wrong tool here: the radar is a flat scan and these hulls do not
''' roll, so the question is a ray against an oriented RECTANGLE - half.X across
''' by half.Z along, turned by the heading. That is two dot products and a slab
''' test, against a 3D box test's twelve, and it runs 120 rays deep.
'''
''' CHEAP BY EXCLUSION FIRST. A circle test on the centres throws out every tank
''' that cannot possibly be on the ray before any of the arithmetic happens, so
''' the usual cost of this module is a handful of subtractions - on a ten-hull
''' roster spread over a kilometre of map, nearly always zero rectangles tested.
''' </summary>
Module BrainMates

    ''' <summary>Off turns tank-vs-tank back off entirely, both the rays and the
    ''' sim's refusal, for comparing against every run recorded before this
    ''' existed.</summary>
    Public Function On_() As Boolean
        Return BrainTune.On_("tankhit", True)
    End Function

    ''' <summary>
    ''' HOW FAR ALONG THIS RAY THE NEAREST OTHER HULL IS, or -1.
    '''
    ''' `me` is skipped, and it has to be: a ray cast from inside a hull's own
    ''' box would report a hit at zero metres on every bearing and the tank would
    ''' believe itself walled in.
    ''' </summary>
    Public Function Along(me_ As Integer, o As Vector2, d As Vector2, maxDist As Single,
                          ByRef whoHull As Integer, ByRef whoTeam As Integer) As Single
        whoHull = -1
        whoTeam = 0
        If Not On_() OrElse BrainTanks.Bodies Is Nothing Then Return -1.0F

        Dim best = -1.0F
        For i = 0 To BrainTanks.Bodies.Count - 1
            If i = me_ Then Continue For
            Dim b = BrainTanks.Bodies(i)

            ' THE CHEAP REJECT. A box whose centre is further from the ray's
            ' origin than the reach plus its own diagonal cannot be touched by
            ' this ray, and neither can one sitting behind the origin.
            Dim toB = b.spawn - o
            Dim reach = maxDist + b.half.X + b.half.Z
            If toB.LengthSquared > reach * reach Then Continue For

            Dim t = ray_rect(o, d, b.spawn, b.headingRad, b.half.X, b.half.Z)
            If t < 0.0F OrElse t > maxDist Then Continue For
            If best < 0.0F OrElse t < best Then
                best = t
                whoHull = i
                whoTeam = b.team
            End If
        Next
        Return best
    End Function

    ''' <summary>
    ''' WOULD THIS HULL, PUT HERE, BE INSIDE ANOTHER ONE?
    '''
    ''' Used by BrainSim.Apply beside Standable, which is the only reason two
    ''' tanks can collide at all - without it they interpenetrate and there is
    ''' no collision to detect, only two hulls sharing a square.
    '''
    ''' BOX AGAINST BOX, both turned. A circle-against-circle test on hulls that
    ''' run 4.6 to 7.9 m long and about 3 m wide would refuse a pass down a
    ''' street at six metres and allow a nose-to-nose touch at three.
    ''' </summary>
    Public Function Blocked(me_ As Integer, want As Vector2, headingRad As Single,
                            ByRef whoHull As Integer) As Boolean
        whoHull = -1
        If Not On_() OrElse BrainTanks.Bodies Is Nothing Then Return False
        If me_ < 0 OrElse me_ >= BrainTanks.Bodies.Count Then Return False

        Dim mine = BrainTanks.Bodies(me_)
        Dim gap = BrainTune.Get_("tankgap", 0.2F)
        Dim ax = mine.half.X + gap, az = mine.half.Z + gap

        For i = 0 To BrainTanks.Bodies.Count - 1
            If i = me_ Then Continue For
            Dim b = BrainTanks.Bodies(i)
            Dim d = b.spawn - want
            Dim rr = ax + az + b.half.X + b.half.Z
            If d.LengthSquared > rr * rr Then Continue For
            If rects_hit(want, headingRad, ax, az,
                         b.spawn, b.headingRad, b.half.X, b.half.Z) Then
                whoHull = i
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>
    ''' Ray against an oriented rectangle, in the rectangle's own frame.
    '''
    ''' The hull's forward is (sin h, cos h) - this app's convention everywhere -
    ''' so its side axis is (cos h, -sin h), and half.X measures across that side
    ''' while half.Z measures along the forward. Getting those two the wrong way
    ''' round swaps a tank's length for its width, which on a 7.9 m Grille is a
    ''' 5 m error and would read as the radar seeing ghosts.
    '''
    ''' Returns the NEAR distance, or -1. A ray starting inside returns 0 rather
    ''' than a negative, because being inside another tank is a hit at zero.
    ''' </summary>
    Private Function ray_rect(o As Vector2, d As Vector2, c As Vector2, hdg As Single,
                              hx As Single, hz As Single) As Single
        Dim f = New Vector2(CSng(Math.Sin(hdg)), CSng(Math.Cos(hdg)))
        Dim s = New Vector2(f.Y, -f.X)

        Dim p = o - c
        Dim ox = Vector2.Dot(p, s), oz = Vector2.Dot(p, f)
        Dim dx = Vector2.Dot(d, s), dz = Vector2.Dot(d, f)

        Dim t0 = -Single.MaxValue, t1 = Single.MaxValue
        If Not slab(ox, dx, hx, t0, t1) Then Return -1.0F
        If Not slab(oz, dz, hz, t0, t1) Then Return -1.0F
        If t1 < 0.0F Then Return -1.0F
        Return Math.Max(0.0F, t0)
    End Function

    ''' <summary>One axis of the slab test. A ray parallel to a slab either
    ''' misses it entirely or is unconstrained by it - the early return is that
    ''' distinction and not a division guard.</summary>
    Private Function slab(o As Single, d As Single, half As Single,
                          ByRef t0 As Single, ByRef t1 As Single) As Boolean
        If Math.Abs(d) < 0.000001F Then Return Math.Abs(o) <= half
        Dim inv = 1.0F / d
        Dim a = (-half - o) * inv
        Dim b = (half - o) * inv
        If a > b Then
            Dim tmp = a
            a = b
            b = tmp
        End If
        If a > t0 Then t0 = a
        If b < t1 Then t1 = b
        Return t0 <= t1
    End Function

    ''' <summary>
    ''' Two oriented rectangles overlapping, by separating axes.
    '''
    ''' Four axes is the whole test for rectangles - each one's two edge
    ''' normals - and a single axis with a gap proves no overlap. No iteration,
    ''' no tolerance to tune.
    ''' </summary>
    Private Function rects_hit(ca As Vector2, ha As Single, ax As Single, az As Single,
                               cb As Vector2, hb As Single, bx As Single, bz As Single) As Boolean
        Dim fa = New Vector2(CSng(Math.Sin(ha)), CSng(Math.Cos(ha)))
        Dim sa = New Vector2(fa.Y, -fa.X)
        Dim fb = New Vector2(CSng(Math.Sin(hb)), CSng(Math.Cos(hb)))
        Dim sb = New Vector2(fb.Y, -fb.X)
        Dim d = cb - ca

        If apart(d, sa, sa, fa, ax, az, sb, fb, bx, bz) Then Return False
        If apart(d, fa, sa, fa, ax, az, sb, fb, bx, bz) Then Return False
        If apart(d, sb, sa, fa, ax, az, sb, fb, bx, bz) Then Return False
        If apart(d, fb, sa, fa, ax, az, sb, fb, bx, bz) Then Return False
        Return True
    End Function

    ''' <summary>True when the two rectangles are separated along `axis`: the
    ''' centre gap exceeds the sum of their half-widths projected onto it.</summary>
    Private Function apart(d As Vector2, axis As Vector2,
                           sa As Vector2, fa As Vector2, ax As Single, az As Single,
                           sb As Vector2, fb As Vector2, bx As Single, bz As Single) As Boolean
        Dim ra = ax * Math.Abs(Vector2.Dot(sa, axis)) + az * Math.Abs(Vector2.Dot(fa, axis))
        Dim rb = bx * Math.Abs(Vector2.Dot(sb, axis)) + bz * Math.Abs(Vector2.Dot(fb, axis))
        Return Math.Abs(Vector2.Dot(d, axis)) > ra + rb
    End Function

End Module

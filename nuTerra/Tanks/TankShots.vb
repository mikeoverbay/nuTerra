Imports OpenTK.Mathematics

''' <summary>Where a shot stopped, and on what.</summary>
Public Enum HitKind
    NoHit = 0
    Ground = 1
    Scenery = 2
    Tank = 3
End Enum

''' <summary>One shot's terminus: where, what, and which way the surface faces.</summary>
Public Structure ShotHit
    Public kind As HitKind
    Public point As Vector3
    Public normal As Vector3
    Public range As Single
    Public tank As TankInstance      ' only for HitKind.Tank
End Structure

''' <summary>
''' Where the guns are pointing, what they hit, and the short-lived effects at
''' both ends of the shot.
'''
''' ONE MARCH, NOT THREE CASTS. The obvious shape is a cast against the terrain,
''' another against the scenery and a third against the vehicles, taking the
''' nearest - three traversals of the same ray with three chances to disagree
''' about where it started. MapFlightBake already holds the whole world as one
''' raster: top_m is the highest surface at each texel with models and terrain
''' baked together, and floor_m is the terrain under them. So terrain and
''' scenery are the SAME test - is the ray below top_m - and what separates
''' them afterwards is whether top_m stood above floor_m there. The bake is
''' also what the tanks were placed against and what the camera flight avoids,
''' so a shot, a parked tank and a camera cannot disagree about what is solid.
'''
''' The vehicles are the exception and have to be their own test: they are not
''' in the bake - it is built once at load, and they move.
''' </summary>
Public Class TankShots

    ''' <summary>How far a shot carries before it is given up on. The map is
    ''' 1400 m across, so this reaches the far side from anywhere on it.</summary>
    Private Const MAX_RANGE_M As Single = 2000.0F

    ''' <summary>
    ''' How far the march moves between samples: ONE TEXEL of the bake.
    '''
    ''' Not a constant, because the bake's resolution is not one. This was 0.5 m
    ''' against a 2048 bake whose texel is 0.68 m over 1400 - sensible then, and
    ''' wrong the moment the bake went to 8192 and the texel to 0.17: a round
    ''' would step clean over anything less than three texels wide. That is
    ''' exactly a fence, which is what the finer bake was raised to capture, so
    ''' the two changes would have cancelled each other out silently.
    '''
    ''' Reading it off the bake means neither has to know about the other. The
    ''' floor is there because the cost is per sample and a ray is up to two
    ''' kilometres; at a tenth of a metre that is twenty thousand lookups, which
    ''' is more than any obstacle in this world needs.
    ''' </summary>
    Private Shared Function step_m(b As MapFlightBake) As Single
        Return Math.Max(0.1F, (b.wx_max - b.wx_min) / MapFlightBake.SIZE)
    End Function

    ''' <summary>Refinement passes after the step that went under. Eight halvings
    ''' take a step of half a metre down to two millimetres, which is past the
    ''' point where the bake itself means anything.</summary>
    Private Const REFINE As Integer = 8

    ''' <summary>
    ''' Fire a ray and return where it stops.
    '''
    ''' The firing tank is skipped. A gun sits inside its own hull's box and
    ''' every shot would otherwise terminate on the vehicle that fired it,
    ''' 0 m out.
    ''' </summary>
    Public Shared Function Cast(origin As Vector3, dir As Vector3,
                                instances As List(Of TankInstance),
                                shooter As TankInstance) As ShotHit
        Dim hit As ShotHit
        hit.kind = HitKind.NoHit
        hit.range = MAX_RANGE_M

        If dir.LengthSquared < 1.0E-8F Then Return hit
        dir = Vector3.Normalize(dir)

        ' VEHICLES FIRST, and not because they are nearer - because the answer
        ' is exact. A box test returns the true entry distance, so once it is
        ' in hand the march only has to run that far, and a round that would
        ' have hit a hill behind a tank stops on the tank.
        If instances IsNot Nothing Then
            For Each other In instances
                If other Is shooter Then Continue For
                Dim t = hit_tank(origin, dir, other)
                If t > 0.0F AndAlso t < hit.range Then
                    hit.kind = HitKind.Tank
                    hit.range = t
                    hit.point = origin + dir * t
                    hit.normal = -dir
                    hit.tank = other
                End If
            Next
        End If

        Dim world = march(origin, dir, hit.range)
        If world.kind <> HitKind.NoHit AndAlso world.range < hit.range Then Return world
        Return hit
    End Function

    ''' <summary>
    ''' Walk the ray until it is under the world, then bisect.
    '''
    ''' Under TOP, which is the surface of whatever is there - a roof, a crate,
    ''' a hillside. The step that first samples below it has crossed the
    ''' surface somewhere in the last half metre, and eight halvings put the
    ''' impact on it. Marching without the bisection puts every impact up to
    ''' half a metre inside the thing it hit, which on a wall reads as the
    ''' decal floating and on a slope as it being buried.
    ''' </summary>
    Private Shared Function march(origin As Vector3, dir As Vector3,
                                  limit As Single) As ShotHit
        Dim hit As ShotHit
        hit.kind = HitKind.NoHit

        Dim b = map_scene.flight_bake
        If b Is Nothing OrElse Not b.ready Then Return hit

        ' stride, not step: Step is a keyword in VB.
        Dim stride = step_m(b)
        Dim t = stride
        Dim prev = 0.0F
        While t < limit
            Dim p = origin + dir * t
            If under_world(b, p) Then
                ' t_hi, not hiT: VB is case blind and hiT is the same
                ' identifier as the hit this function returns.
                Dim t_lo = prev, t_hi = t
                For i = 1 To REFINE
                    Dim mid = (t_lo + t_hi) * 0.5F
                    If under_world(b, origin + dir * mid) Then t_hi = mid Else t_lo = mid
                Next
                hit.range = t_hi
                hit.point = origin + dir * t_hi
                hit.kind = If(is_object(b, hit.point), HitKind.Scenery, HitKind.Ground)
                hit.normal = surface_normal(b, hit.point, hit.kind, dir)
                Return hit
            End If
            prev = t
            t += stride
        End While
        Return hit
    End Function

    Private Shared Function texel(b As MapFlightBake, p As Vector3) As Integer
        Dim c = CInt(Math.Floor((p.X - b.wx_min) / (b.wx_max - b.wx_min) * MapFlightBake.SIZE))
        Dim r = CInt(Math.Floor((b.wz_max - p.Z) / (b.wz_max - b.wz_min) * MapFlightBake.SIZE))
        If c < 0 OrElse r < 0 OrElse c >= MapFlightBake.SIZE OrElse
           r >= MapFlightBake.SIZE Then Return -1
        Return r * MapFlightBake.SIZE + c
    End Function

    Private Shared Function under_world(b As MapFlightBake, p As Vector3) As Boolean
        Dim i = texel(b, p)
        If i < 0 Then Return False        ' off the map: nothing to hit
        Return p.Y <= b.top_m(i)
    End Function

    ''' <summary>Was it a thing standing on the ground, or the ground? The
    ''' bake's own OBSTACLE_MIN_H is the same 1 m the tank placement uses, so
    ''' there is one definition of "an object" across the app.</summary>
    Private Shared Function is_object(b As MapFlightBake, p As Vector3) As Boolean
        Dim i = texel(b, p)
        If i < 0 Then Return False
        Return (b.top_m(i) - b.floor_m(i)) > 1.0F
    End Function

    ''' <summary>
    ''' Which way the struck surface faces.
    '''
    ''' GROUND GETS A REAL NORMAL, from the terrain either side of the hit -
    ''' a decal on a hillside has to lie along the slope or it stands up out of
    ''' it. An OBJECT gets the incoming ray reversed instead: the bake is a
    ''' heightfield and knows the TOP of a wall, not that the wall has a face,
    ''' so its gradient at a vertical surface is a cliff and the normal from it
    ''' would point along the wall rather than out of it. Facing the shooter is
    ''' wrong by however much the wall is not perpendicular, and right about
    ''' the thing that matters, which is that the mark is visible and flat
    ''' against something.
    ''' </summary>
    Private Shared Function surface_normal(b As MapFlightBake, p As Vector3,
                                           kind As HitKind, dir As Vector3) As Vector3
        If kind <> HitKind.Ground Then Return -dir
        Const H As Single = 1.0F
        Dim yl = get_Y_at_XZ_fast(p.X - H, p.Z)
        Dim yr = get_Y_at_XZ_fast(p.X + H, p.Z)
        Dim yd = get_Y_at_XZ_fast(p.X, p.Z - H)
        Dim yu = get_Y_at_XZ_fast(p.X, p.Z + H)
        Dim n = New Vector3(yl - yr, 2.0F * H, yd - yu)
        If n.LengthSquared < 1.0E-8F Then Return Vector3.UnitY
        Return Vector3.Normalize(n)
    End Function

    ''' <summary>
    ''' Distance to a vehicle's box, or -1.
    '''
    ''' THE RAY GOES INTO THE TANK'S FRAME rather than the box coming out into
    ''' the world. A hull is three times longer than it is wide and it is
    ''' parked at a heading, so a world-aligned box around it is half again as
    ''' big as the tank and shots stop in the air beside it. Un-rotating the
    ''' ray by the heading costs two trig calls and makes the test exact
    ''' against the box the vehicle actually occupies.
    ''' </summary>
    Private Shared Function hit_tank(origin As Vector3, dir As Vector3,
                                     inst As TankInstance) As Single
        Dim bmin = inst.vehicle.boundsMin
        Dim bmax = inst.vehicle.boundsMax
        Dim o = origin - shot_position(inst)
        Dim c = CSng(Math.Cos(-inst.headingRad)), s2 = CSng(Math.Sin(-inst.headingRad))
        Dim lo As New Vector3(c * o.X + s2 * o.Z, o.Y, -s2 * o.X + c * o.Z)
        Dim ld As New Vector3(c * dir.X + s2 * dir.Z, dir.Y, -s2 * dir.X + c * dir.Z)

        Dim tmin = 0.0F, tmax = MAX_RANGE_M
        For a = 0 To 2
            Dim od As Single, oo As Single, lo_a As Single, hi_a As Single
            Select Case a
                Case 0 : od = ld.X : oo = lo.X : lo_a = bmin.X : hi_a = bmax.X
                Case 1 : od = ld.Y : oo = lo.Y : lo_a = bmin.Y : hi_a = bmax.Y
                Case Else : od = ld.Z : oo = lo.Z : lo_a = bmin.Z : hi_a = bmax.Z
            End Select

            If Math.Abs(od) < 1.0E-6F Then
                If oo < lo_a OrElse oo > hi_a Then Return -1.0F
                Continue For
            End If
            Dim t1 = (lo_a - oo) / od
            Dim t2 = (hi_a - oo) / od
            If t1 > t2 Then
                Dim tmp = t1
                t1 = t2
                t2 = tmp
            End If
            If t1 > tmin Then tmin = t1
            If t2 < tmax Then tmax = t2
            If tmin > tmax Then Return -1.0F
        Next
        Return If(tmin > 0.01F, tmin, -1.0F)
    End Function

    ''' <summary>Where a vehicle is standing this frame. Set by the renderer
    ''' before a cast, because the shuttle moves it and the box has to be
    ''' where the tank is, not where it was parked.</summary>
    Public Shared Function shot_position(inst As TankInstance) As Vector3
        Return inst.livePosition
    End Function
End Class

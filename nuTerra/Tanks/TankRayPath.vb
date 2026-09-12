Imports OpenTK.Mathematics

''' <summary>
''' THE RAY RESOLVER - find every way to the enemy flag by casting rays and
''' going round what they hit.
'''
''' The owner's algorithm, said several ways over an afternoon before it landed:
''' "start looking left in the hunt sweep", "draw the tangent expanding ring at
''' each collision point", "just draw the rays each frame", "sampling to may
''' rays", and finally "Rays. remember?"
'''
''' HOW IT WORKS. Cast a ray from where you are straight at the flag. If it
''' arrives, that is a path. If it hits something, open an expanding ring at the
''' COLLISION POINT and sweep for the angles that clear - LEFT FIRST - and each
''' one that clears is a new branch cast from the hit. Recurse. A branch that
''' reaches the flag is a route; a branch that runs out of ring is a dead end
''' and stays on the picture in its own colour.
'''
''' WHY IT IS NOT A GRID SEARCH, which is what this was before and what the
''' owner kept telling me it should not be. A* over the cells expanded 45,000
''' of them for ONE route and flooded half the map - 466,229 for a catalogue -
''' because a grid search has no notion of "straight at it" and must consider
''' every cell between here and there. A ray has exactly one direction and only
''' stops where something is actually in the way. The whole map between two
''' obstacles costs one march.
'''
''' THE COST IS THE POINT. A ray across monastery is about 600 half-cell steps
''' and a resolve casts a few hundred rays. That is two orders of magnitude
''' under the grid search it replaces, which is what makes it something you can
''' watch happen rather than something you wait for.
'''
''' WHAT A NODE IS. Where a ray stopped, plus the direction it was going when it
''' stopped. Paths are sequences of these - a list of turning points, not a list
''' of cells - so a route comes out as the handful of corners a tank actually
''' has to steer round, already thinned by construction. There is no separate
''' thinning pass and nothing to get wrong in one.
''' </summary>
Public Class TankRayPath

    Public Structure Node
        Public at As Vector2            ' where this ray started
        Public dir As Vector2           ' unit, the way it was pointed
        Public hit As Vector2           ' where it stopped
        Public parent As Integer        ' node it branched from, -1 at the root
        Public reached As Boolean       ' did this ray make the flag
        Public dead As Boolean          ' did it run out of ring
        Public cost As Single           ' metres travelled to the end of this ray
    End Structure

    Public ReadOnly nodes As New List(Of Node)

    ''' <summary>Every distinct way in, each a list of turning points from the
    ''' start to the flag.</summary>
    Public ReadOnly paths As New List(Of List(Of Vector2))

    Public ready As Boolean = False
    Public why_stopped As String = ""
    Public rays_cast As Integer = 0

    ''' <summary>How far a ray steps as it marches, as a fraction of a cell.
    ''' Half means nothing thinner than a cell can be stepped over.</summary>
    Private Const STEP_FRAC As Single = 0.5F

    ''' <summary>How close to the flag counts as arrived.</summary>
    Private Const REACH_M As Single = 12.0F

    ''' <summary>The expanding ring at a collision: how far it opens and in what
    ''' steps. Twelve degrees a ring out to 156 - past square to the obstacle,
    ''' which is as far as going round it can sensibly mean before the way round
    ''' is behind you.</summary>
    Private Const RING_STEP_RAD As Single = 0.2094F
    Private Const RING_MAX As Integer = 13

    ''' <summary>How many branches one collision may spawn. A ring that clears
    ''' at eight different angles is looking at open ground, not at a corner,
    ''' and casting all eight is how a resolve turns back into a flood.</summary>
    Private Const BRANCH_MAX As Integer = 3

    ''' <summary>A ceiling on the whole resolve. Reached, it is reported rather
    ''' than hidden - a resolve that needs more than this has gone wrong in a
    ''' way a bigger number will not fix.</summary>
    Private Const RAY_BUDGET As Integer = 4000

    ''' <summary>Stop once this many ways in are known. The owner wants ALL of
    ''' them, and "all" on a map with two real corridors is two - but a ceiling
    ''' keeps a pathological map from running forever.</summary>
    Private Const PATH_MAX As Integer = 12

    ''' <summary>How far a candidate must travel to count as having cleared
    ''' something. Below this it has not gone round the obstacle, it has slid
    ''' along it.</summary>
    Private Const MIN_PROGRESS_M As Single = 8.0F

    ''' <summary>How coarsely "somewhere new" is judged, in metres. Two ray ends
    ''' inside one of these squares are the same idea arrived at twice.</summary>
    Private Const NEW_GROUND_M As Single = 18.0F

    ' been_set, NOT been. VB is case-insensitive, so a field named `been` and a
    ' function named `Been` are the SAME identifier and the compiler refuses
    ' both. Fourth time today the same blindness has bitten, in a fourth
    ' disguise: it shadowed BLOCKED, it aliased N to a loop variable, it made
    ' ToLowerInvariant always equal, and now it collides a field with its own
    ' accessor.
    Private been_set As HashSet(Of Integer)

    Private Function Been(nav As TankNav, p As Vector2) As Boolean
        Return been_set IsNot Nothing AndAlso been_set.Contains(Bucket(nav, p))
    End Function

    Private Sub Mark(nav As TankNav, p As Vector2)
        If been_set IsNot Nothing Then been_set.Add(Bucket(nav, p))
    End Sub

    Private Shared Function Bucket(nav As TankNav, p As Vector2) As Integer
        Dim bx = CInt(Math.Floor((p.X - nav.wx_min) / NEW_GROUND_M))
        Dim bz = CInt(Math.Floor((p.Y - nav.wz_min) / NEW_GROUND_M))
        Return bz * 4096 + bx
    End Function

    ''' <summary>How far a ray WOULD travel, without recording a node for it.
    ''' Used to find the tangent: the cheapest way to ask whether an angle
    ''' actually clears the obstacle is to try it.</summary>
    Private Shared Function March(nav As TankNav, hull_r_m As Single,
                                  from As Vector2, dir As Vector2,
                                  goal As Vector2) As Single
        Dim step_m = nav.cell_m * STEP_FRAC
        Dim p = from
        Dim travelled = 0.0F
        Dim limit = (nav.wx_max - nav.wx_min) * 1.5F
        While travelled < limit
            If (goal - p).Length <= REACH_M Then Exit While
            Dim nxt = p + dir * step_m
            If Not Standable(nav, hull_r_m, nxt) Then Exit While
            p = nxt
            travelled += step_m
        End While
        Return travelled
    End Function

    Public Sub Resolve(nav As TankNav, hull_r_m As Single,
                       sx As Single, sz As Single,
                       gx As Single, gz As Single,
                       label As String)
        ready = False
        nodes.Clear()
        paths.Clear()
        rays_cast = 0
        why_stopped = ""
        If nav Is Nothing OrElse Not nav.ready OrElse nav.clear_m Is Nothing Then
            LogThis("tank rays: {0} - no navigation grid", label)
            Return
        End If

        Dim t0 = Date.UtcNow

        ' BOTH ENDS MUST BE GROUND A HULL FITS ON, or the first ray never
        ' leaves. Measured before this existed: the resolve cast ONE ray and
        ' stopped - the base centre has 2.39 m of clearance against a 4.5 m
        ' hull, so the ray could not take a single step, and every direction of
        ' the ring out of that spot was blocked for the same reason. It read as
        ' "every branch is spent", which was true and completely misleading.
        Dim start = SnapFree(nav, hull_r_m, New Vector2(sx, sz))
        Dim goal = SnapFree(nav, hull_r_m, New Vector2(gx, gz))
        Dim s_moved = (start - New Vector2(sx, sz)).Length
        Dim g_moved = (goal - New Vector2(gx, gz)).Length
        If s_moved > 0.0F OrElse g_moved > 0.0F Then
            LogThis("tank rays: {0} - ends moved to standable ground: start {1:0.0} m, goal {2:0.0} m",
                    label, s_moved, g_moved)
        End If
        If Not Standable(nav, hull_r_m, start) OrElse Not Standable(nav, hull_r_m, goal) Then
            LogThis("tank rays: {0} - no ground a {1:0.0} m hull fits on near an end",
                    label, hull_r_m)
            Return
        End If

        ' Open with one ray straight at the flag. Everything else in this
        ' resolve is a consequence of something that one hit.
        been_set = New HashSet(Of Integer)
        Mark(nav, start)
        Dim open As New List(Of Integer)
        PushRay(nav, hull_r_m, start, Norm(goal - start), -1, 0.0F, goal, open)

        While open.Count > 0 AndAlso rays_cast < RAY_BUDGET AndAlso paths.Count < PATH_MAX

            ' Take the branch that has got NEAREST the flag for what it has
            ' spent. Best-first, so the resolve pushes toward the goal instead
            ' of breadth-firsting into every corner of the map.
            Dim bi = 0
            Dim best = Single.MaxValue
            For k = 0 To open.Count - 1
                Dim nd = nodes(open(k))
                Dim f = nd.cost + (goal - nd.hit).Length
                If f < best Then best = f : bi = k
            Next
            Dim ni = open(bi)
            open.RemoveAt(bi)
            Dim n = nodes(ni)

            If n.reached Then
                paths.Add(Pull(nav, hull_r_m, Unwind(ni)))
                Continue While
            End If

            ' THE EXPANDING RING AT THE COLLISION POINT. Sweep out from the
            ' direction that just failed, LEFT FIRST, and take the angles that
            ' clear. The first ring that yields anything is the tangent - going
            ' any wider is going round the long way for no reason - so the
            ' sweep stops at the first ring that produces a branch.
            ' A TANGENT IS AN ANGLE THAT GETS SOMEWHERE, not merely one that
            ' is standable where it starts.
            '
            ' Taking the first standable angle was wrong and the measurement was
            ' brutal about it: 4,000 rays, 0 paths. Twelve degrees off a wall
            ' you are facing is still into the wall two metres later, so the
            ' resolve ground along the face of every obstacle in 2 m hops and
            ' spent its whole budget without going anywhere.
            '
            ' So each candidate angle is TRIED - marched, not just tested at its
            ' start - and only kept if it travels far enough to have cleared
            ' something. The smallest angle that does is the tangent, which is
            ' exactly what opening a ring at the collision point is supposed to
            ' find.
            ' SWEEP THE WHOLE RING, THEN CHOOSE - do not take the first
            ' angle that works.
            '
            ' Stopping at the first productive ring was still too greedy: if 24
            ' degrees buys eight metres, the wide swing at ninety is never tried
            ' - and going round a corner usually IS the wide swing. 368 rays, 0
            ' paths, every branch spent, because every branch shuffled sideways
            ' rather than turning.
            '
            ' So every angle is tried, and the branches kept are the NEAREST
            ' CLEAR ANGLE ON EACH SIDE plus whichever single angle travelled
            ' furthest. Left, right, and the long way - three genuinely
            ' different ideas rather than three shades of one.
            Dim bestL = -1, bestR = -1, bestFar = -1
            Dim farthest = 0.0F
            Dim angL(RING_MAX * 2) As Single, angR(RING_MAX * 2) As Single
            Dim gotL(RING_MAX * 2) As Single, gotR(RING_MAX * 2) As Single
            Dim nL = 0, nR = 0
            Dim farAng = 0.0F

            For ring = 1 To RING_MAX
                Dim ang = RING_STEP_RAD * ring
                For side = 0 To 1
                    Dim sgn = If(side = 0, 1.0F, -1.0F)      ' LEFT first
                    Dim d2 = Rotate(n.dir, ang * sgn)
                    Dim from2 = n.hit + d2 * (nav.cell_m * 1.5F)
                    If Not Standable(nav, hull_r_m, from2) Then Continue For
                    Dim got = March(nav, hull_r_m, from2, d2, goal)
                    If got < MIN_PROGRESS_M Then Continue For

                    If sgn > 0.0F Then
                        If nL = 0 Then angL(0) = ang * sgn : gotL(0) = got : nL = 1
                    Else
                        If nR = 0 Then angR(0) = ang * sgn : gotR(0) = got : nR = 1
                    End If
                    If got > farthest Then farthest = got : farAng = ang * sgn
                Next
            Next

            Dim spawned = 0
            For pick = 0 To 2
                Dim a2 As Single
                If pick = 0 AndAlso nL > 0 Then
                    a2 = angL(0)
                ElseIf pick = 1 AndAlso nR > 0 Then
                    a2 = angR(0)
                ElseIf pick = 2 AndAlso farthest > 0.0F Then
                    a2 = farAng
                Else
                    Continue For
                End If
                Dim d3 = Rotate(n.dir, a2)
                Dim from3 = n.hit + d3 * (nav.cell_m * 1.5F)
                Dim got3 = March(nav, hull_r_m, from3, d3, goal)
                If got3 < MIN_PROGRESS_M Then Continue For
                Dim endp = from3 + d3 * got3
                If Been(nav, endp) Then Continue For
                Mark(nav, endp)
                PushRay(nav, hull_r_m, from3, d3, ni, n.cost, goal, open)
                spawned += 1
            Next

            If spawned = 0 Then
                Dim dn = nodes(ni)
                dn.dead = True
                nodes(ni) = dn
            End If
        End While

        If paths.Count >= PATH_MAX Then
            why_stopped = "hit the path ceiling"
        ElseIf rays_cast >= RAY_BUDGET Then
            why_stopped = "ran out of ray budget"
        Else
            why_stopped = "every branch is spent"
        End If

        ready = True
        LogThis("tank rays: {0} - {1} path(s) from {2} ray(s) in {3:0} ms, stopped because {4}",
                label, paths.Count, rays_cast,
                (Date.UtcNow - t0).TotalMilliseconds, why_stopped)
        For i = 0 To paths.Count - 1
            ' THE GAP, NOT THE CLEARANCE. Clearance is the distance to the
            ' nearest blocked cell - HALF the width of the gap you are standing
            ' in. Reporting one as the other halves every width in the log,
            ' which is exactly the sort of quiet factor of two that gets
            ' designed around.
            Dim tight = 0.0F
            Dim tight_at As Vector2 = Nothing
            Dim narrow = 0
            GapAlong(nav, paths(i), tight, tight_at, narrow)
            LogThis("tank rays:   {0}: {1} turn(s), {2:0} m, tightest gap {3:0.0} m at ({4:0}, {5:0}), {6} sample(s) under 3 m",
                    i, paths(i).Count - 2, PathLen(paths(i)),
                    tight, tight_at.X, tight_at.Y, narrow)
        Next
    End Sub

    ''' <summary>
    ''' March a ray and record where it got to.
    '''
    ''' THE WHOLE SAVING IS HERE. A grid search asks "what about this cell?" a
    ''' hundred thousand times; a ray asks "is it still clear?" along one line
    ''' and crosses open ground in a single march. Half a cell a step, so
    ''' nothing narrower than a cell can be stepped over.
    ''' </summary>
    Private Sub PushRay(nav As TankNav, hull_r_m As Single,
                        from As Vector2, dir As Vector2,
                        parent As Integer, cost0 As Single,
                        goal As Vector2, open As List(Of Integer))
        rays_cast += 1
        Dim step_m = nav.cell_m * STEP_FRAC
        Dim p = from
        Dim travelled = 0.0F
        Dim reached = False

        ' A ray runs until it is stopped or it arrives. The cap is the map's own
        ' diagonal, so a ray can cross anything without running forever.
        Dim limit = (nav.wx_max - nav.wx_min) * 1.5F
        While travelled < limit
            If (goal - p).Length <= REACH_M Then
                reached = True
                Exit While
            End If
            Dim nxt = p + dir * step_m
            If Not Standable(nav, hull_r_m, nxt) Then Exit While
            p = nxt
            travelled += step_m
        End While

        Dim n As Node
        n.at = from
        n.dir = dir
        n.hit = p
        n.parent = parent
        n.reached = reached
        n.dead = False
        n.cost = cost0 + travelled
        nodes.Add(n)
        open.Add(nodes.Count - 1)
    End Sub

    ''' <summary>The nearest point a hull fits on, walking outward in rings of
    ''' cells. Returns the point unchanged if it is already good.</summary>
    Private Shared Function SnapFree(nav As TankNav, hull_r_m As Single,
                                     p As Vector2) As Vector2
        If Standable(nav, hull_r_m, p) Then Return p
        Dim cx, cz As Integer
        nav.CellOf(p.X, p.Y, cx, cz)
        For ring = 1 To 64
            For dz = -ring To ring
                For dx = -ring To ring
                    If Math.Abs(dx) <> ring AndAlso Math.Abs(dz) <> ring Then Continue For
                    Dim nx = cx + dx, nz = cz + dz
                    If Not nav.InBounds(nx, nz) Then Continue For
                    If nav.clear_m(nz * TankNav.SIZE + nx) >= hull_r_m Then
                        Return nav.CentreOf(nx, nz)
                    End If
                Next
            Next
        Next
        Return p
    End Function

    Private Shared Function Standable(nav As TankNav, hull_r_m As Single,
                                      p As Vector2) As Boolean
        Dim cx, cz As Integer
        nav.CellOf(p.X, p.Y, cx, cz)
        If Not nav.InBounds(cx, cz) Then Return False
        Return nav.clear_m(cz * TankNav.SIZE + cx) >= hull_r_m
    End Function

    ''' <summary>
    ''' STRING-PULL the corners out of a path.
    '''
    ''' A ray resolve takes whatever tangent cleared, so the raw path wanders:
    ''' 24 turns and 1,496 m where the ground allows 855. Every one of those
    ''' corners was forced by an obstacle that a LATER corner has already got
    ''' past, so most of them are no longer needed by the time the path is
    ''' finished.
    '''
    ''' Walk forward from each kept point and take the FURTHEST point still
    ''' reachable in a straight line. What is left are the corners the ground
    ''' actually requires. It is the same clearance test the rays used, so the
    ''' pulled path is drivable by exactly the standard the search applied -
    ''' there is no second notion of passable to disagree with the first.
    ''' </summary>
    Private Shared Function Pull(nav As TankNav, hull_r_m As Single,
                                 p As List(Of Vector2)) As List(Of Vector2)
        If p.Count < 3 Then Return p
        Dim out As New List(Of Vector2)
        out.Add(p(0))
        Dim i = 0
        While i < p.Count - 1
            Dim best = i + 1
            For j = p.Count - 1 To i + 1 Step -1
                If Clear(nav, hull_r_m, p(i), p(j)) Then
                    best = j
                    Exit For
                End If
            Next
            out.Add(p(best))
            i = best
        End While
        Return out
    End Function

    ''' <summary>Is the straight line between two points drivable? Marched at
    ''' half a cell, the same as a ray, so nothing narrower than a cell is
    ''' stepped over.</summary>
    Private Shared Function Clear(nav As TankNav, hull_r_m As Single,
                                  a As Vector2, b As Vector2) As Boolean
        Dim d = b - a
        Dim len = d.Length
        If len < 1.0E-4F Then Return True
        Dim dir = d / len

        ' STRICTER THAN THE SEARCH IT IS SMOOTHING, and finer.
        '
        ' The rays validated their own path cell by cell. A smoothing pass that
        ' samples a NEW straight line at its own step size can clip the corner
        ' of a cell neither the old path nor this sampling happened to land on -
        ' and it will always be by a fraction of a cell, which is the size of
        ' error nobody goes looking for.
        '
        ' Measured before this: the tightest gap on a finished path came out
        ' 8.5 m, where a 4.5 m hull radius needs 9.0 and the search accepts
        ' nothing under it. The rays found a valid path and the smoothing made
        ' it marginally invalid.
        '
        ' So the pull asks for half a cell MORE room than the hull needs and
        ' steps at a quarter cell rather than a half. A smoother may refuse a
        ' corner it could have taken; it may never approve one it could not.
        Dim need = hull_r_m + nav.cell_m * 0.5F
        Dim step_m = nav.cell_m * 0.25F
        Dim t = 0.0F
        While t < len
            If Not Standable(nav, need, a + dir * t) Then Return False
            t += step_m
        End While
        Return Standable(nav, need, b)
    End Function

    ''' <summary>The turning points from the start to this node.</summary>
    Private Function Unwind(i As Integer) As List(Of Vector2)
        Dim out As New List(Of Vector2)
        Dim k = i
        out.Add(nodes(k).hit)
        While k >= 0
            out.Add(nodes(k).at)
            k = nodes(k).parent
        End While
        out.Reverse()
        Return out
    End Function

    ''' <summary>
    ''' Walk a path and report the narrowest GAP on it, where it is, and how
    ''' many samples are under three metres.
    '''
    ''' Gap is twice the clearance: clearance measures to the nearest blocked
    ''' cell, so a hull standing in a corridor has walls that distance away on
    ''' BOTH sides. Three metres is the number the owner asked to count - under
    ''' it nothing on this map drives through, so a path that reports any is a
    ''' path the grid believes in and the ground does not.
    ''' </summary>
    Private Shared Sub GapAlong(nav As TankNav, p As List(Of Vector2),
                                ByRef tightest As Single,
                                ByRef where As Vector2,
                                ByRef under3 As Integer)
        tightest = Single.MaxValue
        under3 = 0
        where = New Vector2(0.0F, 0.0F)
        Dim step_m = nav.cell_m * STEP_FRAC
        For i = 1 To p.Count - 1
            Dim d = p(i) - p(i - 1)
            Dim len = d.Length
            If len < 1.0E-4F Then Continue For
            Dim dir = d / len
            Dim t = 0.0F
            While t <= len
                Dim q = p(i - 1) + dir * t
                Dim cx, cz As Integer
                nav.CellOf(q.X, q.Y, cx, cz)
                If nav.InBounds(cx, cz) Then
                    Dim gap = nav.clear_m(cz * TankNav.SIZE + cx) * 2.0F
                    If gap < tightest Then
                        tightest = gap
                        where = q
                    End If
                    If gap < 3.0F Then under3 += 1
                End If
                t += step_m
            End While
        Next
        If tightest = Single.MaxValue Then tightest = 0.0F
    End Sub

    Private Shared Function PathLen(p As List(Of Vector2)) As Single
        Dim d = 0.0F
        For i = 1 To p.Count - 1
            d += (p(i) - p(i - 1)).Length
        Next
        Return d
    End Function

    Private Shared Function Norm(v As Vector2) As Vector2
        Dim l = v.Length
        If l < 1.0E-6F Then Return New Vector2(0.0F, 1.0F)
        Return v / l
    End Function

    Private Shared Function Rotate(v As Vector2, a As Single) As Vector2
        Dim c = CSng(Math.Cos(a)), s = CSng(Math.Sin(a))
        Return New Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c)
    End Function
End Class

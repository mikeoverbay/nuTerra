''' <summary>
''' THE ROUTE CATALOGUE - every distinct way to the enemy base, found once at
''' load rather than discovered by driving.
'''
''' The owner's algorithm, in his words: "Rule one. seek base. Rule 2 seek
''' using path finders algo. Rule 3. Tag that path as used and try path. When
''' we cant find a way there, we are done. Save every path. That needs to be
''' permently in mem at startup unless we want to watch tanks drive for 200
''' years."
'''
''' So: A* to the goal, save the route, ERASE it, A* again, until the search
''' fails. The failure is the terminator - not a count, not a timeout. What
''' comes out is the independent corridors into that base.
'''
''' WHAT "ERASE" HAS TO MEAN, because getting this wrong is the whole game.
''' Erase a route as a LINE and the next search runs alongside it, a metre
''' over, and you get four hundred near-identical routes and no termination
''' worth having. A route is consumed at the width it actually occupies: every
''' cell on it claims the ground within its own CLEARANCE, so a pass across a
''' field eats the field and a pass down an alley eats the alley. That is what
''' makes "no path" mean there is no remaining gap a hull fits down.
'''
''' THE ENDS ARE NEVER CONSUMED. The start is where the tank stands and the
''' goal is the base; erasing either ends the search after one route by
''' destroying the thing being searched between. A guard radius around both is
''' left alone.
'''
''' THIS RAN ON A DISC GRAPH AND NO LONGER DOES. The zone map was built, used,
''' measured and removed: it cost 770 ms to build to save 33 ms of routing, and
''' its per-step test was 9x faster than CanStand while rejecting 44% of
''' genuinely drivable ground - a point in a disc's outer ring fails the radius
''' test while a hull stands there perfectly well. The Path Studio session
''' measured the same failure from the camera end, where it made the shipped
''' route longer with four times the backups.
'''
''' What survived is the clearance field the discs were built on, which now
''' lives in TankNav. A cell is passable for a hull iff its clearance clears
''' the hull: one array read where CanStand tests about fifty cells. That
''' equivalence is the whole reason searching a million-cell grid is affordable
''' and is what made the discs unnecessary rather than what replaced them.
'''
''' IT INHERITS THE MASK, AND THE GRID'S RESOLUTION. TankNav coarsens the
''' 0.171 m bake to a 1.37 m cell by blocking a cell if ANY texel in it is
''' blocked. Conservative, and right for "may a hull stand here" - but a
''' bottleneck search depends on precisely the narrow LINKS that conservative
''' coarsening eats. Measured at both resolutions by the nuTerra session: this
''' grid finds no route between the monastery bases at 8 m clearance, where at
''' full resolution the corridor is 16.06 m wide at its tightest and 16% of
''' texels clear 8.03 m. The rooms survive and the doors do not.
'''
''' So a route this FINDS is real, and one it does not find may still exist.
''' The corridor count is a LOWER BOUND, and any WIDTH claim must be measured
''' on the bake rather than read off this grid.
''' </summary>
Public Class TankRoutes

    Public Structure Route
        ''' <summary>Cell indices, start first and goal last.</summary>
        Public cells() As Integer
        ''' <summary>Path length in metres.</summary>
        Public length_m As Single
        ''' <summary>The least room anywhere on it, in metres. A LOWER BOUND at
        ''' this grid's resolution - see the class note.</summary>
        Public min_clear_m As Single
    End Structure

    Public ReadOnly routes As New List(Of Route)
    Public ready As Boolean = False

    ''' <summary>Why the search stopped, for the log and for whoever reads it
    ''' later. "no path" is the answer we want; anything else means the
    ''' catalogue is short for a reason worth knowing.</summary>
    Public why_stopped As String = ""

    ''' <summary>How long the FIRST search took on its own, apart from the
    ''' erase passes and every later search.</summary>
    Public first_ms As Double = 0.0

    ''' <summary>A ceiling, not a target. The search should end because the map
    ''' ran out of corridors; if it ever ends here instead, the log says so and
    ''' the number is wrong rather than the map.</summary>
    Private Const MAX_ROUTES As Integer = 64

    ''' <summary>How much ground around each end is spared by the erase. About
    ''' a hull length: enough that the next search can still leave the start and
    ''' reach the goal, not so much that it leaves a free stub every search
    ''' reuses.</summary>
    Private Const END_GUARD_M As Single = 12.0F

    Public Sub Build(nav As TankNav, hull_r_m As Single,
                     sx As Single, sz As Single,
                     gx As Single, gz As Single,
                     label As String)
        ready = False
        routes.Clear()
        why_stopped = ""
        first_ms = 0.0
        If nav Is Nothing OrElse Not nav.ready OrElse nav.clear_m Is Nothing Then
            why_stopped = "no navigation grid"
            LogThis("tank routes: {0} - no navigation grid", label)
            Return
        End If

        Dim t0 = Date.UtcNow
        Dim N = TankNav.SIZE

        Dim scx, scz, gcx, gcz As Integer
        nav.CellOf(sx, sz, scx, scz)
        nav.CellOf(gx, gz, gcx, gcz)
        If Not nav.InBounds(scx, scz) OrElse Not nav.InBounds(gcx, gcz) Then
            why_stopped = "an end is off the grid"
            LogThis("tank routes: {0} - an end is off the grid", label)
            Return
        End If

        ' AIM AT GROUND A HULL FITS ON, NOT AT THE MARK. Neither ctf base centre
        ' on monastery is usable - measured off the bake at 0.171 m, team 1 has
        ' 3.78 m of clearance and team 2 has 2.39 m, which is 4.78 m of width
        ' for a 4.5 m hull. Both are plain terrain, so it is obstacles a couple
        ' of metres away rather than anything built on the mark.
        '
        ' Without this the goal CELL is impassable, the search can never arrive,
        ' and it reports no path after expanding most of the map - which reads
        ' as "there is no way to the base" when the truth is "you aimed two
        ' metres off one".
        Dim s_off = SnapFree(nav, hull_r_m, N, scx, scz)
        Dim g_off = SnapFree(nav, hull_r_m, N, gcx, gcz)
        If s_off < 0 OrElse g_off < 0 Then
            why_stopped = "an end has no standable ground near it"
            LogThis("tank routes: {0} - no ground a {1:0.0} m hull fits on within reach of an end",
                    label, hull_r_m)
            Return
        End If
        If s_off > 0 OrElse g_off > 0 Then
            LogThis("tank routes: {0} - aimed at standable ground: start {1} cell(s) off the mark, goal {2}",
                    label, s_off, g_off)
        End If

        Dim start = scz * N + scx
        Dim goal = gcz * N + gcx
        Dim eaten(N * N - 1) As Boolean

        Do
            Dim ta = Date.UtcNow
            Dim path = AStar(nav, hull_r_m, eaten, start, goal, N)
            If routes.Count = 0 Then first_ms = (Date.UtcNow - ta).TotalMilliseconds
            If path Is Nothing Then
                why_stopped = "no path"
                Exit Do
            End If

            Dim r As Route
            r.cells = path
            r.length_m = 0.0F
            r.min_clear_m = Single.MaxValue
            For i = 0 To path.Length - 1
                Dim cl = nav.clear_m(path(i))
                If cl < r.min_clear_m Then r.min_clear_m = cl
                If i > 0 Then r.length_m += StepLen(path(i - 1), path(i), N, nav.cell_m)
            Next
            routes.Add(r)

            If routes.Count >= MAX_ROUTES Then
                why_stopped = "hit the route ceiling"
                Exit Do
            End If

            ' ERASE AT CORRIDOR WIDTH. Each cell claims the ground within its
            ' own clearance, so a pass eats the width it travelled through
            ' rather than a one-cell thread. The ends are spared, or the next
            ' search has nowhere to start from.
            Dim guard_c = END_GUARD_M / Math.Max(nav.cell_m, 0.001F)
            Dim guard2 = guard_c * guard_c
            Dim killed = 0
            For i = 0 To path.Length - 1
                Dim ci = path(i)
                Dim cx = ci Mod N, cz = ci \ N
                If NearCell(cx, cz, scx, scz, guard2) Then Continue For
                If NearCell(cx, cz, gcx, gcz, guard2) Then Continue For
                Dim rc = CInt(Math.Floor(nav.clear_m(ci) / Math.Max(nav.cell_m, 0.001F)))
                If rc < 1 Then rc = 1
                Dim rc2 = rc * rc
                For dz = -rc To rc
                    Dim rr = cz + dz
                    If rr < 0 OrElse rr >= N Then Continue For
                    For dx = -rc To rc
                        If dx * dx + dz * dz > rc2 Then Continue For
                        Dim cc = cx + dx
                        If cc < 0 OrElse cc >= N Then Continue For
                        Dim pi = rr * N + cc
                        If Not eaten(pi) Then
                            eaten(pi) = True
                            killed += 1
                        End If
                    Next
                Next
            Next
            If killed = 0 Then
                why_stopped = "erasing a route consumed nothing - it would repeat for ever"
                Exit Do
            End If
        Loop

        ready = True
        Dim ms = (Date.UtcNow - t0).TotalMilliseconds
        LogThis("tank routes: {0} - {1} route(s) in {2:0} ms, stopped because {3}",
                label, routes.Count, ms, why_stopped)
        For i = 0 To routes.Count - 1
            LogThis("tank routes:   {0}: {1:0} m, least room {2:0.0} m over {3} cell(s)",
                    i, routes(i).length_m, routes(i).min_clear_m, routes(i).cells.Length)
        Next
    End Sub

    Private Shared Function NearCell(ax As Integer, az As Integer,
                                     bx As Integer, bz As Integer,
                                     r2 As Single) As Boolean
        Dim dx = CSng(ax - bx), dz = CSng(az - bz)
        Return dx * dx + dz * dz <= r2
    End Function

    Private Shared Function StepLen(a As Integer, b As Integer,
                                    N As Integer, cm As Single) As Single
        Dim ax = a Mod N, az = a \ N
        Dim bx = b Mod N, bz = b \ N
        If ax <> bx AndAlso az <> bz Then Return cm * 1.41421356F
        Return cm
    End Function

    ''' <summary>The nearest cell that clears the hull, walking outward in
    ''' rings. Returns how many rings out it had to go, or -1 if nothing within
    ''' reach does - so a silly answer is visible rather than silent.</summary>
    Private Shared Function SnapFree(nav As TankNav, hull_r_m As Single, N As Integer,
                                     ByRef cx As Integer, ByRef cz As Integer) As Integer
        If nav.clear_m(cz * N + cx) >= hull_r_m Then Return 0
        For ring = 1 To 64
            For dz = -ring To ring
                For dx = -ring To ring
                    If Math.Abs(dx) <> ring AndAlso Math.Abs(dz) <> ring Then Continue For
                    Dim nx = cx + dx, nz = cz + dz
                    If nx < 0 OrElse nz < 0 OrElse nx >= N OrElse nz >= N Then Continue For
                    If nav.clear_m(nz * N + nx) >= hull_r_m Then
                        cx = nx : cz = nz
                        Return ring
                    End If
                Next
            Next
        Next
        Return -1
    End Function

    ''' <summary>
    ''' A* over the cells, skipping anything eaten by an earlier route.
    '''
    ''' A cell is passable iff its CLEARANCE clears the hull: one array read,
    ''' exactly equivalent to CanStand's swept test over about fifty cells.
    '''
    ''' 8-connected with an octile heuristic, and a BINARY HEAP for the open
    ''' set. The heap is not a detail. The first version scanned every open node
    ''' for the cheapest, took 23 ms where this takes 1.5, and made a benchmark
    ''' report the opposite of the truth - it had the disc graph losing to the
    ''' grid when what was being compared was a scan against a heap.
    ''' </summary>
    Private Shared Function AStar(nav As TankNav, hull_r_m As Single,
                                  eaten() As Boolean,
                                  start As Integer, goal As Integer,
                                  N As Integer) As Integer()
        Dim total = N * N
        Dim g(total - 1) As Single
        Dim came(total - 1) As Integer
        Dim shut(total - 1) As Boolean
        For i = 0 To total - 1
            g(i) = Single.MaxValue
            came(i) = -1
        Next

        Dim cm = nav.cell_m
        Dim gx = goal Mod N, gz = goal \ N

        Dim hf(1023) As Single
        Dim hc(1023) As Integer
        Dim hn = 0
        g(start) = 0.0F
        hf(0) = Oct(start Mod N, start \ N, gx, gz, cm) : hc(0) = start : hn = 1

        Dim DX() As Integer = {1, -1, 0, 0, 1, 1, -1, -1}
        Dim DZ() As Integer = {0, 0, 1, -1, 1, -1, 1, -1}

        While hn > 0
            Dim cur = hc(0)
            hn -= 1
            hf(0) = hf(hn) : hc(0) = hc(hn)
            Dim hi = 0
            While True
                Dim l = hi * 2 + 1, r = l + 1, sm = hi
                If l < hn AndAlso hf(l) < hf(sm) Then sm = l
                If r < hn AndAlso hf(r) < hf(sm) Then sm = r
                If sm = hi Then Exit While
                Dim tf = hf(hi) : hf(hi) = hf(sm) : hf(sm) = tf
                Dim tc = hc(hi) : hc(hi) = hc(sm) : hc(sm) = tc
                hi = sm
            End While

            If shut(cur) Then Continue While
            shut(cur) = True
            If cur = goal Then Exit While

            Dim cx = cur Mod N, cz = cur \ N
            For d = 0 To 7
                Dim nx = cx + DX(d), nz = cz + DZ(d)
                If nx < 0 OrElse nz < 0 OrElse nx >= N OrElse nz >= N Then Continue For
                Dim ni = nz * N + nx
                If shut(ni) OrElse eaten(ni) Then Continue For
                If nav.clear_m(ni) < hull_r_m Then Continue For
                Dim step_m = If(d < 4, cm, cm * 1.41421356F)
                Dim tentative = g(cur) + step_m
                If tentative >= g(ni) Then Continue For
                came(ni) = cur
                g(ni) = tentative
                If hn >= hf.Length Then
                    ReDim Preserve hf(hf.Length * 2 - 1)
                    ReDim Preserve hc(hc.Length * 2 - 1)
                End If
                Dim k = hn
                hf(k) = tentative + Oct(nx, nz, gx, gz, cm) : hc(k) = ni
                hn += 1
                While k > 0
                    Dim par = (k - 1) \ 2
                    If hf(par) <= hf(k) Then Exit While
                    Dim tf2 = hf(par) : hf(par) = hf(k) : hf(k) = tf2
                    Dim tc2 = hc(par) : hc(par) = hc(k) : hc(k) = tc2
                    k = par
                End While
            Next
        End While

        If g(goal) = Single.MaxValue Then Return Nothing

        Dim out As New List(Of Integer)
        Dim c = goal
        Do While c >= 0
            out.Add(c)
            If c = start Then Exit Do
            c = came(c)
        Loop
        out.Reverse()
        If out.Count = 0 OrElse out(0) <> start Then Return Nothing
        Return out.ToArray()
    End Function

    ''' <summary>Octile distance, the admissible heuristic for 8-connected
    ''' movement - straight steps cost one cell, diagonals root two.</summary>
    Private Shared Function Oct(ax As Integer, az As Integer,
                                bx As Integer, bz As Integer, cm As Single) As Single
        Dim dx = Math.Abs(ax - bx), dz = Math.Abs(az - bz)
        Dim lo = Math.Min(dx, dz), hi = Math.Max(dx, dz)
        Return cm * (CSng(hi - lo) + 1.41421356F * lo)
    End Function
End Class

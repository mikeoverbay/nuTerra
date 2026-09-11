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
''' So: A* from start to goal over the zone graph, save the route, ERASE it,
''' A* again, until the search fails. The failure is the terminator - not a
''' count, not a timeout. What comes out is every independent corridor into
''' that base, and how many there are is the map's min-cut.
'''
''' WHAT "ERASE" HAS TO MEAN, because getting this wrong is the whole game.
''' Erase a route as a LINE and the next search runs alongside it, a metre
''' over, and you get four hundred near-identical routes and no termination
''' worth having. A route has to be consumed at the width it actually
''' occupies. Over a disc graph that falls out for free: paint the route's
''' discs into a consumed mask and drop every zone whose centre lands in it.
''' Each pass then eats a corridor rather than a thread, and "no path" means
''' there is no remaining gap a hull fits down.
'''
''' THE ENDS ARE NEVER CONSUMED. The start is where the tank stands and the
''' goal is the base; erasing either would end the search after one route by
''' destroying the thing being searched between. Only the INTERIOR of a route
''' is eaten, and a route with no interior - start and goal already touching -
''' is the last one there is to find.
'''
''' WHY THIS IS WORTH IT. A tank is handed a route instead of finding one, so
''' nothing searches at runtime. Lose a corridor to a wreck and you fall back
''' to catalogue entry 2 rather than re-planning. And it is fully
''' deterministic - no seeded RNG needed at all - which §6 of the handoff
''' wants, because two runs putting the same tank in the same place on the
''' same frame is the only reason two stills can be compared.
'''
''' IT INHERITS THE MASK. A catalogue asserts its routes with confidence and
''' hands them to thirty vehicles. Every route through a wrongly-free cell is
''' a route that does not exist, stated as though it does - which is worse
''' than a wandering tank meeting the same wall, because the wanderer learns
''' and the catalogue does not.
''' </summary>
Public Class TankRoutes

    Public Structure Route
        ''' <summary>Zone ids, start first and goal last.</summary>
        Public hops() As Integer
        ''' <summary>Centre-to-centre length in metres.</summary>
        Public length_m As Single
        ''' <summary>The narrowest disc on the route - what this corridor can
        ''' actually pass, as opposed to what the hull asked for.</summary>
        Public min_r_m As Single
    End Structure

    Public ReadOnly routes As New List(Of Route)
    Public ready As Boolean = False

    ''' <summary>Why the search stopped, for the log and for a human reading
    ''' it later. "no path" is the answer we want; anything else means the
    ''' catalogue is short for a reason worth knowing.</summary>
    Public why_stopped As String = ""

    ''' <summary>How long the FIRST A* took, on its own. The catalogue's total
    ''' includes the erase passes and every later search, so it is the wrong
    ''' number to hold against a single grid search.</summary>
    Public first_ms As Double = 0.0

    ''' <summary>A ceiling, not a target. The search is meant to end because
    ''' the map ran out of corridors; if it ever ends because of this instead,
    ''' the log says so and the number is wrong rather than the map.</summary>
    Private Const MAX_ROUTES As Integer = 64

    Public Sub Build(z As TankZones,
                     sx As Single, sz As Single,
                     gx As Single, gz As Single,
                     label As String)
        ready = False
        routes.Clear()
        why_stopped = ""
        If z Is Nothing OrElse Not z.ready OrElse z.zones.Count = 0 Then
            why_stopped = "no zone map"
            LogThis("tank routes: {0} - no zone map", label)
            Return
        End If

        Dim t0 = Date.UtcNow

        ' AIM AT THE DISC, NOT THE MARK. Neither ctf base centre on monastery is
        ' usable ground - measured off the bake at 0.171 m: team 1 has 3.78 m of
        ' clearance and team 2 has 2.39 m, which is 4.78 m of width for a 4.5 m
        ' hull, fourteen centimetres a side. Both texels are plain terrain, so it
        ' is obstacles a couple of metres away rather than anything built on the
        ' mark itself.
        '
        ' A base centre therefore may not be covered by any disc, and asking for
        ' a route to an uncovered point returns nothing - which reads as "there
        ' is no way to the base" when the truth is "you aimed at a spot two
        ' metres from one". The nearest disc is the honest target: a hull cannot
        ' stand on the mark anyway, and the ring is 50 m across.
        Dim start_off = 0.0F, goal_off = 0.0F
        Dim start = ZoneNear(z, sx, sz, start_off)
        Dim goal = ZoneNear(z, gx, gz, goal_off)
        If start < 0 OrElse goal < 0 Then
            why_stopped = "an end is not on drivable ground"
            LogThis("tank routes: {0} - start zone {1}, goal zone {2}; an end is in no disc and none is near",
                    label, start, goal)
            Return
        End If
        If start_off > 0.0F OrElse goal_off > 0.0F Then
            LogThis("tank routes: {0} - aimed at the nearest disc: start {1:0.0} m off the mark, goal {2:0.0} m",
                    label, start_off, goal_off)
        End If
        If start = goal Then
            why_stopped = "start and goal are the same zone"
            LogThis("tank routes: {0} - start and goal are both zone {1}", label, start)
            Return
        End If

        ' nz, NOT n. VB is case-insensitive: a local n IS the N holding the
        ' grid size. Declared explicitly the compiler refuses it, which is the
        ' friendly case - a FOR loop variable of the same name is silently
        ' reused instead, and that is how the same collision got into
        ' TankZones and made every array index ten times too large.
        Dim nz = z.zones.Count
        Dim dead(nz - 1) As Boolean
        Dim N = TankNav.SIZE
        Dim eaten(N * N - 1) As Boolean

        Do
            Dim ta = Date.UtcNow
            Dim hops = AStar(z, dead, start, goal)
            If routes.Count = 0 Then first_ms = (Date.UtcNow - ta).TotalMilliseconds
            If hops Is Nothing Then
                why_stopped = "no path"
                Exit Do
            End If

            Dim r As Route
            r.hops = hops
            r.length_m = 0.0F
            r.min_r_m = Single.MaxValue
            For i = 0 To hops.Length - 1
                If z.zones(hops(i)).r_m < r.min_r_m Then r.min_r_m = z.zones(hops(i)).r_m
                If i > 0 Then r.length_m += Sep(z, hops(i - 1), hops(i))
            Next
            routes.Add(r)

            If routes.Count >= MAX_ROUTES Then
                why_stopped = "hit the route ceiling"
                Exit Do
            End If

            ' THE INTERIOR ONLY. Painting the ends would destroy the very
            ' places the next search has to begin and end at.
            If hops.Length <= 2 Then
                why_stopped = "the last route has no interior to erase"
                Exit Do
            End If

            For i = 1 To hops.Length - 2
                PaintDisc(z, eaten, hops(i), N)
            Next

            ' Anything whose centre now stands in consumed ground is gone with
            ' it - that is what makes this eat a corridor rather than a thread.
            Dim killed = 0
            For k = 0 To nz - 1
                If dead(k) Then Continue For
                If k = start OrElse k = goal Then Continue For
                Dim zk = z.zones(k)
                If eaten(zk.cz * N + zk.cx) Then
                    dead(k) = True
                    killed += 1
                End If
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
            LogThis("tank routes:   {0}: {1} hop(s), {2:0} m, narrowest {3:0.0} m",
                    i, routes(i).hops.Length, routes(i).length_m, routes(i).min_r_m)
        Next
    End Sub

    ''' <summary>
    ''' The disc holding this point, or the nearest one if none does.
    '''
    ''' ZoneAt is a single array read and answers -1 off drivable ground. That
    ''' is the right answer for "where is this hull" and the wrong one for
    ''' "where am I trying to get to", because a destination is a place on a map
    ''' rather than a place a tank is standing - a base mark, a spotted
    ''' position, a waypoint someone clicked. Falling back to the nearest disc
    ''' turns "no route" into "a route to the closest ground you can actually
    ''' stand on", and reports how far that was so a silly answer is visible
    ''' rather than silent.
    '''
    ''' The scan is over every disc, which is fine: it runs twice per catalogue
    ''' and only when the point is not covered.
    ''' </summary>
    Private Shared Function ZoneNear(z As TankZones, x As Single, zz As Single,
                                     ByRef off_m As Single) As Integer
        off_m = 0.0F
        Dim direct = z.ZoneAt(x, zz)
        If direct >= 0 Then Return direct

        Dim best = -1
        Dim best_d2 = Single.MaxValue
        For i = 0 To z.zones.Count - 1
            Dim dx = z.zones(i).x - x
            Dim dz = z.zones(i).z - zz
            Dim d2 = dx * dx + dz * dz
            If d2 < best_d2 Then
                best_d2 = d2
                best = i
            End If
        Next
        If best >= 0 Then off_m = CSng(Math.Sqrt(best_d2)) - z.zones(best).r_m
        If off_m < 0.0F Then off_m = 0.0F
        Return best
    End Function

    ''' <summary>
    ''' A* straight over the CELLS, for comparison against the disc graph.
    '''
    ''' The owner's condition on the zone map: "if the discs do not aid AI or
    ''' path creation, we can toss them." This is what answers that, and it is
    ''' built to lose fairly - it uses TankZones' clearance field so a cell is
    ''' passable iff it clears the hull, which is precisely equivalent to
    ''' CanStand and costs one read instead of fifty. That matters, because the
    ''' honest question is not discs against nothing: the distance transform is
    ''' worth having either way, and it is only the DISCS that are on trial.
    '''
    ''' 8-connected, octile heuristic, binary heap. Returns path length in
    ''' metres, or -1 if there is none.
    ''' </summary>
    Public Shared Function GridAStar(z As TankZones, hull_r_m As Single,
                                     sx As Single, sz As Single,
                                     gx As Single, gz As Single,
                                     ByRef ms As Double,
                                     ByRef expanded As Integer) As Single
        Dim t0 = Date.UtcNow
        expanded = 0
        Dim N = TankNav.SIZE
        Dim cm = z.cell_m
        Dim x0 = z.frame_wx0, x1 = z.frame_wx1, z0 = z.frame_wz0, z1 = z.frame_wz1

        Dim scx = CInt(Math.Floor((sx - x0) / (x1 - x0) * N))
        Dim scz = CInt(Math.Floor((z1 - sz) / (z1 - z0) * N))
        Dim gcx = CInt(Math.Floor((gx - x0) / (x1 - x0) * N))
        Dim gcz = CInt(Math.Floor((z1 - gz) / (z1 - z0) * N))
        If scx < 0 OrElse scz < 0 OrElse scx >= N OrElse scz >= N Then Return -1.0F
        If gcx < 0 OrElse gcz < 0 OrElse gcx >= N OrElse gcz >= N Then Return -1.0F

        ' SNAP BOTH ENDS TO PASSABLE GROUND, or this measures the wrong thing.
        ' Neither base centre clears a 4.5 m hull - team 2 has 2.39 m - so the
        ' goal CELL is impassable and the search can never arrive, reporting no
        ' path after expanding most of the map. The zone side already snaps,
        ' because ZoneNear finds a disc COVERING the mark whose centre is
        ' elsewhere. Comparing a search that snaps against one that does not
        ' measures the snapping, not the graph.
        If Not SnapFree(z, hull_r_m, N, scx, scz) Then Return -1.0F
        If Not SnapFree(z, hull_r_m, N, gcx, gcz) Then Return -1.0F

        Dim total = N * N
        Dim g(total - 1) As Single
        Dim came(total - 1) As Integer
        Dim shut(total - 1) As Boolean
        For i = 0 To total - 1
            g(i) = Single.MaxValue
            came(i) = -1
        Next

        ' Binary heap of (f, cell).
        Dim hf(1023) As Single
        Dim hc(1023) As Integer
        Dim hn = 0

        Dim start = scz * N + scx
        Dim goal = gcz * N + gcx
        g(start) = 0.0F
        hf(0) = Oct(scx, scz, gcx, gcz, cm) : hc(0) = start : hn = 1

        Dim DX() As Integer = {1, -1, 0, 0, 1, 1, -1, -1}
        Dim DZ() As Integer = {0, 0, 1, -1, 1, -1, 1, -1}

        While hn > 0
            Dim cur = hc(0)
            Dim curf = hf(0)
            hn -= 1
            hf(0) = hf(hn) : hc(0) = hc(hn)
            Dim i2 = 0
            While True
                Dim l = i2 * 2 + 1, r = l + 1, sm = i2
                If l < hn AndAlso hf(l) < hf(sm) Then sm = l
                If r < hn AndAlso hf(r) < hf(sm) Then sm = r
                If sm = i2 Then Exit While
                Dim tf = hf(i2) : hf(i2) = hf(sm) : hf(sm) = tf
                Dim tc = hc(i2) : hc(i2) = hc(sm) : hc(sm) = tc
                i2 = sm
            End While

            If shut(cur) Then Continue While
            shut(cur) = True
            expanded += 1
            If cur = goal Then Exit While

            Dim cx = cur Mod N, cz = cur \ N
            For d = 0 To 7
                Dim nx = cx + DX(d), nz2 = cz + DZ(d)
                If nx < 0 OrElse nz2 < 0 OrElse nx >= N OrElse nz2 >= N Then Continue For
                Dim ni = nz2 * N + nx
                If shut(ni) Then Continue For
                If z.clear_m(ni) < hull_r_m Then Continue For
                Dim step_m = If(d < 4, cm, cm * 1.41421356F)
                Dim tentative = g(cur) + step_m
                If tentative >= g(ni) Then Continue For
                came(ni) = cur
                g(ni) = tentative
                If hn >= hf.Length Then
                    ReDim Preserve hf(hf.Length * 2 - 1)
                    ReDim Preserve hc(hc.Length * 2 - 1)
                End If
                Dim f2 = tentative + Oct(nx, nz2, gcx, gcz, cm)
                Dim k = hn
                hf(k) = f2 : hc(k) = ni
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

        ms = (Date.UtcNow - t0).TotalMilliseconds
        If g(goal) = Single.MaxValue Then Return -1.0F
        Return g(goal)
    End Function

    ''' <summary>Walk outward in rings to the nearest cell that clears the
    ''' hull. Returns False if nothing within a sane distance does.</summary>
    Private Shared Function SnapFree(z As TankZones, hull_r_m As Single, N As Integer,
                                     ByRef cx As Integer, ByRef cz As Integer) As Boolean
        If z.clear_m(cz * N + cx) >= hull_r_m Then Return True
        For ring = 1 To 64
            For dz = -ring To ring
                For dx = -ring To ring
                    If Math.Abs(dx) <> ring AndAlso Math.Abs(dz) <> ring Then Continue For
                    Dim nx = cx + dx, nz = cz + dz
                    If nx < 0 OrElse nz < 0 OrElse nx >= N OrElse nz >= N Then Continue For
                    If z.clear_m(nz * N + nx) >= hull_r_m Then
                        cx = nx : cz = nz
                        Return True
                    End If
                Next
            Next
        Next
        Return False
    End Function

    ''' <summary>Octile distance, the admissible heuristic for 8-connected
    ''' movement - straight steps cost one cell, diagonals root two.</summary>
    Private Shared Function Oct(ax As Integer, az As Integer,
                                bx As Integer, bz As Integer, cm As Single) As Single
        Dim dx = Math.Abs(ax - bx), dz = Math.Abs(az - bz)
        Dim lo = Math.Min(dx, dz), hi = Math.Max(dx, dz)
        Return cm * (CSng(hi - lo) + 1.41421356F * lo)
    End Function

    ''' <summary>Centre-to-centre distance between two zones.</summary>
    Private Shared Function Sep(z As TankZones, a As Integer, b As Integer) As Single
        Dim dx = z.zones(a).x - z.zones(b).x
        Dim dz = z.zones(a).z - z.zones(b).z
        Return CSng(Math.Sqrt(dx * dx + dz * dz))
    End Function

    ''' <summary>Mark every cell a zone's disc covers as consumed.</summary>
    Private Shared Sub PaintDisc(z As TankZones, eaten() As Boolean,
                                 zi As Integer, N As Integer)
        Dim zp = z.zones(zi)
        Dim pr = CInt(Math.Floor(zp.r_m / Math.Max(z.cell_m, 0.001F)))
        Dim pr2 = pr * pr
        For dz = -pr To pr
            Dim rr = zp.cz + dz
            If rr < 0 OrElse rr >= N Then Continue For
            For dx = -pr To pr
                If dx * dx + dz * dz > pr2 Then Continue For
                Dim cc = zp.cx + dx
                If cc < 0 OrElse cc >= N Then Continue For
                eaten(rr * N + cc) = True
            Next
        Next
    End Sub

    ''' <summary>
    ''' A* over the zone graph, skipping anything marked dead.
    '''
    ''' Straight-line distance between centres for both the cost and the
    ''' heuristic, which is admissible because no link is shorter than the
    ''' separation it spans - so the first time the goal is taken off the open
    ''' set it is by the shortest route, and nothing needs re-opening.
    '''
    ''' A linear scan for the cheapest open node rather than a heap: the graph
    ''' is a few thousand nodes and this runs a few dozen times at load. A heap
    ''' is the right answer if either of those grows by an order of magnitude,
    ''' and the wrong complexity to carry before then.
    ''' </summary>
    Private Shared Function AStar(z As TankZones, dead() As Boolean,
                                  start As Integer, goal As Integer) As Integer()
        Dim n = z.zones.Count
        Dim g(n - 1) As Single
        Dim fscore(n - 1) As Single
        Dim came(n - 1) As Integer
        Dim shut(n - 1) As Boolean
        For i = 0 To n - 1
            g(i) = Single.MaxValue
            fscore(i) = Single.MaxValue
            came(i) = -1
        Next

        ' A BINARY HEAP, not a linear scan for the cheapest open node.
        '
        ' The scan was written on the argument that a few thousand nodes run a
        ' few dozen times at load is not worth a heap. That was wrong, and
        ' measurably: it made this search 23 ms where the same problem over the
        ' CELLS took 8 ms with a heap - so the graph looked three times slower
        ' than the grid when what was actually being compared was a scan
        ' against a heap. A benchmark that measures the implementation instead
        ' of the idea is worse than no benchmark, because it gets believed.
        Dim hf(255) As Single
        Dim hc(255) As Integer
        Dim hn = 0

        g(start) = 0.0F
        fscore(start) = Sep(z, start, goal)
        hf(0) = fscore(start) : hc(0) = start : hn = 1

        Do
            If hn = 0 Then Return Nothing           ' open set empty: no path
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

            If shut(cur) Then Continue Do
            If cur = goal Then Exit Do
            shut(cur) = True

            For Each nb In z.link(cur)
                If dead(nb) OrElse shut(nb) Then Continue For
                Dim tentative = g(cur) + Sep(z, cur, nb)
                If tentative >= g(nb) Then Continue For
                came(nb) = cur
                g(nb) = tentative
                fscore(nb) = tentative + Sep(z, nb, goal)
                If hn >= hf.Length Then
                    ReDim Preserve hf(hf.Length * 2 - 1)
                    ReDim Preserve hc(hc.Length * 2 - 1)
                End If
                Dim k = hn
                hf(k) = fscore(nb) : hc(k) = nb
                hn += 1
                While k > 0
                    Dim par = (k - 1) \ 2
                    If hf(par) <= hf(k) Then Exit While
                    Dim tf2 = hf(par) : hf(par) = hf(k) : hf(k) = tf2
                    Dim tc2 = hc(par) : hc(par) = hc(k) : hc(k) = tc2
                    k = par
                End While
            Next
        Loop

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
End Class

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
        Dim start = z.ZoneAt(sx, sz)
        Dim goal = z.ZoneAt(gx, gz)
        If start < 0 OrElse goal < 0 Then
            why_stopped = "an end is not on drivable ground"
            LogThis("tank routes: {0} - start zone {1}, goal zone {2}; an end is not in any disc",
                    label, start, goal)
            Return
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
            Dim hops = AStar(z, dead, start, goal)
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
        Dim open(n - 1) As Boolean
        Dim shut(n - 1) As Boolean
        For i = 0 To n - 1
            g(i) = Single.MaxValue
            fscore(i) = Single.MaxValue
            came(i) = -1
        Next

        g(start) = 0.0F
        fscore(start) = Sep(z, start, goal)
        open(start) = True

        Do
            Dim cur = -1
            Dim best = Single.MaxValue
            For i = 0 To n - 1
                If open(i) AndAlso fscore(i) < best Then
                    best = fscore(i)
                    cur = i
                End If
            Next
            If cur < 0 Then Return Nothing          ' open set empty: no path
            If cur = goal Then Exit Do

            open(cur) = False
            shut(cur) = True

            For Each nb In z.link(cur)
                If dead(nb) OrElse shut(nb) Then Continue For
                Dim tentative = g(cur) + Sep(z, cur, nb)
                If tentative >= g(nb) Then Continue For
                came(nb) = cur
                g(nb) = tentative
                fscore(nb) = tentative + Sep(z, nb, goal)
                open(nb) = True
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

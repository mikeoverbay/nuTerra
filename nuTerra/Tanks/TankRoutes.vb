Imports OpenTK.Mathematics

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
        ''' <summary>Which lane of the sweep hunted it down, in metres left of
        ''' the base-to-base line. Positive is left of the way you are going.
        ''' Kept so the catalogue can be read as a fan rather than a list.</summary>
        Public lane_m As Single
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
    ''' RAISED FROM 12 m. Every route has to leave the same base and reach the
    ''' same flag, so the ground immediately around each is common to all of
    ''' them and must never be eaten. At 12 m, with an erase up to 24 m across,
    ''' two routes were enough to wall a base in - and the next gate search
    ''' then reported "no path" for a map with 213,000 cells still passable.
    ''' The Python resolver needed 45 m for exactly this and found eight routes
    ''' where this found two.
    Private Const END_GUARD_M As Single = 45.0F

    ''' <summary>
    ''' How much wider than the hull a segment's corridor must be before the
    ''' thinning will span it in one straight line.
    '''
    ''' THE DRIVER DOES NOT FLY THE LINE. It turns toward the waypoint at
    ''' TURN_RATE_RAD and drives whenever it is within DRIVE_CONE_RAD - half a
    ''' radian, 28 degrees - of the bearing, so it approaches in a shallow arc
    ''' rather than along the segment. Thinning against the bare hull radius
    ''' therefore guarantees the wrong thing: the STRAIGHT line is clear and the
    ''' tank does not drive the straight line. Three of four hulls ended a run
    ''' stuck with "ground" on exactly that.
    '''
    ''' Demanding slack makes the thinning self-adjusting. Open ground has room
    ''' to spare and still collapses to a handful of waypoints; a corridor only
    ''' just wider than the tank fails the test and keeps its cells, so the hull
    ''' is walked round the bend a step at a time instead of being pointed
    ''' through it. Which is what a tight corridor should cost.
    ''' </summary>
    ''' SET TO ZERO, AND UNSETTLED. At 2.5 m it barely thinned at all - 551
    ''' waypoints on the 1,569 m route, one every 2.8 m - and the hulls still
    ''' did not finish, so the slack bought density without buying progress.
    ''' Bare line-of-sight gives the sensible shape: 18 waypoints on the direct
    ''' route and 62 on the twisting one. Which is right cannot be decided until
    ''' a hull actually completes a route, because nothing downstream of the
    ''' first deadlock has been exercised.
    Public Const THIN_SLACK_M As Single = 0.0F

    ''' <summary>
    ''' The widest swath a single route may erase, in metres.
    '''
    ''' Erasing at the cell's OWN clearance is right in an alley - it is
    ''' single-file, so one pass consumes it - and badly wrong in the open. A
    ''' route crossing a sixty-metre field ate a sixty-metre swath and took
    ''' every parallel way through it with it, which is why the map only ever
    ''' yielded two corridors: the first route ate the middle of it.
    '''
    ''' Capped, a pass through open ground consumes a lane rather than the
    ''' field, and the next sweep can find a genuinely different way across the
    ''' same ground - which is the point, because a field DOES admit many
    ''' routes and tanks abreast in one is what a push looks like.
    ''' </summary>
    Public Const ERASE_CAP_M As Single = 12.0F

    ''' <summary>
    ''' How many lanes the sweep hunts across, and what leaning into one costs.
    '''
    ''' THE OWNER'S METHOD: "start looking left in the hunt sweep". Taking the
    ''' shortest route, erasing it and repeating always begins in the MIDDLE -
    ''' the shortest line between two bases runs up the centre of the map - so
    ''' every later route is only whatever survived the erase rather than a way
    ''' anybody chose. Sweeping the lanes left to right instead HUNTS a
    ''' different corridor on purpose: each pass is told where to look, so the
    ''' catalogue comes out spread across the map's width rather than stacked
    ''' on its spine.
    '''
    ''' The bias is added to the COST, never to the heuristic, so A* is still
    ''' exact - it returns the cheapest path under a cost that prefers a lane,
    ''' which is a different and perfectly well-defined question. A biased route
    ''' is longer than the shortest one by construction, and that is the trade
    ''' being made deliberately: variety costs distance.
    ''' </summary>
    Public Const LANES As Integer = 9
    Public Const LANE_BIAS As Single = 0.55F

    ''' <summary>
    ''' How far off the line between the ends the outermost lanes sit, as a
    ''' fraction of the distance between them.
    '''
    ''' It was 0.45 - on monastery that put the outer lanes 353 m off the centre
    ''' line, so a search was asked to travel a third of a kilometre sideways,
    ''' flood everything it found there, and come back. It cost a quarter of a
    ''' million expansions and returned routes the middle lanes had already
    ''' found.
    '''
    ''' A corridor a tank might actually take is a detour of tens of metres, not
    ''' hundreds. 0.18 keeps the outer lanes inside the ground between the bases
    ''' rather than out past the edges of the map.
    ''' </summary>
    Public Const LANE_SPAN As Single = 0.18F

    ''' <summary>Expansions one search may spend before it admits its lane is
    ''' empty. Generous against a real crossing - the direct route takes about
    ''' 17,000 - and brutal against a flood.</summary>
    ''' RAISED FROM 45,000, which was strangling this. 19_monastery's arena is
    ''' 518,400 cells and a base-to-base crossing is 785 m of cluttered ground;
    ''' 45,000 expansions is 8.7% of the map and nearly every search was hitting
    ''' it. The catalogue's 466,229 total was ten searches each giving up at the
    ''' cap, and because a capped search returned the same Nothing as a failed
    ''' one, the terminator read a budget as a proof and stopped at ONE route
    ''' where the same bake yields eight.
    '''
    ''' A cap is still wanted - proving a negative by flooding is what it exists
    ''' to stop - but it has to be larger than an honest search of this map, not
    ''' smaller. One whole arena of expansions is the natural ceiling: a search
    ''' that has looked at every passable cell has finished, capped or not.
    Public Const EXPAND_CAP As Integer = 600000

    ' ===================================================== the resolver's eye
    '
    ' A PATH RESOLVER YOU CAN WATCH. The owner: "this is a path resolver like
    ' path studio. Just different space... First goal.. find all route to their
    ' flag. I want to see that happening in real time. I want the image to me
    ' centered on the current point found in our path. Keep failed branches on
    ' screen as a diff color that anything else."
    '
    ' The point is that a search cannot be debugged by watching its
    ' consequences. Four hulls bumping about tells you almost nothing about
    ' whether the sweep found every corridor, and it costs minutes a look; the
    ' search itself takes 300 ms and can be watched over and over.
    '
    ' So the search draws itself. Frames are centred on the CURRENT HEAD - the
    ' cell A* is expanding - rather than on the map, because the interesting
    ' thing is always where the frontier is and it is a few cells wide on a
    ' thousand-cell grid. Everything it has already considered stays on screen:
    ' a dead end is not cleared away, it accumulates, so the picture builds into
    ' what the algorithm TRIED and not merely what it chose.

    ''' <summary>Where frames go, or Nothing for a silent search. Set from
    ''' trace=1 on the command line.</summary>
    Public Shared trace_dir As String = Nothing

    ''' <summary>One frame per this many expansions. A cross-map search expands
    ''' about 17,000 cells; a frame each would be seventeen thousand frames of
    ''' almost nothing moving.</summary>
    Public Shared trace_every As Integer = 400

    ''' <summary>
    ''' A HARD CEILING ON FRAMES, because the first version had none and wrote
    ''' 2,809 PNGs before it was killed - the app looked frozen and the owner
    ''' could not see anything, which is the exact opposite of what a
    ''' visualiser is for.
    '''
    ''' Two things were wrong and this is only one of them. The other was that
    ''' EVERY catalogue traced: two team catalogues, four probe catalogues at
    ''' different hull sizes, and one per hull. Tracing stops after the first
    ''' search now, so what comes out is one resolve, watchable end to end.
    ''' </summary>
    Private Const TRACE_MAX As Integer = 320

    ''' <summary>How much map a frame shows, in cells, centred on the head.
    ''' Close enough to see a frontier squeeze through a gap.</summary>
    Private Const TRACE_VIEW As Integer = 260

    Private Shared trace_n As Integer = 0

    ''' <summary>Cells the last search expanded. The honest measure of how hard
    ''' it looked - and the number that says whether it is searching or
    ''' flooding.</summary>
    Public Shared last_expanded As Integer = 0

    ''' <summary>
    ''' Did the last search RUN OUT OF BUDGET, or genuinely find no way?
    '''
    ''' They returned the same Nothing, and the catalogue read both as "no
    ''' path" - so the owner's terminator ("when we cant find a way there, we
    ''' are done") was firing on an expansion cap rather than on the map. On
    ''' 19_monastery that cost SEVEN of the eight routes the same bake yields
    ''' to a resolver without a cap. A budget and a proof are not the same
    ''' answer and must never share a return value.
    ''' </summary>
    Public Shared capped As Boolean = False

    ''' <summary>Expansions across every search in one Build.</summary>
    Public total_expanded As Integer = 0

    ''' <summary>What every cell has meant to the search so far, kept ACROSS
    ''' lanes and sweeps so failed ground stays on the picture.
    ''' 0 untouched, 1 considered and abandoned, 2 on a route that was kept,
    ''' 3 erased by a kept route.</summary>
    Private Shared seen() As Byte

    Public Sub Build(nav As TankNav, hull_r_m As Single,
                     sx As Single, sz As Single,
                     gx As Single, gz As Single,
                     label As String)
        ready = False
        routes.Clear()
        why_stopped = ""
        first_ms = 0.0
        total_expanded = 0
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

        If trace_dir IsNot Nothing Then
            ReDim seen(N * N - 1)
            trace_n = 0
        End If

        ' THE SWEEP AXIS. Lanes are measured across the line between the two
        ' ends, so "left" means left of the way you are going rather than west.
        Dim axx = gx - sx, axz = gz - sz
        Dim alen = CSng(Math.Sqrt(axx * axx + axz * axz))
        If alen < 1.0F Then alen = 1.0F
        Dim dirx = axx / alen, dirz = axz / alen
        Dim perpx = -dirz, perpz = dirx
        Dim midx = (sx + gx) * 0.5F, midz = (sz + gz) * 0.5F
        Dim half = alen * LANE_SPAN

        Dim swept = True
        Do While swept AndAlso routes.Count < MAX_ROUTES
            swept = False

            ' ASK THE CHEAP QUESTION FIRST: is the flag reachable AT ALL through
            ' what is left?
            '
            ' Without this, the last sweep of every resolve costs nine searches
            ' that each flood the whole remaining map to conclude the same
            ' thing. Measured: 805,386 expansions for a catalogue, eighteen
            ' searches all hitting the cap, to return one route. Proving a
            ' negative is the expensive part and it was being proved nine times.
            '
            ' One unbiased search answers it once. If it fails, no lane can
            ' succeed - a lane only ever makes the search harder, never easier -
            ' and the owner's terminator has fired: "When we cant find a way
            ' there, we are done."
            Dim gate = AStar(nav, hull_r_m, eaten, start, goal, N,
                             0.0F, perpx, perpz, midx, midz)
            total_expanded += last_expanded
            If gate Is Nothing Then
                ' THE DIFFERENCE THAT MATTERS. A gate that ran out of budget
                ' has proved nothing at all; a gate that exhausted the open set
                ' has proved the flag unreachable. Reporting both as "no path"
                ' made a budget look like a map.
                why_stopped = If(capped,
                                 "the gate search hit its expansion cap - NOT a proof",
                                 "no path")
                Exit Do
            End If

            ' li = -1 IS THE GATE'S OWN ROUTE, AND IT USED TO BE THROWN AWAY.
            '
            ' The gate above runs an UNBIASED search to ask whether the flag is
            ' reachable at all - and an unbiased search returns the SHORTEST
            ' route through what is left. That answer was being discarded and
            ' the sweep then started at the leftmost lane, so the catalogue
            ' never contained the direct way: every route on monastery came
            ' back at lane 141 m or 106 m and the shortest was 1085 m against
            ' the 812 m the same bake gives a resolver that just takes the
            ' short way first. We paid for the best route every sweep and then
            ' binned it.
            '
            ' Rule one is "seek base". The straight answer goes in first, then
            ' the sweep hunts the ways round it.
            For li = -1 To LANES - 1
            ' LEFT FIRST, then across to the right - the owner's method. The
            ' lane tells the search where to LOOK, so each pass hunts its own
            ' corridor instead of taking what the last erase happened to leave.
            ' LANES = 1 is a legal thing to ask for - it means "no sweep,
            ' just search" - and dividing by LANES - 1 made it NaN, which read
            ' as zero routes rather than as the bug it was.
            Dim lane = If(li < 0, 0.0F,
                          If(LANES <= 1, 0.0F,
                             half - 2.0F * half * CSng(li) / CSng(LANES - 1)))

            Dim ta = Date.UtcNow
            Dim path = If(li < 0, gate,
                          AStar(nav, hull_r_m, eaten, start, goal, N,
                                lane, perpx, perpz, midx, midz))
            If routes.Count = 0 Then first_ms = (Date.UtcNow - ta).TotalMilliseconds
            If li >= 0 Then total_expanded += last_expanded
            If path Is Nothing Then Continue For
            swept = True

            Dim r As Route
            r.cells = path
            r.length_m = 0.0F
            r.min_clear_m = Single.MaxValue
            For i = 0 To path.Length - 1
                Dim cl = nav.clear_m(path(i))
                If cl < r.min_clear_m Then r.min_clear_m = cl
                If i > 0 Then r.length_m += StepLen(path(i - 1), path(i), N, nav.cell_m)
            Next
            r.lane_m = lane
            routes.Add(r)

            ' A ROUTE THAT WAS KEPT paints over its own failed branches, so the
            ' red is only ever ground that led nowhere.
            If trace_dir IsNot Nothing AndAlso seen IsNot Nothing Then
                For Each ci2 In path
                    seen(ci2) = 2
                Next
                TraceFrame(nav, path(path.Length \ 2), start, goal, lane, N,
                           New Integer() {}, 0,
                           String.Format("ROUTE {0} kept, {1:0} m", routes.Count - 1, r.length_m))
            End If

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
                Dim wide = Math.Min(nav.clear_m(ci), ERASE_CAP_M)
                Dim rc = CInt(Math.Floor(wide / Math.Max(nav.cell_m, 0.001F)))
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
                            ' Eaten ground, unless the route itself runs here.
                            If seen IsNot Nothing AndAlso seen(pi) <> 2 Then seen(pi) = 3
                        End If
                    Next
                Next
            Next
            If trace_dir IsNot Nothing Then
                TraceFrame(nav, goal, start, goal, lane, N, New Integer() {}, 0,
                           String.Format("erased {0} cell(s) - next lane", killed))
            End If
            ' WHAT THE ERASE ACTUALLY COST, because "no path" on the next
            ' gate is either a map with no second way or an erase that took
            ' the only one, and the two look identical from outside.
            Dim left_open = 0
            For q = 0 To N * N - 1
                If Not eaten(q) AndAlso nav.clear_m(q) >= hull_r_m Then left_open += 1
            Next
            LogThis("tank routes:   erased {0:N0} cell(s) at lane {1:0} m; " &
                    "{2:N0} cell(s) still passable for this hull",
                    killed, lane, left_open)
            If killed = 0 Then
                why_stopped = "erasing a route consumed nothing - it would repeat for ever"
                Exit Do
            End If
            Next
        Loop
        If why_stopped = "" Then why_stopped = "a whole sweep found nothing new"

        ready = True
        Dim ms = (Date.UtcNow - t0).TotalMilliseconds

        ' ONE RESOLVE, THEN THE EYE CLOSES. A load runs this many times - both
        ' teams, four hull probes, one per hull - and tracing all of them is
        ' what buried the first attempt. The first search is the one worth
        ' watching; the rest run silent.
        If trace_dir IsNot Nothing Then
            LogThis("tank routes: traced {0} frame(s) of this resolve into {1}",
                    trace_n, trace_dir)
            trace_dir = Nothing
        End If

        LogThis("tank routes: {0} - {1} route(s) in {2:0} ms, {3:N0} cell(s) expanded, stopped because {4}",
                label, routes.Count, ms, total_expanded, why_stopped)
        For i = 0 To routes.Count - 1
            LogThis("tank routes:   {0}: {1:0} m, least room {2:0.0} m, lane {3,4:0} m over {4} cell(s)",
                    i, routes(i).length_m, routes(i).min_clear_m,
                    routes(i).lane_m, routes(i).cells.Length)
        Next
    End Sub

    ''' <summary>
    ''' A route as world waypoints a driver can steer at.
    '''
    ''' THINNED BY LINE OF SIGHT, NOT BY DISTANCE. The raw path is one point
    ''' per cell - 584 of them at 1.37 m for the direct route - and a driver
    ''' whose ARRIVE_M is 6 m would spend its life declaring arrival at a point
    ''' it is already standing on and never build speed. So it has to be
    ''' thinned. But dropping points every N metres is WRONG, and measurably:
    ''' at 15 m spacing three of four hulls ended the run stuck with "ground",
    ''' because the driver steers in a STRAIGHT LINE to the next waypoint and a
    ''' straight line between two points of a bend cuts the corner the route
    ''' went round. The path was traversable and the thinning was not.
    '''
    ''' A point is therefore kept exactly when the straight line from the last
    ''' kept point to the next one would leave ground the hull fits through.
    ''' Every surviving segment is then drivable as flown, which is the only
    ''' promise the driver needs. Open ground thins to almost nothing and a
    ''' twisting alley keeps nearly every cell, which is the right shape.
    '''
    ''' The last cell is always kept: the end of the route is the base, and
    ''' that is the one waypoint that must not be thinned away.
    ''' </summary>
    Public Function Waypoints(nav As TankNav, index As Integer,
                              hull_r_m As Single) As List(Of Vector2)
        Dim out As New List(Of Vector2)
        If index < 0 OrElse index >= routes.Count Then Return out
        Dim cells = routes(index).cells
        If cells Is Nothing OrElse cells.Length = 0 Then Return out

        Dim N = TankNav.SIZE
        out.Add(nav.CentreOf(cells(0) Mod N, cells(0) \ N))
        Dim anchor = 0
        For i = 1 To cells.Length - 1
            If Not LineClear(nav, hull_r_m, N, cells(anchor), cells(i)) Then
                anchor = i - 1
                out.Add(nav.CentreOf(cells(anchor) Mod N, cells(anchor) \ N))
            End If
        Next
        Dim fin = cells(cells.Length - 1)
        out.Add(nav.CentreOf(fin Mod N, fin \ N))
        Return out
    End Function

    ''' <summary>Can a hull travel the straight line between two cells? Walked
    ''' at half a cell so nothing is stepped over, against the same clearance
    ''' field the search used, so the answer agrees with the route.</summary>
    Private Shared Function LineClear(nav As TankNav, hull_r_m As Single, N As Integer,
                                      a As Integer, b As Integer) As Boolean
        Dim ax = a Mod N, az = a \ N
        Dim bx = b Mod N, bz = b \ N
        Dim dx = bx - ax, dz = bz - az
        Dim steps = CInt(Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dz)) * 2.0))
        If steps < 1 Then Return True
        For s = 0 To steps
            Dim t = CSng(s) / CSng(steps)
            Dim cx = CInt(Math.Round(ax + dx * t))
            Dim cz = CInt(Math.Round(az + dz * t))
            If cx < 0 OrElse cz < 0 OrElse cx >= N OrElse cz >= N Then Return False
            If nav.clear_m(cz * N + cx) < hull_r_m Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' The catalogue as a picture, because a route is a shape and a log line
    ''' is not.
    '''
    ''' One pixel a cell. The grid underneath in the same colours the nav dump
    ''' uses so the two can be held side by side, dimmed so the routes read on
    ''' top of it. Each catalogue draws its routes in its own hue, brightest
    ''' first, with the waypoints the driver actually steers at marked - those
    ''' are the thing being argued about, and their spacing IS the thinning.
    ''' </summary>
    Public Shared Sub DumpCatalogues(nav As TankNav, map As String,
                                     cats() As TankRoutes, names() As String)
        If nav Is Nothing OrElse Not nav.ready Then Return
        Try
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "tanks")
            IO.Directory.CreateDirectory(dir)
            Dim path = IO.Path.Combine(dir, map & "_routes.png")
            Dim N = TankNav.SIZE

            Using bmp As New Drawing.Bitmap(N, N, Drawing.Imaging.PixelFormat.Format24bppRgb)
                Dim d = bmp.LockBits(New Drawing.Rectangle(0, 0, N, N),
                                     Drawing.Imaging.ImageLockMode.WriteOnly,
                                     Drawing.Imaging.PixelFormat.Format24bppRgb)
                Dim stride = d.Stride
                Dim px(stride * N - 1) As Byte
                For r = 0 To N - 1
                    For c = 0 To N - 1
                        Dim f = nav.cell(r * N + c)
                        Dim rr As Byte = 18, gg As Byte = 40, bb As Byte = 22   ' open
                        If (f And TankNav.IMPASSABLE) <> 0 Then
                            If (f And TankNav.OFFMAP) <> 0 Then
                                rr = 8 : gg = 8 : bb = 10
                            ElseIf (f And TankNav.WATER) <> 0 Then
                                rr = 16 : gg = 34 : bb = 70
                            ElseIf (f And TankNav.STEEP) <> 0 Then
                                rr = 70 : gg = 66 : bb = 26
                            Else
                                rr = 74 : gg = 34 : bb = 22                     ' blocked
                            End If
                        End If
                        Dim o = r * stride + c * 3
                        px(o) = bb : px(o + 1) = gg : px(o + 2) = rr            ' BGR
                    Next
                Next
                Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length)
                bmp.UnlockBits(d)

                Using gfx = Drawing.Graphics.FromImage(bmp)
                    gfx.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias
                    For ci = 0 To cats.Length - 1
                        Dim cat = cats(ci)
                        If cat Is Nothing OrElse Not cat.ready Then Continue For
                        For ri = 0 To cat.routes.Count - 1
                            ' Brightest for route 0 - the one a planner would
                            ' take first - so the picture says which is the
                            ' short way and which is the long way round.
                            Dim fade = 1.0F - 0.45F * Math.Min(ri, 2) / 2.0F
                            Dim col = If(ci = 0,
                                Drawing.Color.FromArgb(255, CInt(90 * fade), CInt(240 * fade), CInt(120 * fade)),
                                Drawing.Color.FromArgb(255, CInt(255 * fade), CInt(110 * fade), CInt(90 * fade)))
                            Dim cells = cat.routes(ri).cells
                            Using pen As New Drawing.Pen(col, 1.6F)
                                For i = 1 To cells.Length - 1
                                    gfx.DrawLine(pen,
                                        cells(i - 1) Mod N, cells(i - 1) \ N,
                                        cells(i) Mod N, cells(i) \ N)
                                Next
                            End Using

                            ' The waypoints the driver steers at. Their spacing
                            ' is the thinning, drawn rather than described.
                            Dim wp = cat.Waypoints(nav, ri, TankDriveTune.HULL_R + THIN_SLACK_M)
                            Using dot As New Drawing.SolidBrush(Drawing.Color.FromArgb(230, 255, 255, 255))
                                For Each w In wp
                                    Dim cx As Integer, cz As Integer
                                    nav.CellOf(w.X, w.Y, cx, cz)
                                    gfx.FillRectangle(dot, cx - 1, cz - 1, 3, 3)
                                Next
                            End Using
                        Next
                    Next

                    ' The bases last so nothing is drawn over them.
                    If map_scene.BASE_RINGS_LOADED Then
                        DrawBase(gfx, nav, -TEAM_1.X, TEAM_1.Z, Drawing.Color.Lime)
                        DrawBase(gfx, nav, -TEAM_2.X, TEAM_2.Z, Drawing.Color.OrangeRed)
                    End If
                End Using

                bmp.Save(path, Drawing.Imaging.ImageFormat.Png)
            End Using
            LogThis("tank routes: wrote {0}", path)
        Catch ex As Exception
            LogThis("tank routes: could not write png - {0}", ex.Message)
        End Try
    End Sub

    Private Shared Sub DrawBase(gfx As Drawing.Graphics, nav As TankNav,
                                wx As Single, wz As Single, col As Drawing.Color)
        Dim cx As Integer, cz As Integer
        nav.CellOf(wx, wz, cx, cz)
        Dim r = 50.0F / Math.Max(nav.cell_m, 0.001F)     ' the 50 m base ring
        Using pen As New Drawing.Pen(col, 2.0F)
            gfx.DrawEllipse(pen, cx - r, cz - r, r * 2.0F, r * 2.0F)
            gfx.DrawLine(pen, cx - 6, cz, cx + 6, cz)
            gfx.DrawLine(pen, cx, cz - 6, cx, cz + 6)
        End Using
    End Sub

    ''' <summary>
    ''' One frame of the search, centred on the cell it is expanding.
    '''
    ''' THE COLOURS ARE THE POINT, so they are chosen to be told apart at a
    ''' glance rather than to look like anything:
    '''
    '''   dim grey/green   the map - open ground, and what is shut and why
    '''   DEEP RED         considered and abandoned. A failed branch. Never
    '''                    cleared, so the dead ends pile up into a picture of
    '''                    everywhere the search has been
    '''   YELLOW           the live frontier - what it is about to try next
    '''   WHITE CROSS      the head, the cell being expanded this instant
    '''   BRIGHT GREEN     a route that was kept
    '''   BLUE-GREY        ground a kept route ate, so the next sweep must go
    '''                    somewhere else
    '''   RINGS            where it started and the flag it is hunting
    ''' </summary>
    Private Shared Sub TraceFrame(nav As TankNav, head As Integer,
                                  start As Integer, goal As Integer,
                                  lane_m As Single, N As Integer,
                                  openc() As Integer, openn As Integer,
                                  note As String)
        If trace_dir Is Nothing Then Return
        If trace_n >= TRACE_MAX Then Return
        Try
            Dim hx = head Mod N, hz = head \ N
            Dim half = TRACE_VIEW \ 2
            Dim px_per = 2
            Dim W = TRACE_VIEW * px_per

            Using bmp As New Drawing.Bitmap(W, W, Drawing.Imaging.PixelFormat.Format24bppRgb)
                Using gfx = Drawing.Graphics.FromImage(bmp)
                    gfx.Clear(Drawing.Color.FromArgb(10, 10, 12))
                    For vz = 0 To TRACE_VIEW - 1
                        Dim cz = hz - half + vz
                        If cz < 0 OrElse cz >= N Then Continue For
                        For vx = 0 To TRACE_VIEW - 1
                            Dim cx = hx - half + vx
                            If cx < 0 OrElse cx >= N Then Continue For
                            Dim i = cz * N + cx
                            Dim f = nav.cell(i)
                            Dim col As Drawing.Color

                            If seen IsNot Nothing AndAlso seen(i) = 3 Then
                                col = Drawing.Color.FromArgb(48, 56, 78)      ' eaten
                            ElseIf seen IsNot Nothing AndAlso seen(i) = 2 Then
                                col = Drawing.Color.FromArgb(70, 240, 110)    ' kept route
                            ElseIf seen IsNot Nothing AndAlso seen(i) = 1 Then
                                col = Drawing.Color.FromArgb(150, 30, 36)     ' FAILED branch
                            ElseIf (f And TankNav.IMPASSABLE) <> 0 Then
                                If (f And TankNav.OFFMAP) <> 0 Then
                                    col = Drawing.Color.FromArgb(6, 6, 8)
                                ElseIf (f And TankNav.WATER) <> 0 Then
                                    col = Drawing.Color.FromArgb(14, 30, 60)
                                ElseIf (f And TankNav.STEEP) <> 0 Then
                                    col = Drawing.Color.FromArgb(58, 54, 22)
                                Else
                                    col = Drawing.Color.FromArgb(62, 30, 20)
                                End If
                            Else
                                col = Drawing.Color.FromArgb(22, 44, 26)
                            End If
                            Using b As New Drawing.SolidBrush(col)
                                gfx.FillRectangle(b, vx * px_per, vz * px_per, px_per, px_per)
                            End Using
                        Next
                    Next

                    ' The live frontier on top of everything it might become.
                    Using fb As New Drawing.SolidBrush(Drawing.Color.FromArgb(255, 235, 90))
                        For k = 0 To openn - 1
                            Dim i = openc(k)
                            Dim cx = i Mod N, cz = i \ N
                            Dim vx = cx - (hx - half), vz = cz - (hz - half)
                            If vx < 0 OrElse vz < 0 OrElse vx >= TRACE_VIEW OrElse vz >= TRACE_VIEW Then Continue For
                            gfx.FillRectangle(fb, vx * px_per, vz * px_per, px_per, px_per)
                        Next
                    End Using

                    DrawMark(gfx, start, hx, hz, half, px_per, N, Drawing.Color.DeepSkyBlue)
                    DrawMark(gfx, goal, hx, hz, half, px_per, N, Drawing.Color.Orange)

                    ' The head, dead centre by construction.
                    Using hp As New Drawing.Pen(Drawing.Color.White, 1.0F)
                        Dim c = half * px_per
                        gfx.DrawLine(hp, c - 7, c, c + 7, c)
                        gfx.DrawLine(hp, c, c - 7, c, c + 7)
                    End Using

                    Using f2 As New Drawing.Font("Consolas", 9.0F)
                        Using tb As New Drawing.SolidBrush(Drawing.Color.White)
                            gfx.DrawString(String.Format("lane {0,4:0} m   open {1}   {2}",
                                                         lane_m, openn, note),
                                           f2, tb, 4, 4)
                        End Using
                    End Using
                End Using
                bmp.Save(IO.Path.Combine(trace_dir,
                         String.Format("trace_{0:00000}.png", trace_n)),
                         Drawing.Imaging.ImageFormat.Png)
            End Using
            trace_n += 1
        Catch
            ' A trace frame is never worth an exception.
        End Try
    End Sub

    Private Shared Sub DrawMark(gfx As Drawing.Graphics, cell As Integer,
                                hx As Integer, hz As Integer, half As Integer,
                                px_per As Integer, N As Integer,
                                col As Drawing.Color)
        Dim cx = cell Mod N, cz = cell \ N
        Dim vx = cx - (hx - half), vz = cz - (hz - half)
        If vx < 0 OrElse vz < 0 OrElse vx >= TRACE_VIEW OrElse vz >= TRACE_VIEW Then Return
        Using p As New Drawing.Pen(col, 2.0F)
            gfx.DrawEllipse(p, vx * px_per - 6, vz * px_per - 6, 12, 12)
        End Using
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
                                  N As Integer,
                                  lane_m As Single,
                                  perpx As Single, perpz As Single,
                                  midx As Single, midz As Single) As Integer()
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

        ' World position of a cell, without calling CentreOf a million times.
        Dim ox = nav.wx_min, oz = nav.wz_max
        Dim sx_c = (nav.wx_max - nav.wx_min) / N
        Dim sz_c = (nav.wz_max - nav.wz_min) / N

        Dim hf(1023) As Single
        Dim hc(1023) As Integer
        Dim hn = 0
        Dim expanded = 0
        capped = False
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

            ' EVERY EXPANSION IS A BRANCH THAT WAS CONSIDERED. Marked before we
            ' know whether it leads anywhere, and never cleared - if the route
            ' ends up going through here it is overwritten green, and if it does
            ' not it stays red as a dead end. That is what makes the picture
            ' accumulate into where the search HAS BEEN rather than only where
            ' it ended up.
            expanded += 1
            If trace_dir IsNot Nothing Then
                If seen IsNot Nothing AndAlso seen(cur) = 0 Then seen(cur) = 1
                If expanded Mod trace_every = 0 Then
                    TraceFrame(nav, cur, start, goal, lane_m, N, hc, hn, "searching")
                End If
            End If

            ' A SEARCH THAT HAS LOOKED THIS HARD IS NOT GOING TO FIND ANYTHING
            ' GOOD. One hull's catalogue expanded 260,896 cells and took 13
            ' seconds before this cap existed - a quarter of a million rays to
            ' return two routes it had already found from another lane.
            '
            ' A lane that cannot reach the flag within the cap is a lane with no
            ' corridor in it, and the honest answer for that lane is nothing.
            ' Giving up cheaply is what makes sweeping NINE of them affordable.
            If expanded > EXPAND_CAP Then
                last_expanded = expanded
                capped = True
                Return Nothing
            End If

            If cur = goal Then Exit While

            Dim cx = cur Mod N, cz = cur \ N
            For d = 0 To 7
                Dim nx = cx + DX(d), nz = cz + DZ(d)
                If nx < 0 OrElse nz < 0 OrElse nx >= N OrElse nz >= N Then Continue For
                Dim ni = nz * N + nx
                If shut(ni) OrElse eaten(ni) Then Continue For
                If nav.clear_m(ni) < hull_r_m Then Continue For
                Dim step_m = If(d < 4, cm, cm * 1.41421356F)

                ' THE LANE BIAS, IN THE COST AND NEVER IN THE HEURISTIC. Put it
                ' in the heuristic and A* stops being exact and starts being a
                ' guess that sometimes returns a worse path than it found. In
                ' the cost it is simply a different question - the cheapest way
                ' when leaning off the line is charged for - and the answer is
                ' exact for that question.
                '
                ' Per METRE travelled rather than per step, so the penalty does
                ' not depend on how many cells a route happens to cross, and
                ' normalised to 100 m so LANE_BIAS reads as "what fraction more
                ' does a hundred metres off-lane cost".
                Dim wxn = ox + (nx + 0.5F) * sx_c
                Dim wzn = oz - (nz + 0.5F) * sz_c
                Dim off = (wxn - midx) * perpx + (wzn - midz) * perpz
                Dim dev = Math.Abs(off - lane_m)
                Dim tentative = g(cur) + step_m * (1.0F + LANE_BIAS * dev / 100.0F)
                If tentative >= g(ni) Then Continue For
                came(ni) = cur
                g(ni) = tentative
                If hn >= hf.Length Then
                    ReDim Preserve hf(hf.Length * 2 - 1)
                    ReDim Preserve hc(hc.Length * 2 - 1)
                End If
                ' THE HEURISTIC HAS TO SPEAK THE COST'S LANGUAGE.
                '
                ' Plain octile distance says a metre costs a metre. The lane
                ' bias charges up to two or three times that, so the estimate
                ' undershot the truth badly - and an A* whose heuristic
                ' undershoots stops being a search and becomes Dijkstra. It
                ' expanded seventeen thousand cells to find one route and
                ' flooded half the map: the owner, looking at the picture,
                ' "sampling to may rays".
                '
                ' Scaling the estimate by the SAME penalty the step just paid
                ' makes the two agree about what a metre is worth from here.
                ' That is an estimate rather than a strict lower bound, so the
                ' route is no longer provably the cheapest under the biased
                ' cost - which is a fine trade, because a biased route was never
                ' the shortest one and we are hunting for variety, not optimum.
                Dim k = hn
                hf(k) = tentative + Oct(nx, nz, gx, gz, cm) *
                        (1.0F + LANE_BIAS * dev / 100.0F)
                hc(k) = ni
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

        last_expanded = expanded
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





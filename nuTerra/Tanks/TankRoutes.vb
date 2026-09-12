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
    Private Const END_GUARD_M As Single = 12.0F

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

        ' THE SWEEP AXIS. Lanes are measured across the line between the two
        ' ends, so "left" means left of the way you are going rather than west.
        Dim axx = gx - sx, axz = gz - sz
        Dim alen = CSng(Math.Sqrt(axx * axx + axz * axz))
        If alen < 1.0F Then alen = 1.0F
        Dim dirx = axx / alen, dirz = axz / alen
        Dim perpx = -dirz, perpz = dirx
        Dim midx = (sx + gx) * 0.5F, midz = (sz + gz) * 0.5F
        Dim half = alen * 0.45F

        Dim swept = True
        Do While swept AndAlso routes.Count < MAX_ROUTES
            swept = False
            For li = 0 To LANES - 1
            ' LEFT FIRST, then across to the right - the owner's method. The
            ' lane tells the search where to LOOK, so each pass hunts its own
            ' corridor instead of taking what the last erase happened to leave.
            Dim lane = half - 2.0F * half * CSng(li) / CSng(LANES - 1)

            Dim ta = Date.UtcNow
            Dim path = AStar(nav, hull_r_m, eaten, start, goal, N,
                             lane, perpx, perpz, midx, midz)
            If routes.Count = 0 Then first_ms = (Date.UtcNow - ta).TotalMilliseconds
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
                        End If
                    Next
                Next
            Next
            If killed = 0 Then
                why_stopped = "erasing a route consumed nothing - it would repeat for ever"
                Exit Do
            End If
            Next
        Loop
        If why_stopped = "" Then why_stopped = "a whole sweep found nothing new"

        ready = True
        Dim ms = (Date.UtcNow - t0).TotalMilliseconds
        LogThis("tank routes: {0} - {1} route(s) in {2:0} ms, stopped because {3}",
                label, routes.Count, ms, why_stopped)
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
                            ElseIf (f And TankNav.TRUNK) <> 0 Then
                                rr = 52 : gg = 34 : bb = 18
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



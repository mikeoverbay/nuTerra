Imports System.Drawing

''' <summary>
''' THE ZONE MAP - free space as a few hundred circles instead of a million
''' cells.
'''
''' The owner's idea, in his words: "We will draw rings and find areas as
''' large as we can that the ring fits with out hitting something. That whole
''' area is safe to drive in. no collision checks. only zone radius checks.
''' ten times faster."
'''
''' That is what this is. Distance-transform the free mask and the value at
''' every cell IS the radius of the largest circle centred there that touches
''' nothing. Cover greedily, widest first, and the result is a set of discs
''' whose interiors are known clear. A hull inside a disc needs no swept test
''' at all - only its distance from the centre, which is one multiply and a
''' compare against a disc it is already holding. TankNav.CanStand tests about
''' fifty cells per step; this replaces that with arithmetic.
'''
''' WHAT IT IS FOR. It is the graph a route planner runs over: a few hundred
''' nodes instead of 1,048,576 cells, so A* across the whole map is
''' sub-millisecond and a whole CATALOGUE of routes can be built at load
''' rather than discovered by driving.
'''
''' TWO THINGS THAT ARE EASY TO GET WRONG, DONE DELIBERATELY HERE.
'''
''' The radius is pulled in by half a cell. The transform measures to the
''' nearest impassable cell's CENTRE, but that cell is solid across its whole
''' area and its area begins half a cell nearer. Handing out the raw distance
''' gives discs that overlap the very thing they were measured against.
'''
''' Two discs are NOT joined merely because they overlap. Overlap says a point
''' lies inside both circles; it does not say a hull can cross between them -
''' in a corridor barely wider than a tank every disc overlaps its neighbour
''' while the safe width is nil. So a link is tested by walking the straight
''' line between the centres and demanding hull clearance the whole way, which
''' is the actual question and is cheap against a field already computed.
'''
''' IT INHERITS THE GRID'S RESOLUTION, AND THAT ERODES DOORWAYS. TankNav
''' coarsens the 0.171 m bake to a 1.37 m cell by blocking a cell if ANY texel
''' in it is blocked - conservative, and right for "may a hull stand here". But
''' a bottleneck search depends on exactly the narrow LINKS that conservative
''' coarsening eats. Measured at both resolutions by the nuTerra session: a
''' 1024 grid coarsened this way finds NO route between the monastery bases at
''' 8 m clearance, while at full resolution the corridor is 16.06 m wide at its
''' tightest. 16% of texels clear 8.03 m, so it is not a thin-ridge artefact -
''' the rooms survive and the doors do not.
'''
''' So a route this class FINDS is real, and a route it does NOT find may still
''' exist: the corridor count is a lower bound. And anything claiming a WIDTH
''' must be measured on the bake rather than read off a disc radius - the
''' smallest disc kept here is 4.79 m, so a route reporting "narrowest 4.8 m"
''' is reporting that floor and not the ground.
'''
''' THE MAP IT INHERITS. A radius field AMPLIFIES an error in the mask rather
''' than tolerating one. Per-step collision checking degrades gracefully - a
''' tank meets the thing, stops, and pins the cell. A radius check does not:
''' one wrongly-free cell lets a disc inflate through a wall, joins two
''' regions that were never joined, and produces a corridor a planner then
''' PREFERS because it is wide. Widest-is-safest is the wrong bias over a mask
''' with holes in it, so the mask has to be right before this is trusted.
''' </summary>
Public Class TankZones

    Public Structure Zone
        Public cx As Integer        ' cell column of the centre
        Public cz As Integer        ' cell row
        Public x As Single          ' world centre
        Public z As Single
        Public r_m As Single        ' clear radius in metres
    End Structure

    Public ReadOnly zones As New List(Of Zone)

    ''' <summary>Neighbours of each zone, by index into `zones`.</summary>
    Public ReadOnly link As New List(Of List(Of Integer))

    ''' <summary>Clear radius in metres at every cell; 0 where impassable.
    ''' Kept because a route planner wants the corridor WIDTH along a path and
    ''' not only at the disc centres - that is how a route gets erased at the
    ''' width it actually consumes.</summary>
    Public clear_m() As Single

    ''' <summary>Zone id per cell, -1 where no disc reaches. The thing that
    ''' makes ZoneAt a lookup rather than a search, and the array that would be
    ''' uploaded as a texture to draw the zone map in the app.</summary>
    Public zone_of() As Integer

    Public ready As Boolean = False
    Public n_links As Integer = 0
    Private cell_m_ As Single = 1.0F
    Private map_name As String = ""
    Private hull_r As Single = 0.0F
    Private frame_x0, frame_x1, frame_z0, frame_z1 As Single

    Private Const INF As Single = 1.0E+20F

    ''' <summary>So a stride fault is reported once rather than per step.</summary>
    Private warned_stride As Boolean = False

    ''' <summary>How far apart disc centres are placed, as a fraction of the
    ''' disc's radius. Covering at the full radius leaves neighbours touching
    ''' at a point and the graph falls apart in corridors; this overlaps them
    ''' deliberately so the link test has room to succeed.</summary>
    Private Const COVER_FRAC As Single = 0.6F

    Public Sub Build(nav As TankNav, hull_r_m As Single, map As String)
        ready = False
        warned_stride = False
        zones.Clear()
        link.Clear()
        If nav Is Nothing OrElse Not nav.ready Then
            LogThis("tank zones: no navigation grid - zone map not built")
            Return
        End If

        Dim t0 = Date.UtcNow
        map_name = map
        cell_m_ = nav.cell_m
        hull_r = hull_r_m
        frame_x0 = nav.wx_min : frame_x1 = nav.wx_max
        frame_z0 = nav.wz_min : frame_z1 = nav.wz_max
        Dim N = TankNav.SIZE

        ' ---- the distance field -------------------------------------------
        ' Felzenszwalb and Huttenlocher's exact transform: two passes of a 1D
        ' lower-envelope sweep, rows then columns, giving the exact squared
        ' distance to the nearest impassable cell in O(cells). A chamfer
        ' approximation would be a few percent out, and a few percent of a
        ' radius is the difference between a corridor a tank fits down and one
        ' it does not.
        Dim f(N * N - 1) As Single
        For i = 0 To N * N - 1
            f(i) = If((nav.cell(i) And TankNav.IMPASSABLE) <> 0, 0.0F, INF)
        Next

        Dim d(N - 1) As Single, ft(N - 1) As Single
        Dim v(N - 1) As Integer, zz(N) As Single

        For r = 0 To N - 1                        ' rows
            Dim o = r * N
            For c = 0 To N - 1 : ft(c) = f(o + c) : Next
            dt1d(ft, N, d, v, zz)
            For c = 0 To N - 1 : f(o + c) = d(c) : Next
        Next
        For c = 0 To N - 1                        ' columns
            For r = 0 To N - 1 : ft(r) = f(r * N + c) : Next
            dt1d(ft, N, d, v, zz)
            For r = 0 To N - 1 : f(r * N + c) = d(r) : Next
        Next

        ' HALF A CELL BACK - see the note at the top of the class.
        ReDim clear_m(N * N - 1)
        For i = 0 To N * N - 1
            Dim rc = CSng(Math.Sqrt(f(i))) - 0.5F
            clear_m(i) = If(rc > 0.0F, rc * cell_m_, 0.0F)
        Next

        ' ---- the discs -----------------------------------------------------
        ' Only cells that could hold a hull are candidates; a disc narrower
        ' than the tank is not a place, it is a gap.
        Dim cand As New List(Of Integer)
        For i = 0 To N * N - 1
            If clear_m(i) >= hull_r_m Then cand.Add(i)
        Next
        If cand.Count = 0 Then
            LogThis("tank zones: no cell clears a {0:0.0} m hull - zone map empty", hull_r_m)
            Return
        End If

        Dim keys(cand.Count - 1) As Single
        Dim idx(cand.Count - 1) As Integer
        For k = 0 To cand.Count - 1
            idx(k) = cand(k)
            keys(k) = -clear_m(cand(k))        ' negated, so the sort puts widest first
        Next
        Array.Sort(keys, idx)

        Dim taken(N * N - 1) As Boolean
        For k = 0 To idx.Length - 1
            Dim i = idx(k)
            If taken(i) Then Continue For
            Dim cz = i \ N, cx = i Mod N
            Dim r_m = clear_m(i)
            Dim ctr = nav.CentreOf(cx, cz)

            Dim z As Zone
            z.cx = cx : z.cz = cz
            z.x = ctr.X : z.z = ctr.Y
            z.r_m = r_m
            zones.Add(z)

            ' Claim the ground this disc speaks for, so the next centre lands
            ' somewhere else rather than one cell over with almost the same
            ' circle.
            Dim rc = CInt(Math.Floor(r_m * COVER_FRAC / cell_m_))
            Dim rc2 = rc * rc
            For dz = -rc To rc
                Dim rr = cz + dz
                If rr < 0 OrElse rr >= N Then Continue For
                For dx = -rc To rc
                    If dx * dx + dz * dz > rc2 Then Continue For
                    Dim cc = cx + dx
                    If cc < 0 OrElse cc >= N Then Continue For
                    taken(rr * N + cc) = True
                Next
            Next
        Next

        ' ---- the id raster -------------------------------------------------
        ' Paint each disc into a cell map so "which zone am I in" is an index
        ' rather than a scan. In emission order, which is widest-first, and
        ' never overwriting - so a cell covered by several discs answers with
        ' the roomiest, matching what a search would have returned.
        ReDim zone_of(N * N - 1)
        For i = 0 To N * N - 1
            zone_of(i) = -1
        Next
        For zi2 = 0 To zones.Count - 1
            Dim zp = zones(zi2)
            Dim pr = CInt(Math.Floor(zp.r_m / cell_m_))
            Dim pr2 = pr * pr
            For dz = -pr To pr
                Dim rr = zp.cz + dz
                If rr < 0 OrElse rr >= N Then Continue For
                For dx = -pr To pr
                    If dx * dx + dz * dz > pr2 Then Continue For
                    Dim cc = zp.cx + dx
                    If cc < 0 OrElse cc >= N Then Continue For
                    Dim pi = rr * N + cc
                    If zone_of(pi) < 0 Then zone_of(pi) = zi2
                Next
            Next
        Next

        ' ---- the graph -----------------------------------------------------
        ' zi, NOT n. VB is case-insensitive, so a loop variable named n IS the
        ' N holding the grid size, and this loop silently left N equal to
        ' zones.Count - after which every index into clear_m was computed
        ' against the wrong stride. It threw rather than misbehaving quietly,
        ' which was luck; the guard in WalkIsClear is what named it.
        For zi = 0 To zones.Count - 1
            link.Add(New List(Of Integer))
        Next
        n_links = 0
        For a = 0 To zones.Count - 1
            For b = a + 1 To zones.Count - 1
                Dim za = zones(a), zb = zones(b)
                Dim ddx = za.x - zb.x, ddz = za.z - zb.z
                Dim dist = CSng(Math.Sqrt(ddx * ddx + ddz * ddz))
                If dist > za.r_m + zb.r_m Then Continue For     ' cheap reject
                If Not WalkIsClear(za, zb, dist, hull_r_m, N) Then Continue For
                link(a).Add(b)
                link(b).Add(a)
                n_links += 1
            Next
        Next

        ready = True
        Dim ms = (Date.UtcNow - t0).TotalMilliseconds
        LogThis("tank zones: {0} zone(s), {1} link(s) for a {2:0.0} m hull in {3:0} ms",
                zones.Count, n_links, hull_r_m, ms)
        Dim widest = 0.0F, mean = 0.0F
        For Each z In zones
            If z.r_m > widest Then widest = z.r_m
            mean += z.r_m
        Next
        LogThis("tank zones:   widest {0:0.0} m, mean {1:0.0} m, isolated {2}",
                widest, mean / zones.Count, CountIsolated())

        ' THE HONEST CONNECTIVITY. Isolated-count misses a walled courtyard
        ' full of mutually-linked discs with no way out, which is unreachable
        ' ground that looks perfectly healthy in every other number here.
        Dim ncomp = 0
        Dim comp = Components(ncomp)
        Dim sizes(ncomp - 1) As Integer
        For i = 0 To comp.Length - 1
            sizes(comp(i)) += 1
        Next
        Array.Sort(sizes)
        Array.Reverse(sizes)
        Dim biggest = If(sizes.Length > 0, sizes(0), 0)
        LogThis("tank zones:   {0} component(s), largest {1} ({2:0.0}% of zones), next {3}",
                ncomp, biggest, 100.0F * biggest / zones.Count,
                If(sizes.Length > 1, sizes(1), 0))
    End Sub

    ''' <summary>
    ''' Can a hull travel the straight line between two disc centres?
    '''
    ''' This is the real question, and overlap is not it. Two circles in a
    ''' corridor barely wider than a tank overlap generously while the ground
    ''' between them will not take a hull. Walking the segment against the
    ''' clearance field asks what is actually being asked, and the field is
    ''' already built, so it costs a handful of lookups.
    ''' </summary>
    Private Function WalkIsClear(a As Zone, b As Zone, dist As Single,
                                 hull_r_m As Single, N As Integer) As Boolean
        Dim steps = CInt(Math.Ceiling(dist / (cell_m_ * 0.5F)))
        If steps < 1 Then Return True
        For s = 0 To steps
            Dim t = CSng(s) / CSng(steps)
            Dim cx = CInt(Math.Round(a.cx + (b.cx - a.cx) * t))
            Dim cz = CInt(Math.Round(a.cz + (b.cz - a.cz) * t))
            If cx < 0 OrElse cz < 0 OrElse cx >= N OrElse cz >= N Then Return False
            ' KEPT. The bounds test above says the CELL is on the grid; this
            ' one says the INDEX is in the array, and the two can disagree only
            ' if the stride is wrong. That is not a hypothetical - a loop
            ' variable named n aliased the N holding the grid size and made
            ' every index here 10x too large. Logged once, so a stride fault
            ' announces itself instead of filling the console.
            Dim wi = cz * N + cx
            If wi < 0 OrElse wi >= clear_m.Length Then
                If Not warned_stride Then
                    warned_stride = True
                    LogThis("tank zones: STRIDE FAULT - idx {0} of {1}, cx {2} cz {3} N {4}",
                            wi, clear_m.Length, cx, cz, N)
                End If
                Return False
            End If
            If clear_m(wi) < hull_r_m Then Return False
        Next
        Return True
    End Function

    ''' <summary>The cell size the field was cut at. Readers outside this
    ''' class need it to turn a radius in metres back into cells.</summary>
    Public ReadOnly Property cell_m As Single
        Get
            Return cell_m_
        End Get
    End Property

    ''' <summary>
    ''' Connected components of the zone graph, one id per zone.
    '''
    ''' "Isolated" counts only zones with no neighbour at all, which badly
    ''' understates how broken up a map is - a walled courtyard holding forty
    ''' mutually-linked discs and no way out counts as zero isolated and is
    ''' every bit as unreachable. This is the honest measure, and a route
    ''' planner needs it: two zones in different components have no path, and
    ''' asking A* to prove that is a full search of the component every time.
    '''
    ''' WHAT THEY TURNED OUT TO BE on 19_monastery, because a count this large
    ''' looks like a fault and is not one. 251 components, every one real ground
    ''' rather than an artefact of the link test:
    '''
    '''   6,696 (64.8%)  the drivable sheet - and INSIDE it every ring road,
    '''                  lane and gap reads connected, so the straight-line
    '''                  link test is not splitting corridors
    '''   977, 748       the shore below the cliffs, west
    '''   223, 174, 84   the same, east
    '''   89             a walled courtyard inside the monastery
    '''   243 others     pockets between the rock bands, 1-5 zones each
    '''
    ''' The shore strips are cut off by a RAVINE, not by a missed link: the
    ''' closest pair across it is 87.5 m apart, the ground between drops from
    ''' 3.6 m to -5.7 m, the bake keys water on the line, and there is a 5.8 m
    ''' rock wall on the far side. Separate ground for a tank, correctly.
    '''
    ''' Two things were suspected and both were wrong. A large component count
    ''' does NOT mean the cover is too sparse - settled by drawing the graph
    ''' coloured by component over the mask rather than by arithmetic. And none
    ''' of it lies outside the arena: every disc centre falls within +/-487.4
    ''' against a boundary at +/-492, so the OFFMAP rule holds. Both were
    ''' answered by measuring, and both measurements said leave it alone.
    ''' </summary>
    Public Function Components(ByRef count As Integer) As Integer()
        Dim comp(zones.Count - 1) As Integer
        For i = 0 To zones.Count - 1
            comp(i) = -1
        Next
        count = 0
        Dim stack As New List(Of Integer)
        For seed = 0 To zones.Count - 1
            If comp(seed) >= 0 Then Continue For
            stack.Clear()
            stack.Add(seed)
            comp(seed) = count
            While stack.Count > 0
                Dim cur = stack(stack.Count - 1)
                stack.RemoveAt(stack.Count - 1)
                For Each nb In link(cur)
                    If comp(nb) >= 0 Then Continue For
                    comp(nb) = count
                    stack.Add(nb)
                Next
            End While
            count += 1
        Next
        Return comp
    End Function

    ''' <summary>Zones with no neighbour at all. A handful is normal - a yard
    ''' behind a wall is genuinely its own island - but a large number means
    ''' the link test is too strict and the graph will not route.</summary>
    Public Function CountIsolated() As Integer
        Dim n = 0
        For i = 0 To link.Count - 1
            If link(i).Count = 0 Then n += 1
        Next
        Return n
    End Function

    ''' <summary>Which zone holds this point, or -1.
    '''
    ''' ONE ARRAY READ, not a scan. The obvious version walks every disc and
    ''' keeps the widest that contains the point, which is O(zones) - 10,334 of
    ''' them on the monastery, for thirty hulls, every frame. That is 18 M
    ''' distance tests a second to answer a question the whole point of this
    ''' class was to make cheap.
    '''
    ''' So the discs are rasterised into zone_of once, at build, and a lookup
    ''' is the same arithmetic TankNav already does to find a cell. Widest
    ''' still wins: zones are emitted widest-first and the paint does not
    ''' overwrite, so the first disc to claim a cell is the roomiest one that
    ''' covers it.</summary>
    Public Function ZoneAt(x As Single, z As Single) As Integer
        If zone_of Is Nothing Then Return -1
        Dim N = TankNav.SIZE
        Dim cx = CInt(Math.Floor((x - frame_x0) / (frame_x1 - frame_x0) * N))
        Dim cz = CInt(Math.Floor((frame_z1 - z) / (frame_z1 - frame_z0) * N))
        If cx < 0 OrElse cz < 0 OrElse cx >= N OrElse cz >= N Then Return -1
        Return zone_of(cz * N + cx)
    End Function

    ''' <summary>The exact 1D squared-distance transform of f into d. v and zz
    ''' are scratch, passed in so the two passes do not allocate 2,048
    ''' short-lived arrays between them.</summary>
    Private Shared Sub dt1d(f() As Single, n As Integer, d() As Single,
                            v() As Integer, zz() As Single)
        Dim k = 0
        v(0) = 0
        zz(0) = -INF
        zz(1) = INF
        For q = 1 To n - 1
            Dim s = ((f(q) + q * q) - (f(v(k)) + v(k) * v(k))) / (2.0F * q - 2.0F * v(k))
            While s <= zz(k)
                k -= 1
                s = ((f(q) + q * q) - (f(v(k)) + v(k) * v(k))) / (2.0F * q - 2.0F * v(k))
            End While
            k += 1
            v(k) = q
            zz(k) = s
            zz(k + 1) = INF
        Next
        k = 0
        For q = 0 To n - 1
            While zz(k + 1) < q
                k += 1
            End While
            Dim dq = CSng(q - v(k))
            d(q) = dq * dq + f(v(k))
        Next
    End Sub

    ''' <summary>
    ''' The zone map as a PNG, because every other part of this pipeline was
    ''' checked by looking at it. A circle that inflated through a wall is
    ''' obvious in a picture and invisible in a count.
    ''' </summary>
    Public Sub Dump()
        If Not ready Then Return
        Try
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "tanks")
            IO.Directory.CreateDirectory(dir)
            Dim path = IO.Path.Combine(dir, map_name & "_zones.png")
            Dim N = TankNav.SIZE

            Using bmp As New Bitmap(N, N, Imaging.PixelFormat.Format24bppRgb)
                ' The clearance field underneath, so the discs can be judged
                ' against the room that was actually there.
                Dim d = bmp.LockBits(New Rectangle(0, 0, N, N),
                                     Imaging.ImageLockMode.WriteOnly,
                                     Imaging.PixelFormat.Format24bppRgb)
                Dim stride = d.Stride
                Dim px(stride * N - 1) As Byte
                For r = 0 To N - 1
                    For c = 0 To N - 1
                        Dim cl = clear_m(r * N + c)
                        Dim g = CByte(Math.Min(255.0F, cl * 8.0F))
                        Dim o = r * stride + c * 3
                        If cl <= 0.0F Then
                            px(o) = 20 : px(o + 1) = 16 : px(o + 2) = 16
                        Else
                            px(o) = CByte(g \ 3) : px(o + 1) = g : px(o + 2) = CByte(g \ 2)
                        End If
                    Next
                Next
                Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length)
                bmp.UnlockBits(d)

                Using gfx = Graphics.FromImage(bmp)
                    gfx.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Using linkpen As New Pen(Color.FromArgb(90, 120, 200, 255), 1.0F)
                        For a = 0 To zones.Count - 1
                            For Each b In link(a)
                                If b < a Then Continue For
                                gfx.DrawLine(linkpen, zones(a).cx, zones(a).cz,
                                                      zones(b).cx, zones(b).cz)
                            Next
                        Next
                    End Using
                    Using pen As New Pen(Color.FromArgb(120, 255, 200, 60), 1.0F)
                        For Each z In zones
                            Dim rc = z.r_m / cell_m_
                            gfx.DrawEllipse(pen, z.cx - rc, z.cz - rc, rc * 2.0F, rc * 2.0F)
                        Next
                    End Using
                    Using dot As New SolidBrush(Color.FromArgb(255, 255, 90, 90))
                        For Each z In zones
                            gfx.FillRectangle(dot, z.cx - 1, z.cz - 1, 3, 3)
                        Next
                    End Using
                End Using

                bmp.Save(path, Imaging.ImageFormat.Png)
            End Using
            LogThis("tank zones: wrote {0}", path)
        Catch ex As Exception
            LogThis("tank zones: could not write png - {0}", ex.Message)
        End Try
    End Sub
    ''' <summary>
    ''' The zone map as a file, for readers OUTSIDE this app.
    '''
    ''' The Path Studio session reads this the way it reads the bake, so the
    ''' bake's own lesson applies: a field added quietly reads as garbage on
    ''' the far side, and a field removed reads as zero, which is worse because
    ''' it looks fine. Everything needed to interpret a row is therefore stated
    ''' in the header rather than assumed - the frame, the cell size, the body
    ''' radius the discs were cut for, and the two rules that decide what a
    ''' radius and a link actually MEAN. A reader that re-derives either of
    ''' those is a reader that will one day derive them differently.
    '''
    ''' It goes beside the bake rather than with the debug PNGs, because it is
    ''' a data product and not an eyeball aid. One file per MASK: a tank's free
    ''' space and a camera's are different questions - a camera flies over what
    ''' a tank must go around - and they must never share a file.
    ''' </summary>
    Public Sub WriteCsv()
        If Not ready Then Return
        Try
            Dim inv = Globalization.CultureInfo.InvariantCulture
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "flight")
            IO.Directory.CreateDirectory(dir)
            Dim path = IO.Path.Combine(dir, map_name & "_zones_tank.csv")

            ' PROVENANCE. A zone map cut from a stale bake is the same trap as
            ' a stale bake and harder to see, being one step further from the
            ' thing that made it. The bake carries no provenance keys of its
            ' own yet; until it does, the best evidence available is when its
            ' meta was written, and reporting "unknown" is better than implying
            ' a freshness that cannot be shown.
            Dim meta = IO.Path.Combine(dir, map_name & "_meta.txt")
            Dim bake_when = "unknown"
            If IO.File.Exists(meta) Then
                bake_when = IO.File.GetLastWriteTimeUtc(meta).ToString("yyyy-MM-ddTHH:mm:ssZ", inv)
            End If

            Dim sb As New System.Text.StringBuilder
            sb.AppendLine("# nuTerra zone map - free space as discs")
            sb.AppendLine("# Cut from the tank navigation grid. The algorithm and its traps: TankZones.vb")
            sb.AppendLine(String.Format(inv, "map={0}", map_name))
            sb.AppendLine("mask=tank")
            sb.AppendLine("mask_rule=impassable is blocked|steep|outland|trunk|water|pinned|offmap; the obstacle-height test is skipped for tree, fence and prop kinds, and a tree is answered by the trunk bit instead")
            sb.AppendLine(String.Format(inv, "grid={0}", TankNav.SIZE))
            sb.AppendLine(String.Format(inv, "cell_m={0:0.0000}", cell_m_))
            sb.AppendLine(String.Format(inv, "body_r_m={0:0.000}", hull_r))
            sb.AppendLine(String.Format(inv, "wx_min={0:0.000}", frame_x0))
            sb.AppendLine(String.Format(inv, "wx_max={0:0.000}", frame_x1))
            sb.AppendLine(String.Format(inv, "wz_min={0:0.000}", frame_z0))
            sb.AppendLine(String.Format(inv, "wz_max={0:0.000}", frame_z1))
            sb.AppendLine("radius_rule=sqrt(distance transform) minus half a cell, then scaled to metres. The transform measures to the nearest blocked cell CENTRE and that cell is solid across its whole area, so the raw distance would overlap the very thing it was measured against")
            sb.AppendLine("link_rule=the straight line between two centres walked at half-cell steps, demanding full body clearance the whole way. NOT mere overlap - in a corridor barely wider than the body every disc overlaps its neighbour while the safe width is nil")
            sb.AppendLine(String.Format(inv, "bake_meta_written={0}", bake_when))
            sb.AppendLine(String.Format(inv, "written={0}",
                                        Date.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", inv)))
            sb.AppendLine(String.Format(inv, "zones={0}", zones.Count))
            sb.AppendLine(String.Format(inv, "links={0}", n_links))
            sb.AppendLine("#")
            sb.AppendLine("# x,z are world metres in the frame above. y_m is terrain height at the")
            sb.AppendLine("# centre. neighbours is a space-separated list of ids and may be empty,")
            sb.AppendLine("# which means the disc is an island - reachable ground with no way out")
            sb.AppendLine("# for a body of this radius, not an error.")
            sb.AppendLine("id,x,z,r_m,y_m,neighbours")

            For i = 0 To zones.Count - 1
                Dim zn = zones(i)
                Dim y = get_Y_at_XZ_fast(zn.x, zn.z)
                sb.AppendLine(String.Format(inv, "{0},{1:0.000},{2:0.000},{3:0.000},{4:0.000},{5}",
                                            i, zn.x, zn.z, zn.r_m, y, String.Join(" ", link(i))))
            Next

            IO.File.WriteAllText(path, sb.ToString())
            LogThis("tank zones: wrote {0} ({1} disc(s), {2} link(s))", path, zones.Count, n_links)
        Catch ex As Exception
            LogThis("tank zones: could not write csv - {0}", ex.Message)
        End Try
    End Sub
End Class

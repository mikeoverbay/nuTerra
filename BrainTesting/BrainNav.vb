Imports OpenTK.Mathematics

''' <summary>
''' What the world knows about getting around it: ground height, and whether a
''' disc of a given radius can stand somewhere.
'''
''' ONE NOTION OF BLOCKED, BOTH USES - Tank AI work's condition, and the one
''' thing here that must hold. If a brain traces an obstacle boundary using a
''' different rule from the one that stops a hull, it is scoring a different
''' map from the one it drives on. So the occupancy grid below is the single
''' source: standable() reads it, and whatever eventually stops a tank must
''' read it too.
'''
''' A 1 m GRID, BUILT ONCE. Testing 6,254 instance boxes per query would be
''' 200 queries x 6,254 boxes per blocked event; rasterising them into a grid
''' at load makes each query a handful of cell reads. 1 m because that is the
''' resolution the path graph and the flight bake already use - a third
''' resolution would be a third answer.
'''
''' CRUSHABLE GROUND IS NOT BLOCKED. A tank drives through a hedge. The rule is
''' the bake's: kind TREE is crushable, everything else standing is not. That
''' is why the grid is built from ModelKind and not from geometry alone - and
''' it is the same rule the vineyard fix turned on.
'''
''' Added 2026-09-16 by nuTerra work, stage 6 of docs/brain_testing_plan.md.
''' </summary>
Module BrainNav

    ''' <summary>Metres a cell. Matches the path graph's dedupe and the flight
    ''' bake, deliberately.</summary>
    Public Const CELL_M As Single = 1.0F

    ''' <summary>The radius a boundary TRACE should use - one cell. Small on
    ''' purpose: a hull-sized disc rounds every corner off and walks past the
    ''' gaps the trace exists to find.</summary>
    Public Const TRACE_R As Single = 0.5F

    ''' <summary>Rise over run a hull will not climb. 0.7 is about 35 degrees;
    ''' a tier 10 gives up well before that, so this is a floor, not a
    ''' simulation, and it is here so "blocked" includes terrain rather than
    ''' only objects.</summary>
    Public Const MAX_SLOPE As Single = 0.7F

    ''' <summary>nuTerra's own OUTLAND_MARGIN, from MapLoader.</summary>
    Private Const OUTLAND_MARGIN As Single = 25.0F

    Private occ() As Byte              ' 1 = something solid stands here
    Private w, h As Integer
    Private x0, z0 As Single           ' world position of cell (0,0)
    Public Ready As Boolean = False

    ''' <summary>Cells marked, for the log - a grid that marks nothing is a
    ''' grid that will call the whole map drivable.</summary>
    Public Marked As Integer = 0

    ''' <summary>Rank the placements by how much ground they claim. A
    ''' diagnostic, off unless `navaudit` is on the command line.</summary>
    Public NAV_AUDIT As Boolean = False

    ''' <summary>
    ''' Rasterise every placement's footprint into the grid.
    '''
    ''' The footprint is the world-space AABB of the local box, which OVER
    ''' states a rotated building's true shape - a 45 degree wall claims its
    ''' bounding square. That is deliberate at this stage: it errs toward
    ''' "blocked", and a brain that refuses a gap that exists is a worse day
    ''' than a brain that drives into a wall is a worse day than a brain that
    ''' is confidently wrong. Tighten it when a real route needs it.
    ''' </summary>
    Public Sub Build()
        Ready = False
        Marked = 0
        If MAP_MODELS Is Nothing OrElse MODEL_INDEX_LIST Is Nothing Then Return

        ' The play field, from the arena box the terrain already read.
        Dim lo = MAP_BB_BL, hi = MAP_BB_UR
        If hi.X - lo.X < 1.0F OrElse hi.Y - lo.Y < 1.0F Then
            LogThis("brain: no arena box - the nav grid cannot be sized")
            Return
        End If

        x0 = lo.X : z0 = lo.Y
        w = CInt(Math.Ceiling((hi.X - lo.X) / CELL_M))
        h = CInt(Math.Ceiling((hi.Y - lo.Y) / CELL_M))
        ReDim occ(w * h - 1)

        Dim sw = Stopwatch.StartNew()
        Dim skippedCrushable = 0
        Dim skippedOutland = 0

        ' WHICH FRAME ARE THE FOOTPRINTS IN? This project negates X between
        ' the arena file and the world, and gets it wrong regularly enough
        ' that TerrainBuilder's own comment warns two readers can each be
        ' internally consistent and still disagree by that sign. So the extent
        ' of every placement is measured and printed beside the arena box: if
        ' they overlap, the frames agree; if one is the other's negation, they
        ' do not, and the grid would come out mirrored - which looks like a
        ' nav grid that works and blocks the wrong half of the map.
        Dim fx0 = Single.MaxValue, fx1 = Single.MinValue
        Dim fz0 = Single.MaxValue, fz1 = Single.MinValue
        For Each inst In MODEL_INDEX_LIST
            Dim b = BrainModels.WorldFootprint(inst)
            If Not b.ok Then Continue For
            fx0 = Math.Min(fx0, b.minX) : fx1 = Math.Max(fx1, b.maxX)
            fz0 = Math.Min(fz0, b.minZ) : fz1 = Math.Max(fz1, b.maxZ)
        Next
        LogThis("brain: placements span X {0:0} to {1:0}, Z {2:0} to {3:0}; " &
                "arena box X {4:0} to {5:0}, Z {6:0} to {7:0}",
                fx0, fx1, fz0, fz1, lo.X, hi.X, lo.Y, hi.Y)

        For Each inst In MODEL_INDEX_LIST
            Dim box = BrainModels.WorldFootprint(inst)
            If Not box.ok Then Continue For

            ' OUTLAND SCENERY IS NOT AN OBSTACLE, and leaving it in is what
            ' made the first grid call 60.1% of the map blocked. The mountains
            ' ringing a map are ordinary placements with enormous bounding
            ' boxes, and an axis-aligned box round a mountain sprawls right
            ' across the play field.
            '
            ' The test is nuTerra's own, from MapLoader.batch_is_outland: a
            ' placement is outland when its POSITION lies outside the arena box
            ' plus a 25 m margin. Measured on monastery, placements span X
            ' -6771 to 3034 against an arena box of +/-500, so this is the
            ' difference between a usable grid and a wall.
            Dim p = inst.matrix.Row3
            If p.X < lo.X - OUTLAND_MARGIN OrElse p.X > hi.X + OUTLAND_MARGIN OrElse
               p.Z < lo.Y - OUTLAND_MARGIN OrElse p.Z > hi.Y + OUTLAND_MARGIN Then
                skippedOutland += 1
                Continue For
            End If

            ' THE CRUSHABLE RULE, the bake's own: a tank drives through trees
            ' and hedges. Anything else standing stops it.
            If box.kind = ModelKind.KIND_TREE Then
                skippedCrushable += 1
                Continue For
            End If

            Dim cx0 = CInt(Math.Floor((box.minX - x0) / CELL_M))
            Dim cx1 = CInt(Math.Ceiling((box.maxX - x0) / CELL_M))
            Dim cz0 = CInt(Math.Floor((box.minZ - z0) / CELL_M))
            Dim cz1 = CInt(Math.Ceiling((box.maxZ - z0) / CELL_M))
            ' THE ORIENTED QUAD, not its bounding rectangle. Marking the AABB
            ' had 51.2% of monastery blocked - no single placement was to
            ' blame, it was six thousand of them each over-claiming: a 20 m
            ' fence at 45 degrees owns a 14x14 m square that way, ten times
            ' the ground it stands on. The cell's CENTRE decides, so a cell is
            ' blocked when the obstacle actually covers the middle of it.
            For cz = Math.Max(cz0, 0) To Math.Min(cz1, h - 1)
                Dim rowBase = cz * w
                Dim wz = z0 + (cz + 0.5F) * CELL_M
                For cx = Math.Max(cx0, 0) To Math.Min(cx1, w - 1)
                    If occ(rowBase + cx) <> 0 Then Continue For
                    Dim wx = x0 + (cx + 0.5F) * CELL_M
                    If Not InQuad(wx, wz, box) Then Continue For
                    occ(rowBase + cx) = 1
                    Marked += 1
                Next
            Next
        Next

        ' WHAT CAN A TANK DRIVE OVER, AND WHAT STOPS IT. The owner's question,
        ' and the grid is the only thing that can answer it - so it reports by
        ' KIND, counting PLACEMENTS rather than models, because fifteen copies
        ' of one fence stop a tank fifteen times.
        Dim pl(7) As Integer
        Dim cells(7) As Integer
        Dim otherNames As New Dictionary(Of String, Integer)
        For Each inst In MODEL_INDEX_LIST
            Dim b = BrainModels.WorldFootprint(inst)
            If Not b.ok Then Continue For
            Dim p3 = inst.matrix.Row3
            If p3.X < lo.X - OUTLAND_MARGIN OrElse p3.X > hi.X + OUTLAND_MARGIN OrElse
               p3.Z < lo.Y - OUTLAND_MARGIN OrElse p3.Z > hi.Y + OUTLAND_MARGIN Then Continue For
            Dim k = b.kind And 7
            pl(k) += 1
            cells(k) += CInt((b.maxX - b.minX) * (b.maxZ - b.minZ))
            If k = ModelKind.KIND_OTHER Then
                ' The folder is what tells a reader what a thing IS - the leaf
                ' filename is usually a variant number.
                Dim f = b.asset
                If f IsNot Nothing Then
                    Dim parts = f.Split("/"c)
                    If parts.Length >= 2 Then f = parts(parts.Length - 2)
                    otherNames(f) = If(otherNames.ContainsKey(f), otherNames(f), 0) + 1
                End If
            End If
        Next

        LogThis("brain: --- what stops a tank on this map ---")
        For k = 0 To 7
            If pl(k) = 0 Then Continue For
            LogThis("brain:   {0,-9} {1,6:N0} placement(s), ~{2,7:N0} m2  {3}",
                    ModelKind.KIND_NAMES(k), pl(k), cells(k),
                    If(k = ModelKind.KIND_TREE, "CRUSHABLE - driven over", "blocks"))
        Next
        LogThis("brain:   {0,-9} {1,6:N0} placement(s), {2}  CRUSHABLE - driven over",
                "speedtree", BrainTrees.Count, "proxy")

        ' THE GREY BIN, NAMED. "other" is the classifier saying it does not
        ' recognise the name, and everything in it currently BLOCKS - so if
        ' something drivable is in there, the map is harder than it looks and
        ' nobody would know which thing to blame.
        If otherNames.Count > 0 Then
            Dim rank = otherNames.ToList()
            rank.Sort(Function(a, b2) b2.Value.CompareTo(a.Value))
            LogThis("brain:   ...of which 'other' is {0} distinct folder(s), top:", rank.Count)
            For i = 0 To Math.Min(11, rank.Count - 1)
                LogThis("brain:     {0,5:N0} x {1}", rank(i).Value, rank(i).Key)
            Next
        End If

        ' WHICH PLACEMENTS COST THE MOST GROUND. Printed because the first
        ' two guesses at why the grid was over half blocked were both wrong,
        ' and a ranked list answers it in one run instead of three.
        If NAV_AUDIT Then
            Dim rank As New List(Of Tuple(Of Single, String))
            For Each inst In MODEL_INDEX_LIST
                Dim b = BrainModels.WorldFootprint(inst)
                If Not b.ok Then Continue For
                Dim p2 = inst.matrix.Row3
                If p2.X < lo.X - OUTLAND_MARGIN OrElse p2.X > hi.X + OUTLAND_MARGIN OrElse
                   p2.Z < lo.Y - OUTLAND_MARGIN OrElse p2.Z > hi.Y + OUTLAND_MARGIN Then Continue For
                If b.kind = ModelKind.KIND_TREE Then Continue For
                Dim area = (b.maxX - b.minX) * (b.maxZ - b.minZ)
                rank.Add(Tuple.Create(area, String.Format("{0} {1:0}x{2:0} m",
                    ModelKind.KIND_NAMES(b.kind And 7), b.maxX - b.minX, b.maxZ - b.minZ)))
            Next
            rank.Sort(Function(a, b2) b2.Item1.CompareTo(a.Item1))
            For i = 0 To Math.Min(9, rank.Count - 1)
                LogThis("brain:   #{0} {1:N0} m2 - {2}", i + 1, rank(i).Item1, rank(i).Item2)
            Next
        End If

        Ready = True
        LogThis("brain: nav grid {0}x{1} at {2} m in {3} ms - {4:N0} cell(s) blocked " &
                "({5:0.0}%), {6:N0} crushable and {7:N0} outland left open",
                w, h, CELL_M, sw.ElapsedMilliseconds, Marked,
                100.0 * Marked / Math.Max(w * h, 1), skippedCrushable, skippedOutland)
    End Sub

    ''' <summary>
    ''' Is this world point inside the footprint quad?
    '''
    ''' Cross products against each edge in order. The quad is convex and
    ''' wound consistently, so a point inside is on the same side of all
    ''' four; the sign itself is not fixed because a mirrored placement
    ''' winds the other way, which is why this tests "all the same" rather
    ''' than "all positive".
    ''' </summary>
    Private Function InQuad(x As Single, z As Single, f As BrainModels.Footprint) As Boolean
        Dim pos = 0, neg = 0
        Dim c = {f.c0, f.c1, f.c2, f.c3}
        For i = 0 To 3
            Dim a = c(i), b = c((i + 1) And 3)
            Dim cross = (b.X - a.X) * (z - a.Y) - (b.Y - a.Y) * (x - a.X)
            If cross > 0.0F Then pos += 1
            If cross < 0.0F Then neg += 1
        Next
        Return pos = 0 OrElse neg = 0
    End Function

    ''' <summary>
    ''' Does the grid agree with the ground the hulls are standing on?
    '''
    ''' THE PERCENTAGE BLOCKED IS A PROXY AND A BAD ONE. A map can be 36%
    ''' covered and perfectly drivable, or 10% covered with every route
    ''' pinched shut. What matters is whether the ground the test actually
    ''' uses is open, so this asks exactly that: can each hull stand where it
    ''' spawned, and how much of the straight line between the two bases is
    ''' clear for a hull-sized disc.
    '''
    ''' A spawn that reports blocked is the loud failure - the tank is
    ''' visibly standing there.
    ''' </summary>
    Public Sub SelfCheck()
        If Not Ready OrElse BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return

        Dim blocked = 0
        Dim worst = ""
        For Each b In BrainTanks.Bodies
            Dim r = CSng(Math.Sqrt(b.half.X * b.half.X + b.half.Z * b.half.Z)) + 0.3F
            If Not Standable(b.spawn.X, b.spawn.Y, r) Then
                blocked += 1
                If worst = "" Then worst = b.tag
            End If
        Next

        ' The base-to-base line, sampled at a metre. Not a route - a route is a
        ' brain's job - but if even a tenth of the straight line is open the
        ' grid is not a solid wall, and if none of it is, it is.
        Dim open_ = 0, total = 0
        If BrainTanks.HasBases Then
            Dim a = BrainTanks.Base1, c = BrainTanks.Base2
            Dim d = c - a
            total = CInt(d.Length)
            For i = 0 To total
                Dim p = a + d * (i / CSng(Math.Max(total, 1)))
                If Standable(p.X, p.Y, 2.0F) Then open_ += 1
            Next
        End If

        LogThis("brain: nav check - {0}/{1} spawn(s) blocked{2}; base-to-base line {3:0}% open",
                blocked, BrainTanks.Bodies.Count,
                If(blocked = 0, "", " (first: " & worst & ")"),
                If(total > 0, 100.0 * open_ / total, 0.0))
    End Sub

    ''' <summary>Ground height, straight from the terrain the app drew - so a
    ''' brain and the picture agree.</summary>
    Public Function Ground(x As Single, z As Single) As Single
        If map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return 0.0F
        Try
            Return get_Y_at_XZ(x, z)
        Catch
            Return 0.0F
        End Try
    End Function

    ''' <summary>
    ''' Can a disc of this radius stand here?
    '''
    ''' THE RADIUS IS THE CALLER'S, never a fixed hull constant. A boundary
    ''' trace passes TRACE_R; a fit test passes the hull's own FitRadius. One
    ''' function, two questions - see the plan's seam section for why using one
    ''' radius for both silently ruins the trace.
    ''' </summary>
    Public Function Standable(x As Single, z As Single, radius As Single) As Boolean
        If Not Ready Then Return True          ' nothing known: refuse nothing

        Dim r = Math.Max(radius, 0.0F)
        Dim cx0 = CInt(Math.Floor((x - r - x0) / CELL_M))
        Dim cx1 = CInt(Math.Floor((x + r - x0) / CELL_M))
        Dim cz0 = CInt(Math.Floor((z - r - z0) / CELL_M))
        Dim cz1 = CInt(Math.Floor((z + r - z0) / CELL_M))

        ' Off the grid is off the map, which is not standable - a hull that
        ' leaves the arena box has left the test.
        If cx0 < 0 OrElse cz0 < 0 OrElse cx1 >= w OrElse cz1 >= h Then Return False

        For cz = cz0 To cz1
            Dim rowBase = cz * w
            For cx = cx0 To cx1
                If occ(rowBase + cx) <> 0 Then Return False
            Next
        Next

        ' And the ground itself. Sampled across the disc rather than at the
        ' centre: a hull straddling a ditch is stopped by the ditch, and a
        ' centre sample sits happily in the bottom of it.
        Dim c = Ground(x, z)
        For Each d In {New Vector2(r, 0.0F), New Vector2(-r, 0.0F),
                       New Vector2(0.0F, r), New Vector2(0.0F, -r)}
            If Math.Abs(Ground(x + d.X, z + d.Y) - c) / Math.Max(r, 0.01F) > MAX_SLOPE Then
                Return False
            End If
        Next
        Return True
    End Function

    ''' <summary>
    ''' Is the straight line from a to b clear for a disc of this radius?
    '''
    ''' Built ON Standable rather than beside it, so there is no second rule to
    ''' drift. Stepped at half a cell: a full cell can jump a one-cell wall
    ''' diagonally, which is the classic way a line test misses a fence.
    ''' </summary>
    Public Function Clear(a As Vector2, b As Vector2, radius As Single) As Boolean
        Dim d = b - a
        Dim len = d.Length
        If len < 0.0001F Then Return Standable(a.X, a.Y, radius)
        Dim steps = CInt(Math.Ceiling(len / (CELL_M * 0.5F)))
        For i = 0 To steps
            Dim t = i / CSng(steps)
            Dim p = a + d * t
            If Not Standable(p.X, p.Y, radius) Then Return False
        Next
        Return True
    End Function

End Module

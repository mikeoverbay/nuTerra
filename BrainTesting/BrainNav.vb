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
''' CRUSHABLE GROUND IS NOT BLOCKED. A tank drives through a hedge. The rule
''' is the bake's, and it now splits a tree in two: canopy is crushable, the
''' TRUNK IS NOT. The owner, 2026-09-16: "if it has a trunk, we can't drive
''' there. if no trunk we can."
'''
''' THE FALLBACK IN THIS FILE CANNOT MAKE THAT SPLIT. It rasterises model
''' bounding boxes and has no trunk bit, so it treats every tree as fully
''' crushable and under-blocks a wood. The square map from the bake is the
''' one that knows, which is another reason it is preferred over anything
''' worked out here.
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
    ''' <summary>
    ''' Bit 0 of a square byte is the block flag, and the test is a MASK
    ''' rather than a comparison against zero.
    '''
    ''' The owner is moving the square data to a bitmask - "bit 0 is our block
    ''' flag. all others are for what it is" - and the failure mode of a
    ''' non-zero test under that format is silent and INVERTED: open ground
    ''' carrying a kind in the upper bits is a non-zero byte, so the map FILLS
    ''' IN rather than emptying. On a 1,400 square grid that reads as a bad
    ''' bake rather than a bad decode.
    '''
    ''' Harmless today, while the byte this file reads is still 0 or 1. Done
    ''' now so this reader does not have to be FOUND on the day the format
    ''' lands - Tank AI work made the same change to their Python at d7281ab8.
    ''' </summary>
    Private Const BLOCK_BIT As Byte = &H1

    Public Ready As Boolean = False

    ''' <summary>Cells marked, for the log - a grid that marks nothing is a
    ''' grid that will call the whole map drivable.</summary>
    Public Marked As Integer = 0

    ''' <summary>True when the grid came from nuTerra's bake rather than from
    ''' this file's own footprint rasteriser. Said out loud at load.</summary>
    Public FromBake As Boolean = False

    ''' <summary>
    ''' The grid's own frame, so a caller can say WHICH CELL rather than only
    ''' blocked or not.
    '''
    ''' Added for BrainRadar, which draws the square a ray landed in: a
    ''' distance cannot be checked against the map by eye and a highlighted
    ''' cell can. Read-only on purpose - the grid owns these, and a second
    ''' writer is how two parts of one app end up describing different maps.
    ''' </summary>
    Public ReadOnly Property CellX0 As Single
        Get
            Return x0
        End Get
    End Property

    ''' <summary>Rows run DOWNWARD from this: a larger z is a smaller row.</summary>
    Public ReadOnly Property CellZTop As Single
        Get
            Return If(FromBake, z_top, z0)
        End Get
    End Property

    Public ReadOnly Property CellSize As Single
        Get
            Return If(FromBake, sq_cell, CELL_M)
        End Get
    End Property

    ''' <summary>Metres a cell as the loaded map states it, and the world Z of
    ''' row 0. The bake's rows run DOWNWARD from wz_max.</summary>
    Private sq_cell As Single = CELL_M
    Private z_top As Single = 0.0F

    ''' <summary>Rank the placements by how much ground they claim. A
    ''' diagnostic, off unless `navaudit` is on the command line.</summary>
    Public NAV_AUDIT As Boolean = False

    ''' <summary>
    ''' Count the d_ / n_ material names, to check a claim rather than adopt
    ''' it.
    '''
    ''' Tank AI work found the owner's crushable convention living inside
    ''' .visual_processed as render-set material names, which is why neither
    ''' of us saw it on a file path. They read it out of the pkg; this reads
    ''' it from the space.bin MATERIAL TABLE, which resolves the same strings
    ''' through cBWST and is already linked here - a second source for the
    ''' same fact, which is the only reason to spend the code.
    '''
    ''' It changes nothing. A measurement that would rewrite the crushable
    ''' rule is the owner's call, and the last time somebody read a kind off
    ''' a remembered table it turned rock into fence.
    ''' </summary>
    Public Sub MaterialAudit()
        Try
            If cBSMA.MaterialItem Is Nothing OrElse cBSMA.MaterialItem.Length = 0 Then
                LogThis("brain: no material table in this space")
                Return
            End If
            ' EVERY PREFIX, not just the two we were told about. The havok
            ' files name bodies n_ / c_ / s_ - undamaged, broken and static -
            ' so counting only d_ and n_ put the other two in a bin called
            ' "neither" and hid them.
            Dim d = 0, n = 0, cc = 0, ss = 0, other = 0
            Dim csample As New List(Of String)
            Dim ssample As New List(Of String)
            Dim osample As New List(Of String)
            Dim seen As New HashSet(Of String)
            Dim sample As New List(Of String)
            For Each m In cBSMA.MaterialItem
                Dim id = m.identifier
                If String.IsNullOrEmpty(id) Then Continue For
                If Not seen.Add(id) Then Continue For
                If id.StartsWith("d_", StringComparison.Ordinal) Then
                    d += 1
                    If sample.Count < 6 Then sample.Add(id)
                ElseIf id.StartsWith("n_", StringComparison.Ordinal) Then
                    n += 1
                    If sample.Count < 6 Then sample.Add(id)
                ElseIf id.StartsWith("c_", StringComparison.Ordinal) Then
                    cc += 1
                    If csample.Count < 4 Then csample.Add(id)
                ElseIf id.StartsWith("s_", StringComparison.Ordinal) Then
                    ss += 1
                    If ssample.Count < 4 Then ssample.Add(id)
                Else
                    other += 1
                    If osample.Count < 4 Then osample.Add(id)
                End If
            Next
            LogThis("brain: material names - {0} distinct: n_ {1}, d_ {2}, c_ {3}, s_ {4}, other {5}",
                    seen.Count, n, d, cc, ss, other)
            If csample.Count > 0 Then LogThis("brain:   c_ e.g. {0}", String.Join(", ", csample))
            If ssample.Count > 0 Then LogThis("brain:   s_ e.g. {0}", String.Join(", ", ssample))
            If osample.Count > 0 Then LogThis("brain:   other e.g. {0}", String.Join(", ", osample))

            ' DRAWN OR NOT, CROSSED WITH THE PREFIX. apply_material_for_pgroup
            ' sets no_draw when a material has no shader properties or no
            ' effect - that is the COLLISION geometry, and it is already
            ' excluded everywhere. The question this answers is the owner's:
            ' whether d_ sets are also excluded, or whether they are real
            ' drawable geometry being drawn on top of the intact model.
            Dim dDraw = 0, dSkip = 0, nDraw = 0, nSkip = 0, oDraw = 0, oSkip = 0
            For Each m In cBSMA.MaterialItem
                Dim id = If(m.identifier, "")
                Dim skip = (m.shaderPropBegin = &HFFFFFFFFUI) OrElse
                           (m.effectIndex = &HFFFFFFFFUI)
                If id.StartsWith("d_", StringComparison.Ordinal) Then
                    If skip Then dSkip += 1 Else dDraw += 1
                ElseIf id.StartsWith("n_", StringComparison.Ordinal) Then
                    If skip Then nSkip += 1 Else nDraw += 1
                Else
                    If skip Then oSkip += 1 Else oDraw += 1
                End If
            Next
            LogThis("brain:   drawable / no_draw   d_ {0}/{1}   n_ {2}/{3}   neither {4}/{5}",
                    dDraw, dSkip, nDraw, nSkip, oDraw, oSkip)
            If sample.Count > 0 Then
                LogThis("brain:   e.g. {0}", String.Join(", ", sample))
            End If
        Catch ex As Exception
            LogThis("brain: material audit failed - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' THE SQUARE MAP THE PROJECT ALREADY BUILDS, in preference to anything
    ''' worked out here.
    '''
    ''' nuTerra cuts <map>_squares.u8 out of the flight bake - one byte a
    ''' metre, 1 for solid, 0 for open - and its rule is the owner's, settled
    ''' over several rounds and written into TankSquares:
    '''
    '''     outland and water always block;
    '''     fence and prop are crushable whatever their height;
    '''     TREE is crushable as CANOPY only - a texel carrying the trunk
    '''       bit blocks, and so does one carrying the solid bit, which is
    '''       rock or wall standing under the canopy;
    '''     everything else blocks if it stands over the obstacle height.
    '''
    ''' READING IT RATHER THAN RE-DERIVING IT IS THE WHOLE POINT. It comes
    ''' from the RENDERED DEPTH, not from bounding boxes, so it does not have
    ''' the over-claim this file's own rasteriser does - measured, rock alone
    ''' claimed 1.16 million square metres on a map with 1.00 million. And it
    ''' is the same array the tank driving reads, which is Tank AI work's one
    ''' condition on the seam: a brain that traces an obstacle boundary and a
    ''' hull that stops on it must not be consulting two different maps.
    '''
    ''' The fallback stays for a map with no bake yet, and it says which one
    ''' is in use - guessing which grid answered a question is not something
    ''' anyone should have to do from the outside.
    ''' </summary>
    Public Function LoadSquares(map As String) As Boolean
        Ready = False
        Marked = 0
        FromBake = False
        Try
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "flight")
            Dim u8 = IO.Path.Combine(dir, map & "_squares.u8")
            Dim txt = IO.Path.Combine(dir, map & "_squares.txt")
            If Not IO.File.Exists(u8) OrElse Not IO.File.Exists(txt) Then
                LogThis("brain: no square map for {0} - falling back to footprints", map)
                Return False
            End If

            Dim meta As New Dictionary(Of String, String)
            For Each line In IO.File.ReadAllLines(txt)
                Dim eq = line.IndexOf("="c)
                If eq > 0 Then meta(line.Substring(0, eq).Trim()) = line.Substring(eq + 1).Trim()
            Next

            Dim nn = CInt(dbl(meta, "n", 0))
            Dim cell = CSng(dbl(meta, "cell_m", 1.0))
            Dim wxmin = CSng(dbl(meta, "wx_min", 0))
            Dim wzmax = CSng(dbl(meta, "wz_max", 0))
            If nn < 8 Then
                LogThis("brain: square map meta has no usable n - falling back")
                Return False
            End If

            Dim bytes = IO.File.ReadAllBytes(u8)
            If bytes.Length <> nn * nn Then
                ' Size is the only cheap check that the meta and the data are
                ' the same bake. A mismatched pair would index happily and
                ' answer about the wrong map.
                LogThis("brain: square map is {0:N0} bytes, meta says {1}x{1}={2:N0} - falling back",
                        bytes.Length, nn, CLng(nn) * nn)
                Return False
            End If

            occ = bytes
            w = nn : h = nn
            sq_cell = cell
            x0 = wxmin
            ' ROW 0 IS wz_MAX AND ROWS GO DOWNWARD - the meta says so, and it
            ' also says the label used to claim wz_min, "which is the opposite
            ' and cost a reader an hour". Worth reading twice.
            z_top = wzmax
            For Each b In bytes
                If b <> 0 Then Marked += 1
            Next
            Ready = True
            FromBake = True
            LogThis("brain: square map {0}x{0} at {1} m from the bake - {2:N0} solid ({3:0.0}%)",
                    nn, cell, Marked, 100.0 * Marked / (CLng(nn) * nn))
            Return True
        Catch ex As Exception
            LogThis("brain: square map would not load - {0}", ex.Message)
            Return False
        End Try
    End Function

    Private Function dbl(m As Dictionary(Of String, String), k As String, dflt As Double) As Double
        Dim v = ""
        If Not m.TryGetValue(k, v) Then Return dflt
        Dim d As Double
        If Double.TryParse(v, Globalization.NumberStyles.Float,
                           Globalization.CultureInfo.InvariantCulture, d) Then Return d
        Return dflt
    End Function

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

            ' THE CRUSHABLE RULE, as far as a bounding box can carry it: a
            ' tank drives through trees and hedges. A trunk stops it, and
            ' this path cannot see trunks - see the note at the top.
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
                    If (occ(rowBase + cx) And BLOCK_BIT) <> 0 Then Continue For
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
                    If(k = ModelKind.KIND_TREE, "canopy crushable, trunk blocks", "blocks"))
        Next
        LogThis("brain:   {0,-9} {1,6:N0} placement(s), {2}  canopy crushable, trunk blocks",
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
    ''' <summary>
    ''' ONE TICK'S WORTH OF GROUND SAMPLES.
    '''
    ''' Standable samples ground at five points every call, and a 28-ray scan
    ''' walks 0.5 m at a time to 20 m - about 5,600 terrain queries a tick, all
    ''' of them going through get_Y_at_XZ into the live scene. Measured on the
    ''' heartbeat at "ai 79.79 ms", which is the brain alone eating five frames
    ''' at 60 Hz.
    '''
    ''' Most of those are the SAME point. Consecutive steps are half a metre
    ''' apart and the slope test samples at plus and minus the radius, so the
    ''' samples land on each other over and over. Quantised to a quarter metre
    ''' and remembered for the tick, the repeats cost a dictionary probe.
    '''
    ''' THIS IS A PATCH, NOT THE FIX. The fix is to read the height plane the
    ''' flight bake already writes, which makes Ground an array index and this
    ''' cache pointless. That crosses into the bake's file format, so it is a
    ''' conversation with the nuTerra lane rather than an edit here.
    ''' </summary>
    ''' <summary>Called once a tick. Ground is only stable WITHIN a tick -
    ''' nothing moves the terrain, but keeping the table forever would grow it
    ''' without bound over a long run.</summary>
    ''' <summary>What the last tick spent where. A tick cost is one number and
    ''' says nothing about which half to attack; these two say it.</summary>
    Public GroundMisses As Integer = 0
    Public GroundHits As Integer = 0
    Public CellTests As Integer = 0
    Public GroundMs As Double = 0.0
    Private ReadOnly gclock As New Stopwatch()

    Public Sub NewTick()
        GroundMisses = 0
        GroundHits = 0
        CellHeightHits = 0
        CellTests = 0
        GroundMs = 0.0
    End Sub

    Public Function Ground(x As Single, z As Single) As Single
        If map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return 0.0F
        ' NO CACHE HERE ANY MORE, and the counters are why. A memo was added
        ' when the ray walk sampled this 5,600 times a tick, and it halved the
        ' brain. Then the walk went to integers and stopped asking the terrain
        ' anything at all - and the heartbeat read "480 miss / 0 hit", every
        ' tick, for the rest of the run. The only callers left are the drive
        ' walk and will_clear, which sample at radius-spaced points along a
        ' ray; those never land on each other, so the table never answered a
        ' single question and charged a key and an insert for asking.
        '
        ' A cache that fixes a hot path is worth keeping until the hot path is
        ' fixed properly. Then it is just cost.
        Dim y As Single
        GroundMisses += 1
        gclock.Restart()
        Try
            y = get_Y_at_XZ(x, z)
        Catch
            y = 0.0F
        End Try
        gclock.Stop()
        GroundMs += gclock.Elapsed.TotalMilliseconds
        Return y
    End Function

    ''' <summary>
    ''' WHICH CELL A WORLD POINT IS IN. The row flip lives here and nowhere
    ''' else - rows run DOWNWARD from z_top on a bake, and indexing it the
    ''' other way answers about a point mirrored through the middle of the map,
    ''' which is the sort of wrong that still looks like a working grid.
    '''
    ''' Math.Floor, not CInt: the map spans negative X and Z, and truncation
    ''' rounds toward zero, which mis-indexes every cell left of the origin.
    ''' </summary>
    Public Function ColOf(x As Single) As Integer
        Return CInt(Math.Floor((x - x0) / If(FromBake, sq_cell, CELL_M)))
    End Function

    Public Function RowOf(z As Single) As Integer
        Dim cell = If(FromBake, sq_cell, CELL_M)
        If FromBake Then Return CInt(Math.Floor((z_top - z) / cell))
        Return CInt(Math.Floor((z - z0) / cell))
    End Function

    ''' <summary>
    ''' THE GROUND HEIGHT OF A CELL, SAMPLED ONCE AND KEPT FOREVER.
    '''
    ''' "I wanna use one scan and get the height from each square we land on
    '''  and check terrain angle" - the owner.
    '''
    ''' TERRAIN DOES NOT MOVE. The per-tick memo that used to sit on Ground was
    ''' thrown away every tick because it cached arbitrary world points and
    ''' there was no telling which would be asked for again. A CELL is a fixed
    ''' place with a fixed answer, so the sample is good for the whole run -
    ''' the first ray to walk a square pays get_Y_at_XZ once and every ray and
    ''' every tick after it reads an array.
    '''
    ''' This is the flight bake's height plane, built lazily over exactly the
    ''' ground the tank drives on, without waiting for the bake to write one.
    ''' Two million cells at four bytes is 7.8 MB if the whole map is ever
    ''' visited, and a run visits a corridor.
    '''
    ''' NaN IS "NOT ASKED YET", which is the one job NaN is genuinely good at:
    ''' no second array, no sentinel height a real map might legitimately have,
    ''' and IsNaN is the only comparison that ever sees it.
    ''' </summary>
    Private hcell() As Single = Nothing

    Public Function CellHeight(col As Integer, row As Integer) As Single
        If col < 0 OrElse row < 0 OrElse col >= w OrElse row >= h Then Return 0.0F
        If hcell Is Nothing OrElse hcell.Length <> w * h Then
            ReDim hcell(w * h - 1)
            For i = 0 To hcell.Length - 1
                hcell(i) = Single.NaN
            Next
        End If
        Dim idx = row * w + col
        Dim y = hcell(idx)
        If Single.IsNaN(y) Then
            Dim cell = If(FromBake, sq_cell, CELL_M)
            Dim wx = x0 + (col + 0.5F) * cell
            Dim wz = If(FromBake, z_top - (row + 0.5F) * cell, z0 + (row + 0.5F) * cell)
            y = Ground(wx, wz)
            hcell(idx) = y
        Else
            CellHeightHits += 1
        End If
        Return y
    End Function

    ''' <summary>How often the height plane answered without touching the
    ''' scene. Next to GroundMisses on the heartbeat, this is what says whether
    ''' the plane has warmed up.</summary>
    Public CellHeightHits As Integer = 0

    ''' <summary>
    ''' ONE CELL, ONE BIT, NO ARITHMETIC. What an integer line walk tests at
    ''' every step - a bounds check and a mask, nothing converted, nothing
    ''' divided, no terrain touched.
    '''
    ''' OFF THE GRID IS BLOCKED, matching Standable: a hull that leaves the
    ''' arena box has left the test, and a ray that leaves it has nothing
    ''' further to report.
    '''
    ''' NO SLOPE HERE ON PURPOSE. Standable's ground sampling is what made a
    ''' scan cost 5,600 terrain queries a tick; this is the obstacle question
    ''' alone. The terrain question is still asked, once per ray, by the drive
    ''' walk - see Hit.drive. Fast shape, careful driving.
    ''' </summary>
    Public Function BlockedCell(col As Integer, row As Integer) As Boolean
        CellTests += 1
        If Not Ready Then Return False
        If col < 0 OrElse row < 0 OrElse col >= w OrElse row >= h Then Return True
        Return (occ(row * w + col) And BLOCK_BIT) <> 0
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
        Dim cell = If(FromBake, sq_cell, CELL_M)
        Dim cx0 = CInt(Math.Floor((x - r - x0) / cell))
        Dim cx1 = CInt(Math.Floor((x + r - x0) / cell))
        Dim cz0 As Integer, cz1 As Integer
        If FromBake Then
            ' ROWS RUN DOWNWARD FROM wz_max, so a LARGER z is a SMALLER row.
            ' Indexing it the other way answers about a point mirrored through
            ' the middle of the map, which is the sort of wrong that still
            ' looks like a working grid.
            cz0 = CInt(Math.Floor((z_top - (z + r)) / cell))
            cz1 = CInt(Math.Floor((z_top - (z - r)) / cell))
        Else
            cz0 = CInt(Math.Floor((z - r - z0) / cell))
            cz1 = CInt(Math.Floor((z + r - z0) / cell))
        End If

        ' Off the grid is off the map, which is not standable - a hull that
        ' leaves the arena box has left the test.
        If cx0 < 0 OrElse cz0 < 0 OrElse cx1 >= w OrElse cz1 >= h Then Return False

        For cz = cz0 To cz1
            Dim rowBase = cz * w
            For cx = cx0 To cx1
                If (occ(rowBase + cx) And BLOCK_BIT) <> 0 Then Return False
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

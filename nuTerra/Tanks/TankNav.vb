Imports OpenTK.Mathematics

''' <summary>
''' Where a tank may go: one coarse grid over the whole map, built from the
''' flight bake at load and pinned at runtime by what actually stops a tank.
'''
''' SEEDED FROM THE MAP, NOT LEARNED BLIND. The first sketch of this was a
''' grid of off-limits cells discovered by driving into things. That still
''' happens - see Pin - but starting from nothing means thirty tanks spend
''' their first minutes rediscovering the monastery wall one nose at a time.
''' The bake already knows: it carries the height of everything standing, the
''' terrain under it, which texels are a tree TRUNK rather than the canopy
''' over it, and which are the outland backdrop. So the grid starts knowing
''' the map and learns only what the map cannot tell it - other tanks, wrecks,
''' and the places where the bake and the collision disagree.
'''
''' ONE BYTE A CELL, AND THE REASON IS KEPT. A plain passable/impassable bit
''' would be enough to path with and useless to debug: "the tank will not go
''' there" and "the tank will not go there BECAUSE the ground falls two metres"
''' are different bugs. The flags below cost nothing and make the dump
''' readable.
'''
''' A POWER-OF-TWO DOWNSAMPLE of the bake, so the mapping from world to cell is
''' the bake's own mapping shifted, with no second rounding to disagree about.
''' At CELL_TEXELS 8 a cell is about 1.4 m on a 1400 m map - finer than the
''' 4.5 m clearance a hull needs, so the clearance is applied when the grid is
''' QUERIED rather than baked into it. That way one grid serves a heavy and a
''' scout, and changing the margin does not mean rebuilding.
''' </summary>
Public Class TankNav

    ''' <summary>Bake texels per nav cell, each side. A power of two so the
    ''' downsample is exact.</summary>
    Public Const CELL_TEXELS As Integer = 8

    Public Const SIZE As Integer = MapFlightBake.SIZE \ CELL_TEXELS

    ' ---- what a cell is -----------------------------------------------------
    ''' <summary>Something stands here taller than a tank may climb.</summary>
    Public Const BLOCKED As Byte = 1
    ''' <summary>The ground itself falls too far across the cell. Separate from
    ''' BLOCKED because a cliff and a crate want different answers later: you
    ''' can shoot over one and not the other.</summary>
    Public Const STEEP As Byte = 2
    ''' <summary>The scenery ring outside the playable area.</summary>
    Public Const OUTLAND As Byte = 4
    ''' <summary>A tree trunk stands here. Its canopy does NOT block - that was
    ''' the whole point of keeping the two apart in the bake.</summary>
    Public Const TRUNK As Byte = 8
    ''' <summary>Water. Not a slope and not a wall; a tank simply does not
    ''' go there.</summary>
    Public Const WATER As Byte = 16
    ''' <summary>Learned at runtime - something stopped a tank here that the
    ''' bake did not predict. Survives a restart; see Save.</summary>
    Public Const PINNED As Byte = 32

    ''' <summary>
    ''' Outside the arena's play area.
    '''
    ''' THIS IS THE BOUNDARY, NOT `OUTLAND`. The outland bit says an outland
    ''' MODEL rasterised at this texel, and those are sparse scenery - on
    ''' monastery only 10.67% of the outer ring carries one, so three sides of
    ''' the map have open ground running to the edge of the bake with nothing
    ''' marked. A tank steered by the outland bit alone drives off the map
    ''' between the cliffs.
    '''
    ''' The real edge is the arena bounding box out of scripts/arena_defs -
    ''' MAP_BB_BL / MAP_BB_UR, which is -500..500 on monastery inside a
    ''' 1400 m bake. Everything beyond it is scenery the player never reaches.
    ''' </summary>
    Public Const OFFMAP As Byte = 64

    ''' <summary>Everything that stops a tank. Kept as one constant so no
    ''' caller assembles its own idea of impassable.</summary>
    Public Const IMPASSABLE As Byte =
        BLOCKED Or STEEP Or OUTLAND Or TRUNK Or WATER Or PINNED Or OFFMAP

    Public ReadOnly cell(SIZE * SIZE - 1) As Byte

    ''' <summary>Room at every cell in metres - the distance to the nearest
    ''' impassable cell, pulled in half a cell. Zero where impassable. See
    ''' BuildClearance.</summary>
    Public clear_m() As Single
    Public ready As Boolean

    Private wx0, wx1, wz0, wz1 As Single
    Private map_name As String = ""

    ''' <summary>Metres across one cell. Read by anything sizing a step or a
    ''' clearance in cells.</summary>
    Public ReadOnly Property cell_m As Single
        Get
            Return (wx1 - wx0) / SIZE
        End Get
    End Property

    ' =========================================================== building

    ''' <summary>
    ''' Fold the bake down into the grid.
    '''
    ''' WORST CASE PER CELL, never an average. A cell is 64 bake texels and if
    ''' one of them is a wall the cell is a wall - averaging would let a tank
    ''' path through a fence because the other 63 texels were grass. Same
    ''' reasoning as the mask's block-any downsample, and the same direction of
    ''' error: over-reporting an obstacle costs a detour, under-reporting costs
    ''' a tank stuck inside a building.
    ''' </summary>
    Public Sub Build(b As MapFlightBake, map As String)
        ready = False
        If b Is Nothing OrElse Not b.ready Then
            LogThis("tank nav: no flight bake - grid not built")
            Return
        End If

        wx0 = b.wx_min : wx1 = b.wx_max
        wz0 = b.wz_min : wz1 = b.wz_max
        map_name = map

        Dim t0 = Date.UtcNow
        Array.Clear(cell, 0, cell.Length)

        ' The play area, pulled in by a hull's width so a tank cannot come to
        ' rest straddling the line it is not allowed to cross.
        Dim ax0 = Math.Min(MAP_BB_BL.X, MAP_BB_UR.X) + ARENA_MARGIN
        Dim ax1 = Math.Max(MAP_BB_BL.X, MAP_BB_UR.X) - ARENA_MARGIN
        Dim az0 = Math.Min(MAP_BB_BL.Y, MAP_BB_UR.Y) + ARENA_MARGIN
        Dim az1 = Math.Max(MAP_BB_BL.Y, MAP_BB_UR.Y) - ARENA_MARGIN
        Dim have_bb = (ax1 > ax0) AndAlso (az1 > az0)
        If Not have_bb Then
            LogThis("tank nav: no arena bounding box - the grid has no edge")
        End If

        For cr = 0 To SIZE - 1
            Dim r0 = cr * CELL_TEXELS
            For cc = 0 To SIZE - 1
                Dim c0 = cc * CELL_TEXELS
                Dim f As Byte = 0

                If have_bb Then
                    Dim ctr = CentreOf(cc, cr)
                    If ctr.X < ax0 OrElse ctr.X > ax1 OrElse
                       ctr.Y < az0 OrElse ctr.Y > az1 Then
                        cell(cr * SIZE + cc) = OFFMAP
                        Continue For
                    End If
                End If
                Dim lo = Single.MaxValue, hi = Single.MinValue

                For dr = 0 To CELL_TEXELS - 1
                    Dim row = (r0 + dr) * MapFlightBake.SIZE + c0
                    For dc = 0 To CELL_TEXELS - 1
                        Dim i = row + dc
                        Dim fl = b.floor_m(i)
                        If fl < lo Then lo = fl
                        If fl > hi Then hi = fl

                        Dim k = b.kind_b(i)
                        If (k And MapFlightBake.OUTLAND_BIT) <> 0 Then f = f Or OUTLAND
                        If (k And MapFlightBake.TRUNK_BIT) <> 0 Then f = f Or TRUNK
                        If (k And MapFlightBake.KIND_MASK) = MapFlightBake.KIND_WATER Then
                            f = f Or WATER
                        End If

                        ' WHAT STOPS A HULL IS WHAT THE THING IS, and only
                        ' then how tall it is. A single height threshold over
                        ' every kind judges a curb and a boulder the same way,
                        ' and the kind byte is already here in the same loop.
                        '
                        ' TREE - the canopy is not an obstacle, so a texel
                        ' whose only height is a tree is answered by the trunk
                        ' bit instead. Without this every wood on the map is a
                        ' solid block and the tanks stay on the roads.
                        '
                        ' FENCE and PROP - a tank goes through a fence and over
                        ' a curb, and a barrel or a vase is smashed rather than
                        ' driven around. Judged by height they block freely:
                        ' a 1.7 m fence clears MAX_OBSTACLE comfortably while
                        ' stopping nothing. On monastery, exempting the two
                        ' kinds frees 2,028 cells - 0.4% of the play area, and
                        ' 69.5% open becomes 69.9% - with steep, trunk and
                        ' water all unchanged to the cell, which is what says
                        ' only the height test moved. Routing a tank around
                        ' scenery it would flatten costs real ground, and the
                        ' player can see that nothing was there.
                        '
                        ' ROCK, BUILDING and OTHER keep the height test. Those
                        ' genuinely stop a hull, and OTHER is the unidentified
                        ' case - the one to be careful with rather than
                        ' generous toward.
                        ' AND ONLY WHERE IT IS NOT ALSO SOLID. bake_version 2
                        ' marks terrain-borne geometry measured with the trees
                        ' left out, and on monastery 317,776 tree-keyed texels
                        ' carry it - rock and walls standing UNDER a canopy.
                        ' Exempting those by kind alone, which is what this did
                        ' until the bit existed, drives a tank into a cliff.
                        ' THE SOLID BIT QUALIFIES TREES, AND ONLY TREES.
                        '
                        ' It is read from the depth buffer BETWEEN the model
                        ' pass and the tree pass, so any MODEL sets it for
                        ' itself: measured on monastery it is set on 75.6% of
                        ' fence texels and 63.3% of prop texels, and on just
                        ' 7.0% of tree texels. Only for a tree does it carry
                        ' information - that something else is standing under
                        ' the canopy.
                        '
                        ' Testing it on fence and prop as well, which is what
                        ' this did first, un-crushed three quarters of the
                        ' fences on the map and drove a tank round them. The
                        ' owner's rule is the other way: "A fence or curb is
                        ' not going to stop a tank."
                        '
                        ' What this CANNOT see is a fence standing in front of
                        ' a wall, where the fence wins the depth test and the
                        ' wall is invisible to us. That risk existed before the
                        ' bit did and this data cannot settle it.
                        Dim k7 = k And MapFlightBake.KIND_MASK
                        Dim crushable =
                            (k7 = MapFlightBake.KIND_FENCE OrElse
                             k7 = MapFlightBake.KIND_PROP) OrElse
                            (k7 = MapFlightBake.KIND_TREE AndAlso
                             (k And MapFlightBake.SOLID_BIT) = 0)
                        If Not crushable Then
                            If b.top_m(i) - fl > TankNavLimits.MAX_OBSTACLE Then f = f Or BLOCKED
                        End If
                    Next
                Next

                ' A RATIO, NOT A DROP. The placement test allowed a 2 m fall
                ' across a 9 m footprint - about 12 degrees - and carrying that
                ' number onto a 1.4 m cell would permit 55 degrees and let the
                ' tanks path up cliff faces. Slope is the thing that does not
                ' change meaning when the cell size does.
                If (hi - lo) > TankNavLimits.MAX_SLOPE * cell_m Then f = f Or STEEP
                cell(cr * SIZE + cc) = f
            Next
        Next

        ready = True
        Load()

        ' CLEARANCE LAST, AFTER THE PINS. Load() ORs PINNED into the cells, and
        ' PINNED is part of IMPASSABLE - so building the field before it left
        ' the planner measuring room that a learned obstacle was standing in.
        ' A route was then cut straight through ground CanStand refuses, and the
        ' hull met it a few metres off the start line. It compounded: every run
        ' learned more pins near the place the last run stopped, and every run
        ' planned as though none of them existed.
        '
        ' The two tests have to see the same world. clear_m >= hull is meant to
        ' be STRICTER than CanStand, not merely different.
        BuildClearance()

        report(CSng((Date.UtcNow - t0).TotalMilliseconds))
    End Sub

    ''' <summary>
    ''' How much room every cell has: the distance to the nearest impassable
    ''' cell, in metres.
    '''
    ''' THIS IS THE PART OF THE ZONE MAP THAT EARNED ITS KEEP. The discs built
    ''' on top of it did not - measured, they cost 770 ms to save 33 ms of
    ''' routing, and their per-step test was 9x faster while rejecting 44% of
    ''' genuinely drivable ground - but the field itself is cheap and is what
    ''' makes a route search fast: a cell is passable for a given hull iff its
    ''' clearance clears the hull, which is one read where CanStand tests about
    ''' fifty cells.
    '''
    ''' Felzenszwalb and Huttenlocher's exact transform - two separable passes
    ''' of a 1D lower-envelope sweep, rows then columns, exact squared distance
    ''' in O(cells). A chamfer approximation is a few percent out, and a few
    ''' percent of a radius is the difference between a corridor a tank fits
    ''' down and one it does not.
    '''
    ''' HALF A CELL BACK. The transform measures to the blocking cell's CENTRE
    ''' and that cell is solid across its whole area, which starts half a cell
    ''' nearer. The raw distance would call ground clear right up to the thing
    ''' it was measured against.
    ''' </summary>
    Private Sub BuildClearance()
        Const INF As Single = 1.0E+20F
        Dim f(SIZE * SIZE - 1) As Single
        For i = 0 To SIZE * SIZE - 1
            f(i) = If((cell(i) And IMPASSABLE) <> 0, 0.0F, INF)
        Next

        Dim d(SIZE - 1) As Single, ft(SIZE - 1) As Single
        Dim v(SIZE - 1) As Integer, zz(SIZE) As Single

        For r = 0 To SIZE - 1
            Dim o = r * SIZE
            For c = 0 To SIZE - 1 : ft(c) = f(o + c) : Next
            dt1d(ft, SIZE, d, v, zz)
            For c = 0 To SIZE - 1 : f(o + c) = d(c) : Next
        Next
        For c = 0 To SIZE - 1
            For r = 0 To SIZE - 1 : ft(r) = f(r * SIZE + c) : Next
            dt1d(ft, SIZE, d, v, zz)
            For r = 0 To SIZE - 1 : f(r * SIZE + c) = d(r) : Next
        Next

        ReDim clear_m(SIZE * SIZE - 1)
        Dim cm = cell_m
        For i = 0 To SIZE * SIZE - 1
            Dim rc = CSng(Math.Sqrt(f(i))) - 0.5F
            clear_m(i) = If(rc > 0.0F, rc * cm, 0.0F)
        Next
    End Sub

    ''' <summary>The exact 1D squared-distance transform of f into d. v and zz
    ''' are scratch, passed in so the two passes do not allocate 2,048
    ''' short-lived arrays between them.</summary>
    Private Shared Sub dt1d(f() As Single, n As Integer, d() As Single,
                            v() As Integer, zz() As Single)
        Const INF As Single = 1.0E+20F
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
    ''' Count what the grid ended up saying.
    '''
    ''' EVERY LOCAL HERE IS PREFIXED n_ FOR A REASON. The obvious spelling -
    ''' Dim blocked, steep, outland - compiles, runs, and reports zero for
    ''' every flag, because VB is case blind and `blocked` shadows the constant
    ''' BLOCKED inside this method. `f And BLOCKED` then reads the local, which
    ''' is 0, so the test is `f And 0` and never fires. It cost a run to spot,
    ''' and nothing about it looks wrong on the page. Do not un-prefix these.
    ''' </summary>
    Private Sub report(ms As Single)
        Dim n = cell.Length
        Dim n_blocked = 0, n_steep = 0, n_outland = 0, n_offmap = 0
        Dim n_trunk = 0, n_water = 0, n_pinned = 0, n_open = 0
        For i = 0 To n - 1
            Dim f = cell(i)
            If (f And BLOCKED) <> 0 Then n_blocked += 1
            If (f And STEEP) <> 0 Then n_steep += 1
            If (f And OUTLAND) <> 0 Then n_outland += 1
            If (f And TRUNK) <> 0 Then n_trunk += 1
            If (f And WATER) <> 0 Then n_water += 1
            If (f And PINNED) <> 0 Then n_pinned += 1
            If (f And OFFMAP) <> 0 Then n_offmap += 1
            If (f And IMPASSABLE) = 0 Then n_open += 1
        Next
        LogThis("tank nav: {0}x{0} cells of {1:0.00} m in {2:0} ms", SIZE, cell_m, ms)
        LogThis("tank nav:   open {0} ({1:0.0}%)  blocked {2}  steep {3}  outland {4}  trunk {5}  water {6}  pinned {7}  offmap {8}",
                n_open, 100.0F * n_open / n, n_blocked, n_steep, n_outland,
                n_trunk, n_water, n_pinned, n_offmap)

        ' Of the ARENA rather than of the bake, which is the number that means
        ' something: the bake is 1400 m of which only the play area is ever
        ' driven, so "76.8% open" flatters it by counting scenery.
        Dim inside = n - n_offmap
        If inside > 0 Then
            LogThis("tank nav:   inside the arena, open {0:0.0}% of {1} cell(s)",
                    100.0F * n_open / inside, inside)
        End If
    End Sub

    ' =========================================================== queries

    ''' <summary>The world frame this grid was cut in, straight off the bake.
    ''' Exposed because the zone map is exported for readers OUTSIDE this app,
    ''' and a reader that re-derives the mapping is a reader that will one day
    ''' derive it differently.</summary>
    Public ReadOnly Property wx_min As Single
        Get
            Return wx0
        End Get
    End Property
    Public ReadOnly Property wx_max As Single
        Get
            Return wx1
        End Get
    End Property
    Public ReadOnly Property wz_min As Single
        Get
            Return wz0
        End Get
    End Property
    Public ReadOnly Property wz_max As Single
        Get
            Return wz1
        End Get
    End Property

    Public Function InBounds(cx As Integer, cz As Integer) As Boolean
        Return cx >= 0 AndAlso cz >= 0 AndAlso cx < SIZE AndAlso cz < SIZE
    End Function

    ''' <summary>World XZ to cell column/row. Same axis convention as the bake:
    ''' row 0 is the wz_max edge.</summary>
    Public Sub CellOf(x As Single, z As Single, ByRef cx As Integer, ByRef cz As Integer)
        cx = CInt(Math.Floor((x - wx0) / (wx1 - wx0) * SIZE))
        cz = CInt(Math.Floor((wz1 - z) / (wz1 - wz0) * SIZE))
    End Sub

    ''' <summary>The centre of a cell in world XZ, for steering toward one.</summary>
    Public Function CentreOf(cx As Integer, cz As Integer) As Vector2
        Return New Vector2(
            wx0 + (cx + 0.5F) * (wx1 - wx0) / SIZE,
            wz1 - (cz + 0.5F) * (wz1 - wz0) / SIZE)
    End Function

    Public Function FlagsAt(x As Single, z As Single) As Byte
        Dim cx, cz As Integer
        CellOf(x, z, cx, cz)
        If Not InBounds(cx, cz) Then Return OUTLAND
        Return cell(cz * SIZE + cx)
    End Function

    ''' <summary>
    ''' May a hull CENTRED here stand, given how wide it is?
    '''
    ''' THE CLEARANCE IS APPLIED HERE, not baked into the grid, so one grid
    ''' serves vehicles of different sizes and the margin can change without a
    ''' rebuild. Tested over the square that covers the hull rather than at the
    ''' centre - a centre-only test parks tanks astride walls, which is the
    ''' same lesson spot_is_clear learned.
    ''' </summary>
    ''' <param name="radius_m">Half the hull, plus margin.</param>
    Public Function CanStand(x As Single, z As Single, radius_m As Single) As Boolean
        If Not ready Then Return True          ' no data, no veto
        Dim rc = Math.Max(1, CInt(Math.Ceiling(radius_m / cell_m)))
        Dim cx, cz As Integer
        CellOf(x, z, cx, cz)

        ' A DISC, NOT THE SQUARE THAT ENCLOSES IT. Testing the whole block
        ' demands clear ground all the way to the corners - at a 4.5 m radius
        ' and a 1.37 m cell that is 7.75 m diagonally, nearly half as far
        ' again as asked for. The first fleet froze on it: tanks drove until
        ' they reached somewhere whose corners were not clear and then no
        ' direction passed.
        Dim r2 = (radius_m / cell_m) * (radius_m / cell_m)
        For dz = -rc To rc
            For dx = -rc To rc
                If dx * dx + dz * dz > r2 Then Continue For
                Dim ax = cx + dx, az = cz + dz
                If Not InBounds(ax, az) Then Return False
                If (cell(az * SIZE + ax) And IMPASSABLE) <> 0 Then Return False
            Next
        Next
        Return True
    End Function

    ''' <summary>
    ''' Mark a cell as learned-impassable.
    '''
    ''' WHAT THE BAKE CANNOT KNOW goes here: another tank that is not moving, a
    ''' wreck, a lip the height test called fine and the hull disagreed with.
    ''' Kept apart from the baked flags by its own bit so a rebuild of the grid
    ''' does not erase the learning, and so the dump shows which is which.
    ''' </summary>
    Public Sub Pin(x As Single, z As Single)
        If Not ready Then Return
        Dim cx, cz As Integer
        CellOf(x, z, cx, cz)
        If Not InBounds(cx, cz) Then Return
        Dim i = cz * SIZE + cx
        If (cell(i) And PINNED) <> 0 Then Return
        If n_pinned >= PIN_BUDGET Then Return

        ' ONCE IS AN ACCIDENT. The first version pinned on the first wedge and
        ' laid down 299 cells in one session, then 264 more in the next, and
        ' the fleet got measurably worse as it "learned" - fewer tanks moving
        ' each run, because most of those cells were not map errors at all.
        ' They were one tank that steered itself into a corner it could not
        ' turn out of, which says nothing about whether the ground is passable.
        '
        ' A real disagreement between the map and a hull repeats: every tank
        ' that tries it gets stuck in the same place. Demanding several
        ' independent wedges keeps those and throws away the rest.
        Dim hits = 0
        suspect.TryGetValue(i, hits)
        hits += 1
        suspect(i) = hits
        If hits < PIN_CONFIRM Then Return

        cell(i) = cell(i) Or PINNED

        ' AND TELL THE CLEARANCE FIELD, or the planner keeps routing through it.
        '
        ' A pin is a LEARNED obstacle - a wreck, a lip the height test forgave,
        ' a hull that stopped - and PINNED is part of IMPASSABLE, so CanStand
        ' refuses it the instant it exists. But clear_m was computed at load and
        ' knows nothing about it, so the planner cuts a route straight through
        ' the cell the driver just learned it cannot cross, and the hull meets
        ' it again. Measured: a run that was 4/4 moving at a kilometre fell to
        ' 1/4 with two blocked on ground, while the pin count climbed.
        '
        ' A new obstacle can only REDUCE clearance, and by a knowable amount: no
        ' cell may now claim more room than its distance to this pin. That is
        ' precisely what the transform would produce locally, so this is not an
        ' approximation OF the field, it is the field.
        '
        ' 40 cells because nothing on this grid holds more clearance than that,
        ' and pins are rare enough that the comparisons do not matter.
        If clear_m IsNot Nothing Then
            Const R As Integer = 40
            For dz = -R To R
                Dim rr = cz + dz
                If rr < 0 OrElse rr >= SIZE Then Continue For
                For dx = -R To R
                    Dim cc = cx + dx
                    If cc < 0 OrElse cc >= SIZE Then Continue For
                    Dim bound = (CSng(Math.Sqrt(dx * dx + dz * dz)) - 0.5F) * cell_m
                    If bound < 0.0F Then bound = 0.0F
                    Dim k = rr * SIZE + cc
                    If clear_m(k) > bound Then clear_m(k) = bound
                Next
            Next
        End If

        suspect.Remove(i)
        n_pinned += 1
        pins_dirty = True
    End Sub

    ''' <summary>Cells that have stopped a tank, and how often. In memory only:
    ''' a suspicion that never repeated is not worth carrying into the next
    ''' session.</summary>
    Private ReadOnly suspect As New Dictionary(Of Integer, Integer)

    ''' <summary>Independent wedges at one cell before it is believed.</summary>
    Private Const PIN_CONFIRM As Integer = 3

    ''' <summary>
    ''' Most cells that may ever be pinned.
    '''
    ''' A ceiling rather than a hope. Pins are permanent and persisted, so
    ''' without one a long-running session degrades in a way that survives a
    ''' restart and cannot be undone except by deleting the file. 2000 cells is
    ''' 0.4% of the arena - ample for the places the bake genuinely gets wrong,
    ''' and nowhere near enough to close a route.
    ''' </summary>
    Private Const PIN_BUDGET As Integer = 2000

    Private n_pinned As Integer
    Private pins_dirty As Boolean

    ' =========================================================== persistence

    Private Function pin_path() As String
        Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "tanks")
        IO.Directory.CreateDirectory(dir)
        Return IO.Path.Combine(dir, map_name & "_pins.u32")
    End Function

    ''' <summary>
    ''' The learned pins, as a plain list of cell indices.
    '''
    ''' INDICES, NOT THE WHOLE GRID. The baked flags come back for free on the
    ''' next load from a bake that is itself rebuilt, so writing them would be
    ''' storing a derived thing and inviting the two to disagree after a map
    ''' changes. Only what was LEARNED is worth keeping, and there are few
    ''' enough of those that four bytes each is nothing.
    ''' </summary>
    Public Sub Save()
        If Not ready OrElse Not pins_dirty OrElse map_name = "" Then Return
        Try
            Dim ids As New List(Of Integer)
            For i = 0 To cell.Length - 1
                If (cell(i) And PINNED) <> 0 Then ids.Add(i)
            Next
            Dim b(ids.Count * 4 - 1) As Byte
            For k = 0 To ids.Count - 1
                BitConverter.GetBytes(ids(k)).CopyTo(b, k * 4)
            Next
            IO.File.WriteAllBytes(pin_path(), b)
            pins_dirty = False
            LogThis("tank nav: saved {0} learned pin(s)", ids.Count)
        Catch ex As Exception
            LogThis("tank nav: could not save pins - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>Bring back what earlier runs learned. A grid whose SIZE has
    ''' changed since the file was written would read every index as a
    ''' different place, so the length is checked against the grid.</summary>
    Private Sub Load()
        Try
            Dim p = pin_path()
            If Not IO.File.Exists(p) Then Return
            Dim b = IO.File.ReadAllBytes(p)
            If b.Length Mod 4 <> 0 Then Return
            Dim n = 0
            For k = 0 To b.Length \ 4 - 1
                Dim i = BitConverter.ToInt32(b, k * 4)
                If i < 0 OrElse i >= cell.Length Then Continue For
                cell(i) = cell(i) Or PINNED
                n += 1
            Next
            n_pinned = n
            If n > 0 Then LogThis("tank nav: recalled {0} learned pin(s)", n)
        Catch ex As Exception
            LogThis("tank nav: could not read pins - {0}", ex.Message)
        End Try
    End Sub

    ' =========================================================== eyeballing

    ''' <summary>
    ''' The grid as a PNG, one pixel a cell, so it can be looked at.
    '''
    ''' Every other part of this bake has been checked by looking at it rather
    ''' than by trusting the code, and a navigation grid is the one where a
    ''' subtle error - an axis flipped, a margin off by a cell - produces
    ''' tanks that merely behave oddly rather than anything that throws.
    ''' </summary>
    ''' <param name="fleet">Optional. Draws where every tank is, which way it is
    ''' pointing and what it is driving at, over the grid it is driving on.
    ''' Written because the alternative was asking the owner whether thirteen of
    ''' thirty moving "looks idle" - a question about a number, put to someone
    ''' who can only see one frame at a time. On the map it is obvious at a
    ''' glance whether a fleet is spread and travelling or clumped and
    ''' shuffling, and whether the ones standing still are against a wall or in
    ''' the open.</param>
    Public Sub DumpPng(path As String, Optional fleet As List(Of TankInstance) = Nothing)
        If Not ready Then Return
        Try
            Using bmp As New Drawing.Bitmap(SIZE, SIZE, Drawing.Imaging.PixelFormat.Format24bppRgb)
                Dim d = bmp.LockBits(New Drawing.Rectangle(0, 0, SIZE, SIZE),
                                     Drawing.Imaging.ImageLockMode.WriteOnly,
                                     Drawing.Imaging.PixelFormat.Format24bppRgb)
                Dim stride = d.Stride
                Dim px(stride * SIZE - 1) As Byte
                For r = 0 To SIZE - 1
                    For c = 0 To SIZE - 1
                        Dim f = cell(r * SIZE + c)
                        Dim rr As Byte = 30, gg As Byte = 34, bb As Byte = 38   ' open
                        If (f And IMPASSABLE) = 0 Then
                            rr = 40 : gg = 90 : bb = 45
                        ElseIf (f And PINNED) <> 0 Then
                            rr = 255 : gg = 70 : bb = 200                       ' learned
                        ElseIf (f And OFFMAP) <> 0 Then
                            rr = 16 : gg = 16 : bb = 20                       ' beyond the arena
                        ElseIf (f And OUTLAND) <> 0 Then
                            rr = 55 : gg = 55 : bb = 60
                        ElseIf (f And WATER) <> 0 Then
                            rr = 40 : gg = 90 : bb = 190
                        ElseIf (f And TRUNK) <> 0 Then
                            rr = 120 : gg = 80 : bb = 40
                        ElseIf (f And BLOCKED) <> 0 Then
                            rr = 200 : gg = 90 : bb = 60
                        ElseIf (f And STEEP) <> 0 Then
                            rr = 190 : gg = 180 : bb = 70
                        End If
                        Dim o = r * stride + c * 3
                        px(o) = bb : px(o + 1) = gg : px(o + 2) = rr          ' BGR
                    Next
                Next
                Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length)
                bmp.UnlockBits(d)

                If fleet IsNot Nothing Then draw_fleet(bmp, fleet)
                bmp.Save(path, Drawing.Imaging.ImageFormat.Png)
            End Using
            LogThis("tank nav: wrote {0}", path)
        Catch ex As Exception
            LogThis("tank nav: could not write png - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' The tanks on top of the grid: a leader from each hull showing where it
    ''' is pointing, a faint line to what it is driving at, and a dot coloured
    ''' by team.
    '''
    ''' A STOPPED TANK IS DRAWN HOLLOW. Which of the standing-still ones are
    ''' wedged and which are mid-turn is the whole question the picture exists
    ''' to answer, and both look identical as a plain dot.
    ''' </summary>
    Private Sub draw_fleet(bmp As Drawing.Bitmap, fleet As List(Of TankInstance))
        Using g = Drawing.Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias
            Using goal_pen As New Drawing.Pen(Drawing.Color.FromArgb(70, 255, 255, 255), 1.0F),
                  head_pen As New Drawing.Pen(Drawing.Color.FromArgb(230, 255, 255, 255), 1.4F)
                For Each inst In fleet
                    Dim p = ToPixel(inst.position.X, inst.position.Z)

                    ' where it is trying to get to
                    If inst.drive.hasGoal Then
                        Dim q = ToPixel(inst.drive.goal.X, inst.drive.goal.Y)
                        g.DrawLine(goal_pen, p.X, p.Y, q.X, q.Y)
                    End If

                    ' which way it is facing, one hull length of it
                    Dim hx = CSng(Math.Sin(inst.headingRad)) * 7.0F / cell_m
                    Dim hz = -CSng(Math.Cos(inst.headingRad)) * 7.0F / cell_m
                    g.DrawLine(head_pen, p.X, p.Y, p.X + hx, p.Y + hz)

                    Dim col = If(inst.team = TankTeam.Red,
                                 Drawing.Color.FromArgb(255, 90, 70),
                                 Drawing.Color.FromArgb(90, 230, 110))
                    Dim r = 3.5F
                    Dim box As New Drawing.RectangleF(p.X - r, p.Y - r, r * 2, r * 2)
                    If inst.drive.speed > 0.1F Then
                        Using b As New Drawing.SolidBrush(col)
                            g.FillEllipse(b, box)
                        End Using
                    Else
                        Using pn As New Drawing.Pen(col, 1.6F)
                            g.DrawEllipse(pn, box)
                        End Using
                    End If
                Next
            End Using
        End Using
    End Sub

    ''' <summary>World XZ to a pixel in the dump, which is one pixel a cell.
    ''' Float, not the integer CellOf, so a hull moving a third of a cell still
    ''' moves on the picture.</summary>
    Private Function ToPixel(x As Single, z As Single) As Drawing.PointF
        Return New Drawing.PointF(
            (x - wx0) / (wx1 - wx0) * SIZE,
            (wz1 - z) / (wz1 - wz0) * SIZE)
    End Function
End Class

''' <summary>
''' What a tank can climb and fall, in one place.
'''
''' Pulled out of MapTanks because the nav grid and the spot tests have to
''' agree: a grid built on one limit and a placement vetoed on another would
''' put tanks on cells the pathfinder then refuses to leave.
''' </summary>
Public Module TankNavLimits
    ''' <summary>Tallest thing a tank may climb. Matches the bake's own
    ''' OBSTACLE_MIN_H so 'blocked' means one thing across the app.</summary>
    Public Const MAX_OBSTACLE As Single = 1.0F

    ''' <summary>
    ''' Steepest ground a tank will take, as rise over run.
    '''
    ''' 0.7 is about 35 degrees, which is the usual figure for a tracked
    ''' vehicle and comfortably past anything a road or a firing position is
    ''' cut at. Expressed as a RATIO rather than as a height per cell, because
    ''' a height only means a slope while the cell size stays put - and the
    ''' cell size follows the map's span, so it does not.
    ''' </summary>
    Public Const MAX_SLOPE As Single = 0.7F

    ''' <summary>How far inside the arena's edge a tank must stay. About a
    ''' hull length, so one cannot come to rest straddling the boundary with
    ''' its nose in scenery the player never reaches.</summary>
    Public Const ARENA_MARGIN As Single = 8.0F
End Module

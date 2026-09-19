Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' THE RADAR, drawn where it is looking.
'''
''' The owner, 2026-09-16: "we will start with trying to teach radar what it is
''' seeing. I think we use 120 degrees off nose and back and scan both. make
''' rays visible and show where the intersect a square. one tank on map 30 rays
''' front and back only. zero speed."
'''
''' TWO ARCS, NOT A CIRCLE. A tank drives forwards and reverses; it does not
''' strafe. The 120 degrees either side of the nose is where it is going and
''' the 120 behind is where it can retreat to. The 60 degrees off each flank
''' are deliberately blind - a hull beside you is neither a path nor an escape,
''' and rays spent there buy a picture nobody acts on.
'''
''' THIRTY RAYS, so 15 an arc: one every 8 degrees. At 20 m that is 2.8 m
''' between neighbours at full reach, which is narrower than a 3.4 m hull - so
''' nothing tank-sized can sit in the gap between two rays and go unseen. That
''' is the number the ray count has to be checked against, not "it looks
''' dense enough".
'''
''' ZERO SPEED IS THE POINT. A radar that is wrong while standing still is
''' wrong for a reason you can look at; a moving tank turns every reading into
''' a question about when it was taken.
'''
''' WHY THE SQUARE IS DRAWN AND NOT JUST THE RAY. "show where the intersect a
''' square" - a distance cannot be checked against the map by eye, and a
''' highlighted cell can. A radar reading the wrong place lights the wrong
''' square, which is obvious on screen and invisible in a number.
'''
''' It asks BrainNav.Standable at TRACE_R, which is the same question the
''' driver asks. A radar that consulted a different map from the driver would
''' be describing a world nothing drives in - that was this week's most
''' expensive bug, in another form.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Module BrainRadar

    ' NINETY DEGREES, THREE DEGREES APART. 60 rays, 30 an arc, 90 / 30 = 3.
    '
    ' The arc narrowed first, from 120 to 90: a ray 60 degrees off the nose
    ' looks at ground the hull drives PAST rather than toward, and it was
    ' joining the range sequence and putting turning points into a test that
    ' was trying to decide whether the thing AHEAD was one surface.
    '
    ' Then the sampling doubled. At 20 m the rays are now 1.05 m apart, down
    ' from 2.8 m at the original 120/30 - so a gap a tank could fit through no
    ' longer hides between two returns at the far end of the scan, and the
    ' shape test has twice the samples to find a turning point in.
    '
    ' It costs a scan of 60 walks instead of 30, which against a 0.4 ms brain
    ' tick is not a number worth protecting.
    ''' <summary>
    ''' THE FIRST LOOK IS CHEAP. "i think we will drop first scan to 9 rays."
    '''
    ''' Two scans, not one. Nine rays across the arc is enough to answer the
    ''' only question a moving tank keeps asking - is there anything ahead -
    ''' and it costs a seventh of the full sweep. The detailed scan is for the
    ''' moment there IS something, which is when a decision has to be made and
    ''' resolution is suddenly worth paying for.
    '''
    ''' That is the right shape for a sensor: look often and coarsely, look
    ''' closely and rarely. Scanning at full detail every frame spent the
    ''' resolution on empty ground, where nothing was ever going to be
    ''' resolved.
    ''' </summary>
    Public Const COARSE_RAYS As Integer = 9

    ' A HUNDRED AND TWENTY DEGREES, 8.6 APART. 28 rays, 14 an arc,
    ' 120 / 14 = 8.57.
    '
    ' The arc went back out to 120 on the owner's call - "lets try 120 deg
    ' sweep" - because a 90 degree sweep cannot SEE the way round something
    ' it is nose-on to. The way round is at 50 or 60 degrees off, which is
    ' exactly the ground the old arc stopped at, so the brain was choosing
    ' the best of the headings it had rather than the best there was.
    '
    ' THE RAY COUNT WENT WITH IT. 20 rays across 120 would be 12 degrees
    ' apart - 4.2 m between returns at full reach, wide enough to hide a
    ' gap a tank fits through. 28 keeps the spacing at 8.6 and the far-end
    ' gap at 3.0 m. Eight more walks against a 0.4 ms tick is not a number
    ' worth protecting.
    '
    ' WHAT NARROWING IT FIXED IS STILL FIXED, ELSEWHERE. The arc was cut to
    ' 90 for a measured reason: a ray 60 degrees off the nose looks at
    ' ground the hull drives PAST rather than toward, and it joined the
    ' range sequence and put turning points into a test asking whether the
    ' thing AHEAD was one surface. So the SCAN is 120 and the FIT is not -
    ' see FIT_ARC_DEG. Widening the sweep and widening the fit are two
    ' different changes and only one of them was ever wanted.
    '
    ' CONST, NOT A VARIABLE, and that is a fix rather than tidying.
    '
    ' As plain module fields these read as ZERO on the first Scan of a run:
    ' rays came back 0, Dim out(-1) is a legal empty array in VB, and so the
    ' radar drew nothing while reporting no error at all. The same log line
    ' printed hits.Length = 0 and RAYS = 20 in one call, which is only
    ' possible if the field was still uninitialised when Scan read it.
    '
    ' Nothing assigns these at runtime, so there is no reason for them to be
    ' fields. A Const is compiled in and cannot be observed half-built.
    ' A HUNDRED AND TWENTY RAYS, ALL THE WAY ROUND. 360 / 120 = 3 degrees.
    '
    ' "we need a wider scan. lets do a full 360 sweep. it should not be blind
    '  to openings it can get though on the sides." - the owner.
    '
    ' TWO ARCS LEFT TWO BLIND WEDGES. A 120-degree sweep off the nose and
    ' another off the tail cover 240 of 360, and the 60 degrees missing on each
    ' side are DEAD ABEAM - precisely where a wall is while the hull is
    ' following one, and where a side opening would be. Follow held its
    ' distance off the outermost forward ray at 55 degrees, which is 35 degrees
    ' short of the beam: it was regulating against a wall it could not see,
    ' using the nearest thing it could.
    '
    ' THREE DEGREES, BECAUSE IT IS NOW AFFORDABLE AND THERE IS A REAL CEILING.
    ' "its faster so we can use more rays" - and the limit is not the clock, it
    ' is the grid: at 20 m reach, rays 1.43 degrees apart land 0.5 m apart,
    ' which is the cell size. Finer than that and neighbouring rays return the
    ' same square, so the extra work buys nothing at all.
    '
    ' Three degrees puts them 1.05 m apart at full reach - two cells - and
    ' about 8 cm apart at the five metres where a decision to turn is actually
    ' made. The old nine degrees was 3.1 m at reach, wide enough to miss the
    ' EDGE of a gap even when it could not miss the gap.
    '
    ' It costs 120 cell walks instead of 28, against a tick that measured
    ' 0.15 ms - the integer walk made the ray count stop being a number worth
    ' protecting.
    Public Const RAYS As Integer = 120
    Public Const ARC_DEG As Single = 360.0F
    ''' <summary>
    ''' HOW FAR THE SWEEP SEES. Forty metres, and the old twenty was the reason
    ''' the hull kept slowing down.
    '''
    ''' THE ARITHMETIC, not a feeling. Twenty metres at 12 m/s is 1.7 seconds
    ''' of warning. A forty-degree turn at 26 degrees a second takes 1.5
    ''' seconds. That is two tenths of a second of margin, so the hull was
    ''' permanently deciding at the edge of what it knew and the only way it
    ''' could be safe was to go slower - the braking was never timidity, it was
    ''' geometry. Doubling the horizon buys 3.3 seconds and turns a 0.2 s
    ''' margin into 1.8.
    '''
    ''' AND IT IS NOW AFFORDABLE, which it was not when this number was chosen.
    ''' The sweep stepped half a metre at a time calling Standable - five
    ''' terrain queries a step - so reaching further cost real milliseconds.
    ''' The walk is integer cells against cached heights now: forty metres is
    ''' about forty cells a ray instead of twenty, and cells do not register on
    ''' the tick.
    ''' </summary>
    ''' <summary>How far a ray goes, metres. NOT a Const any more: the Ray
    ''' Scan node carries it as a setting, so it has to be assignable. Still
    ''' one value for the whole app - a scan is a scan.</summary>
    Public REACH_M As Single = 40.0F

    ''' <summary>How far the per-ray DRIVE walk bothers to look. Beyond this a
    ''' heading is a direction rather than a plan - the hull will have rescanned
    ''' four times before it gets there.</summary>
    Public Const DRIVE_REACH_M As Single = 28.0F

    ''' <summary>
    ''' HOW MUCH OF THE SWEEP THE SURFACE FIT IS ALLOWED TO SEE.
    '''
    ''' The scan is 120 degrees so the brain can find a way round. The fit is
    ''' 90 because that is what it was measured at: past 45 degrees off the
    ''' nose a return is about ground the hull drives past, not ground it
    ''' drives at, and feeding those to a test that asks "is the thing AHEAD
    ''' one surface" is how the shape test started finding turning points in
    ''' its own periphery.
    '''
    ''' A Const and not a ReadOnly field on purpose - see the note above on
    ''' RAYS reading zero on the first Scan of a run.
    ''' </summary>
    Public Const FIT_ARC_DEG As Single = 90.0F

    ''' <summary>Which hull wears it. One tank, as asked.</summary>
    Public HULL As Integer = 0

    Public SHOW As Boolean = True

    ''' <summary>Off the ground so the lines do not z-fight the terrain, and
    ''' under a hull's clearance so they still read as lying on it.</summary>
    Private Const LIFT As Single = 0.35F

    ''' <summary>Step along a ray. Half a cell, so a cell cannot be stepped
    ''' over - the thing being drawn is WHICH CELL, and a stride longer than
    ''' one would skip the answer.</summary>
    Private Const STEP_M As Single = 0.5F

    Private shader As BrainShader
    Private vao, vbo As Integer
    Private verts As Integer = 0
    Private said As Boolean = False
    Private said_scan As Boolean = False

    ' WHAT IT WAS BUILT FOR. A scan and a buffer upload every frame is
    ' thirty ray walks and a few hundred terrain lookups for a picture that
    ' has not changed - the tank has to MOVE for the answer to move.
    Private built_pos As Vector2 = New Vector2(Single.MaxValue, Single.MaxValue)
    Private built_head As Single = Single.MaxValue
    Private built_show As Boolean = False

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    ''' <summary>One ray's answer.</summary>
    Public Structure Hit
        Public angle As Single        ' relative to the nose, radians
        Public front As Boolean
        Public dist As Single
        Public found As Boolean
        Public at As Vector2          ' where it landed
        Public row As Integer
        Public col As Integer

        ''' <summary>
        ''' HOW FAR THE HULL COULD ACTUALLY GO THIS WAY. "2d scans are not
        ''' enough" - the owner, and the tank that proved it drove into a low
        ''' spot and hit terrain in every direction.
        '''
        ''' `dist` is where the LINE stops: a trace at TRACE_R, half a metre
        ''' wide, asking about obstacles and about the ground gradient over
        ''' half a metre. A bowl whose sides are gentle at that scale and steep
        ''' across the hull reads as twenty clear metres, and it is a TRAP -
        ''' the hull drives in, and once in, the same test at the body's radius
        ''' fails in every direction at once. In it goes and it does not come
        ''' out.
        '''
        ''' `drive` is the same walk asked at the BODY's radius, which folds
        ''' the terrain in: Standable samples ground at plus and minus the
        ''' radius, so at 1.94 m it is asking about the slope across the tank
        ''' rather than across a cell. Two numbers per ray - what can be SEEN
        ''' and what can be DRIVEN - so the brain can stop inferring the second
        ''' from the first. That inference is what every bug tonight was.
        ''' </summary>
        Public drive As Single

        ''' <summary>
        ''' THE CHORD TO THE NEXT RAY'S ENDPOINT, and whether the hull fits
        ''' through it.
        '''
        ''' "draw a line between hit points that are adjacent... measure open
        '''  gaps only by cord length" - the owner, 2026-09-17.
        '''
        ''' This is the honest way to ask how wide an opening is. Counting rays
        ''' is meaningless - three degrees is 1 m at twenty metres and 5 cm at
        ''' one - and reading it off the drive distances describes the lane
        ''' rather than the doorway. The straight-line distance between where
        ''' two neighbouring rays stopped is, exactly, how much room there is
        ''' between those two pieces of the world.
        '''
        ''' `linked` means the hull does NOT fit: both rays found something and
        ''' their returns are closer together than the tank is wide, so that
        ''' pair is one continuous barrier with no way between. Two MISSES are
        ''' never linked however close their far ends are - open sky 1 m across
        ''' at twenty metres is sky, not a wall, and that is the trap in
        ''' measuring this naively.
        ''' </summary>
        Public chord As Single
        Public linked As Boolean
    End Structure

    ''' <summary>
    ''' The last FULL-RESOLUTION scan. What the scope draws and what the
    ''' surface fit reads.
    '''
    ''' THE COARSE SCAN DOES NOT PUBLISH HERE, and that was a real bug: the
    ''' nine-ray look runs every tick while driving, so it became "the last
    ''' scan" almost always. The scope went sparse and the fit - which needs
    ''' four front returns and had four rays in the whole front arc - fell
    ''' below its minimum and reported nothing.
    '''
    ''' A cheap look is for deciding whether to look properly. It is not what
    ''' the tank thinks it sees, and it should not overwrite it.
    ''' </summary>
    Public LAST As Hit() = Nothing

    ''' <summary>The last cheap look, kept separately so it can be inspected
    ''' without standing in for the real one.</summary>
    Public LAST_COARSE As Hit() = Nothing

    ''' <summary>Bearings relative to the nose: front arc, then rear.</summary>
    Public Function Bearings() As Single()
        ' RAYS DIRECTLY. There was a parameter called `rays` here, and VB is
        ' CASE-INSENSITIVE: `rays` and `RAYS` are one identifier, so the
        ' parameter shadowed the constant and `If rays <= 0 Then rays = RAYS`
        ' compiled to `rays = rays`. It stayed 0, Dim out(-1) is a legal EMPTY
        ' array in VB, and the radar drew nothing while reporting no error.
        ' The compiler only said so when the local became a Const:
        ' "Constant 'rays' cannot depend on its own value."
        ' ONE SWEEP, IN ANGLE ORDER, from behind the left shoulder round to
        ' behind the right. Index order IS angle order now, which the brain
        ' relies on - it reads the outermost forward rays as front(0) and
        ' front(last) and would pick the wrong ones from a list built in two
        ' pieces.
        '
        ' Centre of each slice, as before, so no ray lands exactly on the nose
        ' OR exactly on the tail. Both would otherwise be special cases in
        ' every reading of this, and the tail matters now that reversing is
        ' steered by these.
        Dim span = MathHelper.DegreesToRadians(ARC_DEG)
        Dim out(RAYS - 1) As Single
        For k = 0 To RAYS - 1
            Dim a = -span * 0.5F + span * (k + 0.5F) / RAYS
            While a > Math.PI
                a -= CSng(Math.PI * 2.0)
            End While
            While a < -Math.PI
                a += CSng(Math.PI * 2.0)
            End While
            out(k) = a
        Next
        Return out
    End Function

    ''' <summary>Sweep both arcs from this hull.</summary>
    ''' <summary>
    ''' Sweep both arcs. `bodyR` is the hull's driving radius - pass 0 to skip
    ''' the drive walk when only the shape is wanted.
    '''
    ''' NOT NAMED `rays`, `arc` OR ANYTHING ELSE THAT IS ALSO A CONSTANT HERE.
    ''' VB is case-insensitive and a parameter that collides with a module
    ''' constant silently becomes it - that cost an evening once already.
    ''' </summary>
    ''' <summary>
    ''' One sweep. With `record` the answer becomes LAST and WAYS - the one
    ''' view the whole app reads - and without it the sweep is the caller's
    ''' alone.
    '''
    ''' THAT SWITCH EXISTS BECAUSE A SECOND VIEWPOINT COSTS A THIRD SCAN
    ''' WITHOUT IT. Looking ahead means scanning from up the road, and since
    ''' every Scan overwrote the globals, the near view had to be scanned
    ''' AGAIN to put it back. Two scans of work to get one extra look, and a
    ''' window in the middle where anything reading WAYS saw the wrong place.
    ''' </summary>
    Public Function Scan(pos As Vector2, headingRad As Single,
                         Optional bodyR As Single = 0.0F,
                         Optional record As Boolean = True) As Hit()
        ' No local named `rays` - see Bearings. VB would fold it into RAYS.
        Dim bear = Bearings()
        Dim out(RAYS - 1) As Hit
        For i = 0 To RAYS - 1
            Dim a = headingRad + bear(i)
            Dim dx = CSng(Math.Sin(a)), dz = CSng(Math.Cos(a))
            Dim h As Hit
            h.angle = bear(i)
            ' FRONT IS THE FORWARD HEMISPHERE, not the first half of the
            ' array. It used to be an index test because the array was built
            ' front-then-rear; with one sweep round the circle the only honest
            ' test is the bearing itself. Everything that reasons about "ahead"
            ' - the surface fit, the door finder, the side choice - reads this,
            ' and the fit narrows itself further to FIT_ARC_DEG regardless.
            h.front = (Math.Abs(bear(i)) <= CSng(Math.PI) * 0.5F)
            h.found = False
            h.dist = REACH_M

            ' ---- THE LINE, IN INTEGERS -------------------------------------
            '
            ' "we wanna use integers as much as we can. The math is much
            '  faster. sine/cosine for finding the lines end should not kill
            '  us. can we try drawing each line use ints and check each new
            '  point as we go. Simple mask on bit 1" - the owner.
            '
            ' The sin and cos are paid ONCE, to find where the ray ends. After
            ' that nothing is converted and nothing is divided: two integer
            ' cell coordinates, an error accumulator, one add and one compare a
            ' step, and a mask on the byte.
            '
            ' What it replaces sampled the line at half a metre and called
            ' Standable at each sample - 40 samples a ray, and every one of
            ' them recomputing four floor-divisions to get back to a cell index
            ' it had just walked away from, then taking FIVE terrain height
            ' queries for the slope test. 28 rays of that was 1,120 calls and
            ' about 5,600 lookups into the live scene, every tick.
            '
            ' ONE AXIS A STEP, which is the whole reason this is not textbook
            ' Bresenham. The classic form moves both axes in one iteration on a
            ' diagonal, stepping corner to corner and skipping the two cells it
            ' passes BETWEEN - for drawing a line that is correct and wanted,
            ' for a scan it is exactly the "jump a one-cell wall diagonally"
            ' failure BrainNav.Clear warns about, and it cannot be fixed by
            ' stepping finer because it is in the construction. Taking one axis
            ' per iteration walks the cells edge to edge and cannot cut a
            ' corner.
            Dim c0 = BrainNav.ColOf(pos.X), r0 = BrainNav.RowOf(pos.Y)
            Dim c1 = BrainNav.ColOf(pos.X + dx * REACH_M)
            Dim r1 = BrainNav.RowOf(pos.Y + dz * REACH_M)
            Dim sc = Math.Sign(c1 - c0), sr = Math.Sign(r1 - r0)
            Dim dc = Math.Abs(c1 - c0), dr = Math.Abs(r1 - r0)
            Dim cc = c0, rr = r0
            Dim err = dc - dr
            Dim guard = dc + dr + 2        ' cannot outlast the cells it covers

            ' ---- THE BODY'S CORRIDOR, IN CELLS ------------------------------
            '
            ' The ray is one cell wide and the tank is not. The perpendicular
            ' is worked out ONCE, here, and the same integer offsets are reused
            ' at every square - so checking the hull's whole width costs a
            ' handful of array reads a step rather than a Standable call with
            ' its four floor-divisions and five terrain queries.
            Dim halfCells = 0
            If bodyR > 0.0F Then halfCells = CInt(Math.Ceiling(bodyR / BrainNav.CellSize))
            Dim pxq = -dz, pzq = dx          ' unit perpendicular, world
            Dim driveOpen = (bodyR > 0.0F)
            Dim driveD2 = 0

            While guard > 0
                guard -= 1
                If (cc <> c0 OrElse rr <> r0) AndAlso BrainNav.BlockedCell(cc, rr) Then
                    ' DISTANCE FROM THE CELLS, not from a stepped t. Integer
                    ' deltas the whole way and one square root at the hit,
                    ' rather than a float accumulated 40 times a ray.
                    Dim gc = cc - c0, gr = rr - r0
                    h.dist = Math.Min(REACH_M,
                        CSng(Math.Sqrt(CDbl(gc) * gc + CDbl(gr) * gr)) * BrainNav.CellSize)
                    h.found = True
                    Exit While
                End If

                ' ---- IS THE HULL'S WIDTH OPEN ON THIS SQUARE ----------------
                '
                ' THE OBSTACLE QUESTION AND ONLY THE OBSTACLE QUESTION.
                '
                ' The slope half of this read CellHeight along the ray and
                ' again across the hull, against MAX_SLOPE. Every one of those
                ' heights came from the LIVE terrain, re-deriving per ray, per
                ' tick, what nuTerra folded into bit 0 once when it baked the
                ' .blk - "we dont use slopes in this.. it is predetermined by
                ' the algo in nuTerra at start", the owner, 2026-09-19.
                '
                ' Nothing is lost by dropping it: a rim steep enough to matter
                ' IS a blocked cell in the bake, and the centre-cell test at
                ' the top of this loop already stops the ray dead on one. The
                ' bowl that defeated the old version - gentle along the line of
                ' travel, steep across the hull - is caught by the same bit,
                ' now that the bit carries the slope rule.
                '
                ' The real saving is the cache that sat behind CellHeight: a
                ' Single per cell, 7.84 million of them on a 2800 grid, which
                ' LoadBlk frees on purpose and the first ray used to allocate
                ' straight back.
                If driveOpen AndAlso (cc <> c0 OrElse rr <> r0) Then
                    ' The hull's width, either side.
                    For q = 1 To halfCells
                        Dim ox = CInt(Math.Round(pxq * q)), oz = CInt(Math.Round(pzq * q))
                        If BrainNav.BlockedCell(cc + ox, rr - oz) OrElse
                           BrainNav.BlockedCell(cc - ox, rr + oz) Then
                            driveOpen = False
                            Exit For
                        End If
                    Next
                    If driveOpen Then
                        Dim ac = cc - c0, ar = rr - r0
                        driveD2 = ac * ac + ar * ar
                    End If
                End If

                If cc = c1 AndAlso rr = r1 Then Exit While
                Dim e2 = err * 2
                If e2 > -dr AndAlso cc <> c1 Then
                    err -= dr
                    cc += sc
                ElseIf rr <> r1 Then
                    err += dc
                    rr += sr
                Else
                    Exit While
                End If
            End While
            ' ONE SCAN. The drive distance came out of the walk above rather
            ' than from a second pass - there used to be one, striding the hull
            ' radius and calling Standable, and it was 70% of the brain's whole
            ' cost because each of those calls took five terrain queries. The
            ' walk already visits every square in order; asking it what it
            ' landed on while it is standing there is free by comparison.
            h.drive = Math.Min(h.dist,
                CSng(Math.Sqrt(CDbl(driveD2))) * BrainNav.CellSize)
            If Not driveOpen AndAlso driveD2 = 0 Then h.drive = 0.0F
            If bodyR <= 0.0F Then h.drive = h.dist

            h.at = New Vector2(pos.X + dx * h.dist, pos.Y + dz * h.dist)
            ' The walk already knows which cell it stopped in - re-deriving it
            ' from the hit point was a second answer to a question that had one.
            h.row = rr
            h.col = cc
            out(i) = h
        Next
        ' ---- JOIN THE NEIGHBOURS -------------------------------------------
        '
        ' One pass round the circle, after every endpoint is known. The last
        ' ray closes onto the first because the sweep IS a circle now - leaving
        ' that pair out would put a permanent seam directly behind the hull,
        ' which is exactly where a reversing tank looks.
        '
        ' Cost is a subtract and a square root per pair; the sines were spent
        ' casting the rays and the endpoints were already stored. No table
        ' needed at this ray count - measured, not assumed.
        Dim gate = If(bodyR > 0.0F, bodyR * 2.0F, 0.0F)
        For i = 0 To RAYS - 1
            Dim j = (i + 1) Mod RAYS
            out(i).chord = (out(i).at - out(j).at).Length
            out(i).linked = out(i).found AndAlso out(j).found AndAlso
                            out(i).chord < gate
        Next

        ' The ways are built either way; only recording them is optional.
        Dim ways = BuildWays(out, pos, headingRad, bodyR)
        If record Then
            WAYS.Clear()
            WAYS.AddRange(ways)
            LAST = out
        End If
        ' TEMPORARY: who called, with what, and what landed.
        If SCAN_SAY < 6 Then
            SCAN_SAY += 1
            Dim fnd = 0, unl = 0
            For q = 0 To out.Length - 1
                If out(q).found Then
                    fnd += 1
                    If Not out(q).linked Then unl += 1
                End If
            Next
            LogThis("brain: scan - record {0}, built {1} | found {2}, " &
                    "unlinked {3} | cand {4}, no-j {5}, too-far {6}, " &
                    "narrow {7}, too-near {8}",
                    record, ways.Count, fnd, unl, WAY_CAND, WAY_NOJ,
                    WAY_FAR, WAY_NARROW, WAY_NEAR)
        End If
        Return out
    End Function

    ''' <summary>
    ''' The doors in a point cloud - ANY point cloud, not only the one the
    ''' last Scan produced.
    '''
    ''' Lifted out of Scan so a cloud merged from two viewpoints can be run
    ''' through the same rule. It is the same rule or it is worthless: a door
    ''' found by a second, looser finder is a door nobody can check against
    ''' the one the hull actually steers on.
    '''
    ''' `pos` is where the HULL is, not where the cloud was sampled from. The
    ''' bearings and the corridor test are the hull's own, because it is the
    ''' hull that has to fit.
    ''' </summary>
    Public Function BuildWays(out As Hit(), pos As Vector2,
                              headingRad As Single,
                              bodyR As Single) As List(Of Way)
        Dim ways As New List(Of Way)
        WAY_CAND = 0 : WAY_FAR = 0 : WAY_NARROW = 0 : WAY_NEAR = 0
        WAY_NOJ = 0 : WAY_FIRST_I = -1 : WAY_FIRST_J = -1 : WAY_MADE = 0
        WAY_ADDS = 0
        If bodyR <= 0.0F OrElse out Is Nothing Then Return ways

        ' ---- THE WAYS THROUGH, AND WHETHER WE ACTUALLY FIT -----------------
        '
        ' "we are trying to go thru gaps we wont fit" - and the chord was why.
        '
        ' A CHORD IS NOT A DOORWAY. Between two jambs at different ranges it is
        ' a DIAGONAL: a return at 5 m on the left and one at 18 m on the right
        ' can be eight metres apart as a straight line while the passage
        ' between those two obstacles is nothing like eight metres wide. The
        ' chord measures line of sight past a corner. It was being read as
        ' clearance, and the hull kept committing to gaps that were never
        ' there.
        '
        ' So the chord stops being the test and becomes the CANDIDATE. The test
        ' is the corridor - the hull's own width in parallel planks, cast at
        ' the gap, exactly the same rule that decides whether to keep the
        ' throttle down. Either the box the tank sweeps is clear or it is not,
        ' and that question has one answer rather than an inference from two
        ' sample points.
        '
        ' THE AIM POINT IS PUSHED PAST the opening's plane. Arriving at the
        ' midpoint of a doorway leaves the hull IN the doorway.
        Dim maxSteps = CInt(90.0F / (ARC_DEG / RAYS))
        For i = 0 To out.Length - 1
            If Not out(i).found OrElse out(i).linked Then Continue For
            WAY_CAND += 1
            Dim j = -1
            For k = 1 To out.Length - 1
                Dim m = (i + k) Mod out.Length
                If out(m).found Then
                    j = m
                    Exit For
                End If
            Next
            If j < 0 OrElse j = i Then
                WAY_NOJ += 1
                Continue For
            End If
            If WAY_FIRST_I < 0 Then
                WAY_FIRST_I = i
                WAY_FIRST_J = j
            End If
            If (((j - i) + out.Length) Mod out.Length) > maxSteps Then
                WAY_FAR += 1
                Continue For
            End If

            Dim wy As Way
            wy.ia = i
            wy.ib = j
            wy.a = out(i).at
            wy.b = out(j).at
            wy.chord = (wy.a - wy.b).Length
            If wy.chord < bodyR * 2.0F Then
                WAY_NARROW += 1
                Continue For
            End If

            Dim mid = (wy.a + wy.b) * 0.5F
            Dim toMid = mid - pos
            Dim reach = toMid.Length
            If reach < 0.5F Then
                WAY_NEAR += 1
                Continue For
            End If
            Dim dir = toMid / reach
            wy.mid = mid + dir * 2.0F
            wy.bearing = wrap_to_pi(CSng(Math.Atan2(dir.X, dir.Y)) - headingRad)

            ' Not named `bear` - Scan already has one for the bearings
            ' array, and VB will not let a block hide it. Worth the compile
            ' error: a silently shadowed name is what cost an evening when
            ' a parameter called `rays` swallowed the RAYS constant.
            Dim gapBear = CSng(Math.Atan2(dir.X, dir.Y))

            ' DOES THE HULL FIT THROUGH IT - tested AT the opening.
            '
            ' This used to cast the corridor from the HULL, the whole way to
            ' the gap and two metres past, which does not ask whether the tank
            ' fits through the door. It asks whether the tank can drive
            ' STRAIGHT INTO IT FROM WHERE IT IS STANDING - and those are
            ' different questions with different answers.
            '
            ' Measured on the owner's hard start: six doors a tick, widest
            ' 12.5 m against a 3.9 m hull, and every one rejected. The hull was
            ' boxed in with returns at 7-10 m all round its nose, so every
            ' straight run from there hit something. The rule failed hardest at
            ' exactly the moment a door was most needed, and Has Door read
            ' false on every tick of every run all night.
            '
            ' So `fits` is now a local property of the opening: eight metres of
            ' hull-wide corridor centred on it, along its own bearing. Whether
            ' we can GET there is a routing question, and the board already
            ' answers that every tick by steering - it does not need to be
            ' smuggled into the definition of a doorway.
            Dim approach = wy.mid - dir * 4.0F
            Dim lane = Corridor(approach, gapBear, bodyR, 8.0F, False)
            wy.fits = Not lane.hit
            ways.Add(wy)
            WAY_ADDS += 1
        Next
        WAY_MADE = ways.Count
        If WAY_SAY < 6 Then
            WAY_SAY += 1
            Dim fits_ = 0, widest_ = 0.0F, widestFit_ = 0.0F
            For Each w In ways
                If w.chord > widest_ Then widest_ = w.chord
                If w.fits Then
                    fits_ += 1
                    If w.chord > widestFit_ Then widestFit_ = w.chord
                End If
            Next
            LogThis("brain: buildways - cand {0}, list {1} | FITS {2} | " &
                    "widest any {3:0.0} m, widest fitting {4:0.0} m",
                    WAY_CAND, ways.Count, fits_, widest_, widestFit_)
        End If
        Return ways
    End Function


    ''' <summary>What two viewpoints agree and disagree about.</summary>
    Public Structure Parallax
        ''' <summary>How far apart the two origins were, in metres.</summary>
        Public L As Single

        ''' <summary>Per ray: how much FURTHER the second scan saw. Positive is
        ''' an opening - no surface facing us can recede as we close on it.
        ''' Negative is the ordinary foreshortening of closing distance, or, if
        ''' it is steeper than closing distance allows, something new.</summary>
        Public opened As Single()

        ''' <summary>The biggest opening, and where. Not checked for
        ''' reachability - that is the brain's question, not the radar's.</summary>
        Public gain As Single
        Public bearing As Single

        ''' <summary>0..1, the share of forward rays whose two readings are
        ''' consistent with one surface. High means this picture survives the
        ''' next L metres; low means we are looking at something we have not
        ''' really seen yet.</summary>
        Public agree As Single
        Public counted As Integer

        ''' <summary>Per ray, what the pair meant. 0 not counted (astern,
        ''' or nothing found either time), 1 consistent - one surface seen
        ''' twice, 2 an OPENING, 3 an intrusion. Named here rather than
        ''' recomputed by whoever wants to draw it: two copies of a band
        ''' test is two chances to disagree about what the radar
        ''' decided.</summary>
        Public verdict As Integer()
    End Structure

    ''' <summary>
    ''' COMPARE TWO SWEEPS TAKEN L METRES APART ON ONE HEADING.
    '''
    ''' Ray i is the same world direction in both, so the pair is two
    ''' measurements of one direction from two places. If both stopped on a
    ''' single flat surface with normal n then
    '''
    '''     d_far - d_near = -L * (f.n) / (u.n)
    '''
    ''' which is -L/cos(a) for a wall square across the nose and exactly 0 for
    ''' one running along the heading. Every case in between lies between those
    ''' two, and all of them are at or below zero: NOTHING FACING US GETS
    ''' FARTHER AWAY AS WE CLOSE ON IT.
    '''
    ''' So a range that GREW is proof the second ray went past whatever stopped
    ''' the first - an opening - and it is proof without knowing the surface or
    ''' its orientation. A range that shrank by more than L/cos(a) is likewise
    ''' more than closing can explain, so something intruded. Only the band
    ''' between is "one wall, seen twice", and the share of rays in it is how
    ''' much of this view will still be true L metres from now.
    '''
    ''' A ray that found NOTHING counts as the full reach rather than being
    ''' skipped. It is a real reading - "clear to the horizon that way" - and
    ''' dropping it loses the strongest openings of all, the ones where a wall
    ''' from here simply is not there from up the road.
    '''
    ''' FORWARD HEMISPHERE ONLY. Behind the sample point is ground we have just
    ''' driven over, and the same relative bearing astern lands on something
    ''' else entirely after L metres of travel - the reflection the owner
    ''' warned about. cos(a) also goes to zero at the beam, which would make the
    ''' intrusion bound meaningless.
    ''' </summary>
    Public Function Compare(near_ As Hit(), far As Hit(), L As Single) As Parallax
        Dim px As Parallax
        px.L = L
        px.bearing = 0.0F
        If near_ Is Nothing OrElse far Is Nothing Then Return px

        Dim n = Math.Min(near_.Length, far.Length)
        Dim opened(n - 1) As Single
        Dim verdict(n - 1) As Integer
        Dim agreed = 0

        For i = 0 To n - 1
            Dim a = wrap_to_pi(near_(i).angle)
            Dim ca = CSng(Math.Cos(a))
            ' 1.4 rad is the same forward limit the look-ahead doors use; past
            ' it the bound below divides by something near zero.
            If Math.Abs(a) > 1.4F OrElse ca < 0.17F Then Continue For
            If Not near_(i).found AndAlso Not far(i).found Then Continue For

            Dim dn = If(near_(i).found, near_(i).dist, REACH_M)
            Dim df = If(far(i).found, far(i).dist, REACH_M)
            opened(i) = df - dn
            px.counted += 1

            If opened(i) > 0.5F Then
                ' An opening. Half a metre of slack for the cell walk.
                verdict(i) = 2
                If opened(i) > px.gain Then
                    px.gain = opened(i)
                    px.bearing = a
                End If
            ElseIf (dn - df) > (L / ca) + 0.5F Then
                ' Closed faster than closing distance can explain.
                verdict(i) = 3
            Else
                verdict(i) = 1
                agreed += 1
            End If
        Next

        px.opened = opened
        px.verdict = verdict
        px.agree = If(px.counted > 0, agreed / CSng(px.counted), 0.0F)
        Return px
    End Function

    ''' <summary>
    ''' ONE CLOUD FROM TWO VIEWPOINTS.
    '''
    ''' The near sweep wins every direction it has a return in - it is what
    ''' actually blocks the hull, and it was taken from where the hull actually
    ''' is. The far sweep only fills directions the near one found NOTHING in,
    ''' and those are precisely the near sweep's occlusion shadow: ground it
    ''' could not see round a corner.
    '''
    ''' THAT ASYMMETRY IS THE SAFETY ARGUMENT. Filling empty directions can only
    ''' ADD surface, never remove it, so door finding on the union is stricter
    ''' than on the near sweep alone - never looser. A sixty degree void where
    ''' nothing returned is not a doorway, it is unknown ground; putting real
    ''' returns into it turns unknown into known and splits the void into the
    ''' doors that are really there.
    '''
    ''' Points are re-measured FROM THE HULL - a far point's own range and
    ''' bearing are relative to where the far sweep stood, and everything
    ''' downstream is the hull's frame.
    ''' </summary>
    Public Function Union(near_ As Hit(), far As Hit(), pos As Vector2,
                          headingRad As Single, bodyR As Single) As Hit()
        If near_ Is Nothing Then Return far
        Dim out(near_.Length - 1) As Hit
        Array.Copy(near_, out, near_.Length)
        If far Is Nothing Then Return out

        Dim span = CSng(Math.PI * 2.0)
        For i = 0 To far.Length - 1
            If Not far(i).found Then Continue For
            Dim v = far(i).at - pos
            Dim d = v.Length
            If d < 0.5F OrElse d > REACH_M Then Continue For

            Dim ang = wrap_to_pi(CSng(Math.Atan2(v.X, v.Y)) - headingRad)
            ' Bearings() lays one slice per ray evenly round the circle, so the
            ' slot is arithmetic rather than a search.
            Dim k = CInt(Math.Floor((ang + Math.PI) / span * out.Length))
            If k < 0 Then k = 0
            If k >= out.Length Then k = out.Length - 1
            If out(k).found Then Continue For

            out(k).found = True
            out(k).dist = d
            out(k).at = far(i).at
            out(k).angle = ang
            out(k).front = Math.Abs(ang) < CSng(Math.PI) * 0.5F
            out(k).row = far(i).row
            out(k).col = far(i).col
        Next

        ' CHORDS AND LINKS AGAIN, or BuildWays reads the near sweep's answers
        ' for slots that now hold a different point - and `linked` is what
        ' decides whether a pair of returns is a wall or a doorway.
        Dim gate = If(bodyR > 0.0F, bodyR * 2.0F, 0.0F)
        For i = 0 To out.Length - 1
            Dim j = (i + 1) Mod out.Length
            out(i).chord = (out(i).at - out(j).at).Length
            out(i).linked = out(i).found AndAlso out(j).found AndAlso
                            out(i).chord < gate
        Next
        Return out
    End Function

    ''' <summary>Angle into -pi..pi. Local, because BrainRadar cannot see the
    ''' brain's copy and two of these drifting apart is a class of bug this
    ''' project has already paid for once.</summary>
    Private Function wrap_to_pi(a As Single) As Single
        While a > Math.PI
            a -= CSng(Math.PI * 2.0)
        End While
        While a < -Math.PI
            a += CSng(Math.PI * 2.0)
        End While
        Return a
    End Function

    ''' <summary>
    ''' ONE INTEGER WALK, REUSABLE. Distance from a to b until a blocked cell,
    ''' or -1 if the whole line is clear. The ray sweep has its own copy inline
    ''' because it needs the cell it stopped in; this is for everything else.
    ''' </summary>
    Private Function trace_cells(ax As Single, az As Single,
                                 bx As Single, bz As Single) As Single
        Dim c0 = BrainNav.ColOf(ax), r0 = BrainNav.RowOf(az)
        Dim c1 = BrainNav.ColOf(bx), r1 = BrainNav.RowOf(bz)
        Dim sc = Math.Sign(c1 - c0), sr = Math.Sign(r1 - r0)
        Dim dc = Math.Abs(c1 - c0), dr = Math.Abs(r1 - r0)
        Dim cc = c0, rr = r0
        Dim err = dc - dr
        Dim guard = dc + dr + 2
        While guard > 0
            guard -= 1
            If (cc <> c0 OrElse rr <> r0) AndAlso BrainNav.BlockedCell(cc, rr) Then
                Dim gc = cc - c0, gr = rr - r0
                Return CSng(Math.Sqrt(CDbl(gc) * gc + CDbl(gr) * gr)) * BrainNav.CellSize
            End If
            If cc = c1 AndAlso rr = r1 Then Exit While
            Dim e2 = err * 2
            If e2 > -dr AndAlso cc <> c1 Then
                err -= dr
                cc += sc
            ElseIf rr <> r1 Then
                err += dc
                rr += sr
            Else
                Exit While
            End If
        End While
        Return -1.0F
    End Function

    ''' <summary>
    ''' A WAY THROUGH, once something has actually been driven at it.
    ''' </summary>
    Public Structure Way
        ''' <summary>Which rays the jambs are. The scope plots in the hull's
        ''' frame and already has every ray's angle and range, so handing it
        ''' indices costs nothing and spares it a world-to-hull transform it
        ''' would otherwise have to keep in step with this file.</summary>
        Public ia As Integer
        Public ib As Integer
        Public a As Vector2         ' the near jamb, where a ray stopped
        Public b As Vector2         ' the far jamb
        Public mid As Vector2       ' aim point, pushed PAST the opening
        Public chord As Single      ' jamb to jamb, straight line
        Public fits As Boolean      ' the hull's own corridor cleared it
        Public bearing As Single    ' relative to the nose
    End Structure

    ''' <summary>Every gap the last scan found, with the ones the hull actually
    ''' fits through marked. Read by the scope and by the brain, so both are
    ''' looking at one answer.</summary>
    Public WAYS As New List(Of Way)

    ''' <summary>TEMPORARY: where BuildWays threw each candidate away.
    ''' Ways came back zero with a dozen edges on the board and three
    ''' different rejections could each produce that.</summary>
    Public WAY_CAND As Integer
    Public WAY_FAR As Integer
    Public WAY_NARROW As Integer
    Public WAY_NEAR As Integer
    Public WAY_NOJ As Integer
    Public WAY_MADE As Integer
    Public WAY_ADDS As Integer
    Public WAY_SAY As Integer = 0
    Public SCAN_SAY As Integer = 0
    Public WAY_FIRST_I As Integer = -1
    Public WAY_FIRST_J As Integer = -1

    ''' <summary>
    ''' THE LAST CORRIDOR, IN THE HULL'S OWN FRAME, for the scope to draw.
    '''
    ''' Lateral offset and forward length rather than world points, because
    ''' that is what the scope plots in - handing it world coordinates would
    ''' make it reconstruct a transform it does not otherwise need, and a
    ''' second copy of that transform is a second thing to get wrong.
    ''' </summary>
    Public LANE_OFF As Single() = Nothing     ' metres left(-) / right(+) of the centreline
    Public LANE_LEN As Single() = Nothing     ' metres forward before it stopped
    Public LANE_HIT As Boolean() = Nothing

    ''' <summary>
    ''' WHICH MODE THE BRAIN IS IN, and they are exclusive.
    '''
    ''' PLANK MODE is the cheap question: cast the corridor, is the box ahead
    ''' clear, keep driving. SCAN MODE is what a plank touching something
    ''' starts: the full sweep decides a way through and the hull drives to it.
    '''
    ''' While scanning, the corridor is NOT cast. It was, every tick, and the
    ''' guard on re-triggering hid it: the cast still ran and still rewrote the
    ''' plank arrays, so as the hull turned toward its chosen gap the planks
    ''' came clear, LANE_ACTIVE went false, and the rays - gated on it -
    ''' vanished in the middle of the manoeuvre they were drawn to explain.
    '''
    ''' The display follows the same rule: planks when planking, rays when
    ''' scanning. Never both, because the tank is never asking both.
    ''' </summary>
    Public SCANNING As Boolean = False

    ''' <summary>True while any plank is touching something. The scope uses it
    ''' to decide whether the full scan is worth showing: when nothing is in
    ''' the corridor there is no decision being made and 120 rays are just
    ''' noise over the top of the thing that matters.</summary>
    Public LANE_ACTIVE As Boolean = False

    ''' <summary>What the corridor test found.</summary>
    Public Structure Lane
        Public hit As Boolean
        Public dist As Single       ' nearest plank return, REACH if none
        Public leftHit As Boolean   ' which side of the centreline it was on
        Public rightHit As Boolean
        Public planks As Integer
        Public blocked As Integer
    End Structure

    ''' <summary>
    ''' CAN WE KEEP GOING STRAIGHT? A block of parallel rays the width of the
    ''' tank, cast straight out from the corners.
    '''
    ''' "shot block of rays the width of our tank, straight out from corners.
    '''  we want to be one plk left and right of fenders and fill all between.
    '''  if one hits, we trigger scanning" - the owner, 2026-09-17.
    '''
    ''' PARALLEL, NOT FANNED, and that is the whole idea. A fan from the hull's
    ''' centre answers "what is out there"; a block of parallel lines the width
    ''' of the tank answers the only question that decides whether to keep the
    ''' throttle down - does the box I am about to sweep contain anything. They
    ''' are different questions and the fan has been standing in for this one
    ''' all along, which is why a ray down a gap two metres across kept
    ''' reporting twenty clear metres.
    '''
    ''' ONE PLANK PROUD OF EACH FENDER, so the corridor is slightly wider than
    ''' the hull. A tank that clears by nothing clears until it yaws.
    '''
    ''' AND IT SAYS WHICH SIDE. A swept-box test returns one bit and throws
    ''' that away; knowing the left planks are the blocked ones is what lets
    ''' the answer be "go right" rather than "stop and think".
    ''' </summary>
    ''' <param name="record">
    ''' Write the LANE_* arrays the scope draws from. TRUE only for the hull's
    ''' own forward corridor.
    '''
    ''' THIS EXISTS BECAUSE THE DISPLAY WAS LYING. Scan calls this once per
    ''' CANDIDATE GAP to decide whether the hull fits through it - and every
    ''' one of those calls was overwriting the plank arrays and LANE_ACTIVE.
    ''' So the planks drawn were whichever gap happened to be verified last,
    ''' and the rays, which are gated on LANE_ACTIVE, flashed on and off with
    ''' it. It read as flickering ray data and it was the picture showing a
    ''' different question from the one the tank was asking.
    ''' </param>
    Public Function Corridor(pos As Vector2, headingRad As Single,
                             halfWidth As Single, reach As Single,
                             Optional record As Boolean = True) As Lane
        Dim lane As Lane
        Dim dx = CSng(Math.Sin(headingRad)), dz = CSng(Math.Cos(headingRad))
        Dim px = -dz, pz = dx                      ' unit perpendicular
        Dim cell = BrainNav.CellSize
        Dim edge = halfWidth + cell                ' one plank proud of the fender
        Dim n = CInt(Math.Ceiling(edge * 2.0F / cell)) + 1
        If n < 3 Then n = 3

        lane.dist = reach
        lane.planks = n
        If record Then LANE_ACTIVE = False
        If record AndAlso (LANE_OFF Is Nothing OrElse LANE_OFF.Length <> n) Then
            ReDim LANE_OFF(n - 1)
            ReDim LANE_LEN(n - 1)
            ReDim LANE_HIT(n - 1)
        End If
        For i = 0 To n - 1
            Dim off = -edge + (2.0F * edge) * i / (n - 1)
            Dim ax = pos.X + px * off, az = pos.Y + pz * off
            Dim d = trace_cells(ax, az, ax + dx * reach, az + dz * reach)
            If record Then
                LANE_OFF(i) = off
                LANE_LEN(i) = If(d >= 0.0F, d, reach)
                LANE_HIT(i) = (d >= 0.0F)
            End If
            If d >= 0.0F Then
                lane.hit = True
                If record Then LANE_ACTIVE = True
                lane.blocked += 1
                If d < lane.dist Then lane.dist = d
                If off < -cell * 0.5F Then lane.leftHit = True
                If off > cell * 0.5F Then lane.rightHit = True
            End If
        Next
        Return lane
    End Function

    ''' <summary>
    ''' THE WIDEST WAY OUT, measured jamb to jamb.
    '''
    ''' "we scan and look for the widest way out of the hits. we scan, find
    '''  center of cord and aim for it."
    '''
    ''' Walks the sweep for returns that are NOT joined to their neighbour - a
    ''' break in the barrier - and takes the chord from there to the next
    ''' return. The widest such chord that the hull fits through, and whose
    ''' jambs are inside the 90-degree rule, is the way out. Its centre is a
    ''' POINT in the world, not a bearing: a bearing is only true from where it
    ''' was measured and the hull is moving while it turns.
    ''' </summary>
    Public Function WidestGap(hits As Hit(), needM As Single,
                              ByRef widthOut As Single) As Vector2
        widthOut = 0.0F
        Dim best As Vector2 = Vector2.Zero
        If hits Is Nothing OrElse hits.Length < 3 Then Return best
        Dim maxSteps = CInt(90.0F / (ARC_DEG / RAYS))
        For i = 0 To hits.Length - 1
            If Not hits(i).found OrElse hits(i).linked Then Continue For
            Dim j = -1
            For k = 1 To hits.Length - 1
                Dim m = (i + k) Mod hits.Length
                If hits(m).found Then
                    j = m
                    Exit For
                End If
            Next
            If j < 0 OrElse j = i Then Continue For
            If (((j - i) + hits.Length) Mod hits.Length) > maxSteps Then Continue For
            Dim span = (hits(i).at - hits(j).at).Length
            If span < needM OrElse span <= widthOut Then Continue For
            widthOut = span
            best = (hits(i).at + hits(j).at) * 0.5F
        Next
        Return best
    End Function

    ''' <summary>Rebuild the line buffer from the current hull.</summary>
    Public Sub Update()
        If Not SHOW Then
            verts = 0
            built_show = False
            Return
        End If
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count <= HULL Then Return

        ' spawn IS the live position - the sim writes movement back into it.
        Dim b = BrainTanks.Bodies(HULL)

        ' A tenth of a metre and a tenth of a degree: below that nothing
        ' visible changes, and rebuilding is pure cost.
        If built_show AndAlso
           Math.Abs(b.spawn.X - built_pos.X) < 0.1F AndAlso
           Math.Abs(b.spawn.Y - built_pos.Y) < 0.1F AndAlso
           Math.Abs(b.headingRad - built_head) < 0.002F Then
            Return
        End If
        built_pos = b.spawn
        built_head = b.headingRad
        built_show = True

        Dim hits = Scan(b.spawn, b.headingRad, b.half.X + 0.3F)
        If Not said Then LogThis("brain: radar scan returned {0} ray(s), RAYS={1}",
                                 If(hits Is Nothing, -1, hits.Length), RAYS)

        Dim v As New List(Of Single)
        For Each q In hits
            add_line(v, b.spawn, q.at)
            If q.found Then
                ' THE SQUARE IT LANDED IN, as a box on the ground at that
                ' cell's true size. This is the part worth looking at.
                Dim cx = BrainNav.CellX0 + (q.col + 0.5F) * BrainNav.CellSize
                Dim cz = BrainNav.CellZTop - (q.row + 0.5F) * BrainNav.CellSize
                Dim r = BrainNav.CellSize * 0.5F
                ' p1..p4, not a..d: b is the BODY in this scope and VB would
                ' not let it be redeclared - the sort of shadowing that reads
                ' fine and refuses to compile.
                Dim p1 = New Vector2(cx - r, cz - r)
                Dim p2 = New Vector2(cx + r, cz - r)
                Dim p3 = New Vector2(cx + r, cz + r)
                Dim p4 = New Vector2(cx - r, cz + r)
                add_line(v, p1, p2) : add_line(v, p2, p3)
                add_line(v, p3, p4) : add_line(v, p4, p1)
            End If
        Next

        Dim arr = v.ToArray()
        verts = arr.Length \ 3
        If Not said Then
            said = True
            Dim ymin = Single.MaxValue, ymax = Single.MinValue
            For k = 1 To arr.Length - 1 Step 3
                If arr(k) < ymin Then ymin = arr(k)
                If arr(k) > ymax Then ymax = arr(k)
            Next
            LogThis("brain: radar built {0} vert(s), y {1:0.0}..{2:0.0}, hull at " &
                    "({3:0.0}, {4:0.0}) y {5:0.0}, shader {6}",
                    verts, ymin, ymax, b.spawn.X, b.spawn.Y, b.y,
                    If(shader Is Nothing, "NULL", If(shader.Ready, "ready", "NOT READY")))
        End If
        If verts = 0 Then Return
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>A segment, both ends dropped onto the terrain under them, so a
    ''' ray follows the ground instead of sailing over a dip.</summary>
    Private Sub add_line(v As List(Of Single), a As Vector2, b As Vector2)
        For Each p In {a, b}
            Dim y = 0.0F
            Try
                ' BrainNav.Ground, NOT nuTerra's get_Y_at_XZ_fast.
                '
                ' The fast one returns 0.0 when mapBoard is Nothing, and this
                ' app never fills mapBoard - it builds its own terrain and
                ' reads it through BrainNav. Swapping to it for speed silently
                ' put every ray end at y = 0, which is UNDER the ground: the
                ' rays did not stop being drawn, they were drawn inside the
                ' hill. Nothing errored and nothing logged.
                '
                ' The speed came from the movement cache above, not from this
                ' call. Borrowing another app's fast path was never the win.
                y = BrainNav.Ground(p.X, p.Y)
            Catch
            End Try
            v.Add(p.X) : v.Add(y + LIFT) : v.Add(p.Y)
        Next
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If Not SHOW OrElse verts = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' Depth WRITE off for the same reason the rings do it: these lie on the
        ' ground and would leave a lip that hulls standing on them clip against.
        GL.DepthMask(False)
        shader.SetVec3("colour", New Vector3(0.45F, 0.85F, 1.0F))
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.DepthMask(True)
    End Sub

    ''' <summary>What a fit across the front arc concluded.</summary>
    Public Structure Surface
        Public valid As Boolean      ' enough hits to say anything
        Public flat As Boolean
        Public residual As Single    ' mean metres off a straight wall
        Public dist As Single        ' perpendicular distance to it
        Public normalRad As Single   ' bearing of its normal, relative to nose
        Public edgeRay As Integer    ' first ray that stopped fitting, or -1
        Public used As Integer
        Public verdict As String

        ' ---- the SHAPE test, which is the owner's and is the better one ----
        Public turns As Integer      ' turning points in the range sequence
        Public gapped As Boolean     ' a miss in the middle of the run
        ''' <summary>HOW MANY breaks, not just whether there was one. One gap is
        ''' a doorway; three is a hedge, a tree and a corner, and the fit is
        ''' describing none of them.</summary>
        Public gaps As Integer
        Public turnRay As Integer    ' the nearest ray: closest approach
        Public turnDist As Single    ' its range - d, without the fit
        Public oneSurface As Boolean
    End Structure

    ''' <summary>
    ''' IS THIS ONE SURFACE? By the shape of the range sequence alone.
    '''
    ''' The owner's test, and it is better than the residual one: "is each
    ''' length consecutivly growing or shrinking?"
    '''
    ''' For a flat wall r(t) = d / cos(t - phi), so sweeping across the arc the
    ''' ranges SHRINK until the ray nearest the wall's normal and GROW after
    ''' it. One turning point, or none at all when the normal falls outside the
    ''' arc. Two or more means two or more surfaces - a corner, a gap, clutter.
    '''
    ''' WHY IT BEATS MEASURING THE RESIDUAL. The grid is a metre, so a wall at
    ''' an angle IS a staircase and a line fitted through it has error BY
    ''' CONSTRUCTION - up to half a cell diagonal, and worst at 45 degrees.
    ''' That made a fixed residual threshold wrong in both directions at once:
    ''' too tight for an angled wall, too loose for a square one.
    '''
    ''' Quantisation moves each range a little. It does not REVERSE a trend. So
    ''' the shape survives exactly the noise that was corrupting the magnitude.
    '''
    ''' THE TOLERANCE IS NOT A FUDGE. Consecutive rays landing on the same
    ''' tread return the SAME range, and a strict "must be decreasing" test
    ''' would count every tread as a reversal and call every angled wall broken.
    ''' Ties are the staircase, not evidence.
    ''' </summary>
    Private Sub shape_test(hits As Hit(), idx As List(Of Integer), ByRef s As Surface)
        s.turnRay = -1
        s.turns = 0
        If idx.Count < 3 Then Return

        ' A MISS IN THE MIDDLE IS A GAP, and a gap is two objects however
        ' nicely the rest of the ranges behave.
        For k = 0 To idx.Count - 2
            If idx(k + 1) <> idx(k) + 1 Then
                s.gapped = True
                s.gaps += 1
            End If
        Next

        ' Half a cell: the most the staircase can move one sample.
        Dim tol = BrainNav.CellSize * 0.5F

        Dim dir = 0                     ' -1 shrinking, +1 growing, 0 flat so far
        Dim nearest = Single.MaxValue
        For k = 0 To idx.Count - 2
            Dim a = hits(idx(k)).dist
            Dim b = hits(idx(k + 1)).dist
            If a < nearest Then nearest = a : s.turnRay = idx(k)
            Dim d = b - a
            If Math.Abs(d) <= tol Then Continue For       ' same tread: no news
            Dim nd = If(d > 0.0F, 1, -1)
            If dir <> 0 AndAlso nd <> dir Then s.turns += 1
            dir = nd
        Next
        Dim last = hits(idx(idx.Count - 1)).dist
        If last < nearest Then nearest = last : s.turnRay = idx(idx.Count - 1)
        s.turnDist = nearest

        ' One surface: at most one turn, and no hole in the middle of it.
        s.oneSurface = (s.turns <= 1) AndAlso Not s.gapped
    End Sub

    ''' <summary>
    ''' IS WHAT I AM LOOKING AT A FLAT WALL, AND WHERE DOES IT END?
    '''
    ''' The owner's idea, and it is the right one: "we need a very fast way to
    ''' find out if a surface scanned is flat or smooth. We could trig it and
    ''' get the diff from hit to radar center?"
    '''
    ''' THE TRICK THAT MAKES IT CHEAP. For a straight wall at perpendicular
    ''' distance d whose normal points at phi, a ray at angle t returns
    '''
    '''     r(t) = d / cos(t - phi)
    '''
    ''' Invert it and the awkward division becomes a straight line:
    '''
    '''     1/r = (cos phi / d) * cos t + (sin phi / d) * sin t
    '''
    ''' So 1/r is LINEAR in (cos t, sin t). Fitting two coefficients across the
    ''' arc is five running sums and a 2x2 solve - no square roots, no atan
    ''' except once at the end for the bearing - and the residual IS the
    ''' roughness. A wall fits; a corner, a gap or clutter does not.
    '''
    ''' AND THE EDGE COMES FREE. Rays that fit, then one that suddenly does
    ''' not, is where the wall stops. That is "find the edge of the blocker"
    ''' answered by LOOKING rather than by driving into it and backing off.
    '''
    ''' Added 2026-09-16 by Tank AI work.
    ''' </summary>
    Public Function FitSurface(Optional hits As Hit() = Nothing) As Surface
        Dim s As Surface
        s.edgeRay = -1
        s.verdict = "nothing"
        If hits Is Nothing Then hits = LAST
        If hits Is Nothing Then Return s

        ' Front arc, hits only. A ray that found nothing has no 1/r.
        '
        ' AND ONLY THE INNER FIT_ARC_DEG OF IT. The sweep is wider than the
        ' fit: the outer rays are there to find a way round, and they are the
        ' ones that look at ground the hull drives past. Letting them into the
        ' fit is what put turning points into the shape test.
        Dim fitHalf = MathHelper.DegreesToRadians(FIT_ARC_DEG * 0.5F)
        Dim idx As New List(Of Integer)
        For i = 0 To hits.Length - 1
            If hits(i).front AndAlso hits(i).found AndAlso hits(i).dist > 0.5F AndAlso
               Math.Abs(hits(i).angle) <= fitHalf Then idx.Add(i)
        Next
        s.used = idx.Count
        If idx.Count < 4 Then
            s.verdict = If(idx.Count = 0, "open ahead", "too little to judge")
            Return s
        End If

        Dim sxx = 0.0, sxy = 0.0, syy = 0.0, bx = 0.0, by = 0.0
        For Each i In idx
            Dim t = hits(i).angle
            Dim c = Math.Cos(t), sn = Math.Sin(t)
            Dim u = 1.0 / hits(i).dist
            sxx += c * c : sxy += c * sn : syy += sn * sn
            bx += u * c : by += u * sn
        Next
        Dim det = sxx * syy - sxy * sxy
        If Math.Abs(det) < 0.000001 Then
            s.verdict = "cannot fit"
            Return s
        End If
        Dim a = (bx * syy - by * sxy) / det
        Dim b = (sxx * by - sxy * bx) / det

        Dim mag = Math.Sqrt(a * a + b * b)
        If mag < 0.000001 Then
            s.verdict = "cannot fit"
            Return s
        End If
        s.dist = CSng(1.0 / mag)
        s.normalRad = CSng(Math.Atan2(b, a))

        ' Residual in METRES, not in 1/r - a tenth of a reciprocal means
        ' nothing to anybody, and the threshold has to be a distance.
        shape_test(hits, idx, s)

        Dim total = 0.0
        Dim worst = 0.0
        For Each i In idx
            Dim t = hits(i).angle
            Dim pred = a * Math.Cos(t) + b * Math.Sin(t)
            If pred <= 0.000001 Then Continue For
            Dim rhat = 1.0 / pred
            Dim e = Math.Abs(hits(i).dist - rhat)
            total += e
            If e > worst Then worst = e
            ' The first ray that leaves the wall, walking outward from the
            ' middle of the arc, is its edge.
            If s.edgeRay < 0 AndAlso e > 1.5 Then s.edgeRay = i
        Next
        s.residual = CSng(total / idx.Count)
        s.valid = True

        ' THE SHAPE DECIDES, the residual describes. One surface or not is the
        ' question the shape answers and answers without caring about the
        ' staircase; how straight and how far away are what the fit adds once
        ' the answer is yes.
        s.flat = s.oneSurface
        If s.gapped Then
            s.verdict = String.Format("TWO things - a gap in the returns ({0} turns)",
                                      s.turns)
        ElseIf s.turns >= 2 Then
            s.verdict = String.Format("BROKEN - {0} turning points, not one surface",
                                      s.turns)
        ElseIf s.residual < 0.5F Then
            s.verdict = String.Format("FLAT {0:0.0} m, face {1:0} deg, nearest {2:0.0} m{3}",
                                      s.dist,
                                      MathHelper.RadiansToDegrees(s.normalRad),
                                      s.turnDist,
                                      If(s.edgeRay >= 0, ", edge", ""))
        Else
            s.verdict = String.Format("one surface, curved - nearest {0:0.0} m",
                                      s.turnDist)
        End If
        Return s
    End Function

End Module

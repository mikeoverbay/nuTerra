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
    Public Const REACH_M As Single = 20.0F

    ''' <summary>How far the per-ray DRIVE walk bothers to look. Beyond this a
    ''' heading is a direction rather than a plan - the hull will have rescanned
    ''' four times before it gets there.</summary>
    Public Const DRIVE_REACH_M As Single = 14.0F

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
    Public Function Scan(pos As Vector2, headingRad As Single,
                         Optional bodyR As Single = 0.0F) As Hit()
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
            Dim yPrev = BrainNav.CellHeight(c0, r0)

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

                ' ---- WHAT THE SQUARE IS LIKE TO DRIVE ON --------------------
                '
                ' One height per square we land on, and the angle to the square
                ' before it. The walk moves ONE AXIS a step, so consecutive
                ' squares are always exactly one cell apart - the gradient is a
                ' subtraction over a constant, with no divide and no second
                ' sample. That constant spacing is a property of the walk, not
                ' an assumption about it.
                '
                ' This is what the low spot defeated. A bowl gentle over half a
                ' metre and steep across the hull read as twenty clear metres,
                ' the tank drove in, and then the body-radius test failed in
                ' every direction at once. Checked per square against the same
                ' MAX_SLOPE the sim uses, the rim is seen from outside it.
                If driveOpen AndAlso (cc <> c0 OrElse rr <> r0) Then
                    Dim yHere = BrainNav.CellHeight(cc, rr)
                    If Math.Abs(yHere - yPrev) / BrainNav.CellSize > BrainNav.MAX_SLOPE Then
                        driveOpen = False
                    Else
                        ' The hull's width, either side, same two tests.
                        For q = 1 To halfCells
                            Dim ox = CInt(Math.Round(pxq * q)), oz = CInt(Math.Round(pzq * q))
                            If BrainNav.BlockedCell(cc + ox, rr - oz) OrElse
                               BrainNav.BlockedCell(cc - ox, rr + oz) Then
                                driveOpen = False
                                Exit For
                            End If
                            ' ACROSS the hull as well as along it: a side slope
                            ' steep enough to shed a tank does not show up in
                            ' the gradient along its own line of travel.
                            If Math.Abs(BrainNav.CellHeight(cc + ox, rr - oz) - yHere) /
                               (q * BrainNav.CellSize) > BrainNav.MAX_SLOPE OrElse
                               Math.Abs(BrainNav.CellHeight(cc - ox, rr + oz) - yHere) /
                               (q * BrainNav.CellSize) > BrainNav.MAX_SLOPE Then
                                driveOpen = False
                                Exit For
                            End If
                        Next
                    End If
                    If driveOpen Then
                        Dim ac = cc - c0, ar = rr - r0
                        driveD2 = ac * ac + ar * ar
                    End If
                    yPrev = yHere
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
        LAST = out
        Return out
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
            If idx(k + 1) <> idx(k) + 1 Then s.gapped = True
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

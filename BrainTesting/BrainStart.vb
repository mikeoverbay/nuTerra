Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' THE START MARKER, AND YOU DRAG IT.
'''
''' "i want a start marker I can drag on the map" then "mouse down and drag" -
''' the owner, 2026-09-19.
'''
''' A MARKER THAT DOES NOT MOVE THE SPAWN IS DECORATION. BrainPanel remembers
''' the opening positions once at load and Reset puts the hulls back to them,
''' so moving a marker without writing that list means the first Reset drags
''' the tank back to where it started and the marker silently means nothing.
''' MoveTo writes both the live hull and the remembered origin.
'''
''' IT TAKES THE MOUSE THE WAY ImGui DOES. BrainWindow already asks ImGui
''' "is the pointer yours" before handing the frame to the camera; this asks
''' the same question one line later. Claiming the button rather than adding a
''' suppression flag inside BrainCamera means the camera keeps one rule - it
''' orbits when it is given the mouse - and there is no second place where
''' orbiting can be switched off and forgotten about.
'''
''' THE GRAB IS A COLOUR PICK, NOT A DISTANCE. BrainRender.PickAt draws the
''' terrain into DEPTH ONLY and this square solid in PICK_RGB, then reads the
''' one pixel under the cursor. So the hit area is the square as drawn - it
''' shrinks with perspective the way the marker does, and a marker behind a
''' hill is occluded by the terrain's depth and cannot be grabbed through it.
''' A pixel-radius test could do neither.
'''
''' A PRESS THAT BEGAN ELSEWHERE IS NOT OURS. The grab only happens on the
''' frame the button goes DOWN. Without that, sweeping an orbit across the
''' marker would snatch the camera drag halfway through, which reads as the
''' map jumping.
''' </summary>
Module BrainStart

    Public SHOW As Boolean = True

    ''' <summary>Where the driven hull starts, world XZ.</summary>
    Public Pos As Vector2
    Public HasStart As Boolean = False

    ''' <summary>
    ''' WHICH WAY IT FACES AT THE START, radians, same convention as
    ''' Body.headingRad - 0 is +Z and it increases toward +X.
    '''
    ''' "i need a way to set the start heading of the tank and remember it.
    '''  A draggable ring?" - the owner, 2026-09-20. A ring, yes: the heading
    ''' is an angle about the start, so the control that sets it should be a
    ''' thing you swing about the start rather than a number in a box.
    ''' </summary>
    Public HeadingRad As Single = 0.0F


    ''' <summary>
    ''' GRID STEPS ACROSS THE SQUARE. The square is defined top-down in XZ and
    ''' every vertex takes its Y from the ground, so it drapes over a slope
    ''' instead of hovering at one height.
    '''
    ''' One flat quad at the centre's height sank into the uphill side and
    ''' floated off the downhill one - metres of error across a 12 m square on
    ''' any real slope.
    ''' </summary>
    Private Const N As Integer = 8

    ''' <summary>Metres above the ground. Coplanar it z-fights and half the
    ''' square speckles away; a hand's breadth is enough and stays invisible
    ''' from any angle this is looked at from.</summary>
    Private Const LIFT As Single = 0.15F

    ''' <summary>Border always; fill only while it is held. The owner:
    ''' "white border and no fill when not mouse down on it", and
    ''' "I want it to just lit when I mouse down on it". So the fill IS the
    ''' feedback - there is no second highlight state to keep in step.</summary>
    Public ReadOnly EDGE_RGB As New Vector3(1.0F, 1.0F, 1.0F)
    Private ReadOnly FILL_RGB As New Vector3(0.13F, 0.13F, 0.14F)

    ''' <summary>The marker's ID in the pick pass. Pure and saturated so the
    ''' readback is an exact byte match - the line shader is flat and unlit and
    ''' blending is off for the pass, so 255,0,255 arrives as 255,0,255.</summary>
    Private ReadOnly PICK_RGB As New Vector3(1.0F, 0.0F, 1.0F)

    ''' <summary>Half-width of the pick square, metres. The SQUARE is what is
    ''' grabbable - the cross is decoration - so this is the hit area and it is
    ''' stated in metres on the ground rather than pixels on the screen.</summary>
    Private Const BASE As Single = 6.0F

    ''' <summary>
    ''' THE RING'S BAND, metres from the start. Outside BASE on purpose - the
    ''' square is +/-6 m, so at 7.5 the two controls cannot overlap and a
    ''' press is never ambiguous about which one it meant.
    ''' </summary>
    Private Const RING_IN As Single = 7.5F
    Private Const RING_OUT As Single = 10.5F

    ''' <summary>Segments round the ring. Enough that it reads as a circle at
    ''' the distance this is looked at from, few enough that draping it is not
    ''' a terrain query per degree.</summary>
    Private Const RING_SEGS As Integer = 48

    ''' <summary>Amber for the ring and its nose, so it is plainly a different
    ''' control from the white square rather than part of it.</summary>
    Public ReadOnly RING_RGB As New Vector3(1.0F, 0.75F, 0.2F)

    Private shader As BrainShader
    ''' <summary>The draped fill, triangles. Drawn dark grey when held and in
    ''' PICK_RGB for the pick pass - ONE geometry, so what you grab is exactly
    ''' what you see, on a slope as much as on the flat.</summary>
    Private fillVao, fillVbo, fillVerts As Integer
    ''' <summary>The perimeter, line segments, draped the same way.</summary>
    Private edgeVao, edgeVbo, edgeVerts As Integer
    ''' <summary>The heading ring and the nose tick that says which way it
    ''' points, line segments, draped like everything else here.</summary>
    Private ringVao, ringVbo, ringVerts As Integer
    Private built_head As Single = Single.MaxValue
    Private built_at As New Vector2(Single.MaxValue, Single.MaxValue)

    Private dragging As Boolean = False
    ''' <summary>Swinging the ring rather than moving the square. A separate
    ''' state because they are separate controls and a single flag would let a
    ''' press that began on one finish on the other.</summary>
    Private turning As Boolean = False
    Private wasDown As Boolean = False


    Public Sub Init()
        shader = New BrainShader("line")
        fillVao = GL.GenVertexArray()
        fillVbo = GL.GenBuffer()
        edgeVao = GL.GenVertexArray()
        edgeVbo = GL.GenBuffer()
        ringVao = GL.GenVertexArray()
        ringVbo = GL.GenBuffer()
    End Sub

    ''' <summary>Put the marker where the hull actually opens, once the roster
    ''' has spawned. Called at load rather than guessed at (0,0).</summary>
    Public Sub FromHull()
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return
        Dim i = Math.Min(Math.Max(BrainRadar.HULL, 0), BrainTanks.Bodies.Count - 1)
        Pos = BrainTanks.Bodies(i).spawn
        HasStart = True
    End Sub

    ''' <summary>
    ''' Move the start, and move the thing it is a marker FOR.
    '''
    ''' Reports standable the way PlaceAtLookAt does. A start buried in
    ''' geometry fails exactly like a goal in water: the run simply does not
    ''' work and nothing on screen says why.
    ''' </summary>
    Public Sub MoveTo(p As Vector2)
        Pos = p
        HasStart = True
        BrainPanel.SetSpawn(BrainRadar.HULL, p)
    End Sub

    ''' <summary>Point the start, and point the hull it is a marker FOR.
    ''' Through BrainPanel.SetHeading so the remembered origin moves with it -
    ''' see the note there on why writing only the live body is a bug that
    ''' only shows up one Reset later.</summary>
    Public Sub PointTo(rad As Single)
        HeadingRad = wrap_pi_(rad)
        HasStart = True
        BrainPanel.SetHeading(BrainRadar.HULL, HeadingRad)
    End Sub

    ''' <summary>Local, because BrainStart has no brain to borrow one from and
    ''' an angle that creeps past pi comes back as a heading pointing the
    ''' other way after a few drags.</summary>
    Private Function wrap_pi_(a As Single) As Single
        Dim x = a
        While x > Math.PI
            x -= CSng(Math.PI * 2.0)
        End While
        While x < -Math.PI
            x += CSng(Math.PI * 2.0)
        End While
        Return x
    End Function

    ''' <summary>
    ''' One frame of mouse. True when this owns the pointer, which tells
    ''' BrainWindow not to let the camera orbit with it.
    ''' </summary>
    Public Function Mouse(m As MouseState, ByRef vp As Matrix4,
                          w As Integer, h As Integer) As Boolean
        If Not SHOW OrElse Not HasStart Then
            wasDown = m.IsButtonDown(MouseButton.Left)
            Return False
        End If

        Dim down = m.IsButtonDown(MouseButton.Left)
        Dim justPressed = down AndAlso Not wasDown
        wasDown = down

        If Not down Then
            If dragging Then
                dragging = False
                LogThis("brain: start ({0:0.0}, {1:0.0}) - standable {2}",
                        Pos.X, Pos.Y,
                        BrainNav.Standable(Pos.X, Pos.Y, BrainNav.TRACE_R))
                ' ON RELEASE, NOT DURING THE DRAG. A write per frame would be
                ' a file rewritten sixty times a second for a position that is
                ' still moving, and the one that matters is where it stopped.
                Save()
            End If
            If turning Then
                turning = False
                LogThis("brain: start heading {0:0} deg",
                        MathHelper.RadiansToDegrees(HeadingRad))
                Save()
            End If
            Return False
        End If

        Dim mx = CInt(m.X), my = CInt(m.Y)
        Dim rgb As Vector3, world As Vector3

        If Not dragging AndAlso Not turning Then
            ' Only on the frame the button goes DOWN. A press that began
            ' elsewhere is an orbit already in progress and is not ours to take.
            If Not justPressed Then Return False
            ' WHAT COLOUR DID WE GRAB. The square is drawn into the pass for
            ' this question; the terrain is in there depth-only so a marker
            ' behind a hill is occluded and cannot be grabbed through it.
            BrainRender.PickAt(mx, my, w, h, True, rgb, world)
            If is_marker(rgb) Then
                dragging = True
                Return True
            End If

            ' ---- THE RING, BY ARITHMETIC RATHER THAN BY COLOUR -----------
            '
            ' No third pick colour and no ring geometry in the pick pass. The
            ' ring is an annulus in world XZ, and the cursor's ground point is
            ' already available without touching the GPU - so "is the press on
            ' the ring" is two distances and a comparison.
            '
            ' That also keeps the rule the drag was rewritten for: pressing a
            ' button must not cost a readback. One pick for the square, which
            ' is genuinely a question about pixels, and nothing for this.
            Dim gp As Vector2
            If Not BrainPick.GroundRay(vp, m.X, m.Y, w, h, gp) Then Return False
            Dim r = (gp - Pos).Length
            If r < RING_IN OrElse r > RING_OUT Then Return False
            turning = True
            PointTo(CSng(Math.Atan2(gp.X - Pos.X, gp.Y - Pos.Y)))
            Return True
        End If

        ' NO GPU WORK ON THE DRAG PATH AT ALL. Just where the cursor's ray
        ' meets the ground - the marker is a location attached to the pointer,
        ' and holding a button must not change how the pointer behaves.
        Dim g As Vector2
        If Not BrainPick.GroundRay(vp, m.X, m.Y, w, h, g) Then Return True
        If turning Then
            ' THE BEARING FROM THE START TO THE CURSOR, at whatever range.
            ' Not the point on the ring nearest the cursor - the angle is the
            ' whole content of this control, so letting the hand wander off
            ' the band mid-swing costs nothing and fighting to stay on it
            ' would be the control fighting the person using it.
            PointTo(CSng(Math.Atan2(g.X - Pos.X, g.Y - Pos.Y)))
        Else
            MoveTo(g)
        End If
        Return True
    End Function

    ''' <summary>Is that our ID? A byte match either way - the shader is flat,
    ''' blending is off and nothing else draws into the pass, so the only
    ''' slack needed is for the 1/255 quantisation.</summary>
    Private Function is_marker(c As Vector3) As Boolean
        Return c.X > 0.9F AndAlso c.Y < 0.1F AndAlso c.Z > 0.9F
    End Function

    ''' <summary>
    ''' THE DRAPED SQUARE IN ITS PICK COLOUR, for the pick pass only.
    '''
    ''' The SAME geometry the eye sees, so the grab area cannot disagree with
    ''' the picture - which a separate flat quad would, the moment either one
    ''' sat on a slope.
    '''
    ''' Drawn whether or not it is being held: this is what ANSWERS "is the
    ''' cursor on it", and it is asked when nothing is held yet.
    ''' </summary>
    Public Sub DrawPick(ByRef viewProj As Matrix4)
        If Not SHOW OrElse Not HasStart OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        build()
        If fillVerts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", PICK_RGB)
        ' BOTH FACES. A draped sheet seen from below is still the thing being
        ' grabbed, and the camera can go under the ground.
        GL.Disable(EnableCap.CullFace)
        GL.BindVertexArray(fillVao)
        GL.DrawArrays(PrimitiveType.Triangles, 0, fillVerts)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' Border always, fill only while held.
    '''
    ''' The fill IS the lit state. A marker that is always filled needs a
    ''' second colour to say "held" and then two things to keep in step; this
    ''' way the feedback is the presence of the fill and there is nothing to
    ''' get out of step with.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW OrElse Not HasStart OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        build()
        If edgeVerts = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)

        ' TESTED AGAINST DEPTH, NEVER WRITING IT. Terrain still hides it, but
        ' it leaves no mark of its own - which is what lets the drag read the
        ' GROUND out of the frame already drawn instead of paying for a pass.
        ' Written depth here would put the marker under the cursor by
        ' definition and it would climb its own surface.
        GL.DepthMask(False)

        If dragging AndAlso fillVerts > 0 Then
            shader.SetVec3("colour", FILL_RGB)
            GL.Disable(EnableCap.CullFace)
            GL.BindVertexArray(fillVao)
            GL.DrawArrays(PrimitiveType.Triangles, 0, fillVerts)
        End If

        shader.SetVec3("colour", EDGE_RGB)
        GL.LineWidth(If(dragging, 3.0F, 2.0F))
        GL.BindVertexArray(edgeVao)
        GL.DrawArrays(PrimitiveType.Lines, 0, edgeVerts)

        ' The ring last, in its own colour, fatter while it is being swung -
        ' the same "the feedback is the drawing" rule the square's fill uses,
        ' so there is no second highlight state to keep in step.
        If ringVerts > 0 Then
            shader.SetVec3("colour", RING_RGB)
            GL.LineWidth(If(turning, 4.0F, 2.0F))
            GL.BindVertexArray(ringVao)
            GL.DrawArrays(PrimitiveType.Lines, 0, ringVerts)
        End If
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
        GL.DepthMask(True)
    End Sub

    ''' <summary>
    ''' Drape the square over the ground: an N by N grid in XZ, every vertex
    ''' sampling BrainNav.Ground.
    '''
    ''' REBUILT ON A MOVE ALONE. The ground under a fixed marker does not
    ''' change, and while it is being dragged Pos changes every frame anyway -
    ''' so there is nothing a height check would catch that this does not.
    ''' </summary>
    Private Sub build()
        If Pos = built_at AndAlso HeadingRad = built_head AndAlso
           fillVerts > 0 Then Return
        built_at = Pos
        built_head = HeadingRad

        Dim step_ = (BASE * 2.0F) / N
        Dim gy(N, N) As Single
        For j = 0 To N
            For i = 0 To N
                gy(i, j) = BrainNav.Ground(Pos.X - BASE + i * step_,
                                           Pos.Y - BASE + j * step_) + LIFT
            Next
        Next

        Dim f As New List(Of Single)
        For j = 0 To N - 1
            For i = 0 To N - 1
                Dim x0 = Pos.X - BASE + i * step_, x1 = x0 + step_
                Dim z0 = Pos.Y - BASE + j * step_, z1 = z0 + step_
                tri(f, x0, gy(i, j), z0, x1, gy(i + 1, j), z0, x1, gy(i + 1, j + 1), z1)
                tri(f, x0, gy(i, j), z0, x1, gy(i + 1, j + 1), z1, x0, gy(i, j + 1), z1)
            Next
        Next

        ' The perimeter, walked along the same samples so it sits on the fill
        ' rather than cutting through it.
        Dim e As New List(Of Single)
        For i = 0 To N - 1
            Dim xa = Pos.X - BASE + i * step_, xb = xa + step_
            Dim za = Pos.Y - BASE + i * step_, zb = za + step_
            seg(e, xa, gy(i, 0), Pos.Y - BASE, xb, gy(i + 1, 0), Pos.Y - BASE)
            seg(e, xa, gy(i, N), Pos.Y + BASE, xb, gy(i + 1, N), Pos.Y + BASE)
            seg(e, Pos.X - BASE, gy(0, i), za, Pos.X - BASE, gy(0, i + 1), zb)
            seg(e, Pos.X + BASE, gy(N, i), za, Pos.X + BASE, gy(N, i + 1), zb)
        Next

        ' ---- THE HEADING RING, AND THE ARROW THAT READS IT -------------
        '
        ' The band is what you grab; the circle drawn down its middle is what
        ' says where it is. Draped at every segment like everything else here,
        ' because a flat circle on a slope is a circle that is half underground.
        ' TWO SAVINGS, BOTH ABOUT THE DRAG PATH, and the owner had to say it
        ' twice: "mouse still moving way fast with start selected. we fixed
        ' this once." He did - the first time it was a GPU readback welded to
        ' pointer motion. This time it was the terrain: Pos changes every
        ' frame while dragging, so build() re-runs every frame, and the ring
        ' as first written asked BrainNav.Ground 96 times on top of the
        ' square's 81. Nearly tripled the cost of moving the marker.
        '
        ' 1. ONE SAMPLE PER VERTEX, not two. The old loop computed Ground at
        '    `prev` and at `p` every iteration - and `prev` IS the previous
        '    iteration's `p`, so half of every ring rebuild was recomputing a
        '    number it had just had. Carrying prevY makes it 49 from 96.
        '
        ' 2. FLAT WHILE THE SQUARE IS MOVING. A ring being dragged across the
        '    map does not need to follow every bump on the way; it needs to
        '    show where the heading points. One sample at the centre while
        '    dragging, the full drape the moment it is let go. 49 from 1.
        '
        ' Dragging is back to 82 samples a frame, which is what it was before
        ' the ring existed. The rule this keeps is the owner's own: pressing a
        ' button changes WHAT moves, never HOW the mouse moves.
        Dim g As New List(Of Single)
        Dim mid = (RING_IN + RING_OUT) * 0.5F
        Dim flat = dragging
        Dim flatY = BrainNav.Ground(Pos.X, Pos.Y) + LIFT

        Dim prev As Vector2 = Nothing
        Dim prevY As Single = 0.0F
        For s = 0 To RING_SEGS
            Dim a = CSng(s) / RING_SEGS * CSng(Math.PI * 2.0)
            Dim p = Pos + New Vector2(CSng(Math.Sin(a)), CSng(Math.Cos(a))) * mid
            Dim py = drape(p.X, p.Y, flat, flatY)
            If s > 0 Then seg(g, prev.X, prevY, prev.Y, p.X, py, p.Y)
            prev = p
            prevY = py
        Next

        ' THE ARROW. Sin for X and Cos for Z, which is Body.headingRad's own
        ' convention - the same one BrainSim integrates with. An arrow drawn
        ' with the axes swapped points somewhere plausible and wrong, and it
        ' would be believed because it is a picture.
        Dim fwd = New Vector2(CSng(Math.Sin(HeadingRad)), CSng(Math.Cos(HeadingRad)))
        Dim side = New Vector2(fwd.Y, -fwd.X)
        Dim tip = Pos + fwd * (RING_OUT + 1.0F)
        Dim tail = Pos + fwd * BASE
        Dim tipY = drape(tip.X, tip.Y, flat, flatY)
        seg(g, tail.X, drape(tail.X, tail.Y, flat, flatY), tail.Y,
           tip.X, tipY, tip.Y)
        For Each sgn In New Single() {-1.0F, 1.0F}
            Dim barb = tip - fwd * 2.2F + side * (sgn * 1.4F)
            seg(g, tip.X, tipY, tip.Y,
               barb.X, drape(barb.X, barb.Y, flat, flatY), barb.Y)
        Next

        fillVerts = upload(fillVao, fillVbo, f)
        edgeVerts = upload(edgeVao, edgeVbo, e)
        ringVerts = upload(ringVao, ringVbo, g)
    End Sub

    Private Function upload(vaoId As Integer, vboId As Integer, v As List(Of Single)) As Integer
        Dim a = v.ToArray()
        GL.BindVertexArray(vaoId)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vboId)
        GL.BufferData(BufferTarget.ArrayBuffer, a.Length * 4, a, BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        Return a.Length \ 3
    End Function

    Private Sub tri(v As List(Of Single), ax As Single, ay As Single, az As Single,
                    bx As Single, by_ As Single, bz As Single,
                    cx As Single, cy As Single, cz As Single)
        v.AddRange({ax, ay, az, bx, by_, bz, cx, cy, cz})
    End Sub

    ''' <summary>The ground under a ring vertex, or the centre's height when
    ''' the marker is being dragged. One place decides, so the ring cannot end
    ''' up half draped and half flat.</summary>
    Private Function drape(x As Single, z As Single, flat As Boolean,
                           flatY As Single) As Single
        If flat Then Return flatY
        Return BrainNav.Ground(x, z) + LIFT
    End Function

    Private Sub seg(v As List(Of Single), x0 As Single, y0 As Single, z0 As Single,
                    x1 As Single, y1 As Single, z1 As Single)
        v.AddRange({x0, y0, z0, x1, y1, z1})
    End Sub


    ''' <summary>Where the saved start lives. Beside the snapshots in
    ''' BrainPanel.OUT_DIR, so if that folder moves again this follows it
    ''' rather than being a second place to remember. KEYED BY MAP - a start
    ''' saved on monastery means nothing on another map, and one file for all
    ''' of them would drop the tank in the sea.</summary>
    Private Function path_for(map As String) As String
        Return IO.Path.Combine(BrainPanel.OUT_DIR, map & "_start.txt")
    End Function

    ''' <summary>Remember this start for the next load.</summary>
    Public Sub Save()
        If Not HasStart OrElse String.IsNullOrEmpty(STARTUP_MAP) Then Return
        Try
            IO.Directory.CreateDirectory(BrainPanel.OUT_DIR)
            Dim p = path_for(STARTUP_MAP)
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("# Brain Testing start " & DateTime.Now.ToString("s"))
            sb.AppendLine("map=" & STARTUP_MAP)
            sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                        "start={0:0.00},{1:0.00}", Pos.X, Pos.Y))
            ' DEGREES IN THE FILE, radians in the code. The file is read by a
            ' person as often as by this loader and 137 is a heading; 2.39 is
            ' a number you have to convert before it means anything.
            sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                        "heading={0:0.0}",
                                        MathHelper.RadiansToDegrees(HeadingRad)))
            IO.File.WriteAllText(p, sb.ToString())
            LogThis("brain: start saved ({0:0.0}, {1:0.0}) -> {2}", Pos.X, Pos.Y, p)
        Catch ex As Exception
            LogThis("brain: could not save the start - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Bring back a saved start, if this map has one.
    '''
    ''' THROUGH MoveTo, not by setting Pos. The hull and the remembered origin
    ''' have to move with it or the marker draws in the saved place while the
    ''' tank spawns in the old one - which looks like the save failing when it
    ''' is the load only doing half the job.
    ''' </summary>
    Public Function LoadSaved(map As String) As Boolean
        If String.IsNullOrEmpty(map) Then Return False
        Try
            Dim p = path_for(map)
            If Not IO.File.Exists(p) Then Return False
            ' HEADING FIRST, POSITION SECOND, and the order is deliberate:
            ' the position branch returns as soon as it lands, so a heading
            ' read after it would only ever be reached on a file that had no
            ' start in it at all.
            For Each line In IO.File.ReadAllLines(p)
                Dim t = line.Trim()
                If Not t.StartsWith("heading=") Then Continue For
                Dim hd As Single
                If Single.TryParse(t.Substring(8), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, hd) Then
                    PointTo(MathHelper.DegreesToRadians(hd))
                    LogThis("brain: start heading restored {0:0} deg", hd)
                End If
            Next
            For Each line In IO.File.ReadAllLines(p)
                Dim t = line.Trim()
                If Not t.StartsWith("start=") Then Continue For
                Dim f = t.Substring(6).Split(","c)
                Dim sx, sz As Single
                If f.Length >= 2 AndAlso
                   Single.TryParse(f(0), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, sx) AndAlso
                   Single.TryParse(f(1), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, sz) Then
                    MoveTo(New Vector2(sx, sz))
                    LogThis("brain: start restored ({0:0.0}, {1:0.0}) - standable {2}",
                            sx, sz, BrainNav.Standable(sx, sz, BrainNav.TRACE_R))
                    Return True
                End If
            Next
        Catch ex As Exception
            LogThis("brain: saved start would not load - {0}", ex.Message)
        End Try
        Return False
    End Function

End Module

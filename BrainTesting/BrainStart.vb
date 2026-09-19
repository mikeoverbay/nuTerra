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
    Private ReadOnly EDGE_RGB As New Vector3(1.0F, 1.0F, 1.0F)
    Private ReadOnly FILL_RGB As New Vector3(0.13F, 0.13F, 0.14F)

    ''' <summary>The marker's ID in the pick pass. Pure and saturated so the
    ''' readback is an exact byte match - the line shader is flat and unlit and
    ''' blending is off for the pass, so 255,0,255 arrives as 255,0,255.</summary>
    Private ReadOnly PICK_RGB As New Vector3(1.0F, 0.0F, 1.0F)

    ''' <summary>Half-width of the pick square, metres. The SQUARE is what is
    ''' grabbable - the cross is decoration - so this is the hit area and it is
    ''' stated in metres on the ground rather than pixels on the screen.</summary>
    Private Const BASE As Single = 6.0F

    Private shader As BrainShader
    ''' <summary>The draped fill, triangles. Drawn dark grey when held and in
    ''' PICK_RGB for the pick pass - ONE geometry, so what you grab is exactly
    ''' what you see, on a slope as much as on the flat.</summary>
    Private fillVao, fillVbo, fillVerts As Integer
    ''' <summary>The perimeter, line segments, draped the same way.</summary>
    Private edgeVao, edgeVbo, edgeVerts As Integer
    Private built_at As New Vector2(Single.MaxValue, Single.MaxValue)

    Private dragging As Boolean = False
    Private wasDown As Boolean = False


    Public Sub Init()
        shader = New BrainShader("line")
        fillVao = GL.GenVertexArray()
        fillVbo = GL.GenBuffer()
        edgeVao = GL.GenVertexArray()
        edgeVbo = GL.GenBuffer()
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
            Return False
        End If

        Dim mx = CInt(m.X), my = CInt(m.Y)
        Dim rgb As Vector3, world As Vector3

        If Not dragging Then
            ' Only on the frame the button goes DOWN. A press that began
            ' elsewhere is an orbit already in progress and is not ours to take.
            If Not justPressed Then Return False
            ' WHAT COLOUR DID WE GRAB. The square is drawn into the pass for
            ' this question; the terrain is in there depth-only so a marker
            ' behind a hill is occluded and cannot be grabbed through it.
            BrainRender.PickAt(mx, my, w, h, True, rgb, world)
            If Not is_marker(rgb) Then Return False
            dragging = True
            Return True
        End If

        ' NO GPU WORK ON THE DRAG PATH AT ALL. Just where the cursor's ray
        ' meets the ground - the marker is a location attached to the pointer,
        ' and holding a button must not change how the pointer behaves.
        Dim g As Vector2
        If BrainPick.GroundRay(vp, m.X, m.Y, w, h, g) Then MoveTo(g)
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
        If Pos = built_at AndAlso fillVerts > 0 Then Return
        built_at = Pos

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

        fillVerts = upload(fillVao, fillVbo, f)
        edgeVerts = upload(edgeVao, edgeVbo, e)
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

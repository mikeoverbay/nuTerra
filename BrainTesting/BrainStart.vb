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


    ''' <summary>Arms of the cross, metres. Larger than the goal's 6 so the
    ''' two are not mistaken for each other at a distance.</summary>
    Private Const ARM As Single = 8.0F

    Private ReadOnly RGB As New Vector3(0.25F, 0.95F, 0.35F)

    ''' <summary>The marker's ID in the pick pass. Pure and saturated so the
    ''' readback is an exact byte match - the line shader is flat and unlit and
    ''' blending is off for the pass, so 255,0,255 arrives as 255,0,255.</summary>
    Private ReadOnly PICK_RGB As New Vector3(1.0F, 0.0F, 1.0F)

    ''' <summary>Half-width of the pick square, metres. The SQUARE is what is
    ''' grabbable - the cross is decoration - so this is the hit area and it is
    ''' stated in metres on the ground rather than pixels on the screen.</summary>
    Private Const BASE As Single = 6.0F

    Private pickVao, pickVbo As Integer

    Private shader As BrainShader
    Private vao, vbo As Integer
    Private verts As Integer = 0
    Private built_at As New Vector2(Single.MaxValue, Single.MaxValue)
    Private built_y As Single = Single.MaxValue

    Private dragging As Boolean = False
    Private wasDown As Boolean = False

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        pickVao = GL.GenVertexArray()
        pickVbo = GL.GenBuffer()
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

        ' Dragging: ask where the GROUND is, with the square left out.
        If BrainRender.PickAt(mx, my, w, h, False, rgb, world) Then
            MoveTo(New Vector2(world.X, world.Z))
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
    ''' THE SOLID SQUARE, FOR THE PICK PASS ONLY. Two triangles, flat in
    ''' PICK_RGB, depth-tested against the terrain already in the buffer - so a
    ''' marker behind a hill cannot be grabbed through the hill.
    '''
    ''' Lifted slightly off the ground for the same reason the visible marker
    ''' is: coplanar with the terrain it z-fights, and half the square fails the
    ''' depth test in a speckle pattern that reads as an intermittent grab.
    ''' </summary>
    Public Sub DrawPick(ByRef viewProj As Matrix4)
        If Not SHOW OrElse Not HasStart OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        Dim y = BrainNav.Ground(Pos.X, Pos.Y) + 0.30F
        Dim x0 = Pos.X - BASE, x1 = Pos.X + BASE
        Dim z0 = Pos.Y - BASE, z1 = Pos.Y + BASE
        Dim a() As Single = {
            x0, y, z0, x1, y, z0, x1, y, z1,
            x0, y, z0, x1, y, z1, x0, y, z1}

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", PICK_RGB)
        GL.BindVertexArray(pickVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, pickVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, a.Length * 4, a, BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        ' BOTH FACES. The square is a flat quad and the camera can be under the
        ' ground looking up; culling it would make the grab vanish from below.
        GL.Disable(EnableCap.CullFace)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6)
        GL.BindVertexArray(0)
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW OrElse Not HasStart OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        build()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", RGB)
        GL.LineWidth(If(dragging, 5.0F, 3.0F))
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
    End Sub

    ''' <summary>Rebuilt only when it has actually moved - including when the
    ''' GROUND under it changed, which is what dragging across a slope does
    ''' without changing XZ enough to notice.</summary>
    Private Sub build()
        Dim y = BrainNav.Ground(Pos.X, Pos.Y) + 0.25F
        If Pos = built_at AndAlso Math.Abs(y - built_y) < 0.01F Then Return
        built_at = Pos
        built_y = y

        Dim v As New List(Of Single)
        seg(v, Pos.X - ARM, y, Pos.Y, Pos.X + ARM, y, Pos.Y)
        seg(v, Pos.X, y, Pos.Y - ARM, Pos.X, y, Pos.Y + ARM)
        ' A square round the cross, so it reads as a start box rather than as
        ' the goal's diamond seen from an odd angle.
        Dim d = ARM * 0.7F
        seg(v, Pos.X - d, y, Pos.Y - d, Pos.X + d, y, Pos.Y - d)
        seg(v, Pos.X + d, y, Pos.Y - d, Pos.X + d, y, Pos.Y + d)
        seg(v, Pos.X + d, y, Pos.Y + d, Pos.X - d, y, Pos.Y + d)
        seg(v, Pos.X - d, y, Pos.Y + d, Pos.X - d, y, Pos.Y - d)

        Dim a = v.ToArray()
        verts = a.Length \ 3
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, a.Length * 4, a, BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
    End Sub

    Private Sub seg(v As List(Of Single), x0 As Single, y0 As Single, z0 As Single,
                    x1 As Single, y1 As Single, z1 As Single)
        v.Add(x0) : v.Add(y0) : v.Add(z0)
        v.Add(x1) : v.Add(y1) : v.Add(z1)
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

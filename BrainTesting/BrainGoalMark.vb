Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' THE GOAL MARKER, AND YOU DRAG IT.
'''
''' "can you make the goal draggable and saved same as start. same thing just
'''  a dupe of code" - the owner, 2026-09-19. So this is BrainStart, twice
''' over, and the differences are only the ones that had to differ.
'''
''' WHY IT IS A COPY AND NOT A SHARED CLASS. The owner asked for a dupe, and
''' the two markers are not actually the same object underneath: the start
''' writes a hull spawn and a remembered origin, this writes BrainGoal.Target
''' and has a pin and a follow-the-camera mode to stay out of the way of. A
''' shared base would have to carry both sets of exceptions as flags, which is
''' how one file ends up harder to read than two.
'''
''' THREE THINGS ARE GENUINELY DIFFERENT, and each one is a bug if copied:
'''
'''  1. IT DOES NOT GO THROUGH BrainGoal.TryMove. TryMove refuses while
'''     Pinned, and `pingoal` is exactly the flag a scored run sets - so a
'''     dragged goal would silently not move on every run worth measuring.
'''     PlaceAtLookAt and SetFromArg both write Target directly for the same
'''     reason, and say so: the pin stops a BRAIN throwing a fresh goal on
'''     arrival, not the run being set up.
'''
'''  2. IT CLEARS Follow. BrainGoal.FollowCamera drags the goal onto the
'''     camera every frame while Follow is set, so without this the marker is
'''     pulled back the instant the button is released - and with the chase
'''     cam on, the camera is the tank.
'''
'''  3. THE PICK COLOUR IS YELLOW, not the start's magenta. One colour for two
'''     squares means grabbing the goal drags the start, which reads as the
'''     marker teleporting.
'''
''' Everything else - the drape, the depth-mask, the colour pick on button-
''' down only, the arithmetic drag, the save on release - is BrainStart's and
''' its comments there explain why.
''' </summary>
Module BrainGoalMark

    Public SHOW As Boolean = True

    ''' <summary>Where the goal marker sits, world XZ. A MIRROR of
    ''' BrainGoal.Target, resynced every frame - the goal can also move by the
    ''' command line, by alt, or by a snapshot restore, and a marker that only
    ''' knew about dragging would sit in the wrong place after any of them.</summary>
    Public Pos As Vector2
    Public HasGoal As Boolean = False

    Private Const N As Integer = 8
    Private Const LIFT As Single = 0.15F

    ''' <summary>Amber, which is what BrainGoal.Draw already paints the goal
    ''' ring - so the square reads as part of the goal rather than as a second
    ''' unrelated thing sitting on it.</summary>
    Public ReadOnly EDGE_RGB As New Vector3(1.0F, 0.75F, 0.2F)
    Private ReadOnly FILL_RGB As New Vector3(0.13F, 0.13F, 0.14F)

    ''' <summary>Pure yellow, and it must not be the start's magenta - see the
    ''' header. Saturated so the readback is an exact byte match.</summary>
    Private ReadOnly PICK_RGB As New Vector3(1.0F, 1.0F, 0.0F)

    Private Const BASE As Single = 6.0F

    Private shader As BrainShader
    Private fillVao, fillVbo, fillVerts As Integer
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

    ''' <summary>Adopt whatever the goal is now, unless we are the ones moving
    ''' it. Cheap, and it is what keeps the square on a goal placed by alt, by
    ''' goal= on the command line, or by a restored snapshot.</summary>
    Private Sub sync()
        If dragging Then Return
        If Not BrainGoal.HasTarget Then
            HasGoal = False
            Return
        End If
        HasGoal = True
        Pos = BrainGoal.Target
    End Sub

    ''' <summary>
    ''' Move the goal, and the thing it is a marker FOR.
    '''
    ''' DIRECTLY, not through TryMove, and Follow off - see the two numbered
    ''' reasons in the header.
    '''
    ''' NOTHING POKES BrainGoal's built_at here, although PlaceAtLookAt and
    ''' SetFromArg both do. Its Draw already tests `built_at &lt;&gt; Target` and
    ''' rebuilds, so writing Target is enough - and a second file reaching in
    ''' to reset another module's cache flag is a thing to add only when the
    ''' module cannot notice for itself.
    ''' </summary>
    Public Sub MoveTo(p As Vector2)
        Pos = p
        HasGoal = True
        BrainGoal.Follow = False
        BrainGoal.Target = p
        BrainGoal.HasTarget = True
    End Sub

    ''' <summary>
    ''' One frame of mouse. True when this owns the pointer, which tells
    ''' BrainWindow not to let the camera orbit with it.
    ''' </summary>
    Public Function Mouse(m As MouseState, ByRef vp As Matrix4,
                          w As Integer, h As Integer) As Boolean
        sync()
        If Not SHOW OrElse Not HasGoal Then
            wasDown = m.IsButtonDown(MouseButton.Left)
            Return False
        End If

        Dim down = m.IsButtonDown(MouseButton.Left)
        Dim justPressed = down AndAlso Not wasDown
        wasDown = down

        If Not down Then
            If dragging Then
                dragging = False
                LogThis("brain: goal ({0:0.0}, {1:0.0}) - standable {2}",
                        Pos.X, Pos.Y,
                        BrainNav.Standable(Pos.X, Pos.Y, BrainNav.TRACE_R))
                ' On release, not during the drag - BrainStart.Save says why.
                Save()
            End If
            Return False
        End If

        Dim mx = CInt(m.X), my = CInt(m.Y)
        Dim rgb As Vector3, world As Vector3

        If Not dragging Then
            If Not justPressed Then Return False
            ' ---- GRABBED BY ARITHMETIC, NOT BY A READBACK ----------------
            '
            ' This used to do its own BrainRender.PickAt, which meant TWO
            ' ReadPixels on every press: the start marker asks first, and when
            ' it declines this one asked again. ReadPixels stalls the CPU
            ' until the GPU drains, so the frame it happens on is long, the
            ' cursor travels a long way during it, and the next frame's Delta
            ' is enormous. That is what the camera was being handed.
            '
            ' The owner settled this rule when the start marker had the same
            ' fault: "mouse down should have nothing to do with how the mouse
            ' moves. we simply have a location attached to it."
            '
            ' The square is BASE either side of Pos in world XZ, so "is the
            ' press on it" is two comparisons against the cursor's ground
            ' point - which BrainPick.GroundRay gives without touching the GPU.
            '
            ' WHAT THIS GIVES UP, honestly: the colour pick was occluded by
            ' terrain, so a marker behind a hill could not be grabbed through
            ' it. This can be. For a goal square that is a fair trade against
            ' a camera that flies; the start marker keeps its colour pick and
            ' its occlusion, and one readback a press is affordable.
            Dim gp As Vector2
            If Not BrainPick.GroundRay(vp, m.X, m.Y, w, h, gp) Then Return False
            If Math.Abs(gp.X - Pos.X) > BASE OrElse Math.Abs(gp.Y - Pos.Y) > BASE Then
                Return False
            End If
            dragging = True
            Return True
        End If

        Dim g As Vector2
        If BrainPick.GroundRay(vp, m.X, m.Y, w, h, g) Then MoveTo(g)
        Return True
    End Function

    Public Sub DrawPick(ByRef viewProj As Matrix4)
        sync()
        If Not SHOW OrElse Not HasGoal OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        build()
        If fillVerts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", PICK_RGB)
        GL.Disable(EnableCap.CullFace)
        GL.BindVertexArray(fillVao)
        GL.DrawArrays(PrimitiveType.Triangles, 0, fillVerts)
        GL.BindVertexArray(0)
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        sync()
        If Not SHOW OrElse Not HasGoal OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        build()
        If edgeVerts = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
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

    ''' <summary>Beside the saved start, and keyed by map for the same reason:
    ''' a goal saved on monastery means nothing on another map.</summary>
    Private Function path_for(map As String) As String
        Return IO.Path.Combine(BrainPanel.OUT_DIR, map & "_goal.txt")
    End Function

    Public Sub Save()
        If Not HasGoal OrElse String.IsNullOrEmpty(STARTUP_MAP) Then Return
        Try
            IO.Directory.CreateDirectory(BrainPanel.OUT_DIR)
            Dim p = path_for(STARTUP_MAP)
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("# Brain Testing goal " & DateTime.Now.ToString("s"))
            sb.AppendLine("map=" & STARTUP_MAP)
            sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                        "goal={0:0.00},{1:0.00}", Pos.X, Pos.Y))
            IO.File.WriteAllText(p, sb.ToString())
            LogThis("brain: goal saved ({0:0.0}, {1:0.0}) -> {2}", Pos.X, Pos.Y, p)
        Catch ex As Exception
            LogThis("brain: could not save the goal - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Bring back a saved goal, if this map has one.
    '''
    ''' THE SAVED GOAL WINS, the same way a saved start beats the roster's
    ''' spawn - "same as start" was the ask and this is the half of it that
    ''' makes dragging worth anything.
    '''
    ''' BUT IT SAYS SO WHEN IT OVERRIDES goal=. A run launched with
    ''' goal=107.4,-5.8 names a scenario, and a file left over from an
    ''' afternoon of dragging silently retargeting it is the same shape of bug
    ''' as block bits surviving a reset: the run still completes, the number
    ''' still looks like a number, and it is answering a different question.
    ''' Delete the file, or drag it back, and the argument is honoured again.
    ''' </summary>
    Public Function LoadSaved(map As String) As Boolean
        If String.IsNullOrEmpty(map) Then Return False
        Try
            Dim p = path_for(map)
            If Not IO.File.Exists(p) Then Return False
            For Each line In IO.File.ReadAllLines(p)
                Dim t = line.Trim()
                If Not t.StartsWith("goal=") Then Continue For
                Dim f = t.Substring(5).Split(","c)
                Dim gx, gz As Single
                If f.Length >= 2 AndAlso
                   Single.TryParse(f(0), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, gx) AndAlso
                   Single.TryParse(f(1), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, gz) Then
                    Dim had = GOAL_ARG
                    MoveTo(New Vector2(gx, gz))
                    LogThis("brain: goal restored ({0:0.0}, {1:0.0}) - standable {2}",
                            gx, gz, BrainNav.Standable(gx, gz, BrainNav.TRACE_R))
                    If had <> "" Then
                        LogThis("brain: THE SAVED GOAL OVERRODE goal={0} ON THE COMMAND " &
                                "LINE. Delete {1} to have the argument honoured.", had, p)
                    End If
                    Return True
                End If
            Next
        Catch ex As Exception
            LogThis("brain: saved goal would not load - {0}", ex.Message)
        End Try
        Return False
    End Function

End Module

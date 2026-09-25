Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' THE COURSE - obstacles you PLACE, so a test is set up rather than gone and
''' found.
'''
''' The owner, 2026-09-25: "I want a way to test what we are hitting without
''' having to go drive and get stuck on purpose... I want to start with stuff
''' and go and be able to change the course as we want to test."
'''
''' WHY THIS EXISTS AT ALL. Every obstacle chased this week turned out to be
''' about the BAKE, not the brain - field junk with a 2.6 m visual mesh and a
''' 0.18 m collision hull, leaf litter, tents. Real ones are worth fixing and
''' they are nuTerra's, but while they are in play no two runs ask the same
''' question, and the answer moves under you between bakes. A course you wrote
''' asks one question and asks it identically every time.
'''
''' AND YOU DO NOT HAVE TO DRIVE INTO ANYTHING. Put a box where you want the
''' problem, drag the start marker to where you want to meet it, and read the
''' radar. No run, no getting stuck on purpose, no waiting.
'''
''' IN MEMORY ONLY, and the file it writes into says why in its own words:
''' "do not over write the .blk on disk. you do not create it. hands off".
''' BrainNav.MarkBlocked sets bit 0 in the loaded grid and nothing else, so
''' the bake is untouched and the course dies with the process.
'''
''' F5 RELOADS IT. reload_data re-reads the .blk, which wipes every stamped
''' cell - so the course is re-stamped right after, and editing the text file
''' then pressing F5 is the whole edit loop. No rebuild, no restart.
'''
''' THE FILE IS THE SCENARIO. Boxes, and optionally the start and the goal, so
''' one file describes a whole test and two files are two tests. It is written
''' with a default the first time so there is something to edit rather than a
''' format to guess at.
''' </summary>
Module BrainCourse

    Public ON_ As Boolean = False

    ''' <summary>One obstacle. Axis-aligned before `deg` turns it, which is
    ''' enough shapes to build anything worth testing out of - a wall, a
    ''' funnel, a doorway, a dead end.</summary>
    Public Structure Box
        Public cx, cz As Single
        Public w, t As Single        ' across and through, metres
        Public tall As Single
        Public deg As Single
    End Structure

    Private ReadOnly boxes As New List(Of Box)
    Private shader As BrainShader
    Private vao, vbo, verts As Integer
    Private loaded As Boolean = False

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    Private Function path_for(map As String) As String
        Return IO.Path.Combine(BrainPanel.OUT_DIR, map & "_course.txt")
    End Function

    ''' <summary>
    ''' Read the course, write a default if there is none, stamp it, and place
    ''' the start and goal if the file names them.
    ''' </summary>
    Public Sub Load(map As String)
        If Not ON_ OrElse Not BrainNav.Ready Then Return
        boxes.Clear()
        verts = 0

        Dim p = path_for(map)
        Try
            IO.Directory.CreateDirectory(BrainPanel.OUT_DIR)
            If Not IO.File.Exists(p) Then write_default(p)

            Dim sp As Vector2, gp As Vector2
            Dim haveS = False, haveG = False, head = CSng(Math.PI)
            For Each line In IO.File.ReadAllLines(p)
                Dim s = line.Trim()
                If s = "" OrElse s.StartsWith("#") Then Continue For
                Dim sp2 = s.IndexOf(" "c)
                If sp2 < 0 Then Continue For
                Dim word = s.Substring(0, sp2).ToLowerInvariant()
                Dim f = s.Substring(sp2 + 1).Split(","c)
                Dim v(5) As Single
                For i = 0 To Math.Min(5, f.Length - 1)
                    Single.TryParse(f(i).Trim(), Globalization.NumberStyles.Float,
                                    Globalization.CultureInfo.InvariantCulture, v(i))
                Next
                Select Case word
                    Case "box"
                        If f.Length < 4 Then Continue For
                        Dim b As Box
                        b.cx = v(0) : b.cz = v(1) : b.w = v(2) : b.t = v(3)
                        b.tall = If(f.Length >= 5, v(4), 3.0F)
                        b.deg = If(f.Length >= 6, v(5), 0.0F)
                        boxes.Add(b)
                    Case "start"
                        If f.Length < 2 Then Continue For
                        sp = New Vector2(v(0), v(1))
                        haveS = True
                        If f.Length >= 3 Then head = MathHelper.DegreesToRadians(v(2))
                    Case "goal"
                        If f.Length < 2 Then Continue For
                        gp = New Vector2(v(0), v(1))
                        haveG = True
                End Select
            Next

            Dim cells = stamp()

            ' THE FILE WINS OVER THE SAVED MARKERS, and only when it says so.
            ' A course that names a start is a whole scenario and must not be
            ' half-overridden by a marker file left from another test; one
            ' that does not is happy to be aimed by hand with the markers.
            If haveS Then
                BrainStart.MoveTo(sp)
                BrainStart.PointTo(head)
            End If
            If haveG Then
                BrainGoal.Follow = False
                BrainGoal.Target = gp
                BrainGoal.HasTarget = True
            End If

            loaded = True
            LogThis("brain: course - {0} box(es), {1:N0} cells marked IN MEMORY{2}{3} -> {4}",
                    boxes.Count, cells,
                    If(haveS, String.Format(", start ({0:0.0}, {1:0.0}) at {2:0} deg",
                                            sp.X, sp.Y, MathHelper.RadiansToDegrees(head)), ""),
                    If(haveG, String.Format(", goal ({0:0.0}, {1:0.0})", gp.X, gp.Y), ""),
                    p)
        Catch ex As Exception
            LogThis("brain: course would not load - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Every box into the grid, stepped at half a cell.
    '''
    ''' HALF A CELL, not a whole one: stepping at the cell size can skip a
    ''' cell whose centre falls between two samples, and a wall with a hole in
    ''' it is a test that lies about what it is testing.
    ''' </summary>
    Private Function stamp() As Integer
        Dim n = 0
        Dim step_ = BrainNav.CellSize * 0.5F
        For Each b In boxes
            Dim c = CSng(Math.Cos(MathHelper.DegreesToRadians(b.deg)))
            Dim s = CSng(Math.Sin(MathHelper.DegreesToRadians(b.deg)))
            Dim hw = b.w * 0.5F, ht = b.t * 0.5F
            Dim u = -hw
            While u <= hw
                Dim v = -ht
                While v <= ht
                    Dim wx = b.cx + u * c - v * s
                    Dim wz = b.cz + u * s + v * c
                    If BrainNav.StampBlocked(BrainNav.ColOf(wx),
                                            BrainNav.RowOf(wz)) Then n += 1
                    v += step_
                End While
                u += step_
            End While
        Next
        Return n
    End Function

    ''' <summary>
    ''' FIVE PIECES, SIXTY METRES APART.
    '''
    ''' "a few maze traps and corners" - the owner, 2026-09-25. Each is a
    ''' shape the brain has actually failed on, built small enough to see
    ''' whole and spaced so no two are in the radar at once: the sweep reaches
    ''' 40 m, so 60 m between pieces means what you are looking at is the
    ''' piece you meant to look at.
    '''
    ''' Laid along X at one Z so the start marker slides between them, and
    ''' every one is approached from the north.
    ''' </summary>
    Private Sub write_default(p As String)
        Dim c = BrainTanks.Base2
        Dim x0 = c.X, z0 = c.Y
        Dim ci = Globalization.CultureInfo.InvariantCulture
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("# Brain Testing course. Edit, then press F5 in the app.")
        sb.AppendLine("#")
        sb.AppendLine("#   box   x, z, width, thick, tall, deg")
        sb.AppendLine("#   start x, z, heading_deg     optional - else drag the marker")
        sb.AppendLine("#   goal  x, z                  optional")
        sb.AppendLine("#")
        sb.AppendLine("# Heading 0 faces +Z, 180 faces -Z. width runs across a box")
        sb.AppendLine("# and thick through it, before deg turns the pair.")
        sb.AppendLine("#")
        sb.AppendLine("# THE RADAR REACHES 40 m. A start further out than that sees")
        sb.AppendLine("# nothing at all - not a distant wall, no wall.")
        sb.AppendLine("#")
        sb.AppendLine("# Five pieces, 60 m apart so only one is ever in the sweep.")
        sb.AppendLine("# Drag the start marker along the row to move between them.")
        sb.AppendLine()
        sb.AppendLine("# --- 1. PLAIN WALL. Go round it. The easy one.")
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 15, 1, 3, 0", x0, z0))
        sb.AppendLine()
        sb.AppendLine("# --- 2. THE V TRAP, 60 m west. Opens north, closes south.")
        sb.AppendLine("# Drive in and both walls converge - the shape that ate whole")
        sb.AppendLine("# runs before the far-white scoring went in.")
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 22, 1, 3, -60", x0 - 67.0F, z0 + 2.0F))
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 22, 1, 3, 60", x0 - 53.0F, z0 + 2.0F))
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 14, 1, 3, 0", x0 - 60.0F, z0 - 12.0F))
        sb.AppendLine()
        sb.AppendLine("# --- 3. THE CORNER, 60 m east. An L: along, then across.")
        sb.AppendLine("# Rounding one arm puts the nose straight at the other.")
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 24, 1, 3, 0", x0 + 60.0F, z0))
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 24, 1, 3, 90", x0 + 72.0F, z0 - 12.0F))
        sb.AppendLine()
        sb.AppendLine("# --- 4. THE DOORWAY, 120 m east. A 4 m gap for a 3.3 m hull.")
        sb.AppendLine("# Wide enough to fit, narrow enough that aiming matters.")
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 10, 1, 3, 0", x0 + 113.0F, z0))
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 10, 1, 3, 0", x0 + 127.0F, z0))
        sb.AppendLine()
        sb.AppendLine("# --- 5. THE FUNNEL, 120 m west. Converging walls with a gap")
        sb.AppendLine("# at the bottom. Looks open the whole way in, and is not.")
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 26, 1, 3, 30", x0 - 132.0F, z0))
        sb.AppendLine(String.Format(ci, "box   {0:0.0}, {1:0.0}, 26, 1, 3, -30", x0 - 108.0F, z0))
        sb.AppendLine()
        sb.AppendLine("# Start 25 m short of piece 1 and the goal 25 m past it -")
        sb.AppendLine("# symmetric, and both inside the radar's 40 m reach. A goal")
        sb.AppendLine("# further out just adds driving that is not the test.")
        sb.AppendLine(String.Format(ci, "start {0:0.0}, {1:0.0}, 180", x0, z0 + 25.0F))
        sb.AppendLine(String.Format(ci, "goal  {0:0.0}, {1:0.0}", x0, z0 - 25.0F))
        IO.File.WriteAllText(p, sb.ToString())
        LogThis("brain: course - no file, wrote a five piece one to {0}", p)
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not ON_ OrElse Not loaded OrElse boxes.Count = 0 Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If verts = 0 Then build_mesh()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", New Vector3(0.85F, 0.25F, 0.25F))
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Triangles, 0, verts)
        GL.BindVertexArray(0)
    End Sub

    Private Sub build_mesh()
        Dim v As New List(Of Single)
        For Each b In boxes
            Dim c = CSng(Math.Cos(MathHelper.DegreesToRadians(b.deg)))
            Dim s = CSng(Math.Sin(MathHelper.DegreesToRadians(b.deg)))
            Dim hw = b.w * 0.5F, ht = b.t * 0.5F
            Dim y0 = BrainNav.Ground(b.cx, b.cz)
            Dim y1 = y0 + b.tall
            ' The four footprint corners, turned.
            Dim px(3) As Single, pz(3) As Single
            Dim cu() As Single = {-hw, hw, hw, -hw}
            Dim cv() As Single = {-ht, -ht, ht, ht}
            For i = 0 To 3
                px(i) = b.cx + cu(i) * c - cv(i) * s
                pz(i) = b.cz + cu(i) * s + cv(i) * c
            Next
            ' Top.
            v.AddRange({px(0), y1, pz(0), px(1), y1, pz(1), px(2), y1, pz(2)})
            v.AddRange({px(0), y1, pz(0), px(2), y1, pz(2), px(3), y1, pz(3)})
            ' The four sides.
            For i = 0 To 3
                Dim j = (i + 1) Mod 4
                v.AddRange({px(i), y0, pz(i), px(i), y1, pz(i), px(j), y1, pz(j)})
                v.AddRange({px(i), y0, pz(i), px(j), y1, pz(j), px(j), y0, pz(j)})
            Next
        Next
        Dim a = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, a.Length * 4, a, BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        verts = a.Length \ 3
    End Sub

End Module

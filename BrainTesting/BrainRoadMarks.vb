Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' A POST AT EACH END OF EVERY ROAD, LABELLED WITH THE ROAD'S ID.
'''
''' "can I have markers at the start and end of the roads with their IDs
''' please?" then "so we we can ID them by road and not point" - the owner,
''' 2026-09-25.
'''
''' BY ROAD, AND THAT IS THE WHOLE REASON THIS IS WORTH DRAWING. A road id
''' comes out of Tank Path Studio's &lt;map&gt;_paths.json and is stable: road 17
''' is the same road tomorrow, in the json, and in the Studio, so it is a name
''' two people can use. A waypoint index is not - deduplicating the Chaikin
''' output moved road 17 from 224 points to 194 without changing the road at
''' all. So every label here names the road and never a point.
'''
''' THE ROAD'S OWN END, NOT WHERE THE TANK STOPS. build_lane appends the enemy
''' base to every road, so all five lanes on a team finish at the same point
''' and a marker on the last waypoint would put five labels on top of each
''' other at the base. Lane.tip is the road's own far end, captured before the
''' append, and those are 226 to 386 m apart along the base line.
'''
''' ONE TEAM'S ROADS. The fan gives both sides very nearly the same five
''' corridors driven in opposite directions - on 19_monastery team 1 drew 16,
''' 28, 8, 4, 17 and team 2 drew 16, 28, 24, 4, 17, and 8 and 24 are the same
''' corridor at x = 66.5. Drawing both would stack two posts and two labels on
''' every point and call one of them "start" and the other "end". So this
''' shows the team being watched, where start and end mean what the driving
''' tanks actually experience.
'''
''' O toggles it.
''' </summary>
Module BrainRoadMarks

    Public SHOW As Boolean = True

    ''' <summary>Where the hull joins the road. Orange - not green (the road),
    ''' not red (the course), not the trail's pink and yellow, not the walk's
    ''' blue and white.</summary>
    Private ReadOnly START_ As New Vector4(1.0F, 0.55F, 0.12F, 1.0F)
    ''' <summary>The road's far end. Magenta, so which end you are looking at
    ''' is answered by colour before the label is close enough to read.</summary>
    Private ReadOnly END_ As New Vector4(0.95F, 0.35F, 0.9F, 1.0F)

    Private Const POST As Single = 14.0F     ' metres tall - visible from a 300 m zoom
    Private Const RING As Single = 3.0F      ' ground ring radius, so it is findable from above
    Private Const ARMS As Single = 2.0F      ' the head's cross-bars
    Private Const SEGS As Integer = 20

    Private shader As BrainShader
    Private vao, vbo, verts, startVerts As Integer
    ''' <summary>What the buffer was built for. The geometry only changes when
    ''' the roads are reassigned, which is once per map load, so rebuilding it
    ''' every frame would be a BufferData a frame for nothing.</summary>
    Private builtKey As Integer = -1

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    ''' <summary>
    ''' Posts and labels. Called between BrainText.SetCamera and
    ''' BrainText.Render3D: the lines draw immediately with their own shader,
    ''' and the labels are queued into the world text buffer that Render3D
    ''' flushes a moment later.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return

        Dim marks = mine()
        If marks.Count = 0 Then Return

        Dim key = key_of(marks)
        If key <> builtKey Then
            build(marks)
            builtKey = key
        End If
        If verts = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        GL.BindVertexArray(vao)
        GL.LineWidth(2.0F)
        If startVerts > 0 Then
            shader.SetVec3("colour", New Vector3(START_.X, START_.Y, START_.Z))
            GL.DrawArrays(PrimitiveType.Lines, 0, startVerts)
        End If
        If verts > startVerts Then
            shader.SetVec3("colour", New Vector3(END_.X, END_.Y, END_.Z))
            GL.DrawArrays(PrimitiveType.Lines, startVerts, verts - startVerts)
        End If
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)

        label(marks)
    End Sub

    ''' <summary>The watched team's roads. See the class header on why not
    ''' both.</summary>
    Private Function mine() As List(Of BrainPath.Mark)
        Dim got As New List(Of BrainPath.Mark)
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return got
        Dim w = Math.Max(0, Math.Min(BrainWatch.Hull, BrainTanks.Bodies.Count - 1))
        Dim team = BrainTanks.Bodies(w).team
        For Each m In BrainPath.Marks()
            If m.team = team Then got.Add(m)
        Next
        Return got
    End Function

    ''' <summary>Cheap signature of the road set, so a reassignment rebuilds
    ''' and a camera nudge does not.</summary>
    Private Function key_of(marks As List(Of BrainPath.Mark)) As Integer
        Dim k = marks.Count
        For Each m In marks
            k = k * 31 + m.road * 7 + m.hull
        Next
        Return k
    End Function

    Private Sub build(marks As List(Of BrainPath.Mark))
        Dim v As New List(Of Single)
        ' STARTS FIRST, so one buffer draws in two colours off one VAO - the
        ' same trick BrainPath uses for the driven road against the rest.
        For Each m In marks
            marker(v, m.a)
        Next
        startVerts = v.Count \ 3
        For Each m In marks
            marker(v, m.b)
        Next

        Dim arr = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr, BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        verts = arr.Length \ 3
    End Sub

    ''' <summary>One marker: a ring on the ground, a post, and a cross at the
    ''' head. The ring is what makes it findable from the top-down view, where
    ''' a vertical post is a single pixel.</summary>
    ''' NOT "post" - VB is case-insensitive and POST is the height constant.
    Private Sub marker(v As List(Of Single), p As Vector2)
        Dim g = BrainNav.Ground(p.X, p.Y) + 0.3F

        For s = 0 To SEGS - 1
            Dim a0 = CSng(s * Math.PI * 2.0 / SEGS)
            Dim a1 = CSng((s + 1) * Math.PI * 2.0 / SEGS)
            push(v, p.X + RING * CSng(Math.Cos(a0)), g, p.Y + RING * CSng(Math.Sin(a0)))
            push(v, p.X + RING * CSng(Math.Cos(a1)), g, p.Y + RING * CSng(Math.Sin(a1)))
        Next

        push(v, p.X, g, p.Y)
        push(v, p.X, g + POST, p.Y)

        Dim h = g + POST
        push(v, p.X - ARMS, h, p.Y)
        push(v, p.X + ARMS, h, p.Y)
        push(v, p.X, h, p.Y - ARMS)
        push(v, p.X, h, p.Y + ARMS)
    End Sub

    ''' <summary>The id at the head of each post, billboarded and held at a
    ''' constant screen size by BrainText.Steady - the same formula the tank
    ''' badges use, so the two sets of labels agree in size.</summary>
    Private Sub label(marks As List(Of BrainPath.Mark))
        Dim eye = BrainRender.Cam.Eye
        Dim ang = BrainTune.Get_("roadtagm", 0.010F)
        Dim lo = BrainTune.Get_("tagmin", 0.8F)
        Dim hi = BrainTune.Get_("tagmax", 6.0F)

        For Each m In marks
            ' "road 8:Start" / "road 8:End" - the owner's own format, and the
            ' colon earns its place: "road 8 start" reads for a moment as three
            ' separate numbers-and-words, where the colon says plainly that
            ' Start is a property OF road 8. Both capitalised, because his two
            ' examples disagreed and one form beats two.
            head(m.a, String.Format("road {0}:Start", m.road), START_, eye, ang, lo, hi)
            head(m.b, String.Format("road {0}:End", m.road), END_, eye, ang, lo, hi)
        Next
    End Sub

    Private Sub head(p As Vector2, s As String, col As Vector4, eye As Vector3,
                     ang As Single, lo As Single, hi As Single)
        Dim at As New Vector3(p.X, BrainNav.Ground(p.X, p.Y) + POST + 1.5F, p.Y)
        BrainText.Text3D(s, at, BrainText.Steady(at, eye, ang, lo, hi), col)
    End Sub

    Private Sub push(v As List(Of Single), x As Single, y As Single, z As Single)
        v.Add(x) : v.Add(y) : v.Add(z)
    End Sub

End Module

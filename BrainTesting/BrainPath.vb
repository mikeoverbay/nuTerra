Imports System.Text.Json
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' FOLLOWING A ROAD THAT WAS WORKED OUT IN ADVANCE.
'''
''' "lets start the pre programed path following from Path Studio" - the
''' owner, 2026-09-25.
'''
''' Tank Path Studio writes &lt;map&gt;_paths.json beside the bake: 1,223 nodes,
''' 1,214 edges and 32 ROADS, each an ordered list of node ids running base to
''' base - 800 to 890 m, 24 to 94 nodes. A road IS the pre-programmed path.
'''
''' THE FOLLOWER IS A MOVING GOAL, AND THAT IS THE WHOLE DESIGN. It does not
''' steer, it does not throttle, it does not touch the board. It writes
''' BrainGoal.Target and advances it along the road as the hull arrives. Every
''' rung the brain already has - the corridor, the go-arounds, the backing out
''' - keeps working underneath, because from the board's point of view
''' nothing has changed except where the goal happens to be.
'''
''' That split is the point: the ROAD says where to go, the BRAIN says how to
''' get through the next forty metres. A follower that steered would be a
''' second driver arguing with the first, and this project has spent real time
''' on two rungs trading the tick between them.
'''
''' WHICH WAY ALONG IT. A road is stored base1 to base2, but the hull may be
''' anywhere and facing either way. So the join is the NEAREST node, and the
''' direction is whichever end is further from it - you set off along the road
''' rather than back to its beginning.
'''
''' WRITTEN DIRECTLY, not through BrainGoal.TryMove, for the reason
''' PlaceAtLookAt and the goal marker both give: TryMove refuses while Pinned,
''' and `pingoal` is exactly the flag a measured run sets. The pin exists to
''' stop a BRAIN throwing a fresh goal on arrival, not to stop the run being
''' driven.
''' </summary>
Module BrainPath

    Public ON_ As Boolean = False
    ''' <summary>Which road. -1 picks the one nearest the hull.</summary>
    Public WantId As Integer = -1

    Private ReadOnly pts As New List(Of Vector2)
    Private at As Integer = 0
    Private RoadId As Integer = -1
    Private ready As Boolean = False

    Private shader As BrainShader
    Private vao, vbo, verts As Integer

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    Public Sub Load(map As String)
        If Not ON_ Then Return
        pts.Clear()
        ready = False
        verts = 0
        Try
            Dim p = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "flight",
                                    map & "_paths.json")
            If Not IO.File.Exists(p) Then
                LogThis("brain: no path graph at {0}", p)
                Return
            End If

            Dim xy As New Dictionary(Of Integer, Vector2)
            Dim roads As New List(Of Tuple(Of Integer, List(Of Integer)))
            Using doc = JsonDocument.Parse(IO.File.ReadAllText(p))
                Dim root = doc.RootElement
                Dim ns As JsonElement = Nothing
                If root.TryGetProperty("nodes", ns) Then
                    For Each e In ns.EnumerateArray()
                        xy(e.GetProperty("id").GetInt32()) =
                            New Vector2(e.GetProperty("x").GetSingle(),
                                        e.GetProperty("z").GetSingle())
                    Next
                End If
                Dim rs As JsonElement = Nothing
                If root.TryGetProperty("roads", rs) Then
                    For Each e In rs.EnumerateArray()
                        Dim ids As New List(Of Integer)
                        For Each i In e.GetProperty("nodes").EnumerateArray()
                            ids.Add(i.GetInt32())
                        Next
                        roads.Add(Tuple.Create(e.GetProperty("id").GetInt32(), ids))
                    Next
                End If
            End Using

            If roads.Count = 0 Then
                LogThis("brain: path graph has no roads")
                Return
            End If

            ' ---- THE BASE, NOT THE HULL ---------------------------------
            '
            ' "we start at base and seek start first" - the owner, 2026-09-25.
            '
            ' The first version joined the road at the node NEAREST the hull,
            ' which quietly skipped however much of the road lay behind it -
            ' it began 9 nodes in and drove 585 m of an 850 m road. That is a
            ' different test every time the hull happens to sit somewhere
            ' else, and it is not what a tank does: it leaves its base, finds
            ' the start of the road, and then follows it.
            '
            ' So the anchor is the BASE, the road is ordered from whichever
            ' END is nearer it, and the first waypoint is the road's own
            ' start. Driving to that start is the brain's problem, exactly as
            ' every other waypoint is - no special case, no separate mode.
            Dim team = 1
            If BrainTanks.Bodies IsNot Nothing AndAlso
               BrainTanks.Bodies.Count > BrainRadar.HULL Then
                team = BrainTanks.Bodies(BrainRadar.HULL).team
            End If
            Dim here = If(team = 2, BrainTanks.Base2, BrainTanks.Base1)

            ' BY THE ENDS, not by any node. A road that passes close to the
            ' base halfway along is not a road that STARTS there, and picking
            ' it by nearest node would choose it and then begin in the middle.
            Dim bestR As Tuple(Of Integer, List(Of Integer)) = Nothing
            Dim bestD = Single.MaxValue
            For Each r In roads
                If WantId >= 0 AndAlso r.Item1 <> WantId Then Continue For
                If r.Item2.Count < 2 Then Continue For
                For Each id In {r.Item2(0), r.Item2(r.Item2.Count - 1)}
                    Dim q As Vector2 = Nothing
                    If Not xy.TryGetValue(id, q) Then Continue For
                    Dim d = (q - here).Length
                    If d < bestD Then
                        bestD = d
                        bestR = r
                    End If
                Next
            Next
            If bestR Is Nothing Then
                LogThis("brain: no road {0} in the graph", WantId)
                Return
            End If

            Dim all As New List(Of Vector2)
            For Each id In bestR.Item2
                Dim q As Vector2 = Nothing
                If xy.TryGetValue(id, q) Then all.Add(q)
            Next
            If all.Count < 2 Then Return

            ' THE WHOLE ROAD, ordered from the end nearer the base.
            Dim dFirst = (all(0) - here).Length
            Dim dLast = (all(all.Count - 1) - here).Length
            If dFirst <= dLast Then
                pts.AddRange(all)
            Else
                For i = all.Count - 1 To 0 Step -1
                    pts.Add(all(i))
                Next
            End If

            RoadId = bestR.Item1
            at = 0
            ready = pts.Count >= 2
            Dim nd = Math.Min(dFirst, dLast)

            ' AND PUT THE HULL ON THE BASE, facing the road's start. The run
            ' then begins with the thing he asked for - seek the start - and
            ' the distance from base to road start is itself worth watching:
            ' it is the brain finding a point with no road to help it.
            If ready Then
                BrainStart.MoveTo(here)
                Dim toFirst = pts(0) - here
                If toFirst.Length > 0.5F Then
                    BrainStart.PointTo(CSng(Math.Atan2(toFirst.X, toFirst.Y)))
                End If
            End If
            Dim len_ = 0.0F
            For i = 0 To pts.Count - 2
                len_ += (pts(i + 1) - pts(i)).Length
            Next
            LogThis("brain: road {0} from base ({1:0.0}, {2:0.0}) - {3} waypoint(s), " &
                    "{4:0} m of road, and {5:0.0} m from the base to its start",
                    RoadId, here.X, here.Y, pts.Count, len_, nd)
        Catch ex As Exception
            LogThis("brain: path graph would not load - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Advance the goal along the road. Called once a tick, before the brain.
    '''
    ''' SKIPS WHATEVER IS ALREADY BEHIND, not just the one in hand: joining a
    ''' road at speed, or being pushed down it by a go-around, can carry the
    ''' hull past several nodes at once, and advancing one per tick would aim
    ''' it backwards at a point it has already driven through.
    ''' </summary>
    Public Sub Tick()
        If Not ON_ OrElse Not ready Then Return
        If BrainTanks.Bodies Is Nothing OrElse
           BrainTanks.Bodies.Count <= BrainRadar.HULL Then Return
        Dim here = BrainTanks.Bodies(BrainRadar.HULL).spawn

        Dim reach = BrainTune.Get_("waypm", 10.0F)
        While at < pts.Count - 1 AndAlso (pts(at) - here).Length < reach
            at += 1
        End While

        BrainGoal.Follow = False
        BrainGoal.Target = pts(at)
        BrainGoal.HasTarget = True
    End Sub

    Public ReadOnly Property Done As Boolean
        Get
            Return ready AndAlso at >= pts.Count - 1
        End Get
    End Property

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not ON_ OrElse Not ready Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If verts = 0 Then build_mesh()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' Green, and nothing else here is green: the trail is pink and yellow,
        ' the walk is blue and white and red, the course is red. A road has to
        ' be tellable from the path actually driven over it at a glance.
        shader.SetVec3("colour", New Vector3(0.25F, 0.95F, 0.35F))
        GL.LineWidth(3.0F)
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
    End Sub

    Private Sub build_mesh()
        Dim v As New List(Of Single)
        For i = 0 To pts.Count - 2
            push(v, pts(i))
            push(v, pts(i + 1))
        Next
        ' A post at every waypoint, so the spacing is visible - a road with
        ' nodes ninety metres apart and one with them every five drive very
        ' differently and look identical as a line.
        For Each q In pts
            push(v, q)
            push2(v, q, 4.0F)
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

    Private Sub push(v As List(Of Single), p As Vector2)
        v.Add(p.X) : v.Add(BrainNav.Ground(p.X, p.Y) + 0.3F) : v.Add(p.Y)
    End Sub

    Private Sub push2(v As List(Of Single), p As Vector2, up As Single)
        v.Add(p.X) : v.Add(BrainNav.Ground(p.X, p.Y) + up) : v.Add(p.Y)
    End Sub

End Module

Imports System.IO
Imports System.Text.Json
Imports OpenTK.Mathematics

''' <summary>
''' The SIM: drive every tank to a start point, then along the path that leaves
''' it, and see where each hull thinks its neighbours are.
'''
''' EVERY METRE OF THIS COMES FROM RAY STUDIO. The graph is read from
''' &lt;map&gt;_paths.json - the editor's own PathEdit.to_dict - and the runs are
''' driven from its saved ordered roads; nodes and edges remain for drawing and metadata. The route catalogue in TankRoutes is NOT
''' consulted: TankDrive.PickGoal is the only thing that reads `drive.path`,
''' and it is switched off while SIM_RUN. `sim_line_up` clears `drive.path`
''' outright so there is nothing left to read by accident.
'''
''' That separation is the point. The catalogue is a search result; this is a
''' path a person drew and checked. Mixing them would make it impossible to say
''' which one a tank was following when it did something interesting.
'''
''' NOTHING HERE READS WHAT THE RAYS FIND. "Get this wired but don't worry about
''' dealing with what the rays find. I just want them drawn." So this produces
''' the sixteen and stops.
'''
''' Added 2026-09-13 by Tank AI work.
''' </summary>
Public Module TankSim

    ''' <summary>The sim is running. The SIM button sets it.</summary>
    Public SIM_RUN As Boolean = False

    ''' <summary>Held. Space toggles it, and it stops the hulls moving without
    ''' throwing away where they were going - a stop that forgot the goals
    ''' would restart as a different run and could not be compared with the
    ''' one before it.</summary>
    Public SIM_PAUSED As Boolean = False

    ''' <summary>Draw the avoidance rays.</summary>
    Public SIM_SHOW_RAYS As Boolean = True

    ''' <summary>Draw the path each hull is following.</summary>
    Public SIM_SHOW_PATHS As Boolean = True

    ''' <summary>Draw the old hull-to-goal ray, the one that predates the sim.
    '''
    ''' OFF, and on its own switch, because it is the other thing that appears
    ''' when SIM starts and vanishes on Reset - it needs drive.hasGoal, which
    ''' only the sim sets. With two blocks behaving identically there was no way
    ''' to say which was on screen without turning one off, so now you can.
    '''
    ''' It also mostly duplicates the run now: under the sim `goal` is the next
    ''' waypoint, so the ray is a short piece of the path already drawn.</summary>
    Public SIM_SHOW_GOAL As Boolean = False

    ''' <summary>How far a hull looks to the SIDES and BEHIND. Short, because
    ''' a neighbour alongside is either touching or it is not.</summary>
    Public SIM_RAY_M As Single = 3.0F

    ''' <summary>How far a hull looks AHEAD - the owner's twenty metres.
    '''
    ''' Long, and only forward, because forward is the one direction with time
    ''' in it: at 7 m/s twenty metres is about three seconds of warning, which is
    ''' enough to turn. Twenty metres out of the sides would just report every
    ''' tank in the column beside it, permanently.</summary>
    Public SIM_RAY_FRONT_M As Single = 20.0F

    ''' <summary>How far ray i reaches. The three forward ones get the long
    ''' range; the rest stay short.</summary>
    Public Function RayLen(i As Integer) As Single
        If i = R_FL OrElse i = R_FR OrElse i = R_FRONT Then Return SIM_RAY_FRONT_M
        Return SIM_RAY_M
    End Function

    ''' <summary>Eight rays a hull: the four corners and the middle of each
    ''' of the four sides. The order is fixed and the code below depends on
    ''' it - FL, FR, RL, RR, then front, rear, right, left.</summary>
    Public Const RAY_COUNT As Integer = 8
    Public Const R_FL As Integer = 0
    Public Const R_FR As Integer = 1
    Public Const R_RL As Integer = 2
    Public Const R_RR As Integer = 3
    Public Const R_FRONT As Integer = 4
    Public Const R_REAR As Integer = 5
    Public Const R_RIGHT As Integer = 6
    Public Const R_LEFT As Integer = 7

    ''' <summary>The rays that look where the hull is going. A blocked one of
    ''' these is a reason to do something; a blocked rear ray is not.</summary>
    Public ReadOnly FORWARD_RAYS As Integer() = {R_FL, R_FR, R_FRONT}

    ''' <summary>How wide another hull counts as when a ray is tested against
    ''' it. Half the drive's hull radius - the ray is looking for a tank to
    ''' avoid, not measuring one.</summary>
    Public Const OTHER_R As Single = 2.25F

    ''' <summary>
    ''' A ray seeing another hull does NOT automatically mean this hull must stop.
    ''' The front ray may look 20 m ahead for warning, but only a nearby hit is an
    ''' immediate obstruction. Corner rays are even stricter: a tank off the front
    ''' corner is relevant only when it is close enough to enter the swept hull.
    ''' Distances are measured from the ray origin on this hull's edge to the near
    ''' edge of the other hull's avoidance disc.
    ''' </summary>
    Public Const FRONT_STOP_M As Single = 6.0F
    Public Const CORNER_STOP_M As Single = 2.5F

    ''' <summary>How close counts as reaching a waypoint. Looser than the
    ''' drive's own ARRIVE_M so a hull that stops just short still advances -
    ''' a run that stalls one metry short of a waypoint never finishes, and
    ''' looks exactly like a hull that has lost its path.</summary>
    Public Const WAYPOINT_M As Single = 8.0F

    ' A START is not an ordinary waypoint. The 8 m waypoint ring is intentionally
    ' loose so a moving hull does not hang just short of a road point, but using
    ' that same ring for START made the sim pause visibly several metres away.
    ' START must be reached by the hull centre before it can pause the run.
    Public Const START_REACH_M As Single = 1.0F

    Public Structure SimNode
        Public x As Single
        Public z As Single
        Public team As Integer      ' bitmask: 1, 2, or 3 for both
        Public isStart As Boolean
        ' WHICH SIDE SPAWNS HERE, and it is NOT `team`. A base vertex is the
        ' first point of its own side's roads and the LAST point of the other
        ' side's, so its team mask is 1|2 = 3 at both bases - it matches
        ' everybody and can never say who starts there. Ray Studio writes this
        ' one where the side is still known. Same bitmask, different question.
        Public startTeam As Integer
        Public msg As String
        Public note As String
        Public spd As String
    End Structure

    ' ONE ORDERED ROAD EXACTLY AS RAY STUDIO SAVED IT. The graph is still kept
    ' for drawing, point metadata and editor topology, but it is NOT enough to
    ' recover a road at a fork. `nodeIds` is the authoritative driving order.
    Private Structure SimRoad
        Public id As Integer
        Public team As Integer
        Public nodeIds As List(Of Integer)
    End Structure

    Private ReadOnly nodes As New Dictionary(Of Integer, SimNode)
    Private ReadOnly adj As New Dictionary(Of Integer, List(Of Integer))
    Private ReadOnly startIds As New List(Of Integer)
    Private ReadOnly savedRoads As New List(Of SimRoad)
    Private savedRoadsValid As Boolean = False

    Public startsMsg As String = "no paths loaded"

    ''' <summary>When the file the sim is driving was last written, and how
    ''' long ago that was.
    '''
    ''' ON SCREEN, because a stale file is invisible otherwise. The tanks
    ''' drove a graph nobody in the room had saved - it was correct, it was
    ''' Ray Studio's format, and it was an hour old - and the only way to
    ''' tell was to notice the paths went base to base and reason backwards.
    ''' A timestamp beside the vert count makes that a glance.</summary>
    Public pathsStamp As String = ""
    Public ReadOnly Property StartCount As Integer
        Get
            Return startIds.Count
        End Get
    End Property

    Private ReadOnly rng As New Random(12345)

    ''' <summary>The run each hull is driving, in world XZ, and how far along
    ''' it is. Per instance so a hull keeps its own path across frames and
    ''' across a pause.</summary>
    Private ReadOnly hullRun As New Dictionary(Of TankInstance, List(Of Vector2))
    Private ReadOnly atOf As New Dictionary(Of TankInstance, Integer)

    ' DEBUG ONE TANK ONLY. The first hull that ACTUALLY REACHES its assigned
    ' start becomes DEBUG_TANK. Keep BOTH representations of its route:
    '   hullRunNodeIds = the ORIGINAL Ray Studio graph nodes, in route order.
    '   hullRunNodeAt  = one entry per subdivided drive waypoint; -1 means the
    '                    waypoint is synthetic, otherwise it is the real node id.
    ' This preserves point identity all the way to the reached-point test.
    Private ReadOnly hullRunNodeIds As New Dictionary(Of TankInstance, List(Of Integer))
    Private ReadOnly hullRunNodeAt As New Dictionary(Of TankInstance, List(Of Integer))
    Private debugTankId As Integer = -1
    Private debugTankDumped As Boolean = False
    Private debugTankLastNodeId As Integer = -1

    ''' <summary>
    ''' Read the editor's saved graph - every node, every edge.
    '''
    ''' The file is Ray Studio's PathEdit.to_dict: a nodes array carrying id,
    ''' x, z, team, start and the message fields, and an edges array of id
    ''' pairs. Both are needed - the starts alone say where to send a tank and
    ''' nothing about where it goes next.
    ''' </summary>
    Public Function LoadPaths(mapName As String) As Integer
        nodes.Clear()
        adj.Clear()
        startIds.Clear()
        savedRoads.Clear()
        savedRoadsValid = False
        lines.Clear()
        startPts.Clear()
        hullRun.Clear()
        atOf.Clear()
        hullRunNodeIds.Clear()
        hullRunNodeAt.Clear()
        debugTankId = -1
        debugTankDumped = False
        debugTankLastNodeId = -1

        Dim p = Path.Combine(Environment.GetEnvironmentVariable("TEMP"),
                             "nuTerra", "flight", mapName & "_paths.json")
        If Not File.Exists(p) Then
            startsMsg = "NO PATHS SAVED - press [F5] in Ray Studio"
            pathsStamp = ""
            LogThis("tank sim: {0} (looked for {1})", startsMsg, p)
            Return 0
        End If
        Try
            Using doc = JsonDocument.Parse(File.ReadAllText(p))
                Dim arr As JsonElement = Nothing
                If Not doc.RootElement.TryGetProperty("nodes", arr) Then
                    startsMsg = "paths.json has no nodes array"
                    Return 0
                End If
                For Each n As JsonElement In arr.EnumerateArray()
                    Dim id = n.GetProperty("id").GetInt32()
                    Dim sn As SimNode
                    sn.x = n.GetProperty("x").GetSingle()
                    sn.z = n.GetProperty("z").GetSingle()
                    Dim e As JsonElement = Nothing
                    sn.team = If(n.TryGetProperty("team", e), e.GetInt32(), 0)
                    sn.isStart = n.TryGetProperty("start", e) AndAlso
                                 e.ValueKind = JsonValueKind.True
                    ' AN OLDER FILE HAS NO start_team. Falling back to the
                    ' mask leaves that file meaning exactly what it meant
                    ' before, rather than reading as "nobody spawns anywhere"
                    ' and parking the whole roster.
                    sn.startTeam = If(n.TryGetProperty("start_team", e),
                                      e.GetInt32(), 0)
                    If sn.isStart AndAlso sn.startTeam = 0 Then
                        sn.startTeam = sn.team
                    End If
                    sn.msg = If(n.TryGetProperty("msg", e), e.GetString(), "")
                    sn.note = If(n.TryGetProperty("note", e), e.GetString(), "")
                    sn.spd = If(n.TryGetProperty("spd", e), e.GetString(), "")
                    nodes(id) = sn
                    adj(id) = New List(Of Integer)
                    If sn.isStart Then startIds.Add(id)
                Next
                Dim ed As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("edges", ed) Then
                    For Each pair As JsonElement In ed.EnumerateArray()
                        Dim a = pair(0).GetInt32(), b = pair(1).GetInt32()
                        If adj.ContainsKey(a) AndAlso adj.ContainsKey(b) Then
                            adj(a).Add(b)
                            adj(b).Add(a)
                        End If
                    Next
                End If

                ' THE ORDERED MAZE ROADS ARE THE DRIVING TRUTH. Nodes+edges are
                ' an undirected merged editor graph; at a fork they cannot tell
                ' which outgoing edge belonged to the road that arrived there.
                ' Ray Studio saves each solved road as an ordered node-id chain.
                Dim rv As JsonElement = Nothing
                savedRoadsValid = doc.RootElement.TryGetProperty("roads_valid", rv) AndAlso
                                  rv.ValueKind = JsonValueKind.True
                If savedRoadsValid Then
                    Dim roadsEl As JsonElement = Nothing
                    If doc.RootElement.TryGetProperty("roads", roadsEl) AndAlso
                       roadsEl.ValueKind = JsonValueKind.Array Then
                        For Each rr As JsonElement In roadsEl.EnumerateArray()
                            Dim sr As SimRoad
                            Dim re As JsonElement = Nothing
                            sr.id = If(rr.TryGetProperty("id", re), re.GetInt32(), savedRoads.Count)
                            sr.team = If(rr.TryGetProperty("team", re), re.GetInt32(), 0)
                            sr.nodeIds = New List(Of Integer)

                            Dim idsEl As JsonElement = Nothing
                            If rr.TryGetProperty("nodes", idsEl) AndAlso
                               idsEl.ValueKind = JsonValueKind.Array Then
                                Dim badRoad = False
                                For Each je As JsonElement In idsEl.EnumerateArray()
                                    Dim nid = je.GetInt32()
                                    If Not nodes.ContainsKey(nid) Then
                                        badRoad = True
                                        Exit For
                                    End If
                                    sr.nodeIds.Add(nid)
                                Next
                                If Not badRoad AndAlso sr.nodeIds.Count >= 2 AndAlso
                                   (sr.team = 1 OrElse sr.team = 2) Then
                                    savedRoads.Add(sr)
                                End If
                            End If
                        Next
                    End If
                    If savedRoads.Count = 0 Then savedRoadsValid = False
                End If
            End Using
        Catch ex As Exception
            ' LOUD, NOT SILENT. A sim that quietly found no path and drove
            ' nowhere is indistinguishable from one that is working and has
            ' nothing to do.
            startsMsg = "paths.json unreadable: " & ex.Message
            LogThis("tank sim: {0}", startsMsg)
            Return 0
        End Try
        Dim edgeCount = 0
        For Each kv In adj
            edgeCount += kv.Value.Count
        Next
        startsMsg = String.Format("{0} verts, {1} lines, {2} start(s), {3} saved road(s)",
                                  nodes.Count, edgeCount \ 2, startIds.Count, savedRoads.Count)
        Dim wrote = File.GetLastWriteTime(p)
        Dim age = DateTime.Now - wrote
        Dim howLong As String
        If age.TotalMinutes < 1.0 Then
            howLong = "just now"
        ElseIf age.TotalMinutes < 90.0 Then
            howLong = CInt(age.TotalMinutes) & " min ago"
        Else
            howLong = age.TotalHours.ToString("0.#") & " h ago"
        End If
        pathsStamp = String.Format("saved {0:HH:mm:ss} ({1})", wrote, howLong)
        BuildDrawLists()
        If savedRoadsValid Then
            LogThis("tank sim: {0} from {1}_paths.json, {2} - driving ordered Ray Studio roads",
                    startsMsg, mapName, pathsStamp)
        Else
            LogThis("tank sim: {0} from {1}_paths.json, {2} - WARNING no valid ordered roads; graph-walk fallback",
                    startsMsg, mapName, pathsStamp)
        End If
        Return startIds.Count
    End Function

    ''' <summary>
    ''' Build one run from the exact ordered road Ray Studio saved. No graph
    ''' walking and no fork choice happens here. Multiple roads may share one
    ''' start; `pick` deterministically selects one for this hull.
    ''' </summary>
    Private Function BuildSavedRoad(startId As Integer,
                                    teamBit As Integer,
                                    pick As Integer,
                                    ByRef rawIds As List(Of Integer),
                                    ByRef driveNodeIds As List(Of Integer)) As List(Of Vector2)
        rawIds = New List(Of Integer)
        driveNodeIds = New List(Of Integer)
        Dim choices As New List(Of Integer)

        For i = 0 To savedRoads.Count - 1
            Dim r = savedRoads(i)
            If r.team <> teamBit OrElse r.nodeIds Is Nothing OrElse r.nodeIds.Count < 2 Then Continue For
            If r.nodeIds(0) = startId Then choices.Add(i)
        Next

        If choices.Count = 0 Then Return New List(Of Vector2)

        Dim slot = Math.Abs(pick) Mod choices.Count
        Dim chosenRoad = savedRoads(choices(slot))
        rawIds.AddRange(chosenRoad.nodeIds)

        Dim pts As New List(Of Vector2)(rawIds.Count)
        For Each id In rawIds
            pts.Add(New Vector2(nodes(id).x, nodes(id).z))
        Next

        Return Subdivide(pts, rawIds, driveNodeIds)
    End Function

    ''' <summary>
    ''' The run leaving a start point, walked to a dead end.
    '''
    ''' IT CHOOSES AT FORKS, and it has to. The first version stopped at one
    ''' rather than guess, on the reasoning that a fork is a real decision and
    ''' inventing a branch here would be this file making up a route. That was
    ''' wrong for a reason the data makes obvious the moment it is looked at:
    ''' EVERY START IS A FORK. The 1 m dedupe merges all the roads leaving a
    ''' base onto one vertex, so the six starts have degree 3, 3, 3, 3, 3 and
    ''' 8 - and a walk that stops at the first fork stopped on the first point
    ''' every time. Six runs, one point each. The tanks drove to their start
    ''' and parked, and nothing was ever drawn under them.
    '''
    ''' `pick` is the choosing hand - each tank passes its own number, so two
    ''' hulls on the same start take different roads off it instead of thirty
    ''' following one. It is deliberately not random: the same tank on the same
    ''' graph takes the same road every run, which is what makes two runs
    ''' comparable.
    '''
    ''' `seen` stops it circling a loop forever; a road that rejoins itself
    ''' ends the run rather than lapping.
    ''' </summary>
    Public Function RunFrom(startId As Integer, Optional pick As Integer = 0) As List(Of Vector2)
        Dim rawIds As List(Of Integer) = Nothing
        Dim driveNodeIds As List(Of Integer) = Nothing
        Return BuildRunFrom(startId, pick, rawIds, driveNodeIds)
    End Function

    ''' <summary>
    ''' Walk the graph and preserve the ORIGINAL Ray Studio node IDs in the exact
    ''' order chosen. It also builds a parallel id list for the subdivided drive
    ''' waypoints: a real saved point carries its node id, while an inserted
    ''' STEP_M point carries -1. That mapping is what lets TargetFor know which
    ''' real path point was reached instead of losing identity in Subdivide().
    ''' </summary>
    Private Function BuildRunFrom(startId As Integer,
                                  pick As Integer,
                                  ByRef rawIds As List(Of Integer),
                                  ByRef driveNodeIds As List(Of Integer)) As List(Of Vector2)
        Dim outp As New List(Of Vector2)
        rawIds = New List(Of Integer)
        If Not nodes.ContainsKey(startId) Then Return outp

        Dim seen As New HashSet(Of Integer)
        Dim cur = startId, prev = -1, step_ = 0

        For guard = 0 To 4000
            If Not nodes.ContainsKey(cur) OrElse seen.Contains(cur) Then Exit For

            seen.Add(cur)
            rawIds.Add(cur)
            outp.Add(New Vector2(nodes(cur).x, nodes(cur).z))

            Dim ways As New List(Of Integer)
            For Each n In adj(cur)
                If n <> prev AndAlso Not seen.Contains(n) Then ways.Add(n)
            Next
            If ways.Count = 0 Then Exit For

            ways.Sort()
            Dim take = 0
            If ways.Count > 1 Then
                take = Math.Abs(pick + step_ * 7) Mod ways.Count
                step_ += 1
            End If

            prev = cur
            cur = ways(take)
        Next

        Return Subdivide(outp, rawIds, driveNodeIds)
    End Function

    ''' <summary>
    ''' Print one readable tank's ORIGINAL saved path once, exactly when it
    ''' reaches its assigned start. These are Ray Studio graph nodes, not the
    ''' synthetic STEP_M points inserted for driving.
    ''' </summary>
    Private Function DebugPathPointType(nodeId As Integer) As String
        ' EXACTLY Ray Studio / PathEdit.kind():
        '   degree >= 3 = fork
        '   degree = 1  = end
        '   degree = 2  = through
        '   degree = 0  = loose
        ' START is a separate saved flag and is printed separately.
        If Not nodes.ContainsKey(nodeId) Then Return "missing"

        Dim degree = DebugPathPointDegree(nodeId)
        If degree >= 3 Then Return "fork"
        If degree = 1 Then Return "end"
        If degree = 2 Then Return "through"
        Return "loose"
    End Function

    Private Function DebugPathPointDegree(nodeId As Integer) As Integer
        Dim links As List(Of Integer) = Nothing
        If adj.TryGetValue(nodeId, links) AndAlso links IsNot Nothing Then
            Return links.Count
        End If
        Return 0
    End Function

    Private Sub DumpDebugTankPath(inst As TankInstance)
        If inst Is Nothing OrElse debugTankDumped Then Return

        Dim ids As List(Of Integer) = Nothing
        If Not hullRunNodeIds.TryGetValue(inst, ids) Then
            LogThis("tank sim: PATH DUMP ERROR - no raw path stored for tank " & inst.id)
            Return
        End If
        If ids Is Nothing Then
            LogThis("tank sim: PATH DUMP ERROR - raw path is NOTHING for tank " & inst.id)
            Return
        End If

        debugTankDumped = True
        LogThis("tank sim: ========== FIRST START PATH BEGIN ==========")

        Dim startId = If(ids.Count > 0, ids(0), -1)
        Dim endId = If(ids.Count > 0, ids(ids.Count - 1), -1)
        LogThis("tank sim: FIRST START HIT: tank=" & inst.id &
                " team=" & inst.team.ToString() &
                " start=" & startId &
                " end=" & endId &
                " saved_points=" & ids.Count)
        LogThis("tank sim: PATH IDS: " & String.Join(" -> ", ids))

        For j = 0 To ids.Count - 1
            Dim id = ids(j)
            If Not nodes.ContainsKey(id) Then
                LogThis("tank sim: PATH [" & j.ToString("000") &
                        "] type=MISSING id=" & id)
                Continue For
            End If

            Dim n = nodes(id)
            Dim pointType = DebugPathPointType(id)
            Dim degree = DebugPathPointDegree(id)

            Dim chainRole As String = "middle"
            If j = 0 Then chainRole = "first"
            If j = ids.Count - 1 Then chainRole = If(j = 0, "first+last", "last")

            LogThis("tank sim: PATH [" & j.ToString("000") & "]" &
                    " id=" & id &
                    " kind=" & pointType &
                    " degree=" & degree &
                    " start=" & n.isStart &
                    " chain=" & chainRole &
                    " x=" & n.x.ToString("0.0") &
                    " z=" & n.z.ToString("0.0") &
                    " team=" & n.team &
                    " start_team=" & n.startTeam &
                    " msg=[" & If(n.msg, "") & "]" &
                    " note=[" & If(n.note, "") & "]" &
                    " spd=[" & If(n.spd, "") & "]")
        Next

        LogThis("tank sim: ========== FIRST START PATH END ==========")
    End Sub

    ''' <summary>
    ''' Output the REAL Ray Studio point that has just been reached. Synthetic
    ''' subdivision waypoints never call this routine because their mapped id is
    ''' -1. Node IDs are unique in BuildRunFrom (the walk keeps a seen set), so
    ''' debugTankLastNodeId also prevents the final point from printing every
    ''' frame after the tank stops there.
    ''' </summary>
    Private Sub LogReachedPathPoint(inst As TankInstance, nodeId As Integer)
        If inst Is Nothing OrElse nodeId < 0 Then Return
        If inst.id <> debugTankId Then Return
        If nodeId = debugTankLastNodeId Then Return

        debugTankLastNodeId = nodeId

        If Not nodes.ContainsKey(nodeId) Then
            LogThis("tank sim: REACHED POINT id=" & nodeId & " MISSING")
            Return
        End If

        Dim n = nodes(nodeId)
        Dim ids As List(Of Integer) = Nothing
        Dim pathIndex = -1
        Dim pathCount = 0
        If hullRunNodeIds.TryGetValue(inst, ids) AndAlso ids IsNot Nothing Then
            pathIndex = ids.IndexOf(nodeId)
            pathCount = ids.Count
        End If

        Dim kind As String = ""
        If pathIndex = 0 Then kind = " START"
        If pathCount > 0 AndAlso pathIndex = pathCount - 1 Then kind &= " END"
        Dim pointType = DebugPathPointType(nodeId)
        Dim degree = DebugPathPointDegree(nodeId)

        LogThis("tank sim: REACHED POINT" & kind &
                " path=" & If(pathIndex >= 0, (pathIndex + 1).ToString(), "?") &
                "/" & If(pathCount > 0, pathCount.ToString(), "?") &
                " kind=" & pointType &
                " degree=" & degree &
                " id=" & nodeId &
                " x=" & n.x.ToString("0.0") &
                " z=" & n.z.ToString("0.0") &
                " team=" & n.team &
                " start=" & n.isStart &
                " start_team=" & n.startTeam &
                " msg=[" & If(n.msg, "") & "]" &
                " note=[" & If(n.note, "") & "]" &
                " spd=[" & If(n.spd, "") & "]")
    End Sub

    ''' <summary>
    ''' Cut every long leg into steps, so a hull FOLLOWS the road instead of
    ''' aiming across it.
    '''
    ''' Ray Studio simplifies before it saves - a straight lane keeps its corners
    ''' and loses everything between - so two consecutive verts on one of these
    ''' roads can be 234 m apart. Measured on the saved file: a hull standing on
    ''' its start advanced to waypoint 1 and had a goal 234 m away, which it then
    ''' drove at in a straight line. Wherever the road bent, the tank did not.
    '''
    ''' It also drew that straight line, which is the base-to-base ray that
    ''' appeared the moment the sim started and went away on Reset.
    '''
    ''' The extra points are on the segment the file already describes, so this
    ''' adds no geometry of its own - it only stops the hull cutting the corner
    ''' between two points that are both on the road.
    ''' </summary>
    Public Const STEP_M As Single = 12.0F

    Private Function Subdivide(pts As List(Of Vector2),
                               rawIds As List(Of Integer),
                               ByRef driveNodeIds As List(Of Integer)) As List(Of Vector2)
        driveNodeIds = New List(Of Integer)
        If pts.Count = 0 Then Return pts

        Dim outp As New List(Of Vector2)(pts.Count * 2)
        outp.Add(pts(0))
        driveNodeIds.Add(If(rawIds IsNot Nothing AndAlso rawIds.Count > 0, rawIds(0), -1))

        If pts.Count < 2 Then Return outp

        For i = 0 To pts.Count - 2
            Dim a = pts(i), b = pts(i + 1)
            Dim d = (b - a).Length
            Dim steps = CInt(Math.Floor(d / STEP_M))

            ' Inserted steering points are geometry only, not Ray Studio points.
            For k = 1 To steps
                Dim f = CSng(k) / CSng(steps + 1)
                outp.Add(New Vector2(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f))
                driveNodeIds.Add(-1)
            Next

            ' The end of each leg IS the next original Ray Studio node.
            outp.Add(b)
            driveNodeIds.Add(If(rawIds IsNot Nothing AndAlso i + 1 < rawIds.Count,
                                rawIds(i + 1), -1))
        Next

        Return outp
    End Function

    Public Function HasPaths() As Boolean
        Return startIds.Count > 0
    End Function

    ''' <summary>
    ''' Where this hull should be steering now, advancing along its run as it
    ''' arrives. Assigns a start at random the first time it is asked.
    '''
    ''' PREFERS ITS OWN SIDE, but takes any start rather than stand still - a
    ''' sim with half the fleet parked is not a test of anything.
    ''' </summary>
    Public Function TargetFor(inst As TankInstance) As Vector2
        Dim run As List(Of Vector2) = Nothing
        If Not hullRun.TryGetValue(inst, run) Then
            Dim chosen = 0
            If Not assigned.TryGetValue(inst, chosen) Then
                ' Not placed by AssignStarts - a hull that appeared after the
                ' line-up. Nearest start rather than a random one, which is
                ' the same rule the sorted map follows.
                Dim best = -1
                Dim bd = Single.MaxValue
                Dim bit = If(inst.team = TankTeam.Green, 1, 2)
                For Each id In startIds
                    If startIds.Count > 1 AndAlso (nodes(id).startTeam And bit) = 0 Then Continue For
                    Dim dx = nodes(id).x - inst.position.X
                    Dim dz = nodes(id).z - inst.position.Z
                    Dim d = dx * dx + dz * dz
                    If d < bd Then bd = d : best = id
                Next
                If best < 0 Then Return New Vector2(inst.position.X, inst.position.Z)
                chosen = best
            End If
            Dim rawIds As List(Of Integer) = Nothing
            Dim builtDriveNodeIds As List(Of Integer) = Nothing
            Dim sideBit = If(inst.team = TankTeam.Green, 1, 2)
            Dim roadPick = inst.id * 3 + If(inst.team = TankTeam.Green, 0, 1)

            If savedRoadsValid Then
                ' EXACT SAVED ROAD. Do not walk the merged graph and do not
                ' choose at forks: Ray Studio already told us the node order.
                run = BuildSavedRoad(chosen, sideBit, roadPick,
                                     rawIds, builtDriveNodeIds)
                If run.Count = 0 Then
                    LogThis("tank sim: ERROR no saved team " & sideBit &
                            " road begins at assigned start " & chosen &
                            " for tank " & inst.id)
                End If
            Else
                ' OLD FILE COMPATIBILITY ONLY. This can choose a wrong branch.
                run = BuildRunFrom(chosen, roadPick, rawIds, builtDriveNodeIds)
            End If
            hullRun(inst) = run
            hullRunNodeIds(inst) = rawIds
            hullRunNodeAt(inst) = builtDriveNodeIds
            atOf(inst) = 0
        End If
        If run.Count = 0 Then Return New Vector2(inst.position.X, inst.position.Z)

        Dim i = atOf(inst)
        Dim here As New Vector2(inst.position.X, inst.position.Z)

        ' WALK THE IDENTITY FIRST. The arrival radius depends on WHAT this drive
        ' point is. Synthetic STEP_M points are -1; real Ray Studio points keep
        ' their node id all the way from BuildRunFrom to here.
        Dim driveNodeIds As List(Of Integer) = Nothing
        Dim reachedNodeId As Integer = -1
        If hullRunNodeAt.TryGetValue(inst, driveNodeIds) AndAlso
           driveNodeIds IsNot Nothing AndAlso i < driveNodeIds.Count Then
            reachedNodeId = driveNodeIds(i)
        End If

        Dim currentIsStart As Boolean =
            reachedNodeId >= 0 AndAlso
            nodes.ContainsKey(reachedNodeId) AndAlso
            nodes(reachedNodeId).isStart

        ' Ordinary road points keep the forgiving 8 m ring. A real START uses
        ' the tight centre-to-point radius, otherwise the sim can register the
        ' start up to eight metres away and falsely look as though it hit the point.
        Dim reachM As Single = If(currentIsStart, START_REACH_M, WAYPOINT_M)
        Dim reached As Boolean =
            i < run.Count AndAlso (run(i) - here).Length < reachM

        If reached Then
            ' AUTHORITATIVE START-ARRIVAL EVENT.
            ' The first tank to reach any real Ray Studio node marked start=True
            ' becomes the debug tank. Dump its entire saved road once, then let
            ' normal waypoint advancement continue without pausing the simulation.
            ' Do not assume that means a global start 0 or depend on drive index 0.
            If reachedNodeId >= 0 AndAlso
               nodes.ContainsKey(reachedNodeId) AndAlso
               nodes(reachedNodeId).isStart AndAlso
               debugTankId < 0 Then

                debugTankId = inst.id

                ' Dump HERE, on the exact first START hit, through the same visible
                ' tank sim: console path. This is diagnostic only: reaching the start
                ' no longer pauses or holds the tank.
                LogThis("tank sim: REACHED START: id=" & inst.id &
                        " team=" & inst.team.ToString() &
                        " node=" & reachedNodeId)
                DumpDebugTankPath(inst)
            End If

            ' Point output comes from the SAME reached event and the SAME preserved
            ' Ray Studio node identity. Synthetic subdivision points stay silent.
            If inst.id = debugTankId Then
                If reachedNodeId >= 0 Then
                    LogReachedPathPoint(inst, reachedNodeId)
                ElseIf driveNodeIds Is Nothing Then
                    LogThis("tank sim: PATH DUMP ERROR - no drive-to-node map for tank " & inst.id)
                End If
            End If

            ' Advance one point only when another point actually exists.
            If i < run.Count - 1 Then
                atOf(inst) = i + 1
                i += 1
            End If
        End If

        Return run(i)
    End Function

    ''' <summary>The run this hull is on, for drawing. Empty until it has been
    ''' assigned one.</summary>
    Public Function RunOf(inst As TankInstance) As List(Of Vector2)
        Dim r As List(Of Vector2) = Nothing
        If hullRun.TryGetValue(inst, r) Then Return r
        Return Nothing
    End Function

    ''' <summary>How far along its run this hull is.</summary>
    Public Function RunAt(inst As TankInstance) As Integer
        Dim i = 0
        atOf.TryGetValue(inst, i)
        Return i
    End Function

    ''' <summary>Bumped once a frame by the renderer, so the rays are cast
    ''' once and both the driving and the drawing read the same answer. Two
    ''' separate casts would be twice the work AND could disagree, which
    ''' would draw a clear ray on a hull that had just swerved for it.</summary>
    Public frame As Integer = 0

    Private ReadOnly hitsOf As New Dictionary(Of TankInstance, Boolean())
    Private ReadOnly hitDistOf As New Dictionary(Of TankInstance, Single())
    Private ReadOnly hitFrame As New Dictionary(Of TankInstance, Integer)

    ''' <summary>
    ''' Which of this hull's eight rays strike another tank, out to SIM_RAY_M.
    '''
    ''' Segment against disc, each neighbour treated as a circle of OTHER_R.
    ''' A box-to-box test would be more exact and it is not worth it: the ray
    ''' is asking "is somebody there", and a hull that is two metres out of
    ''' position has already been swerved for by the time the difference
    ''' between a disc and a rectangle would matter.
    ''' </summary>
    Public Function RayHits(inst As TankInstance,
                            others As List(Of TankInstance)) As Boolean()
        Dim f = 0
        Dim cached As Boolean() = Nothing
        If hitFrame.TryGetValue(inst, f) AndAlso f = frame AndAlso
           hitsOf.TryGetValue(inst, cached) Then
            Return cached
        End If

        Dim hits(RAY_COUNT - 1) As Boolean
        Dim hitDist(RAY_COUNT - 1) As Single
        For i = 0 To RAY_COUNT - 1
            hitDist(i) = Single.MaxValue
        Next

        Dim rays = HullRays(inst)
        If others IsNot Nothing Then
            For i = 0 To Math.Min(RAY_COUNT, rays.Count) - 1
                Dim o = rays(i).Item1, d = rays(i).Item2
                Dim reach = RayLen(i)

                For Each t In others
                    If t Is inst OrElse t Is Nothing Then Continue For

                    ' Project the other hull centre onto this ray.
                    Dim cx = t.position.X - o.X
                    Dim cz = t.position.Z - o.Y
                    Dim along = cx * d.X + cz * d.Y

                    ' A centre entirely behind the ray origin cannot be hit by
                    ' this outward-looking ray unless its disc overlaps origin.
                    Dim centre2 = cx * cx + cz * cz
                    Dim perp2 = centre2 - along * along
                    Dim r2 = OTHER_R * OTHER_R
                    If perp2 > r2 Then Continue For

                    ' Distance to the NEAR edge of the other hull's avoidance
                    ' disc, not merely distance to its centre projection.
                    Dim halfChord = CSng(Math.Sqrt(Math.Max(0.0F, r2 - perp2)))
                    Dim enter = along - halfChord
                    Dim leave = along + halfChord

                    ' The disc misses the finite forward ray segment.
                    If leave < 0.0F OrElse enter > reach Then Continue For

                    If enter < 0.0F Then enter = 0.0F

                    hits(i) = True
                    If enter < hitDist(i) Then hitDist(i) = enter
                Next
            Next
        End If

        hitsOf(inst) = hits
        hitDistOf(inst) = hitDist
        hitFrame(inst) = frame
        Return hits
    End Function

    ''' <summary>
    ''' Distance from each ray origin to the first hull it actually hits.
    ''' Single.MaxValue means no hit. RayHits is called first so the Boolean
    ''' drawing data and the distances are always from the same frame/cast.
    ''' </summary>
    Public Function RayHitDistances(inst As TankInstance,
                                    others As List(Of TankInstance)) As Single()
        RayHits(inst, others)

        Dim d As Single() = Nothing
        If hitDistOf.TryGetValue(inst, d) Then Return d

        Dim none(RAY_COUNT - 1) As Single
        For i = 0 To RAY_COUNT - 1
            none(i) = Single.MaxValue
        Next
        Return none
    End Function

    ''' <summary>Is anything in front of this hull, by the rays that look
    ''' forward. This is what the go-around asks.</summary>
    Public Function BlockedAhead(inst As TankInstance,
                                 others As List(Of TankInstance)) As Boolean
        Dim dist = RayHitDistances(inst, others)

        ' Straight ahead: stop only when the other hull is close enough to
        ' interfere with forward travel. A hit 15-20 m away is warning, not a
        ' reason to freeze now.
        If dist(R_FRONT) <= FRONT_STOP_M Then Return True

        ' Front-corner rays look diagonally outward. They should stop straight
        ' travel only for a very near hull that is entering the swept corner.
        ' A distant side hit is not in this tank's path.
        If dist(R_FL) <= CORNER_STOP_M Then Return True
        If dist(R_FR) <= CORNER_STOP_M Then Return True

        Return False
    End Function

    ''' <summary>Is the ground this hull would swing into already taken - the
    ''' right side, by the rays that look that way.</summary>
    Public Function RightIsClear(inst As TankInstance,
                                 others As List(Of TankInstance),
                                 nav As TankNav) As Boolean
        Dim h = RayHits(inst, others)
        Dim rays = HullRays(inst)

        If h(R_RIGHT) OrElse h(R_FR) Then Return False

        If R_RIGHT < rays.Count Then
            Dim o = rays(R_RIGHT).Item1, d = rays(R_RIGHT).Item2
            If RayHitsNav(nav, o, d, RayLen(R_RIGHT)) Then Return False
        End If

        If R_FR < rays.Count Then
            Dim o = rays(R_FR).Item1, d = rays(R_FR).Item2
            If RayHitsNav(nav, o, d, RayLen(R_FR)) Then Return False
        End If

        Return True
    End Function

    ''' <summary>Is something up against the back of this hull.
    '''
    ''' So it does not reverse into it. Backing out is the drive's answer to
    ''' being wedged, and it is the wrong answer when the thing behind is
    ''' another tank - two hulls nose to tail both reversing is how a column
    ''' concertinas.</summary>
    Public Function RearBlocked(inst As TankInstance,
                                others As List(Of TankInstance)) As Boolean
        Dim h = RayHits(inst, others)
        Return h(R_REAR) OrElse h(R_RL) OrElse h(R_RR)
    End Function

    ''' <summary>
    ''' A ray that hits nothing, and the point at the far end of it.
    '''
    ''' "he should travel the no hit rays to the end of the ray length." So the
    ''' way out is not a fixed turn any more - it is whichever of the eight the
    ''' hull can actually see down, driven to the end of its reach.
    '''
    ''' THE ORDER IS THE RIGHT-HAND RULE, still. Right first, then round the
    ''' front, then left, then the back corners. Two hulls meeting head-on both
    ''' find their right clear and swing apart; picking the roomiest side per
    ''' tank would have them both choose the same side of the road and meet
    ''' again there. The rule decides; the rays only say which options exist.
    '''
    ''' Returns False when every ray is blocked, and then waiting really is the
    ''' only answer.
    ''' </summary>
    Public ReadOnly WAY_OUT As Integer() =
        {R_RIGHT, R_FR, R_FRONT, R_FL, R_LEFT, R_RR, R_RL, R_REAR}

    Public Function ClearWay(inst As TankInstance,
                             others As List(Of TankInstance),
                             nav As TankNav,
                             ByRef target As Vector2) As Boolean
        Dim h = RayHits(inst, others)
        Dim rays = HullRays(inst)

        For Each i In WAY_OUT
            If i >= rays.Count OrElse h(i) Then Continue For

            Dim o = rays(i).Item1, d = rays(i).Item2
            Dim reach = RayLen(i)

            ' A ray that crosses the baked no-go map is NOT a way out.
            ' This is the same cell test used by the yellow diagnostic ray.
            If RayHitsNav(nav, o, d, reach) Then Continue For

            target = o + d * reach
            Return True
        Next

        Return False
    End Function

    ''' <summary>
    ''' Does this finite ray cross the authoritative baked TankNav no-go map?
    '''
    ''' Sample at half a nav cell so a one-cell blocker cannot be stepped over.
    ''' The first sample is beyond the hull-edge ray origin, matching the visual
    ''' yellow-ray diagnostic rather than treating the current hull cell as a hit.
    ''' </summary>
    Private Function RayHitsNav(nav As TankNav,
                                o As Vector2,
                                d As Vector2,
                                reach As Single) As Boolean
        If nav Is Nothing OrElse Not nav.ready Then Return False

        Dim stepM = Math.Max(0.25F, nav.cell_m * 0.5F)
        Dim steps = Math.Max(1, CInt(Math.Ceiling(reach / stepM)))

        For s = 1 To steps
            Dim dist = Math.Min(reach, CSng(s) * stepM)
            Dim q = o + d * dist

            Dim cx As Integer, cz As Integer
            nav.CellOf(q.X, q.Y, cx, cz)

            If Not nav.InBounds(cx, cz) Then Return True

            Dim flags = nav.cell(cz * TankNav.SIZE + cx)
            If (flags And TankNav.IMPASSABLE) <> 0 Then Return True
        Next

        Return False
    End Function

    ''' <summary>
    ''' Combined blocker state for all eight hull rays.
    '''
    ''' True means that ray is unusable because it hits either another tank or
    ''' the authoritative baked TankNav no-go map. TankDrive uses this for the
    ''' left/right truth table.
    ''' </summary>
    Public Function RayBlockers(inst As TankInstance,
                                others As List(Of TankInstance),
                                nav As TankNav) As Boolean()
        Dim blocked(RAY_COUNT - 1) As Boolean
        Dim tankHits = RayHits(inst, others)
        Dim rays = HullRays(inst)

        For i = 0 To RAY_COUNT - 1
            If i < tankHits.Length AndAlso tankHits(i) Then
                blocked(i) = True
                Continue For
            End If

            If i >= rays.Count Then Continue For

            Dim o = rays(i).Item1
            Dim d = rays(i).Item2
            blocked(i) = RayHitsNav(nav, o, d, RayLen(i))
        Next

        Return blocked
    End Function

    ''' <summary>Every line in the file, as two world points and the team
    ''' the line serves. Built once at load - the graph does not move.
    '''
    ''' THE WHOLE FILE, not the bit a tank happens to be on. The runs drawn
    ''' per hull stop at the first fork and only exist once a hull has been
    ''' given one, so most of a graph was never on screen and there was no
    ''' way to see whether the thing loaded matched the thing drawn in Ray
    ''' Studio. This is the file, drawn as the file.</summary>
    Public ReadOnly lines As New List(Of ValueTuple(Of Vector2, Vector2, Integer))

    ''' <summary>Every start point, for the rings.</summary>
    Public ReadOnly startPts As New List(Of ValueTuple(Of Vector2, Integer))

    Private Sub BuildDrawLists()
        lines.Clear()
        startPts.Clear()
        For Each kv In adj
            Dim a = kv.Key
            For Each b In kv.Value
                If a > b Then Continue For
                Dim na = nodes(a), nb = nodes(b)
                ' A line serves what both its ends have in common; only a join
                ' with nothing in common reports both.
                Dim t = (na.team And nb.team)
                If t = 0 Then t = na.team Or nb.team
                lines.Add((New Vector2(na.x, na.z), New Vector2(nb.x, nb.z), t))
            Next
        Next
        For Each id In startIds
            startPts.Add((New Vector2(nodes(id).x, nodes(id).z), nodes(id).startTeam))
        Next
    End Sub

    ''' <summary>
    ''' Re-read the file if it has changed on disk since we last looked.
    '''
    ''' So pressing [F5] in Ray Studio shows up here without pressing anything
    ''' in nuTerra. Checked at most once a second and only against the file's
    ''' write time, which is a directory read, not a parse.
    ''' </summary>
    Public Sub PollFile(mapName As String)
        If DateTime.Now < nextPoll Then Return
        nextPoll = DateTime.Now.AddSeconds(1.0)
        Dim p = Path.Combine(Environment.GetEnvironmentVariable("TEMP"),
                             "nuTerra", "flight", mapName & "_paths.json")
        Dim stamp = If(File.Exists(p), File.GetLastWriteTimeUtc(p), DateTime.MinValue)
        If stamp = lastSeen Then Return
        lastSeen = stamp
        LoadPaths(mapName)
    End Sub

    Private nextPoll As DateTime = DateTime.MinValue
    Private lastSeen As DateTime = DateTime.MinValue

    ''' <summary>
    ''' Hand out the start points BY POSITION - left of the formation to the
    ''' left of the fan.
    '''
    ''' "tanks at base locations that are left should be attached to points to
    ''' the left. We are going to paint the front mid and back rows with the
    ''' path start location points."
    '''
    ''' Picking at random was the wrong shape for this. Thirty hulls drawing
    ''' from a hat means the tank on the far left of the grid can be sent to
    ''' the far right start, so its first move is to drive across the front of
    ''' the whole formation - fourteen hulls crossing each other before anyone
    ''' has left the base. Sorted left to right on both sides, nobody crosses
    ''' anybody, and the column that leaves by the left road is the column
    ''' that was already standing on the left.
    '''
    ''' THE ROWS FALL OUT OF IT. The grid is five abreast and three deep, so
    ''' sorting a side's hulls by X puts the three tanks of one column
    ''' together, and the proportional map sends that whole column to the same
    ''' start - front, middle and back row painted with the same point. That is
    ''' the "paint the rows" part: a column of three follows one road, rather
    ''' than three rows fanning to three different ones.
    '''
    ''' Called from sim_line_up, AFTER the hulls are placed - their X has to be
    ''' the formation X, not wherever the last run left them.
    ''' </summary>
    Public Sub AssignStarts(all As List(Of TankInstance))
        hullRun.Clear()
        atOf.Clear()
        assigned.Clear()
        If all Is Nothing OrElse startIds.Count = 0 Then Return

        ' THE TWO FORMATION CENTRES, taken once and before anything is
        ' assigned, because judging either side's starts needs both.
        Dim ctr(2) As Vector2
        Dim ctrN(2) As Integer
        For Each t In all
            If t Is Nothing Then Continue For
            Dim b = If(t.team = TankTeam.Green, 1, 2)
            ctr(b) = New Vector2(ctr(b).X + t.position.X, ctr(b).Y + t.position.Z)
            ctrN(b) += 1
        Next
        For b = 1 To 2
            If ctrN(b) > 0 Then
                ctr(b) = New Vector2(ctr(b).X / ctrN(b), ctr(b).Y / ctrN(b))
            End If
        Next

        For Each side In {1, 2}
            Dim hulls As New List(Of TankInstance)
            For Each t In all
                If t Is Nothing Then Continue For
                Dim bit = If(t.team = TankTeam.Green, 1, 2)
                If bit = side Then hulls.Add(t)
            Next
            If hulls.Count = 0 Then Continue For

            ' This side's starts, and any start if it has none of its own -
            ' a hull with nowhere to go is worse than one sharing a road.
            Dim pts As New List(Of Integer)
            For Each id In startIds
                If (nodes(id).startTeam And side) <> 0 Then pts.Add(id)
            Next
            If pts.Count = 0 Then pts.AddRange(startIds)

            ' EVERY START MATCHED, WHICH IS NO ANSWER AT ALL. That is what a
            ' graph saved before start_team existed looks like: its starts
            ' carry only the shared mask, and that mask is 1|2 = 3 at BOTH
            ' bases, so this side was just handed the enemy's spawn points
            ' along with its own. It is the bug the owner reported - "it is
            ' assigning both teams to start points".
            '
            ' Re-sweeping and re-saving in Ray Studio writes the real field
            ' and this never fires. Until then, fall back to the one thing
            ' still true on the ground: a side spawns at the base it is
            ' STANDING on. Hulls are on their formation at this moment - the
            ' doc above says so - so their centre IS their base.
            If pts.Count = startIds.Count AndAlso startIds.Count > 1 AndAlso
               ctrN(1) > 0 AndAlso ctrN(2) > 0 Then
                Dim mine = If(side = 1, ctr(1), ctr(2))
                Dim theirs = If(side = 1, ctr(2), ctr(1))
                Dim near As New List(Of Integer)
                For Each id In pts
                    Dim q As New Vector2(nodes(id).x, nodes(id).z)
                    If (q - mine).LengthSquared < (q - theirs).LengthSquared Then
                        near.Add(id)
                    End If
                Next
                ' Only if it actually split them. Every start equidistant
                ' leaves this alone rather than emptying the list.
                If near.Count > 0 AndAlso near.Count < pts.Count Then
                    pts = near
                    LogThis("tank sim: side {0} has no start_team - fell back " &
                            "to the {1} start(s) at its own base", side, near.Count)
                End If
            End If

            ' LEFT TO LEFT. Both sorted on world X, so rank matches rank.
            hulls.Sort(Function(p, q) p.position.X.CompareTo(q.position.X))
            pts.Sort(Function(p, q) nodes(p).x.CompareTo(nodes(q).x))

            For k = 0 To hulls.Count - 1
                Dim idx = CInt(Math.Floor(CDbl(k) * pts.Count / hulls.Count))
                If idx >= pts.Count Then idx = pts.Count - 1
                assigned(hulls(k)) = pts(idx)
            Next
        Next
        LogThis("tank sim: {0} hull(s) attached to start points, left to left",
                assigned.Count)
    End Sub

    Private ReadOnly assigned As New Dictionary(Of TankInstance, Integer)

    ''' <summary>Forget every assignment, so the next frame re-rolls.</summary>
    Public Sub Reroll()
        hullRun.Clear()
        atOf.Clear()
        hullRunNodeIds.Clear()
        hullRunNodeAt.Clear()
        assigned.Clear()
        hitsOf.Clear()
        hitDistOf.Clear()
        hitFrame.Clear()
        debugTankId = -1
        debugTankDumped = False
        debugTankLastNodeId = -1
    End Sub

    ''' <summary>
    ''' The sixteen rays from one hull, as world XZ pairs.
    '''
    ''' Origins are ON the hull edge and directions point OUTWARD - four at the
    ''' corners on the diagonal, three along each side on the side's normal.
    ''' That is the sixteen: 4 + 3*4. A ray that started at the centre would
    ''' spend its first two metres inside the tank it belongs to.
    ''' </summary>
    Public Function HullRays(inst As TankInstance) As List(Of ValueTuple(Of Vector2, Vector2))
        Dim outp As New List(Of ValueTuple(Of Vector2, Vector2))(RAY_COUNT)
        If inst Is Nothing Then Return outp

        Dim hx = TankDriveTune.HULL_R * 0.5F
        Dim hz = TankDriveTune.HULL_R * 0.5F

        ' THE HULL'S BOX, NOT THE VEHICLE'S. A TankVehicle is a list of parts
        ' and each carries its own visual, so "the bounding box" has to say
        ' which. The gun is the reason: a 7 m barrel would push the forward
        ' face out past the muzzle and every front ray would start in mid-air
        ' ahead of the tank. hull first, chassis as the fallback.
        Dim vis As TankVisual = Nothing
        If inst.vehicle IsNot Nothing AndAlso inst.vehicle.parts IsNot Nothing Then
            For Each want In {"hull", "chassis"}
                For Each pt In inst.vehicle.parts
                    If pt.label = want AndAlso pt.visual IsNot Nothing Then
                        vis = pt.visual
                        Exit For
                    End If
                Next
                If vis IsNot Nothing Then Exit For
            Next
        End If
        If vis IsNot Nothing Then
            hx = Math.Max(0.5F, (vis.bbMax.X - vis.bbMin.X) * 0.5F)
            hz = Math.Max(0.5F, (vis.bbMax.Z - vis.bbMin.Z) * 0.5F)
        End If

        Dim ca = CSng(Math.Cos(inst.headingRad))
        Dim sa = CSng(Math.Sin(inst.headingRad))
        Dim toWorld = Function(lx As Single, lz As Single) _
            New Vector2(inst.position.X + lx * ca + lz * sa,
                        inst.position.Z - lx * sa + lz * ca)
        Dim dirWorld = Function(lx As Single, lz As Single) _
            Vector2.Normalize(New Vector2(lx * ca + lz * sa, -lx * sa + lz * ca))

        ' IN THE ORDER THE CONSTANTS NAME. Three down each side was sixteen
        ' and the owner's count is eight, which is also half the geometry at a
        ' point where the frame rate matters - the middle of a side is where a
        ' neighbour alongside shows up, and the extra thirds either side of it
        ' were seeing the same tank twice.
        outp.Add((toWorld(-hx, hz), dirWorld(-1.0F, 1.0F)))    ' R_FL
        outp.Add((toWorld(hx, hz), dirWorld(1.0F, 1.0F)))      ' R_FR
        outp.Add((toWorld(-hx, -hz), dirWorld(-1.0F, -1.0F)))  ' R_RL
        outp.Add((toWorld(hx, -hz), dirWorld(1.0F, -1.0F)))    ' R_RR
        outp.Add((toWorld(0.0F, hz), dirWorld(0.0F, 1.0F)))    ' R_FRONT
        outp.Add((toWorld(0.0F, -hz), dirWorld(0.0F, -1.0F)))  ' R_REAR
        outp.Add((toWorld(hx, 0.0F), dirWorld(1.0F, 0.0F)))    ' R_RIGHT
        outp.Add((toWorld(-hx, 0.0F), dirWorld(-1.0F, 0.0F)))  ' R_LEFT
        Return outp
    End Function

End Module

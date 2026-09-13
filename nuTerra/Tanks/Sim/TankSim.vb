Imports System.IO
Imports System.Text.Json
Imports OpenTK.Mathematics

''' <summary>
''' The SIM: drive every tank to a start point, then along the path that leaves
''' it, and see where each hull thinks its neighbours are.
'''
''' EVERY METRE OF THIS COMES FROM RAY STUDIO. The graph is read from
''' &lt;map&gt;_paths.json - the editor's own PathEdit.to_dict - and the runs are
''' walked from its nodes and edges. The route catalogue in TankRoutes is NOT
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

    ''' <summary>How far a hull looks for its neighbours.</summary>
    Public SIM_RAY_M As Single = 3.0F

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

    ''' <summary>How close counts as reaching a waypoint. Looser than the
    ''' drive's own ARRIVE_M so a hull that stops just short still advances -
    ''' a run that stalls one metry short of a waypoint never finishes, and
    ''' looks exactly like a hull that has lost its path.</summary>
    Public Const WAYPOINT_M As Single = 8.0F

    Public Structure SimNode
        Public x As Single
        Public z As Single
        Public team As Integer      ' bitmask: 1, 2, or 3 for both
        Public isStart As Boolean
        Public msg As String
        Public spd As String
    End Structure

    Private ReadOnly nodes As New Dictionary(Of Integer, SimNode)
    Private ReadOnly adj As New Dictionary(Of Integer, List(Of Integer))
    Private ReadOnly startIds As New List(Of Integer)

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
        hullRun.Clear()
        atOf.Clear()

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
                    sn.msg = If(n.TryGetProperty("msg", e), e.GetString(), "")
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
        startsMsg = String.Format("{0} verts, {1} lines, {2} start(s)",
                                  nodes.Count, edgeCount \ 2, startIds.Count)
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
        LogThis("tank sim: {0} from {1}_paths.json, {2} - Ray Studio's graph, not the route catalogue",
                startsMsg, mapName, pathsStamp)
        Return startIds.Count
    End Function

    ''' <summary>
    ''' The run leaving a start point, walked to the first fork or dead end.
    '''
    ''' STOPS AT A FORK rather than guessing. A fork is a real choice and the
    ''' editor marked it as one; picking a branch here would be this file
    ''' inventing a route, which is the thing the whole arrangement exists to
    ''' avoid. Whatever chooses at forks later gets to choose properly.
    ''' </summary>
    Public Function RunFrom(startId As Integer) As List(Of Vector2)
        Dim outp As New List(Of Vector2)
        If Not nodes.ContainsKey(startId) Then Return outp
        Dim seen As New HashSet(Of Integer)
        Dim cur = startId, prev = -1
        For guard = 0 To 4000
            If Not nodes.ContainsKey(cur) OrElse seen.Contains(cur) Then Exit For
            seen.Add(cur)
            outp.Add(New Vector2(nodes(cur).x, nodes(cur).z))
            Dim nxt = -1, ways = 0
            For Each n In adj(cur)
                If n = prev Then Continue For
                ways += 1
                If nxt < 0 Then nxt = n
            Next
            ' One way on is a road; none is the end; more than one is a fork.
            If ways <> 1 Then Exit For
            prev = cur
            cur = nxt
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
            Dim bit = If(inst.team = TankTeam.Green, 1, 2)
            Dim mine As New List(Of Integer)
            For Each id In startIds
                If (nodes(id).team And bit) <> 0 Then mine.Add(id)
            Next
            If mine.Count = 0 Then mine.AddRange(startIds)
            Dim pick = mine(rng.Next(mine.Count))
            run = RunFrom(pick)
            hullRun(inst) = run
            atOf(inst) = 0
        End If
        If run.Count = 0 Then Return New Vector2(inst.position.X, inst.position.Z)

        Dim i = atOf(inst)
        Dim here As New Vector2(inst.position.X, inst.position.Z)
        ' ADVANCE ON ARRIVAL, one waypoint a frame at most. Skipping ahead to
        ' the furthest reached point would let a hull cut a corner it never
        ' drove, and the path drawn behind it would be a lie.
        If i < run.Count - 1 AndAlso (run(i) - here).Length < WAYPOINT_M Then
            atOf(inst) = i + 1
            i += 1
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
        Dim rays = HullRays(inst)
        If others IsNot Nothing Then
            For i = 0 To Math.Min(RAY_COUNT, rays.Count) - 1
                Dim o = rays(i).Item1, d = rays(i).Item2
                For Each t In others
                    If t Is inst OrElse t Is Nothing Then Continue For
                    ' Closest approach of the segment to the other hull's centre.
                    Dim cx = t.position.X - o.X
                    Dim cz = t.position.Z - o.Y
                    Dim along = cx * d.X + cz * d.Y
                    If along < 0.0F Then along = 0.0F
                    If along > SIM_RAY_M Then along = SIM_RAY_M
                    Dim px = o.X + d.X * along - t.position.X
                    Dim pz = o.Y + d.Y * along - t.position.Z
                    If px * px + pz * pz <= OTHER_R * OTHER_R Then
                        hits(i) = True
                        Exit For
                    End If
                Next
            Next
        End If
        hitsOf(inst) = hits
        hitFrame(inst) = frame
        Return hits
    End Function

    ''' <summary>Is anything in front of this hull, by the rays that look
    ''' forward. This is what the go-around asks.</summary>
    Public Function BlockedAhead(inst As TankInstance,
                                 others As List(Of TankInstance)) As Boolean
        Dim h = RayHits(inst, others)
        For Each i In FORWARD_RAYS
            If h(i) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Is the ground this hull would swing into already taken - the
    ''' right side, by the rays that look that way.</summary>
    Public Function RightIsClear(inst As TankInstance,
                                 others As List(Of TankInstance)) As Boolean
        Dim h = RayHits(inst, others)
        Return Not (h(R_RIGHT) OrElse h(R_FR))
    End Function

    ''' <summary>Forget every assignment, so the next frame re-rolls.</summary>
    Public Sub Reroll()
        hullRun.Clear()
        atOf.Clear()
        hitsOf.Clear()
        hitFrame.Clear()
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

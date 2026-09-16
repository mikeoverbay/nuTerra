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
    ''' The in-zone round every path point: one metre, the owner's number.
    '''
    ''' "each point has a 1m radius in zone" - a point is reached when the hull
    ''' is inside it, and seeking then looks for the next one.
    '''
    ''' ONE METRE IS TIGHT FOR A 4.5 m HULL and that is the point: a loose zone
    ''' lets a tank count a waypoint as reached from across the road, which is
    ''' how a hull ends up cutting the corner it was supposed to drive round.
    ''' It was eight metres, which is most of a hull length either side.
    '''
    ''' THE PASS-BY RULE IS WHAT KEEPS IT FROM STALLING. A hull that misses the
    ''' metre still advances once the point is BEHIND it - measured against the
    ''' leg it is driving, not against the hull's nose - so a near miss costs
    ''' nothing and the run cannot wedge on a point it can never quite touch.
    ''' Without that, one metre would be a trap rather than a tolerance.
    ''' </summary>
    Public Const WAYPOINT_M As Single = 1.0F

    ''' <summary>How straight the road has to stay for the look-ahead to keep
    ''' extending. cos(35 degrees) - past that it is a corner, and aiming
    ''' beyond a corner is aiming off the road.</summary>
    Public Const BEND_COS As Single = 0.819F

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
        lines.Clear()
        startPts.Clear()
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
        BuildDrawLists()
        LogThis("tank sim: {0} from {1}_paths.json, {2} - Ray Studio's graph, not the route catalogue",
                startsMsg, mapName, pathsStamp)
        Return startIds.Count
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
        Dim outp As New List(Of Vector2)
        If Not nodes.ContainsKey(startId) Then Return outp
        Dim seen As New HashSet(Of Integer)
        Dim cur = startId, prev = -1, step_ = 0
        For guard = 0 To 4000
            If Not nodes.ContainsKey(cur) OrElse seen.Contains(cur) Then Exit For
            seen.Add(cur)
            outp.Add(New Vector2(nodes(cur).x, nodes(cur).z))
            Dim ways As New List(Of Integer)
            For Each n In adj(cur)
                If n <> prev AndAlso Not seen.Contains(n) Then ways.Add(n)
            Next
            If ways.Count = 0 Then Exit For
            ' Sorted, so the choice depends on the graph and not on the order a
            ' dictionary happened to hand back its neighbours.
            ways.Sort()
            Dim take = 0
            If ways.Count > 1 Then
                take = Math.Abs(pick + step_ * 7) Mod ways.Count
                step_ += 1
            End If
            prev = cur
            cur = ways(take)
        Next
        Return Subdivide(outp)
    End Function

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

    Private Function Subdivide(pts As List(Of Vector2)) As List(Of Vector2)
        If pts.Count < 2 Then Return pts
        Dim outp As New List(Of Vector2)(pts.Count * 2)
        outp.Add(pts(0))
        For i = 0 To pts.Count - 2
            Dim a = pts(i), b = pts(i + 1)
            Dim d = (b - a).Length
            Dim steps = CInt(Math.Floor(d / STEP_M))
            For k = 1 To steps
                Dim f = CSng(k) / CSng(steps + 1)
                outp.Add(New Vector2(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f))
            Next
            outp.Add(b)
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
            ' The hull's own hand at every fork - its id, so two tanks on one
            ' start fan out down different roads and do it the same way twice.
            run = RunFrom(chosen, inst.id * 3 + If(inst.team = TankTeam.Green, 0, 1))
            hullRun(inst) = run
            atOf(inst) = 0
            startOf(inst) = chosen
            ' WHICH PATH THIS HULL GOT, said once when it gets it. A tank that
            ' stops early is either on a short path or failing to follow a long
            ' one, and those need different fixes - this is the line that tells
            ' them apart.
            Dim L = 0.0F
            For q = 0 To run.Count - 2
                L += (run(q + 1) - run(q)).Length
            Next
            LogThis("tank sim: {0} takes start {1} at ({2:0}, {3:0}) - {4} point(s), {5:0} m",
                    inst.label, chosen, nodes(chosen).x, nodes(chosen).z, run.Count, L)
        End If
        If run.Count = 0 Then Return New Vector2(inst.position.X, inst.position.Z)

        Dim i = atOf(inst)
        Dim here As New Vector2(inst.position.X, inst.position.Z)
        ' ADVANCE ON ARRIVAL, one waypoint a frame at most. Skipping ahead to
        ' the furthest reached point would let a hull cut a corner it never
        ' drove, and the path drawn behind it would be a lie.
        If i < run.Count - 1 Then
            Dim reached = (run(i) - here).Length < WAYPOINT_M
            If Not reached Then
                ' PASSED IT. Project the hull onto the leg it is driving: once
                ' it is beyond the far end, the point is behind and holding the
                ' run on it would stall the hull forever a metre off target.
                Dim leg = run(i + 1) - run(i)
                Dim ll = leg.Length
                If ll > 0.001F Then
                    Dim t = ((here.X - run(i).X) * leg.X +
                             (here.Y - run(i).Y) * leg.Y) / (ll * ll)
                    reached = (t >= 0.0F)
                End If
            End If
            If reached Then
                atOf(inst) = i + 1
                i += 1
            End If
        End If

        ' AIM AT THE END OF THE FRONT RAY, EVERY TIME - the owner's rule.
        '
        ' The waypoints are twelve metres apart and the forward whisker reaches
        ' twenty, so steering at the next waypoint threw away most of what the
        ' hull can see and made it re-decide three times over ground it could
        ' have crossed in one go. Aiming as far down its own road as it can see
        ' gives a straighter line, fewer decisions, and a target that is
        ' already checked clear by the same ray.
        '
        ' IT STOPS AT A BEND. Extending blindly to twenty metres would cut any
        ' corner shorter than that - the hull would leave the road and rejoin
        ' it further on, which is the drawn path becoming a lie again. So the
        ' reach stops the moment the next leg turns more than BEND_COS off the
        ' first: straights get the full twenty, corners get the corner.
        Dim tgt = run(i)
        Dim reach = SIM_RAY_FRONT_M
        Dim gone = (tgt - here).Length
        Dim k = i
        While k < run.Count - 1 AndAlso gone < reach
            Dim leg = run(k + 1) - run(k)
            Dim legLen = leg.Length
            If legLen <= 0.001F Then
                k += 1
                Continue While
            End If
            If k > i Then
                Dim prev = run(k) - run(k - 1)
                Dim pl = prev.Length
                If pl > 0.001F Then
                    Dim dot = (leg.X * prev.X + leg.Y * prev.Y) / (legLen * pl)
                    If dot < BEND_COS Then Exit While
                End If
            End If
            If gone + legLen > reach Then Exit While
            gone += legLen
            k += 1
            tgt = run(k)
        End While
        Return tgt
    End Function

    ''' <summary>
    ''' Put this hull back on its own path at the NEAREST point, wherever a
    ''' manoeuvre left it.
    '''
    ''' RULE 5 - "a tank forced around another tank seeks ANY point on its path
    ''' line, not the original point." Re-aiming at the waypoint it was heading
    ''' for before the swerve sends it back round the obstacle it just cleared;
    ''' the road is a line, not a sequence of gates, and rejoining it anywhere
    ''' is rejoining it.
    '''
    ''' Never backwards past where it already is: the nearest point is taken
    ''' from the current index on, so a road that doubles back near the hull
    ''' cannot hand it a waypoint it drove through five minutes ago.
    ''' </summary>
    Public Sub Rejoin(inst As TankInstance)
        Dim run As List(Of Vector2) = Nothing
        If Not hullRun.TryGetValue(inst, run) OrElse run Is Nothing Then Return
        If run.Count = 0 Then Return
        Dim at = 0
        atOf.TryGetValue(inst, at)
        Dim here As New Vector2(inst.position.X, inst.position.Z)
        Dim best = at
        Dim bd = Single.MaxValue
        For i = at To run.Count - 1
            Dim d = (run(i) - here).LengthSquared
            If d < bd Then
                bd = d
                best = i
            End If
        Next
        atOf(inst) = best
    End Sub

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

    ''' <summary>Where each side's base is, and how big a ring counts as
    ''' standing on it. Set by the renderer at line-up; the sim has no other
    ''' way to know, and guessing from the paths would make arrival depend on
    ''' where a road happened to end.</summary>
    Public baseOf As New Dictionary(Of Integer, Vector2)
    Public Const BASE_RING_M As Single = 50.0F

    ''' <summary>
    ''' Has this hull won - is it standing inside the ENEMY base ring.
    '''
    ''' Rule 3: "inside a ring = a win for that tank. it does not have to make
    ''' it to the base position." Reaching the last waypoint is not the same
    ''' test and can be stricter or looser than it by tens of metres depending
    ''' where the road stopped.
    ''' </summary>
    Public Function InEnemyRing(inst As TankInstance) As Boolean
        Dim foe = If(inst.team = TankTeam.Green, 2, 1)
        Dim b As Vector2
        If Not baseOf.TryGetValue(foe, b) Then Return False
        Dim dx = inst.position.X - b.X
        Dim dz = inst.position.Z - b.Y
        Return dx * dx + dz * dz <= BASE_RING_M * BASE_RING_M
    End Function

    ''' <summary>Bumped once a frame by the renderer, so the rays are cast
    ''' once and both the driving and the drawing read the same answer. Two
    ''' separate casts would be twice the work AND could disagree, which
    ''' would draw a clear ray on a hull that had just swerved for it.</summary>
    Public frame As Integer = 0

    Private ReadOnly hitsOf As New Dictionary(Of TankInstance, Boolean())
    Private ReadOnly distOf As New Dictionary(Of TankInstance, Single())
    Private ReadOnly hitFrame As New Dictionary(Of TankInstance, Integer)

    ''' <summary>How long a hull needs to see something coming: its own length
    ''' plus a second of travel.
    '''
    ''' RULE 4 - "whiskers must not stop a tank following its path when it can
    ''' clear the distance ahead." The forward rays reach 20 m so the hull can
    ''' SEE that far; reacting at 20 m is a different decision and a wrong one,
    ''' because at 7 m/s a tank covers that in under three seconds and the thing
    ''' it swerved for was never in its way. Seeing far and reacting late is
    ''' the whole point of a long whisker.
    '''
    ''' Scales with speed, so a stopped tank tolerates a neighbour at four
    ''' metres and a moving one does not.</summary>
    Public Const REACT_S As Single = 1.0F

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
        ' Distances are filled by the same pass; RayDist reads them after.

        Dim hits(RAY_COUNT - 1) As Boolean
        Dim dist(RAY_COUNT - 1) As Single
        For i = 0 To RAY_COUNT - 1
            dist(i) = Single.MaxValue
        Next
        Dim rays = HullRays(inst)
        If others IsNot Nothing Then
            For i = 0 To Math.Min(RAY_COUNT, rays.Count) - 1
                Dim o = rays(i).Item1, d = rays(i).Item2
                Dim reach = RayLen(i)
                For Each t In others
                    If t Is inst OrElse t Is Nothing Then Continue For
                    ' Closest approach of the segment to the other hull's centre.
                    Dim cx = t.position.X - o.X
                    Dim cz = t.position.Z - o.Y
                    Dim along = cx * d.X + cz * d.Y
                    If along < 0.0F Then along = 0.0F
                    If along > reach Then along = reach
                    Dim px = o.X + d.X * along - t.position.X
                    Dim pz = o.Y + d.Y * along - t.position.Z
                    If px * px + pz * pz <= OTHER_R * OTHER_R Then
                        hits(i) = True
                        ' HOW FAR, not just whether. The distance was computed
                        ' and thrown away, and it is the whole of rule 4 - a
                        ' hit at nineteen metres and one at four are different
                        ' facts and were being treated as the same one.
                        If along < dist(i) Then dist(i) = along
                    End If
                Next
            Next
        End If
        hitsOf(inst) = hits
        distOf(inst) = dist
        hitFrame(inst) = frame
        Return hits
    End Function

    ''' <summary>How far down each ray the nearest hull is, MaxValue for a ray
    ''' that hits nothing. Same frame, same pass as RayHits.</summary>
    Public Function RayDist(inst As TankInstance,
                            others As List(Of TankInstance)) As Single()
        RayHits(inst, others)
        Dim d As Single() = Nothing
        If distOf.TryGetValue(inst, d) Then Return d
        Dim empty(RAY_COUNT - 1) As Single
        For i = 0 To RAY_COUNT - 1
            empty(i) = Single.MaxValue
        Next
        Return empty
    End Function

    ''' <summary>
    ''' Is anything CLOSE ENOUGH in front to be worth turning for.
    '''
    ''' Rule 4. Not "is anything ahead" - that fired at the full twenty metres
    ''' and made a hull swerve for a tank it would have driven past before
    ''' reaching. The bar is the room this hull needs at the speed it is doing:
    ''' its own half-length plus a second of travel.
    ''' </summary>
    Public Function BlockedAhead(inst As TankInstance,
                                 others As List(Of TankInstance),
                                 Optional speed As Single = 0.0F) As Boolean
        Dim d = RayDist(inst, others)
        Dim need = TankDriveTune.HULL_R * 0.5F + Math.Abs(speed) * REACT_S
        For Each i In FORWARD_RAYS
            If d(i) <= need Then Return True
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
                             ByRef target As Vector2) As Boolean
        Dim h = RayHits(inst, others)
        Dim rays = HullRays(inst)
        For Each i In WAY_OUT
            If i >= rays.Count OrElse h(i) Then Continue For
            Dim o = rays(i).Item1, d = rays(i).Item2
            target = o + d * RayLen(i)
            Return True
        Next
        Return False
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
        startOf.Clear()
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

    ''' <summary>Which start each hull actually took, for the log.</summary>
    Private ReadOnly startOf As New Dictionary(Of TankInstance, Integer)

    ''' <summary>The start a hull began on, or -1. Read by the drive when it
    ''' reports an arrival.</summary>
    Public Function StartIdOf(inst As TankInstance) As Integer
        Dim i = -1
        startOf.TryGetValue(inst, i)
        Return i
    End Function

    ''' <summary>How many points of its run this hull has passed, and how many
    ''' there are.</summary>
    Public Function Progress(inst As TankInstance) As ValueTuple(Of Integer, Integer)
        Dim at = 0
        atOf.TryGetValue(inst, at)
        Dim r As List(Of Vector2) = Nothing
        If Not hullRun.TryGetValue(inst, r) OrElse r Is Nothing Then Return (at, 0)
        Return (at, r.Count)
    End Function

    ''' <summary>Forget every assignment, so the next frame re-rolls.</summary>
    Public Sub Reroll()
        hullRun.Clear()
        atOf.Clear()
        assigned.Clear()
        startOf.Clear()
        hitsOf.Clear()
        distOf.Clear()
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

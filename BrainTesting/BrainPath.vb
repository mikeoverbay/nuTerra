Imports System.Text.Json
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' FOLLOWING A ROAD THAT WAS WORKED OUT IN ADVANCE - ONE PER HULL.
'''
''' "lets start the pre programed path following from Path Studio" - the
''' owner, 2026-09-25. Then, once there were five a side: "we need to assign a
''' starting point for each tank to seek ... spread them out so left goes left
''' and right goes right."
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
''' ONE LANE PER HULL, NOT ONE ROAD FOR THE APP. Every hull that loads gets
''' its own road, its own position along it and its own finished flag. Only
''' the DRIVEN hull writes BrainGoal - there is one goal and one brain, so a
''' second lane writing it would be the two-drivers bug again in a new place -
''' but every lane advances against its own hull's position, so the day there
''' are five brains this module already has five answers ready and needs no
''' second pass. See Tick.
'''
''' WHICH ROAD EACH HULL GETS: see assign_team. Short version - both the hulls
''' and the roads are sorted along the SAME across-the-map axis and paired in
''' order, so the tank on one flank draws the road that runs up that flank.
'''
''' WRITTEN DIRECTLY, not through BrainGoal.TryMove, for the reason
''' PlaceAtLookAt and the goal marker both give: TryMove refuses while Pinned,
''' and `pingoal` is exactly the flag a measured run sets. The pin exists to
''' stop a BRAIN throwing a fresh goal on arrival, not to stop the run being
''' driven.
''' </summary>
Module BrainPath

    Public ON_ As Boolean = False
    ''' <summary>Force the DRIVEN hull onto this road, whatever the spread
    ''' would have given it. -1 lets every hull take its place in the fan.
    ''' It pins ONE hull rather than the whole team because that is what it is
    ''' for: running the same road twice and comparing the two.</summary>
    Public WantId As Integer = -1

    ''' <summary>
    ''' ONE HULL'S ROAD, AND HOW FAR DOWN IT THAT HULL HAS GOT.
    '''
    ''' A class rather than a Structure ON PURPOSE. These are held in an array
    ''' and mutated in place every tick; a Structure would hand out copies and
    ''' `ln.at += 1` would advance a temporary that is thrown away - exactly
    ''' the trap BrainPanel.SetSpawn documents for BrainTanks.Body, which IS a
    ''' Structure and has to be written back whole.
    ''' </summary>
    Private Class Lane
        Public hull As Integer
        Public team As Integer
        Public roadId As Integer = -1
        Public ReadOnly pts As New List(Of Vector2)
        Public at As Integer = 0
        Public ready As Boolean = False
        ''' <summary>The road is done and, for the driven hull, the goal has
        ''' been cleared. Latched, so arrival fires once rather than every
        ''' tick - and CLEARED BY Reset, because a latch nobody unlatches is a
        ''' tank that never drives again.</summary>
        Public finished As Boolean = False
        ''' <summary>Mean offset of the road from the base along the across
        ''' axis, metres. Kept only so the assignment can be read back in one
        ''' line - it is what "this one runs up the left" means numerically.</summary>
        Public across As Single
        ''' <summary>How far the ROAD ITSELF stopped short of the enemy base,
        ''' before the base was appended. The number that explains why a tank
        ''' following a flank road used to finish nowhere near it.</summary>
        Public tail As Single
        ''' <summary>Corner cuts the map refused on the last Chaikin pass, and
        ''' the longest gap between consecutive waypoints. Together they say
        ''' how far ahead the goal can sit: a refused run leaves a straight
        ''' the subdivision never broke up.</summary>
        Public refused As Integer
        Public longest As Single
        ''' <summary>Metres driven since the waypoint index last moved, where
        ''' it was measured from, and whether the stall has been reported. A
        ''' follower that has stopped following is the failure this whole
        ''' module can have, and it used to be invisible.</summary>
        Public stalled As Single
        Public was As Vector2
        Public said As Boolean
    End Class

    ''' <summary>Indexed by hull, Nothing where a hull got no road.</summary>
    Private lanes() As Lane = Nothing

    Private shader As BrainShader
    Private vao, vbo, verts, mineVerts As Integer

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    ''' <summary>The driven hull's lane, or Nothing. The one lane allowed to
    ''' touch the goal.</summary>
    Private Function mine() As Lane
        If lanes Is Nothing Then Return Nothing
        Dim i = BrainRadar.HULL
        If i < 0 OrElse i >= lanes.Length Then Return Nothing
        Return lanes(i)
    End Function

    Public Sub Load(map As String)
        If Not ON_ Then Return
        lanes = Nothing
        verts = 0
        mineVerts = 0
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
            If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then
                LogThis("brain: path graph read, but there are no hulls to give roads to")
                Return
            End If

            ReDim lanes(BrainTanks.Bodies.Count - 1)
            ' FORM UP FIRST. Every position below is measured from where the
            ' hulls ARE, so the one hull this app moves has to be moved before
            ' anything reads it - see form_up.
            form_up()
            assign_team(1, xy, roads)
            assign_team(2, xy, roads)
            aim_driven_hull()
            report()
        Catch ex As Exception
            LogThis("brain: path graph would not load - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' GIVE ONE TEAM ITS ROADS, FANNED OUT ACROSS THE MAP.
    '''
    ''' "we need spread them out so left goes left and right goes right" - the
    ''' owner, 2026-09-25. Five tanks all handed the road nearest the base is
    ''' five tanks in one queue down one lane, which tests the queue and not
    ''' the map.
    '''
    ''' THE RULE IS A SORT, AND THAT IS WHY IT IS RIGHT. Both the hulls and
    ''' the roads are measured along the SAME signed across-the-map axis, both
    ''' lists are sorted by it, and they are paired in order. Whichever
    ''' direction that axis happens to point, the hull at one extreme of the
    ''' starting row gets the road at the same extreme of the fan - so "left
    ''' goes left" holds without this code having to know which way the camera
    ''' is facing or which handedness the map uses. Naming it `across` rather
    ''' than `left` is deliberate: the pairing is the guarantee, a claim about
    ''' which side is port would be one more thing to get wrong.
    '''
    ''' BY THE MEAN OFFSET OVER THE WHOLE ROAD, not by where it starts. All 32
    ''' roads run base to base, so all 32 START in much the same place - their
    ''' start points are clustered within a few tens of metres and sorting by
    ''' those would be sorting noise. What makes a road the left-hand road is
    ''' where it GOES, and the mean over its nodes says that in one number
    ''' without letting a single outlying node decide it.
    '''
    ''' EVENLY SPACED THROUGH THE SORTED ROADS, not the first N. Thirty-two
    ''' roads and five hulls: taking roads 0 to 4 would fan them across the
    ''' leftmost eighth of the map. Stepping (m-1)/(n-1) apart spends the
    ''' whole fan, and because that step is at least 1 whenever there are at
    ''' least as many roads as hulls, no two hulls can be handed the same one.
    ''' </summary>
    Private Sub assign_team(team As Integer,
                            xy As Dictionary(Of Integer, Vector2),
                            roads As List(Of Tuple(Of Integer, List(Of Integer))))
        Dim hulls As New List(Of Tuple(Of Integer, Single))
        Dim here = If(team = 2, BrainTanks.Base2, BrainTanks.Base1)
        Dim away = If(team = 2, BrainTanks.Base1, BrainTanks.Base2)
        Dim ax = across_axis(here, away)

        For i = 0 To BrainTanks.Bodies.Count - 1
            If BrainTanks.Bodies(i).team <> team Then Continue For
            hulls.Add(Tuple.Create(i, Vector2.Dot(BrainTanks.Bodies(i).spawn - here, ax)))
        Next
        If hulls.Count = 0 Then Return
        hulls.Sort(Function(a, b) a.Item2.CompareTo(b.Item2))

        ' EVERY ROAD, ORDERED FROM THIS TEAM'S BASE.
        '
        ' A road is stored base1 to base2, so team 2 drives every one of them
        ' backwards. Reversing here, once, is why nothing downstream - the
        ' smoother, the waypoint walk, the base zone - needs to know which
        ' team it is working for.
        Dim cand As New List(Of Tuple(Of Integer, List(Of Vector2), Single))
        For Each r In roads
            If r.Item2.Count < 2 Then Continue For
            Dim all As New List(Of Vector2)
            For Each id In r.Item2
                Dim q As Vector2 = Nothing
                If xy.TryGetValue(id, q) Then all.Add(q)
            Next
            If all.Count < 2 Then Continue For
            ' BY THE ENDS, not by any node. A road that passes close to the
            ' base halfway along is not a road that STARTS there.
            If (all(all.Count - 1) - here).Length < (all(0) - here).Length Then all.Reverse()
            Dim sum = 0.0F
            For Each q In all
                sum += Vector2.Dot(q - here, ax)
            Next
            cand.Add(Tuple.Create(r.Item1, all, sum / all.Count))
        Next
        If cand.Count = 0 Then Return
        cand.Sort(Function(a, b) a.Item3.CompareTo(b.Item3))

        Dim n = hulls.Count, m = cand.Count
        If m < n Then
            ' SAID OUT LOUD. Two hulls on one road is two tanks driving the
            ' same line, and a run that looks like a duplicate rather than a
            ' shortage is a wrong conclusion waiting to be drawn.
            LogThis("brain: team {0} has {1} hull(s) and only {2} road(s) - some will share",
                    team, n, m)
        End If

        For k = 0 To n - 1
            Dim j As Integer
            If n = 1 Then
                j = m \ 2                      ' one tank takes the middle road
            Else
                j = CInt(Math.Round(k * (m - 1) / (n - 1.0)))
            End If
            j = Math.Max(0, Math.Min(m - 1, j))
            build_lane(hulls(k).Item1, team, cand(j))
        Next

        ' AND THE PIN, LAST, so it beats the fan rather than shifting it.
        If WantId >= 0 AndAlso BrainRadar.HULL >= 0 AndAlso
           BrainRadar.HULL < BrainTanks.Bodies.Count AndAlso
           BrainTanks.Bodies(BrainRadar.HULL).team = team Then
            For Each c In cand
                If c.Item1 <> WantId Then Continue For
                build_lane(BrainRadar.HULL, team, c)
                LogThis("brain: hull {0} pinned to road {1} by path=", BrainRadar.HULL, WantId)
                Exit For
            Next
        End If
    End Sub

    ''' <summary>
    ''' The unit axis ACROSS the map for a team - perpendicular to its own
    ''' base-to-base line.
    '''
    ''' Derived rather than hard-coded to world X because the bases are not
    ''' always north-south. On 19_monastery they very nearly are - base1 at
    ''' z -387.8, base2 at z +397.4 - so across IS X there and the spread
    ''' looks like the obvious thing; on a map with the bases corner to corner
    ''' a hard-coded X would fan the tanks along the direction of travel
    ''' instead of across it, and every one of them would be handed a road
    ''' that starts behind or in front rather than beside.
    '''
    ''' Falls back to world X when the two bases coincide or are missing,
    ''' which is what HasBases = False leaves behind: a degenerate axis would
    ''' make every offset zero and the sort meaningless, and an arbitrary but
    ''' consistent axis still pairs left with left.
    ''' </summary>
    Private Function across_axis(here As Vector2, away As Vector2) As Vector2
        Dim fwd = away - here
        If fwd.Length < 0.001F Then Return New Vector2(1.0F, 0.0F)
        fwd = Vector2.Normalize(fwd)
        Return New Vector2(-fwd.Y, fwd.X)
    End Function

    ''' <summary>
    ''' Build one hull's lane, and MAKE IT END AT THE ENEMY BASE.
    '''
    ''' "this tank is trying to get back to base at the end and not to the
    ''' enemy's base" - the owner, 2026-09-25.
    '''
    ''' The path graph calls its 32 roads base-to-base and they are not.
    ''' Measured on 19_monastery, every one of them stops short at both ends,
    ''' and the fan makes it worse the further out it reaches:
    '''
    '''   road  6   12 m from base 1  ->   17 m from base 2
    '''   road  8   87 m              ->   67 m
    '''   road 28  247 m              ->  226 m
    '''   road 16  407 m              ->  386 m
    '''
    ''' They are parallel corridors running the length of the map on a 40 m
    ''' pitch, so a flank road begins and ends out at the flank - beside the
    ''' bases, not at them. Nothing was wrong with the following; the road
    ''' simply never went where the run was supposed to end, and a tank that
    ''' drives to the last waypoint of road 17 finishes 252 m to the side of
    ''' the enemy base with its own base behind it. From the outside that is a
    ''' tank that did not go to the enemy base.
    '''
    ''' SO THE BASE IS APPENDED, and the arrival test in Tick then means what
    ''' it says: the last waypoint IS the base, so "inside the 25 m base zone"
    ''' stops being a claim about a waypoint that happens to be near one.
    '''
    ''' THE CLOSING LEG IS THE OPENING LEG, and that symmetry is the argument
    ''' for doing it this way rather than picking roads that reach further.
    ''' These hulls already cross 64 to 379 m of open ground to REACH their
    ''' road start - that is the design, it is the brain navigating to a point
    ''' with no road to help - so crossing the last stretch the same way is
    ''' nothing new. The alternative, only ever handing out the roads that
    ''' come closest to the bases, would collapse the fan back onto the middle
    ''' few corridors and undo the spread this was built for.
    '''
    ''' The leg goes in BEFORE smoothing, so it is shortcut and corner-cut and
    ''' collision-checked exactly like the rest of the road rather than bolted
    ''' on as a straight line nobody tested.
    ''' </summary>
    Private Sub build_lane(hull As Integer, team As Integer,
                           c As Tuple(Of Integer, List(Of Vector2), Single))
        Dim ln As New Lane With {.hull = hull, .team = team,
                                 .roadId = c.Item1, .across = c.Item3}
        ln.pts.AddRange(c.Item2)

        ' THE ENEMY BASE, as the last waypoint, when the road does not already
        ' finish inside the zone. Same zone Tick arrives on, read once here so
        ' the two cannot drift apart.
        Dim enemy = If(team = 2, BrainTanks.Base1, BrainTanks.Base2)
        Dim zone = BrainTune.Get_("basem", 25.0F)
        If ln.pts.Count > 0 Then
            ln.tail = (ln.pts(ln.pts.Count - 1) - enemy).Length
            If ln.tail > zone Then ln.pts.Add(enemy)
        End If

        ' SMOOTHED BEFORE ANYTHING READS IT, and after the order is final -
        ' shortcutting a road stored the other way round would pull the
        ' string in the direction it is not driven.
        smooth_road(ln)
        ln.ready = ln.pts.Count >= 2
        lanes(hull) = ln
    End Sub

    ''' <summary>
    ''' PUT THE DRIVEN HULL WHERE ITS TEAM IS, BEFORE ANYTHING MEASURES IT.
    '''
    ''' ONE TANK: on the base marker. "we start at base and seek start first" -
    ''' the owner, 2026-09-25 - and with a single hull that is exactly it. It
    ''' stands on the base, pointed at the first waypoint, and driving to that
    ''' waypoint is the brain's problem like any other. This is what `path` has
    ''' always done.
    '''
    ''' FIVE TANKS: into its slot in the starting ROW. The roster parks them
    ''' five abreast fourteen metres behind the marker, and that row IS the
    ''' starting grid - it is the thing the left-to-right assignment is read
    ''' off. Moving one of the five onto the marker would put it alone out in
    ''' front and give it the middle of the row's across-offset while it holds
    ''' a flank road, so the picture and the assignment would stop agreeing.
    '''
    ''' AND IT OVERRIDES A SAVED START, OUT LOUD. That is not new - `path` has
    ''' always overridden it, silently, by moving the hull to the base after
    ''' BrainStart.LoadSaved had placed it. What IS new is saying so, and the
    ''' first five-a-side run is why: a marker saved from some earlier
    ''' single-tank test put hull 0 at (-698.7, 597.4), 1,071 m from the start
    ''' of its own road and a map away from the other four, and it spent the
    ''' run wedging and backing in a corner. The old silence is what made that
    ''' look like a brain fault instead of a stale file.
    '''
    ''' A saved start is a ONE-TANK setup control. With a row there is nothing
    ''' for it to mean, because the formation is the scenario.
    '''
    ''' Through BrainStart.MoveTo, so the marker, the live hull and the
    ''' remembered origin all move together - see BrainPanel.SetSpawn on why
    ''' writing only one of the three is a bug that shows up one Reset later.
    ''' </summary>
    Private Sub form_up()
        Dim i = BrainRadar.HULL
        If BrainTanks.Bodies Is Nothing OrElse i < 0 OrElse
           i >= BrainTanks.Bodies.Count Then Return

        ' The slot number is the hull's POSITION WITHIN ITS TEAM, counted the
        ' same way BeginLoad hands them out - team 1 first, in id order - so it
        ' is the same k SpawnOf was called with when the roster was built.
        Dim team = BrainTanks.Bodies(i).team
        Dim k = 0, mates = 0
        For j = 0 To BrainTanks.Bodies.Count - 1
            If BrainTanks.Bodies(j).team <> team Then Continue For
            If j < i Then k += 1
            mates += 1
        Next

        Dim here = If(team = 2, BrainTanks.Base2, BrainTanks.Base1)
        Dim want = If(mates = 1, here, BrainTanks.SpawnOf(team, k))
        Dim was = BrainTanks.Bodies(i).spawn
        If (want - was).Length < 0.5F Then Return

        BrainStart.MoveTo(want)
        LogThis("brain: hull {0} moved from ({1:0.0}, {2:0.0}) to ({3:0.0}, {4:0.0}) - " &
                "{5}. Any saved start marker is overridden while a road is being followed.",
                i, was.X, was.Y, want.X, want.Y,
                If(mates = 1, "the team 1 base",
                              "slot " & k & " of the " & mates & "-abreast row"))
    End Sub

    ''' <summary>
    ''' Point the driven hull at the first waypoint of the road it drew.
    '''
    ''' AFTER the assignment, because until then there is no road to face, and
    ''' a start heading is the one thing a run cannot pick up later: the board
    ''' reads the hull's heading on tick 0 and a tank facing away spends its
    ''' opening seconds turning round in front of its own team.
    '''
    ''' Through BrainStart.PointTo, which routes to BrainPanel.SetHeading, so
    ''' the remembered origin turns with it and a Reset does not quietly undo
    ''' the heading while keeping the position.
    ''' </summary>
    Private Sub aim_driven_hull()
        Dim ln = mine()
        If ln Is Nothing OrElse Not ln.ready Then Return
        Dim i = BrainRadar.HULL
        If i < 0 OrElse i >= BrainTanks.Bodies.Count Then Return
        Dim toFirst = ln.pts(0) - BrainTanks.Bodies(i).spawn
        If toFirst.Length > 0.5F Then
            BrainStart.PointTo(CSng(Math.Atan2(toFirst.X, toFirst.Y)))
        End If
    End Sub

    ''' <summary>
    ''' THE WHOLE ASSIGNMENT IN ONE LINE PER TEAM.
    '''
    ''' A line per lane would be ten at every startup, and the owner's
    ''' standing rule about the output window is a count and not a list. But
    ''' the pairing IS the thing that was asked for, so it is printed in full
    ''' on one line, in across order - reading it left to right reads the
    ''' starting row left to right. The road ids should climb across that line;
    ''' if two are equal, two tanks are on one road.
    '''
    ''' The turn-radius report is the driven lane's alone, for the same
    ''' reason: it is the one being measured.
    ''' </summary>
    Private Sub report()
        For team = 1 To 2
            Dim order As New List(Of Tuple(Of Integer, Single))
            For i = 0 To lanes.Length - 1
                If lanes(i) IsNot Nothing AndAlso lanes(i).team = team Then
                    order.Add(Tuple.Create(i, lanes(i).across))
                End If
            Next
            If order.Count = 0 Then Continue For
            order.Sort(Function(a, b) a.Item2.CompareTo(b.Item2))
            Dim sb As New Text.StringBuilder()
            For Each o In order
                Dim ln = lanes(o.Item1)
                If sb.Length > 0 Then sb.Append(", ")
                sb.AppendFormat("hull {0}{1} road {2} at {3:0} m",
                                o.Item1, If(o.Item1 = BrainRadar.HULL, "*", ""),
                                ln.roadId, ln.across)
            Next
            LogThis("brain: team {0} roads across the map - {1}", team, sb.ToString())
        Next

        Dim d = mine()
        If d Is Nothing OrElse Not d.ready Then
            LogThis("brain: the driven hull has no road")
            Return
        End If
        Dim here = If(d.team = 2, BrainTanks.Base2, BrainTanks.Base1)
        Dim len_ = 0.0F
        For i = 0 To d.pts.Count - 2
            len_ += (d.pts(i + 1) - d.pts(i)).Length
        Next
        Dim need = BrainSim.TOP_SPEED / Math.Max(0.001F, BrainSim.TURN_RATE)
        LogThis("brain: road {0} stopped {1:0} m short of the enemy base, so the base " &
                "is now its last waypoint. Longest gap between waypoints {2:0} m, " &
                "{3} corner cut(s) refused - the goal rides up to that far ahead.",
                d.roadId, d.tail, d.longest, d.refused)
        LogThis("brain: hull {0} drives road {1} - {2} waypoint(s), {3:0} m of road, " &
                "{4:0.0} m from its spawn to the start, worst turn {5:0.0} m against " &
                "the {6:0.0} m the hull needs at full speed",
                BrainRadar.HULL, d.roadId, d.pts.Count, len_,
                (d.pts(0) - BrainTanks.Bodies(BrainRadar.HULL).spawn).Length,
                worst_radius(d), need)
        ' HOW FAR EACH TANK HAS TO GO BEFORE IT IS ON ITS ROAD, measured
        ' rather than assumed.
        '
        ' The path graph calls these roads base-to-base and the first version
        ' of this line said so - "every road leaves from the base". It does not.
        ' Road 17's near end is 233 m off the base marker, because the fan
        ' spreads across the whole base line and the flank roads begin at the
        ' flank. That opening leg has no road to help with it: it is the brain
        ' crossing open ground to a point, which is a different test from
        ' following a road and is worth being able to tell apart when a run
        ' goes wrong in the first twenty seconds.
        Dim near_ = Single.MaxValue, far_ = Single.MinValue
        For Each ln In lanes
            If ln Is Nothing OrElse Not ln.ready OrElse ln.team <> d.team Then Continue For
            Dim gap = (ln.pts(0) - BrainTanks.Bodies(ln.hull).spawn).Length
            near_ = Math.Min(near_, gap)
            far_ = Math.Max(far_, gap)
        Next
        If far_ > Single.MinValue Then
            LogThis("brain: team {0} leaves from the base at ({1:0.0}, {2:0.0}); the run " &
                    "to the assigned road start is {3:0} to {4:0} m of open ground, with " &
                    "no road to follow until they get there",
                    d.team, here.X, here.Y, near_, far_)
        End If
    End Sub

    ''' <summary>
    ''' SMOOTH ONE ROAD, COLLISION CHECKED, AT LOAD.
    '''
    ''' Ported from tools/smooth_path.py, which already settled this and whose
    ''' docstring is the argument against the obvious alternative:
    '''
    '''   "Running a spline through that is the obvious fix and the wrong one -
    '''    a spline cuts exactly the corners the navigator deliberately went
    '''    around, so it smooths the path straight into the building it was
    '''    avoiding."
    '''
    ''' These waypoints came out of the maze flood fill. They are not
    ''' decoration, they are where it is SAFE, and the tight corners exist
    ''' because something was there. So nothing here moves a point without
    ''' testing the result against the map.
    '''
    ''' AT LOAD RATHER THAN AT BAKE TIME, and that is the whole reason it is
    ''' here in VB rather than in the Studio. Chaikin's guarantee is that every
    ''' new point was checked against the map; check it against a stale map and
    ''' the guarantee is gone. The .blk changed three times on 2026-09-25
    ''' alone. Smoothing against the grid actually in memory is always current,
    ''' and it honours the Course's stamped obstacles for free.
    '''
    ''' SILENT NOW, because it runs ten times rather than once. What it used to
    ''' print per road, report() prints once for the road being driven.
    '''
    ''' It costs nothing worth measuring: a couple of dozen waypoints and a few
    ''' hundred BrainNav.Clear calls per road, once. Clear is bit 0 only now -
    ''' no terrain queries - since the slope sampling came out.
    ''' </summary>
    Private Sub smooth_road(ln As Lane)
        If ln.pts.Count < 3 Then Return
        shortcut_pass(ln)
        chaikin_pass(ln, BrainTune.Get_("chaikin", 4.0F))
        ' THE LONGEST GAP, because it is the real look-ahead. Tick advances
        ' while the current waypoint is inside 10 m, so the goal ends up
        ' somewhere between one gap and the next ahead of the hull - and a gap
        ' the corner cutting could not break up is a goal sitting that far out
        ' with no road between here and it.
        ln.longest = 0.0F
        For i = 0 To ln.pts.Count - 2
            ln.longest = Math.Max(ln.longest, (ln.pts(i + 1) - ln.pts(i)).Length)
        Next
    End Sub

    ''' <summary>
    ''' STRING PULLING. From each kept point, take the furthest point ahead
    ''' that is still reachable in a straight line.
    '''
    ''' GREEDY AND DETERMINISTIC, not the random-pair shortcutting some
    ''' planners use: a route that changes every time it is generated is
    ''' impossible to review, and reviewing it is most of what this is for.
    '''
    ''' This is the pass that does the real work. A path out of a reactive
    ''' planner is mostly detours that stopped being necessary a few metres
    ''' later, and a straight line through them is both shorter and smoother.
    ''' </summary>
    Private Sub shortcut_pass(ln As Lane)
        Dim n = ln.pts.Count
        If n < 3 Then Return
        Dim r = h_radius(ln.hull)
        Dim reach = CInt(BrainTune.Get_("cutreach", 12.0F))
        Dim outp As New List(Of Vector2)
        outp.Add(ln.pts(0))
        Dim i = 0
        Dim guard = 0
        While i < n - 1 AndAlso guard < 10 * n
            guard += 1
            Dim best = i + 1
            Dim hi = Math.Min(n - 1, i + reach)
            For j = hi To i + 2 Step -1
                If BrainNav.Clear(ln.pts(i), ln.pts(j), r) Then
                    best = j
                    Exit For
                End If
            Next
            outp.Add(ln.pts(best))
            i = best
        End While
        ln.pts.Clear()
        ln.pts.AddRange(outp)
    End Sub

    ''' <summary>
    ''' CHAIKIN CORNER CUTTING, with every generated point checked.
    '''
    ''' One pass replaces each edge with its quarter and three-quarter points.
    ''' Repeated, it converges to a quadratic B-spline - so it IS a spline,
    ''' arrived at by subdivision, and that is exactly what lets each new point
    ''' be tested before it is accepted. A fitted curve produces its points and
    ''' asks forgiveness.
    '''
    ''' WHEN A CUT IS REFUSED, RETRY GENTLER rather than giving up. A smaller
    ''' cut moves the path less, so it is likelier to stay clear, and a partly
    ''' rounded corner is worth more than a sharp one. Only if every depth is
    ''' refused does the original vertex survive - which is how a corner the
    ''' planner NEEDED stays sharp instead of being rounded into an obstacle.
    '''
    ''' The ends are never cut: an open path has to start where it starts.
    '''
    ''' AND NO DUPLICATE POINTS, which is not tidiness. A refused segment
    ''' emits both its endpoints, and the next segment emits the first of them
    ''' again, so every run of refusals laid the same point down twice and the
    ''' count doubled per pass without a single new place to drive to. Road 17
    ''' reported 224 waypoints over 843 m - an apparent 3.8 m spacing - while
    ''' the real gaps were long enough for the goal to sit over a hundred
    ''' metres ahead. That made the count a lie in the log AND let Tick's
    ''' advance loop walk a whole pile of them in one tick, because a
    ''' duplicate is always within the 10 m reach. add_pt drops them.
    ''' </summary>
    Private Sub chaikin_pass(ln As Lane, iterations As Single)
        Dim r = h_radius(ln.hull)
        ln.refused = 0
        For pass_ = 1 To CInt(Math.Max(0.0F, iterations))
            Dim n = ln.pts.Count
            If n < 3 Then Exit For
            Dim nxt As New List(Of Vector2)
            add_pt(nxt, ln.pts(0))
            For i = 0 To n - 2
                Dim a = ln.pts(i)
                Dim b = ln.pts(i + 1)
                Dim took = False
                For Each t In New Single() {0.25F, 0.15F, 0.08F}
                    Dim p = a + (b - a) * t
                    Dim q = a + (b - a) * (1.0F - t)
                    If BrainNav.Clear(a, p, r) AndAlso
                       BrainNav.Clear(p, q, r) AndAlso
                       BrainNav.Clear(q, b, r) Then
                        add_pt(nxt, p)
                        add_pt(nxt, q)
                        took = True
                        Exit For
                    End If
                Next
                If Not took Then
                    ln.refused += 1
                    add_pt(nxt, a)
                    add_pt(nxt, b)
                End If
            Next
            add_pt(nxt, ln.pts(n - 1))
            ln.pts.Clear()
            ln.pts.AddRange(nxt)
        Next
    End Sub

    ''' <summary>Append, unless it is the point already on the end. A
    ''' centimetre is far below anything the follower can tell apart and far
    ''' above float noise on map coordinates in the hundreds of metres.</summary>
    Private Sub add_pt(v As List(Of Vector2), p As Vector2)
        If v.Count > 0 AndAlso (v(v.Count - 1) - p).Length < 0.01F Then Return
        v.Add(p)
    End Sub

    ''' <summary>
    ''' The tightest turn on one road, metres.
    '''
    ''' The circumradius of each consecutive triple, by R = abc / 4A - the
    ''' side-lengths form, which needs no circle centre and degenerates
    ''' gracefully: three collinear points have zero area and give a huge
    ''' radius, which is what a straight is.
    '''
    ''' REPORTED, NOT ENFORCED. A hull that cannot hold a corner is a speed
    ''' problem before it is a geometry one, and smooth_path.py says the same.
    ''' </summary>
    Private Function worst_radius(ln As Lane) As Single
        If ln.pts.Count < 3 Then Return 100000.0F
        Dim worst = 100000.0F
        For i = 1 To ln.pts.Count - 2
            Dim a = (ln.pts(i) - ln.pts(i - 1)).Length
            Dim b = (ln.pts(i + 1) - ln.pts(i)).Length
            Dim c = (ln.pts(i + 1) - ln.pts(i - 1)).Length
            Dim s = (ln.pts(i) - ln.pts(i - 1))
            Dim tv = (ln.pts(i + 1) - ln.pts(i))
            Dim area2 = Math.Abs(s.X * tv.Y - s.Y * tv.X)
            If area2 < 0.0001F Then Continue For
            Dim rad = (a * b * c) / (2.0F * area2)
            If rad < worst Then worst = rad
        Next
        Return worst
    End Function

    ''' <summary>The width every candidate is tested at - THAT hull's own
    ''' driving radius, so "clear" here means the same thing it means to the
    ''' sim that refuses moves. Per hull now, because the roster spans 4.6 to
    ''' 7.9 m of length and a road smoothed to the narrow one is a road the
    ''' wide one clips.</summary>
    Private Function h_radius(hull As Integer) As Single
        If BrainTanks.Bodies Is Nothing OrElse hull < 0 OrElse
           hull >= BrainTanks.Bodies.Count Then Return 1.94F
        Return BrainTanks.Bodies(hull).half.X + 0.3F
    End Function

    ''' <summary>
    ''' Advance every hull's goal along its own road. Called once a tick,
    ''' before the brain.
    '''
    ''' REACHED *OR* PASSED, and the second half of that is not a refinement -
    ''' without it the follower deadlocks against the hull's own turning
    ''' circle.
    '''
    ''' The rule used to be "within 10 m" alone. A hull at 12 m/s turning at
    ''' 0.45 rad/s needs 26.7 m of radius, so it cannot place itself inside a
    ''' 10 m disc it is driving past - it misses by a metre or two, the index
    ''' does not move, and the only way back to a goal it has already overshot
    ''' is a full loop. Measured on road 17: the range to goal cycled 11 m out
    ''' to 130 m and back, over and over, and half of every one of those loops
    ''' is the tank driving SOUTH - back toward its own base. That is what
    ''' "this tank is trying to get back to base" looks like from the outside,
    ''' and the tank was doing exactly what it was told.
    '''
    ''' So a waypoint also counts as done when it is BEHIND THE NOSE: the
    ''' bearing to it has a negative component along the hull's heading. That
    ''' is the geometric statement of "we have driven past this".
    '''
    ''' CAPPED AT THREE TIMES THE REACH, because behind-the-nose on its own is
    ''' too strong. A hull swung round during a go-around has half the road
    ''' behind it for a second or two, and an uncapped test would tick off
    ''' every waypoint it had not reached yet and jump the goal to the far
    ''' end. Within 30 m and behind is an overshoot; 200 m and behind is a
    ''' tank facing the wrong way, which is the brain's problem and not a
    ''' reason to abandon the road.
    '''
    ''' SKIPS WHATEVER IS ALREADY BEHIND, not just the one in hand: joining a
    ''' road at speed, or being pushed down it by a go-around, can carry the
    ''' hull past several nodes at once, and advancing one per tick would aim
    ''' it backwards at a point it has already driven through.
    '''
    ''' EVERY LANE WALKS, ONLY THE DRIVEN ONE WRITES THE GOAL. There is one
    ''' BrainGoal and one brain; a second lane writing it would be two drivers
    ''' again. The parked hulls cost a subtract and a compare each and stay
    ''' honest, so the day there is a brain per hull the answers are already
    ''' here.
    '''
    ''' AND IT STOPS AT THE FAR BASE. "it doesn't stop at when it gets to
    ''' base" then "25m rad i think zone" - the owner, 2026-09-25. The base is
    ''' the last waypoint now (see build_lane), so the zone is a zone round the
    ''' base rather than round wherever the road happened to give out.
    '''
    ''' BY TAKING THE GOAL AWAY, not by commanding a stop. The board's very
    ''' first rung is already No Goal -> Stop, so clearing HasTarget is the
    ''' whole of it - and it keeps the follower to the one thing it is allowed
    ''' to touch. A second module commanding throttle would be the second
    ''' driver this design exists to avoid.
    '''
    ''' ONCE, latched. Without the flag the arrival would re-fire every tick,
    ''' and any rung that set a goal for its own reasons would be fighting a
    ''' module that cleared it again a frame later. Reset unlatches it.
    ''' </summary>
    Public Sub Tick()
        If Not ON_ OrElse lanes Is Nothing Then Return
        If BrainTanks.Bodies Is Nothing Then Return

        Dim reach = BrainTune.Get_("waypm", 10.0F)
        Dim zone = BrainTune.Get_("basem", 25.0F)
        Dim behind = reach * BrainTune.Get_("waypast", 3.0F)

        For i = 0 To lanes.Length - 1
            Dim ln = lanes(i)
            If ln Is Nothing OrElse Not ln.ready OrElse ln.finished Then Continue For
            If i >= BrainTanks.Bodies.Count Then Continue For
            Dim drives = (i = BrainRadar.HULL)
            Dim here = BrainTanks.Bodies(i).spawn

            ' The hull's nose, world XZ. Same convention as everywhere else in
            ' this app: heading 0 is +Z, and X = sin, Z = cos.
            Dim hdg = BrainTanks.Bodies(i).headingRad
            Dim nose = New Vector2(CSng(Math.Sin(hdg)), CSng(Math.Cos(hdg)))

            Dim moved = False
            While ln.at < ln.pts.Count - 1
                Dim toWp = ln.pts(ln.at) - here
                Dim gap = toWp.Length
                Dim done = gap < reach OrElse
                           (gap < behind AndAlso Vector2.Dot(toWp, nose) < 0.0F)
                If Not done Then Exit While
                ln.at += 1
                moved = True
            End While

            ' A FOLLOWER THAT HAS STOPPED FOLLOWING SAYS SO.
            '
            ' This was diagnosed from a range-to-goal series in a log, by hand,
            ' working backwards from a shape that turned out to be a hull
            ' orbiting a waypoint it could not reach. That should not have
            ' needed doing twice, so the condition is now a line of its own:
            ' the index has not moved while the hull has driven a long way,
            ' which is the definition of not following.
            If drives Then
                If moved Then
                    ln.stalled = 0
                    ln.said = False
                Else
                    ln.stalled += (here - ln.was).Length
                    If ln.stalled > BrainTune.Get_("waystall", 250.0F) AndAlso Not ln.said Then
                        ln.said = True
                        Dim toWp = ln.pts(ln.at) - here
                        LogThis("brain: hull {0} has driven {1:0} m without passing " &
                                "waypoint {2}/{3} at ({4:0.0}, {5:0.0}) - it is {6:0.0} m " &
                                "away and {7} the nose. The follower is not following.",
                                i, ln.stalled, ln.at, ln.pts.Count - 1,
                                ln.pts(ln.at).X, ln.pts(ln.at).Y, toWp.Length,
                                If(Vector2.Dot(toWp, nose) < 0.0F, "behind", "ahead of"))
                    End If
                End If
                ln.was = here
            End If

            ' THE BASE ZONE. The last waypoint is the far base, and a tank that
            ' has to touch a node to the metre is a tank that circles it - the
            ' zone is the arrival, not the point.
            If ln.at >= ln.pts.Count - 1 AndAlso (ln.pts(ln.at) - here).Length <= zone Then
                ln.finished = True
                If drives Then
                    BrainGoal.HasTarget = False
                    LogThis("brain: hull {0} road {1} COMPLETE - inside the {2:0} m base " &
                            "zone at ({3:0.0}, {4:0.0}), {5:0.0} m from the base. " &
                            "Goal cleared, the board stops on No Goal.",
                            i, ln.roadId, zone, here.X, here.Y,
                            (ln.pts(ln.at) - here).Length)
                End If
                Continue For
            End If

            If drives Then
                BrainGoal.Follow = False
                BrainGoal.Target = ln.pts(ln.at)
                BrainGoal.HasTarget = True
            End If
        Next
    End Sub

    ''' <summary>
    ''' PUT EVERY TANK BACK TO THE START OF ITS OWN ROAD.
    '''
    ''' "reset does not reset the start points the tanks seek" - the owner,
    ''' 2026-09-25, and he is describing two separate failures that look like
    ''' one.
    '''
    ''' THE INDEX. Reset puts the hulls back on their spawns but `at` stayed
    ''' wherever the abandoned run had pushed it, so the tank was returned to
    ''' its base and immediately handed a waypoint four hundred metres down
    ''' the road. It then drove at that point in a straight line, ignoring the
    ''' road it was supposed to be following, because every waypoint between
    ''' here and there had already been marked as passed.
    '''
    ''' THE LATCH, which is worse and is a bug with one tank as much as five.
    ''' A road that reached the far base sets finished and clears the goal -
    ''' deliberately, once. Reset never cleared it, so after one completed run
    ''' the hull went home, was put back on its spawn, and then sat there for
    ''' good: no goal, and the board's first rung is No Goal -> Stop. Pressing
    ''' Run did nothing and there was nothing on screen to say why.
    '''
    ''' AND THE GOAL IS RE-ARMED HERE. The Reset case in BrainWindow leaves the
    ''' goal alone on purpose - "it is not run state, it is the question being
    ''' asked" - but when a road is being followed the road IS the question,
    ''' and its first waypoint is where the question starts. Writing it here
    ''' keeps that rule true rather than breaking it: the follower owns the
    ''' goal while ON_, so the follower is what restores it.
    '''
    ''' Called from BrainSim.Start too, because Start already means a fresh run
    ''' - it resets the trail and the report for exactly this reason - and a
    ''' finished road that survived into the next run would stop it before it
    ''' began.
    ''' </summary>
    Public Sub Reset()
        If Not ON_ OrElse lanes Is Nothing Then Return
        Dim n = 0, wasDone = 0
        For Each ln In lanes
            If ln Is Nothing Then Continue For
            If ln.finished Then wasDone += 1
            ln.at = 0
            ln.finished = False
            ln.stalled = 0.0F
            ln.said = False
            If BrainTanks.Bodies IsNot Nothing AndAlso
               ln.hull < BrainTanks.Bodies.Count Then ln.was = BrainTanks.Bodies(ln.hull).spawn
            n += 1
        Next
        If n = 0 Then Return

        Dim d = mine()
        If d IsNot Nothing AndAlso d.ready Then
            BrainGoal.Follow = False
            BrainGoal.Target = d.pts(0)
            BrainGoal.HasTarget = True
        End If
        LogThis("brain: {0} road(s) rewound to waypoint 0{1}. Hull {2} seeks road {3} " &
                "again at ({4:0.0}, {5:0.0})",
                n, If(wasDone > 0, ", " & wasDone & " of them had finished", ""),
                BrainRadar.HULL,
                If(d Is Nothing, -1, d.roadId),
                If(d Is Nothing OrElse Not d.ready, 0.0F, d.pts(0).X),
                If(d Is Nothing OrElse Not d.ready, 0.0F, d.pts(0).Y))
    End Sub

    ''' <summary>
    ''' CLEAR THE ARRIVAL LATCH AND NOTHING ELSE. Called by BrainSim.Start.
    '''
    ''' This used to call Reset, and that was the bug behind "this tank is
    ''' trying to get back to base at the end". Start does not move the hull -
    ''' only the Reset button does, through RestoreSpawns - so rewinding the
    ''' waypoint index to 0 while the tank stood at the far end of its road
    ''' pointed the goal at waypoint 0, which is the road's start BESIDE ITS
    ''' OWN BASE, and the tank dutifully turned round and drove the whole road
    ''' home. It was following orders; the orders were wrong.
    '''
    ''' Clearing only the latch lets the hull's own position decide, which is
    ''' the right authority for it. Halted half way and pressed Run: it carries
    ''' on from the waypoint it had reached. Standing at the enemy base and
    ''' pressed Run: Tick re-tests the zone on the next tick, arrives again and
    ''' stops - which is honest, because it HAS arrived, and Reset is the
    ''' control that means go again.
    '''
    ''' Without clearing anything at all, a finished road would refuse to
    ''' drive for the rest of the session even after a legitimate reset that
    ''' happened not to go through the button.
    ''' </summary>
    Public Sub Rearm()
        If Not ON_ OrElse lanes Is Nothing Then Return
        For Each ln In lanes
            If ln Is Nothing Then Continue For
            ln.finished = False
            ln.stalled = 0.0F
            ln.said = False
            If BrainTanks.Bodies IsNot Nothing AndAlso
               ln.hull < BrainTanks.Bodies.Count Then ln.was = BrainTanks.Bodies(ln.hull).spawn
        Next
    End Sub

    Public ReadOnly Property Done As Boolean
        Get
            Dim d = mine()
            Return d IsNot Nothing AndAlso d.ready AndAlso d.at >= d.pts.Count - 1
        End Get
    End Property

    ''' <summary>
    ''' TWO DRAWS, TWO COLOURS: the road being driven, and the other nine.
    '''
    ''' Ten roads all in the same green is a green map with no way to tell
    ''' which one is the run. The driven lane keeps the bright green the
    ''' legend names; the rest are dimmed to about a third so they read as
    ''' context. One buffer, the driven lane's vertices first, two DrawArrays
    ''' - a second VAO for the sake of a colour would be a second thing to
    ''' keep in step.
    '''
    ''' Green, and nothing else here is green: the trail is pink and yellow,
    ''' the walk is blue and white and red, the course is red. A road has to
    ''' be tellable from the path actually driven over it at a glance.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not ON_ OrElse lanes Is Nothing Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If verts = 0 Then build_mesh()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        GL.BindVertexArray(vao)
        GL.LineWidth(3.0F)
        If mineVerts > 0 Then
            shader.SetVec3("colour", New Vector3(0.25F, 0.95F, 0.35F))
            GL.DrawArrays(PrimitiveType.Lines, 0, mineVerts)
        End If
        If verts > mineVerts Then
            shader.SetVec3("colour", New Vector3(0.10F, 0.34F, 0.14F))
            GL.DrawArrays(PrimitiveType.Lines, mineVerts, verts - mineVerts)
        End If
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
    End Sub

    Private Sub build_mesh()
        Dim v As New List(Of Single)
        ' THE DRIVEN LANE FIRST, so one buffer can be drawn in two colours.
        Dim h = BrainRadar.HULL
        If h >= 0 AndAlso h < lanes.Length Then push_lane(v, lanes(h))
        mineVerts = v.Count \ 3
        For i = 0 To lanes.Length - 1
            If i = h Then Continue For
            push_lane(v, lanes(i))
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

    ''' <summary>
    ''' One road: the line, a post at every waypoint, and a TALL post at the
    ''' start point that hull was assigned.
    '''
    ''' The start post is twice the height of the others because it is the
    ''' thing that was asked for - "assign a starting point for each tank to
    ''' seek" - and five of them standing in a row across the map is the
    ''' assignment made visible. If they are not spread, the picture says so
    ''' before any log does.
    '''
    ''' The ordinary posts show the spacing, which a plain line hides: a road
    ''' with nodes ninety metres apart and one with them every five drive very
    ''' differently and look identical.
    ''' </summary>
    Private Sub push_lane(v As List(Of Single), ln As Lane)
        If ln Is Nothing OrElse Not ln.ready Then Return
        For i = 0 To ln.pts.Count - 2
            push(v, ln.pts(i))
            push(v, ln.pts(i + 1))
        Next
        For Each q In ln.pts
            push(v, q)
            push2(v, q, 4.0F)
        Next
        push(v, ln.pts(0))
        push2(v, ln.pts(0), 9.0F)
    End Sub

    Private Sub push(v As List(Of Single), p As Vector2)
        v.Add(p.X) : v.Add(BrainNav.Ground(p.X, p.Y) + 0.3F) : v.Add(p.Y)
    End Sub

    Private Sub push2(v As List(Of Single), p As Vector2, up As Single)
        v.Add(p.X) : v.Add(BrainNav.Ground(p.X, p.Y) + up) : v.Add(p.Y)
    End Sub

End Module

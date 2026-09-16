Imports OpenTK.Mathematics

''' <summary>
''' The thirty bodies: where they start, and how big each hull is.
'''
''' No driving lives here. This hands the brain a starting grid and a box per
''' tank, and stops - see "The seam" in docs/brain_testing_plan.md.
'''
''' Added 2026-09-15 by nuTerra work.
''' </summary>
Module BrainTanks

    ''' <summary>One loaded hull, as the brain will meet it.</summary>
    Public Structure Body
        Public id As Integer              ' 1..30, unique across BOTH teams
        Public team As Integer            ' 1 or 2
        Public tag As String              ' e.g. "R110_Object_260"
        Public vehicle As TankVehicle
        Public spawn As Vector2           ' world XZ, fixed
        ''' <summary>Ground height at the spawn, sampled AFTER the terrain is
        ''' up. 0 when there is no terrain - the hull then sits at sea level
        ''' rather than vanishing, which is easier to diagnose than absence.</summary>
        Public y As Single
        Public headingRad As Single
        ''' <summary>Half-extents of the HULL in metres: X across, Y up, Z
        ''' along. The game's own boundingBox, via TankRoster.</summary>
        Public half As Vector3
    End Structure

    Public Bodies As New List(Of Body)

    ' ---- the arena, in the WORLD frame ----------------------------------

    ''' <summary>
    ''' Base markers and declared spawn points, ALREADY IN THE WORLD FRAME.
    '''
    ''' nuTerra's globals hold these in the ARENA frame and negate X at each
    ''' use, and TerrainBuilder's own comment says two sessions reading the
    ''' same arena_defs "can disagree by exactly that sign and each be
    ''' internally consistent". Converting once, here, at the only place the
    ''' file is read, is how this app avoids joining that argument.
    ''' </summary>
    Public Base1, Base2 As Vector2
    Public HasBases As Boolean = False
    Public Spawns1 As New List(Of Vector2)
    Public Spawns2 As New List(Of Vector2)

    Private Const ROW_N As Integer = 5        ' five abreast
    Private Const SPACING As Single = 14.0F   ' metres between hulls

    ''' <summary>Read scripts/arena_defs/&lt;map&gt;.xml for the bases and any
    ''' declared spawn points. False if the map declares no ctf bases.</summary>
    Public Function ReadArena(map As String) As Boolean
        HasBases = False
        Spawns1.Clear()
        Spawns2.Clear()
        If map Is Nothing OrElse map = "" Then Return False

        Dim x = ResMgr.openXML("scripts/arena_defs/" & map & ".xml")
        If x Is Nothing Then
            LogThis("brain: no arena_def for {0}", map)
            Return False
        End If

        Dim bases = x.SelectSingleNode("gameplayTypes/ctf/teamBasePositions")
        If bases Is Nothing Then
            LogThis("brain: {0} declares no ctf teamBasePositions", map)
            Return False
        End If
        Base1 = world_xz(bases("team1").ChildNodes(1).InnerText)
        Base2 = world_xz(bases("team2").ChildNodes(1).InnerText)
        HasBases = True

        ' Spawn points sit beside the bases under gameplayTypes, across any
        ' mode. Taking the first mode that declares them is closer to "where
        ' does this map start tanks" than insisting on ctf and finding none.
        Dim modes = x.SelectSingleNode("gameplayTypes")
        If modes IsNot Nothing Then
            For Each mode As Xml.XmlNode In modes.ChildNodes
                If mode.NodeType <> Xml.XmlNodeType.Element Then Continue For
                Dim sp = mode.SelectSingleNode("teamSpawnPoints")
                If sp Is Nothing Then Continue For
                collect(sp.SelectSingleNode("team1"), Spawns1)
                collect(sp.SelectSingleNode("team2"), Spawns2)
                If Spawns1.Count > 0 OrElse Spawns2.Count > 0 Then Exit For
            Next
        End If

        LogThis("brain: {0} base1 ({1:0.0}, {2:0.0}) base2 ({3:0.0}, {4:0.0}), declared spawns {5}/{6}",
                map, Base1.X, Base1.Y, Base2.X, Base2.Y, Spawns1.Count, Spawns2.Count)
        Return True
    End Function

    ''' <summary>"x z" or "x y z" from the file, to world XZ. X is negated
    ''' here and nowhere else; the y, when present, is dropped because the
    ''' terrain decides height and a stored one would only disagree.</summary>
    Private Function world_xz(text As String) As Vector2
        Dim f = text.Trim().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim ax As Single, az As Single
        If f.Length = 2 Then
            Single.TryParse(f(0), ax) : Single.TryParse(f(1), az)
        ElseIf f.Length >= 3 Then
            Single.TryParse(f(0), ax) : Single.TryParse(f(2), az)
        End If
        Return New Vector2(-ax, az)
    End Function

    Private Sub collect(team As Xml.XmlNode, into As List(Of Vector2))
        If team Is Nothing Then Return
        For Each p As Xml.XmlNode In team.ChildNodes
            If p.NodeType = Xml.XmlNodeType.Element Then into.Add(world_xz(p.InnerText))
        Next
    End Sub

    ''' <summary>
    ''' Where slot k of this team starts. THE SAME ANSWER EVERY RUN.
    '''
    ''' "I want to load these tanks and always to the same spawn locations."
    ''' So this is a pure function of the arena file and the slot number, and
    ''' of nothing else. In particular it does NOT avoid obstacles the way
    ''' nuTerra's find_clear_spot does: that walks outward until it finds
    ''' ground the bake calls clear, and the bake changes - it went through
    ''' four versions in one evening - so the same tank would start somewhere
    ''' different after a rebake and two AI runs would stop being comparable.
    '''
    ''' A hull that starts inside something is a thing the brain has to deal
    ''' with, and seeing it is better than having it quietly moved.
    ''' </summary>
    Public Function SpawnOf(team As Integer, k As Integer) As Vector2
        Dim declared = If(team = 1, Spawns1, Spawns2)
        If k < declared.Count Then Return declared(k)

        ' Five abreast, three deep, behind the marker along the team's own
        ' backward axis so the base ring itself stays clear. Most maps -
        ' 19_monastery among them - declare no spawn points at all, so this
        ' grid is the normal path, not an error case.
        Dim marker = If(team = 1, Base1, Base2)
        Dim col = CSng(k Mod ROW_N) - (ROW_N - 1) / 2.0F
        Dim row = k \ ROW_N
        Dim back = If(team = 1, -1.0F, 1.0F)
        Return New Vector2(marker.X + col * SPACING,
                           marker.Y + (row + 1) * SPACING * back)
    End Function

    ''' <summary>
    ''' Load the roster and fix every hull to its spawn.
    '''
    ''' perTeam is clamped to half the roster: the split is team = i &lt;
    ''' perTeam, so asking for more than half does not field more a side, it
    ''' fields the excess on team 1 and leaves team 2 short.
    ''' </summary>
    Public Function LoadAll(perTeam As Integer) As Integer
        Bodies.Clear()
        Dim per = Math.Max(1, Math.Min(perTeam, TankRoster.PerTeamMax))
        If per <> perTeam Then
            LogThis("brain: {0} a side asked for, {1} is what a roster of {2} allows",
                    perTeam, per, TankRoster.ALL.Length)
        End If

        Dim sw = Stopwatch.StartNew()
        Dim failed = 0
        For i = 0 To per * 2 - 1
            Dim r = TankRoster.ALL(i)
            Dim team = If(i < per, 1, 2)
            Dim k = i Mod per

            Dim v = TankVehicle.Load(r.Item1, r.Item2, Nothing)
            If v Is Nothing Then
                LogThis("brain: {0}/{1} did not load", r.Item1, r.Item2)
                failed += 1
                Continue For
            End If

            Bodies.Add(New Body With {
                .id = i + 1,
                .team = team,
                .tag = r.Item2,
                .vehicle = v,
                .spawn = SpawnOf(team, k),
                .y = ground_at(SpawnOf(team, k)),
                .headingRad = If(team = 1, 0.0F, CSng(Math.PI)),
                .half = TankRoster.HullHalfExtents(v)})
        Next

        report(sw.ElapsedMilliseconds, failed)
        Return Bodies.Count
    End Function

    ''' <summary>Terrain height under a spawn. get_Y_at_XZ is ChunkFunctions'
    ''' own sampler, linked in - so the hull stands on exactly the surface the
    ''' brain will be asked about, with no second height source to disagree
    ''' with it.</summary>
    Private Function ground_at(p As Vector2) As Single
        If map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return 0.0F
        Try
            Return get_Y_at_XZ(p.X, p.Y)
        Catch
            Return 0.0F
        End Try
    End Function

    ''' <summary>
    ''' One line always, thirty only when asked.
    '''
    ''' The per-tank table is what proves the boxes are real, and it is a
    ''' question asked once - so `hullbox` on the command line asks it. Thirty
    ''' lines at every startup would be exactly the noise the owner asked not
    ''' to have.
    ''' </summary>
    Private Sub report(ms As Long, failed As Integer)
        If Bodies.Count = 0 Then
            LogThis("brain: nothing loaded ({0} failed)", failed)
            Return
        End If

        Dim minZ = Single.MaxValue, maxZ = Single.MinValue, sumZ = 0.0F
        Dim boxed = 0
        For Each b In Bodies
            Dim L = b.half.Z * 2.0F
            minZ = Math.Min(minZ, L)
            maxZ = Math.Max(maxZ, L)
            sumZ += L
            If TankRoster.HullVisual(b.vehicle) IsNot Nothing Then boxed += 1
        Next

        LogThis("brain: {0} loaded in {1} ms, {2} failed. Hull boxes from the game: {3}/{0}. " &
                "Length {4:0.0}-{5:0.0} m, mean {6:0.0}",
                Bodies.Count, ms, failed, boxed, minZ, maxZ, sumZ / Bodies.Count)

        If boxed < Bodies.Count Then
            ' Named, because a fallback box is a tank the AI will size wrongly.
            For Each b In Bodies
                If TankRoster.HullVisual(b.vehicle) Is Nothing Then
                    LogThis("brain:   {0} has no hull visual - using the fallback box", b.tag)
                End If
            Next
        End If

        If Not HULL_BOX_TABLE Then Return
        LogThis("brain:  id tm  {0,-32} {1,6} {2,6} {3,6}   spawn", "vehicle", "W", "H", "L")
        For Each b In Bodies
            LogThis("brain:  {0,2} T{1}  {2,-32} {3,6:0.00} {4,6:0.00} {5,6:0.00}   ({6:0.0}, {7:0.0})",
                    b.id, b.team, b.tag,
                    b.half.X * 2.0F, b.half.Y * 2.0F, b.half.Z * 2.0F,
                    b.spawn.X, b.spawn.Y)
        Next
    End Sub

End Module

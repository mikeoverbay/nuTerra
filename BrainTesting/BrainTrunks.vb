Imports System.IO

''' <summary>
''' How thick is a tree's trunk, from the game rather than from a guess.
'''
''' THE BAKE USES 0.6 m FOR EVERY SPECIES. MapFlightBake.TRUNK_RADIUS is a
''' single constant with an honest comment - "0.6 m clears the thickest trunks
''' on the roster while cutting the limbs" - and it has to be a constant
''' because nothing was reading the real shape. This reads the real shape.
'''
''' WHERE IT LIVES, since this cost a detour: trees have NO .havok. Monastery
''' ships 12 .srt files and 36 .havok, and zero of the .havok sit under a
''' vegetation path - those are all static models. A tree's collision is
''' inside its own .srt, and nuTerra's SrtFile already decodes it as
''' PartKind.Collision: "the coarse hull, a handful of capsules built from a
''' few distinct points".
'''
''' So material_kinds.xml naming 71 speedtree_trunk and 72 speedtree_foliage
''' is the game AGREEING that the two are different things - but the geometry
''' that separates them for a WoT tree is in the .srt, not in a havok body.
'''
''' SrtFile is standalone - no GL, no engine types, only System.Linq and
''' System.Text - so this costs one linked file and no cascade.
'''
''' Added 2026-09-16 by nuTerra work.
''' </summary>
Module BrainTrunks

    ''' <summary>Per species, what the collision hull measures.</summary>
    Public Structure Trunk
        Public species As String
        Public placed As Integer
        ''' <summary>Half-width of the collision hull in XZ, metres - the
        ''' radius a hull would have to clear.</summary>
        Public radius As Single
        ''' <summary>The widest collision part. Equal to radius on a species
        ''' with one part, which means the hull IS the whole plant.</summary>
        Public widest As Single
        Public height As Single
        Public parts As Integer
        Public ok As Boolean
        ''' <summary>Whether radius is a TRUNK rather than the whole plant -
        ''' see TreeTrunks.Trunk.trunkKnown. Only these reach the bake.</summary>
        Public trunkKnown As Boolean
    End Structure

    Public All As New List(Of Trunk)

    ''' <summary>
    ''' What MapFlightBake.TRUNK_RADIUS is, for the comparison line only.
    '''
    ''' A LITERAL, and deliberately not the real constant: linking
    ''' MapFlightBake to read one number pulls GLFramebuffer and the whole
    ''' bake pass in behind it. This is used to annotate a report, never in a
    ''' decision, so a stale copy misprints one line rather than moving a
    ''' tank. If the bake's value changes this should follow it.
    ''' </summary>
    Private Const BAKE_TRUNK_RADIUS As Single = 0.6F

    ''' <summary>
    ''' Measure every species the map places.
    '''
    ''' Counts placements per species first so the report is ordered by how
    ''' much of the map each one actually covers - a species placed once is
    ''' not worth the same attention as one placed 1,386 times.
    ''' </summary>
    Public Sub Measure()
        All.Clear()
        If cSpTr.trees Is Nothing OrElse cSpTr.trees.count = 0 Then Return

        Dim placed As New Dictionary(Of String, Integer)
        For i = 0 To CInt(cSpTr.trees.count) - 1
            Dim nm = cSpTr.trees.data(i).spt_name
            If String.IsNullOrEmpty(nm) Then Continue For
            placed(nm) = If(placed.ContainsKey(nm), placed(nm), 0) + 1
        Next

        Dim sw = Stopwatch.StartNew()
        For Each kv In placed
            All.Add(measure_one(kv.Key, kv.Value))
        Next
        All.Sort(Function(a, b) b.placed.CompareTo(a.placed))

        ' COUNTED ON trunkKnown, NOT ON ok. A species with one collision part
        ' measured fine and still has no trunk in it - reporting those in the
        ' range would name numbers the bake never uses, and 6.29 m sitting in a
        ' line about trunks is exactly the sort of figure that gets quoted.
        Dim got = 0, miss = 0
        Dim rmin = Single.MaxValue, rmax = Single.MinValue
        For Each t In All
            If t.trunkKnown Then
                got += 1
                rmin = Math.Min(rmin, t.radius)
                rmax = Math.Max(rmax, t.radius)
            Else
                miss += 1
            End If
        Next

        If got = 0 Then
            LogThis("brain: no species of {0} has a separable trunk - the bake uses " &
                    "{1:0.00} m for all of them", All.Count, BAKE_TRUNK_RADIUS)
            Return
        End If

        LogThis("brain: {0}/{1} species have a separable trunk, {2:0.00}-{3:0.00} m, " &
                "measured in {4} ms - the bake's trunk pass uses those and {5:0.00} m " &
                "for the other {6}",
                got, All.Count, rmin, rmax, sw.ElapsedMilliseconds,
                BAKE_TRUNK_RADIUS, miss)
        For Each t In All
            If Not t.ok Then
                LogThis("brain:   {0,-30} x{1,5}  no collision part - bake uses {2:0.00}",
                        IO.Path.GetFileName(t.species), t.placed, BAKE_TRUNK_RADIUS)
            ElseIf Not t.trunkKnown Then
                ' ONE PART IS THE WHOLE PLANT. Printing its width in the trunk
                ' column would be a measurement of the wrong thing.
                LogThis("brain:   {0,-30} x{1,5}  one hull {2,5:0.00} wide, no trunk in it" &
                        " - bake uses {3:0.00}",
                        IO.Path.GetFileName(t.species), t.placed, t.widest, BAKE_TRUNK_RADIUS)
            Else
                LogThis("brain:   {0,-30} x{1,5}  trunk r {2,5:0.00}  widest {3,5:0.00}  h {4,6:0.00}  {5} part(s)",
                        IO.Path.GetFileName(t.species), t.placed, t.radius, t.widest,
                        t.height, t.parts)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Ask nuTerra, then attach how many this map places.
    '''
    ''' THE MEASUREMENT MOVED. It lived here first, which was the right place
    ''' while nothing else wanted it; the trunk pass now sets its threshold
    ''' from the same numbers, so the measuring belongs where both can reach
    ''' it and a second copy would only drift. What stays here is the part
    ''' that is about THIS MAP - the placement count and the report.
    ''' </summary>
    Private Function measure_one(species As String, placed As Integer) As Trunk
        Dim m = TreeTrunks.Measure(species)
        Return New Trunk With {
            .species = species, .placed = placed,
            .radius = m.radius, .widest = m.widest, .height = m.height,
            .parts = m.parts, .ok = m.ok, .trunkKnown = m.trunkKnown}
    End Function

End Module

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

        Dim got = 0, miss = 0
        Dim rmin = Single.MaxValue, rmax = Single.MinValue
        For Each t In All
            If t.ok Then
                got += 1
                rmin = Math.Min(rmin, t.radius)
                rmax = Math.Max(rmax, t.radius)
            Else
                miss += 1
            End If
        Next

        If got = 0 Then
            LogThis("brain: no collision hull found in any of {0} species", All.Count)
            Return
        End If

        LogThis("brain: trunks from {0}/{1} species in {2} ms - radius {3:0.00}-{4:0.00} m " &
                "(the bake uses {5:0.00} for all of them)",
                got, All.Count, sw.ElapsedMilliseconds, rmin, rmax,
                BAKE_TRUNK_RADIUS)
        For Each t In All
            If Not t.ok Then
                LogThis("brain:   {0,-34} x{1,5}  no collision part", t.species, t.placed)
            Else
                LogThis("brain:   {0,-30} x{1,5}  trunk r {2,5:0.00}  widest {3,5:0.00}  h {4,6:0.00}  {5} part(s)",
                        IO.Path.GetFileName(t.species), t.placed, t.radius, t.widest,
                        t.height, t.parts)
            End If
        Next
    End Sub

    Private Function measure_one(species As String, placed As Integer) As Trunk
        Dim t As New Trunk With {.species = species, .placed = placed}
        Try
            Dim entry = ResMgr.Lookup(species)
            If entry Is Nothing Then Return t
            Dim ms As New MemoryStream()
            entry.Extract(ms)
            Dim srt = SrtFile.FromBytes(ms.ToArray(), species)
            If srt Is Nothing OrElse srt.DrawCalls Is Nothing Then Return t

            ' LOD 0 ONLY. The same hull is repeated per LOD, and mixing them
            ' would count one trunk several times and widen nothing.
            ' PER PART, NOT THE UNION, and that is the whole point. Measuring
            ' every collision part together gives a bush's entire hull and
            ' calls it a trunk - Olive_bush came out at 3.75 m that way. A
            ' species with two parts has them separated: the NARROW one is the
            ' trunk and the wide one is the canopy capsule.
            Dim n = 0
            Dim narrow = Single.MaxValue, wide = Single.MinValue
            Dim tallest = 0.0F
            For Each dc In srt.DrawCalls
                If dc.Kind <> SrtFile.PartKind.Collision Then Continue For
                If dc.Lod <> 0 Then Continue For
                If dc.Positions Is Nothing OrElse dc.Positions.Length < 3 Then Continue For

                Dim lo_x = Single.MaxValue, hi_x = Single.MinValue
                Dim lo_z = Single.MaxValue, hi_z = Single.MinValue
                Dim lo_y = Single.MaxValue, hi_y = Single.MinValue
                Dim v = 0
                While v + 2 < dc.Positions.Length
                    lo_x = Math.Min(lo_x, dc.Positions(v))
                    hi_x = Math.Max(hi_x, dc.Positions(v))
                    lo_y = Math.Min(lo_y, dc.Positions(v + 1))
                    hi_y = Math.Max(hi_y, dc.Positions(v + 1))
                    lo_z = Math.Min(lo_z, dc.Positions(v + 2))
                    hi_z = Math.Max(hi_z, dc.Positions(v + 2))
                    v += 3
                End While
                Dim r = Math.Max((hi_x - lo_x) * 0.5F, (hi_z - lo_z) * 0.5F)
                narrow = Math.Min(narrow, r)
                wide = Math.Max(wide, r)
                tallest = Math.Max(tallest, hi_y - lo_y)
                n += 1
            Next
            If n = 0 Then Return t

            t.radius = narrow          ' the trunk
            t.widest = wide            ' the canopy capsule, where there is one
            t.height = tallest
            t.parts = n
            t.ok = True
        Catch ex As Exception
            LogThis("brain: {0} would not parse - {1}", species, ex.Message)
        End Try
        Return t
    End Function

End Module

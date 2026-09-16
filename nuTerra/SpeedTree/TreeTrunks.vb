Imports System.IO

''' <summary>
''' How thick each species' trunk really is, from its own collision hull.
'''
''' THE BAKE USED 0.6 m FOR EVERY SPECIES. MapFlightBake.TRUNK_RADIUS was one
''' constant with an honest comment - "0.6 m clears the thickest trunks on the
''' roster while cutting the limbs" - and it had to be a constant because
''' nothing read the real shape. The owner, 2026-09-16, asked whether we could
''' use actuals. This is the actuals.
'''
''' WHERE IT LIVES: trees have NO .havok. A tree's collision is inside its own
''' .srt and SrtFile already decodes it as PartKind.Collision - "the coarse
''' hull, a handful of capsules built from a few distinct points". So
''' material_kinds.xml naming 71 speedtree_trunk and 72 speedtree_foliage is
''' the game agreeing the two are different things, but the geometry that
''' separates them for a WoT tree is in the .srt.
'''
''' OBJECT SPACE, and that matters. These radii are the .srt's own coordinates,
''' so a placement that scales a tree up scales its trunk with it. The trunk
''' pass compares in the same space for exactly that reason - see
''' sun_depth_tree.vert, which used world metres back when the threshold was
''' one number for the whole map.
'''
''' ONE COPY. Brain Testing measured this for itself first; it now calls here,
''' because two measurements of the same thing drift and this one decides what
''' a tank can drive through.
'''
''' Added 2026-09-16 by nuTerra work.
''' </summary>
Module TreeTrunks

    ''' <summary>What one species' collision hull measures.</summary>
    Public Structure Trunk
        Public species As String
        ''' <summary>Half-width of the NARROWEST collision part in XZ, metres -
        ''' the trunk. See Measure for why the narrowest.</summary>
        Public radius As Single
        ''' <summary>The widest collision part. Equal to radius on a species
        ''' with only one.</summary>
        Public widest As Single
        Public height As Single
        Public parts As Integer
        ''' <summary>False means no collision part was found at all.</summary>
        Public ok As Boolean

        ''' <summary>
        ''' Whether `radius` is really a TRUNK.
        '''
        ''' TRUE NEEDS TWO PARTS OR MORE, and that is the lesson this file cost.
        ''' Splitting a hull into a narrow part and a wide one works when there
        ''' are two: the narrow one is the trunk, the wide one the canopy
        ''' capsule - Linden 0.22 m against 4.22, Cypress 0.21 against 1.70.
        '''
        ''' With ONE part there is nothing to be narrower than, and 'narrowest'
        ''' returns the whole plant: Olive_bush 3.75 m, Eucalyptys_14m 6.29 m,
        ''' Unknown_Bush_Big 3.06 m. Feeding those to the trunk pass blocked
        ''' MORE ground than the 0.6 m ceiling it replaced - measured, trunk
        ''' cells 4,481 -> 10,236 - which is the opposite of using actuals.
        ''' </summary>
        Public trunkKnown As Boolean
    End Structure

    ''' <summary>
    ''' What a species gets when its own hull cannot answer. This is the old
    ''' global ceiling and it keeps its original justification: 0.6 m clears
    ''' the thickest trunks on the roster while cutting the limbs.
    '''
    ''' DECLARED HERE, not read from MapFlightBake, because Brain Testing links
    ''' this file and stubs that one. MapFlightBake.TRUNK_RADIUS now points at
    ''' this, so there is still one number.
    ''' </summary>
    Public Const FALLBACK_RADIUS As Single = 0.6F

    Private ReadOnly cache As New Dictionary(Of String, Trunk)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Every species measured so far, in first-asked order.</summary>
    Public ReadOnly Measured As New List(Of Trunk)

    ''' <summary>Drop the cache. Only needed if the res packages change under
    ''' a running app, which is the mod-folder case.</summary>
    Public Sub Clear()
        cache.Clear()
        Measured.Clear()
    End Sub

    ''' <summary>
    ''' The radius the trunk pass should use for this species, metres.
    '''
    ''' A SPECIES WHOSE TRUNK IS NOT KNOWN FALLS BACK to the old global
    ''' ceiling - either it has no collision hull at all, or it has a single
    ''' part that cannot be split into trunk and canopy. Falling back to zero
    ''' would silently stop it blocking; falling back to the single part's own
    ''' width blocks the whole bush. The ceiling keeps the previous answer,
    ''' which is the only one of the three that is not a new mistake.
    ''' </summary>
    Public Function RadiusOf(species As String) As Single
        Dim t = Measure(species)
        If t.trunkKnown Then Return t.radius

        ''' A SINGLE HULL STILL BOUNDS THE TRUNK. It is the whole plant, so the
        ''' trunk inside it cannot be wider - and where that hull is narrower
        ''' than the ceiling, the ceiling is the looser of the two numbers.
        ''' Taking the smaller is never worse than the fallback and is tighter
        ''' for the thin ones: Cypress_regular_half-small is 0.34 m wide in
        ''' total, and blocking 0.60 m of ground for it was the ceiling talking,
        ''' not the asset.
        If t.ok Then Return Math.Min(t.radius, FALLBACK_RADIUS)
        Return FALLBACK_RADIUS
    End Function

    ''' <summary>
    ''' Measure one species, cached.
    '''
    ''' PER PART, NOT THE UNION, and that is the whole point. Measuring every
    ''' collision part together gives a bush's entire hull and calls it a trunk
    ''' - Olive_bush came out at 3.75 m that way. A species with two parts has
    ''' them separated: the NARROW one is the trunk and the wide one is the
    ''' canopy capsule.
    '''
    ''' LOD 0 ONLY. The same hull is repeated per LOD, and mixing them would
    ''' count one trunk several times and widen nothing.
    ''' </summary>
    Public Function Measure(species As String) As Trunk
        Dim hit As Trunk = Nothing
        If cache.TryGetValue(species, hit) Then Return hit

        Dim t As New Trunk With {.species = species}
        Try
            Dim entry = ResMgr.Lookup(species)
            If entry IsNot Nothing Then
                Dim ms As New MemoryStream()
                entry.Extract(ms)
                Dim srt = SrtFile.FromBytes(ms.ToArray(), species)
                If srt IsNot Nothing AndAlso srt.DrawCalls IsNot Nothing Then
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
                    If n > 0 Then
                        t.radius = narrow
                        t.widest = wide
                        t.height = tallest
                        t.parts = n
                        t.ok = True
                        t.trunkKnown = n >= 2
                    End If
                End If
            End If
        Catch ex As Exception
            LogThis("tree trunks: {0} would not parse - {1}",
                    IO.Path.GetFileName(species), ex.Message)
        End Try

        If Not t.ok Then t.radius = FALLBACK_RADIUS
        cache(species) = t
        Measured.Add(t)
        Return t
    End Function

End Module

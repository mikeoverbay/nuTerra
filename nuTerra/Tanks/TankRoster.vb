Imports OpenTK.Mathematics

''' <summary>
''' Which thirty vehicles are fielded, and how big a hull is.
'''
''' PULLED OUT OF TankRenderer AND TankSim so there is ONE of each. The roster
''' was a literal inside MapTanks.Load and the hull box was computed inline in
''' TankSim.HullRays; Brain Testing needs both, and a second copy of either is
''' a thing that goes stale silently - the roster by drifting from nuTerra's,
''' the box by disagreeing about whether the gun counts.
'''
''' Nothing here knows anything about driving. That is deliberate: Brain
''' Testing links this file and NOT the brain.
'''
''' Extracted 2026-09-15 by nuTerra work, for docs/brain_testing_plan.md.
''' Roster and box rule are Tank AI work's, unchanged - see the notes below,
''' which are theirs.
''' </summary>
Public NotInheritable Class TankRoster

    ''' <summary>
    ''' THE ROSTER IS TIER 10, and it is taken from the package layout rather
    ''' than from a list anyone typed. The game ships vehicle assets in
    ''' vehicles_level_NN packages, so vehicles_level_10*.pkg IS the tier 10
    ''' roster - 137 asset folders, of which 121 have a matching item_def.
    ''' These 30 are round-robined across the nations so neither team is all
    ''' one country.
    '''
    ''' The two halves of the install spell the nations differently: assets use
    ''' american / british / russian, item_defs use usa / uk / ussr. The names
    ''' below are the item_def spelling, because that is what TankVehicle.Load
    ''' wants.
    '''
    ''' THE ORDER IS THE CONTRACT. Teams split at the halfway mark - the first
    ''' fifteen are team 1, the last fifteen team 2 - and slot k within a team
    ''' picks the spawn. Re-ordering this list moves tanks to different spawn
    ''' points, so a run stops comparing with the run before it. Add to the
    ''' END, never in the middle.
    ''' </summary>
    Public Shared ReadOnly ALL As Tuple(Of String, String)() = {
        Tuple.Create("china", "Ch19_121"),
        Tuple.Create("czech", "Cz04_T50_51"),
        Tuple.Create("france", "F108_Panhard_EBR_105"),
        Tuple.Create("germany", "G121_Grille_15_L63"),
        Tuple.Create("italy", "It08_Progetto_M40_mod_65"),
        Tuple.Create("japan", "J16_ST_B1"),
        Tuple.Create("poland", "Pl15_60TP_Lewandowskiego"),
        Tuple.Create("sweden", "S11_Strv_103B"),
        Tuple.Create("uk", "GB100_Manticore"),
        Tuple.Create("usa", "A106_M48A2_120"),
        Tuple.Create("ussr", "R110_Object_260"),
        Tuple.Create("china", "Ch22_113"),
        Tuple.Create("czech", "Cz17_Vz_55"),
        Tuple.Create("france", "F10_AMX_50B"),
        Tuple.Create("germany", "G125_Spz_57_Rh"),
        Tuple.Create("italy", "It15_Rinoceronte"),
        Tuple.Create("japan", "J20_Type_2605"),
        Tuple.Create("poland", "Pl15_60TP_Lewandowskiego_CFE_A"),
        Tuple.Create("sweden", "S16_Kranvagn"),
        Tuple.Create("uk", "GB114_Vickers_MBT_Mk3"),
        Tuple.Create("usa", "A116_XM551"),
        Tuple.Create("ussr", "R119_Object_777C"),
        Tuple.Create("china", "Ch22_113_Beijing_Opera"),
        Tuple.Create("czech", "Cz21_Vz_60S"),
        Tuple.Create("france", "F141_Durendal"),
        Tuple.Create("germany", "G134_PzKpfw_VII"),
        Tuple.Create("italy", "It20_Carro_Combattimento_45t"),
        Tuple.Create("japan", "J35_Ho_Ri_3"),
        Tuple.Create("poland", "Pl21_CS_63"),
        Tuple.Create("sweden", "S28_UDES_15_16")
    }

    ''' <summary>
    ''' HOW A HULL IS POSED, and both apps must agree or their tanks face
    ''' different ways on the same map.
    '''
    ''' MirrorX: BigWorld's X runs opposite to the world frame every consumer
    ''' here uses, so a vehicle is drawn mirrored on X. The arena file's base
    ''' positions carry the same negation - see BrainTanks.world_xz.
    '''
    ''' FlipSkinnedZ: a SKINNED part - one with bone indices - additionally
    ''' flips on Z. Only skinned parts, which is why the test is on the
    ''' layout's bone offset and not on the part's name.
    '''
    ''' Moved here from MapTanks 2026-09-16 so Brain Testing links the same
    ''' two values rather than keeping a second pair that could drift.
    ''' </summary>
    Public Shared MirrorX As Boolean = True
    Public Shared FlipSkinnedZ As Boolean = True

    ''' <summary>Fifteen a side, which is what ALL holds two of.</summary>
    Public Shared ReadOnly Property PerTeamMax As Integer
        Get
            Return ALL.Length \ 2
        End Get
    End Property

    ''' <summary>Half-extents used when a vehicle has no readable hull visual.
    ''' A mid-sized tier 10 is about 7 m by 3.4 m, so 3.5 x 1.7 - big enough
    ''' that a missing box does not let the AI drive through things, small
    ''' enough that it does not wedge in a gap a real hull would clear.</summary>
    Private Const FALLBACK_HX As Single = 1.7F
    Private Const FALLBACK_HY As Single = 1.2F
    Private Const FALLBACK_HZ As Single = 3.5F

    ''' <summary>
    ''' The HULL's box as half-extents in vehicle-local metres: X across, Y up,
    ''' Z along.
    '''
    ''' THE HULL'S BOX, NOT THE VEHICLE'S, and the gun is the reason. A
    ''' TankVehicle is a list of parts and each carries its own visual, so
    ''' "the bounding box" has to say WHICH - a 7 m barrel would push the
    ''' forward face out past the muzzle, and anything using the box to ask
    ''' "what is in front of me" would start its question in mid-air ahead of
    ''' the tank. hull first, chassis as the fallback.
    '''
    ''' The numbers are the GAME'S: TankVisual reads bbMin/bbMax straight out
    ''' of the visual's boundingBox node. Nothing here estimates them.
    ''' </summary>
    Public Shared Function HullHalfExtents(v As TankVehicle) As Vector3
        Dim vis = HullVisual(v)
        If vis Is Nothing Then
            Return New Vector3(FALLBACK_HX, FALLBACK_HY, FALLBACK_HZ)
        End If
        ' Floored rather than trusted flat: a degenerate box - one axis zero,
        ' which a stripped or placeholder visual can carry - would otherwise
        ' hand out a hull with no width and every clearance test would pass.
        Return New Vector3(
            Math.Max(0.5F, (vis.bbMax.X - vis.bbMin.X) * 0.5F),
            Math.Max(0.5F, (vis.bbMax.Y - vis.bbMin.Y) * 0.5F),
            Math.Max(0.5F, (vis.bbMax.Z - vis.bbMin.Z) * 0.5F))
    End Function

    ''' <summary>The hull part's visual, or the chassis', or Nothing.</summary>
    Public Shared Function HullVisual(v As TankVehicle) As TankVisual
        If v Is Nothing OrElse v.parts Is Nothing Then Return Nothing
        For Each want In {"hull", "chassis"}
            For Each pt In v.parts
                If pt.label = want AndAlso pt.visual IsNot Nothing Then Return pt.visual
            Next
        Next
        Return Nothing
    End Function

End Class

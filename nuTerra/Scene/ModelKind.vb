''' <summary>
''' What a model IS, from its asset path, and what colour it is drawn in.
'''
''' LIFTED OUT OF MapFlightBake so a second app can ask the same question
''' and get the same answer. It is pure string work - no GL, no bake state -
''' but it sat in a file full of framebuffers and passes, so anything
''' wanting to classify a name had to drag the whole bake behind it.
'''
''' Brain Testing shades buildings by kind: "shade models by type", the
''' owner, 2026-09-16. It uses THIS table, not one of its own - two palettes
''' for one set of kinds is two legends to learn, and he has been reading
''' this one since Path Studio.
'''
''' MapFlightBake.kind_of still wraps this with its dump bookkeeping; the
''' classification itself is here and there is one of it.
'''
''' Moved 2026-09-16 by nuTerra work, unchanged.
''' </summary>
Public NotInheritable Class ModelKind

    ''' <summary>The keys, and the names that go in the meta so the reading
    ''' side never has to guess what a number means.</summary>
    Public Const KIND_TERRAIN As Byte = 0
    Public Const KIND_BUILDING As Byte = 1
    Public Const KIND_FENCE As Byte = 2
    Public Const KIND_TREE As Byte = 3
    Public Const KIND_ROCK As Byte = 4
    Public Const KIND_PROP As Byte = 5
    Public Const KIND_WATER As Byte = 6
    Public Const KIND_OTHER As Byte = 7
    Public Shared ReadOnly KIND_NAMES() As String = {
        "terrain", "building", "fence", "tree", "rock", "prop",
        "water", "other"}

    ''' <summary>
    ''' The colour each kind is drawn in, written into the meta so every renderer
    ''' reads ONE table.
    '''
    ''' These values are Path Studio's BAKE_KIND_RGB verbatim, because that is
    ''' the legend the owner has been reading all along and a palette that
    ''' changed appearance the day it moved would be a bug dressed as a feature.
    ''' Ownership moved here - "your job is also to produce the height map and
    ''' colour type" - and Path Studio reads the meta with its own table as the
    ''' fallback for a bake written before the keys existed.
    '''
    ''' INDEX 0 IS DELIBERATELY UNUSED. Terrain is the ground, not a thing
    ''' standing on it, and nothing renders it from this table - giving it a
    ''' colour here would invent a convention neither renderer asked for.
    ''' </summary>
    Public Shared ReadOnly KIND_RGB()() As Byte = {
        Nothing,
        New Byte() {205, 150, 40},    ' building
        New Byte() {230, 90, 40},     ' fence / rail
        New Byte() {70, 160, 70},     ' tree / bush
        New Byte() {120, 130, 150},   ' rock
        New Byte() {170, 110, 200},   ' vehicle / prop
        New Byte() {50, 110, 200},    ' water
        New Byte() {200, 200, 200}}   ' other

    Public Shared Function classify(p As String) As Byte

        ' BEFORE THE FENCE TEST, and it has to be, because the fence test
        ' matches the FOLDER and not just the file. The owner, 2026-09-13:
        ' "the entire olive garden area is mostly marked in black. Those should
        ' be crushable."
        '
        ' The vineyard on monastery is two assets. The vines are SpeedTree -
        ' vegetation/Broadleaves/GrapeVine_01.srt - and were always keyed tree
        ' by the tree pass, which writes a constant. The TRELLIS is a model:
        '
        '     content/GatesAndFences/gaf_19_05_GrapevineFence/normal/lod0/...
        '
        ' and "GatesAndFences" alone matches both `fence` and `gate`, so every
        ' asset under that folder keyed KIND_FENCE whatever its name. Crushable
        ' is "kind = tree AND NOT solid", so a fence-keyed texel can never be
        ' crushed - and a vineyard is a GRID of these, which turned the whole
        ' field into an obstacle a tank would drive around.
        '
        ' I HAD THIS WRONG IN WRITING, below, listing GrapevineFence among names
        ' that "are all fences - correct". Correct about the NAME and wrong about
        ' the consequence: a wooden grape trellis is not a barrier. Its own havok
        ' proxy is named __n_wood0.
        '
        ' Narrow on purpose, like the lamp line. `grapevine` matches exactly one
        ' model family in all 218 packages - gaf_19_05_GrapevineFence - so this
        ' cannot reach a real fence. It does not touch `vine` on its own, which
        ' still falls through to the tree test below where it belongs.
        If has(p, "grapevine") Then Return KIND_TREE

        If has(p, "fence", "zabor", "ograda", "rail", "hedge",
               "gate", "wire", "palisade") Then Return KIND_FENCE
        ' BEFORE the tree test, and this is the whole reason it exists:
        ' "StreetLamp" CONTAINS "tree". s-t-r-e-e-t. Both of monastery's street
        ' lamps keyed as TREE until this line, which means a vehicle rule of the
        ' form "a tank crushes trees" drove through the lamp posts.
        '
        ' Narrow on purpose. The tempting fix is to require a word boundary
        ' around every keyword, and that is a REGRESSION: 23 of monastery's 212
        ' names match a keyword buried inside a longer word and most of them are
        ' right anyway - WoodFence, StoneFence, ForgedFence and RabitzFence are
        ' all fences; ItalyOutlandHousesCluster is a building;
        ' (GrapevineFence was in that list and should not have been - see the
        ' grapevine line above. It is a trellis, and the owner wants it
        ' crushable. The survey was right that the NAME says fence and wrong
        ' that the name settles it.)
        ' VendorCart and WoodenCart reach prop through "car" and a cart IS a
        ' prop. A boundary rule breaks fifteen correct answers to fix one wrong
        ' one. So the fix is the one keyword that actually collides.
        '
        ' "lamp" appears in exactly two names on this map and both are lamps.
        ' No name contains "light" or "lantern", so neither is added on
        ' speculation - add them when a map produces one.
        If has(p, "lamp") Then Return KIND_PROP

        If has(p, "tree", "bush", "foliage", "vine") Then Return KIND_TREE
        If has(p, "rock", "stone", "cliff", "boulder") Then Return KIND_ROCK
        If has(p, "building", "house", "church", "barn", "ruin",
               "industrial", "_bld_") Then Return KIND_BUILDING
        If has(p, "vehicle", "wreck", "tank", "car", "truck", "prop",
               "misc", "barrel", "crate") Then Return KIND_PROP
        Return KIND_OTHER
    End Function

    Private Shared Function has(p As String, ParamArray keys() As String) As Boolean
        For Each k In keys
            If p.Contains(k) Then Return True
        Next
        Return False
    End Function

End Class

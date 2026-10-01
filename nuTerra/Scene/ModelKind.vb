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
''' this one since Flight Studio.
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
    ''' The bits one of the eight keys occupies: three, so &H7.
    '''
    ''' HERE RATHER THAN IN MapFlightBake, which is where it used to live and
    ''' which now forwards to this. Eight kinds need three bits - that is a
    ''' fact about the numbering above, not about the bake's key byte, and a
    ''' mask that lives apart from the numbers it masks cannot be kept honest
    ''' when one of them changes.
    '''
    ''' The practical reason is the same one TreeTrunks.vb gives for declaring
    ''' its own radius: Brain Testing links THIS file and does not link
    ''' MapFlightBake, so anything a linker of this file needs has to be
    ''' reachable from it. On 2026-09-21 a dump in modSpaceBin.vb reached for
    ''' MapFlightBake.KIND_MASK and broke that build - modSpaceBin is linked
    ''' too, and pulling MapFlightBake in cascades to GLFramebuffer. Reported
    ''' by PathFinder, whose read of it was exactly right.
    ''' </summary>
    Public Const KIND_MASK As Byte = &H7

    ''' <summary>
    ''' THE TALLEST THING A TANK DRIVES OVER, metres. One definition.
    '''
    ''' It existed twice until 2026-09-24: TankNavLimits.MAX_OBSTACLE and
    ''' MapFlightBake.OBSTACLE_MIN_H, both 1.0, joined by a comment reading
    ''' "Matches the bake's own OBSTACLE_MIN_H so 'blocked' means one thing
    ''' across the app". That is a promise a human keeps, which is the kind
    ''' DIRECTIVES rule 13 is about - and the kind that let 0.7 survive in two
    ''' places for three days. Both now forward here.
    '''
    ''' HERE rather than with either of them because this file is the one every
    ''' build compiles: Brain Testing links ModelKind and links neither TankNav
    ''' nor MapFlightBake, and the classifier below needs the number.
    ''' </summary>
    Public Const DRIVE_OVER_MAX_H As Single = 1.0F

    ''' <summary>
    ''' The colour each kind is drawn in, written into the meta so every renderer
    ''' reads ONE table.
    '''
    ''' These values are Flight Studio's BAKE_KIND_RGB verbatim, because that is
    ''' the legend the owner has been reading all along and a palette that
    ''' changed appearance the day it moved would be a bug dressed as a feature.
    ''' Ownership moved here - "your job is also to produce the height map and
    ''' colour type" - and Flight Studio reads the meta with its own table as the
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

    ''' <summary>What the game does to a model when it is hit, as space.bin's
    ''' model_info_items states it. HIT_UNKNOWN is this app's own, for a model
    ''' the space did not describe.</summary>
    Public Const HIT_STATIC As Byte = 0
    Public Const HIT_FALLING As Byte = 1
    Public Const HIT_FRAGILE As Byte = 2
    Public Const HIT_UNKNOWN As Byte = 255

    ''' <summary>
    ''' How tall a DESTRUCTIBLE thing may be and still be driven through.
    '''
    ''' A CHOSEN NUMBER, not a derived one, and worth saying so plainly. It is
    ''' the same 3.5 m as MapFlightBake.MIN_CLEARANCE by coincidence of value
    ''' and not of meaning - that one is the gap a hull needs to pass UNDER an
    ''' arch - so the two are deliberately separate constants. Tying them
    ''' together would be a false derivation that the next person has to unpick.
    '''
    ''' Measured on monastery, against the models the name cannot key at all.
    ''' Under the line, and now crushable: field junk 2.05, washing lines 2.15,
    ''' cafe tables 2.95, a bicycle 2.04, jug scatters 2.81, benches 1.92,
    ''' furniture 1.51, baskets 1.77, milk cans 1.49, flowerbeds 1.74, crates
    ''' 2.47, a boat 2.10, a Dodge 2.19, a Gaz 1.76. Over it, and still
    ''' blocking: a crypt 6.64, a pier 4.58, scaffolding 8.38.
    '''
    ''' THE MARGINAL PAIR IS A WELL AT 3.19 AND A FOUNTAIN AT 2.98, both stone
    ''' and both now crushable. 7 square metres each, and named here rather
    ''' than buried so the owner can overrule them without re-deriving which
    ''' they were.
    '''
    ''' Safe in the direction it is used because MODEL_HULL_H is the whole
    ''' model's bounding box, an UPPER bound on any real height: under the line
    ''' can only admit things genuinely shorter.
    ''' </summary>
    Public Const CRUSH_MAX_H As Single = 3.5F

    ''' <summary>
    ''' The kind, with the space's own verdict allowed to break a tie the NAME
    ''' cannot. Use this rather than classify wherever the facts are to hand.
    '''
    ''' The owner, 2026-09-23, relayed through PathFinder: "get nuterra to fix
    ''' the filtering of blockers, that's its job." The case he picked was
    ''' hd_env_UNI_622_FieldJunk - 35 instances, a 3.3 x 2.9 m pile 1.25 m high
    ''' that stopped a run three metres short of its goal. It matches no
    ''' keyword, so it fell to OTHER, and OTHER is not crushable.
    '''
    ''' ONLY WHEN THE NAME SAYS NOTHING. A name that keys building, rock, fence,
    ''' tree or prop keeps that answer untouched - which is what keeps the
    ''' Basilica blocking, because it is FRAGILE too. The space marks a thing
    ''' destructible if a shell can destroy it, and a 30 m basilica qualifies;
    ''' destructible is not drivable, and a rule that read the tag alone would
    ''' have opened every house on the map. The tag only decides cases the
    ''' keyword race was going to lose anyway.
    '''
    ''' WHY THIS BEATS MORE KEYWORDS. 132 of monastery's 246 built models key as
    ''' OTHER - over half have no kind at all - and each keyword added to win
    ''' one of them is a substring that can lose somewhere else. "StreetLamp"
    ''' contains "tree" and cost the lamp posts; "GatesAndFences" cost the
    ''' vineyard. The space.bin tag is not a guess about a string, so it does
    ''' not have that failure mode.
    '''
    ''' FALLING IS UNCONDITIONAL, fragile is not. Falling means the thing topples
    ''' when struck - on this map that is every pole and street lamp, 104
    ''' placements, nothing else - and a hull pushes those over whatever their
    ''' height. Fragile only means it can be broken, so it is held to
    ''' CRUSH_MAX_H.
    '''
    ''' PURE, and it has to stay that way: Brain Testing links this file and not
    ''' the space.bin reader's callers, so the facts arrive as arguments rather
    ''' than being looked up here.
    ''' </summary>
    ''' <summary>
    ''' THE COLLISION HULL OVERRIDES THE NAME, AND EVERYTHING ELSE.
    '''
    ''' The owner, 2026-09-24: "We want to remove what we can drive over." The
    ''' object that finally showed what that meant was
    ''' hd_env441_flora_debris_01, which stopped six runs at (81.50, 39.50):
    ''' leaf litter, 4.37 x 4.72 m wide and a COLLISION HULL 0.18 m TALL, that
    ''' the bake measured at 2.61 m on one texel out of the nine in the cell.
    '''
    ''' THE BAKE RASTERISES WHAT YOU SEE; THE GAME COLLIDES WITH THE HULL. When
    ''' those two disagree the hull wins, because it is what the engine itself
    ''' drives a tank through. A visual mesh can carry a standing branch, a
    ''' tent pole or a flower basket that no collision geometry backs, and
    ''' top-minus-floor cannot tell that from a wall.
    '''
    ''' Sound because MODEL_HULL_H is the whole model's bounding box - an UPPER
    ''' bound - so 'no taller than flat_h' cannot be true of a model with a real
    ''' obstacle in it anywhere.
    '''
    ''' Measured on monastery: 18 models, 872 texels, 25 square metres. Small
    ''' area and the wrong metric for it - these are 5- and 20-texel spikes
    ''' standing in open ground, and one of them cost six runs in seven minutes.
    ''' A cliff blocks where a driver expects a cliff; this does not.
    '''
    ''' BEFORE the name test, unlike the destructible rule below, because a name
    ''' keying rock or building does not make a 0.2 m thing climbable. hull_h = 0
    ''' means no hull was stated, and then nothing here applies.
    ''' </summary>
    Public Shared Function resolve(p As String, hit As Byte, hull_h As Single) As Byte
        If hull_h > 0.0F AndAlso hull_h <= DRIVE_OVER_MAX_H Then Return KIND_PROP

        Dim k = classify(p)
        If k <> KIND_OTHER Then Return k
        If hit = HIT_FALLING Then Return KIND_PROP
        ' hull_h = 0 is 'no collision hull stated', not 'flat' - a model whose
        ' height is unknown keeps the blocking answer rather than guessing.
        If hit = HIT_FRAGILE AndAlso hull_h > 0.0F AndAlso hull_h < CRUSH_MAX_H Then
            Return KIND_PROP
        End If
        Return k
    End Function

    Public Shared Function classify(p As String) As Byte

        ' FIRST OF ALL, because a particle model's path carries the name of
        ' whatever it burns on and every other test below would match that
        ' instead. Measured on monastery: 20 placements of pure effect
        ' geometry were baked as world collision, and the largest of them -
        ' a 21 m fire column - keyed BUILDING, because its path is
        ' particles/.../19_monastery/bld_19_01_Vhouse_05/Fire_monastery_big.
        ' A fire does not stop a tank. Between them the smoke and fire
        ' models blocked 64 square metres of ground.
        '
        ' PROP RATHER THAN A NEW KIND, and that is a compromise worth naming.
        ' The honest key would be "does not collide", which the byte has no
        ' room for and the format does not define; prop is the nearest kind
        ' that is already crushable everywhere, so an effect stops blocking
        ' without inventing a key every consumer would have to learn. The
        ' cost is that Brain Testing draws a fire in the prop colour.
        '
        ' Anchored on the FOLDER, not a word: these all live under
        ' particles/content_deferred/GFX_models. Nothing else on any map
        ' reaches that path, so this cannot catch real geometry.
        If has(p, "particles/content_deferred") Then Return KIND_PROP

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

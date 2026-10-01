Imports System.Xml
Imports OpenTK.Mathematics

''' <summary>How a gun's magazine behaves - see TankVehicle.magazine.</summary>
Public Enum MagazineKind
    OneShot = 0
    Clip = 1
    AutoReloader = 2
End Enum

''' <summary>One part of a vehicle: hull, chassis, turret or gun, with its meshes, visual, textures and local offset.</summary>
Public Class TankPart
    Implements IDisposable
    Public label As String
    Public primitivesPath As String
    Public visualPath As String
    Public offset As Vector3          ' part origin relative to the chassis origin, BigWorld axes
    Public meshes As New List(Of TankMesh)
    ''' <summary>
    ''' THIS PART'S ARMOUR, millimetres, keyed by plate name.
    '''
    ''' armor_1, armor_2, ... on hull, turret and gun; leftTrack and rightTrack
    ''' on the chassis; the gun also carries a plate literally called `gun`.
    ''' Read off the module the most costly rule chose, like every other
    ''' per-module number in this file.
    '''
    ''' DATA RATHER THAN A LOG LINE, which is the whole reason it is here. The
    ''' owner, 2026-09-28: "I wanted this integrated in to braintesting. Not
    ''' nuTerra. All tank AI will be done there first." Brain Testing compiles
    ''' THIS file, so a field on TankPart reaches it; the probe that printed the
    ''' same numbers into nuTerra's log reached nobody.
    '''
    ''' THE NUMBERING IS SPARSE - the 121's hull has no armor_7 and no armor_9 -
    ''' so this is a dictionary, not an array. Do not index it 1..N.
    '''
    ''' Thickness is half the answer. WHICH TRIANGLE IS WHICH PLATE is in the
    ''' part's .havok, as an hknpMaterial named for the plate - see havokPath
    ''' and TankHavok.
    ''' </summary>
    Public ReadOnly armorMm As New Dictionary(Of String, Single)

    ''' <summary>
    ''' This part's collision geometry: collision_client/&lt;part&gt;.havok.
    '''
    ''' NOT the path item_defs states. hitTester names a .model under collision/
    ''' and collision_client/ and NEITHER EXISTS - what ships is one .havok per
    ''' part, client side only. Derived by swapping the extension, and left empty
    ''' when even that is not in the vehicle packages.
    ''' </summary>
    Public havokPath As String = ""

    ''' <summary>Mean road-wheel radius, computed once from the rig.</summary>
    Public meanRoadRadius As Single
    Public visual As TankVisual

    ''' <summary>The material a mesh draws with: matched by the render set's vertices name, else the first.</summary>
    ''' <summary>
    ''' The bone palette for a mesh, in PALETTE ORDER - the order a vertex's
    ''' iii byte indexes after the divide by three.
    '''
    ''' Matched to the renderSet the same way MaterialFor does it, by the
    ''' vertices section name, because that is the only link between a mesh
    ''' out of the primitives file and a renderSet out of the visual.
    ''' Nothing means the mesh is not skinned and the shader uses an identity
    ''' skin - hull and turret on most tanks.
    ''' </summary>
    Public Function PaletteFor(m As TankMesh) As List(Of String)
        If visual Is Nothing Then Return Nothing
        Dim want = If(m.name = "", "vertices", m.name & ".vertices")
        For Each rs In visual.renderSets
            If rs.verticesName = want AndAlso rs.nodes.Count > 0 Then Return rs.nodes
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' EVERY material for a mesh, one per primitive group, in group order.
    '''
    ''' The link is POSITIONAL and that is not an accident: TankVisual appends
    ''' materials while walking geometry/primitiveGroup in document order, and
    ''' the groups come out of the primitives file in the same order, so
    ''' materials(k) is the material of group k. TankMesh.Group carries no
    ''' material index of its own - there is nothing in the format to carry -
    ''' so the two lists agreeing by position is the whole mapping.
    '''
    ''' Most meshes have one group and one material and none of this shows. It
    ''' shows on the EBR, which carries its wheels in the chassis mesh as a
    ''' second group with a tyre material, and on anything else that packs two
    ''' surfaces into one section.
    ''' </summary>
    Public Function MaterialsFor(m As TankMesh) As List(Of TankMaterial)
        If visual Is Nothing Then Return Nothing
        Dim want = If(m.name = "", "vertices", m.name & ".vertices")
        For Each rs In visual.renderSets
            If rs.verticesName = want AndAlso rs.materials.Count > 0 Then Return rs.materials
        Next
        Return Nothing
    End Function

    Public Function MaterialFor(m As TankMesh) As TankMaterial
        If visual Is Nothing Then Return Nothing
        Dim want = If(m.name = "", "vertices", m.name & ".vertices")
        For Each rs In visual.renderSets
            If rs.verticesName = want AndAlso rs.materials.Count > 0 Then Return rs.materials(0)
        Next
        If visual.renderSets.Count > 0 AndAlso visual.renderSets(0).materials.Count > 0 Then Return visual.renderSets(0).materials(0)
        Return Nothing
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each m In meshes
            m.Dispose()
        Next
        meshes.Clear()
    End Sub
End Class

Public Enum TankTeam
    Green = 0
    Red = 1
End Enum

''' <summary>A placed vehicle: where, which way it faces, and whose it is.</summary>
Public Class TankInstance
    Public vehicle As TankVehicle
    Public position As Vector3
    Public headingRad As Single
    Public team As TankTeam = TankTeam.Green
    Public label As String = ""

    ''' <summary>The number on the marker: 1..15 within a team, so the
    ''' two sides read as two teams rather than as one list of thirty.</summary>
    Public id As Integer

    ''' <summary>
    ''' Condition, 0..1, for the two bars on the marker.
    '''
    ''' NOTHING SIMULATES THESE YET - there is no damage model, and these
    ''' are the hooks it will write to. Full by default; TANK_HP_DEMO drives
    ''' them with an obvious sawtooth so the bars can be seen working, and
    ''' turning it off leaves every tank at 100% until something real sets
    ''' them. Whatever that turns out to be only has to assign here.
    ''' </summary>
    Public hullHp As Single = 1.0F
    Public crewHp As Single = 1.0F

    ''' <summary>This gun's own recoil cycle. Per INSTANCE rather than per
    ''' vehicle: two tanks of the same model fire independently, and the
    ''' offset is read once per draw.</summary>
    Public ReadOnly recoil As New TankRecoil

    ''' <summary>This tank's own shots. Per INSTANCE so a fast gun can only
    ''' starve itself, and so each shot keeps its own gun's timing.</summary>
    Public ReadOnly shots As New TankShotPool

    ''' <summary>Where this one is going and how fast - see TankDrive.</summary>
    Public ReadOnly drive As New TankDrive

    ''' <summary>Metres this hull has travelled, ever. The wheels turn and the
    ''' track band scrolls off it, so it is PER TANK: one shared accumulator
    ''' made every track on the map scroll at the speed of whichever tank the
    ''' shuttle happened to be moving.</summary>
    Public trackDistance As Single

    ''' <summary>Shells ready to fire. One for a plain gun; up to clipCount
    ''' for a magazine.</summary>
    Public shells As Integer = 1

    ''' <summary>
    ''' Seconds until the next shell is back IN THE MAGAZINE, and what that
    ''' wait started from.
    '''
    ''' NOT the same clock as shotCoolS below, and keeping them apart is the
    ''' whole of what makes a magazine gun behave. A clip gun spends four
    ''' rounds two seconds apart and then waits half a minute; if one timer
    ''' carried both, the two-second gap between rounds would count as the
    ''' reload and the magazine would refill after it - a 390 mm autoloader
    ''' firing continuously.
    '''
    ''' reloadFullS is kept rather than recomputed: an autoreloader's next
    ''' shell costs a different amount from the last one, so a bar that asked
    ''' the vehicle for its own denominator would rescale halfway through.
    ''' </summary>
    Public reloadLeftS As Single
    Public reloadFullS As Single = 1.0F

    ''' <summary>Seconds until the gun may fire AGAIN - the cadence inside a
    ''' magazine. Zero on a plain gun, which has nothing between its
    ''' shots but the reload.</summary>
    Public shotCoolS As Single
    Public shotCoolFullS As Single = 1.0F

    ''' <summary>Seconds since this gun last fired - the number on the card
    ''' while it is reloading.</summary>
    Public sinceShotS As Single

    ''' <summary>Whether the trigger would do anything: a shell in the
    ''' magazine AND the gun over its last shot.</summary>
    Public ReadOnly Property ready As Boolean
        Get
            Return shells > 0 AndAlso shotCoolS <= 0.0F
        End Get
    End Property

    ''' <summary>
    ''' How close the gun is to being able to shoot, 0..1, for the bar.
    '''
    ''' ONE BAR OFF WHICHEVER CLOCK IS ACTUALLY HOLDING THE GUN. With a
    ''' shell in the magazine that is the cadence; with none it is the
    ''' reload. Showing the reload in both cases would leave a clip gun's
    ''' bar pinned full through the four seconds it cannot fire, and showing
    ''' the cadence in both would say a dry gun is nearly loaded.
    ''' </summary>
    Public ReadOnly Property readyFrac As Single
        Get
            If shells > 0 Then
                If shotCoolS <= 0.0F Then Return 1.0F
                Return clamp01(1.0F - shotCoolS / Math.Max(shotCoolFullS, 0.001F))
            End If
            Return clamp01(1.0F - reloadLeftS / Math.Max(reloadFullS, 0.001F))
        End Get
    End Property

    Private Shared Function clamp01(f As Single) As Single
        Return Math.Min(Math.Max(f, 0.0F), 1.0F)
    End Function

    ''' <summary>Seconds until this gun fires on the free-running cadence.
    ''' Seeded apart per tank so a line of them ripples rather than
    ''' volleying - thirty barrels moving as one reads as a glitch.</summary>
    Public fireIn As Single

    ''' <summary>Where the vehicle actually is this frame - the parked spot
    ''' walked along the shuttle. Written by the renderer before anything reads
    ''' it, because a shot has to test the tank where it is standing now.</summary>
    Public livePosition As Vector3

    ''' <summary>Where the turret and gun are pointing, degrees, and which end
    ''' of their travel each is heading for. Positive pitch is UP.</summary>
    Public turretYaw As Single
    Public gunPitch As Single
    Public yawToMax As Boolean = True
    Public pitchToMax As Boolean = True

    ''' <summary>Seconds still to wait at the end of a traverse. A turret that
    ''' turns straight round again reads as a twitch, and on a casemate with
    ''' three degrees of travel it is nothing else.</summary>
    Public aimHold As Single
End Class

''' <summary>
''' A vehicle assembled from its item_defs XML: the priciest chassis, turret and
''' gun (the owner's rule - the most expensive is the best in the game), the
''' hull, and where each sits. Model paths in the XML end in .model; the files
''' that ship are .primitives_processed and .visual_processed.
''' </summary>
Public Class TankVehicle
    Implements IDisposable

    Public nation As String
    Public tag As String
    Public parts As New List(Of TankPart)

    Private _topY As Single = Single.MinValue

    ''' <summary>
    ''' The highest point on the vehicle, metres above its own origin.
    '''
    ''' Each part's visual carries the bounding box the exporter wrote, so
    ''' this is the box the game itself uses rather than a measurement of the
    ''' vertices. Taken over the parts AT THEIR OFFSETS - the turret's box is
    ''' in turret space and is metres below the hull's until the offset is
    ''' added, which would put a marker inside the roof of everything with a
    ''' tall turret.
    '''
    ''' Computed once on first ask: the parts do not move relative to each
    ''' other, so this is a property of the vehicle and not of the frame.
    ''' </summary>
    ''' <summary>
    ''' The whole vehicle's box, in its own frame, from the parts' own bounding
    ''' boxes at their offsets.
    '''
    ''' What a shot tests against. The boxes come from the visuals, so this is
    ''' the box the game uses rather than a measurement of the vertices, and
    ''' taking them AT THEIR OFFSETS matters for the same reason topY needs it:
    ''' a turret's box is in turret space and sits metres below the hull until
    ''' the offset is added.
    ''' </summary>
    Public ReadOnly Property boundsMin As Vector3
        Get
            ensure_bounds()
            Return _bbMin
        End Get
    End Property
    Public ReadOnly Property boundsMax As Vector3
        Get
            ensure_bounds()
            Return _bbMax
        End Get
    End Property
    Private _bbMin As Vector3, _bbMax As Vector3
    Private _bbDone As Boolean

    Private Sub ensure_bounds()
        If _bbDone Then Return
        _bbDone = True
        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        For Each p In parts
            If p.visual Is Nothing Then Continue For
            Dim a = p.offset + p.visual.bbMin
            Dim b = p.offset + p.visual.bbMax
            lo = New Vector3(Math.Min(lo.X, a.X), Math.Min(lo.Y, a.Y), Math.Min(lo.Z, a.Z))
            hi = New Vector3(Math.Max(hi.X, b.X), Math.Max(hi.Y, b.Y), Math.Max(hi.Z, b.Z))
        Next
        ' A vehicle whose visuals gave nothing still needs a box a shot can
        ' miss rather than one that swallows the map.
        If lo.X > hi.X Then
            lo = New Vector3(-1.5F, 0.0F, -3.5F)
            hi = New Vector3(1.5F, 2.5F, 3.5F)
        End If
        _bbMin = lo
        _bbMax = hi
    End Sub

    Public ReadOnly Property topY As Single
        Get
            If _topY = Single.MinValue Then
                Dim t = 0.0F
                For Each p In parts
                    If p.visual Is Nothing Then Continue For
                    Dim y = p.offset.Y + p.visual.bbMax.Y
                    If y > t Then t = y
                Next
                _topY = t
            End If
            Return _topY
        End Get
    End Property
    Public hullPosition As Vector3
    ''' <summary>
    ''' THE TURRET AND GUN PARTS, AND THE MATRIX THAT POSES THEM. One copy.
    '''
    ''' Added 2026-09-25 for Brain Testing, which links THIS file and does not
    ''' link TankRenderer, where the pose lived and still ran. The precedent is
    ''' DrawDepth, written inside the renderer at the Shader IDE session's
    ''' request for the same reason: "One place computes a tank's pose;
    ''' everything that draws it asks here." A second copy of the composition
    ''' drifts from the tank it belongs to the moment one of them changes.
    '''
    ''' THE PIVOT IS THE PART'S OWN offset, NOT turretPosition. Those two
    ''' differ and the difference is a whole hull offset: turretPosition is the
    ''' raw hull-local number out of item_defs, while part.offset is the
    ''' ACCUMULATED chassis-space origin - see the loader below, which builds it
    ''' as hullOff + turretPosition and then gunOff as turretOff +
    ''' gunPosition. Rotating about turretPosition puts the turret's axis
    ''' wherever the hull happens to sit above the chassis. Ask TurretPivot.
    ''' </summary>
    Public Function PartByLabel(want As String) As TankPart
        If parts Is Nothing Then Return Nothing
        For Each p In parts
            If p.label = want Then Return p
        Next
        Return Nothing
    End Function

    Public ReadOnly Property TurretPart As TankPart
        Get
            Return PartByLabel("turret")
        End Get
    End Property

    Public ReadOnly Property GunPart As TankPart
        Get
            Return PartByLabel("gun")
        End Get
    End Property

    ''' <summary>The point the turret yaws about, chassis space. Vector3.Zero
    ''' on a turretless vehicle, which is what the renderer has always done
    ''' rather than refuse to draw it.</summary>
    Public ReadOnly Property TurretPivot As Vector3
        Get
            Dim t = TurretPart
            Return If(t Is Nothing, Vector3.Zero, t.offset)
        End Get
    End Property

    ''' <summary>
    ''' Where a part sits relative to the vehicle, with the turret yawed and the
    ''' gun pitched. Multiply by the vehicle's world matrix to draw it.
    '''
    ''' Degrees in, to match turretYaw and gunPitch on TankInstance. The gun's
    ''' pitch is NEGATED here and that is not a tidy-up to make later: the
    ''' sign convention is the one the barrel is actually drawn with.
    '''
    ''' The gun composes through the TURRET's pivot, not its own - it has to
    ''' yaw with the turret before it pitches, so the translation is
    ''' part.offset MINUS the turret pivot, pitched, then yawed, then put back.
    '''
    ''' NO TANK_AIM TEST IN HERE. That global is a nuTerra render toggle, and a
    ''' shared function that silently returned bind pose because another app's
    ''' switch was off would be a trap. Pass 0, 0 for bind pose; the renderer
    ''' keeps its own check.
    ''' </summary>
    Public Function PartLocal(part As TankPart, turretYaw As Single,
                              gunPitch As Single) As Matrix4
        If part Is Nothing Then Return Matrix4.Identity
        Dim ya = MathHelper.DegreesToRadians(turretYaw)

        If part.label = "turret" Then
            Return Matrix4.CreateRotationY(ya) *
                   Matrix4.CreateTranslation(part.offset)
        End If

        If part.label = "gun" Then
            Dim tOff = TurretPivot
            Return Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-gunPitch)) *
                   Matrix4.CreateTranslation(part.offset - tOff) *
                   Matrix4.CreateRotationY(ya) *
                   Matrix4.CreateTranslation(tOff)
        End If

        Return Matrix4.CreateTranslation(part.offset)
    End Function

    ''' <summary>
    ''' THE BARREL AND ITS AMMUNITION, off the gun the most costly rule chose.
    '''
    ''' The owner, 2026-09-25: "get the specs on barrels and such using most
    ''' costly rule." Priciest already picked the chassis, then the turret,
    ''' then the gun inside it - what was missing is that almost nothing was
    ''' READ off that gun. Two numbers were hardcoded and both were wrong:
    '''
    '''   shell speed     one global 180 m/s for the whole roster. The real
    '''                   figures are 1115 m/s on the 122 mm and 1400 on the
    '''                   100 mm - out by six to eight times.
    '''   recoil travel   a fixed 0.40 m for every gun. The guns
    '''                   state 0.42 and 0.22, so one was near enough and the
    '''                   other was double.
    '''
    ''' Measured before any of this was written, by dumping the chosen gun's
    ''' own XML - see DumpGunSpec. The item_defs are packed binary and the
    ''' first attempt to read them printed nothing at all, so nothing here is
    ''' from a remembered schema.
    ''' </summary>
    ''' <summary>
    ''' Recoil travel for a gun whose file states no amplitude, metres.
    '''
    ''' HERE, NOT IN TankRecoil, and the distinction is sharper than it looks:
    ''' Brain Testing compiles this file and STUBS TankRecoil in BrainStubs.vb.
    ''' Naming the CLASS is fine - TankInstance.recoil does it and their stub
    ''' supplies one - but naming a MEMBER their stub lacks breaks their build,
    ''' which is exactly what TankRecoil.MAX_TRAVEL_M did here for one compile.
    ''' That constant now forwards to this one.
    '''
    ''' The value is TEPY's 0.40 - what reads as a recoil at 60 fps without the
    ''' barrel appearing to leave the mantlet. Real guns state 0.22 to 0.50, so
    ''' it lands mid-range by luck rather than by derivation.
    ''' </summary>
    Public Const RECOIL_TRAVEL_FALLBACK_M As Single = 0.40F

    Public gunPriceCredits As Single       ' what made this the priciest gun
    Public gunImpulse As Single            ' <impulse>, the shove on the hull
    Public recoilTravelM As Single         ' <recoil><amplitude>, metres
    Public recoilEffectName As String = ""  ' <recoil><recoilEffect>
    Public muzzleBrake As String = ""      ' <muzzleBrake>, HORIZONTAL etc

    ''' <summary>
    ''' The shell this gun actually loads, and what it does.
    '''
    ''' WHICH SHELL IS A CHOICE, and it is defaultPortion's, not price's. A
    ''' gun lists three and none of them carries a &lt;price&gt;, so the most
    ''' costly rule has nothing to compare - it picked the GUN and stops there.
    ''' defaultPortion is the game's own answer to 'what is in the racks': 0.9
    ''' on the 122 mm's AP and 1.0 on the 100 mm's, with the HEAT and HE at 0.
    ''' Taking the largest portion is therefore what the tank fires, which is
    ''' the question being asked. Naming it here so it can be overruled without
    ''' anyone having to rediscover that shells have no price.
    ''' </summary>
    Public shellName As String = ""
    Public shellSpeedMps As Single         ' <speed>, 900..1400 typically
    Public shellGravity As Single          ' <gravity>, 9.81 on every one seen
    Public shellMaxDistanceM As Single     ' <maxDistance>, 720 on every one seen
    Public shellPiercing As Single         ' <piercingPower>, first of the pair

    Public turretPosition As Vector3   ' hull-local
    Public gunPosition As Vector3      ' turret-local

    ''' <summary>
    ''' How far the gun elevates and depresses, AS A FUNCTION OF TURRET YAW.
    '''
    ''' pitchLimits is not two numbers, it is two CURVES - pairs of (fraction of
    ''' a full turret turn, degrees at that yaw). A tank's gun cannot depress
    ''' over its own engine deck, and the file says so: the 121 ships
    ''' maxPitch "0 5  0.366894 5  0.430556 1  0.569444 1  0.633106 5  1 5",
    ''' which is five degrees of depression everywhere except the rear arc,
    ''' where it pinches to one. Taking the most permissive sample - which is
    ''' what TEPY does - throws that away and lets the barrel sink into the
    ''' hull behind the turret.
    '''
    ''' Stored raw, as the file's own (x, y) pairs. See PitchRangeAt.
    ''' </summary>
    Public pitchUpCurve As Single()      ' the file's minPitch
    Public pitchDownCurve As Single()    ' the file's maxPitch

    ''' <summary>Traverse limits in degrees. A turret with none gets the full
    ''' circle; a casemate like the Strv 103B ships "-3 3".</summary>
    Public yawMin As Single = -180.0F
    Public yawMax As Single = 180.0F

    ''' <summary>Traverse speed, degrees per second, from the turret's own
    ''' rotationSpeed. Every stock turret ships one and they differ widely -
    ''' 18 on the slowest here, 66 on the fastest - which is most of what makes
    ''' thirty sweeping turrets still read as thirty vehicles.</summary>
    Public yawRate As Single = 40.0F

    ''' <summary>
    ''' Elevation speed, degrees per second. NOT FROM THE FILE - a gun entry has
    ''' no rotationSpeed, because WoT does not model elevation as a rate at all;
    ''' the gun tracks the reticle and aimingTime covers the settling. 20 is a
    ''' plain default, and it is marked as one in the load log so it is never
    ''' mistaken for something the vehicle declared.
    ''' </summary>
    Public pitchRate As Single = 20.0F
    Public pitchRateFromFile As Boolean

    ''' <summary>
    ''' HULL TRAVERSE, degrees per second, from the CHASSIS rotationSpeed.
    '''
    ''' "be nice to know the tanks turn rate" - the owner, 2026-09-19, and
    ''' this file already had the element in hand: Load pulls chassisEl for
    ''' hullPosition and the model path, then only ever asked the TURRET and
    ''' the GUN for a rotationSpeed. The hull's own was one SelectSingleNode
    ''' away and nobody had asked for it.
    '''
    ''' WHY IT MATTERS BEYOND CURIOSITY. Brain Testing turns every hull at a
    ''' single made-up TURN_RATE of 0.45 rad/s - about 26 deg/s - and its own
    ''' comment admits it: "this is the WORLD applying a brain's numbers, not
    ''' a vehicle model". Since the sim's radius is v / (str * TURN_RATE),
    ''' that one constant decides every turn the brain can physically make.
    ''' A per-tank figure is what lets that stop being a guess.
    '''
    ''' ZERO MEANS THE FILE DID NOT SAY, not "cannot turn" - checked at the
    ''' read, and hullRateFromFile carries the distinction so a default is
    ''' never mistaken for a declared value. Same discipline pitchRate uses.
    ''' </summary>
    Public hullRate As Single = 0.0F
    Public hullRateFromFile As Boolean

    ''' <summary>Forward and reverse speed limits, m/s, from the chassis
    ''' speedLimits. The OTHER half of a turn radius - read here because
    ''' asking a file twice for two halves of one answer is how they end up
    ''' coming from different tanks.</summary>
    Public fwdLimit As Single = 0.0F
    Public revLimit As Single = 0.0F

    ''' <summary>The muzzle blast the gun names, from gun_effects.xml.</summary>
    Public blast As BlastSpec

    ''' <summary>Seconds to reload, from the def. The ordinary one-number
    ''' case; an autoreloader's per-shell list is autoReloadS below.</summary>
    Public reloadS As Single() = {6.0F}

    ''' <summary>
    ''' An AUTORELOADER's per-shell refill times, or empty.
    '''
    ''' A SEPARATE ELEMENT, NOT A LONGER reloadTime. The Progetto M40 carries
    ''' BOTH: reloadTime 15.4 for dumping the magazine, and
    ''' autoreload/reloadTime "10 12 14 16" for putting the first, second,
    ''' third and fourth shell back one at a time, each costing more than the
    ''' last. Reading only the gun's own reloadTime finds 15.4, a clip of 4
    ''' and nothing to say it is an autoreloader - which is a real tank on our
    ''' roster modelled as the wrong kind of gun.
    '''
    ''' A SINGLE VALUE HERE IS STILL AN AUTORELOADER - the Jezevec's is one
    ''' 6.3, meaning every shell costs the same. So the KIND comes from
    ''' whether this element existed, never from how many numbers are in it.
    ''' </summary>
    Public autoReloadS As Single() = {}

    ''' <summary>Shells in a magazine, and the seconds between them inside it.
    ''' The def gives the rate in shots per MINUTE, not seconds.</summary>
    Public clipCount As Integer = 1
    Public clipIntervalS As Single

    ''' <summary>Same again for a burst gun - several rounds off one trigger,
    ''' then the ordinary reload.</summary>
    Public burstCount As Integer = 1
    Public burstIntervalS As Single

    ''' <summary>Seconds for the sights to settle. Read because it is the
    ''' other half of a gun's rhythm; nothing gates on it yet.</summary>
    Public aimingTimeS As Single

    ''' <summary>
    ''' How the magazine behaves. Three shapes, and the def tells them apart
    ''' by which elements it carries rather than by any flag:
    '''   OneShot - one reload between shots.
    '''   Clip    - clipCount shells at clipIntervalS, then ONE reload
    '''             refills the whole magazine.
    '''   Auto    - shells come back one at a time, the k-th costing
    '''             autoReloadS(k), and whatever is in there may be fired.
    '''
    ''' ORDER MATTERS: an autoreloader also has a clip block, so testing
    ''' clipCount first would call every one of them a clip gun.
    ''' </summary>
    Public ReadOnly Property magazine As MagazineKind
        Get
            If autoReloadS.Length > 0 Then Return MagazineKind.AutoReloader
            If clipCount > 1 Then Return MagazineKind.Clip
            Return MagazineKind.OneShot
        End Get
    End Property

    ''' <summary>
    ''' Where the game starts the blast: the gun visual's own HP_gunFire node,
    ''' accumulated down the tree, in the VISUAL's frame.
    '''
    ''' Not measured - named. Every gun visual carries it, under
    ''' Scene Root / nodes_01 / Gun / G / HP_gunFire, and it is where the XML
    ''' attaches both the pixie and the light. On the 121 it accumulates to
    ''' z = 5.560, which is the gun's own bbMax.Z to six figures.
    '''
    ''' IN THE VISUAL'S FRAME, which is not the vertex data's. The skinned
    ''' streams are stored with Z reversed and FlipSkinnedZ undoes that at draw,
    ''' so a point taken from the node tree must NOT go through the flip or it
    ''' comes out at the breech.
    ''' </summary>
    Public muzzleLocal As Vector3
    Public hasMuzzle As Boolean

    ''' <summary>
    ''' The pitch envelope at a given turret yaw: X is the lowest the gun may
    ''' point, Y the highest, degrees, POSITIVE IS UP.
    '''
    ''' THE FILE'S SIGN IS THE OPPOSITE OF THAT, and the names are swapped with
    ''' it. WoT's minPitch is the ELEVATION limit and it is stored negative -
    ''' more negative is further up; its maxPitch is the DEPRESSION limit and is
    ''' stored positive. So the up limit comes from minPitch negated and the
    ''' down limit from maxPitch negated, which is why reading them as a plain
    ''' min and max gives a gun that elevates when it should depress.
    '''
    ''' A tank with no elevation at all is not an error: the Strv 103B ships
    ''' -1 and -1, collapsing the range to a point, because it aims with its
    ''' suspension rather than its gun.
    ''' </summary>
    Public Function PitchRangeAt(yawDeg As Single) As Vector2
        Dim f = yawDeg / 360.0F
        f = CSng(f - Math.Floor(f))
        Dim hi = -SampleCurve(pitchUpCurve, f, -20.0F)
        Dim lo = -SampleCurve(pitchDownCurve, f, 8.0F)
        If lo > hi Then lo = hi
        Return New Vector2(lo, hi)
    End Function

    ''' <summary>
    ''' A pitchLimits curve at x, piecewise linear, flat outside its ends.
    '''
    ''' A single number is a constant rather than a curve - some vehicles ship
    ''' one - and an empty or unparseable curve returns the fallback so a
    ''' vehicle with a malformed def still aims instead of locking at zero.
    ''' </summary>
    Private Shared Function SampleCurve(c As Single(), x As Single,
                                        fallback As Single) As Single
        If c Is Nothing OrElse c.Length = 0 Then Return fallback
        If c.Length = 1 Then Return c(0)
        If c.Length < 4 Then Return c(1)

        Dim n = c.Length \ 2
        If x <= c(0) Then Return c(1)
        For i = 1 To n - 1
            Dim x0 = c((i - 1) * 2), y0 = c((i - 1) * 2 + 1)
            Dim x1 = c(i * 2), y1 = c(i * 2 + 1)
            If x > x1 Then Continue For
            Dim d = x1 - x0
            If d <= 1.0E-6F Then Return y1
            Return y0 + (y1 - y0) * (x - x0) / d
        Next
        Return c((n - 1) * 2 + 1)
    End Function

    ''' <summary>
    ''' Build one vehicle, reporting after each part.
    '''
    ''' The progress callback is what lets a bar FILL rather than pop. A
    ''' vehicle is one blocking call from the outside, so without it the only
    ''' honest states are nothing and done, and thirty bars would each flick
    ''' from empty to full with nothing in between. Four parts is four steps,
    ''' and the caller draws a frame on each.
    ''' </summary>
    Public Shared Function Load(nation As String, tag As String,
                                Optional progress As Action(Of Single) = Nothing) As TankVehicle
        Dim xmlPath = String.Format("scripts/item_defs/vehicles/{0}/{1}.xml", nation, tag)
        Dim entry = ResMgr.Lookup(xmlPath)
        If entry Is Nothing Then
            LogThis("tank: vehicle xml not found {0}", xmlPath)
            Return Nothing
        End If
        Dim root As XmlElement = ResMgr.openXML(entry)
        If root Is Nothing Then
            LogThis("tank: vehicle xml would not parse {0}", xmlPath)
            Return Nothing
        End If

        Dim v As New TankVehicle With {.nation = nation, .tag = tag}

        Dim chassisEl = Priciest(root.SelectSingleNode("chassis"))
        Dim turretEl = Priciest(root.SelectSingleNode("turrets0"))
        Dim gunEl = If(turretEl Is Nothing, Nothing, Priciest(turretEl.SelectSingleNode("guns")))

        v.hullPosition = TankVisual.Vec3(TankVisual.TextOf(If(chassisEl Is Nothing, Nothing, chassisEl.SelectSingleNode("hullPosition"))))
        v.turretPosition = TankVisual.Vec3(TankVisual.TextOf(root.SelectSingleNode("hull/turretPositions/turret")))
        v.gunPosition = TankVisual.Vec3(TankVisual.TextOf(If(turretEl Is Nothing, Nothing, turretEl.SelectSingleNode("gunPosition"))))

        LogThis("tank: {0}/{1}: chassis={2} turret={3} gun={4}", nation, tag,
                If(chassisEl Is Nothing, "?", chassisEl.Name), If(turretEl Is Nothing, "?", turretEl.Name), If(gunEl Is Nothing, "?", gunEl.Name))
        LogThis("tank:   hullPosition {0}  turretPosition {1}  gunPosition {2}", v.hullPosition, v.turretPosition, v.gunPosition)

        ' Offsets are chained the way the game chains them: chassis at the
        ' origin, hull raised by hullPosition, turret at hull + turret joint,
        ' gun at turret + gun joint. BigWorld axes throughout; the world
        ' matrix decides the handedness once, for the whole tank.
        Dim hullOff = v.hullPosition
        Dim turretOff = hullOff + v.turretPosition
        Dim gunOff = turretOff + v.gunPosition

        v.ReadHullTraverse(chassisEl)
        v.ReadAimLimits(turretEl, gunEl)
        v.ReadGunTiming(nation, gunEl)
        v.ReadBarrelSpec(nation, gunEl)
        If TANK_GUN_SPEC_DUMP Then DumpGunSpec(nation, gunEl)
        v.blast = TankBlast.Lookup(TankVisual.TextOf(
            If(gunEl Is Nothing, Nothing, gunEl.SelectSingleNode("effects"))))

        v.AddPart("chassis", ModelOf(chassisEl), Vector3.Zero)
        progress?.Invoke(0.25F)
        v.AddPart("hull", TankVisual.TextOf(root.SelectSingleNode("hull/models/undamaged")), hullOff)
        progress?.Invoke(0.5F)
        v.AddPart("turret", ModelOf(turretEl), turretOff)
        progress?.Invoke(0.75F)
        v.AddPart("gun", ModelOf(gunEl), gunOff)
        progress?.Invoke(1.0F)

        ' THE ARMOUR, onto the parts. One place, right after the parts exist,
        ' so every consumer reads it off the part rather than re-walking
        ' item_defs with its own idea of which module was chosen.
        v.ReadArmor("chassis", chassisEl)
        v.ReadArmor("hull", TryCast(root.SelectSingleNode("hull"), XmlElement))
        v.ReadArmor("turret", turretEl)
        v.ReadArmor("gun", gunEl)

        For Each p In v.parts
            If p.label <> "gun" OrElse p.visual Is Nothing Then Continue For
            Dim mp As Vector3
            If p.visual.nodePos.TryGetValue("HP_gunFire", mp) Then
                v.muzzleLocal = mp
                v.hasMuzzle = True
            End If
        Next
        LogThis("tank:   blast {0} light {1:0.00}s inner {2:0.0} outer {3:0.0}, muzzle {4}",
                If(v.blast Is Nothing, "-", v.blast.name),
                If(v.blast Is Nothing, 0.0F, v.blast.durationS),
                If(v.blast Is Nothing, 0.0F, v.blast.innerRadius),
                If(v.blast Is Nothing, 0.0F, v.blast.outerRadius),
                If(v.hasMuzzle, v.muzzleLocal.ToString(), "not named - barrel tip"))
        Return v
    End Function

    ''' <summary>
    ''' Pull the aim envelope out of the chosen turret and gun.
    '''
    ''' FROM THE CHOSEN ONES, not from the vehicle. Both limits and both rates
    ''' are per module - a different gun on the same turret traverses
    ''' differently, which is the whole reason turretYawLimits lives under the
    ''' GUN rather than the turret - so reading them anywhere but off the
    ''' elements Priciest picked would describe a vehicle we are not drawing.
    ''' </summary>
    ''' <summary>
    ''' The gun's rhythm: reload, magazine, burst, aim time.
    '''
    ''' FROM THE GUN THE VEHICLE IS CARRYING, which is why it reads off the
    ''' element Priciest chose rather than searching the file - a turret's
    ''' other guns reload at their own rates, and the last one in the file is
    ''' not the one on the tank.
    '''
    ''' A value the vehicle file does not override lives in the nation's
    ''' components/guns.xml under the same gun name - the vehicle entry
    ''' carries the text "shared" when it inherits. Looked up there when it
    ''' is missing, so a gun that states nothing locally still gets its own
    ''' numbers rather than a default.
    ''' </summary>
    Private Sub ReadGunTiming(nation As String, gunEl As XmlElement)
        If gunEl Is Nothing Then Return

        Dim sh As XmlElement = Nothing
        Dim f = TankVisual.Floats(TankVisual.TextOf(gunEl.SelectSingleNode("reloadTime")))
        If f Is Nothing OrElse f.Length = 0 Then
            sh = SharedGun(nation, gunEl.Name)
            f = TankVisual.Floats(TankVisual.TextOf(
                If(sh Is Nothing, Nothing, sh.SelectSingleNode("reloadTime"))))
        End If
        If f IsNot Nothing AndAlso f.Length > 0 Then reloadS = f

        Dim a = TankVisual.Floats(TankVisual.TextOf(gunEl.SelectSingleNode("aimingTime")))
        If a Is Nothing OrElse a.Length = 0 Then
            If sh Is Nothing Then sh = SharedGun(nation, gunEl.Name)
            a = TankVisual.Floats(TankVisual.TextOf(
                If(sh Is Nothing, Nothing, sh.SelectSingleNode("aimingTime"))))
        End If
        If a IsNot Nothing AndAlso a.Length > 0 Then aimingTimeS = a(0)

        read_magazine(gunEl.SelectSingleNode("clip"), clipCount, clipIntervalS)
        If clipCount <= 1 Then
            If sh Is Nothing Then sh = SharedGun(nation, gunEl.Name)
            read_magazine(If(sh Is Nothing, Nothing, sh.SelectSingleNode("clip")),
                          clipCount, clipIntervalS)
        End If
        read_magazine(gunEl.SelectSingleNode("burst"), burstCount, burstIntervalS)

        ' The autoreloader's own element, under the gun and beside the clip.
        Dim ar = TankVisual.Floats(TankVisual.TextOf(
            gunEl.SelectSingleNode("autoreload/reloadTime")))
        If ar Is Nothing OrElse ar.Length = 0 Then
            If sh Is Nothing Then sh = SharedGun(nation, gunEl.Name)
            ar = TankVisual.Floats(TankVisual.TextOf(
                If(sh Is Nothing, Nothing, sh.SelectSingleNode("autoreload/reloadTime"))))
        End If
        If ar IsNot Nothing AndAlso ar.Length > 0 Then autoReloadS = ar

        LogThis("tank:   gun timing: reload {0} s, {1} x{2} @{3:0.0} s, aim {4:0.0} s{5}",
                String.Join("/", reloadS), magazine.ToString(), clipCount,
                clipIntervalS, aimingTimeS,
                If(autoReloadS.Length = 0, "",
                   " autoreload " & String.Join("/", autoReloadS) & " s"))
    End Sub

    ''' <summary>A clip or burst block: a count and a rate in shots per
    ''' MINUTE, turned here into the seconds between shells that everything
    ''' downstream actually wants.</summary>
    Private Shared Sub read_magazine(el As XmlNode, ByRef count As Integer,
                                     ByRef intervalS As Single)
        If el Is Nothing Then Return
        Dim c = TankVisual.Floats(TankVisual.TextOf(el.SelectSingleNode("count")))
        Dim r = TankVisual.Floats(TankVisual.TextOf(el.SelectSingleNode("rate")))
        If c IsNot Nothing AndAlso c.Length > 0 Then count = CInt(c(0))
        If r IsNot Nothing AndAlso r.Length > 0 AndAlso r(0) > 0.0F Then
            intervalS = 60.0F / r(0)
        End If
    End Sub

    ''' <summary>The gun's shared entry in the nation's components/guns.xml,
    ''' or Nothing. Cached per nation - the file is large and every vehicle
    ''' of a nation asks for it.</summary>
    ''' <summary>
    ''' Every field the chosen gun carries, local and shared, said out loud.
    '''
    ''' The owner, 2026-09-25: "get the specs on barrels and such using most
    ''' costly rule." The rule is already here - Priciest picks the chassis,
    ''' the turret and then the gun inside that turret - but only reload,
    ''' magazine, aim limits and the blast are READ off it. Shell speed is a
    ''' single global 180 m/s for every gun on the roster, and calibre is not
    ''' read at all.
    '''
    ''' So this says what is THERE before anything is written against it. The
    ''' item_defs are packed binary, which is the same reason ReadHullTraverse
    ''' logs whether or not it found its fields: guessing at a schema you
    ''' cannot open is how you write a parser for a file that does not exist.
    ''' Under `gunspec` on the command line; delete the flag when the real
    ''' fields are being read.
    ''' </summary>
    Private Shared Sub DumpGunSpec(nation As String, gunEl As XmlElement)
        If gunEl Is Nothing Then
            LogThis("tank: gun spec - no gun element")
            Return
        End If
        LogThis("tank: gun spec for [{0}] of {1}", gunEl.Name, nation)
        LogThis("tank:   LOCAL has {0} child node(s), {1} of them elements",
                gunEl.ChildNodes.Count, CountElements(gunEl))
        LogThis("tank:   LOCAL raw: {0}", Clip(gunEl.OuterXml))
        DumpNode(gunEl, "     ", 0)
        Dim sh = SharedGun(nation, gunEl.Name)
        If sh Is Nothing Then
            LogThis("tank:   SHARED components/guns.xml has no [{0}]", gunEl.Name)
        Else
            LogThis("tank:   SHARED {0} child node(s), {1} element(s)",
                    sh.ChildNodes.Count, CountElements(sh))
            LogThis("tank:   SHARED raw: {0}", Clip(sh.OuterXml))
            DumpNode(sh, "     ", 0)
        End If
    End Sub

    ''' <summary>One element and its children, two levels deep. Two, because a
    ''' gun's shells are a level down and their own fields another - and
    ''' unbounded recursion on a packed section prints a wall.</summary>
    Private Shared Function CountElements(el As XmlElement) As Integer
        Dim n = 0
        For Each c As XmlNode In el.ChildNodes
            If c.NodeType = XmlNodeType.Element Then n += 1
        Next
        Return n
    End Function

    Private Shared Function Clip(t As String) As String
        If t Is Nothing Then Return "(nothing)"
        t = t.Replace(vbCr, " ").Replace(vbLf, " ")
        If t.Length > 6000 Then t = t.Substring(0, 6000) & " ...[" & t.Length & " chars]"
        Return t
    End Function

    Private Shared Sub DumpNode(el As XmlElement, pad As String, depth As Integer)
        For Each c As XmlNode In el.ChildNodes
            If c.NodeType <> XmlNodeType.Element Then Continue For
            Dim ce = DirectCast(c, XmlElement)
            Dim kids = 0
            For Each g As XmlNode In ce.ChildNodes
                If g.NodeType = XmlNodeType.Element Then kids += 1
            Next
            Dim txt = If(ce.InnerText, "").Trim()
            If txt.Length > 60 Then txt = txt.Substring(0, 60) & "..."
            If kids = 0 Then
                LogThis("tank:{0}{1} = {2}", pad, ce.Name, txt)
            Else
                LogThis("tank:{0}{1}  ({2} child element(s))", pad, ce.Name, kids)
                If depth < 2 Then DumpNode(ce, pad & "  ", depth + 1)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Pull the barrel and ammunition numbers off the chosen gun.
    '''
    ''' LOCAL FIRST, THEN SHARED, which is the precedence ReadGunTiming
    ''' already uses and the one the files are built for: a vehicle's own gun
    ''' entry overrides, and carries the literal text "shared" where it
    ''' inherits. reloadTime differs between the two on both tanks measured -
    ''' 9.4 local against 11 shared - so reading the wrong one is not
    ''' academic.
    '''
    ''' THE SHOTS BLOCK IS ONLY EVER SHARED. Neither vehicle entry carried one,
    ''' both shared entries did, so the shell is looked up there and there is
    ''' no local override path to write.
    '''
    ''' Every field is optional and a missing one leaves its default. Logged
    ''' either way, for the same reason ReadHullTraverse logs: a silent absence
    ''' would not tell you whether the field is missing or the read is wrong.
    ''' </summary>
    Private Sub ReadBarrelSpec(nation As String, gunEl As XmlElement)
        If gunEl Is Nothing Then Return
        Dim sh = SharedGun(nation, gunEl.Name)

        gunPriceCredits = Num(gunEl, sh, "price", 0.0F)
        gunImpulse = Num(gunEl, sh, "impulse", 0.0F)
        recoilTravelM = Num(gunEl, sh, "recoil/amplitude", RECOIL_TRAVEL_FALLBACK_M)
        recoilEffectName = Txt(gunEl, sh, "recoil/recoilEffect")
        muzzleBrake = Txt(gunEl, sh, "muzzleBrake")

        ' The shell with the largest defaultPortion - see shellName.
        Dim shots = If(sh Is Nothing, Nothing, sh.SelectSingleNode("shots"))
        If shots IsNot Nothing Then
            Dim best As XmlElement = Nothing
            Dim bestPortion = Single.NegativeInfinity
            For Each c As XmlNode In shots.ChildNodes
                If c.NodeType <> XmlNodeType.Element Then Continue For
                Dim el = DirectCast(c, XmlElement)
                Dim p = One(el.SelectSingleNode("defaultPortion"), 0.0F)
                If best Is Nothing OrElse p > bestPortion Then
                    best = el : bestPortion = p
                End If
            Next
            If best IsNot Nothing Then
                shellName = best.Name
                shellSpeedMps = One(best.SelectSingleNode("speed"), 0.0F)
                shellGravity = One(best.SelectSingleNode("gravity"), 9.81F)
                shellMaxDistanceM = One(best.SelectSingleNode("maxDistance"), 0.0F)
                shellPiercing = One(best.SelectSingleNode("piercingPower"), 0.0F)
            End If
        End If

        LogThis("tank:   barrel: price {0:N0}, impulse {1:0.00}, recoil {2:0.00} m" &
                " [{3}], brake {4}",
                gunPriceCredits, gunImpulse, recoilTravelM,
                If(recoilEffectName = "", "-", recoilEffectName),
                If(muzzleBrake = "", "none", muzzleBrake))
        If shellName = "" Then
            LogThis("tank:   shell: none stated - shots fall back to TANK_SHELL_MPS")
        Else
            LogThis("tank:   shell [{0}] {1:0} m/s, gravity {2:0.00}, max {3:0} m," &
                    " piercing {4:0}",
                    shellName, shellSpeedMps, shellGravity, shellMaxDistanceM,
                    shellPiercing)
        End If
    End Sub

    ''' <summary>A number from the local entry, else the shared one, else the
    ''' default. Path may be nested, e.g. "recoil/amplitude".</summary>
    Private Shared Function Num(localEl As XmlElement, sharedEl As XmlElement,
                                path As String, dflt As Single) As Single
        Dim v = One(If(localEl Is Nothing, Nothing, localEl.SelectSingleNode(path)),
                    Single.NaN)
        If Not Single.IsNaN(v) Then Return v
        v = One(If(sharedEl Is Nothing, Nothing, sharedEl.SelectSingleNode(path)),
                Single.NaN)
        If Not Single.IsNaN(v) Then Return v
        Return dflt
    End Function

    Private Shared Function Txt(localEl As XmlElement, sharedEl As XmlElement,
                                path As String) As String
        Dim t = TankVisual.TextOf(
            If(localEl Is Nothing, Nothing, localEl.SelectSingleNode(path)))
        If t IsNot Nothing AndAlso t.Trim() <> "" Then Return t.Trim()
        t = TankVisual.TextOf(
            If(sharedEl Is Nothing, Nothing, sharedEl.SelectSingleNode(path)))
        Return If(t Is Nothing, "", t.Trim())
    End Function

    ''' <summary>The FIRST number in a node's text, or the default. piercingPower
    ''' is a pair ("262 244") and speed is a single, so taking [0] is what the
    ''' other readers in this file do too.</summary>
    Private Shared Function One(n As XmlNode, dflt As Single) As Single
        If n Is Nothing Then Return dflt
        Dim f = TankVisual.Floats(n.InnerText)
        If f Is Nothing OrElse f.Length = 0 Then Return dflt
        Return f(0)
    End Function


    ''' <summary>
    ''' Put one module's armour plates and collision path onto its part.
    '''
    ''' Silent when a module has no &lt;armor&gt; block, because several do not.
    ''' Finding out which was the probe's job and that is now known, so a
    ''' normal load has nothing to report.
    ''' </summary>
    Private Sub ReadArmor(label As String, el As XmlElement)
        If el Is Nothing Then Return
        Dim p = PartByLabel(label)
        If p Is Nothing Then Return

        Dim arm = el.SelectSingleNode("armor")
        If arm IsNot Nothing Then
            For Each c As XmlNode In arm.ChildNodes
                If c.NodeType <> XmlNodeType.Element Then Continue For
                Dim mm = OwnNumber(c)
                If Not Single.IsNaN(mm) Then p.armorMm(c.Name) = mm
            Next
        End If

        Dim mp = TankVisual.TextOf(el.SelectSingleNode("hitTester/collisionModelClient"))
        Dim hv = ""
        If mp IsNot Nothing AndAlso mp.Trim() <> "" Then
            hv = mp.Trim().Replace(".model", ".havok")
            If TankFiles.LookupOwn(hv) IsNot Nothing Then
                p.havokPath = hv
            Else
                hv = ""
            End If
        End If

        ' ONE LINE, like the gun timing and barrel lines above it. Enough to see
        ' the plates were found and the collision file resolved; the exploring
        ' is done and its probe is gone.
        LogThis("tank:   armour [{0}] {1} plate(s){2}{3}", label, p.armorMm.Count,
                If(p.armorMm.Count = 0, "", " " & String.Join(" ", PlateText(p))),
                If(hv = "", " - no .havok", " + havok"))
    End Sub

    Private Shared Function PlateText(p As TankPart) As List(Of String)
        Dim o As New List(Of String)
        For Each kv In p.armorMm
            o.Add(String.Format("{0}={1:0.#}", kv.Key, kv.Value))
        Next
        Return o
    End Function

    Private Shared Function OwnNumber(n As XmlNode) As Single
        Dim sb As New Text.StringBuilder()
        For Each c As XmlNode In n.ChildNodes
            If c.NodeType = XmlNodeType.Text OrElse
               c.NodeType = XmlNodeType.CDATA Then sb.Append(c.Value)
        Next
        Dim f = TankVisual.Floats(sb.ToString())
        If f Is Nothing OrElse f.Length = 0 Then Return Single.NaN
        Return f(0)
    End Function


    Private Shared Function SharedGun(nation As String, name As String) As XmlElement
        Dim root As XmlElement = Nothing
        If Not shared_guns.TryGetValue(nation, root) Then
            Dim entry = ResMgr.Lookup(String.Format(
                "scripts/item_defs/vehicles/{0}/components/guns.xml", nation))
            root = If(entry Is Nothing, Nothing, ResMgr.openXML(entry))
            shared_guns(nation) = root
        End If
        If root Is Nothing Then Return Nothing
        Return TryCast(root.SelectSingleNode("shared/" & name), XmlElement)
    End Function

    Private Shared ReadOnly shared_guns As New Dictionary(Of String, XmlElement)

    ''' <summary>
    ''' What the chassis says about turning and moving.
    '''
    ''' Read through TankVisual.Floats like every other numeric field here, so
    ''' a value that arrives as "45" and one that arrives as "45.0 45.0" are
    ''' both handled by the code that already knows how - speedLimits is a
    ''' PAIR (forward, reverse) and rotationSpeed a single, and taking [0] of
    ''' each is what the turret read does too.
    '''
    ''' LOGGED WHETHER OR NOT IT WAS FOUND. The point of this pass is to learn
    ''' whether these fields are actually present in the item_defs we load -
    ''' they are packed binary, so the only way to know is to ask one and say
    ''' what came back. A silent absence would answer nothing.
    ''' </summary>
    Private Sub ReadHullTraverse(chassisEl As XmlElement)
        If chassisEl Is Nothing Then
            LogThis("tank:   hull: no chassis element - no traverse or speed")
            Return
        End If

        Dim rs = TankVisual.Floats(TankVisual.TextOf(chassisEl.SelectSingleNode("rotationSpeed")))
        If rs IsNot Nothing AndAlso rs.Length > 0 AndAlso rs(0) > 0.0F Then
            hullRate = rs(0)
            hullRateFromFile = True
        End If

        Dim sl = TankVisual.Floats(TankVisual.TextOf(chassisEl.SelectSingleNode("speedLimits")))
        If sl IsNot Nothing AndAlso sl.Length > 0 Then
            fwdLimit = sl(0)
            If sl.Length > 1 Then revLimit = sl(1)
        End If

        ' AND WHAT IT MEANS FOR A TURN, because degrees a second on its own
        ' invites the comparison to be done in someone's head and done wrong.
        ' radius = v / omega, with omega in radians - the same arithmetic
        ' BrainSim does with its constant, so the two are directly comparable.
        Dim omega = hullRate * CSng(Math.PI) / 180.0F
        Dim radius = If(omega > 0.0F AndAlso fwdLimit > 0.0F, fwdLimit / omega, 0.0F)
        LogThis("tank:   hull: traverse {0:0.0} deg/s{1}, speed {2:0.0} fwd / " &
                "{3:0.0} rev m/s -> tightest circle {4:0.0} m at full speed " &
                "(BrainSim uses 25.8 deg/s for every tank)",
                hullRate, If(hullRateFromFile, "", " (ABSENT from the file)"),
                fwdLimit, revLimit, radius)
    End Sub

    Private Sub ReadAimLimits(turretEl As XmlElement, gunEl As XmlElement)
        If gunEl IsNot Nothing Then
            Dim pl = gunEl.SelectSingleNode("pitchLimits")
            If pl IsNot Nothing Then
                pitchUpCurve = TankVisual.Floats(TankVisual.TextOf(pl.SelectSingleNode("minPitch")))
                pitchDownCurve = TankVisual.Floats(TankVisual.TextOf(pl.SelectSingleNode("maxPitch")))
            End If

            ' Absent means the full circle, which is the common case - only
            ' casemates and a few turrets declare it.
            Dim yl = TankVisual.Floats(TankVisual.TextOf(gunEl.SelectSingleNode("turretYawLimits")))
            If yl Is Nothing OrElse yl.Length < 2 Then
                yl = TankVisual.Floats(TankVisual.TextOf(
                    If(turretEl Is Nothing, Nothing, turretEl.SelectSingleNode("turretYawLimits"))))
            End If
            If yl IsNot Nothing AndAlso yl.Length >= 2 Then
                yawMin = Math.Min(yl(0), yl(1))
                yawMax = Math.Max(yl(0), yl(1))
            End If

            ' Looked for anyway: a few modded defs do carry one, and if it is
            ' there it beats a guess.
            Dim gr = TankVisual.Floats(TankVisual.TextOf(gunEl.SelectSingleNode("rotationSpeed")))
            If gr IsNot Nothing AndAlso gr.Length > 0 AndAlso gr(0) > 0.0F Then
                pitchRate = gr(0)
                pitchRateFromFile = True
            End If
        End If

        If turretEl IsNot Nothing Then
            Dim tr = TankVisual.Floats(TankVisual.TextOf(turretEl.SelectSingleNode("rotationSpeed")))
            If tr IsNot Nothing AndAlso tr.Length > 0 AndAlso tr(0) > 0.0F Then yawRate = tr(0)
        End If

        Dim fwd = PitchRangeAt(0.0F)
        Dim rear = PitchRangeAt(180.0F)
        LogThis("tank:   aim: yaw {0:0}..{1:0} at {2:0}/s, pitch {3:0.0}..{4:0.0} ahead, {5:0.0}..{6:0.0} astern at {7:0}/s{8}",
                yawMin, yawMax, yawRate, fwd.X, fwd.Y, rear.X, rear.Y, pitchRate,
                If(pitchRateFromFile, "", " (default)"))
    End Sub

    Private Sub AddPart(label As String, modelPath As String, offset As Vector3)
        If String.IsNullOrEmpty(modelPath) Then
            LogThis("tank:   {0}: no model path - skipped", label)
            Return
        End If
        Dim p As New TankPart With {
            .label = label,
            .offset = offset,
            .primitivesPath = modelPath.Replace(".model", ".primitives_processed"),
            .visualPath = modelPath.Replace(".model", ".visual_processed")}
        p.visual = TankVisual.Load(p.visualPath)
        p.meshes = TankPrimitives.LoadMeshes(p.primitivesPath)
        LoadTextures(p)
        LogThis("tank:   {0}: {1} mesh(es) at offset {2}", label, p.meshes.Count, offset)
        parts.Add(p)
    End Sub

    ''' <summary>Every material's four maps through the core's DDS loader; the detail map too when named.</summary>
    Private Shared Sub LoadTextures(p As TankPart)
        If p.visual Is Nothing Then Return

        ' WHICH SHADER EACH MESH ASKS FOR, logged per part. The fx name is the
        ' only place the engine states a mesh's mechanism: the fake tracks are
        ' PBS_tank_uvtransform_skinned_ao.fx and nothing else about the band
        ' says it scrolls. Printed with the AM beside it because the pair
        ' identifies a mesh across the two exporters and the game.
        For Each m2 In p.meshes
            Dim mm = p.MaterialFor(m2)
            If mm IsNot Nothing Then
                LogThis("tank:   mesh [{0}] fx={1} AM={2}", m2.name,
                        IO.Path.GetFileName(mm.fx), IO.Path.GetFileName(mm.diffuseMap))
            End If
        Next
        For Each rs In p.visual.renderSets
            For Each m In rs.materials
                m.texDiffuse = Tex(m.diffuseMap)
                m.texNormal = Tex(m.normalMap)
                m.texGMM = Tex(m.metallicGlossMap)
                m.texAO = Tex(m.aoMap)
                m.texDetail = Tex(m.detailMap)
                LogThis("tank:     material {0} ({1}): AM={2} ANM={3} GMM={4} AO={5} detail={6}",
                        m.identifier, IO.Path.GetFileName(m.fx),
                        If(m.texDiffuse Is Nothing, "-", "ok"), If(m.texNormal Is Nothing, "-", "ok"),
                        If(m.texGMM Is Nothing, "-", "ok"), If(m.texAO Is Nothing, "-", "ok"), If(m.texDetail Is Nothing, "-", "ok"))
            Next
        Next
    End Sub

    Private Shared Function Tex(path As String) As GLTexture
        Return TankFiles.LoadTexture(path)
    End Function

    ''' <summary>The child element with the highest &lt;price&gt;; the last child when none carries one.</summary>
    Private Shared Function Priciest(parent As XmlNode) As XmlElement
        If parent Is Nothing Then Return Nothing
        Dim best As XmlElement = Nothing
        Dim bestPrice As Double = Double.NegativeInfinity
        For Each c As XmlNode In parent.ChildNodes
            If c.NodeType <> XmlNodeType.Element Then Continue For
            Dim el = DirectCast(c, XmlElement)
            Dim price As Double = 0
            Dim pn = el.SelectSingleNode("price")
            If pn IsNot Nothing Then
                Dim f = TankVisual.Floats(pn.InnerText)
                If f.Length > 0 Then price = f(0)
            End If
            If best Is Nothing OrElse price >= bestPrice Then
                best = el : bestPrice = price
            End If
        Next
        Return best
    End Function

    Private Shared Function ModelOf(el As XmlElement) As String
        If el Is Nothing Then Return ""
        Return TankVisual.TextOf(el.SelectSingleNode("models/undamaged"))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each p In parts
            p.Dispose()
        Next
        parts.Clear()
    End Sub
End Class


''' <summary>
''' WHICH BONE IS THE BARREL, AND WHERE ITS MUZZLE IS.
'''
''' Lifted out of TankRenderer on 2026-09-25 so Brain Testing can reach it.
''' The owner: "nuTerra Work: You have permission to help PathFinder add
''' functionality to the guns as in nuTerra source." PathFinder was firing
''' from the gun part's ORIGIN, which is the breech - the line was already
''' right and the shot started a few metres short of the muzzle.
'''
''' IN THIS FILE rather than a new one, deliberately. A new .vb would need a
''' Compile Include adding to BrainTesting.vbproj, and that file is not mine
''' to edit. TankVehicle.vb is already linked, so this arrives with no change
''' to their project at all.
'''
''' NOTHING HERE IS NEW BEHAVIOUR. It is the code the renderer already ran,
''' moved, with the renderer now calling in - so a muzzle measured by the
''' brain and a barrel drawn by nuTerra cannot drift apart.
''' </summary>
Public Module TankBarrel

    ''' <summary>
    ''' Classify this gun's palette once, and work out which way the barrel
    ''' retracts.
    '''
    ''' THE DIRECTION IS MEASURED, NOT ASSUMED. A fixed axis sign is wrong on
    ''' half the corpus - the muzzle sits at +Z on some guns and -Z on others,
    ''' and getting it backwards pushes the barrel further OUT of the mantlet
    ''' instead of into it, which reads as the gun growing rather than as a
    ''' sign error. ComputeBoneHubs already leaves a weighted centroid per
    ''' palette slot, so the barrel bone's own hub says which end it is on:
    ''' recoil is back toward the origin, so the sign is the opposite of the
    ''' hub's. Nothing here has to know a convention.
    '''
    ''' Where the names give nothing - a palette with no G_ bone at all - the
    ''' hubs answer that too: the bone whose vertices sit furthest out along Z
    ''' IS the barrel, whatever it is called. TEPY needed exactly this for
    ''' A100_T49, which inverts the naming convention outright.
    ''' </summary>
    Public Function PlanFor(part As TankPart, m As TankMesh) As BarrelPlan
        If m.barrelPlan IsNot Nothing Then Return m.barrelPlan
        If m.barrelPlanDone Then Return Nothing
        m.barrelPlanDone = True

        Dim plan As New BarrelPlan
        m.barrelPlan = plan

        Dim palette = part.PaletteFor(m)
        plan.slots = ClassifyPalette(palette)

        ' The barrel is the flagged bone reaching furthest along Z. On a twin
        ' gun both barrels are flagged and either answers the direction, since
        ' they point the same way.
        Dim best = -1
        Dim bestZ = 0.0F
        For i = 0 To 63
            If plan.slots(i) = 0 Then Continue For
            Dim z = hub_z(m, i)
            If Single.IsNaN(z) Then Continue For
            If best < 0 OrElse Math.Abs(z) > Math.Abs(bestZ) Then best = i : bestZ = z
        Next

        ' No G_ bone in the palette: fall back to geometry alone.
        If best < 0 Then
            For i = 0 To 63
                Dim z = hub_z(m, i)
                If Single.IsNaN(z) Then Continue For
                If best < 0 OrElse Math.Abs(z) > Math.Abs(bestZ) Then best = i : bestZ = z
            Next
            If best >= 0 Then
                plan.slots(best) = 1
                LogThis("tank: gun [{0}] has no G_ bone - slot {1} taken as the barrel on its hub alone (z {2:0.00})",
                        m.name, best, bestZ)
            End If
        End If

        If best < 0 Then
            m.barrelPlan = Nothing
            Return Nothing
        End If

        plan.dir = If(bestZ >= 0.0F, -1.0F, 1.0F)
        plan.barrel = best

        ' Logged once per mesh, because "which bone is the barrel" is the whole
        ' question and a wrong answer is only visible as the wrong part sliding.
        Dim names As New List(Of String)
        If palette IsNot Nothing Then
            For i = 0 To Math.Min(palette.Count, 64) - 1
                names.Add(String.Format("{0}{1}", palette(i),
                                        If(plan.slots(i) <> 0, "*", "")))
            Next
        End If
        LogThis("tank: gun [{0}] barrel slot {1} (byte {2}) hub z {3:0.00}, back is {4}Z  [{5}]",
                m.name, best, best * 3, bestZ, If(plan.dir < 0, "-", "+"),
                String.Join(" ", names))
        Return plan
    End Function

    ''' <summary>
    ''' WHERE THIS GUN'S ROUND LEAVES, and which way. The whole rule, in the
    ''' order nuTerra fires by - call this rather than MeasureMuzzle.
    '''
    ''' TWO SOURCES AND THE ORDER MATTERS. A gun whose visual names
    ''' HP_gunFire carries the game's own muzzle point, and that wins; only a
    ''' gun without one falls back to measuring the barrel bone's far end.
    ''' I handed Brain Testing MeasureMuzzle alone on 2026-09-25, which was
    ''' half the rule: on any tank that names the hardpoint they would have
    ''' measured where the game says not to.
    '''
    ''' THE NAMED MUZZLE GOES THROUGH THE UNFLIPPED MATRIX AND THE MEASURED
    ''' ONE DOES NOT. Node positions live in the visual's own frame; only the
    ''' VERTEX streams are stored Z-reversed. Push a node through
    ''' FlipSkinnedZ and the blast comes out of the breech. MeasureMuzzle
    ''' applies the flip itself because it reads vertices; this path must not,
    ''' and that asymmetry is the whole reason both live behind one call.
    '''
    ''' `model` is PartLocal(gunPart, yaw, pitch) times the vehicle's world
    ''' matrix. False means this gun can offer neither, and then muzzle and dir
    ''' are untouched - do not fire.
    '''
    ''' dir is NOT normalised, in either branch, because the caller normalises
    ''' once after a length check.
    ''' </summary>
    Public Function MuzzleFor(v As TankVehicle, gunPart As TankPart,
                              model As Matrix4, ByRef muzzle As Vector3,
                              ByRef dir As Vector3) As Boolean
        If v Is Nothing OrElse gunPart Is Nothing Then Return False
        If v.hasMuzzle Then
            Dim m0 = v.muzzleLocal
            muzzle = Vector3.TransformPosition(m0, model)
            dir = Vector3.TransformPosition(m0 + Vector3.UnitZ, model) - muzzle
            Return True
        End If
        Return MeasureMuzzle(gunPart, model, muzzle, dir)
    End Function

    ''' <summary>A palette slot's centroid Z, or NaN when nothing is bound
    ''' to it.</summary>
    Private Function hub_z(m As TankMesh, slot As Integer) As Single
        If m.boneHubs Is Nothing OrElse slot < 0 OrElse slot >= m.boneHubs.Length Then
            Return Single.NaN
        End If
        Return m.boneHubs(slot).Z
    End Function

    ''' <summary>
    ''' THE MUZZLE IS MEASURED, not offset from the joint by a guessed length.
    '''
    ''' Every gun is a different length and they are not all authored down the
    ''' same axis, so the two points that define a shot come from the barrel
    ''' bone's own vertices: its weighted centroid, about mid-barrel, and the
    ''' furthest its vertices reach along Z, which is the muzzle. Both go
    ''' through the SAME matrix the gun is drawn with, so the ray leaves where
    ''' the barrel is on screen and the direction is the difference of two
    ''' measured points rather than an axis someone had to pick a sign for.
    '''
    ''' `model` is what TankVehicle.PartLocal returned for the GUN part, times
    ''' the vehicle's world matrix. Pass the gun part, not the turret.
    '''
    ''' False when this gun has no measurable barrel bone, and then muzzle and
    ''' dir are left untouched - the renderer falls back to the joint, and a
    ''' caller that ignores the result fires from whatever it initialised.
    ''' </summary>
    Public Function MeasureMuzzle(gunPart As TankPart, model As Matrix4,
                           ByRef muzzle As Vector3,
                           ByRef dir As Vector3) As Boolean
        If TankRoster.FlipSkinnedZ Then model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * model
        For Each m In gunPart.meshes
            If m.layout Is Nothing OrElse m.layout.offBoneIdx < 0 Then Continue For
            Dim plan = PlanFor(gunPart, m)
            If plan Is Nothing OrElse plan.barrel < 0 Then Continue For
            If m.boneHubs Is Nothing OrElse m.boneTipZ Is Nothing Then Continue For
            If plan.barrel >= m.boneHubs.Length Then Continue For
            Dim hub = m.boneHubs(plan.barrel)
            If Single.IsNaN(hub.X) Then Continue For

            Dim back = Vector3.TransformPosition(New Vector3(hub.X, hub.Y, hub.Z), model)
            muzzle = Vector3.TransformPosition(
                New Vector3(hub.X, hub.Y, m.boneTipZ(plan.barrel)), model)
            dir = muzzle - back
            Return True
        Next
        Return False
    End Function

    Public Class BarrelPlan
        Public slots(63) As Integer
        Public dir As Single = 1.0F
        ''' <summary>The barrel's palette slot. The muzzle is the far end of
        ''' this bone's own vertices.</summary>
        Public barrel As Integer = -1
    End Class

    ''' <summary>
    ''' Which palette slots are barrel, from the bone names.
    '''
    ''' MOVED HERE FROM TankRecoil, which now forwards. Recoil and muzzle
    ''' measurement both need it and TankRecoil is not a file Brain Testing
    ''' compiles. Pure string work with no dependencies, so it costs that
    ''' project nothing to carry.
    ''' </summary>
    Public Function ClassifyPalette(palette As List(Of String)) As Integer()
        Dim flags(63) As Integer
        If palette Is Nothing Then Return flags

        For i = 0 To Math.Min(palette.Count, 64) - 1
            Dim n = If(palette(i), "").ToLowerInvariant()
            If n = "" Then Continue For

            ' Mechanisms first, so G_Cover_ and G_Pusher_ never reach the G_
            ' test below. These carry their own animation in the engine and
            ' recoiling them is as wrong as recoiling the mount.
            If n.Contains("cover") OrElse n.Contains("close") OrElse
               n.Contains("pusher") OrElse n.Contains("ejector") OrElse
               n.Contains("breech") OrElse n.Contains("loader") OrElse
               n.Contains("feed") OrElse n.Contains("rotate") OrElse
               n.Contains("spring") OrElse n.Contains("valve") OrElse
               n.Contains("cap_") Then Continue For

            ' Rigid mounts. statik is the German spelling and join_ a typo of
            ' joint - both are real bone names in the corpus, not defensiveness.
            If n.Contains("static") OrElse n.Contains("statik") OrElse
               n.Contains("joint") OrElse n.StartsWith("join_") Then Continue For

            ' Gun_ is the assembly, G_ is the barrel. Order matters: Gun_ also
            ' starts with G, so it has to be rejected first.
            If n.StartsWith("gun_") Then Continue For
            If n.StartsWith("g_") Then flags(i) = 1
        Next
        Return flags
    End Function

End Module

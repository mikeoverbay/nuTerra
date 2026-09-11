Imports System.Xml
Imports OpenTK.Mathematics

''' <summary>One part of a vehicle: hull, chassis, turret or gun, with its meshes, visual, textures and local offset.</summary>
Public Class TankPart
    Implements IDisposable
    Public label As String
    Public primitivesPath As String
    Public visualPath As String
    Public offset As Vector3          ' part origin relative to the chassis origin, BigWorld axes
    Public meshes As New List(Of TankMesh)
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

    ''' <summary>Seconds until this gun fires on the free-running cadence.
    ''' Seeded apart per tank so a line of them ripples rather than
    ''' volleying - thirty barrels moving as one reads as a glitch.</summary>
    Public fireIn As Single

    ''' <summary>Where the turret and gun are pointing, degrees, and which end
    ''' of their travel each is heading for. Positive pitch is UP.</summary>
    ''' <summary>Where the vehicle actually is this frame - the parked spot
    ''' walked along the shuttle. Written by the renderer before anything reads
    ''' it, because a shot has to test the tank where it is standing now.</summary>
    Public livePosition As Vector3

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

    ''' <summary>The muzzle blast the gun names, from gun_effects.xml.</summary>
    Public blast As BlastSpec

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

    Public Shared Function Load(nation As String, tag As String) As TankVehicle
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

        v.ReadAimLimits(turretEl, gunEl)
        v.blast = TankBlast.Lookup(TankVisual.TextOf(
            If(gunEl Is Nothing, Nothing, gunEl.SelectSingleNode("effects"))))

        v.AddPart("chassis", ModelOf(chassisEl), Vector3.Zero)
        v.AddPart("hull", TankVisual.TextOf(root.SelectSingleNode("hull/models/undamaged")), hullOff)
        v.AddPart("turret", ModelOf(turretEl), turretOff)
        v.AddPart("gun", ModelOf(gunEl), gunOff)

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

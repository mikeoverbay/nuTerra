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

        v.AddPart("chassis", ModelOf(chassisEl), Vector3.Zero)
        v.AddPart("hull", TankVisual.TextOf(root.SelectSingleNode("hull/models/undamaged")), hullOff)
        v.AddPart("turret", ModelOf(turretEl), turretOff)
        v.AddPart("gun", ModelOf(gunEl), gunOff)
        Return v
    End Function

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

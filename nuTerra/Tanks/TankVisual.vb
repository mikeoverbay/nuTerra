Imports System.IO
Imports System.Xml
Imports OpenTK.Mathematics

''' <summary>A material block of a .visual_processed: the texture paths and the constants the tank shader takes.</summary>
Public Class TankMaterial
    Public identifier As String = ""
    Public fx As String = ""
    Public diffuseMap As String = ""
    Public normalMap As String = ""
    Public metallicGlossMap As String = ""
    Public aoMap As String = ""          ' excludeMaskAndAOMap
    Public detailMap As String = ""      ' metallicDetailMap
    Public colorIdMap As String = ""
    Public detailUVTiling As New Vector4(1, 1, 0, 0)
    Public floats As New Dictionary(Of String, Single)   ' g_detailPower, g_detailPowerGloss, g_detailPowerAlbedo, g_maskBias, ...
    Public alphaReference As Integer = 0
    Public alphaTestEnable As Boolean
    Public doubleSided As Boolean
    Public useNormalPackDXT1 As Boolean
    Public useDetailMetallic As Boolean

    ' GPU side, filled by TankVehicle after load. Nothing here means "not shipped".
    Public texDiffuse As GLTexture
    Public texNormal As GLTexture
    Public texGMM As GLTexture
    Public texAO As GLTexture
    Public texDetail As GLTexture
End Class

''' <summary>One renderSet: which section pair it draws, the bone palette in palette order, its material(s).</summary>
Public Class TankRenderSet
    Public verticesName As String = ""
    Public primitiveName As String = ""
    Public nodes As New List(Of String)
    Public materials As New List(Of TankMaterial)
End Class

''' <summary>
''' A .visual_processed read through ResMgr.openXML, which already decodes
''' BigWorld packed XML. Materials are property children shaped
''' &lt;property&gt;name&lt;Type&gt;value&lt;/Type&gt;&lt;/property&gt;; the node tree is
''' nested &lt;node&gt; elements with an identifier and a row0..row3 transform.
''' </summary>
Public Class TankVisual
    Public renderSets As New List(Of TankRenderSet)
    ''' <summary>Node name -> position, the row3 sums down from Scene Root (translation only, no rotation).</summary>
    Public nodePos As New Dictionary(Of String, Vector3)
    Public bbMin, bbMax As Vector3

    Public Shared Function Load(path As String) As TankVisual
        Dim entry = TankFiles.Lookup(path)
        If entry Is Nothing Then
            LogThis("tank: visual not found {0}", path)
            Return Nothing
        End If
        Dim root As XmlElement = ResMgr.openXML(entry)
        If root Is Nothing Then
            LogThis("tank: visual would not parse {0}", path)
            Return Nothing
        End If
        Dim v As New TankVisual
        For Each rsNode As XmlNode In root.SelectNodes("renderSet")
            Dim rs As New TankRenderSet
            For Each n As XmlNode In rsNode.SelectNodes("node")
                rs.nodes.Add(n.InnerText.Trim())
            Next
            rs.verticesName = TextOf(rsNode.SelectSingleNode("geometry/vertices"))
            rs.primitiveName = TextOf(rsNode.SelectSingleNode("geometry/primitive"))
            For Each pg As XmlNode In rsNode.SelectNodes("geometry/primitiveGroup")
                Dim mat = pg.SelectSingleNode("material")
                If mat IsNot Nothing Then rs.materials.Add(ParseMaterial(mat))
            Next
            v.renderSets.Add(rs)
        Next
        For Each top As XmlNode In root.SelectNodes("node")
            v.WalkNodes(top, Vector3.Zero)
        Next
        v.bbMin = Vec3(TextOf(root.SelectSingleNode("boundingBox/min")))
        v.bbMax = Vec3(TextOf(root.SelectSingleNode("boundingBox/max")))
        LogThis("tank: visual {0}: {1} renderSet(s), {2} node(s), bbox {3} .. {4}",
                IO.Path.GetFileName(path), v.renderSets.Count, v.nodePos.Count, v.bbMin, v.bbMax)
        Return v
    End Function

    Private Sub WalkNodes(n As XmlNode, parentPos As Vector3)
        Dim id = TextOf(n.SelectSingleNode("identifier"))
        Dim row3 = Vec3(TextOf(n.SelectSingleNode("transform/row3")))
        Dim pos = parentPos + row3
        If id <> "" AndAlso Not nodePos.ContainsKey(id) Then nodePos(id) = pos
        For Each c As XmlNode In n.SelectNodes("node")
            WalkNodes(c, pos)
        Next
    End Sub

    ''' <summary>
    ''' A material's properties. Each &lt;property&gt; carries its NAME as text and
    ''' its VALUE as the one child element, whose tag is the type. Unknown names
    ''' go into the floats table when they parse as a number and are otherwise
    ''' ignored - logged once per material so a new one shows up.
    ''' </summary>
    Private Shared Function ParseMaterial(mat As XmlNode) As TankMaterial
        Dim m As New TankMaterial
        m.identifier = TextOf(mat.SelectSingleNode("identifier"))
        m.fx = TextOf(mat.SelectSingleNode("fx"))
        For Each p As XmlNode In mat.SelectNodes("property")
            Dim name = ""
            Dim valueEl As XmlNode = Nothing
            For Each c As XmlNode In p.ChildNodes
                If c.NodeType = XmlNodeType.Text Then
                    name &= c.Value
                ElseIf c.NodeType = XmlNodeType.Element AndAlso valueEl Is Nothing Then
                    valueEl = c
                End If
            Next
            name = name.Trim()
            Dim val = If(valueEl Is Nothing, "", valueEl.InnerText.Trim())
            Select Case name
                Case "diffuseMap" : m.diffuseMap = val
                Case "normalMap" : m.normalMap = val
                Case "metallicGlossMap" : m.metallicGlossMap = val
                Case "excludeMaskAndAOMap" : m.aoMap = val
                Case "metallicDetailMap" : m.detailMap = val
                Case "colorIdMap" : m.colorIdMap = val
                Case "g_detailUVTiling"
                    Dim f = Floats(val)
                    If f.Length >= 2 Then m.detailUVTiling = New Vector4(f(0), f(1), If(f.Length > 2, f(2), 0), If(f.Length > 3, f(3), 0))
                Case "alphaReference" : Integer.TryParse(val, m.alphaReference)
                Case "alphaTestEnable" : m.alphaTestEnable = (val.ToLower() = "true")
                Case "doubleSided" : m.doubleSided = (val.ToLower() = "true")
                Case "g_useNormalPackDXT1" : m.useNormalPackDXT1 = (val.ToLower() = "true")
                Case "g_useDetailMetallic" : m.useDetailMetallic = (val.ToLower() = "true")
                Case Else
                    Dim f As Single
                    If Single.TryParse(val, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, f) Then
                        m.floats(name) = f
                    End If
            End Select
        Next
        Return m
    End Function

    Public Shared Function TextOf(n As XmlNode) As String
        Return If(n Is Nothing, "", n.InnerText.Trim())
    End Function

    Public Shared Function Floats(s As String) As Single()
        Dim parts = s.Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
        Dim out As New List(Of Single)
        For Each p In parts
            Dim f As Single
            If Single.TryParse(p, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, f) Then out.Add(f)
        Next
        Return out.ToArray()
    End Function

    Public Shared Function Vec3(s As String) As Vector3
        Dim f = Floats(s)
        If f.Length < 3 Then Return Vector3.Zero
        Return New Vector3(f(0), f(1), f(2))
    End Function
End Class

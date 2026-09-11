Imports System.Xml
Imports OpenTK.Mathematics

''' <summary>One key of the muzzle light's animation: when, what colour, how hard.</summary>
Public Structure BlastKey
    Public t As Single          ' 0..1 through the light's life
    Public rgb As Vector3       ' 0..1
    Public mult As Single       ' the game's multiplier, 5..25 on a tank gun
End Structure

''' <summary>
''' What the game says a gun's muzzle blast looks like.
'''
''' READ FROM THE GAME'S OWN TABLE rather than invented. A gun entry names an
''' effect - shot_large_mb, shot_largeext, shot_auto - and
''' scripts/item_defs/vehicles/common/gun_effects.xml defines all fifty-one of
''' them. For shot_large_mb, which is what the 121 and the AMX 50B fire:
'''
'''     timeline   lighting2 0.09   end 0.5   endpixie 2.0
'''     pixie      particles/Tank/shots/shot_large_02.eff  at HP_gunFire
'''     light      inner 1.5  outer 8  no shadows          at HP_gunFire
'''                  t 0.00   255 150 0     x20
'''                  t 0.15   255 200 100   x25
'''                  t 0.75   170 65 25     x15
'''                  t 1.00   0 0 0 0       x5
'''     groundWave groundwave_large_&lt;kind&gt;_mb.eff per surface material
'''
''' THE PIXIE IS NOT READABLE and this does not pretend otherwise. BigWorld's
''' .eff files are binary and undocumented - TEPY probed them and found particle
''' simulation parameters but no atlas rectangles, which is why its flipbooks
''' are sliced from the texture atlas by eye instead. So what is taken here is
''' everything the XML states outright: the timeline, the light with all four of
''' its keys, and the radii. That is the half of the blast that carries it - a
''' 25x multiplier over ninety milliseconds is what makes a gun going off look
''' like a gun going off, and it is exact rather than tuned by hand.
'''
''' The ground wave names a file per surfaceMatKind - ground, stone, sand, snow,
''' water, dirt, oil - which is the same set of surfaces the crater decals in
''' maps/fx are cut for. Not used yet; kept in the notes because the two land
''' together.
''' </summary>
Public Class BlastSpec
    Public name As String = ""
    Public innerRadius As Single = 1.5F
    Public outerRadius As Single = 8.0F
    ''' <summary>How long the LIGHT burns, from the timeline key the light's
    ''' endKey names - lighting2 on every tank gun, 0.09 s.</summary>
    Public durationS As Single = 0.09F

    ''' <summary>
    ''' How long the FLASH burns: timeline.end, which is 0.5 s where the light
    ''' is 0.09.
    '''
    ''' TWO CLOCKS, NOT ONE. The light is the sub-tenth-of-a-second stab that
    ''' lights the hull; the flame at the muzzle outlives it by five times and
    ''' is what the flipbook plays across. Running the sprite on the light's
    ''' clock plays eight frames in ninety milliseconds and the plume is gone
    ''' before the eye finds it.
    ''' </summary>
    Public endS As Single = 0.5F

    Public keys As New List(Of BlastKey)

    ''' <summary>
    ''' How long the flame reaches, in metres.
    '''
    ''' FROM outer_radius, which is not the flame's radius - it is the light's -
    ''' but it is a true proxy for how big the gun is, and it is the one number
    ''' in the entry that scales with calibre. The Tiger's 88 ships outer 8.0 and
    ''' that is the reference at 1.2 m, so shot_huge at 15.0 reaches 2.25 m and
    ''' an autocannon's small entry shrinks to match. Clamped at both ends so a
    ''' missing or outlier value cannot produce a postage stamp or a city block.
    ''' </summary>
    Public ReadOnly Property flashLength As Single
        Get
            Return Math.Min(2.5F, Math.Max(0.4F, 1.2F * outerRadius / 8.0F))
        End Get
    End Property

    ''' <summary>Half the length. A muzzle flame is roughly twice as long as it
    ''' is wide on every reference TEPY was built against.</summary>
    Public ReadOnly Property flashThickness As Single
        Get
            Return flashLength * 0.5F
        End Get
    End Property

    ''' <summary>
    ''' Colour times multiplier at a point through the light's life.
    '''
    ''' Interpolated between the keys, flat outside them. The multiplier is
    ''' carried THROUGH the interpolation rather than applied after it: the
    ''' game's keys move colour and intensity together - 255 150 0 at 20 into
    ''' 255 200 100 at 25 - and separating them turns a flash that whitens as it
    ''' peaks into one that just gets brighter.
    ''' </summary>
    Public Function Sample(u As Single) As Vector3
        If keys.Count = 0 Then Return New Vector3(1.0F, 0.7F, 0.35F)
        If u <= keys(0).t Then Return keys(0).rgb * keys(0).mult
        For i = 1 To keys.Count - 1
            If u > keys(i).t Then Continue For
            Dim a = keys(i - 1), b = keys(i)
            Dim d = b.t - a.t
            Dim f = If(d > 1.0E-6F, (u - a.t) / d, 0.0F)
            Return Vector3.Lerp(a.rgb * a.mult, b.rgb * b.mult, f)
        Next
        Dim last = keys(keys.Count - 1)
        Return last.rgb * last.mult
    End Function
End Class

''' <summary>The game's whole gun-effects table, read once.</summary>
Public Module TankBlast

    Private table As Dictionary(Of String, BlastSpec)

    ''' <summary>The spec for an effect name, or a plain default when the table
    ''' or the entry is missing - a gun with no effect still fires.</summary>
    Public Function Lookup(effect As String) As BlastSpec
        If table Is Nothing Then load_table()
        Dim s As BlastSpec = Nothing
        If effect <> "" AndAlso table.TryGetValue(effect, s) Then Return s
        Return fallback
    End Function

    Private ReadOnly fallback As New BlastSpec With {.name = "(default)"}

    Private Sub load_table()
        table = New Dictionary(Of String, BlastSpec)(StringComparer.OrdinalIgnoreCase)

        Const PATH As String = "scripts/item_defs/vehicles/common/gun_effects.xml"
        Dim entry = ResMgr.Lookup(PATH)
        If entry Is Nothing Then
            LogThis("tank: {0} not found - muzzle blasts use the default light", PATH)
            Return
        End If
        Dim root As XmlElement = ResMgr.openXML(entry)
        If root Is Nothing Then
            LogThis("tank: {0} would not parse", PATH)
            Return
        End If

        For Each node As XmlNode In root.ChildNodes
            If node.NodeType <> XmlNodeType.Element Then Continue For
            Dim spec = parse_effect(DirectCast(node, XmlElement))
            If spec IsNot Nothing Then table(node.Name) = spec
        Next
        LogThis("tank: gun_effects.xml - {0} muzzle effect(s)", table.Count)
    End Sub

    Private Function parse_effect(el As XmlElement) As BlastSpec
        Dim lightEl = el.SelectSingleNode("effects/light")
        If lightEl Is Nothing Then Return Nothing

        Dim s As New BlastSpec With {.name = el.Name}

        Dim f = TankVisual.Floats(TankVisual.TextOf(lightEl.SelectSingleNode("innerRadius")))
        If f IsNot Nothing AndAlso f.Length > 0 Then s.innerRadius = f(0)
        f = TankVisual.Floats(TankVisual.TextOf(lightEl.SelectSingleNode("outerRadius")))
        If f IsNot Nothing AndAlso f.Length > 0 Then s.outerRadius = f(0)

        ' The light's life is a NAMED KEY on the timeline, not a number on the
        ' light: endKey says which one, and every tank gun points it at
        ' lighting2. Resolving it by name rather than assuming keeps a gun that
        ' points somewhere else - the autocannons end on `end` - correct.
        Dim endKey = TankVisual.TextOf(lightEl.SelectSingleNode("endKey"))
        If endKey = "" Then endKey = "lighting2"
        f = TankVisual.Floats(TankVisual.TextOf(el.SelectSingleNode("timeline/" & endKey)))
        If f IsNot Nothing AndAlso f.Length > 0 AndAlso f(0) > 0.0F Then s.durationS = f(0)

        ' The flame's own clock, which is a different key.
        f = TankVisual.Floats(TankVisual.TextOf(el.SelectSingleNode("timeline/end")))
        If f IsNot Nothing AndAlso f.Length > 0 AndAlso f(0) > 0.0F Then s.endS = f(0)

        For Each a As XmlNode In lightEl.SelectNodes("animation")
            Dim k As New BlastKey
            f = TankVisual.Floats(TankVisual.TextOf(a.SelectSingleNode("time")))
            If f IsNot Nothing AndAlso f.Length > 0 Then k.t = f(0)

            ' "R G B" or "R G B A", 0..255. The alpha is the game's own fade and
            ' the last key is 0 0 0 0 - the colour going to black already ends
            ' the light, so the alpha carries nothing this needs.
            Dim c = TankVisual.Floats(TankVisual.TextOf(a.SelectSingleNode("color")))
            If c IsNot Nothing AndAlso c.Length >= 3 Then
                k.rgb = New Vector3(c(0), c(1), c(2)) / 255.0F
            End If
            f = TankVisual.Floats(TankVisual.TextOf(a.SelectSingleNode("multiplier")))
            k.mult = If(f IsNot Nothing AndAlso f.Length > 0, f(0), 1.0F)
            s.keys.Add(k)
        Next

        s.keys.Sort(Function(x, y) x.t.CompareTo(y.t))
        Return s
    End Function
End Module

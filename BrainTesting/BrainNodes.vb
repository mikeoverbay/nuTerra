Imports ImGuiNET
Imports OpenTK.Mathematics

''' <summary>
''' THE BRAIN AS A NODE GRAPH - the editor, with no logic behind it yet.
'''
''' "we need types as windows we can drag and delete with wiring. can we get
'''  that in a lower panel in the ui that I can resize... use guesses just so we
'''  can get the UI build first. logic after" - the owner, 2026-09-17.
'''
''' WHY THIS EXISTS. The brain's rules are already a priority tree - fifteen of
''' them, each an `If ... Return`, first match wins. But the precedence lives in
''' LINE ORDER, which means it cannot be seen, cannot be reordered, and cannot
''' be switched off without a rebuild. The corridor test is called "rule one"
''' in its own comment and evaluates TENTH, and nothing about the code made
''' that visible until the rules were written out by hand.
'''
''' A graph makes the order the thing you look at.
'''
''' NOTHING HERE DECIDES ANYTHING YET. The node kinds are guesses at what the
''' sensors and actions will turn out to be, present so the layout can be built
''' against realistic shapes - a node with three inputs lays out differently
''' from one with none. Wiring them up to the brain comes after the editor is
''' worth using.
'''
''' DRAWN WITH DRAW LISTS, like the scope, rather than pulling in a node-editor
''' library. Everything here is rectangles, beziers and hit tests, and the one
''' dependency this app already has is the one it is using.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainNodes

    Public SHOW As Boolean = False

    ''' <summary>Panel height, dragged by its top edge. Kept here rather than in
    ''' ImGui's ini so it survives a layout reset and is one obvious number.</summary>
    Public PanelH As Single = 280.0F
    Private Const MIN_H As Single = 120.0F
    Private Const GRIP As Single = 6.0F

    ' ---- colours, matching the scope's language --------------------------
    Private Const BG As UInteger = &HFF141A22UI
    Private Const NODE_BG As UInteger = &HFF2A3340UI
    Private Const NODE_HDR As UInteger = &HFF3B4A5CUI
    Private Const NODE_SEL As UInteger = &HFF3CDCFFUI      ' yellow, as on the scope
    Private Const EDGE As UInteger = &HFF55708AUI
    Private Const WIRE As UInteger = &HFF9AD8FFUI
    Private Const WIRE_HOT As UInteger = &HFF3CDCFFUI
    Private Const PIN_IN As UInteger = &HFF80C8FFUI
    Private Const PIN_OUT As UInteger = &HFF40E040UI
    Private Const GRID As UInteger = &HFF1E2630UI
    Private Const TXT As UInteger = &HFFD8E4F0UI

    Private Const PIN_R As Single = 5.0F
    Private Const ROW As Single = 18.0F
    Private Const HDR_H As Single = 22.0F

    ''' <summary>
    ''' A node type. GUESSES, deliberately - the point today is that the editor
    ''' lays out correctly for nodes of different shapes, so these span the
    ''' range: no inputs, several inputs, several outputs.
    ''' </summary>
    Public Class Kind
        Public name As String
        Public group As String
        Public ins As String()
        Public outs As String()
        Public Sub New(g As String, n As String, i As String(), o As String())
            group = g : name = n : ins = i : outs = o
        End Sub
    End Class

    Private ReadOnly KINDS As Kind() = {
        New Kind("sense", "Ray Scan", {}, {"hits", "ahead"}),
        New Kind("sense", "Corridor", {"reach"}, {"clear", "dist", "side"}),
        New Kind("sense", "Gaps", {"hits"}, {"ways"}),
        New Kind("sense", "Rear Scan", {"hits"}, {"deepest", "bearing"}),
        New Kind("sense", "Goal", {}, {"bearing", "range"}),
        New Kind("sense", "Body Ahead", {}, {"metres"}),
        New Kind("test", "Is Clear", {"dist"}, {"yes"}),
        New Kind("test", "Wider Than", {"ways", "metres"}, {"ways"}),
        New Kind("test", "Nearer Than", {"metres"}, {"yes"}),
        New Kind("test", "Is Wedged", {}, {"yes"}),
        New Kind("pick", "Widest", {"ways"}, {"way"}),
        New Kind("pick", "Best Progress", {"ways", "bearing"}, {"way"}),
        New Kind("pick", "Deeper Side", {"hits"}, {"bearing"}),
        New Kind("act", "Drive Heading", {"bearing", "throttle"}, {}),
        New Kind("act", "Drive To Point", {"way"}, {}),
        New Kind("act", "Reverse", {"bearing"}, {}),
        New Kind("act", "Stop", {}, {}),
        New Kind("flow", "Priority", {"a", "b", "c", "d"}, {"out"}),
        New Kind("flow", "Sequence", {"a", "b", "c"}, {"out"}),
        New Kind("flow", "Gate", {"in", "when"}, {"out"})
    }

    Public Class Node
        Public id As Integer
        Public kind As Kind
        Public pos As System.Numerics.Vector2
        Public ReadOnly Property w As Single
            Get
                Return 150.0F
            End Get
        End Property
        Public ReadOnly Property h As Single
            Get
                Return HDR_H + ROW * Math.Max(1, Math.Max(kind.ins.Length, kind.outs.Length)) + 8.0F
            End Get
        End Property
    End Class

    ''' <summary>A wire. Endpoints are (node id, socket index); an index into a
    ''' list would go stale the moment a node is deleted.</summary>
    Public Class Link
        Public fromNode As Integer
        Public fromPin As Integer
        Public toNode As Integer
        Public toPin As Integer
    End Class

    Private ReadOnly nodes As New List(Of Node)
    Private ReadOnly links As New List(Of Link)
    Private nextId As Integer = 1
    Private selected As Integer = -1
    Private dragging As Integer = -1
    Private scroll As System.Numerics.Vector2 = New System.Numerics.Vector2(0.0F, 0.0F)

    ' a wire being pulled: which node/pin it started from, and whether from an output
    Private wireNode As Integer = -1
    Private wirePin As Integer = -1
    Private wireFromOut As Boolean = True

    ''' <summary>Where a pin sits on screen, given the node's screen origin.</summary>
    Private Function pin_pos(n As Node, origin As System.Numerics.Vector2,
                             idx As Integer, isIn As Boolean) As System.Numerics.Vector2
        Dim y = origin.Y + HDR_H + ROW * idx + ROW * 0.5F
        Return New System.Numerics.Vector2(If(isIn, origin.X, origin.X + n.w), y)
    End Function

    Private Function find(id As Integer) As Node
        For Each n In nodes
            If n.id = id Then Return n
        Next
        Return Nothing
    End Function

    Public Sub Add(k As Kind, at As System.Numerics.Vector2)
        Dim n As New Node()
        n.id = nextId
        nextId += 1
        n.kind = k
        n.pos = at
        nodes.Add(n)
        selected = n.id
    End Sub

    Private Sub Remove(id As Integer)
        ' The wires go with it. A link to a node that is gone draws to nowhere
        ' and crashes the moment anything walks it.
        links.RemoveAll(Function(l) l.fromNode = id OrElse l.toNode = id)
        nodes.RemoveAll(Function(n) n.id = id)
        If selected = id Then selected = -1
    End Sub

    Public Sub Draw(displayW As Single, displayH As Single)
        If Not SHOW Then Return
        If PanelH < MIN_H Then PanelH = MIN_H
        If PanelH > displayH - 120.0F Then PanelH = displayH - 120.0F

        ImGui.SetNextWindowPos(New System.Numerics.Vector2(0.0F, displayH - PanelH),
                               ImGuiCond.Always)
        ImGui.SetNextWindowSize(New System.Numerics.Vector2(displayW, PanelH),
                                ImGuiCond.Always)
        ImGui.PushStyleColor(ImGuiCol.WindowBg, New System.Numerics.Vector4(
            0.055F, 0.07F, 0.09F, 0.97F))
        ImGui.Begin("Brain graph",
                    ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoResize Or
                    ImGuiWindowFlags.NoCollapse Or ImGuiWindowFlags.NoScrollbar Or
                    ImGuiWindowFlags.NoScrollWithMouse Or ImGuiWindowFlags.NoTitleBar)

        ' ---- THE RESIZE GRIP, along the top edge --------------------------
        '
        ' Its own invisible button rather than ImGui's window resize, because
        ' the window is pinned to the bottom of the screen: ImGui would resize
        ' it from the bottom-right and the panel would grow off the display.
        ImGui.SetCursorScreenPos(New System.Numerics.Vector2(0.0F, displayH - PanelH))
        ImGui.InvisibleButton("##graph_grip", New System.Numerics.Vector2(displayW, GRIP))
        If ImGui.IsItemActive() Then
            PanelH -= ImGui.GetIO().MouseDelta.Y
        End If
        If ImGui.IsItemHovered() OrElse ImGui.IsItemActive() Then
            ImGui.GetWindowDrawList().AddRectFilled(
                New System.Numerics.Vector2(0.0F, displayH - PanelH),
                New System.Numerics.Vector2(displayW, displayH - PanelH + 2.0F), NODE_SEL)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS)
        End If

        ImGui.SetCursorScreenPos(New System.Numerics.Vector2(8.0F, displayH - PanelH + GRIP + 2.0F))
        ImGui.BeginGroup()
        palette()
        ImGui.EndGroup()
        ImGui.SameLine()

        canvas(displayW, displayH)

        ImGui.End()
        ImGui.PopStyleColor()
    End Sub

    ''' <summary>The list of things that can be added. Grouped, because twenty
    ''' flat entries is a list nobody reads.</summary>
    Private Sub palette()
        ' CType(0) rather than a named flag: this ImGui.NET's ImGuiChildFlags
        ' has no Border member and the bool overload is gone, so naming either
        ' is a guess. The border is drawn by hand below, where it cannot go
        ' stale against a binding.
        ImGui.BeginChild("##palette", New System.Numerics.Vector2(150.0F, 0.0F),
                         CType(0, ImGuiChildFlags))
        ImGui.TextDisabled("ADD")
        Dim lastGroup = ""
        For Each k In KINDS
            If k.group <> lastGroup Then
                lastGroup = k.group
                ImGui.Separator()
                ImGui.TextDisabled(k.group.ToUpper())
            End If
            If ImGui.Selectable(k.name) Then
                ' Dropped near the top-left of the canvas, offset by how many
                ' are already there so a run of clicks does not stack them all
                ' in one spot.
                Add(k, New System.Numerics.Vector2(40.0F + (nodes.Count Mod 6) * 24.0F,
                                                   30.0F + (nodes.Count Mod 6) * 20.0F))
            End If
        Next
        ImGui.Separator()
        ImGui.TextDisabled(String.Format("{0} node(s)", nodes.Count))
        ImGui.TextDisabled(String.Format("{0} wire(s)", links.Count))
        If ImGui.Button("Clear", New System.Numerics.Vector2(134.0F, 0.0F)) Then
            nodes.Clear()
            links.Clear()
            selected = -1
        End If
        ImGui.EndChild()
    End Sub

    Private Sub canvas(displayW As Single, displayH As Single)
        ImGui.BeginChild("##canvas", New System.Numerics.Vector2(0.0F, 0.0F),
                         CType(0, ImGuiChildFlags),
                         ImGuiWindowFlags.NoScrollbar Or ImGuiWindowFlags.NoScrollWithMouse)
        Dim dl = ImGui.GetWindowDrawList()
        Dim p0 = ImGui.GetCursorScreenPos()
        Dim size = ImGui.GetContentRegionAvail()
        Dim io = ImGui.GetIO()

        dl.AddRectFilled(p0, New System.Numerics.Vector2(p0.X + size.X, p0.Y + size.Y), BG)
        dl.AddRect(p0, New System.Numerics.Vector2(p0.X + size.X, p0.Y + size.Y), EDGE)

        ' A grid, so dragging has something to read position against.
        Dim step_px = 32.0F
        Dim gx = (scroll.X Mod step_px)
        While gx < size.X
            dl.AddLine(New System.Numerics.Vector2(p0.X + gx, p0.Y),
                       New System.Numerics.Vector2(p0.X + gx, p0.Y + size.Y), GRID)
            gx += step_px
        End While
        Dim gy = (scroll.Y Mod step_px)
        While gy < size.Y
            dl.AddLine(New System.Numerics.Vector2(p0.X, p0.Y + gy),
                       New System.Numerics.Vector2(p0.X + size.X, p0.Y + gy), GRID)
            gy += step_px
        End While

        ' PAN with the right button held on empty canvas.
        ImGui.SetCursorScreenPos(p0)
        ImGui.InvisibleButton("##pan", size, ImGuiButtonFlags.MouseButtonRight)
        If ImGui.IsItemActive() Then
            scroll.X += io.MouseDelta.X
            scroll.Y += io.MouseDelta.Y
        End If

        ' ---- the wires, under the nodes ------------------------------------
        For Each l In links
            Dim a = find(l.fromNode), b = find(l.toNode)
            If a Is Nothing OrElse b Is Nothing Then Continue For
            Dim ao = New System.Numerics.Vector2(p0.X + scroll.X + a.pos.X,
                                                 p0.Y + scroll.Y + a.pos.Y)
            Dim bo = New System.Numerics.Vector2(p0.X + scroll.X + b.pos.X,
                                                 p0.Y + scroll.Y + b.pos.Y)
            Dim pa = pin_pos(a, ao, l.fromPin, False)
            Dim pb = pin_pos(b, bo, l.toPin, True)
            bez(dl, pa, pb, WIRE)
        Next

        ' the wire being pulled right now
        If wireNode >= 0 Then
            Dim a = find(wireNode)
            If a IsNot Nothing Then
                Dim ao = New System.Numerics.Vector2(p0.X + scroll.X + a.pos.X,
                                                     p0.Y + scroll.Y + a.pos.Y)
                Dim pa = pin_pos(a, ao, wirePin, Not wireFromOut)
                bez(dl, pa, io.MousePos, WIRE_HOT)
            End If
            If Not ImGui.IsMouseDown(ImGuiMouseButton.Left) Then
                drop_wire(p0)
            End If
        End If

        ' ---- the nodes ------------------------------------------------------
        For Each n In nodes
            Dim o = New System.Numerics.Vector2(p0.X + scroll.X + n.pos.X,
                                                p0.Y + scroll.Y + n.pos.Y)
            Dim br = New System.Numerics.Vector2(o.X + n.w, o.Y + n.h)

            dl.AddRectFilled(o, br, NODE_BG, 4.0F)
            dl.AddRectFilled(o, New System.Numerics.Vector2(br.X, o.Y + HDR_H), NODE_HDR, 4.0F)
            dl.AddRect(o, br, If(selected = n.id, NODE_SEL, EDGE), 4.0F, 0,
                       If(selected = n.id, 2.0F, 1.0F))
            dl.AddText(New System.Numerics.Vector2(o.X + 8.0F, o.Y + 4.0F), TXT, n.kind.name)

            ' drag by the header
            ImGui.SetCursorScreenPos(o)
            ImGui.InvisibleButton("##n" & n.id.ToString(),
                                  New System.Numerics.Vector2(n.w, HDR_H))
            If ImGui.IsItemActive() Then
                selected = n.id
                dragging = n.id
                n.pos = New System.Numerics.Vector2(n.pos.X + io.MouseDelta.X,
                                                    n.pos.Y + io.MouseDelta.Y)
            End If

            For i = 0 To n.kind.ins.Length - 1
                Dim pp = pin_pos(n, o, i, True)
                dl.AddCircleFilled(pp, PIN_R, PIN_IN, 10)
                dl.AddText(New System.Numerics.Vector2(pp.X + 9.0F, pp.Y - 7.0F),
                           TXT, n.kind.ins(i))
                pin_button(n, i, True, pp)
            Next
            For i = 0 To n.kind.outs.Length - 1
                Dim pp = pin_pos(n, o, i, False)
                dl.AddCircleFilled(pp, PIN_R, PIN_OUT, 10)
                Dim tw = ImGui.CalcTextSize(n.kind.outs(i)).X
                dl.AddText(New System.Numerics.Vector2(pp.X - 9.0F - tw, pp.Y - 7.0F),
                           TXT, n.kind.outs(i))
                pin_button(n, i, False, pp)
            Next
        Next

        If Not ImGui.IsMouseDown(ImGuiMouseButton.Left) Then dragging = -1

        ' DELETE takes the selected node and its wires with it.
        If selected >= 0 AndAlso ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) Then
            If ImGui.IsKeyPressed(ImGuiKey.Delete) Then Remove(selected)
        End If

        ImGui.EndChild()
    End Sub

    ''' <summary>A pin is a small button so ImGui owns the hit test; drawing it
    ''' and testing it separately is how a pin ends up catching clicks a few
    ''' pixels from where it appears.</summary>
    Private Sub pin_button(n As Node, idx As Integer, isIn As Boolean,
                           at As System.Numerics.Vector2)
        ImGui.SetCursorScreenPos(New System.Numerics.Vector2(at.X - PIN_R - 2.0F,
                                                             at.Y - PIN_R - 2.0F))
        ImGui.InvisibleButton("##p" & n.id.ToString() & If(isIn, "i", "o") & idx.ToString(),
                              New System.Numerics.Vector2(PIN_R * 2.0F + 4.0F,
                                                          PIN_R * 2.0F + 4.0F))
        If ImGui.IsItemActive() AndAlso wireNode < 0 Then
            wireNode = n.id
            wirePin = idx
            wireFromOut = Not isIn
        End If
        If wireNode >= 0 AndAlso ImGui.IsItemHovered() Then
            hoverNode = n.id
            hoverPin = idx
            hoverIn = isIn
        End If
    End Sub

    Private hoverNode As Integer = -1
    Private hoverPin As Integer = -1
    Private hoverIn As Boolean = False

    ''' <summary>Let go of a wire. Only output-to-input counts, in either drag
    ''' direction, and an input takes ONE wire - a second into the same socket
    ''' replaces the first rather than stacking invisibly.</summary>
    Private Sub drop_wire(p0 As System.Numerics.Vector2)
        If hoverNode >= 0 AndAlso hoverNode <> wireNode AndAlso hoverIn <> wireFromOut Then
            Dim l As New Link()
            If wireFromOut Then
                l.fromNode = wireNode : l.fromPin = wirePin
                l.toNode = hoverNode : l.toPin = hoverPin
            Else
                l.fromNode = hoverNode : l.fromPin = hoverPin
                l.toNode = wireNode : l.toPin = wirePin
            End If
            links.RemoveAll(Function(x) x.toNode = l.toNode AndAlso x.toPin = l.toPin)
            links.Add(l)
        End If
        wireNode = -1
        wirePin = -1
        hoverNode = -1
        hoverPin = -1
    End Sub

    Private Sub bez(dl As ImDrawListPtr, a As System.Numerics.Vector2,
                    b As System.Numerics.Vector2, col As UInteger)
        Dim dx = Math.Max(40.0F, Math.Abs(b.X - a.X) * 0.5F)
        dl.AddBezierCubic(a, New System.Numerics.Vector2(a.X + dx, a.Y),
                          New System.Numerics.Vector2(b.X - dx, b.Y), b, col, 2.0F)
    End Sub

End Module

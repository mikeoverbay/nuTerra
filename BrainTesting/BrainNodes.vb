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

    ''' <summary>
    ''' Drawn into the node window's own context rather than over the main
    ''' window. The graph does not change; where it is painted does.
    ''' </summary>
    Public Hosted As Boolean = False

    ''' <summary>
    ''' HOW FAR THE WINDOW HAS BEEN DRAGGED THIS FRAME, for the form to
    ''' move by. The form has no border, so ImGui's title bar is the only
    ''' thing to grab - and what ImGui does with a drag is move its own
    ''' window inside the client area, which would walk it off the edge of
    ''' a window that is exactly its size. So the drag is measured, handed
    ''' over as a move, and undone. The form moves; the window never does.
    ''' </summary>
    Public HostDX As Single = 0.0F
    Public HostDY As Single = 0.0F

    ''' <summary>What the window has been resized to, for the form to match.
    ''' No undo needed here: the window's size IS the client size, so the
    ''' form just follows it.</summary>
    Public HostW As Single = 0.0F
    Public HostH As Single = 0.0F

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
    Private Const HOVER As UInteger = &HFFBFD9EBUI         ' pale, one step under selected
    Private Const PIN_OK As UInteger = &HFF40E040UI        ' will take this wire
    Private Const PIN_NO As UInteger = &HFF4040FFUI        ' will not - ABGR, so red
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

    ''' <summary>
    ''' WHAT A PIN CARRIES, worked out from its name.
    '''
    ''' The names were already the types - hits, ways, bearing, metres, yes -
    ''' they simply were not written down anywhere a check could read them.
    ''' Deriving rather than adding a field to every Kind keeps ONE spelling of
    ''' each concept: a pin called ways IS a ways pin, and a typo in a Kind
    ''' becomes a visible mismatch instead of a second type nobody declared.
    '''
    ''' FLOW is the odd one and the important one. a/b/c/d/in/out carry an
    ''' action-or-nothing between Priority, Sequence and Gate; everything else
    ''' is data. Keeping those two apart is what stops a graph rotting, and it
    ''' is cheap now and miserable to retrofit.
    ''' </summary>
    Private Function pin_type(name As String) As String
        Select Case name
            Case "hits" : Return "hits"
            Case "ways" : Return "ways"
            Case "way" : Return "way"
            Case "bearing" : Return "angle"
            Case "metres", "dist", "reach" : Return "length"
            Case "clear", "yes", "when" : Return "bool"
            Case "side" : Return "side"
            Case "throttle" : Return "number"
            Case "a", "b", "c", "d", "in", "out" : Return "flow"
            Case Else : Return name
        End Select
    End Function

    ''' <summary>Could this drag end here? Output to input or input to output,
    ''' never like to like, never onto itself, and the two must carry the same
    ''' thing.</summary>
    Private Function can_join(fromNodeId As Integer, fromIdx As Integer,
                              fromIsOut As Boolean, toNodeId As Integer,
                              toIdx As Integer, toIsIn As Boolean) As Boolean
        If fromNodeId = toNodeId Then Return False
        ' The drag started on an output exactly when it must land on an input.
        If fromIsOut <> toIsIn Then Return False
        Dim a = find(fromNodeId), b = find(toNodeId)
        If a Is Nothing OrElse b Is Nothing Then Return False
        Dim an = If(fromIsOut, a.kind.outs, a.kind.ins)
        Dim bn = If(toIsIn, b.kind.ins, b.kind.outs)
        If fromIdx < 0 OrElse fromIdx >= an.Length Then Return False
        If toIdx < 0 OrElse toIdx >= bn.Length Then Return False
        Return pin_type(an(fromIdx)) = pin_type(bn(toIdx))
    End Function

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

    ''' <summary>
    ''' THE GRAPH HAS BEEN EDITED SINCE IT WAS LAST SAVED.
    '''
    ''' Set wherever the graph itself changes - a node added or deleted, a
    ''' wire made or broken, a node moved - and NOT for anything that only
    ''' changes the view. Panning, zooming and selecting leave it alone,
    ''' because a mark that turns on when you look at something tells you
    ''' nothing about whether you would lose work by closing it.
    ''' </summary>
    Public Changed As Boolean = False

    ''' <summary>Closing the panel must not cost the graph. Nodes, links and
    ''' the board position all live in module state, so hiding the window
    ''' stops it being drawn and touches nothing else - reopening finds it
    ''' exactly as it was, torn-off window and all.</summary>
    Private Const KEEPS_ITS_DATA As Boolean = True
    Private selected As Integer = -1
    Private dragging As Integer = -1
    Private scroll As System.Numerics.Vector2 = New System.Numerics.Vector2(0.0F, 0.0F)

    ''' <summary>
    ''' BOARD ZOOM. Node positions and sizes are stored in BOARD coordinates
    ''' and multiplied by this on the way to the screen, so zooming never
    ''' touches the graph itself - a saved layout is the same layout whatever
    ''' it was last looked at.
    '''
    ''' Clamped rather than unbounded: past about 3x a node is bigger than the
    ''' panel and past a quarter its title is unreadable, so both ends are
    ''' places you can only get lost.
    ''' </summary>
    Private zoom As Single = 1.0F
    Private Const ZOOM_MIN As Single = 0.3F
    Private Const ZOOM_MAX As Single = 3.0F

    ''' <summary>Which node the pointer is over, for the hover outline. Read
    ''' from ImGui's own hit test rather than compared against rectangles, so
    ''' the pins stacked on top of a node take precedence exactly as they do
    ''' for clicks.</summary>
    Private hovered As Integer = -1

    ' a wire being pulled: which node/pin it started from, and whether from an output
    Private wireNode As Integer = -1
    Private wirePin As Integer = -1
    Private wireFromOut As Boolean = True

    ''' <summary>Where a pin sits on screen, given the node's screen origin.</summary>
    Private Function pin_pos(n As Node, origin As System.Numerics.Vector2,
                             idx As Integer, isIn As Boolean) As System.Numerics.Vector2
        Dim y = origin.Y + (HDR_H + ROW * idx + ROW * 0.5F) * zoom
        Return New System.Numerics.Vector2(If(isIn, origin.X, origin.X + n.w * zoom), y)
    End Function

    ''' <summary>Board coordinates to screen. One place, so a node, its pins
    ''' and its wires cannot end up on three different transforms.</summary>
    Private Function to_screen(p0 As System.Numerics.Vector2,
                               b As System.Numerics.Vector2) As System.Numerics.Vector2
        Return New System.Numerics.Vector2(p0.X + scroll.X + b.X * zoom,
                                           p0.Y + scroll.Y + b.Y * zoom)
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
        Changed = True
    End Sub

    Private Sub Remove(id As Integer)
        ' The wires go with it. A link to a node that is gone draws to nowhere
        ' and crashes the moment anything walks it.
        links.RemoveAll(Function(l) l.fromNode = id OrElse l.toNode = id)
        nodes.RemoveAll(Function(n) n.id = id)
        If selected = id Then selected = -1
        Changed = True
    End Sub

    Public Sub Draw(displayW As Single, displayH As Single)
        ' ---- ITS OWN SWITCH ------------------------------------------------
        '
        ' The G that opens this lives in BrainPanel now, not here. It has to:
        ' once the editor draws only in its own window, a HIDDEN window is one
        ' that runs no frames, so the key that would bring it back would never
        ' be read. The switch has to sit somewhere that is always drawing.
        ' The toggle lives with the other view switches in BrainPanel, beside
        ' Radar and Scope. A floating button of its own was tried and was
        ' wrong: those two are modules with SHOW flags toggled from the panel,
        ' so an editor that hid its switch somewhere else was the odd one out,
        ' and being consistent with where switches live beats owning one more
        ' line of this file.
        If Not SHOW Then Return
        ' ---- AN ORDINARY WINDOW: DRAG IT AND SIZE IT ------------------------
        '
        ' "I will drag to where I want."
        '
        ' It was pinned to the bottom edge at full width, with NoMove, NoResize
        ' and a hand-rolled grip along its top - all of which existed only to
        ' work around the pinning. Unpinned, ImGui's own title bar drags it and
        ' its own corner resizes it, and thirty lines of grip code stop being
        ' needed at all.
        '
        ' FirstUseEver, not Always: the position and size are a STARTING point.
        ' Always would put it back at the bottom every frame, which is the same
        ' as not letting it move - and ImGui remembers where it was left in its
        ' ini, so a placement survives a restart.
        If Hosted Then
            ' IT IS THE WHOLE WINDOW. FirstUseEver and not Always even here,
            ' because the size is then free to be dragged by the corner - and
            ' that resize is what the form follows.
            ImGui.SetNextWindowPos(New System.Numerics.Vector2(0.0F, 0.0F),
                                   ImGuiCond.FirstUseEver)
            ImGui.SetNextWindowSize(New System.Numerics.Vector2(displayW, displayH),
                                    ImGuiCond.FirstUseEver)
        Else
            ImGui.SetNextWindowPos(New System.Numerics.Vector2(40.0F, displayH - 360.0F),
                                   ImGuiCond.FirstUseEver)
            ImGui.SetNextWindowSize(New System.Numerics.Vector2(
                Math.Min(980.0F, displayW - 80.0F), 320.0F), ImGuiCond.FirstUseEver)
        End If
        ImGui.SetNextWindowSizeConstraints(New System.Numerics.Vector2(420.0F, MIN_H),
                                           New System.Numerics.Vector2(9999.0F, 9999.0F))
        ImGui.PushStyleColor(ImGuiCol.WindowBg, New System.Numerics.Vector4(
            0.055F, 0.07F, 0.09F, 0.97F))
        ' A CLOSE BOX, and the title says whether closing would cost anything.
        '
        ' It matters more than it looks: once this panel can be dragged into a
        ' frameless window of its own there is no OS close button on it at
        ' all, so without this the only way back is the checkbox in the panel
        ' behind it - which is exactly the window you have just covered up.
        '
        ' Passing a flag to Begin is what puts the X there. ImGui writes False
        ' into it when the X is clicked, and closing the LAST window in a
        ' torn-off viewport is what makes ImGui destroy that viewport - which
        ' is what shuts the form. Nothing here closes a window directly.
        Dim open As Boolean = True
        ImGui.Begin(If(Changed, "Brain graph *", "Brain graph"), open,
                    ImGuiWindowFlags.NoCollapse Or ImGuiWindowFlags.NoScrollbar Or
                    ImGuiWindowFlags.NoScrollWithMouse)
        If Not open Then SHOW = False

        If Hosted Then
            ' ---- THE MOVE COMMAND ------------------------------------------
            '
            ' ImGui has ALREADY moved the window this frame, to mouse minus the
            ' offset the drag started at. Read how far that put it from the
            ' origin, hand it over, and put it back NOW - in this frame, before
            ' anything is laid out. Undo it next frame instead and every other
            ' mouse delta goes in the bin, which reads as a window that moves
            ' at half speed and lags the pointer.
            Dim wp = ImGui.GetWindowPos()
            If wp.X <> 0.0F OrElse wp.Y <> 0.0F Then
                HostDX += wp.X
                HostDY += wp.Y
                ImGui.SetWindowPos(New System.Numerics.Vector2(0.0F, 0.0F))
            End If
            Dim ws = ImGui.GetWindowSize()
            HostW = ws.X
            HostH = ws.Y
        End If

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
                ' In BOARD coordinates, allowing for where the board has been
                ' scrolled and zoomed to - otherwise a node added while panned
                ' away is created somewhere off screen.
                Add(k, New System.Numerics.Vector2(
                    (-scroll.X + 40.0F + (nodes.Count Mod 6) * 24.0F) / zoom,
                    (-scroll.Y + 30.0F + (nodes.Count Mod 6) * 20.0F) / zoom))
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

        ' A grid, so dragging has something to read position against. It
        ' scales with the board, which is what makes a zoom read as moving
        ' closer rather than as the nodes changing size.
        Dim step_px = 32.0F * zoom
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

        ' ZOOM ABOUT THE POINTER, not about the corner. The board point under
        ' the cursor is worked out BEFORE the zoom changes and scroll is then
        ' set so that same point lands back under the cursor after it - which
        ' is why the thing being looked at stays put instead of sliding away.
        If ImGui.IsWindowHovered() AndAlso Math.Abs(io.MouseWheel) > 0.001F Then
            Dim before = New System.Numerics.Vector2(
                (io.MousePos.X - p0.X - scroll.X) / zoom,
                (io.MousePos.Y - p0.Y - scroll.Y) / zoom)
            zoom = Math.Clamp(zoom * (1.0F + io.MouseWheel * 0.12F), ZOOM_MIN, ZOOM_MAX)
            scroll.X = io.MousePos.X - p0.X - before.X * zoom
            scroll.Y = io.MousePos.Y - p0.Y - before.Y * zoom
        End If

        ' ---- the wires, under the nodes ------------------------------------
        For Each l In links
            Dim a = find(l.fromNode), b = find(l.toNode)
            If a Is Nothing OrElse b Is Nothing Then Continue For
            Dim ao = to_screen(p0, a.pos)
            Dim bo = to_screen(p0, b.pos)
            Dim pa = pin_pos(a, ao, l.fromPin, False)
            Dim pb = pin_pos(b, bo, l.toPin, True)
            bez(dl, pa, pb, WIRE)
        Next

        ' the wire being pulled right now
        If wireNode >= 0 Then
            Dim a = find(wireNode)
            If a IsNot Nothing Then
                Dim ao = to_screen(p0, a.pos)
                Dim pa = pin_pos(a, ao, wirePin, Not wireFromOut)
                ' The band itself says yes or no, so the answer is under the
                ' cursor rather than out at the pin being hovered.
                Dim band = WIRE_HOT
                If hoverNode >= 0 Then
                    band = If(can_join(wireNode, wirePin, wireFromOut,
                                       hoverNode, hoverPin, hoverIn), PIN_OK, PIN_NO)
                End If
                bez(dl, pa, io.MousePos, band)
            End If
            If Not ImGui.IsMouseDown(ImGuiMouseButton.Left) Then
                drop_wire(p0)
            End If
        End If

        ' ---- the nodes ------------------------------------------------------
        Dim font = ImGui.GetFont()
        Dim fsz = ImGui.GetFontSize() * zoom
        hovered = -1
        For Each n In nodes
            Dim o = to_screen(p0, n.pos)
            Dim br = New System.Numerics.Vector2(o.X + n.w * zoom, o.Y + n.h * zoom)

            ' THE WHOLE BODY IS THE HANDLE, not just the header. Submitted
            ' BEFORE the pins so a pin on top of it still wins a click - the
            ' same last-submitted-wins rule the pan button relies on.
            ImGui.SetCursorScreenPos(o)
            ImGui.InvisibleButton("##n" & n.id.ToString(),
                                  New System.Numerics.Vector2(n.w * zoom, n.h * zoom))
            Dim hot = ImGui.IsItemHovered()
            If hot Then hovered = n.id
            If ImGui.IsItemActive() Then
                selected = n.id
                dragging = n.id
                ' Divided by the zoom: the pointer moves in SCREEN pixels and
                ' the node lives in board ones, so without this a node zoomed
                ' out to a third drags three times as far as the cursor.
                Changed = True
                n.pos = New System.Numerics.Vector2(n.pos.X + io.MouseDelta.X / zoom,
                                                    n.pos.Y + io.MouseDelta.Y / zoom)
                o = to_screen(p0, n.pos)
                br = New System.Numerics.Vector2(o.X + n.w * zoom, o.Y + n.h * zoom)
            End If

            dl.AddRectFilled(o, br, NODE_BG, 4.0F * zoom)
            dl.AddRectFilled(o, New System.Numerics.Vector2(br.X, o.Y + HDR_H * zoom),
                             NODE_HDR, 4.0F * zoom)

            ' Selected outlines yellow and thick; merely HOVERED gets a paler
            ' lift, so "this is the one that will move" reads before the button
            ' goes down rather than after.
            Dim edgeCol = EDGE
            Dim edgeW = 1.0F
            If selected = n.id Then
                edgeCol = NODE_SEL
                edgeW = 2.0F
            ElseIf hot Then
                edgeCol = HOVER
                edgeW = 2.0F
            End If
            dl.AddRect(o, br, edgeCol, 4.0F * zoom, 0, edgeW)
            dl.AddText(font, fsz,
                       New System.Numerics.Vector2(o.X + 8.0F * zoom, o.Y + 4.0F * zoom),
                       TXT, n.kind.name)

            For i = 0 To n.kind.ins.Length - 1
                Dim pp = pin_pos(n, o, i, True)
                dl.AddCircleFilled(pp, PIN_R * zoom, PIN_IN, 10)
                mark_target(dl, n, i, True, pp)
                dl.AddText(font, fsz,
                           New System.Numerics.Vector2(pp.X + 9.0F * zoom,
                                                       pp.Y - fsz * 0.5F),
                           TXT, n.kind.ins(i))
                pin_button(n, i, True, pp)
            Next
            For i = 0 To n.kind.outs.Length - 1
                Dim pp = pin_pos(n, o, i, False)
                dl.AddCircleFilled(pp, PIN_R * zoom, PIN_OUT, 10)
                mark_target(dl, n, i, False, pp)
                Dim tw = ImGui.CalcTextSize(n.kind.outs(i)).X * zoom
                dl.AddText(font, fsz,
                           New System.Numerics.Vector2(pp.X - 9.0F * zoom - tw,
                                                       pp.Y - fsz * 0.5F),
                           TXT, n.kind.outs(i))
                pin_button(n, i, False, pp)
            Next
        Next

        ' ---- PAN THE BOARD, LAST AND ONLY IF NOTHING ELSE WANTED IT ---------
        '
        ' The first version submitted this FIRST, covering the whole canvas, on
        ' the theory that ImGui resolves overlapping items in favour of the last
        ' one submitted. It does not reliably: the pan button took the press and
        ' held ActiveId for the whole gesture, so the board moved and nodes
        ' never budged.
        '
        ' So it is not a race any more. Nodes and pins are submitted above; if
        ' any of them is hovered, being dragged, or pulling a wire, the pan
        ' button is NOT SUBMITTED AT ALL and cannot take anything. A button that
        ' does not exist cannot win.
        If hovered < 0 AndAlso dragging < 0 AndAlso wireNode < 0 Then
            ImGui.SetCursorScreenPos(p0)
            ImGui.InvisibleButton("##pan", size,
                                  ImGuiButtonFlags.MouseButtonLeft Or
                                  ImGuiButtonFlags.MouseButtonRight)
            If ImGui.IsItemActive() Then
                scroll.X += io.MouseDelta.X
                scroll.Y += io.MouseDelta.Y
                ' A drag that began on empty board is a pan, not a selection.
                If ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3.0F) Then selected = -1
            End If

            ' DOUBLE CLICK ON EMPTY BOARD FRAMES EVERYTHING. It can live inside
            ' this block with no test of its own: the block only exists when
            ' nothing is hovered, dragged or being wired, so "on the pan button"
            ' already MEANS "on empty board".
            If ImGui.IsItemHovered() AndAlso
               ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) Then
                fit_all(size)
            End If
        End If

        If Not ImGui.IsMouseDown(ImGuiMouseButton.Left) Then dragging = -1

        ' DELETE takes the selected node and its wires with it.
        If selected >= 0 AndAlso ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) Then
            If ImGui.IsKeyPressed(ImGuiKey.Delete) Then Remove(selected)
        End If

        ImGui.EndChild()
    End Sub

    ''' <summary>
    ''' FRAME EVERY NODE IN THE CANVAS.
    '''
    ''' The bounding box is taken in BOARD coordinates - n.pos with n.w and n.h,
    ''' all of which are unscaled - and mapped onto the canvas rectangle, which
    ''' is in screen pixels. Mixing those two is the obvious way to get this
    ''' wrong, and the symptom would be a fit that is correct at zoom 1 and
    ''' wrong at every other zoom, which is the sort of thing that reads as
    ''' "sometimes it works".
    '''
    ''' The centring falls out of the transform rather than being fiddled: a
    ''' board point b lands at scroll + b*zoom, so putting the box's centre at
    ''' the canvas centre is scroll = size/2 - centre*zoom.
    '''
    ''' An empty board resets rather than dividing by a bounding box that does
    ''' not exist.
    ''' </summary>
    Private Sub fit_all(size As System.Numerics.Vector2)
        If nodes.Count = 0 Then
            zoom = 1.0F
            scroll = New System.Numerics.Vector2(0.0F, 0.0F)
            Return
        End If

        Dim minX = Single.MaxValue, minY = Single.MaxValue
        Dim maxX = Single.MinValue, maxY = Single.MinValue
        For Each n In nodes
            minX = Math.Min(minX, n.pos.X)
            minY = Math.Min(minY, n.pos.Y)
            maxX = Math.Max(maxX, n.pos.X + n.w)
            maxY = Math.Max(maxY, n.pos.Y + n.h)
        Next

        ' Room for the pin labels, which hang OUTSIDE the node rectangle on
        ' both sides and would otherwise be cropped by a fit that framed the
        ' bodies exactly.
        Const PAD As Single = 60.0F
        Dim bw = Math.Max(1.0F, maxX - minX)
        Dim bh = Math.Max(1.0F, maxY - minY)
        Dim fitX = (size.X - PAD * 2.0F) / bw
        Dim fitY = (size.Y - PAD * 2.0F) / bh
        zoom = Math.Clamp(Math.Min(fitX, fitY), ZOOM_MIN, ZOOM_MAX)

        Dim cx = (minX + maxX) * 0.5F
        Dim cy = (minY + maxY) * 0.5F
        scroll.X = size.X * 0.5F - cx * zoom
        scroll.Y = size.Y * 0.5F - cy * zoom
    End Sub

    ''' <summary>A pin is a small button so ImGui owns the hit test; drawing it
    ''' and testing it separately is how a pin ends up catching clicks a few
    ''' pixels from where it appears.</summary>
    Private Sub pin_button(n As Node, idx As Integer, isIn As Boolean,
                           at As System.Numerics.Vector2)
        ' The hit area never shrinks below something a pointer can hit, however
        ' far the board is zoomed out - a pin you cannot click is worse than a
        ' pin drawn slightly larger than it looks.
        Dim grab = Math.Max(PIN_R * zoom + 2.0F, 6.0F)
        ImGui.SetCursorScreenPos(New System.Numerics.Vector2(at.X - grab, at.Y - grab))
        ImGui.InvisibleButton("##p" & n.id.ToString() & If(isIn, "i", "o") & idx.ToString(),
                              New System.Numerics.Vector2(grab * 2.0F, grab * 2.0F))
        If ImGui.IsItemActive() AndAlso wireNode < 0 Then
            ' PICKING UP A CONNECTED INPUT TAKES THE WIRE WITH IT. The link is
            ' removed now and the drag continues from its SOURCE output, so the
            ' same gesture re-routes it elsewhere or - dropped on nothing -
            ' disconnects it. Without this an input could only be overwritten,
            ' never cleared.
            Dim held = links.Find(Function(l) l.toNode = n.id AndAlso l.toPin = idx)
            If isIn AndAlso held IsNot Nothing Then
                links.Remove(held)
                Changed = True
                wireNode = held.fromNode
                wirePin = held.fromPin
                wireFromOut = True
            Else
                wireNode = n.id
                wirePin = idx
                wireFromOut = Not isIn
            End If
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
    ''' <summary>
    ''' Let go of a wire.
    '''
    ''' THE TEST USED TO BE hoverIn <> wireFromOut AND REJECTED EVERYTHING.
    ''' Dragging from an output sets wireFromOut True and landing on an input
    ''' gives hoverIn True, so the two are TRUE TOGETHER on a good connection -
    ''' the inequality threw out both valid cases and no wire could ever be
    ''' made. It reads backwards, which is exactly how it came to be written
    ''' that way, so the rule now lives in can_join with the halves named.
    '''
    ''' An input takes ONE wire: a second into the same socket replaces the
    ''' first rather than stacking invisibly behind it.
    ''' </summary>
    Private Sub drop_wire(p0 As System.Numerics.Vector2)
        If hoverNode >= 0 AndAlso
           can_join(wireNode, wirePin, wireFromOut, hoverNode, hoverPin, hoverIn) Then
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
            Changed = True
        End If
        wireNode = -1
        wirePin = -1
        hoverNode = -1
        hoverPin = -1
    End Sub

    ''' <summary>
    ''' While a wire is being pulled, ring every pin that WOULD take it and
    ''' cross the one under the cursor that would not.
    '''
    ''' The moment to know is BEFORE letting go. A drop that silently does
    ''' nothing is indistinguishable from a drop that missed, so the gesture
    ''' teaches nothing either way.
    ''' </summary>
    Private Sub mark_target(dl As ImDrawListPtr, n As Node, idx As Integer,
                            isIn As Boolean, at As System.Numerics.Vector2)
        If wireNode < 0 Then Return
        Dim ok = can_join(wireNode, wirePin, wireFromOut, n.id, idx, isIn)
        Dim r = PIN_R * zoom + 3.0F
        If ok Then
            dl.AddCircle(at, r, PIN_OK, 12, 2.0F)
        ElseIf n.id <> wireNode Then
            ' A cross, not a dimming: will-not-take has to be legible against a
            ' board that is already mostly dim.
            Dim d = r * 0.7F
            dl.AddLine(New System.Numerics.Vector2(at.X - d, at.Y - d),
                       New System.Numerics.Vector2(at.X + d, at.Y + d), PIN_NO, 1.6F)
            dl.AddLine(New System.Numerics.Vector2(at.X - d, at.Y + d),
                       New System.Numerics.Vector2(at.X + d, at.Y - d), PIN_NO, 1.6F)
        End If
    End Sub

    Private Sub bez(dl As ImDrawListPtr, a As System.Numerics.Vector2,
                    b As System.Numerics.Vector2, col As UInteger)
        Dim dx = Math.Max(40.0F, Math.Abs(b.X - a.X) * 0.5F)
        dl.AddBezierCubic(a, New System.Numerics.Vector2(a.X + dx, a.Y),
                          New System.Numerics.Vector2(b.X - dx, b.Y), b, col, 2.0F)
    End Sub

End Module

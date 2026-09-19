Imports System.Text
Imports System.Text.Json
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

    ''' <summary>The pin-help panel is open. Remembered, so closing it
    ''' stays closed until it is asked for again.</summary>
    Public HELP_SHOW As Boolean = True

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

    ''' <summary>
    ''' WHAT THE WINDOW BUTTONS ASKED FOR: 0 nothing, 1 minimise, 2 maximise or
    ''' restore. Posted here and carried out by the form, the same way the drag
    ''' is - this file knows what was clicked, the form knows what a window is.
    '''
    ''' A borderless form has no minimise or maximise box, so if these are not
    ''' drawn they do not exist. That is the cost of dropping the OS frame, and
    ''' paying it deliberately is better than a caption bar above a title bar.
    ''' </summary>
    Public HostCmd As Integer = 0

    ''' <summary>Maximised, so the button can offer the way back.</summary>
    Public HostMaxed As Boolean = False

    ''' <summary>
    ''' The FORM changed its own size and the window must follow, for one frame,
    ''' with Always.
    '''
    ''' Without it maximising fights itself: the form goes full screen, the
    ''' window is still its old size, and the very next frame the form is told
    ''' to shrink back to it. The window follows the form here; every other
    ''' frame the form follows the window.
    ''' </summary>
    Public HostResync As Boolean = False

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
    Private Const FIRED_EDGE As UInteger = &HFF40E040UI    ' the walk came through here
    Private Const ACTED_EDGE As UInteger = &HFF3CDCFFUI    ' and this is what it did
    Private Const ANS_YES As UInteger = &HFF40E040UI
    Private Const ANS_NO As UInteger = &HFF4040FFUI        ' ABGR, so red
    Private Const VAL_TXT As UInteger = &HFF9FE0FFUI       ' the live value
    Private Const WIRE_PICK As UInteger = &HFFD070FFUI     ' ABGR, so pink
    Private Const WIRE_LIVE As UInteger = &HFF3B3BFFUI     ' ABGR, so red

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

        ''' <summary>What this kind can be set to, and to what by default.
        ''' Empty for most - a node with nothing to tune should not grow a
        ''' settings box that says so.</summary>
        Public setNames As String()
        Public setDefs As Single()

        Public Sub New(g As String, n As String, i As String(), o As String(),
                       Optional sn As String() = Nothing,
                       Optional sd As Single() = Nothing)
            group = g : name = n : ins = i : outs = o
            setNames = If(sn, New String() {})
            setDefs = If(sd, New Single() {})
        End Sub

        Public Function DefaultFor(setting As String) As Single
            Dim i = Array.IndexOf(setNames, setting)
            If i < 0 OrElse i >= setDefs.Length Then Return 0.0F
            Return setDefs(i)
        End Function
    End Class

    ''' <summary>
    ''' THE VOCABULARY, and it is the brain's, not a guess any more.
    '''
    ''' The first set of these was written before there was anything to model -
    ''' "use guesses just so we can get the UI build first". Modelling RangeBrain
    ''' found six things it had no word for at all (arrived, will-clear, trap
    ''' marking, doors, wall following, and state itself) and one thing that was
    ''' wrong rather than missing.
    '''
    ''' THE FLOW PINS POINTED THE WRONG WAY. Priority took a, b, c, d IN and gave
    ''' one out, and the act nodes had no flow pin at all - so an action could
    ''' never be reached by anything, and the flow group was decoration. A
    ''' priority runs its children in order, so it takes ONE in and offers four
    ''' outs, and every act now has an `in` to be reached by. Nothing could have
    ''' been wired end to end before this.
    ''' </summary>
    Private ReadOnly KINDS As Kind() = {
        New Kind("sense", "Tick", {}, {"out"}),
        New Kind("sense", "Goal", {}, {"bearing", "range"}),
        New Kind("sense", "Ray Scan", {}, {"hits", "ahead"},
                 {"reach"}, {40.0F}),
        New Kind("sense", "Rear Scan", {"hits"}, {"deepest", "bearing"}),
        New Kind("sense", "Body Ahead", {}, {"metres"}),
        New Kind("sense", "Speed", {}, {"metres"}),
        New Kind("sense", "Corridor", {"reach"}, {"clear", "metres", "side"},
                 {"length", "margin"}, {30.0F, 0.3F}),
        New Kind("sense", "Doors", {"hits"}, {"doors"}),
        New Kind("sense", "Look Ahead", {},
                 {"hits", "doors", "gain", "confidence"},
                 {"metres"}, {0.0F}),
        New Kind("sense", "Way Out", {},
                 {"bearing", "gain", "confidence"},
                 {"metres", "min gain"}, {0.0F, 6.0F}),
        New Kind("test", "Arrived", {"range"}, {"True"}, {"metres"}, {5.0F}),
        New Kind("test", "Is Wedged", {}, {"True"}),
        New Kind("test", "Not Moving", {}, {"True"}),
        New Kind("test", "Backed Enough", {}, {"True"}, {"seconds"}, {1.2F}),
        New Kind("test", "No Goal", {}, {"True"}),
        New Kind("test", "Too Few Rays", {"hits"}, {"True"}),
        New Kind("test", "Hit Count", {"hits"}, {"True"}, {"count"}, {3.0F}),
        New Kind("test", "Has Door", {"door"}, {"True"}),
        New Kind("test", "Path Clear", {"bearing", "metres"}, {"True"}),
        New Kind("test", "Can Pivot To", {"bearing"}, {"True"},
                 {"margin"}, {0.3F}),
        New Kind("test", "Enough Room", {"metres"}, {"True"}, {"metres"}, {5.0F}),
        New Kind("test", "Plank Hit", {"clear"}, {"True"}),
        New Kind("test", "Rear Better", {"metres"}, {"True"}),
        New Kind("test", "Is Seek", {}, {"True"}),
        New Kind("test", "Is Backing", {}, {"True"}),
        New Kind("test", "Is Turning", {}, {"True"}),
        New Kind("test", "Is Door", {}, {"True"}),
        New Kind("test", "Is Follow", {}, {"True"}),
        New Kind("pick", "Round The End", {"bearing"}, {"bearing", "True"},
                 {"margin"}, {0.6F}),
        New Kind("pick", "Widest Door", {"doors"}, {"door"}),
        New Kind("pick", "Best Door", {"doors", "bearing"}, {"door"}),
        New Kind("pick", "Deepest Ray", {"hits"}, {"bearing"}),
        New Kind("pick", "Confirm", {"door"}, {"door"}),
        New Kind("pick", "Commit", {"door"}, {"door"},
                 {"patience", "gained"}, {0.6F, 1.0F}),
        New Kind("pick", "Door Bearing", {"door"}, {"bearing"}),
        New Kind("act", "Stop", {"in"}, {}),
        New Kind("act", "New Goal", {"in"}, {}),
        New Kind("act", "Scanning", {"in"}, {}),
        New Kind("act", "Mark Trap", {"in"}, {}),
        New Kind("act", "Set Seek", {"in"}, {}),
        New Kind("act", "Set Turning", {"in"}, {}),
        New Kind("act", "Set Follow", {"in"}, {}),
        New Kind("act", "Set Door", {"in"}, {}),
        New Kind("act", "Set Backing", {"in"}, {}),
        New Kind("act", "Reverse", {"in", "bearing"}, {}),
        New Kind("act", "Turn To", {"in", "bearing"}, {}),
        New Kind("act", "Drive Heading", {"in", "bearing", "throttle"}, {}),
        New Kind("act", "Through Door", {"in", "door"}, {}),
        New Kind("act", "Follow Wall", {"in", "side"}, {}),
        New Kind("flow", "Priority", {"in"}, {"a", "b", "c", "d"}),
        New Kind("flow", "Sequence", {"in"}, {"a", "b", "c"}),
        New Kind("flow", "Gate", {"in", "True"}, {"out"})
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
    ''' <summary>
    ''' WHAT A PIN CARRIES, in a sentence.
    '''
    ''' Keyed on the pin NAME rather than on the node, because the names are
    ''' the vocabulary - every node that says `ways` means the same thing by
    ''' it, and writing it once per node would be forty chances to say it
    ''' forty slightly different ways.
    ''' </summary>
    Private Function pin_help(name As String) As String
        Select Case name
            Case "in" : Return "run this when the rule above picks it"
            Case "out" : Return "runs whatever is wired here"
            Case "a", "b", "c", "d" : Return "tried in order - first one that acts wins"
            Case "True" : Return "the condition - run the branch when this is true"
            Case "hits" : Return "every ray from the last scan"
            Case "doors" : Return "every gap found between the rays"
            Case "door" : Return "one gap - where it is, how wide, whether we fit"
            Case "bearing" : Return "an angle off the nose. left is negative"
            Case "margin" : Return "extra clearance in metres, on top of the hull"
            Case "gain" : Return "metres of extra sight a step forward buys. big means a way out"
            Case "confidence" : Return "0..1 - how much the two viewpoints agree. low means look again"
            Case "min gain" : Return "how much extra sight counts as a door. under this, no bearing"
            Case "range" : Return "metres to the goal"
            Case "metres", "metres" : Return "a distance in metres"
            Case "ahead" : Return "clear metres straight in front"
            Case "deepest" : Return "metres to the furthest thing behind"
            Case "reach" : Return "how far to look, in metres"
            Case "clear" : Return "true when nothing is in the corridor"
            Case "side" : Return "which side the wall is on"
            Case "throttle" : Return "0 to 1. leave it unwired to pick its own"
            Case Else : Return pin_type(name)
        End Select
    End Function

    ''' <summary>What a node is for, in a line. Enough to tell two similar ones
    ''' apart without opening the source.</summary>
    Private Function kind_help(name As String) As String
        Select Case name
            Case "Tick" : Return "the start. everything runs from here"
            Case "Goal" : Return "where we are trying to get to"
            Case "Ray Scan" : Return "the radar sweep, once a tick"
            Case "Rear Scan" : Return "the deepest way out behind us"
            Case "Body Ahead" : Return "clear metres for the WHOLE HULL, not a ray"
            Case "Speed" : Return "how fast we are actually going"
            Case "Corridor" : Return "planks a hull wide, straight out. the fit test"
            Case "Doors" : Return "openings between the rays"
            Case "Look Ahead" : Return "both viewpoints merged - more returns, fewer blind spots"
            Case "Way Out" : Return "a bearing that OPENS UP from up the road, or nothing"
            Case "Arrived" : Return "close enough to the goal to call it done"
            Case "No Goal" : Return "nothing to drive toward"
            Case "Is Wedged" : Return "asked to move, went nowhere, for most of a second"
            Case "Not Moving" : Return "asked to move and went nowhere, right now"
            Case "Too Few Rays" : Return "the scan came back with almost nothing"
            Case "Hit Count" : Return "at least `count` rays found something"
            Case "Path Clear" : Return "the hull fits along this bearing for this far"
            Case "Can Pivot To" : Return "there is room to TURN to face this. the corners need the half-diagonal"
            Case "Enough Room" : Return "more than a hull length of room"
            Case "Plank Hit" : Return "something is in the corridor. START SCANNING"
            Case "Rear Better" : Return "more room behind than ahead, and ahead is shut"
            Case "Has Door" : Return "there is a gap to aim at"
            Case "Backed Enough" : Return "been reversing long enough, look again"
            Case "Is Seek" : Return "no plan - looking for one"
            Case "Is Backing" : Return "reversing out of something"
            Case "Is Turning" : Return "swinging onto a new heading"
            Case "Is Door" : Return "lined up on a gap, going through"
            Case "Is Follow" : Return "running along a wall"
            Case "Round The End" : Return "steer past the EDGE of what is in the way - no door needed"
            Case "Widest Door" : Return "the widest opening we fit through, any direction"
            Case "Best Door" : Return "the gap that GAINS most ground toward the goal"
            Case "Deepest Ray" : Return "the bearing of the furthest ray in front"
            Case "Confirm" : Return "only passes a gap seen two ticks running"
            Case "Commit" : Return "holds one gap until we stop gaining ground on it"
            Case "Door Bearing" : Return "the angle to a gap"
            Case "Stop" : Return "throttle and steering to nothing"
            Case "New Goal" : Return "throw the next goal, somewhere standable"
            Case "Scanning" : Return "raise the scan. claims nothing, so the chain goes on"
            Case "Mark Trap" : Return "remember this block as one not to drive into"
            Case "Set Seek", "Set Turning", "Set Follow", "Set Door", "Set Backing"
                Return "change what state we are in"
            Case "Reverse" : Return "back up, steering off the TAIL"
            Case "Turn To" : Return "swing to a bearing. no throttle"
            Case "Drive Heading" : Return "go. eases off so the turn fits the room"
            Case "Through Door" : Return "drive through a gap"
            Case "Follow Wall" : Return "run along the wall on one side"
            Case "Priority" : Return "try a, b, c, d in order. stop at the first that acts"
            Case "Sequence" : Return "run all of them"
            Case "Gate" : Return "pass the run on only if `when` is true"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>
    ''' THE HELP PANEL, pinned to the top right of the board.
    '''
    ''' "Can I have hints of what in and outs do when I select a node?"
    '''
    ''' Inside the canvas rather than in a window of its own, so it cannot end
    ''' up behind anything or off on another monitor - it is part of the board,
    ''' at the corner of the board.
    '''
    ''' NO CLOSE BOX OF ITS OWN. It had one, with a small dot in the same corner
    ''' to undo it, and that is the awkward pair: the only way back was the
    ''' place it had just vanished from, over a board that is covered in other
    ''' things to click. The palette checkbox switches it, from somewhere that
    ''' is always on screen and never moves.
    ''' </summary>
    Private Sub help_panel()
        Dim wp = ImGui.GetWindowPos()
        Dim ws = ImGui.GetWindowSize()
        Const W As Single = 330.0F
        Dim x = wp.X + ws.X - W - 10.0F

        ' Switched from the palette, and only from there. See below.
        If Not HELP_SHOW Then Return

        Dim n = find(selected)
        ' ---- SETTINGS, above the pin help ------------------------------
        '
        ' Its own panel rather than a section inside the other one: what a pin
        ' carries is reference, and these are controls. Only drawn for a node
        ' that HAS something to tune, so an empty box never appears to say
        ' there is nothing to say.
        If n IsNot Nothing AndAlso n.kind.setNames.Length > 0 Then
            Dim sh = 30.0F + n.kind.setNames.Length * 24.0F
            ImGui.SetCursorScreenPos(New System.Numerics.Vector2(x, wp.Y + 10.0F))
            ImGui.BeginChild("##nodesets", New System.Numerics.Vector2(W, sh),
                             CType(1, ImGuiChildFlags))
            ImGui.TextDisabled("SETTINGS")
            For Each sn In n.kind.setNames
                Dim v = Setting(n.id, sn, 0.0F)
                Dim was = v
                ImGui.SetNextItemWidth(W - 120.0F)
                If ImGui.DragFloat(sn, v, 0.1F, 0.0F, 400.0F, "%.2f") Then
                    ' BACK TO THE DEFAULT MEANS NO OVERRIDE. Storing a value
                    ' equal to the default would pin it there for good - a
                    ' later change to the default would never reach this board
                    ' and nobody would know why.
                    If Math.Abs(v - n.kind.DefaultFor(sn)) < 0.0001F Then
                        n.sets.Remove(sn)
                    Else
                        n.sets(sn) = v
                    End If
                    If Math.Abs(v - was) > 0.0F Then Changed = True
                End If
            Next
            ImGui.EndChild()
            ImGui.SetCursorScreenPos(New System.Numerics.Vector2(x, wp.Y + 16.0F + sh))
        Else
            ImGui.SetCursorScreenPos(New System.Numerics.Vector2(x, wp.Y + 10.0F))
        End If

        ' Two lines a pin now the description wraps under the name, plus the
        ' node's own line and the headings.
        Dim rows = 5
        If n IsNot Nothing Then rows += (n.kind.ins.Length + n.kind.outs.Length) * 2 + 3
        Dim h = Math.Min(420.0F, 34.0F + rows * 16.0F)

        ImGui.BeginChild("##nodehelp", New System.Numerics.Vector2(W, h),
                         CType(1, ImGuiChildFlags))

        If n Is Nothing Then
            ImGui.TextDisabled("Click a node")
            ImGui.Spacing()
            ImGui.TextWrapped("Drag an output to an input to wire them. " &
                              "RIGHT-CLICK a pin to cut its wires.")
        Else
            ImGui.TextColored(New System.Numerics.Vector4(1.0F, 0.78F, 0.33F, 1.0F),
                              n.kind.name)
            Dim kh = kind_help(n.kind.name)
            If kh <> "" Then ImGui.TextWrapped(kh)
            ImGui.Separator()

            ' THE DESCRIPTION WRAPS UNDER THE NAME, not beside it. Beside it,
            ' anything longer than the space left ran off the right edge and the
            ' end was gone - and the end of a description is the part that
            ' explains it. Under it, no panel width is ever wrong.
            If n.kind.ins.Length > 0 Then
                ImGui.TextDisabled("IN")
                For Each p In n.kind.ins
                    ImGui.TextColored(New System.Numerics.Vector4(0.5F, 0.78F, 1.0F, 1.0F), p)
                    ImGui.Indent(10.0F)
                    ImGui.TextWrapped(pin_help(p))
                    ImGui.Unindent(10.0F)
                Next
            End If
            If n.kind.outs.Length > 0 Then
                ImGui.TextDisabled("OUT")
                For Each p In n.kind.outs
                    ImGui.TextColored(New System.Numerics.Vector4(0.25F, 0.88F, 0.25F, 1.0F), p)
                    ImGui.Indent(10.0F)
                    ImGui.TextWrapped(pin_help(p))
                    ImGui.Unindent(10.0F)
                Next
            End If
        End If

        ImGui.EndChild()
    End Sub

    Private Function pin_type(name As String) As String
        Select Case name
            Case "hits" : Return "hits"
            Case "doors" : Return "doors"
            Case "door" : Return "door"
            Case "bearing" : Return "angle"
            Case "metres", "metres", "reach", "range", "ahead", "deepest" : Return "length"
            Case "clear", "True" : Return "bool"
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

        ''' <summary>ONLY what has been changed from the kind's default. A node
        ''' that has never been touched carries nothing, so a default altered
        ''' in code reaches every board that never overrode it - which is what
        ''' makes it a default rather than a starting value.</summary>
        Public ReadOnly sets As New Dictionary(Of String, Single)
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
    ''' <summary>The confirm popup's title AND its id - ImGui uses the one
    ''' string for both, so it is spelt once here rather than twice at the
    ''' call sites, where a typo would silently open nothing.</summary>
    Private Const LOAD_ASK As String = "Load a graph"
    Private Const SIMPLE_ASK As String = "Build the simple test?"

    ''' <summary>What Save writes to, and what the box under the buttons
    ''' edits. Shown rather than assumed - see the note there.</summary>
    Private graphName As String = "brain"

    Private Const CLEAR_ASK As String = "Clear the graph?"
    Private Const MODEL_ASK As String = "Build the current AI?"

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

    ''' <summary>A kind by name, or Nothing. Used by the model builder, which
    ''' names what it wants rather than indexing into KINDS - an index would go
    ''' silently wrong the first time a kind was inserted above it.</summary>
    ' ======================================================================
    '  READING THE BOARD FROM OUTSIDE
    ' ======================================================================
    '
    ' GraphBrain walks the graph every tick and must not be handed the lists
    ' themselves - a brain that can Add or Remove while the editor is drawing
    ' is a crash waiting for the frame they disagree. These five say what is
    ' wired to what and nothing else.
    '
    ' Ids and PIN NAMES, never indices, for the same reason the save format
    ' uses names: an index is only true until a kind gains a pin.

    ''' <summary>
    ''' A node's setting: its own override, or the kind's default.
    '''
    ''' The fallback argument is for a caller that wants its own value when the
    ''' kind declares none - so a node can gain a setting later without every
    ''' reader having to learn about it first.
    ''' </summary>
    Public Function Setting(id As Integer, name As String, fallback As Single) As Single
        Dim n = find(id)
        If n Is Nothing Then Return fallback
        Dim v As Single
        If n.sets.TryGetValue(name, v) Then Return v
        If Array.IndexOf(n.kind.setNames, name) >= 0 Then Return n.kind.DefaultFor(name)
        Return fallback
    End Function

    ''' <summary>The kind of a node, or "" if there is no such node.</summary>
    Public Function NodeKind(id As Integer) As String
        Dim n = find(id)
        If n Is Nothing Then Return ""
        Return n.kind.name
    End Function

    ''' <summary>The first node of a kind, or -1. Used to find the Tick node -
    ''' a board with two of them runs the first, which is at least
    ''' predictable.</summary>
    Public Function FirstOfKind(kindName As String) As Integer
        For Each n In nodes
            If n.kind.name = kindName Then Return n.id
        Next
        Return -1
    End Function

    ''' <summary>What this node's named OUTPUT feeds, or -1.</summary>
    Public Function Downstream(id As Integer, outName As String) As Integer
        Dim n = find(id)
        If n Is Nothing Then Return -1
        Dim i = Array.IndexOf(n.kind.outs, outName)
        If i < 0 Then Return -1
        For Each l In links
            If l.fromNode = id AndAlso l.fromPin = i Then Return l.toNode
        Next
        Return -1
    End Function

    ''' <summary>What feeds this node's named INPUT, or -1.</summary>
    Public Function UpstreamNode(id As Integer, inName As String) As Integer
        Dim n = find(id)
        If n Is Nothing Then Return -1
        Dim i = Array.IndexOf(n.kind.ins, inName)
        If i < 0 Then Return -1
        For Each l In links
            If l.toNode = id AndAlso l.toPin = i Then Return l.fromNode
        Next
        Return -1
    End Function

    ''' <summary>Which output pin feeds this node's named input, or "".</summary>
    Public Function UpstreamPin(id As Integer, inName As String) As String
        Dim n = find(id)
        If n Is Nothing Then Return ""
        Dim i = Array.IndexOf(n.kind.ins, inName)
        If i < 0 Then Return ""
        For Each l In links
            If l.toNode = id AndAlso l.toPin = i Then
                Dim src = find(l.fromNode)
                If src Is Nothing Then Return ""
                If l.fromPin < 0 OrElse l.fromPin >= src.kind.outs.Length Then Return ""
                Return src.kind.outs(l.fromPin)
            End If
        Next
        Return ""
    End Function

    ' ======================================================================
    '  WHAT THE BOARD DID THIS TICK
    ' ======================================================================
    '
    ' "it was moving and stopped. we need logs at the nodes" - the owner.
    '
    ' Debugging this from the heartbeat meant reading one sentence and
    ' reasoning backwards through fifty-nine nodes to work out which rule
    ' produced it. Every wrong guess this evening came from that. The board
    ' knows which nodes it walked, so it should say.
    '
    ' THREE THINGS, because they answer different questions: which nodes were
    ' VISITED (what the walk considered), what each test ANSWERED (why it
    ' turned where it did), and which act WON (what actually happened).

    ''' <summary>Nodes the walk reached this tick.</summary>
    Public ReadOnly Fired As New HashSet(Of Integer)

    ''' <summary>What each test node answered this tick. A test that was never
    ''' asked is absent, which is different from one that answered False - and
    ''' on a Priority chain that difference is the whole story.</summary>
    Public ReadOnly Tested As New Dictionary(Of Integer, Boolean)

    ''' <summary>The act that ended the tick, or -1.</summary>
    ''' <summary>What each node produced this tick, as text. Tests answer
    ''' yes or no and that is in Tested; this is the numbers - metres,
    ''' degrees, how many ways - so a step can be READ rather than
    ''' inferred from what happened next.</summary>
    Public ReadOnly Vals As New Dictionary(Of Integer, String)

    ''' <summary>
    ''' Wires the walk went down this tick, packed as from:pin>to:pin.
    '''
    ''' A Long rather than a string because this is rebuilt sixty times a
    ''' second over a hundred wires, and a hundred short-lived strings a frame
    ''' is garbage for nothing.
    ''' </summary>
    Public ReadOnly LitWires As New HashSet(Of Long)

    Private Function wire_key(fromId As Integer, fromPin As Integer,
                                     toId As Integer, toPin As Integer) As Long
        Return (CLng(fromId) << 40) Or (CLng(fromPin) << 32) Or
               (CLng(toId) << 8) Or CLng(toPin)
    End Function

    ''' <summary>Mark the flow wire out of this output as taken. Called only
    ''' when the branch ACTED, so what lights up is the chain that won rather
    ''' than every branch that was tried.</summary>
    Public Sub LitFlow(fromId As Integer, outName As String, toId As Integer)
        Dim a = find(fromId)
        If a Is Nothing Then Return
        Dim i = Array.IndexOf(a.kind.outs, outName)
        If i < 0 Then Return
        For Each l In links
            If l.fromNode = fromId AndAlso l.fromPin = i AndAlso l.toNode = toId Then
                LitWires.Add(wire_key(l.fromNode, l.fromPin, l.toNode, l.toPin))
                Return
            End If
        Next
    End Sub

    ''' <summary>Mark the data wire feeding this input as read.</summary>
    Public Sub LitData(toId As Integer, inName As String)
        Dim b = find(toId)
        If b Is Nothing Then Return
        Dim j = Array.IndexOf(b.kind.ins, inName)
        If j < 0 Then Return
        For Each l In links
            If l.toNode = toId AndAlso l.toPin = j Then
                LitWires.Add(wire_key(l.fromNode, l.fromPin, l.toNode, l.toPin))
                Return
            End If
        Next
    End Sub

    Public ActedNode As Integer = -1

    ''' <summary>
    ''' HOW OFTEN EACH ACT WON THE TICK, and how often the answer changed.
    '''
    ''' The score says how well a run went. This says what it spent its
    ''' time doing, which is the half you can act on: a run that reversed
    ''' for sixty per cent of its life and one that drove smoothly into a
    ''' wall can score the same and need opposite fixes.
    ''' </summary>
    Public ReadOnly ActCount As New Dictionary(Of Integer, Integer)
    Public Flips As Integer = 0
    Private lastActed As Integer = -1

    Public Sub ResetCounts()
        ActCount.Clear()
        Flips = 0
        lastActed = -1
    End Sub

    ''' <summary>The top acts, as "name pct" pairs.</summary>
    Public Function TopActs(howMany As Integer) As String()
        Dim total = 0
        For Each kv In ActCount
            total += kv.Value
        Next
        Dim outp As New List(Of String)
        Dim used As New List(Of Integer)
        For n = 1 To howMany
            Dim bestId = -1, bestN = 0
            For Each kv In ActCount
                If used.Contains(kv.Key) Then Continue For
                If kv.Value > bestN Then
                    bestN = kv.Value
                    bestId = kv.Key
                End If
            Next
            If bestId < 0 Then Exit For
            used.Add(bestId)
            outp.Add(NodeKind(bestId))
            outp.Add(CInt(100.0F * bestN / Math.Max(1, total)).ToString())
        Next
        While outp.Count < howMany * 2
            outp.Add("-")
            outp.Add("0")
        End While
        Return outp.ToArray()
    End Function

    Private lastPath As String = ""

    Public Sub TraceBegin()
        Fired.Clear()
        Tested.Clear()
        Vals.Clear()
        LitWires.Clear()
        ActedNode = -1
    End Sub

    Public Sub TraceVisit(id As Integer)
        Fired.Add(id)
    End Sub

    Public Sub TraceTest(id As Integer, answer As Boolean)
        Tested(id) = answer
    End Sub

    Public Sub TraceValue(id As Integer, text As String)
        Vals(id) = text
    End Sub

    Public Sub TraceAct(id As Integer)
        ActedNode = id
        ' Counted HERE rather than at the caller, so every path that acts
        ' is counted and a new one cannot forget to.
        If id <> lastActed Then
            Flips += 1
            lastActed = id
        End If
        Dim had = 0
        ActCount.TryGetValue(id, had)
        ActCount(id) = had + 1
    End Sub

    ''' <summary>The walk as one line: the tests that were asked, what they
    ''' said, and the act that won.</summary>
    Public Function TracePath() As String
        Dim b As New Text.StringBuilder()
        For Each n In nodes
            Dim ans As Boolean
            If Tested.TryGetValue(n.id, ans) Then
                If b.Length > 0 Then b.Append(" ")
                b.Append(n.kind.name)
                b.Append(If(ans, "=Y", "=n"))
            End If
        Next
        Dim a = find(ActedNode)
        b.Append(" -> ")
        b.Append(If(a Is Nothing, "nothing", a.kind.name + "#" + a.id.ToString()))
        Return b.ToString()
    End Function

    ''' <summary>
    ''' Say the path, but ONLY when it changes.
    '''
    ''' At sixty frames a second an unconditional line is four thousand a
    ''' minute and the window is useless. What is worth seeing is the MOMENT
    ''' the board changes its mind - which is exactly when a tank that was
    ''' moving stops.
    ''' </summary>
    Public Sub TraceLogIfChanged()
        Dim p = TracePath()
        If p = lastPath Then Return
        lastPath = p
        LogThis("brain: board {0}", p)
    End Sub

    Private Function kind_of(name As String) As Kind
        For Each k In KINDS
            If k.name = name Then Return k
        Next
        Return Nothing
    End Function

    ''' <summary>Place one node and hand back its id.</summary>
    Private Function spawn(kindName As String, x As Single, y As Single) As Integer
        Dim k = kind_of(kindName)
        If k Is Nothing Then
            LogThis("brain: node kind '{0}' does not exist", kindName)
            Return -1
        End If
        Dim n As New Node()
        n.id = nextId
        nextId += 1
        n.kind = k
        n.pos = New System.Numerics.Vector2(x, y)
        nodes.Add(n)
        Return n.id
    End Function

    ''' <summary>
    ''' Give a spawned node one of its settings.
    '''
    ''' The builder had no way to say this, so every node on the board ran
    ''' at its kind's default - and two rungs that read the SAME quantity
    ''' were therefore stuck with the SAME threshold. Enough Room let go of
    ''' the Backing state at exactly the distance Rear Better grabbed it at,
    ''' which is not a rule, it is a coin landing on its edge.
    ''' </summary>
    Private Sub set_setting(id As Integer, name As String, v As Single)
        For Each n In nodes
            If n.id = id Then
                n.sets(name) = v
                Return
            End If
        Next
    End Sub

    ''' <summary>Join two pins BY NAME. Names, because the builder below makes
    ''' sixty of these and an off-by-one in a pin index is invisible - it wires
    ''' something, just not the thing that was meant.</summary>
    Private Sub join_pins(fromId As Integer, outName As String,
                     toId As Integer, inName As String)
        Dim a = find(fromId), b = find(toId)
        If a Is Nothing OrElse b Is Nothing Then Return
        Dim i = Array.IndexOf(a.kind.outs, outName)
        Dim j = Array.IndexOf(b.kind.ins, inName)
        If i < 0 OrElse j < 0 Then
            LogThis("brain: no pin {0}.{1} -> {2}.{3}",
                    a.kind.name, outName, b.kind.name, inName)
            Return
        End If

        ' THE SAME GATE THE MOUSE GOES THROUGH. Without this the builder could
        ' make a graph the editor itself would refuse to draw - and it did:
        ' rear `deepest`, a distance, wired into a `bearing`. Thirty-eight
        ' metres read as a heading is 2193 degrees, and the tank reversed
        ' toward it. A builder that can express what the UI forbids is a second
        ' set of rules nobody is checking.
        If Not can_join(fromId, i, True, toId, j, True) Then
            LogThis("brain: {0}.{1} does not fit {2}.{3} - {4} into {5}",
                    a.kind.name, outName, b.kind.name, inName,
                    pin_type(outName), pin_type(inName))
            Return
        End If
        links.Add(New Link With {.fromNode = fromId, .fromPin = i,
                                 .toNode = toId, .toPin = j})
    End Sub

    ''' <summary>
    ''' LAY OUT THE BRAIN THAT IS ACTUALLY RUNNING, as a graph.
    '''
    ''' Read off RangeBrain.Tick, which is one ordered chain where the first
    ''' rule to match returns. That is why the spine is four Priority nodes with
    ''' each one's LAST out feeding the next: d is not a fourth choice, it is
    ''' "none of the above", and a chain of them is what an else-if ladder looks
    ''' like once it is drawn.
    '''
    ''' WHAT THE PICTURE SHOWS THAT THE CODE HIDES. Rules 10 to 16 are that
    ''' ladder, not a weighing-up: the vote at rule 11 never competes with the
    ''' door, the wall or the rear, it just happens to be asked first. And the
    ''' Best Progress node sits on the board unwired, because the brain ranks
    ''' fitting ways by WIDTH - the progress score exists in the code and cannot
    ''' be reached. Drawn, that is one dangling node; in the source it is a
    ''' hundred lines apart and nobody has spotted it in a month.
    ''' </summary>
    Public Sub BuildCurrentAI()
        nodes.Clear()
        links.Clear()
        nextId = 1
        selected = -1
        scroll = New System.Numerics.Vector2(0.0F, 0.0F)

        ' ---- what it can sense, down the left -------------------------------
        Dim tick = spawn("Tick", 0.0F, 0.0F)
        Dim goal = spawn("Goal", 0.0F, 110.0F)
        Dim scan = spawn("Ray Scan", 0.0F, 240.0F)
        Dim rear = spawn("Rear Scan", 0.0F, 370.0F)
        Dim body = spawn("Body Ahead", 0.0F, 510.0F)
        Dim spd = spawn("Speed", 0.0F, 620.0F)
        Dim corr = spawn("Corridor", 0.0F, 730.0F)
        Dim gaps = spawn("Doors", 0.0F, 870.0F)

        ' ---- what it makes of that ------------------------------------------
        Dim tArr = spawn("Arrived", 210.0F, 110.0F)
        Dim tWed = spawn("Is Wedged", 210.0F, 220.0F)
        Dim tRay = spawn("Too Few Rays", 210.0F, 330.0F)
        Dim tBack = spawn("Is Backing", 210.0F, 440.0F)
        Dim tTurn = spawn("Is Turning", 210.0F, 550.0F)
        Dim tDoor = spawn("Is Door", 210.0F, 660.0F)
        Dim tFoll = spawn("Is Follow", 210.0F, 770.0F)
        Dim tPlank = spawn("Plank Hit", 210.0F, 880.0F)
        Dim tFit = spawn("Path Clear", 210.0F, 990.0F)
        Dim tRearB = spawn("Rear Better", 210.0F, 1120.0F)

        Dim pWide = spawn("Widest Door", 420.0F, 870.0F)
        Dim pVote = spawn("Confirm", 420.0F, 980.0F)
        Dim pDeep = spawn("Deepest Ray", 420.0F, 1200.0F)
        Dim pProg = spawn("Best Door", 420.0F, 1320.0F)

        join_pins(goal, "range", tArr, "range")
        join_pins(scan, "hits", rear, "hits")
        join_pins(scan, "hits", tRay, "hits")
        join_pins(scan, "hits", gaps, "hits")
        join_pins(scan, "hits", pDeep, "hits")
        join_pins(gaps, "doors", pWide, "doors")
        join_pins(gaps, "doors", pWide, "doors")
        ' VOTE SITS BETWEEN THE PICK AND THE COMMIT. On Widest it fed
        ' nothing; here it is the damper that stops the board changing its
        ' mind every tick between going through a gap and reversing away
        ' from it.
        ' BEST PROGRESS, not widest. Reach times the cosine of how far the
        ' opening is off the goal bearing: metres actually gained toward
        ' where we are going. Widest picks the biggest hole even when it
        ' leads away, which is how a tank ends up touring the map.
        join_pins(gaps, "doors", pProg, "doors")
        join_pins(gaps, "doors", pWide, "doors")
        join_pins(goal, "bearing", pProg, "bearing")
        join_pins(pProg, "door", pVote, "door")
        ' COMMIT HOLDS THE CHOICE. Vote says the way was there two ticks
        ' running; Commit says we are going to it and keeps saying so until
        ' the tank stops gaining ground. Everything downstream reads the
        ' committed way, so a choice cannot be re-made every frame.
        Dim pHold = spawn("Commit", 640.0F, 1400.0F)
        join_pins(pVote, "door", pHold, "door")
        join_pins(goal, "bearing", tFit, "bearing")
        join_pins(body, "metres", tFit, "metres")
        join_pins(body, "metres", tRearB, "metres")
        join_pins(corr, "clear", tPlank, "clear")
        ' Speed feeds the wedge watch in the code; Is Wedged takes no input
        ' here because the watch is a timer over several frames, and a wire
        ' would claim it was a reading.

        ' ---- the spine: the else-if ladder, drawn -----------------------------
        Dim p1 = spawn("Priority", 650.0F, 60.0F)
        Dim p2 = spawn("Priority", 650.0F, 420.0F)
        Dim p3 = spawn("Priority", 650.0F, 780.0F)
        Dim p4 = spawn("Priority", 650.0F, 1140.0F)
        join_pins(tick, "out", p1, "in")
        ' ---- STUCK BEATS EVERYTHING BUT THE GUARDS --------------------
        '
        ' A tank that cannot move has one useful move, and every state's own
        ' answer was to push harder in the direction that was already refused.
        ' Rotation is never refused, so it goes here - above the states, below
        ' the goal and the wedge.
        Dim pStuck2 = spawn("Priority", 650.0F, 250.0F)
        Dim tStuck2 = spawn("Not Moving", 430.0F, 190.0F)
        Dim gStuck2 = spawn("Gate", 870.0F, 250.0F)
        Dim aSpin2 = spawn("Turn To", 1090.0F, 250.0F)
        ' ---- GO, UNLESS SOMETHING STOPS YOU -----------------------------
        '
        ' "think like what a person would do. things you do first and in what
        '  order" - and the first thing a person does is point at where they
        ' are going and drive. Everything else is an exception to that.
        '
        ' This board was built the other way up. Heading for the goal was
        ' p5.d - the last pin of a five deep chain, below scanning, backing,
        ' turning, doors, wall following, planks and going round. The
        ' OBJECTIVE was the lowest priority thing on the board, so any
        ' marginal reason to reverse outranked actually going somewhere.
        '
        ' Placed after the three terminals - no goal, arrived, wedged -
        ' because those are the cases where driving at the goal is wrong or
        ' impossible. Everywhere else: if the way is plausibly open, take it,
        ' and let the rungs below deal with it next tick if it is not. Being
        ' optimistic and correcting is what a person does; checking every
        ' danger before moving is what this board did.
        Dim pGo = spawn("Priority", 650.0F, 200.0F)
        Dim tGo = spawn("Path Clear", 420.0F, 200.0F)
        Dim gGo = spawn("Gate", 870.0F, 200.0F)
        Dim aStraight = spawn("Drive Heading", 1090.0F, 200.0F)
        join_pins(goal, "bearing", tGo, "bearing")
        join_pins(tGo, "True", gGo, "True")
        join_pins(pGo, "c", gGo, "in")
        join_pins(gGo, "out", aStraight, "in")
        join_pins(goal, "bearing", aStraight, "bearing")

        ' ---- AND IF IT IS NOT OPEN, LOOK LEFT AND RIGHT. NOW. ------------
        '
        ' "you are not looking for the way around" - and the board was not,
        ' because this rung lived at p5.a. To reach it a tick had to survive
        ' scanning, backing, turning, doors, wall following and planks first.
        ' A person who is blocked looks left and right immediately; they do
        ' not work through a checklist while sitting against a tree.
        '
        ' The search itself was always right. Measured at the owner's hard
        ' start: nineteen bearings tried, all pivotable, six with an open
        ' plank, and it picks forty degrees left - into a ninety-three degree
        ' arc with nothing in it for forty metres. It had that answer every
        ' tick of every run tonight and was never asked for it.
        Dim pAround = spawn("Round The End", 420.0F, 300.0F)
        Dim gAround = spawn("Gate", 870.0F, 300.0F)
        Dim aAround = spawn("Drive Heading", 1090.0F, 300.0F)
        join_pins(goal, "bearing", pAround, "bearing")
        join_pins(pAround, "True", gAround, "True")
        ' FIRST, not second. The walk already includes the goal bearing among
        ' its candidates, so this one rung answers both "is the way home open"
        ' and "where do I go round" - and there is no second rung left to
        ' argue with it. The old straight-at-the-goal rung stays on pin c as a
        ' backstop for the tick where the walk finds nothing at all.
        join_pins(pGo, "a", gAround, "in")
        join_pins(gAround, "out", aAround, "in")
        join_pins(pAround, "bearing", aAround, "bearing")

        join_pins(p1, "d", pGo, "in")
        join_pins(pGo, "d", pStuck2, "in")
        join_pins(pStuck2, "a", gStuck2, "in")
        join_pins(tStuck2, "True", gStuck2, "True")
        join_pins(gStuck2, "out", aSpin2, "in")
        ' Toward the most open direction there is - the deepest ray - because
        ' when nothing fits, "furthest from anything" is the only honest
        ' heading available.
        join_pins(pDeep, "bearing", aSpin2, "bearing")
        join_pins(pStuck2, "d", p2, "in")
        join_pins(p2, "d", p3, "in")
        join_pins(p3, "d", p4, "in")

        ' ---- rule 1-3: the guards -------------------------------------------
        Dim gNoGoal = spawn("Gate", 870.0F, 40.0F)
        Dim tNoGoal = spawn("No Goal", 650.0F, -60.0F)
        Dim aStop = spawn("Stop", 1090.0F, 40.0F)
        join_pins(p1, "a", gNoGoal, "in")
        join_pins(tNoGoal, "True", gNoGoal, "True")
        join_pins(gNoGoal, "out", aStop, "in")

        Dim gArr = spawn("Gate", 870.0F, 150.0F)
        Dim aGoal = spawn("New Goal", 1090.0F, 150.0F)
        join_pins(p1, "b", gArr, "in")
        join_pins(tArr, "True", gArr, "True")
        join_pins(gArr, "out", aGoal, "in")

        Dim gWed = spawn("Gate", 870.0F, 260.0F)
        Dim sq = spawn("Sequence", 1090.0F, 260.0F)
        Dim aMark = spawn("Mark Trap", 1310.0F, 260.0F)
        Dim aSet = spawn("Set Backing", 1310.0F, 370.0F)
        join_pins(p1, "c", gWed, "in")
        join_pins(tWed, "True", gWed, "True")
        join_pins(gWed, "out", sq, "in")
        join_pins(sq, "a", aMark, "in")
        join_pins(sq, "b", aSet, "in")

        ' ---- rule 4 and the states ------------------------------------------
        Dim gScan = spawn("Gate", 870.0F, 480.0F)
        Dim aRescan = spawn("Scanning", 1090.0F, 480.0F)
        join_pins(p2, "a", gScan, "in")
        join_pins(tRay, "True", gScan, "True")
        join_pins(gScan, "out", aRescan, "in")

        ' BACKING, AND THE WAY OUT OF IT. The first cut of this board could
        ' enter Backing and never leave - the only state it ever set was
        ' Backing, so Is Turning, Is Door and Is Follow were dead branches and
        ' the tank reversed until something else stopped it.
        '
        ' The exit is drawn, not coded: a Priority under the gate that tries
        ' "the way ahead will clear now, go back to seeking" before it tries
        ' "keep reversing". That is what a state machine looks like on a board.
        Dim gBack = spawn("Gate", 870.0F, 590.0F)
        Dim pBack = spawn("Priority", 1090.0F, 560.0F)
        Dim gBackOut = spawn("Gate", 1310.0F, 530.0F)
        ' IS CLEAR of the room AHEAD, not Will Clear from where we stand. The
        ' rule that put us in Backing marked this square a trap, so anything
        ' that traces out of it is false by construction - the state had no
        ' exit at all and the tank reversed until something else stopped it.
        Dim tRoom = spawn("Enough Room", 1310.0F, 410.0F)
        Dim aSeek = spawn("Set Seek", 1530.0F, 530.0F)
        Dim aRev = spawn("Reverse", 1310.0F, 650.0F)
        join_pins(p2, "b", gBack, "in")
        join_pins(tBack, "True", gBack, "True")
        join_pins(gBack, "out", pBack, "in")
        join_pins(pBack, "a", gBackOut, "in")
        ' A DEAD BAND, and it is the whole fix. Rear Better grabs the Backing
        ' state when Body Ahead drops under five metres; this rung hands it
        ' back when Body Ahead climbs over five metres. Same reading, same
        ' number, so the hull sat on five and alternated - grab, release,
        ' grab - 24,265 board decisions in one run and 100% of them a change
        ' of mind.
        '
        ' Nine metres to let go: a hull length clear of the five that took it,
        ' so backing out has to actually achieve something before it counts as
        ' finished. Backed Enough still caps it in time, so this cannot back
        ' across the map looking for nine metres that are not there.
        set_setting(tRoom, "metres", 9.0F)
        join_pins(body, "metres", tRoom, "metres")
        ' BACK UNTIL THERE IS ROOM TO TURN, not until there is room AHEAD.
        '
        ' Enough Room asks whether the way forward has opened. Backing out
        ' of a pocket does not open the way forward - it opens the way
        ' ROUND, and the hull cannot take it without first swinging the
        ' nose. So the old exit was waiting for something reversing does not
        ' produce, and Backed Enough let go after 1.2 s instead: about a
        ' metre of creep, nothing changed, same reading, back again.
        ' Measured: forward and back at a hundred decisions a second.
        '
        ' Can Pivot To answers the question that actually ends a reversal -
        ' can the box now swing to face the deepest open direction. It has
        ' been on the board since it was written and wired to nothing.
        Dim tPivot = spawn("Can Pivot To", 1090.0F, 330.0F)
        join_pins(pDeep, "bearing", tPivot, "bearing")
        join_pins(tPivot, "True", gBackOut, "True")
        join_pins(gBackOut, "out", aSeek, "in")
        ' SPIN BEFORE REVERSING. The sim turns the hull before it tests where
        ' the hull wants to go, so rotation is the one command that is never
        ' refused - and a hull pushing into geometry at zero speed has nothing
        ' else that works. Reversing straight back is about zero steer, so it
        ' pushed and pushed and never came free.
        Dim tStuck = spawn("Not Moving", 1090.0F, 400.0F)
        Dim gStuck = spawn("Gate", 1310.0F, 620.0F)
        Dim aSpin = spawn("Turn To", 1530.0F, 620.0F)
        ' AND A WAY OUT ON THE CLOCK. The clearance exit above can be waited
        ' on forever in a pocket - this one cannot. Whatever else is true,
        ' after long enough backing we stop backing and look again.
        ' A BACKSTOP, NOT THE EXIT.
        '
        ' Backing has two ways out: Can Pivot To, which is the real one -
        ' back up until the box can swing - and this, a timer. At 1.2 s the
        ' timer always won: about a metre of creep, still nothing to turn
        ' into, so the same reading put it straight back into Backing.
        ' Measured with the way-around rung finally firing: Can Pivot To=n,
        ' Backed Enough=Y, Set Seek - then forward and back at frame rate,
        ' 2675 Reverse against 2460 Drive Heading, net movement zero.
        '
        ' Eight seconds, so it only fires when backing genuinely is not
        ' working and something else should be tried. The condition that
        ' ENDS a reversal should be the one the reversal exists to create.
        Dim tBacked = spawn("Backed Enough", 1090.0F, 470.0F)
        set_setting(tBacked, "seconds", 8.0F)
        Dim gBacked = spawn("Gate", 1310.0F, 470.0F)
        Dim aSeek2 = spawn("Set Seek", 1530.0F, 470.0F)
        join_pins(pBack, "b", gBacked, "in")
        join_pins(tBacked, "True", gBacked, "True")
        join_pins(gBacked, "out", aSeek2, "in")

        join_pins(pBack, "c", gStuck, "in")
        join_pins(tStuck, "True", gStuck, "True")
        join_pins(gStuck, "out", aSpin, "in")
        join_pins(pDeep, "bearing", aSpin, "bearing")
        join_pins(pBack, "d", aRev, "in")
        join_pins(rear, "bearing", aRev, "bearing")

        Dim gTurn = spawn("Gate", 870.0F, 710.0F)
        Dim aTurn = spawn("Turn To", 1090.0F, 710.0F)
        join_pins(p2, "c", gTurn, "in")
        join_pins(tTurn, "True", gTurn, "True")
        join_pins(gTurn, "out", aTurn, "in")

        Dim gDoor = spawn("Gate", 870.0F, 840.0F)
        Dim aDoor = spawn("Through Door", 1090.0F, 840.0F)
        join_pins(p3, "a", gDoor, "in")
        join_pins(tDoor, "True", gDoor, "True")
        join_pins(gDoor, "out", aDoor, "in")

        Dim gFoll = spawn("Gate", 870.0F, 960.0F)
        Dim aWall = spawn("Follow Wall", 1090.0F, 960.0F)
        join_pins(p3, "b", gFoll, "in")
        join_pins(tFoll, "True", gFoll, "True")
        join_pins(gFoll, "out", aWall, "in")
        join_pins(corr, "side", aWall, "side")

        ' ---- the fresh decision ----------------------------------------------
        ' ---- RULE ONE: A PLANK TOUCH STOPS US AND STARTS THE SCAN --------
        '
        ' "plank is the trigger to start scanning[;] it needs stop if and until
        '  after we have a clear step forward that clears the sides."
        '
        ' So: hold still, raise the scan, and swing toward the widest way the
        ' hull actually FITS through - all at zero throttle. It keeps doing
        ' that until the corridor comes clear, and the corridor IS "a step
        ' forward that clears the sides": planks a hull wide plus one proud of
        ' each fender. When they clear, this rule declines and the chain falls
        ' through to driving.
        Dim gPlank = spawn("Gate", 870.0F, 1080.0F)
        Dim sqPlank = spawn("Priority", 1090.0F, 1080.0F)
        Dim aScan = spawn("Scanning", 1310.0F, 1020.0F)
        Dim pTurn = spawn("Priority", 1310.0F, 1120.0F)
        Dim gWay = spawn("Gate", 1530.0F, 1120.0F)
        Dim tWay = spawn("Has Door", 1090.0F, 1260.0F)
        Dim pWayB = spawn("Door Bearing", 1310.0F, 1300.0F)
        Dim aGo = spawn("Drive Heading", 1750.0F, 1120.0F)
        join_pins(p3, "c", gPlank, "in")
        join_pins(tPlank, "True", gPlank, "True")
        join_pins(gPlank, "out", sqPlank, "in")

        ' Scanning is the SIDE EFFECT of noticing, so it sits in a Sequence
        ' beside the decision rather than being one. It raises the flag and
        ' claims nothing, which is why the turn below still gets to act.
        ' SCANNING STOPS ONCE WE HAVE SOMEWHERE TO GO.
        '
        ' "plank needs to stop scanning if we are looking for a door or open
        '  area left or right"
        '
        ' As a Sequence this scanned AND THEN looked for a way - every tick,
        ' including all the way through a gap it had already committed to. A
        ' scan is for finding a way out; once there is one there is nothing
        ' left to look for, and the rays stayed up on the scope saying
        ' otherwise.
        '
        ' A Priority says it properly: take the way if there is one, and only
        ' scan when there is not.
        join_pins(sqPlank, "a", pTurn, "in")
        join_pins(sqPlank, "b", aScan, "in")

        ' KEEP DRIVING, TURNING TOWARD THE GAP. Door Gap, not Widest: only a
        ' way the hull FITS through is worth turning toward, and turning
        ' toward one it does not fit is how it arrives wedged. The throttle is
        ' left unwired on purpose - Drive Heading then eases it off the room
        ' actually ahead instead of a number guessed here.
        join_pins(pTurn, "a", gWay, "in")
        ' Same pick here. When a plank touches, the opening worth swinging
        ' toward is the one that still leads onward, not the roomiest one
        ' off to the side.
        join_pins(pHold, "door", tWay, "door")
        join_pins(tWay, "True", gWay, "True")
        join_pins(pHold, "door", pWayB, "door")
        join_pins(pWayB, "bearing", aGo, "bearing")
        join_pins(gWay, "out", aGo, "in")

        ' AND IF NOTHING FITS, DECLINE - do not Stop here.
        '
        ' Stopping claimed the tick, and a stopped tank never changes what it
        ' can see, so no way ever appeared and it sat there for the rest of
        ' the run. Declining hands the problem to the rear rules below, which
        ' is where backing out of a dead end already lives. The owner's rule
        ' is that we stop when we cannot turn - and reversing IS how you stop
        ' being somewhere you cannot turn.

        Dim gFit = spawn("Gate", 870.0F, 1190.0F)
        Dim aDrive = spawn("Drive Heading", 1090.0F, 1190.0F)
        join_pins(p4, "a", gFit, "in")
        join_pins(tFit, "True", gFit, "True")
        join_pins(gFit, "out", aDrive, "in")
        ' THE BEARING IT TESTED. Will Clear asks about the GOAL direction,
        ' so this has to drive the goal direction - it drove the deepest ray
        ' instead, which is a different bearing, and the rule confirmed one
        ' thing then did another.
        join_pins(goal, "bearing", aDrive, "bearing")

        Dim gDoor2 = spawn("Gate", 870.0F, 1320.0F)
        Dim tHasDoor = spawn("Has Door", 650.0F, 1320.0F)
        Dim aDoor2 = spawn("Through Door", 1090.0F, 1320.0F)
        join_pins(p4, "b", gDoor2, "in")
        join_pins(pHold, "door", tHasDoor, "door")
        join_pins(tHasDoor, "True", gDoor2, "True")
        join_pins(gDoor2, "out", aDoor2, "in")
        join_pins(pHold, "door", aDoor2, "door")

        ' ---- ANY GAP THAT FITS, EVEN A SIDEWAYS ONE --------------------
        '
        ' Best Progress above only offers ways that face forward and gain
        ' ground. In a cluttered start nothing qualifies, and without this the
        ' chain went from "no good gap" straight to "reverse" - past the gaps.
        ' The out of a pocket is a gap; it just is not one pointing at the goal.
        Dim pAnyW = spawn("Commit", 640.0F, 1520.0F)
        Dim tAnyW = spawn("Has Door", 870.0F, 1520.0F)
        Dim pAnyB = spawn("Door Bearing", 870.0F, 1620.0F)
        Dim gAnyW = spawn("Gate", 1090.0F, 1520.0F)
        Dim aAnyW = spawn("Through Door", 1310.0F, 1520.0F)
        join_pins(pWide, "door", pAnyW, "door")
        join_pins(pAnyW, "door", tAnyW, "door")
        join_pins(pAnyW, "door", pAnyB, "door")
        join_pins(pAnyW, "door", aAnyW, "door")
        join_pins(tAnyW, "True", gAnyW, "True")
        join_pins(p4, "c", gAnyW, "in")
        join_pins(gAnyW, "out", aAnyW, "in")

        ' ---- and only then, backing out ---------------------------------
        Dim p5 = spawn("Priority", 650.0F, 1700.0F)
        join_pins(p4, "d", p5, "in")

        ' THE FRONT DOOR INTO BACKING, and it had no doorman.
        '
        ' This rung reversed without ever entering the Backing STATE, so
        ' Is Backing read false on every tick, the whole pBack chain - Backed
        ' Enough, Enough Room, Set Seek - was unreachable, and this Reverse
        ' was re-decided from scratch sixty times a second against a bare
        ' threshold. Measured: 1526 board decisions in a 70 s run, 1520 of
        ' them a change of mind, alternating Reverse and Drive Heading with a
        ' median run of ONE tick. 44% of the run spent backing, pace 4%, and
        ' it finished three times further from the goal than it started.
        '
        ' The Sequence is the fix and it is the same shape the wedge rung
        ' already uses above: say what state we are entering, THEN act. Once
        ' Backing is set, Is Backing owns the next tick and the reversal ends
        ' when Backed Enough or Enough Room says so - which is what those
        ' nodes were put on the board to do.
        Dim gRear = spawn("Gate", 870.0F, 1440.0F)
        Dim sqRear = spawn("Sequence", 1090.0F, 1440.0F)
        Dim aSetB2 = spawn("Set Backing", 1310.0F, 1380.0F)
        Dim aRev2 = spawn("Reverse", 1310.0F, 1490.0F)
        ' ---- DOOR FAILED: GO ROUND THE END OF IT ------------------------
        '
        ' Ahead of reversing, because "no door found yet" is not the same
        ' thing as "forward is not an option" - and the old order treated
        ' them as the same thing, so the board backed away from obstacles it
        ' could have driven round.
        '
        ' Fed the GOAL bearing, so of the two edges it picks the one that
        ' costs least ground. Wrong is survivable here: it aims past an edge,
        ' drives, sees more of the thing, and aims again.
        Dim pRound = spawn("Round The End", 650.0F, 1610.0F)
        Dim gRound = spawn("Gate", 870.0F, 1610.0F)
        Dim aRound = spawn("Drive Heading", 1090.0F, 1610.0F)
        join_pins(goal, "bearing", pRound, "bearing")
        join_pins(p5, "a", gRound, "in")
        join_pins(pRound, "True", gRound, "True")
        join_pins(gRound, "out", aRound, "in")
        join_pins(pRound, "bearing", aRound, "bearing")

        join_pins(p5, "b", gRear, "in")
        join_pins(tRearB, "True", gRear, "True")
        join_pins(gRear, "out", sqRear, "in")
        join_pins(sqRear, "a", aSetB2, "in")
        join_pins(sqRear, "b", aRev2, "in")
        join_pins(rear, "bearing", aRev2, "bearing")

        ' THE LAST ELSE: HEAD FOR THE GOAL.
        '
        ' This rung used to be Reverse, and it fired as a matter of routine -
        ' drive, the plank clears, nothing qualifies, reverse, the plank
        ' touches, drive: 1218 times in ten seconds. Reversing belongs to the
        ' rules that have a reason for it, and since the stuck-turns rung sits
        ' above the state machine, being unable to move is already answered
        ' higher up.
        '
        ' So when everything else has declined, point at the goal and go. If
        ' that meets something, the rules above are what handle it.
        Dim aOnward = spawn("Drive Heading", 1090.0F, 1560.0F)
        join_pins(p5, "d", aOnward, "in")
        join_pins(goal, "bearing", aOnward, "bearing")

        Changed = False
        LogThis("brain: model built - {0} nodes, {1} wires", nodes.Count, links.Count)
    End Sub

    ' ======================================================================
    '  SAVING AND LOADING
    ' ======================================================================

    ''' <summary>
    ''' JSON, and the reasons are all about what happens LATER.
    '''
    ''' It is text, so a graph diffs in git and a bad merge is something you can
    ''' read rather than a binary conflict. It is editable by hand, which is how
    ''' a graph gets fixed when the editor is the thing that is broken.
    ''' System.Text.Json is in the box on net8, so no package and nothing to
    ''' install. And a file half-written by an older build still says plainly
    ''' what it meant.
    '''
    ''' NAMES, NEVER INDICES - this is the part that decides whether a saved
    ''' graph survives the next month. A node stores its KIND BY NAME and a wire
    ''' stores its PINS BY NAME. Save an index into KINDS instead and every graph
    ''' on disk silently re-points the first time a kind is inserted above it:
    ''' every node becomes the wrong node, nothing errors, and the file still
    ''' looks fine. Names go wrong LOUDLY - a kind that no longer exists is
    ''' named in the log and skipped, and the rest of the graph still loads.
    ''' </summary>
    Public Const FORMAT As String = "nuterra.brain.nodes"
    Public Const FORMAT_VERSION As Integer = 1

    ''' <summary>Beside the exe, where this app keeps everything else it reads.
    ''' A graph worth keeping gets copied into BrainTesting\graphs in the source
    ''' tree, which the project copies back out on the next build.</summary>
    Public Function GraphDir() As String
        Dim d = IO.Path.Combine(Application.StartupPath, "graphs")
        IO.Directory.CreateDirectory(d)
        Return d
    End Function

    Public Function GraphPath(name As String) As String
        Return IO.Path.Combine(GraphDir(), name & ".json")
    End Function

    ''' <summary>Every saved graph, by name.</summary>
    Public Function SavedGraphs() As List(Of String)
        Dim outp As New List(Of String)
        Try
            For Each f In IO.Directory.GetFiles(GraphDir(), "*.json")
                outp.Add(IO.Path.GetFileNameWithoutExtension(f))
            Next
        Catch
        End Try
        outp.Sort()
        Return outp
    End Function

    ''' <summary>Chr rather than doubled quotes. A backslash and a double quote
    ''' are what JSON needs escaped, and Chr(92)/Chr(34) name them - where ""
    ''' is a thing you have to stop and count.</summary>
    Private Function esc(t As String) As String
        If t Is Nothing Then Return ""
        Return t.Replace(Chr(92), Chr(92) & Chr(92)).Replace(Chr(34), Chr(92) & Chr(34))
    End Function

    ''' <summary>Write the board out. Returns False and says why on failure - a
    ''' save that quietly did nothing is the worst kind there is.</summary>
    Public Function SaveGraph(name As String) As Boolean
        Try
            Dim q = Chr(34)
            Dim b As New StringBuilder()
            b.AppendLine("{")
            b.AppendLine("  " & q & "format" & q & ": " & q & FORMAT & q & ",")
            b.AppendLine("  " & q & "version" & q & ": " & FORMAT_VERSION & ",")
            b.AppendLine("  " & q & "saved" & q & ": " & q &
                         DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") & q & ",")
            b.AppendLine(String.Format(
                "  " & q & "view" & q & ": {{ " & q & "scrollX" & q & ": {0:0.##}, " &
                q & "scrollY" & q & ": {1:0.##}, " & q & "zoom" & q & ": {2:0.###} }},",
                scroll.X, scroll.Y, zoom))

            b.AppendLine("  " & q & "nodes" & q & ": [")
            For i = 0 To nodes.Count - 1
                Dim n = nodes(i)
                ' The overrides, and only when there are some. A `set` block
                ' on every node would double the size of a file to say
                ' nothing, and a reader can tell absent from empty.
                Dim setsTxt = ""
                If n.sets.Count > 0 Then
                    Dim parts As New List(Of String)
                    For Each kv In n.sets
                        parts.Add(q & esc(kv.Key) & q & ": " &
                                  kv.Value.ToString("0.###"))
                    Next
                    setsTxt = ", " & q & "set" & q & ": {" & String.Join(", ", parts) & "}"
                End If
                b.AppendLine(String.Format(
                    "    {{ " & q & "id" & q & ": {0}, " & q & "kind" & q & ": " & q &
                    "{1}" & q & ", " & q & "x" & q & ": {2:0.##}, " & q & "y" & q &
                    ": {3:0.##}{4} }}{5}",
                    n.id, esc(n.kind.name), n.pos.X, n.pos.Y, setsTxt,
                    If(i = nodes.Count - 1, "", ",")))
            Next
            b.AppendLine("  ],")

            b.AppendLine("  " & q & "links" & q & ": [")
            Dim wrote = 0
            For i = 0 To links.Count - 1
                Dim l = links(i)
                Dim a = find(l.fromNode), c = find(l.toNode)
                If a Is Nothing OrElse c Is Nothing Then Continue For
                If l.fromPin >= a.kind.outs.Length Then Continue For
                If l.toPin >= c.kind.ins.Length Then Continue For
                If wrote > 0 Then b.AppendLine(",")
                b.Append(String.Format(
                    "    {{ " & q & "from" & q & ": {0}, " & q & "out" & q & ": " & q &
                    "{1}" & q & ", " & q & "to" & q & ": {2}, " & q & "in" & q & ": " &
                    q & "{3}" & q & " }}",
                    l.fromNode, esc(a.kind.outs(l.fromPin)),
                    l.toNode, esc(c.kind.ins(l.toPin))))
                wrote += 1
            Next
            If wrote > 0 Then b.AppendLine()
            b.AppendLine("  ]")
            b.AppendLine("}")

            IO.File.WriteAllText(GraphPath(name), b.ToString())
            Changed = False
            LogThis("brain: saved {0} - {1} nodes, {2} wires",
                    GraphPath(name), nodes.Count, wrote)
            Return True
        Catch ex As Exception
            LogThis("brain: save failed - {0}", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Read a board back.
    '''
    ''' A node whose kind is gone, or a wire whose pin is gone, is REPORTED AND
    ''' SKIPPED rather than failing the whole load. A graph is a drawing of a
    ''' design; losing one node of it to a rename should cost that node, not the
    ''' morning's work - and the log names which one, so it can be put back.
    ''' </summary>
    Public Function LoadGraph(name As String) As Boolean
        Dim path = GraphPath(name)
        If Not IO.File.Exists(path) Then
            LogThis("brain: no graph called {0}", name)
            Return False
        End If
        Try
            Using doc = JsonDocument.Parse(IO.File.ReadAllText(path))
                Dim root = doc.RootElement

                Dim fmt As JsonElement = Nothing
                If root.TryGetProperty("format", fmt) AndAlso fmt.GetString() <> FORMAT Then
                    LogThis("brain: {0} is not a node graph ({1})", name, fmt.GetString())
                    Return False
                End If

                nodes.Clear()
                links.Clear()
                selected = -1
                nextId = 1

                ' The id AS WRITTEN maps to the node being rebuilt, so the wires
                ' below resolve whatever the ids happen to be.
                Dim byOld As New Dictionary(Of Integer, Node)
                Dim lostKinds = 0
                Dim ns As JsonElement = Nothing
                If root.TryGetProperty("nodes", ns) Then
                    For Each e In ns.EnumerateArray()
                        Dim kn = e.GetProperty("kind").GetString()
                        Dim k = kind_of(kn)
                        If k Is Nothing Then
                            LogThis("brain: graph {0} wants a {1} node, which no longer exists",
                                    name, kn)
                            lostKinds += 1
                            Continue For
                        End If
                        Dim n As New Node()
                        n.id = e.GetProperty("id").GetInt32()
                        n.kind = k
                        n.pos = New System.Numerics.Vector2(e.GetProperty("x").GetSingle(),
                                                            e.GetProperty("y").GetSingle())
                        ' Settings are OPTIONAL and always have been - a file
                        ' written before they existed simply has none, and every
                        ' node falls back to its kind's default.
                        Dim st As JsonElement = Nothing
                        If e.TryGetProperty("set", st) Then
                            For Each sp In st.EnumerateObject()
                                n.sets(sp.Name) = sp.Value.GetSingle()
                            Next
                        End If

                        nodes.Add(n)
                        byOld(n.id) = n
                        nextId = Math.Max(nextId, n.id + 1)
                    Next
                End If

                Dim lostPins = 0
                Dim ls As JsonElement = Nothing
                If root.TryGetProperty("links", ls) Then
                    For Each e In ls.EnumerateArray()
                        Dim fa As Node = Nothing, tb As Node = Nothing
                        If Not byOld.TryGetValue(e.GetProperty("from").GetInt32(), fa) Then Continue For
                        If Not byOld.TryGetValue(e.GetProperty("to").GetInt32(), tb) Then Continue For
                        Dim i = Array.IndexOf(fa.kind.outs, e.GetProperty("out").GetString())
                        Dim j = Array.IndexOf(tb.kind.ins, e.GetProperty("in").GetString())
                        If i < 0 OrElse j < 0 Then
                            lostPins += 1
                            Continue For
                        End If
                        links.Add(New Link With {.fromNode = fa.id, .fromPin = i,
                                                 .toNode = tb.id, .toPin = j})
                    Next
                End If

                Dim vw As JsonElement = Nothing
                If root.TryGetProperty("view", vw) Then
                    Dim sx As JsonElement = Nothing
                    Dim sy As JsonElement = Nothing
                    Dim zm As JsonElement = Nothing
                    If vw.TryGetProperty("scrollX", sx) AndAlso vw.TryGetProperty("scrollY", sy) Then
                        scroll = New System.Numerics.Vector2(sx.GetSingle(), sy.GetSingle())
                    End If
                    If vw.TryGetProperty("zoom", zm) Then
                        zoom = Math.Min(3.0F, Math.Max(0.3F, zm.GetSingle()))
                    End If
                End If

                Changed = False
                LogThis("brain: loaded {0} - {1} nodes, {2} wires, {3} node(s) and {4} wire(s) dropped",
                        name, nodes.Count, links.Count, lostKinds, lostPins)
                Return True
            End Using
        Catch ex As Exception
            LogThis("brain: load failed - {0}", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' NEVER OPEN ON AN EMPTY BOARD. An editor showing nothing looks broken,
    ''' and the first question it raises is whether the window works at all
    ''' rather than what is on it. Last session's graph if there is one, the
    ''' brain as it runs today otherwise.
    ''' </summary>
    Public Sub EnsureSomething()
        If nodes.Count > 0 Then Return
        If LoadGraph("brain") Then Return
        BuildCurrentAI()
    End Sub

    ''' <summary>
    ''' THE SMALLEST BOARD THAT DRIVES: go, and stop on three returns.
    '''
    ''' Seven nodes, one Priority, two rules. It exercises the whole chain - a
    ''' sense, a test, an ordered choice and two acts - so when the big board
    ''' does something strange this is the one to come back to and ask whether
    ''' the machinery itself is still working.
    '''
    ''' Read it as: stop if three rays found something, otherwise drive at the
    ''' goal. The stop is FIRST because a Priority takes the first rule that
    ''' acts, and a stop that came second would never be reached - Drive
    ''' Heading always acts.
    ''' </summary>
    Public Sub BuildSimpleTest()
        nodes.Clear()
        links.Clear()
        nextId = 1
        selected = -1
        scroll = New System.Numerics.Vector2(0.0F, 0.0F)

        Dim tick = spawn("Tick", 0.0F, 40.0F)
        Dim scan = spawn("Ray Scan", 0.0F, 180.0F)
        Dim goal = spawn("Goal", 0.0F, 320.0F)

        Dim three = spawn("Hit Count", 230.0F, 180.0F)
        join_pins(scan, "hits", three, "hits")

        Dim pri = spawn("Priority", 460.0F, 40.0F)
        join_pins(tick, "out", pri, "in")

        Dim gate = spawn("Gate", 690.0F, 40.0F)
        Dim stopIt = spawn("Stop", 920.0F, 40.0F)
        join_pins(pri, "a", gate, "in")
        join_pins(three, "True", gate, "True")
        join_pins(gate, "out", stopIt, "in")

        ' d, not b: it is the fall-through, and it has no test in front of it
        ' because there is nothing left to ask by the time it is reached.
        Dim drive = spawn("Drive Heading", 690.0F, 200.0F)
        join_pins(pri, "d", drive, "in")
        join_pins(goal, "bearing", drive, "bearing")

        Changed = False
        LogThis("brain: simple board - {0} nodes, {1} wires", nodes.Count, links.Count)
    End Sub

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
            Dim how = If(HostResync, ImGuiCond.Always, ImGuiCond.FirstUseEver)
            ImGui.SetNextWindowPos(New System.Numerics.Vector2(0.0F, 0.0F), how)
            ImGui.SetNextWindowSize(New System.Numerics.Vector2(displayW, displayH), how)
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
            HostResync = False

            title_buttons()
        End If

        ImGui.BeginGroup()
        palette()
        ImGui.EndGroup()
        ImGui.SameLine()

        canvas(displayW, displayH)

        ImGui.End()
        ImGui.PopStyleColor()
    End Sub

    ''' <summary>
    ''' MINIMISE AND MAXIMISE, ON THE TITLE BAR BESIDE THE CLOSE X.
    '''
    ''' ImGui has no way to add a button to a title bar - it draws the close box
    ''' itself, inside Begin, and offers no hook. So these are placed over the
    ''' title bar by hand, to the left of where it puts that X, and sized off the
    ''' same font size ImGui sizes its own by, so the three stay a matched set at
    ''' any font scale.
    '''
    ''' THE CLIP RECT IS THE TRICK. Everything after Begin is clipped to the
    ''' window's CONTENT, which starts below the title bar - so both the drawing
    ''' and, less obviously, the hit testing get thrown away up there, and the
    ''' buttons are invisible AND dead. PushClipRect widens it to the whole
    ''' window for the few lines that need it.
    '''
    ''' NO ICON FONT. ImGui draws its own X as two lines rather than a glyph, and
    ''' these follow: a bar for minimise, a square for maximise, two offset
    ''' squares for restore. Shipping a font to draw three shapes would be a file
    ''' to install, a load path to get wrong and a licence to carry.
    ''' </summary>
    Private Sub title_buttons()
        Dim dl = ImGui.GetWindowDrawList()
        Dim wp = ImGui.GetWindowPos()
        Dim ww = ImGui.GetWindowWidth()
        Dim fs = ImGui.GetFontSize()
        Dim pad = ImGui.GetStyle().FramePadding
        Dim bar = ImGui.GetFrameHeight()

        ' Let the next few items live in the title bar.
        ImGui.PushClipRect(wp, New System.Numerics.Vector2(wp.X + ww, wp.Y + bar), False)
        Dim keep = ImGui.GetCursorPos()

        ' ImGui's close box sits one FramePadding in from the right and is one
        ' font-size square. Step left from it by the same stride twice.
        Dim cy = wp.Y + bar * 0.5F
        Dim stride = fs + 2.0F
        Dim cx2 = wp.X + ww - pad.X - fs * 0.5F - stride
        Dim cx1 = cx2 - stride

        If title_hit("##nodemin", cx1, cy, fs, dl) Then HostCmd = 1
        If Not icon(dl, "window-min", cx1, cy) Then glyph_min(dl, cx1, cy, fs)

        If title_hit("##nodemax", cx2, cy, fs, dl) Then HostCmd = 2
        If HostMaxed Then
            If Not icon(dl, "window-restore", cx2, cy) Then glyph_restore(dl, cx2, cy, fs)
        Else
            If Not icon(dl, "window-max", cx2, cy) Then glyph_max(dl, cx2, cy, fs)
        End If

        ImGui.SetCursorPos(keep)
        ImGui.PopClipRect()
    End Sub

    ''' <summary>
    ''' Stamp an icon at its own size, centred. False if there is no such icon,
    ''' and the caller draws the shape by hand instead.
    '''
    ''' AT 16 PIXELS, NOT SCALED TO THE FONT. These are 16px artwork; drawn at
    ''' any other size the hairlines in them blur and they stop looking like
    ''' icons. The hit area still follows the font size, so the button grows
    ''' with the theme even when the picture in it does not.
    '''
    ''' Tinted with the text colour, which is what makes a set drawn for light
    ''' toolbars legible here - see BrainIcons.MONO.
    ''' </summary>
    Private Function icon(dl As ImDrawListPtr, name As String,
                          cx As Single, cy As Single) As Boolean
        Dim t = BrainIcons.Tex(name)
        If t = 0 Then Return False
        Const HALF As Single = 8.0F
        dl.AddImage(New IntPtr(t),
                    New System.Numerics.Vector2(cx - HALF, cy - HALF),
                    New System.Numerics.Vector2(cx + HALF, cy + HALF),
                    New System.Numerics.Vector2(0.0F, 0.0F), New System.Numerics.Vector2(1.0F, 1.0F),
                    ImGui.GetColorU32(ImGuiCol.Text))
        Return True
    End Function

    ''' <summary>One title-bar button's hit area and its hover lozenge, drawn the
    ''' way ImGui draws the close box's. Returns True on a click.</summary>
    Private Function title_hit(id As String, cx As Single, cy As Single,
                               fs As Single, dl As ImDrawListPtr) As Boolean
        ImGui.SetCursorScreenPos(New System.Numerics.Vector2(cx - fs * 0.5F, cy - fs * 0.5F))
        ImGui.InvisibleButton(id, New System.Numerics.Vector2(fs, fs))
        Dim hot = ImGui.IsItemHovered()
        If hot OrElse ImGui.IsItemActive() Then
            Dim col = ImGui.GetColorU32(If(ImGui.IsItemActive(),
                                           ImGuiCol.ButtonActive, ImGuiCol.ButtonHovered))
            dl.AddCircleFilled(New System.Numerics.Vector2(cx, cy), fs * 0.5F + 1.0F, col, 12)
        End If
        Return ImGui.IsItemClicked()
    End Function

    ''' <summary>A bar along the bottom, where a minimised window goes.</summary>
    Private Sub glyph_min(dl As ImDrawListPtr, cx As Single, cy As Single, fs As Single)
        Dim r = fs * 0.28F
        Dim ink = ImGui.GetColorU32(ImGuiCol.Text)
        dl.AddLine(New System.Numerics.Vector2(cx - r, cy + r * 0.7F),
                   New System.Numerics.Vector2(cx + r, cy + r * 0.7F), ink, 1.6F)
    End Sub

    ''' <summary>An empty frame - the window about to fill the screen.</summary>
    Private Sub glyph_max(dl As ImDrawListPtr, cx As Single, cy As Single, fs As Single)
        Dim r = fs * 0.28F
        Dim ink = ImGui.GetColorU32(ImGuiCol.Text)
        dl.AddRect(New System.Numerics.Vector2(cx - r, cy - r),
                   New System.Numerics.Vector2(cx + r, cy + r), ink, 0.0F, ImDrawFlags.None, 1.4F)
    End Sub

    ''' <summary>Two frames, one behind the other - the window stepping back out
    ''' of the screen it filled.</summary>
    Private Sub glyph_restore(dl As ImDrawListPtr, cx As Single, cy As Single, fs As Single)
        Dim r = fs * 0.24F
        Dim off = r * 0.55F
        Dim ink = ImGui.GetColorU32(ImGuiCol.Text)
        dl.AddRect(New System.Numerics.Vector2(cx - r + off, cy - r - off),
                   New System.Numerics.Vector2(cx + r + off, cy + r - off), ink, 0.0F, ImDrawFlags.None, 1.2F)
        dl.AddRect(New System.Numerics.Vector2(cx - r - off, cy - r + off),
                   New System.Numerics.Vector2(cx + r - off, cy + r + off), ink, 0.0F, ImDrawFlags.None, 1.4F)
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
        ' ---- SAVE, LOAD, AND THE NAME BEING WRITTEN ----------------------
        '
        ' The name is shown, not implied. A Save button with no visible target
        ' is a button you press and then go looking for the file.
        If ImGui.Button("Save", New System.Numerics.Vector2(64.0F, 0.0F)) Then
            SaveGraph(graphName)
        End If
        If ImGui.IsItemHovered() Then ImGui.SetTooltip(GraphPath(graphName))
        ImGui.SameLine()
        If ImGui.Button("Load", New System.Numerics.Vector2(66.0F, 0.0F)) Then
            ImGui.OpenPopup(LOAD_ASK)
        End If
        ImGui.SetNextItemWidth(134.0F)
        ImGui.InputText("##graphname", graphName, 64)

        Dim lmid = ImGui.GetIO().DisplaySize
        ImGui.SetNextWindowPos(New System.Numerics.Vector2(lmid.X * 0.5F, lmid.Y * 0.5F),
                               ImGuiCond.Appearing,
                               New System.Numerics.Vector2(0.5F, 0.5F))
        If ImGui.BeginPopupModal(LOAD_ASK) Then
            Dim saved = SavedGraphs()
            If saved.Count = 0 Then
                ImGui.TextDisabled("Nothing saved yet.")
            Else
                ' Said before the click, not after. Loading over unsaved work is
                ' the one thing in here that cannot be undone.
                If Changed Then
                    ImGui.TextDisabled("The board has unsaved changes.")
                End If
                For Each g In saved
                    If ImGui.Button(g, New System.Numerics.Vector2(220.0F, 22.0F)) Then
                        If LoadGraph(g) Then graphName = g
                        ImGui.CloseCurrentPopup()
                    End If
                Next
            End If
            ImGui.Spacing()
            If ImGui.Button("Cancel", New System.Numerics.Vector2(100.0F, 24.0F)) OrElse
               ImGui.IsKeyPressed(ImGuiKey.Escape) Then
                ImGui.CloseCurrentPopup()
            End If
            ImGui.EndPopup()
        End If
        ImGui.Separator()

        If ImGui.Button("Current AI", New System.Numerics.Vector2(134.0F, 0.0F)) Then
            If nodes.Count > 0 OrElse links.Count > 0 Then
                ImGui.OpenPopup(MODEL_ASK)
            Else
                BuildCurrentAI()
            End If
        End If
        If ImGui.IsItemHovered() Then
            ImGui.SetTooltip("Lay out RangeBrain as it runs today")
        End If
        If ImGui.Button("Simple test", New System.Numerics.Vector2(134.0F, 0.0F)) Then
            ImGui.OpenPopup(SIMPLE_ASK)
        End If
        If ImGui.IsItemHovered() Then
            ImGui.SetTooltip("Drive, and stop on three returns. Seven nodes.")
        End If

        Dim smid = ImGui.GetIO().DisplaySize
        ImGui.SetNextWindowPos(New System.Numerics.Vector2(smid.X * 0.5F, smid.Y * 0.5F),
                               ImGuiCond.Appearing, New System.Numerics.Vector2(0.5F, 0.5F))
        If ImGui.BeginPopupModal(SIMPLE_ASK) Then
            ImGui.Text("Replace the board with the simple test?")
            ImGui.TextDisabled(String.Format("{0} node(s) and {1} wire(s) go.",
                                             nodes.Count, links.Count))
            ImGui.Spacing()
            If ImGui.Button("Build it", New System.Numerics.Vector2(100.0F, 24.0F)) Then
                BuildSimpleTest()
                ImGui.CloseCurrentPopup()
            End If
            ImGui.SameLine()
            If ImGui.Button("Cancel", New System.Numerics.Vector2(100.0F, 24.0F)) OrElse
               ImGui.IsKeyPressed(ImGuiKey.Escape) Then
                ImGui.CloseCurrentPopup()
            End If
            ImGui.EndPopup()
        End If

        Dim mmid = ImGui.GetIO().DisplaySize
        ImGui.SetNextWindowPos(New System.Numerics.Vector2(mmid.X * 0.5F, mmid.Y * 0.5F),
                               ImGuiCond.Appearing, New System.Numerics.Vector2(0.5F, 0.5F))
        If ImGui.BeginPopupModal(MODEL_ASK) Then
            ImGui.Text("Replace the board with the current AI?")
            ImGui.TextDisabled(String.Format("{0} node(s) and {1} wire(s) go.",
                                             nodes.Count, links.Count))
            ImGui.Spacing()
            If ImGui.Button("Build it", New System.Numerics.Vector2(100.0F, 24.0F)) Then
                BuildCurrentAI()
                ImGui.CloseCurrentPopup()
            End If
            ImGui.SameLine()
            If ImGui.Button("Cancel", New System.Numerics.Vector2(100.0F, 24.0F)) OrElse
               ImGui.IsKeyPressed(ImGuiKey.Escape) Then
                ImGui.CloseCurrentPopup()
            End If
            ImGui.EndPopup()
        End If
        ImGui.Separator()
        ' THE PIN HELP SWITCH LIVES HERE TOO. Its X is in the corner of the
        ' board and the only way back was a small ? in the same spot - a
        ' control whose route back is where it vanished from is one you can
        ' lose. The palette is always on screen and never moves.
        Dim hp = HELP_SHOW
        If ImGui.Checkbox("Pin help", hp) Then HELP_SHOW = hp
        ImGui.Separator()

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
        ' CLEAR ASKS FIRST.
        '
        ' Every other destructive thing here undoes in one gesture - a node
        ' comes back by adding it, a wire by dragging it again. This one throws
        ' away the whole graph, there is no undo behind it, and it sits two
        ' pixels under a list of buttons whose whole job is to be clicked
        ' without thinking. A misfire costs the session's work.
        '
        ' Greyed out with nothing to lose, so the question is never asked about
        ' nothing - a confirmation that appears when the answer cannot matter
        ' is the one people learn to click through.
        Dim anything = nodes.Count > 0 OrElse links.Count > 0
        If Not anything Then ImGui.BeginDisabled()
        If ImGui.Button("Clear", New System.Numerics.Vector2(134.0F, 0.0F)) Then
            ImGui.OpenPopup(CLEAR_ASK)
        End If
        If Not anything Then ImGui.EndDisabled()

        ' Opened and begun in the same scope on purpose. ImGui hashes a popup's
        ' name against the CURRENT id stack, and this is inside a child window -
        ' so opening here and beginning it out at the top of Draw would hash two
        ' different ids and the popup would never appear.
        Dim mid = ImGui.GetIO().DisplaySize
        ImGui.SetNextWindowPos(New System.Numerics.Vector2(mid.X * 0.5F, mid.Y * 0.5F),
                               ImGuiCond.Appearing,
                               New System.Numerics.Vector2(0.5F, 0.5F))
        If ImGui.BeginPopupModal(CLEAR_ASK) Then
            ImGui.Text(String.Format("Delete all {0} node(s) and {1} wire(s)?",
                                     nodes.Count, links.Count))
            ImGui.TextDisabled("There is no undo.")
            ImGui.Spacing()
            If ImGui.Button("Clear it", New System.Numerics.Vector2(100.0F, 24.0F)) Then
                nodes.Clear()
                links.Clear()
                selected = -1
                ' MARKED CHANGED, which the old Clear did not do. Emptying the
                ' board is the largest edit there is, and it was the one edit
                ' that left the title saying nothing had happened.
                Changed = True
                ImGui.CloseCurrentPopup()
            End If
            ImGui.SameLine()
            ' Escape as well as the button. A confirmation you can only leave
            ' by aiming at something is worse than the click it is guarding.
            If ImGui.Button("Cancel", New System.Numerics.Vector2(100.0F, 24.0F)) OrElse
               ImGui.IsKeyPressed(ImGuiKey.Escape) Then
                ImGui.CloseCurrentPopup()
            End If
            ImGui.EndPopup()
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
        '
        ' A CLICK ON A WIRE SELECTS IT. ImGui cannot hit-test a curve, so the
        ' curve is walked here at the resolution it is drawn at and the nearest
        ' sample to the cursor wins. Only when the click did not land on
        ' something ImGui owns: a node or a pin under the cursor takes it, so a
        ' wire passing behind a node cannot steal the grab.
        ' EVERYTHING THE SELECTED NODE IS JOINED TO, lit pink and thickened.
        ' Both directions, because "what feeds this and what does it feed" is
        ' one question and the board is worst at answering it by eye.
        '
        ' Drawn in two passes so the lit ones land ON TOP. In one pass a
        ' highlighted wire can be buried under the dozen ordinary ones crossing
        ' it, which is exactly the case this is for.
        ' Three passes, dimmest first, so the thing you are looking for is
        ' never buried under the dozen wires crossing it.
        For Each l In links
            If l.fromNode = selected OrElse l.toNode = selected Then Continue For
            If LitWires.Contains(wire_key(l.fromNode, l.fromPin, l.toNode, l.toPin)) Then Continue For
            Dim a = find(l.fromNode), b = find(l.toNode)
            If a Is Nothing OrElse b Is Nothing Then Continue For
            Dim pa = pin_pos(a, to_screen(p0, a.pos), l.fromPin, False)
            Dim pb = pin_pos(b, to_screen(p0, b.pos), l.toPin, True)
            bez(dl, pa, pb, WIRE)
        Next

        ' WHAT THE BRAIN JUST DID. The route it took and the values it read,
        ' this tick - the path was the one thing the board still made you trace
        ' by eye.
        For Each l In links
            If l.fromNode = selected OrElse l.toNode = selected Then Continue For
            If Not LitWires.Contains(wire_key(l.fromNode, l.fromPin, l.toNode, l.toPin)) Then Continue For
            Dim a = find(l.fromNode), b = find(l.toNode)
            If a Is Nothing OrElse b Is Nothing Then Continue For
            Dim pa = pin_pos(a, to_screen(p0, a.pos), l.fromPin, False)
            Dim pb = pin_pos(b, to_screen(p0, b.pos), l.toPin, True)
            bez(dl, pa, pb, WIRE_LIVE, 3.0F)
        Next

        If selected >= 0 Then
            For Each l In links
                If l.fromNode <> selected AndAlso l.toNode <> selected Then Continue For
                Dim a = find(l.fromNode), b = find(l.toNode)
                If a Is Nothing OrElse b Is Nothing Then Continue For
                Dim pa = pin_pos(a, to_screen(p0, a.pos), l.fromPin, False)
                Dim pb = pin_pos(b, to_screen(p0, b.pos), l.toPin, True)
                bez(dl, pa, pb, WIRE_PICK, 3.0F)
            Next
        End If

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
            ' WHAT THE BRAIN DID, on the node itself. Selection and hover
            ' still win - they are about the mouse, and the mouse is what you
            ' are doing right now - but underneath them a node the walk reached
            ' outlines green and the act that ended the tick outlines bright.
            ' A board where you can SEE the live path is the difference between
            ' reading a heartbeat and watching the decision.
            Dim edgeCol = EDGE
            Dim edgeW = 1.0F
            If Fired.Contains(n.id) Then
                edgeCol = FIRED_EDGE
                edgeW = 2.0F
            End If
            If ActedNode = n.id Then
                edgeCol = ACTED_EDGE
                edgeW = 3.0F
            End If
            If selected = n.id Then
                edgeCol = NODE_SEL
                edgeW = 2.0F
            ElseIf hot Then
                edgeCol = HOVER
                edgeW = 2.0F
            End If
            dl.AddRect(o, br, edgeCol, 4.0F * zoom, 0, edgeW)

            ' The answer, on the header. Absent when the test was never asked -
            ' which is a different thing from answering no, and on a Priority
            ' chain that difference is the whole story.
            Dim ans As Boolean
            If Tested.TryGetValue(n.id, ans) Then
                dl.AddCircleFilled(
                    New System.Numerics.Vector2(br.X - 9.0F * zoom, o.Y + HDR_H * 0.5F * zoom),
                    4.0F * zoom, If(ans, ANS_YES, ANS_NO), 10)
            End If

            ' WHAT IT PRODUCED, along the bottom edge. Only while the brain is
            ' actually walking the board - a stale number left over from the
            ' last run it drove would be worse than none.
            Dim vtext As String = Nothing
            If Vals.TryGetValue(n.id, vtext) AndAlso zoom > 0.6F Then
                dl.AddText(font, fsz * 0.9F,
                           New System.Numerics.Vector2(o.X + 8.0F * zoom,
                                                       br.Y - 15.0F * zoom),
                           VAL_TXT, vtext)
            End If
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

            ' AND A PLAIN CLICK LETS GO. This is what was missing: the only
            ' clear was the drag above, so clicking empty board did nothing at
            ' all - and since nudging the board to check fires the drag, it read
            ' as a deselect that had worked and failed to repaint.
            '
            ' On release rather than press, and only when the board was not
            ' dragged in between, or every pan would throw the selection away at
            ' the moment it started.
            If ImGui.IsItemDeactivated() AndAlso
               Not ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3.0F) Then
                selected = -1
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

        help_panel()

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
        ' RIGHT-CLICK CUTS. Dragging a wire off an input and dropping it on
        ' nothing already disconnected it, but only if you dragged far
        ' enough - release on the pin you started from and it reconnects,
        ' which reads as the editor ignoring you. This needs no gesture.
        If ImGui.IsItemClicked(ImGuiMouseButton.Right) Then
            Dim cut = 0
            If isIn Then
                cut = links.RemoveAll(Function(l) l.toNode = n.id AndAlso l.toPin = idx)
            Else
                cut = links.RemoveAll(Function(l) l.fromNode = n.id AndAlso l.fromPin = idx)
            End If
            If cut > 0 Then Changed = True
        End If

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
                ' AN OUTPUT GIVES UP ITS WIRES TOO. "I should be able to break a
                ' route by dragging its out to no where and releasing" - so they
                ' come away on pickup, and the drag either re-makes one on a
                ' valid input or, released over nothing, leaves the route cut.
                If Not isIn Then
                    Dim gone = links.RemoveAll(
                        Function(l) l.fromNode = n.id AndAlso l.fromPin = idx)
                    If gone > 0 Then Changed = True
                End If
                wireNode = n.id
                wirePin = idx
                wireFromOut = Not isIn
            End If
        End If
        ' ALLOW WHEN BLOCKED BY ACTIVE ITEM, and this is the whole reason
        ' dragging a wire never connected anything. The pin the drag STARTED on
        ' is the active item for the length of the drag, and by default ImGui
        ' refuses to report any other item as hovered while that is true. Every
        ' target pin said "not me", hoverNode stayed -1, and the drop had
        ' nowhere to land - with no error, because nothing had gone wrong as
        ' far as any one line of it was concerned.
        If wireNode >= 0 Then
            If ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) Then
                hoverNode = n.id
                hoverPin = idx
                hoverIn = isIn
            ElseIf hoverNode = n.id AndAlso hoverPin = idx AndAlso hoverIn = isIn Then
                ' AND FORGET IT ON THE WAY OUT. This was only ever cleared when
                ' a wire was dropped, so a cursor that brushed a pin and moved
                ' on still counted as being over it - releasing on empty space
                ' would have wired up whatever was last touched.
                hoverNode = -1
                hoverPin = -1
            End If
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
                    b As System.Numerics.Vector2, col As UInteger,
                    Optional thick As Single = 2.0F)
        Dim dx = Math.Max(40.0F, Math.Abs(b.X - a.X) * 0.5F)
        dl.AddBezierCubic(a, New System.Numerics.Vector2(a.X + dx, a.Y),
                          New System.Numerics.Vector2(b.X - dx, b.Y), b, col, thick)
    End Sub

End Module

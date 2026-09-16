Imports System.Collections.Generic

''' <summary>
''' Tank-to-tank brain communications.
'''
''' ROUTING RULES:
'''   * every wire message starts with the SENDER tank id;
'''   * command is a small enumerated byte value after the id;
'''   * each tank has one string inbox: TankDrive.commInput;
'''   * an occupied inbox is never overwritten - another sender retries later;
'''   * each tank may have one outbound request waiting for a reply at a time;
'''   * waiting for a reply NEVER blocks TankDrive movement or its brain state;
'''   * a tank may still RECEIVE and answer another request while its own
'''     outbound request is waiting;
'''   * only the higher-id tank opens first contact for a pair.
'''
'''
''' Wire format is deliberately tiny and deterministic:
'''     senderId|command|data
'''
''' Example:
'''     24|1|0     = tank 24 asks STATUS
'''     11|2|0     = tank 11 replies brainState Forward (0)
'''
'''
''' This is only the communication plumbing. No movement decision consumes a
''' reply yet.
''' </summary>
Public Module TankComms

    ' Radio/brain contact range. This is deliberately independent of the
    ' collision rays: a tank can communicate with any live tank within 20 m,
    ' even when none of the narrow avoidance rays intersects that hull.
    Private Const COMM_RANGE_M As Single = 20.0F

    ''' <summary>
    ''' Commands are numeric on the wire so future branches can be grouped into
    ''' ranges without parsing free-form text.
    ''' </summary>
    Public Enum TankCommCommand As Byte
        None = 0
        StatusRequest = 1
        StatusReply = 2
    End Enum

    ' One completed/opened first-contact exchange per pair for this SIM run.
    ' Without this a pair sitting inside sensor range would exchange STATUS on
    ' every frame after the previous reply finished.
    Private ReadOnly contacted As New HashSet(Of Long)

    ' One diagnostic line per pair when the pair first enters radio range.
    ' This is deliberately separate from the message state so we can prove the
    ' 20 m proximity layer is alive even if routing/handshake is broken.
    Private ReadOnly rangeSeen As New HashSet(Of Long)

    ' ONE outstanding outbound request per tank. The value is the exact peer id
    ' from which this tank expects the reply. This serialises a five-tank pile-up
    ' without ever blocking movement: other contacts simply retry on later frames.
    Private ReadOnly waitingFor As New Dictionary(Of TankInstance, Integer)

    ' Tanks touched by comms, so Reset Sim clears their inbox strings immediately.
    Private ReadOnly known As New HashSet(Of TankInstance)

    ''' <summary>
    ''' Reset all communication state for a new SIM experiment.
    ''' </summary>
    Public Sub Reset()
        For Each t In known
            If t Is Nothing OrElse t.drive Is Nothing Then Continue For
            t.drive.commInput = ""
        Next
        known.Clear()
        rangeSeen.Clear()
        waitingFor.Clear()
        contacted.Clear()
    End Sub

    ''' <summary>
    ''' Read at most one message from this tank's inbox this frame.
    ''' Communications never take ownership of movement; TankDrive continues
    ''' normally after this returns.
    ''' </summary>
    Public Sub Tick(inst As TankInstance, others As List(Of TankInstance))
        If inst Is Nothing OrElse inst.drive Is Nothing Then Return
        If Not TankSim.SIM_RUN Then Return

        known.Add(inst)

        Dim raw = inst.drive.commInput
        If String.IsNullOrEmpty(raw) Then Return

        Dim senderId As Integer
        Dim cmd As TankCommCommand
        Dim data As Integer
        If Not TryDecode(raw, senderId, cmd, data) Then
            LogThis("tank comm: DROP tank={0} bad='{1}'", inst.id, raw)
            ClearInbox(inst)
            Return
        End If

        ' TANK ID IS THE ROUTE. Resolve the sender from the live fleet so a
        ' request or reply always goes back to the exact TankInstance that sent
        ' it. Tank ids are unique across the current fleet.
        Dim sender = FindTankById(senderId, others)
        If sender Is Nothing OrElse sender Is inst OrElse sender.drive Is Nothing Then
            LogThis("tank comm: DROP tank={0} sender={1} not-live", inst.id, senderId)
            ClearInbox(inst)
            Return
        End If
        known.Add(sender)

        Select Case cmd
            Case TankCommCommand.StatusRequest
                HandleStatusRequest(inst, sender)

            Case TankCommCommand.StatusReply
                HandleStatusReply(inst, sender, data)

            Case Else
                LogThis("tank comm: DROP tank={0} sender={1} cmd={2}",
                        inst.id, senderId, CInt(cmd))
                ClearInbox(inst)
        End Select
    End Sub

    ''' <summary>
    ''' Radio contact is proximity, not collision sensing. Every live tank inside
    ''' 20 m is eligible for first contact. The higher-id rule inside
    ''' TryFirstContact still guarantees that only one side opens a pair.
    ''' </summary>
    Public Sub ObserveNearby(inst As TankInstance, others As List(Of TankInstance))
        If inst Is Nothing OrElse inst.drive Is Nothing OrElse others Is Nothing Then Return
        If Not TankSim.SIM_RUN Then Return

        known.Add(inst)
        Dim range2 = COMM_RANGE_M * COMM_RANGE_M

        For Each peer In others
            If peer Is Nothing OrElse peer Is inst OrElse peer.drive Is Nothing Then Continue For

            Dim dx = peer.position.X - inst.position.X
            Dim dz = peer.position.Z - inst.position.Z
            Dim dist2 = dx * dx + dz * dz
            If dist2 > range2 Then Continue For

            known.Add(peer)

            ' PURE RADIO-RANGE DIAGNOSTIC. Only the higher-id side prints so
            ' the same pair does not appear twice. Write DIRECTLY to Debug and
            ' Console rather than through LogThis: this test must survive any
            ' logging filter while we prove the proximity call path itself.
            If inst.id > peer.id Then
                Dim key = PairKey(inst.id, peer.id)
                If rangeSeen.Add(key) Then
                    Dim dist = CSng(Math.Sqrt(dist2))
                    Dim line = String.Format(
                        "tank comm: RANGE tank={0} peer={1} dist={2:0.0}m",
                        inst.id, peer.id, dist)
#If DEBUG Then
                    System.Diagnostics.Debug.Print(line)
#End If
                    Console.WriteLine(line)
                End If
            End If

            TryFirstContact(inst, peer)
        Next
    End Sub

    ''' <summary>
    ''' Sensor-specific peer identity is still available for later commands.
    ''' Duplicate rays hitting the same hull create only one contact try this
    ''' frame. This is no longer required for the basic 20 m STATUS handshake.
    ''' </summary>
    Public Sub ObserveSensorHits(inst As TankInstance, hitTanks As TankInstance())
        If inst Is Nothing OrElse inst.drive Is Nothing OrElse hitTanks Is Nothing Then Return
        If Not TankSim.SIM_RUN Then Return

        known.Add(inst)
        Dim seen As New HashSet(Of Integer)

        For Each peer In hitTanks
            If peer Is Nothing OrElse peer Is inst OrElse peer.drive Is Nothing Then Continue For
            If Not seen.Add(peer.id) Then Continue For
            known.Add(peer)
            TryFirstContact(inst, peer)
        Next
    End Sub

    ''' <summary>
    ''' Answer STATUS from the exact sender id carried in the message.
    '''
    ''' If the sender's own inbox is busy, leave this request in place and try
    ''' again next frame. That is the important pile-up rule: no overwrite, no
    ''' lost reply, and no movement blocking.
    ''' </summary>
    Private Sub HandleStatusRequest(receiver As TankInstance, sender As TankInstance)
        ' First contact for a pair must originate from the higher id. Rejecting
        ' the opposite direction here makes the rule true even if a bad caller
        ' is added later.
        If sender.id <= receiver.id Then
            LogThis("tank comm: DROP tank={0} <- tank={1} STATUS wrong-id-order",
                    receiver.id, sender.id)
            ClearInbox(receiver)
            Return
        End If

        ' The reply is written into the original sender's inbox. If that sender
        ' is currently processing another conversation, keep this request parked
        ' in our inbox until its return route is free.
        If Not String.IsNullOrEmpty(sender.drive.commInput) Then Return

        LogThis("tank comm: REQ tank={0} <- tank={1} cmd=StatusRequest",
                receiver.id, sender.id)

        Dim stateCode = CInt(receiver.drive.brainState)
        sender.drive.commInput = Encode(receiver.id,
                                        TankCommCommand.StatusReply,
                                        stateCode)

        ClearInbox(receiver)

        LogThis("tank comm: REPLY tank={0} -> tank={1} state={2}",
                receiver.id, sender.id, receiver.drive.brainState.ToString())
    End Sub

    ''' <summary>
    ''' Route a STATUS reply only to the peer this tank is actually waiting on.
    ''' A reply from any other id is not allowed to satisfy the wait.
    ''' </summary>
    Private Sub HandleStatusReply(receiver As TankInstance,
                                  sender As TankInstance,
                                  stateCode As Integer)
        Dim expectedId As Integer
        If Not waitingFor.TryGetValue(receiver, expectedId) Then
            LogThis("tank comm: DROP tank={0} <- tank={1} reply-not-waiting",
                    receiver.id, sender.id)
            ClearInbox(receiver)
            Return
        End If

        If expectedId <> sender.id Then
            LogThis("tank comm: DROP tank={0} <- tank={1} expected={2}",
                    receiver.id, sender.id, expectedId)
            ClearInbox(receiver)
            Return
        End If

        Dim stateText = stateCode.ToString()
        If System.Enum.IsDefined(GetType(TankBrainState), stateCode) Then
            stateText = CType(stateCode, TankBrainState).ToString()
        End If

        LogThis("tank comm: RECV tank={0} <- tank={1} state={2}",
                receiver.id, sender.id, stateText)

        waitingFor.Remove(receiver)
        ClearInbox(receiver)
    End Sub

    ''' <summary>
    ''' First contact. Both tanks may see each other; only the higher id opens
    ''' the conversation. A tank with one outstanding request finishes it before
    ''' opening another, which gives pile-ups deterministic serial routing.
    ''' </summary>
    Private Sub TryFirstContact(sender As TankInstance, peer As TankInstance)
        If sender.id <= peer.id Then Return
        If waitingFor.ContainsKey(sender) Then Return

        Dim key = PairKey(sender.id, peer.id)
        If contacted.Contains(key) Then Return

        ' One-string inbox: never overwrite somebody else's message. This pair
        ' simply retries on a later sensor frame.
        If Not String.IsNullOrEmpty(peer.drive.commInput) Then Return

        peer.drive.commInput = Encode(sender.id,
                                      TankCommCommand.StatusRequest,
                                      0)
        waitingFor(sender) = peer.id
        contacted.Add(key)

        LogThis("tank comm: SEND tank={0} -> tank={1} cmd=StatusRequest",
                sender.id, peer.id)
    End Sub

    Private Sub ClearInbox(inst As TankInstance)
        If inst Is Nothing OrElse inst.drive Is Nothing Then Return
        inst.drive.commInput = ""
    End Sub

    ''' <summary>
    ''' senderId is FIRST, per the routing rule.
    ''' </summary>
    Private Function Encode(senderId As Integer,
                            cmd As TankCommCommand,
                            data As Integer) As String
        Return senderId.ToString() & "|" & CInt(cmd).ToString() & "|" & data.ToString()
    End Function

    Private Function TryDecode(raw As String,
                               ByRef senderId As Integer,
                               ByRef cmd As TankCommCommand,
                               ByRef data As Integer) As Boolean
        senderId = -1
        cmd = TankCommCommand.None
        data = 0

        If String.IsNullOrWhiteSpace(raw) Then Return False
        Dim p = raw.Split("|"c)
        If p.Length < 2 Then Return False

        Dim cmdCode As Integer
        If Not Integer.TryParse(p(0), senderId) Then Return False
        If Not Integer.TryParse(p(1), cmdCode) Then Return False
        If cmdCode < Byte.MinValue OrElse cmdCode > Byte.MaxValue Then Return False

        cmd = CType(CByte(cmdCode), TankCommCommand)

        If p.Length >= 3 AndAlso p(2).Length > 0 Then
            If Not Integer.TryParse(p(2), data) Then Return False
        End If

        Return True
    End Function

    Private Function FindTankById(id As Integer,
                                  others As List(Of TankInstance)) As TankInstance
        If others Is Nothing Then Return Nothing
        For Each t In others
            If t Is Nothing Then Continue For
            If t.id = id Then Return t
        Next
        Return Nothing
    End Function

    Private Function PairKey(a As Integer, b As Integer) As Long
        Dim lo = Math.Min(a, b)
        Dim hi = Math.Max(a, b)
        Return (CLng(lo) << 32) Or (CLng(hi) And &HFFFFFFFFL)
    End Function

End Module

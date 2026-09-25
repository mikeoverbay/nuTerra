Imports OpenTK.Mathematics

''' <summary>
''' A CREW OF BRAINS, ONE PER TANK, BEHIND ONE IBrain.
'''
''' "lets get all 5 tanks seeking" - the owner, 2026-09-25.
'''
''' Everything below this was already multi-hull and nobody had noticed.
''' BrainSim.Apply drives EVERY hull from outp.throttle(i) / outp.steer(i),
''' BrainOutput.ForHulls sizes its arrays to the whole roster, and GraphBrain
''' is a Class whose state - the board, the backing counters, the held way -
''' is all per-instance. Exactly one thing was singular: the brain itself.
'''
''' So this is not a rewrite of the driving. It is an IBrain that owns N
''' IBrains and hands each of them the tick in turn, and BrainSim never
''' learns that anything changed - it still calls Brain.Tick once and gets
''' one BrainOutput back. A crew of one behaves precisely as the single brain
''' did, which is what makes it safe to install unconditionally instead of
''' keeping two code paths and a flag to choose between them.
'''
''' THE SEAT, AND WHY IT IS A BORROWED GLOBAL.
'''
''' BrainRadar.HULL means "the hull this brain is working for". A brain reads
''' it to find itself; the camera, the panel, the ahead-scope and the walk
''' view read it to find the tank the human is looking at. Those were the same
''' number only because there was one brain.
'''
''' They still are, almost all of the time. This borrows the seat for the
''' length of one brain's Tick and always puts it back in a Finally, so
''' outside this loop BrainRadar.HULL keeps meaning what every other file in
''' the app already believes it means. That is why no panel, no camera and no
''' overlay needed touching: the number they read is unchanged.
'''
''' AND THE WATCHED HULL GOES LAST. The radar is a shared scratchpad - Scan
''' clears and refills BrainRadar.WAYS, the parallax, the fit - and whatever
''' the last brain left behind is what gets drawn. Ticking the watched tank
''' last means the rays, chains and cubes on screen belong to the tank being
''' looked at rather than to whichever hull happened to have the highest
''' index. It costs an ordering and buys a picture that is not a lie.
'''
''' ONE GOAL, AIMED PER SEAT. BrainGoal is a module with a single target and
''' five tanks need five. BrainPath already keeps a lane per hull, so the seat
''' change is paired with BrainPath.Aim(hull), which expresses that hull's
''' lane as the goal. The goal left standing at the end of the loop is the
''' watched hull's, which is the one drawn and the one scored.
''' </summary>
Public Class BrainCrew
    Implements IBrain

    Private ReadOnly make As Func(Of IBrain)
    Private ReadOnly kind As String
    Private ReadOnly seats As New List(Of Integer)
    Private ReadOnly minds As New List(Of IBrain)
    ''' <summary>Tick order, watched hull last. A field rather than a local so
    ''' a sixty-hertz loop is not allocating a list to hold five integers.</summary>
    Private ReadOnly order As New List(Of Integer)
    Private manned As Boolean = False

    Public Sub New(kindName As String, maker As Func(Of IBrain))
        kind = kindName
        make = maker
    End Sub

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            If seats.Count <= 1 Then Return kind
            Return seats.Count & " x " & kind
        End Get
    End Property

    ''' <summary>The hulls this crew drives, in hull order. BrainWatch cycles
    ''' this list, because the tanks worth following are the ones moving.
    '''
    ''' NOT "Seats" - VB is case-insensitive and that collides with the field
    ''' it would return.</summary>
    Public ReadOnly Property Driving As List(Of Integer)
        Get
            Return seats
        End Get
    End Property

    ''' <summary>
    ''' WHO DRIVES: everybody on the watched hull's team.
    '''
    ''' The team rather than the whole roster, because the other side is
    ''' scenery for now - ten tanks converging on two bases from both ends is
    ''' a different experiment and not the one that was asked for. Team is
    ''' read off the hull currently being watched, so the side you are looking
    ''' at is the side that drives.
    '''
    ''' `tune=crew=N` caps it, and that is the bisect handle: crew=1 is the
    ''' old single-tank behaviour exactly, so "did the crew break this" is one
    ''' argument away from an answer instead of a rebuild.
    ''' </summary>
    Private Sub enlist()
        seats.Clear()
        minds.Clear()
        manned = True
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return

        Dim watch = Math.Max(0, Math.Min(BrainRadar.HULL, BrainTanks.Bodies.Count - 1))
        Dim team = BrainTanks.Bodies(watch).team
        Dim cap = CInt(Math.Max(1.0F, BrainTune.Get_("crew", 99.0F)))

        For i = 0 To BrainTanks.Bodies.Count - 1
            If BrainTanks.Bodies(i).team <> team Then Continue For
            If seats.Count >= cap Then Exit For
            seats.Add(i)
            minds.Add(make())
        Next

        ' THE WATCHED HULL MUST BE ONE OF THE DRIVERS, or the camera sits on a
        ' parked tank and the app looks broken while four others drive off
        ' screen. A cap that excludes it moves the view rather than the cap.
        If seats.Count > 0 AndAlso Not seats.Contains(watch) Then
            LogThis("brain: crew capped at {0}, which leaves hull {1} parked - " &
                    "following hull {2} instead", cap, watch, seats(0))
            BrainRadar.HULL = seats(0)
        End If
    End Sub

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        enlist()
        Dim watch = BrainRadar.HULL
        Try
            For k = 0 To seats.Count - 1
                BrainRadar.HULL = seats(k)
                minds(k).Start(first)
            Next
        Finally
            BrainRadar.HULL = watch
        End Try

        Dim who As New Text.StringBuilder()
        For Each s In seats
            If who.Length > 0 Then who.Append(", ")
            who.Append(s)
            If s = watch Then who.Append("*")
        Next
        LogThis("brain: crew of {0} {1} brain(s) driving hull(s) {2} - the * is the " &
                "one being followed, TAB moves it. The others are driving whether " &
                "or not you are looking at them.",
                seats.Count, kind, who.ToString())
    End Sub

    Public Function Tick(inp As BrainInput) As BrainOutput Implements IBrain.Tick
        Dim n = If(inp.hulls Is Nothing, 0, inp.hulls.Length)
        Dim o = BrainOutput.ForHulls(n)
        For i = 0 To n - 1
            o.why(i) = "parked"
        Next
        If n = 0 Then Return o
        ' SELF-MANNING, the same reason GraphBrain self-starts: swapping the
        ' brain from the panel replaces the instance without anyone calling
        ' Start, and a crew with no seats would silently park the roster.
        If Not manned OrElse seats.Count = 0 Then enlist()

        Dim watch = Math.Max(0, Math.Min(BrainRadar.HULL, n - 1))

        ' WATCHED LAST - see the class header. Built explicitly rather than by
        ' a clever test inside the loop, because the ordering IS the reason
        ' the overlays show the right tank and that should be readable.
        order.Clear()
        For k = 0 To seats.Count - 1
            If seats(k) <> watch Then order.Add(k)
        Next
        For k = 0 To seats.Count - 1
            If seats(k) = watch Then order.Add(k)
        Next

        Try
            For Each k In order
                Dim s = seats(k)
                If s >= n Then Continue For
                BrainRadar.HULL = s
                BrainPath.Aim(s)
                take(o, minds(k).Tick(inp), s)
            Next
        Finally
            ' ALWAYS, even if a brain throws. BrainSim catches and halts, and
            ' a halt that left the seat on hull 3 would point the camera and
            ' every panel at the wrong tank while the owner reads the error.
            BrainRadar.HULL = watch
        End Try
        Return o
    End Function

    ''' <summary>Take one hull's row out of one brain's output. Only its own
    ''' row: a brain fills the array full-width and parks everything that is
    ''' not itself, so copying more would overwrite a crewmate's commands with
    ''' "parked" depending purely on tick order.</summary>
    Private Sub take(ByRef o As BrainOutput, one As BrainOutput, s As Integer)
        If one.throttle IsNot Nothing AndAlso s < one.throttle.Length Then o.throttle(s) = one.throttle(s)
        If one.steer IsNot Nothing AndAlso s < one.steer.Length Then o.steer(s) = one.steer(s)
        If one.target IsNot Nothing AndAlso s < one.target.Length Then o.target(s) = one.target(s)
        If one.row IsNot Nothing AndAlso s < one.row.Length Then o.row(s) = one.row(s)
        If one.why IsNot Nothing AndAlso s < one.why.Length Then o.why(s) = one.why(s)
    End Sub

    ''' <summary>Put every member's own state back, for Reset. Each mind is
    ''' asked; a mind that has no state to clear simply is not a GraphBrain.</summary>
    Public Sub ResetAll()
        For Each m In minds
            Dim g = TryCast(m, GraphBrain)
            If g IsNot Nothing Then g.ResetState()
        Next
    End Sub

End Class

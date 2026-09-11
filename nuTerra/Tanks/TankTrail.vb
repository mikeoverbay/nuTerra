Imports OpenTK.Mathematics

''' <summary>
''' The vapour a round leaves behind it.
'''
''' SPAWNED BY DISTANCE, NOT BY TIME. A round crosses several metres in a frame,
''' so emitting one puff per frame at wherever it happens to be draws a fence of
''' discrete beads whose spacing changes with the frame rate. The count comes
''' from the length of the segment flown instead - particles per METRE - so the
''' trail has the same density at any speed and on any machine.
'''
''' AND THE SEGMENT IS SWEPT. The renderer only ever observes the round at one
''' point per frame, so the particles for that frame are laid along the segment
''' it actually flew, at the centres of equal sub-intervals: k + 0.5 over n,
''' which keeps any of them from collapsing onto either endpoint and doubling up
''' with the next frame's first.
'''
''' The fade runs from birth to death with NO fade-in. A tracer is brightest at
''' the round and dims with age as the burn cools, so the smoke-style ramp-up
''' inverts it - the streak lights from the wrong end. TEPY hit both of these
''' and the notes there name them: fading only over the last part of life gave
''' "a comet", and position jitter turned the trail into parallel strands, so
''' there is none here.
''' </summary>
Public Class TankTrail

    ''' <summary>Particles one shot may leave. At the density below this is
    ''' about fifty metres at full rate; longer shots thin out rather than stop
    ''' short - see Begin.</summary>
    Public Const CAP As Integer = 256

    ''' <summary>Particles per metre at full density. Five is TEPY's number,
    ''' arrived at after fifteen piled up overdraw without reading any
    ''' tighter.</summary>
    Private Const PER_METRE As Single = 5.0F

    ' Named apart from the array it fills - VB is case blind.
    Private Structure Dot
        Public pos As Vector3
        Public age As Single
        Public alive As Boolean
    End Structure

    Private ReadOnly p(CAP - 1) As Dot
    Private n As Integer            ' how many slots have ever been used
    Private accum As Single         ' fractional particles owed
    Private perMetre As Single = PER_METRE
    Private lifeS As Single = 1.0F

    ''' <summary>True while any particle is still visible. The shot's slot is
    ''' held until this goes false, or the trail is cut off in mid air.</summary>
    Public ReadOnly Property alive As Boolean
        Get
            For i = 0 To n - 1
                If p(i).alive Then Return True
            Next
            Return False
        End Get
    End Property

    Public ReadOnly Property count As Integer
        Get
            Return n
        End Get
    End Property

    ''' <summary>
    ''' Start a fresh trail for a shot of this length.
    '''
    ''' THE DENSITY BENDS TO THE RANGE. TEPY trims the particle budget down to
    ''' what a short shot needs and clamps a long one at the buffer, which on a
    ''' fifty-slot single-tank viewer leaves a long shot's trail simply stopping
    ''' part way. With thirty tanks the budget has to be smaller, so instead of
    ''' truncating the streak this thins it: enough particles to reach the
    ''' target at five per metre when it is near, fewer per metre when it is
    ''' far. A sparse trail that gets there beats a dense one that stops.
    ''' </summary>
    Public Sub Begin(distance As Single, lifetime As Single)
        For i = 0 To n - 1
            p(i).alive = False
        Next
        n = 0
        accum = 0.0F
        lifeS = Math.Max(lifetime, 0.05F)
        perMetre = PER_METRE
        If distance > 1.0F Then
            perMetre = Math.Min(PER_METRE, (CAP - 2) / distance)
        End If
    End Sub

    ''' <summary>
    ''' Lay this frame's particles along the segment the round flew.
    ''' </summary>
    Public Sub Emit(from As Vector3, upto As Vector3)
        Dim seg = upto - from
        Dim len = seg.Length
        If len <= 0.0F Then Return

        accum += perMetre * len
        Dim want = CInt(Math.Floor(accum))
        If want <= 0 Then Return
        accum -= want
        If n + want > CAP Then want = CAP - n
        If want <= 0 Then Return

        For k = 0 To want - 1
            ' Centres of equal sub-intervals, so no spawn lands exactly on
            ' either end of the segment and doubles with its neighbour.
            Dim t = (k + 0.5F) / want
            p(n).pos = from + seg * t
            p(n).age = 0.0F
            p(n).alive = True
            n += 1
        Next
    End Sub

    Public Sub Update(dt As Single)
        For i = 0 To n - 1
            If Not p(i).alive Then Continue For
            p(i).age += dt
            If p(i).age >= lifeS Then p(i).alive = False
        Next
    End Sub

    ''' <summary>Write the live particles into a renderer's batch. Returns how
    ''' many were written.</summary>
    Public Function Collect(buf As Single(), ByRef at As Integer,
                            capacity As Integer) As Integer
        Dim written = 0
        For i = 0 To n - 1
            If Not p(i).alive Then Continue For
            If at + 8 > capacity Then Exit For
            Dim u = p(i).age / lifeS
            ' Born bright at the round, gone by the time it lands.
            Dim a = 1.0F - u
            buf(at) = p(i).pos.X
            buf(at + 1) = p(i).pos.Y
            buf(at + 2) = p(i).pos.Z
            ' Grows as it drifts apart, the way a vapour trail widens.
            buf(at + 3) = 0.16F + 0.5F * u
            buf(at + 4) = 0.62F
            buf(at + 5) = 0.60F
            buf(at + 6) = 0.58F
            buf(at + 7) = a * a * 0.55F
            at += 8
            written += 1
        Next
        Return written
    End Function
End Class

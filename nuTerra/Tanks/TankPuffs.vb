Imports OpenTK.Mathematics

''' <summary>
''' A burst of billboarded sprites: the flame at a muzzle, the smoke under it,
''' or the explosion where a round landed.
'''
''' ALL THREE ARE THE SAME THING with different numbers, which is why they are
''' one class. TEPY arrived at that too - its muzzle flash, muzzle smoke and
''' impact bursts are all ParticleSystems differing only in flipbook, count,
''' sizes, drag and blend.
'''
''' CAMERA FACING, NOT A PLUME, and that is the part worth stating because the
''' code it is ported from says it outright and I read it the wrong way round
''' first. TEPY's earlier muzzle flash was three world-aligned quads fanned
''' around the gun axis; it was REPLACED because "WoT's flash is a camera-facing
''' fireball, not a forward-pointing plume" and the fixed fan "always read as
''' flat at certain angles". What replaced it is still billboards - several soft
''' sprites, additively blended, over a wide cone, each with its own rotation -
''' so any viewing angle gets a fireball.
'''
''' The whole pool is spawned on the first frame and then nothing more is
''' emitted: a burst is one event, not a stream. Particles drift outward and are
''' dragged to a halt, which is the "explode out, then hang" shape.
''' </summary>
Public Class TankPuffs

    Private Structure Puff
        Public pos As Vector3
        Public vel As Vector3
        Public age As Single
        Public rot As Single
        Public alive As Boolean
    End Structure

    Private p() As Puff
    Private n As Integer

    Public grid As AtlasGrid
    Public lifeS As Single = 0.25F
    Public size0 As Single = 1.0F
    Public size1 As Single = 1.8F
    Public drag As Single = 5.0F
    Public tint As Vector3 = Vector3.One
    Public opacity As Single = 1.0F
    ''' <summary>Fade in over this fraction of life. Smoke needs it so the
    ''' flame can be seen through it; a flame must not have it.</summary>
    Public fadeIn As Single = 0.0F

    Public Sub New(capacity As Integer)
        ReDim p(Math.Max(capacity, 1) - 1)
    End Sub

    Public ReadOnly Property alive As Boolean
        Get
            For i = 0 To n - 1
                If p(i).alive Then Return True
            Next
            Return False
        End Get
    End Property

    ''' <summary>
    ''' Throw the whole pool outward from one point.
    '''
    ''' A WIDE CONE, not a line. The spread is a lateral push of up to
    ''' `spread` times the outward speed, so the puffs leave along the barrel
    ''' but fan far enough to read as a ball rather than a jet. TEPY's number
    ''' is 0.6, about thirty degrees.
    '''
    ''' Each gets its own rotation, because eight copies of one sprite at the
    ''' same angle read as one sprite drawn eight times.
    ''' </summary>
    Public Sub Burst(at As Vector3, dir As Vector3, speed As Single,
                     spread As Single, jitter As Single)
        Dim d = If(dir.LengthSquared > 1.0E-9F, Vector3.Normalize(dir), Vector3.UnitY)
        ' Any two axes across the cone. Cross with whichever world axis is
        ' least aligned, so a gun pointing straight up still gets a basis.
        Dim up = If(Math.Abs(d.Y) < 0.9F, Vector3.UnitY, Vector3.UnitX)
        Dim rx = Vector3.Normalize(Vector3.Cross(up, d))
        Dim ry = Vector3.Cross(d, rx)

        n = p.Length
        For i = 0 To n - 1
            Dim a = CSng(rng.NextDouble() * Math.PI * 2.0)
            Dim r = CSng(rng.NextDouble()) * spread
            Dim off = rx * CSng(Math.Cos(a)) * r + ry * CSng(Math.Sin(a)) * r
            p(i).pos = at + off * jitter
            p(i).vel = (d + off) * speed
            p(i).age = 0.0F
            p(i).rot = CSng(rng.NextDouble() * Math.PI * 2.0)
            p(i).alive = True
        Next
    End Sub

    Public Sub Update(dt As Single)
        Dim k = Math.Max(0.0F, 1.0F - drag * dt)
        For i = 0 To n - 1
            If Not p(i).alive Then Continue For
            p(i).age += dt
            If p(i).age >= lifeS Then
                p(i).alive = False
                Continue For
            End If
            p(i).pos += p(i).vel * dt
            p(i).vel *= k
        Next
    End Sub

    ''' <summary>Write the live sprites into a batch. 12 floats each: position
    ''' and radius, colour, then the frame's rectangle in the atlas.</summary>
    Public Function Collect(buf As Single(), ByRef at As Integer,
                            capacity As Integer) As Integer
        Dim written = 0
        For i = 0 To n - 1
            If Not p(i).alive Then Continue For
            If at + 12 > capacity Then Exit For
            Dim u = p(i).age / lifeS

            ' The flipbook cursor IS the particle's own age, so each sprite
            ' plays the animation from its own birth rather than every one of
            ' them showing the same frame.
            Dim uv = TankAtlas.FrameUV(grid, CInt(u * (grid.frames - 1)))

            Dim a = 1.0F - u
            If fadeIn > 0.0F AndAlso u < fadeIn Then a = a * (u / fadeIn)

            buf(at) = p(i).pos.X
            buf(at + 1) = p(i).pos.Y
            buf(at + 2) = p(i).pos.Z
            buf(at + 3) = size0 + (size1 - size0) * u
            buf(at + 4) = tint.X
            buf(at + 5) = tint.Y
            buf(at + 6) = tint.Z
            buf(at + 7) = a * opacity
            buf(at + 8) = uv.X
            buf(at + 9) = uv.Y
            buf(at + 10) = uv.Z
            buf(at + 11) = uv.W
            at += 12
            written += 1
        Next
        Return written
    End Function

    ''' <summary>Shared, and deliberately not seeded from the clock: a still
    ''' shot twice from one build has to come out the same, and a burst that
    ''' scatters differently every run breaks that.</summary>
    Private Shared ReadOnly rng As New Random(20260911)
End Class

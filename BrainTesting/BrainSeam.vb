Imports OpenTK.Mathematics

''' <summary>
''' THE SEAM. Everything else in this app exists to make this easy to write
''' against.
'''
''' The owner, 2026-09-15: "the brain will be new. I need the world". So the
''' world hands a brain numbers and takes numbers back, and nothing else
''' crosses. A brain never touches OpenGL, never opens a file, never asks what
''' time it is.
'''
''' Shape agreed with Tank AI work, who will write the first real one against
''' it. Their four corrections are in here:
'''   * hull half-extents ride on BrainHull, so no brain looks them up and gets
'''     them inconsistently;
'''   * dt is the ONLY clock - see the note on BrainInput.dt;
'''   * the PATH is not world data and is absent on purpose;
'''   * standable() is a point query with a caller-chosen radius, in BrainNav.
'''
''' Added 2026-09-16 by nuTerra work, stage 6 of docs/brain_testing_plan.md.
''' </summary>
Public Module BrainSeam
End Module

''' <summary>One hull, as a brain meets it.</summary>
Public Structure BrainHull
    Public id As Integer              ' 1..30, unique across BOTH teams
    Public team As Integer            ' 1 or 2
    Public tag As String              ' the vehicle, e.g. "R110_Object_260"

    Public pos As Vector2             ' world XZ
    Public y As Single                ' ground height under it
    Public headingRad As Single
    Public speed As Single            ' metres a second, signed: negative is reverse

    ''' <summary>
    ''' The HULL's half-extents in metres - X across, Z along - from the game's
    ''' own boundingBox via TankRoster.
    '''
    ''' HANDED OVER RATHER THAN LOOKED UP, at Tank AI work's request: ray
    ''' origins sit ON the hull edge, so every cast depends on this box, and a
    ''' Panhard and a Maus fan their rays differently. A brain that fetched it
    ''' itself would be one more place for the answer to differ.
    ''' </summary>
    Public halfX As Single
    Public halfZ As Single

    ''' <summary>
    ''' THE GUN, because a brain that cannot aim is only half a brain.
    '''
    ''' All of it is read from the vehicle's own def by TankVehicle - none is
    ''' estimated here. The owner asked for "rotations and tilt limits for the
    ''' guns and fire position"; it was already loaded and simply not handed
    ''' over, which is the worst of both - paid for and unusable.
    '''
    ''' yawMin/yawMax are DEGREES and a full -180..180 means a turret; a narrow
    ''' pair means a casemate that has to turn the hull to aim.
    ''' </summary>
    Public yawMinDeg As Single
    Public yawMaxDeg As Single
    Public yawRateDegS As Single
    Public pitchRateDegS As Single

    ''' <summary>
    ''' Pitch limits AT THE CURRENT TURRET YAW, in degrees, X low and Y high.
    '''
    ''' A RANGE PER YAW, not one pair per vehicle: real hulls block their own
    ''' gun over the engine deck, so a tank that depresses 8 degrees forward
    ''' may manage 2 astern. TankVehicle.PitchRangeAt samples the def's
    ''' pitchLimits curve; this is that sampled where the turret is pointing
    ''' now, so a brain never has to know the curve exists.
    ''' </summary>
    Public pitchLowDeg As Single
    Public pitchHighDeg As Single

    ''' <summary>Where the shot leaves, in vehicle-local metres, and whether
    ''' the model actually named it. False means the vehicle has no HP_gunFire
    ''' node and this is the barrel tip guessed from the bounding box - worth
    ''' knowing before trusting it for a line of fire.</summary>
    Public muzzleLocal As Vector3
    Public hasMuzzle As Boolean

    ''' <summary>Where the turret is pointing now, degrees.</summary>
    Public turretYawDeg As Single
    Public gunPitchDeg As Single

    ''' <summary>
    ''' Half the DIAGONAL plus a margin - the radius that answers "will this
    ''' hull fit through that gap".
    '''
    ''' Diagonal, not width, because a hull ROTATES while it drives: a
    ''' width-only test passes gaps a turning tank wedges in. Precomputed here
    ''' so the +0.3 m margin is one number in one place rather than a constant
    ''' every brain picks for itself.
    ''' </summary>
    Public ReadOnly Property FitRadius As Single
        Get
            Return CSng(Math.Sqrt(halfX * halfX + halfZ * halfZ)) + 0.3F
        End Get
    End Property
End Structure

''' <summary>What the world hands a brain, once a frame.</summary>
Public Structure BrainInput
    ''' <summary>
    ''' Seconds since the last step. THE ONLY CLOCK A BRAIN GETS.
    '''
    ''' No DateTime, no Stopwatch, no unseeded RNG anywhere a brain can reach.
    ''' Spawns are a pure function of the arena file and the slot number for
    ''' exactly this reason, and a brain that reads the wall clock throws that
    ''' away - two runs from identical spawns stop being comparable, which is
    ''' the whole purpose of this harness.
    ''' </summary>
    Public dt As Single

    ''' <summary>Steps since the sim started. Integer, so it is reproducible in
    ''' a way elapsed seconds are not.</summary>
    Public frame As Integer

    ''' <summary>Every hull, both teams, in id order. A brain drives all of
    ''' them - team is on the hull.</summary>
    Public hulls As BrainHull()
End Structure

''' <summary>What a brain gives back. Nothing else.</summary>
Public Structure BrainOutput
    ''' <summary>-1..1 per hull, indexed as BrainInput.hulls.</summary>
    Public throttle As Single()

    ''' <summary>-1..1 per hull. Positive turns right.</summary>
    Public steer As Single()

    ''' <summary>
    ''' What the CARD shows each hull heading to, and which row of its path it
    ''' is on.
    '''
    ''' THESE COME FROM THE BRAIN, not from the world working them out. That is
    ''' the entire point of the card: it shows what the brain BELIEVES. A card
    ''' displaying a value the world computed would agree with itself and prove
    ''' nothing.
    ''' </summary>
    Public target As Vector2()
    Public row As Integer()

    ''' <summary>One short phrase per hull saying WHY - "following", "blocked",
    ''' "going around". Goes straight into the black box's `why` column, which
    ''' is what makes a log readable months later.</summary>
    Public why As String()

    ''' <summary>Room for n hulls, all zero. A brain that fills nothing parks
    ''' everything, which is the safe default.</summary>
    Public Shared Function ForHulls(n As Integer) As BrainOutput
        Dim o As New BrainOutput
        ReDim o.throttle(Math.Max(n - 1, 0))
        ReDim o.steer(Math.Max(n - 1, 0))
        ReDim o.target(Math.Max(n - 1, 0))
        ReDim o.row(Math.Max(n - 1, 0))
        ReDim o.why(Math.Max(n - 1, 0))
        Return o
    End Function
End Structure

''' <summary>
''' A brain. Implement this and hand it to BrainSim.
'''
''' Deliberately tiny. Everything a brain needs about the world it asks
''' BrainNav for, and everything it decides it returns - so it can be run
''' without a window, swapped mid-run, or tested against a recorded input.
''' </summary>
Public Interface IBrain
    ''' <summary>Shown on screen and written into the log's filename, so a CSV
    ''' says which brain produced it.</summary>
    ReadOnly Property Name As String

    ''' <summary>Called once when the sim starts, with the opening state.</summary>
    Sub Start(first As BrainInput)

    ''' <summary>One step. Must not block: this is on the render thread.</summary>
    Function Tick(inp As BrainInput) As BrainOutput
End Interface

''' <summary>
''' The brain that does nothing, and the one this app ships with.
'''
''' It exists so the whole loop - input gathered, brain stepped, output
''' applied, black box written - runs and is measurable BEFORE anyone writes
''' driving code. If hulls move with this installed, the bug is in the world,
''' not in a brain.
''' </summary>
Public Class NullBrain
    Implements IBrain

    Public ReadOnly Property Name As String Implements IBrain.Name
        Get
            Return "null"
        End Get
    End Property

    Public Sub Start(first As BrainInput) Implements IBrain.Start
        LogThis("brain: NullBrain started with {0} hull(s) - nothing will move",
                If(first.hulls Is Nothing, 0, first.hulls.Length))
    End Sub

    Public Function Tick(inp As BrainInput) As BrainOutput Implements IBrain.Tick
        Dim o = BrainOutput.ForHulls(If(inp.hulls Is Nothing, 0, inp.hulls.Length))
        For i = 0 To o.why.Length - 1
            o.why(i) = "parked"
        Next
        Return o
    End Function
End Class

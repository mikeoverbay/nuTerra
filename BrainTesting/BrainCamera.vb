Imports OpenTK.Mathematics
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' A fly camera. WASD to move, arrow keys to look, Q and E for down and up,
''' Shift to go faster.
'''
''' KEYS ONLY, NO MOUSE LOOK, and that is deliberate rather than unfinished: a
''' mouse-look camera grabs the pointer, and on this machine several apps and
''' the owner share one desktop. Losing the cursor into a test harness is a
''' worse day than pressing an arrow key.
'''
''' The far plane is 4 km because a WoT map is 1.4 km across and the outland
''' ring runs past it; the near plane is 0.5 m, which at that far distance
''' leaves enough depth precision that hills do not z-fight at range.
'''
''' Added 2026-09-15 by nuTerra work.
''' </summary>
Public Class BrainCamera

    Public Position As Vector3 = New Vector3(0.0F, 300.0F, -600.0F)
    Public YawRad As Single = 0.0F
    Public PitchRad As Single = -0.45F

    Private Const NEAR_M As Single = 0.5F
    Private Const FAR_M As Single = 4000.0F
    Private Const FOV_DEG As Single = 60.0F

    Private Const WALK_MS As Single = 60.0F     ' metres a second
    Private Const RUN_MULT As Single = 6.0F
    Private Const LOOK_RATE As Single = 1.6F    ' radians a second

    Public ReadOnly Property Forward As Vector3
        Get
            Return New Vector3(
                CSng(Math.Cos(PitchRad) * Math.Sin(YawRad)),
                CSng(Math.Sin(PitchRad)),
                CSng(Math.Cos(PitchRad) * Math.Cos(YawRad)))
        End Get
    End Property

    Public ReadOnly Property Right As Vector3
        Get
            Return Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY))
        End Get
    End Property

    Public Function ViewProj(aspect As Single) As Matrix4
        Dim view = Matrix4.LookAt(Position, Position + Forward, Vector3.UnitY)
        Dim proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FOV_DEG), Math.Max(aspect, 0.1F), NEAR_M, FAR_M)
        Return view * proj
    End Function

    ''' <summary>
    ''' Frame the whole map: back off along -Z far enough that the play field
    ''' fits, and look down at it. Called once when a map finishes loading, so
    ''' the first frame shows the ground rather than whatever the default
    ''' position happened to point at.
    ''' </summary>
    Public Sub FrameMap(sizeMetres As Single)
        Dim d = Math.Max(400.0F, sizeMetres * 0.75F)
        Position = New Vector3(0.0F, d * 0.55F, -d)
        YawRad = 0.0F
        PitchRad = -0.5F
    End Sub

    ''' <summary>Stand off from a world point and look at it. The stand-off is
    ''' along -Z and up at 35 degrees, which shows a formation's depth rather
    ''' than looking straight down at its roofs.</summary>
    Public Sub LookAt(x As Single, z As Single, ground As Single, dist As Single)
        Dim h = dist * 0.7F
        Position = New Vector3(x, ground + h, z - dist)
        YawRad = 0.0F
        PitchRad = CSng(-Math.Atan2(h, dist))
    End Sub

    Public Sub Update(dt As Single, k As KeyboardState)
        Dim look = LOOK_RATE * dt
        If k.IsKeyDown(Keys.Left) Then YawRad -= look
        If k.IsKeyDown(Keys.Right) Then YawRad += look
        If k.IsKeyDown(Keys.Up) Then PitchRad += look
        If k.IsKeyDown(Keys.Down) Then PitchRad -= look

        ' Stopped just short of straight up and straight down. At exactly
        ' vertical the forward vector is parallel to UnitY and LookAt's cross
        ' product collapses, which shows as the view snapping to an arbitrary
        ' roll for one frame.
        Dim lim = CSng(Math.PI / 2.0 - 0.01)
        PitchRad = Math.Max(-lim, Math.Min(lim, PitchRad))

        Dim speed = WALK_MS * dt
        If k.IsKeyDown(Keys.LeftShift) OrElse k.IsKeyDown(Keys.RightShift) Then
            speed *= RUN_MULT
        End If

        If k.IsKeyDown(Keys.W) Then Position += Forward * speed
        If k.IsKeyDown(Keys.S) Then Position -= Forward * speed
        If k.IsKeyDown(Keys.A) Then Position -= Right * speed
        If k.IsKeyDown(Keys.D) Then Position += Right * speed
        If k.IsKeyDown(Keys.E) Then Position += Vector3.UnitY * speed
        If k.IsKeyDown(Keys.Q) Then Position -= Vector3.UnitY * speed
    End Sub

End Class

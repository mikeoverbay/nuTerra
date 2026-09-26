Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' THE TURRET AND THE GUN: where they should point, and when to fire.
'''
''' "add turret and gun rotations per nuTerra info. Have turret and gun aim at
''' the other tank. I also want to be able to fire with and have animated shots
''' and impacts as in nuTerra." - the owner, 2026-09-25.
'''
''' THE DATA WAS ALREADY THERE AND NOTHING READ IT. Every hull arrives carrying
''' yawMin, yawMax, yawRateDegS, pitchRateDegS, pitchLowDeg, pitchHighDeg,
''' muzzleLocal and hasMuzzle, read from the vehicle's own definition by
''' TankVehicle. Body has carried turretYawDeg and gunPitchDeg since it was
''' written, with the comment "The brain moves these; the world only carries
''' them" - and no brain ever did. This is the half that moves them.
'''
''' NOT ON THE BOARD, AND THAT IS DELIBERATE. The node graph decides DRIVING,
''' and its priorities exist so that exactly one rung acts per tick. A gun does
''' not compete with a steering decision: a real tank lays its gun while it
''' drives, and a turret that only tracked on ticks the driver had nothing to
''' say would stutter for reasons that have nothing to do with aiming. So this
''' runs every tick beside the board rather than inside it, and writes its own
''' two fields of the output.
'''
''' AT THE DECLARED RATES, clamped to the declared arc. The two hulls a monastery
''' run loads declare 50.0 and 54.0 deg/s of hull traverse; the turret and gun
''' rates come from the same definitions. Slewing instantly would hide the one
''' thing worth watching - whether a tank can bring its gun round before the
''' other one does.
''' </summary>
Module BrainGun

    ''' <summary>One shot in flight or fading. Kept in world terms so the draw
    ''' needs nothing from the tank that fired it - a tank may have moved,
    ''' turned, or been given a different target by the time its shot lands.</summary>
    Private Structure Shot
        Public from_ As Vector3
        Public to_ As Vector3
        Public age As Single
        Public hit As Boolean
    End Structure

    Private ReadOnly shots As New List(Of Shot)
    ''' <summary>Seconds until each hull may fire again. Per hull, because a
    ''' crew of five firing off one clock would volley.</summary>
    Private ReadOnly reload_ As New Dictionary(Of Integer, Single)

    Public Fired As Integer = 0

    Private shader As BrainShader
    Private vao, vbo, verts As Integer

    ''' <summary>Placeholder tracer lifetime. nuTerra work is bringing the real
    ''' animated shot and impact from the nuTerra source; this exists so the
    ''' AIMING can be tested and watched before those land, and should be the
    ''' first thing deleted when they do.</summary>
    Private Const LIFE As Single = 0.45F

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    ''' <summary>
    ''' WHERE THIS HULL'S GUN SHOULD POINT, hull-relative degrees.
    '''
    ''' False when there is nothing to aim at, so the caller leaves the turret
    ''' where it is rather than snapping it to dead ahead - a turret that
    ''' returns to centre every time it loses sight is a turret that is never
    ''' laid when it matters.
    '''
    ''' THE NEAREST OTHER HULL, either team. The owner's test is two tanks of
    ''' the SAME team aiming at each other, so an enemies-only rule would have
    ''' nothing to do. Team is reported so a later rule can prefer one.
    ''' </summary>
    Public Function Aim(me_ As Integer, ByRef tDeg As Single, ByRef gDeg As Single,
                        ByRef atHull As Integer) As Boolean
        atHull = -1
        If BrainTanks.Bodies Is Nothing Then Return False
        If me_ < 0 OrElse me_ >= BrainTanks.Bodies.Count Then Return False
        Dim mine = BrainTanks.Bodies(me_)

        Dim best = Single.MaxValue
        For i = 0 To BrainTanks.Bodies.Count - 1
            If i = me_ Then Continue For
            Dim d = (BrainTanks.Bodies(i).spawn - mine.spawn).LengthSquared
            If d < best Then
                best = d
                atHull = i
            End If
        Next
        If atHull < 0 Then Return False

        Dim tgt = BrainTanks.Bodies(atHull)
        Dim eye = muzzle(mine)
        ' CENTRE OF THE HULL BOX, not the ground under it. Aiming at the
        ' contact point would put every shot into the dirt at the target's
        ' tracks, and half the box height is the honest middle of what there is
        ' to hit.
        Dim aimAt = New Vector3(tgt.spawn.X, tgt.y + tgt.half.Y, tgt.spawn.Y)

        Dim flat = New Vector2(aimAt.X - eye.X, aimAt.Z - eye.Z)
        tDeg = MathHelper.RadiansToDegrees(
            wrap180r(CSng(Math.Atan2(flat.X, flat.Y)) - mine.headingRad))
        Dim run = Math.Max(0.001F, flat.Length)
        gDeg = MathHelper.RadiansToDegrees(CSng(Math.Atan2(aimAt.Y - eye.Y, run)))
        Return True
    End Function

    ''' <summary>
    ''' Where this hull's gun muzzle is, in the world.
    '''
    ''' APPROXIMATED FOR NOW, and flagged rather than hidden. BrainHull carries
    ''' muzzleLocal from the vehicle definition, but which frame it is expressed
    ''' in - hull, turret or gun - is nuTerra's own knowledge and I have asked
    ''' for it rather than guessed. Until the answer comes back this is the hull
    ''' box's own geometry: centred, and high enough up the box to be a turret
    ''' roof rather than a track.
    '''
    ''' The error this can carry is a metre of height over a shot of tens of
    ''' metres, which moves the pitch by about a degree. Worth correcting, not
    ''' worth blocking on.
    ''' </summary>
    Private Function muzzle(b As BrainTanks.Body) As Vector3
        Return New Vector3(b.spawn.X,
                           b.y + b.half.Y * BrainTune.Get_("gunh", 1.8F),
                           b.spawn.Y)
    End Function

    ''' <summary>
    ''' LAID AND LOADED? Fires if so, and reports it.
    '''
    ''' Both halves have to be true and they fail for different reasons: a gun
    ''' that is still slewing has a lay error, a gun that has just fired has a
    ''' clock. Reporting them together would make a tank that never fires
    ''' impossible to diagnose.
    ''' </summary>
    Public Function MayFire(me_ As Integer, tWant As Single, gWant As Single) As Boolean
        If BrainTanks.Bodies Is Nothing OrElse me_ < 0 OrElse
           me_ >= BrainTanks.Bodies.Count Then Return False
        Dim b = BrainTanks.Bodies(me_)
        Dim tol = BrainTune.Get_("laydeg", 1.5F)
        If Math.Abs(wrap180(tWant - b.turretYawDeg)) > tol Then Return False
        If Math.Abs(gWant - b.gunPitchDeg) > tol Then Return False
        Dim left_ = 0.0F
        reload_.TryGetValue(me_, left_)
        Return left_ <= 0.0F
    End Function

    ''' <summary>
    ''' FIRE. Records the shot, starts the reload, and says so once.
    '''
    ''' The impact point comes from BrainMates, the same ray-against-hull-box
    ''' the radar uses - so a shot lands on the target's actual box rather than
    ''' at its centre, and a shot that misses because the turret was still
    ''' swinging lands on whatever it really hit.
    ''' </summary>
    Public Sub Fire(me_ As Integer, atHull As Integer)
        If BrainTanks.Bodies Is Nothing OrElse me_ >= BrainTanks.Bodies.Count Then Return
        Dim b = BrainTanks.Bodies(me_)
        Dim eye = muzzle(b)

        ' The line the gun is actually pointing along - turret yaw is
        ' hull-relative, so the world bearing is the sum.
        Dim wy = b.headingRad + MathHelper.DegreesToRadians(b.turretYawDeg)
        Dim wp = MathHelper.DegreesToRadians(b.gunPitchDeg)
        Dim flat = CSng(Math.Cos(wp))
        Dim dir = New Vector3(CSng(Math.Sin(wy)) * flat, CSng(Math.Sin(wp)),
                              CSng(Math.Cos(wy)) * flat)

        Dim range = BrainTune.Get_("gunrange", 400.0F)
        Dim who = -1, team = 0
        Dim d2 = New Vector2(dir.X, dir.Z)
        Dim flatLen = Math.Max(0.0001F, d2.Length)
        d2 /= flatLen
        Dim t = BrainMates.Along(me_, New Vector2(eye.X, eye.Z), d2, range, who, team)

        Dim landed = (t >= 0.0F)
        Dim dist = If(landed, t / flatLen, range)
        Dim endPt = eye + dir * dist

        shots.Add(New Shot With {.from_ = eye, .to_ = endPt, .age = 0.0F, .hit = landed})
        reload_(me_) = BrainTune.Get_("reload", 3.0F)
        Fired += 1
        If Fired <= 8 Then
            LogThis("brain: hull {0} FIRES at hull {1} - turret {2:0.0} deg, gun {3:0.0} " &
                    "deg, {4}{5} at {6:0.0} m",
                    me_, atHull, b.turretYawDeg, b.gunPitchDeg,
                    If(landed, "HIT hull ", "missed - nothing within "),
                    If(landed, who.ToString(), BrainTune.Get_("gunrange", 400.0F).ToString("0")),
                    dist)
        End If
    End Sub

    ''' <summary>Age the tracers and run the reload clocks down. Called once a
    ''' tick from the sim, with the same clamped dt the hulls move on so a shot
    ''' and the tank that fired it agree about how long a second is.</summary>
    Public Sub Tick(dt As Single)
        For i = shots.Count - 1 To 0 Step -1
            Dim s = shots(i)
            s.age += dt
            If s.age > LIFE Then
                shots.RemoveAt(i)
            Else
                shots(i) = s
            End If
        Next
        verts = 0
        Dim keys = New List(Of Integer)(reload_.Keys)
        For Each k In keys
            reload_(k) = Math.Max(0.0F, reload_(k) - dt)
        Next
    End Sub

    Public Sub Reset()
        shots.Clear()
        reload_.Clear()
        Fired = 0
        verts = 0
    End Sub

    ''' <summary>
    ''' PLACEHOLDER TRACERS. A line from muzzle to impact, and a cross at the
    ''' impact when it hit something.
    '''
    ''' Deliberately plain: nuTerra work is bringing the real animated shot and
    ''' impact from the nuTerra source, and this exists only so the aiming can be
    ''' watched before then. When theirs lands, this whole routine goes - which
    ''' is why it draws with the line shader every other overlay already uses and
    ''' owns no textures, no flipbooks and no particle state to unpick.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4)
        If shots.Count = 0 Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If verts = 0 Then build()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", New Vector3(1.0F, 0.85F, 0.35F))
        GL.LineWidth(2.5F)
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
    End Sub

    Private Sub build()
        Dim v As New List(Of Single)
        For Each s In shots
            seg(v, s.from_, s.to_)
            If Not s.hit Then Continue For
            ' A cross at the impact, sized so it reads from the 300 m zoom the
            ' app is usually driven at.
            Dim r = 2.0F
            seg(v, s.to_ - New Vector3(r, 0.0F, 0.0F), s.to_ + New Vector3(r, 0.0F, 0.0F))
            seg(v, s.to_ - New Vector3(0.0F, 0.0F, r), s.to_ + New Vector3(0.0F, 0.0F, r))
            seg(v, s.to_ - New Vector3(0.0F, r, 0.0F), s.to_ + New Vector3(0.0F, r, 0.0F))
        Next
        Dim arr = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr, BufferUsageHint.StreamDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        verts = arr.Length \ 3
    End Sub

    Private Sub seg(v As List(Of Single), a As Vector3, b As Vector3)
        v.Add(a.X) : v.Add(a.Y) : v.Add(a.Z)
        v.Add(b.X) : v.Add(b.Y) : v.Add(b.Z)
    End Sub

    ''' <summary>Degrees into -180..180.</summary>
    Public Function wrap180(a As Single) As Single
        Dim x = a
        While x > 180.0F
            x -= 360.0F
        End While
        While x < -180.0F
            x += 360.0F
        End While
        Return x
    End Function

    Private Function wrap180r(a As Single) As Single
        Dim x = a
        While x > Math.PI
            x -= CSng(Math.PI * 2.0)
        End While
        While x < -Math.PI
            x += CSng(Math.PI * 2.0)
        End While
        Return x
    End Function

End Module

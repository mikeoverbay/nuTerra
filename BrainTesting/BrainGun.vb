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
        ''' <summary>Metres to the impact, and metres a second the shell covers.
        ''' Per shot, because the guns on one roster range 1115 to 1400 m/s and a
        ''' shared constant would make every tracer the same lie.</summary>
        Public dist As Single
        Public speed As Single
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

    ''' <summary>How long an impact mark stays after the shell arrives. The
    ''' flight itself is timed by the shell, not by this.</summary>
    Private Const DWELL As Single = 0.35F

    ''' <summary>Length of the visible streak behind the shell, metres. A shell
    ''' is not 12 m long; this is the eye's persistence, which is what a tracer
    ''' actually is.</summary>
    Private Const TRACER As Single = 12.0F

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
        Dim eye As Vector3 = Vector3.Zero, look As Vector3 = Vector3.Zero
        barrel(me_, mine, eye, look)
        ' CENTRE OF THE HULL BOX, not the ground under it. Aiming at the
        ' contact point would put every shot into the dirt at the target's
        ' tracks, and half the box height is the honest middle of what there is
        ' to hit.
        Dim aimAt = New Vector3(tgt.spawn.X, tgt.y + tgt.half.Y, tgt.spawn.Y)

        ' ---- INTO THE VEHICLE'S FRAME, WHICH IS MIRRORED -------------------
        '
        ' TankRoster.MirrorX is True: "BigWorld's X runs opposite to the world
        ' frame every consumer uses", so the hull's world matrix carries a -1 X
        ' scale and a local yaw of theta DRAWS as -theta in the world.
        '
        ' turretYawDeg lives in the VEHICLE's frame, because that is the frame
        ' PartLocal composes in and the frame yawMin/yawMax and PitchRangeAt are
        ' declared in. So a world-relative bearing has to be flipped on the way
        ' in. Without this the turret points at the MIRROR of the target - and it
        ' did, invisibly, because the shot direction was derived from the same
        ' wrong assumption as the aim, so the two agreed and every shot "hit".
        ' Measuring the real barrel is what exposed it: every shot missed.
        '
        ' PITCH IS NOT AFFECTED. Mirroring X leaves the YZ plane alone, and a
        ' rotation about X lives entirely in YZ, so the gun's elevation sign
        ' survives the reflection unchanged.
        Dim mirror = If(TankRoster.MirrorX, -1.0F, 1.0F)
        Dim flat = New Vector2(aimAt.X - eye.X, aimAt.Z - eye.Z)
        tDeg = mirror * MathHelper.RadiansToDegrees(
            wrap180r(CSng(Math.Atan2(flat.X, flat.Y)) - mine.headingRad))
        Dim run = Math.Max(0.001F, flat.Length)
        gDeg = MathHelper.RadiansToDegrees(CSng(Math.Atan2(aimAt.Y - eye.Y, run)))
        Return True
    End Function

    ''' <summary>
    ''' WHERE THE BARREL ENDS AND WHICH WAY IT POINTS, both measured.
    '''
    ''' TankBarrel.MeasureMuzzle walks the barrel bone's own vertices through the
    ''' very matrix the gun is drawn with, so the shot leaves exactly where the
    ''' barrel appears. nuTerra work's argument for why this cannot be
    ''' approximated is the right one: guns differ in length and are not all
    ''' authored down the same axis, so any assumed barrel length misses, and the
    ''' sign of a picked axis is a coin flip per vehicle.
    '''
    ''' THE DIRECTION IS THE BETTER HALF. It comes back as the muzzle minus the
    ''' mid-barrel hub - a difference of two MEASURED points - rather than an
    ''' axis someone chose a sign for. It arrives un-normalised, so it is
    ''' normalised here.
    '''
    ''' FALSE IS A REAL ANSWER and must be tested: a gun with no measurable
    ''' barrel bone leaves both outputs untouched. They are seeded with the
    ''' fallback FIRST - hull-box height, direction from the commanded angles -
    ''' so a refusal degrades to the old approximation rather than firing from
    ''' an uninitialised vector.
    ''' </summary>
    Private Function barrel(hull As Integer, b As BrainTanks.Body,
                            ByRef from_ As Vector3, ByRef dir As Vector3) As Boolean
        ' ---- THE FALLBACK, SEEDED BEFORE ANYTHING CAN DECLINE ---------------
        from_ = New Vector3(b.spawn.X,
                            b.y + b.half.Y * BrainTune.Get_("gunh", 1.8F),
                            b.spawn.Y)
        ' Back OUT of the vehicle's mirrored frame for a world direction - see
        ' the note in Aim. Only reached when a gun has no measurable barrel.
        Dim mirror = If(TankRoster.MirrorX, -1.0F, 1.0F)
        Dim wy = b.headingRad + MathHelper.DegreesToRadians(mirror * b.turretYawDeg)
        Dim wp = MathHelper.DegreesToRadians(b.gunPitchDeg)
        Dim flat = CSng(Math.Cos(wp))
        dir = New Vector3(CSng(Math.Sin(wy)) * flat, CSng(Math.Sin(wp)),
                          CSng(Math.Cos(wy)) * flat)

        If b.vehicle Is Nothing Then Return False
        Dim g = b.vehicle.GunPart
        If g Is Nothing Then Return False

        ' The gun's own draw matrix. PartLocal composes the gun through the
        ' TURRET's pivot, so this is the barrel where it actually is, not where
        ' a second derivation would put it.
        Dim m = b.vehicle.PartLocal(g, b.turretYawDeg, b.gunPitchDeg) *
                BrainTanks.WorldOf(b)

        Dim mz As Vector3 = Vector3.Zero
        Dim md As Vector3 = Vector3.Zero
        ' MuzzleFor, NOT MeasureMuzzle. nuTerra work's own correction, and it
        ' matters on every tank on this roster: a gun whose visual names
        ' HP_gunFire carries the game's own muzzle point and it WINS - measuring
        ' vertices is only the fallback for a gun without one. All four tanks
        ' they loaded name it, so calling MeasureMuzzle directly took the
        ' fallback path for every gun and never once used the real data.
        '
        ' The two paths also differ in a way that would have been silent: the
        ' named node goes through the UNFLIPPED matrix, because node positions
        ' live in the visual's frame while only vertex streams are Z-reversed.
        ' MuzzleFor keeps that straight; picking a path by hand would not have.
        If Not TankBarrel.MuzzleFor(b.vehicle, g, m, mz, md) Then
            say_barrel(hull, b, False)
            Return False
        End If
        If md.LengthSquared < 0.000001F Then
            say_barrel(hull, b, False)
            Return False
        End If

        from_ = mz
        dir = Vector3.Normalize(md)
        say_barrel(hull, b, True)
        Return True
    End Function

    ''' <summary>Once per hull, because "this vehicle has no measurable barrel"
    ''' is a fact about the roster worth knowing and a fact about every frame
    ''' worth knowing once.</summary>
    Private Sub say_barrel(hull As Integer, b As BrainTanks.Body, ok As Boolean)
        If saidBarrel.Contains(hull) Then Return
        saidBarrel.Add(hull)
        If ok Then
            ' THE GUN, AS THE GAME STATES IT. Printed once per hull because it is
            ' a fact about the roster, and printed at all because these numbers
            ' were placeholders until nuTerra work read the real ones: shell
            ' speed was one global 180 m/s for every gun, recoil travel a fixed
            ' 0.40 m for every barrel.
            Dim v = b.vehicle
            If v Is Nothing Then
                LogThis("brain: hull {0} ({1}) - muzzle found", hull, b.tag)
            Else
                LogThis("brain: hull {0} ({1}) - muzzle found. {2} at {3:0} m/s, " &
                        "pierce {4:0}, reach {5:0} m, recoil {6:0.00} m{7}",
                        hull, b.tag,
                        If(v.shellName = "", "shell", v.shellName),
                        v.shellSpeedMps, v.shellPiercing, v.shellMaxDistanceM,
                        v.recoilTravelM,
                        If(v.muzzleBrake = "", "", ", brake " & v.muzzleBrake))
            End If
        Else
            LogThis("brain: hull {0} ({1}) - no measurable barrel bone, falling back to " &
                    "the hull box and the commanded angles", hull, b.tag)
        End If
    End Sub

    Private ReadOnly saidBarrel As New HashSet(Of Integer)

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

        ' MEASURED, not derived. The muzzle and the line both come off the
        ' barrel bone through the gun's own draw matrix, so the shot leaves where
        ' the barrel appears and travels the way it is pointing - no assumed
        ' length, no picked axis sign.
        Dim eye As Vector3 = Vector3.Zero, dir As Vector3 = Vector3.Zero
        barrel(me_, b, eye, dir)

        ' THE SHELL'S OWN MAX DISTANCE when it states one - 720 m on every
        ' shell measured - rather than a number I picked. tune=gunrange= still
        ' overrides, for keeping a test inside the visible area.
        Dim range = BrainTune.Get_("gunrange", 0.0F)
        If range <= 0.0F Then
            range = If(b.vehicle Is Nothing OrElse b.vehicle.shellMaxDistanceM <= 1.0F,
                       400.0F, b.vehicle.shellMaxDistanceM)
        End If
        Dim who = -1, team = 0
        Dim d2 = New Vector2(dir.X, dir.Z)
        Dim flatLen = Math.Max(0.0001F, d2.Length)
        d2 /= flatLen
        Dim t = BrainMates.Along(me_, New Vector2(eye.X, eye.Z), d2, range, who, team)

        Dim landed = (t >= 0.0F)
        Dim dist = If(landed, t / flatLen, range)
        Dim endPt = eye + dir * dist

        ' THE GUN'S OWN SHELL SPEED. It was a single global 180 m/s for every
        ' gun in nuTerra until nuTerra work read the real ones off the shells:
        ' 1115 to 1400 m/s on this roster, so anything built on 180 was out by
        ' six to eight times. Nothing here used it before - the shot was
        ' instantaneous - which is why the error never showed and why using it
        ' now is what makes the shot ANIMATED rather than a line that appears.
        Dim mps = 0.0F
        If b.vehicle IsNot Nothing Then mps = b.vehicle.shellSpeedMps
        If mps <= 1.0F Then mps = BrainTune.Get_("shellmps", 1200.0F)

        shots.Add(New Shot With {.from_ = eye, .to_ = endPt, .dist = dist,
                                 .speed = mps, .age = 0.0F, .hit = landed})
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
            ' FLIGHT TIME PLUS DWELL, not a fixed life. A 4 m point-blank shot
            ' and a 400 m one are 0.003 s and 0.33 s of flight at 1200 m/s, and
            ' giving both the same lifetime would either strobe the near one or
            ' delete the far one before it arrived.
            If s.age > (s.dist / Math.Max(1.0F, s.speed)) + DWELL Then
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
            Dim run = Math.Max(0.0001F, s.dist)
            Dim u = (s.to_ - s.from_) / run
            Dim head = Math.Min(s.dist, s.age * s.speed)

            If head < s.dist Then
                ' IN FLIGHT. A streak from where the eye still remembers the
                ' shell to where it is now, travelling at the gun's own muzzle
                ' velocity - so a 400 m shot visibly crosses the ground and a
                ' point-blank one is over before the frame ends, which is the
                ' truth about tank gunnery.
                Dim tail = Math.Max(0.0F, head - TRACER)
                seg(v, s.from_ + u * tail, s.from_ + u * head)
                Continue For
            End If

            ' ARRIVED. The cross marks where, sized to read from the 300 m zoom
            ' this app is usually driven at.
            If Not s.hit Then Continue For
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

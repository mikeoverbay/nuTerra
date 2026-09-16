Imports OpenTK.Mathematics
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' nuTerra's mouse camera, by way of Exporter Studio's transcription of it.
'''
'''     left drag            orbit      - velocity chases the mouse and coasts
'''     middle / ctrl drag   pan        - in world axes, rotated by the yaw
'''     shift drag           height     - raises and lowers the look-at point
'''     right drag           zoom       - exponential, radius-scaled
'''     wheel                zoom
'''     W A S D              walk the look-at point over the map
'''     Q E                  lower / raise it
'''
''' REPLACED A FIRST-PERSON CAMERA THAT GRABBED THE CURSOR. The owner:
''' "mouse is a mess. copy Exporter Studios method". It was - a fly camera
''' that captures the pointer is wrong for a viewer, and it is not what
''' anything else in this project does. An ORBIT is: nuTerra flies this way,
''' Exporter Studio transcribed it from nuTerra, and now so has this. The
''' cursor is never grabbed at any point.
'''
''' Two things here are load-bearing and were not obvious from the feel -
''' Exporter Studio's notes, kept because they cost someone the finding:
'''
''' * THE DAMPING FACTOR IS dt-CORRECTED. nuTerra's own comment says a fixed
'''   per-frame factor is what killed its first two attempts - above 200 fps
'''   the pool drains before the coast can be felt at all.
''' * EVERY POOL GETS A REST SNAP. Exponential decay never reaches zero, so
'''   without one the camera crawls sub-pixel for seconds after release.
'''
''' WASD is this app's own addition and does not exist in either of the
''' others. A tank map is 1.4 km across and the thing being looked at moves;
''' orbit and pan alone make crossing it tedious.
'''
''' Rewritten 2026-09-16 by nuTerra work.
''' </summary>
Public Class BrainCamera

    ''' <summary>The look-at point - nuTerra's LOOK_AT_*.</summary>
    Public Target As Vector3 = Vector3.Zero
    Public YawRad As Single = 0.7F
    ''' <summary>nuTerra's sign: a NEGATIVE pitch puts the camera above the
    ''' target. See the eye maths in ViewProj.</summary>
    Public PitchRad As Single = -0.45F
    Public Dist As Single = 400.0F

    Private Const ROT_DAMPING As Single = 0.1F       ' nuTerra's slider default
    Private Const MOUSE_SPEED As Single = 1.0F
    Private Const PITCH_MIN As Single = -1.5697963F  ' -PI/2 + 0.001, as nuTerra clamps
    Private Const PITCH_MAX As Single = 1.3F
    Private Const FOV_DEG As Single = 60.0F

    ' The pools. A drag adds to these; the damping below drains them, which is
    ' what makes the view coast after the button comes up.
    Private rotDeltaX, rotDeltaY As Single
    Private panDeltaX, panDeltaZ As Single
    Private zoomDelta As Single
    Private dragging As Boolean = False

    ''' <summary>Metres a second for the keys. Scaled by distance, so walking
    ''' feels the same zoomed in on one hull and zoomed out over the map.</summary>
    Private ReadOnly Property WalkRate As Single
        Get
            Return Math.Max(20.0F, Dist * 0.9F)
        End Get
    End Property

    Public Function ViewProj(aspect As Single) As Matrix4
        ' nuTerra's eye maths, sign for sign. The -dist on Y is why a negative
        ' pitch is above the target rather than below it.
        Dim eye = Target + New Vector3(
            CSng(Math.Cos(PitchRad) * Math.Sin(YawRad)) * Dist,
            CSng(Math.Sin(PitchRad)) * -Dist,
            CSng(Math.Cos(PitchRad) * Math.Cos(YawRad)) * Dist)

        Dim view = Matrix4.LookAt(eye, Target, Vector3.UnitY)
        ' Near and far follow the radius: a 1.4 km map seen from 2 km needs a
        ' far plane that reaches, and a hull inspected from 5 m needs a near
        ' plane that does not clip its track.
        Dim proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FOV_DEG), Math.Max(aspect, 0.1F),
            Math.Max(Dist * 0.001F, 0.05F), Dist * 10.0F + 4000.0F)
        Return view * proj
    End Function

    ''' <summary>Frame the whole map: look at its middle from far enough out.</summary>
    Public Sub FrameMap(sizeMetres As Single)
        Target = New Vector3(0.0F, 0.0F, 0.0F)
        Dist = Math.Max(400.0F, sizeMetres * 0.8F)
        YawRad = 0.0F
        PitchRad = -0.5F
    End Sub

    ''' <summary>
    ''' Look at one spot from `standoff` metres away.
    '''
    ''' THE PARAMETER IS NOT CALLED `dist`, AND THAT IS NOT STYLE. VB is case
    ''' insensitive, so a parameter named `dist` IS the field `Dist` - and
    ''' `Dist = Math.Max(5.0F, dist)` then assigns the parameter to itself and
    ''' leaves the field untouched. It cost most of an hour: `at=` placed the
    ''' camera correctly, the log printed the right number (it was reading the
    ''' parameter too), and the view stayed framed on the whole map. Every
    ''' piece of evidence agreed and all of it was about the parameter.
    '''
    ''' Second time today in this file - `speed` shadowed `Speed` the same way.
    ''' </summary>
    Public Sub LookAt(x As Single, z As Single, ground As Single, standoff As Single)
        Target = New Vector3(x, ground, z)
        Dist = Math.Max(5.0F, standoff)
        ' STAND OUTSIDE AND LOOK IN. A fixed yaw puts the eye on whichever side
        ' the number happened to pick, and for a base at the south edge that is
        ' off the map looking further off it. Placing the eye on the far side
        ' from the map centre means `at=` always looks back across the ground
        ' the spot sits on.
        YawRad = CSng(Math.Atan2(x, z))
        PitchRad = -0.45F
        LogThis("brain: camera at ({0:0.0}, {1:0.0}) ground {2:0.0}, dist {3:0.0}",
                x, z, ground, Dist)
    End Sub

    ''' <summary>
    ''' One frame of camera input. dt is this loop's own, clamped the way
    ''' nuTerra clamps it.
    ''' </summary>
    Public Sub Update(dt As Single, m As MouseState, k As KeyboardState)
        dt = Math.Clamp(dt, 0.000001F, 0.1F)

        ' 100 px of travel is about MOUSE_SPEED radians - nuTerra's scale.
        Dim dx = m.Delta.X / 100.0F * MOUSE_SPEED
        Dim dy = m.Delta.Y / 100.0F * MOUSE_SPEED

        ' Distance changes speed. nuTerra uses 0.2 * VIEW_RADIUS, negative
        ' there; Dist is positive here, so the sign comes out.
        Dim ms = 0.2F * Dist

        Dim leftDown = m.IsButtonDown(MouseButton.Left)
        Dim midDown = m.IsButtonDown(MouseButton.Middle)
        Dim rightDown = m.IsButtonDown(MouseButton.Right)
        Dim ctrl = k.IsKeyDown(Keys.LeftControl) OrElse k.IsKeyDown(Keys.RightControl)
        Dim shiftHeld = k.IsKeyDown(Keys.LeftShift) OrElse k.IsKeyDown(Keys.RightShift)
        ' IGNORE THE FRAME A BUTTON GOES DOWN. Delta carries the travel since
        ' the last update, which can be a long way if the cursor was moved
        ' elsewhere first, and that arrives as one jump.
        '
        ' THE GUARD COVERS THE RIGHT BUTTON TOO, and leaving it out was a real
        ' bug: orbit and pan were protected, zoom was not, so the first frame's
        ' jump went straight into the zoom pool. `at=` looked broken because of
        ' it - the camera was placed correctly at 60 m and had zoomed itself to
        ' 1,040 by the third frame, which reads as "the argument was ignored".
        ' By hand it is a right-drag that leaps on first press.
        Dim anyDown = leftDown OrElse midDown OrElse rightDown
        If anyDown AndAlso Not dragging Then
            dragging = True
            dx = 0 : dy = 0
        ElseIf Not anyDown Then
            dragging = False
        End If

        ' Orbit and pan are the LEFT and MIDDLE buttons only - right belongs to
        ' zoom below, so it must not be in here or a right-drag would orbit.
        Dim held = leftDown OrElse midDown

        If held Then
            If shiftHeld Then
                ' Height, applied directly - nuTerra does not pool this one.
                Target.Y -= dy * ms
            ElseIf midDown OrElse ctrl Then
                Dim ca = CSng(Math.Cos(YawRad))
                Dim sa = CSng(Math.Sin(YawRad))
                panDeltaX -= (dx * ms) * ca + (dy * ms) * sa
                panDeltaZ -= (dx * ms) * -sa + (dy * ms) * ca
            Else
                rotDeltaX -= dx
                rotDeltaY -= dy
            End If
        ElseIf rightDown Then
            ' Right drag zooms, at nuTerra's 12 * 0.2 sensitivity.
            zoomDelta += dy * 12.0F * 0.2F
        End If

        If m.ScrollDelta.Y <> 0 Then zoomDelta -= m.ScrollDelta.Y * 0.25F

        ' THE dt CORRECTION. A flat per-frame factor makes the coast vanish at
        ' high frame rates - the pool drains in three frames at 300 fps and in
        ' twenty at 50.
        Dim f = 1.0F - CSng(Math.Pow(1.0F - Math.Min(ROT_DAMPING, 0.999F), dt * 60.0F))

        YawRad += rotDeltaX * f
        PitchRad = Math.Clamp(PitchRad + rotDeltaY * f, PITCH_MIN, PITCH_MAX)
        If YawRad > CSng(Math.PI * 2) Then YawRad -= CSng(Math.PI * 2)
        If YawRad < 0 Then YawRad += CSng(Math.PI * 2)
        rotDeltaX *= (1.0F - f)
        rotDeltaY *= (1.0F - f)
        If Math.Abs(rotDeltaX) < 0.0005F Then rotDeltaX = 0
        If Math.Abs(rotDeltaY) < 0.0005F Then rotDeltaY = 0

        If zoomDelta <> 0 Then
            Dim d = Dist * CSng(Math.Exp(zoomDelta * f))
            ' Hitting a clamp kills the pending delta so it cannot grind
            ' against the limit - nuTerra does the same at both ends.
            If d > 6000.0F Then
                d = 6000.0F : zoomDelta = 0
            ElseIf d < 3.0F Then
                d = 3.0F : zoomDelta = 0
            End If
            Dist = d
            zoomDelta *= (1.0F - f)
            If Math.Abs(zoomDelta) < 0.00001F Then zoomDelta = 0
        End If

        If panDeltaX <> 0 OrElse panDeltaZ <> 0 Then
            Target.X += panDeltaX * f
            Target.Z += panDeltaZ * f
            panDeltaX *= (1.0F - f)
            panDeltaZ *= (1.0F - f)
            If Math.Abs(panDeltaX) < 0.0001F Then panDeltaX = 0
            If Math.Abs(panDeltaZ) < 0.0001F Then panDeltaZ = 0
        End If

        ' ---- the keys, this app's own ----------------------------------
        ' They move the LOOK-AT POINT, not an eye: with an orbit camera there
        ' is no eye to move, and walking the target is what "go over there"
        ' means when the view is anchored to a spot.
        Dim step_m = WalkRate * dt
        If shiftHeld Then step_m *= 4.0F
        Dim fwd As New Vector3(CSng(Math.Sin(YawRad)), 0.0F, CSng(Math.Cos(YawRad)))
        Dim rgt As New Vector3(CSng(Math.Cos(YawRad)), 0.0F, -CSng(Math.Sin(YawRad)))

        ' Forward is AWAY from the camera, which is -fwd: the eye sits at
        ' +fwd * dist from the target, so pressing W has to close that gap.
        If k.IsKeyDown(Keys.W) Then Target -= fwd * step_m
        If k.IsKeyDown(Keys.S) Then Target += fwd * step_m
        If k.IsKeyDown(Keys.A) Then Target -= rgt * step_m
        If k.IsKeyDown(Keys.D) Then Target += rgt * step_m
        If k.IsKeyDown(Keys.E) Then Target.Y += step_m
        If k.IsKeyDown(Keys.Q) Then Target.Y -= step_m
    End Sub

End Class

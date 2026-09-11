Imports OpenTK.Mathematics

''' <summary>
''' One firing event, with everything its animation needs carried on it.
'''
''' CAPTURED AT FIRE TIME, not read back from the gun. The muzzle position and
''' the forward direction are stamped here and never touched again: the gun goes
''' on turning and elevating, and the flame belongs to the barrel that produced
''' it rather than to wherever that barrel has swung to two frames later. The
''' per-gun numbers are stamped the same way, so a shot keeps its own gun's
''' timing and size even if the vehicle it came from is replaced mid-flight.
''' </summary>
Public Class TankShot

    Public active As Boolean
    Public age As Single

    ''' <summary>Muzzle world position at the moment of firing. Static.</summary>
    Public pos As Vector3

    ''' <summary>Gun forward at the moment of firing. Static, normalised.</summary>
    Public fwd As Vector3

    ''' <summary>0..1 across the FLAME's own life - the flipbook cursor.</summary>
    Public flashPhase As Single = 1.0F

    ''' <summary>0..1 across the light's life, which is five times shorter.</summary>
    Public lightPhase As Single = 1.0F

    ''' <summary>The gun's entry from gun_effects.xml. Everything below comes
    ''' from it, and it is kept so the colour can be sampled per frame.</summary>
    Public spec As BlastSpec

    Public flashLifeS As Single = 0.5F
    Public lightLifeS As Single = 0.09F
    Public length As Single = 1.2F
    Public thickness As Single = 0.6F

    ' ---- the round in flight -------------------------------------------------

    ''' <summary>Where the round is now, and where it was at the START of this
    ''' step. The pair is the segment it flew, and the trail is spawned along
    ''' it - see TankTrail.</summary>
    Public curPos As Vector3
    Public prevPos As Vector3

    ''' <summary>Where it is going: the ray's hit point, captured at fire
    ''' time.</summary>
    Public targetPos As Vector3

    Public inFlight As Boolean
    Public speed As Single = 180.0F

    ''' <summary>The hit this round will deliver when it ARRIVES. Held rather
    ''' than applied at fire time: a burst that goes off the instant the gun
    ''' fires beats its own round to the target.</summary>
    Public hit As ShotHit
    Public hitDelivered As Boolean

    ''' <summary>This shot's own trail particles.</summary>
    Public ReadOnly trail As New TankTrail

    Public Sub Fire(p As Vector3, d As Vector3, s As BlastSpec, h As ShotHit,
                    v As Single)
        active = True
        age = 0.0F
        pos = p
        fwd = If(d.LengthSquared > 1.0E-9F, Vector3.Normalize(d), -Vector3.UnitZ)
        flashPhase = 0.0F
        lightPhase = 0.0F
        spec = s
        If s IsNot Nothing Then
            flashLifeS = Math.Max(s.endS, 0.02F)
            lightLifeS = Math.Max(s.durationS, 0.01F)
            length = s.flashLength
            thickness = s.flashThickness
        End If

        curPos = p
        prevPos = p
        hit = h
        hitDelivered = False
        speed = Math.Max(v, 1.0F)
        targetPos = If(h.kind = HitKind.NoHit, p + fwd * 2000.0F, h.point)
        inFlight = True

        ' THE TRAIL'S LIFETIME IS THE FLIGHT TIME. The first particle, born at
        ' the muzzle, then reaches zero alpha exactly as the round arrives, and
        ' the ones born further along outlive it by however much less far they
        ' have come - so the trail rolls up from the gun toward the impact
        ' instead of the whole streak vanishing at once. A floor keeps a
        ' point-blank shot visible for a moment.
        Dim dist = (targetPos - p).Length
        trail.Begin(dist, Math.Max(0.45F, dist / speed))
    End Sub

    Public Sub Update(dt As Single)
        If Not active Then Return
        age += dt
        ' Clamped at 1 so a renderer can read the last frame indefinitely
        ' without running off the end of the flipbook.
        flashPhase = Math.Min(1.0F, age / flashLifeS)
        lightPhase = Math.Min(1.0F, age / lightLifeS)

        If inFlight Then
            ' Stashed BEFORE the step, so it really is the start of it.
            prevPos = curPos
            curPos += fwd * (speed * dt)

            ' Projected onto the firing direction rather than compared by
            ' distance: a round that overshoots a target below the muzzle is
            ' caught by the projection and is not by a plain range test.
            If Vector3.Dot(targetPos - curPos, fwd) <= 0.0F Then
                curPos = targetPos
                inFlight = False
            End If
            trail.Emit(prevPos, curPos)
        End If

        trail.Update(dt)

        ' THE SLOT IS RENTED UNTIL THE SMOKE HAS GONE. Freeing it when the
        ' flame burns out cuts the trail off mid-air, because the round is
        ' still flying and its particles are still fading.
        If flashPhase >= 1.0F AndAlso Not inFlight AndAlso Not trail.alive Then
            active = False
        End If
    End Sub
End Class

''' <summary>
''' One tank's shots.
'''
''' PER VEHICLE, NOT ONE POOL FOR THE MAP. Thirty guns sharing a single pool
''' means a fast-firing tank can take every slot and another vehicle's shot is
''' silently dropped - the flash that goes missing is not the one that caused
''' the pressure. A pool each makes the budget local: a gun can only ever starve
''' itself, and it takes its own gun's timing and size with it.
'''
''' Small on purpose. A gun fires every couple of seconds and a flame lives half
''' a second, so one or two are alive at a time; eight is room for an autocannon
''' entry without pretending a tank needs fifty.
''' </summary>
Public Class TankShotPool

    Public Const SLOTS As Integer = 8

    Public ReadOnly shots(SLOTS - 1) As TankShot

    Public Sub New()
        For i = 0 To SLOTS - 1
            shots(i) = New TankShot()
        Next
    End Sub

    ''' <summary>Arm a slot, or drop the shot. A dropped shot is a missing
    ''' flame rather than a list that grows while the guns run.</summary>
    Public Function Fire(p As Vector3, d As Vector3, s As BlastSpec,
                         h As ShotHit, v As Single) As TankShot
        For i = 0 To SLOTS - 1
            If shots(i).active Then Continue For
            shots(i).Fire(p, d, s, h, v)
            Return shots(i)
        Next
        Return Nothing
    End Function

    Public Sub Update(dt As Single)
        For i = 0 To SLOTS - 1
            shots(i).Update(dt)
        Next
    End Sub
End Class

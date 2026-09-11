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

    Public Sub Fire(p As Vector3, d As Vector3, s As BlastSpec)
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
    End Sub

    Public Sub Update(dt As Single)
        If Not active Then Return
        age += dt
        ' Clamped at 1 so a renderer can read the last frame indefinitely
        ' without running off the end of the flipbook.
        flashPhase = Math.Min(1.0F, age / flashLifeS)
        lightPhase = Math.Min(1.0F, age / lightLifeS)
        If flashPhase >= 1.0F Then active = False
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
    Public Function Fire(p As Vector3, d As Vector3, s As BlastSpec) As TankShot
        For i = 0 To SLOTS - 1
            If shots(i).active Then Continue For
            shots(i).Fire(p, d, s)
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

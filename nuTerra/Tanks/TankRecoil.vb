''' <summary>
''' One gun's recoil cycle: the barrel slams back, holds, and settles home.
'''
''' PORTED FROM TEPY's tankExporterPy/gun_recoil.py, which is where this was
''' worked out - the timings, the shape of the return, and above all the rule
''' for WHICH vertices move. The history there is worth keeping because every
''' wrong version of it looked plausible:
'''
'''  * Driving the recoil through the BONE PALETTE moved the wrong parts. The
'''    palette order is not the same on every tank - TEPY measured G78 shipping
'''    ['G_BlendBone', 'Gun_BlendBone'] and A38 the same two reversed - so any
'''    rule of the form "bone 1 recoils" is right on one nation and wrong on
'''    the next. Worse, WoT's naming is the inverse of the intuition: G_* is
'''    the BARREL and Gun_* is the rigid mount, confirmed there by measuring
'''    the Z span of the vertices bound to each.
'''
'''  * Name-classifying the bones offline fixed the order problem and still
'''    failed: A100_T49 inverts the convention outright.
'''
'''  * A WEIGHTED per-slot rule - every slot referencing a recoil bone
'''    contributes its own weight - looks the most principled of the lot and is
'''    wrong for the single largest vertex family in the game. TEPY counted
'''    815k vertices across 579 tanks shaped (0, 3, ?, ?): mantlet vertices
'''    with secondary skinning to the recoil bone for a smooth joint. Under the
'''    weighted rule they slide partway back on every shot, which is exactly
'''    the "wrong parts move on the gun" symptom.
'''
''' What survived all of that is a BYTE TEST ON ONE SLOT:
'''
'''     a_bone_idx.x == 3  ->  this vertex recoils, by the full amount
'''     anything else      ->  it does not move at all
'''
''' Binary, not blended. (0, 3, 3, 0) is 100% mantlet even though two slots
''' name the recoil bone, and (3, 6, 6, 0) is 100% barrel even though two name
''' the mantlet. The byte is read RAW - it is not divided by three and never
''' indexes the palette - so the rule cannot care what order a tank declares
''' its bones in, which is the whole reason it works everywhere.
'''
''' The translation is added AFTER the skin, as a plain offset in mesh-local
''' space, for the same reason: it bypasses the palette entirely.
'''
''' THE OTHER HALF, the deform, is not here - see TankRenderer.upload_recoil.
''' </summary>
Public Class TankRecoil

    ''' <summary>How far back the barrel slides. A real 152 mm gun runs 0.6 to
    ''' 0.9 m; TEPY settled on 0.40 m as what reads as a recoil at 60 fps
    ''' without the barrel appearing to fall out of the mantlet.</summary>
    Public Const MAX_TRAVEL_M As Single = 0.40F

    ''' <summary>Back-stroke. Linear - a real gun is an impulse then a
    ''' deceleration, and at 60 ms the difference is not visible.</summary>
    Private Const OUT_S As Single = 0.06F

    ''' <summary>Held at full travel, so the eye registers that it went.</summary>
    Private Const DWELL_S As Single = 0.04F

    ''' <summary>The recuperator pushing it home. Eased, not linear: a spring
    ''' and hydraulic return settles rather than stopping dead at zero.</summary>
    Private Const RETURN_S As Single = 0.30F

    Private Const IDLE = 0, OUT = 1, DWELL = 2, HOMING = 3

    Private phase As Integer = IDLE
    Private t As Single

    ''' <summary>Metres back from battery, this frame. Zero at rest.</summary>
    Public ReadOnly Property offset_m As Single
        Get
            Return m_offset
        End Get
    End Property
    Private m_offset As Single

    ''' <summary>True while a cycle is running.</summary>
    Public ReadOnly Property firing As Boolean
        Get
            Return phase <> IDLE
        End Get
    End Property

    ''' <summary>
    ''' Start a cycle. IGNORED while one is already running, so a trigger held
    ''' down - or two sources both asking on the same frame - fires once rather
    ''' than restarting the stroke and freezing the barrel at full travel.
    ''' </summary>
    Public Sub Fire()
        If phase <> IDLE Then Return
        phase = OUT
        t = 0.0F
        m_offset = 0.0F
    End Sub

    ''' <summary>Advance by dt. Safe every frame whether or not it is firing.</summary>
    Public Sub Update(dt As Single)
        If phase = IDLE Then
            m_offset = 0.0F
            Return
        End If

        t += dt

        Select Case phase
            Case OUT
                Dim u = Math.Min(1.0F, t / OUT_S)
                m_offset = MAX_TRAVEL_M * u
                If u >= 1.0F Then phase = DWELL : t = 0.0F

            Case DWELL
                m_offset = MAX_TRAVEL_M
                If t >= DWELL_S Then phase = HOMING : t = 0.0F

            Case HOMING
                Dim u = Math.Min(1.0F, t / RETURN_S)
                ' 1 - (1-u)^3: quick off full travel, slow into battery.
                Dim k = 1.0F - u
                m_offset = MAX_TRAVEL_M * k * k * k
                If u >= 1.0F Then phase = IDLE : t = 0.0F : m_offset = 0.0F
        End Select
    End Sub
End Class

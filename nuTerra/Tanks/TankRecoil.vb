Imports OpenTK.Mathematics

''' <summary>
''' One gun's recoil cycle: the barrel slams back, holds, and settles home.
'''
''' The timings are TEPY's (tankExporterPy/gun_recoil.py). The rule for WHICH
''' vertices move is not, and the difference is measured rather than argued -
''' see ClassifyPalette below.
''' </summary>
Public Class TankRecoil

    ''' <summary>
    ''' Which palette slots are the BARREL, from the bone names.
    '''
    ''' THE BYTE IS NOT THE ANSWER, and this is the correction to what shipped
    ''' first. TEPY's shader tests the raw bone byte against a hardcoded 3 and
    ''' calls it a WoT-wide convention. Its own corpus table says otherwise:
    ''' of 1185 guns, byte 3 is the barrel on 613 and byte 0 on 475, with the
    ''' rest on 6, 9, 12, 15 or 21. It is a coin flip, because the byte is just
    ''' palette_index * 3 and the two bones are declared in whichever order the
    ''' artist wrote them:
    '''
    '''     [G_BlendBone, Gun_BlendBone]  -> barrel is byte 0
    '''     [Gun_BlendBone, G_BlendBone]  -> barrel is byte 3
    '''
    ''' Half of the thirty on this map are one way and half the other, which is
    ''' exactly what "some guns move the wrong parts" looks like: on the other
    ''' half, byte 3 is the rigid MOUNT, so the mantlet slid back and the barrel
    ''' stayed put.
    '''
    ''' The names are stable where the order is not. Across all 1185 guns:
    ''' G_BlendBone appears on 1158, Gun_BlendBone on 1174, and WoT's naming is
    ''' the inverse of the intuition - G_* is the BARREL, Gun_* the assembly it
    ''' slides in. TEPY verified that spatially on the Tiger: G_BlendBone's
    ''' vertices span Z -5.44..-1.43 and Gun_BlendBone's -1.43..-0.01.
    '''
    ''' AND THERE IS NO CLOTH BONE. The category exists in TEPY's classifier and
    ''' fires on exactly zero tanks - no bone in the corpus is named cloth,
    ''' fabric or rubber, and a separate scan found no cloth material either:
    ''' all 1185 guns are one material, PBS_tank_skinned.fx. What the shader
    ''' comments call the cloth slot is whatever bone is declared third, and in
    ''' this corpus that is static_joint_BlendBone or Joint_01_BlendBone - a
    ''' rigid mount. The fabric at the mantlet is not a bone or a material; it
    ''' is the 1.3% of vertices whose WEIGHT is split between the barrel and a
    ''' rigid bone, and it stretches for free if the recoil is weighted.
    ''' </summary>
    ''' <summary>Forwards to TankBarrel.ClassifyPalette, which is where this
    ''' moved on 2026-09-25 so Brain Testing could reach it - it compiles
    ''' TankVehicle.vb and not this file. Same answer, one copy.</summary>
    Public Shared Function ClassifyPalette(palette As List(Of String)) As Integer()
        Return TankBarrel.ClassifyPalette(palette)
    End Function

    ''' <summary>How far back the barrel slides. A real 152 mm gun runs 0.6 to
    ''' 0.9 m; TEPY settled on 0.40 m as what reads as a recoil at 60 fps
    ''' without the barrel appearing to fall out of the mantlet.</summary>
    ''' <summary>How far back the barrel slides when the gun does not say.
    '''
    ''' A FALLBACK NOW, not the value. Every gun states its own
    ''' recoil/amplitude - 0.42 on the 122 mm, 0.22 on the 100 mm - read into
    ''' TankVehicle.recoilTravelM on 2026-09-25. This is what a gun with no
    ''' amplitude gets, and it stays at TEPY's 0.40 because that is the number
    ''' chosen to read as a recoil at 60 fps without the barrel appearing to
    ''' leave the mantlet.</summary>
    Public Const MAX_TRAVEL_M As Single = TankVehicle.RECOIL_TRAVEL_FALLBACK_M

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
    ''' <summary>This gun's own recoil travel, metres. Set from
    ''' TankVehicle.recoilTravelM when the vehicle is built; the guns state 0.42
    ''' and 0.22, so a single constant made every barrel slide the same
    ''' distance.</summary>
    Public travel_m As Single = MAX_TRAVEL_M

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
    '''
    ''' Returns whether a shot actually went off, which is what the effects and
    ''' the ray hang on: a swallowed trigger must not also spawn a muzzle flash
    ''' and a second round downrange.
    ''' </summary>
    Public Function Fire() As Boolean
        If phase <> IDLE Then Return False
        phase = OUT
        t = 0.0F
        m_offset = 0.0F
        Return True
    End Function

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
                m_offset = travel_m * u
                If u >= 1.0F Then phase = DWELL : t = 0.0F

            Case DWELL
                m_offset = travel_m
                If t >= DWELL_S Then phase = HOMING : t = 0.0F

            Case HOMING
                Dim u = Math.Min(1.0F, t / RETURN_S)
                ' 1 - (1-u)^3: quick off full travel, slow into battery.
                Dim k = 1.0F - u
                m_offset = travel_m * k * k * k
                If u >= 1.0F Then phase = IDLE : t = 0.0F : m_offset = 0.0F
        End Select
    End Sub
End Class

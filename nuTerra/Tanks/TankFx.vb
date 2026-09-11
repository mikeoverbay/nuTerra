Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' The flash at the muzzle and the burst where the round lands.
'''
''' A FIXED POOL, NOT A LIST. A slot is armed when a gun fires and freed when
''' its oldest component has burned out; nothing is allocated while the guns are
''' running. Thirty tanks firing every two seconds with a half-second burst is
''' about eight live at once, so sixty-four slots is room to spare and a hard
''' ceiling if the fire rate is wound up - a burst that cannot find a slot is
''' dropped, which is a missing flash rather than a growing list.
'''
''' DRAWN INTO THE FX BUFFER, not over the finished frame. gFX_HDR is float16
''' and the bright pass and blur that already turn fire and lamp bulbs into
''' glare run over it - so a muzzle flash written there gets its halo from the
''' machinery that is there, and does not have to fake one. Writing to gColor
''' instead would clamp at 1.0 and the flash would be a flat white lozenge.
'''
''' The camera-facing quad is built from gl_VertexID against the empty VAO, the
''' same as the lamp bulbs and the ID cards; there is no buffer to keep in step
''' with the pool.
''' </summary>
Public Class TankFx
    Implements IDisposable

    Private Const SLOTS As Integer = 64

    ''' <summary>Muzzle flash: brief and bright. A tank gun's flash is gone
    ''' inside a couple of frames at 60 fps, and a longer one reads as a flare
    ''' hanging off the barrel.</summary>
    Private Const FLASH_S As Single = 0.10F

    ''' <summary>The burst at the far end. Longer, because it is dust and fire
    ''' expanding rather than a single event.</summary>
    Private Const BURST_S As Single = 0.55F

    Private Structure Puff
        Public pos As Vector3
        Public colour As Vector3
        Public life As Single        ' seconds it lives for
        Public age As Single
        Public r0 As Single          ' metres at birth
        Public r1 As Single          ' metres at death
        Public active As Boolean
    End Structure

    Private ReadOnly pool(SLOTS - 1) As Puff
    Private shader As Shader

    ''' <summary>How many are alive, for the log and the sliders.</summary>
    Public ReadOnly Property live As Integer
        Get
            Dim n = 0
            For i = 0 To SLOTS - 1
                If pool(i).active Then n += 1
            Next
            Return n
        End Get
    End Property

    ''' <summary>
    ''' Arm the burst where a round landed.
    '''
    ''' THE IMPACTS ARE NOT PER TANK and are not indexed to the shots. A round
    ''' from one vehicle lands on another, and one shot can produce no impact at
    ''' all - fired at the sky - or later more than one, once spall and
    ''' ricochets exist. So this is one flat list of hit events that anything
    ''' can add to, which is the same split TEPY makes between its ShotPool and
    ''' its ImpactPool and for the same reason.
    '''
    ''' THE BURST IS PUSHED OFF THE SURFACE by a fraction of its own radius
    ''' along the surface normal. Centred exactly on the impact point, half of a
    ''' ground burst is under the ground and the visible half is a semicircle
    ''' with a hard straight edge along the terrain.
    ''' </summary>
    Public Sub Impact(hit As ShotHit)
        If hit.kind = HitKind.NoHit Then Return

        ' Earth throws up dust, armour throws sparks. Same burst, different
        ' colour, because the two reading alike is the thing that would make
        ' thirty impacts a frame look like one effect.
        Dim col = If(hit.kind = HitKind.Tank,
                     New Vector3(1.0F, 0.85F, 0.55F),
                     New Vector3(0.72F, 0.62F, 0.48F))
        Dim n = If(hit.normal.LengthSquared > 1.0E-6F,
                   Vector3.Normalize(hit.normal), Vector3.UnitY)
        arm(hit.point + n * 0.6F, col, BURST_S, 0.4F, 2.6F)
    End Sub

    Private Sub arm(p As Vector3, colour As Vector3, life As Single,
                    r0 As Single, r1 As Single)
        For i = 0 To SLOTS - 1
            If pool(i).active Then Continue For
            pool(i).pos = p
            pool(i).colour = colour
            pool(i).life = life
            pool(i).age = 0.0F
            pool(i).r0 = r0
            pool(i).r1 = r1
            pool(i).active = True
            Return
        Next
        ' Full. Dropped on purpose - see the class note.
    End Sub

    Public Sub Update(dt As Single)
        For i = 0 To SLOTS - 1
            If Not pool(i).active Then Continue For
            pool(i).age += dt
            If pool(i).age >= pool(i).life Then pool(i).active = False
        Next
    End Sub

    ''' <summary>
    ''' Draw every live puff into whatever buffer is bound.
    '''
    ''' Called from the FX block with gFX_HDR bound, so the blend is the
    ''' premultiplied one the rest of that buffer uses: the shader emits alpha
    ''' 0, which under One / OneMinusSrcAlpha reduces to dst + src. It adds
    ''' light and attenuates nothing, which is what a flash does and what lets
    ''' overlapping bursts brighten rather than cover each other.
    ''' </summary>
    Public Sub Draw(instances As List(Of TankInstance))
        Dim any = live > 0
        If Not any AndAlso instances IsNot Nothing Then
            For Each inst In instances
                For Each sh In inst.shots.shots
                    If sh.active Then any = True : Exit For
                Next
                If any Then Exit For
            Next
        End If
        If Not any Then Return
        If shader Is Nothing Then shader = New Shader("tank_fx")

        GL_PUSH_GROUP("tank_fx")
        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        shader.Use()
        defaultVao.Bind()
        GL.Uniform1(shader("gain"), TANK_FX_GAIN)

        ' EVERY TANK'S OWN SHOTS FIRST. Each carries the muzzle it left, the
        ' direction it left along, and its own gun's timing and size - so a
        ' 15 cm and an autocannon differ here without this loop knowing which
        ' is which.
        If instances IsNot Nothing Then
            For Each inst In instances
                For Each sh In inst.shots.shots
                    If Not sh.active Then Continue For
                    Dim c = If(sh.spec IsNot Nothing,
                               sh.spec.Sample(sh.lightPhase) * 0.05F,
                               New Vector3(1.0F, 0.6F, 0.2F))
                    ' Grows along the barrel as it burns, and sits half its
                    ' own length out so the root is at the muzzle rather than
                    ' the middle.
                    Dim r = sh.thickness + (sh.length - sh.thickness) * sh.flashPhase
                    Dim p = sh.pos + sh.fwd * (sh.length * 0.5F)
                    GL.Uniform3(shader("centre"), p.X, p.Y, p.Z)
                    GL.Uniform3(shader("colour"), c.X, c.Y, c.Z)
                    GL.Uniform1(shader("radius"), r)
                    GL.Uniform1(shader("phase"), sh.flashPhase)
                    GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
                Next
            Next
        End If

        For i = 0 To SLOTS - 1
            If Not pool(i).active Then Continue For
            Dim u = pool(i).age / Math.Max(pool(i).life, 1.0E-4F)
            GL.Uniform3(shader("centre"), pool(i).pos.X, pool(i).pos.Y, pool(i).pos.Z)
            GL.Uniform3(shader("colour"), pool(i).colour.X, pool(i).colour.Y, pool(i).colour.Z)
            GL.Uniform1(shader("radius"), pool(i).r0 + (pool(i).r1 - pool(i).r0) * u)
            GL.Uniform1(shader("phase"), u)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        Next
        shader.StopUse()

        GL.BindVertexArray(0)
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For i = 0 To SLOTS - 1
            pool(i).active = False
        Next
    End Sub
End Class

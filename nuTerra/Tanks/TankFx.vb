Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Every sprite the guns throw: the flame at a muzzle, the smoke under it, the
''' vapour behind the round, and the explosion where it lands.
'''
''' NOTHING IS DRAWN PER PARTICLE. One buffer is refilled each frame from
''' whatever is alive anywhere on the map and drawn in two instanced calls -
''' thirty tanks in a firefight is thousands of sprites, and a draw call each
''' would cost more than the sprites do.
'''
''' TWO CALLS BECAUSE THE ORDER IS LOAD-BEARING. Smoke goes down first and
''' BLENDS, so it covers what is behind it; the flames and explosions go over it
''' and ADD. That way the fire is visible through its own smoke - which is also
''' why the smoke fades in rather than starting opaque. TEPY states the same
''' ordering and the same reason.
'''
''' Into gFX_HDR, before the bright pass, so the glare comes from the machinery
''' that already serves fire and lamp bulbs rather than being faked.
'''
''' The impacts are a flat list, not per tank and not indexed to the shots: a
''' round from one vehicle lands on another, a shot at the sky lands nowhere,
''' and one round will produce several once spall exists.
''' </summary>
Public Class TankFx
    Implements IDisposable

    ''' <summary>Impacts that can be burning at once. Thirty guns at a round
    ''' every two seconds with a second of burn is a handful; this is room to
    ''' spare, and a burst that finds no slot is dropped rather than growing a
    ''' list while the guns run.</summary>
    Private Const SLOTS As Integer = 48

    Private ReadOnly impacts(SLOTS - 1) As TankPuffs

    ' ---- the batch -----------------------------------------------------------
    Private Const BATCH As Integer = 24000       ' sprites
    Private Const FLOATS As Integer = 12         ' pos.xyz+radius, rgba, uv rect
    Private ReadOnly batch_buf(BATCH * FLOATS - 1) As Single
    Private shader As Shader
    Private vbo As GLBuffer
    Private vao As GLVertexArray

    Public Sub New()
        For i = 0 To SLOTS - 1
            impacts(i) = New TankPuffs(12)
        Next
    End Sub

    ''' <summary>
    ''' Set off the burst where a round landed.
    '''
    ''' TWO SEQUENCES, PICKED BY WHAT WAS HIT, and the choice is TEPY's: the
    ''' atlas holds the same fireball twice, once cleaner and oranger and once
    ''' browner and rougher, and its note says the first is for an object and
    ''' the second for the ground. Armour gets the fire, earth gets the dust.
    '''
    ''' PUSHED OFF THE SURFACE by a fraction of its own size along the normal.
    ''' Centred exactly on the hit, half the burst is inside what it hit and
    ''' what shows is a semicircle with a straight edge along the ground.
    '''
    ''' It is thrown back ALONG THE NORMAL rather than outward from a point,
    ''' because that is the direction the material actually leaves in - a round
    ''' into a hillside throws dirt up the slope, not back down the barrel.
    ''' </summary>
    Public Sub Impact(hit As ShotHit)
        If hit.kind = HitKind.NoHit Then Return

        Dim b = free_impact()
        If b Is Nothing Then Return

        Dim n = If(hit.normal.LengthSquared > 1.0E-6F,
                   Vector3.Normalize(hit.normal), Vector3.UnitY)
        Dim armour = (hit.kind = HitKind.Tank)

        b.grid = If(armour, TankAtlas.EXPLOSION_FIRE, TankAtlas.EXPLOSION_DUST)
        b.lifeS = If(armour, 0.75F, 1.0F)
        b.size0 = If(armour, 0.6F, 0.9F)
        b.size1 = If(armour, 2.2F, 3.4F)
        b.drag = 3.0F
        b.opacity = 1.0F
        b.fadeIn = 0.0F
        b.tint = Vector3.One
        b.Burst(hit.point + n * 0.35F, n, If(armour, 3.5F, 2.4F), 0.7F, 0.25F)
    End Sub

    Private Function free_impact() As TankPuffs
        For i = 0 To SLOTS - 1
            If Not impacts(i).alive Then Return impacts(i)
        Next
        Return Nothing
    End Function

    Public Sub Update(dt As Single)
        For i = 0 To SLOTS - 1
            impacts(i).Update(dt)
        Next
    End Sub

    Public Sub Draw(instances As List(Of TankInstance))
        Dim at = 0
        Dim cap = BATCH * FLOATS

        ' SMOKE FIRST and on its own, because it is the only thing here that
        ' blends. Collected into the front of the buffer so the two draws are
        ' two ranges of one upload rather than two uploads.
        Dim nSmoke = 0
        If instances IsNot Nothing Then
            For Each inst In instances
                For Each sh In inst.shots.shots
                    If sh.active Then nSmoke += sh.smoke.Collect(batch_buf, at, cap)
                Next
            Next
        End If

        Dim nAdd = 0
        If instances IsNot Nothing Then
            For Each inst In instances
                For Each sh In inst.shots.shots
                    If Not sh.active Then Continue For
                    nAdd += sh.trail.Collect(batch_buf, at, cap)
                    nAdd += sh.flame.Collect(batch_buf, at, cap)
                Next
            Next
        End If
        For i = 0 To SLOTS - 1
            nAdd += impacts(i).Collect(batch_buf, at, cap)
        Next

        If nSmoke + nAdd = 0 Then Return
        ensure_gl()

        GL_PUSH_GROUP("tank_fx")
        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.Blend)
        ' Premultiplied. With an alpha it is "over"; with the shader's alpha
        ' forced to zero the same equation is pure addition, so one blend
        ' function serves both passes.
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        vbo.SubData(IntPtr.Zero, (nSmoke + nAdd) * FLOATS * 4, batch_buf)
        shader.Use()
        Dim atlas = TankAtlas.texture
        If atlas IsNot Nothing Then atlas.BindUnit(0)
        vao.Bind()

        If nSmoke > 0 Then
            GL.Uniform1(shader("additive"), 0)
            vao.VertexBuffer(0, vbo, IntPtr.Zero, FLOATS * 4)
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, nSmoke)
        End If
        If nAdd > 0 Then
            GL.Uniform1(shader("additive"), 1)
            vao.VertexBuffer(0, vbo, New IntPtr(nSmoke * FLOATS * 4), FLOATS * 4)
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, nAdd)
        End If

        shader.StopUse()
        GL.BindVertexArray(0)
        GL.BindTextureUnit(0, 0)
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL_POP_GROUP()
    End Sub

    Private Sub ensure_gl()
        If shader IsNot Nothing Then Return
        shader = New Shader("tank_particle")
        vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tank_fx")
        vbo.StorageNullData(BATCH * FLOATS * 4, BufferStorageFlags.DynamicStorageBit)
        vao = GLVertexArray.Create("tank_fx")
        vao.VertexBuffer(0, vbo, IntPtr.Zero, FLOATS * 4)
        vao.AttribFormat(0, 4, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)
        vao.AttribFormat(1, 4, VertexAttribType.Float, False, 16)
        vao.AttribBinding(1, 0)
        vao.EnableAttrib(1)
        vao.AttribFormat(2, 4, VertexAttribType.Float, False, 32)
        vao.AttribBinding(2, 0)
        vao.EnableAttrib(2)
        ' One record per INSTANCE, not per vertex - the quad's four corners all
        ' read the same sprite.
        vao.BindingDivisor(0, 1)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        vbo?.Dispose()
        vao?.Dispose()
        vbo = Nothing
        vao = Nothing
    End Sub
End Class

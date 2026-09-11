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

    ' ---- the flame cards -----------------------------------------------------
    '
    ' Their own buffer and their own shader, because they are not billboards:
    ' three world-standing rectangles per shot, rolled 120 degrees apart about
    ' the barrel. See tank_flash.vert for why the artwork forces that.
    Private Const CARDS As Integer = 512         ' quads
    Private Const CFLOATS As Integer = 16        ' pos+len, fwd+thick, up+alpha, uv
    Private ReadOnly card_buf(CARDS * CFLOATS - 1) As Single
    Private flashShader As Shader
    Private flashVbo As GLBuffer
    Private flashVao As GLVertexArray

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
                Next
            Next
        End If
        For i = 0 To SLOTS - 1
            nAdd += impacts(i).Collect(batch_buf, at, cap)
        Next

        ' ORDER MATTERS FOR THE SMOKE AND NOT FOR THE REST, which is the whole
        ' reason they are separate draws. Alpha "over" is not commutative: two
        ' clouds composited near-first come out differently from far-first, and
        ' the wrong order puts a distant puff in front of a near one. Addition
        ' IS commutative, so the flames, the trail and the explosions can go
        ' down in whatever order they were collected and come out identical.
        If nSmoke > 1 Then sort_back_to_front(nSmoke)

        If nSmoke + nAdd = 0 Then Return
        ensure_gl()

        GL_PUSH_GROUP("tank_fx")

        ' TESTED AGAINST THE SCENE, NOT WRITING TO IT.
        '
        ' The test is what stops a flame burning through the hill in front of
        ' it. Reversed Z, so the comparison is Greater - the same convention
        ' the rest of the frame uses and the same one MapParticles sets for
        ' exactly this pass.
        '
        ' The WRITE has to stay off, and that is the half that bites. A sprite
        ' that writes depth occludes every sprite behind it, so the nearest
        ' card of a burst would discard the fragments of the ones behind it
        ' that are supposed to be blending through - the cloud loses its
        ' interior and reads as one flat card. Transparent geometry tests
        ' against opaque depth and never contributes to it.
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
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
        draw_flashes(instances)

        GL.BindVertexArray(0)
        GL.BindTextureUnit(0, 0)
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Hang three flame cards on every barrel that is still burning.
    '''
    ''' THE THIRD AXIS IS DERIVED FROM THE BARREL, not from the camera. Each
    ''' card needs a direction across the gun; the first is any vector
    ''' perpendicular to it and the other two are that one rolled 120 and 240
    ''' degrees about the barrel, which is Rodrigues with the axis term dropped
    ''' because the vector is already perpendicular to the axis.
    '''
    ''' The card GROWS along the barrel as it burns and fades as it goes, so
    ''' the plume reaches out of the muzzle rather than appearing at full
    ''' length; and the flipbook frame advances on the same phase, so the
    ''' picture on the card is changing while it does.
    ''' </summary>
    Private Sub draw_flashes(instances As List(Of TankInstance))
        If instances Is Nothing Then Return
        Dim atlas = TankAtlas.texture
        If atlas Is Nothing Then Return

        Dim at = 0
        Dim n = 0
        For Each inst In instances
            For Each sh In inst.shots.shots
                If Not sh.active OrElse sh.flashPhase >= 1.0F Then Continue For
                If at + 3 * CFLOATS > CARDS * CFLOATS Then Exit For

                Dim f = sh.fwd
                ' Any perpendicular: cross with whichever world axis the barrel
                ' is least aligned with, so a gun pointing straight up still
                ' gets a basis.
                Dim aux = If(Math.Abs(f.Y) < 0.9F, Vector3.UnitY, Vector3.UnitX)
                Dim u0 = Vector3.Normalize(Vector3.Cross(aux, f))
                Dim v0 = Vector3.Cross(f, u0)

                Dim u = sh.flashPhase
                Dim uv = TankAtlas.FrameUV(TankAtlas.GUN_FLASH,
                                           CInt(u * (TankAtlas.GUN_FLASH.frames - 1)))
                ' Out of the muzzle rather than onto it, and gone by the end.
                Dim len = sh.length * (0.45F + 0.55F * Math.Min(1.0F, u * 3.0F))
                Dim thick = sh.thickness
                Dim a = (1.0F - u) * (1.0F - u)

                For k = 0 To 2
                    Dim ang = CSng(k * 2.0 * Math.PI / 3.0)
                    Dim up = u0 * CSng(Math.Cos(ang)) + v0 * CSng(Math.Sin(ang))
                    card_buf(at) = sh.pos.X
                    card_buf(at + 1) = sh.pos.Y
                    card_buf(at + 2) = sh.pos.Z
                    card_buf(at + 3) = len
                    card_buf(at + 4) = f.X
                    card_buf(at + 5) = f.Y
                    card_buf(at + 6) = f.Z
                    card_buf(at + 7) = thick
                    card_buf(at + 8) = up.X
                    card_buf(at + 9) = up.Y
                    card_buf(at + 10) = up.Z
                    card_buf(at + 11) = a
                    card_buf(at + 12) = uv.X
                    card_buf(at + 13) = uv.Y
                    card_buf(at + 14) = uv.Z
                    card_buf(at + 15) = uv.W
                    at += CFLOATS
                    n += 1
                Next
            Next
        Next
        If n = 0 Then Return

        If flashShader Is Nothing Then
            flashShader = New Shader("tank_flash")
            flashVbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tank_flash")
            flashVbo.StorageNullData(CARDS * CFLOATS * 4,
                                     BufferStorageFlags.DynamicStorageBit)
            flashVao = GLVertexArray.Create("tank_flash")
            flashVao.VertexBuffer(0, flashVbo, IntPtr.Zero, CFLOATS * 4)
            For k = 0 To 3
                flashVao.AttribFormat(k, 4, VertexAttribType.Float, False, k * 16)
                flashVao.AttribBinding(k, 0)
                flashVao.EnableAttrib(k)
            Next
            flashVao.BindingDivisor(0, 1)
        End If

        flashVbo.SubData(IntPtr.Zero, n * CFLOATS * 4, card_buf)
        flashShader.Use()
        atlas.BindUnit(0)
        GL.Uniform3(flashShader("tint"), TANK_FX_GAIN, TANK_FX_GAIN, TANK_FX_GAIN)
        flashVao.Bind()
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, n)
        flashShader.StopUse()
    End Sub

    ''' <summary>
    ''' Put the first `count` records in farthest-first order.
    '''
    ''' Sorted on the SQUARED distance, because only the ordering is wanted and
    ''' a square root per sprite per frame buys nothing. Negated so an ascending
    ''' sort puts the farthest first - the same trick, for the same reason, as
    ''' MapParticles.
    ''' </summary>
    Private Sub sort_back_to_front(count As Integer)
        If sortKeys Is Nothing OrElse sortKeys.Length < count Then
            ReDim sortKeys(count * 2)
            ReDim sortIdx(count * 2)
            ReDim scratch((count * 2 + 1) * FLOATS)
        End If

        Dim cam = map_scene.camera.CAM_POSITION
        For i = 0 To count - 1
            Dim b = i * FLOATS
            Dim dx = batch_buf(b) - cam.X
            Dim dy = batch_buf(b + 1) - cam.Y
            Dim dz = batch_buf(b + 2) - cam.Z
            sortKeys(i) = -(dx * dx + dy * dy + dz * dz)
            sortIdx(i) = i
        Next
        Array.Sort(sortKeys, sortIdx, 0, count)

        Array.Copy(batch_buf, scratch, count * FLOATS)
        For i = 0 To count - 1
            Array.Copy(scratch, sortIdx(i) * FLOATS,
                       batch_buf, i * FLOATS, FLOATS)
        Next
    End Sub

    Private sortKeys() As Single
    Private sortIdx() As Integer
    Private scratch() As Single

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
        flashVbo?.Dispose()
        flashVao?.Dispose()
        vbo = Nothing
        vao = Nothing
        flashVbo = Nothing
        flashVao = Nothing
    End Sub
End Class

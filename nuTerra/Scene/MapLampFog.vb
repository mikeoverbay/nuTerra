Imports OpenTK.Graphics.OpenGL4

Imports OpenTK.Mathematics

''' <summary>
''' The lamps' light scattered by fog - the shafts.
'''
''' A second pass, after the fog, drawing nothing but one sphere per lamp. Every
''' other lighting calculation in this renderer runs AT A SURFACE; this one runs
''' in the air between the camera and the surface, which is the only place a
''' shaft exists. Turning the fog up cannot produce one - the fog pass is a
''' screen-space tint by depth and has no idea where the lamps are.
'''
''' Geometry rather than a full screen quad, for the same reason a deferred
''' engine draws light volumes: a lamp only scatters inside its own radius, so
''' the rasteriser culls, and a pixel nowhere near a lamp is never shaded. With
''' the sphere clipped against the scene depth in the shader, the cost tracks
''' how much of the screen the lamps actually cover.
'''
''' Reads scene depth and the baked shadow cubes. No material, no G-buffer, no
''' new render target - fog has no albedo, normal or BRDF to look up.
''' </summary>
Public Class MapLampFog
    Implements IDisposable

    ReadOnly scene As MapScene

    Private sphere_vao As GLVertexArray
    Private sphere_vbo As GLBuffer
    Private sphere_verts As Integer

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>
    ''' Add every lamp's scattered light to the frame. Call AFTER the fog pass -
    ''' this is light in the air in front of what the fog has already tinted.
    ''' </summary>
    Public Sub Draw()
        If Not LAMP_FOG Then Return

        Dim cp = scene.cam_path
        If cp Is Nothing OrElse Not cp.loaded OrElse cp.lights Is Nothing OrElse cp.lights.Length = 0 Then Return

        build_sphere()
        If sphere_vao Is Nothing Then Return

        GL_PUSH_GROUP("MapLampFog::Draw")

        lampFogShader.Use()
        sphere_vao.Bind()

        MainFBO.gPosition.BindUnit(0)
        ' Always bound, even with no bake - a shadow sampler left unbound is an
        ' illegal state the driver reports on every draw, not a way to switch
        ' the test off. lamp_index does that, per lamp, below.
        ' The shadow CUBE. The baked volume was tried here and reverted: at
        ' 0.6 m a voxel it cannot hold the shadow of a lamp FIXTURE, which is
        ' the occluder a street lamp's god rays are made of.
        ' LAMP_SHADOW_ENABLED first, the same gate the surfaces use
        ' (upload_path_lights zeroes lamp_shadow_count when it is off). Without
        ' it, unticking "Lamp shadows" unshadowed the pools and left the shafts
        ' carved - and a cam-path reload while it was off skipped the re-bake,
        ' so the shafts kept sampling cubes baked for the OLD lamp positions.
        Dim have_vol = LAMP_SHADOW_ENABLED AndAlso
                       scene.lamp_shadow IsNot Nothing AndAlso
                       scene.lamp_shadow.ready AndAlso
                       scene.lamp_shadow.depth_tex IsNot Nothing
        If have_vol Then
            scene.lamp_shadow.depth_tex.BindUnit(1)
        Else
            modRender.lamp_shadow_stand_in().BindUnit(1)
        End If

        GL.Uniform1(lampFogShader("fog_gain"), LAMP_FOG_GAIN)
        GL.Uniform1(lampFogShader("fog_phase"), LAMP_FOG_PHASE)
        GL.Uniform1(lampFogShader("fog_density"), LAMP_FOG_DENSITY)
        GL.Uniform1(lampFogShader("fog_falloff"), LAMP_FOG_FALLOFF)
        GL.Uniform1(lampFogShader("fog_steps"), LAMP_FOG_STEPS)
        GL.Uniform1(lampFogShader("lamp_shadow_near"), MapLampShadow.NEAR_M)
        GL.Uniform1(lampFogShader("lamp_shadow_bias"), LAMP_SHADOW_BIAS)

        ' ADDITIVE. Scattering puts light into the air; it never covers what is
        ' behind it, so the shader writes alpha 0 and this is a straight add.
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One)

        ' Depth test OFF, and that is deliberate rather than an oversight. The
        ' sphere is a bounding volume, not a thing being drawn: the shader
        ' clips the march against the scene depth itself, which is exact, where
        ' the depth test would reject the whole sphere on one comparison at its
        ' surface. Depth WRITE off too - nothing here belongs in the buffer.
        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)

        ' Keep the FAR faces of the sphere - and note which mode that is.
        '
        ' The near faces fall behind the camera the moment it enters a lamp's
        ' radius, which is exactly when a shaft should fill the screen. Culling
        ' the wrong way is silent and looks like the whole feature is dead: at
        ' 68 m the effect worked and at 18.5 m - just inside a 19.9 m lamp - the
        ' frame came back BIT IDENTICAL to the pass being switched off.
        '
        ' CullFaceMode.Back is what leaves the far faces HERE, because
        ' build_sphere's winding runs the opposite way to the engine's Ccw
        ' front-face setting. Do not "correct" this to Front on the strength of
        ' the name; test it from inside a lamp.
        ' Set, not assumed. The decal pass sets FrontFace per decal from the
        ' sign of its matrix determinant and does not put Ccw back, and nothing
        ' between it and this pass resets it - so on a map whose last decal is
        ' mirrored the Back cull below kept the NEAR faces and the shafts died
        ' from inside a lamp, exactly the symptom described above.
        GL.FrontFace(FrontFaceDirection.Ccw)
        GL.Enable(EnableCap.CullFace)
        GL.CullFace(CullFaceMode.Back)

        Dim n = Math.Min(cp.lights.Length, MapLampShadow.MAX_LAMPS)
        For i = 0 To n - 1
            Dim l = cp.lights(i)
            ' The same world position the surface lighting and the bake use -
            ' the file's Y is metres ABOVE THE TERRAIN.
            Dim wx = l.pos.X
            Dim wz = l.pos.Z
            Dim wy = get_Y_at_XZ_fast(wx, wz) + l.pos.Y
            Dim r = Math.Max(0.1F, l.range_m)

            GL.Uniform3(lampFogShader("centre"), wx, wy, wz)
            GL.Uniform1(lampFogShader("radius"), r)
            GL.Uniform3(lampFogShader("lamp_pos"), wx, wy, wz)
            GL.Uniform1(lampFogShader("lamp_range"), r)
            GL.Uniform3(lampFogShader("lamp_color"), l.color.X, l.color.Y, l.color.Z)
            GL.Uniform1(lampFogShader("lamp_level"), l.level)
            ' -1 means "no cube for this lamp" and the march skips the shadow
            ' test rather than the lamp: a missing bake must not delete light.
            GL.Uniform1(lampFogShader("lamp_index"),
                        If(have_vol AndAlso i < scene.lamp_shadow.layers, i, -1))

            GL.DrawArrays(PrimitiveType.Triangles, 0, sphere_verts)
        Next

        GL.CullFace(CullFaceMode.Back)
        ' Put the blend FUNCTION back, not just the enable. One/One is additive,
        ' and leaving it set turned every later blended pass additive too - the
        ' minimap came out solid white. Disabling blending does not undo the
        ' function; the next pass to enable it inherits whatever was left here.
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Disable(EnableCap.Blend)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)

        lampFogShader.StopUse()

        GL_POP_GROUP()
    End Sub

    ' Its own sphere rather than MapCamPath's. That one is private to the debug
    ' overlay, and coupling a render pass to an overlay's lifetime is how the
    ' overlay's Dispose_gl came to destroy something the renderer still wanted.
    Private Sub build_sphere()
        If sphere_vao IsNot Nothing Then Return

        Const RINGS As Integer = 12
        Const SECTORS As Integer = 18
        Dim v As New List(Of Single)(RINGS * SECTORS * 6 * 3)

        For i = 0 To RINGS - 1
            Dim p0 = CSng(Math.PI) * i / RINGS
            Dim p1 = CSng(Math.PI) * (i + 1) / RINGS
            For j = 0 To SECTORS - 1
                Dim t0 = CSng(Math.PI * 2.0) * j / SECTORS
                Dim t1 = CSng(Math.PI * 2.0) * (j + 1) / SECTORS

                Dim a = sphere_point(p0, t0)
                Dim b = sphere_point(p1, t0)
                Dim c = sphere_point(p1, t1)
                Dim d = sphere_point(p0, t1)

                For Each p In {a, b, c, a, c, d}
                    v.Add(p.X) : v.Add(p.Y) : v.Add(p.Z)
                Next
            Next
        Next

        Dim arr = v.ToArray()
        sphere_verts = arr.Length \ 3

        sphere_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "lampFogSphere")
        sphere_vbo.Storage(arr.Length * 4, arr, BufferStorageFlags.None)

        sphere_vao = GLVertexArray.Create("lampFogVao")
        sphere_vao.VertexBuffer(0, sphere_vbo, IntPtr.Zero, 3 * 4)
        sphere_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        sphere_vao.AttribBinding(0, 0)
        sphere_vao.EnableAttrib(0)
    End Sub

    Private Shared Function sphere_point(phi As Single, theta As Single) As Vector3
        Dim sp = CSng(Math.Sin(phi))
        Return New Vector3(sp * CSng(Math.Cos(theta)),
                           CSng(Math.Cos(phi)),
                           sp * CSng(Math.Sin(theta)))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        sphere_vao?.Dispose()
        sphere_vao = Nothing
        sphere_vbo?.Dispose()
        sphere_vbo = Nothing
        GC.SuppressFinalize(Me)
    End Sub
End Class

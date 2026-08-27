Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Water bodies, straight from BWWa.
'''
''' The game ships the tessellated surface mesh - vertices, indices, and a per
''' body parameter block with the authored deep colour and fresnel curve - so
''' this draws exactly what the game draws, not a generated plane.
'''
''' Drawn forward, after the deferred resolve and SSR, blended over the lit
''' frame: water needs the scene behind it already shaded, and the G-buffer has
''' no slot for a transparent surface. Depth tests against the scene depth
''' (reversed-Z, so Greater) but does not write it.
''' </summary>
Public Class MapWater
    Implements IDisposable

    ReadOnly scene As MapScene

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WaterVertex
        Public pos As Vector3
        ''' <summary>The float at +20 of the source vertex - reads like a shore
        ''' or foam factor. Carried through for when it is understood.</summary>
        Public aux As Single
    End Structure

    Private Class Body
        Public idx_byte_offset As Integer
        Public idx_count As Integer
        Public base_vertex As Integer
        Public deep_color As Vector4
        Public fresnel_bias As Single
        Public fresnel_power As Single
        Public sun_power As Single
        Public sun_scale As Single
        Public sun_tint As Vector3
    End Class

    Private vertices_buffer As GLBuffer
    Private indices_buffer As GLBuffer
    Private vao As GLVertexArray
    Private ReadOnly bodies As New List(Of Body)

    ' The shared 8-frame ripple loop from maps/water/. Two frames bound per
    ' draw, blended by the fractional frame position.
    Private ripple(7) As GLTexture
    Private ReadOnly clock As New Stopwatch

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub Build()
        If cBWWa.bodies Is Nothing OrElse cBWWa.verts Is Nothing Then
            LogThis("Water: no bodies in BWWa (bodies={0} verts={1}) - nothing to build",
                    If(cBWWa.bodies Is Nothing, "nothing", CStr(cBWWa.bodies.Length)),
                    If(cBWWa.verts Is Nothing, "nothing", CStr(cBWWa.verts.Length)))
            Return
        End If

        ' Two triangles per body, from the authored corners. The X mirror for
        ' display swaps which source corner is min and which is max, so remap
        ' rather than assume.
        Dim varr As New List(Of WaterVertex)
        Dim iarr As New List(Of UInt32)
        For Each b In cBWWa.bodies
            Dim x0 = Math.Min(-b.bbox_min.X, -b.bbox_max.X)
            Dim x1 = Math.Max(-b.bbox_min.X, -b.bbox_max.X)
            Dim y = b.bbox_min.Y
            Dim z0 = b.bbox_min.Z
            Dim z1 = b.bbox_max.Z

            Dim base_v = varr.Count
            varr.Add(New WaterVertex With {.pos = New Vector3(x0, y, z0)})
            varr.Add(New WaterVertex With {.pos = New Vector3(x1, y, z0)})
            varr.Add(New WaterVertex With {.pos = New Vector3(x1, y, z1)})
            varr.Add(New WaterVertex With {.pos = New Vector3(x0, y, z1)})

            Dim base_i = iarr.Count
            For Each ix In {0UI, 1UI, 2UI, 0UI, 2UI, 3UI}
                iarr.Add(ix)
            Next

            bodies.Add(New Body With {
                .idx_byte_offset = base_i * 4,
                .idx_count = 6,
                .base_vertex = base_v,
                .deep_color = b.deep_color,
                .fresnel_bias = b.fresnel_bias,
                .fresnel_power = b.fresnel_power,
                .sun_power = b.sun_power,
                .sun_scale = b.sun_scale,
                .sun_tint = b.sun_tint})

            LogThis("Water: body at x {0:0}..{1:0}  z {2:0}..{3:0}  y {4:0.00}  colour ({5:0.00} {6:0.00} {7:0.00})",
                    x0, x1, z0, z1, y, b.deep_color.X, b.deep_color.Y, b.deep_color.Z)
        Next

        ' -------------------------------------------------------------------
        ' Outland water. The game fills the world past the map edge with its
        ' instanced water clipmap at the open water's level - any outland
        ' ground below it reads as sea or river, ground above it occludes.
        ' The stand-in: if any body touches or crosses the terrain footprint
        ' (that is what makes it OPEN water - closed ponds do not), lay a ring
        ' of sheets at that level over the whole outland, hole-cut at the
        ' footprint, clipped against body rects that poke past the edge so
        ' nothing coplanar double-draws. The outland heightmap (terrain-
        ' patched at the seam, authored seabed beyond) does the rest.
        If DONT_BLOCK_OUTLAND AndAlso map_scene.OUTLAND_LOADED AndAlso bodies.Count > 0 Then
            Const EDGE_EPS As Single = 2.0F
            Dim fmin = theMap.terrain_footprint_min
            Dim fmax = theMap.terrain_footprint_max
            Dim half = If(map_scene.terrain.CASCADE_LEVELS = 2,
                          New Vector2(theMap.far_scale.X, theMap.far_scale.Y) * 50.0F,
                          New Vector2(theMap.near_scale.X, theMap.near_scale.Y) * 50.0F)
            Dim omin = theMap.center_offset - half
            Dim omax = theMap.center_offset + half

            ' the open-water level and look: the lowest edge-touching body
            Dim open_body As Integer = -1
            Dim sea_y As Single = Single.MaxValue
            Dim body_rects As New List(Of Vector4)
            For e = 0 To cBWWa.bodies.Length - 1
                Dim b = cBWWa.bodies(e)
                Dim bx0 = Math.Min(-b.bbox_min.X, -b.bbox_max.X)
                Dim bx1 = Math.Max(-b.bbox_min.X, -b.bbox_max.X)
                Dim bz0 = b.bbox_min.Z
                Dim bz1 = b.bbox_max.Z
                body_rects.Add(New Vector4(bx0, bz0, bx1, bz1))
                If bx0 <= fmin.X + EDGE_EPS OrElse bx1 >= fmax.X - EDGE_EPS OrElse
                   bz0 <= fmin.Y + EDGE_EPS OrElse bz1 >= fmax.Y - EDGE_EPS Then
                    If b.bbox_min.Y < sea_y Then
                        sea_y = b.bbox_min.Y
                        open_body = e
                    End If
                End If
            Next

            If open_body >= 0 Then
                ' four strips around the footprint, out to the outland extent
                Dim strips As New List(Of Vector4) From {
                    New Vector4(omin.X, omin.Y, omax.X, fmin.Y),
                    New Vector4(omin.X, fmax.Y, omax.X, omax.Y),
                    New Vector4(omin.X, fmin.Y, fmin.X, fmax.Y),
                    New Vector4(fmax.X, fmin.Y, omax.X, fmax.Y)
                }
                ' subtract every body rect (only their outside-the-footprint
                ' overhangs actually intersect)
                For Each brc As Vector4 In body_rects
                    Dim out_list As New List(Of Vector4)
                    For Each sr As Vector4 In strips
                        If brc.X >= sr.Z OrElse brc.Z <= sr.X OrElse brc.Y >= sr.W OrElse brc.W <= sr.Y Then
                            out_list.Add(sr) ' no overlap
                            Continue For
                        End If
                        ' split the strip around the body rect
                        If brc.Y > sr.Y Then out_list.Add(New Vector4(sr.X, sr.Y, sr.Z, brc.Y))
                        If brc.W < sr.W Then out_list.Add(New Vector4(sr.X, brc.W, sr.Z, sr.W))
                        Dim mz0 = Math.Max(sr.Y, brc.Y)
                        Dim mz1 = Math.Min(sr.W, brc.W)
                        If brc.X > sr.X Then out_list.Add(New Vector4(sr.X, mz0, brc.X, mz1))
                        If brc.Z < sr.Z Then out_list.Add(New Vector4(brc.Z, mz0, sr.Z, mz1))
                    Next
                    strips = out_list
                Next

                Dim ob = cBWWa.bodies(open_body)
                Dim added = 0
                For Each sr As Vector4 In strips
                    If sr.Z - sr.X < 0.5F OrElse sr.W - sr.Y < 0.5F Then Continue For
                    Dim bv = varr.Count
                    varr.Add(New WaterVertex With {.pos = New Vector3(sr.X, sea_y, sr.Y)})
                    varr.Add(New WaterVertex With {.pos = New Vector3(sr.Z, sea_y, sr.Y)})
                    varr.Add(New WaterVertex With {.pos = New Vector3(sr.Z, sea_y, sr.W)})
                    varr.Add(New WaterVertex With {.pos = New Vector3(sr.X, sea_y, sr.W)})
                    Dim bi = iarr.Count
                    For Each ix In {0UI, 1UI, 2UI, 0UI, 2UI, 3UI}
                        iarr.Add(ix)
                    Next
                    bodies.Add(New Body With {
                        .idx_byte_offset = bi * 4,
                        .idx_count = 6,
                        .base_vertex = bv,
                        .deep_color = ob.deep_color,
                        .fresnel_bias = ob.fresnel_bias,
                        .fresnel_power = ob.fresnel_power,
                        .sun_power = ob.sun_power,
                        .sun_scale = ob.sun_scale,
                        .sun_tint = ob.sun_tint})
                    added += 1
                Next
                LogThis("Water: outland sheet at y {0:0.00} from body {1} - {2} strips over x {3:0}..{4:0} z {5:0}..{6:0}",
                        sea_y, open_body, added, omin.X, omax.X, omin.Y, omax.Y)
            End If
        End If

        Dim stride = Marshal.SizeOf(Of WaterVertex)
        Dim va = varr.ToArray()
        Dim ia = iarr.ToArray()

        vertices_buffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "waterVerts")
        vertices_buffer.Storage(va.Length * stride, va, BufferStorageFlags.None)

        indices_buffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "waterIndices")
        indices_buffer.Storage(ia.Length * 4, ia, BufferStorageFlags.None)

        vao = GLVertexArray.Create("waterVao")
        vao.VertexBuffer(0, vertices_buffer, IntPtr.Zero, stride)
        vao.ElementBuffer(indices_buffer)

        vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)

        vao.AttribFormat(1, 1, VertexAttribType.Float, False, 12)
        vao.AttribBinding(1, 0)
        vao.EnableAttrib(1)

        ' The shared ripple animation. Missing frames are tolerated - the shader
        ' just gets the same frame twice and the water sits still.
        For i = 0 To 7
            ripple(i) = TextureMgr.find_and_load_texture_from_pkgs_No_Suffix_change(
                String.Format("maps/water/ripple_short_8_frames_normal_animation/normal00{0}.dds", i), False)
        Next

        clock.Start()
        scene.WATER_LOADED = True
        LogThis("Water: {0} bodies as corner quads", bodies.Count)
    End Sub

    Public Sub draw()
        If Not scene.WATER_LOADED OrElse vao Is Nothing Then Return

        GL_PUSH_GROUP("draw_water")

        waterShader.Use()

        ' 8 frames looping at 8 fps, adjacent pair blended.
        Dim t = clock.Elapsed.TotalSeconds * 8.0
        Dim fi = CInt(Math.Floor(t)) Mod 8
        ripple(fi)?.BindUnit(0)
        ripple((fi + 1) Mod 8)?.BindUnit(1)
        scene.sky.CUBE_TEXTURE_ID?.BindUnit(2)
        GL.Uniform1(waterShader("frame_lerp"), CSng(t - Math.Floor(t)))
        GL.Uniform1(waterShader("water_y_offset"), WATER_Y_OFFSET)

        ' The rule, applied to water: no sun out of the sun's reach. The glint
        ' is gated by the same baked map everything else uses. Sky reflection
        ' stays in shadow - shade blocks the sun, not the sky.
        If scene.sun_shadow.ready AndAlso scene.sun_shadow.depth_tex IsNot Nothing Then
            scene.sun_shadow.depth_tex.BindUnit(3)
            GL.UniformMatrix4(waterShader("sunViewProj"), False, scene.sun_shadow.sun_view_proj)
            GL.Uniform1(waterShader("has_sun_shadow"), 1)
        Else
            GL.Uniform1(waterShader("has_sun_shadow"), 0)
        End If
        Dim sd = LIGHT_POS.Normalized()
        GL.Uniform3(waterShader("sun_dir"), sd.X, sd.Y, sd.Z)

        ' The lit frame and the view-space position buffer, for reflecting the
        ' geometry that is actually on screen. gColor is safe to read here -
        ' the pass writes gColor_2.
        MainFBO.gColor.BindUnit(4)
        MainFBO.gPosition.BindUnit(5)
        ' The flag channel: which kind of surface sits under each water pixel.
        MainFBO.gGMF.BindUnit(6)
        ' And its normal - the boat mask needs to tell decks from hull sides.
        MainFBO.gNormal.BindUnit(7)
        GL.Uniform1(waterShader("exclude_band"), WATER_EXCLUDE_BAND)

        ' Reversed-Z depth test against the scene, no write - water is the last
        ' thing into the frame and nothing tests against it.
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        GL.DepthMask(False)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        ' The x mirror at load reversed the winding; seen only from above anyway.
        GL.Disable(EnableCap.CullFace)

        vao.Bind()
        For Each b In bodies
            GL.Uniform4(waterShader("deep_color"), b.deep_color.X, b.deep_color.Y, b.deep_color.Z, b.deep_color.W)
            GL.Uniform2(waterShader("fresnel"), b.fresnel_bias, b.fresnel_power)
            GL.Uniform2(waterShader("sun_glint"), Math.Max(b.sun_power, 1.0F), b.sun_scale)
            GL.Uniform3(waterShader("sun_tint"), b.sun_tint.X, b.sun_tint.Y, b.sun_tint.Z)
            GL.DrawElementsBaseVertex(PrimitiveType.Triangles, b.idx_count,
                                      DrawElementsType.UnsignedInt,
                                      New IntPtr(b.idx_byte_offset), b.base_vertex)
        Next

        waterShader.StopUse()

        ' Back the way the tail of draw_scene expects it.
        GL.Enable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL.DepthFunc(DepthFunction.Less)
        GL.Disable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        vertices_buffer?.Dispose()
        indices_buffer?.Dispose()
        vao?.Dispose()
        For i = 0 To 7
            ripple(i) = Nothing ' owned by TextureMgr's cache
        Next
    End Sub
End Class


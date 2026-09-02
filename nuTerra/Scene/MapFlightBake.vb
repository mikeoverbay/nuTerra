Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Bakes the map down to the flat arrays the camera flight planner needs, and
''' writes them to %TEMP%\nuTerra\flight\ so the path algorithm can be built and
''' argued with offline before any of it is ported back in here.
'''
''' Three layers, from two depth-only passes straight down:
'''
'''   floor - terrain alone. The ground you would land on.
'''   top   - terrain plus models plus trees. The highest thing in the column.
'''   mask  - derived, top minus floor over a threshold. Obstacle or open.
'''
''' No new shaders. MapSunShadow already renders all three of those sets from an
''' orthographic view through sun_depth_terrain / _model / _tree, each of which
''' takes exactly one matrix and writes only depth. Point that matrix straight
''' down and the depth buffer IS a height field, so this class is a projection,
''' two clears and a readback.
'''
''' Differences from the sun bake, both deliberate:
'''   - 32 bit depth, not 16. These come back as metres and get differenced, so
'''     the precision here is the planner's clearance margin, not a shadow edge.
'''   - no polygon offset. The sun bake nudges depth to kill acne, which is
'''     exactly the bias we must not have when the depth IS the answer.
''' </summary>
Public Class MapFlightBake
    Implements IDisposable

    ReadOnly scene As MapScene

    ''' <summary>Texels on a side. 1024 over a ~1 km map is about a metre per
    ''' texel - fine enough that one lamppost cannot blank a whole cell, coarse
    ''' enough that both layers together are 8 MB.</summary>
    Public Const SIZE As Integer = 1024

    ''' <summary>Height above the terrain at which something counts as an
    ''' obstacle in the exported mask. The mask is for eyeballing only - the
    ''' planner gets top and floor and should threshold them itself, so it can
    ''' change its mind about what a 1 m kerb means without a re-bake.</summary>
    Public Const OBSTACLE_MIN_H As Single = 1.0F

    ''' <summary>Bake and export on every map load. On while the planner is
    ''' being written offline; turn it off once the algorithm moves in here and
    ''' the files stop being the interface.</summary>
    Public Const BAKE_AT_LOAD As Boolean = True

    Private fbo As GLFramebuffer
    Private depth_tex As GLTexture

    Public top_m(SIZE * SIZE - 1) As Single
    Public floor_m(SIZE * SIZE - 1) As Single
    Public ready As Boolean

    ' The world footprint the two arrays span, and the constants that turn a
    ' depth back into a height. Public because the export writes them out and
    ' the planner cannot index anything without them.
    Public wx_min, wx_max, wz_min, wz_max As Single
    Private eye_y As Single
    Private far_d As Single

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub Bake()
        ready = False

        ' The terrain's true world footprint, taken from the same expressions
        ' MapSunShadow uses - X has no offset, Z is shifted back one chunk. That
        ' asymmetry is real; deriving it by hand puts the centre half a chunk out.
        wx_min = 100.0F * b_x_min
        wx_max = 100.0F * (b_x_max + 1)
        wz_min = 100.0F * (b_y_min - 1)
        wz_max = 100.0F * b_y_max

        If wx_max - wx_min <= 0.0F OrElse wz_max - wz_min <= 0.0F Then
            LogThis("flight bake: map extent is zero - skipped")
            Return
        End If

        Dim cx = (wx_min + wx_max) * 0.5F
        Dim cz = (wz_min + wz_max) * 0.5F
        Dim half_w = (wx_max - wx_min) * 0.5F
        Dim half_h = (wz_max - wz_min) * 0.5F

        ' Straight down from clear above everything. For an orthographic
        ' projection the eye height changes no framing at all, only what near and
        ' far bracket, so it only has to clear the tallest model.
        eye_y = MAX_MAP_HEIGHT + 500.0F
        far_d = eye_y - (MIN_MAP_HEIGHT - 500.0F)

        Dim eye As New Vector3(cx, eye_y, cz)
        Dim view = Matrix4.LookAt(eye,
                                  New Vector3(cx, eye_y - 1.0F, cz),
                                  New Vector3(0.0F, 0.0F, 1.0F))

        ' Left and right are SWAPPED, the same reversal Ortho_MiniMap uses. The
        ' up vector above puts view x on -worldX, so without the swap the readback
        ' is mirrored and every column index the planner computes is off by a
        ' reflection - which looks perfectly plausible on a roughly symmetric map
        ' and is the kind of thing that gets found three days later.
        Dim proj = Matrix4.CreateOrthographicOffCenter(half_w, -half_w,
                                                       -half_h, half_h,
                                                       0.0F, far_d)

        ' ClipDepthMode.ZeroToOne - remap whatever -1..1 OpenTK produced onto
        ' 0..1, exactly as MapSunShadow does, since the same shaders run here.
        proj.M33 *= 0.5F
        proj.M43 = (proj.M43 + 1.0F) * 0.5F

        Dim vp = view * proj

        If depth_tex Is Nothing Then create_target()

        GL_PUSH_GROUP("flight_bake")

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, SIZE, SIZE)

        ' Plain depth ordering, not the reversed-Z the main pass uses. Both
        ' ClearDepth and DepthFunc are global, so they have to go back exactly as
        ' they were at the end or every later clear fails DepthFunc.Greater and
        ' the whole scene vanishes behind the sky.
        GL.ClearDepth(1.0)
        GL.DepthFunc(DepthFunction.Less)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        GL.Disable(EnableCap.CullFace)

        ' floor - the ground on its own
        GL.Clear(ClearBufferMask.DepthBufferBit)
        draw_terrain(vp)
        read_heights(floor_m)

        ' top - the ground and everything standing on it
        GL.Clear(ClearBufferMask.DepthBufferBit)
        draw_terrain(vp)
        draw_models(vp)
        draw_trees(vp)
        read_heights(top_m)

        GL.Enable(EnableCap.CullFace)
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        GL_POP_GROUP()

        ready = True

        LogThis("flight bake: {0}x{0} over {1:0} x {2:0} m ({3:0.00} m per texel), map heights {4:0}..{5:0} m",
                SIZE, wx_max - wx_min, wz_max - wz_min,
                (wx_max - wx_min) / SIZE, MIN_MAP_HEIGHT, MAX_MAP_HEIGHT)

        ' Terrain only, so this must run BEFORE the water goes in - it probes
        ' the floor against get_Y_at_XZ_fast, which knows nothing about water.
        verify_against_cpu()

        add_water()
        report_coverage()
        export()
    End Sub

    ''' <summary>
    ''' The world-to-texel mapping above is a DERIVATION, and a derivation is not
    ''' a measurement. Probe the floor map against the CPU height function at
    ''' asymmetric points and report the mean error for that mapping and for its
    ''' three reflections. If one of the reflections wins, the orientation is
    ''' wrong and this says which way; if all four are large, something further
    ''' up is wrong and no amount of flipping will fix it.
    ''' </summary>
    Private Sub verify_against_cpu()
        Dim names() As String = {"as-derived", "flip-x", "flip-z", "flip-both"}
        Dim err(3) As Double
        Dim n = 0

        For gz = 1 To 5
            For gx = 1 To 5
                Dim c = CInt((gx / 6.0) * (SIZE - 1))
                Dim r = CInt((gz / 6.0) * (SIZE - 1))
                Dim wx = wx_min + (c + 0.5F) * (wx_max - wx_min) / SIZE
                Dim wz = wz_max - (r + 0.5F) * (wz_max - wz_min) / SIZE
                Dim truth = get_Y_at_XZ_fast(wx, wz)

                err(0) += Math.Abs(floor_m(r * SIZE + c) - truth)
                err(1) += Math.Abs(floor_m(r * SIZE + (SIZE - 1 - c)) - truth)
                err(2) += Math.Abs(floor_m((SIZE - 1 - r) * SIZE + c) - truth)
                err(3) += Math.Abs(floor_m((SIZE - 1 - r) * SIZE + (SIZE - 1 - c)) - truth)
                n += 1
            Next
        Next

        Dim best = 0
        For i = 1 To 3
            If err(i) < err(best) Then best = i
        Next

        For i = 0 To 3
            LogThis("flight bake: probe error {0,-10} {1,8:0.000} m{2}",
                    names(i), err(i) / n, If(i = best, "   <- best", ""))
        Next

        If best <> 0 Then
            LogThis("flight bake: WRONG ORIENTATION - the mapping should be {0}", names(best))
        End If
    End Sub

    ''' <summary>
    ''' Raise floor and top to the water surface wherever a body covers a cell.
    '''
    ''' Water is a forward pass in MapWater and appears in NEITHER depth pass,
    ''' so without this the bake reports the LAKE BED. A quarter of Abbey has
    ''' terrain below y=0, and a flight planned 4 m over that floor is 4 m over
    ''' the bed - underwater, and nothing downstream could tell.
    '''
    ''' Both layers, for different reasons. FLOOR so that 'so many metres above
    ''' the ground' means above the surface you can actually see. TOP so that a
    ''' flight level below the surface is correctly blocked rather than reading
    ''' as open water.
    '''
    ''' Bodies are axis-aligned rectangles at a fixed height - MapWater.Build
    ''' makes each one two triangles from its bbox corners, with the same X
    ''' mirror applied - so this is a rectangle fill, not a rasteriser. The
    ''' mirror is repeated here rather than assumed away; getting it wrong puts
    ''' every lake on the opposite side of the map.
    ''' </summary>
    Private Sub add_water()
        If cBWWa.bodies Is Nothing OrElse cBWWa.bodies.Length = 0 Then
            LogThis("flight bake: no water bodies")
            Return
        End If

        Dim raised = 0
        Dim wsum = 0.0
        For Each b In cBWWa.bodies
            Dim x0 = Math.Min(-b.bbox_min.X, -b.bbox_max.X)
            Dim x1 = Math.Max(-b.bbox_min.X, -b.bbox_max.X)
            Dim z0 = Math.Min(b.bbox_min.Z, b.bbox_max.Z)
            Dim z1 = Math.Max(b.bbox_min.Z, b.bbox_max.Z)
            Dim y = b.bbox_min.Y

            ' world -> texel, the mapping the exported header documents
            Dim c0 = CInt(Math.Floor((x0 - wx_min) / (wx_max - wx_min) * SIZE))
            Dim c1 = CInt(Math.Ceiling((x1 - wx_min) / (wx_max - wx_min) * SIZE))
            Dim r0 = CInt(Math.Floor((wz_max - z1) / (wz_max - wz_min) * SIZE))
            Dim r1 = CInt(Math.Ceiling((wz_max - z0) / (wz_max - wz_min) * SIZE))

            c0 = Math.Max(0, c0) : c1 = Math.Min(SIZE, c1)
            r0 = Math.Max(0, r0) : r1 = Math.Min(SIZE, r1)

            For r = r0 To r1 - 1
                Dim row = r * SIZE
                For c = c0 To c1 - 1
                    Dim i = row + c
                    If y > floor_m(i) Then
                        floor_m(i) = y
                        raised += 1
                    End If
                    If y > top_m(i) Then top_m(i) = y
                Next
            Next
            wsum += y
        Next

        LogThis("flight bake: {0} water bodies raised {1} cells ({2:0.00}% of the map), mean surface {3:0.0} m",
                cBWWa.bodies.Length, raised, 100.0 * raised / (SIZE * SIZE),
                wsum / Math.Max(1, cBWWa.bodies.Length))
    End Sub

    ''' <summary>How much of the map the mask calls blocked, and how tall the
    ''' blocking is. A number to sanity check the bake against the one-off mask,
    ''' which came out around 25 percent on Abbey.</summary>
    Private Sub report_coverage()
        Dim empty_h = eye_y - far_d + 1.0F
        Dim blocked = 0, no_data = 0
        Dim tallest As Single = 0.0F

        For i = 0 To SIZE * SIZE - 1
            If floor_m(i) < empty_h Then
                no_data += 1
            ElseIf top_m(i) - floor_m(i) > OBSTACLE_MIN_H Then
                blocked += 1
                tallest = Math.Max(tallest, top_m(i) - floor_m(i))
            End If
        Next

        LogThis("flight bake: {0:0.0}% blocked, {1:0.0}% no terrain, tallest obstacle {2:0.0} m",
                100.0 * blocked / (SIZE * SIZE),
                100.0 * no_data / (SIZE * SIZE),
                tallest)
    End Sub

    Private Sub read_heights(dst() As Single)
        Dim d(SIZE * SIZE - 1) As Single
        GL.GetTextureImage(depth_tex.texture_id, 0,
                           OpenGL4.PixelFormat.DepthComponent, PixelType.Float,
                           d.Length * 4, d)

        ' GL hands back row 0 = bottom = wz_min. Flip on the way out so row 0 is
        ' the wz_max edge - then the array reads like the picture you would draw
        ' of it, north up, and nobody downstream has to remember a convention.
        For r = 0 To SIZE - 1
            Dim src = (SIZE - 1 - r) * SIZE
            Dim dst_row = r * SIZE
            For c = 0 To SIZE - 1
                dst(dst_row + c) = eye_y - d(src + c) * far_d
            Next
        Next
    End Sub

    Private Sub draw_terrain(vp As Matrix4)
        If Not scene.TERRAIN_LOADED Then Return

        sunDepthTerrainShader.Use()
        GL.UniformMatrix4(sunDepthTerrainShader("sunViewProj"), False, vp)

        scene.terrain.all_chunks_vao.Bind()
        scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        For i = 0 To theMap.render_set.Length - 1
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                                    New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
        Next

        sunDepthTerrainShader.StopUse()
    End Sub

    Private Sub draw_models(vp As Matrix4)
        If Not scene.MODELS_LOADED OrElse Not DONT_BLOCK_MODELS Then Return

        sunDepthModelShader.Use()
        GL.UniformMatrix4(sunDepthModelShader("sunViewProj"), False, vp)

        scene.static_models.allMapModels.Bind()
        scene.static_models.indirect_shadow_mapping.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                                     IntPtr.Zero, scene.static_models.indirectShadowMappingDrawCount, 0)

        sunDepthModelShader.StopUse()
    End Sub

    Private Sub draw_trees(vp As Matrix4)
        If Not scene.TREES_LOADED OrElse Not DONT_BLOCK_TREES Then Return
        scene.trees.sun_depth_pass(vp)
    End Sub

    Private Sub create_target()
        depth_tex = GLTexture.Create(TextureTarget.Texture2D, "FlightBakeDepth")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        depth_tex.Storage2D(1, DirectCast(InternalFormat.DepthComponent32f, SizedInternalFormat), SIZE, SIZE)

        fbo = GLFramebuffer.Create("FlightBakeFBO")
        fbo.Texture(FramebufferAttachment.DepthAttachment, depth_tex, 0)
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.None)
        GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)

        If Not fbo.IsComplete Then
            LogThis("flight bake: FBO incomplete at {0}x{0}", SIZE)
        End If
    End Sub

    Private Sub export()
        Try
            Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "flight")
            IO.Directory.CreateDirectory(dir)
            Dim stem = IO.Path.Combine(dir, MAP_NAME_NO_PATH)

            write_r32(stem & "_top.r32", top_m)
            write_r32(stem & "_floor.r32", floor_m)
            write_mask_png(stem & "_mask.png")
            write_meta(stem & "_meta.txt")

            LogThis("flight bake: exported {0}_top.r32 / _floor.r32 / _mask.png / _meta.txt to {1}",
                    MAP_NAME_NO_PATH, dir)
        Catch ex As Exception
            LogThis("flight bake: export FAILED: {0}", ex.Message)
        End Try
    End Sub

    Private Shared Sub write_r32(path As String, a() As Single)
        Dim b(a.Length * 4 - 1) As Byte
        System.Buffer.BlockCopy(a, 0, b, 0, b.Length)
        IO.File.WriteAllBytes(path, b)
    End Sub

    Private Sub write_mask_png(path As String)
        Dim px(SIZE * SIZE * 4 - 1) As Byte
        For i = 0 To SIZE * SIZE - 1
            Dim v As Byte = If(top_m(i) - floor_m(i) > OBSTACLE_MIN_H, CByte(255), CByte(0))
            px(i * 4 + 0) = v
            px(i * 4 + 1) = v
            px(i * 4 + 2) = v
            px(i * 4 + 3) = 255
        Next

        ' row 0 already holds the wz_max edge, and GDI+ row 0 is the top of the
        ' image, so this lands north up with no further flipping.
        Using bmp As New Drawing.Bitmap(SIZE, SIZE, Drawing.Imaging.PixelFormat.Format32bppArgb)
            Dim bd = bmp.LockBits(New Drawing.Rectangle(0, 0, SIZE, SIZE),
                                  Drawing.Imaging.ImageLockMode.WriteOnly,
                                  Drawing.Imaging.PixelFormat.Format32bppArgb)
            Marshal.Copy(px, 0, bd.Scan0, px.Length)
            bmp.UnlockBits(bd)
            bmp.Save(path, Drawing.Imaging.ImageFormat.Png)
        End Using
    End Sub

    Private Sub write_meta(path As String)
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim sb As New Text.StringBuilder

        sb.AppendLine("# nuTerra flight bake")
        sb.AppendLine("map=" & MAP_NAME_NO_PATH)
        sb.AppendLine("width=" & SIZE)
        sb.AppendLine("height=" & SIZE)
        sb.AppendLine(String.Format(inv, "wx_min={0:0.000}", wx_min))
        sb.AppendLine(String.Format(inv, "wx_max={0:0.000}", wx_max))
        sb.AppendLine(String.Format(inv, "wz_min={0:0.000}", wz_min))
        sb.AppendLine(String.Format(inv, "wz_max={0:0.000}", wz_max))
        sb.AppendLine(String.Format(inv, "empty={0:0.000}", eye_y - far_d))
        sb.AppendLine(String.Format(inv, "obstacle_min_h={0:0.000}", OBSTACLE_MIN_H))
        sb.AppendLine("#")
        sb.AppendLine("# top.r32   highest surface - terrain, models and trees")
        sb.AppendLine("# floor.r32 terrain alone")
        sb.AppendLine("# both are float32 little endian, row major, width*height, in metres")
        sb.AppendLine("# a cell at or below 'empty' means nothing rasterised there")
        sb.AppendLine("#")
        sb.AppendLine("# row 0 is the wz_max edge, rows increase toward wz_min")
        sb.AppendLine("# col 0 is the wx_min edge, cols increase toward wx_max")
        sb.AppendLine("# world_x = wx_min + (col + 0.5) * (wx_max - wx_min) / width")
        sb.AppendLine("# world_z = wz_max - (row + 0.5) * (wz_max - wz_min) / height")

        IO.File.WriteAllText(path, sb.ToString())
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        depth_tex?.Dispose()
        fbo?.Dispose()
        GC.SuppressFinalize(Me)
    End Sub
End Class

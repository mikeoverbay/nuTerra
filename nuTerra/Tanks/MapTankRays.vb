Imports System.Reflection
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Every driving tank's ray to where it is going, drawn on the ground, rebuilt
''' from scratch every frame.
'''
''' WHY THIS EXISTS. Routing reached the owner as log lines - "3/4 moving, 1
''' stuck, ground 1" - and his answer to that was "I am blind here on this
''' routing". A count cannot show a shape. Whether two tanks are aimed through
''' the same gap, whether a hull is driving at something behind a wall, whether
''' the one that is stuck is wedged against a rock or against another tank: all
''' of that is geometry, and none of it survives being turned into a number.
'''
''' REBUILT EVERY FRAME, ON PURPOSE. "just draw the rays each frame. Speed is not
''' a concern." A cache would have to be invalidated by every goal change, every
''' arrival, every re-pick and every hot rebuild, and a stale overlay of a live
''' system is worse than none - it would be believed. Thirty hulls is a few
''' thousand vertices and the terrain samples are a lookup each, so the honest
''' thing is also the cheap one.
'''
''' IT ASKS NOTHING OF THE TANK AI SIDE. Master has no route array - a hull
''' carries one `goal`, and TankDrive says the catalogue "slots in at pick_goal,
''' everything else stays". So a ray from hull to current goal is the whole truth
''' on master AND the current leg of a corridor on the branch that has one, off
''' the same two public fields either way. Nothing to expose, nothing to keep in
''' step, nobody waiting on anybody.
'''
''' ON THE GROUND rather than straight through the air: the line is cut every
''' SEG_M and each vertex set just above the terrain under it. A straight segment
''' between two ground points buries itself in every ridge between them, and with
''' the depth test off it would draw over the hill it is inside - which reads as a
''' route THROUGH the hill. Following the ground also shows what the route
''' crosses, which is most of what the picture is for.
'''
''' Uses campathShader as it stands - same vertex format, vec3 position and vec4
''' colour, and its geometry stage already widens a segment to a constant pixel
''' width, which is what stops a distant ray thinning to nothing. Read the
''' comment in MapCamPath.DrawPath before changing how this is composited: the
''' two-pass version that ghosts where occluded is a genuine height cue and it
''' cannot survive the camera pulling back, because a metre of clearance is well
''' under a pixel at map scale, so the solid pass loses the depth test along more
''' and more of its length and the whole thing fades toward the ghost. One pass,
''' no depth test, over everything.
''' </summary>
Public Class MapTankRays
    Implements IDisposable

    Private ReadOnly map_scene As MapScene

    Private vao As GLVertexArray
    Private vbo As GLBuffer

    ' Cached in-game TankNav overlay. The baked/nav geometry is static, so do
    ' not rebuild a million-cell grid every frame. Rebuild only when the TankNav
    ' object changes.
    Private nav_vao As GLVertexArray
    Private nav_vbo As GLBuffer
    Private nav_vert_count As Integer
    Private nav_cached As TankNav

    ' Raw flight-bake obstacle-height overlay. These markers are placed at the
    ' actual bake texel that produced the height, not at the coarse TankNav cell.
    Private height_vao As GLVertexArray
    Private height_vbo As GLBuffer
    Private height_vert_count As Integer
    Private height_cached As MapFlightBake

    ''' <summary>Vertices the buffer can hold. Grown, never shrunk.</summary>
    Private cap_verts As Integer

    ''' <summary>Whether the one-off count has been logged.</summary>
    Private said As Boolean
    Private saidNavMissing As Boolean

    ''' <summary>CPU staging, reused between frames so a per-frame rebuild does
    ''' not also mean a per-frame allocation of a few hundred kilobytes.</summary>
    Private verts() As Single

    Private Const FLOATS_PER_VERT As Integer = 7      ' vec3 pos + vec4 colour
    Private Const SEG_M As Single = 4.0F              ' ground sample spacing
    Private Const LIFT_M As Single = 0.7F             ' above the terrain
    Private Const MAX_SEG As Integer = 256            ' per ray, a bound not a budget
    Private Const CROSS_M As Single = 5.0F            ' arm of the goal marker
    Private Const LINE_PX As Single = 2.0F
    Private Const NAV_LINE_PX As Single = 1.2F          ' in-game nav outline
    Private Const NAV_LIFT_M As Single = 0.35F          ' above terrain
    Private Const HEIGHT_LINE_PX As Single = 2.0F       ' raw bake height markers

    ''' <summary>
    ''' Blank the few metres nearest the eye. Standing beside a hull, its own ray
    ''' leaves the camera at point blank and lies down the middle of the screen,
    ''' hiding the tanks it was drawn to explain. Same trick as the camera path's,
    ''' and unlike that one it is wanted whether flying or not, because the
    ''' interesting view here is from down among them.
    ''' </summary>
    Private Const HIDE_NEAR As Single = 2.0F
    Private Const HIDE_FAR As Single = 7.0F

    Public Sub New(scene As MapScene)
        map_scene = scene
    End Sub

    ''' <summary>
    ''' One ray a hull, plus a cross on the spot it is driving at.
    '''
    ''' Colour says which team, except when a hull is wedged, and then it says
    ''' that instead - amber wins over the team colour. A stuck tank is the thing
    ''' being looked for, and on screen that is worth more than whose it is, which
    ''' the hull itself already shows.
    ''' </summary>
    Public Sub Draw()
        TankProbe.StartPass("tank rays")
        DrawInner()
        TankProbe.StopPass("tank rays")
    End Sub

    Private Sub DrawInner()
        If map_scene Is Nothing OrElse map_scene.tanks Is Nothing Then Return
        If Not map_scene.tanks.HasTanks Then Return
        If Not map_scene.TERRAIN_LOADED Then Return

        Dim live = map_scene.tanks.instances
        If live Is Nothing OrElse live.Count = 0 Then Return

        ' The same TankNav the tanks drive against.
        Dim nav = loaded_nav()
        If nav Is Nothing AndAlso Not saidNavMissing Then
            saidNavMissing = True
            LogThis("tank rays: TankNav not found on MapTanks/MapScene - nav overlay and map-hit colours disabled")
        End If

        ' Global debug switch supplied by Global Vars.
        ' True  = draw yellow TankNav boundaries + cyan raw height markers.
        ' False = hide both overlays. Ray diagnostics keep their own controls.
        Dim showNavMap As Boolean = NAVMAP_Visable

        If showNavMap Then
            refresh_nav_overlay(nav)

            ' Raw flight-bake height data that TankNav was built from.
            Dim bake = loaded_bake()
            refresh_height_overlay(bake, nav)
        End If

        ' Count first, then fill. A ray's LENGTH decides how many segments it
        ' takes, so the total is not a function of the hull count alone.
        Dim want = 0
        If TankSim.SIM_SHOW_GOAL Then
            For Each t In live
                If t Is Nothing OrElse Not t.drive.hasGoal Then Continue For
                want += segments_for(t) * 2 + 4      ' the ray, then the cross
            Next
        End If
        ' THE AVOIDANCE RAYS ARE STRAIGHT AND SHORT, so two vertices each and
        ' no following the ground - over three metres the terrain under a hull
        ' does not bend enough to bury a line, and cutting them into segments
        ' would quadruple the buffer for a picture nobody could tell apart.
        Dim rayHulls = 0
        If TankSim.SIM_SHOW_RAYS Then
            For Each t In live
                If t Is Nothing Then Continue For
                rayHulls += 1
            Next
            want += rayHulls * TankSim.RAY_COUNT * 2
        End If
        ' The whole graph from the file, plus the run each hull is on over it.
        If TankSim.SIM_SHOW_PATHS Then
            want += TankSim.lines.Count * 2 + TankSim.startPts.Count * 8
            If TankSim.SIM_RUN Then
                For Each t In live
                    If t Is Nothing Then Continue For
                    Dim rr = TankSim.RunOf(t)
                    If rr IsNot Nothing AndAlso rr.Count > 1 Then want += (rr.Count - 1) * 2
                Next
            End If
        End If
        ' NOT "no goals, nothing to draw" any more - the file's own lines are
        ' worth drawing with every hull sitting still, which is exactly the
        ' state before the sim is started.
        If want = 0 AndAlso
           (Not showNavMap OrElse
            (nav_vert_count = 0 AndAlso height_vert_count = 0)) Then Return

        If want > 0 AndAlso
           (verts Is Nothing OrElse verts.Length < want * FLOATS_PER_VERT) Then
            ReDim verts(want * FLOATS_PER_VERT * 2 - 1)
        End If

        Dim n = 0, rays = 0, amber = 0
        For Each t In live
            If Not TankSim.SIM_SHOW_GOAL Then Exit For
            If t Is Nothing OrElse Not t.drive.hasGoal Then Continue For
            rays += 1

            Dim c As Vector4
            If t.drive.stuckS > TankDriveTune.STUCK_S Then
                amber += 1
                c = New Vector4(1.0F, 0.72F, 0.12F, 1.0F)
            ElseIf t.team = TankTeam.Green Then
                ' The two colours the base rings already use, so a ray and the
                ' ring it is heading for read as belonging to each other.
                c = New Vector4(0.28F, 1.0F, 0.38F, 0.85F)
            Else
                c = New Vector4(1.0F, 0.34F, 0.3F, 0.85F)
            End If

            Dim x0 = t.position.X, z0 = t.position.Z
            Dim x1 = t.drive.goal.X, z1 = t.drive.goal.Y   ' Vector2 is XZ, not XY
            Dim segs = segments_for(t)

            ' GL_LINES, so every segment is its own pair. A strip would be half
            ' the vertices and would also join one tank's ray to the next tank's
            ' in one unbroken line.
            Dim px = x0, pz = z0, py = ground(x0, z0)
            For i = 1 To segs
                Dim f = CSng(i) / CSng(segs)
                Dim qx = x0 + (x1 - x0) * f
                Dim qz = z0 + (z1 - z0) * f
                Dim qy = ground(qx, qz)
                n = put(n, px, py, pz, c)
                n = put(n, qx, qy, qz, c)
                px = qx : py = qy : pz = qz
            Next

            ' The cross marks the goal itself. Without it a tank sitting almost on
            ' its goal has a ray too short to see, which looks the same as a tank
            ' with nowhere to go - and those are opposite conditions.
            Dim gy = ground(x1, z1)
            n = put(n, x1 - CROSS_M, gy, z1, c)
            n = put(n, x1 + CROSS_M, gy, z1, c)
            n = put(n, x1, gy, z1 - CROSS_M, c)
            n = put(n, x1, gy, z1 + CROSS_M, c)
        Next

        ' ---- the run each hull is on -------------------------------------
        ' RAY STUDIO'S PATH, not the route catalogue. Drawn from the hull's
        ' own assignment so what is on screen is what the tank is actually
        ' steering down, leg by leg.
        '
        ' BRIGHT AHEAD, DIM BEHIND. A path drawn at one weight says where the
        ' road goes; this says how far along it the tank has got, which is the
        ' question being asked while a sim runs. Twenty per cent alpha for the
        ' part already driven is enough to see the shape without competing
        ' with the part that still matters.
        ' THE FILE ITSELF, drawn on load and whether or not the sim is running.
        ' Dim, because it is the map rather than the action - the run a hull is
        ' actually driving is drawn over the top at full weight.
        If TankSim.SIM_SHOW_PATHS Then
            For Each ln In TankSim.lines
                Dim c As Vector4
                Select Case ln.Item3
                    Case 1 : c = New Vector4(0.31F, 0.92F, 0.43F, 0.45F)
                    Case 2 : c = New Vector4(1.0F, 0.27F, 0.27F, 0.45F)
                    Case 3 : c = New Vector4(0.88F, 0.75F, 1.0F, 0.5F)
                    Case Else : c = New Vector4(0.78F, 0.8F, 0.84F, 0.4F)
                End Select
                Dim a = ln.Item1, b = ln.Item2
                n = put(n, a.X, ground(a.X, a.Y) + 0.15F, a.Y, c)
                n = put(n, b.X, ground(b.X, b.Y) + 0.15F, b.Y, c)
            Next
            ' A cross on every start, in its side's colour.
            For Each sp In TankSim.startPts
                Dim c = If(sp.Item2 = 2,
                           New Vector4(1.0F, 0.27F, 0.27F, 1.0F),
                           New Vector4(0.31F, 0.92F, 0.43F, 1.0F))
                Dim p = sp.Item1
                Dim gy = ground(p.X, p.Y) + 0.3F
                n = put(n, p.X - CROSS_M, gy, p.Y, c)
                n = put(n, p.X + CROSS_M, gy, p.Y, c)
                n = put(n, p.X, gy, p.Y - CROSS_M, c)
                n = put(n, p.X, gy, p.Y + CROSS_M, c)
            Next
        End If

        If TankSim.SIM_SHOW_PATHS AndAlso TankSim.SIM_RUN Then
            For Each t In live
                If t Is Nothing Then Continue For
                Dim rr = TankSim.RunOf(t)
                If rr Is Nothing OrElse rr.Count < 2 Then Continue For
                Dim at_ = TankSim.RunAt(t)
                Dim warm = (t.team = TankTeam.Green)
                For k = 0 To rr.Count - 2
                    Dim done = (k < at_)
                    Dim cc As Vector4
                    If warm Then
                        cc = New Vector4(0.28F, 1.0F, 0.38F, If(done, 0.18F, 0.9F))
                    Else
                        cc = New Vector4(1.0F, 0.34F, 0.3F, If(done, 0.18F, 0.9F))
                    End If
                    Dim ax = rr(k).X, az = rr(k).Y
                    Dim bx = rr(k + 1).X, bz = rr(k + 1).Y
                    n = put(n, ax, ground(ax, az) + 0.25F, az, cc)
                    n = put(n, bx, ground(bx, bz) + 0.25F, bz, cc)
                Next
            Next
        End If

        ' ---- the avoidance rays ------------------------------------------
        ' Drawn for every hull, driving or not: a tank that has stopped is
        ' exactly the one whose neighbours matter, and drawing only the moving
        ' ones would hide the jam being diagnosed.
        If TankSim.SIM_SHOW_RAYS Then
            ' COLOUR IS DIAGNOSTIC:
            '   pale blue = clear
            '   red       = another tank
            '   yellow    = baked/nav-map impassable ground/scenery/off-map
            '
            ' Map colours are display-only. They do NOT feed back into TankDrive.
            Dim clearC As New Vector4(0.55F, 0.85F, 1.0F, 0.5F)
            Dim tankHitC As New Vector4(1.0F, 0.25F, 0.2F, 1.0F)
            Dim mapHitC As New Vector4(1.0F, 1.0F, 0.0F, 1.0F)

            For Each t In live
                If t Is Nothing Then Continue For

                Dim ry = ground(t.position.X, t.position.Z) + 0.35F
                Dim tankHits = TankSim.RayHits(t, live)
                Dim hullR = TankSim.HullRays(t)
                Dim mapHits = ray_map_hits(nav, hullR)

                For i = 0 To hullR.Count - 1
                    Dim o = hullR(i).Item1, d = hullR(i).Item2

                    Dim cc As Vector4
                    If i < mapHits.Length AndAlso mapHits(i) Then
                        cc = mapHitC
                    ElseIf i < tankHits.Length AndAlso tankHits(i) Then
                        cc = tankHitC
                    Else
                        cc = clearC
                    End If

                    Dim reach = TankSim.RayLen(i)
                    n = put(n, o.X, ry, o.Y, cc)
                    n = put(n, o.X + d.X * reach, ry, o.Y + d.Y * reach, cc)
                Next
            Next
        End If

        Dim vcount = n \ FLOATS_PER_VERT
        If vcount = 0 AndAlso
           (Not showNavMap OrElse
            (nav_vert_count = 0 AndAlso height_vert_count = 0)) Then Return
        If vcount > 0 Then ensure_buffer(vcount)

        ' ONCE, on the first frame that actually has something to draw.
        If Not said Then
            said = True
            LogThis("tank rays: {0} ray(s), {1} dynamic vertice(s), {2} nav vertice(s), {3} height vertice(s), {4} amber",
                    rays, vcount,
                    If(showNavMap, nav_vert_count, 0),
                    If(showNavMap, height_vert_count, 0),
                    amber)
        End If

        GL_PUSH_GROUP("MapTankRays::Draw")
        If vcount > 0 Then vbo.SubData(IntPtr.Zero, n * 4, verts)

        campathShader.Use()
        GL.Uniform2(campathShader("viewport"), CSng(MainFBO.width), CSng(MainFBO.height))
        GL.Uniform1(campathShader("alpha_mul"), 1.0F)

        Dim eye = map_scene.camera.CAM_POSITION
        GL.Uniform3(campathShader("hide_from"), eye.X, eye.Y, eye.Z)
        GL.Uniform1(campathShader("hide_near"), HIDE_NEAR)
        GL.Uniform1(campathShader("hide_far"), HIDE_FAR)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Disable(EnableCap.DepthTest)

        ' Draw RAW FLIGHT-BAKE HEIGHT first:
        '   cyan vertical markers = actual baked obstacle height at the raw texel
        '   from floor_m to top_m, only where height exceeds MAX_OBSTACLE.
        If showNavMap AndAlso
           height_vert_count > 0 AndAlso height_vao IsNot Nothing Then
            height_vao.Bind()
            GL.Uniform1(campathShader("line_px"), HEIGHT_LINE_PX)
            GL.DrawArrays(PrimitiveType.Lines, 0, height_vert_count)
        End If

        ' Then draw the derived TankNav map:
        '   yellow outlines = baked/nav impassable regions
        If showNavMap AndAlso
           nav_vert_count > 0 AndAlso nav_vao IsNot Nothing Then
            nav_vao.Bind()
            GL.Uniform1(campathShader("line_px"), NAV_LINE_PX)
            GL.DrawArrays(PrimitiveType.Lines, 0, nav_vert_count)
        End If

        ' Paths, goals and avoidance rays draw over the diagnostics.
        If vcount > 0 Then
            vao.Bind()
            GL.Uniform1(campathShader("line_px"), LINE_PX)
            GL.DrawArrays(PrimitiveType.Lines, 0, vcount)
        End If

        ' Put back what was borrowed. Everything after this expects the depth test
        ' on and blending off - the tank markers draw next.
        GL.Enable(EnableCap.DepthTest)
        GL.Disable(EnableCap.Blend)
        campathShader.StopUse()
        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Build the RAW flight-bake obstacle-height overlay once per loaded bake.
    '''
    ''' Cyan vertical lines are placed at the actual bake texel position. Their
    ''' bottom is floor_m and their top is top_m, so their visible length is the
    ''' measured obstacle height that TankNav uses for BLOCKED.
    '''
    ''' To keep this readable, only the strongest blocking texel in each TankNav
    ''' cell is drawn. That preserves the exact blocker position while avoiding
    ''' up to 64 cyan lines inside every 1.4 m nav cell.
    ''' </summary>
    Private Sub refresh_height_overlay(bake As MapFlightBake, nav As TankNav)
        If bake Is Nothing OrElse Not bake.ready OrElse nav Is Nothing OrElse Not nav.ready Then
            height_vert_count = 0
            height_cached = Nothing
            Return
        End If

        If bake Is height_cached AndAlso height_vao IsNot Nothing Then Return

        height_cached = bake
        build_height_overlay(bake, nav)
    End Sub

    Private Sub build_height_overlay(bake As MapFlightBake, nav As TankNav)
        Dim data As New List(Of Single)
        Dim cyan As New Vector4(0.0F, 1.0F, 1.0F, 0.9F)

        Dim BN = MapFlightBake.SIZE
        Dim N = TankNav.SIZE
        Dim tpc = TankNav.CELL_TEXELS

        Dim dxw = (bake.wx_max - bake.wx_min) / BN
        Dim dzw = (bake.wz_max - bake.wz_min) / BN

        For cz = 0 To N - 1
            Dim r0 = cz * tpc
            For cx = 0 To N - 1
                ' Height markers are only relevant to cells whose derived nav
                ' result includes BLOCKED. STEEP/WATER/OFFMAP have other causes.
                Dim nf = nav.cell(cz * N + cx)
                If (nf And TankNav.BLOCKED) = 0 Then Continue For

                Dim c0 = cx * tpc
                Dim bestI = -1
                Dim bestH = TankNavLimits.MAX_OBSTACLE

                For dr = 0 To tpc - 1
                    Dim rr = r0 + dr
                    Dim row = rr * BN + c0
                    For dc = 0 To tpc - 1
                        Dim cc = c0 + dc
                        Dim i = row + dc

                        Dim fl = bake.floor_m(i)
                        Dim h = bake.top_m(i) - fl
                        If h <= bestH Then Continue For

                        ' Match TankNav.Build's crushable rule. Fence/prop do not
                        ' block by height; a tree blocks by height only when the
                        ' bake says solid geometry exists under the canopy.
                        Dim k = bake.kind_b(i)
                        Dim k7 = k And MapFlightBake.KIND_MASK
                        Dim crushable =
                            (k7 = MapFlightBake.KIND_FENCE OrElse
                             k7 = MapFlightBake.KIND_PROP) OrElse
                            (k7 = MapFlightBake.KIND_TREE AndAlso
                             (k And MapFlightBake.SOLID_BIT) = 0)

                        If crushable Then Continue For

                        bestH = h
                        bestI = i
                    Next
                Next

                If bestI < 0 Then Continue For

                Dim br = bestI \ BN
                Dim bc = bestI Mod BN
                Dim wx = bake.wx_min + (bc + 0.5F) * dxw
                Dim wz = bake.wz_max - (br + 0.5F) * dzw
                Dim y0 = bake.floor_m(bestI) + 0.12F
                Dim y1 = bake.top_m(bestI) + 0.12F

                height_vert(data, wx, y0, wz, cyan)
                height_vert(data, wx, y1, wz, cyan)

                ' Small horizontal cap at the baked top so a short vertical line
                ' remains visible from a high/map camera.
                Dim cap = Math.Max(dxw * 3.0F, 0.35F)
                height_vert(data, wx - cap, y1, wz, cyan)
                height_vert(data, wx + cap, y1, wz, cyan)
                height_vert(data, wx, y1, wz - cap, cyan)
                height_vert(data, wx, y1, wz + cap, cyan)
            Next
        Next

        height_vert_count = data.Count \ FLOATS_PER_VERT

        If height_vbo IsNot Nothing Then height_vbo.Dispose()
        If height_vao IsNot Nothing Then height_vao.Dispose()
        height_vbo = Nothing
        height_vao = Nothing

        If height_vert_count = 0 Then Return

        Dim a = data.ToArray()
        height_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tankHeightOverlayVerts")
        height_vbo.StorageNullData(a.Length * 4, BufferStorageFlags.DynamicStorageBit)
        height_vbo.SubData(IntPtr.Zero, a.Length * 4, a)

        height_vao = GLVertexArray.Create("tankHeightOverlayVao")
        height_vao.VertexBuffer(0, height_vbo, IntPtr.Zero, FLOATS_PER_VERT * 4)
        height_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        height_vao.AttribBinding(0, 0)
        height_vao.EnableAttrib(0)
        height_vao.AttribFormat(1, 4, VertexAttribType.Float, False, 3 * 4)
        height_vao.AttribBinding(1, 0)
        height_vao.EnableAttrib(1)

        LogThis("tank height overlay: {0} vertices from raw flight bake",
                height_vert_count)
    End Sub

    Private Shared Sub height_vert(data As List(Of Single),
                                   x As Single, y As Single, z As Single,
                                   c As Vector4)
        data.Add(x)
        data.Add(y)
        data.Add(z)
        data.Add(c.X)
        data.Add(c.Y)
        data.Add(c.Z)
        data.Add(c.W)
    End Sub

    ''' <summary>
    ''' Find the MapFlightBake already loaded by MapTanks/MapScene.
    ''' </summary>
    Private Function loaded_bake() As MapFlightBake
        Dim b = flight_bake_from(If(map_scene Is Nothing, Nothing, map_scene.tanks))
        If b IsNot Nothing Then Return b
        Return flight_bake_from(map_scene)
    End Function

    Private Shared Function flight_bake_from(owner As Object) As MapFlightBake
        If owner Is Nothing Then Return Nothing

        Dim flags = BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic
        Dim tp = owner.GetType()

        Try
            For Each f In tp.GetFields(flags)
                If GetType(MapFlightBake).IsAssignableFrom(f.FieldType) Then
                    Dim b = TryCast(f.GetValue(owner), MapFlightBake)
                    If b IsNot Nothing Then Return b
                End If
            Next

            For Each p In tp.GetProperties(flags)
                If Not p.CanRead OrElse p.GetIndexParameters().Length <> 0 Then Continue For
                If GetType(MapFlightBake).IsAssignableFrom(p.PropertyType) Then
                    Dim b = TryCast(p.GetValue(owner), MapFlightBake)
                    If b IsNot Nothing Then Return b
                End If
            Next
        Catch
            ' Diagnostic only.
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' Build/rebuild the in-game navigation overlay only when needed.
    ''' The navigation map is static: it comes entirely from the flight bake.
    ''' Only the outline between impassable and passable cells is drawn.
    ''' </summary>
    Private Sub refresh_nav_overlay(nav As TankNav)
        If nav Is Nothing OrElse Not nav.ready Then
            nav_vert_count = 0
            nav_cached = Nothing
            Return
        End If

        If nav Is nav_cached AndAlso nav_vao IsNot Nothing Then Return

        nav_cached = nav
        build_nav_overlay(nav)
    End Sub

    Private Sub build_nav_overlay(nav As TankNav)
        Dim data As New List(Of Single)
        Dim N = TankNav.SIZE
        Dim half = nav.cell_m * 0.5F
        Dim mapC As New Vector4(1.0F, 0.82F, 0.08F, 0.34F)

        For cz = 0 To N - 1
            For cx = 0 To N - 1
                Dim idx = cz * N + cx
                Dim f = nav.cell(idx)
                If (f And TankNav.IMPASSABLE) = 0 Then Continue For

                Dim p = nav.CentreOf(cx, cz)
                Dim xL = p.X - half, xR = p.X + half
                Dim zB = p.Y - half, zT = p.Y + half

                If cx = 0 OrElse
                   (nav.cell(cz * N + (cx - 1)) And TankNav.IMPASSABLE) = 0 Then
                    nav_line(data, xL, zB, xL, zT, mapC)
                End If
                If cx = N - 1 OrElse
                   (nav.cell(cz * N + (cx + 1)) And TankNav.IMPASSABLE) = 0 Then
                    nav_line(data, xR, zB, xR, zT, mapC)
                End If
                If cz = 0 OrElse
                   (nav.cell((cz - 1) * N + cx) And TankNav.IMPASSABLE) = 0 Then
                    nav_line(data, xL, zT, xR, zT, mapC)
                End If
                If cz = N - 1 OrElse
                   (nav.cell((cz + 1) * N + cx) And TankNav.IMPASSABLE) = 0 Then
                    nav_line(data, xL, zB, xR, zB, mapC)
                End If
            Next
        Next

        nav_vert_count = data.Count \ FLOATS_PER_VERT

        If nav_vbo IsNot Nothing Then nav_vbo.Dispose()
        If nav_vao IsNot Nothing Then nav_vao.Dispose()
        nav_vbo = Nothing
        nav_vao = Nothing

        If nav_vert_count = 0 Then Return

        Dim a = data.ToArray()

        nav_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tankNavOverlayVerts")
        nav_vbo.StorageNullData(a.Length * 4, BufferStorageFlags.DynamicStorageBit)
        nav_vbo.SubData(IntPtr.Zero, a.Length * 4, a)

        nav_vao = GLVertexArray.Create("tankNavOverlayVao")
        nav_vao.VertexBuffer(0, nav_vbo, IntPtr.Zero, FLOATS_PER_VERT * 4)
        nav_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        nav_vao.AttribBinding(0, 0)
        nav_vao.EnableAttrib(0)
        nav_vao.AttribFormat(1, 4, VertexAttribType.Float, False, 3 * 4)
        nav_vao.AttribBinding(1, 0)
        nav_vao.EnableAttrib(1)

        LogThis("tank nav overlay: {0} vertices from baked no-go map",
                nav_vert_count)
    End Sub

    Private Sub nav_line(data As List(Of Single),
                         x0 As Single, z0 As Single,
                         x1 As Single, z1 As Single,
                         c As Vector4)
        nav_vert(data, x0, ground(x0, z0) + NAV_LIFT_M, z0, c)
        nav_vert(data, x1, ground(x1, z1) + NAV_LIFT_M, z1, c)
    End Sub

    Private Shared Sub nav_vert(data As List(Of Single),
                                x As Single, y As Single, z As Single,
                                c As Vector4)
        data.Add(x)
        data.Add(y)
        data.Add(z)
        data.Add(c.X)
        data.Add(c.Y)
        data.Add(c.Z)
        data.Add(c.W)
    End Sub

    ''' <summary>
    ''' Find the TankNav that is already owned by the loaded tank/map scene.
    '''
    ''' Reflection keeps this a one-file diagnostic change. It accepts a public
    ''' or private TankNav field/property on MapTanks first, then MapScene.
    ''' Nothing is created and nothing is modified.
    ''' </summary>
    Private Function loaded_nav() As TankNav
        Dim n = tank_nav_from(If(map_scene Is Nothing, Nothing, map_scene.tanks))
        If n IsNot Nothing Then Return n
        Return tank_nav_from(map_scene)
    End Function

    Private Shared Function tank_nav_from(owner As Object) As TankNav
        If owner Is Nothing Then Return Nothing

        Dim flags = BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic
        Dim tp = owner.GetType()

        Try
            For Each f In tp.GetFields(flags)
                If GetType(TankNav).IsAssignableFrom(f.FieldType) Then
                    Dim n = TryCast(f.GetValue(owner), TankNav)
                    If n IsNot Nothing Then Return n
                End If
            Next

            For Each p In tp.GetProperties(flags)
                If Not p.CanRead OrElse p.GetIndexParameters().Length <> 0 Then Continue For
                If GetType(TankNav).IsAssignableFrom(p.PropertyType) Then
                    Dim n = TryCast(p.GetValue(owner), TankNav)
                    If n IsNot Nothing Then Return n
                End If
            Next
        Catch
            ' Visual diagnostic only. Failure to discover the nav must never
            ' disturb rendering or tank simulation.
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' VISUAL ONLY: mark every avoidance ray that crosses an impassable TankNav
    ''' cell. The nav now comes only from the baked static no-go map.
    ''' </summary>
    Private Shared Function ray_map_hits(
        nav As TankNav,
        hullR As List(Of ValueTuple(Of Vector2, Vector2))) As Boolean()

        Dim hit(TankSim.RAY_COUNT - 1) As Boolean
        If nav Is Nothing OrElse Not nav.ready OrElse hullR Is Nothing Then Return hit

        Dim stepM = Math.Max(0.25F, nav.cell_m * 0.5F)

        For i = 0 To Math.Min(TankSim.RAY_COUNT, hullR.Count) - 1
            Dim o = hullR(i).Item1
            Dim d = hullR(i).Item2
            Dim reach = TankSim.RayLen(i)
            Dim steps = Math.Max(1, CInt(Math.Ceiling(reach / stepM)))

            For s = 1 To steps
                Dim dist = Math.Min(reach, CSng(s) * stepM)
                Dim q = o + d * dist

                Dim cx As Integer, cz As Integer
                nav.CellOf(q.X, q.Y, cx, cz)

                If Not nav.InBounds(cx, cz) Then
                    hit(i) = True
                    Exit For
                End If

                Dim flags = nav.cell(cz * TankNav.SIZE + cx)
                If (flags And TankNav.IMPASSABLE) <> 0 Then
                    hit(i) = True
                    Exit For
                End If
            Next
        Next

        Return hit
    End Function

    ''' <summary>Segments this hull's ray needs: one every SEG_M, at least one,
    ''' and bounded so a goal at the far corner of an outsized map cannot make the
    ''' buffer unbounded.</summary>
    Private Function segments_for(t As TankInstance) As Integer
        Dim dx = t.drive.goal.X - t.position.X
        Dim dz = t.drive.goal.Y - t.position.Z
        Dim m = CSng(Math.Sqrt(dx * dx + dz * dz))
        Return Math.Max(1, Math.Min(MAX_SEG, CInt(Math.Ceiling(m / SEG_M))))
    End Function

    Private Function ground(x As Single, z As Single) As Single
        Return get_Y_at_XZ_fast(x, z) + LIFT_M
    End Function

    Private Function put(n As Integer, x As Single, y As Single, z As Single,
                         c As Vector4) As Integer
        verts(n + 0) = x
        verts(n + 1) = y
        verts(n + 2) = z
        verts(n + 3) = c.X
        verts(n + 4) = c.Y
        verts(n + 5) = c.Z
        verts(n + 6) = c.W
        Return n + FLOATS_PER_VERT
    End Function

    ''' <summary>
    ''' A buffer at least this big, with headroom, and DYNAMIC so SubData can
    ''' write it - MapCamPath's is immutable because its geometry is built once at
    ''' map load, which is exactly the assumption this class does not make. Grown
    ''' and never shrunk: a fleet that dropped from thirty rays to four would
    ''' otherwise reallocate on the way back up.
    ''' </summary>
    Private Sub ensure_buffer(vcount As Integer)
        If vao IsNot Nothing AndAlso cap_verts >= vcount Then Return

        Dim want = Math.Max(vcount * 2, 4096)
        If vbo IsNot Nothing Then vbo.Dispose()
        If vao IsNot Nothing Then vao.Dispose()

        vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tankRayVerts")
        vbo.StorageNullData(want * FLOATS_PER_VERT * 4, BufferStorageFlags.DynamicStorageBit)

        vao = GLVertexArray.Create("tankRayVao")
        vao.VertexBuffer(0, vbo, IntPtr.Zero, FLOATS_PER_VERT * 4)
        vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)
        vao.AttribFormat(1, 4, VertexAttribType.Float, False, 3 * 4)
        vao.AttribBinding(1, 0)
        vao.EnableAttrib(1)

        cap_verts = want
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If vbo IsNot Nothing Then vbo.Dispose()
        If vao IsNot Nothing Then vao.Dispose()
        If nav_vbo IsNot Nothing Then nav_vbo.Dispose()
        If nav_vao IsNot Nothing Then nav_vao.Dispose()
        If height_vbo IsNot Nothing Then height_vbo.Dispose()
        If height_vao IsNot Nothing Then height_vao.Dispose()
        vbo = Nothing
        vao = Nothing
        nav_vbo = Nothing
        nav_vao = Nothing
        height_vbo = Nothing
        height_vao = Nothing
        cap_verts = 0
        nav_vert_count = 0
        height_vert_count = 0
        nav_cached = Nothing
        height_cached = Nothing
    End Sub
End Class

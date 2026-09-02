Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' A baked camera flight path - cam_paths\&lt;map&gt;.campath, written by
''' tools/export_cam_path.py.
'''
''' Two jobs: draw the route in the world so the data can be checked by eye, and
''' fly the camera along it.
'''
''' The file is a 64 byte header then a flat array of fixed size records. See
''' cam_paths\README.md for the layout, and tools/cam_path.py, which is the
''' authority - if the two ever disagree, the Python one is right, because it is
''' what writes the files.
'''
''' Records are skipped by the header's STRIDE rather than by the 32 bytes
''' version 1 uses, so a later exporter can append fields and this still loads.
''' </summary>
Public Class MapCamPath
    Implements IDisposable

    Private Const MAGIC As UInteger = &H3150434EUI   ' "NCP1" little endian
    Private Const HEADER_SIZE As Integer = 64
    Private Const V1_STRIDE As Integer = 32

    Public Structure CamPoint
        Public pos As Vector3
        Public heading As Single      ' yaw, radians. atan2(dx, dz)
        Public tilt As Single         ' pitch, radians. POSITIVE LOOKS UP
        Public roll As Single         ' bank, radians. POSITIVE BANKS RIGHT
        Public s As Single            ' metres from the first point
        Public speed As Single        ' metres per second
    End Structure

    Public points() As CamPoint
    Public loaded As Boolean
    Public closed As Boolean
    Public total_len As Single
    Public map_name As String = ""

    ''' <summary>Distance travelled along the path, metres. Advanced by Fly.</summary>
    Public travelled As Single

    Private vao As GLVertexArray
    Private vbo As GLBuffer
    Private vertex_count As Integer

    ''' <summary>How much of the route is blanked around the eye while flying,
    ''' in metres. Inside HIDE_NEAR the line is discarded outright; from there it
    ''' fades up, reaching full strength at HIDE_FAR.
    '''
    ''' By distance from the eye, not by position along the route. Cutting a
    ''' fixed stretch of route ahead has to guess how much of it is on screen,
    ''' and 45 m of it left nothing to fly by. Distance cuts exactly what is
    ''' close, wants no special case where the loop joins, and also blanks a
    ''' later lap that happens to pass nearby.
    '''
    ''' The fade is the part that matters. A hard edge alone either leaves the
    ''' line in your face or deletes so much there is nothing to follow.</summary>
    Private Const HIDE_NEAR As Single = 0.5F
    Private Const HIDE_FAR As Single = 2.5F

    ' Metres of heading tick drawn at every TICK_EVERY points. Long enough to
    ' read the direction off the screen, short enough not to become the picture.
    Private Const TICK_LEN As Single = 6.0F
    Private Const TICK_EVERY As Integer = 8

    Public Sub Load(map As String)
        Dispose_gl()
        loaded = False
        points = Nothing
        travelled = 0.0F

        Dim path = IO.Path.Combine(Application.StartupPath, "cam_paths", map & ".campath")
        If Not File.Exists(path) Then
            LogThis("cam path: none for {0} ({1})", map, path)
            Return
        End If

        Try
            Dim raw = File.ReadAllBytes(path)
            If raw.Length < HEADER_SIZE Then
                LogThis("cam path: {0} is shorter than its header", path)
                Return
            End If

            Dim magic = BitConverter.ToUInt32(raw, 0)
            If magic <> MAGIC Then
                LogThis("cam path: bad magic in {0}", path)
                Return
            End If

            Dim version = BitConverter.ToUInt16(raw, 4)
            Dim flags = BitConverter.ToUInt16(raw, 6)
            Dim count = CInt(BitConverter.ToUInt32(raw, 8))
            Dim stride = CInt(BitConverter.ToUInt32(raw, 12))
            total_len = BitConverter.ToSingle(raw, 16)
            map_name = Text.Encoding.ASCII.GetString(raw, 20, 40).TrimEnd(ChrW(0))
            closed = (flags And 1) <> 0

            If stride < V1_STRIDE Then
                LogThis("cam path: stride {0} is smaller than version 1's {1}", stride, V1_STRIDE)
                Return
            End If

            Dim want = HEADER_SIZE + count * stride
            If raw.Length <> want Then
                LogThis("cam path: {0} is {1} bytes, the header says {2}", path, raw.Length, want)
                Return
            End If

            If count < 2 Then
                LogThis("cam path: {0} has only {1} points", path, count)
                Return
            End If

            ReDim points(count - 1)
            For i = 0 To count - 1
                Dim o = HEADER_SIZE + i * stride
                points(i).pos = New Vector3(BitConverter.ToSingle(raw, o),
                                            BitConverter.ToSingle(raw, o + 4),
                                            BitConverter.ToSingle(raw, o + 8))
                points(i).heading = BitConverter.ToSingle(raw, o + 12)
                points(i).tilt = BitConverter.ToSingle(raw, o + 16)
                points(i).roll = BitConverter.ToSingle(raw, o + 20)
                points(i).s = BitConverter.ToSingle(raw, o + 24)
                points(i).speed = BitConverter.ToSingle(raw, o + 28)
            Next

            loaded = True
            build_geometry()

            ' Report what was read rather than what was expected. A path that
            ' loads but lands in the wrong place shows up here as a bounding box
            ' nowhere near the map, before anything is drawn.
            Dim lo = points(0).pos, hi = points(0).pos
            Dim maxroll = 0.0F
            For i = 0 To count - 1
                lo = Vector3.ComponentMin(lo, points(i).pos)
                hi = Vector3.ComponentMax(hi, points(i).pos)
                maxroll = Math.Max(maxroll, Math.Abs(points(i).roll))
            Next
            LogThis("cam path: {0} v{1} {2} points over {3:0} m ({4}), x {5:0}..{6:0} y {7:0.0}..{8:0.0} z {9:0}..{10:0}, roll to {11:0.0} deg",
                    map_name, version, count, total_len,
                    If(closed, "closed loop", "open"),
                    lo.X, hi.X, lo.Y, hi.Y, lo.Z, hi.Z,
                    maxroll * 180.0F / CSng(Math.PI))

        Catch ex As Exception
            LogThis("cam path: failed to read {0}: {1}", path, ex.Message)
            loaded = False
        End Try
    End Sub

    ''' <summary>
    ''' One interleaved buffer of GL_LINES - the route, then a heading tick every
    ''' few points. Built once at load, because the path does not move.
    '''
    ''' The ticks are the reason this is worth more than drawing the positions
    ''' alone: they are the only thing on screen that can show the HEADING and
    ''' TILT fields were read correctly. A path whose angles are garbage still
    ''' draws a perfectly good line.
    ''' </summary>
    Private Sub build_geometry()
        Dim n = points.Length
        Dim segs = If(closed, n, n - 1)
        Dim ticks = (n + TICK_EVERY - 1) \ TICK_EVERY
        vertex_count = (segs + ticks) * 2

        ' pos.xyz + rgba
        Dim v(vertex_count * 7 - 1) As Single
        Dim k = 0

        Dim put = Sub(p As Vector3, r As Single, g As Single, b As Single, a As Single)
                      v(k) = p.X : v(k + 1) = p.Y : v(k + 2) = p.Z
                      v(k + 3) = r : v(k + 4) = g : v(k + 5) = b : v(k + 6) = a
                      k += 7
                  End Sub

        ' The route. Coloured along its length so the direction of travel is
        ' visible without an arrow - it runs from green at the start round to
        ' magenta at the end.
        For i = 0 To segs - 1
            Dim j = (i + 1) Mod n
            Dim t0 = CSng(i) / CSng(n)
            Dim t1 = CSng(j) / CSng(n)
            put(points(i).pos, t0, 1.0F - t0 * 0.7F, 0.35F + t0 * 0.65F, 1.0F)
            put(points(j).pos, t1, 1.0F - t1 * 0.7F, 0.35F + t1 * 0.65F, 1.0F)
        Next

        ' Heading and tilt ticks - the direction the camera is actually facing
        ' at that point, built from the same formula MapCamera uses to turn its
        ' two angles into a look vector.
        For i = 0 To n - 1 Step TICK_EVERY
            Dim h = points(i).heading
            Dim t = points(i).tilt
            Dim dir As New Vector3(CSng(Math.Cos(t) * Math.Sin(h)),
                                   CSng(Math.Sin(t)),
                                   CSng(Math.Cos(t) * Math.Cos(h)))
            put(points(i).pos, 1.0F, 0.85F, 0.1F, 1.0F)
            put(points(i).pos + dir * TICK_LEN, 1.0F, 0.4F, 0.0F, 0.15F)
        Next

        vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "camPathVerts")
        vbo.Storage(v.Length * 4, v, BufferStorageFlags.None)

        vao = GLVertexArray.Create("camPathVao")
        vao.VertexBuffer(0, vbo, IntPtr.Zero, 7 * 4)
        vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        vao.AttribBinding(0, 0)
        vao.EnableAttrib(0)
        vao.AttribFormat(1, 4, VertexAttribType.Float, False, 3 * 4)
        vao.AttribBinding(1, 0)
        vao.EnableAttrib(1)
    End Sub

    ''' <summary>
    ''' Draw the route. Twice: depth tested and solid, then again with the depth
    ''' test off and nearly transparent.
    '''
    ''' One pass is not enough either way. Depth tested alone, a route behind a
    ''' hill vanishes and reads as "it did not load". Depth off alone, it draws
    ''' straight through the monastery and there is no way to tell whether it is
    ''' at the right height. Both together answer the question this exists for.
    ''' </summary>
    Public Sub DrawPath()
        If Not loaded OrElse vao Is Nothing Then Return

        GL_PUSH_GROUP("MapCamPath::DrawPath")

        campathShader.Use()
        vao.Bind()
        GL.LineWidth(2.0F)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        ' Blank the piece of route the camera is standing on. While flying the
        ' path runs THROUGH the eye, so its near end lies down the middle of the
        ' screen and hides the thing it was drawn to show. The cut is a distance
        ' test in the fragment shader - see campath.frag.
        '
        ' Nothing here rebuilds the buffer. The geometry is static and only the
        ' eye moves, so the whole buffer is drawn every frame either way and the
        ' fragment stage decides what survives.
        Dim flying = FLY_CAM_PATH AndAlso map_scene IsNot Nothing AndAlso map_scene.camera.FLYING
        Dim eye = If(flying, map_scene.camera.CAM_POSITION, Vector3.Zero)
        GL.Uniform3(campathShader("hide_from"), eye.X, eye.Y, eye.Z)
        GL.Uniform1(campathShader("hide_near"), If(flying, HIDE_NEAR, 0.0F))
        GL.Uniform1(campathShader("hide_far"), If(flying, HIDE_FAR, 0.0F))

        If flying Then
            ' Flying, ONE pass and no depth test.
            '
            ' The ghost/solid split below is noise from in here. At 1 m the view
            ' along the route is grazing, so every rise between here and there
            ' occludes it: the line spends most of its length ghosted and snaps
            ' to full colour wherever the ground happens to fall away. Both
            ' answers are correct and the flicker between them is unreadable.
            GL.Disable(EnableCap.DepthTest)
            GL.Uniform1(campathShader("alpha_mul"), 1.0F)
            GL.DrawArrays(PrimitiveType.Lines, 0, vertex_count)
        Else
            ' Inspecting, the split is the entire point - solid where the route
            ' is really in view, ghosted where something is in front of it, which
            ' is how its HEIGHT gets checked by eye.
            '
            ' The ghost is deliberately strong at 0.35. Seen from far enough
            ' away the route grazes the ground it runs over and loses the depth
            ' test along most of its length, so without a strong ghost the path
            ' appeared to vanish on zoom out.
            GL.Disable(EnableCap.DepthTest)
            GL.Uniform1(campathShader("alpha_mul"), 0.35F)
            GL.DrawArrays(PrimitiveType.Lines, 0, vertex_count)

            ' No polygon offset here. There used to be, meant to bias this pass
            ' toward the camera, and it never did anything: polygon offset
            ' applies to POLYGONS, and this is GL_LINES. Nothing needs it -
            ' depth is 32f reversed-Z, which resolves the 1 m the route sits
            ' above the ground with room to spare at any range.
            GL.Enable(EnableCap.DepthTest)
            GL.Uniform1(campathShader("alpha_mul"), 1.0F)
            GL.DrawArrays(PrimitiveType.Lines, 0, vertex_count)
        End If

        ' Leave the depth test on however we got here - the flying branch turned
        ' it off and everything drawn after this expects it back.
        GL.Enable(EnableCap.DepthTest)
        GL.LineWidth(1.0F)
        GL.Disable(EnableCap.Blend)
        campathShader.StopUse()

        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Where the camera should be after moving dt seconds along the path.
    ''' Returns False when there is nothing to fly.
    '''
    ''' Interpolates position linearly and the angles as SHORTEST ARC, which
    ''' matters: heading wraps, and lerping 179 to -179 degrees the long way
    ''' spins the camera all the way round once per lap.
    ''' </summary>
    Public Function Sample(dt As Single, ByRef pos As Vector3,
                           ByRef heading As Single, ByRef tilt As Single,
                           ByRef roll As Single) As Boolean
        If Not loaded OrElse points Is Nothing OrElse points.Length < 2 Then Return False

        Dim n = points.Length
        travelled += dt * points(0).speed

        If closed Then
            If total_len > 0.0F Then
                travelled = travelled - CSng(Math.Floor(travelled / total_len)) * total_len
            End If
        Else
            travelled = Math.Max(0.0F, Math.Min(travelled, points(n - 1).s))
        End If

        ' Points are near enough evenly spaced that a scan from a guessed index
        ' is wasted work; a straight search is a few hundred compares once a
        ' frame and cannot get out of step.
        Dim i = 0
        While i < n - 1 AndAlso points(i + 1).s <= travelled
            i += 1
        End While
        Dim j = If(closed, (i + 1) Mod n, Math.Min(i + 1, n - 1))

        Dim span = If(j = 0, total_len - points(i).s, points(j).s - points(i).s)
        Dim f = If(span > 1.0E-4F, (travelled - points(i).s) / span, 0.0F)
        f = Math.Max(0.0F, Math.Min(1.0F, f))

        pos = points(i).pos + (points(j).pos - points(i).pos) * f
        heading = points(i).heading + wrap_pi(points(j).heading - points(i).heading) * f
        tilt = points(i).tilt + (points(j).tilt - points(i).tilt) * f
        roll = points(i).roll + (points(j).roll - points(i).roll) * f
        Return True
    End Function

    Private Shared Function wrap_pi(a As Single) As Single
        Dim TWO_PI = CSng(Math.PI * 2.0)
        a = CSng(a - TWO_PI * Math.Floor((a + Math.PI) / TWO_PI))
        Return a
    End Function

    Private Sub Dispose_gl()
        vao?.Dispose()
        vbo?.Dispose()
        vao = Nothing
        vbo = Nothing
        vertex_count = 0
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose_gl()
        GC.SuppressFinalize(Me)
    End Sub
End Class

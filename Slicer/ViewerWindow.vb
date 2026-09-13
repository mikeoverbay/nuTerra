Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' The viewer. Orbit a building, step through the library, switch LOD, solo a
''' part - and cut it.
'''
''' Mouse navigation is nuTerra's, transcribed from Window.vb: left drag orbits
''' with damping and coasts to a stop, middle or ctrl drag pans, shift drag
''' raises and lowers, right drag zooms. The wheel zooms too, which nuTerra does
''' not do - there the wheel belongs to ImGui and the camera never sees it.
'''
'''     left drag     orbit                     S      slicing on / off
'''     mid / ctrl    pan                       , .    move the plane
'''     shift drag    height                    X Y Z  cut axis
'''     right drag    zoom                      K      keep below / above / both
'''     wheel         zoom                      C      cut outline on / off
'''     left / right  previous / next building  W      wireframe
'''     [ / ]         coarser / finer LOD       R      reload
'''     up / down     solo one part             Esc    quit
'''
''' The shaders are embedded rather than shipped as files. SrtViewer keeps its
''' in a folder and copies them at build; there are two here and thirty lines
''' between them, and an exe that cannot fail to find its own shaders is worth
''' more than the ability to edit them without a rebuild.
''' </summary>
Public Class ViewerWindow
    Inherits GameWindow

    Private Const VERT As String =
        "#version 330 core" & vbLf &
        "layout(location = 0) in vec3 a_pos;" & vbLf &
        "layout(location = 1) in vec3 a_nrm;" & vbLf &
        "uniform mat4 u_mvp;" & vbLf &
        "out vec3 v_nrm;" & vbLf &
        "void main() {" & vbLf &
        "    v_nrm = a_nrm;" & vbLf &
        "    gl_Position = u_mvp * vec4(a_pos, 1.0);" & vbLf &
        "}" & vbLf

    ' Two-sided lighting on purpose: only 4.6% of these meshes are watertight,
    ' so a face turned away from the key light is normal and must not read as a
    ' hole. abs() on the diffuse term, not a clamp. A cut makes this matter
    ' more, not less - it exposes interior backfaces by design.
    Private Const FRAG As String =
        "#version 330 core" & vbLf &
        "in vec3 v_nrm;" & vbLf &
        "uniform vec3 u_tint;" & vbLf &
        "uniform int  u_flat;" & vbLf &
        "out vec4 o_col;" & vbLf &
        "void main() {" & vbLf &
        "    if (u_flat == 1) { o_col = vec4(u_tint, 1.0); return; }" & vbLf &
        "    vec3 n = normalize(v_nrm);" & vbLf &
        "    vec3 l = normalize(vec3(0.45, 0.8, 0.35));" & vbLf &
        "    float d = abs(dot(n, l));" & vbLf &
        "    float sky = 0.30 + 0.20 * n.y;" & vbLf &
        "    vec3 c = u_tint * (0.35 * sky + 0.75 * d);" & vbLf &
        "    o_col = vec4(pow(c, vec3(1.0 / 2.2)), 1.0);" & vbLf &
        "}" & vbLf

    Private ReadOnly pkg As PkgIndex
    Private ReadOnly assets As List(Of BuildingAsset)
    Private ReadOnly settings As SliceSettings

    Private assetIndex As Integer = 0
    Private lodIndex As Integer = 0
    Private soloPart As Integer = -1
    Private wireframe As Boolean = False

    ' slicing
    Private slicing As Boolean = True
    Private showCut As Boolean = True
    Private planeNudge As Single = 0.0F        ' metres, on top of settings.Offset
    Private axisOverride As String = Nothing
    Private keepOverride As String = Nothing

    Private vao, vbo, ebo, shader As Integer
    Private cutVao, cutVbo As Integer
    Private cutVertCount As Integer = 0
    Private uMvp, uTint, uFlat As Integer

    ''' <summary>A part as it came out of the packages, kept CPU-side so the
    ''' plane can be moved without re-reading a single byte.</summary>
    Private Class RawPart
        Public Name As String
        Public Pos As Vector3()
        Public Idx As Integer()
    End Class
    Private ReadOnly rawParts As New List(Of RawPart)

    Private Class DrawPart
        Public Name As String
        Public First As Integer
        Public Count As Integer
        Public Tint As Vector3
        ''' <summary>True for triangles this app invented to close the bottom,
        ''' false for geometry that shipped in the package. Kept apart so the
        ''' fill can be inspected rather than blending into the model.</summary>
        Public IsFill As Boolean
    End Class
    Private ReadOnly parts As New List(Of DrawPart)

    ''' <summary>Paint the bottom fill red and everything else blue. On by
    ''' default because the fill is the thing being checked; B turns it off to
    ''' get the ordinary per-part tints back.</summary>
    Private fillDebug As Boolean = True
    Private fillOn As Boolean = True
    Private fillTris, fillRings, fillOpen, fillEdges As Integer
    ''' <summary>
    ''' The worst Y spread of any ONE mesh's fill, and the real check - stronger
    ''' than the picture, because a fill that climbed off its plane shows as a
    ''' number even when the render looks plausible.
    '''
    ''' PER MESH, not across the asset. An asset-wide spread was the first
    ''' version and it was meaningless: a kit's pieces each sit at their own
    ''' height, so hd_bld_eu_049_thouse legitimately spreads 0.99 m across its
    ''' eleven parts while every individual fill is dead flat. What must be flat
    ''' is each fill, not the set of them.
    ''' </summary>
    Private fillWorstSpread As Single
    Private fillWorstName As String

    ''' <summary>Set by --shot: render one frame, save it, quit. The window
    ''' still opens - reading the default framebuffer is the point, so there
    ''' has to be one.</summary>
    Private shotPath As String = Nothing
    Private shotFrames As Integer = 0
    ''' <summary>Set by --shell: run the rebuild once, before the shot.</summary>
    Private shellOnLoad As Boolean = False

    Private boundsMin, boundsMax As Vector3
    Private totalVerts, totalTris, clippedTris, cutSegs As Integer

    ' ---- camera, transcribed from nuTerra -------------------------------
    ' Window.vb camera_mouse_update, which itself follows three.js
    ' OrbitControls (MIT, mrdoob/three.js). The mechanism is one pending-delta
    ' pool per axis: input only ever ADDS to the pool, and each frame the
    ' camera takes `f` of what is pending while the remainder decays. That is
    ' what gives the coast-to-a-stop rather than stopping dead with the cursor.
    Private yaw As Single = 0.7F              ' nuTerra CAM_X_ANGLE
    ' NEGATIVE to start above the model. With the eye's Y term carrying
    ' nuTerra's sign (see OnRenderFrame), a positive pitch puts the camera under
    ' the building looking up - and the clamp range now means what nuTerra means
    ' by it: -PI/2 is straight overhead, +1.3 is as far below as it will go.
    Private pitch As Single = -0.35F          ' nuTerra CAM_Y_ANGLE
    Private dist As Single = 40.0F            ' nuTerra VIEW_RADIUS, but POSITIVE here
    Private target As Vector3 = Vector3.Zero  ' nuTerra LOOK_AT_*
    Private dragging As Boolean = False

    Private rotDeltaX, rotDeltaY As Single
    Private zoomDelta As Single
    Private panDeltaX, panDeltaZ As Single
    ''' <summary>Its own clock, NOT the render frame time. nuTerra makes the
    ''' same distinction: this runs in the update loop, which is not throttled
    ''' to the render, and the damping factor below is only correct if it is
    ''' fed the time this loop actually took.</summary>
    Private ReadOnly rotClock As New Diagnostics.Stopwatch

    ''' <summary>nuTerra's ROT_DAMPING default, its "Rotation damping" slider.</summary>
    Private Const ROT_DAMPING As Single = 0.1F
    ''' <summary>nuTerra reads My.Settings.speed here. The Slicer has no
    ''' settings store for it, so it takes the same neutral 1.0 that a fresh
    ''' nuTerra profile starts at.</summary>
    Private Const MOUSE_SPEED As Single = 1.0F
    Private Const PITCH_MIN As Single = -1.5697963F   ' -PI/2 + 0.001, as nuTerra clamps
    Private Const PITCH_MAX As Single = 1.3F

    Public Sub New(index As PkgIndex, bl As BuildingLibrary, startAsset As Integer, cfg As SliceSettings,
                   Optional shot As String = Nothing, Optional doShell As Boolean = False,
                   Optional shotAngle As String = Nothing)
        MyBase.New(GameWindowSettings.Default,
                   New NativeWindowSettings With {
                       .Size = New Vector2i(1280, 800),
                       .Title = "Slicer",
                       .APIVersion = New Version(3, 3),
                       .Profile = ContextProfile.Core})
        pkg = index
        assets = bl.Assets.Values.ToList()
        settings = cfg
        assetIndex = Math.Max(0, Math.Min(startAsset, assets.Count - 1))
        lodIndex = Math.Max(0, cfg.Lod)
        shotPath = shot
        shellOnLoad = doShell
        If shotPath IsNot Nothing Then
            ' The angle decides what the picture can prove, so it is explicit
            ' rather than whatever the viewer happened to open at. A before and
            ' an after are only comparable if both used the same one.
            slicing = False          ' a cut would hide half of whatever is being shown
            Select Case If(shotAngle, "bottom").Trim().ToLowerInvariant()
                Case "iso"
                    ' Three-quarter from above - the ordinary way to look at a
                    ' building, and the only angle that shows walls and roof at
                    ' once.
                    pitch = -0.45F
                    yaw = 0.8F
                Case "front"
                    pitch = -0.08F
                    yaw = 0.0F
                Case Else
                    ' Looking UP at the underside: after the Y flip a positive
                    ' pitch is below the model, and 1.15 is just inside the 1.3
                    ' clamp, so the camera sits low rather than edge-on.
                    pitch = 1.15F
                    yaw = 0.9F
            End Select
        End If
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        GL.ClearColor(0.13F, 0.14F, 0.16F, 1.0F)
        GL.Enable(EnableCap.DepthTest)
        shader = BuildShader()
        uMvp = GL.GetUniformLocation(shader, "u_mvp")
        uTint = GL.GetUniformLocation(shader, "u_tint")
        uFlat = GL.GetUniformLocation(shader, "u_flat")
        vao = GL.GenVertexArray() : vbo = GL.GenBuffer() : ebo = GL.GenBuffer()
        cutVao = GL.GenVertexArray() : cutVbo = GL.GenBuffer()
        LoadCurrent()
        If shellOnLoad Then RunShellPipeline()
    End Sub

    Private Function BuildShader() As Integer
        Dim vs = GL.CreateShader(ShaderType.VertexShader)
        GL.ShaderSource(vs, VERT) : GL.CompileShader(vs) : CheckShader(vs, "vertex")
        Dim fs = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fs, FRAG) : GL.CompileShader(fs) : CheckShader(fs, "fragment")
        Dim p = GL.CreateProgram()
        GL.AttachShader(p, vs) : GL.AttachShader(p, fs) : GL.LinkProgram(p)
        Dim ok As Integer
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then Throw New Exception("shader link failed: " & GL.GetProgramInfoLog(p))
        GL.DeleteShader(vs) : GL.DeleteShader(fs)
        Return p
    End Function

    Private Shared Sub CheckShader(s As Integer, what As String)
        Dim ok As Integer
        GL.GetShader(s, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception(what & " shader: " & GL.GetShaderInfoLog(s))
    End Sub

    ''' <summary>Read the current asset's current LOD out of the packages. Only
    ''' called when the asset or LOD changes - moving the plane does not touch
    ''' this, which is what lets the plane be dragged.</summary>
    Private Sub LoadCurrent()
        rawParts.Clear()
        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return
        lodIndex = Math.Max(0, Math.Min(lodIndex, lods.Count - 1))
        Dim lod = lods(lodIndex)

        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        Dim any = False
        Dim wanted = If(settings.Parts, "all").Trim().ToLowerInvariant()

        For Each part In asset.PartsAt(lod)
            If wanted <> "all" AndAlso Not part.Name.ToLowerInvariant().Contains(wanted) Then Continue For
            Dim raw = pkg.ReadPath(PrimitivesPathFor(part))
            If raw Is Nothing Then Continue For
            Dim meshes As List(Of PrimMesh)
            Try
                meshes = PrimitivesFile.Parse(raw)
            Catch
                Continue For
            End Try
            For Each m In meshes
                If m.Positions.Length = 0 OrElse m.Indices.Length < 3 Then Continue For
                rawParts.Add(New RawPart With {.Name = m.Name, .Pos = m.Positions, .Idx = m.Indices})
                For Each p In m.Positions
                    lo = Vector3.ComponentMin(lo, p)
                    hi = Vector3.ComponentMax(hi, p)
                    any = True
                Next
            Next
        Next

        If Not any Then
            Console.WriteLine("{0} lod{1}: no geometry could be read", asset.Name, lod)
            parts.Clear()
            Return
        End If

        boundsMin = lo : boundsMax = hi
        target = (lo + hi) * 0.5F
        ' A shot frames wider than the interactive default: the whole model
        ' has to be inside the image for the picture to prove anything.
        dist = Math.Max((hi - lo).Length * If(shotPath IsNot Nothing, 1.35F, 0.9F), 2.0F)
        planeNudge = 0.0F
        Rebuild()
        Console.WriteLine("{0}  lod{1}  {2} mesh(es)  {3:N0} tris  span {4:F1} m",
                          asset.Name, lod, rawParts.Count, totalTris, (hi - lo).Length)
    End Sub

    Private Function EffectiveAxis() As String
        Return If(axisOverride, settings.Axis)
    End Function

    Private Function EffectiveKeep() As String
        Return If(keepOverride, settings.Keep).Trim().ToLowerInvariant()
    End Function

    Private Function AxisNormal() As Vector3
        Select Case EffectiveAxis().Trim().ToLowerInvariant()
            Case "x" : Return New Vector3(1, 0, 0)
            Case "y" : Return New Vector3(0, 1, 0)
            Case "z" : Return New Vector3(0, 0, 1)
            Case Else : Return settings.EffectiveNormal()
        End Select
    End Function

    ''' <summary>
    ''' Slice (or not) and upload. Cheap enough to call while a key is held.
    '''
    ''' Normals are DERIVED from the winding rather than read from the packed
    ''' 8-8-8 in the vertex, and after a cut they have to be: a clipped triangle
    ''' has corners that did not exist in the source, so there is no stored
    ''' normal to read for them.
    ''' </summary>
    Private Sub Rebuild()
        parts.Clear()
        totalVerts = 0 : totalTris = 0 : clippedTris = 0 : cutSegs = 0
        fillTris = 0 : fillRings = 0 : fillOpen = 0 : fillEdges = 0
        fillWorstSpread = 0.0F : fillWorstName = Nothing

        Dim verts As New List(Of Single)
        Dim idx As New List(Of Integer)
        Dim cuts As New List(Of Single)

        Dim palette = {
            New Vector3(0.82F, 0.79F, 0.73F), New Vector3(0.74F, 0.66F, 0.58F),
            New Vector3(0.66F, 0.72F, 0.78F), New Vector3(0.79F, 0.72F, 0.62F),
            New Vector3(0.70F, 0.76F, 0.68F), New Vector3(0.80F, 0.70F, 0.70F)}

        Dim n = AxisNormal()
        Dim planeD = MeshSlicer.PlaneDistance(settings, boundsMin, boundsMax, planeNudge)
        Dim keep = EffectiveKeep()
        ' On-plane tolerance proportional to the model, so a 206 m dam and an
        ' 8 m shed behave the same.
        Dim eps = Math.Max((boundsMax - boundsMin).Length * 0.000001F, 0.000001F)

        For Each rp In rawParts
            Dim pos = rp.Pos
            Dim tri = rp.Idx

            If slicing Then
                Dim res As SliceResult
                If keep = "above" Then
                    res = MeshSlicer.Clip(pos, tri, -n, -planeD, eps)
                ElseIf keep = "both" Then
                    ' Both halves, each clipped, so the mesh is re-triangulated
                    ' at the plane and the cut outline still falls out. Looks
                    ' like the whole building, but it has been cut.
                    Dim a = MeshSlicer.Clip(pos, tri, n, planeD, eps)
                    Dim b = MeshSlicer.Clip(pos, tri, -n, -planeD, eps)
                    Dim shift = a.Positions.Count
                    For Each p In b.Positions
                        a.Positions.Add(p)
                    Next
                    For Each i In b.Indices
                        a.Indices.Add(i + shift)
                    Next
                    a.CutEdges.AddRange(b.CutEdges)
                    a.TrianglesClipped += b.TrianglesClipped
                    res = a
                Else
                    res = MeshSlicer.Clip(pos, tri, n, planeD, eps)
                End If

                If res.Positions.Count = 0 Then Continue For
                pos = res.Positions.ToArray()
                tri = res.Indices.ToArray()
                clippedTris += res.TrianglesClipped
                cutSegs += res.CutSegments
                For Each p In res.CutEdges
                    cuts.Add(p.X) : cuts.Add(p.Y) : cuts.Add(p.Z)
                    cuts.Add(0.0F) : cuts.Add(1.0F) : cuts.Add(0.0F)   ' normal unused, flat shaded
                Next
            End If

            If pos.Length = 0 OrElse tri.Length < 3 Then Continue For

            ' Inigo Quilez's area-weighted smoothing - see MeshNormals. It
            ' only actually SMOOTHS where vertices are shared, so on a raw part
            ' (45% duplicate vertices, split at every UV seam) it comes out
            ' faceted, and on a welded shell it comes out smooth. That is the
            ' right behaviour both times rather than two different code paths.
            Dim nrm = MeshNormals.Compute(pos, tri)

            Dim baseVert = totalVerts
            For i = 0 To pos.Length - 1
                verts.Add(pos(i).X) : verts.Add(pos(i).Y) : verts.Add(pos(i).Z)
                verts.Add(nrm(i).X) : verts.Add(nrm(i).Y) : verts.Add(nrm(i).Z)
            Next
            Dim first = idx.Count
            For i = 0 To tri.Length - 1
                Dim vi = tri(i)
                If vi < 0 OrElse vi >= pos.Length Then vi = 0
                idx.Add(baseVert + vi)
            Next
            parts.Add(New DrawPart With {
                .Name = rp.Name, .First = first, .Count = tri.Length,
                .Tint = palette(parts.Count Mod palette.Length)})
            totalVerts += pos.Length
            totalTris += tri.Length \ 3

            ' ---- close the bottom.
            ' Built from the geometry AS DRAWN, not from the raw mesh, so a cut
            ' that removes the bottom correctly leaves nothing to fill.
            '
            ' EACH MESH USES ITS OWN LOWEST POINT, not the asset's. This was
            ' the other way round first, on the theory that a roof's lowest
            ' boundary is up in the air and filling it would staple a lid across
            ' the eaves. Measuring hd_bld_eu_049_thouse killed that: it is a KIT
            ' of 11 independent pieces - lowerfloors, upperfloors, roof, balcony
            ' - each plane-cut at its own base and sitting at its own height,
            ' spread over a metre. Against the asset minimum, ten of the eleven
            ' were excluded and the building rendered wide open from below.
            '
            '     lowerfloorssmall_01  -1.1138   8 bottom edges  <- the only match
            '     lowerfloorsbig_01    -1.0000  12 bottom edges
            '     upperfloorsbig_03    -0.1379  24 bottom edges
            '     roof_01              -0.7378   8 bottom edges
            '
            ' A roof HAS a bottom opening - the underside that sits on the walls
            ' - and closing it is right rather than a mistake.
            If fillOn Then
                Dim meshBottom = Single.MaxValue
                For Each mp In pos
                    If mp.Y < meshBottom Then meshBottom = mp.Y
                Next
                ' A FIXED 2 cm, not a fraction of the model. Scaling it with
                ' the asset span was wrong: the bottom is a PLANE CUT, so the
                ' tolerance only has to absorb float noise and authoring slop,
                ' neither of which grows with the building. At span-scaled
                ' tolerance the 102 m cathedral got 0.205 m of slack and swept
                ' in edges that were never on its bottom plane - its worst fill
                ' spread 0.18 m, which the flatness check caught.
                Dim bf = BottomFill.Build(pos, tri, meshBottom, 0.02F, settings.WeldTolerance)
                If bf.Indices.Count >= 3 Then
                    Dim fb = totalVerts
                    ' Flat-down normals: the fill is planar and faces the ground.
                    Dim mlo = Single.MaxValue, mhi = Single.MinValue
                    For Each p In bf.Positions
                        verts.Add(p.X) : verts.Add(p.Y) : verts.Add(p.Z)
                        verts.Add(0.0F) : verts.Add(-1.0F) : verts.Add(0.0F)
                        mlo = Math.Min(mlo, p.Y)
                        mhi = Math.Max(mhi, p.Y)
                    Next
                    If mhi - mlo > fillWorstSpread Then
                        fillWorstSpread = mhi - mlo
                        fillWorstName = rp.Name
                    End If
                    Dim ffirst = idx.Count
                    For Each i In bf.Indices
                        idx.Add(fb + i)
                    Next
                    parts.Add(New DrawPart With {
                        .Name = rp.Name & " [bottom fill]", .First = ffirst,
                        .Count = bf.Indices.Count, .Tint = New Vector3(0.85F, 0.12F, 0.12F),
                        .IsFill = True})
                    totalVerts += bf.Positions.Count
                    fillTris += bf.Indices.Count \ 3
                    fillRings += bf.Rings
                    fillOpen += bf.OpenChains
                    fillEdges += bf.BottomEdges
                End If
            End If
        Next

        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.DynamicDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.BindVertexArray(0)

        cutVertCount = cuts.Count \ 6
        GL.BindVertexArray(cutVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, cutVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, cuts.Count * 4, cuts.ToArray(), BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.BindVertexArray(0)

        UpdateTitle(planeD)
    End Sub

    Private Sub UpdateTitle(planeD As Single)
        Dim asset = assets(assetIndex)
        Dim lod = If(asset.Lods.Count > 0, asset.Lods(Math.Min(lodIndex, asset.Lods.Count - 1)), 0)
        Title = String.Format(
            "Slicer - {0}  lod{1}  [{2}/{3}]  {4:N0} tris{5}",
            asset.Name, lod, assetIndex + 1, assets.Count, totalTris,
            If(slicing,
               String.Format("   CUT {0} {1} @ {2:F2} m   {3:N0} clipped, {4:N0} cut edges",
                             EffectiveAxis().ToUpperInvariant(), EffectiveKeep(), planeD, clippedTris, cutSegs),
               "   cut OFF") &
            If(fillOn,
               String.Format("   FILL {0:N0} tris / {1} ring(s), {2} open, {3} edges",
                             fillTris, fillRings, fillOpen, fillEdges),
               "   fill OFF"))
    End Sub

    Private Shared Function PrimitivesPathFor(part As BuildingPart) As String
        If Not String.IsNullOrEmpty(part.Visual) Then
            Return part.Visual.Replace("\"c, "/"c).ToLowerInvariant() & ".primitives_processed"
        End If
        Return part.Path.Substring(0, part.Path.Length - ".model".Length) & ".primitives_processed"
    End Function

    Protected Overrides Sub OnRenderFrame(e As FrameEventArgs)
        MyBase.OnRenderFrame(e)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        If parts.Count > 0 Then
            Dim aspect = CSng(Math.Max(ClientSize.X, 1)) / Math.Max(ClientSize.Y, 1)
            ' The Y term is NEGATED, and that is not a taste setting - it is the
            ' sign nuTerra has.
            '
            ' MapCamera.set_prespective_view builds the eye as
            '     cam_y = sin(CAM_Y_ANGLE) * VIEW_RADIUS
            ' and VIEW_RADIUS IS NEGATIVE there - it is clamped between
            ' MAX_ZOOM_OUT and -0.1, never positive. `dist` here is positive, so
            ' copying the pitch maths exactly (which this does, from
            ' camera_mouse_update) still produced the opposite vertical, because
            ' the radius it gets multiplied by has the other sign.
            '
            ' Only Y is flipped, not the whole radius. Negating all three would
            ' give full parity with nuTerra including its yaw phase, but it would
            ' also reverse left/right drag, which was not what was asked for and
            ' reads as correct as it is.
            Dim eye = target + New Vector3(
                CSng(Math.Cos(pitch) * Math.Sin(yaw)) * dist,
                CSng(Math.Sin(pitch)) * -dist,
                CSng(Math.Cos(pitch) * Math.Cos(yaw)) * dist)
            Dim view = Matrix4.LookAt(eye, target, Vector3.UnitY)
            Dim proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45.0F), aspect,
                Math.Max(dist * 0.001F, 0.02F), dist * 10.0F + 500.0F)
            Dim mvp = view * proj

            GL.UseProgram(shader)
            GL.UniformMatrix4(uMvp, False, mvp)
            GL.BindVertexArray(vao)
            GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))
            For i = 0 To parts.Count - 1
                If soloPart >= 0 AndAlso i <> soloPart Then Continue For
                Dim tc = parts(i).Tint
                If fillDebug Then
                    ' The owner's check: bottom fill red, everything that
                    ' shipped in the package blue. Any red NOT on the underside
                    ' is a fill in the wrong place, which is the failure this is
                    ' meant to make obvious.
                    tc = If(parts(i).IsFill,
                            New Vector3(0.9F, 0.1F, 0.1F),
                            New Vector3(0.16F, 0.34F, 0.78F))
                End If
                GL.Uniform3(uTint, tc.X, tc.Y, tc.Z)
                GL.Uniform1(uFlat, If(wireframe, 1, 0))
                GL.DrawElements(PrimitiveType.Triangles, parts(i).Count,
                                DrawElementsType.UnsignedInt, parts(i).First * 4)
            Next
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

            ' The cut outline last, flat and unlit, with depth test off - the
            ' point of it is to show WHERE the plane met the surface, including
            ' the parts of the cut that sit behind a wall.
            If showCut AndAlso slicing AndAlso cutVertCount > 0 Then
                GL.Disable(EnableCap.DepthTest)
                GL.BindVertexArray(cutVao)
                GL.Uniform1(uFlat, 1)
                GL.Uniform3(uTint, 1.0F, 0.35F, 0.1F)
                GL.LineWidth(2.0F)
                GL.DrawArrays(PrimitiveType.Lines, 0, cutVertCount)
                GL.Enable(EnableCap.DepthTest)
            End If
            GL.BindVertexArray(0)
        End If

        SwapBuffers()

        If shotPath IsNot Nothing Then
            shotFrames += 1
            ' Not the first frame. The window is still sizing itself and the
            ' driver has not necessarily presented anything yet; reading too
            ' early gives a black or half-cleared buffer, which would look
            ' exactly like the fill having failed.
            If shotFrames >= 3 Then
                SaveShot(shotPath)
                shotPath = Nothing
                Close()
            End If
        End If
    End Sub

    ''' <summary>
    ''' Read the default framebuffer and write it out, so this can be checked
    ''' without anyone having to look at the screen.
    '''
    ''' OpenGL hands back rows bottom-up and PNG wants them top-down, so the
    ''' scanlines are reversed on the way into the encoder. Skip that and the
    ''' image is a perfect vertical mirror - which on a symmetrical building is
    ''' genuinely hard to notice, and would quietly invert every up/down
    ''' judgement made from it.
    ''' </summary>
    Private Sub SaveShot(path As String)
        Dim w = ClientSize.X, h = ClientSize.Y
        If w <= 0 OrElse h <= 0 Then Return
        Dim buf(w * h * 3 - 1) As Byte
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1)
        GL.ReadBuffer(ReadBufferMode.Front)
        GL.ReadPixels(0, 0, w, h, PixelFormat.Rgb, PixelType.UnsignedByte, buf)

        Dim flipped(buf.Length - 1) As Byte
        For y = 0 To h - 1
            Array.Copy(buf, (h - 1 - y) * w * 3, flipped, y * w * 3, w * 3)
        Next

        Try
            Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(path))
            If dir IsNot Nothing AndAlso Not IO.Directory.Exists(dir) Then IO.Directory.CreateDirectory(dir)
            PngWriter.WriteRgb(path, w, h, flipped)
            Console.WriteLine("shot: {0}  ({1}x{2})", IO.Path.GetFullPath(path), w, h)
            Console.WriteLine("      fill {0:N0} tris / {1} ring(s), {2} open chain(s), {3} bottom edges",
                              fillTris, fillRings, fillOpen, fillEdges)
            If fillTris > 0 Then
                Console.WriteLine("      worst single-mesh fill spread {0:F4} m{1}{2}",
                                  fillWorstSpread,
                                  If(fillWorstName Is Nothing, "", "  (" & fillWorstName & ")"),
                                  If(fillWorstSpread > 0.05F, "   <-- NOT FLAT, that fill is off its plane", "   flat"))
            End If
        Catch ex As Exception
            Console.WriteLine("shot failed: {0}: {1}", ex.GetType().Name, ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnUpdateFrame(e As FrameEventArgs)
        MyBase.OnUpdateFrame(e)
        Dim k = KeyboardState
        If k.IsKeyDown(Keys.Escape) Then Close()

        If k.IsKeyPressed(Keys.Right) Then
            assetIndex = (assetIndex + 1) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.Left) Then
            assetIndex = (assetIndex - 1 + assets.Count) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : LoadCurrent()
        End If

        If k.IsKeyPressed(Keys.RightBracket) Then
            lodIndex += 1 : soloPart = -1 : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.LeftBracket) Then
            lodIndex = Math.Max(0, lodIndex - 1) : soloPart = -1 : LoadCurrent()
        End If

        If k.IsKeyPressed(Keys.Down) Then
            soloPart += 1
            If soloPart >= parts.Count Then soloPart = -1
            ReportSolo()
        ElseIf k.IsKeyPressed(Keys.Up) Then
            soloPart -= 1
            If soloPart < -1 Then soloPart = parts.Count - 1
            ReportSolo()
        End If

        If k.IsKeyPressed(Keys.W) Then wireframe = Not wireframe
        If k.IsKeyPressed(Keys.B) Then fillDebug = Not fillDebug
        If k.IsKeyPressed(Keys.R) Then LoadCurrent()

        ' ---- the cut and the fill ----
        Dim dirty = False
        If k.IsKeyPressed(Keys.F) Then fillOn = Not fillOn : dirty = True
        If k.IsKeyPressed(Keys.S) Then slicing = Not slicing : dirty = True
        If k.IsKeyPressed(Keys.C) Then showCut = Not showCut
        If k.IsKeyPressed(Keys.X) Then axisOverride = "x" : dirty = True
        If k.IsKeyPressed(Keys.Y) Then axisOverride = "y" : dirty = True
        If k.IsKeyPressed(Keys.Z) Then axisOverride = "z" : dirty = True

        If k.IsKeyPressed(Keys.K) Then
            Select Case EffectiveKeep()
                Case "below" : keepOverride = "above"
                Case "above" : keepOverride = "both"
                Case Else : keepOverride = "below"
            End Select
            dirty = True
        End If

        ' The step scales with the model, so one press is a useful move on an
        ' 8 m shed and on a 206 m dam alike.
        Dim span = (boundsMax - boundsMin).Length
        Dim stepM = Math.Max(span * 0.004F, 0.005F)
        Dim fast = k.IsKeyDown(Keys.LeftShift) OrElse k.IsKeyDown(Keys.RightShift)
        Dim amount = If(fast, stepM * 10.0F, stepM)
        If k.IsKeyDown(Keys.Period) Then planeNudge += amount : dirty = True
        If k.IsKeyDown(Keys.Comma) Then planeNudge -= amount : dirty = True

        If k.IsKeyPressed(Keys.E) Then RunShellPipeline()

        If dirty AndAlso rawParts.Count > 0 Then Rebuild()

        CameraMouseUpdate()
    End Sub

    ''' <summary>
    ''' Rebuild the set model into a shell: look from outside, keep what can be
    ''' seen, weld it, and throw away what the weld collapses.
    '''
    ''' The owner's recipe in his order - rays through to find the outside walls,
    ''' weld every vertex in range, then a post pass removing everything that
    ''' makes a zero-length line. The alternative he named, a ball-pivot walk
    ''' that reconstructs a real surface, is correct and far too slow to sit
    ''' behind a key.
    '''
    ''' ALL PARTS ARE MERGED BEFORE THE SCAN, and they have to be: a wall is
    ''' only interior because ANOTHER part stands outside it. Scanning each part
    ''' alone would find every part to be its own exterior and keep the lot.
    ''' </summary>
    Private Sub RunShellPipeline()
        If rawParts.Count = 0 Then Return
        Dim sw = Diagnostics.Stopwatch.StartNew()

        ' ---- 1. close each part's bottom, THEN merge.
        '
        ' ORDER MATTERS AND THIS WAS WRONG FIRST TIME. The fill ran after the
        ' scan, and the render showed why that fails: with the bottom still
        ' open, rays arriving from below fly straight up into the building and
        ' light up the interior, so the scan calls all of it exterior and keeps
        ' it. Close the bottom first and those same rays stop at the fill, which
        ' is what makes the inside genuinely unseen and therefore removable.
        '
        ' Filling before the merge is equally load-bearing: the fill works per
        ' mesh off that mesh's own lowest point, and merging first collapses
        ' eleven kit pieces at eleven heights into one mesh with a single
        ' bottom. That is exactly what produced one 8-triangle ring where there
        ' should have been eleven.
        Dim allPos As New List(Of Vector3)
        Dim allIdx As New List(Of Integer)
        Dim preFillTris = 0
        For Each rp In rawParts
            Dim b = allPos.Count
            allPos.AddRange(rp.Pos)
            For Each i In rp.Idx
                allIdx.Add(b + i)
            Next

            Dim partBottom = Single.MaxValue
            For Each p In rp.Pos
                If p.Y < partBottom Then partBottom = p.Y
            Next
            Dim pf = BottomFill.Build(rp.Pos, rp.Idx, partBottom, 0.02F, settings.WeldTolerance)
            If pf.Indices.Count >= 3 Then
                Dim fb = allPos.Count
                allPos.AddRange(pf.Positions)
                For Each i In pf.Indices
                    allIdx.Add(fb + i)
                Next
                preFillTris += pf.Indices.Count \ 3
            End If
        Next
        Dim pos = allPos.ToArray()
        Dim idx = allIdx.ToArray()
        Dim before = MeshCheck.Analyse(pos, idx, settings.WeldTolerance)

        ' ---- 2. weld every vert in range
        Dim w1 = MeshWeld.Weld(pos, idx, settings.ShellWeldRange)
        Dim triCount = w1.Indices.Length \ 3
        If triCount <= 0 Then
            Console.WriteLine("shell: nothing survived the weld")
            Return
        End If

        ' ---- 3. rays through, to find the outside walls.
        ' Expanded to three vertices per triangle so the shader can read
        ' gl_VertexID / 3 as the triangle number.
        Dim flat(triCount * 9 - 1) As Single
        For t = 0 To triCount - 1
            For c = 0 To 2
                Dim p = w1.Positions(w1.Indices(t * 3 + c))
                flat(t * 9 + c * 3) = p.X
                flat(t * 9 + c * 3 + 1) = p.Y
                flat(t * 9 + c * 3 + 2) = p.Z
            Next
        Next
        Dim visible = ShellExtract.Visible(flat, triCount, boundsMin, boundsMax,
                                           settings.ShellViews, settings.ShellResolution)
        ' Put the framebuffer and viewport back - the scan borrowed both.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)

        Dim seenCount = 0
        For Each v In visible
            If v Then seenCount += 1
        Next

        ' ---- 4. keep the outside, then weld again, so the raw edges left by
        ' what was removed get stitched and the new degenerates go with them.
        Dim kept = MeshWeld.KeepFlagged(w1.Positions, w1.Indices, visible)
        Dim w2 = MeshWeld.Weld(w1.Positions, kept, settings.ShellWeldRange)
        Dim after = MeshCheck.Analyse(w2.Positions, w2.Indices, settings.WeldTolerance)
        sw.Stop()

        Console.WriteLine()
        Console.WriteLine("SHELL REBUILD  ({0:N0} ms, {1} views at {2}px)",
                          sw.ElapsedMilliseconds, settings.ShellViews, settings.ShellResolution)
        Console.WriteLine("  bottoms closed   {0:N0} tris added before the scan", preFillTris)
        Console.WriteLine("  merged           {0:N0} tris, {1:N0} verts", before.Triangles, before.Vertices)
        Console.WriteLine("  welded @{0:F3} m   {1:N0} tris  (-{2:N0} degenerate, -{3:N0} duplicate)",
                          settings.ShellWeldRange, w1.TrisOut, w1.DroppedDegenerate, w1.DroppedDuplicate)
        Console.WriteLine("  seen from out    {0:N0} of {1:N0} tris  ({2:F1}%)",
                          seenCount, triCount, 100.0 * seenCount / Math.Max(triCount, 1))
        Console.WriteLine("  final            {0:N0} tris, {1:N0} verts", w2.TrisOut, w2.VertsOut)
        Console.WriteLine("  normals          recomputed on the welded shell (IQ, area-weighted)")
        Console.WriteLine()
        Console.WriteLine("  before   {0}", before.Describe())
        Console.WriteLine("  after    {0}", after.Describe())
        Console.WriteLine("  boundary {0:N0} -> {1:N0}    non-manifold {2:N0} -> {3:N0}",
                          before.BoundaryEdges, after.BoundaryEdges,
                          before.NonManifoldEdges, after.NonManifoldEdges)

        rawParts.Clear()
        rawParts.Add(New RawPart With {.Name = "shell", .Pos = w2.Positions, .Idx = w2.Indices})
        Rebuild()
    End Sub

    ''' <summary>
    ''' nuTerra's mouse camera, transcribed from `Window.vb camera_mouse_update`.
    '''
    '''     left drag            orbit      - velocity chases the mouse and coasts
    '''     middle / ctrl drag   pan        - in world axes, rotated by the yaw
    '''     shift drag           height     - raises and lowers the look-at point
    '''     right drag           zoom       - exponential, radius-scaled
    '''
    ''' Two things here are load-bearing and were not obvious from the feel:
    '''
    ''' * THE DAMPING FACTOR IS dt-CORRECTED. nuTerra's own comment says a fixed
    '''   per-frame factor is what killed its first two attempts at this - at
    '''   200+ fps the pool drains before the coast can be felt at all.
    ''' * EVERY POOL GETS A REST SNAP. Exponential decay alone never reaches
    '''   zero, so the camera crawls sub-pixel for seconds after release. In
    '''   nuTerra that kept the virtual-texture feedback re-baking distant pages
    '''   the whole time; here it would just never stop nudging the view.
    '''
    ''' The wheel is kept as well, which nuTerra does NOT do - there the wheel
    ''' belongs to ImGui and the camera never sees it. This viewer has no UI to
    ''' give it to, and a viewer that will not zoom on the wheel reads as broken.
    ''' </summary>
    Private Sub CameraMouseUpdate()
        Dim m = MouseState
        Dim k = KeyboardState

        ' dt from this loop's own clock, clamped the way nuTerra clamps it.
        Dim dt As Single = 0.016F
        If rotClock.IsRunning Then
            dt = Math.Clamp(CSng(rotClock.Elapsed.TotalSeconds), 0.000001F, 0.1F)
        End If
        rotClock.Restart()

        ' 100 px of travel is about `speed` radians - nuTerra's scale.
        Dim dx = m.Delta.X / 100.0F * MOUSE_SPEED
        Dim dy = m.Delta.Y / 100.0F * MOUSE_SPEED

        ' Distance changes speed. nuTerra uses 0.2 * VIEW_RADIUS, which is
        ' negative there; dist is positive here, so the sign is taken out.
        Dim ms = 0.2F * dist

        Dim leftDown = m.IsButtonDown(MouseButton.Left)
        Dim midDown = m.IsButtonDown(MouseButton.Middle)
        Dim rightDown = m.IsButtonDown(MouseButton.Right)
        Dim ctrl = k.IsKeyDown(Keys.LeftControl) OrElse k.IsKeyDown(Keys.RightControl)
        Dim shiftHeld = k.IsKeyDown(Keys.LeftShift) OrElse k.IsKeyDown(Keys.RightShift)
        Dim held = leftDown OrElse midDown

        ' Ignore the frame a button goes down: MouseState.Delta carries the
        ' travel since the last update, which can be a long way if the cursor
        ' was moved elsewhere first, and that arrives as one jump.
        If held AndAlso Not dragging Then
            dragging = True
            dx = 0 : dy = 0
        ElseIf Not held Then
            dragging = False
        End If

        If held Then
            If shiftHeld Then
                ' Height, applied directly - nuTerra does not pool this one.
                target.Y -= dy * ms
            ElseIf midDown OrElse ctrl Then
                Dim ca = CSng(Math.Cos(yaw))
                Dim sa = CSng(Math.Sin(yaw))
                panDeltaX -= (dx * ms) * ca + (dy * ms) * sa
                panDeltaZ -= (dx * ms) * -sa + (dy * ms) * ca
            Else
                rotDeltaX -= dx
                rotDeltaY -= dy
            End If
        ElseIf rightDown Then
            ' Right drag zooms, at nuTerra's 12 * 0.2 sensitivity.
            zoomDelta += dy * 12.0F * 0.2F
        End If

        ' The wheel, this viewer's own addition - straight into the same pool
        ' so it coasts and decays like everything else.
        If m.ScrollDelta.Y <> 0 Then zoomDelta -= m.ScrollDelta.Y * 0.25F

        Dim f = 1.0F - CSng(Math.Pow(1.0F - Math.Min(ROT_DAMPING, 0.999F), dt * 60.0F))

        yaw += rotDeltaX * f
        pitch = Math.Clamp(pitch + rotDeltaY * f, PITCH_MIN, PITCH_MAX)
        If yaw > CSng(Math.PI * 2) Then yaw -= CSng(Math.PI * 2)
        If yaw < 0 Then yaw += CSng(Math.PI * 2)
        rotDeltaX *= (1.0F - f)
        rotDeltaY *= (1.0F - f)
        If Math.Abs(rotDeltaX) < 0.0005F Then rotDeltaX = 0
        If Math.Abs(rotDeltaY) < 0.0005F Then rotDeltaY = 0

        If zoomDelta <> 0 Then
            Dim d = dist * CSng(Math.Exp(zoomDelta * f))
            ' Hitting a clamp kills the pending delta so it cannot grind
            ' against the limit - nuTerra does the same at both ends.
            Dim far = Math.Max((boundsMax - boundsMin).Length * 20.0F, 1000.0F)
            If d > far Then
                d = far : zoomDelta = 0
            ElseIf d < 0.1F Then
                d = 0.1F : zoomDelta = 0
            End If
            dist = d
            zoomDelta *= (1.0F - f)
            If Math.Abs(zoomDelta) < 0.00001F Then zoomDelta = 0
        End If

        If panDeltaX <> 0 OrElse panDeltaZ <> 0 Then
            target.X += panDeltaX * f
            target.Z += panDeltaZ * f
            panDeltaX *= (1.0F - f)
            panDeltaZ *= (1.0F - f)
            If Math.Abs(panDeltaX) < 0.0001F Then panDeltaX = 0
            If Math.Abs(panDeltaZ) < 0.0001F Then panDeltaZ = 0
        End If
    End Sub

    Private Sub ReportSolo()
        If soloPart < 0 Then
            Console.WriteLine("  all {0} parts", parts.Count)
        Else
            Console.WriteLine("  part {0}/{1}  {2}  {3:N0} tris",
                              soloPart + 1, parts.Count, parts(soloPart).Name, parts(soloPart).Count \ 3)
        End If
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)
    End Sub
End Class

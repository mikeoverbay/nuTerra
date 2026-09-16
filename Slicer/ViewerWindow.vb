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

    ' ---- the model browser ----
    Private ui As UiOverlay
    Private browser As ModelBrowser

    ''' <summary>
    ''' When set, ONLY this `.model` is loaded instead of every part of the
    ''' current asset.
    '''
    ''' An asset's lod0 is a kit holding all of its interchangeable variants at
    ''' once - hd_bld_eu_049_thouse has 24 - so loading the whole asset stacks
    ''' two dozen overlapping walls in the same cubic metre. That is what made
    ''' the first OBJ export unreadable. Picking a row narrows to the one mesh;
    ''' Left/Right clears it and goes back to the whole asset.
    ''' </summary>
    Private soloModel As BuildingPart = Nothing

    ' Double-click is measured here rather than asked of the OS: OpenTK reports
    ' button presses, not clicks, so the interval and the travel are ours to
    ' judge. 400 ms and 6 px are the usual Windows defaults.
    Private lastClickAt As DateTime = DateTime.MinValue
    Private lastClickRow As Integer = -1
    Private lastClickPos As Vector2 = Vector2.Zero
    Private pressedOverPanel As Boolean = False
    Private partsClickAt As DateTime = DateTime.MinValue
    Private partsClickRow As Integer = -1

    ''' <summary>--hide: switch off every part whose identifier or source
    ''' .model matches this wildcard, once, after the first load. Exists so a
    ''' scripted shot can show the model without its roof, and so the panel's
    ''' visibility path can be proven without anyone clicking.</summary>
    Private hideQuery As String = Nothing

    ''' <summary>--ui: build the panel even for a --shot, so the interface
    ''' itself can be looked at in a still instead of only over someone's
    ''' shoulder. Off by default, because a strip of UI down one side of every
    ''' comparison shot is worse than useless.</summary>
    Private panelInShot As Boolean = False

    ''' <summary>--find: open with the search box already filled in, the first
    ''' match selected and loaded. Saves arrowing through 1,189 rows to reach a
    ''' known model, and it is how the browser gets exercised end to end without
    ''' anyone having to click.</summary>
    Private startQuery As String = Nothing
    Private Const DOUBLE_CLICK_MS As Double = 400.0
    Private Const DOUBLE_CLICK_PX As Single = 6.0F

    ' The cut is an inspection aid now, not the job - this is an exporter.
    ' S still turns it on to look inside a building.
    Private slicing As Boolean = False
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
    Private killZero, killSliver, killDupe As Integer
    Private supportsOn As Boolean = False
    Private supPillars, supOverhang, supPlate, supModel As Integer
    Private supTallest As Single
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
    ''' <summary>Set by --bake: bake the maps once on load, then quit.</summary>
    Private bakeDir As String = Nothing
    Private bakeSize As Integer = 2048
    ''' <summary>Set by --obj: load an exported OBJ back and draw it, so the
    ''' export can be looked at rather than trusted.</summary>
    Private objPath As String = Nothing
    Private checkerTex As Integer = 0

    ' ---- PBR -------------------------------------------------------------
    ' Its own buffer and its own draw list, deliberately separate from the
    ' sliced path. The clipper interpolates POSITIONS and knows nothing about
    ' UVs or tangents, and a bottom-fill triangle has neither, so feeding the
    ' cut geometry to a textured shader would put garbage texture coordinates
    ' on every cut face. PBR therefore draws the mesh AS AUTHORED.
    Private pbrProgram As Integer = 0
    Private pbrVao As Integer = 0
    Private pbrVbo As Integer = 0
    Private pbrEbo As Integer = 0
    Private pbrOn As Boolean = True
    Private pbrDebug As Integer = 0
    Private pbrExposure As Single = 1.6F
    Private whiteTex As Integer = 0
    Private flatNrmTex As Integer = 0
    Private pbrTris As Integer = 0

    Private Class PbrPart
        Public Name As String
        ''' <summary>The material `identifier` from the .visual_processed. The
        ''' name the GAME gives this piece, which is what the parts panel shows -
        ''' `Name` above is the primitives mesh, a different and coarser thing.</summary>
        Public Ident As String = ""
        ''' <summary>The .model this group came from - Roof_01,
        ''' UpperFloorsSmall_02. The visual's identifiers repeat hard (43
        ''' groups on the townhouse, most of them called s_nd-something), so
        ''' this is what actually tells two rows apart.</summary>
        Public PartName As String = ""
        ''' <summary>Switched off in the parts panel. Checked by all three draw
        ''' passes; it does not touch the geometry, so an export still writes
        ''' the whole model.</summary>
        Public Hidden As Boolean
        Public First As Integer
        Public Count As Integer
        Public Albedo As Integer
        Public NormalTex As Integer
        Public Gmm As Integer
        Public HasNormal As Boolean
        Public PackDxt1 As Boolean
        Public EnableAO As Boolean
        Public Fx As String = ""

        ' ---- PBS_tiled_atlas_global ----
        ' IsAtlas decides which PROGRAM draws this part. The atlas path needs
        ' sampler2DArray and the flat one needs sampler2D, and a single program
        ' carrying both types on one texture unit is undefined GL.
        Public IsAtlas As Boolean
        Public AtlasAm As Integer
        Public AtlasNgs As Integer
        Public AtlasMao As Integer
        Public AtlasBlend As Integer
        Public AtlasDirt As Integer
        Public AtlasGlobal As Integer
        Public Idx As Vector4
        Public Grid As Vector4
        Public Tint0 As Vector4
        Public Tint1 As Vector4
        Public Tint2 As Vector4
        Public UvScale As Vector4
        Public DirtColor As Vector4
        Public DirtParams As Vector4
    End Class
    Private ReadOnly pbrParts As New List(Of PbrPart)
    ''' <summary>Texture path to GL handle, so a map shared by twenty parts is
    ''' uploaded once. The building library leans on shared tile sets heavily.</summary>
    Private ReadOnly texCache As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Atlas path to its array texture. The dam's 56 materials name
    ''' the same three manifests, so this is three uploads and not 168.</summary>
    Private ReadOnly atlasCache As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    Private atlasProgram As Integer = 0
    Private partsPanel As New PartsPanel()
    Private atlasParts As Integer = 0

    ''' <summary>The vestigial tile inset. 0.0 is what the data wants; 0.0625
    ''' reproduces nuTerra's current sampling exactly. See AtlasShader for why
    ''' the two differ and which one is right.</summary>
    Private atlasPad As Single = 0.0F

    ''' <summary>Whether globalTex is mixed into the normal/gloss channel at
    ''' half, as nuTerra does. Switchable because it is the first suspect when
    ''' the surface normal comes out flat.</summary>
    Private atlasMixGlobal As Boolean = True

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
                   Optional shotAngle As String = Nothing, Optional shotCut As Boolean = False,
                   Optional bakeTo As String = Nothing, Optional bakePx As Integer = 2048,
                   Optional objFile As String = Nothing, Optional uiInShot As Boolean = False,
                   Optional findPattern As String = Nothing, Optional debugView As Integer = 0,
                   Optional hidePattern As String = Nothing)
        MyBase.New(GameWindowSettings.Default,
                   New NativeWindowSettings With {
                       .Size = New Vector2i(1280, 800),
                       .Title = "Slicer",
                       .APIVersion = New Version(3, 3),
                       .Profile = ContextProfile.Core,
                       .StartFocused = False})
        pkg = index
        assets = bl.Assets.Values.ToList()
        settings = cfg
        assetIndex = Math.Max(0, Math.Min(startAsset, assets.Count - 1))
        lodIndex = Math.Max(0, cfg.Lod)
        shotPath = shot
        shellOnLoad = doShell
        bakeDir = bakeTo
        objPath = objFile
        panelInShot = uiInShot
        startQuery = findPattern
        pbrDebug = Math.Max(0, debugView)
        hideQuery = hidePattern
        If bakePx >= 64 Then bakeSize = bakePx
        If shotPath IsNot Nothing Then
            ' The angle decides what the picture can prove, so it is explicit
            ' rather than whatever the viewer happened to open at. A before and
            ' an after are only comparable if both used the same one.
            ' A cut normally hides half of whatever is being shown, so shots
            ' default to no cut - unless the cut IS the subject.
            slicing = shotCut
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
        pbrVao = GL.GenVertexArray() : pbrVbo = GL.GenBuffer() : pbrEbo = GL.GenBuffer()
        pbrProgram = PbrShader.Build()
        atlasProgram = AtlasShader.Build()
        whiteTex = DdsTexture.White()
        checkerTex = DdsTexture.Checker(512, 16)
        flatNrmTex = DdsTexture.FlatNormal()

        ' The browser needs a live GL context for its font atlas, so it is built
        ' here and not in the constructor. A --shot run gets no panel at all:
        ' the whole point of a shot is a picture of the MODEL, and a strip of
        ' interface down one side would be in every comparison forever after.
        If shotPath Is Nothing OrElse panelInShot Then
            ui = New UiOverlay(New UiFont("Consolas", 13.0F))
            browser = New ModelBrowser(assets)
            Console.WriteLine("browser: {0:N0} lod0 models across {1:N0} assets",
                              browser.Rows.Count, assets.Count)
        End If

        ' A --find picks the model, so it must run BEFORE the fallback load -
        ' otherwise the whole asset is read out of the packages and thrown away
        ' one line later.
        Dim found = False
        If browser IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(startQuery) Then
            browser.Query = startQuery
            browser.Apply()
            If browser.Shown.Count > 0 Then
                browser.Selected = 0
                LoadRow(browser.Shown(0))
                found = True
            End If
            Console.WriteLine("find {0}: {1:N0} match(es){2}", startQuery, browser.Shown.Count,
                              If(found, "", "  - nothing loaded"))
        End If

        If found Then
            ' already loaded
        ElseIf objPath IsNot Nothing Then
            LoadObj(objPath)
        Else
            LoadCurrent()
        End If
        If shellOnLoad Then RunShellPipeline()
        If bakeDir IsNot Nothing Then
            RunBake(bakeDir, bakeSize)
            If shotPath Is Nothing Then Close()
        End If
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

    ''' <summary>
    ''' The parts to load: every part of the current asset at the current LOD,
    ''' or the single `.model` the browser picked.
    '''
    ''' One place, because three separate loops read this - the geometry load,
    ''' the PBR build and the texture bake. When they disagreed about which
    ''' parts were in play the render and the bake quietly described different
    ''' buildings, and nothing in either output said so.
    ''' </summary>
    Private Function CurrentParts() As List(Of BuildingPart)
        If soloModel IsNot Nothing Then Return New List(Of BuildingPart) From {soloModel}
        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return New List(Of BuildingPart)
        Return asset.PartsAt(lods(Math.Max(0, Math.Min(lodIndex, lods.Count - 1))))
    End Function

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

        For Each part In CurrentParts()
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
        BuildPbr()
        Console.WriteLine("{0}  lod{1}  {2} mesh(es)  {3:N0} tris  span {4:F1} m",
                          asset.Name, lod, rawParts.Count, totalTris, (hi - lo).Length)
    End Sub

    ''' <summary>
    ''' Build the textured buffer: geometry as authored, plus the material each
    ''' primitive group is drawn with.
    '''
    ''' ONE MATERIAL PER PRIMITIVE GROUP, not per mesh. A group is a contiguous
    ''' run of the index buffer, and a single mesh routinely carries several -
    ''' the roof of hd_bld_EU_049_THouse is three groups across two different
    ''' shaders. Drawing a whole mesh with its first material would texture two
    ''' thirds of it wrongly, and plausibly.
    ''' </summary>
    Private Sub BuildPbr()
        pbrParts.Clear()
        pbrTris = 0
        atlasParts = 0
        Dim verts As New List(Of Single)
        Dim idx As New List(Of Integer)

        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return
        Dim lod = lods(Math.Max(0, Math.Min(lodIndex, lods.Count - 1)))

        For Each part In CurrentParts()
            Dim stem = If(Not String.IsNullOrEmpty(part.Visual),
                          part.Visual.Replace("\"c, "/"c).ToLowerInvariant(),
                          part.Path.Substring(0, part.Path.Length - ".model".Length))

            Dim rawPrim = pkg.ReadPath(stem & ".primitives_processed")
            If rawPrim Is Nothing Then Continue For
            Dim meshes As List(Of PrimMesh)
            Try
                meshes = PrimitivesFile.Parse(rawPrim)
            Catch
                Continue For
            End Try

            Dim mats As New List(Of VisualMaterial)
            Dim rawVis = pkg.ReadPath(stem & ".visual_processed")
            If rawVis IsNot Nothing Then
                Try
                    mats = VisualFile.Parse(rawVis).Materials
                Catch
                End Try
            End If

            Dim matAt = 0
            For Each m In meshes
                If m.Positions.Length = 0 OrElse m.Indices.Length < 3 Then Continue For

                Dim baseVert = verts.Count \ 16
                For i = 0 To m.Positions.Length - 1
                    Dim pp = m.Positions(i)
                    Dim nn = If(m.Normals.Length > i, m.Normals(i), Vector3.UnitY)
                    Dim uvv = If(m.UVs.Length > i, m.UVs(i), Vector2.Zero)
                    Dim tt = If(m.HasTangents, m.Tangents(i), Vector3.UnitX)
                    Dim bb = If(m.Binormals.Length > i, m.Binormals(i), Vector3.UnitZ)
                    verts.Add(pp.X) : verts.Add(pp.Y) : verts.Add(pp.Z)
                    verts.Add(nn.X) : verts.Add(nn.Y) : verts.Add(nn.Z)
                    verts.Add(uvv.X) : verts.Add(uvv.Y)
                    verts.Add(tt.X) : verts.Add(tt.Y) : verts.Add(tt.Z)
                    verts.Add(bb.X) : verts.Add(bb.Y) : verts.Add(bb.Z)
                    ' UV2, the second set. The tiled and atlas families address
                    ' their blend mask and their per-object global texture with
                    ' it, so without it here those materials cannot be shaded at
                    ' all - which is why they have been drawing flat white.
                    Dim u2 = If(m.HasUV2 AndAlso m.UV2.Length > i, m.UV2(i), Vector2.Zero)
                    verts.Add(u2.X) : verts.Add(u2.Y)
                Next

                Dim groups = m.Groups
                If groups.Count = 0 Then
                    groups = New List(Of PrimGroup) From {
                        New PrimGroup With {.StartIndex = 0, .PrimitiveCount = m.Indices.Length \ 3}}
                End If

                For Each g In groups
                    Dim first = idx.Count
                    Dim from = Math.Max(0, g.StartIndex)
                    Dim upto = Math.Min(m.Indices.Length, from + g.PrimitiveCount * 3)
                    For i = from To upto - 1
                        Dim vi = m.Indices(i)
                        If vi < 0 OrElse vi >= m.Positions.Length Then vi = 0
                        idx.Add(baseVert + vi)
                    Next
                    Dim count = idx.Count - first
                    If count < 3 Then Continue For

                    Dim mat As VisualMaterial = Nothing
                    If matAt < mats.Count Then mat = mats(matAt)
                    matAt += 1

                    Dim pt As New PbrPart With {
                        .Name = m.Name, .PartName = part.Name, .First = first, .Count = count,
                        .Albedo = whiteTex, .NormalTex = flatNrmTex, .Gmm = whiteTex}
                    If mat IsNot Nothing Then
                        pt.Fx = mat.Fx
                        pt.Ident = mat.Identifier
                        pt.PackDxt1 = mat.Flag("g_useNormalPackDXT1", False)
                        pt.EnableAO = mat.Flag("g_enableAO", False)
                        If mat.IsAtlas Then
                            ' The three atlases are MANIFESTS listing member
                            ' textures, not images - see AtlasFile. Each gets its
                            ' own array at its own members' size: the MAO sheets
                            ' are consistently half the resolution of their AM
                            ' and GBMT siblings, so sizing all three from the
                            ' first would halve or double two of them.
                            Dim am = mat.AtlasMaps()
                            Dim nA = 0, nB = 0, nC = 0
                            pt.AtlasAm = LoadAtlas(am(0), nA)
                            pt.AtlasNgs = LoadAtlas(am(1), nB)
                            pt.AtlasMao = LoadAtlas(am(2), nC)
                            ' All three or none. A part drawn by the atlas
                            ' program with a missing array samples unit 0 as the
                            ' wrong type and comes out black, which reads as a
                            ' shading bug rather than a missing file.
                            pt.IsAtlas = pt.AtlasAm <> 0 AndAlso pt.AtlasNgs <> 0 AndAlso pt.AtlasMao <> 0
                            If pt.IsAtlas Then
                                Dim one As New Vector4(1.0F, 1.0F, 1.0F, 1.0F)
                                ' The blend sheet is declared .png in the material and ships
                                ' as .dds, exactly like the atlas member paths. Without the
                                ' correction it silently falls back to white, and a white
                                ' blend makes every surface one tile - which looks like flat
                                ' untextured grey rather than like a missing file.
                                pt.AtlasBlend = If(am(3) IsNot Nothing, LoadTex(AsDds(am(3)), whiteTex), whiteTex)
                                pt.AtlasDirt = If(am(4) IsNot Nothing, LoadTex(AsDds(am(4)), 0), 0)
                                pt.AtlasGlobal = If(am(5) IsNot Nothing, LoadTex(AsDds(am(5)), 0), 0)
                                pt.Idx = mat.Vec4("g_atlasIndexes", Vector4.Zero)
                                pt.Grid = mat.Vec4("g_atlasSizes", one)
                                pt.Tint0 = mat.Vec4("g_tile0Tint", one)
                                pt.Tint1 = mat.Vec4("g_tile1Tint", one)
                                pt.Tint2 = mat.Vec4("g_tile2Tint", one)
                                pt.UvScale = mat.Vec4("g_tileUVScale", Vector4.Zero)
                                pt.DirtColor = mat.Vec4("g_dirtColor", Vector4.Zero)
                                pt.DirtParams = mat.Vec4("g_dirtParams", one)
                                atlasParts += 1
                            End If
                        End If

                        If Not pt.IsAtlas AndAlso mat.IsTiled Then
                            ' TILED, which is the MAJORITY - 19,121 materials
                            ' against the atlas family's 118, and about four
                            ' fifths of the surface of a typical building. It drew
                            ' flat white until now because the live shader only
                            ' ever implemented PBS_ext, which is why every render
                            ' came out mostly grey.
                            '
                            ' Reuses the atlas path exactly: three tiles in a
                            ' three-layer array, indices 0/1/2, and the blend mask
                            ' on uv2 with a ONE-CELL grid. The grid is forced to
                            ' 1x1 rather than read from g_atlasSizes - tiled
                            ' materials carry that property too, with the atlas
                            ' family's values, and honouring it here would sample
                            ' a twentieth of the blend mask.
                            Dim tAm = TileList(mat.TiledMaps(), 3)
                            Dim tNgs = TileList(mat.TiledNormalMaps(), 3)
                            Dim tMao = TileList(mat.TiledMetalMaps(), 3)
                            If tAm IsNot Nothing AndAlso tNgs IsNot Nothing AndAlso tMao IsNot Nothing Then
                                Dim nA = 0, nB = 0, nC = 0
                                pt.AtlasAm = LoadTileArray(tAm, nA)
                                pt.AtlasNgs = LoadTileArray(tNgs, nB)
                                pt.AtlasMao = LoadTileArray(tMao, nC)
                                pt.IsAtlas = pt.AtlasAm <> 0 AndAlso pt.AtlasNgs <> 0 AndAlso pt.AtlasMao <> 0
                                If pt.IsAtlas Then
                                    Dim one As New Vector4(1.0F, 1.0F, 1.0F, 1.0F)
                                    Dim bm = mat.Texture("blendMask")
                                    Dim dm = mat.Texture("dirtMap")
                                    pt.AtlasBlend = If(bm IsNot Nothing, LoadTex(AsDds(bm), whiteTex), whiteTex)
                                    pt.AtlasDirt = If(dm IsNot Nothing, LoadTex(AsDds(dm), 0), 0)
                                    pt.AtlasGlobal = 0
                                    pt.Idx = New Vector4(0.0F, 1.0F, 2.0F, 0.0F)
                                    pt.Grid = one
                                    pt.Tint0 = mat.Vec4("g_tile0Tint", one)
                                    pt.Tint1 = mat.Vec4("g_tile1Tint", one)
                                    pt.Tint2 = mat.Vec4("g_tile2Tint", one)
                                    pt.UvScale = mat.Vec4("g_tileUVScale", Vector4.Zero)
                                    pt.DirtColor = mat.Vec4("g_dirtColor", Vector4.Zero)
                                    pt.DirtParams = mat.Vec4("g_dirtParams", one)
                                    atlasParts += 1
                                End If
                            End If
                        End If

                        Dim maps = mat.ExtMaps()
                        If maps(0) IsNot Nothing Then pt.Albedo = LoadTex(maps(0), whiteTex)
                        If maps(1) IsNot Nothing Then
                            pt.NormalTex = LoadTex(maps(1), flatNrmTex)
                            pt.HasNormal = pt.NormalTex <> flatNrmTex
                        End If
                        If maps(2) IsNot Nothing Then pt.Gmm = LoadTex(maps(2), whiteTex)
                    End If
                    pbrParts.Add(pt)
                    pbrTris += count \ 3
                Next
            Next
        Next

        If idx.Count = 0 Then Return

        GL.BindVertexArray(pbrVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, pbrVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.StaticDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, pbrEbo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.StaticDraw)
        Const ST As Integer = 16 * 4
        GL.EnableVertexAttribArray(0) : GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, ST, 0)
        GL.EnableVertexAttribArray(1) : GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, ST, 12)
        GL.EnableVertexAttribArray(2) : GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, ST, 24)
        GL.EnableVertexAttribArray(3) : GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, False, ST, 32)
        GL.EnableVertexAttribArray(4) : GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, False, ST, 44)
        GL.EnableVertexAttribArray(5) : GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, False, ST, 56)
        GL.BindVertexArray(0)

        RefreshPartsPanel()
        ApplyHideQuery()

        Dim withN = pbrParts.Where(Function(x) x.HasNormal).Count()
        Console.WriteLine("  pbr: {0} group(s), {1:N0} tris, {2} with a normal map, {3} texture(s) resident",
                          pbrParts.Count, pbrTris, withN, texCache.Count)
    End Sub

    ''' <summary>
    ''' Bake every material of the current asset into flat maps in UV2 space and
    ''' write them beside an MTL.
    '''
    ''' Runs inside the window because it needs a GL context to rasterise into.
    ''' The rest of the export path is headless; this is the one step that
    ''' cannot be.
    ''' </summary>
    Private Sub RunBake(outDir As String, size As Integer)
        If Not IO.Directory.Exists(outDir) Then IO.Directory.CreateDirectory(outDir)
        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return
        Dim lod = lods(Math.Max(0, Math.Min(lodIndex, lods.Count - 1)))

        Console.WriteLine()
        Console.WriteLine("BAKE  {0} -> {1}  at {2}px", asset.Name, IO.Path.GetFullPath(outDir), size)
        Console.WriteLine()

        Dim mtl As New Text.StringBuilder
        Dim written = 0, noUv = 0, noMat = 0
        Dim meshIndex = 0

        For Each part In CurrentParts()
            Dim stem = If(Not String.IsNullOrEmpty(part.Visual),
                          part.Visual.Replace("\"c, "/"c).ToLowerInvariant(),
                          part.Path.Substring(0, part.Path.Length - ".model".Length))
            Dim rawPrim = pkg.ReadPath(stem & ".primitives_processed")
            If rawPrim Is Nothing Then Continue For
            Dim meshes As List(Of PrimMesh)
            Try
                meshes = PrimitivesFile.Parse(rawPrim)
            Catch
                Continue For
            End Try
            Dim mats As New List(Of VisualMaterial)
            Dim rawVis = pkg.ReadPath(stem & ".visual_processed")
            If rawVis IsNot Nothing Then
                Try
                    mats = VisualFile.Parse(rawVis).Materials
                Catch
                End Try
            End If

            For Each m In meshes
                If mats.Count = 0 Then
                    noMat += 1
                    Continue For
                End If
                ' One material per MESH here, not per primitive group. A group
                ' split would want its own bake and its own OBJ group; this
                ' takes the mesh's material and the report says so.
                Dim mat = mats(Math.Min(meshIndex, mats.Count - 1))
                meshIndex += 1
                ' NOT m.Name. A single-section .primitives_processed has one
                ' global vertices/indices pair, and this reader calls that
                ' "mesh" - so every mesh of every part shares the name and each
                ' bake overwrote the last. The PART name is the unique one, and
                ' a suffix disambiguates the rare multi-section file.
                Dim meshTag = If(m.Name = "mesh", "", "_" & m.Name)
                Dim baseName = part.Name & meshTag
                Dim wroteAny = False
                Dim maps = mat.ExtMaps()

                If mat.IsTiled Then
                    ' TILED: three tiles blended through a mask that lives in
                    ' UV2, so this one genuinely has to be baked and cannot be
                    ' done without a uv2.
                    If Not m.HasUV2 Then
                        noUv += 1
                        Console.WriteLine("  {0,-50} tiled but no uv2, cannot bake", m.Name)
                        Continue For
                    End If
                    Dim tm = mat.TiledMaps()
                    Dim t0 = If(tm(0) IsNot Nothing, LoadTex(tm(0), whiteTex), whiteTex)
                    Dim t1 = If(tm(1) IsNot Nothing, LoadTex(tm(1), t0), t0)
                    Dim t2 = If(tm(2) IsNot Nothing, LoadTex(tm(2), t0), t0)
                    Dim bl = If(tm(3) IsNot Nothing, LoadTex(tm(3), whiteTex), whiteTex)
                    Dim dt = If(tm(4) IsNot Nothing, LoadTex(tm(4), 0), 0)
                    Dim res = TextureBake.BakeTiled(m.UVs, m.UV2, m.Indices, t0, t1, t2, bl, dt, size)
                    If res IsNot Nothing Then
                        Dim png = IO.Path.Combine(outDir, baseName & "_AM.png")
                        PngWriter.WriteRgb(png, res.Width, res.Height, res.Pixels)
                        Console.WriteLine("  {0,-50} {1,6:P1} covered   tiled", IO.Path.GetFileName(png), res.Coverage)
                        wroteAny = True
                    End If
                Else
                    ' PBS_ext: its map is a per-object texture already addressed
                    ' by UV1, so UV1 IS the unwrap and there is nothing to
                    ' resample. Decode it straight out at its own resolution.
                    ' Baking it into UV2 would be wrong even where a uv2 exists.
                    For slot = 0 To 2
                        Dim srcPath = maps(slot)
                        If srcPath Is Nothing Then Continue For
                        Dim src = LoadTex(srcPath, 0)
                        If src = 0 Then Continue For
                        Dim suffix = If(slot = 0, "AM", If(slot = 1, "NM", "GMM"))
                        Dim res = TextureBake.Decode(src, size, slot = 1)
                        If res Is Nothing Then Continue For
                        Dim png = IO.Path.Combine(outDir, baseName & "_" & suffix & ".png")
                        PngWriter.WriteRgb(png, res.Width, res.Height, res.Pixels)
                        Console.WriteLine("  {0,-50} {1,6:P1} covered   ext", IO.Path.GetFileName(png), res.Coverage)
                        wroteAny = True
                    Next
                End If

                If wroteAny Then
                    written += 1
                    mtl.AppendLine("newmtl " & baseName)
                    mtl.AppendLine("Ka 1.000 1.000 1.000")
                    mtl.AppendLine("Kd 1.000 1.000 1.000")
                    mtl.AppendLine("map_Kd " & baseName & "_AM.png")
                    If maps(1) IsNot Nothing Then mtl.AppendLine("map_Bump " & baseName & "_NM.png")
                    If maps(2) IsNot Nothing Then mtl.AppendLine("map_Ks " & baseName & "_GMM.png")
                    mtl.AppendLine()
                End If
            Next
        Next

        If mtl.Length > 0 Then
            Dim mtlPath = IO.Path.Combine(outDir, asset.Name & ".mtl")
            IO.File.WriteAllText(mtlPath, mtl.ToString())
            Console.WriteLine()
            Console.WriteLine("  wrote {0}", mtlPath)
        End If
        Console.WriteLine("  {0} material(s) baked, {1} without uv2, {2} without a material",
                          written, noUv, noMat)
    End Sub

    ''' <summary>Draw the textured mesh, one call per primitive group so each
    ''' gets its own material.</summary>
    Private Sub DrawPbr(mvp As Matrix4, eye As Vector3)
        GL.UseProgram(pbrProgram)
        GL.UniformMatrix4(GL.GetUniformLocation(pbrProgram, "u_mvp"), False, mvp)
        GL.Uniform3(GL.GetUniformLocation(pbrProgram, "u_eye"), eye.X, eye.Y, eye.Z)
        ' A fixed key light. There is no map to take a sun from, so it is a
        ' stated choice rather than anything the game would agree with.
        GL.Uniform3(GL.GetUniformLocation(pbrProgram, "u_lightDir"), 0.45F, 0.75F, 0.4F)
        GL.Uniform3(GL.GetUniformLocation(pbrProgram, "u_lightColor"), 1.9F, 1.84F, 1.7F)
        GL.Uniform4(GL.GetUniformLocation(pbrProgram, "u_tint"), 1.0F, 1.0F, 1.0F, 1.0F)
        GL.Uniform1(GL.GetUniformLocation(pbrProgram, "u_debug"), pbrDebug)
        GL.Uniform1(GL.GetUniformLocation(pbrProgram, "u_exposure"), pbrExposure)
        GL.Uniform1(GL.GetUniformLocation(pbrProgram, "u_albedo"), 0)
        GL.Uniform1(GL.GetUniformLocation(pbrProgram, "u_normal"), 1)
        GL.Uniform1(GL.GetUniformLocation(pbrProgram, "u_gmm"), 2)

        Dim uHasN = GL.GetUniformLocation(pbrProgram, "u_hasNormal")
        Dim uPack = GL.GetUniformLocation(pbrProgram, "u_packDXT1")
        Dim uAO = GL.GetUniformLocation(pbrProgram, "u_enableAO")

        GL.BindVertexArray(pbrVao)
        GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))
        For i = 0 To pbrParts.Count - 1
            If soloPart >= 0 AndAlso i <> soloPart Then Continue For
            Dim pt = pbrParts(i)
            If pt.Hidden Then Continue For
            If pt.IsAtlas Then Continue For          ' drawn by DrawAtlas instead
            GL.ActiveTexture(TextureUnit.Texture0) : GL.BindTexture(TextureTarget.Texture2D, pt.Albedo)
            GL.ActiveTexture(TextureUnit.Texture1) : GL.BindTexture(TextureTarget.Texture2D, pt.NormalTex)
            GL.ActiveTexture(TextureUnit.Texture2) : GL.BindTexture(TextureTarget.Texture2D, pt.Gmm)
            GL.Uniform1(uHasN, If(pt.HasNormal, 1, 0))
            GL.Uniform1(uPack, If(pt.PackDxt1, 1, 0))
            GL.Uniform1(uAO, If(pt.EnableAO, 1, 0))
            GL.DrawElements(PrimitiveType.Triangles, pt.Count, DrawElementsType.UnsignedInt, pt.First * 4)
        Next
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' The atlas parts, in their own pass and their own program.
    '''
    ''' A second pass rather than a program switch inside the first: switching
    ''' per part would re-upload every uniform of both programs at every
    ''' boundary. Nothing here runs at all when a model carries no atlas
    ''' material, which is 321 of the 325 assets.
    ''' </summary>
    Private Sub DrawAtlas(mvp As Matrix4, eye As Vector3)
        If atlasParts = 0 Then Return

        GL.UseProgram(atlasProgram)
        GL.UniformMatrix4(GL.GetUniformLocation(atlasProgram, "u_mvp"), False, mvp)
        GL.Uniform3(GL.GetUniformLocation(atlasProgram, "u_eye"), eye.X, eye.Y, eye.Z)
        GL.Uniform3(GL.GetUniformLocation(atlasProgram, "u_lightDir"), 0.45F, 0.75F, 0.4F)
        GL.Uniform3(GL.GetUniformLocation(atlasProgram, "u_lightColor"), 1.9F, 1.84F, 1.7F)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_debug"), pbrDebug)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_exposure"), pbrExposure)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_pad"), atlasPad)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_mixGlobal"), If(atlasMixGlobal, 1, 0))

        ' Every sampler gets its OWN unit. They all default to unit 0, and two
        ' samplers of DIFFERENT TYPES on one unit is undefined - it shows up as
        ' a black surface rather than as an error.
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_am"), 0)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_ngs"), 1)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_mao"), 2)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_blend"), 3)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_dirt"), 4)
        GL.Uniform1(GL.GetUniformLocation(atlasProgram, "u_global"), 5)

        Dim uIdx = GL.GetUniformLocation(atlasProgram, "u_idx")
        Dim uGrid = GL.GetUniformLocation(atlasProgram, "u_grid")
        Dim uT0 = GL.GetUniformLocation(atlasProgram, "u_tint0")
        Dim uT1 = GL.GetUniformLocation(atlasProgram, "u_tint1")
        Dim uT2 = GL.GetUniformLocation(atlasProgram, "u_tint2")
        Dim uSc = GL.GetUniformLocation(atlasProgram, "u_uvScale")
        Dim uDc = GL.GetUniformLocation(atlasProgram, "u_dirtColor")
        Dim uDp = GL.GetUniformLocation(atlasProgram, "u_dirtParams")
        Dim uHd = GL.GetUniformLocation(atlasProgram, "u_hasDirt")
        Dim uHg = GL.GetUniformLocation(atlasProgram, "u_hasGlobal")

        GL.BindVertexArray(pbrVao)
        GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))
        For i = 0 To pbrParts.Count - 1
            If soloPart >= 0 AndAlso i <> soloPart Then Continue For
            Dim pt = pbrParts(i)
            If pt.Hidden Then Continue For
            If Not pt.IsAtlas Then Continue For
            GL.ActiveTexture(TextureUnit.Texture0) : GL.BindTexture(TextureTarget.Texture2DArray, pt.AtlasAm)
            GL.ActiveTexture(TextureUnit.Texture1) : GL.BindTexture(TextureTarget.Texture2DArray, pt.AtlasNgs)
            GL.ActiveTexture(TextureUnit.Texture2) : GL.BindTexture(TextureTarget.Texture2DArray, pt.AtlasMao)
            GL.ActiveTexture(TextureUnit.Texture3) : GL.BindTexture(TextureTarget.Texture2D, pt.AtlasBlend)
            GL.ActiveTexture(TextureUnit.Texture4) : GL.BindTexture(TextureTarget.Texture2D, If(pt.AtlasDirt = 0, whiteTex, pt.AtlasDirt))
            GL.ActiveTexture(TextureUnit.Texture5) : GL.BindTexture(TextureTarget.Texture2D, If(pt.AtlasGlobal = 0, whiteTex, pt.AtlasGlobal))
            GL.Uniform4(uIdx, pt.Idx.X, pt.Idx.Y, pt.Idx.Z, pt.Idx.W)
            GL.Uniform4(uGrid, pt.Grid.X, pt.Grid.Y, pt.Grid.Z, pt.Grid.W)
            GL.Uniform4(uT0, pt.Tint0.X, pt.Tint0.Y, pt.Tint0.Z, pt.Tint0.W)
            GL.Uniform4(uT1, pt.Tint1.X, pt.Tint1.Y, pt.Tint1.Z, pt.Tint1.W)
            GL.Uniform4(uT2, pt.Tint2.X, pt.Tint2.Y, pt.Tint2.Z, pt.Tint2.W)
            GL.Uniform4(uSc, pt.UvScale.X, pt.UvScale.Y, pt.UvScale.Z, pt.UvScale.W)
            GL.Uniform4(uDc, pt.DirtColor.X, pt.DirtColor.Y, pt.DirtColor.Z, pt.DirtColor.W)
            GL.Uniform4(uDp, pt.DirtParams.X, pt.DirtParams.Y, pt.DirtParams.Z, pt.DirtParams.W)
            GL.Uniform1(uHd, If(pt.AtlasDirt <> 0, 1, 0))
            GL.Uniform1(uHg, If(pt.AtlasGlobal <> 0, 1, 0))
            GL.DrawElements(PrimitiveType.Triangles, pt.Count, DrawElementsType.UnsignedInt, pt.First * 4)
        Next
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>Upload an atlas manifest as a texture array, cached by
    ''' path.</summary>
    ''' <summary>A texture path as it actually ships. Materials and atlas
    ''' manifests both record the artist source as .png; the build converts
    ''' it. 880 of 880 atlas members resolve once corrected and 0 before.</summary>
    Private Shared Function AsDds(pth As String) As String
        If pth Is Nothing Then Return Nothing
        If pth.EndsWith(".png", StringComparison.OrdinalIgnoreCase) Then
            Return pth.Substring(0, pth.Length - 4) & ".dds"
        End If
        Return pth
    End Function

    ''' <summary>The first `want` tile paths as .dds, or Nothing if any is
    ''' absent. All three or none: a two-layer array indexed at layer 2 samples
    ''' the wrong tile rather than failing.</summary>
    Private Shared Function TileList(src As String(), want As Integer) As List(Of String)
        If src Is Nothing OrElse src.Length < want Then Return Nothing
        Dim outp As New List(Of String)
        For i = 0 To want - 1
            If src(i) Is Nothing Then Return Nothing
            outp.Add(AsDds(src(i)))
        Next
        Return outp
    End Function

    ''' <summary>Three tile textures as one three-layer array, cached on the
    ''' joined paths - tile sets are shared heavily across the library, so the
    ''' same trio recurs on many materials.</summary>
    Private Function LoadTileArray(paths As List(Of String), ByRef layers As Integer) As Integer
        layers = 0
        If paths Is Nothing OrElse paths.Count = 0 Then Return 0
        Dim key = String.Join("|", paths)
        Dim got = 0
        If atlasCache.TryGetValue(key, got) Then Return got
        Dim tex = AtlasFile.UploadLayers(pkg, paths, layers, quiet:=True)
        atlasCache(key) = tex
        Return tex
    End Function

    Private Function LoadAtlas(atlasPath As String, ByRef layers As Integer) As Integer
        layers = 0
        If String.IsNullOrEmpty(atlasPath) Then Return 0
        Dim got = 0
        If atlasCache.TryGetValue(atlasPath, got) Then Return got

        ' The material names ".atlas"; what ships is ".atlas_processed", the
        ' same _processed suffix the rest of this family carries.
        Dim raw = pkg.ReadPath(atlasPath & "_processed")
        If raw Is Nothing Then raw = pkg.ReadPath(atlasPath)
        If raw Is Nothing Then
            Console.WriteLine("    atlas: no manifest at {0}", atlasPath)
            atlasCache(atlasPath) = 0
            Return 0
        End If

        Dim af = AtlasFile.Load(raw, atlasPath)
        If af Is Nothing Then
            Console.WriteLine("    atlas: {0} is not a manifest", atlasPath)
            atlasCache(atlasPath) = 0
            Return 0
        End If

        Dim tex = af.Upload(pkg, layers, quiet:=False)
        Console.WriteLine("    atlas {0}  {1}", IO.Path.GetFileName(atlasPath), af.Describe())
        atlasCache(atlasPath) = tex
        Return tex
    End Function

    Private Sub ReportPbr()
        Console.WriteLine("  pbr {0}   view: {1}   {2} group(s), {3:N0} tris",
                          If(pbrOn, "ON", "off"), PbrShader.DebugNames(pbrDebug),
                          pbrParts.Count, pbrTris)
        Title = String.Format("Slicer - {0}  PBR {1}  [{2}]  {3:N0} tris",
                              assets(assetIndex).Name, If(pbrOn, "on", "off"),
                              PbrShader.DebugNames(pbrDebug), pbrTris)
    End Sub

    ''' <summary>
    ''' Load an exported OBJ back and draw it, textured with a CHECKER rather
    ''' than its own maps.
    '''
    ''' The checker is the point. Its own texture would only tell you the file
    ''' loads; a checker tells you whether the UVs are RIGHT - squares that are
    ''' square and evenly sized mean a sane unwrap, and anything stretched,
    ''' mirrored or wrapped is unmistakable. That is the question a round trip
    ''' exists to answer, and it is the one the counting checks could not.
    '''
    ''' OBJ lets a face index a position and a UV independently, so corners are
    ''' expanded to one vertex each. That triples the vertex count and is
    ''' unavoidable: an index buffer over positions alone cannot express a
    ''' vertex carrying two different UVs on two faces, which is what a seam is.
    ''' </summary>
    Private Sub LoadObj(path As String)
        Dim o = ObjFile.Load(path)
        Console.WriteLine()
        Console.WriteLine("OBJ  {0}", IO.Path.GetFullPath(path))
        Console.WriteLine("  {0}", o.Describe())
        If o.TriangleCount = 0 Then
            Console.WriteLine("  nothing to draw")
            Return
        End If

        pbrParts.Clear() : rawParts.Clear() : parts.Clear()
        pbrTris = 0

        Dim verts As New List(Of Single)
        Dim idx As New List(Of Integer)
        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)

        For Each pt In o.Parts
            Dim first = idx.Count
            Dim t = pt.First
            Dim stopAt = Math.Min(o.Tri.Count, pt.First + pt.Count)
            While t + 2 < stopAt
                ' Flat normal per triangle: the OBJ written here carries no vn,
                ' and smoothing would hide exactly the creases a round-trip
                ' check wants to show.
                Dim p0 = o.Positions(o.Tri(t))
                Dim p1 = o.Positions(o.Tri(t + 1))
                Dim p2 = o.Positions(o.Tri(t + 2))
                Dim nn = Vector3.Cross(p1 - p0, p2 - p0)
                If nn.LengthSquared > 0.0000001F Then nn.Normalize() Else nn = Vector3.UnitY
                For c = 0 To 2
                    Dim vi = o.Tri(t + c)
                    Dim ti = o.TriUv(t + c)
                    Dim pp = o.Positions(vi)
                    Dim uvv = If(ti >= 0 AndAlso ti < o.UVs.Count, o.UVs(ti), Vector2.Zero)
                    verts.Add(pp.X) : verts.Add(pp.Y) : verts.Add(pp.Z)
                    verts.Add(nn.X) : verts.Add(nn.Y) : verts.Add(nn.Z)
                    verts.Add(uvv.X) : verts.Add(uvv.Y)
                    verts.Add(1.0F) : verts.Add(0.0F) : verts.Add(0.0F)
                    verts.Add(0.0F) : verts.Add(0.0F) : verts.Add(1.0F)
                    ' uv2, to keep the 16-float stride the shared VAO expects.
                    ' An exported OBJ has ONE uv set by design - uv2 was moved
                    ' into it on the way out - so there is no second set to give.
                    verts.Add(0.0F) : verts.Add(0.0F)
                    idx.Add(idx.Count)
                    lo = Vector3.ComponentMin(lo, pp)
                    hi = Vector3.ComponentMax(hi, pp)
                Next
                t += 3
            End While
            Dim count = idx.Count - first
            If count < 3 Then Continue For
            pbrParts.Add(New PbrPart With {
                .Name = pt.Name, .First = first, .Count = count,
                .Albedo = checkerTex, .NormalTex = flatNrmTex, .Gmm = whiteTex,
                .HasNormal = False, .EnableAO = False, .Fx = If(pt.Material, "")})
            pbrTris += count \ 3
        Next

        boundsMin = lo : boundsMax = hi
        target = (lo + hi) * 0.5F
        dist = Math.Max((hi - lo).Length * If(shotPath IsNot Nothing, 1.35F, 0.9F), 2.0F)

        GL.BindVertexArray(pbrVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, pbrVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.StaticDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, pbrEbo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.StaticDraw)
        Const ST As Integer = 16 * 4
        GL.EnableVertexAttribArray(0) : GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, ST, 0)
        GL.EnableVertexAttribArray(1) : GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, ST, 12)
        GL.EnableVertexAttribArray(2) : GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, ST, 24)
        GL.EnableVertexAttribArray(3) : GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, False, ST, 32)
        GL.EnableVertexAttribArray(4) : GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, False, ST, 44)
        GL.EnableVertexAttribArray(5) : GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, False, ST, 56)
        GL.BindVertexArray(0)

        pbrOn = True
        Console.WriteLine("  {0} part(s), {1:N0} tris, span {2:F0} - drawn with a checker",
                          pbrParts.Count, pbrTris, (hi - lo).Length)
        Title = String.Format("Slicer - OBJ {0}  {1:N0} tris", IO.Path.GetFileName(path), pbrTris)
    End Sub

    ''' <summary>Upload a texture once and remember it, because the building
    ''' library shares tile sets heavily - one map can serve twenty parts.
    ''' Returns the fallback when the file is absent or is a DDS variant this
    ''' reader does not handle.</summary>
    Private Function LoadTex(path As String, fallback As Integer) As Integer
        Dim h As Integer
        If texCache.TryGetValue(path, h) Then Return h
        Dim bytes = pkg.ReadPath(path)
        Dim got As DdsTexture.Info
        Dim tex = 0
        If bytes IsNot Nothing Then tex = DdsTexture.Upload(bytes, got)
        If tex = 0 Then tex = fallback
        texCache(path) = tex
        Return tex
    End Function

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
        killZero = 0 : killSliver = 0 : killDupe = 0
        supPillars = 0 : supOverhang = 0 : supPlate = 0 : supModel = 0 : supTallest = 0.0F
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

            ' Kill degenerates BEFORE the cut, not after. A zero-length edge
            ' has no side for the clipper to put it on, a sliver below the weld
            ' tolerance is not a triangle by this app's own definition, and a
            ' stacked duplicate face makes the plane cross the same edge twice
            ' and emit two identical cut segments. All three produce garbage
            ' loops, and none of them is recoverable once the cut has run.
            If settings.DropDegenerate Then
                Dim z = 0, sl = 0, du = 0
                tri = MeshWeld.DropDegenerates(pos, tri, settings.WeldTolerance, z, sl, du)
                killZero += z : killSliver += sl : killDupe += du
                If tri.Length < 3 Then Continue For
            End If

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

            ' Inigo Quilez's area-weighted smoothing - see MeshNormals.
            '
            ' ComputeShared, not Compute: the vertices have to be resolved
            ' through the index to find which of them are actually the same
            ' point before the math is done. 45% of these vertices are
            ' duplicates split at UV seams, and accumulating straight into the
            ' array gives each copy only the faces that named that copy - a
            ' fraction of the faces that really meet there. The result is a
            ' crease down every seam of a surface that ought to be smooth.
            Dim nrm = MeshNormals.ComputeShared(pos, tri, settings.WeldTolerance)

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
               "   fill OFF") &
            If(killZero + killSliver + killDupe > 0,
               String.Format("   KILLED {0:N0} zero, {1:N0} sliver, {2:N0} dupe",
                             killZero, killSliver, killDupe), ""))
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

        ' The 3D gets the window MINUS the panel, and the aspect is computed
        ' from that same width. Drawing the model full-window and laying the
        ' panel over it would centre the building behind the list, and every
        ' framing decision - the `dist` a load picks, what a zoom is centred on
        ' - would be measured against a width that is not the visible one.
        Dim viewX = PanelWidth()
        Dim viewW = Math.Max(1, ClientSize.X - viewX - RightPanelWidth())
        GL.Viewport(viewX, 0, viewW, Math.Max(1, ClientSize.Y))

        If (If(pbrOn, pbrParts.Count, parts.Count)) > 0 Then
            Dim aspect = CSng(viewW) / Math.Max(ClientSize.Y, 1)
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

            ' NOT an early return. Returning here skipped the whole tail of
            ' this method, including the --shot capture - so a PBR shot wrote no
            ' file at all and said nothing about why.
            If pbrOn Then
                DrawPbr(mvp, eye)
                DrawAtlas(mvp, eye)
                GoTo drawn
            End If

            GL.UseProgram(shader)
            GL.UniformMatrix4(uMvp, False, mvp)
            GL.BindVertexArray(vao)
            GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))
            For i = 0 To parts.Count - 1
                If soloPart >= 0 AndAlso i <> soloPart Then Continue For
                ' A MESH is hidden only when every primitive group in it is.
                ' The panel switches groups, which is finer than this pass
                ' can draw - so a half-hidden mesh stays visible rather than
                ' vanishing wholesale, which would not be what was asked for.
                If MeshFullyHidden(parts(i).Name) Then Continue For
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
drawn:
        End If

        ' The panel wants the WHOLE window back, because its coordinates are
        ' window pixels - the same space the mouse arrives in. Leaving the inset
        ' viewport here would squeeze the UI into the 3D area and put every
        ' click a panel-width out.
        If browser IsNot Nothing AndAlso (browser.Visible OrElse partsPanel.Rows.Count > 0) Then
            GL.Viewport(0, 0, Math.Max(1, ClientSize.X), Math.Max(1, ClientSize.Y))
            Dim mp = MouseState.Position
            ui.BeginFrame(ClientSize.X, ClientSize.Y)
            browser.Draw(ui, PanelWidth(), ClientSize.Y, mp.X, mp.Y)
            partsPanel.Draw(ui, ClientSize.X, ClientSize.Y, mp.X, mp.Y)
            ui.EndFrame()
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
            Console.WriteLine("      {0:N0} tris drawn   degenerates killed: {1:N0} zero-edge, {2:N0} sliver, {3:N0} duplicate",
                              totalTris, killZero, killSliver, killDupe)
            If slicing Then Console.WriteLine("      cut: {0:N0} clipped, {1:N0} cut segments", clippedTris, cutSegs)
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

        ' EVERY key below is a bare letter or an arrow, so all of it has to stand
        ' down while the search box has the keyboard - otherwise typing "house"
        ' walks through the shell pipeline, the bottom fill, the cut and the
        ' solid/wireframe toggle on its way to filtering the list. The mouse is
        ' deliberately NOT gated: the camera stays live while you type.
        If browser IsNot Nothing AndAlso browser.Focused Then
            CameraMouseUpdate()
            Return
        End If

        If k.IsKeyDown(Keys.Escape) Then Close()

        If k.IsKeyPressed(Keys.Right) Then
            assetIndex = (assetIndex + 1) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : soloModel = Nothing : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.Left) Then
            assetIndex = (assetIndex - 1 + assets.Count) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : soloModel = Nothing : LoadCurrent()
        End If

        ' A LOD change drops the single-model pick: that pick names a part at
        ' lod0, and it does not exist at lod3. Falling back to the whole asset
        ' is the honest answer rather than silently showing nothing.
        If k.IsKeyPressed(Keys.RightBracket) Then
            lodIndex += 1 : soloPart = -1 : soloModel = Nothing : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.LeftBracket) Then
            lodIndex = Math.Max(0, lodIndex - 1) : soloPart = -1 : soloModel = Nothing : LoadCurrent()
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

        If k.IsKeyPressed(Keys.V) Then partsPanel.Visible = Not partsPanel.Visible
        If k.IsKeyPressed(Keys.W) Then wireframe = Not wireframe
        If k.IsKeyPressed(Keys.B) Then fillDebug = Not fillDebug
        If k.IsKeyPressed(Keys.P) Then pbrOn = Not pbrOn : ReportPbr()
        If k.IsKeyPressed(Keys.D) Then
            pbrDebug = (pbrDebug + 1) Mod PbrShader.DebugNames.Length
            ReportPbr()
        End If
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
        ' A drag that STARTED on the panel is the panel's, for as long as the
        ' button is held - including after the pointer leaves the panel. Testing
        ' where the cursor is right now instead would let a click on a row spin
        ' the model the moment the drag crossed into the 3D view.
        Dim held = (leftDown OrElse midDown) AndAlso Not pressedOverPanel

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
        '
        ' NOT while the pointer is over the browser. `OnMouseWheel` already
        ' routes the wheel to the list there, but this poll of ScrollDelta is a
        ' SECOND, INDEPENDENT reader of the same notch - an override and a poll
        ' do not exclude each other - so without this test one notch scrolled
        ' the list and zoomed the camera at the same time.
        If m.ScrollDelta.Y <> 0 AndAlso Not PointerOverPanel() Then
            zoomDelta -= m.ScrollDelta.Y * 0.25F
        End If

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

    ''' <summary>Every primitive group of this mesh switched off? The flat
    ''' pass draws whole meshes, so it can only honour the panel at that
    ''' coarser grain.</summary>
    Private Function MeshFullyHidden(meshName As String) As Boolean
        Dim any = False
        For Each pt In pbrParts
            If Not String.Equals(pt.Name, meshName, StringComparison.OrdinalIgnoreCase) Then Continue For
            any = True
            If Not pt.Hidden Then Return False
        Next
        Return any
    End Function

    ''' <summary>Refill the parts panel from what BuildPbr just produced.
    ''' Rebuilt rather than merged, because a new model has different parts -
    ''' carrying hidden flags across would switch off a piece of a building
    ''' the owner never hid.</summary>
    Private Sub RefreshPartsPanel()
        If partsPanel Is Nothing Then Return
        partsPanel.Clear()
        For i = 0 To pbrParts.Count - 1
            Dim pt = pbrParts(i)
            pt.Hidden = False
            partsPanel.Add(If(String.IsNullOrEmpty(pt.Ident), pt.Name, pt.Ident),
                           pt.PartName, pt.Fx, pt.Count \ 3, i)
        Next
    End Sub

    ''' <summary>Is the cursor over the browser panel right now? Asked by every
    ''' input path the panel has to win.</summary>
    Private Function PointerOverPanel() As Boolean
        If browser Is Nothing OrElse Not browser.Visible Then Return False
        Dim p = MouseState.Position
        Return browser.HitsPanel(p.X, p.Y) OrElse partsPanel.HitsPanel(p.X, p.Y)
    End Function

    ''' <summary>Pixels the panel takes off the left of the 3D view, 0 when it
    ''' is hidden or was never built (a --shot run). This is the ONE width -
    ''' the draw and the hit tests both take it, so they cannot drift.</summary>
    ''' <summary>Pixels the parts panel takes off the RIGHT of the 3D view.
    ''' Zero when nothing is loaded, so an empty viewer is not framed by a
    ''' blank strip.</summary>
    Private Function RightPanelWidth() As Integer
        If partsPanel Is Nothing OrElse Not partsPanel.Visible OrElse partsPanel.Rows.Count = 0 Then Return 0
        Return Math.Min(PartsPanel.PANEL_W, Math.Max(0, ClientSize.X - 160))
    End Function

    Private Function PanelWidth() As Integer
        If browser Is Nothing OrElse Not browser.Visible Then Return 0
        Return Math.Min(ModelBrowser.PANEL_W, Math.Max(0, ClientSize.X - 160))
    End Function

    ''' <summary>
    ''' Load the single `.model` a row names.
    '''
    ''' The asset and LOD are set to that model's own, so the title, the PBR
    ''' build and a bake all stay consistent with what is on screen - only the
    ''' PART list is narrowed. Setting `soloModel` without moving `assetIndex`
    ''' would leave the window captioned with whatever was loaded before.
    ''' </summary>
    Private Sub LoadRow(r As ModelRow)
        If r Is Nothing Then Return
        assetIndex = Math.Max(0, Math.Min(r.AssetIndex, assets.Count - 1))
        Dim lods = assets(assetIndex).Lods
        lodIndex = Math.Max(0, lods.IndexOf(r.Part.Lod))
        soloModel = r.Part
        soloPart = -1
        Console.WriteLine("load {0}  ({1})", r.Part.Name, r.AssetName)
        LoadCurrent()
    End Sub

    ''' <summary>Characters for the search box. Only reaches the box when it has
    ''' focus, so the viewer's letter hotkeys are untouched otherwise.</summary>
    Protected Overrides Sub OnTextInput(e As TextInputEventArgs)
        MyBase.OnTextInput(e)
        If browser Is Nothing OrElse Not browser.Focused Then Return
        For Each c In e.AsString
            browser.TypeChar(c)
        Next
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseWheelEventArgs)
        MyBase.OnMouseWheel(e)
        If browser Is Nothing Then Return
        ' Only when the pointer is actually over a panel - otherwise the wheel
        ' belongs to the camera, which reads it in CameraMouseUpdate.
        If Not PointerOverPanel() Then Return
        Dim p = MouseState.Position
        Dim lines = -CInt(Math.Sign(e.OffsetY)) * 3
        If partsPanel.HitsPanel(p.X, p.Y) Then
            partsPanel.ScrollBy(lines)
        ElseIf browser.Visible Then
            browser.ScrollBy(lines)
        End If
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseButtonEventArgs)
        MyBase.OnMouseDown(e)
        If browser Is Nothing Then Return
        Dim p = MouseState.Position
        pressedOverPanel = browser.HitsPanel(p.X, p.Y) OrElse partsPanel.HitsPanel(p.X, p.Y)
        If Not pressedOverPanel OrElse e.Button <> MouseButton.Left Then Return

        ' The right panel first, and independently of the left one - Tab
        ' hides the model list and the parts must keep working.
        If partsPanel.HitsPanel(p.X, p.Y) Then
            HandlePartsClick(p)
            Return
        End If
        If Not browser.Visible Then Return

        If browser.HitsSearch(p.X, p.Y) Then
            browser.Focused = True
            Return
        End If

        Dim row = browser.RowAtPixel(p.X, p.Y)
        If row < 0 Then
            browser.Focused = False
            Return
        End If

        ' A double click is the SAME row, twice, close together in both time and
        ' space. Testing time alone fires on a fast pair of clicks on different
        ' rows and loads whichever was second, which reads as the list picking
        ' at random.
        Dim now = DateTime.UtcNow
        Dim near = (New Vector2(p.X, p.Y) - lastClickPos).Length <= DOUBLE_CLICK_PX
        Dim quick = (now - lastClickAt).TotalMilliseconds <= DOUBLE_CLICK_MS
        browser.Selected = row
        browser.Focused = False
        If quick AndAlso near AndAlso row = lastClickRow Then
            LoadRow(browser.Shown(row))
            lastClickAt = DateTime.MinValue          ' a triple click is not two loads
            lastClickRow = -1
        Else
            lastClickAt = now
            lastClickRow = row
            lastClickPos = New Vector2(p.X, p.Y)
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseButtonEventArgs)
        MyBase.OnMouseUp(e)
        pressedOverPanel = False
    End Sub


    ''' <summary>
    ''' A click in the parts panel.
    '''
    ''' Single click toggles the part under the pointer; double click SOLOS it,
    ''' showing that one and hiding the rest. Solo earns its gesture on a model
    ''' like the townhouse - 43 groups, and "which piece is s_nd4" is not a
    ''' question you answer by switching off forty things one at a time.
    '''
    ''' Its own click clock, not the browser's. Sharing one would make a click on
    ''' a row here and a click on a row there read as a double click, which is
    ''' the same class of bug as testing a double click by time alone.
    ''' </summary>
    Private Sub HandlePartsClick(p As Vector2)
        If partsPanel.HitsShowAll(p.X, p.Y) Then
            partsPanel.ShowAll() : ApplyPartVisibility() : Return
        End If
        If partsPanel.HitsHideAll(p.X, p.Y) Then
            partsPanel.HideAll() : ApplyPartVisibility() : Return
        End If

        Dim row = partsPanel.RowAtPixel(p.X, p.Y)
        If row < 0 Then Return

        Dim now = DateTime.UtcNow
        Dim quick = (now - partsClickAt).TotalMilliseconds <= DOUBLE_CLICK_MS
        If quick AndAlso row = partsClickRow Then
            partsPanel.Solo(row)
            partsClickAt = DateTime.MinValue
            partsClickRow = -1
            Console.WriteLine("  solo {0}", partsPanel.Rows(row).Ident)
        Else
            partsPanel.Rows(row).Hidden = Not partsPanel.Rows(row).Hidden
            partsClickAt = now
            partsClickRow = row
        End If
        ApplyPartVisibility()
    End Sub

    ''' <summary>Apply --hide, if one was given. Matched against
    ''' "identifier partname" so either half of what the panel shows can be
    ''' named, with the same wildcard the model list uses.</summary>
    Private Sub ApplyHideQuery()
        If String.IsNullOrWhiteSpace(hideQuery) Then Return
        Dim pat = hideQuery.Trim().ToLowerInvariant()
        If pat.IndexOf("*"c) < 0 Then pat = "*" & pat & "*"
        Dim n = 0
        For Each r In partsPanel.Rows
            If ModelBrowser.WildcardMatch((r.Ident & " " & r.MeshName).ToLowerInvariant(), pat) Then
                r.Hidden = True
                n += 1
            End If
        Next
        ApplyPartVisibility()
        Console.WriteLine("  hide {0}: {1} of {2} part(s) switched off", hideQuery, n, partsPanel.Rows.Count)
    End Sub

    ''' <summary>Push the panel's switches onto the parts the draw passes read.
    ''' The panel holds the intent and PbrPart holds the flag, so nothing in the
    ''' render loop has to know a panel exists.</summary>
    Private Sub ApplyPartVisibility()
        For Each r In partsPanel.Rows
            If r.Index >= 0 AndAlso r.Index < pbrParts.Count Then
                pbrParts(r.Index).Hidden = r.Hidden
            End If
        Next
    End Sub

    ''' <summary>Editing keys, which do not arrive as text input.</summary>
    Protected Overrides Sub OnKeyDown(e As KeyboardKeyEventArgs)
        MyBase.OnKeyDown(e)
        If browser Is Nothing Then Return

        ' Tab toggles the panel whether or not anything has focus.
        If e.Key = Keys.Tab Then
            browser.Visible = Not browser.Visible
            If Not browser.Visible Then browser.Focused = False
            Return
        End If

        If Not browser.Visible Then Return

        If browser.Focused Then
            Select Case e.Key
                Case Keys.Backspace : browser.Backspace()
                Case Keys.Escape
                    ' Escape leaves the box rather than closing the window. The
                    ' window's Escape still works the moment focus is elsewhere.
                    If browser.Query.Length > 0 Then browser.ClearQuery() Else browser.Focused = False
                Case Keys.Enter, Keys.KeyPadEnter
                    If browser.Shown.Count > 0 Then
                        If browser.Selected < 0 Then browser.MoveSelection(1)
                        LoadRow(browser.Shown(browser.Selected))
                    End If
                Case Keys.Down : browser.MoveSelection(1)
                Case Keys.Up : browser.MoveSelection(-1)
                Case Keys.PageDown : browser.MoveSelection(10)
                Case Keys.PageUp : browser.MoveSelection(-10)
            End Select
            Return
        End If

        ' Not focused: F starts a search the way every list does.
        If e.Key = Keys.Slash Then browser.Focused = True
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)
    End Sub
End Class

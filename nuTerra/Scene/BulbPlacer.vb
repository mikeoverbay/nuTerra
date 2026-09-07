Imports System.IO
Imports System.Runtime.InteropServices
Imports ImGuiNET
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' The Light Bulb Placer. Puts LIGHTS ON A LIGHT MODEL - a bulb in a street
''' lamp's hood, a flame on a brazier - in the model's own space, and saves them
''' as the bulb table of the map's .campath (MapCamPath.CamBulb). Every instance
''' of that model on the map then carries the light. It does not place lamps on
''' the terrain: the map already says where every lamp stands.
'''
''' Three panes. LEFT: the fire and lamp models on the loaded map, by name, with
''' how many of each. CENTRE: one 3D view of the chosen model on a GL surface
''' sized to the pane - orbit with the left mouse, wheel to zoom, and three
''' buttons that snap to exact Top, Front and Side orthographic views for a
''' number you can trust. A crosshair marks the bulb: right-drag moves it in
''' the horizontal plane, Shift + right-drag moves it up and down. The light is
''' drawn as a wire sphere for its range and a wire cone for its aim, so the
''' view is also the preview. RIGHT: the lights on this model, add and remove,
''' and for the selected one its type - point, cone, or inverse cone (omni
''' EXCEPT inside the cone, which is what a cowled lamp does) - colour, level,
''' range, cone angle, edge blend, fog mix and curve. Save writes the table.
'''
''' STANDALONE. The model is read again from the pkg by its own small loader -
''' position and normal, 24 bytes a vertex - and drawn by its own shaders. The
''' map's models stay in the shared buffers and render exactly as before;
''' nothing here touches the main model path, and nothing in the main path
''' knows this exists.
''' </summary>
Public Class BulbPlacer
    Implements IDisposable

    ' ------------------------------------------------------------------ list
    Public Structure Entry
        Public model_id As Integer
        Public label As String
        ''' <summary>The .primitives path - the campath bulb key.</summary>
        Public primitives As String
        Public kind As String
        Public count As Integer
        ''' <summary>LOD 0 section names, kept because MAP_MODELS is erased at
        ''' the end of the load and the standalone loader needs them later.</summary>
        Public primitive_name As String
        Public verts_names As List(Of String)
        Public prims_names As List(Of String)
    End Structure

    ''' <summary>
    ''' The light models of the loaded map, collected by MapLoader just before
    ''' it erases MAP_MODELS. Everything the placer needs to list a model and
    ''' read it again from the pkg; the instance transforms stay reachable
    ''' through MODEL_BATCH_LIST and MODEL_INDEX_LIST, which outlive the load.
    ''' </summary>
    Public Shared LIGHT_MODELS As New List(Of Entry)

    Private entries As New List(Of Entry)
    Private list_map As String = ""
    Private sel As Integer = -1

    ' --------------------------------------------------------- model on show
    Private meshes As New List(Of LampMesh)
    Private bmin, bmax As Vector3
    Private has_model As Boolean
    Private shown_id As Integer = -1

    ' ---------------------------------------------------------------- camera
    Private yaw As Single = 0.8F
    Private pitch As Single = 0.45F
    Private dist As Single = 12.0F
    Private target As Vector3
    ''' <summary>0 perspective, 1 top, 2 front, 3 side. Orbiting returns to 0.</summary>
    Private ortho As Integer = 0
    Private ortho_half As Single = 5.0F
    Private Const FOV As Single = 0.9F
    Private ldrag, mdrag, rdrag As Boolean

    ' ------------------------------------------------------- bulbs, editing
    ''' <summary>Working copy of THIS model's bulbs. Other models' bulbs stay in
    ''' the campath untouched and are written back beside these on Save.</summary>
    Private edits As New List(Of MapCamPath.CamBulb)
    Private cur As Integer = -1
    Private move_aim As Boolean = False
    Private dirty As Boolean = False
    Private status As String = ""

    ' ------------------------------------------------------------- GL side
    Public pane_w As Integer = 0
    Public pane_h As Integer = 0
    Private tex_w, tex_h As Integer
    Private fbo As GLFramebuffer
    Private color_tex As GLTexture
    Private depth_rb As GLRenderbuffer
    Private line_vao As GLVertexArray
    Private line_vbo As GLBuffer
    Private line_cap As Integer = 0

    Private split_left As Single = 210.0F
    Private split_right As Single = 300.0F

    Private Shared ReadOnly KIND_NAMES As String() = {"point", "cone", "inverse cone"}

    ' =====================================================================
    '  The list
    ' =====================================================================

    ''' <summary>
    ''' The light models on the loaded map, classified the way the catalogue
    ''' scan classifies them: the MATERIAL first (a GFX flame sheet is
    ''' volumetric all the way through; a lit lamp glass is glow.fx), then the
    ''' name for the lamp posts that are ordinary geometry. Firewood, fire
    ''' stairs and hydrants are named out.
    ''' </summary>
    Private Sub rebuild_list()
        entries = New List(Of Entry)(LIGHT_MODELS)
        sel = -1
    End Sub

    ''' <summary>
    ''' Called by MapLoader while MAP_MODELS is still alive. Fills LIGHT_MODELS
    ''' with the light models of this map - names, kinds, counts, section names.
    ''' </summary>
    Public Shared Sub CollectLightModels()
        Dim entries = LIGHT_MODELS
        entries.Clear()
        If MODEL_BATCH_LIST Is Nothing OrElse MAP_MODELS Is Nothing Then Return

        Dim shader_of As New Dictionary(Of Integer, Integer)
        If materials IsNot Nothing Then
            For Each mv In materials.Values
                shader_of(mv.id) = CInt(mv.shader_type)
            Next
        End If
        Dim LAMP_PATTERNS = {"streetlamp", "street_lamp", "lamp", "fonar", "lantern"}
        Dim FIRE_PATTERNS = {"fire", "ogon", "torch", "smokebotton", "campfire"}
        Dim NOT_A_LIGHT = {"firewood", "firewoodstack", "firewoodpile", "firestairs",
                           "fireplug", "fireshield", "firetower", "firezone",
                           "smoke_end_fire", "lamp_glare"}

        Dim seen As New Dictionary(Of Integer, Integer)   ' model_id -> entry index
        For Each batch In MODEL_BATCH_LIST
            If batch.model_id < 0 OrElse batch.model_id >= MAP_MODELS.Length Then Continue For
            Dim idx = 0
            If seen.TryGetValue(batch.model_id, idx) Then
                Dim e = entries(idx) : e.count += batch.count : entries(idx) = e
                Continue For
            End If
            Dim lods = MAP_MODELS(batch.model_id).modelLods
            If lods Is Nothing OrElse lods.Length = 0 Then Continue For
            Dim sets0 = lods(0).render_sets
            If sets0 Is Nothing OrElse sets0.Count = 0 Then Continue For
            Dim vn = sets0(0).verts_name
            If vn Is Nothing Then Continue For
            Dim prim = vn
            If prim.EndsWith("/vertices") Then prim = prim.Substring(0, prim.Length - 9)
            Dim low = prim.ToLowerInvariant()

            Dim n_groups = 0, n_vol = 0, n_glow = 0
            For Each rs In sets0
                If rs.primitiveGroups Is Nothing Then Continue For
                For Each pg In rs.primitiveGroups.Values
                    n_groups += 1
                    Dim st = 0
                    If shader_of.TryGetValue(pg.material_id, st) Then
                        If st = CInt(ShaderTypes.FX_volumetric) Then n_vol += 1
                        If st = CInt(ShaderTypes.FX_glow) Then n_glow += 1
                    End If
                Next
            Next
            Dim excluded = NOT_A_LIGHT.Any(Function(p) low.Contains(p))
            Dim name_fire = Not excluded AndAlso FIRE_PATTERNS.Any(Function(p) low.Contains(p))
            Dim name_lamp = Not excluded AndAlso LAMP_PATTERNS.Any(Function(p) low.Contains(p))

            Dim kind As String = Nothing
            If n_groups > 0 AndAlso n_vol = n_groups Then
                If name_fire Then kind = "fire"
            ElseIf n_groups > 0 AndAlso n_glow > 0 AndAlso n_vol = 0 Then
                kind = "glow"
            ElseIf name_lamp Then
                kind = "lamp"
            ElseIf name_fire Then
                kind = "fire"
            End If
            If kind Is Nothing Then Continue For

            Dim nm = prim.Substring(prim.LastIndexOf("/"c) + 1).Replace(".primitives", "")
            seen(batch.model_id) = entries.Count
            Dim ent As New Entry With {.model_id = batch.model_id, .primitives = prim,
                                       .kind = kind, .count = batch.count, .label = nm,
                                       .primitive_name = lods(0).primitive_name,
                                       .verts_names = New List(Of String), .prims_names = New List(Of String)}
            For Each rs In sets0
                ent.verts_names.Add(rs.verts_name)
                ent.prims_names.Add(rs.prims_name)
            Next
            entries.Add(ent)
        Next
        entries.Sort(Function(a, b) String.Compare(a.kind & a.label, b.kind & b.label, StringComparison.OrdinalIgnoreCase))
        LogThis("bulb placer: {0} light model(s) on {1}", entries.Count, MAP_NAME_NO_PATH)
    End Sub

    ' =====================================================================
    '  The standalone loader
    ' =====================================================================

    ''' <summary>
    ''' Read one model's LOD 0 straight from the pkg and keep position and
    ''' normal in buffers of its own. The main loader erases its CPU arrays the
    ''' moment they reach the card, so this reads the .primitives again - a few
    ''' hundred kilobytes, once per selection.
    ''' </summary>
    Private Sub load_model(e As Entry)
        clear_model()
        If e.prims_names Is Nothing OrElse e.prims_names.Count = 0 Then Return

        ' A fresh holder with the same section names: load_primitive fills its
        ' buffers and never sees the map's own entries (which are gone anyway -
        ' MAP_MODELS is erased at the end of the load).
        Dim holder As New base_model_holder_ With {
            .primitive_name = e.primitive_name,
            .render_sets = New List(Of RenderSetEntry)}
        For i = 0 To e.prims_names.Count - 1
            ' primitiveGroups must exist: load_primitives_indices writes each
            ' group it finds into it, and a Nothing dictionary threw on the
            ' first model anyone picked.
            holder.render_sets.Add(New RenderSetEntry With {
                .verts_name = e.verts_names(i), .prims_name = e.prims_names(i),
                .primitiveGroups = New Dictionary(Of Integer, PrimitiveGroup)})
        Next

        Dim filename = e.prims_names(0).Replace(".primitives", ".primitives_processed")
        filename = filename.Substring(0, filename.LastIndexOf("/"c))
        Dim entry = ResMgr.Lookup(filename)
        If entry Is Nothing Then
            status = "not in any pkg: " & filename
            LogThis("bulb placer: {0}", status)
            Return
        End If
        Try
            Using ms As New MemoryStream()
                entry.Extract(ms)
                load_primitive(ms, holder)
            End Using
        Catch ex As Exception
            status = "could not read " & Path.GetFileName(filename) & ": " & ex.Message
            LogThis("bulb placer: {0}", status)
            Return
        End Try

        Dim first = True
        For Each rs In holder.render_sets
            Dim verts = rs.buffers.vertexBuffer
            Dim tris = rs.buffers.index_buffer32
            If verts Is Nothing OrElse tris Is Nothing OrElse verts.Length = 0 OrElse tris.Length = 0 Then Continue For
            Dim slim(verts.Length - 1) As LampVertex
            For i = 0 To verts.Length - 1
                slim(i).pos = verts(i).pos
                slim(i).nrm = New Vector3(CSng(verts(i).normal.X), CSng(verts(i).normal.Y), CSng(verts(i).normal.Z))
                If first Then
                    bmin = verts(i).pos : bmax = verts(i).pos : first = False
                Else
                    bmin = Vector3.ComponentMin(bmin, verts(i).pos)
                    bmax = Vector3.ComponentMax(bmax, verts(i).pos)
                End If
            Next
            Dim idx(tris.Length * 3 - 1) As UInteger
            For i = 0 To tris.Length - 1
                idx(i * 3) = tris(i).x : idx(i * 3 + 1) = tris(i).y : idx(i * 3 + 2) = tris(i).z
            Next
            Dim m As New LampMesh With {.index_count = idx.Length}
            m.vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "bulbMeshVbo")
            m.vbo.Storage(slim.Length * Marshal.SizeOf(Of LampVertex), slim, BufferStorageFlags.None)
            m.ibo = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "bulbMeshIbo")
            m.ibo.Storage(idx.Length * 4, idx, BufferStorageFlags.None)
            m.vao = GLVertexArray.Create("bulbMeshVao")
            m.vao.VertexBuffer(0, m.vbo, IntPtr.Zero, Marshal.SizeOf(Of LampVertex))
            m.vao.ElementBuffer(m.ibo)
            m.vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
            m.vao.AttribBinding(0, 0) : m.vao.EnableAttrib(0)
            m.vao.AttribFormat(1, 3, VertexAttribType.Float, False, 12)
            m.vao.AttribBinding(1, 0) : m.vao.EnableAttrib(1)
            meshes.Add(m)
        Next
        has_model = meshes.Count > 0
        If Not has_model Then
            status = "no geometry in " & Path.GetFileName(filename)
            LogThis("bulb placer: {0}", status)
            Return
        End If
        LogThis("bulb placer: {0} - {1} mesh(es), box {2:0.00} x {3:0.00} x {4:0.00} m",
                e.label, meshes.Count, bmax.X - bmin.X, bmax.Y - bmin.Y, bmax.Z - bmin.Z)
        shown_id = e.model_id
        frame_model()
    End Sub

    Private Sub clear_model()
        For Each m In meshes
            m.Dispose()
        Next
        meshes.Clear()
        has_model = False
        shown_id = -1
    End Sub

    Private Sub frame_model()
        target = (bmin + bmax) * 0.5F
        Dim d = bmax - bmin
        Dim r = Math.Max(0.5F, Math.Max(d.X, Math.Max(d.Y, d.Z)))
        dist = r * 1.6F
        ortho_half = r * 0.62F
        yaw = 0.8F : pitch = 0.35F : ortho = 0
    End Sub

    ' =====================================================================
    '  Selection and the bulbs of a model
    ' =====================================================================

    Private Sub select_entry(i As Integer)
        If i < 0 OrElse i >= entries.Count Then Return
        sel = i
        status = ""
        load_model(entries(i))
        pull_edits()
    End Sub

    ''' <summary>This model's bulbs out of the campath into the working list.</summary>
    Private Sub pull_edits()
        edits.Clear()
        cur = -1
        dirty = False
        If sel < 0 OrElse map_scene Is Nothing OrElse map_scene.cam_path.bulbs Is Nothing Then Return
        Dim key = entries(sel).primitives
        For Each b In map_scene.cam_path.bulbs
            If String.Equals(b.primitives, key, StringComparison.OrdinalIgnoreCase) Then edits.Add(b)
        Next
        ' A model with no lights yet gets one, unsaved, at the top of its box,
        ' so there is always a crosshair to pick up and drag. Nothing reaches
        ' the file until Save.
        If edits.Count = 0 AndAlso has_model Then edits.Add(new_bulb())
        If edits.Count > 0 Then cur = 0
    End Sub

    Private Function new_bulb() As MapCamPath.CamBulb
        ' Top centre of the box: within a metre of the bulb on every street
        ' lamp in the game. The aim looks straight down from there.
        Dim top = New Vector3((bmin.X + bmax.X) * 0.5F, bmax.Y, (bmin.Z + bmax.Z) * 0.5F)
        Dim b As New MapCamPath.CamBulb With {
            .primitives = If(sel >= 0, entries(sel).primitives, ""),
            .kind = MapCamPath.BULB_POINT,
            .pos = top,
            .aim = top - New Vector3(0.0F, Math.Max(1.0F, bmax.Y - bmin.Y), 0.0F),
            .cone = 120.0F, .blend = 0.35F,
            .color = New Vector3(1.0F, 0.85F, 0.63F),
            .level = 0.5F, .range_m = 20.0F, .vol_mix = 0.45F, .curve = 0}
        If entries.Count > 0 AndAlso sel >= 0 AndAlso entries(sel).kind = "fire" Then
            b.color = New Vector3(1.0F, 0.6F, 0.25F)
            b.pos = (bmin + bmax) * 0.5F
        End If
        Return b
    End Function

    Private Sub save()
        If map_scene Is Nothing OrElse sel < 0 Then Return
        Dim key = entries(sel).primitives
        Dim all As New List(Of MapCamPath.CamBulb)
        If map_scene.cam_path.bulbs IsNot Nothing Then
            For Each b In map_scene.cam_path.bulbs
                If Not String.Equals(b.primitives, key, StringComparison.OrdinalIgnoreCase) Then all.Add(b)
            Next
        End If
        all.AddRange(edits)
        status = map_scene.cam_path.SaveBulbs(all.ToArray())
        dirty = False
    End Sub

    ' =====================================================================
    '  The ImGui panel
    ' =====================================================================

    Public Sub Draw(client As Vector2i, origin As System.Numerics.Vector2, size As System.Numerics.Vector2)
        If Not SHOW_LAMP_VIEW Then Return
        ' Only once a map is up. Asked for before the load finishes (the
        ' placer=1 flag, or the button during a load) it simply waits.
        If map_scene Is Nothing OrElse Not MAP_LOADED Then Return

        ImGui.SetNextWindowPos(origin, ImGuiCond.FirstUseEver)
        ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver)
        ' Constrained every frame - imgui.ini beats FirstUseEver. docs/ui_panels.md.
        ImGui.SetNextWindowSizeConstraints(
            New System.Numerics.Vector2(640, 320),
            New System.Numerics.Vector2(Math.Max(640.0F, CSng(client.X) - origin.X - 15.0F),
                                        Math.Max(320.0F, CSng(client.Y) - origin.Y - 15.0F)))

        If ImGui.Begin("Light Bulb Placer###BulbPlacer", SHOW_LAMP_VIEW) Then
            Dim avail = ImGui.GetContentRegionAvail()
            Const BAR As Single = 6.0F
            Const MIN_PANE As Single = 120.0F
            split_left = Math.Max(MIN_PANE, Math.Min(split_left, avail.X - 2 * BAR - 2 * MIN_PANE))
            split_right = Math.Max(MIN_PANE, Math.Min(split_right, avail.X - 2 * BAR - split_left - MIN_PANE))

            ' ---- LEFT: the list --------------------------------------------
            ImGui.BeginChild("##bulb_left", New System.Numerics.Vector2(split_left, 0), True)
            draw_list()
            ImGui.EndChild()

            ImGui.SameLine(0.0F, 0.0F)
            splitter("##bulb_split_l", BAR, split_left, 1.0F)
            ImGui.SameLine(0.0F, 0.0F)

            ' ---- CENTRE: the view --------------------------------------------
            Dim centre_w = avail.X - split_left - split_right - 2 * BAR
            ImGui.BeginChild("##bulb_view", New System.Numerics.Vector2(centre_w, 0), True)
            draw_view()
            ImGui.EndChild()

            ImGui.SameLine(0.0F, 0.0F)
            splitter("##bulb_split_r", BAR, split_right, -1.0F)
            ImGui.SameLine(0.0F, 0.0F)

            ' ---- RIGHT: settings and tools ------------------------------------
            ImGui.BeginChild("##bulb_right", New System.Numerics.Vector2(0, 0), True)
            draw_tools()
            ImGui.EndChild()
        End If
        ImGui.End()
    End Sub

    ''' <summary>A thin button that drags a pane width. ImGui has no splitter;
    ''' this is the idiom.</summary>
    Private Sub splitter(id As String, bar As Single, ByRef width As Single, sign As Single)
        ImGui.Button(id, New System.Numerics.Vector2(bar, -1.0F))
        If ImGui.IsItemActive() Then width += ImGui.GetIO().MouseDelta.X * sign
        If ImGui.IsItemHovered() OrElse ImGui.IsItemActive() Then ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW)
    End Sub

    Private Sub draw_list()
        If map_scene Is Nothing OrElse Not MAP_LOADED Then
            ImGui.TextWrapped("Load a map first.")
            list_map = ""
            Return
        End If
        ' Once per map, and only once the map is LOADED - the name changes at
        ' the start of a load while the batches are still the old map's.
        If list_map <> MAP_NAME_NO_PATH Then
            list_map = MAP_NAME_NO_PATH
            rebuild_list()
            clear_model()
            edits.Clear() : cur = -1
            ' The first model straight away, so the view is never empty and a
            ' loader problem shows in the log without anyone clicking.
            If entries.Count > 0 Then select_entry(0)
        End If
        If entries.Count = 0 Then
            ImGui.TextWrapped("No lamp or fire models on this map.")
            Return
        End If
        ImGui.Text(String.Format("{0} light model(s)", entries.Count))
        Dim n_bulbs = If(map_scene.cam_path.bulbs Is Nothing, 0, map_scene.cam_path.bulbs.Length)
        ImGui.TextDisabled(String.Format("{0} bulb(s) in the campath", n_bulbs))
        ImGui.Separator()

        ' A list, with the kind as a tag and the instance count: the job is
        ' working down the lamps one after another.
        Dim rgn = ImGui.GetContentRegionAvail()
        ImGui.BeginChild("##bulb_names", New System.Numerics.Vector2(0, rgn.Y), False)
        For i = 0 To entries.Count - 1
            Dim e = entries(i)
            Dim has = False
            If map_scene.cam_path.bulbs IsNot Nothing Then
                For Each b In map_scene.cam_path.bulbs
                    If String.Equals(b.primitives, e.primitives, StringComparison.OrdinalIgnoreCase) Then has = True : Exit For
                Next
            End If
            ' Single-argument Selectable only - the overloads are ambiguous
            ' from VB. The selection shows as a marker in the text instead.
            Dim mark = If(i = sel, "> ", If(has, "* ", "  "))
            Dim label = String.Format("{0}[{1}] {2}  x{3}", mark, e.kind, e.label, e.count)
            If ImGui.Selectable(label) Then
                If i <> sel OrElse Not has_model Then select_entry(i)
            End If
            If ImGui.IsItemHovered() Then ImGui.SetTooltip(e.primitives)
        Next
        ImGui.EndChild()
    End Sub

    Private Sub draw_view()
        ' Snap buttons, then the surface fills what is left.
        If ImGui.SmallButton("3D") Then ortho = 0
        ImGui.SameLine()
        If ImGui.SmallButton("Top") Then ortho = 1
        ImGui.SameLine()
        If ImGui.SmallButton("Front") Then ortho = 2
        ImGui.SameLine()
        If ImGui.SmallButton("Side") Then ortho = 3
        ImGui.SameLine()
        If ortho = 0 Then
            ImGui.TextDisabled("3D | LMB orbit  MMB/wheel zoom  RMB move  Shift+RMB up/down")
        Else
            ImGui.TextDisabled({"", "top", "front", "side"}(ortho) & " | LMB move in this plane  MMB/wheel zoom  RMB pan")
        End If

        Dim rgn = ImGui.GetContentRegionAvail()
        Dim rw = CInt(Math.Max(8.0F, rgn.X))
        Dim rh = CInt(Math.Max(8.0F, rgn.Y))
        ' Recorded, not rendered: the GL work runs before the UI pass.
        pane_w = rw
        pane_h = rh
        If color_tex Is Nothing Then Return

        ' An INVISIBLE BUTTON under the picture, not ImGui.Image. An Image is
        ' not an item that takes the mouse, so a drag over it was a drag on the
        ' window: the panel moved with the cursor. The button claims all three
        ' buttons and the picture is painted into its rectangle.
        Dim p0 = ImGui.GetCursorScreenPos()
        Dim sz = New System.Numerics.Vector2(rw, rh)
        ImGui.InvisibleButton("##bulbview", sz,
                              ImGuiButtonFlags.MouseButtonLeft Or ImGuiButtonFlags.MouseButtonRight Or
                              ImGuiButtonFlags.MouseButtonMiddle)
        ImGui.GetWindowDrawList().AddImage(New IntPtr(color_tex.texture_id), p0, p0 + sz,
                                           New System.Numerics.Vector2(0, 1), New System.Numerics.Vector2(1, 0))
        Dim hovered = ImGui.IsItemHovered()
        Dim io = ImGui.GetIO()

        ' Drags start over the image and keep going until the button is up,
        ' even when the pointer leaves it - a drag that stops at the edge is
        ' the most irritating thing a viewport can do.
        If hovered AndAlso ImGui.IsMouseClicked(ImGuiMouseButton.Left) Then ldrag = True
        If hovered AndAlso ImGui.IsMouseClicked(ImGuiMouseButton.Middle) Then mdrag = True
        If hovered AndAlso ImGui.IsMouseClicked(ImGuiMouseButton.Right) Then rdrag = True
        If Not ImGui.IsMouseDown(ImGuiMouseButton.Left) Then ldrag = False
        If Not ImGui.IsMouseDown(ImGuiMouseButton.Middle) Then mdrag = False
        If Not ImGui.IsMouseDown(ImGuiMouseButton.Right) Then rdrag = False

        Dim d = io.MouseDelta
        Dim moved = d.X <> 0 OrElse d.Y <> 0

        ' Zoom: the wheel, or a middle-button drag, in every view.
        Dim zoom_f As Single = 1.0F
        If hovered AndAlso io.MouseWheel <> 0 Then zoom_f *= CSng(Math.Exp(-io.MouseWheel * 0.15))
        If mdrag AndAlso moved Then zoom_f *= CSng(Math.Exp(d.Y * 0.01))
        If zoom_f <> 1.0F Then
            dist = Math.Clamp(dist * zoom_f, 0.2F, 500.0F)
            ortho_half = Math.Clamp(ortho_half * zoom_f, 0.1F, 250.0F)
        End If

        If ortho = 0 Then
            ' 3D: left orbits, right moves the marker on the ground plane, or
            ' up and down with Shift.
            If ldrag AndAlso moved Then
                yaw -= d.X * 0.008F
                pitch = Math.Clamp(pitch + d.Y * 0.008F, -1.5F, 1.5F)
            End If
            If rdrag AndAlso moved AndAlso has_model AndAlso cur >= 0 Then move_marker(d.X, d.Y, io.KeyShift)
        Else
            ' A plane view: left DRAGS THE MARKER in that plane - it never
            ' drops you back to 3D, the 3D button does that. Right pans.
            If ldrag AndAlso moved AndAlso has_model AndAlso cur >= 0 Then move_marker(d.X, d.Y, False)
            If rdrag AndAlso moved Then pan_view(d.X, d.Y)
        End If
    End Sub

    ''' <summary>Slide an ortho view along its own axes by a mouse delta.</summary>
    Private Sub pan_view(dx As Single, dy As Single)
        Dim eye, up As Vector3
        camera(eye, up)
        Dim fwd = Vector3.Normalize(target - eye)
        Dim right = Vector3.Normalize(Vector3.Cross(fwd, up))
        Dim cam_up = Vector3.Cross(right, fwd)
        Dim m_per_px = 2.0F * ortho_half / Math.Max(1, tex_h)
        target -= right * (dx * m_per_px) - cam_up * (dy * m_per_px)
    End Sub

    ''' <summary>
    ''' Move the bulb (or its aim) by a mouse delta. Pixels become metres at
    ''' the target's distance, so a drag follows the pointer. Perspective:
    ''' along the camera's right and its forward projected onto the ground, or
    ''' straight up with Shift. Ortho: along the view's own right and up, which
    ''' is exact.
    ''' </summary>
    Private Sub move_marker(dx As Single, dy As Single, up_down As Boolean)
        Dim eye, up As Vector3
        camera(eye, up)
        Dim fwd = Vector3.Normalize(target - eye)
        Dim right = Vector3.Normalize(Vector3.Cross(fwd, up))
        Dim cam_up = Vector3.Cross(right, fwd)
        Dim m_per_px As Single
        If ortho = 0 Then
            m_per_px = 2.0F * dist * CSng(Math.Tan(FOV * 0.5)) / Math.Max(1, tex_h)
        Else
            m_per_px = 2.0F * ortho_half / Math.Max(1, tex_h)
        End If

        Dim delta As Vector3
        If ortho <> 0 Then
            delta = right * (dx * m_per_px) - cam_up * (dy * m_per_px)
        ElseIf up_down Then
            delta = New Vector3(0.0F, -dy * m_per_px, 0.0F)
        Else
            Dim r_flat = New Vector3(right.X, 0.0F, right.Z)
            Dim f_flat = New Vector3(fwd.X, 0.0F, fwd.Z)
            If r_flat.LengthSquared < 1.0E-6F Then r_flat = Vector3.UnitX Else r_flat.Normalize()
            If f_flat.LengthSquared < 1.0E-6F Then f_flat = Vector3.UnitZ Else f_flat.Normalize()
            delta = r_flat * (dx * m_per_px) - f_flat * (dy * m_per_px)
        End If

        Dim b = edits(cur)
        If move_aim AndAlso b.kind <> MapCamPath.BULB_POINT Then
            b.aim += delta
        Else
            b.pos += delta
            b.aim += delta
        End If
        edits(cur) = b
        dirty = True
    End Sub

    Private Sub draw_tools()
        If sel < 0 OrElse Not has_model Then
            ImGui.TextWrapped("Pick a model on the left.")
            If status <> "" Then ImGui.TextWrapped(status)
            Return
        End If
        Dim e = entries(sel)
        ImGui.TextWrapped(e.label)
        ImGui.TextDisabled(String.Format("{0}, x{1} on this map", e.kind, e.count))
        ImGui.TextDisabled(String.Format("box {0:0.00} x {1:0.00} x {2:0.00} m, origin at y {3:0.00}",
                                         bmax.X - bmin.X, bmax.Y - bmin.Y, bmax.Z - bmin.Z, -bmin.Y))
        ImGui.Separator()

        ' ---- the lights on this model --------------------------------------
        ImGui.TextDisabled("lights on this model")
        For i = 0 To edits.Count - 1
            Dim b = edits(i)
            Dim lbl = String.Format("{0}{1}: {2} at ({3:0.00}, {4:0.00}, {5:0.00})", If(i = cur, "> ", "  "), i + 1,
                                    KIND_NAMES(Math.Clamp(b.kind, 0, 2)), b.pos.X, b.pos.Y, b.pos.Z)
            If ImGui.Selectable(lbl) Then cur = i
        Next
        If ImGui.Button("Add light") Then
            edits.Add(new_bulb()) : cur = edits.Count - 1 : dirty = True
        End If
        ImGui.SameLine()
        If cur >= 0 AndAlso ImGui.Button("Duplicate") Then
            edits.Add(edits(cur)) : cur = edits.Count - 1 : dirty = True
        End If
        ImGui.SameLine()
        If cur >= 0 AndAlso ImGui.Button("Remove") Then
            edits.RemoveAt(cur) : cur = Math.Min(cur, edits.Count - 1) : dirty = True
        End If

        If cur >= 0 Then
            Dim b = edits(cur)
            Dim changed = False
            ImGui.Separator()

            Dim k = b.kind
            ImGui.PushItemWidth(-1)
            If ImGui.Combo("##kind", k, KIND_NAMES, KIND_NAMES.Length) Then b.kind = k : changed = True
            ImGui.PopItemWidth()
            If ImGui.IsItemHovered() Then
                ImGui.SetTooltip("point:        every direction" & vbLf &
                                 "cone:         only inside the cone, toward the aim" & vbLf &
                                 "inverse cone: every direction EXCEPT inside the cone -" & vbLf &
                                 "              a cowled lamp, dark into its own hood")
            End If

            ImGui.TextDisabled("move with the right mouse:")
            Dim mv = If(move_aim, 1, 0)
            If ImGui.RadioButton("bulb", mv, 0) Then move_aim = False
            ImGui.SameLine()
            If b.kind <> MapCamPath.BULB_POINT Then
                If ImGui.RadioButton("aim point", mv, 1) Then move_aim = True
            Else
                move_aim = False
            End If

            ImGui.PushItemWidth(-1)
            Dim p = New System.Numerics.Vector3(b.pos.X, b.pos.Y, b.pos.Z)
            ImGui.TextDisabled("position (model space, m)")
            If ImGui.InputFloat3("##pos", p, "%.3f") Then
                Dim np = New Vector3(p.X, p.Y, p.Z)
                b.aim += np - b.pos : b.pos = np : changed = True
            End If
            If b.kind <> MapCamPath.BULB_POINT Then
                Dim a = New System.Numerics.Vector3(b.aim.X, b.aim.Y, b.aim.Z)
                ImGui.TextDisabled("aim point")
                If ImGui.InputFloat3("##aim", a, "%.3f") Then b.aim = New Vector3(a.X, a.Y, a.Z) : changed = True
                Dim cn = b.cone
                If ImGui.SliderFloat("##cone", cn, 2.0F, 178.0F, "cone %.0f deg") Then b.cone = cn : changed = True
                Dim bl = b.blend
                If ImGui.SliderFloat("##blend", bl, 0.0F, 1.0F, "edge blend %.2f") Then b.blend = bl : changed = True
            End If

            Dim col = New System.Numerics.Vector3(b.color.X, b.color.Y, b.color.Z)
            If ImGui.ColorEdit3("##colour", col) Then b.color = New Vector3(col.X, col.Y, col.Z) : changed = True
            Dim lv = b.level
            If ImGui.SliderFloat("##level", lv, 0.0F, 1.0F, "level %.2f") Then b.level = lv : changed = True
            Dim rg = b.range_m
            If ImGui.SliderFloat("##range", rg, 0.1F, 50.0F, "range %.1f m") Then b.range_m = rg : changed = True
            Dim vm = b.vol_mix
            If ImGui.SliderFloat("##volmix", vm, 0.0F, 1.0F, "fog mix %.2f") Then b.vol_mix = vm : changed = True
            Dim cv = b.curve
            If ImGui.SliderInt("##curve", cv, 0, 2, "shaft curve %d") Then b.curve = cv : changed = True
            ImGui.PopItemWidth()

            If changed Then edits(cur) = b : dirty = True
        End If

        ImGui.Separator()
        If ImGui.Button(If(dirty, "Save to campath *", "Save to campath"), New System.Numerics.Vector2(-1, 0)) Then save()
        If ImGui.IsItemHovered() Then
            ImGui.SetTooltip("Writes this model's lights into the bulb table of" & vbLf &
                             MAP_NAME_NO_PATH & ".campath. Other models' bulbs, the route" & vbLf &
                             "and Path Studio's lights are copied through untouched.")
        End If
        If ImGui.Button("Reload from campath", New System.Numerics.Vector2(-1, 0)) Then pull_edits() : status = "reloaded"
        If status <> "" Then ImGui.TextWrapped(status)
    End Sub

    ' =====================================================================
    '  The GL side
    ' =====================================================================

    ''' <summary>Where the eye is and which way is up, for the current mode.</summary>
    Private Sub camera(ByRef eye As Vector3, ByRef up As Vector3)
        Select Case ortho
            Case 1 : eye = target + New Vector3(0.0F, dist, 0.0F) : up = Vector3.UnitZ
            Case 2 : eye = target + New Vector3(0.0F, 0.0F, -dist) : up = Vector3.UnitY
            Case 3 : eye = target + New Vector3(dist, 0.0F, 0.0F) : up = Vector3.UnitY
            Case Else
                Dim cp = CSng(Math.Cos(pitch))
                eye = target + New Vector3(CSng(Math.Sin(yaw)) * cp, CSng(Math.Sin(pitch)), CSng(Math.Cos(yaw)) * cp) * dist
                up = Vector3.UnitY
        End Select
    End Sub

    Private Function view_proj(ByRef eye As Vector3) As Matrix4
        Dim up As Vector3
        camera(eye, up)
        Dim view = Matrix4.LookAt(eye, target, up)
        Dim aspect = CSng(Math.Max(1, tex_w)) / Math.Max(1, tex_h)
        Dim proj As Matrix4
        If ortho = 0 Then
            proj = Matrix4.CreatePerspectiveFieldOfView(FOV, aspect, 0.05F, Math.Max(50.0F, dist * 8.0F))
        Else
            proj = Matrix4.CreateOrthographic(ortho_half * 2.0F * aspect, ortho_half * 2.0F, 0.01F, dist * 8.0F)
        End If
        Return view * proj
    End Function

    ''' <summary>
    ''' Draw the view into its texture at the recorded pane size. Called from
    ''' OnRenderFrame BEFORE the UI pass, never from inside the panel - binding
    ''' a framebuffer mid-UI is what took the whole window down once. Saves and
    ''' restores everything it touches, ClearColor included.
    ''' </summary>
    Public Sub RenderView()
        If Not SHOW_LAMP_VIEW OrElse pane_w < 8 OrElse pane_h < 8 Then Return
        create_target(pane_w, pane_h)
        If fbo Is Nothing Then Return

        GL_PUSH_GROUP("BulbPlacer::RenderView")
        Dim prev_fbo = GL.GetInteger(GetPName.FramebufferBinding)
        Dim prev_vp(3) As Integer
        GL.GetInteger(GetPName.Viewport, prev_vp)
        Dim was_blend = GL.IsEnabled(EnableCap.Blend)
        Dim was_depth = GL.IsEnabled(EnableCap.DepthTest)
        Dim was_cull = GL.IsEnabled(EnableCap.CullFace)
        Dim prev_clear(3) As Single
        GL.GetFloat(GetPName.ColorClearValue, prev_clear)

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.DepthMask(True)
        GL.ClearColor(0.10F, 0.11F, 0.14F, 1.0F)
        GL.ClearDepth(1.0)
        GL.Viewport(0, 0, tex_w, tex_h)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        If has_model Then
            Dim eye As Vector3
            Dim vp = view_proj(eye)

            lampViewShader.Use()
            GL.UniformMatrix4(lampViewShader("mvp"), False, vp)
            GL.Uniform3(lampViewShader("view_pos"), eye.X, eye.Y, eye.Z)
            GL.Uniform3(lampViewShader("base_color"), 0.72F, 0.72F, 0.75F)
            Dim ld = Vector3.Normalize(eye - target + New Vector3(0.3F, 0.9F, 0.2F))
            GL.Uniform3(lampViewShader("light_dir"), ld.X, ld.Y, ld.Z)
            For Each m In meshes
                m.vao.Bind()
                GL.DrawElements(PrimitiveType.Triangles, m.index_count, DrawElementsType.UnsignedInt, IntPtr.Zero)
            Next
            lampViewShader.StopUse()

            draw_overlays(vp)
        End If

        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)
        If was_blend Then GL.Enable(EnableCap.Blend) Else GL.Disable(EnableCap.Blend)
        If was_depth Then GL.Enable(EnableCap.DepthTest) Else GL.Disable(EnableCap.DepthTest)
        If was_cull Then GL.Enable(EnableCap.CullFace) Else GL.Disable(EnableCap.CullFace)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prev_fbo)
        GL.Viewport(prev_vp(0), prev_vp(1), prev_vp(2), prev_vp(3))
        GL.ClearColor(prev_clear(0), prev_clear(1), prev_clear(2), prev_clear(3))
        GL_POP_GROUP()
    End Sub

    ''' <summary>Grid, crosshair, range sphere and aim cone, as lines.</summary>
    Private Sub draw_overlays(vp As Matrix4)
        lampCursorShader.Use()
        GL.UniformMatrix4(lampCursorShader("mvp"), False, vp)

        ' Ground grid on the box floor, 1 m, depth tested so the model sits on it.
        Dim g As New List(Of Single)
        Dim ext = CSng(Math.Ceiling(Math.Max(3.0F, Math.Max(bmax.X - bmin.X, bmax.Z - bmin.Z))))
        Dim cx = CSng(Math.Round((bmin.X + bmax.X) * 0.5F)), cz = CSng(Math.Round((bmin.Z + bmax.Z) * 0.5F))
        For i = -CInt(ext) To CInt(ext)
            g.AddRange({cx + i, bmin.Y, cz - ext, cx + i, bmin.Y, cz + ext})
            g.AddRange({cx - ext, bmin.Y, cz + i, cx + ext, bmin.Y, cz + i})
        Next
        draw_lines(g, 0.30F, 0.32F, 0.38F, 1.0F)
        ' Axes at the model origin.
        draw_lines(New List(Of Single)({0, 0, 0, 1, 0, 0}), 0.9F, 0.3F, 0.3F, 1.0F)
        draw_lines(New List(Of Single)({0, 0, 0, 0, 1, 0}), 0.3F, 0.9F, 0.3F, 1.0F)
        draw_lines(New List(Of Single)({0, 0, 0, 0, 0, 1}), 0.3F, 0.5F, 1.0F, 1.0F)

        ' The lights, depth tested like the model, so a bulb inside a hood reads
        ' as inside it and one behind the post is behind it.
        For i = 0 To edits.Count - 1
            Dim b = edits(i)
            Dim a = If(i = cur, 1.0F, 0.45F)
            Dim r = Math.Max(0.05F, b.range_m)

            ' Crosshair
            Dim cl = Math.Max(0.15F, r * 0.08F)
            draw_lines(New List(Of Single)({
                b.pos.X - cl, b.pos.Y, b.pos.Z, b.pos.X + cl, b.pos.Y, b.pos.Z,
                b.pos.X, b.pos.Y - cl, b.pos.Z, b.pos.X, b.pos.Y + cl, b.pos.Z,
                b.pos.X, b.pos.Y, b.pos.Z - cl, b.pos.X, b.pos.Y, b.pos.Z + cl}), 1.0F, 0.85F, 0.2F, a)

            ' Range sphere: three great circles in the light's colour.
            Dim s As New List(Of Single)
            circle(s, b.pos, r, 0) : circle(s, b.pos, r, 1) : circle(s, b.pos, r, 2)
            draw_lines(s, b.color.X, b.color.Y, b.color.Z, 0.35F * a)

            If b.kind <> MapCamPath.BULB_POINT Then
                ' Cone: the axis to the aim, and a ring of rays at the half
                ' angle, out to the range. Inverse cone in a cooler colour -
                ' it is the DARK part.
                Dim axis = b.aim - b.pos
                If axis.LengthSquared < 1.0E-6F Then axis = -Vector3.UnitY
                axis.Normalize()
                Dim c As New List(Of Single)
                c.AddRange({b.pos.X, b.pos.Y, b.pos.Z, b.aim.X, b.aim.Y, b.aim.Z})
                Dim half = CSng(Math.Clamp(b.cone, 1.0F, 179.0F) * 0.5 * Math.PI / 180.0)
                Dim side = Vector3.Cross(axis, If(Math.Abs(axis.Y) < 0.9F, Vector3.UnitY, Vector3.UnitX))
                side.Normalize()
                Dim side2 = Vector3.Cross(axis, side)
                Const N As Integer = 24
                Dim rim(N - 1) As Vector3
                For k = 0 To N - 1
                    Dim t = k * 2.0 * Math.PI / N
                    Dim dir = axis * CSng(Math.Cos(half)) + (side * CSng(Math.Cos(t)) + side2 * CSng(Math.Sin(t))) * CSng(Math.Sin(half))
                    rim(k) = b.pos + dir * r
                Next
                For k = 0 To N - 1
                    Dim q = rim((k + 1) Mod N)
                    c.AddRange({rim(k).X, rim(k).Y, rim(k).Z, q.X, q.Y, q.Z})
                    If k Mod 3 = 0 Then c.AddRange({b.pos.X, b.pos.Y, b.pos.Z, rim(k).X, rim(k).Y, rim(k).Z})
                Next
                If b.kind = MapCamPath.BULB_INVERSE_CONE Then
                    draw_lines(c, 0.35F, 0.55F, 1.0F, a)
                Else
                    draw_lines(c, 1.0F, 0.95F, 0.6F, a)
                End If
                ' The aim point itself.
                Dim al = cl * 0.6F
                draw_lines(New List(Of Single)({
                    b.aim.X - al, b.aim.Y, b.aim.Z, b.aim.X + al, b.aim.Y, b.aim.Z,
                    b.aim.X, b.aim.Y, b.aim.Z - al, b.aim.X, b.aim.Y, b.aim.Z + al}), 1.0F, 1.0F, 1.0F, a)
            End If
        Next
        lampCursorShader.StopUse()
    End Sub

    Private Shared Sub circle(s As List(Of Single), c As Vector3, r As Single, plane As Integer)
        Const N As Integer = 48
        For k = 0 To N - 1
            Dim t0 = k * 2.0 * Math.PI / N, t1 = (k + 1) * 2.0 * Math.PI / N
            Dim p0, p1 As Vector3
            Select Case plane
                Case 0 : p0 = c + New Vector3(CSng(Math.Cos(t0)), 0, CSng(Math.Sin(t0))) * r : p1 = c + New Vector3(CSng(Math.Cos(t1)), 0, CSng(Math.Sin(t1))) * r
                Case 1 : p0 = c + New Vector3(CSng(Math.Cos(t0)), CSng(Math.Sin(t0)), 0) * r : p1 = c + New Vector3(CSng(Math.Cos(t1)), CSng(Math.Sin(t1)), 0) * r
                Case Else : p0 = c + New Vector3(0, CSng(Math.Cos(t0)), CSng(Math.Sin(t0))) * r : p1 = c + New Vector3(0, CSng(Math.Cos(t1)), CSng(Math.Sin(t1))) * r
            End Select
            s.AddRange({p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z})
        Next
    End Sub

    ''' <summary>Upload and draw one batch of line segments through a growing
    ''' dynamic buffer. The cursor shader must already be in use.</summary>
    Private Sub draw_lines(v As List(Of Single), r As Single, g As Single, b As Single, a As Single)
        If v.Count < 6 Then Return
        Dim arr = v.ToArray()
        If line_vbo Is Nothing OrElse arr.Length > line_cap Then
            line_vao?.Dispose() : line_vbo?.Dispose()
            line_cap = Math.Max(arr.Length, Math.Max(4096, line_cap * 2))
            line_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "bulbLinesVbo")
            line_vbo.StorageNullData(line_cap * 4, BufferStorageFlags.DynamicStorageBit)
            line_vao = GLVertexArray.Create("bulbLinesVao")
            line_vao.VertexBuffer(0, line_vbo, IntPtr.Zero, 12)
            line_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
            line_vao.AttribBinding(0, 0)
            line_vao.EnableAttrib(0)
        End If
        GL.NamedBufferSubData(line_vbo.buffer_id, IntPtr.Zero, arr.Length * 4, arr)
        line_vao.Bind()
        GL.Uniform4(lampCursorShader("line_color"), r, g, b, a)
        GL.DrawArrays(PrimitiveType.Lines, 0, arr.Length \ 3)
    End Sub

    Private Sub create_target(w As Integer, h As Integer)
        If color_tex IsNot Nothing AndAlso tex_w = w AndAlso tex_h = h Then Return
        color_tex?.Dispose() : color_tex = Nothing
        depth_rb?.Dispose() : depth_rb = Nothing
        fbo?.Dispose() : fbo = Nothing
        tex_w = w : tex_h = h

        color_tex = GLTexture.Create(TextureTarget.Texture2D, "BulbViewColor")
        color_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_tex.Storage2D(1, SizedInternalFormat.Rgba8, tex_w, tex_h)
        depth_rb = GLRenderbuffer.Create("BulbViewDepth")
        depth_rb.Storage(RenderbufferStorage.DepthComponent24, tex_w, tex_h)
        fbo = GLFramebuffer.Create("BulbViewFBO")
        fbo.Texture(FramebufferAttachment.ColorAttachment0, color_tex, 0)
        fbo.Renderbuffer(FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth_rb)
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.ColorAttachment0)
        If Not fbo.IsComplete Then LogThis("bulb placer: framebuffer incomplete")
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        clear_model()
        color_tex?.Dispose() : color_tex = Nothing
        depth_rb?.Dispose() : depth_rb = Nothing
        fbo?.Dispose() : fbo = Nothing
        line_vao?.Dispose() : line_vao = Nothing
        line_vbo?.Dispose() : line_vbo = Nothing
        GC.SuppressFinalize(Me)
    End Sub
End Class

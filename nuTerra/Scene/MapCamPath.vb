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

    ' "NCP2" little endian. The magic changed with version 2 rather than only
    ' the version field, and deliberately: the header grew 64 -> 128 bytes, so a
    ' version 1 reader would take its points from offset 64 - the middle of the
    ' header - and fly plausible garbage. Refusing to load is the better failure.
    Private Const MAGIC As UInteger = &H3250434EUI
    Private Const MAGIC_V1 As UInteger = &H3150434EUI

    Private Const HEADER_SIZE As Integer = 128
    Private Const POINT_STRIDE As Integer = 32
    Private Const SEED_STRIDE As Integer = 12
    Private Const LIGHT_STRIDE As Integer = 32
    ''' <summary>What SaveBulbs emits: the 224 below plus ang0 and ang1.</summary>
    Private Const BULB_STRIDE As Integer = 232
    ''' <summary>What a reader must ACCEPT - a file written before the two
    ''' angles is 224 bytes a bulb and is not a lesser file. Guarding on
    ''' BULB_STRIDE instead would refuse the whole campath, route and map
    ''' lights included, the moment the record grew. Same rule as
    ''' LIGHT_STRIDE above, which is likewise the minimum, not the size this
    ''' version writes.</summary>
    Private Const BULB_STRIDE_MIN As Integer = 224
    Private Const BULB_NAME_LEN As Integer = 160

    Private Const SEED_START As UInteger = 0UI

    Public Structure CamPoint
        Public pos As Vector3
        Public heading As Single      ' yaw, radians. atan2(dx, dz)
        Public tilt As Single         ' pitch, radians. POSITIVE LOOKS UP
        Public roll As Single         ' bank, radians. POSITIVE BANKS RIGHT
        Public s As Single            ' metres from the first point
        Public speed As Single        ' metres per second
    End Structure

    ''' <summary>
    ''' A point that was CLICKED in Path Studio, as opposed to flown.
    '''
    ''' nuTerra does not need these to fly - the route is the route - but they
    ''' are what the path was planned from, and having them here means the
    ''' overlay can show what a route was asked to do rather than only what it
    ''' ended up doing.
    ''' </summary>
    Public Structure SeedPoint
        Public x As Single
        Public z As Single
        Public is_start As Boolean
    End Structure

    Public seeds() As SeedPoint

    ''' <summary>
    ''' A light placed in Path Studio, read from the tail of the .campath.
    '''
    ''' Every field here reaches the deferred shader - position and range as
    ''' pl_pos_range, colour and level as pl_color_level - via
    ''' modRender.upload_path_lights. DrawLights below draws the same records
    ''' as overlay spheres, so what is authored, what is drawn and what lights
    ''' the ground are all one set of numbers.
    ''' </summary>
    Public Structure CamLight
        ''' <summary>World position. Y is metres ABOVE THE TERRAIN, not
        ''' absolute - Path Studio places lights on a 2D map and writes 0, so
        ''' the ground height has to be resolved here. This is the one field
        ''' that does not follow the point record's convention.</summary>
        Public pos As Vector3
        ''' <summary>Colour 0..1, sRGB as authored in the picker - NOT linear.
        ''' Linearise before lighting with it.</summary>
        Public color As Vector3
        ''' <summary>Authored 0..1. A FRACTION, not an amount of light - it
        ''' scales PATH_LIGHT_GAIN, which carries the quantity. Multiplies the
        ''' radiance linearly, so half the level really is half the light
        ''' reaching a surface.</summary>
        Public level As Single
        ''' <summary>Radius of influence in metres, 0.1 .. 50.</summary>
        Public range_m As Single
        ''' <summary>Which fog falloff curve the SHAFT uses: 0, 1 or 2, a row
        ''' of VM_FOG_Curve_&lt;n&gt;.png beside the .campath. Files written
        ''' before the field are 32 bytes a light and read as 0.</summary>
        Public curve As Integer

        ' The fields below exist for BULB lights - lights a model carries, one
        ' per instance (ExpandBulbs). A map light from the file has kind 0,
        ' vol_mix 1 and absolute False.
        ''' <summary>0 point, 1 cone, 2 inverse cone, 3 dual cowled.</summary>
        Public kind As Integer
        ''' <summary>World unit direction a cone looks along.</summary>
        Public dir As Vector3
        ''' <summary>Full cone angle, degrees.</summary>
        Public cone As Single
        ''' <summary>0..1, soft edge fraction of the cone.</summary>
        Public blend As Single
        ''' <summary>The two HALF angles from the axis, degrees. Read per kind -
        ''' see CamBulb.ang0 below, which is where these come from. Both 0
        ''' means "fall back to blend".</summary>
        Public ang0 As Single
        Public ang1 As Single
        ''' <summary>Scales what this light scatters into fog.</summary>
        Public vol_mix As Single
        ''' <summary>True when pos.Y is absolute world height, not metres
        ''' above the terrain.</summary>
        Public absolute As Boolean
    End Structure

    ''' <summary>Every light: the file's map lights first, then one per bulb
    ''' instance. Rebuilt by ExpandBulbs.</summary>
    Public lights() As CamLight
    ''' <summary>The file's own map lights, as read. The shadow cubes are
    ''' baked for these.</summary>
    Public path_lights() As CamLight = {}

    ''' <summary>
    ''' A BULB: a light attached to a MODEL, placed once in the model's own
    ''' space by the Light Bulb Placer. At load nuTerra puts one at every
    ''' instance of that model on the map. The CamLights above are placed on
    ''' the map by Path Studio; these are placed on a model. Both feed the same
    ''' lamp path. Record layout: tools/cam_path.py, "Bulb record".
    ''' </summary>
    Public Structure CamBulb
        ''' <summary>The model's .primitives path - the key. The only identity
        ''' a model has that survives across maps.</summary>
        Public primitives As String
        ''' <summary>0 point, 1 cone, 2 inverse cone (omni EXCEPT inside the
        ''' cone: a cowled street lamp lights everything but its own hood),
        ''' 3 dual cowled - see ang0.</summary>
        Public kind As Integer
        ''' <summary>Model space, metres from the model origin.</summary>
        Public pos As Vector3
        ''' <summary>Model space point a cone looks at. Ignored for a point.</summary>
        Public aim As Vector3
        ''' <summary>Full cone angle, degrees.</summary>
        Public cone As Single
        ''' <summary>0..1, how much of the cone is soft edge.</summary>
        Public blend As Single
        ''' <summary>sRGB 0..1 as the picker holds it - NOT linear.</summary>
        Public color As Vector3
        ''' <summary>0..1, fraction of the global light gain.</summary>
        Public level As Single
        Public range_m As Single
        ''' <summary>0..1, how much this light scatters into fog.</summary>
        Public vol_mix As Single
        ''' <summary>Shaft falloff curve, 0..2.</summary>
        Public curve As Integer
        ''' <summary>
        ''' The two HALF angles from the axis, in degrees. One pair of fields
        ''' serves all three aimed kinds, read differently by each:
        '''
        '''   kind            lit where              ang0            ang1
        '''   0 point         everywhere             -               -
        '''   1 cone          inside ang1            inner hot edge  outer edge
        '''   2 inverse cone  outside ang0           dark edge       soft-out edge
        '''   3 dual cowled   BETWEEN ang0 and ang1  the cap cut     the base cut
        '''
        ''' The DUAL COWLED lamp is two inverse lobes on ONE shared axis, so
        ''' what it lights is a toroidal band - a lamp on a vertical post,
        ''' where the cap swallows the light going up and the post blocks it
        ''' going down. Band width is ang1 - ang0 and blend softens both edges.
        '''
        ''' Both 0 means "fall back to blend", which is what a file written
        ''' before these fields reads back as, so nothing already authored
        ''' changes appearance.
        ''' </summary>
        Public ang0 As Single
        Public ang1 As Single
    End Structure

    Public Const BULB_POINT As Integer = 0
    Public Const BULB_CONE As Integer = 1
    Public Const BULB_INVERSE_CONE As Integer = 2
    ''' <summary>Two inverse lobes on one axis: a lit band. See CamBulb.ang0.</summary>
    Public Const BULB_DUAL_COWL As Integer = 3
    ''' <summary>The highest kind this build understands, for the clamps that
    ''' keep a hand-edited or future file from indexing off the end.</summary>
    Public Const BULB_KIND_MAX As Integer = 3

    ''' <summary>
    ''' The two cosines every cone mask needs: cos of the INNER half angle and
    ''' cos of the OUTER one. cos_in is the LARGER of the two, because cosine
    ''' falls as the angle grows.
    '''
    ''' One function, called from both upload paths, because deferred.frag and
    ''' lamp_fog.frag run the SAME mask - one on a surface, one in the air -
    ''' and a shaft has the shape of the light that casts it. Two copies of
    ''' this arithmetic would eventually disagree and the beam would stop
    ''' matching its own pool of light.
    '''
    ''' With ang0 / ang1 unset - every bulb authored before those fields, and
    ''' every Path Studio map light - a cone falls back to the shipped
    ''' cone-plus-blend pair and renders exactly as it did.
    ''' </summary>
    Public Shared Sub cone_cosines(kind As Integer, cone As Single, blend As Single,
                                   ang0 As Single, ang1 As Single,
                                   ByRef cos_in As Single, ByRef cos_out As Single)
        Dim bl = Math.Clamp(blend, 0.0F, 1.0F)
        Dim have_pair = ang1 > ang0 AndAlso ang1 > 0.0F

        If kind = BULB_DUAL_COWL Then
            ' Two lobes on one axis; the lit part is the band between them. A
            ' bulb switched to this kind before its angles were set gets a wide
            ' band rather than a black lamp.
            Dim a0 = If(have_pair, ang0, 20.0F)
            Dim a1 = If(have_pair, ang1, 160.0F)
            cos_in = CSng(Math.Cos(Math.Clamp(a0, 0.0F, 179.0F) * Math.PI / 180.0))
            cos_out = CSng(Math.Cos(Math.Clamp(a1, 1.0F, 180.0F) * Math.PI / 180.0))
            Return
        End If

        ' Cone and inverse cone. `cone` is the FULL angle, so half of it is the
        ' outer edge when no explicit pair was authored.
        Dim outer = If(have_pair, ang1, Math.Clamp(cone, 1.0F, 179.0F) * 0.5F)
        cos_out = CSng(Math.Cos(Math.Clamp(outer, 0.5F, 89.5F) * Math.PI / 180.0))
        If ang0 > 0.0F AndAlso ang0 < outer Then
            cos_in = CSng(Math.Cos(Math.Clamp(ang0, 0.0F, 89.0F) * Math.PI / 180.0))
        Else
            ' The shipped soft edge: a fraction of the way from the rim to the
            ' axis, in cosine space.
            cos_in = cos_out + (1.0F - cos_out) * bl
        End If
        ' smoothstep needs the two edges apart; equal ones give a hard rim.
        cos_in = Math.Max(cos_in, cos_out + 0.0001F)
    End Sub

    ''' <summary>Model-attached lights read from the file. Empty, never
    ''' Nothing, once Load has run.</summary>
    Public bulbs() As CamBulb = {}

    ''' <summary>Departure heading the route was planned with, radians.</summary>
    Public seed_heading As Single
    ''' <summary>When the file was written. Unix seconds UTC, 0 if unknown.</summary>
    Public created As Long

    Public points() As CamPoint
    Public loaded As Boolean
    Public closed As Boolean
    Public total_len As Single
    Public map_name As String = ""

    ''' <summary>The file this came from. Two folders can hold one, so which is
    ''' worth knowing when the route is not the one you just saved.</summary>
    Public source_file As String = ""

    ''' <summary>Distance travelled along the path, metres. Advanced by Fly.</summary>
    Public travelled As Single

    Private vao As GLVertexArray
    Private vbo As GLBuffer

    ' One unit sphere, shared by every light. Built on first use and kept for
    ' the life of the map - it does not depend on the path, so reloading a
    ' route must not throw it away.
    Private sphere_vao As GLVertexArray
    Private sphere_vbo As GLBuffer
    Private sphere_verts As Integer
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

    ''' <summary>
    ''' Ribbon width in PIXELS. Not glLineWidth - a core profile only has to
    ''' support width 1, so that call is commonly clamped to a hairline with no
    ''' error reported, and a hairline's brightness and apparent thickness then
    ''' change with the angle it crosses the pixel grid at. campath.geom widens
    ''' each segment to a quad this many pixels across instead.
    ''' </summary>
    Private Const LINE_PX As Single = 3.0F

    ' Metres of heading tick drawn at every TICK_EVERY points. Long enough to
    ' read the direction off the screen, short enough not to become the picture.
    Private Const TICK_LEN As Single = 6.0F
    Private Const TICK_EVERY As Integer = 8

    ''' <summary>
    ''' Where this map's .campath actually is, or Nothing.
    '''
    ''' TWO places can hold one and they are routinely different. The build
    ''' copies cam_paths beside the exe, which is also where an install puts it -
    ''' but Path Studio writes to the PROJECT folder the build copies FROM. So
    ''' the file saved next door and the file played here were not the same file,
    ''' re-reading found the same stale copy every time, and the only thing that
    ''' appeared to help was restarting after a build had quietly copied one over
    ''' the other.
    '''
    ''' Take whichever is NEWER rather than preferring a location - but only
    ''' among files in the SOURCE tree. Build output is scanned solely as a
    ''' fallback, because Application.StartupPath IS bin\Debug\&lt;tfm&gt; and a
    ''' plain newest-wins walk therefore picks the copy MSBuild just dropped
    ''' beside the exe. That copy is gitignored and is replaced by the next
    ''' PreserveNewest copy, so a bulb table saved into it is invisible to git
    ''' and one Path Studio regenerate away from being gone. A build output
    ''' copy is a COPY; the master lives in the source tree.
    '''
    ''' Installed, there is no source tree - the only copy sits beside the exe
    ''' and it IS the master - which is what the fallback pass is for.
    ''' </summary>
    Private Shared Function resolve_campath(map As String) As String
        Dim best = scan_campaths(map, False)
        If best Is Nothing Then best = scan_campaths(map, True)
        Return best
    End Function

    ''' <summary>Newest &lt;dir&gt;\cam_paths\&lt;map&gt;.campath walking up from the
    ''' exe, either skipping build output or allowing it.</summary>
    Private Shared Function scan_campaths(map As String, allow_output As Boolean) As String
        Dim best As String = Nothing
        Dim best_t = DateTime.MinValue

        Dim dir = New IO.DirectoryInfo(Application.StartupPath)
        While dir IsNot Nothing
            For Each cand In {IO.Path.Combine(dir.FullName, "cam_paths", map & ".campath"),
                              IO.Path.Combine(dir.FullName, "nuTerra", "cam_paths", map & ".campath")}
                If (allow_output OrElse Not is_build_output(cand)) AndAlso IO.File.Exists(cand) Then
                    Dim t = IO.File.GetLastWriteTimeUtc(cand)
                    If t > best_t Then
                        best_t = t
                        best = cand
                    End If
                End If
            Next
            dir = dir.Parent
        End While

        Return best
    End Function

    ''' <summary>True when a path runs through a bin or obj directory.</summary>
    Private Shared Function is_build_output(path As String) As Boolean
        Dim p = "\" & path.Replace("/"c, "\"c).ToLowerInvariant() & "\"
        Return p.Contains("\bin\") OrElse p.Contains("\obj\")
    End Function

    ''' <summary>
    ''' Set by every Load, cleared by whoever re-bakes off the back of it.
    '''
    ''' The lamp shadow cubes are baked from these lights, so a Path Studio save
    ''' picked up through the FLY / Show Path / Show Lights checkboxes moves the
    ''' lamps and leaves the cubes describing where they used to be. A flag
    ''' rather than a call from each of those three: they are UI code, a bake
    ''' binds its own framebuffer and rewrites the global depth state, and the
    ''' fourth caller that forgets is only a matter of time.
    ''' </summary>
    Public lights_dirty As Boolean
    ''' <summary>Bumped on every Load. Consumers that build something from the
    ''' file - the shaft falloff curves - compare against it rather than being
    ''' called from each of the UI paths that re-read the route.</summary>
    Public load_gen As Integer
    ''' <summary>Folder the .campath was read from. VM_FOG_Curve_&lt;n&gt;.png
    ''' live beside it, so a Path Studio save of a curve is picked up by the
    ''' same Reload Cam Path that picks up the lamps.</summary>
    Public curve_dir As String


    ''' <summary>
    ''' Write the bulb block of the loaded .campath and nothing else. The
    ''' points, seeds and lights are copied byte for byte; the two header words
    ''' at 104 and 108 are patched; the bulb records follow. Same surgery as
    ''' Path Studio's copy_with_lights, from the other side: Path Studio owns
    ''' the route and the map lights, nuTerra owns the bulbs, and each rewrites
    ''' only its own block.
    '''
    ''' Returns a one-line status for the panel.
    ''' </summary>
    Public Function SaveBulbs(new_bulbs() As CamBulb) As String
        If source_file = "" OrElse Not File.Exists(source_file) Then
            Return "no .campath loaded to save into"
        End If
        Try
            Dim raw = File.ReadAllBytes(source_file)
            If raw.Length < HEADER_SIZE OrElse BitConverter.ToUInt32(raw, 0) <> MAGIC Then
                Return "not a version 2 .campath"
            End If
            Dim count = CInt(BitConverter.ToUInt32(raw, 8))
            Dim stride = CInt(BitConverter.ToUInt32(raw, 12))
            Dim head_size = CInt(BitConverter.ToUInt32(raw, 60))
            Dim seed_count = CInt(BitConverter.ToUInt32(raw, 72))
            Dim seed_stride = CInt(BitConverter.ToUInt32(raw, 76))
            Dim light_count = CInt(BitConverter.ToUInt32(raw, 96))
            Dim light_stride = CInt(BitConverter.ToUInt32(raw, 100))
            Dim body_end = head_size + count * stride + seed_count * seed_stride _
                           + light_count * light_stride
            If body_end > raw.Length Then Return "header describes more data than the file holds"

            Dim n = If(new_bulbs Is Nothing, 0, new_bulbs.Length)
            Using ms As New MemoryStream()
                Dim head(HEADER_SIZE - 1) As Byte
                Array.Copy(raw, head, HEADER_SIZE)
                Array.Copy(BitConverter.GetBytes(CUInt(n)), 0, head, 104, 4)
                Array.Copy(BitConverter.GetBytes(CUInt(If(n > 0, BULB_STRIDE, 0))), 0, head, 108, 4)
                ms.Write(head, 0, HEADER_SIZE)
                ms.Write(raw, HEADER_SIZE, body_end - HEADER_SIZE)

                Using bw As New BinaryWriter(ms, Text.Encoding.UTF8, True)
                    For i = 0 To n - 1
                        Dim b = new_bulbs(i)
                        Dim name(BULB_NAME_LEN - 1) As Byte
                        Dim enc = Text.Encoding.UTF8.GetBytes(If(b.primitives, ""))
                        Array.Copy(enc, name, Math.Min(enc.Length, BULB_NAME_LEN - 1))
                        bw.Write(name)
                        bw.Write(CUInt(b.kind))
                        bw.Write(b.pos.X) : bw.Write(b.pos.Y) : bw.Write(b.pos.Z)
                        bw.Write(b.aim.X) : bw.Write(b.aim.Y) : bw.Write(b.aim.Z)
                        bw.Write(b.cone)
                        bw.Write(b.blend)
                        bw.Write(b.color.X) : bw.Write(b.color.Y) : bw.Write(b.color.Z)
                        bw.Write(b.level)
                        bw.Write(b.range_m)
                        bw.Write(b.vol_mix)
                        bw.Write(CUInt(b.curve))
                        bw.Write(b.ang0)
                        bw.Write(b.ang1)
                    Next
                    bw.Flush()
                End Using
                File.WriteAllBytes(source_file, ms.ToArray())
            End Using

            bulbs = If(new_bulbs, New CamBulb() {})
            ExpandBulbs()
            lights_dirty = True
            LogThis("cam path: wrote {0} bulb(s) to {1}", n, source_file)
            Return String.Format("saved {0} bulb(s) to {1}", n, IO.Path.GetFileName(source_file))
        Catch ex As Exception
            Return "save failed: " & ex.Message
        End Try
    End Function


    ''' <summary>
    ''' The world position of light i, resolved ONE way for every consumer -
    ''' the surface lighting, the shadow bake, the shafts and the overlay. A
    ''' Path Studio light stores Y as metres above the terrain; a bulb light
    ''' is already absolute, transformed through its instance.
    ''' </summary>
    Public Function world_pos(i As Integer) As Vector3
        Dim l = lights(i)
        If l.absolute Then Return l.pos
        Return New Vector3(l.pos.X, get_Y_at_XZ_fast(l.pos.X, l.pos.Z) + l.pos.Y, l.pos.Z)
    End Function

    ''' <summary>How many of the lights are the file's own map lights. They come
    ''' first in the array, so the shadow cubes - baked for these only - line up
    ''' with the first indices everywhere.</summary>
    Public Function path_light_count() As Integer
        Return If(path_lights Is Nothing, 0, path_lights.Length)
    End Function

    ''' <summary>
    ''' The lights worth uploading this frame, as indices into lights(): every
    ''' map light first, in file order, then the bulb lights nearest the camera
    ''' until the shader's 32 slots are full. A map with 145 street lamps
    ''' cannot light them all at once; the ones near the camera are the ones
    ''' that show.
    ''' </summary>
    Public Function visible_lights(cam As Vector3, max_n As Integer) As Integer()
        If lights Is Nothing OrElse lights.Length = 0 Then Return New Integer() {}
        Dim np = Math.Min(path_light_count(), max_n)
        Dim out As New List(Of Integer)(max_n)
        For i = 0 To np - 1
            out.Add(i)
        Next
        If lights.Length > np AndAlso out.Count < max_n Then
            Dim rest = Enumerable.Range(np, lights.Length - np).ToList()
            rest.Sort(Function(a, b)
                          Dim da = (lights(a).pos - cam).LengthSquared - lights(a).range_m * lights(a).range_m
                          Dim db = (lights(b).pos - cam).LengthSquared - lights(b).range_m * lights(b).range_m
                          Return da.CompareTo(db)
                      End Function)
            For Each r In rest
                If out.Count >= max_n Then Exit For
                out.Add(r)
            Next
        End If
        Return out.ToArray()
    End Function

    ''' <summary>
    ''' lights() = the file's map lights, then one light per INSTANCE of every
    ''' bulb's model on this map. Needs the load's model tables - LIGHT_MODELS
    ''' for the model id behind a primitives path, MODEL_BATCH_LIST and
    ''' MODEL_INDEX_LIST for the instance transforms - which are all there by
    ''' the time the cam path is read, and stay there. Called from Load and
    ''' after SaveBulbs; every consumer sees the new set on the next frame and
    ''' lights_dirty rebakes the cubes.
    ''' </summary>
    Public Sub ExpandBulbs()
        Dim all As New List(Of CamLight)
        If path_lights IsNot Nothing Then all.AddRange(path_lights)

        Dim placed = 0, unmatched = 0
        If bulbs IsNot Nothing AndAlso MODEL_BATCH_LIST IsNot Nothing AndAlso MODEL_INDEX_LIST IsNot Nothing Then
            For Each b In bulbs
                Dim model_id = -1
                For Each e In BulbPlacer.LIGHT_MODELS
                    If String.Equals(e.primitives, b.primitives, StringComparison.OrdinalIgnoreCase) Then
                        model_id = e.model_id
                        Exit For
                    End If
                Next
                If model_id < 0 Then
                    unmatched += 1
                    Continue For
                End If
                For Each batch In MODEL_BATCH_LIST
                    If batch.model_id <> model_id Then Continue For
                    For i = 0 To batch.count - 1
                        Dim idx = batch.offset + i
                        If idx < 0 OrElse idx >= MODEL_INDEX_LIST.Length Then Continue For
                        Dim m = MODEL_INDEX_LIST(idx).matrix
                        Dim wp = Vector3.TransformPosition(b.pos, m)
                        Dim wa = Vector3.TransformPosition(b.aim, m)
                        Dim dir = wa - wp
                        If dir.LengthSquared < 1.0E-8F Then dir = -Vector3.UnitY Else dir.Normalize()
                        all.Add(New CamLight With {
                            .pos = wp, .absolute = True,
                            .color = b.color, .level = b.level, .range_m = b.range_m,
                            .curve = b.curve, .kind = b.kind, .dir = dir,
                            .cone = b.cone, .blend = b.blend, .vol_mix = b.vol_mix,
                            .ang0 = b.ang0, .ang1 = b.ang1})
                        placed += 1
                    Next
                Next
            Next
        End If
        lights = all.ToArray()
        If bulbs IsNot Nothing AndAlso bulbs.Length > 0 Then
            LogThis("cam path: {0} bulb(s) placed {1} light(s) on {2} instance(s); {3} bulb(s) matched no model on this map",
                    bulbs.Length, placed, placed, unmatched)
        End If
    End Sub

    Public Sub Load(map As String)
        Dispose_gl()
        loaded = False
        ' Set at the TOP so every exit path below is covered, including the ones
        ' that leave no lights at all - "no lamps now" invalidates a bake just
        ' as surely as "lamps somewhere else".
        lights_dirty = True
        load_gen += 1
        points = Nothing
        bulbs = New CamBulb() {}
        path_lights = New CamLight() {}
        travelled = 0.0F

        Dim path = resolve_campath(map)
        If path Is Nothing Then
            LogThis("cam path: none for {0}", map)
            Return
        End If
        ' In full, because "which of the copies is this" cost an evening once:
        ' the file being played and the file being saved were not the same one.
        LogThis("cam path: reading {0}", path)
        curve_dir = IO.Path.GetDirectoryName(path)

        Try
            Dim raw = File.ReadAllBytes(path)
            If raw.Length < HEADER_SIZE Then
                LogThis("cam path: {0} is shorter than its header", path)
                Return
            End If

            Dim magic = BitConverter.ToUInt32(raw, 0)
            If magic = MAGIC_V1 Then
                ' Named rather than lumped in with "bad magic", because this one
                ' has a fix: it is an old file, and Path Studio writes the new
                ' one. A version 1 file carries no seed and cannot be upgraded.
                LogThis("cam path: {0} is version 1 - regenerate it in Path Studio", path)
                Return
            End If
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

            ' Take the header size FROM the header. The format's own rule is to
            ' skip by the sizes it declares rather than the ones this build was
            ' compiled against, which is what lets it grow again without this
            ' code needing to know.
            Dim head_size = CInt(BitConverter.ToUInt32(raw, 60))
            created = BitConverter.ToInt64(raw, 64)
            Dim seed_count = CInt(BitConverter.ToUInt32(raw, 72))
            Dim seed_stride = CInt(BitConverter.ToUInt32(raw, 76))
            seed_heading = BitConverter.ToSingle(raw, 80)

            ' Lights live in what version 2 originally reserved, so a file
            ' written before they existed has zeros here and reads as none -
            ' no version test, no special case.
            Dim light_count = CInt(BitConverter.ToUInt32(raw, 96))
            Dim light_stride = CInt(BitConverter.ToUInt32(raw, 100))
            ' Bulbs took the next two words of the reserve; same rule.
            Dim bulb_count = CInt(BitConverter.ToUInt32(raw, 104))
            Dim bulb_stride = CInt(BitConverter.ToUInt32(raw, 108))

            If stride < POINT_STRIDE Then
                LogThis("cam path: point stride {0} is smaller than {1}", stride, POINT_STRIDE)
                Return
            End If
            If head_size < HEADER_SIZE Then
                LogThis("cam path: header size {0} is smaller than {1}", head_size, HEADER_SIZE)
                Return
            End If
            If seed_count > 0 AndAlso seed_stride < SEED_STRIDE Then
                LogThis("cam path: seed stride {0} is smaller than {1}", seed_stride, SEED_STRIDE)
                Return
            End If
            ' A count with no stride is a corrupt header, not an old file.
            If light_count > 0 AndAlso light_stride < LIGHT_STRIDE Then
                LogThis("cam path: light stride {0} is smaller than {1}", light_stride, LIGHT_STRIDE)
                Return
            End If
            If bulb_count > 0 AndAlso bulb_stride < BULB_STRIDE_MIN Then
                LogThis("cam path: bulb stride {0} is smaller than {1}", bulb_stride, BULB_STRIDE_MIN)
                Return
            End If

            Dim want = head_size + count * stride + seed_count * seed_stride _
                       + light_count * light_stride + bulb_count * bulb_stride
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
                Dim o = head_size + i * stride
                points(i).pos = New Vector3(BitConverter.ToSingle(raw, o),
                                            BitConverter.ToSingle(raw, o + 4),
                                            BitConverter.ToSingle(raw, o + 8))
                points(i).heading = BitConverter.ToSingle(raw, o + 12)
                points(i).tilt = BitConverter.ToSingle(raw, o + 16)
                points(i).roll = BitConverter.ToSingle(raw, o + 20)
                points(i).s = BitConverter.ToSingle(raw, o + 24)
                points(i).speed = BitConverter.ToSingle(raw, o + 28)
            Next

            ' The seeds sit after the points. Read but not required - a file
            ' written by the command line exporter has none, and the flight is
            ' identical either way.
            ReDim seeds(Math.Max(0, seed_count) - 1)
            Dim seed_base = head_size + count * stride
            For i = 0 To seed_count - 1
                Dim o = seed_base + i * seed_stride
                seeds(i).x = BitConverter.ToSingle(raw, o)
                seeds(i).z = BitConverter.ToSingle(raw, o + 4)
                seeds(i).is_start = BitConverter.ToUInt32(raw, o + 8) = SEED_START
            Next

            ' Lights sit after the seeds. Optional in exactly the same way -
            ' a file with none is not a lesser file, and the flight is
            ' identical either way.
            ReDim lights(Math.Max(0, light_count) - 1)
            Dim light_base = head_size + count * stride + seed_count * seed_stride
            For i = 0 To light_count - 1
                Dim o = light_base + i * light_stride
                lights(i).pos = New Vector3(BitConverter.ToSingle(raw, o),
                                            BitConverter.ToSingle(raw, o + 4),
                                            BitConverter.ToSingle(raw, o + 8))
                lights(i).color = New Vector3(BitConverter.ToSingle(raw, o + 12),
                                              BitConverter.ToSingle(raw, o + 16),
                                              BitConverter.ToSingle(raw, o + 20))
                lights(i).level = BitConverter.ToSingle(raw, o + 24)
                lights(i).range_m = BitConverter.ToSingle(raw, o + 28)
                ' By the stride the file declares, not by the record size this
                ' version knows: a file from before the curve field is 32 bytes
                ' a light and reads as curve 0 without a special case.
                lights(i).curve = If(light_stride >= 36,
                                     CInt(Math.Min(2UI, BitConverter.ToUInt32(raw, o + 32))), 0)
                lights(i).vol_mix = 1.0F
                lights(i).dir = -Vector3.UnitY
            Next

            ' Bulbs sit after the lights: lights a model carries, one record per
            ' model, expanded onto every instance at load. Optional like the
            ' rest - a file from before them has zeros in the header.
            ReDim bulbs(Math.Max(0, bulb_count) - 1)
            Dim bulb_base = light_base + light_count * light_stride
            For i = 0 To bulb_count - 1
                Dim o = bulb_base + i * bulb_stride
                Dim nlen = Array.IndexOf(raw, CByte(0), o, BULB_NAME_LEN)
                If nlen < 0 Then nlen = o + BULB_NAME_LEN
                bulbs(i).primitives = Text.Encoding.UTF8.GetString(raw, o, nlen - o)
                bulbs(i).kind = CInt(Math.Min(CUInt(BULB_KIND_MAX), BitConverter.ToUInt32(raw, o + 160)))
                bulbs(i).pos = New Vector3(BitConverter.ToSingle(raw, o + 164),
                                           BitConverter.ToSingle(raw, o + 168),
                                           BitConverter.ToSingle(raw, o + 172))
                bulbs(i).aim = New Vector3(BitConverter.ToSingle(raw, o + 176),
                                           BitConverter.ToSingle(raw, o + 180),
                                           BitConverter.ToSingle(raw, o + 184))
                bulbs(i).cone = BitConverter.ToSingle(raw, o + 188)
                bulbs(i).blend = BitConverter.ToSingle(raw, o + 192)
                bulbs(i).color = New Vector3(BitConverter.ToSingle(raw, o + 196),
                                             BitConverter.ToSingle(raw, o + 200),
                                             BitConverter.ToSingle(raw, o + 204))
                bulbs(i).level = BitConverter.ToSingle(raw, o + 208)
                bulbs(i).range_m = BitConverter.ToSingle(raw, o + 212)
                bulbs(i).vol_mix = BitConverter.ToSingle(raw, o + 216)
                bulbs(i).curve = CInt(Math.Min(2UI, BitConverter.ToUInt32(raw, o + 220)))
                ' By the stride the FILE declares, not by the record size this
                ' version knows: a file from before the two angles is 224 bytes
                ' a bulb and reads as 0 / 0, which every consumer treats as
                ' "fall back to blend". Same rule as the light record's curve.
                If bulb_stride >= BULB_STRIDE Then
                    bulbs(i).ang0 = BitConverter.ToSingle(raw, o + 224)
                    bulbs(i).ang1 = BitConverter.ToSingle(raw, o + 228)
                Else
                    bulbs(i).ang0 = 0.0F
                    bulbs(i).ang1 = 0.0F
                End If
            Next

            path_lights = lights
            ExpandBulbs()

            loaded = True
            source_file = path
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
            LogThis("cam path: {0} seed point(s), {1} light(s), {4} bulb(s), planned heading {2:0.0} deg, written {3}",
                    seed_count, light_count, seed_heading * 180.0F / CSng(Math.PI),
                    If(created > 0,
                       DateTimeOffset.FromUnixTimeSeconds(created).LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                       "unknown"), bulb_count)
            For i = 0 To light_count - 1
                LogThis("cam path: light {0} at ({1:0.0}, {2:0.0}) rgb ({3:0.00}, {4:0.00}, {5:0.00}) level {6:0.00} range {7:0.0} m",
                        i, lights(i).pos.X, lights(i).pos.Z,
                        lights(i).color.X, lights(i).color.Y, lights(i).color.Z,
                        lights(i).level, lights(i).range_m)
            Next

        Catch ex As Exception
            LogThis("cam path: failed to read {0}: {1}", path, ex.Message)
            loaded = False
        End Try
    End Sub

    ''' <summary>
    ''' Re-report the loaded path on demand. Same facts Load logs, but the boot
    ''' log is long gone by the time anyone looks - Snapshot only tees what it
    ''' writes itself, so anything worth reading later has to be written here.
    ''' </summary>
    Public Sub LogSnapshot()
        If Not loaded Then
            LogThis("  cam path: none loaded")
            Return
        End If
        LogThis("  cam path: {0} v2, {1} points over {2:0} m ({3}), {4} seed point(s), written {5}",
                map_name, points.Length, total_len,
                If(closed, "closed", "open"),
                If(seeds Is Nothing, 0, seeds.Length),
                If(created > 0,
                   DateTimeOffset.FromUnixTimeSeconds(created).LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                   "unknown"))
        LogThis("  cam path: from {0}", source_file)
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
    ''' <summary>
    ''' Build the unit sphere every light is drawn with.
    '''
    ''' A plain UV sphere, non-indexed. 16 x 24 is 768 triangles, which is
    ''' nothing next to a map and saves an index buffer and its bookkeeping
    ''' for a mesh that is uploaded once and never touched again.
    ''' </summary>
    Private Sub build_sphere()
        If sphere_vao IsNot Nothing Then Return

        Const RINGS As Integer = 16
        Const SECTORS As Integer = 24
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

        sphere_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "camLightSphere")
        sphere_vbo.Storage(arr.Length * 4, arr, BufferStorageFlags.None)

        sphere_vao = GLVertexArray.Create("camLightVao")
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

    ''' <summary>
    ''' Draw each light as a translucent sphere the size of its range.
    '''
    ''' Nothing is LIT by these - they show what was authored in Path Studio,
    ''' at the size it was authored at, which is the only way to judge whether
    ''' a range is sensible before there is any lighting to look at.
    ''' </summary>
    Public Sub DrawLights()
        If Not loaded OrElse lights Is Nothing OrElse lights.Length = 0 Then Return

        build_sphere()
        If sphere_vao Is Nothing Then Return

        GL_PUSH_GROUP("MapCamPath::DrawLights")

        camlightShader.Use()
        sphere_vao.Bind()

        Dim eye = If(map_scene IsNot Nothing, map_scene.camera.CAM_POSITION, Vector3.Zero)
        GL.Uniform3(camlightShader("eye"), eye.X, eye.Y, eye.Z)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        ' Depth TEST on, depth WRITE off. The test is what lets terrain in front
        ' of a light hide it, which is most of the height cue; the write is what
        ' would make the nearest sphere punch a hole in every one behind it, and
        ' in the scene drawn after this.
        GL.DepthMask(False)

        ' Culling off. A 50 m range is bigger than the camera's distance to it
        ' more often than not, and with back faces culled a light you are INSIDE
        ' vanishes completely - the one moment its size is most worth seeing.
        GL.Disable(EnableCap.CullFace)

        For i = 0 To lights.Length - 1
            ' y in the file is metres ABOVE THE TERRAIN - Path Studio places on
            ' a 2D map and writes 0 - so the ground is resolved here. Without
            ' this every light sits at world zero, which on most maps is under
            ' the landscape and invisible.
            Dim g = world_pos(i)

            GL.Uniform3(camlightShader("centre"), g.X, g.Y, g.Z)
            GL.Uniform1(camlightShader("radius"), Math.Max(0.1F, lights(i).range_m))
            GL.Uniform3(camlightShader("light_color"),
                        lights(i).color.X, lights(i).color.Y, lights(i).color.Z)
            ' Level drives opacity. A light turned down should look turned down,
            ' not merely be labelled that way.
            GL.Uniform1(camlightShader("alpha"),
                        0.10F + 0.35F * Math.Max(0.0F, Math.Min(1.0F, lights(i).level)))
            GL.DrawArrays(PrimitiveType.Triangles, 0, sphere_verts)
        Next

        GL.Enable(EnableCap.CullFace)
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)
        camlightShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub DrawPath()
        If Not loaded OrElse vao Is Nothing Then Return

        GL_PUSH_GROUP("MapCamPath::DrawPath")

        campathShader.Use()
        vao.Bind()

        ' The geometry stage needs the viewport to size the ribbon in pixels.
        ' Read every frame rather than cached - the window resizes.
        GL.Uniform2(campathShader("viewport"), CSng(MainFBO.width), CSng(MainFBO.height))
        GL.Uniform1(campathShader("line_px"), LINE_PX)
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

        ' ONE pass, no depth test, full alpha - whether flying or not.
        '
        ' There used to be two: a 0.35 ghost with the depth test off, and a solid
        ' pass with it on, so the route read solid where it was really visible
        ' and ghosted where something stood in front of it. That is a genuine
        ' height cue and it was worth having while this was a debugging aid.
        '
        ' It cannot survive the camera moving, though. Pull back and the route's
        ' pixels increasingly land on terrain that is NEARER than the route -
        ' a metre of clearance is well under a pixel at map scale - so the solid
        ' pass loses the depth test along more and more of its length and the
        ' whole path fades toward the ghost. Nothing about the route changed;
        ' only how much of it wins a depth comparison. The same effect made it
        ' flicker while flying, where the view along the route is grazing.
        '
        ' A route overlay that changes brightness with zoom is worse than one
        ' that cannot tell you its height. Draw it once, unlit, over everything.
        GL.Disable(EnableCap.DepthTest)
        GL.Uniform1(campathShader("alpha_mul"), 1.0F)
        GL.DrawArrays(PrimitiveType.Lines, 0, vertex_count)

        ' Leave the depth test on however we got here - the flying branch turned
        ' it off and everything drawn after this expects it back.
        GL.Enable(EnableCap.DepthTest)
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

    ''' <summary>
    ''' Release the PATH's buffers. Called on every Load, not just at teardown.
    '''
    ''' The light sphere is deliberately NOT freed here. It belongs to the
    ''' renderer, not to the route - the same unit mesh serves every light on
    ''' every map - and tearing it down when a path reloads is exactly what
    ''' broke Show Lights: the buffers were disposed but the fields were left
    ''' non-Nothing, so build_sphere's "already built" guard skipped the rebuild
    ''' and every light afterwards drew from a dead VAO.
    ''' </summary>
    Private Sub Dispose_gl()
        vao?.Dispose()
        vbo?.Dispose()
        vao = Nothing
        vbo = Nothing
        vertex_count = 0
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose_gl()
        ' The sphere outlives any single path, so it is freed HERE and only
        ' here. Nulled as well as disposed, so a rebuild is possible if this
        ' object is ever reused - a disposed handle that still reads as
        ' "present" is the bug this replaced.
        sphere_vao?.Dispose()
        sphere_vbo?.Dispose()
        sphere_vao = Nothing
        sphere_vbo = Nothing
        sphere_verts = 0
        GC.SuppressFinalize(Me)
    End Sub
End Class

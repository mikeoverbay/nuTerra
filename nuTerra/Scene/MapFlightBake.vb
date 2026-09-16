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

    ''' <summary>Texels a side. 8192 over a 1200 m map is 0.15 m per texel.
    ''' It was 2048 (0.59 m), and 1024 before that (1.17 m, a 3 m wall two
    ''' cells wide). Path Studio reads width and height from the meta and
    ''' works at 2048 whatever the bake is - block MAX for the top, so
    ''' anything a finer bake caught survives the downsample - so this can
    ''' move without breaking bakes already on disk. What it costs: two
    ''' 268 MB .r32 files per map in TEMP and a 256 MB depth texture.
    '''
    ''' What it does NOT buy on its own is fences: a fence is a vertical
    ''' plane, and from straight above a vertical plane has no area, so the
    ''' fill pass writes nothing for it at any resolution. That is what the
    ''' LINE pass in draw_models is for.</summary>
    Public Const SIZE As Integer = 8192

    ''' <summary>The mask PNG is for eyeballing, so it is written at this
    ''' many texels a side: SIZE squared through GDI+ would be a quarter
    ''' gigabyte bitmap and seconds of PNG on every map load. A block is
    ''' white if ANY texel in it stands over OBSTACLE_MIN_H.</summary>
    Public Const MASK_SIZE As Integer = 2048

    ''' <summary>Height above the terrain at which something counts as an
    ''' obstacle in the exported mask. The mask is for eyeballing only - the
    ''' planner gets top and floor and should threshold them itself, so it can
    ''' change its mind about what a 1 m kerb means without a re-bake.</summary>
    Public Const OBSTACLE_MIN_H As Single = 1.0F

    ''' <summary>Bake and export on every map load. On while the planner is
    ''' being written offline; turn it off once the algorithm moves in here and
    ''' the files stop being the interface.</summary>
    ''' <summary>
    ''' Bumped whenever a change here would make an ALREADY SAVED bake wrong.
    '''
    ''' This is the one check that cannot be derived from the files, and it is the
    ''' one that matters. Every other test compares a number in the meta against
    ''' the same number now - but a change to WHAT IS DRAWN, the trunk pass, the
    ''' foliage alpha cut, the water raise, the despike, leaves every one of those
    ''' numbers identical and the contents different.
    '''
    ''' So: change what the bake contains, bump this. Otherwise the next run loads
    ''' the bake from before your change and you measure the old one, which looks
    ''' exactly like your change having no effect.
    '''
    ''' 2 - the SOLID bit in the key byte and the per-object id layer, together,
    ''' because both change what the bake contains and one bump covers both.
    ''' </summary>
    Public Const BAKE_VERSION As Integer = 5

    Public Const BAKE_AT_LOAD As Boolean = True

    Private fbo As GLFramebuffer
    Private depth_tex As GLTexture
    Private kind_tex As GLTexture
    Private id_tex As GLTexture

    ''' <summary>What stands at each texel - see kind_of. One byte per texel,
    ''' the kind of the TOPMOST thing, which the depth test decides for
    ''' free.</summary>
    Public kind_b(SIZE * SIZE - 1) As Byte

    ''' <summary>
    ''' WHICH object stands at each texel, biased by one so zero means nothing.
    '''
    ''' Not a field for the life of the map: it is filled by read_ids, written
    ''' by export and erased. Nothing in the app reads it - the consumers are
    ''' the planners, and they read the file - so keeping 268 MB resident for a
    ''' map's lifetime would buy nobody anything.
    ''' </summary>
    Private id_u() As UInteger

    ''' <summary>One byte a texel, 1 where something solid stands. Scratch
    ''' between read_solid and apply_solid, erased as soon as the bit is in the
    ''' key byte.</summary>
    Private solid_b() As Byte

    ''' <summary>What the last read_solid / read_ids measured, for the log and
    ''' for the meta.</summary>
    Private solid_cells As Integer
    Private id_nonzero As Integer
    Private id_max_seen As UInteger

    ''' <summary>
    ''' The two height maps, SIXTEEN BIT, in the same encoding the files use.
    '''
    ''' These were float32, which at 8192 squared is 268 MB each and half a
    ''' gigabyte resident for the life of the map. The export has always
    ''' written 16-bit and nothing has ever wanted more: a step is
    ''' 1/HEIGHT_SCALE = 1.6 cm, an order finer than the 17 cm texel the
    ''' heights are sampled on, so the float precision was describing detail
    ''' the grid could not hold. Storing what is exported also means there is
    ''' one representation to be wrong rather than two to disagree.
    '''
    ''' Read and written through the top_m / floor_m properties below, which
    ''' are indexed exactly as the arrays were - so every caller reads the
    ''' same as it always did and none of them had to change.
    ''' </summary>
    Private top_u(SIZE * SIZE - 1) As UShort
    Private floor_u(SIZE * SIZE - 1) As UShort

    ''' <summary>Metres the stored counts are measured up from. Fixed by the
    ''' FLOOR pass and used by both maps and by the export, so the numbers in
    ''' memory and the numbers on disk cannot drift apart.</summary>
    Public h_offset As Single

    ''' <summary>The highest surface at a texel - terrain, models and trees
    ''' together.</summary>
    Public Property top_m(i As Integer) As Single
        Get
            Return h_offset + top_u(i) / HEIGHT_SCALE
        End Get
        Set(value As Single)
            top_u(i) = quantise(value)
        End Set
    End Property

    ''' <summary>The terrain alone, under whatever is standing on it.</summary>
    Public Property floor_m(i As Integer) As Single
        Get
            Return h_offset + floor_u(i) / HEIGHT_SCALE
        End Get
        Set(value As Single)
            floor_u(i) = quantise(value)
        End Set
    End Property

    ''' <summary>Metres to a stored count, clamped. The clamp is the reason the
    ''' offset is fitted to the map rather than assumed: 65535 counts is 1024 m
    ''' of range, ample for any arena, but only if zero sits below the deepest
    ''' point - otherwise a quarry encodes negative and reads as flat.</summary>
    Private Function quantise(y As Single) As UShort
        Dim v = CInt(Math.Round((y - h_offset) * HEIGHT_SCALE))
        Return CUShort(Math.Min(Math.Max(v, 0), 65535))
    End Function
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

    ''' <summary>
    ''' What kind of thing a model is, by the folder it came out of.
    '''
    ''' BY FOLDER, because that is the only description the map carries. A
    ''' model in the render set knows its vertices' name and the directory
    ''' that held it, and nothing else says whether a mesh is a church or a
    ''' crate. The owner's framing: mark it by the folder it came out of.
    '''
    ''' FIRST HIT WINS, in this order, and the order is doing work: a path
    ''' like hd_bld_UNI_006_KitCrashFactory carries both bld and crash, and a
    ''' fence around a house sits under the building's folder. Testing fence
    ''' before building would call the factory a fence; testing building
    ''' first would swallow the railings. The order below is the one that
    ''' puts each of those where it belongs.
    '''
    ''' Eight keys at most, agreed with Path Studio, who colours them.
    ''' </summary>
    ''' <summary>
    ''' Every name kind_of was asked about, and what it answered. Only collected
    ''' under `kinddump`, because it is a dictionary per map load otherwise.
    '''
    ''' It exists because the kind NAMES read as though they were derived from
    ''' something - "rock", "building" - when they are the winner of a substring
    ''' race over whatever string the model handed over. Anyone deciding what a
    ''' tank collides with should read the names that landed in a bin rather than
    ''' the name OF the bin.
    ''' </summary>
    Public Shared kind_seen As Dictionary(Of String, Byte)

    Public Shared ReadOnly KIND_DUMP_SYNC As New Object

    ''' <summary>
    ''' The kinds, forwarded from ModelKind, which now owns them.
    '''
    ''' FORWARDERS RATHER THAN A SECOND COPY. Dozens of sites in this file
    ''' and MapLoader say KIND_TREE or MapFlightBake.KIND_OTHER, and
    ''' rewriting every one of them to carry a class name would be a large
    ''' diff whose only effect is churn. These re-export the single
    ''' definition; there is still exactly one table.
    ''' </summary>
    Public Const KIND_TERRAIN As Byte = ModelKind.KIND_TERRAIN
    Public Const KIND_BUILDING As Byte = ModelKind.KIND_BUILDING
    Public Const KIND_FENCE As Byte = ModelKind.KIND_FENCE
    Public Const KIND_TREE As Byte = ModelKind.KIND_TREE
    Public Const KIND_ROCK As Byte = ModelKind.KIND_ROCK
    Public Const KIND_PROP As Byte = ModelKind.KIND_PROP
    Public Const KIND_WATER As Byte = ModelKind.KIND_WATER
    Public Const KIND_OTHER As Byte = ModelKind.KIND_OTHER
    Public Shared ReadOnly KIND_NAMES() As String = ModelKind.KIND_NAMES
    Public Shared ReadOnly KIND_RGB()() As Byte = ModelKind.KIND_RGB

    Private Shared Function classify(p As String) As Byte
        Return ModelKind.classify(p)
    End Function

    Public Shared Function kind_of(path As String) As Byte
        If String.IsNullOrEmpty(path) Then Return KIND_OTHER
        Dim answer = classify(path.Replace("\\", "/").ToLowerInvariant())

        If KIND_DUMP Then
            SyncLock KIND_DUMP_SYNC
                If kind_seen Is Nothing Then kind_seen = New Dictionary(Of String, Byte)
                kind_seen(path) = answer
            End SyncLock
        End If
        Return answer
    End Function

    ''' <summary>
    ''' The substring race itself. FIRST MATCH WINS AND THE ORDER IS LOAD-BEARING:
    ''' rock is tested before building, so anything with "stone" in its name is a
    ''' rock even when it is a house. Not a defect to fix blind - the order has
    ''' been tuned against real maps - but it is why a kind name cannot be taken
    ''' at face value.
    ''' </summary>

    ''' <summary>
    ''' Write what each name was classified as, beside the bake. Runs on the
    ''' loaded path too - the classifier runs while MODELS load, which happens
    ''' whether or not the bake itself was rebuilt.
    ''' </summary>
    Private Sub dump_kinds()
        Try
            Dim rows As New List(Of String)
            Dim tally(7) As Integer
            SyncLock KIND_DUMP_SYNC
                If kind_seen Is Nothing Then Return
                For Each kv In kind_seen
                    Dim k = kv.Value And KIND_MASK
                    tally(k) += 1
                    rows.Add(String.Format("{0},{1},{2}", KIND_NAMES(k), k,
                                           kv.Key.Replace(","c, "_"c)))
                Next
            End SyncLock
            rows.Sort()

            Dim path = bake_stem() & "_kinds.csv"
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("kind,key,name")
            For Each r In rows
                sb.AppendLine(r)
            Next
            IO.File.WriteAllText(path, sb.ToString())

            Dim parts As New List(Of String)
            For k = 0 To 7
                If tally(k) > 0 Then parts.Add(String.Format("{0} {1}", KIND_NAMES(k), tally(k)))
            Next
            LogThis("kind dump: {0} distinct name(s) - {1} - written to {2}",
                    rows.Count, String.Join(", ", parts), path)
        Catch ex As Exception
            LogThis("kind dump: failed - {0}", ex.Message)
        End Try
    End Sub



    ''' <summary>
    ''' Height encoding for the export: metres to a 16-bit count.
    '''
    ''' 64 steps to the metre is 1.5 cm, and 65535 of them is 1024 m of
    ''' range - far more than any map's span. The offset is a whole number
    ''' below the map minimum so nothing encodes negative, and it is written
    ''' into the meta rather than assumed, because a map with a deeper pit
    ''' than this one would otherwise silently clamp at zero.
    ''' </summary>
    Public Const HEIGHT_SCALE As Single = 64.0F

    ''' <summary>
    ''' The key byte carries two things, so read it with these.
    '''
    ''' The low three bits are the kind, unchanged and still 0..7. The top bit
    ''' says a TREE TRUNK stands at this texel, whatever else won the surface
    ''' above it - see the trunk pass in sun_depth_tree.frag. A reader that
    ''' does not know about the bit will see keys of 128..135 and fall off the
    ''' end of its table, so both numbers go in the meta.
    '''
    ''' One byte rather than a second channel because the trunk is a yes or no,
    ''' not a height. A 67 MB plane to carry one bit a texel would be the
    ''' expensive way to say the same thing.
    ''' </summary>
    Public Const KIND_MASK As Byte = &H7

    ''' <summary>
    ''' Bit 4: this is OUTLAND - the scenery ring outside the playable area.
    '''
    ''' A BIT RATHER THAN A NINTH KIND, so nothing is lost. An outland cliff is
    ''' still a cliff; giving it the key "outland" would answer where it is at
    ''' the cost of what it is, and the map would stop being able to say either
    ''' one on its own. With a bit, kind and place are separate questions.
    '''
    ''' It is worth marking because of how much of the map it is. On monastery
    ''' 97.85% of everything standing over 40 m above its terrain is out here,
    ''' and of the texels that are both tall and key 7 - the ones that made the
    ''' unclassified bucket look enormous - exactly SIX are inside 500 m of
    ''' centre. Without this bit a planner reading the far ring sees a wall of
    ''' unexplained obstacles; with it, it sees the backdrop and ignores it.
    '''
    ''' NOT A GUESS. MapLoader partitions the shadow commands into inland and
    ''' outland at load and MapSunShadow already skips the tail, so which draws
    ''' are outland is something the app knows rather than something a folder
    ''' name suggests.
    ''' </summary>
    Public Const OUTLAND_BIT As Byte = &H10

    ''' <summary>
    ''' Bit 5: something SOLID stands at this texel - terrain-borne geometry
    ''' over obstacle_min_h, measured with the trees left out.
    '''
    ''' It exists because the top map is a SINGLE LAYER and the canopy wins the
    ''' depth test. On monastery 2,581 cells hold something solid over 1 m -
    ''' median 1.70 m, up to 22.08 m, mostly rock - and every one of them keys
    ''' as `tree`, because a tree stands over it. A reader whose rule is "a tank
    ''' crushes trees" drives through all of them, and nothing in the bake said
    ''' otherwise.
    '''
    ''' A BIT RATHER THAN A KEY CHANGE, for the reason OUTLAND_BIT gives: the
    ''' tree really is the topmost thing and the camera still wants its height.
    ''' Re-keying the texel to rock would answer the tank's question by
    ''' destroying the camera's. With a bit, both are askable - a ground vehicle
    ''' tests `solid Or trunk`, a camera keeps using the height.
    '''
    ''' AND NOT A SECOND HEIGHT LAYER, which was the other candidate. The
    ''' question a reader has is whether something solid is there, not how tall
    ''' it is - the height it wants is the one already in the top map. 67 MB a
    ''' map to carry one bit a texel is the expensive way to say it, the same
    ''' argument that made the trunk a bit.
    '''
    ''' Written by read_solid, from the depth buffer as it stands after the
    ''' models and before the trees - a moment that already exists in the pass
    ''' order and costs one readback to look at.
    ''' </summary>
    Public Const SOLID_BIT As Byte = &H20

    Public Const TRUNK_BIT As Byte = &H80

    ''' <summary>
    ''' Metres from a tree's axis still counted as its trunk.
    '''
    ''' Bark is trunk AND limbs on every species in the corpus, so the bark
    ''' flag alone cannot isolate a trunk and this radius is what does. 0.6 m
    ''' clears the thickest trunks on the roster while cutting the limbs, which
    ''' fan out well past it.
    ''' </summary>
    Public Shared TRUNK_RADIUS As Single = TreeTrunks.FALLBACK_RADIUS

    ''' <summary>
    ''' Can a hull drive through whatever stands on this texel?
    '''
    ''' ONE COPY. This rule lived in three - TankSquares, TankNav and
    ''' MapTankRays - and one of the three carried a comment telling the
    ''' reader to keep it matching the others. A rule that holds only while
    ''' someone remembers to copy it is a rule that drifts, and all three
    ''' decide whether a tank is stopped.
    '''
    ''' THE TRUNK BLOCKS. The owner, 2026-09-16: "lets assume if it has a
    ''' trunk, we can't drive there. if no trunk we can." So a tree texel is
    ''' crushable canopy unless it carries the trunk bit.
    '''
    ''' This was tried once before and reverted, on the argument that a tank
    ''' knocks the whole tree flat. Measured against the cached bake before
    ''' putting it back: it blocks 8,307 more square metres on monastery, 0.42
    ''' points of the map, because the trunk bit is real bark geometry within
    ''' TRUNK_RADIUS of the axis rather than a stamped disc.
    '''
    ''' The solid bit still disqualifies a tree on its own: rock or wall
    ''' standing under a canopy. Fence and prop are crushable at any height -
    ''' "A fence or curb is not going to stop a tank."
    '''
    ''' THE SOLID BIT QUALIFIES TREES, AND ONLY TREES. It is read from the
    ''' depth buffer BETWEEN the model pass and the tree pass, so any MODEL
    ''' sets it for itself: on monastery it is set on 75.6% of fence texels
    ''' and 63.3% of prop texels, and on just 7.0% of tree texels. Only for a
    ''' tree does it carry information - that something else stands under the
    ''' canopy. Testing it on fence and prop as well, which this did first,
    ''' un-crushed three quarters of the fences on the map and drove a tank
    ''' round them.
    '''
    ''' WHAT THIS CANNOT SEE is a fence standing in front of a wall, where the
    ''' fence wins the depth test and the wall is invisible to the bake. That
    ''' risk predates the bit and this data cannot settle it.
    '''
    ''' OUTLAND AND WATER ARE NOT ASKED HERE. Those block whatever their
    ''' height, so they are the caller's first test and never reach this one.
    ''' </summary>
    Public Shared Function Crushable(k As Byte) As Boolean
        Dim k7 = k And KIND_MASK
        If k7 = KIND_FENCE OrElse k7 = KIND_PROP Then Return True
        If k7 <> KIND_TREE Then Return False
        Return (k And (SOLID_BIT Or TRUNK_BIT)) = 0
    End Function

    Public Shared kind_map() As Byte

    Public Sub Bake()
        ready = False
        Dim bake_clock = Diagnostics.Stopwatch.StartNew()

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

        If KIND_DUMP Then dump_kinds()

        ' Straight down from clear above everything. For an orthographic
        ' projection the eye height changes no framing at all, only what near and
        ' far bracket, so it only has to clear the tallest model.
        '
        ' ABOVE THE CACHE CHECK because the loaded path needs them too: they are
        ' what "nothing rasterised here" means in metres, so report_coverage
        ' cannot read a loaded bake without them. They depend only on the map
        ' height range, which the terrain settled before this ran, so both paths
        ' get the same numbers.
        eye_y = MAX_MAP_HEIGHT + 500.0F
        far_d = eye_y - (MIN_MAP_HEIGHT - 500.0F)

        ' SAVED, AND ONLY REMADE WHEN SOMETHING THAT SHAPES IT HAS MOVED. After
        ' the extent above, because the extent is part of deciding whether the
        ' saved one still describes this map.
        If try_load() Then
            ' The SAME report the bake prints, over the arrays that just came off
            ' disk. It is the cheapest possible check that what was loaded is a
            ' map rather than 400 MB of plausible bytes: the blocked share, the
            ' water and the tallest obstacle all have to come out where they did
            ' when it was baked, and they are printed either way so the two runs
            ' can be read against each other.
            report_coverage()

            ' AND AN INDEPENDENT ONE, because report_coverage cannot see the
            ' error that matters most here.
            '
            ' It measures top_m - floor_m, a DIFFERENCE, and h_offset cancels out
            ' of a difference. So if the offset read back from the meta were
            ' wrong, every absolute height on the map would shift, the blocked
            ' share and the tallest obstacle would come out identical, and this
            ' path would report itself correct. The check and the thing it checks
            ' would share the assumption - which is how the Tank AI session's
            ' routes measured clean for a week while being undriveable: the
            ' validator sampled the centre line, exactly as the planner did.
            '
            ' verify_against_cpu asks a different source entirely - it probes the
            ' loaded floor against get_Y_at_XZ_fast, the terrain's own height
            ' function, which knows nothing about the bake or its offset. 25
            ' probes, and it is the only thing on this path that would notice a
            ' bake loaded a metre out.
            verify_against_cpu()

            ' The meta, but only if this build would write it differently - a new
            ' key, an edited palette. export() does not run on this path, so
            ' without this a saved bake would keep its original meta for ever and
            ' a reader waiting on a new key would wait for a rebake that has no
            ' reason to happen.
            write_meta(bake_stem() & "_meta.txt")

            ' The sidecar, if it has gone missing, WITHOUT rebaking for it. It
            ' is not derived from the bake at all - it comes from the model and
            ' tree tables this map load just built - so it can be rewritten from
            ' live state, and losing a 20 KB text file is no reason to spend six
            ' seconds rasterising the map again.
            Try
                Dim f_names = bake_stem() & "_ids.csv"
                If Not IO.File.Exists(f_names) Then write_id_names(f_names)
            Catch ex As Exception
                LogThis("flight bake: could not rewrite the id sidecar - {0}", ex.Message)
            End Try

            ready = True
            Return
        End If

        Dim cx = (wx_min + wx_max) * 0.5F
        Dim cz = (wz_min + wz_max) * 0.5F
        Dim half_w = (wx_max - wx_min) * 0.5F
        Dim half_h = (wz_max - wz_min) * 0.5F


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
        read_heights(False)

        ' top - the ground and everything standing on it
        '
        ' The KEY channel is cleared to zero, which is the terrain's own key, so
        ' a texel nothing stood on reads as ground without the terrain pass
        ' having to write anything. Only the floor pass above skips the clear -
        ' it has no key to gather.
        ' The ID channel is cleared the same way and for the same reason: zero
        ' is "no object here", which is what a texel showing bare ground is.
        GL.Clear(ClearBufferMask.DepthBufferBit)
        GL.ClearBuffer(ClearBuffer.Color, 1, {0.0F, 0.0F, 0.0F, 0.0F})
        GL.ClearBuffer(ClearBuffer.Color, 2, New UInteger() {0UI, 0UI, 0UI, 0UI})
        draw_terrain(vp)
        draw_models(vp)

        ' BETWEEN THE MODELS AND THE TREES, and that is the whole of the idea.
        ' The depth buffer at this instant holds terrain and built geometry and
        ' no foliage at all, which is the one moment in the pass where the
        ' question "is something SOLID standing here" has an answer. A texel
        ' that later keys as tree because a canopy closed over it still carries
        ' the bit this reads. See SOLID_BIT.
        '
        ' DO NOT MOVE THIS CALL. Since bake_version 5 the ordering is not a
        ' convenience, it is load bearing for CORRECTNESS, and moving it breaks
        ' the bake silently rather than loudly.
        '
        ' read_solid also reads kind_tex, and it is only meaningful here: every
        ' byte in that texture right now belongs to a MODEL, because draw_trees
        ' has not run. That is what lets it ask "is the solid thing standing at
        ' this texel ITSELF crushable" and exempt a grape trellis from its own
        ' solid bit while a rock under a canopy keeps one. Draw the trees first
        ' and every canopy texel reads as kind TREE, so the exemption would fire
        ' on foliage standing over walls and walk tanks through them - with no
        ' error, no crash, and a bake that still looks entirely reasonable.
        read_solid()

        draw_trees(vp)

        ' What a GROUND VEHICLE would hit, which is not what the camera hits.
        ' Runs last and writes only the key channel's top bit, so it cannot
        ' disturb the heights the two passes above just settled.
        draw_trunks(vp)

        read_heights(True)
        read_kinds()

        ' AFTER read_kinds, which overwrites the whole key array with what the
        ' GL texture holds - the bit would be gone if it went in before.
        apply_solid()

        read_ids()
        despike_top()

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

        LogThis("flight bake: built and saved in {0} ms", bake_clock.ElapsedMilliseconds)
    End Sub

    ''' <summary>
    ''' The world-to-texel mapping above is a DERIVATION, and a derivation is not
    ''' a measurement. Probe the floor map against the CPU height function at
    ''' asymmetric points and report the mean error for that mapping and for its
    ''' three reflections. If one of the reflections wins, the orientation is
    ''' wrong and this says which way; if all four are large, something further
    ''' up is wrong and no amount of flipping will fix it.
    ''' </summary>
    ''' <summary>
    ''' The saved bake, if there is one and it still describes this map. True when
    ''' the arrays have been filled from disk and the bake can be skipped.
    '''
    ''' The files are the export's own, read back: top.rgba carries the kind byte
    ''' and the 16-bit top height, floor.r16 the floor. Those three arrays ARE the
    ''' whole of what anything downstream reads - TankNav, the shot tracing, the
    ''' drivable tests - so this is not an approximation of the bake, it is the
    ''' bake.
    '''
    ''' TWO THINGS INVALIDATE IT and the rest is cheap insurance: our own bake
    ''' changing, which BAKE_VERSION says, and a major game release changing the
    ''' assets under us, which game_version says. The map itself does not move
    ''' between releases.
    '''
    ''' EVERY REJECTION SAYS WHY. A cache that silently misses looks exactly like
    ''' one that is working, and being wrong here means measuring yesterday's map,
    ''' so the log always names which of the two happened.
    ''' </summary>
    Private Function try_load() As Boolean
        If FLIGHT_REBAKE Then
            LogThis("flight bake: rebuild forced - ignoring any saved bake")
            Return False
        End If

        Try
            Dim stem = bake_stem()
            Dim f_top = stem & "_top.rgba"
            Dim f_floor = stem & "_floor.r16"
            Dim f_ids = stem & "_ids.u32"
            Dim f_meta = stem & "_meta.txt"

            ' NAME THE FILE THAT IS MISSING. "nothing saved" was honest while
            ' the set was three files that had always appeared together; since
            ' _ids.u32 arrived in bake_version 2 the common case is a bake that
            ' is entirely there EXCEPT the new layer, and being told "nothing
            ' saved" while looking at 400 MB of bake on disk reads as a broken
            ' cache rather than as a version gate doing its job. It cost the
            ' tank AI session an hour of believing a rebake was impossible.
            Dim missing As New List(Of String)
            If Not IO.File.Exists(f_top) Then missing.Add("_top.rgba")
            If Not IO.File.Exists(f_floor) Then missing.Add("_floor.r16")
            If Not IO.File.Exists(f_ids) Then missing.Add("_ids.u32")
            If Not IO.File.Exists(f_meta) Then missing.Add("_meta.txt")
            If missing.Count > 0 Then
                LogThis("flight bake: {0} for {1} - baking. Missing: {2}",
                        If(missing.Count = 4, "nothing saved", "the saved bake is incomplete"),
                        MAP_NAME_NO_PATH, String.Join(", ", missing))
                Return False
            End If

            ' Size first: a half-written file from an interrupted run is the one
            ' corruption that reads as a perfectly good header.
            Dim want_top As Long = CLng(SIZE) * SIZE * 4
            Dim want_floor As Long = CLng(SIZE) * SIZE * 2
            Dim want_ids As Long = CLng(SIZE) * SIZE * 4
            Dim got_top = New IO.FileInfo(f_top).Length
            Dim got_floor = New IO.FileInfo(f_floor).Length
            Dim got_ids = New IO.FileInfo(f_ids).Length
            If got_top <> want_top OrElse got_floor <> want_floor OrElse got_ids <> want_ids Then
                LogThis("flight bake: saved bake is the wrong size (top {0} of {1}, floor {2} of {3}, ids {4} of {5}) - baking",
                        got_top, want_top, got_floor, want_floor, got_ids, want_ids)
                Return False
            End If

            Dim meta As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each raw In IO.File.ReadAllLines(f_meta)
                Dim line = raw.Trim()
                If line.Length = 0 OrElse line.StartsWith("#") Then Continue For
                Dim eq = line.IndexOf("="c)
                If eq > 0 Then meta(line.Substring(0, eq).Trim()) = line.Substring(eq + 1).Trim()
            Next

            Dim why = ""
            If num(meta, "bake_version") <> BAKE_VERSION Then
                why = String.Format("built by bake version {0}, this is {1}",
                                    meta_or(meta, "bake_version", "none"), BAKE_VERSION)
            ElseIf meta_or(meta, "game_version", "") <> game_version() Then
                why = String.Format("built against game {0}, this is {1}",
                                    meta_or(meta, "game_version", "none"), game_version())
            ElseIf Not String.Equals(meta_or(meta, "map", ""), MAP_NAME_NO_PATH,
                                     StringComparison.OrdinalIgnoreCase) Then
                why = "it is another map's bake: " & meta_or(meta, "map", "?")
            ElseIf num(meta, "width") <> SIZE OrElse num(meta, "height") <> SIZE Then
                why = String.Format("it is {0}x{1}, this build bakes {2}x{2}",
                                    meta_or(meta, "width", "?"), meta_or(meta, "height", "?"), SIZE)
            ElseIf Not near(num(meta, "wx_min"), wx_min) OrElse Not near(num(meta, "wx_max"), wx_max) OrElse
                   Not near(num(meta, "wz_min"), wz_min) OrElse Not near(num(meta, "wz_max"), wz_max) Then
                why = "the map extent moved"
            ElseIf Not near(num(meta, "height_scale"), HEIGHT_SCALE) OrElse
                   Not near(num(meta, "obstacle_min_h"), OBSTACLE_MIN_H) OrElse
                   Not near(num(meta, "trunk_radius"), TRUNK_RADIUS) Then
                why = "a bake constant changed"
            ElseIf num(meta, "kind_mask") <> KIND_MASK OrElse num(meta, "outland_bit") <> OUTLAND_BIT OrElse
                   num(meta, "trunk_bit") <> TRUNK_BIT OrElse num(meta, "solid_bit") <> SOLID_BIT Then
                why = "the key bits changed"
            End If

            If why <> "" Then
                LogThis("flight bake: saved bake is stale - {0} - baking", why)
                Return False
            End If

            ' The offset the heights are measured from comes FROM the file. It is
            ' whole - Math.Floor(lo) - 10 - so it survives the meta's "{0:0}", and
            ' taking this run's value instead would shift every height on the map
            ' by the difference with nothing looking wrong.
            h_offset = CSng(num(meta, "height_offset"))

            ' Keep when these bytes were baked. The meta may be rewritten below
            ' to carry keys this build adds, and that must not restamp the bake
            ' itself as having been made just now.
            meta_written = meta_or(meta, "written", "")
            If meta_written = "" Then
                ' A bake saved before this key existed. The honest answer is not
                ' "now" - that would date yesterday's bytes to this launch - it is
                ' when the bytes were actually written, which the file itself
                ' knows.
                Try
                    meta_written = IO.File.GetLastWriteTime(f_top).ToString("s")
                Catch
                End Try
            End If

            Dim clock = Diagnostics.Stopwatch.StartNew()
            Dim b = IO.File.ReadAllBytes(f_top)
            Dim floor_bytes = IO.File.ReadAllBytes(f_floor)
            Dim read_ms = clock.ElapsedMilliseconds

            ' THE ID LAYER IS CHECKED ABOVE AND NOT READ. Its consumers are the
            ' planners and they read the file; nothing in this process asks for
            ' it, so pulling 268 MB into managed memory on every map load would
            ' buy nobody anything. The size test above is what says it is there
            ' and whole.
            '
            ' THE FLOOR GOES STRAIGHT IN. write_floor_r16 puts the low byte
            ' first, which is exactly how a UShort sits in memory on x86, so the
            ' file IS the array and a 67-million-iteration loop to copy it to
            ' itself is pure waste. The top map cannot do this - the kind byte
            ' and a BIG-endian height are interleaved a texel - so that one is
            ' still a loop, and the two timings below say what each costs.
            System.Buffer.BlockCopy(floor_bytes, 0, floor_u, 0, floor_bytes.Length)

            For i = 0 To SIZE * SIZE - 1
                Dim o = i * 4
                kind_b(i) = b(o)
                top_u(i) = CUShort((CInt(b(o + 1)) << 8) Or b(o + 2))
            Next

            LogThis("flight bake: LOADED the saved bake for {0} in {1} ms ({2} ms reading, {3} ms decoding) - not rebuilt",
                    MAP_NAME_NO_PATH, clock.ElapsedMilliseconds, read_ms,
                    clock.ElapsedMilliseconds - read_ms)
            Return True
        Catch ex As Exception
            LogThis("flight bake: could not read the saved bake ({0}) - baking", ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function meta_or(m As Dictionary(Of String, String), k As String, dflt As String) As String
        Dim v As String = Nothing
        If m.TryGetValue(k, v) Then Return v
        Return dflt
    End Function

    Private Shared Function num(m As Dictionary(Of String, String), k As String) As Double
        Dim v As String = Nothing
        If Not m.TryGetValue(k, v) Then Return Double.NaN
        Dim d As Double
        If Double.TryParse(v, Globalization.NumberStyles.Float,
                           Globalization.CultureInfo.InvariantCulture, d) Then Return d
        Return Double.NaN
    End Function

    ''' <summary>Equal to within the meta's own written precision. NaN - a key that
    ''' was missing or unparseable - is near nothing, so an old meta lacking a key
    ''' it needs is stale rather than accidentally acceptable.</summary>
    Private Shared Function near(a As Double, b As Double) As Boolean
        If Double.IsNaN(a) OrElse Double.IsNaN(b) Then Return False
        Return Math.Abs(a - b) <= 0.002
    End Function

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
    ''' <summary>Water is raised on the CPU after the draw, so it keys itself
    ''' here - there is no pass for it to have written from.</summary>
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
                    If y > top_m(i) Then
                        top_m(i) = y
                        ' OR, not assign: the flag bits were set by other
                        ' passes and water raising the surface here does not
                        ' mean the trunk stopped existing, the texel left the
                        ' outland, or the wall standing in the shallows
                        ' dissolved.
                        kind_b(i) = CByte(KIND_WATER Or
                                          (kind_b(i) And (TRUNK_BIT Or OUTLAND_BIT Or SOLID_BIT)))
                    End If
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

    ''' <summary>The key channel, flipped the same way the heights are so row
    ''' 0 is the wz_max edge and the two arrays index alike.</summary>
    ''' <summary>
    ''' Above this much over the local ground, a texel is worth LOOKING at.
    ''' Not worth condemning - see despike_top. Real things reach this height:
    ''' the outland ring runs to 164 m and is excluded from the test outright,
    ''' and a mast or a spire inland could too.
    ''' </summary>
    Private Const NEEDLE_LOOK_H As Single = 60.0F

    ''' <summary>How far a texel must stand above its OWN NEIGHBOURS before it
    ''' is called a needle. A real tower is surrounded by itself.</summary>
    Private Const NEEDLE_OVER_NB As Single = 30.0F

    ''' <summary>
    ''' Pull down the isolated single texels that stand hundreds of metres over
    ''' nothing.
    '''
    ''' Monastery bakes four of them - 602 m, 303 m, 214 m and 186 m above their
    ''' own ground, each one texel wide with its eight neighbours at normal
    ''' height. They are not a bug in this writer: the heights encode and decode
    ''' exactly, nothing clamps, and the old bake simply never saw them. They
    ''' are near-vertical slivers in a few source meshes whose footprint from
    ''' straight above is about one texel, and the LINE pass in draw_models
    ''' finds them precisely because finding thin vertical things is its job.
    '''
    ''' TWO TESTS, AND THE SECOND IS THE ONE THAT MATTERS. Height above ground
    ''' alone would condemn any real mast; what marks a needle is that it is
    ''' alone. A genuine tower is surrounded by more of itself, so its
    ''' neighbours are nearly as high and it survives untouched.
    '''
    ''' THE OUTLAND IS EXEMPT. It is legitimately enormous - cliffs to 164 m -
    ''' and clamping it would be inventing terrain, not removing an artefact.
    '''
    ''' Replaced with the neighbourhood maximum rather than a constant ceiling,
    ''' so what is left is a height the surface actually reaches somewhere
    ''' rather than a number this code chose. Counted and logged either way: a
    ''' silent repair is how bad data becomes believed data.
    ''' </summary>
    Private Sub despike_top()
        Dim fixed_n = 0
        Dim worst = 0.0F
        For r = 1 To SIZE - 2
            Dim row = r * SIZE
            For c = 1 To SIZE - 2
                Dim i = row + c
                If (kind_b(i) And OUTLAND_BIT) <> 0 Then Continue For
                Dim h = top_m(i) - floor_m(i)
                If h <= NEEDLE_LOOK_H Then Continue For

                ' The tallest of the eight around it.
                Dim nb = Single.MinValue
                For dr = -1 To 1
                    For dc = -1 To 1
                        If dr = 0 AndAlso dc = 0 Then Continue For
                        Dim j = i + dr * SIZE + dc
                        If top_m(j) > nb Then nb = top_m(j)
                    Next
                Next

                If top_m(i) - nb <= NEEDLE_OVER_NB Then Continue For
                worst = Math.Max(worst, top_m(i) - nb)
                top_m(i) = nb
                fixed_n += 1
            Next
        Next
        If fixed_n > 0 Then
            LogThis("flight bake: pulled down {0} needle texel(s), worst stood {1:0.0} m over its neighbours",
                    fixed_n, worst)
        End If
    End Sub

    ''' <summary>
    ''' The depth buffer as it stands with the models in and the trees not yet,
    ''' turned into one bit a texel: is something solid standing here.
    '''
    ''' Compared against the FLOOR, not against the top map, which does not
    ''' exist yet at this point in the pass - and against the same
    ''' OBSTACLE_MIN_H that report_coverage and the mask PNG use, so "blocked"
    ''' means one thing across the whole bake.
    '''
    ''' Held in its own array rather than ORed straight into kind_b because
    ''' read_kinds has not run yet and overwrites every byte of it when it
    ''' does. apply_solid puts the bit in afterwards.
    ''' </summary>
    Private Sub read_solid()
        Dim d(SIZE * SIZE - 1) As Single
        GL.GetTextureImage(depth_tex.texture_id, 0,
                           OpenGL4.PixelFormat.DepthComponent, PixelType.Float,
                           d.Length * 4, d)

        ' THE MODELS' OWN KINDS, read at the one moment they are alone in the
        ' buffer. draw_models has run and draw_trees has NOT, so every byte
        ' here belongs to a model - which is what lets the loop below ask
        ' 'is the solid thing at this texel crushable' and get a truthful
        ' answer. Ten lines later the trees overwrite it and the question
        ' becomes unanswerable.
        Dim mk(SIZE * SIZE - 1) As Byte
        GL.GetTextureImage(kind_tex.texture_id, 0,
                           OpenGL4.PixelFormat.Red, PixelType.UnsignedByte,
                           mk.Length, mk)

        If solid_b Is Nothing Then ReDim solid_b(SIZE * SIZE - 1)
        ' Every texel is assigned, not just the set ones: this array outlives a
        ' map load, and a rebake on a second map would otherwise inherit the
        ' first map's bits wherever the new one has nothing standing.
        Dim n = 0, skipped = 0
        For r = 0 To SIZE - 1
            Dim src = (SIZE - 1 - r) * SIZE
            Dim dst_row = r * SIZE
            For c = 0 To SIZE - 1
                Dim i = dst_row + c
                ' A MODEL THAT KEYS AS TREE DOES NOT MAKE ITS OWN TEXEL SOLID.
                '
                ' Measured by Tank AI work on the bake_version 4 bake: the
                ' grapevine re-key put 100% of the vineyard's 13,044 texels
                ' under kind TREE, and 4,903 of them - 37.6% - STILL blocked a
                ' hull, because crushable is 'tree AND NOT solid' and the
                ' trellis is a model, so it set the solid bit for itself.
                ' Re-keying moved it under the tree rule; the tree rule then
                ' asked the one question it answers wrongly.
                '
                ' This does NOT relax 'tree AND solid' generally, and must not:
                ' that pairing is what keeps a tank out of a wall or a rock
                ' standing under a canopy - 2,581 cells on monastery. Those
                ' survive untouched, because the solid there is contributed by
                ' the ROCK, which keys rock at this moment, and the canopy that
                ' makes the texel read tree is a SpeedTree that has not been
                ' drawn yet. Only a model that is ITSELF crushable is exempted,
                ' and only from its OWN bit.
                If (mk(src + c) And KIND_MASK) = KIND_TREE Then
                    solid_b(i) = 0
                    skipped += 1
                ElseIf (eye_y - d(src + c) * far_d) - floor_m(i) > OBSTACLE_MIN_H Then
                    solid_b(i) = 1
                    n += 1
                Else
                    solid_b(i) = 0
                End If
            Next
        Next
        solid_cells = n
        If skipped > 0 Then
            LogThis("flight bake: {0} texel(s) left NOT solid because the model " &
                    "standing there is itself crushable - a trellis, not a wall", skipped)
        End If
    End Sub

    ''' <summary>
    ''' Put the solid bit into the key byte, and say how many texels it just
    ''' rescued from being read as crushable foliage.
    '''
    ''' THE SECOND NUMBER IS THE POINT. Solid-and-keyed-tree is the canopy-over-
    ''' rock population - documented at 2,581 cells on monastery when it was
    ''' measured offline - and it is the only figure here that says whether
    ''' this bit is earning its place on a given map.
    ''' </summary>
    Private Sub apply_solid()
        If solid_b Is Nothing Then Return

        Dim under_canopy = 0
        For i = 0 To SIZE * SIZE - 1
            If solid_b(i) = 0 Then Continue For
            kind_b(i) = CByte(kind_b(i) Or SOLID_BIT)
            If (kind_b(i) And KIND_MASK) = KIND_TREE Then under_canopy += 1
        Next

        LogThis("flight bake: solid bit set on {0} texel(s) ({1:0.0}% of the map), " &
                "{2} of them keyed tree - solid ground under a canopy",
                solid_cells, 100.0 * solid_cells / (SIZE * SIZE), under_canopy)

        ' 67 MB of scratch, and the bit it carried is now in kind_b.
        Erase solid_b
    End Sub

    ''' <summary>
    ''' The id channel, flipped to the same row order as everything else.
    '''
    ''' FLIPPED IN PLACE, a row at a time. The other two readbacks copy out of
    ''' a scratch array into their destination, which for this one would mean
    ''' 268 MB of source and 268 MB of destination live at once for the sake of
    ''' turning the map upside down. Swapping row r with row SIZE-1-r needs one
    ''' 32 KB row of scratch and says the same thing.
    ''' </summary>
    Private Sub read_ids()
        If id_u Is Nothing Then ReDim id_u(SIZE * SIZE - 1)
        GL.GetTextureImage(id_tex.texture_id, 0,
                           OpenGL4.PixelFormat.RedInteger, PixelType.UnsignedInt,
                           id_u.Length * 4, id_u)

        Dim row(SIZE - 1) As UInteger
        For r = 0 To SIZE \ 2 - 1
            Dim a = r * SIZE
            Dim b = (SIZE - 1 - r) * SIZE
            Array.Copy(id_u, a, row, 0, SIZE)
            Array.Copy(id_u, b, id_u, a, SIZE)
            Array.Copy(row, 0, id_u, b, SIZE)
        Next

        id_nonzero = 0
        id_max_seen = 0UI
        For i = 0 To id_u.Length - 1
            Dim v = id_u(i)
            If v <> 0UI Then
                id_nonzero += 1
                If v > id_max_seen Then id_max_seen = v
            End If
        Next

        ' The ceiling this layer would need to wrap at if it were 16 bit - the
        ' evidence for or against narrowing the format. See create_target.
        LogThis("flight bake: ids on {0} texel(s) ({1:0.0}% of the map), highest id {2}, " &
                "space is {3} model + {4} tree placement(s)",
                id_nonzero, 100.0 * id_nonzero / (SIZE * SIZE), id_max_seen,
                scene.static_models.numModelInstances,
                If(scene.TREES_LOADED, scene.trees.total_instances, 0))
    End Sub

    Private Sub read_kinds()
        Dim d(SIZE * SIZE - 1) As Byte
        GL.GetTextureImage(kind_tex.texture_id, 0,
                           OpenGL4.PixelFormat.Red, PixelType.UnsignedByte,
                           d.Length, d)
        For r = 0 To SIZE - 1
            Array.Copy(d, (SIZE - 1 - r) * SIZE, kind_b, r * SIZE, SIZE)
        Next
    End Sub

    ''' <param name="into_top">False reads the floor, and FIXES THE OFFSET for
    ''' both maps. It has to run first, which it does - the floor pass is the
    ''' first of the two in Bake - because nothing can be quantised until the
    ''' zero is known.</param>
    Private Sub read_heights(into_top As Boolean)
        Dim d(SIZE * SIZE - 1) As Single
        GL.GetTextureImage(depth_tex.texture_id, 0,
                           OpenGL4.PixelFormat.DepthComponent, PixelType.Float,
                           d.Length * 4, d)

        If Not into_top Then
            ' THE MINIMUM IS FLIP INVARIANT, so it can be taken from the raw
            ' read before the rows are turned over - which is what lets the
            ' whole conversion happen without a second float array the size of
            ' the map.
            Dim lo = Single.MaxValue
            For i = 0 To d.Length - 1
                Dim y = eye_y - d(i) * far_d
                If y < lo Then lo = y
            Next
            If lo = Single.MaxValue Then lo = 0.0F
            h_offset = CSng(Math.Floor(lo) - 10.0F)
        End If

        ' GL hands back row 0 = bottom = wz_min. Flip on the way out so row 0 is
        ' the wz_max edge - then the array reads like the picture you would draw
        ' of it, north up, and nobody downstream has to remember a convention.
        Dim dst = If(into_top, top_u, floor_u)
        For r = 0 To SIZE - 1
            Dim src = (SIZE - 1 - r) * SIZE
            Dim dst_row = r * SIZE
            For c = 0 To SIZE - 1
                dst(dst_row + c) = quantise(eye_y - d(src + c) * far_d)
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
        If Not scene.MODELS_LOADED OrElse Not DONT_BLOCK_MODELS Then
            LogThis("flight bake: models NOT baked - loaded={0} enabled={1}",
                    scene.MODELS_LOADED, DONT_BLOCK_MODELS)
            Return
        End If

        ' The count comes from the SUN SHADOW cull, which was run for the sun's
        ' frustum rather than for this top-down ortho. If it is 0 here the bake
        ' has no buildings in it at all and Path Studio cannot see them.
        LogThis("flight bake: models drawn from the shadow indirect buffer, {0} draw(s)",
                scene.static_models.indirectShadowMappingDrawCount)

        sunDepthModelShader.Use()
        GL.UniformMatrix4(sunDepthModelShader("sunViewProj"), False, vp)

        scene.static_models.allMapModels.Bind()
        scene.static_models.indirect_shadow_mapping.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                                     IntPtr.Zero, scene.static_models.indirectShadowMappingDrawCount, 0)

        ' The same models again as LINES. Thin vertical geometry - fences,
        ' railings, posts, wire - has no area from straight above, so the fill
        ' pass above writes nothing for it, at any resolution; the navigator
        ' then flies straight through a fence a tank cannot. Drawn as lines,
        ' every edge rasterises at least one texel along its length, at the
        ' edge's own depth, so the top map carries the fence at the fence's
        ' height. Costs one more draw of the same buffers.
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                                     IntPtr.Zero, scene.static_models.indirectShadowMappingDrawCount, 0)
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

        sunDepthModelShader.StopUse()
    End Sub

    Private Sub draw_trees(vp As Matrix4)
        If Not scene.TREES_LOADED OrElse Not DONT_BLOCK_TREES Then Return
        scene.trees.sun_depth_pass(vp, id_base:=tree_id_base())
    End Sub

    ''' <summary>
    ''' Where the trees start in the ONE id space the bake writes.
    '''
    ''' Models take 1 .. numModelInstances and trees the block above them, so a
    ''' texel carries a single number and a reader needs no second layer to say
    ''' which kind of thing it indexes. The join goes in the meta as
    ''' id_tree_base; the sidecar names both sides.
    ''' </summary>
    Private Function tree_id_base() As UInteger
        Return CUInt(scene.static_models.numModelInstances) + 1UI
    End Function

    ''' <summary>
    ''' Stamp the trunk bit wherever a tree's base stands.
    '''
    ''' DEPTH OFF AND LOGIC OP OR, which is the whole trick. A trunk is
    ''' directly underneath its own canopy and loses every depth test to it, so
    ''' no ordinary pass can record one - the leaves are always nearer the sky.
    ''' ORing a bit into the key sidesteps ordering completely: the bit says a
    ''' trunk is here, and says nothing about what is above it, which is
    ''' exactly the question a vehicle asks.
    '''
    ''' Logic op, not blending - the two are mutually exclusive in GL and OR is
    ''' what accumulates a flag. The depth MASK goes off as well as the test:
    ''' with the test off a fragment would otherwise still write depth and
    ''' quietly flatten the top map to the trunk tops.
    ''' </summary>
    Private Sub draw_trunks(vp As Matrix4)
        If Not scene.TREES_LOADED OrElse Not DONT_BLOCK_TREES Then Return

        GL_PUSH_GROUP("flight_bake_trunks")
        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Enable(EnableCap.ColorLogicOp)
        GL.LogicOp(LogicOp.Or)

        ' THE ID BUFFER IS MASKED OFF FOR THIS PASS. A logic op applies to every
        ' enabled draw buffer, integer ones included, so without this the OR
        ' that accumulates the trunk bit would also OR ids together and produce
        ' numbers naming no object at all - 5 OR 9 = 13, which is some third
        ' tree. Index 2 is this buffer's position in the draw-buffer array named
        ' in create_target, not its attachment number.
        GL.ColorMask(2, False, False, False, False)

        scene.trees.sun_depth_pass(vp, trunk_only:=True, id_base:=tree_id_base())

        GL.ColorMask(2, True, True, True, True)
        GL.Disable(EnableCap.ColorLogicOp)
        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)
        GL_POP_GROUP()
    End Sub

    Private Sub create_target()
        depth_tex = GLTexture.Create(TextureTarget.Texture2D, "FlightBakeDepth")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        depth_tex.Storage2D(1, DirectCast(InternalFormat.DepthComponent32f, SizedInternalFormat), SIZE, SIZE)

        ' THE KEY CHANNEL. One byte a texel saying what the topmost thing is,
        ' written by the same depth pass that decides which thing that is - so
        ' there is no second pass and no sorting, and the two answers cannot
        ' disagree about which surface won.
        kind_tex = GLTexture.Create(TextureTarget.Texture2D, "FlightBakeKind")
        kind_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        kind_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        kind_tex.Storage2D(1, DirectCast(InternalFormat.R8, SizedInternalFormat), SIZE, SIZE)

        ' THE ID CHANNEL. Which object, where the key says only which kind.
        '
        ' R32UI AND NOT R16UI, which is 134 MB rather than 268 and was the size
        ' the planners were told to expect. The id space is model placements
        ' plus tree placements, and neither is bounded by anything: a map that
        ' crossed 65,535 would wrap silently and hand back ids naming the wrong
        ' objects, which is exactly the failure this file keeps warning about.
        ' The count is logged at every bake, so if it turns out no map comes
        ' near the ceiling this can be narrowed on evidence rather than hope.
        id_tex = GLTexture.Create(TextureTarget.Texture2D, "FlightBakeId")
        id_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        id_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        id_tex.Storage2D(1, DirectCast(InternalFormat.R32ui, SizedInternalFormat), SIZE, SIZE)

        fbo = GLFramebuffer.Create("FlightBakeFBO")
        fbo.Texture(FramebufferAttachment.DepthAttachment, depth_tex, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment1, kind_tex, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment2, id_tex, 0)

        ' LOCATION 1, NOT 0. The depth shaders have written the Moment Shadow
        ' Map's four moments at location 0 since the sun bake needed them, and
        ' taking that location for the key would have replaced the shadow data
        ' with a byte. The draw-buffer array is indexed BY OUTPUT LOCATION, so
        ' naming None first and ColorAttachment1 second sends the moments
        ' nowhere and the key to the texture above.
        GL.NamedFramebufferDrawBuffers(fbo.fbo_id, 3,
            {DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1,
             DrawBuffersEnum.ColorAttachment2})
        GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)

        If Not fbo.IsComplete Then
            LogThis("flight bake: FBO incomplete at {0}x{0}", SIZE)
        End If
    End Sub

    ''' <summary>Where the saved bake lives, and the stem its four files share.
    ''' One definition, because export writes them and try_load reads them, and a
    ''' cache that writes one path and looks in another never hits and never says
    ''' why.</summary>
    Private Shared Function bake_stem() As String
        Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra", "flight")
        IO.Directory.CreateDirectory(dir)
        Return IO.Path.Combine(dir, MAP_NAME_NO_PATH)
    End Function

    ''' <summary>The game version the assets came from - the res_mods folder the
    ''' game itself is using, "2.4.0.0" today. The map does not change unless a
    ''' major release happens, and this is that release, named.</summary>
    Private Shared Function game_version() As String
        Try
            Return IO.Path.GetFileName(ResMgr.RES_MODS_PATH.TrimEnd("\"c, "/"c))
        Catch
            Return ""
        End Try
    End Function

    Private Sub export()
        Try
            Dim stem = bake_stem()
            Dim dir = IO.Path.GetDirectoryName(stem)

            write_top_rgba(stem & "_top.rgba")
            write_floor_r16(stem & "_floor.r16")
            write_ids_u32(stem & "_ids.u32")
            write_id_names(stem & "_ids.csv")
            write_mask_png(stem & "_mask.png")
            write_meta(stem & "_meta.txt")

            ' 268 MB, and nothing in this process reads it - see id_u.
            Erase id_u

            LogThis("flight bake: exported {0}_top.rgba / _floor.r16 / _ids.u32 / _ids.csv / _mask.png / _meta.txt to {1}",
                    MAP_NAME_NO_PATH, dir)
        Catch ex As Exception
            LogThis("flight bake: export FAILED: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' The offset the 16-bit heights are measured from: a whole number below
    ''' the lowest point on the map.
    '''
    ''' WRITTEN INTO THE META rather than agreed as a constant. A map with a
    ''' deeper pit than this one would otherwise encode negative and clamp
    ''' silently at zero - a whole quarry reading as flat ground.
    ''' </summary>
    ''' <summary>
    ''' The arena's playable box, for readers that need to know where the map
    ''' ends - agreed with Path Studio and previously only inferable.
    '''
    ''' NOT THE BAKE BOX. wx_min..wz_max is the terrain CHUNK footprint; this is
    ''' the box scripts/arena_defs declares, and the two are independently
    ''' derived. The arena has fallen inside the bake on every map looked at,
    ''' which is an observation and not a guarantee - so the containment is
    ''' CHECKED here and logged when it fails, rather than assumed by anyone
    ''' downstream.
    '''
    ''' Nothing is written when the arena was not read: MAP_BB stays zero when
    ''' the XML is missing, and four zeros presented as a playable area would be
    ''' worse than no keys at all - a reader would clip the whole map away.
    ''' </summary>
    Private Sub append_arena(sb As Text.StringBuilder, inv As Globalization.CultureInfo)
        Dim x0 = Math.Min(MAP_BB_BL.X, MAP_BB_UR.X)
        Dim x1 = Math.Max(MAP_BB_BL.X, MAP_BB_UR.X)
        Dim z0 = Math.Min(MAP_BB_BL.Y, MAP_BB_UR.Y)
        Dim z1 = Math.Max(MAP_BB_BL.Y, MAP_BB_UR.Y)

        If x1 - x0 <= 0.0F OrElse z1 - z0 <= 0.0F Then
            LogThis("flight bake: no arena box for {0} - arena_* keys not written",
                    MAP_NAME_NO_PATH)
            Return
        End If

        sb.AppendLine(String.Format(inv, "arena_x0={0:0.000}", x0))
        sb.AppendLine(String.Format(inv, "arena_x1={0:0.000}", x1))
        sb.AppendLine(String.Format(inv, "arena_z0={0:0.000}", z0))
        sb.AppendLine(String.Format(inv, "arena_z1={0:0.000}", z1))

        If x0 < wx_min OrElse x1 > wx_max OrElse z0 < wz_min OrElse z1 > wz_max Then
            LogThis("flight bake: ARENA BOX IS NOT INSIDE THE BAKE BOX - arena x {0:0}..{1:0} z {2:0}..{3:0}, " &
                    "bake x {4:0}..{5:0} z {6:0}..{7:0}. Either this map's arena really does " &
                    "overhang the terrain chunks, or the two are in different X frames.",
                    x0, x1, z0, z1, wx_min, wx_max, wz_min, wz_max)
        End If
    End Sub

    Private Function height_offset() As Single
        ' Fixed when the floor was read - see read_heights. It used to be
        ' rescanned here over 67 million texels to answer a question already
        ' settled.
        Return h_offset
    End Function

    Private Function encode16(y As Single, off As Single) As Integer
        Dim v = CInt(Math.Round((y - off) * HEIGHT_SCALE))
        Return Math.Min(Math.Max(v, 0), 65535)
    End Function

    ''' <summary>
    ''' The top map: the kind in R and a 16-bit height across G and B.
    '''
    ''' THE LOW BYTE IS IN B, NOT ALPHA. The spec came over with it in alpha
    ''' and B spare; alpha is the one channel something downstream might
    ''' premultiply, blend or drop on the way through an image tool, and half
    ''' a height silently becoming 255 is a hill. B was spare anyway, so this
    ''' costs nothing and removes the whole class of accident. Alpha is left
    ''' at 255 so the file also opens as a sane picture.
    ''' </summary>
    Private Sub write_top_rgba(path As String)
        Dim b(SIZE * SIZE * 4 - 1) As Byte
        For i = 0 To SIZE * SIZE - 1
            ' STRAIGHT OUT OF STORAGE. The heights are already held in exactly
            ' this encoding, so decoding them to metres and re-encoding would
            ' be a round trip to the same number.
            Dim h = CInt(top_u(i))
            Dim o = i * 4
            b(o) = kind_b(i)
            b(o + 1) = CByte((h >> 8) And &HFF)
            b(o + 2) = CByte(h And &HFF)
            b(o + 3) = 255
        Next
        IO.File.WriteAllBytes(path, b)
    End Sub

    ''' <summary>The floor, same encoding, no kind - the ground is kind 0 by
    ''' definition and a byte a texel saying so is 67 MB of nothing.</summary>
    Private Sub write_floor_r16(path As String)
        Dim b(SIZE * SIZE * 2 - 1) As Byte
        For i = 0 To SIZE * SIZE - 1
            Dim h = CInt(floor_u(i))
            b(i * 2) = CByte(h And &HFF)
            b(i * 2 + 1) = CByte((h >> 8) And &HFF)
        Next
        IO.File.WriteAllBytes(path, b)
    End Sub

    ''' <summary>
    ''' The id layer: uint32 little endian, one per texel, row major, same row
    ''' order and same world mapping as the other two.
    '''
    ''' WRITTEN A ROW AT A TIME rather than through one 268 MB byte array. The
    ''' array is already little-endian uint32 in memory, so the file IS the
    ''' array and the only work is moving it - doing that through a full-size
    ''' copy would double the peak for no gain. 32 KB a row, 8192 writes,
    ''' behind a 1 MB stream buffer.
    ''' </summary>
    Private Sub write_ids_u32(path As String)
        If id_u Is Nothing Then Return
        Dim row(SIZE * 4 - 1) As Byte
        Using fs = New IO.FileStream(path, IO.FileMode.Create, IO.FileAccess.Write,
                                     IO.FileShare.None, 1 << 20)
            For r = 0 To SIZE - 1
                System.Buffer.BlockCopy(id_u, r * SIZE * 4, row, 0, row.Length)
                fs.Write(row, 0, row.Length)
            Next
        End Using
    End Sub

    ''' <summary>
    ''' What each id IS: first_id, count, source, name.
    '''
    ''' RANGES, not one row per object. Placements come in runs that share a
    ''' model - a hedge is forty instances of one .primitives file - so run-
    ''' length encoding turns a hundred thousand rows into a few hundred
    ''' without losing a single id. An id belongs to the row with the greatest
    ''' first_id not above it.
    '''
    ''' NO KIND COLUMN, deliberately. The kind of what stands at a texel is in
    ''' the key channel, written by the same fragment that wrote the id, and a
    ''' second copy here derived from a different string could disagree with it.
    ''' `source` is not a classification - it says which half of the id space
    ''' the row is in, which is structural and cannot drift.
    '''
    ''' Model names come from PICK_DICTIONARY, which MapLoader already fills
    ''' keyed by the very instance index the model shader turns into an id -
    ''' the picker has been showing these strings on click all along.
    ''' </summary>
    Private Sub write_id_names(path As String)
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("# nuTerra flight bake - id -> object")
        sb.AppendLine("# id 0 is nothing: bare terrain, or nothing rasterised.")
        sb.AppendLine("# a row covers ids first_id .. first_id + count - 1")
        sb.AppendLine("first_id,count,source,name")

        Dim rows = 0
        Dim n_models = scene.static_models.numModelInstances
        Dim i = 0
        While i < n_models
            Dim nm = pick_name(i)
            Dim j = i + 1
            While j < n_models AndAlso pick_name(j) = nm
                j += 1
            End While
            sb.AppendLine(String.Format("{0},{1},model,{2}", i + 1, j - i, nm.Replace(","c, "_"c)))
            rows += 1
            i = j
        End While

        If scene.TREES_LOADED Then
            For Each b In scene.trees.instance_blocks()
                sb.AppendLine(String.Format("{0},{1},tree,{2}",
                                            tree_id_base() + CUInt(b.first), b.count,
                                            If(b.name, "?").Replace(","c, "_"c)))
                rows += 1
            Next
        End If

        IO.File.WriteAllText(path, sb.ToString())
        LogThis("flight bake: {0} id range(s) named in {1}", rows, IO.Path.GetFileName(path))
    End Sub

    ''' <summary>The model directory PICK_DICTIONARY holds for an instance, or
    ''' "?" - a gap is not fatal, it just leaves that id unnamed.</summary>
    Private Function pick_name(instance As Integer) As String
        Dim nm As String = Nothing
        If scene.PICK_DICTIONARY.TryGetValue(CUInt(instance), nm) AndAlso nm IsNot Nothing Then
            Return nm
        End If
        Return "?"
    End Function

    Private Shared Sub write_r32(path As String, a() As Single)
        Dim b(a.Length * 4 - 1) As Byte
        System.Buffer.BlockCopy(a, 0, b, 0, b.Length)
        IO.File.WriteAllBytes(path, b)
    End Sub

    Private Sub write_mask_png(path As String)
        ' Block-ANY down to MASK_SIZE: a block is an obstacle if one texel in
        ' it is, so a fence the line pass drew one texel wide still shows.
        Dim f = Math.Max(1, SIZE \ MASK_SIZE)
        Dim n = SIZE \ f
        Dim px(n * n * 4 - 1) As Byte
        For r = 0 To n - 1
            For c = 0 To n - 1
                Dim v As Byte = 0
                For rr = r * f To r * f + f - 1
                    Dim base = rr * SIZE + c * f
                    For cc = 0 To f - 1
                        If top_m(base + cc) - floor_m(base + cc) > OBSTACLE_MIN_H Then
                            v = 255
                            Exit For
                        End If
                    Next
                    If v = 255 Then Exit For
                Next
                Dim o = (r * n + c) * 4
                px(o + 0) = v
                px(o + 1) = v
                px(o + 2) = v
                px(o + 3) = 255
            Next
        Next

        ' row 0 already holds the wz_max edge, and GDI+ row 0 is the top of the
        ' image, so this lands north up with no further flipping.
        Using bmp As New Drawing.Bitmap(n, n, Drawing.Imaging.PixelFormat.Format32bppArgb)
            Dim bd = bmp.LockBits(New Drawing.Rectangle(0, 0, n, n),
                                  Drawing.Imaging.ImageLockMode.WriteOnly,
                                  Drawing.Imaging.PixelFormat.Format32bppArgb)
            Marshal.Copy(px, 0, bd.Scan0, px.Length)
            bmp.UnlockBits(bd)
            bmp.Save(path, Drawing.Imaging.ImageFormat.Png)
        End Using
    End Sub

    ''' <summary>
    ''' When THESE BAKE BYTES were produced - not when the meta file was last
    ''' touched. Carried across a meta-only refresh so it keeps meaning what it
    ''' says: a bake loaded from disk and given new meta keys is still the bake
    ''' that was made at this time.
    ''' </summary>
    Private meta_written As String = ""

    ''' <summary>The commit the running exe was built from, found by walking up
    ''' from the exe for the .git that defines the checkout - the same walk the
    ''' window's owner tag uses. Empty when there is no repo, which is a normal
    ''' state for a shipped build rather than an error.</summary>
    Private Shared Function git_commit() As String
        Try
            Dim d = New IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            While d IsNot Nothing
                Dim g = IO.Path.Combine(d.FullName, ".git")
                Dim dir = g
                If IO.File.Exists(g) Then
                    ' A worktree: .git is a file saying where the real one is.
                    Dim first = IO.File.ReadAllText(g).Trim()
                    If first.StartsWith("gitdir:") Then dir = first.Substring(7).Trim()
                End If
                If IO.Directory.Exists(dir) Then
                    Dim head = IO.Path.Combine(dir, "HEAD")
                    If IO.File.Exists(head) Then
                        Dim h = IO.File.ReadAllText(head).Trim()
                        If h.StartsWith("ref:") Then
                            Dim rf = IO.Path.Combine(dir, h.Substring(4).Trim().Replace("/"c, IO.Path.DirectorySeparatorChar))
                            If IO.File.Exists(rf) Then Return IO.File.ReadAllText(rf).Trim().Substring(0, 8)
                        ElseIf h.Length >= 8 Then
                            Return h.Substring(0, 8)
                        End If
                    End If
                    Return ""
                End If
                d = d.Parent
            End While
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' Write the meta ONLY IF IT WOULD DIFFER.
    '''
    ''' Path Studio stamps the bake files every two seconds and reloads on a
    ''' change that holds still. Rewriting an identical meta every launch would
    ''' move its timestamp and cost them a reload that carries no new
    ''' information - so an unchanged meta is left alone, and "reloads on a real
    ''' change only" stays true.
    '''
    ''' This is also what lets a meta-only change - a new key, a palette edit -
    ''' reach an already-saved bake without rebuilding 400 MB to deliver it.
    ''' </summary>
    Private Sub write_meta(path As String)
        Dim want = meta_text()
        Try
            If IO.File.Exists(path) AndAlso IO.File.ReadAllText(path) = want Then Return
        Catch
            ' Unreadable: fall through and write it.
        End Try
        IO.File.WriteAllText(path, want)
    End Sub

    Private Function meta_text() As String
        Dim inv = Globalization.CultureInfo.InvariantCulture
        Dim sb As New Text.StringBuilder

        sb.AppendLine("# nuTerra flight bake")
        sb.AppendLine("map=" & MAP_NAME_NO_PATH)
        sb.AppendLine("bake_version=" & BAKE_VERSION)
        sb.AppendLine("game_version=" & game_version())

        ' Provenance, asked for by Path Studio so its status line can say where a
        ' bake came from. `written` is when the BYTES were baked, not when this
        ' file was last touched - see meta_written.
        ' THE FULL PATH, not the file name. Three checkouts build this app on this
        ' machine and two of them share one temp folder, so "which exe wrote this
        ' bake" is a real question with a useful answer. GetEntryAssembly gives
        ' nuTerra.DLL on .NET, which answers it for nobody.
        Try
            Dim exe = Environment.ProcessPath
            If String.IsNullOrEmpty(exe) Then exe = Reflection.Assembly.GetEntryAssembly().Location
            sb.AppendLine("exe=" & exe)
            sb.AppendLine("built=" & IO.File.GetLastWriteTime(exe).ToString("s"))
        Catch
        End Try
        If git_commit() <> "" Then sb.AppendLine("commit=" & git_commit())
        If meta_written = "" Then meta_written = Date.Now.ToString("s")
        sb.AppendLine("written=" & meta_written)
        sb.AppendLine("width=" & SIZE)
        sb.AppendLine("height=" & SIZE)
        sb.AppendLine(String.Format(inv, "wx_min={0:0.000}", wx_min))
        sb.AppendLine(String.Format(inv, "wx_max={0:0.000}", wx_max))
        sb.AppendLine(String.Format(inv, "wz_min={0:0.000}", wz_min))
        sb.AppendLine(String.Format(inv, "wz_max={0:0.000}", wz_max))
        sb.AppendLine(String.Format(inv, "empty={0:0.000}", eye_y - far_d))
        sb.AppendLine(String.Format(inv, "obstacle_min_h={0:0.000}", OBSTACLE_MIN_H))
        sb.AppendLine("format=rgba8")
        sb.AppendLine(String.Format(inv, "height_scale={0:0}", HEIGHT_SCALE))
        sb.AppendLine(String.Format(inv, "height_offset={0:0}", height_offset()))
        For k = 0 To KIND_NAMES.Length - 1
            sb.AppendLine(String.Format("kind_{0}={1}", k, KIND_NAMES(k)))
        Next

        ' The colour each kind renders in. One table, written here, read by
        ' everything that draws the bake - so the legend cannot drift between
        ' two views of the same map. No key for kind 0: terrain is the ground.
        For k = 1 To KIND_RGB.Length - 1
            If KIND_RGB(k) IsNot Nothing Then
                sb.AppendLine(String.Format("kind_{0}_rgb={1},{2},{3}", k,
                                            KIND_RGB(k)(0), KIND_RGB(k)(1), KIND_RGB(k)(2)))
            End If
        Next
        sb.AppendLine(String.Format(inv, "kind_mask={0}", KIND_MASK))
        sb.AppendLine(String.Format(inv, "outland_bit={0}", OUTLAND_BIT))
        sb.AppendLine(String.Format(inv, "trunk_bit={0}", TRUNK_BIT))
        sb.AppendLine(String.Format(inv, "solid_bit={0}", SOLID_BIT))
        sb.AppendLine(String.Format(inv, "trunk_radius={0:0.00}", TRUNK_RADIUS))

        ' The id layer and where its two halves join. Written on the loaded path
        ' too: both counts come from the model and tree tables the map load
        ' built, not from the bake, so they are the same numbers either way.
        sb.AppendLine("id_layer=" & MAP_NAME_NO_PATH & "_ids.u32")
        sb.AppendLine("id_names=" & MAP_NAME_NO_PATH & "_ids.csv")
        sb.AppendLine("id_format=u32")
        sb.AppendLine(String.Format(inv, "id_model_count={0}",
                                    scene.static_models.numModelInstances))
        sb.AppendLine(String.Format(inv, "id_tree_base={0}", tree_id_base()))
        sb.AppendLine(String.Format(inv, "id_tree_count={0}",
                                    If(scene.TREES_LOADED, scene.trees.total_instances, 0)))

        append_arena(sb, inv)
        sb.AppendLine("#")
        sb.AppendLine("# R is FOUR fields: kind = R & kind_mask, outland = R & outland_bit,")
        sb.AppendLine("# solid = R & solid_bit, trunk = R & trunk_bit. outland is the ring outside the")
        sb.AppendLine("# playable area - real geometry, but nothing should ever route into")
        sb.AppendLine("# it; almost everything standing very tall on the map is out there.")
        sb.AppendLine("# trunk means a tree trunk stands at this texel whatever is above")
        sb.AppendLine("# it - the canopy still owns the height. A ground vehicle should")
        sb.AppendLine("# treat the trunk bit as solid and tree canopy as passable; a")
        sb.AppendLine("# camera should do the opposite and use the height.")
        sb.AppendLine("# solid means terrain-borne geometry over obstacle_min_h stands here,")
        sb.AppendLine("# measured with the trees left out. It is the answer to canopy over")
        sb.AppendLine("# rock: a texel can key tree AND be solid, and a ground vehicle that")
        sb.AppendLine("# crushes foliage must test 'kind = tree AND NOT solid', never kind")
        sb.AppendLine("# alone. The height at such a texel is still the canopy's.")
        sb.AppendLine("#")
        sb.AppendLine("# ids.u32  uint32 little endian, one per texel, same rows and mapping.")
        sb.AppendLine("#          WHICH object is on top, biased by one - 0 is nothing.")
        sb.AppendLine("#          1 .. id_model_count are model placements; id_tree_base and")
        sb.AppendLine("#          up are tree placements. ids.csv names every range.")
        sb.AppendLine("#          The id and the key at a texel are written by the same")
        sb.AppendLine("#          fragment, so they always describe the same surface.")
        sb.AppendLine("#")
        sb.AppendLine("# top.rgba  R = kind key, G = height high byte, B = height low byte,")
        sb.AppendLine("#           A = 255. height = height_offset + h16 / height_scale")
        sb.AppendLine("# floor.r16 uint16 little endian, same encoding, terrain alone")
        sb.AppendLine("# both row major, width*height, row 0 = the wz_max edge")
        sb.AppendLine("# a cell at or below 'empty' means nothing rasterised there")
        sb.AppendLine("#")
        sb.AppendLine("# row 0 is the wz_max edge, rows increase toward wz_min")
        sb.AppendLine("# col 0 is the wx_min edge, cols increase toward wx_max")
        sb.AppendLine("# world_x = wx_min + (col + 0.5) * (wx_max - wx_min) / width")
        sb.AppendLine("# world_z = wz_max - (row + 0.5) * (wz_max - wz_min) / height")

        Return sb.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        depth_tex?.Dispose()
        kind_tex?.Dispose()
        id_tex?.Dispose()
        fbo?.Dispose()
        GC.SuppressFinalize(Me)
    End Sub
End Class

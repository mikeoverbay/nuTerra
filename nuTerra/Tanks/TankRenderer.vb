Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' The tank module's face to the rest of nuTerra. The core touches it in two
''' places only: MapScene owns one (construct / dispose) and draw_scene calls
''' Draw() once, after the static models. Everything else - the vehicle XML,
''' the primitives, the visuals, the textures, the shader - lives under
''' nuTerra\Tanks and shaders\Tanks.
'''
''' Loading is lazy, on the first Draw after the map is up, so no load-order
''' hook is needed in the map loader.
''' </summary>
Public Class MapTanks
    Implements IDisposable

    ''' <summary>Master switch. Off draws nothing and loads nothing.</summary>
    Public Shared Enabled As Boolean = True

    ''' <summary>
    ''' nuTerra's world is the game's mirrored in X for display. On, the tank
    ''' takes the same mirror so it faces the same way as the buildings around
    ''' it. Left settable because it is a verification step, not a known.
    ''' </summary>
    Public Shared MirrorX As Boolean = True

    ''' <summary>Which normal encoding the .vert unpacks: 1 = 8/8/8 unsigned bytes (what the VB exporter uses for BPVT streams), 0 = 11/10/10 signed.</summary>
    Public Shared NormalMode As Integer = 1

    ''' <summary>
    ''' Skinned parts - the gun and the chassis, the streams with bone bytes -
    ''' are stored with Z reversed relative to the hull and turret. Both
    ''' exporters carry this rule: the VB loader negates Z for chassis and gun
    ''' (ModTankLoader.vb, unpackNormal_8_8_8, xmlget_mode 1 and 4) and the
    ''' Python viewer flips Z for unskinned parts only. First seen here as the
    ''' gun drawn pointing out over the spade. Verified on the still that
    ''' followed; left as a flag so the A/B is one line.
    ''' </summary>
    Public Shared FlipSkinnedZ As Boolean = True

    Private ReadOnly scene As MapScene
    Private shader As Shader
    Private loaded As Boolean
    Private failed As Boolean
    Public vehicles As New List(Of TankVehicle)
    Public instances As New List(Of TankInstance)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>
    ''' The test set, parked at base 1 facing +Z.
    '''
    ''' A TABLE rather than one hardcoded vehicle, because the armour colour is
    ''' a per-NATION value and a single tank can never show whether it is being
    ''' applied. usa is 82 72 51 and france is 15 36 36 - olive against a dark
    ''' teal - so the two side by side answer it at a glance.
    '''
    ''' Tags are the item_defs file names, which live in scripts.pkg under
    ''' scripts/item_defs/vehicles/&lt;nation&gt;/ - the core's ResMgr indexes that
    ''' one, TankFiles only indexes vehicles_*.pkg. Read the list from there
    ''' rather than guessing: F18_Bat_Chatillon25t, not F18_Bat_Chatillon.
    ''' </summary>
    Private Sub Load()
        loaded = True
        Try
            shader = New Shader("tank_gbuffer")

            ' nation, tag, metres to the RIGHT of the first, label
            Dim wanted = {
                Tuple.Create("usa", "A88_M53_55", 0.0F, "M53/M55"),
                Tuple.Create("france", "F18_Bat_Chatillon25t", 14.0F, "Bat 25t")
            }

            ' Base 1, the way the ring draws it: X negated, height from the terrain.
            '
            ' Then STOOD BACK from it. Parked on the base marker a vehicle sits
            ' inside the base model and the two intersect; BASE_STANDOFF walks it
            ' along its own backward axis so both can be looked at.
            '
            ' Backward and rightward are derived from the heading rather than
            ' hard wired to world axes, so they still mean "behind" and "beside"
            ' if the heading ever stops being zero. At heading 0 back is world
            ' -Z and right is world +X.
            Const BASE_STANDOFF As Single = 10.0F
            Dim heading = 0.0F
            Dim sh = CSng(Math.Sin(heading)), ch = CSng(Math.Cos(heading))
            Dim back_x = -sh * BASE_STANDOFF
            Dim back_z = -ch * BASE_STANDOFF

            For Each w In wanted
                Dim v = TankVehicle.Load(w.Item1, w.Item2)
                If v Is Nothing Then
                    LogThis("tank: {0}/{1} did not load - skipped", w.Item1, w.Item2)
                    Continue For
                End If
                vehicles.Add(v)

                Dim x = -TEAM_1.X + back_x + ch * w.Item3
                Dim z = TEAM_1.Z + back_z - sh * w.Item3
                ' Height sampled AFTER the move - the ground over there is not
                ' the ground at the marker, and sampling first buries or floats.
                Dim y = get_Y_at_XZ(x, z)
                instances.Add(New TankInstance With {
                    .vehicle = v, .position = New Vector3(x, y, z),
                    .headingRad = heading, .team = TankTeam.Green,
                    .label = w.Item4})
                LogThis("tank: placed {0}/{1} at ({2:0.00}, {3:0.00}, {4:0.00}) armour {5}",
                        w.Item1, v.tag, x, y, z, armor_text(w.Item1))
            Next

            If instances.Count = 0 Then failed = True
        Catch ex As Exception
            failed = True
            LogThis("tank: load failed - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Draw every placed tank into the G-buffer with the model shaders' own
    ''' convention, so the deferred pass lights, shadows and fogs it like any
    ''' model. Runs after draw_models, which leaves depth at Greater (reversed
    ''' Z) and the CNGPA attachments; we take CNGP for the draw and hand CNGPA
    ''' back. Depth WRITES on: the models had a pre-pass, we do not.
    ''' </summary>
    Public Sub Draw()
        If Not Enabled Then Return
        If Not MAP_LOADED OrElse map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return
        If Not loaded Then Load()
        If failed OrElse instances.Count = 0 OrElse shader Is Nothing Then Return

        GL_PUSH_GROUP("draw_tanks")
        MainFBO.attach_CNGP()
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        GL.DepthMask(True)
        ' Culling off until the handedness is settled by eye - a mirrored
        ' world flips the winding, and a wrong guess here hides every face.
        GL.Disable(EnableCap.CullFace)

        shader.Use()
        GL.Uniform1(shader("normal_mode"), NormalMode)
        upload_shading()

        For Each inst In instances
            upload_lights(inst.position)
            upload_armor(inst.vehicle.nation)
            Dim world = Matrix4.CreateScale(If(MirrorX, -1.0F, 1.0F), 1.0F, 1.0F) *
                        Matrix4.CreateRotationY(inst.headingRad) *
                        Matrix4.CreateTranslation(inst.position)
            For Each part In inst.vehicle.parts
                Dim partModel = Matrix4.CreateTranslation(part.offset) * world
                For Each m In part.meshes
                    Dim model = partModel
                    If FlipSkinnedZ AndAlso m.layout.offBoneIdx >= 0 Then
                        model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * partModel
                    End If
                    GL.UniformMatrix4(shader("u_model"), False, model)
                    upload_bones(part, m)
                    Dim mat = part.MaterialFor(m)
                    upload_uv_scroll(m, mat)
                    BindMaterial(mat)
                    m.vao.Bind()
                    Dim itype = If(m.index32, DrawElementsType.UnsignedInt, DrawElementsType.UnsignedShort)
                    Dim isz = If(m.index32, 4, 2)
                    For Each g In m.groups
                        GL.DrawElementsBaseVertex(PrimitiveType.Triangles, g.nPrimitives * 3, itype,
                                                  New IntPtr(g.startIndex * isz), g.startVertex)
                    Next
                Next
            Next
        Next

        shader.StopUse()
        GL.BindVertexArray(0)
        MainFBO.attach_CNGPA()
        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Everything the ported exporter shader needs that is not per-material.
    '''
    ''' tank_shading is the sentinel the shader tests: 0 means "the app has not
    ''' been rebuilt" and it falls back to a camera-relative rig, so a shader
    ''' dropped in ahead of its build still draws a lit tank. Setting it to 1
    ''' here is what hands the shader the real rig and the environment.
    ''' </summary>
    Private Sub upload_shading()
        ' One accumulator for the whole frame, advanced here rather than per
        ' mesh - upload_bones runs once per mesh and would otherwise step the
        ' distance a dozen times a frame.
        track_distance_m += TANK_SPEED * DELTA_TIME

        GL.Uniform1(shader("tank_shading"), 1)
        GL.Uniform1(shader("metal_scale"), TANK_LIGHT)
        GL.Uniform1(shader("shine_scale"), TANK_AMBIENT)
        GL.Uniform1(shader("apply_normal_map"), CInt(If(TANK_NORMAL_MAP, 1, 0)))
        GL.Uniform1(shader("apply_ao"), CInt(If(TANK_AO, 1, 0)))
        GL.Uniform1(shader("spec_scale"), TANK_SPECULAR)
        GL.Uniform1(shader("total_level"), TANK_TOTAL)

        ' THE ONE TRUE MIRROR. MirrorX is the only scale that actually reverses
        ' the geometry; FlipSkinnedZ is undoing a reversal already present in
        ' the skinned vertex data, so it must NOT be counted. The vertex shader
        ' used to infer this from determinant(mat3(u_model)) and got the skinned
        ' parts backwards - see the note there.
        GL.Uniform1(shader("u_winding"), If(MirrorX, -1.0F, 1.0F))

        ' THE ENVIRONMENT. Without it metal reflects nothing and the vehicle
        ' reads as plastic - not a figure of speech, it is what the first port
        ' looked like with these three flags at zero.
        '
        ' Unit 6 takes the RAW cube, exactly as the exporter does: its own
        ' comment records that binding a GGX-prefiltered cube there rendered
        ' the tank BLACK, because the prefilter at roughness 0 is a degenerate
        ' delta lobe that many drivers write as an all-zero mip 0. The mip
        ' chain is glGenerateMipmap's box blur, and that is accepted.
        '
        ' Unit 4 wants IRRADIANCE and nuTerra has no irradiance cube - the
        ' engine carries its ambient as 9 SH coefficients instead. The same
        ' cube goes up and the shader samples it at a high LOD: a blurred cube
        ' standing in for a cosine convolution. Close enough to read as
        ' ambient, and marked in the shader as the approximation it is.
        Dim ibl = TANK_IBL AndAlso map_scene IsNot Nothing AndAlso
                  map_scene.sky IsNot Nothing AndAlso
                  map_scene.sky.CUBE_TEXTURE_ID IsNot Nothing
        If ibl Then
            map_scene.sky.CUBE_TEXTURE_ID.BindUnit(4)
            map_scene.sky.CUBE_TEXTURE_ID.BindUnit(6)
            GL.Uniform1(shader("has_irradiance"), 1)
            GL.Uniform1(shader("has_prefiltered"), 1)
        Else
            GL.Uniform1(shader("has_irradiance"), 0)
            GL.Uniform1(shader("has_prefiltered"), 0)
        End If

        If ibl AndAlso map_scene.ENV_BRDF_LUT_ID IsNot Nothing Then
            map_scene.ENV_BRDF_LUT_ID.BindUnit(5)
            GL.Uniform1(shader("has_brdf_lut"), 1)
        Else
            GL.Uniform1(shader("has_brdf_lut"), 0)
        End If

        ' Not wired yet, and set explicitly rather than left reading whatever
        ' the last program left behind: the crash tile needs a /crash/ visual
        ' and PBS_tank_crash to mean anything, nuTerra has no nation armour
        ' colour for a tank, and nothing is firing a gun.
        GL.Uniform1(shader("has_crash_tile"), 0)
        ' The armour colour is NOT set here - it is per NATION, so it belongs
        ' in the instance loop. Setting it once a frame painted every vehicle
        ' with the first one's colour, which is exactly the thing a second tank
        ' was added to check.
        GL.Uniform1(shader("u_mflash_intensity"), 0.0F)
        GL.Uniform1(shader("alpha_in_normal_red"), 0)
        GL.Uniform1(shader("ao_in_diffuse_alpha"), 0)
    End Sub

    ''' <summary>
    ''' The exporter's three-point rig, centred on THIS tank.
    '''
    ''' 120 degrees apart on a horizontal ring, LIGHT_RADIUS out and
    ''' LIGHT_HEIGHT up - viewer.py line 20045, with its own
    ''' LIGHT_RADIUS = 10 and LIGHT_HEIGHT = 10. The exporter can hardcode the
    ''' ring about the origin because its tank is always there; here it has to
    ''' follow the instance, or a vehicle away from world zero is lit from the
    ''' wrong side entirely.
    ''' </summary>
    Private Sub upload_lights(centre As Vector3)
        Const FILL_RADIUS As Single = 10.0F
        Const FILL_HEIGHT As Single = 10.0F
        Static lp(8) As Single

        ' LIGHT 0 IS THE MAP'S SUN. The other two are fill, placed relative to
        ' it rather than to the world axes.
        '
        ' The exporter's rig is three lights on a ring at FIXED angles, because
        ' its tank stands alone on a turntable and there is no sun to disagree
        ' with. Carried across unchanged it lit a tank standing in a MAP from
        ' wherever world +X happens to point - no relation to the map's sun,
        ' which is why the angle to it read as mirrored.
        '
        ' LIGHT_POS is a point on a sphere about the world ORIGIN - MapLoader
        ' ~870 builds it from the orbit angles times LIGHT_RADIUS - so it is a
        ' direction, not a place. Pushed far out from the tank it gives the same
        ' direction deferred.frag uses for the terrain, and the shader treats
        ' its lights as directional anyway (no falloff), so the distance only
        ' has to be large enough not to skew across the hull.
        Dim sun = LIGHT_POS
        If sun.LengthSquared < 0.000001F Then sun = New Vector3(0.0F, 1.0F, 0.0F)
        sun = Vector3.Normalize(sun)

        lp(0) = centre.X + sun.X * 1000.0F
        lp(1) = centre.Y + sun.Y * 1000.0F
        lp(2) = centre.Z + sun.Z * 1000.0F

        ' The fills keep the exporter's 120 degree spacing, but measured from
        ' the SUN's azimuth, so they read as fill around the key light instead
        ' of two more suns aimed at world +X.
        Dim az = Math.Atan2(sun.Z, sun.X)
        For i = 1 To 2
            Dim a = CSng(az + i * (2.0 * Math.PI / 3.0))
            lp(i * 3 + 0) = centre.X + FILL_RADIUS * CSng(Math.Cos(a))
            lp(i * 3 + 1) = centre.Y + FILL_HEIGHT
            lp(i * 3 + 2) = centre.Z + FILL_RADIUS * CSng(Math.Sin(a))
        Next
        GL.Uniform3(shader("light_pos"), 3, lp)
    End Sub

    ''' <summary>
    ''' The armour colour for the loaded vehicle's nation, 0..1.
    '''
    ''' Values are the original Tank Exporter's, frmMain.vb:1350,
    ''' which notes they come from each nation's customization.xml.
    ''' Kept as a table rather than read from the game because that
    ''' is what the reference build does, and matching it is the
    ''' point. Zero means 'no colour for this nation' and the shader
    ''' is told to skip the tint entirely.
    ''' </summary>
    Private Sub upload_armor(nation As String)
        Dim ac = nation_armor_color(nation)
        If ac.LengthSquared > 0.0F Then
            GL.Uniform3(shader("armor_color"), ac.X, ac.Y, ac.Z)
            GL.Uniform1(shader("has_armor_color"), 1)
        Else
            GL.Uniform1(shader("has_armor_color"), 0)
        End If
    End Sub

    Private Function armor_text(nation As String) As String
        Dim c = nation_armor_color(nation) * 255.0F
        If c.LengthSquared = 0.0F Then Return "none"
        Return String.Format("{0:0} {1:0} {2:0}", c.X, c.Y, c.Z)
    End Function

    Private Function nation_armor_color(nation As String) As Vector3
        If String.IsNullOrEmpty(nation) Then Return Vector3.Zero
        Dim n = nation
        Dim c As Vector3
        Select Case n.ToLowerInvariant()
            Case "usa", "uk" : c = New Vector3(82, 72, 51)
            Case "china", "ussr" : c = New Vector3(61, 62, 42)
            Case "germany" : c = New Vector3(90, 103, 94)
            Case "czech", "france", "japan", "poland", "sweden", "italy"
                c = New Vector3(15, 36, 36)
            Case Else : Return Vector3.Zero
        End Select
        Return c / 255.0F
    End Function

    ''' <summary>
    ''' The bone palette for one mesh, as mat4s the vertex shader indexes with
    ''' iii/3.
    '''
    ''' IDENTITY FOR NOW. That is deliberate and it is the null test: with an
    ''' identity palette the weighted sum collapses to the bind pose, so this
    ''' frame must come out byte for byte what it was before skinning existed.
    ''' Anything else means the plumbing is wrong - the weights, the byte
    ''' divide, or the order - and that is far easier to find now than after a
    ''' wheel starts turning on top of it.
    '''
    ''' 64 is the shader's ceiling. A palette longer than that is clamped and
    ''' said out loud rather than silently drawing the wrong bones.
    ''' </summary>
    Private Sub upload_bones(part As TankPart, m As TankMesh)
        Const MAX_BONES As Integer = 64
        Dim palette = part.PaletteFor(m)
        If Not TANK_SKINNING OrElse palette Is Nothing OrElse m.layout.offBoneIdx < 0 Then
            GL.Uniform1(shader("u_skinned"), 0)
            Return
        End If

        If logged_palettes.Count < 12 AndAlso Not logged_palettes.Contains(m.name) Then
            logged_palettes.Add(m.name)
            LogThis("tank: palette [{0}] ({1}): {2}", m.name, palette.Count,
                    String.Join(", ", palette))
        End If

        Dim n = Math.Min(palette.Count, MAX_BONES)
        If palette.Count > MAX_BONES AndAlso Not warned_palette Then
            warned_palette = True
            LogThis("tank: palette of {0} exceeds the shaders {1} bones - clamped",
                    palette.Count, MAX_BONES)
        End If

        ' EVERY slot filled, and ALL of them uploaded - not just the n this
        ' palette uses. The shader clamps a bone index to the array bound, not
        ' to the palette length, so an index between n and 63 would read a slot
        ' that was never written: a ZERO matrix, which drops that vertex's
        ' contribution and shrinks it toward the origin. Caught by the identity
        ' null test below, which came back 10.78% of the frame darker instead of
        ' byte identical.
        Static bones(MAX_BONES * 16 - 1) As Single
        Array.Clear(bones, 0, bones.Length)
        For i = 0 To MAX_BONES - 1
            Dim o = i * 16
            bones(o + 0) = 1.0F : bones(o + 5) = 1.0F
            bones(o + 10) = 1.0F : bones(o + 15) = 1.0F
        Next

        ' Spin whatever in this palette is a wheel. Everything else stays
        ' identity, which is the bind pose it was already drawing at.
        ' The skinned streams are stored Z-reversed, and FlipSkinnedZ puts them
        ' back with a matrix scale. The node tree is NOT reversed, so a hub read
        ' from it has to be flipped into the vertex data's space before it can
        ' be used as a pivot.
        For i = 0 To n - 1
            spin_wheel_bone(part, m, palette(i), i, bones, i * 16)
        Next

        GL.UniformMatrix4(shader("u_bones"), MAX_BONES, False, bones)
        GL.Uniform1(shader("u_skinned"), 1)
    End Sub

    Private warned_palette As Boolean
    Private ReadOnly logged_palettes As New List(Of String)
    Private ReadOnly logged_hubs As New List(Of String)

    ''' <summary>
    ''' Write the spin matrix for one bone, if that bone is a wheel.
    '''
    ''' W_&lt;side&gt;&lt;i&gt;_BlendBone are the road wheels and WD_ the drive
    ''' sprockets, idlers and return rollers - read off the real palettes, not
    ''' assumed: exportChassL1Shape carries WD_L0..L3 and W_L0..L6.
    '''
    ''' The matrix is the Tank Exporter's, from tank_physics.bone_matrix_array:
    '''     T(hub) . Rx(theta) . T(-hub)
    ''' which shifts the vertex to hub-centred, spins it about chassis-local +X
    ''' - the YZ side plane, where a wheel turns - and puts it back. TEPY adds
    ''' the suspension residual to the Y translate; there is no suspension here
    ''' yet, so that term is zero and the rest is identical.
    '''
    ''' WRITTEN COLUMN MAJOR. GL reads the array column-major when transpose is
    ''' False, so these floats reach GLSL exactly as laid out. Identity hid this
    ''' - it is symmetric - and a rotation does not.
    ''' </summary>
    Private Sub spin_wheel_bone(part As TankPart, m As TankMesh,
                                bone As String, slot As Integer,
                                bones As Single(), o As Integer)
        If Not (bone.StartsWith("W_") OrElse bone.StartsWith("WD_")) Then Return
        If m.boneHubs Is Nothing OrElse slot >= m.boneHubs.Length Then Return

        ' THE HUB COMES FROM THE VERTICES THIS BONE WEIGHTS, not from the node
        ' tree. See TankMesh.ComputeBoneHubs for why - it is already in the
        ' vertex data's own space, mirrors and all, so there is no sign to get
        ' wrong here.
        Dim hub = m.boneHubs(slot)
        If Single.IsNaN(hub.X) Then Return

        ' THE RADIUS COMES FROM THE VERTICES TOO. The node-tree route -
        ' W_<i>.y minus Track_<i>.y - only works for road wheels, and WD_ bones
        ' have no Track_ partner at all, so every one of them fell back to a
        ' single mean. Equal radii mean equal angular rate, and a small wheel
        ' turning at a big wheel's rate is the "spin speed is flipped" look.
        Dim rad = 0.0F
        If m.boneRadii IsNot Nothing AndAlso slot < m.boneRadii.Length Then
            rad = m.boneRadii(slot)
        End If
        If rad <= 0.05F Then rad = wheel_radius(part, bone.Replace("_BlendBone", ""))
        If rad <= 0.05F Then Return

        If logged_hubs.Count < 9 AndAlso Not logged_hubs.Contains(bone) Then
            logged_hubs.Add(bone)
            LogThis("tank: wheel {0} hub-from-verts {1} radius {2:0.000} m",
                    bone, hub, rad)
        End If

        ' Angle from DISTANCE, so wheels of different radii share one
        ' accumulator and a speed change cannot make them jump.
        Dim th = -track_distance_m / rad
        Dim c = CSng(Math.Cos(th)), sn = CSng(Math.Sin(th))

        ' T(hub) . Rx(theta) . T(-hub), written COLUMN MAJOR because GL reads
        ' the array that way with transpose False. Identity hid this - it is
        ' symmetric - and a rotation does not.
        Dim ty = hub.Y - (c * hub.Y - sn * hub.Z)
        Dim tz = hub.Z - (sn * hub.Y + c * hub.Z)
        bones(o + 5) = c
        bones(o + 6) = sn
        bones(o + 9) = -sn
        bones(o + 10) = c
        bones(o + 13) = ty
        bones(o + 14) = tz
    End Sub

    ''' <summary>
    ''' A road wheel's radius, taken from the rig rather than from a constant.
    '''
    ''' Track_&lt;side&gt;&lt;i&gt;_BlendBone sits at the GROUND CONTACT under
    ''' W_&lt;side&gt;&lt;i&gt;_BlendBone - same X and Z, ground Y - so the gap
    ''' between the two IS the rolling radius, and no chassis xml is needed to
    ''' find it. TEPY's TRACK_PHYSICS notes the same relationship.
    '''
    ''' WD_ bones - sprockets, idlers, rollers - have no Track_ partner, so they
    ''' fall back to the mean road-wheel radius. That is approximate: a toothed
    ''' sprocket rides the chain at the polygon pitch radius
    ''' p / (2 sin(pi/N)), and TEPY measures ~1% of drift per revolution from
    ''' using bare R instead. Visible only once the chain is being driven from
    ''' the sprocket angle, which it is not yet.
    ''' </summary>
    Private Function wheel_radius(part As TankPart, bare As String) As Single
        Dim hub As Vector3 = Nothing
        If Not part.visual.nodePos.TryGetValue(bare, hub) Then Return 0.0F
        If bare.StartsWith("W_") Then
            ' W_L0 -> Track_L0, both bare: the Track_ nodes cancel to the origin
            ' through their BlendBones exactly as the wheels do.
            Dim ground = "Track_" & bare.Substring(2)
            Dim gp As Vector3 = Nothing
            If part.visual.nodePos.TryGetValue(ground, gp) Then
                Dim rr = hub.Y - gp.Y
                If rr > 0.05F AndAlso rr < 1.5F Then Return rr
            End If
        End If
        Return mean_road_radius(part)
    End Function

    Private Function mean_road_radius(part As TankPart) As Single
        If part.meanRoadRadius > 0.0F Then Return part.meanRoadRadius
        Dim tot = 0.0F, cnt = 0
        For Each kv In part.visual.nodePos
            If Not kv.Key.StartsWith("W_") Then Continue For
            If kv.Key.EndsWith("_BlendBone") Then Continue For
            Dim ground = "Track_" & kv.Key.Substring(2)
            Dim gp As Vector3 = Nothing
            If Not part.visual.nodePos.TryGetValue(ground, gp) Then Continue For
            Dim rr = kv.Value.Y - gp.Y
            If rr > 0.05F AndAlso rr < 1.5F Then
                tot += rr : cnt += 1
            End If
        Next
        part.meanRoadRadius = If(cnt > 0, tot / cnt, 0.35F)
        Return part.meanRoadRadius
    End Function

    ''' <summary>How far the track has run, metres. One accumulator for every
    ''' wheel; theta is this over each wheel's own radius.</summary>
    Private Shared track_distance_m As Single

    ''' <summary>
    ''' Scroll the track band's UVs, and nothing else's.
    '''
    ''' THE BAND IS NOT ANIMATED BY ITS BONES. Its palette carries
    ''' Track_&lt;side&gt;&lt;i&gt;_BlendBone per road wheel, V_BlendBone for the
    ''' top run, and Track_VT_ / Track_VD_ for the top and the wraparound - and
    ''' those exist so the band DEFORMS when suspension moves a wheel, which is
    ''' a different thing from the tread appearing to run. On a parked tank with
    ''' no suspension they would do nothing at all.
    '''
    ''' What makes it run is a UV transform, and the material says so outright:
    ''' PBS_tank_uvtransform_skinned_ao.fx. Keyed off the effect name rather
    ''' than a mesh-name guess, so a vehicle whose band is called something else
    ''' still scrolls and a hull that happens to match a name pattern does not.
    '''
    ''' THE AXIS IS MEASURED, NOT ASSUMED. A band is a long strip, so its UVs
    ''' span far further along the running direction than across it; whichever
    ''' of U or V has the greater extent is the run. Guessing V and being wrong
    ''' would slide the tread sideways across the track, and this session has
    ''' already spent two rounds on axis guesses that looked plausible.
    ''' </summary>
    Private Sub upload_uv_scroll(m As TankMesh, mat As TankMaterial)
        Dim sx = 0.0F, sy = 0.0F
        If mat IsNot Nothing AndAlso mat.fx IsNot Nothing AndAlso
           mat.fx.IndexOf("uvtransform", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Dim du = m.uvMax.X - m.uvMin.X
            Dim dv = m.uvMax.Y - m.uvMin.Y
            Dim off = track_distance_m * TANK_TRACK_UV
            If du >= dv Then sx = off Else sy = off

            If Not logged_uv Then
                logged_uv = True
                LogThis("tank: band [{0}] fx={1} uv span u={2:0.00} v={3:0.00} -> scrolling {4}",
                        m.name, mat.fx, du, dv, If(du >= dv, "U", "V"))
            End If
        End If
        GL.Uniform2(shader("uv_scroll"), sx, sy)
    End Sub

    Private logged_uv As Boolean

    ''' <summary>Units 0..3: AM, ANM, GMM, AO. A missing map binds nothing and the shader is told.</summary>
    Private Sub BindMaterial(mat As TankMaterial)
        Dim hasD = 0, hasN = 0, hasG = 0, hasA = 0
        If mat IsNot Nothing Then
            If mat.texDiffuse IsNot Nothing Then mat.texDiffuse.BindUnit(0) : hasD = 1
            If mat.texNormal IsNot Nothing Then mat.texNormal.BindUnit(1) : hasN = 1
            If mat.texGMM IsNot Nothing Then mat.texGMM.BindUnit(2) : hasG = 1
            If mat.texAO IsNot Nothing Then mat.texAO.BindUnit(3) : hasA = 1
            GL.Uniform1(shader("alpha_test"), If(mat.alphaTestEnable, 1, 0))
            GL.Uniform1(shader("alpha_ref"), mat.alphaReference / 255.0F)
            GL.Uniform1(shader("normal_dxt1"), If(mat.useNormalPackDXT1, 1, 0))

            ' metallicDetailMap, unit 7. The loader already resolves this and
            ' TankMaterial has been carrying texDetail and detailUVTiling the
            ' whole time - the stub shader simply had nowhere to put them. It
            ' is the exporter's "scrach" highlight: detail.r * detail.g tiled
            ' about 7x, which is what gives chrome and lens surfaces their
            ' sparkle instead of a flat sheen.
            If mat.texDetail IsNot Nothing Then
                mat.texDetail.BindUnit(7)
                GL.Uniform1(shader("has_detail_map"), 1)
                GL.Uniform2(shader("detail_tiling"),
                            mat.detailUVTiling.X, mat.detailUVTiling.Y)
            Else
                GL.Uniform1(shader("has_detail_map"), 0)
            End If
        Else
            GL.Uniform1(shader("alpha_test"), 0)
            GL.Uniform1(shader("normal_dxt1"), 0)
            GL.Uniform1(shader("has_detail_map"), 0)
        End If
        GL.Uniform4(shader("has_maps"), hasD, hasN, hasG, hasA)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each v In vehicles
            v.Dispose()
        Next
        vehicles.Clear()
        instances.Clear()
    End Sub
End Class

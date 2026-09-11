Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' The tank module's face to the rest of nuTerra. The core touches it in three
''' places only: MapScene owns one (construct / dispose), draw_scene calls
''' Draw() once after the static models, and calls DrawBillboards() once more
''' late in the frame with the other world-space overlays. Everything else -
''' the vehicle XML, the primitives, the visuals, the textures, the shaders -
''' lives under nuTerra\Tanks and shaders\Tanks.
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
    Private cards As TankCards
    Private shader As Shader
    Private loaded As Boolean
    Private failed As Boolean
    Public vehicles As New List(Of TankVehicle)
    Public instances As New List(Of TankInstance)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>
    ''' Fifteen tier 10 vehicles at each base, facing each other.
    '''
    ''' A FULL TEAM rather than a handful, because most of what is still open on
    ''' the tank pass only shows up in numbers: the armour colour is per NATION
    ''' and needs several side by side to read as a scheme; the wheel radii and
    ''' the band UV scale are measured per vehicle and a single tank can only
    ''' ever confirm the one it was tuned on; and thirty hulls is the first
    ''' honest look at what the pass costs.
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

            ' THE ROSTER IS TIER 10, and it is taken from the package layout
            ' rather than from a list anyone typed. The game ships vehicle
            ' assets in vehicles_level_NN packages, so vehicles_level_10*.pkg
            ' IS the tier 10 roster - 137 asset folders, of which 121 have a
            ' matching item_def. These 30 are round-robined across the nations
            ' so neither team is all one country.
            '
            ' The two halves of the install spell the nations differently:
            ' assets use american / british / russian, item_defs use usa / uk /
            ' ussr. The names below are the item_def spelling, because that is
            ' what TankVehicle.Load wants.
            Dim roster = {
                Tuple.Create("china", "Ch19_121"),
                Tuple.Create("czech", "Cz04_T50_51"),
                Tuple.Create("france", "F108_Panhard_EBR_105"),
                Tuple.Create("germany", "G121_Grille_15_L63"),
                Tuple.Create("italy", "It08_Progetto_M40_mod_65"),
                Tuple.Create("japan", "J16_ST_B1"),
                Tuple.Create("poland", "Pl15_60TP_Lewandowskiego"),
                Tuple.Create("sweden", "S11_Strv_103B"),
                Tuple.Create("uk", "GB100_Manticore"),
                Tuple.Create("usa", "A106_M48A2_120"),
                Tuple.Create("ussr", "R110_Object_260"),
                Tuple.Create("china", "Ch22_113"),
                Tuple.Create("czech", "Cz17_Vz_55"),
                Tuple.Create("france", "F10_AMX_50B"),
                Tuple.Create("germany", "G125_Spz_57_Rh"),
                Tuple.Create("italy", "It15_Rinoceronte"),
                Tuple.Create("japan", "J20_Type_2605"),
                Tuple.Create("poland", "Pl15_60TP_Lewandowskiego_CFE_A"),
                Tuple.Create("sweden", "S16_Kranvagn"),
                Tuple.Create("uk", "GB114_Vickers_MBT_Mk3"),
                Tuple.Create("usa", "A116_XM551"),
                Tuple.Create("ussr", "R119_Object_777C"),
                Tuple.Create("china", "Ch22_113_Beijing_Opera"),
                Tuple.Create("czech", "Cz21_Vz_60S"),
                Tuple.Create("france", "F141_Durendal"),
                Tuple.Create("germany", "G134_PzKpfw_VII"),
                Tuple.Create("italy", "It20_Carro_Combattimento_45t"),
                Tuple.Create("japan", "J35_Ho_Ri_3"),
                Tuple.Create("poland", "Pl21_CS_63"),
                Tuple.Create("sweden", "S28_UDES_15_16")
            }

            Const PER_TEAM As Integer = 15
            Const ROW_N As Integer = 5
            Const SPACING As Single = 14.0F

            ' The two lines face each other: team 1 at heading 0 looks down +Z,
            ' team 2 at PI looks back down -Z, and each block is set BEHIND its
            ' own marker along its own backward axis. That keeps the base ring
            ' itself clear and puts the tanks where a match would start them.
            Dim placed As New List(Of Vector2)
            Dim from_spawn_count = 0

            For i = 0 To roster.Length - 1
                Dim r = roster(i)
                Dim team = If(i < PER_TEAM, 1, 2)
                Dim k = i Mod PER_TEAM

                Dim v = TankVehicle.Load(r.Item1, r.Item2)
                If v Is Nothing Then
                    LogThis("tank: {0}/{1} did not load - skipped", r.Item1, r.Item2)
                    Continue For
                End If
                vehicles.Add(v)

                Dim heading = If(team = 1, 0.0F, CSng(Math.PI))
                Dim spawns = If(team = 1, TEAM_1_SPAWNS, TEAM_2_SPAWNS)
                Dim marker = If(team = 1, TEAM_1, TEAM_2)

                ' A REAL SPAWN POINT IF THE MAP DECLARES ONE for this slot,
                ' else a block behind the marker. Most maps - 19_monastery
                ' among them - declare none at all, and of the 37 that do most
                ' carry fewer than fifteen, so the block is the normal path for
                ' a full team rather than an error case.
                Dim x As Single, z As Single
                If k < spawns.Count Then
                    x = -spawns(k).X
                    z = spawns(k).Z
                    from_spawn_count += 1
                Else
                    ' Five abreast, three deep, centred on the marker. Rows
                    ' start one SPACING back so nothing lands on the ring.
                    Dim col = CSng(k Mod ROW_N) - (ROW_N - 1) / 2.0F
                    Dim row = k \ ROW_N
                    Dim back = If(team = 1, -1.0F, 1.0F)
                    x = -marker.X + col * SPACING
                    z = marker.Z + (row + 1) * SPACING * back
                End If

                ' Then the rules. The block is a guess at open ground and a
                ' spawn point is wherever the map put it; neither of them knows
                ' what is standing there, and tanks already placed count as
                ' obstacles so the fifteen cannot stack on each other.
                Dim spot = find_clear_spot(x, z, placed)
                x = spot.X : z = spot.Y
                placed.Add(spot)

                ' Height sampled AFTER the move - the ground over there is not
                ' the ground at the marker, and sampling first buries or floats.
                ' SPREAD THE STARTING POSE across each vehicle's own envelope.
                ' Thirty turrets all at zero read as a parade rather than as
                ' thirty tanks, and a single frame of a capture - which freezes
                ' the animation clock at zero - would show nothing aiming at
                ' all. The fraction is per index, so each is somewhere
                ' different in its OWN range: a casemate still only moves the
                ' three degrees it has.
                Dim f = CSng((i * 0.37) Mod 1.0)
                Dim yaw0 = v.yawMin + (v.yawMax - v.yawMin) * f
                Dim pr0 = v.PitchRangeAt(yaw0)
                Dim pitch0 = pr0.X + (pr0.Y - pr0.X) * CSng((i * 0.61) Mod 1.0)

                Dim y = get_Y_at_XZ(x, z)
                instances.Add(New TankInstance With {
                    .vehicle = v, .position = New Vector3(x, y, z),
                    .headingRad = heading,
                    .team = If(team = 1, TankTeam.Green, TankTeam.Red),
                    .label = r.Item2, .id = k + 1,
                    .fireIn = 0.21F * i,
                    .turretYaw = yaw0, .gunPitch = pitch0})
                LogThis("tank: team {0} slot {1,2} {2}/{3} at ({4:0.0}, {5:0.0}, {6:0.0}) obstacle {7:0.00} m armour {8}",
                        team, k, r.Item1, v.tag, x, y, z, obstacle_at(x, z),
                        armor_text(r.Item1))
            Next

            LogThis("tank: {0} of {1} placed - {2} on declared spawn points",
                    instances.Count, roster.Length, from_spawn_count)
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
            Dim wp = shuttle_position(inst)
            upload_lights(wp)
            upload_armor(inst.vehicle.nation)
            Dim world = world_matrix(inst, wp)
            For Each part In inst.vehicle.parts
                Dim partModel = part_model(inst, part, world)
                For Each m In part.meshes
                    Dim model = partModel
                    If FlipSkinnedZ AndAlso m.layout.offBoneIdx >= 0 Then
                        model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * partModel
                    End If
                    GL.UniformMatrix4(shader("u_model"), False, model)
                    upload_bones(part, m)
                    upload_recoil(part, m, inst)
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
        advance_shuttle()

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
        ' alpha_in_normal_red is gone: the alpha test's source is decided by
        ' normal_dxt1 now, which is the game's own rule and is already uploaded
        ' per material. Pinning this to zero made every alpha-tested material
        ' threshold diffuseMap.a - a channel PBS_tank.fx never reads.
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
            ' NEGATIVE, to match the wheels. They spin on theta = -s / R, and
            ' the band was scrolling on +s, so the tread ran backwards under
            ' wheels turning forwards. The two signs are not independent - both
            ' describe the same track moving the same way - so this is tied to
            ' the wheel convention rather than being a free choice.
            ' UV units per metre from the MESH, so the tread advances exactly
            ' the distance the wheels rolled. TANK_TRACK_UV stays as a trim at
            ' 1.0 rather than being the whole conversion - it was 2.5 by eye,
            ' which could only ever be right for the one vehicle it was set on.
            Dim upm = m.uvPerMetre
            If upm <= 0.0F Then upm = 2.5F   ' unmeasurable: the old eyeball
            Dim off = -track_distance_m * upm * TANK_TRACK_UV
            If du >= dv Then sx = off Else sy = off

            ' Keyed on the band's own texture so each VEHICLE reports once - the
            ' mesh names are identical across tanks.
            If Not logged_uv.Contains(mat.diffuseMap) Then
                logged_uv.Add(mat.diffuseMap)
                LogThis("tank: band {0} uv span u={1:0.00} v={2:0.00} -> {3}, {4:0.00} uv/m",
                        IO.Path.GetFileName(mat.diffuseMap), du, dv,
                        If(du >= dv, "U", "V"), upm)
            End If
        End If
        GL.Uniform2(shader("uv_scroll"), sx, sy)
    End Sub

    Private ReadOnly logged_uv As New List(Of String)
    Private ReadOnly logged_alpha As New List(Of String)

    ''' <summary>
    ''' Run the tanks SHUTTLE_M forward, then back, forever.
    '''
    ''' THE GROUND TRAVEL AND THE ROTATION COME FROM THE SAME NUMBER. Both the
    ''' hull's displacement and track_distance_m - which the wheels turn on as
    ''' theta = -s/R and the band scrolls on - are advanced by this one signed
    ''' step. There is no second integrator to drift against the first, so the
    ''' tank cannot slip: a metre of ground is a metre of tread by construction
    ''' rather than by the two being tuned to agree.
    '''
    ''' That is also why the step is SIGNED. Reversing at the end of a run has
    ''' to wind the wheels backwards too; an accumulator that only ever grew
    ''' would drive the tank home with its tracks still running forwards.
    ''' </summary>
    Private Sub advance_shuttle()
        ' Not SHUTTLE_M: VB is case-insensitive, so that name and the
        ' shuttle_m field below are the SAME identifier.
        Const SHUTTLE_RANGE_M As Single = 10.0F

        ' ANIM_DELTA, NOT DELTA_TIME. The two are the same while the app is
        ' just running; they part company during a capture, where ANIM_DELTA is
        ' pinned to one frame of the output rate and is zero while the recorder
        ' waits for the virtual texture to settle. Driving the tanks off the
        ' real frame time meant a still shot the same build twice caught them
        ' at two different points of the run - the tanks had walked on during
        ' however long the VT took that time - so nothing about the tank pass
        ' could be compared between two captures. Everything animated here now
        ' runs on the recorder's clock.
        Dim step_m = TANK_SPEED * ANIM_DELTA * shuttle_dir
        shuttle_m += step_m
        track_distance_m += step_m

        Dim reversed = False
        If shuttle_m >= SHUTTLE_RANGE_M Then
            shuttle_m = SHUTTLE_RANGE_M
            shuttle_dir = -1.0F
            reversed = True
        ElseIf shuttle_m <= 0.0F Then
            shuttle_m = 0.0F
            shuttle_dir = 1.0F
            reversed = True
        End If

        advance_guns(reversed)
    End Sub

    ''' <summary>
    ''' Tick every gun, and pull the triggers.
    '''
    ''' TWO TRIGGERS, both live. The end of the shuttle run is a volley - the
    ''' whole line fires as it turns round - and between turns each gun runs
    ''' its own cadence. The reversal on its own comes about every twenty
    ''' seconds at the default crawl, which is far too sparse to judge a
    ''' recoil by; the cadence on its own loses the moment where they all go
    ''' at once.
    '''
    ''' The cadence timers are seeded APART, in Load, and each gun reloads to
    ''' the full period after firing, so they stay spread. Seeding them all at
    ''' zero makes thirty barrels move in lockstep, which reads as one object
    ''' rather than thirty tanks.
    '''
    ''' TankRecoil.Fire ignores a trigger while a cycle is running, so the two
    ''' sources landing on the same frame fire one shot rather than restarting
    ''' the stroke and leaving the barrel stuck out.
    ''' </summary>
    Private Sub advance_guns(reversed As Boolean)
        For Each inst In instances
            ' WHERE IT IS, before anything asks. The shuttle moves the vehicle
            ' and both the aim and the shot have to use this frame's position -
            ' a ray fired from last frame's muzzle leaves the barrel.
            inst.livePosition = shuttle_position(inst)
            advance_aim(inst)
            inst.recoil.Update(ANIM_DELTA)
            inst.shots.Update(ANIM_DELTA)
            If Not TANK_FIRING Then Continue For

            Dim went = False
            If reversed Then went = inst.recoil.Fire()

            If TANK_FIRE_PERIOD > 0.0F Then
                inst.fireIn -= ANIM_DELTA
                If inst.fireIn <= 0.0F Then
                    inst.fireIn = TANK_FIRE_PERIOD
                    If inst.recoil.Fire() Then went = True
                End If
            End If

            If went Then fire_shot(inst)
        Next
        fx.Update(ANIM_DELTA)
    End Sub

    ''' <summary>
    ''' The ID and condition card over each tank.
    '''
    ''' SEPARATE FROM Draw, and late in the frame. Draw writes the G-buffer and
    ''' its pixels are then lit, fogged and tonemapped like any model's - which
    ''' is right for a hull and wrong for a marker, whose whole job is to be
    ''' read. This runs after all of that, straight into the finished frame,
    ''' with no depth test so a card is never lost behind the tank in front.
    '''
    ''' The positions come from shuttle_position, the same call Draw uses, so a
    ''' card cannot lag its tank by a frame.
    ''' </summary>
    Public Sub DrawBillboards()
        If Not TANK_TAGS OrElse Not Enabled Then Return
        If failed OrElse Not loaded OrElse instances.Count = 0 Then Return

        advance_demo_hp()
        If cards Is Nothing Then cards = New TankCards()
        cards.Bake(instances)
        cards.Draw(instances, Function(inst) shuttle_position(inst))
    End Sub

    ''' <summary>
    ''' Move the demo condition bars.
    '''
    ''' Each tank drains at its OWN rate from its own starting phase, so thirty
    ''' markers show thirty different bars instead of one value copied thirty
    ''' times - which is the only arrangement that would show a card baking the
    ''' wrong slot. Crew falls more slowly than hull because two bars that move
    ''' together are indistinguishable from one bar drawn twice.
    '''
    ''' Nothing here is a measurement. See TANK_HP_DEMO.
    ''' </summary>
    Private Sub advance_demo_hp()
        If Not TANK_HP_DEMO Then Return
        demo_t += ANIM_DELTA
        For i = 0 To instances.Count - 1
            Dim rate = 0.030F + 0.004F * ((i * 7) Mod 11)
            Dim phase = CSng((demo_t * rate + i * 0.137F) Mod 1.0F)
            instances(i).hullHp = 1.0F - phase
            instances(i).crewHp = 1.0F - phase * 0.55F
        Next
    End Sub

    ''' <summary>
    ''' Walk one vehicle's turret and gun across their own envelope.
    '''
    ''' AT THE FILE'S OWN RATES, which is why thirty tanks sweeping together
    ''' still look like thirty tanks: the M48's turret comes round at 50 deg/s
    ''' and the Rinoceronte's at 28, and the difference is visible at a glance.
    '''
    ''' THE PITCH ENVELOPE IS RE-READ AT THE CURRENT YAW every frame, not once.
    ''' That is the whole reason pitchLimits is a curve: as the turret sweeps
    ''' through the rear arc the depression limit pinches - five degrees to one
    ''' on the 121, ten to three and a half on the Rinoceronte - and a gun that
    ''' was depressed gets pushed back up rather than sinking through the
    ''' engine deck. Clamping after the move is what makes that happen; a
    ''' limit checked only when the target is chosen would let it pass through.
    ''' </summary>
    Private Sub advance_aim(inst As TankInstance)
        If Not TANK_AIM Then Return
        Dim v = inst.vehicle

        ' STAND AT THE END OF THE TRAVERSE before starting back. Without it a
        ' casemate reverses four times a second - the Strv 103B has three
        ' degrees of travel and covers them in a fifth of a second - which
        ' reads as the turret vibrating rather than as a short traverse.
        '
        ' THE HOLD IS ON THE TRAVERSE ONLY. It used to return early and freeze
        ' the whole vehicle's aim, so every gun on the map stopped elevating
        ' for a second and a half at a time, out of step with each other. A
        ' gunner laying the gun does not stop because the turret stopped.
        If inst.aimHold > 0.0F Then
            inst.aimHold -= ANIM_DELTA
        Else
            ' Traverse, toward whichever end it is heading for. A casemate's
            ' three degrees and a turret's full circle are the same code.
            Dim yTarget = If(inst.yawToMax, v.yawMax, v.yawMin)
            Dim yStep = v.yawRate * ANIM_DELTA
            If Math.Abs(yTarget - inst.turretYaw) <= yStep Then
                inst.turretYaw = yTarget
                inst.yawToMax = Not inst.yawToMax
                inst.aimHold = AIM_HOLD_S + 0.13F * (inst.id Mod 7)
            Else
                inst.turretYaw += Math.Sign(yTarget - inst.turretYaw) * yStep
            End If
        End If

        Dim pr = v.PitchRangeAt(inst.turretYaw)
        Dim pTarget = If(inst.pitchToMax, pr.Y, pr.X)
        Dim pStep = v.pitchRate * ANIM_DELTA
        If Math.Abs(pTarget - inst.gunPitch) <= pStep Then
            inst.gunPitch = pTarget
            inst.pitchToMax = Not inst.pitchToMax
        Else
            inst.gunPitch += Math.Sign(pTarget - inst.gunPitch) * pStep
        End If

        ' The envelope moved under it. Clamp AFTER the step.
        inst.gunPitch = Math.Min(Math.Max(inst.gunPitch, pr.X), pr.Y)
    End Sub

    ''' <summary>
    ''' Where one part sits this frame: its own joint rotation, its offset, and
    ''' the vehicle's place in the world.
    '''
    ''' THE JOINTS ARE THE OFFSETS. The vehicle already chained them out of the
    ''' file - turretPosition on the hull, gunPosition on the turret - and a
    ''' part's mesh origin IS its joint, so rotating the mesh about its own
    ''' origin and then translating by the offset is the rotation about the
    ''' joint. There is nothing extra to measure and no pivot to store.
    '''
    ''' The gun reads as the composition it is: pitch about the gun's own
    ''' origin, out to where the gun hangs off the turret, yaw about the turret,
    ''' out to where the turret sits on the hull. Written in that order because
    ''' OpenTK is row-vector - the leftmost matrix applies first - and reversing
    ''' the two rotations carries the elevation axis round with the turret, so
    ''' the gun climbs sideways instead of up.
    '''
    ''' THE PITCH IS NEGATED, and it is not a fudge. gunPitch is degrees UP,
    ''' which is the convention the envelope is written in and the one worth
    ''' reading. The rotation that produces it is the other sign: the gun's
    ''' vertex data has the muzzle at -Z and FlipSkinnedZ puts it at +Z, and
    ''' OpenTK's row-vector CreateRotationX sends a point at +Z to negative Y
    ''' for a positive angle - so +15 degrees of rotation is fifteen degrees of
    ''' DEPRESSION. Converting once, here, keeps every number above and in the
    ''' def file meaning what it says.
    ''' </summary>
    Private Function part_model(inst As TankInstance, part As TankPart,
                                world As Matrix4) As Matrix4
        If Not TANK_AIM Then Return Matrix4.CreateTranslation(part.offset) * world

        Dim ya = MathHelper.DegreesToRadians(inst.turretYaw)
        If part.label = "turret" Then
            Return Matrix4.CreateRotationY(ya) *
                   Matrix4.CreateTranslation(part.offset) * world
        End If

        If part.label = "gun" Then
            Dim tOff = turret_offset(inst)
            Return Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-inst.gunPitch)) *
                   Matrix4.CreateTranslation(part.offset - tOff) *
                   Matrix4.CreateRotationY(ya) *
                   Matrix4.CreateTranslation(tOff) * world
        End If

        Return Matrix4.CreateTranslation(part.offset) * world
    End Function

    ''' <summary>A vehicle's place in the world. One definition, because the
    ''' draw and the shot have to agree about where the gun is pointing to the
    ''' last decimal - a muzzle computed from a second copy of this drifts from
    ''' the barrel it is supposed to be at the end of.</summary>
    Private Function world_matrix(inst As TankInstance, wp As Vector3) As Matrix4
        Return Matrix4.CreateScale(If(MirrorX, -1.0F, 1.0F), 1.0F, 1.0F) *
               Matrix4.CreateRotationY(inst.headingRad) *
               Matrix4.CreateTranslation(wp)
    End Function

    Private Function turret_offset(inst As TankInstance) As Vector3
        For Each p In inst.vehicle.parts
            If p.label = "turret" Then Return p.offset
        Next
        Return Vector3.Zero
    End Function

    ''' <summary>
    ''' Send one round down the barrel this gun is actually pointing.
    '''
    ''' THE MUZZLE IS MEASURED, not offset from the joint by a guessed length.
    ''' Every gun is a different length and they are not all authored down the
    ''' same axis, so the two points that define the shot come from the barrel
    ''' bone's own vertices: its weighted centroid, which is about mid-barrel,
    ''' and the furthest its vertices reach along Z, which is the muzzle. Both
    ''' go through the SAME matrix the gun is drawn with - flip, pitch, yaw,
    ''' hull, heading, mirror - so the ray leaves exactly where the barrel is on
    ''' screen, and the direction is the difference between them rather than an
    ''' axis anyone had to pick a sign for.
    ''' </summary>
    Private Sub fire_shot(inst As TankInstance)
        If Not TANK_SHOTS Then Return

        Dim gunPart As TankPart = Nothing
        For Each p In inst.vehicle.parts
            If p.label = "gun" Then gunPart = p : Exit For
        Next
        If gunPart Is Nothing Then Return

        Dim world = world_matrix(inst, inst.livePosition)
        Dim model = part_model(inst, gunPart, world)

        Dim muzzle As Vector3, dir As Vector3
        If inst.vehicle.hasMuzzle Then
            ' THE GAME'S OWN MUZZLE, and through the UNFLIPPED matrix. The node
            ' tree is in the visual's frame; only the vertex streams are stored
            ' Z-reversed, so pushing a node position through FlipSkinnedZ puts
            ' the blast at the breech.
            Dim m0 = inst.vehicle.muzzleLocal
            muzzle = Vector3.TransformPosition(m0, model)
            dir = Vector3.TransformPosition(m0 + Vector3.UnitZ, model) - muzzle
        ElseIf Not measured_muzzle(inst, gunPart, model, muzzle, dir) Then
            Return
        End If

        If dir.LengthSquared < 1.0E-6F Then Return
        dir = Vector3.Normalize(dir)

        Dim hit = TankShots.Cast(muzzle, dir, instances, inst)

        ' The FLAME goes in this tank's own pool; the IMPACT goes in the shared
        ' one. They are not linked and must not be: a round from one vehicle
        ' lands on another, and TEPY makes the same split for the same reason -
        ' one shot can produce no impact at all, or later more than one.
        inst.shots.Fire(muzzle, dir, inst.vehicle.blast)
        fx.Impact(hit)

        ' THE FIRST FEW IN FULL, then a tally. Thirty guns at a round every two
        ' seconds is fifteen lines a second forever, which buries the load log
        ' it shares - but two dozen lines is enough to see that the muzzle is on
        ' the barrel and the rounds are landing on things, and a running count
        ' every hundred says whether that is still true an hour later.
        shots_fired += 1
        shots_by_kind(CInt(hit.kind)) += 1
        If shots_fired <= 24 Then
            LogThis("tank: {0} #{1} fired from ({2:0.0}, {3:0.0}, {4:0.0}) pitch {5:0.0} -> {6} at {7:0.0} m{8}",
                    inst.vehicle.tag, inst.id, muzzle.X, muzzle.Y, muzzle.Z,
                    inst.gunPitch, hit.kind.ToString(), hit.range,
                    If(hit.kind = HitKind.Tank AndAlso hit.tank IsNot Nothing,
                       " (" & hit.tank.vehicle.tag & " #" & hit.tank.id & ")", ""))
        ElseIf shots_fired Mod 100 = 0 Then
            LogThis("tank: {0} shots - {1} ground, {2} scenery, {3} tank, {4} away",
                    shots_fired, shots_by_kind(CInt(HitKind.Ground)),
                    shots_by_kind(CInt(HitKind.Scenery)),
                    shots_by_kind(CInt(HitKind.Tank)),
                    shots_by_kind(CInt(HitKind.NoHit)))
        End If
    End Sub

    ''' <summary>
    ''' The muzzle for a gun whose visual does not name HP_gunFire.
    '''
    ''' Every stock gun does name it, so this is the path for a modded or
    ''' malformed visual rather than the normal one. It takes the barrel bone's
    ''' own vertices - the weighted centroid and the furthest they reach along Z
    ''' - and goes through the FLIPPED matrix, because unlike the node tree the
    ''' vertex data is stored Z-reversed.
    ''' </summary>
    Private Function measured_muzzle(inst As TankInstance, gunPart As TankPart,
                                     model As Matrix4, ByRef muzzle As Vector3,
                                     ByRef dir As Vector3) As Boolean
        If FlipSkinnedZ Then model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * model
        For Each m In gunPart.meshes
            If m.layout Is Nothing OrElse m.layout.offBoneIdx < 0 Then Continue For
            Dim plan = resolve_recoil(gunPart, m)
            If plan Is Nothing OrElse plan.barrel < 0 Then Continue For
            If m.boneHubs Is Nothing OrElse m.boneTipZ Is Nothing Then Continue For
            If plan.barrel >= m.boneHubs.Length Then Continue For
            Dim hub = m.boneHubs(plan.barrel)
            If Single.IsNaN(hub.X) Then Continue For

            Dim back = Vector3.TransformPosition(New Vector3(hub.X, hub.Y, hub.Z), model)
            muzzle = Vector3.TransformPosition(
                New Vector3(hub.X, hub.Y, m.boneTipZ(plan.barrel)), model)
            dir = muzzle - back
            Return True
        Next
        Return False
    End Function

    ''' <summary>Base dwell at the end of a traverse. Staggered per tank on
    ''' top of this, so a line of them does not pause as one.</summary>
    Private Const AIM_HOLD_S As Single = 1.6F

    Private Shared shots_fired As Integer
    Private Shared shots_by_kind(3) As Integer
    Private Shared demo_t As Single

    ''' <summary>
    ''' Where an instance is this frame: its parked spot, walked forward along
    ''' its own heading, re-seated on the terrain.
    '''
    ''' The height is resampled at the NEW xz every frame. Carrying the parked
    ''' Y across ten metres of monastery would bury the tank in the rise or
    ''' float it over the dip - the same reason the 10 m standoff samples after
    ''' the move rather than before it.
    ''' </summary>
    Private Function shuttle_position(inst As TankInstance) As Vector3
        Dim sh = CSng(Math.Sin(inst.headingRad))
        Dim ch = CSng(Math.Cos(inst.headingRad))
        Dim x = inst.position.X + sh * shuttle_m
        Dim z = inst.position.Z + ch * shuttle_m
        Return New Vector3(x, get_Y_at_XZ_fast(x, z), z)
    End Function

    Private Shared shuttle_m As Single
    Private Shared shuttle_dir As Single = 1.0F

    ''' <summary>
    ''' Tell the shader whether this mesh recoils, which of its bones is the
    ''' barrel, and by how much.
    '''
    ''' THE GUN PART, AND ONLY WHEN IT IS SKINNED. An unskinned gun has no bone
    ''' bytes at all, so the test in the shader would read whatever the unused
    ''' attribute defaults to - which is zero, and zero is a real palette slot,
    ''' not an absence.
    '''
    ''' Resolved ONCE per mesh and cached. The palette is a property of the
    ''' visual, so the answer cannot change between frames, and a per-frame name
    ''' walk over thirty guns would be thirty string classifications a frame for
    ''' a constant.
    '''
    ''' AND THE DEFORM IS NOT A SEPARATE MECHANISM. TEPY delivers the mantlet
    ''' cover's stretch by overriding one palette slot with the inverse of the
    ''' gun's PITCH matrix, so weighted skinning interpolates between pitched and
    ''' anchored. That is a real trick and it is about pitch, which nuTerra's
    ''' guns do not have. Under RECOIL the same vertices are handled by the
    ''' weighted share in the shader: a vertex straddling barrel and mount takes
    ''' the fraction of the travel its weights put on the barrel, which is the
    ''' drape. When aim arrives, the pitch trick is the other half and goes in
    ''' upload_bones, not here.
    ''' </summary>
    Private Sub upload_recoil(part As TankPart, m As TankMesh, inst As TankInstance)
        If part.label <> "gun" OrElse m.layout Is Nothing OrElse
           m.layout.offBoneIdx < 0 Then
            GL.Uniform1(shader("u_recoil_on"), 0)
            Return
        End If

        Dim r = resolve_recoil(part, m)
        If r Is Nothing Then
            GL.Uniform1(shader("u_recoil_on"), 0)
            Return
        End If

        GL.Uniform1(shader("u_recoil_on"), 1)
        GL.Uniform1(shader("u_recoil_weighted"), If(TANK_RECOIL_WEIGHTED, 1, 0))
        GL.Uniform1(shader("u_recoil_slot"), 64, r.slots)
        GL.Uniform3(shader("u_recoil_t"), 0.0F, 0.0F, r.dir * inst.recoil.offset_m)
    End Sub

    ''' <summary>What one gun mesh needs: which slots are barrel, and which way
    ''' back is.</summary>
    Private Class RecoilPlan
        Public slots(63) As Integer
        Public dir As Single = 1.0F
        ''' <summary>The barrel's palette slot. The muzzle is the far end of
        ''' this bone's own vertices.</summary>
        Public barrel As Integer = -1
    End Class

    Private ReadOnly recoil_plans As New Dictionary(Of TankMesh, RecoilPlan)

    ''' <summary>The impacts in flight, and the pass that draws them and
    ''' every tank's own muzzle flames. Owned here because the shots are fired
    ''' here; drawn from the FX block, where the glow is.</summary>
    Public ReadOnly fx As New TankFx

    ''' <summary>Draw the muzzle flames and the impact bursts. One call from
    ''' draw_scene's FX block, so the core still touches the tank module in a
    ''' handful of named places rather than reaching into its pools.</summary>
    Public Sub DrawFx()
        If failed OrElse Not loaded Then Return
        fx.Draw(instances)
    End Sub

    ''' <summary>
    ''' Classify this gun's palette once, and work out which way the barrel
    ''' retracts.
    '''
    ''' THE DIRECTION IS MEASURED, NOT ASSUMED. A fixed axis sign is wrong on
    ''' half the corpus - the muzzle sits at +Z on some guns and -Z on others,
    ''' and getting it backwards pushes the barrel further OUT of the mantlet
    ''' instead of into it, which reads as the gun growing rather than as a
    ''' sign error. ComputeBoneHubs already leaves a weighted centroid per
    ''' palette slot, so the barrel bone's own hub says which end it is on:
    ''' recoil is back toward the origin, so the sign is the opposite of the
    ''' hub's. Nothing here has to know a convention.
    '''
    ''' Where the names give nothing - a palette with no G_ bone at all - the
    ''' hubs answer that too: the bone whose vertices sit furthest out along Z
    ''' IS the barrel, whatever it is called. TEPY needed exactly this for
    ''' A100_T49, which inverts the naming convention outright.
    ''' </summary>
    Private Function resolve_recoil(part As TankPart, m As TankMesh) As RecoilPlan
        Dim plan As RecoilPlan = Nothing
        If recoil_plans.TryGetValue(m, plan) Then Return plan

        plan = New RecoilPlan
        recoil_plans(m) = plan

        Dim palette = part.PaletteFor(m)
        plan.slots = TankRecoil.ClassifyPalette(palette)

        ' The barrel is the flagged bone reaching furthest along Z. On a twin
        ' gun both barrels are flagged and either answers the direction, since
        ' they point the same way.
        Dim best = -1
        Dim bestZ = 0.0F
        For i = 0 To 63
            If plan.slots(i) = 0 Then Continue For
            Dim z = hub_z(m, i)
            If Single.IsNaN(z) Then Continue For
            If best < 0 OrElse Math.Abs(z) > Math.Abs(bestZ) Then best = i : bestZ = z
        Next

        ' No G_ bone in the palette: fall back to geometry alone.
        If best < 0 Then
            For i = 0 To 63
                Dim z = hub_z(m, i)
                If Single.IsNaN(z) Then Continue For
                If best < 0 OrElse Math.Abs(z) > Math.Abs(bestZ) Then best = i : bestZ = z
            Next
            If best >= 0 Then
                plan.slots(best) = 1
                LogThis("tank: gun [{0}] has no G_ bone - slot {1} taken as the barrel on its hub alone (z {2:0.00})",
                        m.name, best, bestZ)
            End If
        End If

        If best < 0 Then
            recoil_plans(m) = Nothing
            Return Nothing
        End If

        plan.dir = If(bestZ >= 0.0F, -1.0F, 1.0F)
        plan.barrel = best

        ' Logged once per mesh, because "which bone is the barrel" is the whole
        ' question and a wrong answer is only visible as the wrong part sliding.
        Dim names As New List(Of String)
        If palette IsNot Nothing Then
            For i = 0 To Math.Min(palette.Count, 64) - 1
                names.Add(String.Format("{0}{1}", palette(i),
                                        If(plan.slots(i) <> 0, "*", "")))
            Next
        End If
        LogThis("tank: gun [{0}] barrel slot {1} (byte {2}) hub z {3:0.00}, back is {4}Z  [{5}]",
                m.name, best, best * 3, bestZ, If(plan.dir < 0, "-", "+"),
                String.Join(" ", names))
        Return plan
    End Function

    ''' <summary>A palette slot's centroid Z, or NaN when nothing is bound to
    ''' it.</summary>
    Private Function hub_z(m As TankMesh, slot As Integer) As Single
        If m.boneHubs Is Nothing OrElse slot < 0 OrElse slot >= m.boneHubs.Length Then
            Return Single.NaN
        End If
        Return m.boneHubs(slot).Z
    End Function


    ''' <summary>How much clear ground a tank needs, metres from its centre.
    ''' A hull is about 7 m long, so this is half of it plus a margin.</summary>
    Private Const TANK_CLEAR_R As Single = 4.5F

    ''' <summary>Tallest thing a tank may sit on. The bake's own
    ''' OBSTACLE_MIN_H is 1.0 m - anything shorter is not counted as an
    ''' obstacle by the bake either, so matching it keeps one definition of
    ''' 'blocked' across the app.</summary>
    Private Const TANK_MAX_OBSTACLE As Single = 1.0F

    ''' <summary>Most the ground may fall across the footprint. A tank on a
    ''' 2 m step over 9 m is bridging a wall, not standing on a slope.</summary>
    Private Const TANK_MAX_DROP As Single = 2.0F

    ''' <summary>
    ''' The tallest obstacle over a tank's footprint, for the log.
    '''
    ''' The same reading spot_is_clear vetoes on, printed rather than tested, so
    ''' a placement line says how close to the limit it landed. Without it a run
    ''' where nothing moved and a run where the bake was never ready look
    ''' identical in the log.
    ''' </summary>
    Private Function obstacle_at(x As Single, z As Single) As Single
        Dim b = map_scene.flight_bake
        If b Is Nothing OrElse Not b.ready Then Return -1.0F   ' no data
        Dim worst = 0.0F
        For dz = -1 To 1
            For dx = -1 To 1
                Dim sx = x + dx * TANK_CLEAR_R
                Dim sz = z + dz * TANK_CLEAR_R
                Dim c = CInt(Math.Floor((sx - b.wx_min) / (b.wx_max - b.wx_min) * MapFlightBake.SIZE))
                Dim r = CInt(Math.Floor((b.wz_max - sz) / (b.wz_max - b.wz_min) * MapFlightBake.SIZE))
                If c < 0 OrElse r < 0 OrElse c >= MapFlightBake.SIZE OrElse
                   r >= MapFlightBake.SIZE Then Continue For
                Dim i = r * MapFlightBake.SIZE + c
                Dim h = b.top_m(i) - b.floor_m(i)
                If h > worst Then worst = h
            Next
        Next
        Return worst
    End Function

    ''' <summary>
    ''' Is this spot clear enough to park on?
    '''
    ''' Tested against MapFlightBake, which is already built at load and is the
    ''' same occupancy the camera-flight planner uses to avoid geometry -
    ''' obstacle height is top_m minus floor_m per texel, models and all. Using
    ''' it rather than a new collision pass means a tank and a camera agree on
    ''' what is solid, and there is only one thing to be wrong.
    '''
    ''' Sampled over the footprint, not at the centre. A centre-only test puts
    ''' tanks neatly astride crates and walls: the one texel between them is
    ''' clear and everything around it is not.
    ''' </summary>
    Private Function spot_is_clear(x As Single, z As Single) As Boolean
        Dim b = map_scene.flight_bake
        If b Is Nothing OrElse Not b.ready Then Return True   ' no data, no veto

        Dim lo = Single.MaxValue, hi = Single.MinValue
        For dz = -1 To 1
            For dx = -1 To 1
                Dim sx = x + dx * TANK_CLEAR_R
                Dim sz = z + dz * TANK_CLEAR_R
                Dim c = CInt(Math.Floor((sx - b.wx_min) / (b.wx_max - b.wx_min) * MapFlightBake.SIZE))
                Dim r = CInt(Math.Floor((b.wz_max - sz) / (b.wz_max - b.wz_min) * MapFlightBake.SIZE))
                If c < 0 OrElse r < 0 OrElse c >= MapFlightBake.SIZE OrElse
                   r >= MapFlightBake.SIZE Then Return False   ' off the map
                Dim i = r * MapFlightBake.SIZE + c
                Dim fl = b.floor_m(i)
                If b.top_m(i) - fl > TANK_MAX_OBSTACLE Then Return False
                If fl < lo Then lo = fl
                If fl > hi Then hi = fl
            Next
        Next
        Return (hi - lo) <= TANK_MAX_DROP
    End Function

    ''' <summary>
    ''' The wanted spot if it is clear, otherwise the nearest one that is.
    '''
    ''' Searched as rings rather than a grid so the FIRST hit is also the
    ''' closest - a tank ends up beside where it was asked for rather than
    ''' somewhere arbitrary that happened to be scanned early. Rings step by
    ''' roughly the footprint so consecutive rings cannot both miss a gap.
    '''
    ''' Tanks already placed count as obstacles. Two spawn points can sit
    ''' closer together than a hull is long, and without this the second tank
    ''' parks inside the first.
    '''
    ''' Falls back to the original spot if nothing within range is clear. A tank
    ''' standing in a crate is a better failure than one teleported across the
    ''' map, and the log says which happened.
    ''' </summary>
    Private Function find_clear_spot(x As Single, z As Single,
                                     placed As List(Of Vector2)) As Vector2
        Const MAX_R As Single = 40.0F
        Dim step_m = TANK_CLEAR_R * 2.0F

        Dim ring = 0
        Do
            Dim rad = ring * step_m
            Dim n = If(ring = 0, 1, ring * 8)
            For k = 0 To n - 1
                Dim a = (k / n) * 2.0 * Math.PI
                Dim cx = x + CSng(Math.Cos(a)) * rad
                Dim cz = z + CSng(Math.Sin(a)) * rad
                If Not spot_is_clear(cx, cz) Then Continue For
                Dim busy = False
                For Each p In placed
                    Dim ddx = p.X - cx, ddz = p.Y - cz
                    If ddx * ddx + ddz * ddz < (TANK_CLEAR_R * 2.0F) ^ 2 Then
                        busy = True : Exit For
                    End If
                Next
                If busy Then Continue For
                If ring > 0 Then
                    LogThis("tank: spot ({0:0.0}, {1:0.0}) blocked - moved {2:0.0} m",
                            x, z, rad)
                End If
                Return New Vector2(cx, cz)
            Next
            ring += 1
        Loop While ring * step_m <= MAX_R

        LogThis("tank: no clear ground within {0:0} m of ({1:0.0}, {2:0.0}) - placed anyway",
                MAX_R, x, z)
        Return New Vector2(x, z)
    End Function

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
            If mat.alphaTestEnable AndAlso Not logged_alpha.Contains(mat.fx) Then
                logged_alpha.Add(mat.fx)
                LogThis("tank: alpha test on [{0}] ref {1}/255 source normalMap.{2}",
                        IO.Path.GetFileName(mat.fx), mat.alphaReference,
                        If(mat.useNormalPackDXT1, "b", "r"))
            End If

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
        cards?.Dispose()
        cards = Nothing
        fx.Dispose()
        For Each v In vehicles
            v.Dispose()
        Next
        vehicles.Clear()
        instances.Clear()
    End Sub
End Class

Imports System.IO
Imports OpenTK.Mathematics

Module modSpaceBin
    Public sectionHeaders As Dictionary(Of String, SectionHeader)
    Public materials As Dictionary(Of UInt32, Material)

    Public Structure SectionHeader
        Public magic As String
        Public version As Int32
        Public offset As Int64
        Public length As Int64

        Public Sub New(br As BinaryReader)
            magic = br.ReadChars(4)
            version = br.ReadInt32
            offset = br.ReadInt64
            length = br.ReadInt64
        End Sub
    End Structure

    Private Sub ShowDecodeFailedMessage(ex As Exception, magic As String)
        Debug.Print(ex.ToString)
        MsgBox(String.Format("{0} decode Failed", magic), MsgBoxStyle.Exclamation, "Oh NO!!")
    End Sub

    ''' <summary>
    ''' Debug trap for the material loader's unhandled cases.
    '''
    ''' Every one of these was a bare Stop, and outside a debugger Stop is a
    ''' silent no-op - so an unsupported fx, an unknown material property or a
    ''' new property type simply vanished and the model rendered wrong with
    ''' nothing said anywhere.
    '''
    ''' Recorded once per distinct message, with a count: a property that shows
    ''' up on ten thousand models would otherwise bury the log. DumpTraps prints
    ''' the tally when the space.bin finishes loading.
    ''' </summary>
    Private ReadOnly TRAPPED As New Dictionary(Of String, Integer)
    Private ReadOnly TRAP_FIRST As New Dictionary(Of String, String)
    Private ReadOnly TRAP_MAPS As New Dictionary(Of String, HashSet(Of String))

    ''' <summary>Directories of models carrying a glow material, so the
    ''' placements can be counted and located once MODEL_INDEX_LIST exists.</summary>
    Private ReadOnly GLOW_MODEL_DIRS As New HashSet(Of String)
    Public ReadOnly VOLUMETRIC_MODEL_DIRS As New HashSet(Of String)

    Public Sub Trap(kind As String, detail As String, context As String)
        Dim key = kind & ": " & detail

        ' Which maps a case turns up on is the useful part when hunting: a
        ' property on one event space is a different problem from one on
        ' every map in the game.
        If Not TRAP_MAPS.ContainsKey(key) Then
            TRAP_MAPS(key) = New HashSet(Of String)
        End If
        TRAP_MAPS(key).Add(MAP_NAME_NO_PATH)

        If TRAPPED.ContainsKey(key) Then
            TRAPPED(key) += 1
            Return
        End If
        TRAPPED(key) = 1
        TRAP_FIRST(key) = context
    End Sub

    ''' <summary>
    ''' Where the glow cards actually are. They are small alpha-cut props, so
    ''' "is it working" is unanswerable without knowing whether any are even
    ''' placed, and where to go and look.
    ''' </summary>
    Public Sub ReportGlowPlacements()
        If GLOW_MODEL_DIRS.Count = 0 Then Return

        ReportPlacements("glow", GLOW_MODEL_DIRS)
    End Sub

    ''' <summary>
    ''' How many instances of a set of models are actually placed. A material
    ''' existing proves only that the loader saw it; without a placement count
    ''' there is no telling "not drawn" from "drawn but invisible".
    ''' </summary>
    Private Sub ReportPlacements(kind As String, dirs As HashSet(Of String))
        If dirs.Count = 0 Then Return
        Dim per As New Dictionary(Of String, Integer)
        Dim first_pos As New Dictionary(Of String, String)
        Dim total = 0
        For Each entry In MODEL_INDEX_LIST
            If entry.model_index < 0 OrElse entry.model_index >= MAP_MODELS.Length Then Continue For
            Dim lods = MAP_MODELS(entry.model_index).modelLods
            If lods Is Nothing OrElse lods.Length = 0 Then Continue For
            Dim sets = lods(0).render_sets
            If sets Is Nothing OrElse sets.Count = 0 Then Continue For
            Dim dir = IO.Path.GetDirectoryName(sets(0).verts_name)
            If Not dirs.Contains(dir) Then Continue For

            Dim leaf = IO.Path.GetFileName(dir)
            If per.ContainsKey(leaf) Then per(leaf) += 1 Else per(leaf) = 1

            total += 1
            ' One position per distinct model, not the first three overall -
            ' otherwise a model with many instances hides every other one, and
            ' there is no way to cross-check a known landmark's coordinates
            ' against what the camera's LOOK_AT uses.
            If Not first_pos.ContainsKey(leaf) Then
                Dim pos = entry.matrix.Row3
                first_pos(leaf) = String.Format("({0:0.#}, {1:0.#}, {2:0.#})", pos.X, pos.Y, pos.Z)
            End If
        Next
        For Each kv In per
            LogThis("   {0}: {1,3} x {2,-42} first at {3}", kind, kv.Value, kv.Key, first_pos(kv.Key))
        Next
        LogThis("{0} placements: {1} instance(s) from {2} model(s)", kind, total, dirs.Count)
    End Sub

    ''' <summary>Everything the loader could not handle, most frequent first.</summary>
    Public Sub DumpTraps()
        If TRAPPED.Count = 0 Then
            LogThis("loader traps: none - every fx, property and property type was handled")
            Return
        End If

        LogThis("loader traps: {0} distinct case(s) the material loader does not handle", TRAPPED.Count)
        Dim keys As New List(Of String)(TRAPPED.Keys)
        keys.Sort(Function(a, b) TRAPPED(b).CompareTo(TRAPPED(a)))
        For Each k In keys
            LogThis("   x{0}  {1}", TRAPPED(k), k)
            LogThis("        maps: {0}", String.Join(", ", TRAP_MAPS(k)))
            LogThis("        first seen on {0}", TRAP_FIRST(k))
        Next
    End Sub

    Public Function ReadSpaceBinData(ByRef ms As MemoryStream) As Boolean


        Using br As New BinaryReader(ms)
            br.BaseStream.Position = &H14
            Dim table_size = br.ReadInt32

            sectionHeaders = New Dictionary(Of String, SectionHeader)

            ' read each entry in the header table
            For i = 0 To table_size - 1
                Dim header As New SectionHeader(br)
                sectionHeaders.Add(header.magic, header)
            Next

            '------------------------------------------------------------------
            ' Now we will grab the game data we need.
            '------------------------------------------------------------------

            Try
                cBWST = New cBWST_(sectionHeaders("BWST"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWST")
                GoTo Failed
            End Try

            Try
                cBWT2 = New cBWT2_(sectionHeaders("BWT2"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWT2")
                GoTo Failed
            End Try

            Try
                cBSMI = New cBSMI_(sectionHeaders("BSMI"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMI")
                GoTo Failed
            End Try

            Try
                cBSMO = New cBSMO_(sectionHeaders("BSMO"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMO")
                GoTo Failed
            End Try

            Try
                get_BSMA(sectionHeaders("BSMA"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMA")
                GoTo Failed
            End Try

            Try
                get_WGSD(sectionHeaders("WGSD"), br)
                ' The decal priority sort used to happen here, on
                ' DECAL_INDEX_LIST. That array is written and never read by
                ' anything, so the sort had no effect on what was drawn. The
                ' real one is in MapLoader.build_decals, on the all_decals list
                ' the renderer actually walks.
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "WGSD")
                GoTo Failed
            End Try

            Try
                cBWWa = New cBWWa_(sectionHeaders("BWWa"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWWa")
                GoTo Failed
            End Try

            ' World box of the SH probe grid. Not fatal: a map without a baked
            ' probe field still lights from the single global probe.
            Try
                If sectionHeaders.ContainsKey("WGSH") Then
                    get_WGSH(sectionHeaders("WGSH"), br)
                Else
                    WGSH_LOADED = False
                End If
            Catch ex As Exception
                Debug.Print(ex.ToString)
                LogThis("WGSH decode failed, the probe grid will be skipped")
                WGSH_LOADED = False
            End Try

            ' SpeedTree placement. Not fatal: a map without trees still loads.
            Try
                If sectionHeaders.ContainsKey("SpTr") Then
                    cSpTr = New cSpTr_(sectionHeaders("SpTr"), br)
                End If
            Catch ex As Exception
                Debug.Print(ex.ToString)
                LogThis("SpTr decode failed, trees will be skipped")
                cSpTr = Nothing
            End Try
        End Using

        ms.Dispose()

        Dim destroyed_model_ids As New List(Of Integer)
        For k = 0 To cBSMO.model_info_items.count - 1
            Dim modelInfo = cBSMO.model_info_items.data(k)
            Select Case modelInfo.type
                Case 0
                    ' Static, doing nothing
                Case 1
                    ' Falling, doing nothing
                Case 2
                    ' Fragile
                    Dim fragile_info = cBSMO.fragile_model_info_items.data(modelInfo.info_index)
                    If fragile_info.destroyed_model_index <> &HFFFFFFFFUI Then
                        destroyed_model_ids.Add(fragile_info.destroyed_model_index)
                    End If
                Case Else
                    Debug.Assert(False, modelInfo.type.ToString)
            End Select
        Next

        '----------------------------------------------------------------------------------
        'build the model information
        materials = New Dictionary(Of UInt32, Material)
        ReDim MAP_MODELS(cBSMO.models_colliders.count - 1)
        For k = 0 To cBSMO.models_colliders.count - 1
            With MAP_MODELS(k)
                Dim lod0_offset = cBSMO.models_loddings.data(k).lod_begin
                Dim lodx_offset = cBSMO.models_loddings.data(k).lod_end

                ' max lod count = 4 for now
                Dim lod_count = Math.Min(6, lodx_offset - lod0_offset + 1)
                If lod_count >= 4 Then lod_count -= 1
                For i = 0 To lod_count - 1
                    Dim lod_offset = lod0_offset + i
                    Dim lod_render_set_begin = cBSMO.lod_renders.data(lod_offset).render_set_begin
                    Dim lod_render_set_end = cBSMO.lod_renders.data(lod_offset).render_set_end
                    If lod_render_set_end < lod_render_set_begin Then
                        lod_count -= 1
                    End If
                Next

                ReDim .modelLods(lod_count - 1)
                .visibilityBounds = cBSMO.models_visibility_bounds.data(k)

                For i = 0 To lod_count - 1
                    .modelLods(i) = New base_model_holder_
                    Dim lod_offset = lod0_offset + i

                    Dim lod_render_set_begin = cBSMO.lod_renders.data(lod_offset).render_set_begin
                    Dim lod_render_set_end = cBSMO.lod_renders.data(lod_offset).render_set_end

                    Dim num_render_sets = lod_render_set_end - lod_render_set_begin + 1
                    Debug.Assert(num_render_sets > 0)

                    ' Creating renderSets
                    .modelLods(i).render_sets = New List(Of RenderSetEntry)
                    Dim dict As New Dictionary(Of String, Integer)
                    For z As UInteger = 0 To num_render_sets - 1
                        Dim renderItem = cBSMO.renders.data(lod_render_set_begin + z)
                        Dim verts_name = cBWST.find_str(renderItem.verts_name_fnv)
                        Dim prims_name = cBWST.find_str(renderItem.prims_name_fnv)

                        Dim pGroup As New PrimitiveGroup
                        apply_material_for_pgroup(pGroup, renderItem.material_index, Path.GetDirectoryName(verts_name))

                        If Not dict.ContainsKey(verts_name) Then
                            Dim rs As New RenderSetEntry With {
                                .verts_name = verts_name,
                                .prims_name = prims_name,
                                .primitiveGroups = New Dictionary(Of Integer, PrimitiveGroup)
                            }
                            rs.primitiveGroups(renderItem.primtive_index) = pGroup
                            dict(verts_name) = .modelLods(i).render_sets.Count
                            .modelLods(i).render_sets.Add(rs)
                        Else
                            .modelLods(i).render_sets(dict(verts_name)).primitiveGroups(renderItem.primtive_index) = pGroup
                        End If
                    Next
                Next
            End With
        Next

        ReDim MODEL_INDEX_LIST(cBSMI.model_BSMO_indexes.count - 1)
        Dim cnt As Integer = 0

        Dim j = 0
        For k = 0 To cBSMI.model_BSMO_indexes.count - 1
            If Not cBSMI.visibility_masks.data(k).mask.HasFlag(VisbilityFlags.CAPTURE_THE_FLAG) Then
                Continue For
            End If

            Dim bsmo_id = cBSMI.model_BSMO_indexes.data(k).BSMO_MODEL_INDEX
            MODEL_INDEX_LIST(j).model_index = bsmo_id
            MODEL_INDEX_LIST(j).matrix = cBSMI.transforms.data(k)

            'Flip some row values to convert from DirectX to Opengl
            MODEL_INDEX_LIST(j).matrix.M12 *= -1.0
            MODEL_INDEX_LIST(j).matrix.M13 *= -1.0
            MODEL_INDEX_LIST(j).matrix.M21 *= -1.0
            MODEL_INDEX_LIST(j).matrix.M31 *= -1.0
            MODEL_INDEX_LIST(j).matrix.M41 *= -1.0
            j += 1
        Next
        ' j entries were filled (0..j-1), so the length is j - resizing to
        ' j - 1 silently dropped the last accepted instance on every map.
        Array.Resize(MODEL_INDEX_LIST, j)

        ReadSpaceBinData = True
        GoTo CleanUp

Failed:
        ReadSpaceBinData = False

CleanUp:
        'Clear headers
        sectionHeaders = Nothing

        'Clear Sections
        'cBWST = Nothing
        'cBWT2 = Nothing
        cBSMI = Nothing
        cBSMO = Nothing
        cBSMA = Nothing
        'cWGSD = Nothing
        ' Kept, like the others commented out above: MapWater.Build reads the
        ' bodies and mesh from it AFTER this function returns. Nulling it here
        ' is why water silently never appeared - the parse succeeded and the
        ' data was freed in the same breath. ~100 KB retained per map.
        'cBWWa = Nothing

        '====================================================
        ' Sort and batch the models for instanced drawing
        '====================================================
        Array.Sort(MODEL_INDEX_LIST) 'sort our list by model_index


        MODEL_BATCH_LIST = New List(Of ModelBatch)

        Dim tmpDict As New Dictionary(Of Integer, Integer)

        For i = 0 To MODEL_INDEX_LIST.Length - 1
            Dim id = MODEL_INDEX_LIST(i).model_index
            If tmpDict.ContainsKey(id) Then
                tmpDict(id) += 1
            Else
                tmpDict(id) = 1
            End If
        Next

        Dim offset As Integer = 0
        For Each it In tmpDict
            If MAP_MODELS(it.Key).modelLods(0).junk Then
                offset += it.Value
                Continue For
            End If

            Dim batch As New ModelBatch With {
                .model_id = it.Key,
                .count = it.Value,
                .offset = offset
            }
            MODEL_BATCH_LIST.Add(batch)
            offset += it.Value
        Next

        ReportGlowPlacements()
        ReportPlacements("volumetric", VOLUMETRIC_MODEL_DIRS)

        ' Everything the material loader could not account for, in one place.
        DumpTraps()
    End Function

    Private Sub apply_material_for_pgroup(pGroup As PrimitiveGroup, material_id As Integer, ByVal model_name As String)

        Dim item = cBSMA.MaterialItem(material_id)

        If item.shaderPropBegin = &HFFFFFFFFUI Then
            pGroup.no_draw = True
            Return
        End If

        If item.effectIndex = &HFFFFFFFFUI Then
            pGroup.no_draw = True
            Return
        End If

        If materials.ContainsKey(material_id) Then
            pGroup.material_id = materials(material_id).id
        Else
            pGroup.material_id = materials.Count
            Dim mat As New Material
            mat.id = pGroup.material_id

            Dim props As New Dictionary(Of String, Object)
            Dim fx = cBSMA.FXStringKey(item.effectIndex).FX_string

            For i = item.shaderPropBegin To item.shaderPropEnd
                With cBSMA.ShaderPropertyItem(i)
                    Select Case .property_type
                        Case 0 ' special case for volumetrics.
                            props(.property_name_string) = .val_int

                        Case 1
                            ' Bool
                            props(.property_name_string) = .val_boolean

                        Case 2
                            ' Float
                            props(.property_name_string) = .val_float

                        Case 3
                            ' Int
                            props(.property_name_string) = .val_int

                        Case 4
                            ' Never seen in the wild - trapped so it cannot pass silently.
                                Trap("property type", "type 4 on " & .property_name_string, model_name)

                        Case 5
                            ' Vector4
                            props(.property_name_string) = .val_vec4

                        Case 6
                            ' Texture
                            'If .property_value_string.ToLower.Contains("dirt_pchurch_01_dm") Then
                            '    Debug.WriteLine(.property_name_string)
                            'End If
                            props(.property_name_string) = .property_value_string
                            'There is probably a better place to do this
                            'where it isnt checking every single texture!
                            If props.ContainsKey("dirtMap") Then
                                Dim s As String = props("dirtMap")
                                If s.ToLower.Contains("dirt_pchurch_01_dm") Then
                                    props("dirtMap") = Replace(s, "/Tiles/", "/00_Tiles/")
                                    'Debug.WriteLine(props("dirtMap"))
                                End If
                            End If
                        Case Else
                                Trap("property type", "type " & .property_type.ToString() & " on " & .property_name_string, model_name)
                    End Select
                End With
            Next
            Select Case fx
                Case "shaders/std_effects/PBS_ext.fx", "shaders/std_effects/PBS_ext_skinned.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "diffuseMap",
                        "normalMap",
                        "metallicGlossMap",
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "g_useNormalPackDXT1",
                        "g_enableTerrainBlending",
                        "g_enableAO",
                        "g_vertexAnimationParams",
                        "g_vertexColorMode",
                        "dynamicObject",
                        "g_enableTransmission",
                        "g_tintColor",
                        "g_useTintColor",
                        "texAddressMode",
                        "selfIllumination",
                        "applyOverlay",
                        "g_repaintColor",
                        "g_baseColor",
                        "dirtAlbedoMap",
                        "glassMap",
                        "g_applyScreenSpaceMorphing",
                        "g_glossConversions",
                        "g_metallicConversions",
                        "g_aging",
                        "g_albedoConversions",
                        "g_defaultPBSConversionParams",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_ext
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        .normalMap = If(props.ContainsKey("normalMap"), props("normalMap").ToLower, props("diffuseMap").ToLower) ' HACK
                        .metallicGlossMap = If(props.ContainsKey("metallicGlossMap"), props("metallicGlossMap").ToLower, props("diffuseMap").ToLower) ' HACK: use system/maps/default_norms.dds ?
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        'force double sided if its a border model
                        If model_name.Contains("Borders") Then
                            .doubleSided = True
                        End If
                        .g_useNormalPackDXT1 = If(props.ContainsKey("g_useNormalPackDXT1"), props("g_useNormalPackDXT1"), False)
                        '.g_useTintColor = If(props.ContainsKey("g_useTintColor"), props("g_useTintColor"), False)
                        .g_enableAO = If(props.ContainsKey("g_enableAO"), props("g_enableAO"), False)
                        .g_colorTint = If(props.ContainsKey("g_colorTint"), props("g_colorTint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_ext
                    mat.props = obj

                Case "shaders/std_effects/PBS_ext_dual.fx", "shaders/std_effects/PBS_ext_skinned_dual.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "diffuseMap",
                        "diffuseMap2",
                        "normalMap",
                        "metallicGlossMap",
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "g_useNormalPackDXT1",
                        "g_enableAO",
                        "g_vertexColorMode",
                        "g_enableTerrainBlending",
                        "g_vertexAnimationParams",
                        "g_useTintColor",
                        "g_tintColor",
                        "g_enableTransmission",
                        "texAddressMode",
                        "dynamicObject",
                        "selfIllumination",
                        "applyOverlay",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_ext_dual
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        .diffuseMap2 = props("diffuseMap2").ToLower
                        .normalMap = props("normalMap").ToLower
                        .metallicGlossMap = props("metallicGlossMap").ToLower
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        .g_useNormalPackDXT1 = If(props.ContainsKey("g_useNormalPackDXT1"), props("g_useNormalPackDXT1"), False)
                        '.g_useTintColor = If(props.ContainsKey("g_useTintColor"), props("g_useTintColor"), False)
                        .g_colorTint = If(props.ContainsKey("g_colorTint"), props("g_colorTint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        If props.ContainsKey("g_useTintColor") Then
                            If props("g_useTintColor") = "True" Then
                                Trap("unexpected prop", fx & " -> g_useTintColor=True", model_name)
                            End If
                        End If
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_ext_dual
                    mat.props = obj

                Case "shaders/std_effects/PBS_ext_detail.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "diffuseMap",
                        "normalMap",
                        "metallicGlossMap",
                        "g_detailMap",
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "g_useNormalPackDXT1",
                        "g_detailInfluences",
                        "g_detailRejectTiling",
                        "g_enableTerrainBlending",
                        "g_useTintColor",
                        "g_vertexColorMode",
                        "dynamicObject",
                        "g_enableTransmission",
                        "g_vertexAnimationParams",
                        "g_tintColor",
                        "g_enableAO",
                        "g_metalReject",
                        "g_glossReject",
                        "g_normalMapInfluence",
                        "g_glossMapInfluence",
                        "g_albedoMapInfluence",
                        "g_tile",
                        "texAddressMode",
                        "g_applyScreenSpaceMorphing",
                        "applyOverlay",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_ext_detail
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        .normalMap = props("normalMap").ToLower
                        .metallicGlossMap = props("metallicGlossMap").ToLower
                        .g_detailMap = If(props.ContainsKey("g_detailMap"), props("g_detailMap").ToLower, Nothing)
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        .g_useNormalPackDXT1 = If(props.ContainsKey("g_useNormalPackDXT1"), props("g_useNormalPackDXT1"), False)
                        '.g_useTintColor = If(props.ContainsKey("g_useTintColor"), props("g_useTintColor"), False)
                        .g_colorTint = If(props.ContainsKey("g_colorTint"), props("g_colorTint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        .g_enableAO = If(props.ContainsKey("g_enableAO"), props("g_enableAO"), False)
                        .g_detailInfluences = If(props.ContainsKey("g_detailInfluences"), props("g_detailInfluences"), New Vector4(1.0F, 0.0F, 0.0F, 0.0F))
                        .g_detailRejectTiling = If(props.ContainsKey("g_detailRejectTiling"), props("g_detailRejectTiling"), New Vector4(20.0F, 20.0F, 0.0F, 0.0F))

                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_ext_detail
                    mat.props = obj

                Case "shaders/std_effects/PBS_tiled_atlas.fx", "shaders/std_effects/PBS_tiled_atlas_rigid_skinned.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "g_atlasSizes",
                        "g_atlasIndexes",
                        "atlasNormalGlossSpec",
                        "atlasMetallicAO",
                        "atlasBlend",
                        "atlasAlbedoHeight",
                        "g_dirtParams",
                        "g_dirtColor",
                        "dirtMap",
                        "g_tile0Tint",
                        "g_tile1Tint",
                        "g_tile2Tint",
                        "g_fakeShadowsParams",
                        "g_enableTerrainBlending",
                        "dynamicObject",
                        "texAddressMode",
                        "selfIllumination",
                        "diffuseMap",
                        "applyOverlay",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_tiled_atlas
                    With obj
                        .atlasAlbedoHeight = props("atlasAlbedoHeight").ToLower
                        .atlasBlend = props("atlasBlend").ToLower
                        .atlasNormalGlossSpec = props("atlasNormalGlossSpec").ToLower
                        .atlasMetallicAO = props("atlasMetallicAO").ToLower

                        .dirtMap = If(props.ContainsKey("dirtMap"), props("dirtMap").ToLower, Nothing)
                        .dirtColor = If(props.ContainsKey("dirtColor"), props("dirtColor"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .dirtParams = If(props.ContainsKey("dirtParams"), props("dirtParams"), New Vector4(1.0, 1.0, 1.0, 1.0))

                        .g_atlasIndexes = If(props.ContainsKey("g_atlasIndexes"), props("g_atlasIndexes"), New Vector4(0, 0, 0, 0))
                        .g_atlasSizes = If(props.ContainsKey("g_atlasSizes"), props("g_atlasSizes"), New Vector4(4, 4, 8, 4))

                        'Stupid hacks for missing or incorrect atlas sizes
                        If model_name.Contains("hd_bld_UNI_005_Hangar\normal\") Then
                            .g_atlasSizes = New Vector4(3, 2, 4, 4)
                        End If
                        If Not props.ContainsKey("g_atlasSizes") Then

                            .g_atlasSizes = New Vector4(4, 4, 8, 4) 'default
                        End If

                        .g_tile0Tint = If(props.ContainsKey("g_tile0Tint"), props("g_tile0Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile1Tint = If(props.ContainsKey("g_tile1Tint"), props("g_tile1Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile2Tint = If(props.ContainsKey("g_tile2Tint"), props("g_tile2Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tileUVScale = If(props.ContainsKey("g_tileUVScale"), props("g_tileUVScale"), New Vector4(1.0, 1.0, 1.0, 1.0))

                        If props.ContainsKey("g_tintColor") Then
                            Trap("unexpected prop", fx & " -> g_tintColor", model_name)
                        End If
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_tiled_atlas
                    mat.props = obj

                Case "shaders/std_effects/PBS_tiled_atlas_global.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "g_atlasSizes",
                        "g_atlasIndexes",
                        "atlasNormalGlossSpec",
                        "atlasMetallicAO",
                        "atlasBlend",
                        "atlasAlbedoHeight",
                        "g_dirtParams",
                        "g_dirtColor",
                        "dirtMap",
                        "g_tile0Tint",
                        "g_tile1Tint",
                        "g_tile2Tint",
                        "g_fakeShadowsParams",
                        "g_enableTerrainBlending",
                        "dynamicObject",
                        "texAddressMode",
                        "selfIllumination",
                        "diffuseMap",
                        "applyOverlay",
                        "globalTex",
                        "g_applyScreenSpaceMorphing",
                        "g_tileUVScale",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_atlas_global
                    With obj
                        .atlasAlbedoHeight = props("atlasAlbedoHeight").ToLower
                        .atlasBlend = props("atlasBlend").ToLower
                        .atlasNormalGlossSpec = props("atlasNormalGlossSpec").ToLower
                        .atlasMetallicAO = props("atlasMetallicAO").ToLower
                        .dirtMap = If(props.ContainsKey("dirtMap"), props("dirtMap").ToLower, Nothing)
                        .globalTex = props("globalTex").ToLower

                        .dirtColor = If(props.ContainsKey("dirtColor"), props("dirtColor"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .dirtParams = If(props.ContainsKey("dirtParams"), props("dirtParams"), New Vector4(1.0, 1.0, 1.0, 1.0))

                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)

                        .g_atlasIndexes = If(props.ContainsKey("g_atlasIndexes"), props("g_atlasIndexes"), New Vector4(0, 0, 0, 0))
                        .g_atlasSizes = If(props.ContainsKey("g_atlasSizes"), props("g_atlasSizes"), New Vector4(0, 0, 0, 0))

                        If .atlasMetallicAO = "content/outland/00_atlases/hd_out_na_47_mountain_main_mao.atlas" Then
                            ' HACK!!!!!!
                            .g_atlasIndexes.Z = 0
                        End If

                        If Not props.ContainsKey("g_atlasIndexes") Then


                        End If
                        'hack! Must supply missing atlas sizes!

                        If Not props.ContainsKey("g_atlasSizes") Then
                            'some entire folders use the same atlas sizes.
                            'Some DONT.Every model must be checked that is missing atlas sizes.

                            If model_name.Contains("hd_env_EU_001_Cliff_rocks\normal\") Then
                                .g_atlasSizes = New Vector4(2, 2, 8, 1)
                                GoTo got_it
                            End If

                            If model_name.Contains("hd_out_EU_002_Talus\normal\") Then
                                .g_atlasSizes = New Vector4(2, 2, 8, 1)
                                GoTo got_it
                            End If

                            If model_name.Contains("hd_env_EU_003_Cliff_rocks\normal\") Then
                                .g_atlasSizes = New Vector4(2, 2, 8, 1)
                                GoTo got_it
                            End If

                            If model_name.Contains("hd_envAF_033_Cliff_rocks\normal\lod0\hd_envAF_033_Cliff_rock_02.primitives") Then
                                .g_atlasSizes = New Vector4(2, 2, 8, 1)
                                GoTo got_it
                            End If
                            If model_name.Contains("hd_envAF_033_Cliff_rocks\normal\lod0\hd_envAF_033_Cliff_rock_01.primitives") Then
                                .g_atlasSizes = New Vector4(4, 4, 8, 1)
                                GoTo got_it
                            End If
                            If model_name.Contains("hd_envAF_033_Cliff_rocks\normal\lod0\hd_envAF_033_Cliff_rock_03.primitives") Then
                                .g_atlasSizes = New Vector4(4, 4, 8, 1)
                                GoTo got_it
                            End If
                            If model_name.Contains("hd_envAF_033_Cliff_rocks\normal\lod0\hd_envAF_033_Cliff_rock_05.primitives") Then
                                .g_atlasSizes = New Vector4(4, 4, 8, 1)
                                GoTo got_it
                            End If

                            '-------------------------------------------------------------------------------------------------
                            LogThis("atlas_global: Missing Atlas Size: {0}", props("atlasAlbedoHeight"))
                            LogThis("Model: {0}", model_name)

                            Dim visual_xml = ResMgr.openXML(model_name.Replace(".primitives", ".visual_processed"))
                            If visual_xml IsNot Nothing Then
                                LogThis("Visual")
                                LogThis(visual_xml.InnerXml + vbCrLf)
                            End If
                            '-------------------------------------------------------------------------------------------------
                            .g_atlasSizes = New Vector4(4, 4, 8, 4) 'default
                        End If
got_it:
                        .g_tile0Tint = If(props.ContainsKey("g_tile0Tint"), props("g_tile0Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile1Tint = If(props.ContainsKey("g_tile1Tint"), props("g_tile1Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile2Tint = If(props.ContainsKey("g_tile2Tint"), props("g_tile2Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))

                        .g_tileUVScale = If(props.ContainsKey("g_tileUVScale"), props("g_tileUVScale"), New Vector4(1.0, 1.0, 1.0, 1.0))

                        If props.ContainsKey("g_tintColor") Then 'Just in case. Remove after serious testing!
                            Trap("unexpected prop", fx & " -> g_tintColor", model_name)
                        End If
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_tiled_atlas_global
                    mat.props = obj


                Case "shaders/std_effects/PBS_tiled.fx", "shaders/std_effects/PBS_tiled_skinned.fx"
                    ' The skinned variant rides the same path as its static
                    ' kin (the PBS_ext / atlas_rigid_skinned precedent): the
                    ' iiiww vertex formats already parse, and a viewer renders
                    ' the bind pose.
                    Dim knownPropNames As New HashSet(Of String)({
                        "albedoHeightTile0", "normalGlossSpecTile0", "metallicAOTile0",
                        "albedoHeightTile1", "normalGlossSpecTile1", "metallicAOTile1",
                        "albedoHeightTile2", "normalGlossSpecTile2", "metallicAOTile2",
                        "blendMask",
                        "dirtMap",
                        "colorTex",
                        "g_tile0Tint",
                        "g_tile1Tint",
                        "g_tile2Tint",
                        "g_dirtColor",
                        "g_dirtColorParams",
                        "g_fakeShadowsAndDetailParams",
                        "g_atlasSizes",
                        "g_enableTerrainBlending",
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "applyOverlay",
                        "dynamicObject",
                        "texAddressMode",
                        "selfIllumination",
                        "diffuseMap",
                        "ditherTestEnable"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_tiled
                    With obj
                        .albedoHeightTile0 = props("albedoHeightTile0").ToLower
                        .normalGlossSpecTile0 = props("normalGlossSpecTile0").ToLower
                        .metallicAOTile0 = props("metallicAOTile0").ToLower
                        .albedoHeightTile1 = props("albedoHeightTile1").ToLower
                        .normalGlossSpecTile1 = props("normalGlossSpecTile1").ToLower
                        .metallicAOTile1 = props("metallicAOTile1").ToLower
                        .albedoHeightTile2 = props("albedoHeightTile2").ToLower
                        .normalGlossSpecTile2 = props("normalGlossSpecTile2").ToLower
                        .metallicAOTile2 = props("metallicAOTile2").ToLower
                        ' blendMask is always shipped as .png but only exists as .dds
                        .blendMask = props("blendMask").ToLower.Replace(".png", ".dds")
                        .dirtMap = If(props.ContainsKey("dirtMap"), props("dirtMap").ToLower, Nothing)
                        .colorTex = props("colorTex").ToLower
                        .g_tile0Tint = If(props.ContainsKey("g_tile0Tint"), props("g_tile0Tint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        .g_tile1Tint = If(props.ContainsKey("g_tile1Tint"), props("g_tile1Tint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        .g_tile2Tint = If(props.ContainsKey("g_tile2Tint"), props("g_tile2Tint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        .g_dirtColor = If(props.ContainsKey("g_dirtColor"), props("g_dirtColor"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        .g_dirtColorParams = If(props.ContainsKey("g_dirtColorParams"), props("g_dirtColorParams"), New Vector4(0.0F, 1.0F, 1.0F, 0.0F))
                        .g_fakeShadowsAndDetailParams = If(props.ContainsKey("g_fakeShadowsAndDetailParams"), props("g_fakeShadowsAndDetailParams"), New Vector4(0.0F, 0.0F, 0.0F, 0.0F))
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_tiled
                    mat.props = obj

                Case "shaders/std_effects/PBS_tiled_global.fx"
                    ' PBS_tiled plus a per-object global set (blend mask,
                    ' colorTex GCM, globalTex GNM), heavy on the newer maps.
                    ' Semantics transcribed from the fxo - the tile
                    ' normalGlossSpec/metallicAO textures are authored but
                    ' never sampled by the game's techniques, so they are
                    ' known-listed and skipped.
                    Dim knownPropNames As New HashSet(Of String)({
                        "albedoHeightTile0", "normalGlossSpecTile0", "metallicAOTile0",
                        "albedoHeightTile1", "normalGlossSpecTile1", "metallicAOTile1",
                        "albedoHeightTile2", "normalGlossSpecTile2", "metallicAOTile2",
                        "blendMask",
                        "dirtMap",
                        "colorTex",
                        "globalTex",
                        "g_tileUVScale",
                        "g_tintParams",
                        "g_dirtColorParams",
                        "g_dirtColor",
                        "g_fakeShadowsAndDetailParams",
                        "g_enableTerrainBlending",
                        "applyOverlay",
                        "alphaReference",
                        "alphaTestEnable",
                        "doubleSided",
                        "dynamicObject",
                        "texAddressMode",
                        "ditherTestEnable",
                        "g_ditherCoeff"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            LogThis("PBS_tiled_global: unknown property '{0}' = {1} on {2}", name, props(name), model_name)
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_tiled_global
                    With obj
                        .albedoHeightTile0 = props("albedoHeightTile0").ToLower
                        .albedoHeightTile1 = props("albedoHeightTile1").ToLower
                        .albedoHeightTile2 = props("albedoHeightTile2").ToLower
                        ' blendMask is authored as .png but ships as .dds
                        .blendMask = props("blendMask").ToLower.Replace(".png", ".dds")
                        .dirtMap = If(props.ContainsKey("dirtMap"), props("dirtMap").ToLower, Nothing)
                        .colorTex = props("colorTex").ToLower
                        .globalTex = props("globalTex").ToLower
                        .g_tileUVScale = If(props.ContainsKey("g_tileUVScale"), props("g_tileUVScale"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))
                        ' Register defaults from the fxo reflection.
                        .g_tintParams = If(props.ContainsKey("g_tintParams"), props("g_tintParams"), Vector4.Zero)
                        .g_dirtColorParams = If(props.ContainsKey("g_dirtColorParams"), props("g_dirtColorParams"), New Vector4(0.1F, 0.0F, 0.0F, 0.0F))
                        .g_dirtColor = If(props.ContainsKey("g_dirtColor"), props("g_dirtColor"), New Vector4(0.47F, 0.43F, 0.38F, 1.0F))
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_tiled_global
                    mat.props = obj

                Case "shaders/std_effects/PBS_glass.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "dirtAlbedoMap",
                        "normalMap",
                        "glassMap",
                        "alphaReference",
                        "g_filterColor",
                        "texAddressMode",
                        "doubleSided",
                        "selfIllumination",
                        "applyOverlay",
                        "alphaTestEnable",
                        "g_applyOverlay"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next
                    Dim obj As New MaterialProps_PBS_glass
                    With obj
                        .dirtAlbedoMap = props("dirtAlbedoMap")
                        .normalMap = If(props.ContainsKey("normalMap"), props("normalMap").ToLower, props("dirtAlbedoMap").ToLower) ' HACK
                        .glassMap = If(props.ContainsKey("glassMap"), props("glassMap").ToLower, props("dirtAlbedoMap").ToLower) ' HACK

                        If props.ContainsKey("alphaTestEnable") Then
                            .alphaTestEnable = props("alphaTestEnable")
                        End If
                        If props.ContainsKey("alphaReference") Then
                            .alphaTestEnable = props("alphaReference")
                        End If
                        .g_filterColor = If(props.ContainsKey("g_filterColor"), props("g_filterColor"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .texAddressMode = If(props.ContainsKey("texAddressMode"), props("texAddressMode"), 0)
                        If props.ContainsKey("texAddressMode") Then Debug.WriteLine("adressMode:" + props("texAddressMode").ToString)
                        If props.ContainsKey("texAddressMode") Then
                            Debug.WriteLine(model_name)
                        End If
                    End With
                    mat.props = obj
                    mat.shader_type = ShaderTypes.FX_PBS_glass

                Case "shaders/std_effects/PBS_ext_repaint.fx", "shaders/std_effects/PBS_ext_skinned_repaint.fx", "shaders/std_effects/PBS_ext_detail_repaint.fx"
                    Dim knownPropNames As New HashSet(Of String)({
                        "diffuseMap",
                        "normalMap",
                        "metallicGlossMap",
                        "g_baseColor",
                        "alphaReference",
                        "g_repaintColor",
                        "alphaTestEnable",
                        "g_enableAO",
                        "g_enableTerrainBlending",
                        "g_aging",
                        "doubleSided",
                        "selfIllumination",
                        "dirtAlbedoMap",
                        "applyOverlay",
                        "glassMap",
                        "g_useTintColor",
                        "g_tintColor",
                        "texAddressMode",
                        "g_applyOverlay",
                        "g_detailInfluences",
                        "g_detailMap",
                        "g_detailRejectTiling"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next
                    If fx = "shaders/std_effects/PBS_ext_detail_repaint.fx" Then
                        Trap("fx variant", fx, model_name)
                    End If
                    Dim obj As New MaterialProps_PBS_ext_repaint
                    With obj
                        'If props.ContainsKey("glassMap") Then Stop
                        If Not props.ContainsKey("g_repaintColor") Then
                            If materials.ElementAt(pGroup.material_id - 1).Value.shader_type = ShaderTypes.FX_PBS_ext_repaint Then
                                Dim props_2 = materials.ElementAt(pGroup.material_id - 1).Value.props
                                .g_repaintColor = props_2.g_repaintColor
                            End If
                        Else
                            .g_repaintColor = props("g_repaintColor")
                        End If

                        If Not props.ContainsKey("g_baseColor") Then
                            If materials.ElementAt(pGroup.material_id - 1).Value.shader_type = ShaderTypes.FX_PBS_ext_repaint Then
                                Dim props_2 = materials.ElementAt(pGroup.material_id - 1).Value.props
                                .g_baseColor = props_2.g_baseColor
                            End If

                        Else
                            .g_baseColor = props("g_baseColor")
                        End If

                        .diffuseMap = props("diffuseMap")
                        .normalMap = props("normalMap")
                        .metallicGlossMap = props("metallicGlossMap")
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .g_enableAO = props("g_enableAO")
                        If props.ContainsKey("detailinfluences") Then
                            .g_detailInfluences = props("g_detailInfluences")
                        End If
                        If props.ContainsKey("g_detailInfluences") Then
                            .g_detailInfluences = props("g_detailInfluences")
                        End If
                        If props.ContainsKey("g_detailMap") Then
                            .g_detailMap = props("g_detailMap")
                        End If

                        If props.ContainsKey("g_detailRejectTiling") Then
                            .g_detailRejectTiling = props("g_detailRejectTiling")
                        End If
                        '.g_baseColor = If(props.ContainsKey("g_baseColor"), props("g_baseColor"), New Vector4(0.223529, 0.25098, 0.282353, 1))

                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_ext_repaint
                    mat.props = obj


                Case "shaders/std_effects/glow.fx"
                    ' Emissive card, its own family now. The compiled shader is
                    ' diffuse -> alpha test -> gamma decode -> * g_tintColor
                    ' * (selfIllumination + 1) * g_envLumMultipliers.x -> fog,
                    ' with no lighting and a single render target. It used to
                    ' ride the lightonly case, which lit it and threw the
                    ' multiplier away - on Abbey's burnt grass that is a x16.
                    Dim knownGlowProps As New HashSet(Of String)({
                        "diffuseMap",
                        "alphaTestEnable",
                        "alphaReference",
                        "doubleSided",
                        "selfIllumination",
                        "g_tintColor"
                    })
                    For Each name In props.Keys
                        If Not knownGlowProps.Contains(name) Then
                            Trap("unknown property", fx & " -> " & name, model_name)
                        End If
                    Next

                    Dim gobj As New MaterialProps_lightonly_alpha
                    With gobj
                        .diffuseMap = props("diffuseMap").ToLower
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        .selfIllumination = If(props.ContainsKey("selfIllumination"), CSng(props("selfIllumination")), 0.0F)
                    End With
                    mat.shader_type = ShaderTypes.FX_glow
                    mat.props = gobj
                    GLOW_MODEL_DIRS.Add(model_name)
                    ' Same reporting the volumetric materials get - without it
                    ' there is no way to tell whether a glow card was even
                    ' classified, let alone what multiplier it ended up with.
                    LogThis("glow material {0}: diffuse={1} selfIllum={2} (x{3}) alphaRef={4} doubleSided={5} on {6}",
                            material_id, gobj.diffuseMap, gobj.selfIllumination,
                            gobj.selfIllumination + 1.0F, gobj.alphaReference,
                            gobj.doubleSided, model_name)

                Case "shaders/std_effects/lightonly_alpha.fx", "shaders/std_effects/lightonly.fx", "shaders/std_effects/normalmap_specmap.fx", "shaders/std_effects/lightonly_dual.fx"
                    ' Unlit alpha-tested cards - diffuse map, cutout,
                    ' double-sided, no PBS maps. glow.fx used to ride this
                    ' path; it has its own case above now, because it is
                    ' emissive and this one is not.
                    If If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False) Then
                        ' Alpha-TESTED card (burnt grass and kin): deferred
                        ' cutout path.
                        Dim obj As New MaterialProps_lightonly_alpha
                        With obj
                            .diffuseMap = props("diffuseMap").ToLower
                            .alphaTestEnable = True
                            .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                            .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        End With
                        mat.shader_type = ShaderTypes.FX_lightonly_alpha
                        mat.props = obj
                    Else
                        ' Alpha-BLENDED card (hills_outland_smokes and kin):
                        ' the deferred pass cannot blend, so ride the
                        ' volumetric forward pass as a static, unwarped,
                        ' over-blended billboard. Plain alpha variant gives
                        ' alpha = texA * vertA. Lighting multipliers borrow
                        ' the vista-smoke authoring (sun 0.15, ambient 0.5)
                        ' as the stand-in for lightonly's scene lighting.
                        Dim vobj As New MaterialProps_volumetric
                        With vobj
                            .diffuseMap = props("diffuseMap").ToLower
                            .distortionMap = .diffuseMap ' amount 0 = no-op warp
                            .TintlColor = New Vector4(1, 1, 1, 1)
                            .diffuseUVSpeedAlphaOffset = Vector4.Zero
                            .distortion_UV_Speed_Amount = Vector4.Zero
                            .lightMultipliers = New Vector4(1, 0.15F, 0.5F, 0)
                            .selfIllumLight = Vector4.Zero
                            .FreshnelColor = New Vector4(1, 1, 1, 1)
                            .alphaFadeAmountFresnel = New Vector4(1, 1, 1, 0)
                            .alphaFreshnelEnable = False
                            .destBlend = 6
                            .fadeMinDistance = 0.01F
                            .fadeMaxDistance = 1.0F
                            .alphaAdditiveEnable = False
                            .enableLighting = True
                            .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        End With
                        mat.shader_type = ShaderTypes.FX_volumetric
                        mat.props = vobj
                    End If

                Case "shaders/custom/volumetric_effect.fx", "shaders/custom/volumetric_effect_vtx.fx", "shaders/custom/volumetric_effect_layer_vtx.fx"
                    ' GFX smoke/flame/distortion meshes. Semantics transcribed
                    ' from the game's volumetric_effect_vtx fxo - see
                    ' volumetric.vert/frag for who consumes what.
                    Dim knownPropNames As New HashSet(Of String)({
                        "diffuseMap",
                        "distortionMap",
                        "TintlColor",
                        "diffuseUVSpeedAlphaOffset",
                        "distortion_UV_Speed_Amount",
                        "lightMultipliers",
                        "selfIllumLight",
                        "FreshnelColor",
                        "alphaFadeAmountFresnel",
                        "alphaAdditiveEnable",
                        "doubleSided",
                        "enableLighting",
                        "alphaTestEnable",
                        "alphaReference",
                        "alphaFreshnelEnable",
                        "destBlend",
                        "srcBlend",
                        "fadeMinDistance",
                        "fadeMaxDistance",
                        "softFactor"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            ' Log, not Stop: outside a debugger Stop is a
                            ' silent no-op and an unknown knob would vanish.
                            LogThis("volumetric: unknown property '{0}' = {1} on {2}", name, props(name), fx)
                        End If
                    Next
                    Dim obj As New MaterialProps_volumetric
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        ' No distortion map authored = warp against the
                        ' diffuse itself, which at amount 0 is a no-op.
                        .distortionMap = If(props.ContainsKey("distortionMap"), props("distortionMap").ToLower, .diffuseMap)
                        ' Compiled-shader defaults for everything unauthored.
                        .TintlColor = New Vector4(1, 1, 1, 1)
                        .diffuseUVSpeedAlphaOffset = Vector4.Zero
                        .distortion_UV_Speed_Amount = Vector4.Zero
                        .lightMultipliers = New Vector4(1, 0, 0, 0)
                        .selfIllumLight = Vector4.Zero
                        .FreshnelColor = New Vector4(1, 1, 1, 1)
                        ' The compiled fxo's register default has gain (y) = 0,
                        ' which makes every material that does not author this
                        ' invisible - and the game visibly renders such
                        ' materials (vista_smoke_01), so the artist-side
                        ' default must be gain 1. Match the working authored
                        ' value instead of the dead register default.
                        .alphaFadeAmountFresnel = New Vector4(1, 1, 1, 0)
                        If props.ContainsKey("TintlColor") Then .TintlColor = props("TintlColor")
                        If props.ContainsKey("diffuseUVSpeedAlphaOffset") Then .diffuseUVSpeedAlphaOffset = props("diffuseUVSpeedAlphaOffset")
                        If props.ContainsKey("distortion_UV_Speed_Amount") Then .distortion_UV_Speed_Amount = props("distortion_UV_Speed_Amount")
                        If props.ContainsKey("lightMultipliers") Then .lightMultipliers = props("lightMultipliers")
                        If props.ContainsKey("selfIllumLight") Then .selfIllumLight = props("selfIllumLight")
                        If props.ContainsKey("FreshnelColor") Then .FreshnelColor = props("FreshnelColor")
                        If props.ContainsKey("alphaFadeAmountFresnel") Then .alphaFadeAmountFresnel = props("alphaFadeAmountFresnel")
                        If props.ContainsKey("alphaAdditiveEnable") Then .alphaAdditiveEnable = props("alphaAdditiveEnable")
                        If props.ContainsKey("doubleSided") Then .doubleSided = props("doubleSided")
                        If props.ContainsKey("enableLighting") Then .enableLighting = props("enableLighting")
                        ' Variant selector, default True - that is the variant
                        ' D-Day's vista smoke renders with (ps blob 8); Abbey's
                        ' smoke sheets author False (ps blob 9, plain alpha).
                        .alphaFreshnelEnable = If(props.ContainsKey("alphaFreshnelEnable"), props("alphaFreshnelEnable"), True)
                        ' D3DBLEND: default 6 = INVSRCALPHA (standard "over").
                        .destBlend = If(props.ContainsKey("destBlend"), props("destBlend"), 6)
                        ' Compiled register defaults - past a metre the fade
                        ' saturates to 1, so unauthored materials are always
                        ' fully faded in.
                        .fadeMinDistance = If(props.ContainsKey("fadeMinDistance"), props("fadeMinDistance"), 0.01F)
                        .fadeMaxDistance = If(props.ContainsKey("fadeMaxDistance"), props("fadeMaxDistance"), 1.0F)
                        ' softFactor is cb0[81].x and IS live in both pixel
                        ' variants - the "[unused]" that made it look dead is
                        ' the VERTEX shader's listing. It sets the distance over
                        ' which a card fades out as it approaches whatever is
                        ' behind it, which is what stops the sheet cutting a
                        ' hard straight line where it intersects the ground.
                        ' No material on any map read so far authors this, and the
                        ' compiled register default could not be recovered, so the
                        ' fallback is tuned rather than sourced. Note the sense:
                        ' softFade = sat(distance_behind / softFactor), so a LARGER
                        ' value fades MORE. At 1.0 m every part of a ground-hugging
                        ' smoke sheet is inside the fade and it cost 27% of the
                        ' smoke's contrast against the ground (+11.1 -> +8.7).
                        ' 0.25 m still removes the hard line where a card cuts into
                        ' terrain, which is only a few pixels wide, without thinning
                        ' the body of the sheet.
                        .softFactor = If(props.ContainsKey("softFactor"), props("softFactor"), 0.25F)
                        ' srcBlend is known-listed but ignored: the only
                        ' observed value is 5 = SRCALPHA, which both output
                        ' paths already implement (rgb is multiplied by alpha
                        ' in the shader).
                    End With
                    mat.shader_type = ShaderTypes.FX_volumetric
                    mat.props = obj
                    ' Which MODEL a volumetric material belongs to. Without this
                    ' the material log stands alone and there is no telling the
                    ' smoke sheet from the fire sheets.
                    LogThis("volumetric material {0} <- {1}", material_id, model_name)
                    VOLUMETRIC_MODEL_DIRS.Add(model_name)

                Case "shaders/particles/wg_particles.fx", "shaders/custom/coloronly_alpha.fx", "shaders/std_effects/PBS_ext_detail_dual.fx", "shaders/custom/emissive.fx", "shaders/custom/volumetric_effect_vtx_skinned.fx", "shaders/std_effects/PBS_sss_skinned.fx", "shaders/std_effects/PBS_hair_skinned.fx", "shaders/std_effects/fur_skinned.fx", "shaders/custom/emissive_playground.fx"
                    ' Names every invisible-by-unsupported-shader model, so a
                    ' bounding box with nothing in it can be identified from
                    ' the log instead of guessed at.
                    LogThis("unsupported fx: {0} on {1}", fx, model_name)
                    mat.shader_type = ShaderTypes.FX_unsupported

                Case Else
                        ' An fx the loader has never seen. shader_type stays 0, which is
                        ' model.frag's default_entry - it renders RED, so it is visible too.
                        Trap("unhandled fx", fx, model_name)
            End Select

            materials(material_id) = mat
        End If
    End Sub

End Module

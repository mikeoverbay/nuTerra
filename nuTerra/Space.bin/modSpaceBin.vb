﻿Imports System.IO
Imports System.Text
Imports OpenTK

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

    Public Function ReadSpaceBinData(p As String) As Boolean
        If Not File.Exists(Path.Combine(TEMP_STORAGE, p)) Then
            GoTo Failed
        End If

        Dim f = File.OpenRead(Path.Combine(TEMP_STORAGE, p))

        Using br As New BinaryReader(f, Encoding.ASCII)
            br.BaseStream.Position = &H14
            Dim table_size = br.ReadInt32

            sectionHeaders = New Dictionary(Of String, SectionHeader)

            ' read each entry in the header table
            For i = 0 To table_size - 1
                Dim header As New SectionHeader(br)
                sectionHeaders.Add(header.magic, header)
            Next

            Try
                ' we must grab this data first!
                cBSGD = New cBSGD_(sectionHeaders("BSGD"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSGD")
                GoTo Failed
            End Try

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
                cBWAL = New cBWAL_(sectionHeaders("BWAL"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWAL")
                GoTo Failed
            End Try

            Try
                get_BWSG(sectionHeaders("BWSG"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWSG")
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
                cSpTr = New cSpTr_(sectionHeaders("SpTr"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "SpTr")
                GoTo Failed
            End Try

            Try
                get_WGSD(sectionHeaders("WGSD"), br)
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

            Try
                cWTbl = New cWTbl_(sectionHeaders("WTbl"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "WTbl")
                GoTo Failed
            End Try

            'Unimplemented sections:
            'BWCS
            'BWS2
            'BSG2
            'WTCP
            'BWEP
            'WGCO
            'BWPs
            'CENT
            'UDOS
            'WGDE
            'BWLC
            'WTau
            'WGSH
            'WGMM
        End Using

        f.Close()


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
                .mdl = New base_model_holder_

                .visibilityBounds = cBSMO.models_visibility_bounds.data(k)

                Dim lod0_offset = cBSMO.models_loddings.data(k).lod_begin
                Dim lodx_offset = cBSMO.models_loddings.data(k).lod_end

                ' Set lod0 as default
                Dim lod_offset = Math.Min(lod0_offset + 0, lodx_offset)

                Dim lod_render_set_begin = cBSMO.lod_renders.data(lod_offset).render_set_begin
                Dim lod_render_set_end = cBSMO.lod_renders.data(lod_offset).render_set_end

                Dim num_render_sets = lod_render_set_end - lod_render_set_begin + 1
                Debug.Assert(num_render_sets > 0)

                ' Creating renderSets
                .mdl.render_sets = New List(Of RenderSetEntry)
                Dim dict As New Dictionary(Of String, Integer)
                For z As UInteger = 0 To num_render_sets - 1
                    Dim renderItem = cBSMO.renders.data(lod_render_set_begin + z)
                    Dim verts_name = cBWST.find_str(renderItem.verts_name_fnv)
                    Dim prims_name = cBWST.find_str(renderItem.prims_name_fnv)

                    Dim pGroup As New PrimitiveGroup
                    apply_material_for_pgroup(pGroup, renderItem.material_index)

                    If Not dict.ContainsKey(verts_name) Then
                        Dim rs As New RenderSetEntry With {
                            .verts_name = verts_name,
                            .prims_name = prims_name,
                            .primitiveGroups = New Dictionary(Of Integer, PrimitiveGroup)
                        }
                        rs.primitiveGroups(renderItem.primtive_index) = pGroup
                        dict(verts_name) = .mdl.render_sets.Count
                        .mdl.render_sets.Add(rs)
                    Else
                        .mdl.render_sets(dict(verts_name)).primitiveGroups(renderItem.primtive_index) = pGroup
                    End If
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
        Array.Resize(MODEL_INDEX_LIST, j - 1)

        ReadSpaceBinData = True
        GoTo CleanUp

Failed:
        ReadSpaceBinData = False

CleanUp:
        'Clear headers
        sectionHeaders = Nothing

        'Clear Sections
        cBSGD = Nothing
        'cBWST = Nothing
        cBWSG = Nothing
        'cBWT2 = Nothing
        cBSMI = Nothing
        cWTbl = Nothing
        cBSMO = Nothing
        cBSMA = Nothing
        cBWAL = Nothing
        cWGSD = Nothing
        cSpTr = Nothing
        cBWWa = Nothing

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
            If MAP_MODELS(it.Key).mdl.junk Then
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
    End Function

    Private Sub apply_material_for_pgroup(pGroup As PrimitiveGroup, material_id As Integer)
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
                            ' ?
                            Stop

                        Case 5
                            ' Vector4
                            props(.property_name_string) = .val_vec4

                        Case 6
                            ' Texture
                            props(.property_name_string) = .property_value_string

                        Case Else
                            Stop
                    End Select
                End With
            Next

            Select Case fx
                Case "shaders/std_effects/PBS_ext.fx", "shaders/std_effects/PBS_ext_skinned.fx", "shaders/std_effects/PBS_ext_repaint.fx"
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
                        "g_aging"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Stop
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_ext
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        .normalMap = props("normalMap").ToLower
                        .metallicGlossMap = props("metallicGlossMap").ToLower
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        .g_useNormalPackDXT1 = If(props.ContainsKey("g_useNormalPackDXT1"), props("g_useNormalPackDXT1"), False)
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
                        "selfIllumination"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Stop
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_ext_dual
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                        .diffuseMap2 = props("diffuseMap2").ToLower
                        .normalMap = props("normalMap").ToLower
                        .metallicGlossMap = props("metallicGlossMap")
                        .alphaReference = If(props.ContainsKey("alphaReference"), props("alphaReference"), 0)
                        .alphaTestEnable = If(props.ContainsKey("alphaTestEnable"), props("alphaTestEnable"), False)
                        .doubleSided = If(props.ContainsKey("doubleSided"), props("doubleSided"), False)
                        .g_useNormalPackDXT1 = If(props.ContainsKey("g_useNormalPackDXT1"), props("g_useNormalPackDXT1"), False)
                        If props.ContainsKey("g_tintColor") Then
                            If props("g_useTintColor") = "True" Then
                                Stop
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
                        "texAddressMode"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Stop
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
                        .g_useTintColor = If(props.ContainsKey("g_useTintColor"), props("g_useTintColor"), False)
                        .g_colorTint = If(props.ContainsKey("g_colorTint"), props("g_colorTint"), New Vector4(1.0F, 1.0F, 1.0F, 1.0F))

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
                        "diffuseMap"
                    })
                    For Each name In props.Keys
                        If Not knownPropNames.Contains(name) Then
                            Stop
                        End If
                    Next

                    Dim obj As New MaterialProps_PBS_tiled_atlas
                    With obj
                        .atlasAlbedoHeight = props("atlasAlbedoHeight").ToLower
                        .atlasBlend = props("atlasBlend").ToLower
                        .atlasNormalGlossSpec = props("atlasNormalGlossSpec").ToLower
                        .atlasMetallicAO = props("atlasMetallicAO").ToLower

                        .dirtMap = If(props.ContainsKey("dirtMap"), props("dirtMap"), Nothing)
                        .dirtColor = If(props.ContainsKey("dirtColor"), props("dirtColor"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .dirtParams = If(props.ContainsKey("dirtParams"), props("dirtParams"), New Vector4(1.0, 1.0, 1.0, 1.0))


                        .g_atlasIndexes = If(props.ContainsKey("g_atlasIndexes"), props("g_atlasIndexes"), New Vector4(0, 0, 0, 0))
                        .g_atlasSizes = If(props.ContainsKey("g_atlasSizes"), props("g_atlasSizes"), New Vector4(4, 4, 8, 4))

                        .g_tile0Tint = If(props.ContainsKey("g_tile0Tint"), props("g_tile0Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile1Tint = If(props.ContainsKey("g_tile1Tint"), props("g_tile1Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tile2Tint = If(props.ContainsKey("g_tile2Tint"), props("g_tile2Tint"), New Vector4(1.0, 1.0, 1.0, 1.0))
                        .g_tintColor = If(props.ContainsKey("g_tintColor"), props("g_tintColor"), New Vector4(1.0, 1.0, 1.0, 1.0))
                    End With
                    mat.shader_type = ShaderTypes.FX_PBS_tiled_atlas
                    mat.props = obj

                Case "shaders/std_effects/PBS_tiled_atlas_global.fx"
                    mat.shader_type = ShaderTypes.FX_PBS_tiled_atlas_global

                Case "shaders/std_effects/lightonly_alpha.fx", "shaders/std_effects/lightonly.fx", "shaders/std_effects/normalmap_specmap.fx"
                    Dim obj As New MaterialProps_lightonly_alpha
                    With obj
                        .diffuseMap = props("diffuseMap").ToLower
                    End With
                    mat.shader_type = ShaderTypes.FX_lightonly_alpha
                    mat.props = obj

                Case "shaders/custom/volumetric_effect_vtx.fx", "shaders/custom/volumetric_effect_layer_vtx.fx", "shaders/std_effects/glow.fx", "shaders/std_effects/PBS_glass.fx", "shaders/custom/emissive.fx"
                    mat.shader_type = ShaderTypes.FX_unsupported

                Case Else
                    Stop
            End Select

            materials(material_id) = mat
        End If
    End Sub

End Module

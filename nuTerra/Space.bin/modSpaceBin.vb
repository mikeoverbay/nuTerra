
Imports System.IO
Imports System.Text

Module modSpaceBin
    Public space_bin_Chunk_table As SectionHeader()
    Public Structure SectionHeader
        Public chunk_name As String
        Public version As Int32
        Public offset As Int64
        Public length As Int64
    End Structure

    Public Function ReadSpaceBinData(ByRef p As String) As Boolean
        If Not File.Exists(TEMP_STORAGE + p) Then
            Return False
        End If
        Dim sb As New StringBuilder
        Dim f = File.OpenRead(TEMP_STORAGE + p)
        Dim br As New BinaryReader(f)
        f.Position = &H14
        Dim table_size = br.ReadInt32
        ReDim space_bin_Chunk_table(table_size - 1)

        'read each entry in the header table
        For t_cnt = 0 To table_size - 1
            Dim ds = br.ReadBytes(4)
            space_bin_Chunk_table(t_cnt).chunk_name = System.Text.Encoding.ASCII.GetString(ds, 0, 4)

            Debug.WriteLine(space_bin_Chunk_table(t_cnt).chunk_name)
            sb.Append("Case header = " + """" + space_bin_Chunk_table(t_cnt).chunk_name + """" + vbCrLf)

            space_bin_Chunk_table(t_cnt).version = br.ReadInt32
            space_bin_Chunk_table(t_cnt).offset = br.ReadInt64
            space_bin_Chunk_table(t_cnt).length = br.ReadInt64
        Next

        If False Then
            If Not Directory.Exists("C:\!_bin_data\") Then
                Directory.CreateDirectory("C:\!_bin_data\")
            End If
            File.WriteAllText("C:\!_bin_data\headers.txt", sb.ToString)
        End If

        If False Then
            If Not Directory.Exists("C:\!_bin_data\") Then
                Directory.CreateDirectory("C:\!_bin_data\")
            End If
            For i = 0 To space_bin_Chunk_table.Length - 1
                br.BaseStream.Position = space_bin_Chunk_table(i).offset
                Dim d = br.ReadBytes(space_bin_Chunk_table(i).length)
                File.WriteAllBytes("C:\!_bin_data\" + MAP_NAME_NO_PATH + "_" + space_bin_Chunk_table(i).chunk_name + ".bin", d)
            Next
        End If

        ' we mush grab this data first!
        For Each header As SectionHeader In space_bin_Chunk_table
            If header.chunk_name = "BSGD" Then
                If Not get_BSGD(header, br) Then
                    MsgBox("BSGD decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                    Return False
                End If
                Exit For
            End If
        Next

        '----------------------------------------------------------------------------------
        'Now we will grab the game data we need.
        For Each header As SectionHeader In space_bin_Chunk_table
            Select Case header.chunk_name
                Case "BWST"
                    If Not get_BWST(header, br) Then
                        MsgBox("BWST decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BWAL"
                    If Not get_BWAL(header, br) Then
                        MsgBox("BWAL decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BWCS"
                Case "BWSG"
                    If Not get_BWSG(header, br) Then
                        MsgBox("BWSG decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BWS2"
                Case "BSG2"
                Case "BWT2"
                Case "BSMI"
                    If Not get_BSMI(header, br) Then
                        MsgBox("BSMI decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BSMO"
                    If Not get_BSMO(header, br) Then
                        MsgBox("BSMO decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BSMA"
                    If Not get_BSMA(header, br) Then
                        MsgBox("BSMA decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "SpTr"
                    If Not get_SpTr(header, br) Then
                        MsgBox("SpTr decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "WGSD"
                    If Not get_WGSD(header, br) Then
                        MsgBox("WGSD decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "WTCP"
                Case "BWWa"
                    If Not get_BWWa(header, br) Then
                        MsgBox("BWWa decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "BWEP"
                Case "WGCO"
                Case "BWPs"
                Case "CENT"
                Case "UDOS"
                Case "WGDE"
                Case "BWLC"
                Case "WTau"
                Case "WTbl"
                    If Not get_WTbl(header, br) Then
                        MsgBox("WTbl decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case "WGSH"
                Case "WGMM"
            End Select
        Next

        'Clear headers
        space_bin_Chunk_table = Nothing

        br.Close()
        f.Close()

        br = Nothing
        f = Nothing

        '----------------------------------------------------------------------------------
        'build the model information
        ReDim MAP_MODELS(cBSMO.models_colliders.count - 1)
        For k = 0 To cBSMO.models_colliders.count - 1
            MAP_MODELS(k) = New mdl_
            ReDim MAP_MODELS(k).mdl(1)
            MAP_MODELS(k).mdl(0) = New base_model_holder_

            MAP_MODELS(k).mdl(0).primitive_name = cBSMO.models_colliders.data(k).primitive_name

            With MAP_MODELS(k).mdl(0)
                .sb_model_material_begin = cBSMO.models_colliders.data(k).bsp_material_kind_begin
                .sb_model_material_end = cBSMO.models_colliders.data(k).bsp_material_kind_end

                If .sb_model_material_begin = &HFFFFFFFFUI Then ' no drawable item.. light prob or sound location?
                    MAP_MODELS(k).mdl(0).junk = True
                    GoTo ignore_this_one
                End If

                'get lod set pointers for this model
                Dim lod0 = .sb_LOD_set_start

                Dim entry_count = MAP_MODELS(k).mdl(0).sb_model_material_end - MAP_MODELS(k).mdl(0).sb_model_material_begin

                Dim mat_kind_index = cBSMO.bsp_material_kinds.data(.sb_model_material_begin).material_index
                Dim shader_prop_start = cBSMA.MaterialItem(mat_kind_index).shaderPropBegin
                Dim shader_prop_end = cBSMA.MaterialItem(mat_kind_index).shaderPropEnd

                'this is all wrong.. we don't want LODs! We Want the total primitiveGroups!
                'I can NOT figure out how to get the list of all the primitivegroups!!
                Dim component_cnt = shader_prop_end - shader_prop_start
                If .primitive_name.ToLower.Contains("vhouse_05") Then
                    'Stop
                End If

                ReDim .entries(component_cnt)
                .primitive_count = component_cnt + 1
                Dim run_cnt As Integer = 0
                For z = 0 To component_cnt
                    Dim mat_index = cBSMO.renders.data(z)
                    .entries(z).identifier = cBSMA.MaterialItem(z + shader_prop_start).identifier
                    .entries(z).FX_shader = cBSMA.MaterialItem(z + shader_prop_start).FX_string
                    'Dim l_cnt = cBSMA.MaterialItem(z + shader_prop_start).shaderPropEnd - cBSMA.MaterialItem(z + shader_prop_start).shaderPropBegin
                    For j = cBSMA.MaterialItem(mat_kind_index).shaderPropBegin To cBSMA.MaterialItem(mat_kind_index).shaderPropEnd - 1
                        'I so wish I knew of a better way

                        Select Case cBSMA.ShaderPropertyItem(j).property_name_string
                            'booleans -------------------------------------------------------------
                            Case "doubleSided"
                                .entries(z).doubleSided = cBSMA.ShaderPropertyItem(j).val_boolean
                            Case "alphaEnable"
                                .entries(z).alphaEnable = cBSMA.ShaderPropertyItem(j).val_boolean
                            Case "TexAddressMode"
                                .entries(z).TexAddressMode = cBSMA.ShaderPropertyItem(j).val_int '? check this!!
                            Case "dynamicobject"
                                .entries(z).dynamicobject = cBSMA.ShaderPropertyItem(j).val_boolean
                            Case "g_enableAO"
                                .entries(z).g_enableAO = cBSMA.ShaderPropertyItem(j).val_boolean
                            Case "g_useNormalPackDXT1"
                                .entries(z).g_useNormalPackDXT1 = cBSMA.ShaderPropertyItem(j).val_boolean
                                'values -------------------------------------------------------------
                            Case "alphaReference"
                                .entries(z).alphaReference = cBSMA.ShaderPropertyItem(j).val_int
                            Case "g_tintColor"
                                .entries(z).g_tintColor = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_tile0Tint"
                                .entries(z).g_tile0Tint = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_tile1Tint"
                                .entries(z).g_tile1Tint = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_tile2Tint"
                                .entries(z).g_tile2Tint = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_dirtParams"
                                .entries(z).g_dirtParams = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_dirtColor"
                                .entries(z).g_dirtColor = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_atlasSizes"
                                .entries(z).g_atlasSizes = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_atlasIndexes"
                                .entries(z).g_atlasIndexes = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_vertexColorMode"
                                .entries(z).g_vertexColorMode = cBSMA.ShaderPropertyItem(j).val_int
                            Case "g_vertexAnimationParams"
                                .entries(z).g_vertexAnimationParams = cBSMA.ShaderPropertyItem(j).val_vec4
                            Case "g_fakeShadowsParams"
                                .entries(z).g_fakeShadowsParams = cBSMA.ShaderPropertyItem(j).val_vec4

                                'texture string -------------------------------------------------------------
                            Case "FX_shader"
                                .entries(z).FX_shader = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "diffuseMap"
                                .entries(z).diffuseMap = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "diffuseMap2"
                                .entries(z).diffuseMap2 = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "normalMap"
                                .entries(z).normalMap = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "metallicGlossMap"
                                .entries(z).metallicGlossMap = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "atlasBlend"
                                .entries(z).atlasBlend = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "atlasMetallicAO"
                                .entries(z).atlasMetallicAO = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "atlasNormalGlossSpec"
                                .entries(z).atlasNormalGlossSpec = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "atlasAlbedoHeight"
                                .entries(z).atlasAlbedoHeight = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "dirtMap"
                                .entries(z).dirtMap = cBSMA.ShaderPropertyItem(j).property_value_string
                            Case "globalTex"
                                .entries(z).globalTex = cBSMA.ShaderPropertyItem(j).property_value_string

                        End Select
                    Next
                Next

ignore_this_one:
            End With

        Next

#If 0 Then
        'I don't know how to index in to the vertex data or where the incides list is.
        'Maybe one of the v_types is for indices?
        For i = 0 To MAP_MODELS.Length - 1
            For j = MAP_MODELS(i).sb_start_index To MAP_MODELS(i).sb_end_index - 1
                Dim v_type = cBWSG.primitive_data_list(j).block_type
                Dim start_ = cBWSG.primitive_data_list(j).chunkDataOffset
                Dim length_ = cBWSG.primitive_data_list(j).chunkDataBlockLength
                Dim chunkIndx = cBWSG.primitive_data_list(j).chunkDataBlockIndex
                Select Case True
                    'v_types
                    ' 0   = Vertex
                    ' 10  = UV2
                    ' 11 = color We ignore this.
                    '
                    'copy the data
                    Case v_type = 0 ' vertex data
                        ReDim MAP_MODELS(i).sb_vertex_data(length_ - 1)
                        For z = 0 To length_ - 1
                            MAP_MODELS(i).sb_vertex_data(z) = cBWSG.cBWSG_VertexDataChunks(chunkIndx).data(z + start_)
                        Next
                    Case v_type = 10
                        MAP_MODELS(i).has_uv2 = 1 'set has_uv2 flag
                        ReDim MAP_MODELS(i).sp_uv2_data(length_ - 1)
                        For z = 0 To length_ - 1
                            MAP_MODELS(i).sp_uv2_data(z) = cBWSG.cBWSG_VertexDataChunks(chunkIndx).data(z + start_)
                        Next

                End Select
            Next
        Next
#End If
        Dim max_id As Integer
        ReDim MODEL_MATRIX_LIST(cBSMI.chunk_models.count - 1)
        Dim cnt As Integer = 0
        For k = 0 To cBSMI.model_BSMO_indexes.count - 1
            MODEL_MATRIX_LIST(k) = New model_matrix_list_
            Dim BSMO_MODEL_INDEX = cBSMI.model_BSMO_indexes.data(k).BSMO_MODEL_INDEX

            MODEL_MATRIX_LIST(k).model_index = BSMO_MODEL_INDEX
            If BSMO_MODEL_INDEX > max_id Then
                max_id = BSMO_MODEL_INDEX
            End If

            MODEL_MATRIX_LIST(k).primitive_name = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).model_name

            MODEL_MATRIX_LIST(k).matrix = cBSMI.transforms.data(k)
            'MODEL_MATRIX_LIST(k).matrix.M21 *= -1.0
            'MODEL_MATRIX_LIST(k).matrix.M31 *= -1.0
            'MODEL_MATRIX_LIST(k).matrix.M12 *= -1.0
            'MODEL_MATRIX_LIST(k).matrix.M13 *= -1.0
            'MODEL_MATRIX_LIST(k).matrix.M14 *= -1.0

            MODEL_MATRIX_LIST(k).matrix.M12 *= -1.0
            MODEL_MATRIX_LIST(k).matrix.M13 *= -1.0
            MODEL_MATRIX_LIST(k).matrix.M21 *= -1.0
            MODEL_MATRIX_LIST(k).matrix.M31 *= -1.0
            MODEL_MATRIX_LIST(k).matrix.M41 *= -1.0

            MODEL_MATRIX_LIST(k).mask = False
            MODEL_MATRIX_LIST(k).BB_Min = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).collision_bounds_min
            MODEL_MATRIX_LIST(k).BB_Max = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).collision_bounds_max

            'create model culling box
            ReDim MODEL_MATRIX_LIST(k).BB(8)
            get_translated_bb_model(MODEL_MATRIX_LIST(k))

        Next
        '----------------------------------------------------------------------------------
        ' remove models that we dont want on the map
        Dim mc As Int32 = 0
        Dim HQ As Integer = 1
        Dim tm(cBSMI.model_BSMO_indexes.count - 1) As model_matrix_list_
        For i = 0 To cBSMI.model_BSMO_indexes.count - 1
            If cBSMI.model_BSMO_indexes.data(i).BSMO_extras = HQ Then
                If cBSMI.visibility_masks.data(i).mask = &HFFFFFFFFUI Then 'visibility mask
                    tm(mc) = New model_matrix_list_
                    tm(mc) = MODEL_MATRIX_LIST(i)
                    mc += 1
                Else
                    'Debug.WriteLine(i.ToString("00000") + ":" + cBSMI.bsmi_t7(i).v_mask.ToString("x") + ":" + Path.GetFileNameWithoutExtension(Model_Matrix_list(i).primitive_name))
                End If
            End If
        Next
        ReDim Preserve tm(mc)
        ReDim MODEL_MATRIX_LIST(mc)
        'pack the Model_Matrix_list to used models.
        For i = 0 To mc
            MODEL_MATRIX_LIST(i) = tm(i)
            If MODEL_MATRIX_LIST(i).exclude = True Then
                'Debug.WriteLine(i.ToString("0000") + " : " + Model_Matrix_list(i).primitive_name)
            End If

        Next

        'Clear Sections
        cBSGD = Nothing
        cBWST = Nothing
        cBWSG = Nothing
        cBSMI = Nothing
        cWTbl = Nothing
        cBSMO = Nothing
        cBSMA = Nothing
        cBWAL = Nothing
        cWGSD = Nothing
        cSpTr = Nothing
        cBWWa = Nothing

        Return True
    End Function
End Module

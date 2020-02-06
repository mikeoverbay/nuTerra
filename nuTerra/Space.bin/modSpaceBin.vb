
#Region "imports"
Imports System.IO
Imports System.Windows.Forms
Imports System.Math
Imports System.String
Imports System.Text
Imports OpenTK
#End Region

Module modSpaceBin
    Public space_bin_Chunk_table() As space_chunks_
    Public Structure space_chunks_
        Public chunk_name As String
        Public type As Int32
        Public chunk_Start As Long
        Public chunk_length As Long
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
        Dim old_pos = br.BaseStream.Position
        For t_cnt = 0 To table_size - 1
            old_pos = br.BaseStream.Position
            Dim ds() = br.ReadBytes(4)
            space_bin_Chunk_table(t_cnt).chunk_name = System.Text.Encoding.UTF8.GetString(ds, 0, 4)

            Debug.WriteLine(space_bin_Chunk_table(t_cnt).chunk_name)
            sb.Append("Case header = " + """" + space_bin_Chunk_table(t_cnt).chunk_name + """" + vbCrLf)

            space_bin_Chunk_table(t_cnt).type = br.ReadInt32
            space_bin_Chunk_table(t_cnt).chunk_Start = br.ReadInt64
            space_bin_Chunk_table(t_cnt).chunk_length = br.ReadInt64
            old_pos = br.BaseStream.Position

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
                br.BaseStream.Position = space_bin_Chunk_table(i).chunk_Start
                Dim d = br.ReadBytes(space_bin_Chunk_table(i).chunk_length)
                File.WriteAllBytes("C:\!_bin_data\" + MAP_NAME_NO_PATH + "_" + space_bin_Chunk_table(i).chunk_name + ".bin", d)
            Next
        End If
        'we mush grab this data first!
        For t_cnt = 0 To table_size - 1
            Dim header As String = space_bin_Chunk_table(t_cnt).chunk_name
            Select Case True
                Case header = "BSGD"
                    If Not get_BSGD(t_cnt, br) Then
                        MsgBox("BSGD decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
            End Select
        Next

        '----------------------------------------------------------------------------------
        'Now we will grab the game data we need.
        For t_cnt = 0 To table_size - 1
            Dim header As String = space_bin_Chunk_table(t_cnt).chunk_name
            Select Case True
                Case header = "BWST"
                    If Not get_BWST(t_cnt, br) Then
                        MsgBox("BWST decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "BWAL"
                    If Not get_BWAL(t_cnt, br) Then
                        MsgBox("BWAL decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "BWCS"
                Case header = "BWSG"
                    If Not get_BWSG(t_cnt, br) Then
                        MsgBox("BWSG decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "BWS2"
                Case header = "BSG2"
                Case header = "BWT2"
                Case header = "BSMI"
                    If Not get_BSMI(t_cnt, br) Then
                        MsgBox("BSMI decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "BSMO"
                    If Not get_BSMO(t_cnt, br) Then
                        MsgBox("BSMO) decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select

                Case header = "BSMA"
                    If Not get_BSMA(t_cnt, br) Then
                        MsgBox("BSMA) decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "SpTr"
                    If Not get_SPTR(t_cnt, br) Then
                        MsgBox("SPTR decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "WGSD"
                    If Not get_WGSD(t_cnt, br) Then
                        MsgBox("WGSD decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "WTCP"
                Case header = "BWWa"
                    If Not get_BWWa(t_cnt, br) Then
                        MsgBox("BWWa decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "BWEP"
                Case header = "WGCO"
                Case header = "BWPs"
                Case header = "CENT"
                Case header = "UDOS"
                Case header = "WGDE"
                Case header = "BWLC"
                Case header = "WTau"
                Case header = "WTbl"
                    If Not get_WTbl(t_cnt, br) Then
                        MsgBox("Wtbl decode Failed", MsgBoxStyle.Exclamation, "Oh NO!!")
                        Return False
                    End If
                    Exit Select
                Case header = "WGSH"
                Case header = "WGMM"
            End Select
        Next
        '----------------------------------------------------------------------------------
        'build the model information
        ReDim MAP_MODELS(cBSMO.model_entries.Length - 1)
        For k = 0 To cBSMO.model_entries.Length - 1
            MAP_MODELS(k) = New mdl_
            ReDim MAP_MODELS(k).mdl(1)
            MAP_MODELS(k).mdl(0) = New base_model_holder_

            MAP_MODELS(k).mdl(0).primitive_name = cBSMO.model_entries(k).model_name

            With MAP_MODELS(k).mdl(0)

                .sb_model_material_begin = cBSMO.model_entries(k).model_material_kind_begin
                .sb_model_material_end = cBSMO.model_entries(k).model_material_kind_end
                'get lod set pointers for this model
                Dim lod0 = .sb_LOD_set_start

                Dim entry_count = MAP_MODELS(k).mdl(0).sb_model_material_end - MAP_MODELS(k).mdl(0).sb_model_material_begin
                Dim L_start, L_end As Integer
                'Used to index in to lodRenderItems
                Dim r_set_begin, r_set_end As Integer

                r_set_begin = cBSMO.lodRenderItem(L_start).render_set_begin
                r_set_end = cBSMO.lodRenderItem(L_end).render_set_end

                L_start = cBSMO.render_item_ranges(k).lod_start
                L_end = cBSMO.render_item_ranges(k).lod_end

                Dim component_cnt = r_set_end - r_set_begin
                If component_cnt > 4 Then
                    'Stop
                End If
                ReDim .entries(component_cnt)
                .primitive_count = component_cnt + 1
                Dim run_cnt As Integer = 0
                For z = 0 To component_cnt
                    .entries(z) = New entries_
                    Dim mat_index = cBSMO.renderItem(z)
                    .entries(z).identifier = cBSMA.MaterialItem(z).identifier
                    .entries(z).FX_shader = cBSMA.MaterialItem(z).FX_string
                    Dim l_cnt = cBSMA.MaterialItem(z).shaderPropEnd - cBSMA.MaterialItem(z).shaderPropBegin
                    For j = cBSMA.MaterialItem(z).shaderPropBegin To cBSMA.MaterialItem(z).shaderPropEnd - 1
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
        ReDim MODEL_MATRIX_LIST(cBSMI.tbl_2.Length - 1)
        Dim cnt As Integer = 0
        For k = 0 To cBSMI.model_BSMO_indexes.Length - 1
            MODEL_MATRIX_LIST(k) = New model_matrix_list_
            Dim BSMO_MODEL_INDEX = cBSMI.model_BSMO_indexes(k).BSMO_MODEL_INDEX

            MODEL_MATRIX_LIST(k).model_index = BSMO_MODEL_INDEX
            If BSMO_MODEL_INDEX > max_id Then
                max_id = BSMO_MODEL_INDEX
            End If

            Dim primitive_name = ""
            If cBSMO.model_entries(BSMO_MODEL_INDEX).model_name IsNot Nothing Then
                primitive_name = cBSMO.model_entries(BSMO_MODEL_INDEX).model_name.Replace("primitives", "model")
            End If
            cBSMO.model_entries(BSMO_MODEL_INDEX).model_name.Replace("primitives", "model")
            MODEL_MATRIX_LIST(k).primitive_name = primitive_name

            MODEL_MATRIX_LIST(k).matrix = cBSMI.matrix_list(k)
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
            MODEL_MATRIX_LIST(k).BB_Min = cBSMO.model_entries(BSMO_MODEL_INDEX).min_BB
            MODEL_MATRIX_LIST(k).BB_Max = cBSMO.model_entries(BSMO_MODEL_INDEX).max_BB

            'create model culling box
            ReDim MODEL_MATRIX_LIST(k).BB(8)
            get_translated_bb_model(MODEL_MATRIX_LIST(k))

        Next
        '----------------------------------------------------------------------------------
        ' remove models that we dont want on the map
        Dim mc As Int32 = 0
        Dim HQ As Integer = 1
        Dim tm(cBSMI.model_BSMO_indexes.Length - 1) As model_matrix_list_
        For i = 0 To cBSMI.model_BSMO_indexes.Length - 1
            If cBSMI.model_BSMO_indexes(i).BSMO_extras = HQ Then
                If cBSMI.vis_mask(i).mask = &HFFFFFFFF Then 'visibility mask
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

        Return True
    End Function
End Module

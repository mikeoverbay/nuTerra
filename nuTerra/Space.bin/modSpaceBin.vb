Imports System.IO
Imports System.Text

Module modSpaceBin
    Public sectionHeaders As Dictionary(Of String, SectionHeader)
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
        If Not File.Exists(TEMP_STORAGE + p) Then
            GoTo Failed
        End If

        Dim f = File.OpenRead(TEMP_STORAGE + p)

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
            'BWT2
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

        '----------------------------------------------------------------------------------
        'build the model information
        ReDim MAP_MODELS(cBSMO.models_colliders.count - 1)
        For k = 0 To cBSMO.models_colliders.count - 1
            ReDim MAP_MODELS(k).mdl(1)

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

                ' Hack for now
                If shader_prop_start = &HFFFFFFFFUI Then
                    Debug.Print("shader_prop_start = &HFFFFFFFFUI")
                    MAP_MODELS(k).mdl(0).junk = True
                    GoTo ignore_this_one
                End If

                'this is all wrong.. we don't want LODs! We Want the total primitiveGroups!
                'I can NOT figure out how to get the list of all the primitivegroups!!
                Dim component_cnt = shader_prop_end - shader_prop_start
                If .primitive_name.ToLower.Contains("vhouse_05") Then
                    Stop
                End If

                ReDim .entries(component_cnt)
                .primitive_count = component_cnt + 1
                Dim run_cnt As Integer = 0
                For z As UInteger = 0 To component_cnt
                    Dim mat_index = cBSMO.renders.data(z)
                    .entries(z).identifier = cBSMA.MaterialItem(z + shader_prop_start).identifier

                    If .entries(z).identifier Is Nothing Then
                        Continue For
                    End If

                    .entries(z).FX_shader = cBSMA.FXStringKey(cBSMA.MaterialItem(z + shader_prop_start).effectIndex).FX_string
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
        ReDim MODEL_INDEX_LIST(cBSMI.model_BSMO_indexes.count - 1)
        Dim cnt As Integer = 0
        For k = 0 To cBSMI.model_BSMO_indexes.count - 1
            Dim BSMO_MODEL_INDEX = cBSMI.model_BSMO_indexes.data(k).BSMO_MODEL_INDEX

            MODEL_INDEX_LIST(k).model_index = BSMO_MODEL_INDEX
            If BSMO_MODEL_INDEX > max_id Then
                max_id = BSMO_MODEL_INDEX
            End If

            MODEL_INDEX_LIST(k).primitive_name = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).model_name

            MODEL_INDEX_LIST(k).matrix = cBSMI.transforms.data(k)

            'Flip some row values to convert from DirectX to Opengl
            MODEL_INDEX_LIST(k).matrix.M12 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M13 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M21 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M31 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M41 *= -1.0

            MODEL_INDEX_LIST(k).mask = False
            MODEL_INDEX_LIST(k).BB_Min = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).collision_bounds_min
            MODEL_INDEX_LIST(k).BB_Max = cBSMO.models_colliders.data(BSMO_MODEL_INDEX).collision_bounds_max

            'The X has to be flipped jsut like the vertices of any model.
            MODEL_INDEX_LIST(k).BB_Min.X *= -1.0F
            MODEL_INDEX_LIST(k).BB_Max.X *= -1.0F
            'create model culling box
            ReDim MODEL_INDEX_LIST(k).BB(8)
            Transform_BB(MODEL_INDEX_LIST(k))

        Next
        '----------------------------------------------------------------------------------
        ' remove models that we dont want on the map
        Dim mc As Int32 = 0
        Dim HQ As Integer = 1
        Dim tm(cBSMI.model_BSMO_indexes.count - 1) As MODEL_INDEX_LIST_
        For i = 0 To cBSMI.model_BSMO_indexes.count - 1
            If cBSMI.model_BSMO_indexes.data(i).BSMO_extras = HQ Then
                If cBSMI.visibility_masks.data(i).mask = &HFFFFFFFFUI Then 'visibility mask
                    tm(mc) = MODEL_INDEX_LIST(i)
                    mc += 1
                Else
                    'Debug.WriteLine(i.ToString("00000") + ":" + cBSMI.bsmi_t7(i).v_mask.ToString("x") + ":" + Path.GetFileNameWithoutExtension(MODEL_INDEX_LIST(i).primitive_name))
                End If
            End If
        Next
        ReDim Preserve tm(mc - 1)
        ReDim MODEL_INDEX_LIST(mc - 1)
        'pack the MODEL_INDEX_LIST to used models.
        For i = 0 To mc - 1
            If tm(i).BB Is Nothing Then
                Stop
            End If
            MODEL_INDEX_LIST(i) = tm(i)
            If MODEL_INDEX_LIST(i).exclude = True Then
                'Debug.WriteLine(i.ToString("0000") + " : " + MODEL_INDEX_LIST(i).primitive_name)
            End If
        Next

        ReadSpaceBinData = True
        GoTo CleanUp

Failed:
        ReadSpaceBinData = False

CleanUp:
        'Clear headers
        sectionHeaders = Nothing

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
        '====================================================
        ' Sort and batch the models for instanced drawing
        '====================================================
        Dim MaxEstimate As Integer = 300 ' Most we expect of any one model Id
        Dim sanity_check As Integer = 1
        Array.Sort(MODEL_INDEX_LIST) 'sort our list by model_index
        ReDim MODEL_BATCH_LIST(1000)
        MODEL_BATCH_LIST(0) = New MODEL_BATCH_LIST_
        ReDim MODEL_BATCH_LIST(0).MAP_MODEL_INDEX_LIST(MaxEstimate) ' initlize first cell
        ReDim MODEL_BATCH_LIST(0).MATRIX_INDEX_LIST(MaxEstimate) ' initlize first cell

        'We need to set the very first model_index in our batch
        MODEL_BATCH_LIST(0).MAP_MODEL_INDEX_LIST(0) = MODEL_INDEX_LIST(0).model_index
        MODEL_BATCH_LIST(0).MATRIX_INDEX_LIST(0) = 0

        Dim b_pntr, i_pntr As Integer
        For i = 0 To MODEL_INDEX_LIST.Length - 2
            Dim id = MODEL_INDEX_LIST(i).model_index
            'if the next one matches, add it to this batch
            If id = MODEL_INDEX_LIST(i + 1).model_index Then
                MODEL_BATCH_LIST(b_pntr).MAP_MODEL_INDEX_LIST(i_pntr + 1) = MODEL_INDEX_LIST(i + 1).model_index
                MODEL_BATCH_LIST(b_pntr).MATRIX_INDEX_LIST(i_pntr + 1) = i + 1
                i_pntr += 1
            Else
                'If it does not, store the count of this model_index and
                'redim the size and grab the next id that is new
                ReDim Preserve MODEL_BATCH_LIST(b_pntr).MAP_MODEL_INDEX_LIST(i_pntr)
                ReDim Preserve MODEL_BATCH_LIST(b_pntr).MATRIX_INDEX_LIST(i_pntr)
                MODEL_BATCH_LIST(b_pntr).count = i_pntr ' save count
                b_pntr += 1 ' next batch
                sanity_check += i_pntr + 1
                i_pntr = 0 ' reset the cnt
                ReDim MODEL_BATCH_LIST(b_pntr).MAP_MODEL_INDEX_LIST(MaxEstimate) ' reserve lots a room
                ReDim  MODEL_BATCH_LIST(b_pntr).MATRIX_INDEX_LIST(MaxEstimate) ' reserve lots a room
                MODEL_BATCH_LIST(b_pntr).MAP_MODEL_INDEX_LIST(0) = MODEL_INDEX_LIST(i + 1).model_index ' initialize first entry
                MODEL_BATCH_LIST(b_pntr).MATRIX_INDEX_LIST(0) = i + 1

                If i + 1 = MODEL_INDEX_LIST.Length - 1 Then
                    MODEL_BATCH_LIST(b_pntr).count = i_pntr ' save count
                    ReDim Preserve MODEL_BATCH_LIST(b_pntr).MAP_MODEL_INDEX_LIST(i_pntr) 'redim size
                    ReDim Preserve MODEL_BATCH_LIST(b_pntr).MATRIX_INDEX_LIST(i_pntr)
                    Exit For ' so we dont over run the MODEL_INDEX_LIST

                End If
            End If
        Next
        ReDim Preserve MODEL_BATCH_LIST(b_pntr) 'resize the batch to batches count.


    End Function
End Module

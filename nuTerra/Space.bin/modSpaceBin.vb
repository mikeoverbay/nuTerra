
#Region "imports"
Imports System.IO
Imports System.Windows.Forms
Imports System.Math
Imports System.String
Imports System.Text
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
            MAP_MODELS(k) = New base_model_holder_
            MAP_MODELS(k).sb_model_material_begin = cBSMO.model_entries(k).model_material_kind_begin
            MAP_MODELS(k).sb_model_material_end = cBSMO.model_entries(k).model_material_kind_end
            MAP_MODELS(k).sb_render_set_begin = cBSMO.render_item_ranges(k).lod_start
            MAP_MODELS(k).sb_render_set_end = cBSMO.render_item_ranges(k).lod_end


        Next
        'MAP_MODELS(k).primitive_name = cBWSG.primitive_entries(k).model
        'MAP_MODELS(k).sb_vertex_count = cBWSG.primitive_entries(k).vertex_count
        'MAP_MODELS(k).sb_vertex_type = cBWSG.primitive_entries(k).vertex_type
        'MAP_MODELS(k).sb_start_index = cBWSG.primitive_entries(k).start_idx
        'MAP_MODELS(k).sb_end_index = cBWSG.primitive_entries(k).end_idx
        'MAP_MODELS(k).sp_table_size = MAP_MODELS(k).sb_end_index - MAP_MODELS(k).sb_end_index 'gets vertex and uv2 data

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

        ReDim MODEL_MATRIX_LIST(cBSMI.tbl_2.Length - 1)
        Dim cnt As Integer = 0
        For k = 0 To cBSMI.model_BSMO_indexes.Length - 1
            MODEL_MATRIX_LIST(k) = New model_matrix_list_
            Dim BSMO_MODEL_INDEX = cBSMI.model_BSMO_indexes(k).BSMO_MODEL_INDEX
            Dim primitive_name = ""
            If cBSMO.model_entries(BSMO_MODEL_INDEX).model_name IsNot Nothing Then
                primitive_name = cBSMO.model_entries(BSMO_MODEL_INDEX).model_name.Replace("primitives", "model")
            End If
            cBSMO.model_entries(BSMO_MODEL_INDEX).model_name.Replace("primitives", "model")
            MODEL_MATRIX_LIST(k).primitive_name = primitive_name

            MODEL_MATRIX_LIST(k).matrix = cBSMI.matrix_list(k).matrix
            MODEL_MATRIX_LIST(k).matrix(1) *= -1.0
            MODEL_MATRIX_LIST(k).matrix(2) *= -1.0
            MODEL_MATRIX_LIST(k).matrix(4) *= -1.0
            MODEL_MATRIX_LIST(k).matrix(8) *= -1.0
            MODEL_MATRIX_LIST(k).matrix(12) *= -1.0

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

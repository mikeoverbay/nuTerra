
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
        If Not File.Exists(Temp_Storage + p) Then
            Return False
        End If
        Dim sb As New StringBuilder
        Dim f = File.OpenRead(Temp_Storage + p)
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
        If True Then
            If Not Directory.Exists("C:\!_bin_data\") Then
                Directory.CreateDirectory("C:\!_bin_data\")
            End If
            File.WriteAllText("C:\!_bin_data\headers.txt", sb.ToString)
        End If

        If True Then
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
            Select True
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
        ReDim Model_Matrix_list(cBSMI.tbl_2.Length - 1)
        Dim cnt As Integer = 0
        For k = 0 To cBSMI.model_BSMO_indexes.Length - 1
            Model_Matrix_list(k) = New model_matrix_list_
            Dim BSMO_Index = cBSMI.model_BSMO_indexes(k).BSMO_index
            Dim primitive_name = ""
            If cBSMO.model_entries(BSMO_Index).model_name IsNot Nothing Then
                primitive_name = cBSMO.model_entries(BSMO_Index).model_name.Replace("primitives", "model")
            End If
            cBSMO.model_entries(BSMO_Index).model_name.Replace("primitives", "model")
            Model_Matrix_list(k).primitive_name = primitive_name

            Model_Matrix_list(k).matrix = cBSMI.matrix_list(k).matrix
            Model_Matrix_list(k).matrix(1) *= -1.0
            Model_Matrix_list(k).matrix(2) *= -1.0
            Model_Matrix_list(k).matrix(4) *= -1.0
            Model_Matrix_list(k).matrix(8) *= -1.0
            Model_Matrix_list(k).matrix(12) *= -1.0

            Model_Matrix_list(k).mask = False
            Model_Matrix_list(k).BB_Min = cBSMO.model_entries(BSMO_Index).min_BB
            Model_Matrix_list(k).BB_Max = cBSMO.model_entries(BSMO_Index).max_BB

            'create model culling box
            ReDim Model_Matrix_list(k).BB(8)
            get_translated_bb_model(Model_Matrix_list(k))

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
                    tm(mc) = Model_Matrix_list(i)
                    mc += 1
                Else
                    'Debug.WriteLine(i.ToString("00000") + ":" + cBSMI.bsmi_t7(i).v_mask.ToString("x") + ":" + Path.GetFileNameWithoutExtension(Model_Matrix_list(i).primitive_name))
                End If
            End If
        Next
        ReDim Preserve tm(mc)
        ReDim Model_Matrix_list(mc)
        'pack the Model_Matrix_list to used models.
        For i = 0 To mc
            Model_Matrix_list(i) = tm(i)
            If Model_Matrix_list(i).exclude = True Then
                'Debug.WriteLine(i.ToString("0000") + " : " + Model_Matrix_list(i).primitive_name)
            End If

        Next

        Return True
    End Function
End Module

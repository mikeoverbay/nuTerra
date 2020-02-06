Imports System.IO
Imports System.Windows.Forms
Imports System.Math
Imports System.String
Imports System.Text

Module modSpaceBinFunctions

    Public Function get_BSGD(tbl As Integer, br As BinaryReader) As Boolean

        Try
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            cBSGD = New cBSGD_
            cBSGD.data = br.ReadBytes(space_bin_Chunk_table(tbl).chunk_length)
        Catch ex As Exception
            Return False
        End Try
        Return True
    End Function

    Public Function get_BWST(tbl As Integer, br As BinaryReader) As Boolean

        'set stream reader to point at this chunk
        br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

        cBWST = New cBWST_

        Dim debug_strings As New StringBuilder
        '----------------------------------------------------------------------
        Try
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            Dim d_length = br.ReadUInt32
            Dim entry_cnt = br.ReadUInt32
            ReDim cBWST.strs(entry_cnt)
            ReDim cBWST.keys(entry_cnt)
            Dim old_pos = br.BaseStream.Position
            Dim start_offset As Long = space_bin_Chunk_table(tbl).chunk_Start + (d_length * entry_cnt) + 12
            For k = 0 To entry_cnt - 1
                br.BaseStream.Position = old_pos
                cBWST.keys(k) = br.ReadUInt32
                Dim offset = br.ReadUInt32
                Dim length = br.ReadUInt32
                old_pos = br.BaseStream.Position
                'move to strings locations and read it
                br.BaseStream.Position = offset + start_offset
                Dim ds() = br.ReadBytes(length)
                cBWST.strs(k) = System.Text.Encoding.UTF8.GetString(ds, 0, length)
                '------------------------------------------------------------
                'For debug/help at sorting things out
                Dim hexCharArray = BitConverter.GetBytes(cBWST.keys(k))
                Dim hexStringReversed = BitConverter.ToString(hexCharArray)
                debug_strings.AppendLine(hexStringReversed.Replace("-", "") + " : " + cBWST.strs(k))
                '------------------------------------------------------------
            Next
        Catch ex As Exception
            Return False
        End Try

        If False Then 'If true save the string data for debug/help
            If Not Directory.Exists("C:\!_bin_data\") Then
                Directory.CreateDirectory("C:\!_bin_data\")
            End If
            File.WriteAllText("C:\!_bin_data\BWST_strings.txt", debug_strings.ToString)
        End If
        debug_strings.Length = 0
        GC.Collect()
        Return True

    End Function

    Public Function get_BWSG(tbl As Integer, br As BinaryReader) As Boolean
        Dim debug_strings As New StringBuilder

        'set stream reader to point at this chunk
        br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

        cBWSG = New cBWSG_
        '----------------------------------------------------------------------
        'get strings section
        Dim d_length = br.ReadUInt32
        Dim entry_cnt = br.ReadUInt32
        ReDim cBWSG.strs(entry_cnt)
        ReDim cBWSG.keys(entry_cnt)
        Dim old_pos = br.BaseStream.Position
        Dim start_offset As Long = space_bin_Chunk_table(tbl).chunk_Start + (d_length * entry_cnt) + 12
        For k = 0 To entry_cnt - 1
            br.BaseStream.Position = old_pos
            cBWSG.keys(k) = br.ReadUInt32
            Dim offset = br.ReadInt32
            Dim length = br.ReadInt32
            old_pos = br.BaseStream.Position
            'move to strings locations and read it
            br.BaseStream.Position = offset + start_offset
            Dim ds() = br.ReadBytes(length) 'get the string as bytes
            br.ReadByte() 'read off the extra byte
            cBWSG.strs(k) = System.Text.Encoding.UTF8.GetString(ds, 0, length) ' convert to string
            '------------------------------------------------------------
            'For debug/help at sorting things out
            Dim hexCharArray = BitConverter.GetBytes(cBWSG.keys(k))
            Dim hexStringReversed = BitConverter.ToString(hexCharArray)
            debug_strings.AppendLine(hexStringReversed.Replace("-", "") + _
                                     " : " + cBWSG.strs(k))
            '------------------------------------------------------------
        Next

        '----------------------------------------------------------------------
        'get primitive entries 
        d_length = br.ReadUInt32
        entry_cnt = br.ReadUInt32

        ReDim cBWSG.primitive_entries(entry_cnt - 1)
        For k = 0 To entry_cnt - 1
            cBWSG.primitive_entries(k) = New cBWSG_.primitive_entries_
            cBWSG.primitive_entries(k).str_key1 = br.ReadUInt32
            cBWSG.primitive_entries(k).start_idx = br.ReadUInt32 ' points at primitive data list
            cBWSG.primitive_entries(k).end_idx = br.ReadUInt32 ' points at primitive data list
            cBWSG.primitive_entries(k).vertex_count = br.ReadUInt32
            cBWSG.primitive_entries(k).str_key2 = br.ReadUInt32
            cBWSG.primitive_entries(k).model = find_str_BWSG(cBWSG.primitive_entries(k).str_key1)
            cBWSG.primitive_entries(k).vertex_type = find_str_BWSG(cBWSG.primitive_entries(k).str_key2)
        Next

        '----------------------------------------------------------------------
        'get primitive data list 
        d_length = br.ReadUInt32
        entry_cnt = br.ReadUInt32

        ReDim cBWSG.primitive_data_list(entry_cnt - 1)
        For k = 0 To entry_cnt - 1
            cBWSG.primitive_data_list(k) = New cBWSG_.primitive_data_list_
            cBWSG.primitive_data_list(k).block_type = br.ReadUInt32
            cBWSG.primitive_data_list(k).vertex_stride = br.ReadUInt32
            cBWSG.primitive_data_list(k).chunkDataBlockLength = br.ReadInt32
            cBWSG.primitive_data_list(k).chunkDataBlockIndex = br.ReadUInt32
            cBWSG.primitive_data_list(k).chunkDataOffset = br.ReadUInt32

        Next

        '----------------------------------------------------------------------
        'get primitive data list 
        d_length = br.ReadUInt32
        entry_cnt = br.ReadUInt32

        ReDim cBWSG.cBWSG_VertexDataChunks(entry_cnt - 1)
        For k = 0 To entry_cnt - 1
            cBWSG.cBWSG_VertexDataChunks(k) = New cBWSG_.raw_data_
            cBWSG.cBWSG_VertexDataChunks(k).data_size = br.ReadUInt32
        Next
        Dim cBSGD_ms As New MemoryStream(cBSGD.data)
        Dim cBSGD_br As New BinaryReader(cBSGD_ms)

        For k = 0 To entry_cnt - 1
            ReDim cBWSG.cBWSG_VertexDataChunks(k).data(cBWSG.cBWSG_VertexDataChunks(k).data_size - 1)
            cBWSG.cBWSG_VertexDataChunks(k).data = cBSGD_br.ReadBytes(cBWSG.cBWSG_VertexDataChunks(k).data_size)
        Next
        cBSGD_ms.Close()

        '----------------------------------------------------------------------
        Dim byt As Byte
        Dim running As Long = 0
        For i = 0 To cBWSG.primitive_data_list.Length - 1
            Dim l = cBWSG.primitive_data_list(i).chunkDataBlockLength
            Dim o = cBWSG.primitive_data_list(i).chunkDataOffset
            Dim ind = cBWSG.primitive_data_list(i).chunkDataBlockIndex
            ReDim cBWSG.primitive_data_list(i).data(l - 1)
            running += l
            For k = 0 To l - 1
                byt = cBWSG.cBWSG_VertexDataChunks(ind).data(o + k)
                cBWSG.primitive_data_list(i).data(k) = byt
            Next
            If False Then
                If Not Directory.Exists("C:\!_bin_data\") Then
                    Directory.CreateDirectory("C:\!_bin_data\")
                End If
                Dim path_ = "C:\!_bin_data\" + "cBWSG_VertexDataChunks_" + ind.ToString("00") + "_" + i.ToString("0000") + ".bin"
                File.WriteAllBytes(path_, cBWSG.primitive_data_list(i).data)
            End If

        Next

        Return True
    End Function

    Public Function get_BSMI(tbl As Integer, br As BinaryReader) As Boolean
        Try
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start
            cBSMI = New cBSMI_

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table
            'get matrix table
            ReDim cBSMI.matrix_list(tl - 1)
            For k = 0 To tl - 1
                'read in row order
                cBSMI.matrix_list(k).M11 = br.ReadSingle
                cBSMI.matrix_list(k).M12 = br.ReadSingle
                cBSMI.matrix_list(k).M13 = br.ReadSingle
                cBSMI.matrix_list(k).M14 = br.ReadSingle

                cBSMI.matrix_list(k).M21 = br.ReadSingle
                cBSMI.matrix_list(k).M22 = br.ReadSingle
                cBSMI.matrix_list(k).M23 = br.ReadSingle
                cBSMI.matrix_list(k).M24 = br.ReadSingle

                cBSMI.matrix_list(k).M31 = br.ReadSingle
                cBSMI.matrix_list(k).M32 = br.ReadSingle
                cBSMI.matrix_list(k).M33 = br.ReadSingle
                cBSMI.matrix_list(k).M34 = br.ReadSingle

                cBSMI.matrix_list(k).M41 = br.ReadSingle
                cBSMI.matrix_list(k).M42 = br.ReadSingle
                cBSMI.matrix_list(k).M43 = br.ReadSingle
                cBSMI.matrix_list(k).M44 = br.ReadSingle

                '+++++++++++++++++++++++++++++++++++++++++

                'cBSMI.matrix_list(k).M11 = br.ReadSingle
                'cBSMI.matrix_list(k).M21 = br.ReadSingle
                'cBSMI.matrix_list(k).M31 = br.ReadSingle
                'cBSMI.matrix_list(k).M41 = br.ReadSingle

                'cBSMI.matrix_list(k).M12 = br.ReadSingle
                'cBSMI.matrix_list(k).M22 = br.ReadSingle
                'cBSMI.matrix_list(k).M32 = br.ReadSingle
                'cBSMI.matrix_list(k).M42 = br.ReadSingle

                'cBSMI.matrix_list(k).M13 = br.ReadSingle
                'cBSMI.matrix_list(k).M23 = br.ReadSingle
                'cBSMI.matrix_list(k).M33 = br.ReadSingle
                'cBSMI.matrix_list(k).M43 = br.ReadSingle

                'cBSMI.matrix_list(k).M14 = br.ReadSingle
                'cBSMI.matrix_list(k).M24 = br.ReadSingle
                'cBSMI.matrix_list(k).M34 = br.ReadSingle
                'cBSMI.matrix_list(k).M44 = br.ReadSingle

            Next
            '--------------------------------------------------------------

            'Get destructable model list ?
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.tbl_2(tl - 1)
            For k = 0 To tl - 1
                cBSMI.tbl_2(k).index1 = br.ReadInt32 '?
                cBSMI.tbl_2(k).index2 = br.ReadInt32 '?
            Next
            '--------------------------------------------------------------

            'Get visiblity mask
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.vis_mask(tl - 1)
            For k = 0 To tl - 1
                cBSMI.vis_mask(k).mask = br.ReadInt32
            Next
            '--------------------------------------------------------------

            'Get BSMO model Index IDs
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.model_BSMO_indexes(tl - 1)
            For k = 0 To tl - 1
                cBSMI.model_BSMO_indexes(k).BSMO_MODEL_INDEX = br.ReadUInt32
                cBSMI.model_BSMO_indexes(k).BSMO_extras = br.ReadUInt32 'if this matches the model index, its a building on the map that can be destoryed/damaged.
            Next
            '--------------------------------------------------------------

            'Get animation tbl
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.animation_tbl(tl - 1)
            For k = 0 To tl - 1
                cBSMI.animation_tbl(k).is_animation = br.ReadInt32
            Next
            '--------------------------------------------------------------

            'Get animation info
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.animation_info(tl - 1)
            For k = 0 To tl - 1
                cBSMI.animation_info(k).model_index = br.ReadUInt32
                cBSMI.animation_info(k).seq_resource_key = br.ReadUInt32
                cBSMI.animation_info(k).clip_name_key = br.ReadUInt32
                cBSMI.animation_info(k).auto_start = br.ReadUInt32
                cBSMI.animation_info(k).loop_cnt = br.ReadUInt32
                cBSMI.animation_info(k).speed = br.ReadSingle
                cBSMI.animation_info(k).delay = br.ReadSingle
                cBSMI.animation_info(k).unknown = br.ReadSingle
            Next
            '--------------------------------------------------------------

            'Get tbl 7
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            br.BaseStream.Position += (ds * tl)
            GoTo skip_unknown1
            ReDim cBSMI.tbl_7(tl - 1)
            For k = 0 To tl - 1
                cBSMI.tbl_7(k).unknown1 = br.ReadUInt32
                cBSMI.tbl_7(k).unknown2 = br.ReadUInt32
            Next
            '--------------------------------------------------------------
skip_unknown1:

            'Get skined_tbl
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.skined_tbl(tl - 1)
            For k = 0 To tl - 1
                cBSMI.skined_tbl(k).index1 = br.ReadUInt32
                cBSMI.skined_tbl(k).index2 = br.ReadUInt32
                cBSMI.skined_tbl(k).index3 = br.ReadUInt32
            Next
            '--------------------------------------------------------------

            'Get tbl 9
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.tbl_9(tl - 1)
            For k = 0 To tl - 1
                cBSMI.tbl_9(k).index1 = br.ReadUInt32
            Next
            '--------------------------------------------------------------

            'Get tbl 10
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMI.tbl_10(tl - 1)
            For k = 0 To tl - 1
                cBSMI.tbl_10(k).s1 = br.ReadSingle
                cBSMI.tbl_10(k).s2 = br.ReadSingle
                cBSMI.tbl_10(k).s3 = br.ReadSingle
                cBSMI.tbl_10(k).s4 = br.ReadSingle
                cBSMI.tbl_10(k).s5 = br.ReadSingle

            Next

        Catch ex As Exception
            Debug.Print(ex.ToString)
            Return False
        End Try

        Return True
    End Function

    Public Function get_WTbl(tbl As Integer, br As BinaryReader)

        Try
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start
            cWtbl = New cWtbl_

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table
            ReDim cWtbl.tbl_1(tl - 1)
            For k = 0 To tl - 1
                cWtbl.tbl_1(k).s1 = br.ReadSingle
                cWtbl.tbl_1(k).s2 = br.ReadSingle
                cWtbl.tbl_1(k).s3 = br.ReadSingle
            Next
            '------------------------------------------------------------

            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cWtbl.tbl_2(tl - 1)
            For k = 0 To tl - 1
                cWtbl.tbl_2(k).flag1 = br.ReadUInt32
                cWtbl.tbl_2(k).flag2 = br.ReadUInt32
                cWtbl.tbl_2(k).flag3 = br.ReadUInt32
                cWtbl.tbl_2(k).flag4 = br.ReadUInt32
                cWtbl.tbl_2(k).flag5 = br.ReadUInt32
            Next

        Catch ex As Exception
            Debug.Print(ex.ToString)
            Return False
        End Try

        Return True
    End Function

    Public Function get_BSMO(tbl As Integer, br As BinaryReader)

        Try
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start
            cBSMO = New cBSMO_

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.render_item_ranges(tl - 1)
            For k = 0 To tl - 1 'not sure these are even named right
                cBSMO.render_item_ranges(k).lod_start = br.ReadUInt32
                cBSMO.render_item_ranges(k).lod_end = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'tbl_2
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.tbl_2(tl - 1)
            For k = 0 To tl - 1
                cBSMO.tbl_2(k).unknown = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'model_entries tbl_3
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.model_entries(tl - 1)
            For k = 0 To tl - 1
                cBSMO.model_entries(k).min_BB.x = -br.ReadSingle 'make negitive because of GL rendering!
                cBSMO.model_entries(k).min_BB.y = br.ReadSingle
                cBSMO.model_entries(k).min_BB.z = br.ReadSingle

                cBSMO.model_entries(k).max_BB.x = -br.ReadSingle
                cBSMO.model_entries(k).max_BB.y = br.ReadSingle
                cBSMO.model_entries(k).max_BB.z = br.ReadSingle

                cBSMO.model_entries(k).Model_String_key = br.ReadUInt32
                cBSMO.model_entries(k).model_material_kind_begin = br.ReadInt32
                cBSMO.model_entries(k).model_material_kind_end = br.ReadInt32
                cBSMO.model_entries(k).model_name = find_str_BWST(cBSMO.model_entries(k).Model_String_key)
            Next
            '--------------------------------------------------------

            'material_kind tbl_4
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.material_kind(tl - 1)
            For k = 0 To tl - 1
                cBSMO.material_kind(k).mat_index = br.ReadUInt32
                cBSMO.material_kind(k).flags = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'model_visibility_bb tbl_5
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.model_visibility_bb(tl - 1)
            For k = 0 To tl - 1
                cBSMO.model_visibility_bb(k).min_BB.x = -br.ReadSingle 'make negitive because of GL rendering!
                cBSMO.model_visibility_bb(k).min_BB.y = br.ReadSingle
                cBSMO.model_visibility_bb(k).min_BB.z = br.ReadSingle

                cBSMO.model_visibility_bb(k).max_BB.x = -br.ReadSingle 'make negitive because of GL rendering!
                cBSMO.model_visibility_bb(k).max_BB.y = br.ReadSingle
                cBSMO.model_visibility_bb(k).max_BB.z = br.ReadSingle
            Next
            '--------------------------------------------------------

            'tbl_6
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.tbl_6(tl - 1)
            For k = 0 To tl - 1
                cBSMO.tbl_6(k).index1 = br.ReadUInt32
                cBSMO.tbl_6(k).index2 = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'tbl_7
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.tbl_7(tl - 1)
            For k = 0 To tl - 1
                cBSMO.tbl_7(k).index1 = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'tbl_8
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.lod_range(tl - 1)
            For k = 0 To tl - 1
                cBSMO.lod_range(k).range = br.ReadSingle
            Next
            '--------------------------------------------------------

            'lodRenderItem tbl_9
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.lodRenderItem(tl - 1)
            For k = 0 To tl - 1
                cBSMO.lodRenderItem(k).render_set_begin = br.ReadUInt32
                cBSMO.lodRenderItem(k).render_set_end = br.ReadUInt32

            Next
            '--------------------------------------------------------

            'renderItem tbl_10
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.renderItem(tl - 1)
            For k = 0 To tl - 1
                cBSMO.renderItem(k).node_start = br.ReadUInt32
                cBSMO.renderItem(k).node_end = br.ReadUInt32
                cBSMO.renderItem(k).mat_index = br.ReadUInt32
                cBSMO.renderItem(k).primtive_index = br.ReadUInt32
                cBSMO.renderItem(k).vert_string_key = br.ReadUInt32
                cBSMO.renderItem(k).indi_string_key = br.ReadUInt32
                cBSMO.renderItem(k).is_skinned = br.ReadUInt32
                cBSMO.renderItem(k).vert_name = find_str_BWST(cBSMO.renderItem(k).vert_string_key)
                cBSMO.renderItem(k).indi_name = find_str_BWST(cBSMO.renderItem(k).indi_string_key)
                'cBSMO.renderItem (k).pad_31 <-- hmmmm
            Next
            '--------------------------------------------------------

            'tbl_11
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.tbl_11(tl - 1)
            For k = 0 To tl - 1
                cBSMO.tbl_11(k).index1 = br.ReadUInt32
            Next
            '--------------------------------------------------------

            'NodeItem tbl_12
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBSMO.NodeItem(tl - 1)
            For k = 0 To tl - 1
                cBSMO.NodeItem(k).parent_index = br.ReadUInt32
                ReDim cBSMO.NodeItem(k).matrix(15)
                For i = 0 To 15
                    cBSMO.NodeItem(k).matrix(i) = br.ReadSingle
                Next
                cBSMO.NodeItem(k).identifier_string_key = br.ReadUInt32
                cBSMO.NodeItem(k).identifier_name = find_str_BWST(cBSMO.NodeItem(k).identifier_string_key)
            Next

            Dim z = 0
        Catch ex As Exception
            Debug.Print(ex.ToString)
        End Try

        Return True
    End Function

    Public Function get_BSMA(tbl As Integer, br As BinaryReader)
        Try

            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start
            cBSMA = New cBSMA_

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table

            ReDim cBSMA.MaterialItem(tl - 1)
            For k = 0 To tl - 1
                cBSMA.MaterialItem(k).effectIndex = br.ReadInt32
                cBSMA.MaterialItem(k).shaderPropBegin = br.ReadInt32
                cBSMA.MaterialItem(k).shaderPropEnd = br.ReadInt32
                cBSMA.MaterialItem(k).BWST_str_key = br.ReadUInt32
                cBSMA.MaterialItem(k).identifier = find_str_BWST(cBSMA.MaterialItem(k).BWST_str_key)
            Next
            '----------------------------------------------------------

            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table

            ReDim cBSMA.FXStringKey(tl - 1)
            For k = 0 To tl - 1
                cBSMA.FXStringKey(k).FX_str_key = br.ReadUInt32
                cBSMA.FXStringKey(k).FX_string = find_str_BWST(cBSMA.FXStringKey(k).FX_str_key)
            Next
            '----------------------------------------------------------


            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table

            ReDim cBSMA.ShaderPropertyItem(tl - 1)
            For k = 0 To tl - 1
                cBSMA.ShaderPropertyItem(k).bwst_key = br.ReadUInt32
                cBSMA.ShaderPropertyItem(k).property_type = br.ReadUInt32
                cBSMA.ShaderPropertyItem(k).bwst_key_or_value = br.ReadUInt32
                'ket the property name
                cBSMA.ShaderPropertyItem(k).property_name_string = find_str_BWST(cBSMA.ShaderPropertyItem(k).bwst_key)
                '.bwst_key_or_value either points as a string or is a value depending on .property_type
            Next
            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table

            ReDim cBSMA.ShaderPropertyMatrixItem(tl - 1)
            For k = 0 To tl - 1
                ReDim cBSMA.ShaderPropertyMatrixItem(k).matrix(15)
                For i = 0 To 15
                    cBSMA.ShaderPropertyMatrixItem(k).matrix(i) = br.ReadSingle
                Next
            Next
            '----------------------------------------------------------

            ds = br.ReadUInt32 'data size per entry in bytes
            tl = br.ReadUInt32 ' number of entries in this table

            ReDim cBSMA.ShaderPropertyVectorItem(tl - 1)
            For k = 0 To tl - 1
                cBSMA.ShaderPropertyVectorItem(k).vector4.x = br.ReadSingle
                cBSMA.ShaderPropertyVectorItem(k).vector4.y = br.ReadSingle
                cBSMA.ShaderPropertyVectorItem(k).vector4.z = br.ReadSingle
                cBSMA.ShaderPropertyVectorItem(k).vector4.w = br.ReadSingle
            Next
            For k = 0 To cBSMA.ShaderPropertyItem.Length - 1

                'property types (.property_type)
                '0 = ?
                '1 = boolean, bwst_key_or_value = 1 if true, 0 if false
                '2 = float, read the single other wise read as uint32.
                '3 = integer, bwst_key_or_value = value
                '4 =
                '5 = vec4, bwst_key_or_value = start index in to float table. n*4 will point at first entry
                '6 = texture name, bwst_key = look up key
                'lets fill the property_string
                Select Case True
                    Case cBSMA.ShaderPropertyItem(k).property_type = 1
                        If cBSMA.ShaderPropertyItem(k).bwst_key_or_value = 1 Then
                            cBSMA.ShaderPropertyItem(k).val_boolean = True
                        Else
                            cBSMA.ShaderPropertyItem(k).val_boolean = False
                        End If
                        Exit Select
                    Case cBSMA.ShaderPropertyItem(k).property_type = 2
                        cBSMA.ShaderPropertyItem(k).val_float = cBSMA.ShaderPropertyItem(k).bwst_key_or_value
                        'BSMA.bsma_t2(k).bwst_key_or_value is a float stored as a uint32.
                        'We must convert it from hex value to float
                        Dim hexString = cBSMA.ShaderPropertyItem(k).bwst_key_or_value.ToString("x8")
                        Dim floatVals() = BitConverter.GetBytes(cBSMA.ShaderPropertyItem(k).bwst_key_or_value)
                        cBSMA.ShaderPropertyItem(k).val_float = BitConverter.ToSingle(floatVals, 0)

                        Exit Select
                    Case cBSMA.ShaderPropertyItem(k).property_type = 4
                        Stop
                    Case cBSMA.ShaderPropertyItem(k).property_type = 3
                        cBSMA.ShaderPropertyItem(k).val_int = cBSMA.ShaderPropertyItem(k).bwst_key_or_value
                        Exit Select

                    Case cBSMA.ShaderPropertyItem(k).property_type = 5
                        Dim indx = cBSMA.ShaderPropertyItem(k).bwst_key_or_value
                        cBSMA.ShaderPropertyItem(k).val_vec4 = cBSMA.ShaderPropertyVectorItem(indx).vector4
                        Exit Select

                    Case cBSMA.ShaderPropertyItem(k).property_type = 6
                        cBSMA.ShaderPropertyItem(k).property_value_string = find_str_BWST(cBSMA.ShaderPropertyItem(k).bwst_key_or_value)
                        Exit Select

                End Select
            Next
            For k = 0 To cBSMA.MaterialItem.Length - 1
                If cBSMA.MaterialItem(k).effectIndex > -1 Then ' some have no shaders?
                    cBSMA.MaterialItem(k).FX_string = cBSMA.FXStringKey(cBSMA.MaterialItem(k).effectIndex).FX_string
                Else
                    cBSMA.MaterialItem(k).FX_string = "NONE!"
                End If
            Next

            '----------------------------------------------------------
        Catch ex As Exception
            Debug.Print(ex.ToString)
            Return False
        End Try
        Return True
    End Function

    Public Function get_BWAL(tbl As Integer, br As BinaryReader)
        Dim ns() = {"ASSET_TYPE_UNKNOWN_TYPE", "ASSET_TYPE_DATASECTION", "ASSET_TYPE_TEXTURE", "ASSET_TYPE_EFFECT", "ASSET_TYPE_PRIMITIVE", "ASSET_TYPE_VISUAL", "ASSET_TYPE_MODEL"}
        Dim models, textures, primitives, visuals, effects, unknown, datasections As UInt32
        Try
            cBWAL = New cBWAL_
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBWAL.assetList(tl - 1)
            For k = 0 To tl - 1
                cBWAL.assetList(k).AssetType = br.ReadUInt32
                cBWAL.assetList(k).EntryID = br.ReadUInt32
                cBWAL.assetList(k).string_name = ns(cBWAL.assetList(k).AssetType)
                Select Case True
                    Case cBWAL.assetList(k).AssetType = 0
                        unknown += 1
                    Case cBWAL.assetList(k).AssetType = 1
                        datasections += 1
                    Case cBWAL.assetList(k).AssetType = 2
                        textures += 1
                    Case cBWAL.assetList(k).AssetType = 3
                        effects += 1
                    Case cBWAL.assetList(k).AssetType = 4
                        primitives += 1
                    Case cBWAL.assetList(k).AssetType = 5
                        visuals += 1
                    Case cBWAL.assetList(k).AssetType = 6
                        models += 1
                End Select
            Next

        Catch ex As Exception
            Debug.Print(ex.ToString)
            Return False
        End Try
        Return True
    End Function

    Public Function get_WGSD(tbl As Integer, br As BinaryReader)
        'get the decal lists
        Try
            cWGSD = New cWGSD_
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            Dim ty = br.ReadUInt32() 'type?
            Dim vr = br.ReadUInt32() 'version?

            If ty = 2 And vr = 3 Then
                GoTo read3_only
            End If
            If ty = 1 And vr = 3 Then
                GoTo read3_only
            End If
            If ty = 1 And vr = 2 Then
                Stop
            End If
            '===================================================================================
            'type 2 data storage
            'these are backwards from most chunks
            Dim tl = br.ReadUInt32 ' number of entries in this table
            Dim ds = br.ReadUInt32 'data size per entry in bytes
            ReDim cWGSD.decalEntries(tl - 1)
            ReDim decal_matrix_list(tl - 1)
            For k = 0 To tl - 1
                decal_matrix_list(k) = New decal_matrix_list_
                ReDim cWGSD.decalEntries(k).matrix(15)
                ReDim decal_matrix_list(k).matrix(15)

                cWGSD.decalEntries(k).v1 = br.ReadUInt32
                cWGSD.decalEntries(k).v2 = br.ReadUInt32

                cWGSD.decalEntries(k).accuracyType = br.ReadByte
                For i = 0 To 15
                    cWGSD.decalEntries(k).matrix(i) = br.ReadSingle
                    decal_matrix_list(k).matrix(i) = cWGSD.decalEntries(k).matrix(i)
                Next
                'get the texture names
                cWGSD.decalEntries(k).diffuseMapKey = br.ReadUInt32
                cWGSD.decalEntries(k).normalMapKey = br.ReadUInt32
                cWGSD.decalEntries(k).gmmMapkey = br.ReadUInt32
                cWGSD.decalEntries(k).extrakey = br.ReadUInt32

                Dim priority = br.ReadUInt32
                cWGSD.decalEntries(k).flags = br.ReadUInt16
                decal_matrix_list(k).flags = cWGSD.decalEntries(k).flags

                cWGSD.decalEntries(k).off_x = br.ReadSingle
                cWGSD.decalEntries(k).off_y = br.ReadSingle
                cWGSD.decalEntries(k).off_z = br.ReadSingle
                cWGSD.decalEntries(k).off_w = br.ReadSingle

                decal_matrix_list(k).offset.z = cWGSD.decalEntries(k).off_x
                decal_matrix_list(k).offset.y = cWGSD.decalEntries(k).off_y
                decal_matrix_list(k).offset.z = cWGSD.decalEntries(k).off_z
                decal_matrix_list(k).offset.w = cWGSD.decalEntries(k).off_w

                cWGSD.decalEntries(k).uv_wrapping_u = br.ReadSingle
                cWGSD.decalEntries(k).uv_wrapping_v = br.ReadSingle

                decal_matrix_list(k).u_wrap = cWGSD.decalEntries(k).uv_wrapping_u
                decal_matrix_list(k).v_wrap = cWGSD.decalEntries(k).uv_wrapping_v

                cWGSD.decalEntries(k).visibilityMask = br.ReadUInt32 'always 0xFFFFFFFF?
                Dim un = br.ReadUInt32
                If cWGSD.decalEntries(k).visibilityMask <> 4294967295 Then
                    'Stop
                    'Debug.WriteLine(k.ToString + ":" + WGSD.Table_Entries(k).visibilityMask.ToString("00000000000"))
                End If
                If cWGSD.decalEntries(k).visibilityMask <> 4294967295 Then
                    GoTo ignore_this
                End If
                'now we can get the strings from the keys.
                cWGSD.decalEntries(k).diffuseMap = find_str_BWST(cWGSD.decalEntries(k).diffuseMapKey)
                cWGSD.decalEntries(k).normalMap = find_str_BWST(cWGSD.decalEntries(k).normalMapKey)
                cWGSD.decalEntries(k).gmmMap = find_str_BWST(cWGSD.decalEntries(k).gmmMapkey)
                cWGSD.decalEntries(k).extraMap = find_str_BWST(cWGSD.decalEntries(k).extrakey)
                'this is a temp hack
                If cWGSD.decalEntries(k).extraMap <> "" Then
                    cWGSD.decalEntries(k).diffuseMap = cWGSD.decalEntries(k).extraMap
                    cWGSD.decalEntries(k).normalMap = cWGSD.decalEntries(k).extraMap
                    GoTo ignore_this
                    If True Then
                        'some sorta special enviroment map
                        Debug.WriteLine(cWGSD.decalEntries(k).diffuseMap)
                    End If
                End If
                decal_matrix_list(k).decal_texture = cWGSD.decalEntries(k).diffuseMap
                '' the normal map for Stone_06 does not exist in the pkg files!!
                If decal_matrix_list(k).decal_texture.Contains("Stone06.") Then
                    cWGSD.decalEntries(k).normalMap = "Stone06_NM.dds"
                End If
                decal_matrix_list(k).decal_normal = cWGSD.decalEntries(k).normalMap
                decal_matrix_list(k).decal_gmm = cWGSD.decalEntries(k).gmmMap
                decal_matrix_list(k).decal_extra = cWGSD.decalEntries(k).extraMap
ignore_this:
                decal_matrix_list(k).influence = cWGSD.decalEntries(k).flags 'CInt((WGSD.Table_Entries(k).flags And &HFF00) / 256)
                If decal_matrix_list(k).influence = 6 Then
                    decal_matrix_list(k).influence = 2
                End If

                decal_matrix_list(k).priority = priority '(WGSD.Table_Entries(k).flags And &HFF)
                Dim d_type As Integer = (cWGSD.decalEntries(k).flags And &HF0000) / 65536


            Next
            Dim type = br.ReadUInt32
            '===================================================================================
            'type 3 data storage. These are parallax shaded decals
read3_only:
            Dim cnt2 = br.ReadUInt32
            Dim dl = br.ReadUInt32
            ReDim Preserve decal_matrix_list(tl + cnt2 + -1)
            ReDim Preserve cWGSD.decalEntries(tl + cnt2 + -1)
            '2nd group
            For k = tl To (tl + cnt2) - 1
                decal_matrix_list(k) = New decal_matrix_list_
                ReDim cWGSD.decalEntries(k).matrix(15)
                ReDim decal_matrix_list(k).matrix(15)

                cWGSD.decalEntries(k).v1 = br.ReadUInt32
                cWGSD.decalEntries(k).v2 = br.ReadUInt32

                cWGSD.decalEntries(k).accuracyType = br.ReadByte
                For i = 0 To 15
                    cWGSD.decalEntries(k).matrix(i) = br.ReadSingle
                    decal_matrix_list(k).matrix(i) = cWGSD.decalEntries(k).matrix(i)
                Next
                'get the texture names
                cWGSD.decalEntries(k).diffuseMapKey = br.ReadUInt32
                cWGSD.decalEntries(k).normalMapKey = br.ReadUInt32
                cWGSD.decalEntries(k).gmmMapkey = br.ReadUInt32
                cWGSD.decalEntries(k).extrakey = br.ReadUInt32

                Dim priority = br.ReadInt32

                cWGSD.decalEntries(k).flags = br.ReadUInt16
                decal_matrix_list(k).flags = cWGSD.decalEntries(k).flags

                cWGSD.decalEntries(k).off_x = br.ReadSingle
                cWGSD.decalEntries(k).off_y = br.ReadSingle
                cWGSD.decalEntries(k).off_z = br.ReadSingle
                cWGSD.decalEntries(k).off_w = br.ReadSingle

                decal_matrix_list(k).offset.z = cWGSD.decalEntries(k).off_x
                decal_matrix_list(k).offset.y = cWGSD.decalEntries(k).off_y
                decal_matrix_list(k).offset.z = cWGSD.decalEntries(k).off_z
                decal_matrix_list(k).offset.w = cWGSD.decalEntries(k).off_w

                cWGSD.decalEntries(k).uv_wrapping_u = br.ReadSingle
                cWGSD.decalEntries(k).uv_wrapping_v = br.ReadSingle

                decal_matrix_list(k).u_wrap = cWGSD.decalEntries(k).uv_wrapping_u
                decal_matrix_list(k).v_wrap = cWGSD.decalEntries(k).uv_wrapping_v

                cWGSD.decalEntries(k).visibilityMask = br.ReadUInt32 'always 0xFFFFFFFF?

                If cWGSD.decalEntries(k).visibilityMask <> 4294967295 Then
                    'Stop
                    'Debug.WriteLine(k.ToString + ":" + WGSD.Table_Entries(k).visibilityMask.ToString("00000000000"))
                End If
                If cWGSD.decalEntries(k).visibilityMask <> 4294967295 Then
                    GoTo ignore_this2
                End If
                'these 3 are only in type 3 decals!
                cWGSD.decalEntries(k).tiles_fade = br.ReadSingle
                cWGSD.decalEntries(k).parallax_offset = br.ReadSingle
                cWGSD.decalEntries(k).parallax_amplitude = br.ReadSingle
                decal_matrix_list(k).is_parallax = True

                'now we can get the strings from the keys.
                cWGSD.decalEntries(k).diffuseMap = find_str_BWST(cWGSD.decalEntries(k).diffuseMapKey)
                cWGSD.decalEntries(k).normalMap = find_str_BWST(cWGSD.decalEntries(k).normalMapKey)
                cWGSD.decalEntries(k).gmmMap = find_str_BWST(cWGSD.decalEntries(k).gmmMapkey)
                cWGSD.decalEntries(k).extraMap = find_str_BWST(cWGSD.decalEntries(k).extrakey)
                'this is a temp hack
                If cWGSD.decalEntries(k).extraMap <> "" Then
                    GoTo ignore_this2
                    cWGSD.decalEntries(k).diffuseMap = cWGSD.decalEntries(k).extraMap
                    cWGSD.decalEntries(k).normalMap = cWGSD.decalEntries(k).extraMap
                    decal_matrix_list(k).is_wet = True
                    If True Then
                        'some sorta special enviroment map
                        Debug.WriteLine(cWGSD.decalEntries(k).diffuseMap)
                    End If
                End If
                decal_matrix_list(k).decal_texture = cWGSD.decalEntries(k).diffuseMap
                '' the normal map for Stone_06 does not exist in the pkg files!!
                If decal_matrix_list(k).decal_texture.Contains("Stone06.") Then
                    cWGSD.decalEntries(k).normalMap = "Stone06_NM.dds"
                End If
                decal_matrix_list(k).decal_normal = cWGSD.decalEntries(k).normalMap
                decal_matrix_list(k).decal_gmm = cWGSD.decalEntries(k).gmmMap
                decal_matrix_list(k).decal_extra = cWGSD.decalEntries(k).extraMap
ignore_this2:
                decal_matrix_list(k).influence = cWGSD.decalEntries(k).flags 'CInt((WGSD.Table_Entries(k).flags And &HFF00) / 256)
                If decal_matrix_list(k).influence = 6 Then
                    decal_matrix_list(k).influence = 2
                End If

                decal_matrix_list(k).priority = priority '(WGSD.Table_Entries(k).flags And &HFF)
                Dim d_type As Integer = (cWGSD.decalEntries(k).flags And &HF0000) / 65536


            Next

        Catch ex As Exception
            Debug.Print(ex.ToString)
            Return False
        End Try
        Return True
    End Function

    Public Function get_SPTR(tbl As Integer, br As BinaryReader)
        Try
            cSPTR = New cSPTR_
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table

            ReDim speedtree_matrix_list(tl - 1)
            ReDim cSPTR.Stree(tl - 1)

            For k = 0 To tl - 1
                speedtree_matrix_list(k) = New speedtree_matrix_list_
                ReDim speedtree_matrix_list(k).matrix(16)
                ReDim cSPTR.Stree(k).Matrix(16)
                For i = 0 To 15
                    speedtree_matrix_list(k).matrix(i) = br.ReadSingle
                    cSPTR.Stree(k).Matrix(i) = speedtree_matrix_list(k).matrix(i)
                Next

                cSPTR.Stree(k).key = br.ReadUInt32
                cSPTR.Stree(k).e1 = br.ReadUInt32
                cSPTR.Stree(k).e2 = br.ReadUInt32
                cSPTR.Stree(k).e3 = br.ReadUInt32

                speedtree_matrix_list(k).tree_name = find_str_BWST(cSPTR.Stree(k).key)

            Next
        Catch ex As Exception
            Return False
        End Try
        Return True
    End Function

    Public Function get_BWWa(tbl As Integer, br As BinaryReader)
        Try
            cBWWa = New cBWWa_
            'set stream reader to point at this chunk
            br.BaseStream.Position = space_bin_Chunk_table(tbl).chunk_Start

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table
            ReDim cBWWa.bwwa_t1(1)
            cBWWa.bwwa_t1(0) = New cBWWa_.cbwwa_t1_
            If tl = 0 Then
                'no water
                water.IsWater = False
                Return True
            End If
            Try
                Dim l, r As vect3
                l.x = br.ReadSingle
                l.y = br.ReadSingle
                l.z = br.ReadSingle

                r.x = br.ReadSingle
                r.y = br.ReadSingle
                r.z = br.ReadSingle

                cBWWa.bwwa_t1(0).position.x = -(l.x + r.x) / 2.0!
                cBWWa.bwwa_t1(0).position.y = l.y
                cBWWa.bwwa_t1(0).position.z = (l.z + r.z) / 2.0!
                cBWWa.bwwa_t1(0).width = Abs(l.x) + Abs(r.x)
                cBWWa.bwwa_t1(0).plane = l.y
                cBWWa.bwwa_t1(0).height = Abs(l.z) + Abs(r.z)
                water.IsWater = True
                WATER_LINE = cBWWa.bwwa_t1(0).position.y
            Catch ex As Exception
                water.IsWater = False
                WATER_LINE = -500.0
            End Try

        Catch ex As Exception
            Return False
        End Try
        Return True
    End Function

#Region "string look up routines"
    Private Function find_str_FX(ByVal key As UInt32) As String
        For z = 0 To cBSMA.FXStringKey.Length - 1
            If key = cBSMA.FXStringKey(z).FX_str_key Then
                Return cBSMA.FXStringKey(z).FX_string(z)
            End If
        Next
        Return "ERROR!"
    End Function
    Private Function find_str_BWST(ByVal key As UInt32) As String
        For z = 0 To cBWST.strs.Length - 1
            If key = cBWST.keys(z) Then
                'Console.WriteLine("key: " + key.ToString("x8").ToUpper + " " + BWST.entries(z).str + vbCrLf)
                Return cBWST.strs(z)
            End If
        Next
        Return "ERROR!"
    End Function
    Private Function find_str_BWSG(ByVal key As UInt32) As String
        For z = 0 To cBWSG.strs.Length - 1
            If key = cBWSG.keys(z) Then
                'Console.WriteLine("key: " + key.ToString("x8").ToUpper + vbCrLf)
                Return cBWSG.strs(z)
            End If
        Next
        Return "ERROR!"
    End Function
    Private Function find_str_SpTr(ByVal key As UInt32) As String
        For z = 0 To cSPTR.Stree.Length - 1
            If key = cSPTR.Stree(z).key Then
                'Console.WriteLine("key: " + key.ToString("x8").ToUpper + vbCrLf)
                Return cSPTR.Stree(z).Tree_name
            End If
        Next
        Return "ERROR!"
    End Function

#End Region

End Module

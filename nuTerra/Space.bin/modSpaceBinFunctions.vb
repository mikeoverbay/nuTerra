﻿Imports System.IO
Imports System.Text

Module modSpaceBinFunctions
    Public Sub get_BWSG(ByRef bwsgHeader As SectionHeader, br As BinaryReader)
        ' Dont load for now
        Return

        Dim debug_strings As New StringBuilder

        'set stream reader to point at this chunk
        br.BaseStream.Position = bwsgHeader.offset

        cBWSG = New cBWSG_
        '----------------------------------------------------------------------
        'get strings section
        Dim d_length = br.ReadUInt32
        Dim entry_cnt = br.ReadUInt32
        ReDim cBWSG.strs(entry_cnt)
        ReDim cBWSG.keys(entry_cnt)
        Dim old_pos = br.BaseStream.Position
        Dim start_offset As Long = bwsgHeader.offset + (d_length * entry_cnt) + 12
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
            debug_strings.AppendLine(hexStringReversed.Replace("-", "") +
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
            cBWSG.primitive_entries(k).model = cBWSG.find_str(cBWSG.primitive_entries(k).str_key1)
            cBWSG.primitive_entries(k).vertex_type = cBWSG.find_str(cBWSG.primitive_entries(k).str_key2)
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

        cBSGD_br.Close()
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
    End Sub


    Public Sub get_BSMA(ByRef bsmaHeader As SectionHeader, br As BinaryReader)
        ' set stream reader to point at this chunk
        br.BaseStream.Position = bsmaHeader.offset
        cBSMA = New cBSMA_

        Dim ds = br.ReadUInt32 'data size per entry in bytes
        Dim tl = br.ReadUInt32 ' number of entries in this table

        ReDim cBSMA.MaterialItem(tl - 1)
        For k = 0 To tl - 1
            cBSMA.MaterialItem(k).effectIndex = br.ReadUInt32
            cBSMA.MaterialItem(k).shaderPropBegin = br.ReadUInt32
            cBSMA.MaterialItem(k).shaderPropEnd = br.ReadUInt32
            cBSMA.MaterialItem(k).BWST_str_key = br.ReadUInt32
        Next
        '----------------------------------------------------------

        ds = br.ReadUInt32 'data size per entry in bytes
        tl = br.ReadUInt32 ' number of entries in this table

        ReDim cBSMA.FXStringKey(tl - 1)
        For k = 0 To tl - 1
            cBSMA.FXStringKey(k).FX_str_key = br.ReadUInt32
            cBSMA.FXStringKey(k).FX_string = cBWST.find_str(cBSMA.FXStringKey(k).FX_str_key)
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
            cBSMA.ShaderPropertyItem(k).property_name_string = cBWST.find_str(cBSMA.ShaderPropertyItem(k).bwst_key)
            '.bwst_key_or_value either points as a string or is a value depending on .property_type
        Next
        ds = br.ReadUInt32 'data size per entry in bytes
        tl = br.ReadUInt32 ' number of entries in this table

        ReDim cBSMA.ShaderPropertyMatrixItem(tl - 1)
        For k = 0 To tl - 1
            For i = 0 To 3
                For j = 0 To 3
                    cBSMA.ShaderPropertyMatrixItem(k)(i, j) = br.ReadSingle
                Next
            Next
        Next
        '----------------------------------------------------------

        ds = br.ReadUInt32 'data size per entry in bytes
        tl = br.ReadUInt32 ' number of entries in this table

        ReDim cBSMA.ShaderPropertyVectorItem(tl - 1)
        For k = 0 To tl - 1
            cBSMA.ShaderPropertyVectorItem(k).X = br.ReadSingle
            cBSMA.ShaderPropertyVectorItem(k).Y = br.ReadSingle
            cBSMA.ShaderPropertyVectorItem(k).Z = br.ReadSingle
            cBSMA.ShaderPropertyVectorItem(k).W = br.ReadSingle
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
                    cBSMA.ShaderPropertyItem(k).val_vec4 = cBSMA.ShaderPropertyVectorItem(indx)
                    Exit Select

                Case cBSMA.ShaderPropertyItem(k).property_type = 6
                    cBSMA.ShaderPropertyItem(k).property_value_string = cBWST.find_str(cBSMA.ShaderPropertyItem(k).bwst_key_or_value)
                    Exit Select

            End Select
        Next
    End Sub


    Public Sub get_WGSD(ByRef wgsdHeader As SectionHeader, br As BinaryReader)
        'get the decal lists
        cWGSD = New cWGSD_
        'set stream reader to point at this chunk
        br.BaseStream.Position = wgsdHeader.offset

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
        ReDim DECAL_INDEX_LIST(tl - 1)
        For k = 0 To tl - 1
            DECAL_INDEX_LIST(k) = New DECAL_INDEX_LIST_
            ReDim cWGSD.decalEntries(k).matrix(15)
            ReDim DECAL_INDEX_LIST(k).matrix(15)

            cWGSD.decalEntries(k).v1 = br.ReadUInt32
            cWGSD.decalEntries(k).v2 = br.ReadUInt32

            cWGSD.decalEntries(k).accuracyType = br.ReadByte
            For i = 0 To 15
                cWGSD.decalEntries(k).matrix(i) = br.ReadSingle
                DECAL_INDEX_LIST(k).matrix(i) = cWGSD.decalEntries(k).matrix(i)
            Next
            'get the texture names
            cWGSD.decalEntries(k).diffuseMapKey = br.ReadUInt32
            cWGSD.decalEntries(k).normalMapKey = br.ReadUInt32
            cWGSD.decalEntries(k).gmmMapkey = br.ReadUInt32
            cWGSD.decalEntries(k).extrakey = br.ReadUInt32

            Dim priority = br.ReadUInt32
            cWGSD.decalEntries(k).flags = br.ReadUInt16
            DECAL_INDEX_LIST(k).flags = cWGSD.decalEntries(k).flags

            cWGSD.decalEntries(k).off_x = br.ReadSingle
            cWGSD.decalEntries(k).off_y = br.ReadSingle
            cWGSD.decalEntries(k).off_z = br.ReadSingle
            cWGSD.decalEntries(k).off_w = br.ReadSingle

            DECAL_INDEX_LIST(k).offset.Z = cWGSD.decalEntries(k).off_x
            DECAL_INDEX_LIST(k).offset.Y = cWGSD.decalEntries(k).off_y
            DECAL_INDEX_LIST(k).offset.Z = cWGSD.decalEntries(k).off_z
            DECAL_INDEX_LIST(k).offset.W = cWGSD.decalEntries(k).off_w

            cWGSD.decalEntries(k).uv_wrapping_u = br.ReadSingle
            cWGSD.decalEntries(k).uv_wrapping_v = br.ReadSingle

            DECAL_INDEX_LIST(k).u_wrap = cWGSD.decalEntries(k).uv_wrapping_u
            DECAL_INDEX_LIST(k).v_wrap = cWGSD.decalEntries(k).uv_wrapping_v

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
            cWGSD.decalEntries(k).diffuseMap = cBWST.find_str(cWGSD.decalEntries(k).diffuseMapKey)
            cWGSD.decalEntries(k).normalMap = cBWST.find_str(cWGSD.decalEntries(k).normalMapKey)
            cWGSD.decalEntries(k).gmmMap = cBWST.find_str(cWGSD.decalEntries(k).gmmMapkey)
            cWGSD.decalEntries(k).extraMap = cBWST.find_str(cWGSD.decalEntries(k).extrakey)
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
            DECAL_INDEX_LIST(k).decal_texture = cWGSD.decalEntries(k).diffuseMap
            '' the normal map for Stone_06 does not exist in the pkg files!!
            If DECAL_INDEX_LIST(k).decal_texture.Contains("Stone06.") Then
                cWGSD.decalEntries(k).normalMap = "Stone06_NM.dds"
            End If
            DECAL_INDEX_LIST(k).decal_normal = cWGSD.decalEntries(k).normalMap
            DECAL_INDEX_LIST(k).decal_gmm = cWGSD.decalEntries(k).gmmMap
            DECAL_INDEX_LIST(k).decal_extra = cWGSD.decalEntries(k).extraMap
ignore_this:
            DECAL_INDEX_LIST(k).influence = cWGSD.decalEntries(k).flags 'CInt((WGSD.Table_Entries(k).flags And &HFF00) / 256)
            If DECAL_INDEX_LIST(k).influence = 6 Then
                DECAL_INDEX_LIST(k).influence = 2
            End If

            DECAL_INDEX_LIST(k).priority = priority '(WGSD.Table_Entries(k).flags And &HFF)
            Dim d_type As Integer = (cWGSD.decalEntries(k).flags And &HF0000) / 65536


        Next
        Dim type = br.ReadUInt32
        '===================================================================================
        'type 3 data storage. These are parallax shaded decals
read3_only:
        Dim cnt2 = br.ReadUInt32
        Dim dl = br.ReadUInt32
        ReDim Preserve DECAL_INDEX_LIST(tl + cnt2 + -1)
        ReDim Preserve cWGSD.decalEntries(tl + cnt2 + -1)
        '2nd group
        For k = tl To (tl + cnt2) - 1
            DECAL_INDEX_LIST(k) = New DECAL_INDEX_LIST_
            ReDim cWGSD.decalEntries(k).matrix(15)
            ReDim DECAL_INDEX_LIST(k).matrix(15)

            cWGSD.decalEntries(k).v1 = br.ReadUInt32
            cWGSD.decalEntries(k).v2 = br.ReadUInt32

            cWGSD.decalEntries(k).accuracyType = br.ReadByte
            For i = 0 To 15
                cWGSD.decalEntries(k).matrix(i) = br.ReadSingle
                DECAL_INDEX_LIST(k).matrix(i) = cWGSD.decalEntries(k).matrix(i)
            Next
            'get the texture names
            cWGSD.decalEntries(k).diffuseMapKey = br.ReadUInt32
            cWGSD.decalEntries(k).normalMapKey = br.ReadUInt32
            cWGSD.decalEntries(k).gmmMapkey = br.ReadUInt32
            cWGSD.decalEntries(k).extrakey = br.ReadUInt32

            Dim priority = br.ReadInt32

            cWGSD.decalEntries(k).flags = br.ReadUInt16
            DECAL_INDEX_LIST(k).flags = cWGSD.decalEntries(k).flags

            cWGSD.decalEntries(k).off_x = br.ReadSingle
            cWGSD.decalEntries(k).off_y = br.ReadSingle
            cWGSD.decalEntries(k).off_z = br.ReadSingle
            cWGSD.decalEntries(k).off_w = br.ReadSingle

            DECAL_INDEX_LIST(k).offset.Z = cWGSD.decalEntries(k).off_x
            DECAL_INDEX_LIST(k).offset.Y = cWGSD.decalEntries(k).off_y
            DECAL_INDEX_LIST(k).offset.Z = cWGSD.decalEntries(k).off_z
            DECAL_INDEX_LIST(k).offset.W = cWGSD.decalEntries(k).off_w

            cWGSD.decalEntries(k).uv_wrapping_u = br.ReadSingle
            cWGSD.decalEntries(k).uv_wrapping_v = br.ReadSingle

            DECAL_INDEX_LIST(k).u_wrap = cWGSD.decalEntries(k).uv_wrapping_u
            DECAL_INDEX_LIST(k).v_wrap = cWGSD.decalEntries(k).uv_wrapping_v

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
            DECAL_INDEX_LIST(k).is_parallax = True

            'now we can get the strings from the keys.
            cWGSD.decalEntries(k).diffuseMap = cBWST.find_str(cWGSD.decalEntries(k).diffuseMapKey)
            cWGSD.decalEntries(k).normalMap = cBWST.find_str(cWGSD.decalEntries(k).normalMapKey)
            cWGSD.decalEntries(k).gmmMap = cBWST.find_str(cWGSD.decalEntries(k).gmmMapkey)
            cWGSD.decalEntries(k).extraMap = cBWST.find_str(cWGSD.decalEntries(k).extrakey)
            'this is a temp hack
            If cWGSD.decalEntries(k).extraMap <> "" Then
                GoTo ignore_this2
                cWGSD.decalEntries(k).diffuseMap = cWGSD.decalEntries(k).extraMap
                cWGSD.decalEntries(k).normalMap = cWGSD.decalEntries(k).extraMap
                DECAL_INDEX_LIST(k).is_wet = True
                If True Then
                    'some sorta special enviroment map
                    Debug.WriteLine(cWGSD.decalEntries(k).diffuseMap)
                End If
            End If
            DECAL_INDEX_LIST(k).decal_texture = cWGSD.decalEntries(k).diffuseMap
            '' the normal map for Stone_06 does not exist in the pkg files!!
            If DECAL_INDEX_LIST(k).decal_texture.Contains("Stone06.") Then
                cWGSD.decalEntries(k).normalMap = "Stone06_NM.dds"
            End If
            DECAL_INDEX_LIST(k).decal_normal = cWGSD.decalEntries(k).normalMap
            DECAL_INDEX_LIST(k).decal_gmm = cWGSD.decalEntries(k).gmmMap
            DECAL_INDEX_LIST(k).decal_extra = cWGSD.decalEntries(k).extraMap
ignore_this2:
            DECAL_INDEX_LIST(k).influence = cWGSD.decalEntries(k).flags 'CInt((WGSD.Table_Entries(k).flags And &HFF00) / 256)
            If DECAL_INDEX_LIST(k).influence = 6 Then
                DECAL_INDEX_LIST(k).influence = 2
            End If

            DECAL_INDEX_LIST(k).priority = priority '(WGSD.Table_Entries(k).flags And &HFF)
            Dim d_type As Integer = (cWGSD.decalEntries(k).flags And &HF0000) / 65536


        Next
    End Sub

End Module
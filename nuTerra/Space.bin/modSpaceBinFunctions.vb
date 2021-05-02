Imports System.IO
Imports System.Text

Module modSpaceBinFunctions
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
            cBSMA.MaterialItem(k).identifier_fnv = br.ReadUInt32
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

        If ty = 2 AndAlso vr = 3 Then
            GoTo read3_only
        End If
        If ty = 1 AndAlso vr = 3 Then
            GoTo read3_only
        End If
        If ty = 1 AndAlso vr = 2 Then
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
            cWGSD.decalEntries(k).v1 = br.ReadUInt32
            cWGSD.decalEntries(k).v2 = br.ReadUInt32

            cWGSD.decalEntries(k).accurate = br.ReadByte

            For i = 0 To 3
                For j = 0 To 3
                    cWGSD.decalEntries(k).transform(i, j) = br.ReadSingle
                Next
            Next

            ' get the texture names
            cWGSD.decalEntries(k).diff_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).bump_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).hm_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).add_tex_fnv = br.ReadUInt32

            cWGSD.decalEntries(k).priority = br.ReadUInt32
            cWGSD.decalEntries(k).influenceType = br.ReadByte
            cWGSD.decalEntries(k).materialType = br.ReadByte

            cWGSD.decalEntries(k).offsets.X = br.ReadSingle
            cWGSD.decalEntries(k).offsets.Y = br.ReadSingle
            cWGSD.decalEntries(k).offsets.Z = br.ReadSingle
            cWGSD.decalEntries(k).offsets.W = br.ReadSingle

            cWGSD.decalEntries(k).uv_wrapping.X = br.ReadSingle
            cWGSD.decalEntries(k).uv_wrapping.Y = br.ReadSingle

            cWGSD.decalEntries(k).visibility_mask = br.ReadUInt32
            cWGSD.decalEntries(k).tiles_fade = br.ReadSingle


            DECAL_INDEX_LIST(k).matrix = cWGSD.decalEntries(k).transform
            DECAL_INDEX_LIST(k).offsets = cWGSD.decalEntries(k).offsets
            DECAL_INDEX_LIST(k).uv_wrapping = cWGSD.decalEntries(k).uv_wrapping

            ' now we can get the strings from the keys.
            DECAL_INDEX_LIST(k).decal_texture = cBWST.find_str(cWGSD.decalEntries(k).diff_tex_fnv)

            ' the normal map for Stone_06 does not exist in the pkg files!!
            If DECAL_INDEX_LIST(k).decal_texture.Contains("Stone06.") Then
                DECAL_INDEX_LIST(k).decal_normal = "Stone06_NM.dds"
            Else
                DECAL_INDEX_LIST(k).decal_normal = cBWST.find_str(cWGSD.decalEntries(k).bump_tex_fnv)
            End If

            DECAL_INDEX_LIST(k).decal_gmm = cBWST.find_str(cWGSD.decalEntries(k).hm_tex_fnv)
            DECAL_INDEX_LIST(k).decal_extra = cBWST.find_str(cWGSD.decalEntries(k).add_tex_fnv)

            DECAL_INDEX_LIST(k).influence = cWGSD.decalEntries(k).influenceType
            If DECAL_INDEX_LIST(k).influence = 6 Then
                DECAL_INDEX_LIST(k).influence = 2
            End If

            DECAL_INDEX_LIST(k).priority = cWGSD.decalEntries(k).priority
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
            cWGSD.decalEntries(k).v1 = br.ReadUInt32
            cWGSD.decalEntries(k).v2 = br.ReadUInt32

            cWGSD.decalEntries(k).accurate = br.ReadByte

            For i = 0 To 3
                For j = 0 To 3
                    cWGSD.decalEntries(k).transform(i, j) = br.ReadSingle
                Next
            Next

            ' get the texture names
            cWGSD.decalEntries(k).diff_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).bump_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).hm_tex_fnv = br.ReadUInt32
            cWGSD.decalEntries(k).add_tex_fnv = br.ReadUInt32

            cWGSD.decalEntries(k).priority = br.ReadUInt32
            cWGSD.decalEntries(k).influenceType = br.ReadByte
            cWGSD.decalEntries(k).materialType = br.ReadByte

            cWGSD.decalEntries(k).offsets.X = br.ReadSingle
            cWGSD.decalEntries(k).offsets.Y = br.ReadSingle
            cWGSD.decalEntries(k).offsets.Z = br.ReadSingle
            cWGSD.decalEntries(k).offsets.W = br.ReadSingle

            cWGSD.decalEntries(k).uv_wrapping.X = br.ReadSingle
            cWGSD.decalEntries(k).uv_wrapping.Y = br.ReadSingle

            cWGSD.decalEntries(k).visibility_mask = br.ReadUInt32
            cWGSD.decalEntries(k).tiles_fade = br.ReadSingle

            'these 2 are only in type 3 decals!
            cWGSD.decalEntries(k).parallax_offset = br.ReadSingle
            cWGSD.decalEntries(k).parallax_amplitude = br.ReadSingle

            DECAL_INDEX_LIST(k).is_parallax = True

            DECAL_INDEX_LIST(k).matrix = cWGSD.decalEntries(k).transform

            ' now we can get the strings from the keys.
            DECAL_INDEX_LIST(k).decal_texture = cBWST.find_str(cWGSD.decalEntries(k).diff_tex_fnv)

            ' HACK: the normal map for Stone_06 does not exist in the pkg files!!
            If DECAL_INDEX_LIST(k).decal_texture.Contains("Stone06.") Then
                DECAL_INDEX_LIST(k).decal_normal = "Stone06_NM.dds"
            Else
                DECAL_INDEX_LIST(k).decal_normal = cBWST.find_str(cWGSD.decalEntries(k).bump_tex_fnv)
            End If

            DECAL_INDEX_LIST(k).decal_gmm = cBWST.find_str(cWGSD.decalEntries(k).hm_tex_fnv)
            DECAL_INDEX_LIST(k).decal_extra = cBWST.find_str(cWGSD.decalEntries(k).add_tex_fnv)

            DECAL_INDEX_LIST(k).influence = cWGSD.decalEntries(k).influenceType
            If DECAL_INDEX_LIST(k).influence = 6 Then
                DECAL_INDEX_LIST(k).influence = 2
            End If

            DECAL_INDEX_LIST(k).priority = cWGSD.decalEntries(k).priority
        Next
    End Sub

End Module

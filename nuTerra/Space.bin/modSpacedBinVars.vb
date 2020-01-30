Imports System.Text

Module modSpacedBinVars
#Region "Structures"

    Public Model_Matrix_list() As model_matrix_list_
    Public Structure model_matrix_list_
        Public primitive_name As String
        Public matrix() As Single
        Public mask As Boolean
        Public BB_Min As vect3
        Public BB_Max As vect3
        Public BB() As vect3
        Public exclude As Boolean
        Public destructible As Boolean
        Public exclude_list() As Integer
    End Structure

    Public speedtree_matrix_list() As speedtree_matrix_list_
    Public Structure speedtree_matrix_list_
        Public tree_name As String
        Public matrix() As Single
    End Structure
    Public decal_matrix_list() As decal_matrix_list_
    Public Structure decal_matrix_list_
        Public u_wrap As Single
        Public v_wrap As Single
        Public decal_data() As vertex_data
        Public texture_id As Integer
        Public normal_id As Integer
        Public gmm_id As Integer
        Public display_id As Integer
        Public decal_texture As String
        Public decal_normal As String
        Public decal_gmm As String
        Public decal_extra As String
        Public matrix() As Single
        Public good As Boolean
        Public offset As vect4
        Public priority As Integer
        Public influence As Integer
        Public texture_matrix() As Single
        Public lbl As vect3
        Public lbr As vect3
        Public ltl As vect3
        Public ltr As vect3
        Public rbl As vect3
        Public rbr As vect3
        Public rtl As vect3
        Public rtr As vect3
        Public BB() As vect3
        Public visible As Boolean
        Public flags As UInteger
        Public cull_method As Integer
        Public is_parallax As Boolean
        Public is_wet As Boolean


    End Structure
#End Region

#Region "BSGD"
    Public cBSGD As cBSGD_
    Public Structure cBSGD_
        'This data contains chunks of vertex creation data
        'Chunk size is stored in cBWSG tbl 3
        Public data() As Byte
    End Structure
#End Region

#Region "BWST"
    Public cBWST As cBWST_
    Public Structure cBWST_
        'Storage for all the strings
        Public keys() As UInt32
        Public strs() As String
    End Structure
#End Region

#Region "BWSG"
    Public cBWSG As cBWSG_
    Public Structure cBWSG_
        Public location As UInt32
        Public length As UInt32
        Public keys() As UInt32
        Public strs() As String

        Public primitive_entries() As primitive_entries_
        Public primitive_data_list() As primitive_data_list_
        Public cBWSG_VertexDataChunks() As raw_data_
        Public primitive_data() As primitive_data_

        Public Structure primitive_entries_
            Public str_key1 As UInt32
            Public start_idx As UInt32
            Public end_idx As UInt32
            Public vertex_count As UInt32
            Public str_key2 As UInt32
            Public vertex_type As String
            Public model As String
        End Structure

        Public Structure primitive_data_list_
            Public block_type As UInt32
            Public vertex_stride As UInt32
            Public chuckDataBlockLength As UInt32
            Public chuckDataBlockIndex As UInt32
            Public chuckDataOffset As UInt32
            Public data() As Byte
        End Structure

        Public Structure primitive_data_
            Public block_type As UInt32
            Public vertex_stride As UInt32
            Public data_length() As UInt32
            Public section_index As UInt32
            Public offset As UInt32
            Public data() As Byte
        End Structure

        Public Structure raw_data_
            Public data_size As UInt32
            Public data() As Byte
        End Structure


    End Structure
#End Region

#Region "BSMI"
    Public cBSMI As cBSMI_

    Public Structure cBSMI_
        Public matrix_list() As matrix_
        Public tbl_2() As tbl_2_
        Public vis_mask() As vis_mask_
        Public model_BSMO_indexes() As model_index_
        Public animation_tbl() As animation_tbl_
        Public animation_info() As animation_info_
        Public tbl_7() As tbl_7_
        Public skined_tbl() As skined_tbl_
        Public tbl_9() As tbl_9_
        Public tbl_10() As tbl_10_
        't2
        Public Structure tbl_2_
            Public index1 As Int32 '?
            Public index2 As Int32 '?
        End Structure

        't3
        Public Structure vis_mask_
            Public mask As Int32
        End Structure

        't4
        Public Structure model_index_
            Public BSMO_index As UInt32
            Public BSMO_extras As UInt32
            'If BSMO_extras = 1 It's an important model.
            'If 0 its an extra detailing model thats not really needed.
        End Structure

        't5
        Public Structure animation_tbl_
            Public is_animation As Int32
            'If this is <> &hFFFFFFFF, its a skined animation model
        End Structure

        't6
        Public Structure animation_info_
            Public model_index As UInt32
            Public seq_resource_key As UInt32
            Public clip_name_key As UInt32
            Public auto_start As UInt32
            Public loop_cnt As UInt32
            Public speed As Single
            Public delay As Single
            Public unknown As Single
        End Structure

        't7
        Public Structure tbl_7_
            Public unknown1 As UInt32
            Public unknown2 As UInt32
        End Structure

        't8
        Public Structure skined_tbl_
            Public index1 As UInt32
            Public index2 As UInt32
            Public index3 As UInt32
        End Structure

        't9
        Public Structure tbl_9_
            Public index1 As UInt32
        End Structure

        '10
        Public Structure tbl_10_
            'five singles. No idea what they are for.
            Public s1 As Single
            Public s2 As Single
            Public s3 As Single
            Public s4 As Single
            Public s5 As Single
        End Structure
    End Structure

#End Region

#Region "Wtbl"
    Public cWtbl As cWtbl_
    Public Structure cWtbl_
        Public tbl_1() As tbl_1_
        Public tbl_2() As tbl_2_

        Public Structure tbl_1_
            Public s1 As Single
            Public s2 As Single
            Public s3 As Single
        End Structure
        Public Structure tbl_2_
            Public flag1 As UInt32
            Public flag2 As UInt32
            Public flag3 As UInt32
            Public flag4 As UInt32
            Public flag5 As UInt32
        End Structure
    End Structure
#End Region

#Region "BSMO"
    Public cBSMO As cBSMO_
    Public Structure cBSMO_
        Public tbl_1() As tbl_1_
        Public tbl_2() As tbl_2_
        Public model_entries() As model_entries_ 'tbl_3
        Public material_kind() As material_kind_ 'tbl_4
        Public model_visibility_bb() As model_visibility_bb_ 'tbl_5
        Public tbl_6() As tbl_6_
        Public tbl_7() As tbl_7_
        Public lod_range() As lod_range_ 'tbl_8
        Public lodRenderItem() As lodRenderItem_ 'tbl 9
        Public renderItem() As renderItem_ 'tbl 10
        Public tbl_11() As tbl_11_
        Public NodeItem() As NodeItem_ 'tbl_12


        Public Structure tbl_1_ '???
            Public lod_start As UInt32
            Public lod_end As UInt32
        End Structure

        Public Structure tbl_2_ '???
            Public unknown As UInt32
        End Structure

        Public Structure tbl_3_ '???
            Public index1 As UInt32
            Public index2 As UInt32
        End Structure

        Public Structure model_entries_
            Public min_BB As vect3
            Public max_BB As vect3
            Public Model_String_key As UInt32
            Public model_material_kind_begin As UInt32
            Public model_material_kind_end As UInt32
            Public model_name As String
        End Structure

        Public Structure material_kind_
            Public mat_index As UInt32
            Public flags As UInt32
        End Structure

        Public Structure model_visibility_bb_
            Public min_BB As vect3
            Public max_BB As vect3
        End Structure

        Public Structure tbl_6_ '??? not even sure of data size
            Public index1 As UInt32
            Public index2 As UInt32
        End Structure

        Public Structure tbl_7_ '???
            Public index1 As UInt32
        End Structure

        Public Structure lod_range_
            'These are the square of the actual range
            '(x*x) + (y*y) + (z*z) - Camera to item distance
            Public lod_range As Single
        End Structure

        Public Structure lodRenderItem_
            Public render_set_begin As UInt32
            Public render_set_end As UInt32
        End Structure

        Public Structure renderItem_
            Public node_start As UInt32
            Public node_end As UInt32
            Public mat_index As UInt32
            Public primtive_index As UInt32
            Public vert_string_key As UInt32
            Public indi_string_key As UInt32
            Public is_skinned As UInt32
            Public vert_name As String
            Public indi_name As String
            Public pad_31
        End Structure

        Public Structure tbl_11_ '???
            Public index1 As UInt32
        End Structure

        Public Structure NodeItem_ '???
            Public parent_index As UInt32
            Public matrix() As Single
            Public identifier_string_key As UInt32
            Public identifier_name As String
        End Structure

    End Structure
#End Region

#Region "BSMA"
    Public cBSMA As cBSMA_
    Public Structure cBSMA_
        Public MaterialItem() As MaterialItem_
        Public FXStringKey() As FXStringKey_
        Public ShaderPropertyItem() As ShaderPropertyItem_
        Public ShaderPropertyMatrixItem() As matrix_
        Public ShaderPropertyVectorItem() As ShaderPropertyVectorItem_

        Public Structure MaterialItem_
            Public effectIndex As UInt32
            Public shaderPropBegin As UInt32
            Public shaderPropEnd As UInt32
            Public BWST_str_key As UInt32
            Public identifier As String
        End Structure

        Public Structure FXStringKey_
            Public FX_str_key As UInt32
            Public FX_string As String
        End Structure

        Public Structure ShaderPropertyItem_
            Public bwst_key_or_value As UInt32
            Public property_type As UInt32
            Public bwst_key As UInt32
            Public property_name_string As String
            Public property_value_string As String
            Public val_boolean As Boolean
            Public val_float As Single
            Public val_int As Integer
            Public val_vec4 As vect4
            Public vec4_indx As UInt32
        End Structure

        Public Structure ShaderPropertyVectorItem_
            Public vector4 As vect4
        End Structure


    End Structure
#End Region

#Region "BWAL"
    'bigworld asset list
    Public cBWAL As cBWAL_
    Public Structure cBWAL_
        Public assetList() As assetList_

        Public Structure assetList_
            Public AssetType As UInt32
            Public EntryID As UInt32
            Public string_name As String
        End Structure
    End Structure
#End Region

#Region "BWSD"
    'Decal entries
    Public cWGSD As cWGSD_
    Public Structure cWGSD_
        Public decalEntries() As DecalEntries_
        Public Structure DecalEntries_
            Public v1, v2 As UInt32
            Public accuracyType As Byte
            Public matrix() As Single
            Public diffuseMapKey As UInt32
            Public normalMapKey As UInt32
            Public gmmMapkey As UInt32
            Public extrakey As UInt32
            '
            Public flags As UInt16
            '
            Public off_x As Single
            Public off_y As Single
            Public off_z As Single
            Public off_w As Single
            '
            Public uv_wrapping_u As Single
            Public uv_wrapping_v As Single
            Public visibilityMask As UInt32

            'these 3 only exist in type 3 decals
            Public tiles_fade As Single
            Public parallax_offset As Single
            Public parallax_amplitude As Single
            '---------------------------
            Public diffuseMap As String
            Public normalMap As String
            Public gmmMap As String
            Public extraMap As String
            '---------------------------
            Public s1, s2, s3 As String
        End Structure

    End Structure
#End Region

#Region "SpTr"
    'speed tree table
    Public cSPTR As cSPTR_
    Public Structure cSPTR_
        Public Stree() As cStree_

        Public Structure cStree_
            Public key As UInt32
            Public e1 As UInt32
            Public e2 As UInt32
            Public e3 As UInt32 ' no idea what the e1-e3 are for yet
            Public Tree_name As String
            Public Matrix() As Single
        End Structure
    End Structure

#End Region

#Region "BWWa"
    Public cBWWa As cBWWa_
    Public Structure cBWWa_
        Public bwwa_t1() As cbwwa_t1_
        Public Structure cbwwa_t1_
            Public position As vect3
            Public width As Single
            Public height As Single
            Public plane As Single
            Public orientation As Single
        End Structure
    End Structure
#End Region

End Module

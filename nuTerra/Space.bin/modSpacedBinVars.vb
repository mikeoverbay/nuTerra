Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK

Module modSpacedBinVars

    Public Class BWArray(Of t)
        Public size As UInt32 ' data size per entry in bytes
        Public count As UInt32 ' number of entries in this table
        Public data As t()

        Public Sub New(br As BinaryReader)
            size = br.ReadUInt32() ' item size in bytes
            count = br.ReadUInt32() ' number of items

            ' Check item size
            Debug.Assert(Marshal.SizeOf(GetType(t)) = size)

            If count = 0 Then
                ' No items
                Return
            End If

            ReDim data(count - 1)

            Dim buffer = br.ReadBytes(size * count)
            Dim handle = GCHandle.Alloc(buffer, GCHandleType.Pinned)
            For i = 0 To count - 1
                data(i) = Marshal.PtrToStructure(Marshal.UnsafeAddrOfPinnedArrayElement(buffer, i * size), GetType(t))
            Next
            handle.Free()
        End Sub
    End Class

#Region "Structures"

    Public MODEL_MATRIX_LIST() As model_matrix_list_
    Public Structure model_matrix_list_
        Public primitive_name As String
        Public model_index As Integer
        Public matrix As Matrix4
        Public mask As Boolean
        Public BB_Min As Vector3
        Public BB_Max As Vector3
        Public BB() As Vector3
        Public exclude As Boolean
        Public destructible As Boolean
        Public exclude_list() As Integer
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
        Public offset As OpenTK.Vector4
        Public priority As Int32
        Public influence As Integer
        Public texture_matrix() As Single
        Public lbl As Vector3
        Public lbr As Vector3
        Public ltl As Vector3
        Public ltr As Vector3
        Public rbl As Vector3
        Public rbr As Vector3
        Public rtl As Vector3
        Public rtr As Vector3
        Public BB() As Vector3
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
        '''<summary>
        '''This data contains chunks of vertex creation data
        '''Chunk size is stored in cBWSG tbl 3
        '''</summary>
        Public data As Byte()

        Public Sub New(bsgdHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bsgdHeader.offset

            ' Check version in header
            Debug.Assert(bsgdHeader.version = 2)

            data = br.ReadBytes(bsgdHeader.length)
        End Sub
    End Structure
#End Region

#Region "BWST"
    Public cBWST As cBWST_
    Public Structure cBWST_
        'Storage for all the strings
        Public keys As UInt32()
        Public strs As String()

        Public Sub New(bwstHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bwstHeader.offset

            ' Check version in header
            Debug.Assert(bwstHeader.version = 2)

            Dim d_length = br.ReadUInt32
            Dim entry_cnt = br.ReadUInt32

            ReDim strs(entry_cnt)
            ReDim keys(entry_cnt)

            Dim old_pos = br.BaseStream.Position
            Dim start_offset As Long = bwstHeader.offset + (d_length * entry_cnt) + 12

            For k = 0 To entry_cnt - 1
                br.BaseStream.Position = old_pos
                keys(k) = br.ReadUInt32
                Dim offset = br.ReadUInt32
                Dim length = br.ReadUInt32
                old_pos = br.BaseStream.Position
                'move to strings locations and read it
                br.BaseStream.Position = offset + start_offset
                Dim bs = br.ReadBytes(length)
                strs(k) = Encoding.ASCII.GetString(bs)
            Next
        End Sub

        Public Function find_str(key As UInt32) As String
            Dim index As Integer = Array.BinarySearch(keys, key)
            If index >= 0 Then
                Return strs(index)
            Else
                Debug.Fail("String in BWST not found!", key.ToString)
                Return Nothing
            End If
        End Function
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
            Public chunkDataBlockLength As UInt32
            Public chunkDataBlockIndex As UInt32
            Public chunkDataOffset As UInt32
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

        Public Function find_str(key As UInt32) As String
            Dim index As Integer = Array.BinarySearch(keys, key)
            If index >= 0 Then
                Return strs(index)
            Else
                Debug.Fail("String in BWSG not found!", key.ToString)
                Return Nothing
            End If
        End Function
    End Structure
#End Region

#Region "BSMI"
    Public cBSMI As cBSMI_

    Public Structure cBSMI_
        Public transforms As BWArray(Of Matrix4)
        Public chunk_models As BWArray(Of ChunkModel_v1_0_0)
        Public visibility_masks As BWArray(Of vis_mask_)
        Public model_BSMO_indexes As BWArray(Of model_index_)
        Public animation_tbl As BWArray(Of animation_tbl_)
        Public animation_info As BWArray(Of animation_info_)
        Public tbl_7 As BWArray(Of tbl_7_)
        Public skined_tbl As BWArray(Of skined_tbl_)
        Public tbl_9 As BWArray(Of tbl_9_)
        Public tbl_10 As BWArray(Of tbl_10_)

        Public Sub New(ByRef bsmiHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bsmiHeader.offset

            ' Check version in header
            Debug.Assert(bsmiHeader.version = 3)

            transforms = New BWArray(Of Matrix4)(br)
            chunk_models = New BWArray(Of ChunkModel_v1_0_0)(br)
            visibility_masks = New BWArray(Of vis_mask_)(br)
            model_BSMO_indexes = New BWArray(Of model_index_)(br)
            animation_tbl = New BWArray(Of animation_tbl_)(br)
            animation_info = New BWArray(Of animation_info_)(br)
            tbl_7 = New BWArray(Of tbl_7_)(br)
            skined_tbl = New BWArray(Of skined_tbl_)(br)
            tbl_9 = New BWArray(Of tbl_9_)(br)
            tbl_10 = New BWArray(Of tbl_10_)(br)
        End Sub

        't2
        <StructLayout(LayoutKind.Sequential)>
        Public Structure ChunkModel_v1_0_0
            Public flags As UInt64

            ReadOnly Property flag_casts_shadow As Boolean
                Get
                    ' TODO: find correct mask
                    Return flags And 1
                End Get
            End Property

            ReadOnly Property flag_casts_local_shadow As Boolean
                Get
                    ' TODO: find correct mask
                    Return flags And 1
                End Get
            End Property

            ReadOnly Property flag_has_animations As Boolean
                Get
                    ' TODO: find correct mask
                    Return flags And 1
                End Get
            End Property
        End Structure

        't3
        <StructLayout(LayoutKind.Sequential)>
        Public Structure vis_mask_
            Public mask As UInt32
        End Structure

        't4
        <StructLayout(LayoutKind.Sequential)>
        Public Structure model_index_
            Public BSMO_MODEL_INDEX As UInt32
            Public BSMO_extras As UInt32
            'If BSMO_extras = 1 It's an important model.
            'If 0 its an extra detailing model thats not really needed.
        End Structure

        't5
        <StructLayout(LayoutKind.Sequential)>
        Public Structure animation_tbl_
            Public is_animation As UInt32
            'If this is <> &hFFFFFFFFUI, its a skined animation model
        End Structure

        't6
        <StructLayout(LayoutKind.Sequential)>
        Public Structure animation_info_
            Public model_index As UInt32
            Public seq_res_fnv As UInt32
            Public clip_name_fnv As UInt32
            Public flags As UInt32
            Public loop_count As Int32
            Public speed As Single
            Public delay As Single
            Public unknown As Single

            ReadOnly Property flag_auto_start As Boolean
                Get
                    ' TODO: find correct mask
                    Return flags And 1
                End Get
            End Property

            ReadOnly Property flag_loop As Boolean
                Get
                    ' TODO: find correct mask
                    Return flags And 1
                End Get
            End Property
        End Structure

        't7
        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_7_
            Public unknown1 As UInt32
        End Structure

        't8
        <StructLayout(LayoutKind.Sequential)>
        Public Structure skined_tbl_
            Public index1 As UInt32
            Public index2 As UInt32
            Public index3 As UInt32
        End Structure

        't9
        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_9_
            Public index1 As UInt32
        End Structure

        '10
        <StructLayout(LayoutKind.Sequential)>
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

#Region "WTbl"
    Public cWTbl As cWTbl_
    Public Structure cWTbl_
        Public benchmark_locations As BWArray(Of Vector3)

        Public Sub New(ByRef wtblHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = wtblHeader.offset

            ' Check version in header
            Debug.Assert(wtblHeader.version = 0)

            benchmark_locations = New BWArray(Of Vector3)(br)
        End Sub
    End Structure
#End Region

#Region "BSMO"
    Public cBSMO As cBSMO_
    Public Structure cBSMO_
        Public models_loddings As BWArray(Of ModelLoddingItem_v0_9_12)
        Public tbl_2 As BWArray(Of tbl_2_)
        Public models_colliders As BWArray(Of ModelColliderItem_v0_9_12)
        Public bsp_material_kinds As BWArray(Of BSPMaterialKindItem_v0_9_12)
        Public models_visibility_bounds As BWArray(Of BoundingBox)
        Public tbl_6 As BWArray(Of tbl_6_)
        Public tbl_7 As BWArray(Of tbl_7_)
        Public lod_loddings As BWArray(Of lod_range_)
        Public lod_renders As BWArray(Of LODRenderItem_v0_9_12)
        Public renders As BWArray(Of RenderItem_v0_9_12)
        Public node_affectors1 As BWArray(Of tbl_11_)
        Public visual_nodes As BWArray(Of NodeItem_v1_0_0)
        ' TODO: wsmo_4
        ' TODO: wsmo_2
        ' TODO: wsmo_3
        ' TODO: havok_info
        ' TODO: 16_8
        ' TODO: vertices_data_sizes

        Public Sub New(ByRef bsmoHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bsmoHeader.offset

            ' Check version in header
            Debug.Assert(bsmoHeader.version = 2)

            ' FIXME: Find a shorter way
            models_loddings = New BWArray(Of ModelLoddingItem_v0_9_12)(br)
            tbl_2 = New BWArray(Of tbl_2_)(br)
            models_colliders = New BWArray(Of ModelColliderItem_v0_9_12)(br)
            bsp_material_kinds = New BWArray(Of BSPMaterialKindItem_v0_9_12)(br)
            models_visibility_bounds = New BWArray(Of BoundingBox)(br)
            tbl_6 = New BWArray(Of tbl_6_)(br)
            tbl_7 = New BWArray(Of tbl_7_)(br)
            lod_loddings = New BWArray(Of lod_range_)(br)
            lod_renders = New BWArray(Of LODRenderItem_v0_9_12)(br)
            renders = New BWArray(Of RenderItem_v0_9_12)(br)
            node_affectors1 = New BWArray(Of tbl_11_)(br)
            visual_nodes = New BWArray(Of NodeItem_v1_0_0)(br)
        End Sub

        '''<summary>
        '''Contains the list of LODs for this model.
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure ModelLoddingItem_v0_9_12
            Public lod_begin As UInt32
            Public lod_end As UInt32
        End Structure

        '''<summary>
        '''???
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_2_
            Public unknown As UInt32
        End Structure

        '''<summary>
        '''Contains the collision data for this model such as bounds and bsp.
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure ModelColliderItem_v0_9_12
            Public collision_bounds_min As Vector3
            Public collision_bounds_max As Vector3
            Public bsp_section_name_fnv As UInt32
            Public bsp_material_kind_begin As UInt32
            Public bsp_material_kind_end As UInt32

            ReadOnly Property primitive_name As String
                Get
                    If bsp_section_name_fnv = 0 Then
                        Return Nothing
                    End If
                    Return cBWST.find_str(bsp_section_name_fnv)
                End Get
            End Property

            ReadOnly Property model_name As String
                Get
                    If bsp_section_name_fnv = 0 Then
                        Return Nothing
                    End If
                    Return cBWST.find_str(bsp_section_name_fnv).Replace(".primitives", ".model")
                End Get
            End Property
        End Structure

        '''<summary>
        '''Contains data for bsp material linkage and flags
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure BSPMaterialKindItem_v0_9_12
            Public material_index As UInt32
            Public flags As UInt32
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure BoundingBox
            Public min_BB As Vector3
            Public max_BB As Vector3
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_6_ '??? not even sure of data size
            Public index1 As UInt32
            Public index2 As UInt32
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_7_ '???
            Public index1 As UInt32
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure lod_range_
            'These are the square of the actual range
            '(x*x) + (y*y) + (z*z) - Camera to item distance
            Public range As Single
        End Structure

        '''<summary>
        '''Contains the render sets to draw for this lod
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure LODRenderItem_v0_9_12
            Public render_set_begin As UInt32
            Public render_set_end As UInt32
        End Structure

        '''<summary>
        '''Contains the data for rendering a model segment, including nodes, material,
        '''primitives, verts And draw flags.
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure RenderItem_v0_9_12
            Public node_begin As UInt32
            Public node_end As UInt32
            Public material_index As UInt32
            Public primtive_index As UInt32
            Public verts_name_fnv As UInt32
            Public prims_name_fnv As UInt32
            Public is_skinned As UInt32

            ReadOnly Property vert_name As String
                Get
                    Return cBWST.find_str(verts_name_fnv)
                End Get
            End Property

            ReadOnly Property indi_name As String
                Get
                    Return cBWST.find_str(prims_name_fnv)
                End Get
            End Property
        End Structure

        '''<summary>
        '''Contains a relative index To a node that affects this render Set,
        '''so we can have sequential access
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure tbl_11_
            Public index1 As UInt32
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure NodeItem_v1_0_0
            Public parent_index As UInt32
            Public matrix As Matrix4
            Public identifier_fnv As UInt32

            ReadOnly Property identifier_name As String
                Get
                    Return cBWST.find_str(identifier_fnv)
                End Get
            End Property
        End Structure
    End Structure
#End Region

#Region "BSMA"
    Public cBSMA As cBSMA_
    Public Structure cBSMA_
        Public MaterialItem() As MaterialItem_
        Public FXStringKey() As FXStringKey_
        Public ShaderPropertyItem() As ShaderPropertyItem_
        Public ShaderPropertyMatrixItem As Matrix4()
        Public ShaderPropertyVectorItem As Vector4()

        Public Structure MaterialItem_
            Public effectIndex As Int32
            Public shaderPropBegin As Int32
            Public shaderPropEnd As Int32
            Public BWST_str_key As UInt32
            Public identifier As String
            Public FX_string As String
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
            Public val_vec4 As Vector4
            Public vec4_indx As UInt32
        End Structure

    End Structure
#End Region

#Region "BWAL"
    'bigworld asset list
    Public cBWAL As cBWAL_
    Public Structure cBWAL_
        Public assetList() As assetList_

        Public Sub New(ByRef bwalHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bwalHeader.offset

            ' Check version in header
            Debug.Assert(bwalHeader.version = 2)

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table

            ReDim assetList(tl - 1)
            For k = 0 To tl - 1
                assetList(k).AssetType = br.ReadUInt32
                assetList(k).EntryID = br.ReadUInt32
            Next
        End Sub

        <StructLayout(LayoutKind.Sequential)>
        Public Structure assetList_
            Public AssetType As UInt32
            Public EntryID As UInt32

            ReadOnly Property string_name As String
                Get
                    Dim ns() = {"ASSET_TYPE_UNKNOWN_TYPE", "ASSET_TYPE_DATASECTION", "ASSET_TYPE_TEXTURE", "ASSET_TYPE_EFFECT", "ASSET_TYPE_PRIMITIVE", "ASSET_TYPE_VISUAL", "ASSET_TYPE_MODEL"}
                    Return ns(AssetType)
                End Get
            End Property
        End Structure
    End Structure
#End Region

#Region "WGSD"
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
    Public cSpTr As cSpTr_
    Public Structure cSpTr_
        Public Stree As BWArray(Of cStree_)

        Public Sub New(ByRef sptrHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = sptrHeader.offset

            ' Check version in header
            Debug.Assert(sptrHeader.version = 3)

            Stree = New BWArray(Of cStree_)(br)
        End Sub

        <StructLayout(LayoutKind.Sequential)>
        Public Structure cStree_
            Public transform As Matrix4
            Public spt_fnv As UInt32
            Public seed As UInt32
            Public flags As UInt32
            Public visibility_mask As UInt32

            ReadOnly Property tree_name As String
                Get
                    Return cBWST.find_str(spt_fnv)
                End Get
            End Property
        End Structure
    End Structure

#End Region

#Region "BWWa"
    Public cBWWa As cBWWa_
    Public Structure cBWWa_
        Public bwwa_t1() As cbwwa_t1_

        Public Sub New(ByRef bwwaHeader As SectionHeader, br As BinaryReader)
            'set stream reader to point at this chunk
            br.BaseStream.Position = bwwaHeader.offset

            ' Check version in header
            Debug.Assert(bwwaHeader.version = 2)

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table

            If tl = 0 Then
                'no water
                water.IsWater = False
                Return
            End If

            ReDim bwwa_t1(0)

            Try
                Dim bbox_min, bbox_max As Vector3
                bbox_min.X = br.ReadSingle
                bbox_min.Y = br.ReadSingle
                bbox_min.Z = br.ReadSingle

                bbox_max.X = br.ReadSingle
                bbox_max.Y = br.ReadSingle
                bbox_max.Z = br.ReadSingle

                bwwa_t1(0).position.X = -(bbox_min.X + bbox_max.X) / 2.0!
                bwwa_t1(0).position.Y = bbox_min.Y
                bwwa_t1(0).position.Z = (bbox_min.Z + bbox_max.Z) / 2.0!

                bwwa_t1(0).width = Math.Abs(bbox_min.X) + Math.Abs(bbox_max.X)
                bwwa_t1(0).plane = bbox_min.Y
                bwwa_t1(0).height = Math.Abs(bbox_min.Z) + Math.Abs(bbox_max.Z)

                water.IsWater = True
                WATER_LINE = bwwa_t1(0).position.Y
            Catch ex As Exception
                'FIXME!
                water.IsWater = False
                WATER_LINE = -500.0
            End Try
        End Sub

        Public Structure cbwwa_t1_
            Public position As Vector3
            Public width As Single
            Public height As Single
            Public plane As Single
            Public orientation As Single
        End Structure
    End Structure
#End Region

End Module

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports OpenTK.Mathematics

Module modSpacedBinVars
    <Flags()> Public Enum VisbilityFlags As UInt32
        CAPTURE_THE_FLAG = 1 << 0
        DOMINATION = 1 << 1
        ASSAULT = 1 << 2
        NATIONS = 1 << 3
        CAPTURE_THE_FLAG_2 = 1 << 4
        DOMINATION_2 = 1 << 5
        ASSAULT_2 = 1 << 6
        FALLOUT_BOMB = 1 << 7
        FALLOUT_2_FLAG = 1 << 8
        FALLOUT_3 = 1 << 9
        FALLOUT_4 = 1 << 10
        CAPTURE_THE_FLAG_30_VS_30 = 1 << 11
        DOMINATION_30_VS_30 = 1 << 12
        SANDBOX = 1 << 13
        BOOTCAMP = 1 << 14
        VISIBLE_FOR_OBSERVER = 1 << 15
    End Enum

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
                data(i) = Marshal.PtrToStructure(Marshal.UnsafeAddrOfPinnedArrayElement(buffer, CType(i * size, Integer)), GetType(t))
            Next
            handle.Free()
        End Sub
    End Class

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

#Region "BWT2"
    Public cBWT2 As cBWT2_
    Public Structure cBWT2_
        Public settings As TerrainSettings1_v0_9_20
        Public cdatas As BWArray(Of ChunkTerrain_v0_9_12)
        Public _3 As BWArray(Of Int32)
        Public settings2 As TerrainSettings2_v1_6_1
        Public lod_distances As BWArray(Of Single) ' terrain/lodInfo/lodDistances
        Public _6 As _6_
        Public cascades As BWArray(Of OutlandCascade_v1_0_0) ' outland/cascade
        Public tiles_fnv As BWArray(Of UInt32) ' outland/tiles

        Public Sub New(bwt2Header As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bwt2Header.offset

            ' Check version in header
            Debug.Assert(bwt2Header.version = 3)

            settings = New TerrainSettings1_v0_9_20(br)
            cdatas = New BWArray(Of ChunkTerrain_v0_9_12)(br)
            _3 = New BWArray(Of Integer)(br)
            settings2 = TerrainSettings2_v1_6_1.Create(br)
            lod_distances = New BWArray(Of Single)(br)
            _6 = New _6_(br)
            cascades = New BWArray(Of OutlandCascade_v1_0_0)(br)
            tiles_fnv = New BWArray(Of UInt32)(br)
        End Sub

        <StructLayout(LayoutKind.Sequential)>
        Public Structure _6_
            Public int_1 As Int32
            Public int_2 As Int32
            Public Sub New(br As BinaryReader)
                int_1 = br.ReadInt32
                int_2 = br.ReadInt32
            End Sub
        End Structure

        <StructLayout(LayoutKind.Sequential)>
            Public Structure OutlandCascade_v1_0_0
                Public outland_BB_min As Vector3
                Public outland_bb_max As Vector3
                Public height_map_fnv As UInt32
                Public normal_map_fvn As UInt32
                Public tile_map_fvn As UInt32
                Public tileScale As Single


                Public Sub New(br As BinaryReader)
                    Dim size = br.ReadUInt32()
                    Debug.Assert(Marshal.SizeOf(Me) = size)

                    outland_BB_min.X = br.ReadSingle
                    outland_BB_min.Y = br.ReadSingle
                    outland_BB_min.Z = br.ReadSingle

                    outland_bb_max.X = br.ReadSingle
                    outland_bb_max.Y = br.ReadSingle
                    outland_bb_max.Z = br.ReadSingle

                    height_map_fnv = br.ReadUInt32
                    normal_map_fvn = br.ReadUInt32
                    tile_map_fvn = br.ReadUInt32

                    tileScale = br.ReadSingle
                End Sub

                ReadOnly Property height_map As String
                    Get
                        Return cBWST.find_str(height_map_fnv)
                    End Get
                End Property

                ReadOnly Property normal_map As String
                    Get
                        Return cBWST.find_str(normal_map_fvn)
                    End Get
                End Property

                ReadOnly Property tile_map As String
                    Get
                        Return cBWST.find_str(tile_map_fvn)
                    End Get
                End Property

            End Structure

            <StructLayout(LayoutKind.Sequential)>
            Public Structure TerrainSettings1_v0_9_20
                Public chunk_size As Single ' space.settings/chunkSize or 100.0 by default
                Public bounds_minX As Int32 ' space.settings/bounds
                Public bounds_maxX As Int32 ' space.settings/bounds
                Public bounds_minY As Int32 ' space.settings/bounds
                Public bounds_maxY As Int32 ' space.settings/bounds
                Public normal_map_fnv As UInt32
                Public global_map_fnv As UInt32 ' global_AM.dds, maybe tintTexture - global terrain albedo map
                Public noise_texture_fnv As UInt32 ' noiseTexture

                Public Sub New(br As BinaryReader)
                    Dim size = br.ReadUInt32()
                    Debug.Assert(Marshal.SizeOf(Me) = size)

                    chunk_size = br.ReadSingle()
                    bounds_minX = br.ReadInt32()
                    bounds_maxX = br.ReadInt32()
                    bounds_minY = br.ReadInt32()
                    bounds_maxY = br.ReadInt32()
                    normal_map_fnv = br.ReadUInt32()
                    global_map_fnv = br.ReadUInt32()
                    noise_texture_fnv = br.ReadUInt32()
                End Sub

                ReadOnly Property normal_map As String
                    Get
                        Return cBWST.find_str(normal_map_fnv)
                    End Get
                End Property

                ReadOnly Property global_map As String
                    Get
                        Return cBWST.find_str(global_map_fnv)
                    End Get
                End Property

                ReadOnly Property noise_texture As String
                    Get
                        Return cBWST.find_str(noise_texture_fnv)
                    End Get
                End Property
            End Structure

            <StructLayout(LayoutKind.Sequential)>
            Public Structure ChunkTerrain_v0_9_12
                Public resource_fnv As UInt32
                Public loc_x As Int16
                Public loc_y As Int16

                ReadOnly Property resource As String
                    Get
                        Return cBWST.find_str(resource_fnv)
                    End Get
                End Property
            End Structure

            <StructLayout(LayoutKind.Sequential)>
            Public Structure TerrainSettings2_v1_6_1
                Public terrain_version As UInt32      ' space.settings/terrain/version
                Public flags As UInt32
                Public height_map_size As UInt32        ' terrain/heightMapSize
                Public normal_map_size As UInt32        ' terrain/normalMapSize
                Public hole_map_size As UInt32          ' terrain/holeMapSize
                Public shadow_map_size As UInt32        ' terrain/shadowMapSize
                Public blend_map_size As UInt32         ' terrain/blendMapSize
                Public lod_texture_distance As Single   ' terrain/lodInfo/lodTextureDistance
                Public macro_lod_start As Single        ' terrain/lodInfo/macroLODStart
                Public unknown_1 As UInt32              ' blend mode avg color/avg alpha height ??
                Public start_bias As Single             ' terrain/lodInfo/startBias
                Public end_bias As Single               ' terrain/lodInfo/endBias
                Public direct_occlusion As Single       ' terrain/soundOcclusion/directOcclusion
                Public reverb_occlusion As Single       ' terrain/soundOcclusion/reverbOcclusion
                Public wrap_u As Single                 ' terrain/detailNormal/wrapU
                Public wrap_v As Single                 ' terrain/detailNormal/wrapV
                Public unknown_2 As UInt32              ' tessZoomUpperThreshold
                Public unknown_3 As Single              ' tessZoomLowerThreshold
                Public unknown_4 As Single              ' tessZoomUpperScale
                Public unknown_5 As Single              ' tessZoomLowerScale
                Public blend_macro_influence As Single  ' terrain/blendMacroInfluence
                Public blend_global_threshold As Single ' terrain/blendGlobalThreshold
                Public blend_height As Single           ' terrain/blendHeight
                Public disabled_blend_height As Single  ' terrain/disabledBlendHeight
                Public vt_lod_params As Vector4         ' terrain/VTLodParams
                Public bounding_box As Vector4

                Public Shared Function Create(br As BinaryReader) As TerrainSettings2_v1_6_1
                    Dim size = br.ReadUInt32()
                    Debug.Assert(Marshal.SizeOf(Create) = size)

                    Dim buffer = br.ReadBytes(size)
                    Dim handle = GCHandle.Alloc(buffer, GCHandleType.Pinned)
                    Create = Marshal.PtrToStructure(Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), GetType(TerrainSettings2_v1_6_1))
                    handle.Free()

                    CommonProperties.blend_macro_influence = Create.blend_macro_influence
                    CommonProperties.blend_global_threshold = Create.blend_global_threshold
                End Function
            End Structure
        End Structure
#End Region

#Region "BSMI"
        Public cBSMI As cBSMI_

    Public Structure cBSMI_
        Public transforms As BWArray(Of Matrix4)
        Public chunk_models As BWArray(Of ChunkModel_v1_0_0)
        Public visibility_masks As BWArray(Of vis_mask_)
        Public model_BSMO_indexes As BWArray(Of model_index_)

        Public Sub New(bsmiHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bsmiHeader.offset

            ' Check version in header
            Debug.Assert(bsmiHeader.version = 3)

            transforms = New BWArray(Of Matrix4)(br)
            chunk_models = New BWArray(Of ChunkModel_v1_0_0)(br)
            visibility_masks = New BWArray(Of vis_mask_)(br)
            model_BSMO_indexes = New BWArray(Of model_index_)(br)
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

        <StructLayout(LayoutKind.Sequential)>
        Public Structure vis_mask_
            Public mask As VisbilityFlags
        End Structure

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
            Public unknown2 As Single

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

#Region "BSMO"
    Public cBSMO As cBSMO_
    Public Structure cBSMO_
        Public models_loddings As BWArray(Of ModelLoddingItem_v0_9_12)
        Public tbl_2 As BWArray(Of tbl_2_)
        Public models_colliders As BWArray(Of ModelColliderItem_v0_9_12)
        Public bsp_material_kinds As BWArray(Of BSPMaterialKindItem_v0_9_12)
        Public models_visibility_bounds As BWArray(Of Matrix2x3)
        Public model_info_items As BWArray(Of WoTModelInfoItem_v0_9_12)
        Public model_sound_items As BWArray(Of UInt32)
        Public lod_loddings As BWArray(Of lod_range_)
        Public lod_renders As BWArray(Of LODRenderItem_v0_9_12)
        Public renders As BWArray(Of RenderItem_v0_9_12)
        Public node_affectors1 As BWArray(Of tbl_11_)
        Public visual_nodes As BWArray(Of NodeItem_v1_0_0)
        Public model_hardpoint_items As BWArray(Of Matrix4)
        Public falling_model_info_items As BWArray(Of WoTFallingModelInfoItem_v1_0_0)
        Public fragile_model_info_items As BWArray(Of WoTFragileModelInfoItem_v1_0_0)

        Public Sub New(bsmoHeader As SectionHeader, br As BinaryReader)
            ' set stream reader to point at this chunk
            br.BaseStream.Position = bsmoHeader.offset

            ' Check version in header
            Debug.Assert(bsmoHeader.version = 2)

            ' FIXME: Find a shorter way
            models_loddings = New BWArray(Of ModelLoddingItem_v0_9_12)(br)
            tbl_2 = New BWArray(Of tbl_2_)(br)
            models_colliders = New BWArray(Of ModelColliderItem_v0_9_12)(br)
            bsp_material_kinds = New BWArray(Of BSPMaterialKindItem_v0_9_12)(br)
            models_visibility_bounds = New BWArray(Of Matrix2x3)(br)
            model_info_items = New BWArray(Of WoTModelInfoItem_v0_9_12)(br)
            model_sound_items = New BWArray(Of UInt32)(br)
            lod_loddings = New BWArray(Of lod_range_)(br)
            lod_renders = New BWArray(Of LODRenderItem_v0_9_12)(br)
            renders = New BWArray(Of RenderItem_v0_9_12)(br)
            node_affectors1 = New BWArray(Of tbl_11_)(br)
            visual_nodes = New BWArray(Of NodeItem_v1_0_0)(br)
            model_hardpoint_items = New BWArray(Of Matrix4)(br)
            falling_model_info_items = New BWArray(Of WoTFallingModelInfoItem_v1_0_0)(br)
            fragile_model_info_items = New BWArray(Of WoTFragileModelInfoItem_v1_0_0)(br)
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

        '''<summary>
        '''Contains information on the type of WoT model (Static, Falling,
        '''Fragile, Structure) And an index to associated data for that.
        '''</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure WoTModelInfoItem_v0_9_12
            Public type As UInt32
            Public info_index As UInt32
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

        <StructLayout(LayoutKind.Sequential)>
        Public Structure WoTFallingModelInfoItem_v1_0_0
            Public lifetime_effect_fnv As UInt32
            Public fracture_effect_fnv As UInt32
            Public touchdown_effect_fnv As UInt32
            Public unknown As Single
            Public effect_scale As Single
            Public physic_params_1 As Single
            Public physic_params_2 As Single
            Public physic_params_3 As Single
            Public physic_params_4 As Single
            Public physic_params_5 As Single
            Public physic_params_6 As Single
            Public physic_params_7 As Single
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure WoTFragileModelInfoItem_v1_0_0
            Public lifetime_effect_fnv As UInt32
            Public effect_fnv As UInt32
            Public decay_effect_fnv As UInt32
            Public hit_effect_fnv As Single
            Public _4 As Single
            Public effect_scale As Single
            Public hardpoint_index As UInt32
            Public destroyed_model_index As UInt32
            Public entry_type As UInt32
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
            Public effectIndex As UInt32
            Public shaderPropBegin As UInt32
            Public shaderPropEnd As UInt32
            Public identifier_fnv As UInt32

            ReadOnly Property identifier As String
                Get
                    Return cBWST.find_str(identifier_fnv)
                End Get
            End Property
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

#Region "WGSD"
    'Decal entries
    Public cWGSD As cWGSD_
    Public Structure cWGSD_
        Public decalEntries() As DecalEntries_
        Public Structure DecalEntries_
            Public v1, v2 As UInt32
            Public accurate As Byte
            Public transform As Matrix4
            Public diff_tex_fnv As UInt32
            Public bump_tex_fnv As UInt32
            Public hm_tex_fnv As UInt32
            Public add_tex_fnv As UInt32
            '
            Public priority As UInt32
            Public influenceType As Byte
            Public materialType As Byte
            '
            Public offsets As Vector4
            '
            Public uv_wrapping As Vector2
            Public visibility_mask As UInt32

            ' these 3 only exist in type 3 decals
            Public tiles_fade As Single
            Public parallax_offset As Single
            Public parallax_amplitude As Single

            Public s1, s2, s3 As String
        End Structure

    End Structure
#End Region

#Region "SpTr"
    'speed tree table
    Public cSpTr As cSpTr_
    Public Structure cSpTr_
        Public Stree As BWArray(Of cStree_)

        Public Sub New(sptrHeader As SectionHeader, br As BinaryReader)
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

        Public Sub New(bwwaHeader As SectionHeader, br As BinaryReader)
            'set stream reader to point at this chunk
            br.BaseStream.Position = bwwaHeader.offset

            ' Check version in header
            Debug.Assert(bwwaHeader.version = 2)

            Dim ds = br.ReadUInt32 'data size per entry in bytes
            Dim tl = br.ReadUInt32 ' number of entries in this table

            If tl = 0 Then
                'no water
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

                WATER_LINE = bwwa_t1(0).position.Y
            Catch ex As Exception
                'FIXME!
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

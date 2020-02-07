﻿Imports OpenTK.Graphics.OpenGL
Imports System.Runtime.InteropServices.Marshal
Module modTypeStructures
    '--------------------------------------------------------

    Public Structure vect4
        Public x As Single
        Public y As Single
        Public z As Single
        Public w As Single
    End Structure

    Public Structure vect3
        Public x As Single
        Public y As Single
        Public z As Single
    End Structure
    Public Structure vect3_16
        Public x As UInt16
        Public y As UInt16
        Public z As UInt16
    End Structure
    Public Structure vect3_32
        Public x As UInt32
        Public y As UInt32
        Public z As UInt32
    End Structure

    Public Structure vect2
        Public x As Single
        Public y As Single
    End Structure
    '--------------------------------------------------------
    Public water As New water_model_
    Public Structure water_model_
        Public displayID_cube As Integer
        Public displayID_plane As Integer
        Public textureID As Integer
        Public normalID As Integer
        Public aspect As Single
        Public size_ As vect3
        Public position As vect3
        Public orientation As Single
        Public type As String
        Public IsWater As Boolean
        Public foam_id As Integer
        Public lbl As vect3
        Public lbr As vect3
        Public ltl As vect3
        Public ltr As vect3
        Public rbl As vect3
        Public rbr As vect3
        Public rtl As vect3
        Public rtr As vect3
        Public BB() As vect3
        Public matrix() As Single
    End Structure
    '--------------------------------------------------------
    Public Structure matrix_
        Public matrix() As Single
    End Structure
    '--------------------------------------------------------
    Public Structure vertex_data
        Public x As Single
        Public y As Single
        Public z As Single
        Public u As Single
        Public v As Single
        Public nx As Single
        Public ny As Single
        Public nz As Single
        Public map As Integer
        Public t As vect3
        Public bt As vect3
        Public hole As Single
    End Structure

    Public triangle_holder As New mappedFile_

    Public Models As model_
    Public Structure model_
        Public Model_list() As String
        Public models() As primitive
        Public matrix() As matrix_
        Public model_count As UInt32
    End Structure
    Public Structure primitive
        Public _count As Integer
        Public componets() As Model_Section
        Public isBuilding As Boolean 'Used to render buildings only on first pass. Its decal rendering logic.
        Public visible As Boolean
    End Structure
    Public Structure Model_Section
        Public callList_ID As Int32
        ' Public indices() As Integer
        Public vertices() As vect3
        Public normals() As vect3
        Public tangents() As vect3
        Public binormals() As vect3
        Public UVs() As vect2
        Public UV2s() As vect2
        Public name As String
        'for texture mapping
        Public color_id As Int32
        Public normal_Id As Int32
        Public color2_Id As Integer
        Public _count As UInt32
        Public multi_textured As Boolean
        Public alpha_only As Boolean
        Public bumped As Boolean
        Public GAmap As Boolean
        Public color2_name As String
        Public color_name As String
        Public normal_name As String
        Public alphaRef As Integer
        Public alphaTestEnable As Integer
    End Structure

    Public Structure destructibles
        Public filename As List(Of String)
        Public matName As List(Of String)
    End Structure

    Public Structure base_model_holder_

        '------------------------------------------------
        Public primitive_name As String
        '------------------------------------------------
        'VAO and render flags
        Public has_uv2 As Integer
        Public has_tangent As Integer
        Public USHORTS As Boolean 'If true, indices are Uint16, other wise unit32

        Public is_building As Boolean ' used with decals
        Public POLY_COUNT As Integer
        Public junk As Boolean
        '------------------------------------------------
        'These are in the MODEL_MATRIX_LIST and wont be used here.
        Public BB() As vect3 'Used for frustrum culling
        Public culled As Boolean 'Draw if not true
        '------------------------------------------------

        'used to create VBO
        Public element_count As Integer ' how many buffers will be created

        Public indice_count As Integer
        Public indice_size As Integer

        Public sb_vertex_count As UInteger
        Public sb_start_index As UInteger
        Public sb_end_index As UInteger
        Public sb_vertex_type As String
        Public sb_vertex_stride As UInteger
        Public sb_block_type As Integer
        Public sb_table_size As Integer
        Public sb_LOD_set_start As Integer
        Public sb_LOD_set_end As Integer
        Public sb_model_material_begin As UInt32
        Public sb_model_material_end As UInt32
        ' storage
        Public sb_vertex_data() As Byte
        Public sp_indi_data() As Byte
        Public sp_uv2_data() As Byte

        'buffer Ids
        Public mdl_VAO As Integer
        Public mBuffers() As Integer

        '------------------------------------------------
        'Storage
        Public Vertex_buffer() As vect3
        Public Normal_buffer() As vect3
        Public UV1_buffer() As vec2
        Public tangent_buffer() As vect3
        Public biNormal_buffer() As vect3
        Public UV2_buffer() As vec2

        Public index_buffer16() As vect3_16
        Public index_buffer32() As vect3_32

        'list of indice sizes, offsets,
        'texture IDs.. render settings... so on
        Public entries() As entries_

        'number if model components.
        Public primitive_count As Integer

        Public Sub flush()
            'free the memory
            ReDim Me.Vertex_buffer(0)
            ReDim Me.Normal_buffer(0)
            ReDim Me.UV1_buffer(0)
            ReDim Me.tangent_buffer(0)
            ReDim Me.biNormal_buffer(0)
            ReDim Me.UV2_buffer(0)

            'ReDim Me.index_buffer16(0)
            'ReDim Me.index_buffer32(0)
        End Sub

    End Structure


    Public Structure entries_
        'If I remember from Tank Exporter..
        'There are cases where some components of the same model
        'have no UV2 stream but others do.
        'We need this flag for building the VBO and signaling the shader.
        'We use a Integer because it can be passed directly to the shader.
        Public has_uv2 As Integer
        '------------------------------------
        'length and size of each primitive component
        'startIndex and numIndices is scaled in load_primtive.
        Public numIndices As Int32
        Public UnumIndices As UInt32
        Public numVertices As UInt32
        Public startVertex As Int32
        Public startIndex As Int32
        '------------------------------------
        Public list_id As Integer
        '------------------------------------
        Public draw As Boolean
        '------------------------------------

        Public ShaderType As Integer
        'shader types
        '1 = color only
        '2 = color normal
        '3 = color normal gmm
        '4 = atlas
        '5 = atlas glass
        '------------------------------------
        'texture string names from space.bin
        Public diffuseMap As String
        Public diffuseMap2 As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public atlasBlend As String
        Public atlasMetallicAO As String
        Public atlasNormalGlossSpec As String
        Public atlasAlbedoHeight As String
        Public dirtMap As String
        Public globalTex As String
        'texture ids
        Public diffuseMap_id As Integer
        Public diffuseMap2_id As Integer
        Public normalMap_id As Integer
        Public metallicGlossMap_id As Integer
        Public atlasBlend_id As Integer
        Public atlasMetallicAO_id As Integer
        Public atlasNormalGlossSpec_id As Integer
        Public atlasAlbedoHeight_id As Integer
        Public dirtMap_id As Integer
        Public globalTex_id As Integer
        '------------------------------------
        'values from space.bin
        Public alphaReference As Integer
        Public g_tintColor As vect4
        Public g_tile0Tint As vect4
        Public g_tile1Tint As vect4
        Public g_tile2Tint As vect4
        Public g_dirtParams As vect4
        Public g_dirtColor As vect4
        Public g_atlasSizes As vect4
        Public g_atlasIndexes As vect4
        Public g_vertexColorMode As Integer
        Public g_vertexAnimationParams As vect4
        Public g_fakeShadowsParams As vect4 '<-- interesting
        '- render params from space.bin
        Public FX_shader As String
        Public identifier As String
        Public groupID As Integer
        '------------------------------------
        'booleans from space.bin
        Public doubleSided As Integer
        Public alphaEnable As Integer
        Public TexAddressMode As Integer
        Public dynamicobject As Integer
        Public g_enableAO As Integer
        Public g_useNormalPackDXT1 As Integer ' If this is true, this uses the old RGB normal maps;
        '------------------------------------

    End Structure


End Module

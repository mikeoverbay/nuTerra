﻿Imports OpenTK

Module modTypeStructures
    '--------------------------------------------------------

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

    '--------------------------------------------------------
    Public water As New water_model_
    Public Structure water_model_
        Public displayID_cube As Integer
        Public displayID_plane As Integer
        Public textureID As Integer
        Public normalID As Integer
        Public aspect As Single
        Public size_ As Vector3
        Public position As Vector3
        Public orientation As Single
        Public type As String
        Public IsWater As Boolean
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
        Public t As Vector3
        Public bt As Vector3
        Public hole As Single
    End Structure

    Public triangle_holder As New mappedFile_


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
        Public POLY_COUNT As UInteger
        Public junk As Boolean
        '------------------------------------------------
        'These are in the MODEL_MATRIX_LIST and wont be used here.
        Public BB() As Vector3 'Used for frustrum culling
        '------------------------------------------------

        'used to create VBO
        'how many parallel buffers will be created
        Public element_count As Integer

        'number if model components
        Public primitive_count As Integer

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
        Public Vertex_buffer() As Vector3
        Public Normal_buffer() As Vector3
        Public UV1_buffer() As Vector2
        Public tangent_buffer() As Vector3
        Public biNormal_buffer() As Vector3
        Public UV2_buffer() As Vector2

        Public index_buffer16() As vect3_16
        Public index_buffer32() As vect3_32

        'list of indice sizes, offsets,
        'texture IDs.. render settings... so on
        Public entries() As entries_


        Public Sub flush()
            'free the memory
            Vertex_buffer = Nothing
            Normal_buffer = Nothing
            UV1_buffer = Nothing
            tangent_buffer = Nothing
            biNormal_buffer = Nothing
            UV2_buffer = Nothing

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
        Public g_tintColor As Vector4
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_dirtParams As Vector4
        Public g_dirtColor As Vector4
        Public g_atlasSizes As Vector4
        Public g_atlasIndexes As Vector4
        Public g_vertexColorMode As Integer
        Public g_vertexAnimationParams As Vector4
        Public g_fakeShadowsParams As Vector4 '<-- interesting
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

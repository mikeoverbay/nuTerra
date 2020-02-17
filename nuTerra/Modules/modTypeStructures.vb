Imports OpenTK

Module modTypeStructures

#Region "MODEL_INDEX_LIST"
    Public Structure boundingBox_
        Public BB() As Vector3
    End Structure

    Public MODEL_INDEX_LIST() As MODEL_INDEX_LIST_
    Public Structure MODEL_INDEX_LIST_ : Implements IComparable(Of MODEL_INDEX_LIST_)
        Public model_index As Integer
        Public matrix As Matrix4
        Public BB_Max As Vector3
        Public BB_Min As Vector3
        Public BB() As Vector3
        Public VAO As Integer
        Public VB As Integer
        Public Function CompareTo(ByVal other As MODEL_INDEX_LIST_) As Integer Implements System.IComparable(Of MODEL_INDEX_LIST_).CompareTo
            Return Me.model_index.CompareTo(other.model_index)
        End Function
    End Structure

#End Region

#Region "Model_Batch_list"

    Public MODEL_BATCH_LIST As List(Of ModelBatch)
    Public Class ModelBatch
        Public model_id As Integer
        Public offset As Integer
        Public count As Integer

        Public culledQuery As Integer
        Public cullVA As Integer
        Public instanceDataBO As Integer
        Public culledInstanceDataBO As Integer
        Public BoundingBoxInstanceDataBO As Integer
        Public visibleCount As Integer
    End Class

#End Region

#Region "DECAL_INDEX_LIST"

    Public DECAL_INDEX_LIST() As DECAL_INDEX_LIST_
    Public Structure DECAL_INDEX_LIST_
        Public u_wrap As Single
        Public v_wrap As Single
        Public decal_data() As vertex_data_
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

    '--------------------------------------------------------
    Public triangle_holder As New mappedFile_
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
    Public Structure destructibles
        Public filename As List(Of String)
        Public matName As List(Of String)
    End Structure
    '--------------------------------------------------------

#Region "Water_model"

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

#End Region

#Region "vertex_data_"

    Public Structure vertex_data_
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

#End Region



#Region "base_Model_holder_"

    Public Class base_model_holder_

        '------------------------------------------------
        Public primitive_name As String
        '------------------------------------------------
        'VAO and render flags
        Public has_uv2 As Integer
        Public USHORTS As Boolean 'If true, indices are Uint16, other wise unit32

        Public is_building As Boolean ' used with decals
        Public POLY_COUNT As UInteger
        Public junk As Boolean

        '------------------------------------------------
        'used to create VBO
        'how many parallel buffers will be created
        Public element_count As Integer

        Public has_tangent As Integer

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
        'Public sb_vertex_data() As Byte
        'Public sb_indi_data() As Byte
        'Public sb_uv2_data() As Byte

        'buffer Ids
        Public mdl_VAO As Integer
        Public mBuffers() As Integer

        '------------------------------------------------
        'Storage
        Public Vertex_buffer() As Vector3
        Public Normal_buffer() As Vector4h
        Public UV1_buffer() As Vector2
        Public tangent_buffer() As Vector4h
        Public biNormal_buffer() As Vector4h
        Public UV2_buffer() As Vector2

        'list of indice sizes, offsets,
        'texture IDs.. render settings... so on
        Public render_sets As List(Of RenderSetEntry)

        Public Sub flush()
            'free the memory
            Vertex_buffer = Nothing
            Normal_buffer = Nothing
            UV1_buffer = Nothing
            tangent_buffer = Nothing
            biNormal_buffer = Nothing
            UV2_buffer = Nothing
        End Sub

    End Class

    Public Class BuffersStorage
        ' triangle buffers
        Public index_buffer16() As vect3_16
        Public index_buffer32() As vect3_32

        ' vertex storage
        Public vertexBuffer() As Vector3
        Public normalBuffer() As Vector4h
        Public uvBuffer() As Vector2
        Public tangentBuffer() As Vector4h
        Public binormalBuffer() As Vector4h
    End Class

    Public Class RenderSetEntry
        Public mdl_VAO As Integer

        Public verts_name As String
        Public prims_name As String

        '------------------------------------------------
        'used to create VBO
        'how many parallel buffers will be created
        Public element_count As Integer

        Public has_tangent As Boolean

        ' 2 or 4
        Public indexSize As Integer

        Public primitiveGroups As Dictionary(Of Integer, PrimitiveGroup)

        Public no_draw As Boolean
    End Class
#End Region


End Module

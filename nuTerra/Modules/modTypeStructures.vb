Imports System.Runtime.InteropServices
Imports OpenTK

Module modTypeStructures

#Region "MODEL_INDEX_LIST"

    Public MODEL_INDEX_LIST() As MODEL_INDEX_LIST_
    Public Structure MODEL_INDEX_LIST_ : Implements IComparable(Of MODEL_INDEX_LIST_)
        Public model_index As Integer
        Public matrix As Matrix4
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
        Public visibleCount As Integer
    End Class

#End Region

#Region "DECAL_INDEX_LIST"

    Public DECAL_INDEX_LIST() As DECAL_INDEX_LIST_
    Public Structure DECAL_INDEX_LIST_
        Public uv_wrapping As Vector2
        Public decal_data() As vertex_data_
        Public texture_id As Integer
        Public normal_id As Integer
        Public gmm_id As Integer
        Public display_id As Integer
        Public decal_texture As String
        Public decal_normal As String
        Public decal_gmm As String
        Public decal_extra As String
        Public matrix As Matrix4
        Public good As Boolean
        Public offsets As Vector4
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
        Public BB As Matrix3x2
        Public visible As Boolean
        Public cull_method As Integer
        Public is_parallax As Boolean
        Public is_wet As Boolean
    End Structure

#End Region


    <StructLayout(LayoutKind.Sequential)>
    Public Structure vect3_16
        Public x As UInt16
        Public y As UInt16
        Public z As UInt16
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure vect3_32
        Public x As UInt32
        Public y As UInt32
        Public z As UInt32
    End Structure


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
        Public primitive_name As String
        Public junk As Boolean

        'list of indice sizes, offsets,
        'texture IDs.. render settings... so on
        Public render_sets As List(Of RenderSetEntry)
    End Class

    <StructLayout(LayoutKind.Sequential)>
    Public Structure ModelVertex
        Public pos As Vector3
        Public normal As Vector4h
        Public tangent As Vector4h
        Public binormal As Vector4h
        Public uv As Vector2
    End Structure

    Public Class BuffersStorage
        Public index_buffer32() As vect3_32
        Public vertexBuffer() As ModelVertex
        Public uv2() As Vector2
    End Class

    Public Class RenderSetEntry
        Public buffers As New BuffersStorage

        Public verts_name As String
        Public prims_name As String

        '------------------------------------------------
        'used to create VBO
        'how many parallel buffers will be created
        Public element_count As Integer
        Public numVertices As Integer

        Public has_tangent As Boolean

        Public primitiveGroups As Dictionary(Of Integer, PrimitiveGroup)

        Public no_draw As Boolean
    End Class
#End Region


End Module

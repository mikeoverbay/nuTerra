Imports OpenTK.Graphics.OpenGL
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

    Public dest_buildings As destructibles
    Public Structure destructibles
        Public filename As List(Of String)
        Public matName As List(Of String)
    End Structure

    Dim VERTEX_VB As Integer = 1
    Dim NORMAL_VB As Integer = 2
    Dim UV1_VB As Integer = 3
    Dim TANGENT_VB As Integer = 4
    Dim BINORMAL_VB As Integer = 5
    Dim UV2_VB As Integer = 6
    Dim INDEX_BUFFER As Integer = 0

    Public Structure base_model_holder_
        Public element_count As Integer
        '1 = vertex
        '2 = vertex & normal
        '3 = vertex & normal & UV
        '4 = vertex & normal & UV and Tangent
        '5 = vertex & normal & UV and Tangent & UV2
        '------------------------------------------------
        'location render flags
        Public has_uv2 As Integer
        Public has_tangent As Integer

        Public is_building As Boolean ' used with decals
        Private index As Integer
        Public USHORTS As Boolean 'If true, indices are Uint16, other wise unit32

        Public BB() As vect3 'Used for frustrum culling
        Public culled As Boolean 'Draw if not true

        'used to create VBO
        Public vertex_count As Integer
        Public indice_count As Int32
        Public indice_size As Integer
        Public vertex_stride As UInt32
        'Ids
        Public mdl_VAO As Integer
        'Public VBO As Integer
        'Public IBO As Integer
        Public mBuffers() As Integer

        'Storage
        Public Vertex_buffer() As vect3
        Public Normal_buffer() As vect3
        Public UV1_buffer() As vec2
        Public tangent_buffer() As vect3
        Public biNormal_buffer() As vect3
        Public UV2_buffer() As vec2

        Public index_buffer16() As vect3_16
        Public index_buffer32() As vect3_32

        'list of indic sizes and offsets
        Public entries() As entries_

        'number if model components.
        Public primitive_count As Integer
        'buffer Ids

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
        'There are cases where some components have no UV2 stream.
        'We need this flag for building the VBO and signaling the shader.
        'We use a Integer because it can be passed directly to the shader.
        Public has_uv2 As Integer
        '------------------------------------
        'length and size of each primitive component
        Public numIndices As Int32
        Public UnumIndices As UInt32
        Public numVertices As UInt32
        Public startVertex As Int32
        Public startIndex As Int32
        '------------------------------------
        Public list_id As Integer
        '------------------------------------

        Public ShaderType As Integer
        'shader types
        '1 = color only
        '2 = color normal
        '3 = color normal gmm
        '4 = atlas
        '5 = atlas glass
        '------------------------------------
        'texture ids
        Public AM As Integer
        Public ANM As Integer
        Public GMM As Integer
        Public atlas_BLEND As Integer
        Public atlas_MAO As Integer
        Public atlas_GBMT As Integer
        Public atlas_AM As Integer
        Public dirtmap As Integer
        '------------------------------------
        'values
        Public alphaReference As Integer
        '------------------------------------
        'booleans
        Public doubleSided As Integer
        Public alphaEnable As Integer
        '------------------------------------

    End Structure

    Public Sub build_model_VAO(ByRef m As base_model_holder_)
        Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)
        'Gen VBO id
        GL.GenVertexArrays(1, m.mdl_VAO)
        'm.IBO = GL.GenBuffer

        'GL.BindVertexArray(m.mdl_VAO)
        GL.BindVertexArray(m.mdl_VAO)

        ReDim m.mBuffers(m.element_count)
        GL.GenBuffers(m.element_count + 1, m.mBuffers)


        Dim er0 = GL.GetError

        'vertex
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(VERTEX_VB))
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(0)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect3)), m.Vertex_buffer, BufferUsageHint.StaticDraw)

        'normal
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(NORMAL_VB))
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(1)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect3)), m.Normal_buffer, BufferUsageHint.StaticDraw)

        'UV1
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(UV1_VB))
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(2)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect2)), m.UV1_buffer, BufferUsageHint.StaticDraw)

        If m.has_tangent = 1 Then
            'Tangent
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(TANGENT_VB))
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(3)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect3)), m.tangent_buffer, BufferUsageHint.StaticDraw)

            'biNormal
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(BINORMAL_VB))
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(4)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect3)), m.biNormal_buffer, BufferUsageHint.StaticDraw)
        End If

        If m.has_uv2 = 1 Then
            'UV1
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(INDEX_BUFFER))
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(5)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length - 1) * SizeOf(GetType(vect2)), m.UV2_buffer, BufferUsageHint.StaticDraw)

        End If
        Dim er = GL.GetError

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.mBuffers(INDEX_BUFFER))
        If m.USHORTS Then
            GL.BufferData(BufferTarget.ElementArrayBuffer, m.indice_count * SizeOf(GetType(vect3_16)), m.index_buffer16, BufferUsageHint.StaticDraw)
        Else
            GL.BufferData(BufferTarget.ElementArrayBuffer, m.indice_count * SizeOf(GetType(vect3_32)), m.index_buffer32, BufferUsageHint.StaticDraw)
        End If

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        GL.BindVertexArray(m.mdl_VAO)

        'm.flush()



    End Sub

    Public Sub build_test_lists(ByRef m As base_model_holder_)
        For i = 0 To m.primitive_count - 1
            m.entries(i).list_id = GL.GenLists(1)
            GL.NewList(m.entries(i).list_id, ListMode.Compile)

            Dim p As vect3_32
            GL.Begin(PrimitiveType.Triangles)
            For k = m.entries(i).startIndex To (m.entries(i).numIndices / 3 - 1) + m.entries(i).startIndex
                If m.USHORTS Then
                    p.x = m.index_buffer16(k).x
                    p.y = m.index_buffer16(k).y
                    p.z = m.index_buffer16(k).z
                Else
                    p.x = m.index_buffer32(k).x
                    p.y = m.index_buffer32(k).y
                    p.z = m.index_buffer32(k).z
                End If
                '1
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.x).x, m.UV1_buffer(p.x).y)
                GL.Normal3(m.Normal_buffer(p.x).x, m.Normal_buffer(p.x).y, m.Normal_buffer(p.x).z)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.x).x, m.tangent_buffer(p.x).y, m.tangent_buffer(p.x).z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.x).x, m.biNormal_buffer(p.x).y, m.biNormal_buffer(p.x).z)
                GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.x).x, m.UV2_buffer(p.x).y)
                GL.Vertex3(m.Vertex_buffer(p.x).x, m.Vertex_buffer(p.x).y, m.Vertex_buffer(p.x).z)

                '1
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.y).x, m.UV1_buffer(p.y).y)
                GL.Normal3(m.Normal_buffer(p.y).x, m.Normal_buffer(p.y).y, m.Normal_buffer(p.y).z)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.y).x, m.tangent_buffer(p.y).y, m.tangent_buffer(p.y).z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.y).x, m.biNormal_buffer(p.y).y, m.biNormal_buffer(p.y).z)
                GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.y).x, m.UV2_buffer(p.y).y)
                GL.Vertex3(m.Vertex_buffer(p.y).x, m.Vertex_buffer(p.y).y, m.Vertex_buffer(p.y).z)

                '1
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.z).x, m.UV1_buffer(p.z).y)
                GL.Normal3(m.Normal_buffer(p.z).x, m.Normal_buffer(p.z).y, m.Normal_buffer(p.z).z)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.z).x, m.tangent_buffer(p.z).y, m.tangent_buffer(p.z).z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.z).x, m.biNormal_buffer(p.z).y, m.biNormal_buffer(p.z).z)
                GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.z).x, m.UV2_buffer(p.z).y)
                GL.Vertex3(m.Vertex_buffer(p.z).x, m.Vertex_buffer(p.z).y, m.Vertex_buffer(p.z).z)

            Next
            GL.End()
            GL.EndList()
        Next
    End Sub

End Module

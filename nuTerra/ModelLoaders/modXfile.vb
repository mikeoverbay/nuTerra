Imports System.IO
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modXfile
    Private Structure _vertex
        Public v As Vector3
        Public n As Vector3
        Public uv As Vector2
    End Structure

    Private Structure _indice
        Public a, b, c As UShort
    End Structure

    Public Class XModel
        Public vao As Integer
        Public indices_count As Integer
    End Class

    Public Function get_X_model(file_ As String) As XModel
        Dim vbuff() As _vertex
        Dim vertices() As Vector3
        Dim normals() As Vector3
        Dim uvs() As Vector2
        Dim indices() As _indice

        'reads single object directX ASCII file.
        'At some point this will load multi model files.
        '##################################################

        Dim start_locations(1) As UInteger
        Dim obj_count As Integer = get_start_locations(start_locations, file_)
        If obj_count < 0 Then
            Return Nothing
        End If
        Dim foutname = Path.GetFileNameWithoutExtension(file_)

        Dim s As New StreamReader(file_)
        Dim txt As String = ""
        While Not txt.ToLower.Contains("mesh")
            txt = s.ReadLine
        End While
        'get vertices ------------------------------------------
        txt = s.ReadLine ' this should be the number of vertices
        Dim brk = txt.Split(";")
        Dim vertice_count = CInt(brk(0))
        ReDim vertices(vertice_count - 1)
        For i = 0 To vertice_count - 1
            txt = s.ReadLine
            brk = txt.Split(";")
            vertices(i).X = CSng(brk(0))
            vertices(i).Y = CSng(brk(1))
            vertices(i).Z = CSng(brk(2))
        Next
        txt = s.ReadLine ' this should be a blank line
        txt = s.ReadLine ' this should be the indice count for the vertices
        brk = txt.Split(";")
        Dim indice_count As Int32 = CInt(brk(0))
        ReDim indices(indice_count - 1)

        Dim index_buffer16(indice_count - 1) As vect3_16

        For i = 0 To indice_count - 1
            txt = s.ReadLine
            brk = txt.Split(";")
            brk = brk(1).Split(",")
            indices(i).c = CUShort(brk(2)) ' flip winding
            indices(i).b = CUShort(brk(1))
            indices(i).a = CUShort(brk(0))
            index_buffer16(i).x = indices(i).c
            index_buffer16(i).y = indices(i).b
            index_buffer16(i).z = indices(i).a
        Next
        ' get normals----------------------------------------
        s.Close()
        s = New StreamReader(file_)
        While Not txt.ToLower.Contains("meshnormals")
            txt = s.ReadLine
        End While
        txt = s.ReadLine ' this should be the normal count
        brk = txt.Split(";")
        Dim normal_count As Int32
        normal_count = CInt(brk(0))
        ReDim normals(normal_count - 1)
        For i = 0 To normal_count - 1
            txt = s.ReadLine
            brk = txt.Split(";")
            normals(i).X = CSng(brk(0))
            normals(i).Y = CSng(brk(1))
            normals(i).Z = CSng(brk(2))
        Next
        s.Close()
        s = New StreamReader(file_)
        While Not txt.ToLower.Contains("meshtexturecoords")
            txt = s.ReadLine
        End While
        'get UVs -----------------------------------------------
        txt = s.ReadLine ' this should be the texture coordinate count
        brk = txt.Split(";")
        Dim txt_coord_cnt As Int32
        txt_coord_cnt = CInt(brk(0))
        ReDim uvs(txt_coord_cnt - 1)
        For i = 0 To txt_coord_cnt - 1
            txt = s.ReadLine
            brk = txt.Split(";")
            uvs(i).X = CSng(brk(0))
            uvs(i).Y = CSng(brk(1))
        Next

        'build vertex list for VBO
        ReDim vbuff(vertice_count - 1)

        For i = 0 To vertice_count - 1
            vbuff(i).v = vertices(i)
            vbuff(i).n = normals(i)
            vbuff(i).uv = uvs(i)
        Next

        Dim result As New XModel
        result.indices_count = index_buffer16.Length

        'Create VAO id
        GL.CreateVertexArrays(1, result.vao)

        Dim mBuffer As Integer
        GL.CreateBuffers(1, mBuffer)

        Dim vbuff_offset = New IntPtr(index_buffer16.Length * 6)
        GL.NamedBufferStorage(mBuffer, index_buffer16.Length * 6 + vbuff.Length * 32, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit)
        GL.NamedBufferSubData(mBuffer, IntPtr.Zero, index_buffer16.Length * 6, index_buffer16)
        GL.NamedBufferSubData(mBuffer, vbuff_offset, vbuff.Length * 32, vbuff)

        GL.VertexArrayVertexBuffer(result.vao, 0, mBuffer, vbuff_offset, 32)
        GL.VertexArrayAttribFormat(result.vao, 0, 3, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(result.vao, 0, 0)
        GL.EnableVertexArrayAttrib(result.vao, 0)

        GL.VertexArrayVertexBuffer(result.vao, 1, mBuffer, vbuff_offset, 32)
        GL.VertexArrayAttribFormat(result.vao, 1, 3, VertexAttribType.Float, False, 12)
        GL.VertexArrayAttribBinding(result.vao, 1, 1)
        GL.EnableVertexArrayAttrib(result.vao, 1)

        GL.VertexArrayVertexBuffer(result.vao, 2, mBuffer, vbuff_offset, 32)
        GL.VertexArrayAttribFormat(result.vao, 2, 2, VertexAttribType.Float, False, 24)
        GL.VertexArrayAttribBinding(result.vao, 2, 2)
        GL.EnableVertexArrayAttrib(result.vao, 2)

        GL.VertexArrayElementBuffer(result.vao, mBuffer)
        Return result
    End Function

    Private Function get_start_locations(ByRef loc() As UInteger, ByRef file_ As String)
        Dim m_count As Integer = 0
        Dim c_pos As UInteger = 0
        Dim txt As String = ""
        If File.Exists(file_) Then

            Dim s As New StreamReader(file_)
            While Not s.EndOfStream
                c_pos = s.BaseStream.Position
                txt = s.ReadLine
                If txt.ToLower.Contains("mesh ") Then
                    ReDim Preserve loc(m_count + 1)
                    loc(m_count) = c_pos
                    m_count += 1
                End If
            End While
            Return m_count
        End If
        Return -1
    End Function
End Module

﻿Imports System.IO
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module modXfile
    Public dial_face_ID As Integer
    Public VBO As Integer
    Public IBO As Integer

    Public vbuff() As _vertex
    Public vertices() As Vector3
    Public normals() As Vector3
    Public uvs() As Vector2
    Public indices() As _indice
    Public Structure model_
        Public componet() As componet_
    End Structure
    Public Structure componet_
        Public list_ID As Integer
        Public diffuse As String
        Public diffuse_2 As String
        Public normal As String
        Public diffuse_ID As Integer
        Public diffuse2_ID As Integer
        Public normal_ID As Integer
    End Structure
    Public Structure _vertex
        Public v As Vector3
        Public n As Vector3
        Public uv As Vector2
    End Structure

    Public Structure _indice
        Public a, b, c As UShort
    End Structure

    Public Function get_X_model(file_ As String, ByRef mdl As base_model_holder_) As Integer
        'reads single object directX ASCII file.
        'At some point this will load multi model files.
        '##################################################

        Dim start_locations(1) As UInteger
        Dim obj_count As Integer = get_start_locations(start_locations, file_)
        If obj_count < 0 Then
            Return -1
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
        Dim indice_count As Int32 = 0
        indice_count = CInt(brk(0))
        ReDim indices(indice_count - 1)

        Dim index_buffer16(indice_count - 1) As vect3_16

        For i = 0 To indice_count - 1
            indices(i) = New _indice
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
        'At this point, we have all the data to make the mesh
        Dim er = GL.GetError

        mdl.indice_count = indice_count

        'Gen VAO id
        GL.GenVertexArrays(1, mdl.mdl_VAO)
        GL.BindVertexArray(mdl.mdl_VAO)

        ReDim mdl.mBuffers(1)
        GL.GenBuffers(2, mdl.mBuffers)

        GL.BindBuffer(BufferTarget.ArrayBuffer, mdl.mBuffers(1))
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 32, 0)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 32, 12)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, 32, 24)
        GL.EnableVertexAttribArray(2)

        GL.BufferData(BufferTarget.ArrayBuffer, (vbuff.Length) * 32, vbuff, BufferUsageHint.StaticDraw)

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, mdl.mBuffers(0))
        GL.BufferData(BufferTarget.ElementArrayBuffer,
                      mdl.indice_count * 6,
                      index_buffer16,
                      BufferUsageHint.StaticDraw)

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)
        GL.BindVertexArray(0)

        'dl.flush()

        Dim er1 = GL.GetError

        Return 0
    End Function
    Public Function get_start_locations(ByRef loc() As UInteger, ByRef file_ As String)
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
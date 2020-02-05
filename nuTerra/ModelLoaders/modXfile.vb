Imports System.Math
Imports System
Imports System.Globalization
Imports System.Threading
Imports System.Windows
Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports System.IO
Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module modXfile
    Public dial_face_ID As Integer
    Public VBO As Integer
    Public IBO As Integer

    Public vbuff() As _vertex
    Public vertices() As vec3
    Public normals() As vec3
    Public uvs() As vec2
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
        Public v As vec3
        Public n As vec3
        Public uv As vec2
    End Structure

    Public Structure vec3
        Public x, y, z As Single
    End Structure
    Public Structure vec2
        Public x, y As Single
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
            vertices(i) = New vec3
            txt = s.ReadLine
            brk = txt.Split(";")
            vertices(i).x = CSng(brk(0))
            vertices(i).y = CSng(brk(1))
            vertices(i).z = CSng(brk(2))
        Next
        txt = s.ReadLine ' this should be a blank line
        txt = s.ReadLine ' this should be the indice count for the vertices
        brk = txt.Split(";")
        Dim indice_count As Int32 = 0
        indice_count = CInt(brk(0))
        ReDim indices(indice_count - 1)
        ReDim mdl.index_buffer16(indice_count - 1)

        For i = 0 To indice_count - 1
            indices(i) = New _indice
            txt = s.ReadLine
            brk = txt.Split(";")
            brk = brk(1).Split(",")
            indices(i).c = CUShort(brk(2)) ' flip winding
            indices(i).b = CUShort(brk(1))
            indices(i).a = CUShort(brk(0))
            mdl.index_buffer16(i).x = indices(i).c
            mdl.index_buffer16(i).y = indices(i).b
            mdl.index_buffer16(i).z = indices(i).a
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
            normals(i) = New vec3
            txt = s.ReadLine
            brk = txt.Split(";")
            normals(i).x = CSng(brk(0))
            normals(i).y = CSng(brk(1))
            normals(i).z = CSng(brk(2))
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
            uvs(i) = New vec2
            txt = s.ReadLine
            brk = txt.Split(";")
            uvs(i).x = CSng(brk(0))
            uvs(i).y = CSng(brk(1))
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

        'Gen VAO id
        ReDim mdl.mBuffers(2)
        GL.GenVertexArrays(1, mdl.mdl_VAO)
        GL.BindVertexArray(mdl.mdl_VAO)
        GL.GenBuffers(2, mdl.mBuffers)

        GL.BindBuffer(BufferTarget.ArrayBuffer, mdl.mBuffers(1))
        GL.BufferData(BufferTarget.ArrayBuffer, (vbuff.Length) * 32, vbuff, BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 32, 0)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 32, 12)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, 32, 24)

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, mdl.mBuffers(0))
        GL.BufferData(BufferTarget.ElementArrayBuffer, (mdl.indice_count) * 6, mdl.index_buffer16, BufferUsageHint.StaticDraw)

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        GL.BindVertexArray(0)

        mdl.indice_count = indice_count

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

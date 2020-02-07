Imports System
Imports System.Math
Imports System.IO
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK

Module PrimitiveLoader
    Public Structure section_info
        Public name As String
        Public location As UInt32
        Public size As UInt32
        Public has_color As Boolean
        Public has_uv2 As Boolean
    End Structure

    Structure primGroup
        Public startIndex_ As Long
        Public nPrimitives_ As Long
        Public startVertex_ As Long
        Public nVertices_ As Long
    End Structure
    Structure IndexHeader
        Public ind_h As String
        Public nIndices_ As UInt32
        Public nInd_groups As UShort
    End Structure
    Structure VerticesHeader
        Public header_text As String
        Public nVertice_count As UInt32
    End Structure

    Public fup_counter As Integer = 0

    Public Function get_primitive(ByRef filename As String, ByRef mdl() As base_model_holder_) As Boolean
        'Loads a model from the pcakages. It will attempt to locate it in all packages.

        filename = filename.Replace("\", "/") ' fix any path issues

        Dim thefile = filename.Replace("model", "visual_processed")

        'search everywhere!
        Dim entry As Ionic.Zip.ZipEntry = search_pkgs(filename)
        If entry Is Nothing Then
            'MsgBox("Can't find " + filename, MsgBoxStyle.Exclamation, "shit!")
            Return False
        End If

        Dim ms As New MemoryStream
        entry.Extract(ms)

        'Get the .model file in to TheXML_String
        'We have to get the nodeFullName entry because it might point to a different visual than the name.
        openXml_stream(ms, Path.GetFileNameWithoutExtension(thefile))
        Dim nodeFullName = get_full_visual_name() + ".visual_processed"
        entry = search_pkgs(nodeFullName)
        If entry IsNot Nothing Then
            ms = New MemoryStream
            entry.Extract(ms)
            openXml_stream(ms, Path.GetFileNameWithoutExtension(nodeFullName))
            ms.Dispose()
            filename = nodeFullName.Replace(".visual_processed", ".primitives_processed")

            Dim success = load_primitive(filename, mdl) '<-------- Load Model

            Return True
        End If

        MsgBox("Failed to find:" + filename, MsgBoxStyle.Exclamation, "Well shit!!")
        Return False
    End Function

    Private Function get_full_visual_name() As String
        'We load the model file and get the name of the visual from with in it.
        'We have to do this because some model files contain differently named visuals.
        'If it isn't done this way, we could render the wrong primitive!
        Dim tex1_pos = InStr(1, TheXML_String, "<nodefullVisual>") + "<nodefullVisual>".Length
        If Not tex1_pos = "<nodefullVisual>".Length Then
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</nodefullVisual>")
            Dim newS As String = ""
            Return Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
        Else
            tex1_pos = InStr(1, TheXML_String, "<nodelessVisual>") + "<nodelessVisual>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</nodelessVisual>")
            Dim newS As String = ""
            Return Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
        End If
    End Function

    Public Function load_primitive(ByRef filename As String, ByRef mdl() As base_model_holder_) As Boolean

        'see if this is a junk model we dont want
        If mdl(0).junk Then
            Return True
        End If

        Dim name = Path.GetFileNameWithoutExtension(filename)
        Dim fPath = Path.GetDirectoryName(filename) + "\"

        TOTAL_TRIANGLES_DRAWN_MODEL = 0


        Dim runner As UInt32 = 0
        Dim sub_groups As Integer = 0
        Dim number_of_groups As Integer = 0
        Dim last_ind_pos As UInt32 = 0
        Dim last_vert_pos As UInt32 = 0
        Dim last_uv_pos As UInt32 = 0
        Dim color_start As UInt32 = 0
        Dim sub_group_count As Integer = 0

        Dim na As String = ""

        Dim section(50) As section_info
        Dim pGroups(1) As primGroup

        Dim master_cnt As Integer = 0
        Dim object_start As Integer = 1

        Dim br As BinaryReader

        Dim ms As New MemoryStream
        '--------------------------------------
        'get primtive data
        Dim entry = search_pkgs(filename)

        If entry Is Nothing Then
            MsgBox("failed to load from pgk " + filename)
            Return False
        End If
        entry.Extract(ms)
        ms.Position = 0
        br = New BinaryReader(ms)
        'get table start position
        ms.Position = ms.Length - 4
        Dim table_start = br.ReadUInt32

        'point at start of table
        ms.Position = ms.Length - 4 - table_start

        Dim dummy As UInt32
        For i = 0 To 99
            If ms.Position < ms.Length - 4 Then
                section(i) = New section_info
                section(i).size = br.ReadUInt32
                If i > 0 Then
                    section(i).location = runner + 4
                End If
                runner += section(i).size
                Dim m = section(i).size Mod 4
                If m > 0 Then
                    runner += 4 - m

                End If
                'read 16 bytes of unused junk
                dummy = br.ReadUInt32
                dummy = br.ReadUInt32
                dummy = br.ReadUInt32
                dummy = br.ReadUInt32
                'get section names length
                Dim sec_name_len As UInt32 = br.ReadUInt32
                'get this sections name
                For read_at As UInteger = 1 To sec_name_len
                    na = na & br.ReadChar
                Next
                section(i).name = na
                If InStr(na, "vertices") > 0 Then
                    sub_groups += 1
                End If

                If InStr(na, "colour") > 0 Then
                    section(i).has_color = True
                End If

                If InStr(na, "uv2") > 0 Then
                    section(i).has_uv2 = True
                End If
                Dim l = na.Length Mod 4 'read off pad characters
                If l > 0 Then
                    br.ReadChars(4 - l)
                End If
                na = ""
            Else
                ReDim Preserve section(i)
                Exit For
            End If

        Next 'keep reading until we run out of file to read

        If sub_groups > 1 Then
            Stop
        End If

        Dim got_subs As Boolean = False
        Dim gp_pointer As Integer
        Dim cur_sub As Integer
        gp_pointer = sub_groups
        If sub_groups > 1 Then
            got_subs = True
            Stop
        End If
        Dim ind_start As UInt32 = 0
        Dim ind_length As UInt32 = 0
        Dim vert_start As UInt32 = 0
        Dim vert_length As UInt32 = 0
        'Fun Fact 1.. Only animated models have multiple groups with parts in each group.
        'Fun Fact 2.. Animated models have the winding order backwards of the vertices.
        'This code does not deal with reversing the winding order of aminated models yet.
        'All others are one group with all parts in that group.
        While sub_groups > 0

            cur_sub = gp_pointer - sub_groups
            'mdl(cur_sub) = New base_model_holder_
            mdl(cur_sub).has_uv2 = 0

            Dim uv2_data(0) As Byte
            Dim vertex_data(0) As Byte
            Dim cr As Byte
            Dim dr As Boolean = False

            sub_groups -= 1 ' take one off.. if there is one, this results in zero and collects only one model set

            Dim indi_scale As UInt32

            'If fup_counter = 14 Then Stop '<--------------------------------- break on

            'We have to loop and look at each entry because they are not always in the same order.
            For i = 0 To section.Length - 1
                If InStr(section(i).name, "indices") > 0 Then
                    If last_ind_pos < (section(i).location + 1) Then
                        ind_length = section(i).size - 76
                        ind_start = section(i).location + 4
                        ms.Position = ind_start
                        last_ind_pos = ind_start + 3 'Needed for when there are mulitple groups of components
                        'read text block
                        For z = 0 To 63
                            cr = br.ReadByte
                            If cr = 0 Then dr = True
                            If cr > 30 And cr <= 123 Then
                                If Not dr Then
                                    na = na & Chr(cr)
                                End If
                            End If
                        Next
                        'list = uint16 pointers. list32 = uint32 pointers
                        If na.Contains("list32") Then
                            indi_scale = 4 '32 bit pointers
                            mdl(cur_sub).USHORTS = False
                        Else
                            indi_scale = 2 ' 16 bit pointers
                            mdl(cur_sub).USHORTS = True
                        End If

                        Dim ih As IndexHeader
                        ih.nIndices_ = br.ReadUInt32
                        ih.nInd_groups = br.ReadUInt32
                        '-------------------------------------
                        ReDim pGroups(ih.nInd_groups - 1)

                        ReDim Preserve mdl(cur_sub).entries(ih.nInd_groups - 1)

                        'This probable needs to not be set here
                        'and use the count from the space.bin data if and when I get it figured out!
                        mdl(cur_sub).primitive_count = ih.nInd_groups '<----------- count setting
                        '-------------------------------------

                        Dim cp = ms.Position 'save position

                        Dim nOffset As UInteger = (ih.nIndices_ * indi_scale)
                        ms.Position += nOffset 'The component table is at the end of the indicies list.
                        'read the tables
                        For z = 0 To ih.nInd_groups - 1
                            pGroups(z).startIndex_ = br.ReadUInt32
                            pGroups(z).nPrimitives_ = br.ReadUInt32
                            pGroups(z).startVertex_ = br.ReadUInt32
                            pGroups(z).nVertices_ = br.ReadUInt32
                            'update triangle count
                            TOTAL_TRIANGLES_DRAWN_MODEL += pGroups(z).nPrimitives_
                        Next

                        ms.Position = cp 'restore position
                        'We flip the winding order because of directX to Opengl 
                        mdl(cur_sub).indice_count = (ih.nIndices_ / 3) - 1

                        If mdl(cur_sub).USHORTS Then
                            ReDim mdl(cur_sub).index_buffer16((ih.nIndices_ / 3) - 1)
                            For k = 0 To (ih.nIndices_ / 3) - 1
                                mdl(cur_sub).index_buffer16(k).y = br.ReadUInt16
                                mdl(cur_sub).index_buffer16(k).x = br.ReadUInt16
                                mdl(cur_sub).index_buffer16(k).z = br.ReadUInt16
                            Next
                        Else
                            ReDim mdl(cur_sub).index_buffer32((ih.nIndices_ - 1) / 3)
                            For k = 0 To (ih.nIndices_ / 3) - 1
                                mdl(cur_sub).index_buffer32(k).y = br.ReadUInt32
                                mdl(cur_sub).index_buffer32(k).x = br.ReadUInt32
                                mdl(cur_sub).index_buffer32(k).z = br.ReadUInt32
                            Next
                        End If
                        Exit For 'found it, stop looking
                    End If
                End If
            Next
            'need to find the start of the vertices section
            For i = 0 To section.Length - 1
                If InStr(section(i).name, "vertices") > 0 Then
                    If last_vert_pos < (section(i).location + 1) Then
                        vert_length = section(i).size
                        vert_start = section(i).location
                        last_vert_pos = vert_start + 3 'Needed for when there are mulitple groups of components
                        ms.Position = vert_start
                        ReDim vertex_data(vert_length)
                        vertex_data = br.ReadBytes(vert_length)
                        'File.WriteAllBytes("c:\!_bin_data\!_vertex_Data.bin", vertex_data)
                        Exit For 'found it, stop looking
                    End If
                End If
            Next

            Dim uv2_length As Int32
            Dim uv2_start As UInt32
            For i = 0 To section.Length - 1
                If InStr(section(i).name, "uv2") > 0 Then
                    mdl(cur_sub).has_uv2 = 1
                    If last_uv_pos < (section(i).location + 1) Then
                        uv2_length = section(i).size
                        uv2_start = section(i).location
                        last_uv_pos = uv2_start + 3 'Needed for when there are mulitple groups of components
                        ms.Position = uv2_start
                        ReDim uv2_data(uv2_length - 1)
                        uv2_data = br.ReadBytes(uv2_length)
                        'File.WriteAllBytes("c:\!_bin_data\!_uv2_Data.bin", uv2_data)
                        Exit For 'found it, stop looking
                    End If
                End If
            Next

            Dim vt_ms As New MemoryStream(vertex_data)
            Dim vt_br As New BinaryReader(vt_ms)

            Dim uv2_ms As New MemoryStream(uv2_data) ' not all primitives have UV2 blocks
            Dim uv2_br As New BinaryReader(uv2_ms)

            'this is old code that I wont remove until I'm sure it isn't needed!
            Dim lucky_72 As UInt32 = 72
            If Not got_subs Then
                If ind_start < vert_start Then
                    lucky_72 = 72
                    ind_start += 4
                    vert_start += 0
                Else
                    lucky_72 = 72
                    vert_start += 4
                End If
            Else
                If vert_start = 0 Then
                    vert_start += 4
                End If
            End If
            '-------------------------------------
            '-------------------------------------
            'Get vertex type header and total vertex count.
            Dim vh As VerticesHeader
            dr = False
            na = ""
            For i = 0 To 63
                cr = vt_br.ReadByte
                If cr = 0 Then dr = True
                If cr > 64 And cr <= 123 Then
                    If Not dr Then
                        na = na & Chr(cr)
                    End If
                End If
            Next
            vh.header_text = na
            '-------------------------------
            Dim BPVT_mode As Boolean = False
            Dim realNormals As Boolean = False
            Dim hasIdx As Boolean = False
            mdl(cur_sub).has_tangent = 0

            Dim stride As Integer = 0
            ' get stride and flags of each vertex element
            If vh.header_text = "xyznuv" Then
                stride = 32
                realNormals = True
                mdl(cur_sub).element_count = 4 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 0
            End If
            If vh.header_text = "BPVTxyznuv" Then
                BPVT_mode = True
                stride = 24
                realNormals = False
                mdl(cur_sub).element_count = 4 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 0
            End If
            If vh.header_text = "xyznuviiiwwtb" > 0 Then
                stride = 37
                mdl(cur_sub).element_count = 5 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 1
                hasIdx = True
            End If
            If vh.header_text = "BPVTxyznuviiiww" Then
                BPVT_mode = True
                stride = 32
                mdl(cur_sub).element_count = 4 + mdl(cur_sub).has_uv2
                hasIdx = True
            End If
            If vh.header_text = "BPVTxyznuviiiwwtb" Then
                BPVT_mode = True
                stride = 40
                mdl(cur_sub).element_count = 5 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 1
                hasIdx = True
            End If
            If vh.header_text = "xyznuvtb" Then
                stride = 32
                mdl(cur_sub).element_count = 5 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 1
            End If
            If vh.header_text = "BPVTxyznuvtb" Then
                BPVT_mode = True
                stride = 32
                mdl(cur_sub).element_count = 5 + mdl(cur_sub).has_uv2
                mdl(cur_sub).has_tangent = 1
            End If

            If BPVT_mode Then
                vt_br.BaseStream.Position = 132 'move to where count is located
            End If

            vh.nVertice_count = vt_br.ReadUInt32 ' read total count of vertcies

            'should be in same offset in both buffers.
            uv2_ms.Position = vt_ms.Position
            Dim v3 As Vector3
            '---------------------------
            ReDim mdl(0).Vertex_buffer(vh.nVertice_count - 1)
            ReDim mdl(0).Normal_buffer(vh.nVertice_count - 1)
            ReDim mdl(0).UV1_buffer(vh.nVertice_count - 1)
            ReDim mdl(0).tangent_buffer(vh.nVertice_count - 1)
            ReDim mdl(0).biNormal_buffer(vh.nVertice_count - 1)

            If mdl(cur_sub).has_uv2 Then
                ReDim mdl(0).UV2_buffer(vh.nVertice_count - 1)
            End If

            'mdl(cur_sub).indice_count = vh.nVertice_count' <--- WRONG.. They are NOT the same size!!!

            mdl(cur_sub).indice_size = indi_scale

            Dim running As Integer = 0 'Continuous pointer in to the buffers

            For i = 0 To mdl(cur_sub).primitive_count - 1
                'mdl(cur_sub).entries(i) = New entries_

                mdl(cur_sub).entries(i).startIndex = pGroups(i).startIndex_ / 3
                mdl(cur_sub).entries(i).numIndices = pGroups(i).nPrimitives_ * 3 'draw 3 verts per triangle
                mdl(cur_sub).entries(i).startVertex = pGroups(i).startVertex_
                mdl(cur_sub).entries(i).numVertices = pGroups(i).nVertices_

                For z = mdl(cur_sub).entries(i).startVertex To mdl(cur_sub).entries(i).startVertex + (mdl(cur_sub).entries(i).numVertices - 1)
                    '-----------------------------------------------------------------------
                    'We have to flip the sign of X on all vertex values because of DirectX to OpenGL
                    '
                    'uv2 if it exist

                    'If mdl(cur_sub).entries(i).identifier.Contains("d_") Then
                    '    mdl(cur_sub).entries(i).draw = False
                    'End If

                    If mdl(cur_sub).has_uv2 = 1 Then
                        mdl(cur_sub).UV2_buffer(running).X = uv2_br.ReadSingle
                        mdl(cur_sub).UV2_buffer(running).Y = uv2_br.ReadSingle
                    End If
                    '-----------------------------------------------------------------------
                    'vertex
                    mdl(cur_sub).Vertex_buffer(running).X = -vt_br.ReadSingle
                    mdl(cur_sub).Vertex_buffer(running).Y = vt_br.ReadSingle
                    mdl(cur_sub).Vertex_buffer(running).Z = vt_br.ReadSingle
                    '-----------------------------------------------------------------------
                    'normal
                    If realNormals Then
                        mdl(cur_sub).Normal_buffer(running).X = -vt_br.ReadSingle
                        mdl(cur_sub).Normal_buffer(running).Y = vt_br.ReadSingle
                        mdl(cur_sub).Normal_buffer(running).Z = vt_br.ReadSingle
                    Else
                        v3 = unpackNormal_8_8_8(vt_br.ReadUInt32)   ' unpack normals
                        mdl(cur_sub).Normal_buffer(running).X = -v3.x
                        mdl(cur_sub).Normal_buffer(running).Y = v3.y
                        mdl(cur_sub).Normal_buffer(running).Z = v3.z
                    End If

                    '-----------------------------------------------------------------------
                    'uv 1
                    mdl(cur_sub).UV1_buffer(running).X = vt_br.ReadSingle
                    mdl(cur_sub).UV1_buffer(running).Y = vt_br.ReadSingle
                    '-----------------------------------------------------------------------
                    'if this vertex has index junk, skip it.
                    'no tangent and bitangent on BPVTxyznuviiiww type vertex
                    'If hasIdx Then
                    '    vt_ms.Position += 8

                    'End If
                    If vh.header_text = "BPVTxyznuviiiww" Then
                        vt_ms.Position += 8

                    Else
                        If stride = 37 Or stride = 40 Then
                            vt_ms.Position += 8
                        End If
                    End If
                    '-----------------------------------------------------------------------

                    If mdl(cur_sub).has_tangent = 1 Then
                        'tangents
                        v3 = unpackNormal(vt_br.ReadUInt32)
                        mdl(cur_sub).tangent_buffer(running).X = -v3.x
                        mdl(cur_sub).tangent_buffer(running).Y = v3.y
                        mdl(cur_sub).tangent_buffer(running).Z = v3.z
                        'biNormals
                        v3 = unpackNormal(vt_br.ReadUInt32)
                        mdl(cur_sub).biNormal_buffer(running).X = -v3.x
                        mdl(cur_sub).biNormal_buffer(running).Y = v3.y
                        mdl(cur_sub).biNormal_buffer(running).Z = v3.z
                    End If
                    '-----------------------------------------------------------------------
                    running += 1
                Next
            Next
            Try

                build_model_VAO(mdl(cur_sub)) 'builds the VAO
            Catch ex As Exception

            End Try
            fup_counter += 1
            build_test_lists(mdl(cur_sub)) 'builds test display lists

        End While ' end of outside sub_groups loop
        mdl(cur_sub).POLY_COUNT = TOTAL_TRIANGLES_DRAWN_MODEL
        ms.Close()

        Return True

    End Function
    Private Function unpackNormal_8_8_8(ByVal packed As UInt32) As Vector3

        Dim pkz, pky, pkx As Int32
        pkx = CLng(packed) And &HFF Xor 127
        pky = CLng(packed >> 8) And &HFF Xor 127
        pkz = CLng(packed >> 16) And &HFF Xor 127

        Dim x As Single = (pkx)
        Dim y As Single = (pky)
        Dim z As Single = (pkz)

        Dim p As New Vector3
        If x > 127 Then
            x = -128 + (x - 128)
        End If

        If y > 127 Then
            y = -128 + (y - 128)
        End If
        If z > 127 Then
            z = -128 + (z - 128)
        End If
        p.x = CSng(x) / 127
        p.y = CSng(y) / 127
        p.z = CSng(z) / 127
        Dim len As Single = Sqrt((p.x ^ 2) + (p.y ^ 2) + (p.z ^ 2))

        'avoid division by 0
        If len = 0.0F Then len = 1.0F
        'len = 1.0
        'reduce to unit size
        p.x = -(p.x / len)
        p.y = -(p.y / len)
        p.z = -(p.z / len)
        Return p
    End Function

    Public Function unpackNormal(ByVal packed As UInt32) As Vector3
        Dim pkz, pky, pkx As Int32
        pkz = packed And &HFFC00000
        pky = packed And &H4FF800
        pkx = packed And &H7FF

        Dim z As Int32 = pkz >> 22
        Dim y As Int32 = (pky << 10L) >> 21
        Dim x As Int32 = (pkx << 21L) >> 21
        Dim p As New Vector3
        p.x = CSng(x) / 1023.0! '* -1.0!

        p.x = CSng(x) / 1023.0!
        p.y = CSng(y) / 1023.0!
        p.z = CSng(z) / 511.0!
        Dim len As Single = Sqrt((p.x ^ 2) + (p.y ^ 2) + (p.z ^ 2))

        'avoid division by 0
        If len = 0.0F Then len = 1.0F

        'reduce to unit size
        p.x = (p.x / len)
        p.y = (p.y / len)
        p.z = (p.z / len)
        Return p
    End Function

    Dim VERTEX_VB As Integer = 1
    Dim NORMAL_VB As Integer = 2
    Dim UV1_VB As Integer = 3
    Dim TANGENT_VB As Integer = 4
    Dim BINORMAL_VB As Integer = 5
    Dim UV2_VB As Integer = 6
    Dim INDEX_BUFFER As Integer = 0


    Public Sub build_model_VAO(ByRef m As base_model_holder_)
        Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)
        GL.Finish()

        'Gen VAO id
        GL.GenVertexArrays(1, m.mdl_VAO)
        'm.IBO = GL.GenBuffer

        GL.BindVertexArray(m.mdl_VAO)

        ReDim m.mBuffers(m.element_count)
        GL.GenBuffers(m.element_count + 1, m.mBuffers)


        Dim er0 = GL.GetError

        'vertex
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(VERTEX_VB))
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(0)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector3)), m.Vertex_buffer, BufferUsageHint.StaticDraw)

        'normal
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(NORMAL_VB))
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(1)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector3)), m.Normal_buffer, BufferUsageHint.StaticDraw)

        'UV1
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(UV1_VB))
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(2)
        GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector2)), m.UV1_buffer, BufferUsageHint.StaticDraw)

        If m.has_tangent = 1 Then
            'Tangent
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(TANGENT_VB))
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(3)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector3)), m.tangent_buffer, BufferUsageHint.StaticDraw)

            'biNormal
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(BINORMAL_VB))
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(4)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector3)), m.biNormal_buffer, BufferUsageHint.StaticDraw)
        End If

        If m.has_uv2 = 1 Then
            'UV1
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.mBuffers(UV2_VB))
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, False, 0, 0)
            GL.EnableVertexAttribArray(5)
            GL.BufferData(BufferTarget.ArrayBuffer, (m.Vertex_buffer.Length) * SizeOf(GetType(Vector2)), m.UV2_buffer, BufferUsageHint.StaticDraw)
        End If

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.mBuffers(INDEX_BUFFER))
        If m.USHORTS Then
            GL.BufferData(BufferTarget.ElementArrayBuffer, m.indice_count * SizeOf(GetType(vect3_16)), m.index_buffer16, BufferUsageHint.StaticDraw)
        Else
            GL.BufferData(BufferTarget.ElementArrayBuffer, m.indice_count * SizeOf(GetType(vect3_32)), m.index_buffer32, BufferUsageHint.StaticDraw)
        End If
        Dim er = GL.GetError

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0)
        GL.BindVertexArray(0)
        GL.Finish()
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
                GL.Normal3(m.Normal_buffer(p.x).X, m.Normal_buffer(p.x).Y, m.Normal_buffer(p.x).Z)
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.x).X, m.UV1_buffer(p.x).Y)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.x).X, m.tangent_buffer(p.x).Y, m.tangent_buffer(p.x).Z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.x).X, m.biNormal_buffer(p.x).Y, m.biNormal_buffer(p.x).Z)
                If m.has_uv2 = 1 Then
                    GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.x).X, m.UV2_buffer(p.x).Y)
                End If
                GL.Vertex3(m.Vertex_buffer(p.x).X, m.Vertex_buffer(p.x).Y, m.Vertex_buffer(p.x).Z)

                '1
                GL.Normal3(m.Normal_buffer(p.y).X, m.Normal_buffer(p.y).Y, m.Normal_buffer(p.y).Z)
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.y).X, m.UV1_buffer(p.y).Y)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.y).X, m.tangent_buffer(p.y).Y, m.tangent_buffer(p.y).Z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.y).X, m.biNormal_buffer(p.y).Y, m.biNormal_buffer(p.y).Z)
                If m.has_uv2 = 1 Then
                    GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.y).X, m.UV2_buffer(p.y).Y)
                End If
                GL.Vertex3(m.Vertex_buffer(p.y).X, m.Vertex_buffer(p.y).Y, m.Vertex_buffer(p.y).Z)

                '1
                GL.Normal3(m.Normal_buffer(p.z).X, m.Normal_buffer(p.z).Y, m.Normal_buffer(p.z).Z)
                GL.MultiTexCoord2(TextureUnit.Texture0, m.UV1_buffer(p.z).X, m.UV1_buffer(p.z).Y)
                GL.MultiTexCoord3(TextureUnit.Texture1, m.tangent_buffer(p.z).X, m.tangent_buffer(p.z).Y, m.tangent_buffer(p.z).Z)
                GL.MultiTexCoord3(TextureUnit.Texture2, m.biNormal_buffer(p.z).X, m.biNormal_buffer(p.z).Y, m.biNormal_buffer(p.z).Z)
                If m.has_uv2 = 1 Then
                    GL.MultiTexCoord2(TextureUnit.Texture3, m.UV2_buffer(p.z).X, m.UV2_buffer(p.z).Y)
                End If
                GL.Vertex3(m.Vertex_buffer(p.z).X, m.Vertex_buffer(p.z).Y, m.Vertex_buffer(p.z).Z)

            Next
            GL.End()
            GL.EndList()
        Next
    End Sub

End Module

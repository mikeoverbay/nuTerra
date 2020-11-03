#Region "imports"
Imports System.Globalization
Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows
Imports OpenTK.Graphics
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
Imports FastColoredTextBoxNS
Imports System.Text.RegularExpressions
#End Region


Module modPirmitiveLoader
#Region "variables"
    Public Model_Loaded As Boolean
    Public bounding_box_size As Single
    Public sections(10) As sections_
    Public x_max = -10000.0F
    Public x_min = 10000.0F
    Public y_max = -10000.0F
    Public y_min = 10000.0F
    Public z_max = -10000.0F
    Public z_min = 10000.0F
    Dim indi_scale As Integer

    Private Structure _vertex
        Public v As Vector3
        Public n As Vector3
        Public uv As Vector2
    End Structure
    Public Structure sections_
        Public v_name As String
        Public i_name As String
        Public locations() As UInteger
        Public sizes() As UInt32
        Public v_data() As Byte
        Public i_data() As Byte
        Public uv2_data() As Byte
        Public has_uv2 As Boolean
    End Structure

    Public section_names(50) As String
    Dim section_sizes(50) As UInt32
    Dim section_locations(50) As UInt32
    Dim section_data(50) As data_
    Dim sub_group_data(50) As sub_group_data_
    Public Structure sub_group_data_
        Public has_uv2 As Boolean
        Public uv2_data() As Byte
    End Structure
    Public Structure data_
        Public data() As Byte
    End Structure
    Public Structure names_
        Public names() As String
    End Structure
    Public Structure sizes_
        Public sizes() As UInt32
    End Structure
    Public Structure locations_
        Public locations() As UInt32
    End Structure
    Dim pGroups(1) As primGroup
    Structure primGroup
        Public startIndex_ As Long
        Public nPrimitives_ As Long
        Public startVertex_ As Long
        Public nVertices_ As Long
    End Structure
    Dim f_name_vertices, f_name_indices, f_name_uv2, f_name_color, bsp_materials_name, bsp_name As String
    Dim has_uv2 As Boolean
    Dim ih As IndexHeader
    Dim vh As VerticesHeader
    Structure IndexHeader
        Public ind_h As String
        Public nIndices_ As UInt32
        Public nInd_groups As UShort
    End Structure
    Structure VerticesHeader
        Public header_text As String
        Public nVertice_count As UInt32
    End Structure

    Public object_cnt As Integer
    Public _object(1) As obj_
    Public Structure obj_
        Dim model As XModel
        Public indis() As vect3_32
        Public verts() As Vector3
        Public norms() As Vector3
        Public uvs() As Vector2
        Public uv2s() As uvs
        Public has_uv2 As Boolean
        Public Cnt As Integer
        Public vBuffer As Integer
        Public hiden As Boolean
    End Structure
    Public Structure uvs
        Public u, v As Single
    End Structure
    Public Structure indi
        Public p1, p2, p3 As Integer
    End Structure
    Dim object_start As Integer
    Dim big_l As Integer
#End Region
    Public Sub Update_view_screen()
        frmModelViewer.draw_model_view()
    End Sub
    Public Sub loadmodel(ByVal ms As MemoryStream, ByVal file_name As String)
        x_max = -10000
        x_min = 10000
        y_max = -10000
        y_min = 10000
        z_max = -10000
        z_min = 10000
        If Model_Loaded Then
            Model_Loaded = False
            For i = 0 To object_cnt
                If _object(i).model IsNot Nothing Then
                    GL.DeleteBuffer(_object(i).model.vao)
                End If
            Next
        End If
        frmModelViewer.SplitContainer1.Panel1.Controls.Clear()
        GC.Collect()
        Application.DoEvents()
        ms.Position = ms.Length - 4
        Dim rd As New BinaryReader(ms)
        Dim e_offset = rd.ReadUInt32
        ms.Position = ms.Length - e_offset - 4
        Dim file_len As UInteger = ms.Length
        Dim location As ULong = 4 ' we start with offset of 4
        Dim na As String = ""
        Dim entry_count As Integer
        ReDim Preserve section_sizes(30)
        ReDim Preserve section_locations(30)
        ReDim Preserve section_names(30)
        ReDim Preserve section_data(30)
        ReDim sub_group_data(30)
        ReDim sections(30)
        '========================================================
        'get table at the end of the primitives file
        For i = 0 To 99
            If ms.Position < file_len - 4 Then
                section_sizes(i) = rd.ReadUInt32 'get chunk size
                ReDim section_data(i).data(section_sizes(i)) 'allocate for data
                section_locations(i) = location 'save location
                location += section_sizes(i)
                location += location Mod 4
                'read 16 bytes of unused junk
                Dim dummy = rd.ReadUInt32
                dummy = rd.ReadUInt32
                dummy = rd.ReadUInt32
                dummy = rd.ReadUInt32

                'get this sections name
                Dim sec_name_len As UInt32 = rd.ReadUInt32
                For read_at As UInteger = 1 To sec_name_len
                    na = na & rd.ReadChar
                Next
                section_names(i) = na.Trim
                Dim l = na.Length Mod 4 'read off pad characters
                If l > 0 Then
                    rd.ReadChars(4 - l)
                End If
                na = ""
            Else
                ReDim Preserve section_sizes(i - 1)
                ReDim Preserve section_locations(i - 1)
                ReDim Preserve section_names(i - 1)
                ReDim Preserve section_data(i - 1)
                entry_count = i
                Exit For
            End If
        Next
        '========================================================
        '========================================================
        Dim uv2_data(1) As Byte
        Dim sub_groups As Integer = 0
        Dim section_id As Integer = 0
        Dim id As Integer = 0
        Dim loop_count As Integer = 0
        Dim rc As Integer = 0
        For i = 0 To entry_count - 1
            f_name_vertices = "zz"
            f_name_indices = "zz"
            f_name_uv2 = "zz"
            If InStr(section_names(i), "indices") > 0 Then
                'Debug.WriteLine("indices")
                rc += 1
                f_name_indices = section_names(i).Trim
            End If
            If InStr(section_names(i), "vertices") > 0 Then
                'Debug.WriteLine("vertices")
                f_name_vertices = section_names(i).Trim
                rc += 1
            End If
            If InStr(section_names(i), "uv2") > 0 Then
                'Debug.WriteLine("uv2")
                f_name_uv2 = section_names(i).Trim
                has_uv2 = True
            End If
            If InStr(section_names(i), "colour") > 0 Then
                Debug.WriteLine("colour")
                f_name_color = section_names(i).Trim
            End If
            id = sub_groups
            If f_name_vertices = section_names(i) Then
                sections(id).v_name = f_name_vertices
                ms.Position = section_locations(i)
                ReDim sections(id).v_data(section_sizes(i))
                sections(id).v_data = rd.ReadBytes(section_sizes(i))
            End If
            If f_name_indices = section_names(i) Then
                sections(id).i_name = f_name_indices
                ms.Position = section_locations(i)
                ReDim sections(id).i_data(section_sizes(i))
                sections(id).i_data = rd.ReadBytes(section_sizes(i))
            End If
            If f_name_uv2 = section_names(i) Then
                sub_group_data(sub_groups - 1) = New sub_group_data_
                sub_group_data(sub_groups - 1).has_uv2 = True
                ms.Position = section_locations(i)
                ReDim sub_group_data(sub_groups - 1).uv2_data(section_sizes(i))
                sub_group_data(sub_groups - 1).uv2_data = rd.ReadBytes(section_sizes(i))
            End If

            If rc = 2 Then
                rc = 0
                sub_groups += 1
                ReDim Preserve sub_group_data(sub_groups)
            End If
        Next
        ReDim Preserve sections(sub_groups)
        Dim pk As Long = rd.BaseStream.Position

        Dim uv2ms As MemoryStream
        Dim uv2_data_reader As BinaryReader

        For gCnt = 0 To sub_groups - 1
            Dim Ims As New MemoryStream(sections(gCnt).i_data)
            Dim Vms As New MemoryStream(sections((gCnt)).v_data)
            Dim Ird As New BinaryReader(Ims)
            Dim Vrd As New BinaryReader(Vms)

            f_name_indices = sections(gCnt).i_name
            f_name_vertices = sections(gCnt).v_name
            If sub_group_data(gCnt).has_uv2 Then
                has_uv2 = True
                uv2ms = New MemoryStream(sub_group_data(gCnt).uv2_data)
                uv2_data_reader = New BinaryReader(uv2ms)
            Else
                has_uv2 = False
            End If
            Dim cr As Byte
            Dim dr As Boolean = False
            For i = 0 To 63
                cr = Ird.ReadByte
                If cr = 0 Then dr = True
                If cr > 30 And cr <= 123 Then
                    If Not dr Then
                        na = na & Chr(cr)

                    End If
                End If
            Next
            Dim r_count As UInt32 = 0
            ih.ind_h = na
            indi_scale = 2
            If InStr(na, "list32") > 0 Then
                indi_scale = 4
            End If
            na = ""
            Try 'sanity check
                ih.nIndices_ = Ird.ReadUInt32
                ih.nInd_groups = Ird.ReadUInt32
            Catch ex As Exception
                MsgBox("data in " + file_name + " is unreadable!", MsgBoxStyle.Exclamation, "Error!")
                Return
            End Try
            dr = False
            ReDim pGroups(ih.nInd_groups)
            Dim nOffset As UInteger = (ih.nIndices_ * indi_scale) + 72
            Ird.BaseStream.Position = nOffset
            'Get the groups.. IE. get addresses, offsets and counts for the parts in the model
            Try

                For i = 0 To ih.nInd_groups - 1
                    pGroups(i).startIndex_ = Ird.ReadUInt32
                    pGroups(i).nPrimitives_ = Ird.ReadUInt32
                    pGroups(i).startVertex_ = Ird.ReadUInt32
                    pGroups(i).nVertices_ = Ird.ReadUInt32

                Next
            Catch ex As Exception

            End Try
            Vrd.BaseStream.Position = 0
            For i = 0 To 63
                cr = Vrd.ReadByte
                If cr = 0 Then dr = True
                If cr > 64 And cr <= 123 Then
                    If Not dr Then
                        na = na & Chr(cr)

                    End If
                End If
            Next
            vh.header_text = na
            na = ""
            Dim BPVT_mode As Boolean = False
            Dim realNormals As Boolean = False
            Dim stride As Integer
            If vh.header_text = "xyznuv" Then
                stride = 32
                realNormals = True
            End If
            If vh.header_text = "BPVTxyznuv" Then
                BPVT_mode = True
                stride = 24
                realNormals = False
            End If
            If InStr(vh.header_text, "xyznuviiiwwtb") > 0 Then
                stride = 37
            End If
            If InStr(vh.header_text, "BPVTxyznuviiiwwtb") > 0 Then
                BPVT_mode = True
                stride = 40
            End If
            If InStr(vh.header_text, "xyznuvtb") > 0 Then
                stride = 32
            End If
            If InStr(vh.header_text, "BPVTxyznuvtb") > 0 Then
                BPVT_mode = True
                stride = 32
            End If
            If BPVT_mode Then
                Vrd.BaseStream.Position = 132
            End If
            vh.nVertice_count = Vrd.ReadUInt32
            object_start = gCnt
            big_l = ih.nInd_groups 'get object count


            For k As UInt32 = object_start To ((ih.nInd_groups - 1) + gCnt)
                If pGroups(k - object_start).nPrimitives_ = 0 Then
                    'Exit For
                End If
                ReDim Preserve _object(k + 1)
                '==========================
                'Add checkbox for each primitive
                Dim cb As New CheckBox
                cb.AutoSize = True
                cb.TextAlign = ContentAlignment.MiddleCenter
                cb.Checked = True
                cb.Text = "Primitive " + k.ToString("00") + " ■ Poly Cnt: " + pGroups(k - object_start).nPrimitives_.ToString("00000")
                Dim h = cb.Height
                cb.ForeColor = Color.Wheat
                cb.BackColor = Color.Transparent
                AddHandler cb.CheckedChanged, AddressOf Update_view_screen
                frmModelViewer.SplitContainer1.Panel1.Controls.Add(cb)
                cb.Location = New Point(3, (k * (h - 3)) + 5)
                Dim pos = pGroups(k - object_start).nVertices_
                Ird.BaseStream.Seek(pGroups(k - object_start).startIndex_ * indi_scale + 72, SeekOrigin.Begin)
                Vrd.BaseStream.Position = pGroups(k - object_start).startVertex_ * stride + 136

                object_cnt = k

                _object(k) = New obj_
                ReDim _object(k).indis(pGroups(k - object_start).nPrimitives_)
                ReDim _object(k).verts(pos)
                ReDim _object(k).norms(pos)
                ReDim _object(k).uvs(pos)
                If has_uv2 Then
                    _object(k).has_uv2 = True
                    ReDim _object(k).uv2s(pos)
                Else
                    _object(k).has_uv2 = False
                    ReDim _object(k).uv2s(0)
                End If
                If BPVT_mode Then
                    'Vrd.BaseStream.Position = 132
                End If
                Dim indi_offset As UInteger = 0
                indi_offset = pGroups(k - object_start).startVertex_
                For cnt = 0 To pos - 1
                    With _object(k)

                        .verts(cnt) = New Vector3
                        .norms(cnt) = New Vector3
                        If has_uv2 Then
                            .uv2s(cnt) = New uvs
                        End If

                        .verts(cnt).X = Vrd.ReadSingle
                        .verts(cnt).Y = Vrd.ReadSingle
                        .verts(cnt).Z = Vrd.ReadSingle
                        check_Bounds(.verts(cnt))
                        If realNormals Then
                            .norms(cnt).X = Vrd.ReadSingle
                            .norms(cnt).Y = Vrd.ReadSingle
                            .norms(cnt).Z = Vrd.ReadSingle
                        Else
                            Dim n = Vrd.ReadUInt32
                            Dim v As Vector3
                            If BPVT_mode Then
                                v = unpackNormal_8_8_8(n)   ' unpack normals
                            Else
                                v = unpackNormal(n)   ' unpack normals
                            End If
                            .norms(cnt).X = v.X
                            .norms(cnt).Y = v.Y
                            .norms(cnt).Z = v.Z

                        End If
                        .uvs(cnt).X = Vrd.ReadSingle
                        .uvs(cnt).Y = Vrd.ReadSingle
                        If has_uv2 Then
                            .uv2s(cnt).u = uv2_data_reader.ReadSingle
                            .uv2s(cnt).v = uv2_data_reader.ReadSingle
                        End If
                        If stride = 37 Or stride = 40 Then
                            Vrd.ReadByte() 'indexes
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadByte()
                            Vrd.ReadUInt32() 't
                            Vrd.ReadUInt32() 'tbn
                        Else
                            If Not realNormals And Not stride = 24 Then
                                'these dont exist in XYZNUV format vertex
                                Vrd.ReadUInt32() 't
                                Vrd.ReadUInt32() 'tbn
                            End If
                        End If

                    End With
                Next
                pos = pGroups(k - object_start).nPrimitives_ - 1
                Ird.BaseStream.Position = pGroups(k - object_start).startIndex_ * indi_scale + 72
                ReDim _object(k).indis(pos)
                For cnt = 0 To pos
                    With _object(k)

                        If indi_scale = 2 Then
                            .indis(cnt).x = Ird.ReadUInt16
                            .indis(cnt).y = Ird.ReadUInt16
                            .indis(cnt).z = Ird.ReadUInt16
                            .indis(cnt).x -= indi_offset
                            .indis(cnt).y -= indi_offset
                            .indis(cnt).z -= indi_offset
                        Else
                            .indis(cnt).x = Ird.ReadUInt32
                            .indis(cnt).y = Ird.ReadUInt32
                            .indis(cnt).z = Ird.ReadUInt32
                            .indis(cnt).x -= indi_offset
                            .indis(cnt).y -= indi_offset
                            .indis(cnt).z -= indi_offset
                        End If
                    End With
                Next
                Try
                    make_VBO(k)
                Catch ex As Exception
                    LogThis("VAO ERROR loading primitive!" + vbCrLf + ex.Message)
                End Try
            Next

        Next
        bounding_box_size = (-x_min + x_max + -y_min + y_max + -z_min + z_max) / 3.0!
        GC.Collect()
        ms.Dispose()
        Model_Loaded = True
        Return
    End Sub
    Private Sub make_VBO(ByVal id As Integer)

        'build vertex list for VBO
        Dim vbuff() As _vertex
        ReDim vbuff(_object(id).verts.Length - 1)
        'pack data
        For i = 0 To _object(id).verts.Length - 1
            vbuff(i).v = _object(id).verts(i)
            vbuff(i).n = _object(id).norms(i)
            vbuff(i).uv = _object(id).uvs(i)
        Next

        Dim result As New XModel
        result.indices_count = _object(id).indis.Length

        'Create VAO id
        GL.CreateVertexArrays(1, result.vao)

        Dim mBuffer As Integer
        GL.CreateBuffers(1, mBuffer)

        Dim vbuff_offset = New IntPtr(_object(id).indis.Length * 12)
        GL.NamedBufferStorage(mBuffer, _object(id).indis.Length * 12, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit)
        GL.NamedBufferSubData(mBuffer, IntPtr.Zero, _object(id).indis.Length * 12, _object(id).indis)
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

        _object(id).model = result
    End Sub
    Private Sub check_Bounds(ByVal v As vector3)
        If v.x > x_max Then x_max = v.x
        If v.y > y_max Then y_max = v.y
        If v.z > z_max Then z_max = v.z

        If v.x < x_min Then x_min = v.x
        If v.y < y_min Then y_min = v.y
        If v.z < z_min Then z_min = v.z
    End Sub
    Private Function unpackNormal_8_8_8(ByVal packed As UInt32) As vector3
        'Console.WriteLine(packed.ToString("x"))
        Dim pkz, pky, pkx As Int32
        'Dim sample As Byte
        pkx = CLng(packed) And &HFF Xor 127
        'sample = packed And &HFF
        pky = CLng(packed >> 8) And &HFF Xor 127
        pkz = CLng(packed >> 16) And &HFF Xor 127

        Dim x As Single = (pkx)
        Dim y As Single = (pky)
        Dim z As Single = (pkz)

        Dim p As New vector3
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
    Public Function unpackNormal(ByVal packed As UInt32)
        Dim pkz, pky, pkx As Int32
        pkz = packed And &HFFC00000
        pky = packed And &H4FF800
        pkx = packed And &H7FF

        Dim z As Int32 = pkz >> 22
        Dim y As Int32 = (pky << 10L) >> 21
        Dim x As Int32 = (pkx << 21L) >> 21
        Dim p As New vector3
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

End Module

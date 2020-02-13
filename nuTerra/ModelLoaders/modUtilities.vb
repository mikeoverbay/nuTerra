Imports System.IO
Imports System.Runtime.CompilerServices

Module modUtilities
    Private Sub reconstitute_buffers(ByRef m As base_model_holder_, p_groups() As primGroup_)

        Dim removed_indices As Integer
        Dim removed_from_buffer As Integer
        Dim Rsize = 0
        Dim changed As Boolean = False
        For t = 0 To m.primitive_count - 1

            If m.entries(t).draw Then

                changed = True

                If t = m.primitive_count - 1 Then
                    'we cant do the next one so we are done
                    Rsize += p_groups(t).nVertices_
                    m.primitive_count -= 1
                    removed_indices += p_groups(t).nPrimitives_ * 3
                    Exit For
                Else
                    'we need to shift the indice pointers and remove the block of vertices.
                    Dim read_at = p_groups(t + 1).startVertex_
                    Dim write_at = p_groups(t).startVertex_
                    Dim t_end = (m.Vertex_buffer.Length - 1)
                    removed_from_buffer += p_groups(t).nVertices_ ' for final redim of buffers.
                    For i = read_at To t_end
                        m.Vertex_buffer(write_at) = m.Vertex_buffer(read_at)
                        m.Normal_buffer(write_at) = m.Normal_buffer(read_at)
                        m.UV1_buffer(write_at) = m.UV1_buffer(read_at)
                        If m.has_uv2 Then
                            m.UV2_buffer(write_at) = m.UV2_buffer(read_at)
                        End If
                        If m.has_tangent Then
                            m.tangent_buffer(write_at) = m.tangent_buffer(read_at)
                            m.biNormal_buffer(write_at) = m.biNormal_buffer(read_at)
                        End If
                        write_at += 1
                    Next
                    'now we off set all affected indices
                    write_at = p_groups(t).startIndex_
                    If m.USHORTS Then
                        t_end = m.index_buffer16.Length - 1
                        For i = write_at To t_end - p_groups(t).nPrimitives_
                            m.index_buffer16(i).x = m.index_buffer16(i + p_groups(t).nPrimitives_).x - p_groups(t).nVertices_
                            m.index_buffer16(i).y = m.index_buffer16(i + p_groups(t).nPrimitives_).y - p_groups(t).nVertices_
                            m.index_buffer16(i).z = m.index_buffer16(i + p_groups(t).nPrimitives_).z - p_groups(t).nVertices_
                        Next
                    Else
                        t_end = m.index_buffer32.Length - 1
                        For i = write_at To t_end - p_groups(t).nPrimitives_
                            m.index_buffer32(i).x = m.index_buffer32(i + p_groups(t).nPrimitives_).x - p_groups(t).nVertices_
                            m.index_buffer32(i).y = m.index_buffer32(i + p_groups(t).nPrimitives_).y - p_groups(t).nVertices_
                            m.index_buffer32(i).z = m.index_buffer32(i + p_groups(t).nPrimitives_).z - p_groups(t).nVertices_
                        Next
                    End If
                    Rsize += p_groups(t).nVertices_
                    m.indice_count -= p_groups(t).nPrimitives_
                    removed_indices += p_groups(t).nPrimitives_ * 3
                    m.primitive_count -= 1
                End If
            End If

        Next
        '============================================
        'resize only if data was removed
        If changed Then
            Rsize = m.Vertex_buffer.Length - Rsize
            ReDim Preserve m.Vertex_buffer(Rsize)
            ReDim Preserve m.Normal_buffer(Rsize)
            ReDim Preserve m.UV1_buffer(Rsize)
            If m.has_uv2 Then
                ReDim Preserve m.UV2_buffer(Rsize)
            End If
            If m.has_tangent Then
                ReDim Preserve m.tangent_buffer(Rsize)
                ReDim Preserve m.biNormal_buffer(Rsize)
            End If
            If m.USHORTS Then
                ReDim Preserve m.index_buffer16(m.index_buffer16.Length - removed_indices)
            Else
                ReDim Preserve m.index_buffer16(m.index_buffer32.Length - removed_indices)
            End If
        End If

    End Sub

    Public Sub ShowText(ByVal text As String)
        frmShowText.Show()
        frmShowText.FastColoredTextBox1.Text = text
    End Sub

    Public Sub ShowTextAppend(ByVal text As String)
        frmShowText.Show()
        frmShowText.FastColoredTextBox1.Text =
            frmShowText.FastColoredTextBox1.Text + vbCrLf +
            text
    End Sub

    Public Sub LogThis(ByVal entry As String)
        'Writes to the log and immediately saves it.
        nuTerra_LOG.AppendLine(entry)
        File.WriteAllText(TEMP_STORAGE + "nuTerra_Log.txt", nuTerra_LOG.ToString)
    End Sub

    ' Allows us to split by strings. Not just characters.
    <Extension()> _
    Public Function Split(ByVal input As String, _
                          ByVal ParamArray delimiter As String()) As String()
        Return input.Split(delimiter, StringSplitOptions.None)
        Dim a(0) As String
        a(0) = input
        Return a
    End Function

    Public Sub load_destructibles()
        Dim script_pkg As Ionic.Zip.ZipFile = Nothing
        Dim ms As New MemoryStream
        Try
            script_pkg = Ionic.Zip.ZipFile.Read(GAME_PATH & "scripts.pkg")
            Dim script As Ionic.Zip.ZipEntry = script_pkg("scripts\destructibles.xml")
            script.Extract(ms)
            ms.Position = 0
            Dim bdata As Byte()
            Dim br As BinaryReader = New BinaryReader(ms)
            bdata = br.ReadBytes(br.BaseStream.Length)
            Dim des As MemoryStream = New MemoryStream(bdata, 0, bdata.Length)
            des.Write(bdata, 0, bdata.Length)
            openXml_stream(des, "destructibles.xml")
            des.Close()
            des.Dispose()
        Catch ex As Exception
            MsgBox("Something failed loading the scripts\destructibles.xml file", MsgBoxStyle.Exclamation, "Dammit!")
        End Try
        script_pkg.Dispose()
        ms.Dispose()
        Dim entry, mName As DataTable
        entry = xmldataset.Tables("entry")
        mName = xmldataset.Tables("matName")
        Dim q = From fname_ In entry.AsEnumerable Join mat In mName On _
                fname_.Field(Of Int32)("entry_ID") Equals mat.Field(Of Int32)("entry_ID") _
                              Select _
                  filename = fname_.Field(Of String)("filename"), _
                  mat = mat.Field(Of String)("matName_Text")

        DESTRUCTABLE_DATA_TABLE = New DataTable("destructibles")
        DESTRUCTABLE_DATA_TABLE.Columns.Add("filename", System.Type.GetType("System.String"))
        DESTRUCTABLE_DATA_TABLE.Columns.Add("mat_name", System.Type.GetType("System.String"))

        For Each it In q
            If it.mat IsNot Nothing Then
                Dim row = DESTRUCTABLE_DATA_TABLE.NewRow
                row(0) = it.filename.Replace("model", "visual").ToLower
                row(1) = it.mat.Replace("model", "visual").ToLower
                DESTRUCTABLE_DATA_TABLE.Rows.Add(row)
            End If
        Next
        '---------------------------------------
    End Sub

    Public Function can_this_be_broken(ByVal s As String) As Boolean

        Dim q = From d In DESTRUCTABLE_DATA_TABLE.AsEnumerable
                Where d.Field(Of String)("filename") = s
                Select dname = d.Field(Of String)("mat_name")

        If q.Count > 0 Then
            Return False 'we do not want to ignore this one.
        End If
        Return True ' not on list so probably dont want to draw it?
    End Function
End Module

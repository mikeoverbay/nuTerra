Imports System.IO
Imports System.Text

Module modSpaceBin
    Public sectionHeaders As Dictionary(Of String, SectionHeader)
    Public Structure SectionHeader
        Public magic As String
        Public version As Int32
        Public offset As Int64
        Public length As Int64

        Public Sub New(br As BinaryReader)
            magic = br.ReadChars(4)
            version = br.ReadInt32
            offset = br.ReadInt64
            length = br.ReadInt64
        End Sub
    End Structure

    Private Sub ShowDecodeFailedMessage(ex As Exception, magic As String)
        Debug.Print(ex.ToString)
        MsgBox(String.Format("{0} decode Failed", magic), MsgBoxStyle.Exclamation, "Oh NO!!")
    End Sub

    Public Function ReadSpaceBinData(p As String) As Boolean
        If Not File.Exists(TEMP_STORAGE + p) Then
            GoTo Failed
        End If

        Dim f = File.OpenRead(TEMP_STORAGE + p)

        Using br As New BinaryReader(f, Encoding.ASCII)
            br.BaseStream.Position = &H14
            Dim table_size = br.ReadInt32

            sectionHeaders = New Dictionary(Of String, SectionHeader)

            ' read each entry in the header table
            For i = 0 To table_size - 1
                Dim header As New SectionHeader(br)
                sectionHeaders.Add(header.magic, header)
            Next

            Try
                ' we must grab this data first!
                cBSGD = New cBSGD_(sectionHeaders("BSGD"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSGD")
                GoTo Failed
            End Try

            '------------------------------------------------------------------
            ' Now we will grab the game data we need.
            '------------------------------------------------------------------

            Try
                cBWST = New cBWST_(sectionHeaders("BWST"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWST")
                GoTo Failed
            End Try

            Try
                cBWAL = New cBWAL_(sectionHeaders("BWAL"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWAL")
                GoTo Failed
            End Try

            Try
                get_BWSG(sectionHeaders("BWSG"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWSG")
                GoTo Failed
            End Try

            Try
                cBSMI = New cBSMI_(sectionHeaders("BSMI"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMI")
                GoTo Failed
            End Try

            Try
                cBSMO = New cBSMO_(sectionHeaders("BSMO"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMO")
                GoTo Failed
            End Try

            Try
                get_BSMA(sectionHeaders("BSMA"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BSMA")
                GoTo Failed
            End Try

            Try
                cSpTr = New cSpTr_(sectionHeaders("SpTr"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "SpTr")
                GoTo Failed
            End Try

            Try
                get_WGSD(sectionHeaders("WGSD"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "WGSD")
                GoTo Failed
            End Try

            Try
                cBWWa = New cBWWa_(sectionHeaders("BWWa"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "BWWa")
                GoTo Failed
            End Try

            Try
                cWTbl = New cWTbl_(sectionHeaders("WTbl"), br)
            Catch ex As Exception
                ShowDecodeFailedMessage(ex, "WTbl")
                GoTo Failed
            End Try

            'Unimplemented sections:
            'BWCS
            'BWS2
            'BSG2
            'BWT2
            'WTCP
            'BWEP
            'WGCO
            'BWPs
            'CENT
            'UDOS
            'WGDE
            'BWLC
            'WTau
            'WGSH
            'WGMM
        End Using

        f.Close()

        '----------------------------------------------------------------------------------
        'build the model information
        ReDim MAP_MODELS(cBSMO.models_colliders.count - 1)
        For k = 0 To cBSMO.models_colliders.count - 1
            With MAP_MODELS(k)
                .mdl = New base_model_holder_
                .mdl.primitive_name = cBSMO.models_colliders.data(k).primitive_name

                If .mdl.primitive_name Is Nothing Then
                    .mdl.junk = True
                    Continue For
                End If

                .visibilitiBounds = cBSMO.models_visibility_bounds.data(k)

                Dim lod0_offset = cBSMO.models_loddings.data(k).lod_begin

                Dim lod0_render_set_begin = cBSMO.lod_renders.data(lod0_offset).render_set_begin
                Dim lod0_render_set_end = cBSMO.lod_renders.data(lod0_offset).render_set_end

                Dim num_render_sets = lod0_render_set_end - lod0_render_set_begin + 1

                ReDim .mdl.render_sets(num_render_sets - 1)

                For z As UInteger = 0 To num_render_sets - 1
                    .mdl.render_sets(z) = New RenderSetEntry
                    '.mdl.render_sets(z).identifier = cBSMA.MaterialItem(z + shader_prop_start).identifier

                    .mdl.render_sets(z).verts_name = cBWST.find_str(cBSMO.renders.data(z).verts_name_fnv)
                    .mdl.render_sets(z).prims_name = cBWST.find_str(cBSMO.renders.data(z).prims_name_fnv)

                    '.mdl.render_sets(z).FX_shader = cBSMA.FXStringKey(cBSMA.MaterialItem(z + shader_prop_start).effectIndex).FX_string
                    'Dim l_cnt = cBSMA.MaterialItem(z + shader_prop_start).shaderPropEnd - cBSMA.MaterialItem(z + shader_prop_start).shaderPropBegin
                Next
            End With
        Next

        ReDim MODEL_INDEX_LIST(cBSMI.model_BSMO_indexes.count - 1)
        Dim cnt As Integer = 0

        For k = 0 To cBSMI.model_BSMO_indexes.count - 1
            Dim bsmo_id = cBSMI.model_BSMO_indexes.data(k).BSMO_MODEL_INDEX
            MODEL_INDEX_LIST(k).model_index = bsmo_id
            MODEL_INDEX_LIST(k).matrix = cBSMI.transforms.data(k)

            'Flip some row values to convert from DirectX to Opengl
            MODEL_INDEX_LIST(k).matrix.M12 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M13 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M21 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M31 *= -1.0
            MODEL_INDEX_LIST(k).matrix.M41 *= -1.0
        Next

        ReadSpaceBinData = True
        GoTo CleanUp

Failed:
        ReadSpaceBinData = False

CleanUp:
        'Clear headers
        sectionHeaders = Nothing

        'Clear Sections
        cBSGD = Nothing
        cBWST = Nothing
        cBWSG = Nothing
        cBSMI = Nothing
        cWTbl = Nothing
        cBSMO = Nothing
        cBSMA = Nothing
        cBWAL = Nothing
        cWGSD = Nothing
        cSpTr = Nothing
        cBWWa = Nothing

        '====================================================
        ' Sort and batch the models for instanced drawing
        '====================================================
        Array.Sort(MODEL_INDEX_LIST) 'sort our list by model_index


        MODEL_BATCH_LIST = New List(Of ModelBatch)

        Dim tmpDict As New Dictionary(Of Integer, Integer)

        For i = 0 To MODEL_INDEX_LIST.Length - 2
            Dim id = MODEL_INDEX_LIST(i).model_index
            If tmpDict.ContainsKey(id) Then
                tmpDict(id) += 1
            Else
                tmpDict(id) = 1
            End If
        Next

        Dim offset As Integer = 0
        For Each it In tmpDict
            Dim batch As New ModelBatch With {
                .model_id = it.Key,
                .count = it.Value,
                .offset = offset
            }
            MODEL_BATCH_LIST.Add(batch)
            offset += it.Value
        Next

        'Stop
    End Function
End Module

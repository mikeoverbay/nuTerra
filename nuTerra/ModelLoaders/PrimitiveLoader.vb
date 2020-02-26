Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module PrimitiveLoader
    Public Class BinarySectionInfo
        Public location As UInt32
        Public size As UInt32
    End Class

    Class PrimitiveGroup
        Public startIndex As Integer
        Public nPrimitives As Integer
        Public startVertex As Integer
        Public nVertices As Integer

        Public fx As String
        Public props As Dictionary(Of String, Object)
    End Class

    Public Function get_primitive(ByRef mdl As base_model_holder_) As Boolean
        If mdl.junk Then
            Return True
        End If

        Dim filename = mdl.render_sets(0).prims_name.Replace(".primitives", ".primitives_processed")
        filename = filename.Substring(0, filename.LastIndexOf("/"c)) ' remove "/indices" at the end

        ' search everywhere!

        Dim entry As Ionic.Zip.ZipEntry = search_pkgs(filename)
        If entry Is Nothing Then
            MsgBox("Can't find " + filename, MsgBoxStyle.Exclamation, "shit!")
            Return False
        End If

        Dim ms As New MemoryStream
        entry.Extract(ms)

        load_primitive(ms, mdl)
        Return True
    End Function

    Public Sub load_primitive(ms As MemoryStream,
                              ByRef mdl As base_model_holder_)
        ms.Position = 0
        Dim br As New BinaryReader(ms, System.Text.Encoding.ASCII)

        'get table start position
        br.BaseStream.Position = br.BaseStream.Length - 4
        Dim table_start = br.ReadUInt32

        'point at start of table
        br.BaseStream.Position = br.BaseStream.Length - 4 - table_start

        Dim binSectionOffset = 4
        Dim binSections As New Dictionary(Of String, BinarySectionInfo)

        While br.BaseStream.Position < br.BaseStream.Length - 4
            Dim section As New BinarySectionInfo With {
                .size = br.ReadUInt32,
                .location = binSectionOffset
            }

            binSectionOffset += section.size

            ' Make binary section offset align
            If section.size Mod 4 > 0 Then
                binSectionOffset += 4 - section.size Mod 4
            End If

            ' Skip 16 bytes of unused junk
            br.BaseStream.Position += 16

            ' Get section names length
            Dim sec_name_len As UInt32 = br.ReadUInt32

            ' Get sections name
            Dim sec_name = br.ReadChars(sec_name_len)
            ' Skip pad characters
            Dim l = sec_name_len Mod 4
            If l > 0 Then
                br.BaseStream.Position += 4 - l
            End If

            binSections(sec_name) = section
        End While


        For Each renderSet In mdl.render_sets
            Dim vertsSectionName = renderSet.verts_name.Substring(renderSet.verts_name.LastIndexOf("/"c) + 1)
            Dim primsSectionName = renderSet.prims_name.Substring(renderSet.prims_name.LastIndexOf("/"c) + 1)


            Dim buffers As New BuffersStorage
            load_primitives_indices(br, renderSet, binSections(primsSectionName), buffers)
            load_primitives_vertices(br, renderSet, binSections(vertsSectionName), buffers)

            build_renderset_VAO(renderSet, buffers)
        Next
    End Sub

    Public Sub load_primitives_indices(br As BinaryReader,
                                       ByRef renderSet As RenderSetEntry,
                                       ByRef sectionInfo As BinarySectionInfo,
                                       ByRef buffers As BuffersStorage)
        br.BaseStream.Position = sectionInfo.location

        ' "list" = UInt16 pointers
        ' "list32" = UInt32 pointers

        Dim triTypeName As New String(br.ReadChars(64))
        triTypeName = triTypeName.Substring(0, triTypeName.IndexOf(vbNullChar))

        If triTypeName = "list32" Then
            renderSet.indexSize = 4
        Else
            Debug.Assert(triTypeName = "list")
            renderSet.indexSize = 2
        End If

        Dim numIndices = br.ReadUInt32
        Dim numPrimGroups = br.ReadUInt32

        'renderSet.primitiveGroups = New Dictionary(Of Integer, PrimitiveGroup)

        ' save current stream position
        Dim savedPos = br.BaseStream.Position

        ' The component table is at the end of the indicies list.
        br.BaseStream.Position += numIndices * renderSet.indexSize

        ' read the tables
        For z = 0 To numPrimGroups - 1
            If Not renderSet.primitiveGroups.ContainsKey(z) Then
                renderSet.primitiveGroups(z) = New PrimitiveGroup
            End If
            With renderSet.primitiveGroups(z)
                .startIndex = br.ReadInt32
                .nPrimitives = br.ReadInt32
                .startVertex = br.ReadInt32
                .nVertices = br.ReadInt32
            End With
            'renderSet.primitiveGroups.Add(pGroup)
            TOTAL_TRIANGLES_DRAWN_MODEL += renderSet.primitiveGroups(z).nPrimitives
        Next

            ' restore position
            br.BaseStream.Position = savedPos


            'We flip the winding order because of directX to Opengl 
            If renderSet.indexSize = 2 Then
                ReDim buffers.index_buffer16((numIndices / 3) - 1)
                For k = 0 To buffers.index_buffer16.Length - 1
                    buffers.index_buffer16(k).y = br.ReadUInt16
                    buffers.index_buffer16(k).x = br.ReadUInt16
                    buffers.index_buffer16(k).z = br.ReadUInt16
                Next
            Else
                ReDim buffers.index_buffer32((numIndices / 3) - 1)
                For k = 0 To buffers.index_buffer32.Length - 1
                    buffers.index_buffer32(k).y = br.ReadUInt32
                    buffers.index_buffer32(k).x = br.ReadUInt32
                    buffers.index_buffer32(k).z = br.ReadUInt32
                Next
            End If
    End Sub


    Public Sub load_primitives_vertices(br As BinaryReader,
                                        ByRef renderSet As RenderSetEntry,
                                        ByRef sectionInfo As BinarySectionInfo,
                                        ByRef buffers As BuffersStorage)
        br.BaseStream.Position = sectionInfo.location

        Dim vertTypeName As New String(br.ReadChars(64))
        vertTypeName = vertTypeName.Substring(0, vertTypeName.IndexOf(vbNullChar))

        '-------------------------------
        Dim BPVT_mode As Boolean = False
        Dim realNormals As Boolean = False
        Dim hasIdx As Boolean = False
        Dim stride As Integer = 0
        renderSet.has_tangent = False

        ' get stride and flags of each vertex element
        Select Case vertTypeName
            Case "xyznuv"
                stride = 32
                realNormals = True
                renderSet.element_count = 4
                renderSet.has_tangent = False

            Case "BPVTxyznuv"
                BPVT_mode = True
                stride = 24
                realNormals = False
                renderSet.element_count = 4
                renderSet.has_tangent = False

            Case "xyznuviiiwwtb"
                stride = 37
                renderSet.element_count = 5
                renderSet.has_tangent = True
                hasIdx = True

            Case "BPVTxyznuviiiww"
                BPVT_mode = True
                stride = 32
                renderSet.element_count = 4
                hasIdx = True

            Case "BPVTxyznuviiiwwtb"
                BPVT_mode = True
                stride = 40
                renderSet.element_count = 5
                renderSet.has_tangent = True
                hasIdx = True

            Case "xyznuvtb"
                stride = 32
                renderSet.element_count = 5
                renderSet.has_tangent = True

            Case "BPVTxyznuvtb"
                BPVT_mode = True
                stride = 32
                renderSet.element_count = 5
                renderSet.has_tangent = True

            Case Else
                Debug.Assert(False)

        End Select

        If BPVT_mode Then
            br.BaseStream.Position += 68 ' move to where count is located
        End If


        Dim numVertices = br.ReadUInt32 ' read total count of vertcies
        Debug.Assert(numVertices > 2)

        ' should be in same offset in both buffers.
        '---------------------------
        ReDim buffers.vertexBuffer(numVertices - 1)
        ReDim buffers.normalBuffer(numVertices - 1)
        ReDim buffers.uvBuffer(numVertices - 1)
        If renderSet.has_tangent Then
            ReDim buffers.tangentBuffer(numVertices - 1)
            ReDim buffers.binormalBuffer(numVertices - 1)
        End If

        Dim running As Integer = 0 'Continuous accumulator pointer in to the buffers

        For Each primGroup In renderSet.primitiveGroups.Values
            For z = primGroup.startVertex To primGroup.startVertex + primGroup.nVertices - 1
                '-----------------------------------------------------------------------
                'We have to flip the sign of X on all vertex values because of DirectX to OpenGL

                '-----------------------------------------------------------------------
                'vertex
                With buffers.vertexBuffer(running)
                    .X = -br.ReadSingle
                    .Y = br.ReadSingle
                    .Z = br.ReadSingle
                End With

                '-----------------------------------------------------------------------
                'normal
                With buffers.normalBuffer(running)
                    If realNormals Then
                        .X = -br.ReadSingle
                        .Y = br.ReadSingle
                        .Z = br.ReadSingle
                    Else
                        Dim v3 = unpackNormal_8_8_8(br.ReadUInt32) ' unpack normals
                        .X = -v3.X
                        .Y = v3.Y
                        .Z = v3.Z
                    End If
                End With

                '-----------------------------------------------------------------------
                'uv 1
                With buffers.uvBuffer(running)
                    .X = br.ReadSingle
                    .Y = br.ReadSingle
                End With

                '-----------------------------------------------------------------------
                'if this vertex has index junk, skip it.
                'no tangent and bitangent on BPVTxyznuviiiww type vertex
                If hasIdx Then
                    br.BaseStream.Position += 8
                End If

                If renderSet.has_tangent Then
                    'tangents
                    Dim v3 = unpackNormal_8_8_8(br.ReadUInt32)
                    With buffers.tangentBuffer(running)
                        .X = -v3.X
                        .Y = v3.Y
                        .Z = v3.Z
                    End With

                    'binormals
                    v3 = unpackNormal_8_8_8(br.ReadUInt32)
                    With buffers.binormalBuffer(running)
                        .X = -v3.X
                        .Y = v3.Y
                        .Z = v3.Z
                    End With
                End If

                running += 1
            Next
        Next


    End Sub

    Public Function unpackNormal_8_8_8(packed As UInt32) As Vector3
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
        p.X = CSng(x) / 127
        p.Y = CSng(y) / 127
        p.Z = CSng(z) / 127
        Dim len As Single = Sqrt((p.X ^ 2) + (p.Y ^ 2) + (p.Z ^ 2))

        'avoid division by 0
        If len = 0.0F Then len = 1.0F
        'len = 1.0
        'reduce to unit size
        p.X = -(p.X / len)
        p.Y = -(p.Y / len)
        p.Z = -(p.Z / len)
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
        p.X = CSng(x) / 1023.0!
        p.Y = CSng(y) / 1023.0!
        p.Z = CSng(z) / 511.0!
        Dim len As Single = Sqrt((p.X ^ 2) + (p.Y ^ 2) + (p.Z ^ 2))

        'avoid division by 0
        If len = 0.0F Then len = 1.0F

        'reduce to unit size (normalize)
        p.X = (p.X / len)
        p.Y = (p.Y / len)
        p.Z = (p.Z / len)
        Return p
    End Function

    Dim INDEX_BUFFER As Integer = 0
    Dim VERTEX_VB As Integer = 1
    Dim NORMAL_VB As Integer = 2
    Dim UV1_VB As Integer = 3
    Dim TANGENT_VB As Integer = 4
    Dim BINORMAL_VB As Integer = 5


    Public Sub build_renderset_VAO(ByRef renderSet As RenderSetEntry,
                                   ByRef buffers As BuffersStorage)
        ' Dim max_vertex_elements = GL.GetInteger(GetPName.MaxElementsVertices)

        ' Gen VAO id
        GL.GenVertexArrays(1, renderSet.mdl_VAO)
        GL.BindVertexArray(renderSet.mdl_VAO)

        Dim mBuffers(renderSet.element_count) As Integer
        GL.GenBuffers(renderSet.element_count + 1, mBuffers)

        Dim v3_size = SizeOf(GetType(Vector3))
        Dim v2_size = SizeOf(GetType(Vector2))

        Dim er0 = GL.GetError

        'vertex
        GL.BindBuffer(BufferTarget.ArrayBuffer, mBuffers(VERTEX_VB))
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(0)
        GL.BufferData(BufferTarget.ArrayBuffer,
                      buffers.vertexBuffer.Length * v3_size,
                      buffers.vertexBuffer,
                      BufferUsageHint.StaticDraw)

        'normal
        GL.BindBuffer(BufferTarget.ArrayBuffer, mBuffers(NORMAL_VB))
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.HalfFloat, False, 0, 0)
        GL.EnableVertexAttribArray(1)
        GL.BufferData(BufferTarget.ArrayBuffer,
                      buffers.normalBuffer.Length * SizeOf(GetType(Vector4h)),
                      buffers.normalBuffer,
                      BufferUsageHint.StaticDraw)

        'UV1
        GL.BindBuffer(BufferTarget.ArrayBuffer, mBuffers(UV1_VB))
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, False, 0, 0)
        GL.EnableVertexAttribArray(2)
        GL.BufferData(BufferTarget.ArrayBuffer,
                      buffers.uvBuffer.Length * v2_size,
                      buffers.uvBuffer,
                      BufferUsageHint.StaticDraw)

        If renderSet.has_tangent Then
            'Tangent
            GL.BindBuffer(BufferTarget.ArrayBuffer, mBuffers(TANGENT_VB))
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.HalfFloat, False, 0, 0)
            GL.EnableVertexAttribArray(3)
            GL.BufferData(BufferTarget.ArrayBuffer,
                          buffers.tangentBuffer.Length * SizeOf(GetType(Vector4h)),
                          buffers.tangentBuffer,
                          BufferUsageHint.StaticDraw)

            'Binormal
            GL.BindBuffer(BufferTarget.ArrayBuffer, mBuffers(BINORMAL_VB))
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.HalfFloat, False, 0, 0)
            GL.EnableVertexAttribArray(4)
            GL.BufferData(BufferTarget.ArrayBuffer,
                          buffers.binormalBuffer.Length * SizeOf(GetType(Vector4h)),
                          buffers.binormalBuffer,
                          BufferUsageHint.StaticDraw)
        End If

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, mBuffers(INDEX_BUFFER))
        If renderSet.indexSize = 2 Then
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                          buffers.index_buffer16.Length * SizeOf(GetType(vect3_16)),
                          buffers.index_buffer16,
                          BufferUsageHint.StaticDraw)
        Else
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                          buffers.index_buffer32.Length * SizeOf(GetType(vect3_32)),
                          buffers.index_buffer32,
                          BufferUsageHint.StaticDraw)
        End If

        GL.BindVertexArray(0)
    End Sub

End Module

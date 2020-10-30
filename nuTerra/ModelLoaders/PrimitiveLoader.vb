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

    Public Enum ShaderTypes
        FX_PBS_ext = 1
        FX_PBS_ext_dual = 2
        FX_PBS_ext_detail = 3
        FX_PBS_tiled_atlas = 4
        FX_PBS_tiled_atlas_global = 5
        FX_PBS_glass = 6
        FX_PBS_ext_repaint = 7
        FX_lightonly_alpha = 8
        FX_unsupported = 9
    End Enum

    Structure MaterialProps_PBS_ext
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_useNormalPackDXT1 As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
    End Structure

    Structure MaterialProps_PBS_ext_dual
        Public diffuseMap As String
        Public diffuseMap2 As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_useNormalPackDXT1 As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
    End Structure

    Structure MaterialProps_PBS_ext_detail
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_detailMap As String
        Public g_useNormalPackDXT1 As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
    End Structure

    Structure MaterialProps_PBS_tiled_atlas
        Public atlasAlbedoHeight As String
        Public atlasBlend As String
        Public atlasNormalGlossSpec As String
        Public atlasMetallicAO As String
        Public dirtMap As String
        Public globalTex As String
        Public dirtParams As Vector4
        Public dirtColor As Vector4
        Public g_atlasSizes As Vector4
        Public g_atlasIndexes As Vector4
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_tileUVScale As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
    End Structure

    Structure MaterialProps_PBS_atlas_global
        Public atlasAlbedoHeight As String
        Public atlasBlend As String
        Public atlasNormalGlossSpec As String
        Public atlasMetallicAO As String
        Public dirtMap As String
        Public globalTex As String
        Public dirtParams As Vector4
        Public dirtColor As Vector4
        Public g_atlasSizes As Vector4
        Public g_atlasIndexes As Vector4
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_tileUVScale As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
    End Structure

    Structure MaterialProps_PBS_glass
        Public dirtAlbedoMap As String
        Public normalMap As String
        Public glassMap As String
        Public alphaTestEnable As Boolean
        Public alphaReference As Integer
        Public texAddressMode As UInteger
        Public g_filterColor As Vector4
    End Structure

    Structure MaterialProps_PBS_ext_repaint
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public g_enableAO As Boolean
        Public alphaTestEnable As Boolean
        Public alphaReference As Integer
        Public g_baseColor As Vector4
        Public g_repaintColor As Vector4
    End Structure

    Structure MaterialProps_lightonly_alpha
        Public diffuseMap As String
    End Structure

    Structure Material
        Public id As UInt32
        Public shader_type As ShaderTypes
        Public props As Object
    End Structure

    Class PrimitiveGroup
        Public startIndex As Integer
        Public nPrimitives As Integer
        Public startVertex As Integer
        Public nVertices As Integer
        Public material_id As Integer

        Public no_draw As Boolean
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
            load_primitives_indices(br, renderSet, binSections(primsSectionName))
            load_primitives_vertices(br, renderSet, binSections(vertsSectionName))
            Dim uv2SectionName = If(vertsSectionName.Contains("."), vertsSectionName.Split(".")(0) + ".uv2", "uv2")
            If binSections.ContainsKey(uv2SectionName) Then
                load_primitives_uv2(br, renderSet, binSections(uv2SectionName))
            End If
        Next
    End Sub

    Public Sub load_primitives_indices(br As BinaryReader,
                                       ByRef renderSet As RenderSetEntry,
                                       ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        ' "list" = UInt16 pointers
        ' "list32" = UInt32 pointers

        Dim triTypeName As New String(br.ReadChars(64))
        triTypeName = triTypeName.Substring(0, triTypeName.IndexOf(vbNullChar))

        Dim indexSize = If(triTypeName = "list32", 4, 2)

        Dim numIndices = br.ReadUInt32
        Dim numPrimGroups = br.ReadUInt32

        ' save current stream position
        Dim savedPos = br.BaseStream.Position

        ' The component table is at the end of the indicies list.
        br.BaseStream.Position += numIndices * indexSize

        ' read the tables
        For z = 0 To numPrimGroups - 1
            If Not renderSet.primitiveGroups.ContainsKey(z) Then
                renderSet.primitiveGroups(z) = New PrimitiveGroup
                renderSet.primitiveGroups(z).no_draw = True
            End If
            With renderSet.primitiveGroups(z)
                .startIndex = br.ReadInt32
                .nPrimitives = br.ReadInt32
                .startVertex = br.ReadInt32
                .nVertices = br.ReadInt32
            End With
        Next

        ' restore position
        br.BaseStream.Position = savedPos

        'We flip the winding order because of directX to Opengl 
        ReDim renderSet.buffers.index_buffer32((numIndices / 3) - 1)
        If indexSize = 2 Then
            For k = 0 To renderSet.buffers.index_buffer32.Length - 1
                With renderSet.buffers.index_buffer32(k)
                    .y = br.ReadUInt16
                    .x = br.ReadUInt16
                    .z = br.ReadUInt16
                End With
            Next
        Else
            For k = 0 To renderSet.buffers.index_buffer32.Length - 1
                With renderSet.buffers.index_buffer32(k)
                    .y = br.ReadUInt32
                    .x = br.ReadUInt32
                    .z = br.ReadUInt32
                End With
            Next
        End If
    End Sub


    Public Sub load_primitives_vertices(br As BinaryReader,
                                        ByRef renderSet As RenderSetEntry,
                                        ByRef sectionInfo As BinarySectionInfo)
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


        renderSet.numVertices = br.ReadUInt32 ' read total count of vertcies
        Debug.Assert(renderSet.numVertices > 2)

        ' should be in same offset in both buffers.
        '---------------------------
        ReDim renderSet.buffers.vertexBuffer(renderSet.numVertices - 1)

        Dim running As Integer = 0 'Continuous accumulator pointer in to the buffers

        For Each primGroup In renderSet.primitiveGroups.Values
            For z = primGroup.startVertex To primGroup.startVertex + primGroup.nVertices - 1
                '-----------------------------------------------------------------------
                'We have to flip the sign of X on all vertex values because of DirectX to OpenGL

                '-----------------------------------------------------------------------
                'vertex
                With renderSet.buffers.vertexBuffer(running)
                    .pos.X = -br.ReadSingle
                    .pos.Y = br.ReadSingle
                    .pos.Z = br.ReadSingle
                    If realNormals Then
                        .normal.X = -br.ReadSingle
                        .normal.Y = br.ReadSingle
                        .normal.Z = br.ReadSingle
                    Else
                        Dim v3 = unpackNormal_8_8_8(br.ReadUInt32) ' unpack normals
                        .normal.X = -v3.X
                        .normal.Y = v3.Y
                        .normal.Z = v3.Z
                    End If
                    .uv.X = br.ReadSingle
                    .uv.Y = br.ReadSingle

                    '-----------------------------------------------------------------------
                    'if this vertex has index junk, skip it.
                    'no tangent and bitangent on BPVTxyznuviiiww type vertex
                    If hasIdx Then
                        br.BaseStream.Position += 8
                    End If

                    If renderSet.has_tangent Then
                        'tangents
                        Dim v3 = unpackNormal_8_8_8(br.ReadUInt32)
                        .tangent.X = -v3.X
                        .tangent.Y = v3.Y
                        .tangent.Z = v3.Z
                        v3 = unpackNormal_8_8_8(br.ReadUInt32)
                        .binormal.X = -v3.X
                        .binormal.Y = v3.Y
                        .binormal.Z = v3.Z
                    End If

                    running += 1

                End With
            Next
        Next
    End Sub

    Public Sub load_primitives_uv2(br As BinaryReader,
                                   ByRef renderSet As RenderSetEntry,
                                   ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        Dim uv2_subname As New String(br.ReadChars(64))
        uv2_subname = uv2_subname.Substring(0, uv2_subname.IndexOf(vbNullChar))
        Debug.Assert(uv2_subname.StartsWith("BPVS"))

        Dim unused = br.ReadUInt32()
        Debug.Assert(unused = 0)

        Dim uv2_format As New String(br.ReadChars(64))
        uv2_format = uv2_format.Substring(0, uv2_format.IndexOf(vbNullChar))
        Debug.Assert(uv2_format = "set3/uv2pc")

        Dim uv2_count = br.ReadUInt32()
        Debug.Assert(uv2_count = renderSet.buffers.vertexBuffer.Length)

        ReDim renderSet.buffers.uv2(uv2_count - 1)
        For i = 0 To uv2_count - 1
            renderSet.buffers.uv2(i).X = br.ReadSingle
            renderSet.buffers.uv2(i).Y = br.ReadSingle
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

End Module

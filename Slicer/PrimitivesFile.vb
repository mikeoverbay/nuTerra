Imports System.Text
Imports OpenTK.Mathematics

''' <summary>One primitive group inside a mesh - a contiguous run of triangles
''' that share a material.</summary>
Public Class PrimGroup
    Public Property StartIndex As Integer
    Public Property PrimitiveCount As Integer
    Public Property StartVertex As Integer
    Public Property VertexCount As Integer
End Class

''' <summary>
''' One drawable mesh: a &lt;base&gt;.vertices section paired with its
''' &lt;base&gt;.indices section.
''' </summary>
Public Class PrimMesh
    Public Property Name As String
    Public Property Format As String
    Public Property Stride As Integer
    Public Property Positions As Vector3() = Array.Empty(Of Vector3)()
    Public Property UVs As Vector2() = Array.Empty(Of Vector2)()
    ''' <summary>Triangle corners, already wound for OpenGL.</summary>
    Public Property Indices As Integer() = Array.Empty(Of Integer)()
    Public ReadOnly Groups As New List(Of PrimGroup)

    Public ReadOnly Property TriangleCount As Integer
        Get
            Return Indices.Length \ 3
        End Get
    End Property
End Class

''' <summary>
''' Reads a `.primitives_processed` - the vertex and index buffers themselves.
'''
''' Standalone, like the rest of the Slicer. `nuTerra/ModelLoaders/PrimitiveLoader.vb`
''' is the engine's version and `docs/primitives_reader.md` writes the format up;
''' this is a third reader of the same bytes, which is the point - when two
''' disagree the file is not the thing that is wrong.
'''
''' THE SECTION TABLE IS AT THE END OF THE FILE.
'''
'''     last 4 bytes    length of the table
'''     table starts at len - 4 - that
'''     per entry       size:u32, 16 unused bytes, namelen:u32, name, pad to 4
'''
''' Entries carry NO OFFSETS. Bodies start at offset 4 and each is padded to 4,
''' so the only way to locate a body is to add every size before it, in order.
''' Get the padding wrong and every section after the first is shifted.
''' </summary>
Public NotInheritable Class PrimitivesFile

    ''' <summary>
    ''' Bytes per vertex, by the format name stored in the section.
    '''
    ''' The stride is NOT in the file - the name is, and the name has to be
    ''' looked up. Three formats appear across the shipped buildings and all
    ''' three were confirmed arithmetically rather than assumed: a vertex body
    ''' runs from 136 to the end of its section, so `(sectionSize - 136) / count`
    ''' IS the stride, and it came out a whole number on every one of the 5,352
    ''' vertex sections in content/buildings.
    '''
    '''     BPVTxyznuvtb        32   4,796 sections
    '''     BPVTxyznuviiiwwtb   40     392 sections
    '''     BPVTxyznuvitb       36     164 sections
    '''
    ''' The first two agree with the engine's own table, which is what makes the
    ''' third trustworthy: the method reproduces the known answers before it is
    ''' believed on the unknown one. 36 is also exactly what the name predicts -
    ''' `BPVTxyznuvtb` plus one 4-byte `i` bone index.
    '''
    ''' NOTE FOR nuTerra: `PrimitiveLoader.load_primitives_vertices` has no case
    ''' for `BPVTxyznuvitb`. It falls to `Case Else`, which is `Debug.Assert(False)` -
    ''' compiled out in Release, leaving stride 0. Those 164 sections cannot be
    ''' read by the engine today. Reported to the nuTerra Work session rather
    ''' than fixed here; ModelLoaders is theirs.
    ''' </summary>
    Private Shared ReadOnly STRIDES As New Dictionary(Of String, Integer)(StringComparer.Ordinal) From {
        {"xyznuv", 32}, {"BPVTxyznuv", 24},
        {"xyznuvtb", 32}, {"BPVTxyznuvtb", 32},
        {"xyznuviiiwwtb", 37}, {"BPVTxyznuviiiww", 32}, {"BPVTxyznuviiiwwtb", 40},
        {"BPVTxyznuvitb", 36}}

    Public Structure SectionRef
        Public Offset As Integer
        Public Size As Integer
    End Structure

    Private Shared Function Pad4(n As Integer) As Integer
        If n Mod 4 = 0 Then Return 0
        Return 4 - (n Mod 4)
    End Function

    Private Shared Function CStrAt(raw As Byte(), at As Integer, maxLen As Integer) As String
        Dim n = 0
        While n < maxLen AndAlso at + n < raw.Length AndAlso raw(at + n) <> 0
            n += 1
        End While
        Return Encoding.ASCII.GetString(raw, at, n)
    End Function

    ''' <summary>The section table, name to (offset, size).</summary>
    Public Shared Function ReadSections(raw As Byte()) As Dictionary(Of String, SectionRef)
        Dim out As New Dictionary(Of String, SectionRef)(StringComparer.Ordinal)
        If raw Is Nothing OrElse raw.Length < 8 Then Return out

        Dim tableLen = BitConverter.ToInt32(raw, raw.Length - 4)
        Dim p = raw.Length - 4 - tableLen
        If p < 0 OrElse p > raw.Length - 4 Then Return out

        Dim bodyOffset = 4
        While p < raw.Length - 4
            Dim size = BitConverter.ToInt32(raw, p)
            p += 4 + 16                                  ' 16 unused bytes
            If p + 4 > raw.Length Then Exit While
            Dim nameLen = BitConverter.ToInt32(raw, p)
            p += 4
            If nameLen < 0 OrElse p + nameLen > raw.Length Then Exit While
            Dim nm = Encoding.ASCII.GetString(raw, p, nameLen)
            p += nameLen + Pad4(nameLen)
            If Not out.ContainsKey(nm) Then
                out.Add(nm, New SectionRef With {.Offset = bodyOffset, .Size = size})
            End If
            bodyOffset += size + Pad4(size)
        End While
        Return out
    End Function

    ''' <summary>
    ''' Every mesh in the file.
    '''
    ''' Two section layouts ship, and both are handled by pairing on the base
    ''' name rather than by position: one global `vertices`/`indices` pair, or
    ''' one `&lt;base&gt;.vertices` + `&lt;base&gt;.indices` per mesh. Pairing the two
    ''' sorted lists off against each other happens to work on most files and
    ''' silently mismatches when a mesh has one and not the other.
    ''' </summary>
    Public Shared Function Parse(raw As Byte()) As List(Of PrimMesh)
        Dim meshes As New List(Of PrimMesh)
        Dim sections = ReadSections(raw)

        For Each kv In sections
            Dim nm = kv.Key
            Dim isVerts = (nm = "vertices") OrElse nm.EndsWith(".vertices", StringComparison.Ordinal)
            If Not isVerts Then Continue For

            Dim baseName = If(nm = "vertices", "", nm.Substring(0, nm.Length - ".vertices".Length))
            Dim idxName = If(baseName = "", "indices", baseName & ".indices")
            If Not sections.ContainsKey(idxName) Then Continue For

            Dim mesh = ReadMesh(raw, kv.Value, sections(idxName), If(baseName = "", "mesh", baseName))
            If mesh IsNot Nothing Then meshes.Add(mesh)
        Next

        meshes.Sort(Function(a, b) String.CompareOrdinal(a.Name, b.Name))
        Return meshes
    End Function

    Private Shared Function ReadMesh(raw As Byte(), vs As SectionRef, isec As SectionRef, nm As String) As PrimMesh
        Dim fmt = CStrAt(raw, vs.Offset, 64)
        Dim stride = 0
        If Not STRIDES.TryGetValue(fmt, stride) Then Return Nothing

        ' A BPVT section carries a SECOND 64-byte format string plus 4 more
        ' bytes before the count - 68 extra - so the body starts at 136, not 68.
        ' A 132-byte guess matches by integer-division coincidence and shifts
        ' the whole stream forward by one float. This has bitten the project
        ' before, on the uv2 section.
        Dim countAt = vs.Offset + 64 + If(fmt.StartsWith("BPVT", StringComparison.Ordinal), 68, 0)
        If countAt + 4 > raw.Length Then Return Nothing
        Dim nVerts = BitConverter.ToInt32(raw, countAt)
        Dim body = countAt + 4
        If nVerts <= 0 OrElse body + CLng(nVerts) * stride > raw.Length Then Return Nothing

        Dim mesh As New PrimMesh With {.Name = nm, .Format = fmt, .Stride = stride}
        Dim pos(nVerts - 1) As Vector3
        Dim uv(nVerts - 1) As Vector2
        For i = 0 To nVerts - 1
            Dim at = body + i * stride
            ' Negate X: DirectX to OpenGL. The winding is flipped below to match -
            ' do one without the other and the model looks right in silhouette
            ' while every face points inward.
            pos(i) = New Vector3(-BitConverter.ToSingle(raw, at),
                                  BitConverter.ToSingle(raw, at + 4),
                                  BitConverter.ToSingle(raw, at + 8))
            ' Position is 12 bytes and the packed normal 4, so uv sits at +16 in
            ' all three shipped layouts - they only differ after it.
            uv(i) = New Vector2(BitConverter.ToSingle(raw, at + 16),
                                BitConverter.ToSingle(raw, at + 20))
        Next
        mesh.Positions = pos
        mesh.UVs = uv

        ' ---- indices
        Dim itype = CStrAt(raw, isec.Offset, 64)
        Dim nIdx = BitConverter.ToInt32(raw, isec.Offset + 64)
        Dim nGroups = BitConverter.ToInt32(raw, isec.Offset + 68)
        Dim wide = (itype = "list32")
        Dim idxSize = If(wide, 4, 2)
        Dim idxAt = isec.Offset + 72
        If nIdx <= 0 OrElse idxAt + CLng(nIdx) * idxSize > raw.Length Then Return mesh

        Dim tri(nIdx - 1) As Integer
        For i = 0 To nIdx - 1
            tri(i) = If(wide, BitConverter.ToInt32(raw, idxAt + i * 4),
                              CInt(BitConverter.ToUInt16(raw, idxAt + i * 2)))
        Next
        ' Swap the first two corners of every triangle - the winding has to flip
        ' with the X mirror above.
        Dim t = 0
        While t + 2 < tri.Length
            Dim tmp = tri(t)
            tri(t) = tri(t + 1)
            tri(t + 1) = tmp
            t += 3
        End While
        mesh.Indices = tri

        ' The group table sits AFTER the indices, so skip the whole index buffer
        ' to reach it.
        Dim gAt = idxAt + nIdx * idxSize
        For g = 0 To nGroups - 1
            If gAt + 16 > raw.Length Then Exit For
            mesh.Groups.Add(New PrimGroup With {
                .StartIndex = BitConverter.ToInt32(raw, gAt),
                .PrimitiveCount = BitConverter.ToInt32(raw, gAt + 4),
                .StartVertex = BitConverter.ToInt32(raw, gAt + 8),
                .VertexCount = BitConverter.ToInt32(raw, gAt + 12)})
            gAt += 16
        Next

        Return mesh
    End Function
End Class

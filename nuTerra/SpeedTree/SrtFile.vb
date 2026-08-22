Imports System.Linq
Imports System.Text

''' <summary>
''' Reader for SpeedTree "SRT 06.0.0" runtime trees as shipped with World of Tanks.
''' Reverse engineered - see the notes in DecodeGeometry for what is and is not known.
'''
''' This is a straight copy of SrtViewer's SrtFile.vb, which is where the format
''' is worked out. Change it there first, prove it in the viewer, then copy it back.
'''
''' Layout:
'''   0x00  char[16]  "SRT 06.0.0"
'''   0x10  uint32    flags
'''   0x14  float[6]  bounding box, min.xyz then max.xyz
'''   0x30  float[4]  LOD distances
'''   ...             wind coefficient tables
'''   string table:    uint64 count, uint64 length[count], then padded strings
'''   billboard mesh:  uint32 nv, uint32 nidx, half[2] uv[nv], uint16 idx[nidx]
'''   draw call table: N entries of 40 bytes, vertex count at +0, index count at +12
'''   geometry:        one [vertices][indices] block per draw call, 4 byte aligned
'''
''' Vertex data is half floats. The common declaration is
'''   [0:3] position, [3] constant 2.0, [4:6] texcoord, [8:11] LOD position.
''' The stride varies per geometry type and is never stored, so it is solved for:
''' the block chain has to consume the file exactly, which pins it down.
''' </summary>
Public Class SrtFile

    ''' <summary>
    ''' What a draw call actually holds.
    '''
    ''' Bones is the wind skeleton - not surface geometry, never drawn.
    ''' Collision is the coarse hull, a handful of capsules built from a few
    ''' distinct points. It is identical across variants of the same tree, where
    ''' the visible geometry is not.
    ''' Skin is the trunk and branch surface, skinned to those bones: each of its
    ''' vertices carries a bone index and a weight so it bends with them. A tree
    ''' has one skin per bone set, which is why they arrive as separate draw
    ''' calls with different vertex declarations.
    ''' Foliage is the leaf and frond cards cut from the atlas.
    ''' </summary>
    Public Enum PartKind
        Unknown
        Bones
        Collision
        Skin
        Foliage
    End Enum

    Public Class DrawCall
        Public VertexCount As Integer
        Public IndexCount As Integer
        Public Stride As Integer
        Public VertexOffset As Integer
        Public IndexOffset As Integer
        ''' <summary>Byte offset of the position field inside the vertex.</summary>
        Public PosOffset As Integer
        ''' <summary>True when the texcoord slots never change, ie there are no real UVs.</summary>
        Public FlatUV As Boolean
        Public DuplicateVerts As Integer
        Public DegenerateTris As Integer
        Public HasMarker As Boolean
        Public UvMax As Single
        Public Kind As PartKind = PartKind.Unknown
        ''' <summary>Geometry type id stored just before the table entry, -1 if unreadable.</summary>
        Public TypeId As Integer = -1
        Public Lod As Integer
        ''' <summary>Position of this draw call within its LOD.</summary>
        Public Slot As Integer
        ''' <summary>Identifies the same geometry across LODs. See AlignLods.</summary>
        Public PairKey As Integer
        ''' <summary>Set when the geometry itself says this is a hull or a skeleton.</summary>
        Public Structural As Boolean
        '''<summary>
        ''' The texture the file declares for this part, empty when it declares
        ''' none - which is how hulls and bone chains are marked. Empty also when
        ''' the render states could not be read and the kind was guessed instead.
        '''</summary>
        Public DiffuseTexture As String = ""
        '''<summary>True when the kind came from the file rather than from the geometry.</summary>
        Public Declared As Boolean
        '''<summary>True when the vertex is float32 rather than half float.</summary>
        Public Wide As Boolean
        Public ReadOnly Property Renderable As Boolean
            Get
                Return Kind <> PartKind.Bones AndAlso Kind <> PartKind.Collision
            End Get
        End Property
        Public Positions() As Single   ' xyz per vertex
        Public TexCoords() As Single   ' uv per vertex
        Public Normals() As Single     ' xyz per vertex
        ''' <summary>True when the file supplied normals; False when we derived them.</summary>
        Public HasNormals As Boolean
        Public Indices() As UInteger
        Public ReadOnly Property TriangleCount As Integer
            Get
                Return IndexCount \ 3
            End Get
        End Property
    End Class

    Public Property Path As String
    Public Property Magic As String
    Public Property BoundsMin As Single() = New Single(2) {}
    Public Property BoundsMax As Single() = New Single(2) {}
    Public Property LodDistances As Single() = New Single(3) {}
    Public Property Strings As New List(Of String)
    Public Property DrawCalls As New List(Of DrawCall)
    Public Property LodCount As Integer = 1
    Public Property Solved As Boolean
    Public Property Notes As String = ""

    ' textures picked out of the material strings
    Public Property FoliageTexture As String
    Public Property BarkTexture As String

    Private data() As Byte
    ''' <summary>
    ''' Bounds with a working margin, and with each axis put the right way round -
    ''' some files store the z pair inverted, which used to fail every check.
    ''' </summary>
    Private boundsLo As Single() = New Single(2) {}
    Private boundsHi As Single() = New Single(2) {}

    '''<summary>
    ''' Vertex widths to try. Most assets pack their vertices as half floats and
    ''' land between 16 and 48, but a few use full float32 and run much wider -
    ''' linden_regular_tall is 64, 76, 88 and 108 - so the list has to reach past
    ''' the narrow family.
    '''</summary>
    Private Shared ReadOnly CandidateStrides() As Integer =
        {16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72,
         76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116, 120, 124, 128}

    '''<summary>
    ''' The wide vertex is float32 and packs as two vec4s, position and normal
    ''' each carrying one texcoord component in their w:
    '''
    '''     +0   float3 position   +12  float u
    '''     +16  float3 normal     +28  float v
    '''     +32  float  2.0        +36  float3 tangent
    '''     +52  float3 LOD position
    '''
    ''' The 2.0 at +32 is the same foliage marker the half format keeps at slot 3.
    '''</summary>
    Private Const WIDE_UV_U As Integer = 12
    Private Const WIDE_NORMAL As Integer = 16
    Private Const WIDE_UV_V As Integer = 28
    '''<summary>No half float layout is this wide, so anything from here up is float32.</summary>
    Private Const WIDE_MIN_STRIDE As Integer = 64

    Public Shared Function FromBytes(bytes() As Byte, name As String) As SrtFile
        Dim f As New SrtFile With {.Path = name}
        f.data = bytes
        f.Parse()
        Return f
    End Function

    Public ReadOnly Property TotalTriangles As Integer
        Get
            Dim t = 0
            For Each dc In DrawCalls
                t += dc.TriangleCount
            Next
            Return t
        End Get
    End Property

    ''' <summary>Triangles that are actual surface, ie excluding the bend bones.</summary>
    Public ReadOnly Property RenderableTriangles As Integer
        Get
            Dim t = 0
            For Each dc In DrawCalls
                If dc.Renderable Then t += dc.TriangleCount
            Next
            Return t
        End Get
    End Property

    Private Function Half(offset As Integer) As Single
        Return CSng(BitConverter.ToHalf(data, offset))
    End Function

    Private Sub Parse()
        If data.Length < 64 Then
            Notes = "file too small"
            Return
        End If

        Magic = Encoding.ASCII.GetString(data, 0, 10).TrimEnd(ChrW(0))
        If Not Magic.StartsWith("SRT") Then
            Notes = "not an SRT file"
            Return
        End If

        For i = 0 To 2
            BoundsMin(i) = BitConverter.ToSingle(data, &H14 + i * 4)
            BoundsMax(i) = BitConverter.ToSingle(data, &H20 + i * 4)
            Dim lo = Math.Min(BoundsMin(i), BoundsMax(i))
            Dim hi = Math.Max(BoundsMin(i), BoundsMax(i))
            Dim margin = (hi - lo) * 0.1F + 0.05F
            boundsLo(i) = lo - margin
            boundsHi(i) = hi + margin
        Next
        For i = 0 To 3
            LodDistances(i) = BitConverter.ToSingle(data, &H30 + i * 4)
        Next

        ' The declared table is what the render states index into, so it has to
        ' be tried first. The loose scan is only a safety net for files whose
        ' table cannot be located.
        ReadStringsTable()
        If Strings.Count = 0 Then ReadStrings()
        PickTextures()
        DecodeGeometry()
    End Sub

    ''' <summary>
    ''' Material and texture names. The declared length array turned out to be
    ''' laid out inconsistently between assets, so rather than trust it we just
    ''' scan the header region for NUL terminated printable runs. The names are
    ''' only used to choose a texture, so this is both simpler and safer.
    ''' </summary>
    Private Sub ReadStrings()
        Dim limit = Math.Min(data.Length, &H4000)
        Dim out As New List(Of String)
        Dim i = &H40
        While i < limit
            If data(i) >= 32 AndAlso data(i) < 127 Then
                Dim j = i
                While j < limit AndAlso data(j) >= 32 AndAlso data(j) < 127
                    j += 1
                End While
                If j - i >= 4 AndAlso j < limit AndAlso data(j) = 0 Then
                    out.Add(Encoding.ASCII.GetString(data, i, j - i))
                End If
                i = j
            End If
            i += 1
        End While
        Strings = out
    End Sub

    ''' <summary>Kept for reference: the length-prefixed form seen in some assets.</summary>
    '''<summary>
    ''' The declared string table. Layout, which took some squinting:
    '''
    '''     uint32  count
    '''     count x 8 byte slots, the length in the *second* dword of each
    '''     the blobs, each NUL terminated and padded out to its length
    '''
    ''' Entry 0 is a four byte empty string, and it is the one that matters: a
    ''' render state whose texture index is 0 is declaring that the part has no
    ''' texture, which is how hulls and bone chains are marked.
    '''
    ''' Reading the slots as plain uint64 lengths, which is the obvious guess,
    ''' lands the blobs four bytes late and every string comes out shifted.
    '''</summary>
    Private Sub ReadStringsTable()
        Dim limit = Math.Min(data.Length - 16, &H4000)
        For off = &H40 To limit - 1 Step 4
            Dim count = BitConverter.ToUInt32(data, off)
            If count < 2UI OrElse count > 256UI Then Continue For
            If off + 4 + 8 * CInt(count) > data.Length Then Continue For

            Dim lens As New List(Of Integer)
            Dim ok = True
            For k = 0 To CInt(count) - 1
                Dim L = BitConverter.ToUInt32(data, off + 8 + k * 8)
                If L < 4UI OrElse L > 512UI OrElse (L Mod 4UI) <> 0UI Then ok = False : Exit For
                lens.Add(CInt(L))
            Next
            If Not ok Then Continue For

            Dim got As New List(Of String)
            Dim q = off + 4 + 8 * CInt(count)
            For Each L In lens
                If q + L > data.Length Then ok = False : Exit For

                ' A correctly aligned entry is <text> NUL <zero padding>. If we
                ' latched on at the wrong offset the padding check fails, which
                ' is what stops this accepting a near miss.
                Dim z = -1
                For b = 0 To L - 1
                    If data(q + b) = 0 Then z = b : Exit For
                Next
                If z < 0 Then ok = False : Exit For
                For b = z To L - 1
                    If data(q + b) <> 0 Then ok = False : Exit For
                Next
                If Not ok Then Exit For

                Dim raw = Encoding.ASCII.GetString(data, q, z)
                For Each ch In raw
                    If AscW(ch) < 32 OrElse AscW(ch) > 126 Then ok = False : Exit For
                Next
                If Not ok Then Exit For

                got.Add(raw)
                q += L
            Next
            If Not ok Then Continue For

            Dim anyDds = False
            For Each s In got
                If s.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) Then anyDds = True
            Next
            If anyDds Then
                Strings = got
                Return
            End If
        Next
    End Sub

    ''' <summary>
    ''' Foliage is the plain _AM map; bark is the _Bark_ one. Billboard atlases are
    ''' generated by the game at runtime and are not shipped, so they are ignored.
    ''' </summary>
    Private Sub PickTextures()
        For Each s In Strings
            Dim l = s.ToLower
            If Not l.EndsWith("_am.dds") Then Continue For
            If l.Contains("billboard") Then Continue For
            If l.Contains("_bark_") Then
                If BarkTexture Is Nothing Then BarkTexture = s
            ElseIf FoliageTexture Is Nothing Then
                FoliageTexture = s
            End If
        Next
        If FoliageTexture Is Nothing Then FoliageTexture = BarkTexture
    End Sub

    ''' <summary>Draw call table: the longest run of 40 byte entries.</summary>
    Private Function FindTable() As List(Of Integer())
        Dim hits As New List(Of Integer())
        For off = &H600 To data.Length - 41 Step 4
            Dim nv = BitConverter.ToUInt32(data, off)
            Dim z1 = BitConverter.ToUInt32(data, off + 4)
            Dim z2 = BitConverter.ToUInt32(data, off + 8)
            Dim ni = BitConverter.ToUInt32(data, off + 12)
            If z1 <> 0UI OrElse z2 <> 0UI Then Continue For
            If nv < 3UI OrElse nv > 60000UI Then Continue For
            If ni < 3UI OrElse ni > 300000UI Then Continue For
            If (ni Mod 3UI) <> 0UI Then Continue For
            If ni < nv \ 2UI Then Continue For
            hits.Add(New Integer() {off, CInt(nv), CInt(ni)})
        Next

        Dim best As New List(Of Integer())
        Dim i = 0
        While i < hits.Count
            Dim run As New List(Of Integer()) From {hits(i)}
            Dim j = i + 1
            While j < hits.Count AndAlso hits(j)(0) = run(run.Count - 1)(0) + 40
                run.Add(hits(j))
                j += 1
            End While
            If run.Count > best.Count Then best = run
            i += 1
        End While
        Return best
    End Function

    ''' <summary>
    ''' Works out where each block of geometry lives.
    '''
    ''' The stride is never stored, so it has to be solved for. Draw calls are
    ''' walked in order, each one trying every plausible stride, and a candidate
    ''' is only accepted when
    '''
    '''   * its index buffer lands where the stride says it should and every
    '''     index addresses a vertex that exists, and
    '''   * the positions decode as half floats inside the file's own bounding box.
    '''
    ''' The chain then has to consume the file exactly. Those three together pin
    ''' the layout down: across the shipped library every file that solves at all
    ''' solves to exactly one answer.
    '''
    ''' An earlier version assumed the draw calls repeated on a fixed period, one
    ''' pass per LOD. That is usually true but not always - a palm can carry a
    ''' frond type in LOD0 that LOD1 drops - and those files could not be solved
    ''' at all. Solving per draw call removes the assumption.
    ''' </summary>
    Private Sub DecodeGeometry()
        Dim tab = FindTable()
        If tab.Count = 0 Then
            Notes = "no draw call table"
            Return
        End If

        ' Geometry begins inside the final table entry, not after it.
        Dim start = tab(tab.Count - 1)(0) + 28
        Dim chosen(tab.Count - 1) As Integer

        If Not WalkBlocks(0, start, tab, chosen) Then
            Notes = "could not solve strides"
            Return
        End If

        Dim built As New List(Of DrawCall)
        Dim pos = start
        For i = 0 To tab.Count - 1
            Dim nv = tab(i)(1), ni = tab(i)(2), st = chosen(i)
            built.Add(New DrawCall With {
                .VertexCount = nv, .IndexCount = ni, .Stride = st,
                .VertexOffset = pos, .IndexOffset = pos + nv * st,
                .Wide = (st >= WIDE_MIN_STRIDE),
                .TypeId = TypeIdOf(tab, i)})
            pos = ((pos + nv * st + ni * 2 + 3) \ 4) * 4
        Next

        AssignLods(built)
        For Each dc In built
            ReadBlock(dc)
        Next
        AlignLods(built)

        ' The file states what every part is. Only fall back to reading it out of
        ' the geometry for the assets whose render states cannot be located.
        If ReadRenderStates(built, tab(0)(0)) Then
            ClassifyFromStates(built)
            For Each dc In built
                dc.Declared = True
            Next
        Else
            MarkHulls(built)
            HarmoniseKinds(built)
        End If

        DrawCalls = built
        Solved = True
    End Sub

    '''<summary>
    ''' Works out which draw calls are the same piece of geometry at different LODs.
    '''
    ''' Position within the LOD is not enough. A LOD may drop a part, and
    ''' everything after the gap then shifts up - olive_bush keeps a stride 40
    ''' part in LOD0 that LOD1 does not have, so LOD1's third part is LOD0's
    ''' fourth. The type ids are no better: they rise through each LOD but the
    ''' numbering is not shared between LODs in any consistent way.
    '''
    ''' What does hold is the order and the vertex layout. Parts appear in the
    ''' same order in every LOD, and a part keeps its stride, so the two lists are
    ''' aligned on their longest common subsequence of strides and whatever
    ''' matches is a pair. Anything that does not match is left unpaired rather
    ''' than guessed at - that covers the assets whose LOD1 uses a more compact
    ''' vertex format, where the strides legitimately differ.
    '''</summary>
    Private Sub AlignLods(built As List(Of DrawCall))
        For Each dc In built
            dc.PairKey = -1
        Next

        Dim lod0 = built.Where(Function(d) d.Lod = 0).ToList()
        For i = 0 To lod0.Count - 1
            lod0(i).PairKey = i
        Next

        For lod = 1 To LodCount - 1
            Dim other = built.Where(Function(d) d.Lod = lod).ToList()
            For Each pair In MatchByStride(lod0, other)
                other(pair.Value).PairKey = lod0(pair.Key).PairKey
            Next
        Next
    End Sub

    '''<summary>
    ''' Longest common subsequence over the two lists' strides, returned as index
    ''' pairs into a and b.
    '''</summary>
    Private Shared Function MatchByStride(a As List(Of DrawCall), b As List(Of DrawCall)) _
            As List(Of KeyValuePair(Of Integer, Integer))
        Dim n = a.Count, m = b.Count
        Dim len(n, m) As Integer
        For i = n - 1 To 0 Step -1
            For j = m - 1 To 0 Step -1
                If a(i).Stride = b(j).Stride Then
                    len(i, j) = len(i + 1, j + 1) + 1
                Else
                    len(i, j) = Math.Max(len(i + 1, j), len(i, j + 1))
                End If
            Next
        Next

        Dim pairs As New List(Of KeyValuePair(Of Integer, Integer))
        Dim x = 0, y = 0
        While x < n AndAlso y < m
            If a(x).Stride = b(y).Stride Then
                pairs.Add(New KeyValuePair(Of Integer, Integer)(x, y))
                x += 1 : y += 1
            ElseIf len(x + 1, y) >= len(x, y + 1) Then
                x += 1
            Else
                y += 1
            End If
        End While
        Return pairs
    End Function

    '''<summary>
    ''' Reads the render state that the file declares for each draw call, which
    ''' says outright which texture the part is drawn with.
    '''
    ''' The states are 680 byte records sitting just before the draw call table,
    ''' one per distinct type id plus, usually, a trailing one for the billboard.
    ''' The id stored before each table entry indexes them. Inside a record the
    ''' three texture layers are string table indices:
    '''
    '''     +0x04  diffuse
    '''     +0x0c  normal
    '''     +0x24  specular
    '''
    ''' Index 0 is the empty string, and that is how the file marks a part that
    ''' is not drawn at all - the collision hulls and the bend bone chains. Every
    ''' judgement the geometry tests were making is stated here instead, which is
    ''' why this runs first and the tests only cover the files it cannot read.
    '''</summary>
    Private Function ReadRenderStates(built As List(Of DrawCall), tableStart As Integer) As Boolean
        If Strings.Count = 0 Then Return False

        Dim maxId = -1
        For Each dc In built
            If dc.TypeId > maxId Then maxId = dc.TypeId
        Next
        If maxId < 0 Then Return False

        ' The array sits just before the draw call table, but not at a fixed
        ' distance from it - the gap is usually 68 bytes and sometimes more, and
        ' there may or may not be a trailing billboard state. Rather than model
        ' that, walk back from the table and let the checks in Resolve say which
        ' offset is the real one. They are strict enough that a wrong offset
        ' essentially never passes: every id has to index a string that exists,
        ' every name has to be blank or a .dds, none may be the billboard atlas,
        ' and at least one part has to be drawn.
        ' The array ends a short way before the draw call table, but the gap is
        ' not fixed - 68 bytes on most assets, 92 on some - so it is searched
        ' for. What is fixed is that there is usually one more record than there
        ' are ids, holding the billboard, and that layout is tried first: an
        ' asset whose billboard record happens to carry no texture will also
        ' validate one record too high, and then a hull comes out wearing bark.
        For spare = 1 To 0 Step -1
            Dim count = maxId + 1 + spare
            For tail = MIN_STATE_TAIL To MAX_STATE_TAIL Step 4
                Dim first = tableStart - tail - count * STATE_SIZE
                If first < 0 Then Exit For
                If Resolve(built, first) Then Return True
            Next
        Next
        Return False
    End Function

    Private Function Resolve(built As List(Of DrawCall), first As Integer) As Boolean
        Dim names(built.Count - 1) As String
        For i = 0 To built.Count - 1
            Dim id = built(i).TypeId
            If id < 0 Then Return False
            Dim at = first + id * STATE_SIZE + 4
            If at + 4 > data.Length Then Return False
            Dim slot = BitConverter.ToUInt32(data, at)
            If slot >= Strings.Count Then Return False
            Dim name = Strings(CInt(slot))

            ' The three texture layers are the anchor. A record holds diffuse,
            ' normal and specular as X_AM.dds, X_NM.dds and X_SM.dds, so an
            ' offset that is off by a field reads the normal as the diffuse and
            ' fails here. Without this the scan happily settles on a shift that
            ' hands every part the same normal map.
            If name.Length > 0 Then
                If Not name.EndsWith("_AM.dds", StringComparison.OrdinalIgnoreCase) Then Return False
                Dim stem = name.Substring(0, name.Length - 7)
                Dim nslot = BitConverter.ToUInt32(data, at + &H8)
                If nslot >= Strings.Count Then Return False
                If Not String.Equals(Strings(CInt(nslot)), stem & "_NM.dds",
                                     StringComparison.OrdinalIgnoreCase) Then Return False
            End If

            ' The billboard state is the giveaway for a misaligned guess. It sits
            ' last, so reading the array one record short slides every id up by
            ' one and the highest lands on it - and no real draw call is ever
            ' drawn with the billboard atlas.
            If name.IndexOf("billboard", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return False
            End If

            names(i) = name
        Next

        ' A guess that lands outside the array reads as all zeroes, which would
        ' otherwise pass as "nothing is drawn". No tree is entirely hulls.
        Dim drawn = 0
        For Each nm In names
            If nm <> "" Then drawn += 1
        Next
        If drawn = 0 Then Return False

        For i = 0 To built.Count - 1
            built(i).DiffuseTexture = names(i)
        Next
        Return True
    End Function

    '''<summary>
    ''' Turns the declared texture into a part kind. A part with no texture is
    ''' not drawn; the rest are named as bark or foliage only so the report and
    ''' the viewer's labels stay readable - what actually gets bound is
    ''' DiffuseTexture, so a species with three atlases is no longer a problem.
    '''</summary>
    Private Sub ClassifyFromStates(built As List(Of DrawCall))
        For Each dc In built
            If dc.DiffuseTexture = "" Then
                Dim unique = dc.VertexCount - dc.DuplicateVerts
                dc.Kind = If(unique <= 32, PartKind.Collision, PartKind.Bones)
                dc.Structural = True
            ElseIf dc.DiffuseTexture.IndexOf("_bark_", StringComparison.OrdinalIgnoreCase) >= 0 Then
                dc.Kind = PartKind.Skin
            Else
                dc.Kind = PartKind.Foliage
            End If
        Next
    End Sub

    '''<summary>
    ''' Finds the parts that are collision hulls whatever their UVs suggest.
    '''
    ''' Two things have to hold together. The part has to be too coarse to be a
    ''' surface - sunflower_var1 opens with six distinct points and eight
    ''' triangles, a three sided tube up the stalk - and it has to be the same
    ''' size in every LOD. A hull is authored once and shared, so it never
    ''' decimates; real geometry always does. drytree_01 is why the second half
    ''' is needed: its stride 40 part drops from 19 vertices to 8 between LODs,
    ''' so the LOD1 copy looks like a hull on its own and is nothing of the sort.
    '''
    ''' Assets with a single LOD have nothing to compare against, so the size
    ''' test alone decides. Being wrong there hides a handful of triangles, where
    ''' being wrong the other way paints the leaf atlas over a capsule.
    '''</summary>
    Private Sub MarkHulls(built As List(Of DrawCall))
        For Each g In built.GroupBy(Function(d) If(d.PairKey < 0, -1, d.PairKey))
            Dim group = g.ToList()
            Dim coarse = group.All(Function(d) d.VertexCount - d.DuplicateVerts <= HULL_POINTS)
            If Not coarse Then Continue For

            ' unpaired parts are judged one at a time, not as a group
            If g.Key < 0 Then
                For Each dc In group
                    dc.Kind = PartKind.Collision
                    dc.Structural = True
                Next
                Continue For
            End If

            Dim shared_size = group.All(Function(d) d.VertexCount = group(0).VertexCount)
            If Not shared_size Then Continue For

            For Each dc In group
                dc.Kind = PartKind.Collision
                dc.Structural = True
            Next
        Next
    End Sub

    '''<summary>
    ''' The same geometry appears once per LOD, so a draw call cannot be a bend
    ''' bone chain in one LOD and a trunk in another. Where a pair disagrees the
    ''' surface answer wins, for two reasons: the structural tests sit close to
    ''' their threshold on a low LOD trunk, and the kind decides which atlas the
    ''' part is drawn with, so getting it wrong either hides real geometry or
    ''' paints leaves onto bark.
    '''
    ''' palmetto_palm_17m showed the first failure - its LOD0 trunk came out Skin
    ''' at 65% unique vertices and its LOD1 trunk came out Bones at 57%, either
    ''' side of the 60% line, and 897 triangles of palm trunk went missing.
    ''' olive_bush showed the second: its LOD1 trunk came out Unknown and was
    ''' drawn with the leaf atlas.
    '''</summary>
    Private Sub HarmoniseKinds(built As List(Of DrawCall))
        Dim byPair As New Dictionary(Of Integer, PartKind)
        For Each dc In built
            If dc.PairKey < 0 Then Continue For
            If dc.Kind = PartKind.Skin OrElse dc.Kind = PartKind.Foliage Then
                If Not byPair.ContainsKey(dc.PairKey) Then byPair(dc.PairKey) = dc.Kind
            End If
        Next
        For Each dc In built
            If dc.PairKey < 0 Then Continue For
            If dc.Kind = PartKind.Skin OrElse dc.Kind = PartKind.Foliage Then Continue For
            If dc.Structural Then Continue For
            If byPair.ContainsKey(dc.PairKey) Then dc.Kind = byPair(dc.PairKey)
        Next
    End Sub

    ''' <summary>
    ''' Each table entry is preceded by a geometry type id, the first one
    ''' included - it sits in the four bytes just before the table. Anything that
    ''' does not read as a small number comes back as -1.
    '''
    ''' The id is the index of the draw call's render state, which is what makes
    ''' it worth reading rather than inferring: the render state names the
    ''' texture. It also orders the LODs, since the ids climb within one and
    ''' restart at the next.
    ''' </summary>
    Private Function TypeIdOf(tab As List(Of Integer()), i As Integer) As Integer
        Dim off = tab(i)(0) - 4
        If off < 0 OrElse off + 4 > data.Length Then Return -1
        Dim v = BitConverter.ToUInt32(data, off)
        If v > 64UI Then Return -1
        Return CInt(v)
    End Function

    ''' <summary>
    ''' LODs are laid out one after another and their type ids climb, so a LOD
    ''' ends wherever the next id fails to beat the last one.
    '''
    ''' Testing for an id of zero instead is not enough: a LOD does not have to
    ''' start at zero. sunflower_var1 runs 3,5 then 1,3,5 then 0,2,4 - three LODs,
    ''' only the last of which starts at zero - and reading that as two LODs put
    ''' two of them in the same bucket, so both drew at once, one inside the other.
    ''' </summary>
    Private Sub AssignLods(built As List(Of DrawCall))
        Dim lod = 0, slot = 0, previous = -1
        For i = 0 To built.Count - 1
            Dim id = built(i).TypeId
            If i > 0 AndAlso id >= 0 AndAlso previous >= 0 AndAlso id <= previous Then
                lod += 1
                slot = 0
            End If
            built(i).Lod = lod
            built(i).Slot = slot
            slot += 1
            If id >= 0 Then previous = id
        Next
        LodCount = lod + 1
    End Sub

    ''' <summary>Depth first search over the stride of each block in turn.</summary>
    Private Function WalkBlocks(i As Integer, pos As Integer,
                                tab As List(Of Integer()), chosen() As Integer) As Boolean
        If i = tab.Count Then Return Math.Abs(pos - data.Length) <= 3

        Dim nv = tab(i)(1), ni = tab(i)(2)
        For Each st In CandidateStrides
            Dim idxAt = pos + nv * st
            Dim blockEnd = idxAt + ni * 2
            If blockEnd > data.Length Then Continue For
            If Not IndicesInRange(idxAt, ni, nv) Then Continue For
            If Not PositionsInBounds(pos, nv, st, st >= WIDE_MIN_STRIDE) Then Continue For
            chosen(i) = st
            If WalkBlocks(i + 1, ((blockEnd + 3) \ 4) * 4, tab, chosen) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' A wrong stride puts the index buffer over vertex data, and those bytes
    ''' read as indices well past the end of the block. This is the sharpest of
    ''' the three tests.
    ''' </summary>
    Private Function IndicesInRange(at As Integer, count As Integer, nv As Integer) As Boolean
        For k = 0 To count - 1
            If BitConverter.ToUInt16(data, at + k * 2) >= nv Then Return False
        Next
        Return True
    End Function

    '''<summary>
    ''' Positions have to decode inside the file's own bounding box, and they
    ''' have to be real positions.
    '''
    ''' The second half matters more than it sounds. Index data read as float32
    ''' comes out as denormals a whisker from zero, and zero is inside every
    ''' bounding box, so an index buffer will happily pass for a run of vertices
    ''' unless something insists the model has some size to it.
    '''</summary>
    Private Function PositionsInBounds(at As Integer, nv As Integer, stride As Integer,
                                       wide As Boolean) As Boolean
        Dim step_ = Math.Max(1, nv \ 48)
        Dim seen = 0, good = 0, real = 0
        For v = 0 To nv - 1 Step step_
            Dim o = at + v * stride
            Dim x, y, z As Single
            If wide Then
                If o + 12 > data.Length Then Return False
                x = BitConverter.ToSingle(data, o)
                y = BitConverter.ToSingle(data, o + 4)
                z = BitConverter.ToSingle(data, o + 8)
            Else
                x = Half(o) : y = Half(o + 2) : z = Half(o + 4)
            End If
            seen += 1
            If Single.IsNaN(x) OrElse Single.IsNaN(y) OrElse Single.IsNaN(z) Then Continue For
            If x < boundsLo(0) OrElse x > boundsHi(0) Then Continue For
            If y < boundsLo(1) OrElse y > boundsHi(1) Then Continue For
            If z < boundsLo(2) OrElse z > boundsHi(2) Then Continue For
            good += 1
            If Math.Abs(x) > 0.001F OrElse Math.Abs(y) > 0.001F OrElse Math.Abs(z) > 0.001F Then
                real += 1
            End If
        Next
        If seen = 0 OrElse good * 100 \ seen < 95 Then Return False
        Return real * 100 \ seen >= 50
    End Function

    ''' <summary>
    ''' Re-read one block. PosOffset is exposed because the vertex declaration is
    ''' not stored anywhere we can read, so a few geometry types put the position
    ''' somewhere other than byte 0 and the only way to find it is to try.
    ''' </summary>
    Public Sub ReadBlock(dc As DrawCall)
        ReDim dc.Positions(dc.VertexCount * 3 - 1)
        ReDim dc.TexCoords(dc.VertexCount * 2 - 1)

        Dim seen As New HashSet(Of String)
        Dim uMin = Single.MaxValue, uMax = Single.MinValue
        Dim vMin = Single.MaxValue, vMax = Single.MinValue

        For v = 0 To dc.VertexCount - 1
            Dim vert = dc.VertexOffset + v * dc.Stride
            Dim o = vert + dc.PosOffset
            Dim x, y, z, tu, tv As Single
            If dc.Wide Then
                x = BitConverter.ToSingle(data, o)
                y = BitConverter.ToSingle(data, o + 4)
                z = BitConverter.ToSingle(data, o + 8)
                tu = BitConverter.ToSingle(data, vert + WIDE_UV_U)
                tv = BitConverter.ToSingle(data, vert + WIDE_UV_V)
            Else
                x = Half(o) : y = Half(o + 2) : z = Half(o + 4)
                ' texcoord sits two halves past the marker slot
                tu = Half(vert + 8)
                tv = Half(vert + 10)
            End If
            dc.Positions(v * 3 + 0) = x
            dc.Positions(v * 3 + 1) = y
            dc.Positions(v * 3 + 2) = z
            If Not seen.Add(x & "|" & y & "|" & z) Then dc.DuplicateVerts += 1

            dc.TexCoords(v * 2 + 0) = tu
            dc.TexCoords(v * 2 + 1) = tv

            uMin = Math.Min(uMin, tu) : uMax = Math.Max(uMax, tu)
            vMin = Math.Min(vMin, tv) : vMax = Math.Max(vMax, tv)
        Next

        dc.FlatUV = (uMax - uMin < 0.001F) AndAlso (vMax - vMin < 0.001F)
        dc.UvMax = Math.Max(Math.Max(Math.Abs(uMin), Math.Abs(uMax)),
                            Math.Max(Math.Abs(vMin), Math.Abs(vMax)))

        ' the 2.0 marker only appears in the foliage card declaration
        dc.HasMarker = True
        For v = 0 To dc.VertexCount - 1
            Dim m As Single
            If dc.Wide Then
                m = BitConverter.ToSingle(data, dc.VertexOffset + v * dc.Stride + 32)
            Else
                m = Half(dc.VertexOffset + v * dc.Stride + 6)
            End If
            If Math.Abs(m - 2.0F) > 0.001F Then
                dc.HasMarker = False
                Exit For
            End If
        Next

        ReDim dc.Indices(dc.IndexCount - 1)
        For i = 0 To dc.IndexCount - 1
            dc.Indices(i) = BitConverter.ToUInt16(data, dc.IndexOffset + i * 2)
        Next

        ReadNormals(dc)

        ' count collapsed triangles; a wrong stride or offset shows up here first
        dc.DegenerateTris = 0
        For t = 0 To dc.IndexCount - 3 Step 3
            Dim i0 = CInt(dc.Indices(t)), i1 = CInt(dc.Indices(t + 1)), i2 = CInt(dc.Indices(t + 2))
            If i0 >= dc.VertexCount OrElse i1 >= dc.VertexCount OrElse i2 >= dc.VertexCount Then
                dc.DegenerateTris += 1
                Continue For
            End If
            Dim ax = dc.Positions(i1 * 3) - dc.Positions(i0 * 3)
            Dim ay = dc.Positions(i1 * 3 + 1) - dc.Positions(i0 * 3 + 1)
            Dim az = dc.Positions(i1 * 3 + 2) - dc.Positions(i0 * 3 + 2)
            Dim bx = dc.Positions(i2 * 3) - dc.Positions(i0 * 3)
            Dim by = dc.Positions(i2 * 3 + 1) - dc.Positions(i0 * 3 + 1)
            Dim bz = dc.Positions(i2 * 3 + 2) - dc.Positions(i0 * 3 + 2)
            Dim cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx
            If Math.Sqrt(cx * cx + cy * cy + cz * cz) * 0.5 < 0.0000001 Then dc.DegenerateTris += 1
        Next

        Classify(dc)
    End Sub

    ''' <summary>
    ''' Normals are three unsigned bytes at stride-8, mapped (b-127.5)/127.5, with
    ''' the matching tangent four bytes later and a handedness byte after each.
    ''' Verified three ways: the vectors are unit length to within 1% on every
    ''' vertex, the pair is perpendicular (median |dot| 0.002), and the first of
    ''' the two tracks the surface while the second does not.
    '''
    ''' Not every geometry type stores them - the stride 40 type has no unit
    ''' length triple anywhere - so when they are missing we derive smooth
    ''' normals from the triangles instead.
    ''' </summary>
    Private Sub ReadNormals(dc As DrawCall)
        ReDim dc.Normals(dc.VertexCount * 3 - 1)

        If dc.Wide Then
            For v = 0 To dc.VertexCount - 1
                Dim o = dc.VertexOffset + v * dc.Stride + WIDE_NORMAL
                Dim x = BitConverter.ToSingle(data, o)
                Dim y = BitConverter.ToSingle(data, o + 4)
                Dim z = BitConverter.ToSingle(data, o + 8)
                Dim L = CSng(Math.Sqrt(x * x + y * y + z * z))
                If L > 0.0001F Then
                    dc.Normals(v * 3 + 0) = x / L
                    dc.Normals(v * 3 + 1) = y / L
                    dc.Normals(v * 3 + 2) = z / L
                End If
            Next
            dc.HasNormals = True
            Return
        End If

        Dim off = dc.Stride - 8
        Dim unit = 0
        If off >= 12 Then
            For v = 0 To dc.VertexCount - 1
                Dim o = dc.VertexOffset + v * dc.Stride + off
                Dim x = (CSng(data(o)) - 127.5F) / 127.5F
                Dim y = (CSng(data(o + 1)) - 127.5F) / 127.5F
                Dim z = (CSng(data(o + 2)) - 127.5F) / 127.5F
                Dim L = CSng(Math.Sqrt(x * x + y * y + z * z))
                If L > 0.97F AndAlso L < 1.03F Then unit += 1
                If L > 0.0001F Then
                    dc.Normals(v * 3 + 0) = x / L
                    dc.Normals(v * 3 + 1) = y / L
                    dc.Normals(v * 3 + 2) = z / L
                End If
            Next
        End If

        dc.HasNormals = (dc.VertexCount > 0 AndAlso unit * 100 \ dc.VertexCount >= 90)
        If dc.HasNormals Then Return

        ' derive them: area weighted average of the adjacent faces
        Array.Clear(dc.Normals, 0, dc.Normals.Length)
        For t = 0 To dc.IndexCount - 3 Step 3
            Dim i0 = CInt(dc.Indices(t)), i1 = CInt(dc.Indices(t + 1)), i2 = CInt(dc.Indices(t + 2))
            If i0 >= dc.VertexCount OrElse i1 >= dc.VertexCount OrElse i2 >= dc.VertexCount Then Continue For
            Dim ax = dc.Positions(i1 * 3) - dc.Positions(i0 * 3)
            Dim ay = dc.Positions(i1 * 3 + 1) - dc.Positions(i0 * 3 + 1)
            Dim az = dc.Positions(i1 * 3 + 2) - dc.Positions(i0 * 3 + 2)
            Dim bx = dc.Positions(i2 * 3) - dc.Positions(i0 * 3)
            Dim by = dc.Positions(i2 * 3 + 1) - dc.Positions(i0 * 3 + 1)
            Dim bz = dc.Positions(i2 * 3 + 2) - dc.Positions(i0 * 3 + 2)
            Dim nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx
            For Each k In {i0, i1, i2}
                dc.Normals(k * 3 + 0) += nx
                dc.Normals(k * 3 + 1) += ny
                dc.Normals(k * 3 + 2) += nz
            Next
        Next
        For v = 0 To dc.VertexCount - 1
            Dim x = dc.Normals(v * 3), y = dc.Normals(v * 3 + 1), z = dc.Normals(v * 3 + 2)
            Dim L = CSng(Math.Sqrt(x * x + y * y + z * z))
            If L > 0.000001F Then
                dc.Normals(v * 3) = x / L
                dc.Normals(v * 3 + 1) = y / L
                dc.Normals(v * 3 + 2) = z / L
            Else
                dc.Normals(v * 3 + 1) = 1.0F
            End If
        Next
    End Sub

    ''' <summary>
    ''' Works out what a draw call is from the shape of its data.
    '''
    ''' Bend bones give themselves away twice over: they carry no texcoords at
    ''' all, and where they do vary they are chains of shared endpoints, so most
    ''' vertices are duplicates and many triangles collapse. The skin that bends
    ''' with them tiles its bark texture so its UVs run outside 0..1, while
    ''' foliage cards are atlas cutouts that stay inside it and carry the 2.0
    ''' marker in the vertex.
    ''' </summary>
    '''<summary>
    ''' Above this many distinct points a part is treated as surface geometry.
    ''' The largest hull found in the shipped library has 16.
    '''</summary>
    Private Const HULL_POINTS As Integer = 16

    '''<summary>
    ''' Vertex widths that name the declaration outright. Across the shipped
    ''' library not one part at 32, 36 or 40 bytes is foliage, and 931 of the
    ''' 1075 parts at 48 carry the foliage marker while none of the narrower ones
    ''' above 28 do. Judging bark by UV range instead used to fail on trunks that
    ''' barely tile - pheonixpalm_medium02 reaches 2.43 - and painted them with
    ''' the leaf atlas.
    '''</summary>
    '''<summary>Size of one render state record, and the gap it leaves before the table.</summary>
    Private Const STATE_SIZE As Integer = 680
    Private Const MIN_STATE_TAIL As Integer = 64
    Private Const MAX_STATE_TAIL As Integer = 4096

    Private Const SKIN_STRIDE As Integer = 32
    Private Const CARD_STRIDE As Integer = 44

    Private Shared Sub Classify(dc As DrawCall)
        Dim dupFrac = If(dc.VertexCount > 0, dc.DuplicateVerts / CSng(dc.VertexCount), 0.0F)
        Dim degFrac = If(dc.TriangleCount > 0, dc.DegenerateTris / CSng(dc.TriangleCount), 0.0F)
        Dim unique = dc.VertexCount - dc.DuplicateVerts
        Dim uniqueFrac = If(dc.VertexCount > 0, unique / CSng(dc.VertexCount), 1.0F)

        ' The 2.0 marker is a property of the foliage vertex declaration, so it
        ' is checked first. Deciding on UV range alone is not safe: foliage cards
        ' can bleed slightly past 1.0 (1.52 seen on Apple_7m_Flowers) and would
        ' then be mistaken for tiled bark and handed the wrong texture.
        If dc.HasMarker Then
            dc.Kind = PartKind.Foliage

        ElseIf dc.FlatUV OrElse uniqueFrac < 0.6F Then
            ' Structural, not surface. Both the skeleton and the hull are built
            ' from shared points so they collapse heavily; the hull is only a few
            ' capsules and ends up with far fewer distinct points than the
            ' skeleton, which has one per segment. Degeneracy is not a reliable
            ' test here - some trees ship a hull with no collapsed triangles.
            ' Not flagged Structural: this test is a judgement call, and a low
            ' LOD trunk can land the wrong side of it. HarmoniseKinds is allowed
            ' to overrule it from the other LODs; the point count test above is
            ' not up for debate that way.
            dc.Kind = If(unique <= 32, PartKind.Collision, PartKind.Bones)

        ElseIf dc.Stride >= CARD_STRIDE Then
            ' The card declaration. Every foliage part in the library is this wide
            ' and no bark part is, so the layout settles it even for the ones that
            ' do not carry the marker.
            dc.Kind = PartKind.Foliage

        ElseIf dc.Stride >= SKIN_STRIDE Then
            ' The skin declaration: position, a copy of it as the LOD morph
            ' target, texcoord, then the normal and tangent. Never used for
            ' foliage anywhere in the library.
            dc.Kind = PartKind.Skin

        ElseIf dc.UvMax > 2.5F Then
            ' Only the compact declarations reach here, and they are not
            ' distinctive enough to read from the stride alone. Fall back to UV
            ' range: bark tiles along the branch and runs well outside 0..1.
            dc.Kind = PartKind.Skin

        Else
            dc.Kind = PartKind.Unknown
        End If
    End Sub
End Class

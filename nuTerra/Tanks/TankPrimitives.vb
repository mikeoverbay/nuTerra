Imports System.IO
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' One draw-ready section pair of a tank part: the vertex stream uploaded
''' BYTE FOR BYTE, the index stream likewise, and the primitive-group records
''' that say which slice of each to draw. No vertex is decoded on the CPU; the
''' vertex array reads the raw bytes and the vertex shader unpacks the packed
''' normal, tangent and binormal. That is the whole point of this loader
''' against the exporter's: it draws, it does not build triangles.
''' </summary>
Public Class TankMesh
    Implements IDisposable

    Public Class Group
        Public startIndex As Integer
        Public nPrimitives As Integer
        Public startVertex As Integer
        Public nVertices As Integer
    End Class

    Public name As String              ' section base name, "" for a single-group file
    Public layout As TankVertexLayout
    Public vertexCount As Integer
    Public indexCount As Integer
    Public index32 As Boolean          ' "list32" indices; A88_M53_55 ships "list" = 16-bit
    Public groups As New List(Of Group)
    Public hasUV2 As Boolean

    Public vao As GLVertexArray
    Public vbo As GLBuffer
    Public ibo As GLBuffer
    Public uv2 As GLBuffer

    ''' <summary>
    ''' Attribute slots the tank shaders read. Fixed here and in the .vert:
    '''   0 position   float3
    '''   1 normal     4 x u8 normalised (8/8/8, fourth byte junk) - or float3 for "xyznuv"
    '''   2 uv0        float2
    '''   3 tangent    u32, packed 11/10/10, unpacked in the shader
    '''   4 binormal   u32, same
    '''   5 bone idx   4 x u8 integer (palette index * 3, the engine's convention)
    '''   6 bone wt    4 x u8 normalised
    '''   7 uv1        float2, from the .uv2 sidecar on binding 1, or interleaved
    ''' </summary>
    ''' <summary>Per-bone hub, indexed by PALETTE index. NaN where a bone
    ''' weights no vertices in this mesh.</summary>
    Public boneHubs As Vector3()

    ''' <summary>Per-bone rolling radius, indexed by PALETTE index. 0 where the
    ''' bone weights nothing, or nothing dominantly.</summary>
    Public boneRadii As Single()

    ''' <summary>UV0 extent, so the track band's running direction can be
    ''' measured instead of assumed.</summary>
    Public uvMin As Vector2 = New Vector2(Single.MaxValue, Single.MaxValue)
    Public uvMax As Vector2 = New Vector2(Single.MinValue, Single.MinValue)

    ''' <summary>UV units per METRE along the band's running axis, measured
    ''' from the mesh. 0 when it could not be measured.</summary>
    Public uvPerMetre As Single

    ''' <summary>
    ''' Each bone's hub, taken from the VERTICES IT WEIGHTS rather than from
    ''' the visual's node tree.
    '''
    ''' The node tree gives a position in ITS space, and getting from there to
    ''' the vertex data's space means knowing about three separate mirrors: the
    ''' _BlendBone offset that cancels its parent and sums to the origin,
    ''' FlipSkinnedZ because skinned streams are stored Z-reversed, and MirrorX
    ''' on the model matrix. Every one of those is a chance to pick the wrong
    ''' sign, and picking the wrong sign puts a wheel's pivot metres away so the
    ''' whole set swings about a common point instead of each wheel turning on
    ''' its axle. That happened twice.
    '''
    ''' A weighted centroid of the vertices bound to a bone has all three
    ''' already baked in, because they are baked into the positions being
    ''' averaged. For a road wheel that centroid IS the axle - the geometry is
    ''' a disc about it. No conventions to get right.
    '''
    ''' Weighted, not a plain mean: a vertex in the blend zone between two
    ''' wheels belongs mostly to one of them, and counting it equally would
    ''' drag both centroids toward the seam.
    ''' </summary>
    Public Sub ComputeBoneHubs(vertexBytes As Byte())
        boneHubs = Nothing
        If layout Is Nothing OrElse layout.offBoneIdx < 0 OrElse
           layout.offBoneW < 0 OrElse layout.offPos < 0 Then Return
        If vertexBytes Is Nothing OrElse vertexCount <= 0 Then Return

        Const MAXB As Integer = 128
        Dim acc(MAXB - 1) As Vector3
        Dim wsum(MAXB - 1) As Single
        ' Second pass needs the positions again; keep the dominant ones only.
        Dim rmax(MAXB - 1) As Single

        For v = 0 To vertexCount - 1
            Dim b = v * layout.stride
            If b + layout.stride > vertexBytes.Length Then Exit For
            If layout.offUV0 >= 0 Then
                Dim u0 = BitConverter.ToSingle(vertexBytes, b + layout.offUV0)
                Dim v0 = BitConverter.ToSingle(vertexBytes, b + layout.offUV0 + 4)
                uvMin = New Vector2(Math.Min(uvMin.X, u0), Math.Min(uvMin.Y, v0))
                uvMax = New Vector2(Math.Max(uvMax.X, u0), Math.Max(uvMax.Y, v0))
            End If
            Dim px = BitConverter.ToSingle(vertexBytes, b + layout.offPos)
            Dim py = BitConverter.ToSingle(vertexBytes, b + layout.offPos + 4)
            Dim pz = BitConverter.ToSingle(vertexBytes, b + layout.offPos + 8)
            Dim p As New Vector3(px, py, pz)
            For k = 0 To 3
                ' The byte is palette_index * 3 - SC_UBYTE4_REVERSE_PADDED,
                ' the same divide the vertex shader does.
                Dim bi = CInt(vertexBytes(b + layout.offBoneIdx + k)) \ 3
                Dim w = CSng(vertexBytes(b + layout.offBoneW + k)) / 255.0F
                If w <= 0.0F OrElse bi < 0 OrElse bi >= MAXB Then Continue For
                acc(bi) += p * w
                wsum(bi) += w
            Next
        Next

        ' ---- second pass: the rim -------------------------------------------
        ' The radius is the furthest a bone's own vertices reach from its hub in
        ' the YZ plane - the plane a wheel turns in. That is the rim, and the rim
        ' is the rolling radius.
        '
        ' Only vertices this bone OWNS, weight above a half. A vertex in the
        ' blend zone between two wheels is partly the neighbour's, and letting it
        ' count would stretch this wheel's rim out to the next one's and make
        ' every radius come out the same - which is what the node-tree lookup was
        ' already doing by falling back to a mean.
        Dim hubs(MAXB - 1) As Vector3
        For i = 0 To MAXB - 1
            hubs(i) = If(wsum(i) > 0.0001F, acc(i) / wsum(i),
                         New Vector3(Single.NaN, Single.NaN, Single.NaN))
        Next

        For v = 0 To vertexCount - 1
            Dim b = v * layout.stride
            If b + layout.stride > vertexBytes.Length Then Exit For
            Dim py = BitConverter.ToSingle(vertexBytes, b + layout.offPos + 4)
            Dim pz = BitConverter.ToSingle(vertexBytes, b + layout.offPos + 8)
            For k = 0 To 3
                Dim bi = CInt(vertexBytes(b + layout.offBoneIdx + k)) \ 3
                Dim w = CSng(vertexBytes(b + layout.offBoneW + k)) / 255.0F
                If w <= 0.5F OrElse bi < 0 OrElse bi >= MAXB Then Continue For
                If Single.IsNaN(hubs(bi).X) Then Continue For
                Dim dy = py - hubs(bi).Y
                Dim dz = pz - hubs(bi).Z
                Dim rr = CSng(Math.Sqrt(dy * dy + dz * dz))
                If rr > rmax(bi) Then rmax(bi) = rr
            Next
        Next

        Dim any = False
        For i = 0 To MAXB - 1
            If wsum(i) > 0.0001F Then any = True
        Next
        If any Then
            boneHubs = hubs
            boneRadii = rmax
        End If
    End Sub

    ''' <summary>
    ''' How many UV units the band's running axis covers per metre of surface.
    '''
    ''' THE TREAD HAS TO ADVANCE THE DISTANCE THE WHEELS ROLLED. The drive
    ''' sprocket's teeth sit in the chain's pin slots, so there is no slip to
    ''' model: whatever angle the sprocket turns, the chain moves theta * R
    ''' along its loop. TEPY states it the same way - "the chain is locked to
    ''' the W_D wheels, it can NOT move on those wheels".
    '''
    ''' The wheels already turn on theta = -s / R from one distance accumulator,
    ''' so scrolling the band by that same s IS the sprocket's rolling distance.
    ''' What was missing is the conversion from metres to UV: that was a slider
    ''' I set by eye at 2.5, which cannot be right for two vehicles at once -
    ''' the M53's band spans 38 V over its loop and a longer hull spans more.
    '''
    ''' It is measurable. V runs along the loop in proportion to arc length, so
    ''' an edge's |dV| over its |dPosition| IS units-per-metre. Taken over every
    ''' triangle edge with a real dV and MEDIANED, because a strip mesh has
    ''' plenty of edges running across the band rather than along it and those
    ''' carry almost no dV - a mean would be dragged anywhere by them, a median
    ''' lands on the population that actually runs lengthwise.
    ''' </summary>
    Public Sub MeasureUVScale(vertexBytes As Byte(), indexBytes As Byte())
        uvPerMetre = 0.0F
        If layout Is Nothing OrElse layout.offUV0 < 0 OrElse layout.offPos < 0 Then Return
        If vertexBytes Is Nothing OrElse indexBytes Is Nothing Then Return
        If vertexCount <= 0 OrElse indexCount < 3 Then Return

        ' Which axis is the run - the one with the greater spread. Same test
        ' the renderer uses to pick the scroll axis.
        Dim alongV = (uvMax.Y - uvMin.Y) >= (uvMax.X - uvMin.X)

        Dim isz = If(index32, 4, 2)
        Dim tris = Math.Min(indexCount \ 3, 20000)
        Dim vals As New List(Of Single)

        For t = 0 To tris - 1
            Dim idx(2) As Integer
            Dim bad = False
            For k = 0 To 2
                Dim io_ = (t * 3 + k) * isz
                If io_ + isz > indexBytes.Length Then bad = True : Exit For
                idx(k) = If(index32,
                            CInt(BitConverter.ToUInt32(indexBytes, io_)),
                            CInt(BitConverter.ToUInt16(indexBytes, io_)))
                If idx(k) < 0 OrElse idx(k) >= vertexCount Then bad = True : Exit For
            Next
            If bad Then Continue For

            For e = 0 To 2
                Dim a = idx(e), b = idx((e + 1) Mod 3)
                Dim ao = a * layout.stride, bo = b * layout.stride
                If ao + layout.stride > vertexBytes.Length Then Continue For
                If bo + layout.stride > vertexBytes.Length Then Continue For

                Dim av = BitConverter.ToSingle(vertexBytes, ao + layout.offUV0 + If(alongV, 4, 0))
                Dim bv = BitConverter.ToSingle(vertexBytes, bo + layout.offUV0 + If(alongV, 4, 0))
                Dim duv = Math.Abs(bv - av)
                If duv < 0.001F Then Continue For

                Dim dx = BitConverter.ToSingle(vertexBytes, bo + layout.offPos) -
                         BitConverter.ToSingle(vertexBytes, ao + layout.offPos)
                Dim dy = BitConverter.ToSingle(vertexBytes, bo + layout.offPos + 4) -
                         BitConverter.ToSingle(vertexBytes, ao + layout.offPos + 4)
                Dim dz = BitConverter.ToSingle(vertexBytes, bo + layout.offPos + 8) -
                         BitConverter.ToSingle(vertexBytes, ao + layout.offPos + 8)
                Dim dist = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
                If dist < 0.0005F Then Continue For

                vals.Add(duv / dist)
            Next
        Next

        If vals.Count < 16 Then Return
        vals.Sort()
        uvPerMetre = vals(vals.Count \ 2)
    End Sub

    Public Sub BuildVAO(vertexBytes As Byte(), indexBytes As Byte(), uv2Bytes As Byte())
        vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "tank_vbo_" & name)
        vbo.Storage(vertexBytes.Length, vertexBytes, BufferStorageFlags.None)
        ibo = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "tank_ibo_" & name)
        ibo.Storage(indexBytes.Length, indexBytes, BufferStorageFlags.None)

        vao = GLVertexArray.Create("tank_vao_" & name)
        vao.VertexBuffer(0, vbo, IntPtr.Zero, layout.stride)
        vao.ElementBuffer(ibo)

        vao.AttribFormat(0, 3, VertexAttribType.Float, False, layout.offPos)
        vao.AttribBinding(0, 0) : vao.EnableAttrib(0)

        If layout.realNormals Then
            vao.AttribFormat(1, 3, VertexAttribType.Float, False, layout.offNormal)
        Else
            vao.AttribFormat(1, 4, VertexAttribType.UnsignedByte, True, layout.offNormal)
        End If
        vao.AttribBinding(1, 0) : vao.EnableAttrib(1)

        vao.AttribFormat(2, 2, VertexAttribType.Float, False, layout.offUV0)
        vao.AttribBinding(2, 0) : vao.EnableAttrib(2)

        If layout.offTangent >= 0 Then
            vao.AttribIFormat(3, 1, VertexAttribType.UnsignedInt, layout.offTangent)
            vao.AttribBinding(3, 0) : vao.EnableAttrib(3)
            vao.AttribIFormat(4, 1, VertexAttribType.UnsignedInt, layout.offBinormal)
            vao.AttribBinding(4, 0) : vao.EnableAttrib(4)
        End If
        If layout.offBoneIdx >= 0 Then
            vao.AttribIFormat(5, 4, VertexAttribType.UnsignedByte, layout.offBoneIdx)
            vao.AttribBinding(5, 0) : vao.EnableAttrib(5)
        End If
        If layout.offBoneW >= 0 Then
            vao.AttribFormat(6, 4, VertexAttribType.UnsignedByte, True, layout.offBoneW)
            vao.AttribBinding(6, 0) : vao.EnableAttrib(6)
        End If
        If layout.offUV1 >= 0 Then
            vao.AttribFormat(7, 2, VertexAttribType.Float, False, layout.offUV1)
            vao.AttribBinding(7, 0) : vao.EnableAttrib(7)
        ElseIf uv2Bytes IsNot Nothing Then
            ' Sidecar: parallel to the vertex stream, 8 bytes per vertex, on
            ' its own binding. Its preamble mirrors the vertex section's.
            uv2 = GLBuffer.Create(BufferTarget.ArrayBuffer, "tank_uv2_" & name)
            uv2.Storage(uv2Bytes.Length, uv2Bytes, BufferStorageFlags.None)
            vao.VertexBuffer(1, uv2, IntPtr.Zero, 8)
            vao.AttribFormat(7, 2, VertexAttribType.Float, False, 0)
            vao.AttribBinding(7, 1) : vao.EnableAttrib(7)
            hasUV2 = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        vao?.Dispose() : vbo?.Dispose() : ibo?.Dispose() : uv2?.Dispose()
    End Sub
End Class

Public Module TankPrimitives

    ''' <summary>
    ''' Read one .primitives_processed out of the packages into draw-ready
    ''' meshes: one TankMesh per vertices/indices pair, matched by base name.
    ''' Every section whose format this loader does not know is logged and
    ''' skipped, never guessed at.
    ''' </summary>
    Public Function LoadMeshes(path As String) As List(Of TankMesh)
        Dim out As New List(Of TankMesh)
        Dim data = TankFiles.ReadAll(path)
        If data Is Nothing Then
            LogThis("tank: primitives not found {0}", path)
            Return out
        End If

        Dim sections = TankFormats.ReadSectionTable(data)
        LogThis("tank: {0} - {1} bytes, {2} section(s)", IO.Path.GetFileName(path), data.Length, sections.Count)

        ' Group by base name.
        Dim byBase As New Dictionary(Of String, Dictionary(Of String, TankSection))
        For Each s In sections
            Dim base = "", kind = ""
            TankFormats.SplitSectionName(s.name, base, kind)
            If Not byBase.ContainsKey(base) Then byBase(base) = New Dictionary(Of String, TankSection)
            byBase(base)(kind) = s
        Next

        For Each kv In byBase
            Dim base = kv.Key
            Dim secs = kv.Value
            If Not secs.ContainsKey("vertices") OrElse Not secs.ContainsKey("indices") Then
                LogThis("tank:   [{0}] has no vertices/indices pair - skipped", base)
                Continue For
            End If
            Dim vs = secs("vertices"), ix = secs("indices")

            ' ---- vertices ----
            Dim fmt = TankFormats.ReadFormatString(data, vs.offset)
            Dim layout = TankVertexLayout.Parse(fmt)
            If layout Is Nothing Then
                LogThis("tank:   [{0}] unknown vertex format '{1}' - skipped", base, fmt)
                Continue For
            End If
            Dim vBody = vs.offset + TankFormats.VertexBodyOffset(layout.bpvt)
            Dim vBytes = vs.size - TankFormats.VertexBodyOffset(layout.bpvt)
            If vBytes <= 0 OrElse vBytes Mod layout.stride <> 0 Then
                LogThis("tank:   [{0}] vertex body {1} bytes is not a multiple of stride {2} for '{3}' - skipped", base, vBytes, layout.stride, fmt)
                Continue For
            End If
            Dim vCount = vBytes \ layout.stride

            ' ---- indices: 64-byte format, u32 count, u16 group count, 2 pad, data, then groups ----
            Dim ifmt = TankFormats.ReadFormatString(data, ix.offset)
            Dim index32 = ifmt.StartsWith("list32")
            Dim nIdx = BitConverter.ToInt32(data, ix.offset + 64)
            Dim nGroups = CInt(BitConverter.ToUInt16(data, ix.offset + 68))
            Dim idxSize = If(index32, 4, 2)
            Dim iBody = ix.offset + 72
            Dim iBytes = nIdx * idxSize
            Dim gAt = iBody + iBytes
            If gAt + nGroups * 16 > ix.offset + ix.size Then
                LogThis("tank:   [{0}] index section too small for {1} indices + {2} groups - skipped", base, nIdx, nGroups)
                Continue For
            End If

            Dim m As New TankMesh With {.name = base, .layout = layout, .vertexCount = vCount, .indexCount = nIdx, .index32 = index32}
            For g = 0 To nGroups - 1
                Dim o = gAt + g * 16
                m.groups.Add(New TankMesh.Group With {
                    .startIndex = BitConverter.ToInt32(data, o),
                    .nPrimitives = BitConverter.ToInt32(data, o + 4),
                    .startVertex = BitConverter.ToInt32(data, o + 8),
                    .nVertices = BitConverter.ToInt32(data, o + 12)})
            Next

            ' ---- uv2 sidecar, when present: same preamble shape as the vertex section ----
            Dim uv2Bytes As Byte() = Nothing
            If secs.ContainsKey("uv2") Then
                Dim u = secs("uv2")
                Dim uBody = TankFormats.VertexBodyOffset(layout.bpvt)
                If (u.size - uBody) = vCount * 8 Then
                    uv2Bytes = New Byte(vCount * 8 - 1) {}
                    Array.Copy(data, u.offset + uBody, uv2Bytes, 0, vCount * 8)
                Else
                    LogThis("tank:   [{0}] uv2 sidecar {1} bytes does not fit {2} verts - ignored", base, u.size, vCount)
                End If
            End If

            Dim vSlice(vBytes - 1) As Byte
            Array.Copy(data, vBody, vSlice, 0, vBytes)
            Dim iSlice(iBytes - 1) As Byte
            Array.Copy(data, iBody, iSlice, 0, iBytes)
            ' Before the bytes go out of scope - BuildVAO uploads and drops them.
            m.ComputeBoneHubs(vSlice)
            m.MeasureUVScale(vSlice, iSlice)
            m.BuildVAO(vSlice, iSlice, uv2Bytes)

            LogThis("tank:   [{0}] '{1}' stride {2} verts {3} | '{4}' indices {5} groups {6}{7}",
                    If(base = "", "(bare)", base), fmt, layout.stride, vCount, ifmt, nIdx, nGroups,
                    If(m.hasUV2, " +uv2", ""))
            out.Add(m)
        Next
        Return out
    End Function
End Module

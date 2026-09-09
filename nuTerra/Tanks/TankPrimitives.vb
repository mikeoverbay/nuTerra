Imports System.IO
Imports OpenTK.Graphics.OpenGL4

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
            m.BuildVAO(vSlice, iSlice, uv2Bytes)

            LogThis("tank:   [{0}] '{1}' stride {2} verts {3} | '{4}' indices {5} groups {6}{7}",
                    If(base = "", "(bare)", base), fmt, layout.stride, vCount, ifmt, nIdx, nGroups,
                    If(m.hasUV2, " +uv2", ""))
            out.Add(m)
        Next
        Return out
    End Function
End Module

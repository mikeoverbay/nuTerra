Imports System.IO
Imports System.Text

''' <summary>
''' The byte-level facts of a WoT .primitives_processed, and nothing else.
''' Reads the section table and turns a vertex format string into offsets and
''' a stride. Kept apart from ModelLoaders\PrimitiveLoader.vb on purpose: this
''' module never touches the map loader and the map loader never touches it.
'''
''' Every rule here was read off Tank Exporter (ModTankLoader.vb, VB) and Tank
''' Exporter PY (VISUAL_PROCESSED_FORMAT.md) and checked against the section
''' sizes of A88_M53_55 - see docs\Tank Docs\03_formats_verified.md.
''' </summary>
Public Class TankSection
    Public name As String
    Public offset As Integer   ' body start, absolute in the file
    Public size As Integer     ' body size, excluding the 4-byte padding
End Class

''' <summary>Where each attribute sits inside one vertex, and how wide a vertex is.</summary>
Public Class TankVertexLayout
    Public format As String
    Public bpvt As Boolean
    Public realNormals As Boolean   ' ONLY the exact string "xyznuv" ships float normals
    Public stride As Integer
    Public offPos As Integer = -1
    Public offNormal As Integer = -1
    Public offUV0 As Integer = -1
    Public offUV1 As Integer = -1
    Public offBoneIdx As Integer = -1
    Public offBoneW As Integer = -1
    Public offTangent As Integer = -1
    Public offBinormal As Integer = -1

    ''' <summary>
    ''' Walk the format string left to right. Token widths: xyz 12, n 4 (12 for
    ''' the one real-normal format), uv 8 each, iii 4, ww 4, tb 8. A token this
    ''' does not know returns Nothing - the caller logs and skips the section
    ''' rather than guessing a stride and reading garbage.
    ''' </summary>
    Public Shared Function Parse(fmt As String) As TankVertexLayout
        Dim L As New TankVertexLayout With {.format = fmt}
        Dim s = fmt
        If s.StartsWith("BPVT") Then
            L.bpvt = True
            s = s.Substring(4)
        End If
        L.realNormals = (s = "xyznuv")
        Dim o = 0
        Dim uvSeen = 0
        While s.Length > 0
            If s.StartsWith("xyz") Then
                L.offPos = o : o += 12 : s = s.Substring(3)
            ElseIf s.StartsWith("nuv") OrElse s.StartsWith("n") Then
                L.offNormal = o : o += If(L.realNormals, 12, 4) : s = s.Substring(1)
            ElseIf s.StartsWith("uv") Then
                If uvSeen = 0 Then L.offUV0 = o Else L.offUV1 = o
                uvSeen += 1 : o += 8 : s = s.Substring(2)
            ElseIf s.StartsWith("iii") Then
                L.offBoneIdx = o : o += 4 : s = s.Substring(3)
            ElseIf s.StartsWith("ww") Then
                L.offBoneW = o : o += 4 : s = s.Substring(2)
            ElseIf s.StartsWith("tb") Then
                L.offTangent = o : L.offBinormal = o + 4 : o += 8 : s = s.Substring(2)
            Else
                Return Nothing
            End If
        End While
        L.stride = o
        If L.offPos < 0 OrElse L.offNormal < 0 OrElse L.offUV0 < 0 Then Return Nothing
        Return L
    End Function
End Class

Public Module TankFormats

    ''' <summary>
    ''' The section table sits at the END of the file: the last 4 bytes are an
    ''' offset back from EOF to the table. Each entry is size:u32, 16 unused
    ''' bytes, namelen:u32, the name, then padding to 4. Bodies start at offset
    ''' 4 and follow in table order, each padded to a 4-byte boundary. Entries
    ''' carry no offsets - the only way to place a body is to add up every size
    ''' before it.
    ''' </summary>
    Public Function ReadSectionTable(data As Byte()) As List(Of TankSection)
        Dim out As New List(Of TankSection)
        Dim n = data.Length
        If n < 8 Then Return out
        Dim back = BitConverter.ToInt32(data, n - 4)
        Dim p = n - 4 - back
        If p < 4 OrElse p >= n - 4 Then Return out
        Dim loc = 4
        While p + 24 <= n - 4
            Dim size = BitConverter.ToInt32(data, p)
            Dim nameLen = BitConverter.ToInt32(data, p + 20)
            If nameLen < 0 OrElse p + 24 + nameLen > n Then Exit While
            Dim name = Encoding.ASCII.GetString(data, p + 24, nameLen).TrimEnd(ControlChars.NullChar)
            p += 24 + nameLen + ((4 - nameLen Mod 4) Mod 4)
            out.Add(New TankSection With {.name = name, .offset = loc, .size = size})
            loc += size
            loc += (4 - loc Mod 4) Mod 4
        End While
        Return out
    End Function

    ''' <summary>The 64-byte format string at the head of a vertex or index section.</summary>
    Public Function ReadFormatString(data As Byte(), at As Integer) As String
        Dim s = Encoding.ASCII.GetString(data, at, 64)
        Dim z = s.IndexOf(ControlChars.NullChar)
        Return If(z >= 0, s.Substring(0, z), s)
    End Function

    ''' <summary>
    ''' Where the vertex stream begins. Non-BPVT: 64-byte format + u32 count =
    ''' 68. BPVT: 68-byte primary + 64-byte secondary + u32 count = 136. The
    ''' count field is NOT trusted (it reads 0 on every A88_M53_55 section);
    ''' the caller derives the count from size and stride, which is exact.
    ''' </summary>
    Public Function VertexBodyOffset(bpvt As Boolean) As Integer
        Return If(bpvt, 136, 68)
    End Function

    ''' <summary>Split "base.vertices" into ("base", "vertices"); a bare "vertices" gives ("", "vertices").</summary>
    Public Sub SplitSectionName(name As String, ByRef base As String, ByRef kind As String)
        Dim dot = name.LastIndexOf("."c)
        If dot < 0 Then
            base = "" : kind = name
        Else
            base = name.Substring(0, dot) : kind = name.Substring(dot + 1)
        End If
    End Sub
End Module

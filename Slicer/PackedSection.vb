Imports System.Text
Imports OpenTK.Mathematics

''' <summary>
''' A BigWorld "packed section" - the binary form of what was XML before the
''' game shipped it. A .model file is one of these; so are .visual_processed
''' and the rest of the _processed family.
'''
''' This is a standalone transcription of nuTerra's ResMgr\packed_section.vb,
''' deliberately NOT shared with it - the same rule SrtViewer follows. The
''' Slicer has to run without nuTerra, and a format reader that can be proven
''' out in seconds beats one tangled into a map load.
'''
''' Two differences from the ResMgr version, both on purpose:
'''
''' * It reads a Byte() with its own cursor rather than a BinaryReader.
'''   BinaryReader.ReadChar decodes through an Encoding, so a byte above 127 in
'''   a name or a string comes back as U+FFFD or eats the byte after it. The
'''   dictionary is plain bytes and has to be treated as such.
''' * It builds a small typed tree instead of an XmlDocument. Nothing here wants
'''   XPath, and 4,962 XmlDocuments per scan is a lot of garbage for no gain.
'''
''' The layout, which is what actually matters:
'''
'''     u32      magic 62A14E45
'''     u8       version
'''     strings  the dictionary, NUL terminated, ended by an empty string
'''     element  the root
'''
''' and an element is
'''
'''     i16      number of children
'''     u32      own data descriptor
'''     i16+u32  one name index + data descriptor per child
'''     bytes    the data blobs, back to back
'''
''' A descriptor packs an END OFFSET in its low 28 bits and a TYPE in its top 4.
''' The offsets are ends, not starts, and they are relative to the first blob -
''' so a blob's length is its own end minus the previous end. There are no start
''' offsets anywhere in the file. Read the blobs in order or not at all.
''' </summary>
Public Enum PsKind
    Element = 0
    Str = 1
    IntNum = 2
    Floats = 3
    Flag = 4
    Blob = 5
End Enum

Public Class PsNode
    Public Name As String = ""
    Public Kind As PsKind = PsKind.Element
    Public Text As String = ""
    Public Number As Long = 0
    Public Bool As Boolean = False
    Public Floats As Single() = Array.Empty(Of Single)()
    Public Blob As Byte() = Array.Empty(Of Byte)()
    Public ReadOnly Children As New List(Of PsNode)

    ''' <summary>First child with this name, or Nothing. Case insensitive
    ''' because the shipped files are not consistent: three building models
    ''' spell it "nodefullvisual" where the other 1,996 spell it
    ''' "nodefullVisual".</summary>
    Public Function Child(childName As String) As PsNode
        For Each c In Children
            If String.Equals(c.Name, childName, StringComparison.OrdinalIgnoreCase) Then Return c
        Next
        Return Nothing
    End Function

    Public Function ChildText(childName As String) As String
        Dim c = Child(childName)
        If c Is Nothing Then Return Nothing
        Return c.Text
    End Function

    ''' <summary>
    ''' A 3-vector, however this particular file chose to store it.
    '''
    ''' Most write it as three float32 (type 3), but some write the same value
    ''' as TEXT - "-8.299856 -0.950243 -4.354095" - and a reader that only
    ''' handles the float form silently drops those. Measured over the shipped
    ''' building library, so it is not a hypothetical.
    ''' </summary>
    Public Function AsVector3() As Vector3?
        If Kind = PsKind.Floats AndAlso Floats.Length >= 3 Then
            Return New Vector3(Floats(0), Floats(1), Floats(2))
        End If
        If Kind = PsKind.Str AndAlso Not String.IsNullOrWhiteSpace(Text) Then
            Dim bits = Text.Split(New Char() {" "c, vbTab, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            If bits.Length >= 3 Then
                Dim x, y, z As Single
                Dim inv = Globalization.CultureInfo.InvariantCulture
                Dim st = Globalization.NumberStyles.Float
                If Single.TryParse(bits(0), st, inv, x) AndAlso
                   Single.TryParse(bits(1), st, inv, y) AndAlso
                   Single.TryParse(bits(2), st, inv, z) Then
                    Return New Vector3(x, y, z)
                End If
            End If
        End If
        Return Nothing
    End Function
End Class

Public NotInheritable Class PackedSection

    Public Const MAGIC As UInteger = &H62A14E45UI

    Private ReadOnly buf As Byte()
    Private pos As Integer
    Private dict As List(Of String)

    Private Sub New(bytes As Byte())
        buf = bytes
        pos = 0
    End Sub

    ''' <summary>Parse a packed section. Returns Nothing if the magic is wrong,
    ''' which is the cheap way to tell a packed file from a loose XML one.</summary>
    Public Shared Function Parse(bytes As Byte()) As PsNode
        If bytes Is Nothing OrElse bytes.Length < 8 Then Return Nothing
        If BitConverter.ToUInt32(bytes, 0) <> MAGIC Then Return Nothing
        Dim ps As New PackedSection(bytes)
        ps.pos = 4
        ps.pos += 1                      ' version byte
        ps.dict = ps.ReadDictionary()
        Dim root = ps.ReadElement()
        root.Name = "root"
        Return root
    End Function

    Private Function ReadDictionary() As List(Of String)
        Dim out As New List(Of String)
        While True
            Dim s = ReadCString()
            If s.Length = 0 Then Exit While
            out.Add(s)
        End While
        Return out
    End Function

    ''' <summary>Latin-1 on purpose: the dictionary is bytes, and every byte has
    ''' to survive the trip so a name compares equal to the one in the file.</summary>
    Private Function ReadCString() As String
        Dim start = pos
        While pos < buf.Length AndAlso buf(pos) <> 0
            pos += 1
        End While
        Dim s = Encoding.Latin1.GetString(buf, start, pos - start)
        If pos < buf.Length Then pos += 1      ' step over the NUL
        Return s
    End Function

    Private Function ReadI16() As Integer
        Dim v = BitConverter.ToInt16(buf, pos)
        pos += 2
        Return v
    End Function

    ''' <summary>Returns the descriptor's end offset, and gives back its type.</summary>
    Private Function ReadDescriptor(ByRef kind As PsKind) As Integer
        Dim v = BitConverter.ToInt32(buf, pos)
        pos += 4
        kind = CType((v >> 28) And &HF, PsKind)
        Return v And &HFFFFFFF
    End Function

    Private Function ReadElement() As PsNode
        Dim node As New PsNode With {.Kind = PsKind.Element}

        Dim childCount = ReadI16()
        Dim selfKind As PsKind
        Dim selfEnd = ReadDescriptor(selfKind)

        ' The child table has to be read out in full BEFORE any blob, because
        ' the blobs start where the table stops.
        Dim names(Math.Max(childCount - 1, 0)) As Integer
        Dim ends(Math.Max(childCount - 1, 0)) As Integer
        Dim kinds(Math.Max(childCount - 1, 0)) As PsKind
        For i = 0 To childCount - 1
            names(i) = ReadI16()
            ends(i) = ReadDescriptor(kinds(i))
        Next

        ' The element's own value comes first and runs from 0 to selfEnd.
        ReadInto(node, selfKind, selfEnd)

        Dim off = selfEnd
        For i = 0 To childCount - 1
            Dim child As New PsNode With {
                .Name = If(names(i) >= 0 AndAlso names(i) < dict.Count, dict(names(i)), "?" & names(i)),
                .Kind = kinds(i)}
            ReadInto(child, kinds(i), ends(i) - off)
            node.Children.Add(child)
            off = ends(i)
        Next

        Return node
    End Function

    ''' <summary>Decode one blob of `length` bytes at the cursor into `node`.
    ''' A nested element ignores the length and reads itself - it knows its own
    ''' size, and the length is only there to tell the NEXT blob where it starts.</summary>
    Private Sub ReadInto(node As PsNode, kind As PsKind, length As Integer)
        Select Case kind
            Case PsKind.Element
                Dim nested = ReadElement()
                node.Kind = PsKind.Element
                node.Children.AddRange(nested.Children)
                node.Text = nested.Text
                node.Floats = nested.Floats
                node.Number = nested.Number

            Case PsKind.Str
                node.Text = Encoding.Latin1.GetString(buf, pos, length)
                pos += length

            Case PsKind.IntNum
                ' Width is whatever the blob is - 0, 1, 2, 4 or 8 bytes, signed.
                Dim v As Long = 0
                If length > 0 Then
                    v = CLng(CSByte(buf(pos + length - 1)))          ' sign from the top byte
                    For i = length - 2 To 0 Step -1
                        v = (v << 8) Or buf(pos + i)
                    Next
                End If
                node.Number = v
                pos += length

            Case PsKind.Floats
                Dim n = length \ 4
                Dim f(Math.Max(n - 1, 0)) As Single
                For i = 0 To n - 1
                    f(i) = BitConverter.ToSingle(buf, pos + i * 4)
                Next
                If n = 0 Then f = Array.Empty(Of Single)()
                node.Floats = f
                pos += length

            Case PsKind.Flag
                node.Bool = (length = 1 AndAlso buf(pos) = 1)
                pos += length

            Case PsKind.Blob
                Dim b(Math.Max(length - 1, 0)) As Byte
                If length > 0 Then Array.Copy(buf, pos, b, 0, length)
                node.Blob = If(length > 0, b, Array.Empty(Of Byte)())
                pos += length

            Case Else
                pos += length
        End Select
    End Sub
End Class

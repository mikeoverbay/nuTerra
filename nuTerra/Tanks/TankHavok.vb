Imports System.Text

''' <summary>
''' A Havok TAGFILE, opened far enough to find a tank's armour plates.
'''
''' WHY THIS EXISTS. A tank's armour thickness is in two files that have to be
''' joined by name: item_defs carries the millimetres as named plates
''' (armor_1=135, leftTrack=20), and collision_client/&lt;part&gt;.havok carries the
''' collision geometry with an hknpMaterial per shape named for its plate. See
''' nuterra_work/tank_armour.md for how that was established. This is the
''' reader for the second half.
'''
''' STARTED 2026-09-26 at the owner's ask - "can you start so I can look" - so
''' it is deliberately incremental and says what it does NOT yet know rather
''' than guessing. Four earlier instruments in this session produced confident
''' false negatives by assuming a layout, and a binary format is exactly where
''' that happens.
'''
''' THE SECTION FRAME IS CERTAIN, because the file's own first eight bytes prove
''' it. hull.havok opens 00 05 BA C4 54 41 47 30: 0x0005BAC4 is 375,492, which
''' is the file's exact length, and 54 41 47 30 is "TAG0". The next section
''' reads 40 00 00 10 53 44 4B 56 - 0x40000010, bit 30 set, length 16, "SDKV" -
''' and its eight bytes of content are "20200200". So:
'''
'''   u32 big-endian   bit 30 set = raw data, clear = subsections
'''                    low 30 bits = the section's whole length, header included
'''   char[4]          the tag
'''   length - 8 bytes the content
'''
''' Nothing below that frame is assumed. The type table is decoded with a
''' variable-length integer scheme that is CHECKED against strings already
''' known to be in the file, and if the check fails this says so instead of
''' returning nonsense.
''' </summary>
Public Module TankHavok

    ''' <summary>One section of a tagfile: a four-character tag, and either raw
    ''' bytes or children.</summary>
    Public Class Section
        Public tag As String = ""
        Public start As Integer          ' offset of the section header
        Public length As Integer         ' whole length, header included
        Public isData As Boolean
        Public ReadOnly children As New List(Of Section)

        Public ReadOnly Property dataStart As Integer
            Get
                Return start + 8
            End Get
        End Property

        Public ReadOnly Property dataLength As Integer
            Get
                Return length - 8
            End Get
        End Property

        ''' <summary>The first child with this tag, or Nothing.</summary>
        Public Function Child(t As String) As Section
            For Each c In children
                If c.tag = t Then Return c
            Next
            Return Nothing
        End Function

        ''' <summary>Depth-first search for a tag at any depth.</summary>
        Public Function Find(t As String) As Section
            If tag = t Then Return Me
            For Each c In children
                Dim hit = c.Find(t)
                If hit IsNot Nothing Then Return hit
            Next
            Return Nothing
        End Function
    End Class

    Private Function BE32(b As Byte(), at As Integer) As UInteger
        Return (CUInt(b(at)) << 24) Or (CUInt(b(at + 1)) << 16) Or
               (CUInt(b(at + 2)) << 8) Or CUInt(b(at + 3))
    End Function

    ''' <summary>
    ''' Walk the section tree.
    '''
    ''' A SECTION WHOSE LENGTH DOES NOT FIT ITS PARENT STOPS THE WALK rather
    ''' than being clamped. A bad length means the frame assumption is wrong for
    ''' this file, and carrying on would invent a tree; the caller reports how
    ''' far it got, which is a fact, instead of a structure that is not there.
    ''' </summary>
    Public Function Parse(bytes As Byte(), ByRef problem As String) As Section
        problem = ""
        If bytes Is Nothing OrElse bytes.Length < 8 Then
            problem = "shorter than one section header"
            Return Nothing
        End If
        Dim root = ReadSection(bytes, 0, bytes.Length, problem, 0)
        If root IsNot Nothing AndAlso root.tag <> "TAG0" Then
            problem = "root is [" & root.tag & "], expected TAG0"
        End If
        Return root
    End Function

    Private Function ReadSection(b As Byte(), at As Integer, limit As Integer,
                                 ByRef problem As String, depth As Integer) As Section
        If depth > 8 Then
            problem = "nested deeper than 8 - refusing to recurse further"
            Return Nothing
        End If
        If at + 8 > limit Then
            problem = String.Format("header at {0} runs past the end ({1})", at, limit)
            Return Nothing
        End If

        Dim raw = BE32(b, at)
        Dim s As New Section With {
            .start = at,
            .isData = (raw And &H40000000UI) <> 0UI,
            .length = CInt(raw And &H3FFFFFFFUI),
            .tag = Encoding.ASCII.GetString(b, at + 4, 4)}

        If s.length < 8 Then
            problem = String.Format("section [{0}] at {1} claims length {2}",
                                    s.tag, at, s.length)
            Return Nothing
        End If
        If at + s.length > limit Then
            problem = String.Format("section [{0}] at {1} claims {2} bytes, only {3} left",
                                    s.tag, at, s.length, limit - at)
            Return Nothing
        End If

        If Not s.isData Then
            Dim p = s.dataStart
            Dim stop_ = at + s.length
            While p + 8 <= stop_
                Dim kid = ReadSection(b, p, stop_, problem, depth + 1)
                If kid Is Nothing Then Return s     ' keep what we have
                s.children.Add(kid)
                p += kid.length
            End While
        End If
        Return s
    End Function

    ''' <summary>
    ''' The type-name table out of TNA1, and whether it can be trusted.
    '''
    ''' TNA1 is a count followed by that many entries, each a string index into
    ''' the TST1 table plus a template-parameter count. Both are written with
    ''' Havok's variable-length integer, which is the one piece of this that is
    ''' not proven by the file's own header - so the caller is handed `ok` and
    ''' should check it. The test is not "did it parse": it is whether the names
    ''' that come out include hknpMaterial, which a strings pass already proved
    ''' is in there. A decode that is subtly wrong produces plausible-looking
    ''' short strings, so parsing cleanly is not evidence.
    ''' </summary>
    Public Function TypeNames(root As Section, b As Byte(),
                              ByRef ok As Boolean, ByRef note As String) As List(Of String)
        ok = False
        note = ""
        Dim names As New List(Of String)

        Dim tst = root.Find("TST1")
        If tst Is Nothing Then tst = root.Find("TSTR")
        Dim tna = root.Find("TNA1")
        If tna Is Nothing Then tna = root.Find("TNAM")
        If tst Is Nothing OrElse tna Is Nothing Then
            note = "no " & If(tst Is Nothing, "TST1/TSTR", "TNA1/TNAM") & " section"
            Return names
        End If

        ' TST1 is NUL-separated strings, start to end. Nothing variable-length
        ' about it, so this part cannot be got wrong.
        Dim pool As New List(Of String)
        Dim cur As New StringBuilder()
        For i = tst.dataStart To tst.dataStart + tst.dataLength - 1
            If b(i) = 0 Then
                pool.Add(cur.ToString())
                cur.Clear()
            Else
                cur.Append(ChrW(b(i)))
            End If
        Next
        If cur.Length > 0 Then pool.Add(cur.ToString())

        Dim p = tna.dataStart
        Dim last = tna.dataStart + tna.dataLength
        Dim count = ReadVarInt(b, p, last)
        If count < 0 OrElse count > 200000 Then
            note = "TNA1 count reads " & count
            Return names
        End If

        For i = 1 To count - 1                     ' entry 0 is the void type
            If p >= last Then Exit For
            Dim idx = ReadVarInt(b, p, last)
            Dim tmpl = ReadVarInt(b, p, last)
            If idx < 0 OrElse idx >= pool.Count Then
                note = String.Format("entry {0} names string {1} of {2}", i, idx, pool.Count)
                Return names
            End If
            names.Add(pool(idx))
            ' Skip the template parameters: each is a string index and a value.
            For t = 1 To Math.Max(0, tmpl)
                ReadVarInt(b, p, last)
                ReadVarInt(b, p, last)
            Next
        Next

        ' THE CHECK. hknpMaterial is in every tank part's file - a strings pass
        ' found it before this parser existed - so its absence means the decode
        ' is wrong however tidy the output looks.
        For Each n In names
            If n = "hknpMaterial" Then
                ok = True
                Exit For
            End If
        Next
        If Not ok Then note = "decoded " & names.Count & " name(s), none of them hknpMaterial"
        Return names
    End Function

    ''' <summary>
    ''' Havok's variable-length integer. -1 on a malformed read.
    '''
    ''' The leading bits say the width: 0xxxxxxx is one byte, 10xxxxxx two,
    ''' 110xxxxx three, 1110xxxx five. NOT VERIFIED INDEPENDENTLY - it is the
    ''' scheme the format is documented to use, and TypeNames above tests the
    ''' result rather than trusting it.
    ''' </summary>
    Private Function ReadVarInt(b As Byte(), ByRef p As Integer, last As Integer) As Integer
        If p >= last Then Return -1
        Dim b0 = CInt(b(p))

        If (b0 And &H80) = 0 Then
            p += 1
            Return b0
        End If
        If (b0 And &HC0) = &H80 Then
            If p + 1 >= last Then Return -1
            Dim v = ((b0 And &H3F) << 8) Or CInt(b(p + 1))
            p += 2
            Return v
        End If
        If (b0 And &HE0) = &HC0 Then
            If p + 2 >= last Then Return -1
            Dim v = ((b0 And &H1F) << 16) Or (CInt(b(p + 1)) << 8) Or CInt(b(p + 2))
            p += 3
            Return v
        End If
        If (b0 And &HF0) = &HE0 Then
            If p + 4 >= last Then Return -1
            Dim v = ((b0 And &HF) << 24) Or (CInt(b(p + 1)) << 16) Or
                    (CInt(b(p + 2)) << 8) Or CInt(b(p + 3))
            p += 5
            Return v
        End If
        Return -1
    End Function

    ''' <summary>
    ''' Say what is in one .havok, at whatever depth the parse actually reached.
    '''
    ''' The point of the log is that it can FAIL VISIBLY. Every step reports
    ''' either a number or why it has none, so a wrong assumption shows up as a
    ''' complaint rather than as a quietly empty list.
    ''' </summary>
    Public Sub Report(label As String, path As String)
        Dim bytes = TankFiles.ReadAll(path)
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            LogThis("tank havok: [{0}] {1} - not in the vehicle packages", label, path)
            Return
        End If

        Dim problem As String = ""
        Dim root = Parse(bytes, problem)
        If root Is Nothing Then
            LogThis("tank havok: [{0}] {1:N0} bytes - PARSE FAILED: {2}",
                    label, bytes.Length, problem)
            Return
        End If

        LogThis("tank havok: [{0}] {1:N0} bytes, root [{2}] claims {3:N0}{4}",
                label, bytes.Length, root.tag, root.length,
                If(root.length = bytes.Length, " - matches the file", " - DOES NOT MATCH"))
        If problem <> "" Then
            LogThis("tank havok: [{0}]   walk stopped early: {1}", label, problem)
        End If
        DumpTree(label, root, "  ")

        Dim sdkv = root.Find("SDKV")
        If sdkv IsNot Nothing Then
            LogThis("tank havok: [{0}]   SDKV = {1}", label,
                    Encoding.ASCII.GetString(bytes, sdkv.dataStart, sdkv.dataLength))
        End If

        Dim ok As Boolean, note As String = ""
        Dim names = TypeNames(root, bytes, ok, note)
        If Not ok Then
            LogThis("tank havok: [{0}]   type table NOT TRUSTED - {1}", label, note)
        Else
            Dim hkn = 0
            For Each n In names
                If n.StartsWith("hknp") Then hkn += 1
            Next
            LogThis("tank havok: [{0}]   {1} type name(s), {2} hknp*, hknpMaterial found",
                    label, names.Count, hkn)
            ' THE CLASS LIST IS THE MAP FOR WHAT COMES NEXT. Which shape classes
            ' a part actually uses decides what has to be decoded to reach the
            ' triangles and their material indices - printed rather than assumed.
            Dim shapes As New List(Of String)
            For Each n In names
                If n.StartsWith("hknp") AndAlso
                   (n.Contains("Shape") OrElse n.Contains("Material") OrElse
                    n.Contains("Body") OrElse n.Contains("Mesh")) Then shapes.Add(n)
            Next
            shapes.Sort()
            LogThis("tank havok: [{0}]   shape/material classes: {1}",
                    label, String.Join(", ", shapes))
        End If
    End Sub

    Private Sub DumpTree(label As String, s As Section, pad As String)
        For Each c In s.children
            LogThis("tank havok: [{0}] {1}[{2}] {3:N0} bytes {4}", label, pad, c.tag,
                    c.dataLength, If(c.isData, "data", c.children.Count & " child(ren)"))
            If Not c.isData Then DumpTree(label, c, pad & "  ")
        Next
    End Sub

End Module

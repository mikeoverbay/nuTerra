Imports System.IO

''' <summary>
''' Works out whether a decal texture needs a geometric edge fade.
'''
''' space.bin has no flag for this, and it does not need one: the artist encodes
''' it by where the pixels go. Broad ground-coverage textures (rock scatter,
''' track marks, ash, ground) run opaque content right to the border and get cut
''' off square by the projection box. Discrete things (craters, signs, hatches,
''' puddles, garbage) are painted with a transparent margin and fade themselves.
'''
''' Measured on Abbey: 4 of 25 textures reach the border, covering roughly half
''' the decal instances on the map. So we measure the alpha border once per
''' texture at load and derive the flag the file does not carry.
''' </summary>
Public Module DecalEdgeProbe

    ' Fraction of the outermost ring that must be opaque before we call it
    ' "content runs to the edge". Measured on the 256 mip the four Abbey
    ' textures that qualify land at 5.88, 4.24, 1.27 and 0.69 percent, and the
    ' other twenty-one are all exactly zero - not one opaque texel between them.
    ' The ring is about 1020 texels, so a single texel is already 0.10%, and
    ' this threshold means "at least three". Mip filtering softens border
    ' content, so anything tighter than this drops PBS_ASH_01 (161 instances on
    ' Abbey), which does reach the border at full resolution.
    Private Const RING_OPAQUE_FRACTION As Single = 0.0025F
    Private Const OPAQUE_ALPHA As Integer = 128

    ' Big enough that a small authored margin still reads as a margin. The
    ' tightest self-fading texture on Abbey insets by 8 texels at 1024, which is
    ' still 2 texels here - clear of the 1 texel ring we sample.
    Private Const TARGET_SIZE As Integer = 256

    Private ReadOnly cache As New Dictionary(Of String, Boolean)

    Public Sub ClearCache()
        cache.Clear()
    End Sub

    ''' <summary>
    ''' True when the texture's opaque content reaches its border, so the decal
    ''' needs the shader to fade it out at the box edge. Results are cached by
    ''' path - a map reuses the same handful of textures thousands of times.
    ''' </summary>
    Public Function NeedsEdgeFade(path As String) As Boolean
        If String.IsNullOrEmpty(path) Then Return False

        Dim hit As Boolean
        If cache.TryGetValue(path, hit) Then Return hit

        hit = False
        Try
            Dim entry = ResMgr.LookupHD(path)
            If entry IsNot Nothing Then
                Dim ms As New MemoryStream
                entry.Extract(ms)
                hit = MeasureBorder(ms.GetBuffer(), CInt(ms.Length))
            End If
        Catch ex As Exception
            ' a texture we cannot read is not a texture we should be fading
            Debug.Print("DecalEdgeProbe failed on {0}: {1}", path, ex.Message)
        End Try

        cache(path) = hit
        Return hit
    End Function

    ''' <summary>
    ''' Decodes the alpha plane of a mip near TARGET_SIZE and reports whether its
    ''' outermost one texel ring is opaque often enough to need fading.
    ''' </summary>
    Private Function MeasureBorder(d() As Byte, length As Integer) As Boolean
        If length < 128 Then Return False
        If d(0) <> Asc("D"c) OrElse d(1) <> Asc("D"c) OrElse d(2) <> Asc("S"c) Then Return False

        Dim height = BitConverter.ToInt32(d, 12)
        Dim width = BitConverter.ToInt32(d, 16)
        Dim mips = BitConverter.ToInt32(d, 28)
        Dim fourcc = Text.Encoding.ASCII.GetString(d, 84, 4)

        ' alpha lives in the first 8 bytes of a block for both of these; DXT1
        ' carries no usable alpha so it can never be measured this way
        If fourcc <> "DXT5" AndAlso fourcc <> "DXT3" Then Return False
        If width < 8 OrElse height < 8 Then Return False
        If mips < 1 Then mips = 1

        ' walk the chain to the first mip at or below TARGET_SIZE
        Dim offset = 128
        Dim w = width, h = height
        For level = 0 To mips - 1
            If w <= TARGET_SIZE OrElse level = mips - 1 Then Exit For
            offset += ((w + 3) \ 4) * ((h + 3) \ 4) * 16
            w = Math.Max(1, w \ 2)
            h = Math.Max(1, h \ 2)
        Next
        If w < 8 OrElse h < 8 Then Return False

        Dim bx = (w + 3) \ 4, by = (h + 3) \ 4
        If offset + bx * by * 16 > length Then Return False

        Dim alpha(w * h - 1) As Byte
        Dim tbl(7) As Integer
        For j = 0 To by - 1
            For i = 0 To bx - 1
                Dim b = offset + (j * bx + i) * 16
                If fourcc = "DXT5" Then
                    Dim a0 = CInt(d(b)), a1 = CInt(d(b + 1))
                    tbl(0) = a0 : tbl(1) = a1
                    If a0 > a1 Then
                        For k = 1 To 6
                            tbl(k + 1) = ((7 - k) * a0 + k * a1) \ 7
                        Next
                    Else
                        For k = 1 To 4
                            tbl(k + 1) = ((5 - k) * a0 + k * a1) \ 5
                        Next
                        tbl(6) = 0 : tbl(7) = 255
                    End If
                    Dim bits As ULong = 0
                    For k = 0 To 5
                        bits = bits Or (CULng(d(b + 2 + k)) << (8 * k))
                    Next
                    For px = 0 To 15
                        Dim y = j * 4 + px \ 4, x = i * 4 + px Mod 4
                        If x < w AndAlso y < h Then
                            alpha(y * w + x) = CByte(tbl(CInt((bits >> (3 * px)) And 7UL)))
                        End If
                    Next
                Else ' DXT3, 4 bits per texel
                    For px = 0 To 15
                        Dim y = j * 4 + px \ 4, x = i * 4 + px Mod 4
                        If x < w AndAlso y < h Then
                            Dim nib = d(b + px \ 2)
                            If (px And 1) = 1 Then nib = CByte(nib >> 4) Else nib = CByte(nib And &HF)
                            alpha(y * w + x) = CByte(nib * 17)
                        End If
                    Next
                End If
            Next
        Next

        Dim opaque = 0, total = 0
        For x = 0 To w - 1
            If alpha(x) > OPAQUE_ALPHA Then opaque += 1
            If alpha((h - 1) * w + x) > OPAQUE_ALPHA Then opaque += 1
            total += 2
        Next
        For y = 1 To h - 2
            If alpha(y * w) > OPAQUE_ALPHA Then opaque += 1
            If alpha(y * w + w - 1) > OPAQUE_ALPHA Then opaque += 1
            total += 2
        Next

        Return total > 0 AndAlso (CSng(opaque) / CSng(total)) > RING_OPAQUE_FRACTION
    End Function

End Module

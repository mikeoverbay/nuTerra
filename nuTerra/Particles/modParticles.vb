Imports System.IO
Imports OpenTK.Mathematics

''' <summary>
''' Readers for the game's particle data: the effect definitions (.vfxbin) and
''' the per-map placements (the BWPs section of space.bin).
'''
''' The formats are reverse engineered - see docs/VFXBIN_PARTICLE_FORMAT.md for
''' how each offset was established and which are still guesses. Everything
''' here is read-only parsing; simulation and drawing live in MapParticles.
''' </summary>
Module modParticles

    ' Record ids inside a .vfxbin.
    Private Const REC_ROOT As UInteger = 1004UI   ' lod_effect_f, +4 is the file length
    Private Const REC_LOD As UInteger = 1003UI    ' a LOD level, named Lod_0-500 etc
    Private Const REC_EMITTER As UInteger = 1001UI
    Private Const BLOCK_SOURCE As UInteger = 1000UI
    Private Const BLOCK_PARTICLE As UInteger = 999UI

    ' Separates the keyframe tracks at the tail of a particle block.
    Private Const TRACK_MARKER As UInteger = &H50505050UI

    ''' <summary>One keyframe track: normalised times against 1 or 4 floats per key.</summary>
    Public Class PfxTrack
        Public times As Single()
        Public values As Single()()   ' one array per key, length 1 (scalar) or 4 (rgba)

        ''' <summary>Sample with linear interpolation. t is 0..1 over the particle's life.</summary>
        Public Function Sample(t As Single, component As Integer) As Single
            If times Is Nothing OrElse times.Length = 0 Then Return 1.0F
            If t <= times(0) Then Return values(0)(component)
            For i = 1 To times.Length - 1
                If t <= times(i) Then
                    Dim span = times(i) - times(i - 1)
                    Dim f = If(span > 0.000001F, (t - times(i - 1)) / span, 0.0F)
                    Return values(i - 1)(component) + (values(i)(component) - values(i - 1)(component)) * f
                End If
            Next
            Return values(values.Length - 1)(component)
        End Function
    End Class

    Public Class PfxEmitter
        Public name As String
        Public diffuse As String

        ' source block (id 1000)
        Public rate As Single              ' particles per second
        Public boxHalf As Vector3          ' emitter box half extents, metres
        Public spread As Single            ' radians, symmetric

        ' particle block (id 999)
        Public sizeMin As Single, sizeMax As Single      ' metres
        Public lifeMin As Single, lifeMax As Single      ' seconds
        Public atlasCols As Integer, atlasRows As Integer
        Public atlasFps As Single
        Public uvRect As Vector4

        ' Fixed 8-track schema. Only these three are identified; see the doc.
        Public scaleTrack As PfxTrack      ' track 0, scale over life
        Public speedTrack As PfxTrack      ' track 3, speed over life (decays)
        Public colourTrack As PfxTrack     ' track 6, rgba over life
    End Class

    Public Class PfxEffect
        Public path As String
        Public emitters As New List(Of PfxEmitter)
    End Class

    Public Class PfxPlacement
        Public transform As Matrix4
        Public effectId As UInteger
    End Class

    Private Function U32(b As Byte(), o As Integer) As UInteger
        Return BitConverter.ToUInt32(b, o)
    End Function

    Private Function F32(b As Byte(), o As Integer) As Single
        Return BitConverter.ToSingle(b, o)
    End Function

    Private Function AsciiAt(b As Byte(), o As Integer, max As Integer) As String
        Dim n = 0
        While n < max AndAlso o + n < b.Length AndAlso b(o + n) <> 0
            If b(o + n) < 32 OrElse b(o + n) > 126 Then Return Nothing
            n += 1
        End While
        If n = 0 Then Return Nothing
        Return System.Text.Encoding.ASCII.GetString(b, o, n)
    End Function

    ''' <summary>
    ''' The 64-byte NUL-padded name field. It sits at +28 for some record types
    ''' and +32 for others, so probe rather than assume.
    ''' </summary>
    Private Function FindName(b As Byte(), off As Integer, size As Integer, ByRef namePos As Integer) As String
        Dim probe = 8
        While probe < Math.Min(56, size)
            Dim s = AsciiAt(b, off + probe, 64)
            If s IsNot Nothing AndAlso s.Length >= 3 Then
                namePos = off + probe
                Return s
            End If
            probe += 4
        End While
        namePos = -1
        Return Nothing
    End Function

    ''' <summary>Read the keyframe tracks at the tail of a particle block.</summary>
    Private Function ReadTracks(b As Byte(), start As Integer, [end] As Integer) As List(Of PfxTrack)
        Dim res As New List(Of PfxTrack)
        Dim o = start
        ' find the first count+marker pair
        While o + 8 <= [end] AndAlso Not (U32(b, o + 4) = TRACK_MARKER AndAlso U32(b, o) <= 64UI)
            o += 4
        End While
        While o + 8 <= [end] AndAlso U32(b, o + 4) = TRACK_MARKER
            Dim n = CInt(U32(b, o))
            Dim base_ = o + 8
            ' stride is 1 for scalar tracks and 4 for colour; pick the one whose
            ' end lands on the next count+marker pair, or exactly on the block end
            Dim stride = 0
            For Each cand In {1, 4}
                Dim nxt = base_ + 4 * n + 4 * n * cand
                If nxt = [end] Then stride = cand : Exit For
                If nxt + 8 <= [end] AndAlso U32(b, nxt + 4) = TRACK_MARKER AndAlso U32(b, nxt) <= 64UI Then
                    stride = cand
                    Exit For
                End If
            Next
            If stride = 0 Then Exit While

            Dim tr As New PfxTrack
            ReDim tr.times(Math.Max(n - 1, 0))
            ReDim tr.values(Math.Max(n - 1, 0))
            For i = 0 To n - 1
                tr.times(i) = F32(b, base_ + 4 * i)
                Dim v(stride - 1) As Single
                For k = 0 To stride - 1
                    v(k) = F32(b, base_ + 4 * n + 4 * (i * stride + k))
                Next
                tr.values(i) = v
            Next
            res.Add(tr)
            o = base_ + 4 * n + 4 * n * stride
        End While
        Return res
    End Function

    ''' <summary>Locate a 1000/999 sub-block inside an emitter payload.</summary>
    Private Function FindBlock(b As Byte(), s As Integer, e As Integer, id As UInteger) As Integer
        Dim o = s
        While o + 8 <= e
            If U32(b, o) = id AndAlso U32(b, o + 4) = 2UI Then Return o
            o += 4
        End While
        Return -1
    End Function

    ''' <summary>
    ''' Parse a .vfxbin. Only the first LOD's emitters are taken - the later
    ''' LODs are the same emitters authored for distance.
    ''' </summary>
    Public Function LoadVfx(bytes As Byte(), path As String) As PfxEffect
        If bytes Is Nothing OrElse bytes.Length < 96 Then Return Nothing
        If U32(bytes, 0) <> REC_ROOT Then Return Nothing

        Dim eff As New PfxEffect With {.path = path}
        Dim fileEnd = Math.Min(CInt(U32(bytes, 4)), bytes.Length)

        ' Emitters are id 1001 records. Scan rather than walk the tree: the
        ' parameter block between a record's name and its children has a size
        ' that varies by record type and is not worth modelling.
        Dim o = 80
        Dim seen As New HashSet(Of String)
        While o + 8 <= fileEnd
            If U32(bytes, o) = REC_EMITTER Then
                Dim size = CInt(U32(bytes, o + 4))
                If size >= 16 AndAlso o + size <= fileEnd Then
                    Dim namePos = -1
                    Dim nm = FindName(bytes, o, size, namePos)
                    If nm IsNot Nothing AndAlso Not seen.Contains(nm) Then
                        seen.Add(nm)
                        Dim em = ParseEmitter(bytes, nm, namePos + 64, o + size)
                        If em IsNot Nothing Then eff.emitters.Add(em)
                    End If
                    o += size
                    Continue While
                End If
            End If
            o += 4
        End While
        Return eff
    End Function

    Private Function ParseEmitter(b As Byte(), nm As String, s As Integer, e As Integer) As PfxEmitter
        Dim src = FindBlock(b, s, e, BLOCK_SOURCE)
        Dim par = FindBlock(b, s, e, BLOCK_PARTICLE)
        If par < 0 Then Return Nothing

        Dim em As New PfxEmitter With {.name = nm}
        If src >= 0 Then
            em.rate = F32(b, src + 36)
            em.boxHalf = New Vector3(F32(b, src + 44), F32(b, src + 48), F32(b, src + 52))
            em.spread = Math.Abs(F32(b, src + 68))
        End If

        em.diffuse = If(AsciiAt(b, par + 48, 200), "")
        em.sizeMin = F32(b, par + 176)
        em.sizeMax = F32(b, par + 180)
        em.lifeMin = F32(b, par + 184)
        em.lifeMax = F32(b, par + 188)
        em.uvRect = New Vector4(F32(b, par + 192), F32(b, par + 196), F32(b, par + 200), F32(b, par + 204))
        em.atlasCols = Math.Max(CInt(U32(b, par + 220)), 1)
        em.atlasRows = Math.Max(CInt(U32(b, par + 224)), 1)
        em.atlasFps = F32(b, par + 228)

        Dim tracks = ReadTracks(b, par, e)
        If tracks.Count >= 7 Then
            em.scaleTrack = tracks(0)
            em.speedTrack = tracks(3)
            em.colourTrack = tracks(6)
        End If
        Return em
    End Function

    ''' <summary>
    ''' The BWPs section of space.bin: where particle effects are placed.
    ''' [u32 record_size = 80][u32 count][count * (4x4 matrix, u32 effect id, 12 bytes)]
    ''' </summary>
    Public Function LoadPlacements(space_bin As Stream) As List(Of PfxPlacement)
        Dim res As New List(Of PfxPlacement)
        Try
            space_bin.Position = 0
            Dim head(23) As Byte
            Dim tableOff = 0
            ' Section table starts at offset 0: magic[4], version u32,
            ' offset u64, length u32, extra u32.
            Dim secOff As Long = -1, secLen As Integer = 0
            While True
                space_bin.Position = tableOff
                If space_bin.Read(head, 0, 24) <> 24 Then Exit While
                Dim magic = System.Text.Encoding.ASCII.GetString(head, 0, 4)
                Dim printable = True
                For i = 0 To 3
                    If head(i) < 32 OrElse head(i) > 126 Then printable = False
                Next
                If Not printable Then Exit While
                If magic = "BWPs" Then
                    secOff = BitConverter.ToInt64(head, 8)
                    secLen = BitConverter.ToInt32(head, 16)
                    Exit While
                End If
                tableOff += 24
                If tableOff > 24 * 200 Then Exit While
            End While
            If secOff < 0 OrElse secLen < 8 Then Return res

            Dim buf(secLen - 1) As Byte
            space_bin.Position = secOff
            If space_bin.Read(buf, 0, secLen) <> secLen Then Return res

            Dim recSize = CInt(U32(buf, 0))
            Dim count = CInt(U32(buf, 4))
            If recSize < 68 OrElse count < 0 OrElse 8 + recSize * count > secLen Then Return res

            For i = 0 To count - 1
                Dim o = 8 + i * recSize
                Dim m(15) As Single
                For k = 0 To 15
                    m(k) = F32(buf, o + k * 4)
                Next
                Dim p As New PfxPlacement With {
                    .transform = New Matrix4(m(0), m(1), m(2), m(3),
                                             m(4), m(5), m(6), m(7),
                                             m(8), m(9), m(10), m(11),
                                             m(12), m(13), m(14), m(15)),
                    .effectId = U32(buf, o + 64)
                }
                res.Add(p)
            Next
        Catch ex As Exception
            LogThis("BWPs parse failed: {0}", ex.Message)
        End Try
        Return res
    End Function

End Module

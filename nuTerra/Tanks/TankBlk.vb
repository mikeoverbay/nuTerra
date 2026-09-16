Imports System.IO

''' <summary>
''' The .blk file: half-metre cells, one byte of WHAT IS HERE, and the ground
''' height beside it.
'''
''' THE OWNER, 2026-09-16: "we screwed up. we were going to use bit masking to
''' inform what was there in to the squares map. bit 0 is our block and bit 2-7
''' is open for data", then "bit 0 is our block flag. all others are for what it
''' is", and "Get nuTerra to make a .5 res .blk file".
'''
''' squares.u8 spends a whole byte saying one bit's worth. It can say THAT a
''' cell is blocked and never WHAT blocked it, which is why the tank side
''' cannot tell a wall from a fence at planner resolution, why an overlay paints
''' a crushable pole the same grey as a cliff, and why the crushable rule had to
''' be COPIED into Python where it drifted silently. Seven spare bits were
''' sitting there the whole time.
'''
''' HALF A METRE, and the reason is doorways rather than the blocked
''' percentage. Tank AI work measured both: the ceiling clause frees 282,098
''' texels, of which 1,804 survive as distinct planner cells at 1 m and 8,529 at
''' 0.5 m - 4.7x more of the same finding. The blocked share barely moves,
''' 28.47% to 27.13%. A 1 m cell cannot honestly describe a 1.5 m opening, and
''' once kind rides in the cell a fence and the wall beside it should not land
''' in the same one.
'''
''' RAW GEOMETRY, NO HULL EROSION. The owner: "2.25 is too much I dont want
''' that. I want tank to see wall and stay away." Clearance is the driver's
''' rays now, so this file must not bake a margin into the data - a consumer
''' that wants one can grow it, and a consumer that does not cannot shrink it
''' back out.
'''
''' Added 2026-09-16 by nuTerra work.
''' </summary>
Public Class TankBlk

    ''' <summary>Half a metre. His number.</summary>
    Public Const CELL_M As Single = 0.5F

    Public Const VERSION As UInteger = 1UI

    ''' <summary>
    ''' The mask, one byte a cell.
    '''
    ''' BIT 0 IS OURS AND THE REST DESCRIBE THE THING - his rule, verbatim:
    ''' "bit 0 is our block flag. all others are for what it is". So no bit
    ''' above 0 is a decision, they are all observations, and a reader that
    ''' disagrees with our block bit can recompute it from the rest.
    '''
    ''' NO DOORWAY BIT, and that is not an omission. He settled it: "door ways =
    ''' bit 0 = 0". A cell that is open while carrying a BUILT kind is the
    ''' archway; a bit would only restate what those two already say.
    ''' </summary>
    Public Const BIT_BLOCKED As Byte = &H1      ' bit 0
    Public Const KIND_SHIFT As Integer = 1      ' bits 1-3, values 0-7
    Public Const KIND_MASK As Byte = &HE
    Public Const BIT_OUTLAND As Byte = &H10     ' bit 4
    Public Const BIT_SOLID As Byte = &H20       ' bit 5
    Public Const BIT_TRUNK As Byte = &H40       ' bit 6
    Public Const BIT_CRUSHABLE As Byte = &H80   ' bit 7

    Public Shared ReadOnly Property FileFor(map As String) As String
        Get
            Return Path.Combine(Path.GetTempPath(), "nuTerra", "flight", map & ".blk")
        End Get
    End Property

    ''' <summary>
    ''' Cut the .blk from the flight bake. Returns cells marked blocked, or -1.
    '''
    ''' THE CELL'S BYTE COMES FROM ONE REPRESENTATIVE TEXEL, not from a vote.
    ''' At 0.5 m over a 0.171 m texel a cell holds about nine of them, and a
    ''' cell that blocks does so because of ONE thing - so the kind, solid,
    ''' trunk and crushable bits are taken from the texel that decided it: the
    ''' first blocker, or the tallest texel where nothing blocks. Averaging kind
    ''' across a cell would invent a kind that is not standing anywhere in it.
    '''
    ''' OUTLAND IS OR'D ACROSS THE CELL instead, because it is not a property of
    ''' a thing standing somewhere - it is the map saying this ground is outside
    ''' the arena, and any part of a cell being outside is enough.
    ''' </summary>
    Public Shared Function Build(b As MapFlightBake, map As String) As Integer
        If b Is Nothing OrElse Not b.ready Then Return -1

        Dim sw = Stopwatch.StartNew()
        Dim wx0 = b.wx_min
        Dim span = b.wx_max - b.wx_min
        Dim n = CInt(Math.Round(span / CELL_M))
        If n < 8 Then Return -1

        Dim tex_per_m = MapFlightBake.SIZE / span
        Dim mask(n * n - 1) As Byte
        Dim height(n * n - 1) As Single
        Dim blocked = 0

        For cz = 0 To n - 1
            Dim r0 = CInt(Math.Floor(cz * CELL_M * tex_per_m))
            Dim r1 = CInt(Math.Floor((cz + 1) * CELL_M * tex_per_m))
            If r1 <= r0 Then r1 = r0 + 1
            If r1 > MapFlightBake.SIZE Then r1 = MapFlightBake.SIZE

            For cx = 0 To n - 1
                Dim c0 = CInt(Math.Floor(cx * CELL_M * tex_per_m))
                Dim c1 = CInt(Math.Floor((cx + 1) * CELL_M * tex_per_m))
                If c1 <= c0 Then c1 = c0 + 1
                If c1 > MapFlightBake.SIZE Then c1 = MapFlightBake.SIZE

                Dim hit = False
                Dim outland = False
                Dim pick = -1            ' the texel that decides this cell
                Dim tallest = Single.MinValue

                For r = r0 To r1 - 1
                    Dim row = r * MapFlightBake.SIZE
                    For c = c0 To c1 - 1
                        Dim i = row + c
                        Dim k = b.kind_b(i)

                        If (k And MapFlightBake.OUTLAND_BIT) <> 0 Then outland = True

                        ' The same rule TankSquares cuts by, and deliberately
                        ' the same call - MapFlightBake.Crushable - so the two
                        ' files cannot disagree about what stops a hull.
                        Dim blocks = False
                        If (k And MapFlightBake.OUTLAND_BIT) <> 0 OrElse
                           (k And MapFlightBake.KIND_MASK) = MapFlightBake.KIND_WATER Then
                            blocks = True
                        ElseIf Not MapFlightBake.Crushable(k) Then
                            If b.top_m(i) - b.floor_m(i) > TankNavLimits.MAX_OBSTACLE Then
                                If Not (b.has_ceiling(i) AndAlso
                                        b.clearance_m(i) >= MapFlightBake.MIN_CLEARANCE) Then
                                    blocks = True
                                End If
                            End If
                        End If

                        If blocks Then
                            If Not hit Then
                                hit = True
                                pick = i
                            End If
                        ElseIf Not hit Then
                            Dim t = b.top_m(i)
                            If t > tallest Then
                                tallest = t
                                pick = i
                            End If
                        End If
                    Next
                Next

                Dim idx = cz * n + cx
                Dim m As Byte = 0
                If hit Then
                    m = m Or BIT_BLOCKED
                    blocked += 1
                End If
                If outland Then m = m Or BIT_OUTLAND

                If pick >= 0 Then
                    Dim k = b.kind_b(pick)
                    m = m Or CByte((CInt(k And MapFlightBake.KIND_MASK) << KIND_SHIFT) And KIND_MASK)
                    If (k And MapFlightBake.SOLID_BIT) <> 0 Then m = m Or BIT_SOLID
                    If (k And MapFlightBake.TRUNK_BIT) <> 0 Then m = m Or BIT_TRUNK
                    ' STAMPED, NOT LEFT TO THE READER. This is the rule that was
                    ' copied into four places and drifted in the one that could
                    ' not call it. Written once here, every reader stops
                    ' re-deriving it and the fourth copy has nothing to do.
                    If MapFlightBake.Crushable(k) Then m = m Or BIT_CRUSHABLE
                End If

                mask(idx) = m

                ' The GROUND at the cell centre, metres. Not the top: a consumer
                ' wants to know where the tank's tracks would sit, and the top
                ' of a wall is not that.
                Dim mr = Math.Min(MapFlightBake.SIZE - 1, (r0 + r1) \ 2)
                Dim mc = Math.Min(MapFlightBake.SIZE - 1, (c0 + c1) \ 2)
                height(idx) = b.floor_m(mr * MapFlightBake.SIZE + mc)
            Next
        Next

        Dim p = FileFor(map)
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(p))
            Using fs As New FileStream(p, FileMode.Create, FileAccess.Write),
                  w As New BinaryWriter(fs)
                ' Header, 32 bytes. Little endian throughout - BinaryWriter's
                ' own order on every platform this runs on.
                w.Write(New Byte() {Asc("n"c), Asc("B"c), Asc("L"c), Asc("K"c)})
                w.Write(VERSION)
                w.Write(CUInt(n))
                w.Write(CELL_M)
                w.Write(wx0)
                w.Write(b.wz_max)
                w.Write(0UI)
                w.Write(0UI)

                w.Write(mask, 0, mask.Length)
                Dim hb(height.Length * 4 - 1) As Byte
                Buffer.BlockCopy(height, 0, hb, 0, hb.Length)
                w.Write(hb, 0, hb.Length)
            End Using
        Catch ex As Exception
            LogThis("tank blk: could not write {0} - {1}", p, ex.Message)
            Return -1
        End Try

        LogThis("tank blk: {0}x{0} of {1:0.##} m in {2} ms, {3:N0} blocked ({4:0.0}%), " &
                "{5:0.0} MB -> {6}",
                n, CELL_M, sw.ElapsedMilliseconds, blocked, 100.0 * blocked / (n * n),
                (32.0 + mask.Length + height.Length * 4.0) / 1048576.0, p)
        Return blocked
    End Function

End Class

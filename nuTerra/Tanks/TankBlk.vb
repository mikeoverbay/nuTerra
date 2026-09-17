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

    ''' <summary>
    ''' 2: heights quantised to uint16, and an obstacle plane added.
    '''
    ''' THE HEADER IS STILL 32 BYTES. v1 spent its last eight on two pad
    ''' words; height_offset and height_scale are eight bytes and go exactly
    ''' there. So a reader takes 32 bytes whatever the version and branches on
    ''' the version only for the planes - the header length never becomes
    ''' version-dependent, which is the thing that makes a format painful to
    ''' read years later.
    ''' </summary>
    Public Const VERSION As UInteger = 2UI

    ''' <summary>
    ''' Metres a step in the obstacle plane, and the value that means "taller
    ''' than this plane can say".
    '''
    ''' 0.25 m because the precision matters at a doorway lintel and nowhere
    ''' else. 255 is a STRICT SENTINEL, not 63.75 m: a reader must treat it as
    ''' a wall rather than compute a height from it, or the 206 m dam on
    ''' monastery becomes a 64 m one that a camera thinks it can clear. 254 is
    ''' a real 63.5 m.
    '''
    ''' Tank AI work's reasoning for saturating at all, and it is right: a
    ''' camera does not plan to clear a spire, it plans to go round it.
    ''' </summary>
    Public Const OBSTACLE_STEP_M As Single = 0.25F
    Public Const OBSTACLE_TALLER As Byte = 255

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
        Dim height(n * n - 1) As UShort
        Dim obstacle(n * n - 1) As Byte
        Dim blocked = 0
        Dim saturated = 0

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
                ' THE TALLEST OBSTACLE IN THE CELL, not the representative
                ' one. The mask bits come from the texel that decided the cell,
                ' which answers "what is here"; a camera has to clear the
                ' HIGHEST thing in the cell, so a max is the only safe
                ' reduction - a representative pick would under-report a lintel
                ' standing beside open floor.
                Dim obs_max = 0.0F

                For r = r0 To r1 - 1
                    Dim row = r * MapFlightBake.SIZE
                    For c = c0 To c1 - 1
                        Dim i = row + c
                        Dim k = b.kind_b(i)

                        If (k And MapFlightBake.OUTLAND_BIT) <> 0 Then outland = True

                        Dim oh = b.top_m(i) - b.floor_m(i)
                        If oh > obs_max Then obs_max = oh

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
                ' QUANTISED THE WAY _floor.r16 ALREADY IS, same offset and
                ' scale, so the two are directly comparable and a reader that
                ' knows one knows the other. 1/64 m is 1.6 cm - over a half
                ' metre cell that is about 0.03 of slope against a 0.84
                ' threshold, which is noise.
                Dim gy = b.floor_m(mr * MapFlightBake.SIZE + mc)
                Dim q = CInt(Math.Round((gy - b.h_offset) * MapFlightBake.HEIGHT_SCALE))
                height(idx) = CUShort(Math.Min(Math.Max(q, 0), 65535))

                ' SATURATE ON THE METRES, NOT ON THE ROUNDED STEP, so the
                ' sentinel has a bound that can be stated exactly. Testing the
                ' rounded value made 255 mean "taller than 63.625 m" - the
                ' round pulled the boundary half a step below the top of the
                ' range - and a reader quoting any round number for it would
                ' have been wrong. It now means >= 63.75 m and nothing else.
                If obs_max >= OBSTACLE_TALLER * OBSTACLE_STEP_M Then
                    obstacle(idx) = OBSTACLE_TALLER
                    saturated += 1
                ElseIf obs_max <= 0.0F Then
                    obstacle(idx) = 0
                Else
                    obstacle(idx) = CByte(Math.Round(obs_max / OBSTACLE_STEP_M))
                End If
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
                ' The two v1 pad words, now carrying what the height plane
                ' needs to be read back as metres.
                w.Write(b.h_offset)
                w.Write(MapFlightBake.HEIGHT_SCALE)

                w.Write(mask, 0, mask.Length)
                Dim hb(height.Length * 2 - 1) As Byte
                Buffer.BlockCopy(height, 0, hb, 0, hb.Length)
                w.Write(hb, 0, hb.Length)
                w.Write(obstacle, 0, obstacle.Length)
            End Using
        Catch ex As Exception
            LogThis("tank blk: could not write {0} - {1}", p, ex.Message)
            Return -1
        End Try

        LogThis("tank blk: v{0} {1}x{1} of {2:0.##} m in {3} ms, {4:N0} blocked ({5:0.0}%), " &
                "{6:N0} cell(s) taller than the obstacle plane can say, {7:0.0} MB -> {8}",
                VERSION, n, CELL_M, sw.ElapsedMilliseconds, blocked,
                100.0 * blocked / (n * n), saturated,
                (32.0 + mask.Length + height.Length * 2.0 + obstacle.Length) / 1048576.0, p)
        Return blocked
    End Function

End Class

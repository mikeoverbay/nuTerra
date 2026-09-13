Imports System.IO

''' <summary>
''' The one-metre square map: can a tank stand in this square, yes or no.
'''
''' THE OWNER'S DESIGN, and it replaces the discs that were cut out at
''' aa100c09. Those were maximal free-space circles with a neighbour graph -
''' clever, 770 ms to build, and they did not pay for routing. This is the
''' opposite: a flat array of bytes with no structure at all, and the whole
''' point is that finding your square is arithmetic rather than a search.
'''
'''     "fit squares 1m apart... We need to divide and round our position in xz
'''      and use that to find the [1 m] cube in the map. If its 1 its a
'''      collision. other wise wide open."
'''
''' So: round x and z to metres, index, read a byte. No tree, no graph, no
''' nearest-neighbour query. On a 1400 m map that is 1,960,000 bytes.
'''
''' IT ALSO CARRIES THE MEMORY OF WHERE WE HAVE DRIVEN. A completed route sets
''' the last square it stood in to 1, so the next search cannot come home the
''' same way and has to find another. One array answers both "is this solid"
''' and "has this been used", which is why a reset has to reload the file
''' rather than clear a flag - the pristine copy is on disk and the working
''' copy is scribbled on.
'''
''' WHAT COUNTS AS SOLID is the tank's rule, not the camera's: a hull goes
''' through a fence, over a curb and through a bush, so those kinds are exempt
''' from the height test. But ONLY where they are not also solid - bake_version
''' 2 marks terrain-borne geometry under a canopy, and on monastery that is
''' 317,776 tree-keyed texels standing on rock. Exempting those by kind alone
''' drives a tank into a cliff.
''' </summary>
Public Class TankSquares

    ''' One metre a side. The owner's number, and it wants no tuning: it is
    ''' about a quarter of a hull, so a square is a place rather than a pose.
    Public Const CELL_M As Single = 1.0F

    Public Shared ReadOnly Property FileFor(map As String) As String
        Get
            Return Path.Combine(Path.GetTempPath(), "nuTerra", "flight",
                                map & "_squares.u8")
        End Get
    End Property

    ''' <summary>
    ''' Cut the square map from the flight bake and write it beside the bake.
    '''
    ''' Returns the number of squares marked solid, or -1 if it could not.
    ''' </summary>
    Public Shared Function Build(b As MapFlightBake, map As String) As Integer
        If b Is Nothing OrElse Not b.ready Then Return -1

        Dim wx0 = b.wx_min, wz0 = b.wz_min
        Dim span = b.wx_max - b.wx_min
        Dim n = CInt(Math.Round(span / CELL_M))
        If n < 8 Then Return -1

        ' How many bake texels fall in one square. 1 m over a 0.171 m texel is
        ' about 5.85, so this is 5 or 6 and the loop below walks the exact
        ' texel range rather than assuming a whole number.
        Dim tex_per_m = MapFlightBake.SIZE / span

        Dim sq(n * n - 1) As Byte
        Dim solid = 0

        For cz = 0 To n - 1
            ' The texel rows this square covers, half open.
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
                For r = r0 To r1 - 1
                    Dim row = r * MapFlightBake.SIZE
                    For c = c0 To c1 - 1
                        Dim i = row + c
                        Dim k = b.kind_b(i)

                        ' The two that always stop a hull whatever their
                        ' height: the ring outside the arena, and water.
                        ' THE TRUNK USED TO BE A THIRD.
                        ' A tree is crushable and a tank knocks the whole thing flat -
                        ' trunk included - so re-blocking the trunk refused the very ground
                        ' the crushable rule had just opened. 98.05% of trunk texels key
                        ' tree; the
                        ' 1.95% landing on building, rock or other carry the solid bit and stay
                        ' blocked by the height test on their own. What still stops a tank in a
                        ' wood is `tree AND solid` - rock or wall standing under the canopy.
                        If (k And MapFlightBake.OUTLAND_BIT) <> 0 OrElse
                           (k And MapFlightBake.KIND_MASK) = MapFlightBake.KIND_WATER Then
                            hit = True
                            Exit For
                        End If

                        ' CRUSHABLE ONLY IF IT IS NOT ALSO SOLID. "remove all
                        ' items from the zones we can drive over like fences
                        ' and bushes" - but a texel that keys tree and carries
                        ' the solid bit is rock or wall standing under a
                        ' canopy, and it is not driven over by anything.
                        ' THE SOLID BIT QUALIFIES TREES, AND ONLY TREES.
                        '
                        ' It is read from the depth buffer BETWEEN the model
                        ' pass and the tree pass, so any MODEL sets it for
                        ' itself: measured on monastery it is set on 75.6% of
                        ' fence texels and 63.3% of prop texels, and on just
                        ' 7.0% of tree texels. Only for a tree does it carry
                        ' information - that something else is standing under
                        ' the canopy.
                        '
                        ' Testing it on fence and prop as well, which is what
                        ' this did first, un-crushed three quarters of the
                        ' fences on the map and drove a tank round them. The
                        ' owner's rule is the other way: "A fence or curb is
                        ' not going to stop a tank."
                        '
                        ' What this CANNOT see is a fence standing in front of
                        ' a wall, where the fence wins the depth test and the
                        ' wall is invisible to us. That risk existed before the
                        ' bit did and this data cannot settle it.
                        Dim k7 = k And MapFlightBake.KIND_MASK
                        Dim crushable =
                            (k7 = MapFlightBake.KIND_FENCE OrElse
                             k7 = MapFlightBake.KIND_PROP) OrElse
                            (k7 = MapFlightBake.KIND_TREE AndAlso
                             (k And MapFlightBake.SOLID_BIT) = 0)
                        If Not crushable Then
                            If b.top_m(i) - b.floor_m(i) > TankNavLimits.MAX_OBSTACLE Then
                                hit = True
                                Exit For
                            End If
                        End If
                    Next
                    If hit Then Exit For
                Next

                If hit Then
                    sq(cz * n + cx) = 1
                    solid += 1
                End If
            Next
        Next

        Dim p = FileFor(map)
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        File.WriteAllBytes(p, sq)
        ' Self describing, because a bare 1.96 MB of bytes tells a reader
        ' nothing about which corner it starts in or how wide a square is.
        File.WriteAllText(Path.ChangeExtension(p, ".txt"),
            String.Format(
                "map={0}" & vbLf & "cell_m={1:0.###}" & vbLf & "n={2}" & vbLf &
                "wx_min={3:0.###}" & vbLf & "wz_max={4:0.###}" & vbLf &
                "order=row major from wz_MAX downward, x fastest - the bake's" &
                " own row order, and the label used to say wz_min which is the" &
                " opposite and cost a reader an hour" & vbLf &
                "value=1 solid or used, 0 open" & vbLf &
                "rule=outland|trunk|water always; height over {5:0.##} m unless" &
                " the kind is tree, fence or prop AND the solid bit is clear" & vbLf,
                map, CELL_M, n, wx0, b.wz_max, TankNavLimits.MAX_OBSTACLE))

        LogThis("tank squares: {0}x{0} of {1:0.#} m, {2:N0} solid ({3:0.0}%), wrote {4}",
                n, CELL_M, solid, 100.0 * solid / (n * n), p)
        Return solid
    End Function

End Class

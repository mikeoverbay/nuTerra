Imports System.Diagnostics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' THE .blk ITSELF, AS THIN WAFERS.
'''
''' "i want a check box to hide all buildings and draw the .blk as thin
'''  wafers. .1 m thick and size of each block carea" - the owner, 2026-09-20.
'''
''' A wafer per BLOCKED cell: a cell square in XZ, a tenth of a metre thick,
''' sitting on that cell's baked ground height, and a flat dark-brown tile per
''' open one. With the buildings hidden this
''' is the map the brain actually reads, with nothing drawn over it - which is
''' the only way to see the difference between what is there and what the bake
''' thinks is there.
'''
''' THE WHOLE MAP, BY RUNS. Every cell of the 2800 x 2800 grid is drawn -
''' you cannot place a start or a goal on a map you can only see 80 m of. What
''' makes that affordable is merging each row's consecutive same-kind,
''' same-height cells into a single quad; build has the arithmetic.
'''
''' HEIGHTS COME FROM THE .blk, NOT FROM THE TERRAIN. BlkGround is an array
''' read of the plane the bake wrote; BrainNav.Ground is a live terrain query,
''' and eighteen thousand of those would be the same mistake this file exists
''' to make visible. It also means the wafers show the height the BAKE
''' believes, which is the more useful answer here - where they float or sink
''' against the drawn ground, the two disagree, and that is worth seeing.
''' </summary>
Module BrainBlkView

    ''' <summary>Off by default. This is a diagnostic, and a map of grey
    ''' wafers over the terrain is not what anyone wants to fly around in.</summary>
    Public SHOW As Boolean = False

    ''' <summary>A tenth of a metre, as asked. Thin enough to read as a skin on
    ''' the ground rather than as a block, thick enough to catch the light at a
    ''' grazing angle and not vanish.</summary>
    Private Const THICK As Single = 0.1F

    ''' <summary>Clear of the ground so it does not z-fight the terrain it is
    ''' lying on. Smaller than the marker squares' lift because a wafer that
    ''' hovers is a wafer that lies about where the cell is.</summary>
    Private Const LIFT As Single = 0.03F

    ''' <summary>How far the baked ground may step before a row run is broken.
    ''' Half the wafer's own thickness: a step that small is invisible at the
    ''' scale these are drawn at, and anything larger is a slope that ought to
    ''' show as one.</summary>
    Private Const HSTEP As Single = 0.05F

    ''' <summary>Steel blue: cool and flat, so it reads as MAP DATA rather than
    ''' as one of the brain's decisions. The walk view owns red, amber and
    ''' white for verdicts and they must not be confused with this.</summary>
    Public ReadOnly WAFER_RGB As New Vector3(0.38F, 0.58F, 0.88F)

    ''' <summary>Dark brown for ground a tank may drive on - "i want non
    ''' blocking squares to be dark brown", the owner, 2026-09-20. Dark on
    ''' purpose: the open cells are the overwhelming majority, so they have to
    ''' read as a floor the blue sits ON rather than as a second thing
    ''' competing with it for attention.</summary>
    Public ReadOnly OPEN_RGB As New Vector3(0.28F, 0.18F, 0.10F)

    Private shader As BrainShader
    Private vao, vbo, verts As Integer
    Private vaoO, vboO, vertsO As Integer

    ''' <summary>Throw the built map away so the next Draw remakes it. For
    ''' F5, which re-reads what nuTerra wrote under a running app.</summary>
    Public Sub Invalidate()
        verts = 0
        vertsO = 0
    End Sub

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        vaoO = GL.GenVertexArray()
        vboO = GL.GenBuffer()
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        If Not BrainNav.Ready Then Return

        build()
        If verts = 0 AndAlso vertsO = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' SOLID AND DEPTH-WRITING, unlike the markers. These are meant to be
        ' objects in the scene - a wafer behind a hill should be hidden by the
        ' hill, and one wafer should occlude another.
        '
        ' OPEN GROUND FIRST so the blocked wafers land on top of it where the
        ' two share an edge, rather than the floor winning the depth fight at
        ' equal heights and speckling through.
        If vertsO > 0 Then
            shader.SetVec3("colour", OPEN_RGB)
            GL.BindVertexArray(vaoO)
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertsO)
        End If
        If verts > 0 Then
            shader.SetVec3("colour", WAFER_RGB)
            GL.BindVertexArray(vao)
            GL.DrawArrays(PrimitiveType.Triangles, 0, verts)
        End If
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' THE WHOLE MAP, ONCE.
    '''
    ''' "i can't place the goal or start if I cant see it just draw all of the
    '''  squares.. im getting 1000 fps" - the owner, 2026-09-20. He is right
    ''' about the need and right about the headroom; the windowed version was
    ''' solving a problem that was not the frame rate.
    '''
    ''' BUT NOT AS SEVEN MILLION SQUARES. The grid is 2800 x 2800. One quad
    ''' per open cell is 460 MB of vertices, and the blocked cells as boxes
    ''' another 618 MB. Nothing about drawing that is slow - it is slow about
    ''' EXISTING.
    '''
    ''' SO RUNS, NOT CELLS. Along each row, consecutive cells that are the
    ''' same kind AND sit at the same height become ONE quad. Open ground runs
    ''' for hundreds of cells and a wall for tens, so the whole map lands
    ''' around 20 MB at exactly the same resolution. This is a change of
    ''' REPRESENTATION, not of fidelity: no cell is approximated, merged away
    ''' or moved.
    '''
    ''' THE HEIGHT TOLERANCE is what keeps that true. A run breaks when the
    ''' baked ground steps more than HSTEP, so a slope comes out as a
    ''' staircase of quads that follows it. Merging regardless of height would
    ''' flatten hillsides into single sheets - tidier, and a lie.
    '''
    ''' BUILT ONCE, not per camera move. The .blk does not change under a
    ''' running app except by F5, and Invalidate is there for that.
    ''' </summary>
    Private Sub build()
        If verts > 0 OrElse vertsO > 0 Then Return

        Dim cell = BrainNav.CellSize
        Dim cols = BrainNav.CellCols, rows = BrainNav.CellRows
        If cols <= 0 OrElse rows <= 0 Then Return

        Dim sw = Stopwatch.StartNew()
        Dim v As New List(Of Single)
        Dim o As New List(Of Single)
        Dim nBlk = 0, nOpen = 0, runsB = 0, runsO = 0

        For r = 0 To rows - 1
            Dim z1 = BrainNav.CellZTop - r * cell
            Dim z0 = z1 - cell

            Dim c = 0
            While c < cols
                Dim blocked = BrainNav.BlockedCell(c, r)
                Dim y = BrainNav.BlkGround(c, r)

                ' How far does this run reach - same kind, same height.
                Dim cEnd = c
                While cEnd + 1 < cols
                    If BrainNav.BlockedCell(cEnd + 1, r) <> blocked Then Exit While
                    If Math.Abs(BrainNav.BlkGround(cEnd + 1, r) - y) > HSTEP Then Exit While
                    cEnd += 1
                End While

                Dim x0 = BrainNav.CellX0 + c * cell
                Dim x1 = BrainNav.CellX0 + (cEnd + 1) * cell
                Dim y0 = y + LIFT
                If blocked Then
                    wafer(v, x0, x1, z0, z1, y0, y0 + THICK)
                    nBlk += (cEnd - c + 1)
                    runsB += 1
                Else
                    lid(o, x0, x1, z0, z1, y0)
                    nOpen += (cEnd - c + 1)
                    runsO += 1
                End If
                c = cEnd + 1
            End While
        Next

        verts = upload(vao, vbo, v)
        vertsO = upload(vaoO, vboO, o)
        LogThis("brain: blk wafers - the whole {0} x {1} grid in {2} ms: " &
                "{3:N0} blocked cells as {4:N0} runs, {5:N0} open as {6:N0} runs, " &
                "{7:N0} triangles, {8:N1} MB",
                cols, rows, sw.ElapsedMilliseconds, nBlk, runsB, nOpen, runsO,
                (verts + vertsO) \ 3, (verts + vertsO) * 12.0 / 1048576.0)
    End Sub

    Private Function upload(vaoId As Integer, vboId As Integer,
                            v As List(Of Single)) As Integer
        Dim a = v.ToArray()
        GL.BindVertexArray(vaoId)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vboId)
        GL.BufferData(BufferTarget.ArrayBuffer, a.Length * 4, a, BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        Return a.Length \ 3
    End Function

    ''' <summary>One flat tile, top face only - see the note in build on why
    ''' open ground does not get sides.</summary>
    Private Sub lid(v As List(Of Single), x0 As Single, x1 As Single,
                    z0 As Single, z1 As Single, y As Single)
        v.AddRange({x0, y, z0, x1, y, z1, x1, y, z0})
        v.AddRange({x0, y, z0, x0, y, z1, x1, y, z1})
    End Sub

    ''' <summary>One box, six faces, winding to match everything else drawn
    ''' here. Written out rather than looped over a corner table because this
    ''' runs eighteen thousand times a rebuild and the table lookup was the
    ''' whole cost of it.</summary>
    Private Sub wafer(v As List(Of Single), x0 As Single, x1 As Single,
                      z0 As Single, z1 As Single, y0 As Single, y1 As Single)
        ' top
        v.AddRange({x0, y1, z0, x1, y1, z1, x1, y1, z0})
        v.AddRange({x0, y1, z0, x0, y1, z1, x1, y1, z1})
        ' bottom
        v.AddRange({x0, y0, z0, x1, y0, z0, x1, y0, z1})
        v.AddRange({x0, y0, z0, x1, y0, z1, x0, y0, z1})
        ' -z
        v.AddRange({x0, y0, z0, x0, y1, z0, x1, y1, z0})
        v.AddRange({x0, y0, z0, x1, y1, z0, x1, y0, z0})
        ' +z
        v.AddRange({x0, y0, z1, x1, y1, z1, x0, y1, z1})
        v.AddRange({x0, y0, z1, x1, y0, z1, x1, y1, z1})
        ' -x
        v.AddRange({x0, y0, z0, x0, y1, z1, x0, y1, z0})
        v.AddRange({x0, y0, z0, x0, y0, z1, x0, y1, z1})
        ' +x
        v.AddRange({x1, y0, z0, x1, y1, z0, x1, y1, z1})
        v.AddRange({x1, y0, z0, x1, y1, z1, x1, y0, z1})
    End Sub

End Module

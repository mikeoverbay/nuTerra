Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' The trees, as proxies, drawn by whether a tank gets through them.
'''
''' PLACEMENTS COME STRAIGHT OUT OF space.bin - cSpTr.trees, which this app
''' already links - so none of nuTerra's tree machinery is needed: not
''' MapTrees (836 lines of instanced SpeedTree rendering with its own shaders
''' and atlases) and not SrtFile (1,054 lines of .srt parsing). Every record
''' carries a full world transform and a species name, which is all a proxy
''' needs.
'''
''' A TRUNK AND A CANOPY, twenty-odd triangles. The owner asked for the trees
''' because a map without them is not the map he is testing on - sight lines,
''' cover and what a hull can see past all depend on them being there. He did
''' not ask for SpeedTree: this app has no textures by design, and a billboard
''' atlas would be the single most expensive thing in it.
'''
''' INSTANCED, because monastery places tens of thousands of plants from a
''' couple of dozen species. One draw call, one mat4 per tree.
'''
''' THE TRUNK IS THE TELL. The owner, 2026-09-16: "i want the trees drawn by
''' if we can drive over them. make them with no trunks and make ones we can
''' drive over forest green (darker)". So a tree that stops a hull keeps its
''' trunk and its brown-to-green shading, and a tree a tank goes through has
''' its trunk dropped in the vertex shader and is flat dark forest green.
'''
''' The picture then states the rule instead of decorating it: anything with a
''' trunk standing in it is something to drive round.
'''
''' STILL NOT CONSULTED ABOUT WHAT BLOCKS. The proxy READS the answer out of
''' the nav grid; it never contributes to it. The bake's trunk bit comes from
''' real bark geometry, not from these twenty triangles, and the proxy's
''' 0.22 m trunk is a DRAWING dimension that merely happens to land in the
''' range BrainTrunks measures.
'''
''' Added 2026-09-16 by nuTerra work, on the owner's ask.
''' </summary>
Module BrainTrees

    Private shader As BrainShader
    Private vao, vbo, ibo, inst_vbo, block_vbo As Integer
    Private indexCount As Integer = 0
    Public Count As Integer = 0

    ''' <summary>How many placements stop a hull. Reported rather than left to
    ''' be eyeballed off the picture.</summary>
    Public Blocking As Integer = 0

    ''' <summary>World XZ of every placement, kept so drivability can be
    ''' decided AFTER the nav grid loads - see MarkDrivable.</summary>
    Private at_xz() As Vector2

    Public Sub Init()
        shader = New BrainShader("tree")
    End Sub

    Public Sub Build()
        Count = 0
        Blocking = 0
        If cSpTr.trees Is Nothing OrElse cSpTr.trees.count = 0 Then
            LogThis("brain: no trees in this space")
            Return
        End If

        Dim sw = Stopwatch.StartNew()
        Dim n = CInt(cSpTr.trees.count)

        ' One mat4 per tree, straight from the file. No filtering by species:
        ' every placement the map made is a thing standing on the ground.
        Dim mats(n * 16 - 1) As Single
        ReDim at_xz(n - 1)
        Dim kept = 0
        ' MIRRORED IN X, exactly as MapTrees.vb:155 does it:
        '     inst.transform * Matrix4.CreateScale(-1, 1, 1)
        '
        ' The tree section of space.bin is in a frame whose X runs opposite to
        ' the one everything else here draws in. The MODEL placements need no
        ' such mirror - MODEL_INDEX_LIST is used raw and the buildings land
        ' correctly - so this is not a global convention, it is this section's,
        ' and that is why only the trees came out wrong. The owner spotted it
        ' on screen: "their locations are flipped in X Y or both".
        '
        ' Post-multiplied, so the mirror applies in WORLD space and moves the
        ' placement as well as the geometry. Pre-multiplying would mirror each
        ' tree about its own trunk and leave every one of them in the wrong
        ' place, which looks almost right and is not.
        Dim mirrorX = Matrix4.CreateScale(-1.0F, 1.0F, 1.0F)
        For i = 0 To n - 1
            Dim m = cSpTr.trees.data(i).transform * mirrorX
            Dim b = kept * 16
            mats(b + 0) = m.M11 : mats(b + 1) = m.M12 : mats(b + 2) = m.M13 : mats(b + 3) = m.M14
            mats(b + 4) = m.M21 : mats(b + 5) = m.M22 : mats(b + 6) = m.M23 : mats(b + 7) = m.M24
            mats(b + 8) = m.M31 : mats(b + 9) = m.M32 : mats(b + 10) = m.M33 : mats(b + 11) = m.M34
            mats(b + 12) = m.M41 : mats(b + 13) = m.M42 : mats(b + 14) = m.M43 : mats(b + 15) = m.M44
            ' THE MIRRORED POSITION, not the raw one. The grid is asked about
            ' where the tree is DRAWN, and a query at the unmirrored X would
            ' read the cell on the far side of the map.
            at_xz(kept) = New Vector2(m.M41, m.M43)
            kept += 1
        Next
        Count = kept
        If kept = 0 Then Return

        build_proxy()

        inst_vbo = GL.GenBuffer()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, inst_vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, kept * 16 * 4, mats, BufferUsageHint.StaticDraw)
        ' A mat4 attribute is FOUR vec4 slots, 2 through 5, each advancing once
        ' per instance. Forgetting the divisor draws one tree and stops.
        For k = 0 To 3
            Dim loc = 2 + k
            GL.EnableVertexAttribArray(loc)
            GL.VertexAttribPointer(loc, 4, VertexAttribPointerType.Float, False, 64, k * 16)
            GL.VertexAttribDivisor(loc, 1)
        Next

        ' One float an instance: does a hull stop here. Allocated now and
        ' FILLED LATER - the nav grid is not loaded when the trees are built,
        ' and asking it here would get "nothing known, refuse nothing" and
        ' paint every tree drivable. Starts at 1 so a run that never reaches
        ' MarkDrivable draws trees the way they have always been drawn, rather
        ' than silently claiming the whole wood is open ground.
        Dim ones(kept - 1) As Single
        For i = 0 To kept - 1
            ones(i) = 1.0F
        Next
        block_vbo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ArrayBuffer, block_vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, kept * 4, ones, BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(7)
        GL.VertexAttribPointer(7, 1, VertexAttribPointerType.Float, False, 4, 0)
        GL.VertexAttribDivisor(7, 1)
        GL.BindVertexArray(0)

        LogThis("brain: {0:N0} tree(s) in {1} ms, {2} triangles each",
                kept, sw.ElapsedMilliseconds, indexCount \ 3)
    End Sub

    ''' <summary>
    ''' Decide, per placement, whether a hull is stopped there.
    '''
    ''' THE GRID'S ANSWER, NOT THIS FILE'S. The owner asked for the trees to be
    ''' drawn by whether we can drive over them, and the only answer worth
    ''' drawing is the one the AI acts on: the square map, sampled at the
    ''' placement's own base.
    '''
    ''' SO A TREE CAN READ BLOCKED FOR SOMETHING ELSE'S REASON - a wall or a
    ''' rock standing in the same square metre. That is not a flaw to correct
    ''' here. The question is whether a tank gets through, and it does not
    ''' matter to the tank which of the two stopped it; a per-species answer
    ''' would draw a tidier picture of a different question.
    '''
    ''' CALLED AFTER THE NAV LOADS, because BrainNav.Standable refuses nothing
    ''' until it is Ready and every tree would come out drivable.
    ''' </summary>
    Public Sub MarkDrivable()
        Blocking = 0
        If Count = 0 OrElse at_xz Is Nothing OrElse block_vbo = 0 Then Return
        If Not BrainNav.Ready Then
            LogThis("brain: trees - no nav grid, so they stay drawn as blocking")
            Return
        End If

        Dim flags(Count - 1) As Single
        For i = 0 To Count - 1
            ' RADIUS ZERO: the question is what stands in this square metre,
            ' not whether a hull would fit beside it.
            Dim open_ = BrainNav.Standable(at_xz(i).X, at_xz(i).Y, 0.0F)
            flags(i) = If(open_, 0.0F, 1.0F)
            If Not open_ Then Blocking += 1
        Next

        GL.BindBuffer(BufferTarget.ArrayBuffer, block_vbo)
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, Count * 4, flags)
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0)

        LogThis("brain: trees - {0:N0} of {1:N0} stop a hull ({2:0.0}%). The rest are drawn " &
                "trunkless in forest green, because a tank goes through them",
                Blocking, Count, 100.0 * Blocking / Count)
    End Sub

    ''' <summary>
    ''' The proxy: a tapered trunk and a canopy octahedron, in metres, standing
    ''' on the origin so the placement transform puts it on the ground.
    '''
    ''' Nominal size, NOT the species' own. The .srt holds the real dimensions
    ''' and parsing it is the 1,054-line file this deliberately avoids; the
    ''' placement transform carries scale, so a big tree still comes out big.
    '''
    ''' SEVEN FLOATS A VERTEX, not six: the last marks the trunk, so the vertex
    ''' shader can drop it on a tree that stops nothing. Every index divisor
    ''' below moved with it - they count VERTICES, and one left at 6 walks the
    ''' buffer at the wrong stride and builds a tree out of pieces of others.
    ''' </summary>
    ''' <summary>
    ''' The canopy's lowest vertex, metres up the proxy. It overlaps the
    ''' trunk's upper half rather than reaching the ground.
    '''
    ''' PUBLIC, AND THE SHADER IS TOLD IT. tree.vert lowers a trunkless tree by
    ''' exactly this much so it sits on the ground, and a copy of the number
    ''' typed into the GLSL is the same trap that put 0.7 in two files after it
    ''' was retired. One constant, passed as a uniform.
    ''' </summary>
    Public Const CB As Single = 2.6F

    Private Sub build_proxy()
        Dim v As New List(Of Single)     ' x y z  nx ny nz  isTrunk
        Dim idx As New List(Of UInteger)

        Const STRIDE As Integer = 7
        Const TR As Single = 0.22F       ' trunk half-width at the base
        Const TH As Single = 4.0F        ' trunk height to the canopy
        Const CR As Single = 2.6F        ' canopy half-width
        Const CT As Single = 9.5F        ' canopy top

        ' Trunk: a four-sided taper. Flat normals per face, which is all a
        ' silhouette needs.
        Dim tq = {New Vector2(-TR, -TR), New Vector2(TR, -TR),
                  New Vector2(TR, TR), New Vector2(-TR, TR)}
        For i = 0 To 3
            Dim a = tq(i), b = tq((i + 1) And 3)
            Dim nx = (a.X + b.X) * 0.5F, nz = (a.Y + b.Y) * 0.5F
            Dim nl = CSng(Math.Sqrt(nx * nx + nz * nz))
            If nl > 0 Then nx /= nl : nz /= nl
            Dim base_ = CUInt(v.Count \ STRIDE)
            add(v, a.X, 0.0F, a.Y, nx, 0.0F, nz, 1.0F)
            add(v, b.X, 0.0F, b.Y, nx, 0.0F, nz, 1.0F)
            add(v, b.X * 0.6F, TH, b.Y * 0.6F, nx, 0.0F, nz, 1.0F)
            add(v, a.X * 0.6F, TH, a.Y * 0.6F, nx, 0.0F, nz, 1.0F)
            idx.AddRange({base_, base_ + 1UI, base_ + 2UI, base_, base_ + 2UI, base_ + 3UI})
        Next

        ' Canopy: an octahedron. Six vertices, eight faces, and it reads as a
        ' tree at every distance this app is looked at from.
        Dim top = CUInt(v.Count \ STRIDE)
        add(v, 0.0F, CT, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F)
        Dim bot = CUInt(v.Count \ STRIDE)
        add(v, 0.0F, CB, 0.0F, 0.0F, -1.0F, 0.0F, 0.0F)
        Dim ring = CUInt(v.Count \ STRIDE)
        Dim mid = (CB + CT) * 0.5F
        For i = 0 To 3
            Dim a = CSng(i * Math.PI / 2.0)
            Dim x = CSng(Math.Cos(a)) * CR, z = CSng(Math.Sin(a)) * CR
            add(v, x, mid, z, CSng(Math.Cos(a)), 0.3F, CSng(Math.Sin(a)), 0.0F)
        Next
        For i = 0 To 3
            Dim a = ring + CUInt(i), b = ring + CUInt((i + 1) And 3)
            idx.AddRange({top, a, b})
            idx.AddRange({bot, b, a})
        Next

        vao = GL.GenVertexArray()
        GL.BindVertexArray(vao)
        vbo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        Dim va = v.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, va.Length * 4, va, BufferUsageHint.StaticDraw)
        Dim bytes = STRIDE * 4
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, bytes, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, bytes, 12)
        GL.EnableVertexAttribArray(6)
        GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, False, bytes, 24)

        ibo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo)
        Dim ia = idx.ToArray()
        GL.BufferData(BufferTarget.ElementArrayBuffer, ia.Length * 4, ia, BufferUsageHint.StaticDraw)
        indexCount = ia.Length
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>One vertex. The last field is 1 on the trunk and 0 on the
    ''' canopy - the vertex shader drops the trunk on a tree that stops
    ''' nothing.</summary>
    Private Sub add(v As List(Of Single), x As Single, y As Single, z As Single,
                    nx As Single, ny As Single, nz As Single, isTrunk As Single)
        v.AddRange({x, y, z, nx, ny, nz, isTrunk})
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready OrElse Count = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' The bake's own tree colour, so a tree here and a tree in the kind
        ' legend are the same green.
        Dim rgb = ModelKind.KIND_RGB(ModelKind.KIND_TREE)
        shader.SetVec3("kindColour", New Vector3(rgb(0) / 255.0F, rgb(1) / 255.0F, rgb(2) / 255.0F))
        ' Forest green, darker than the kind colour on purpose: the ones a tank
        ' drives through should sit back so the ones that stop it are what the
        ' eye lands on.
        shader.SetVec3("driveColour", New Vector3(0.09F, 0.24F, 0.11F))
        shader.SetFloat("canopyBottom", CB)
        GL.BindVertexArray(vao)
        GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount,
                                 DrawElementsType.UnsignedInt, IntPtr.Zero, Count)
        GL.BindVertexArray(0)
    End Sub

End Module

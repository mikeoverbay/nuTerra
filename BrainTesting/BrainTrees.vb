Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' The trees, as proxies.
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
''' THEY DO NOT BLOCK. The crushable rule is the bake's: kind TREE is
''' crushable, and a tank drives through a hedge and knocks a tree down. So
''' the nav grid leaves them open and this file is about what the owner SEES,
''' not about what stops a hull. If a species ever needs to block - a trunk
''' thick enough to stop a tier 10 - that is a trunk radius per species, and
''' it belongs beside the crushable rule in BrainNav, not here.
'''
''' Added 2026-09-16 by nuTerra work, on the owner's ask.
''' </summary>
Module BrainTrees

    Private shader As BrainShader
    Private vao, vbo, ibo, inst_vbo As Integer
    Private indexCount As Integer = 0
    Public Count As Integer = 0

    Public Sub Init()
        shader = New BrainShader("tree")
    End Sub

    Public Sub Build()
        Count = 0
        If cSpTr.trees Is Nothing OrElse cSpTr.trees.count = 0 Then
            LogThis("brain: no trees in this space")
            Return
        End If

        Dim sw = Stopwatch.StartNew()
        Dim n = CInt(cSpTr.trees.count)

        ' One mat4 per tree, straight from the file. No filtering by species:
        ' every placement the map made is a thing standing on the ground.
        Dim mats(n * 16 - 1) As Single
        Dim kept = 0
        For i = 0 To n - 1
            Dim m = cSpTr.trees.data(i).transform
            Dim b = kept * 16
            mats(b + 0) = m.M11 : mats(b + 1) = m.M12 : mats(b + 2) = m.M13 : mats(b + 3) = m.M14
            mats(b + 4) = m.M21 : mats(b + 5) = m.M22 : mats(b + 6) = m.M23 : mats(b + 7) = m.M24
            mats(b + 8) = m.M31 : mats(b + 9) = m.M32 : mats(b + 10) = m.M33 : mats(b + 11) = m.M34
            mats(b + 12) = m.M41 : mats(b + 13) = m.M42 : mats(b + 14) = m.M43 : mats(b + 15) = m.M44
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
        GL.BindVertexArray(0)

        LogThis("brain: {0:N0} tree(s) in {1} ms, {2} triangles each - crushable, not blocking",
                kept, sw.ElapsedMilliseconds, indexCount \ 3)
    End Sub

    ''' <summary>
    ''' The proxy: a tapered trunk and a canopy octahedron, in metres, standing
    ''' on the origin so the placement transform puts it on the ground.
    '''
    ''' Nominal size, NOT the species' own. The .srt holds the real dimensions
    ''' and parsing it is the 1,054-line file this deliberately avoids; the
    ''' placement transform carries scale, so a big tree still comes out big.
    ''' </summary>
    Private Sub build_proxy()
        Dim v As New List(Of Single)     ' x y z  nx ny nz
        Dim idx As New List(Of UInteger)

        Const TR As Single = 0.22F       ' trunk half-width at the base
        Const TH As Single = 4.0F        ' trunk height to the canopy
        Const CR As Single = 2.6F        ' canopy half-width
        Const CB As Single = 2.6F        ' canopy bottom
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
            Dim base_ = CUInt(v.Count \ 6)
            add(v, a.X, 0.0F, a.Y, nx, 0.0F, nz)
            add(v, b.X, 0.0F, b.Y, nx, 0.0F, nz)
            add(v, b.X * 0.6F, TH, b.Y * 0.6F, nx, 0.0F, nz)
            add(v, a.X * 0.6F, TH, a.Y * 0.6F, nx, 0.0F, nz)
            idx.AddRange({base_, base_ + 1UI, base_ + 2UI, base_, base_ + 2UI, base_ + 3UI})
        Next

        ' Canopy: an octahedron. Six vertices, eight faces, and it reads as a
        ' tree at every distance this app is looked at from.
        Dim top = CUInt(v.Count \ 6)
        add(v, 0.0F, CT, 0.0F, 0.0F, 1.0F, 0.0F)
        Dim bot = CUInt(v.Count \ 6)
        add(v, 0.0F, CB, 0.0F, 0.0F, -1.0F, 0.0F)
        Dim ring = CUInt(v.Count \ 6)
        Dim mid = (CB + CT) * 0.5F
        For i = 0 To 3
            Dim a = CSng(i * Math.PI / 2.0)
            Dim x = CSng(Math.Cos(a)) * CR, z = CSng(Math.Sin(a)) * CR
            add(v, x, mid, z, CSng(Math.Cos(a)), 0.3F, CSng(Math.Sin(a)))
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
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)

        ibo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo)
        Dim ia = idx.ToArray()
        GL.BufferData(BufferTarget.ElementArrayBuffer, ia.Length * 4, ia, BufferUsageHint.StaticDraw)
        indexCount = ia.Length
        GL.BindVertexArray(0)
    End Sub

    Private Sub add(v As List(Of Single), x As Single, y As Single, z As Single,
                    nx As Single, ny As Single, nz As Single)
        v.AddRange({x, y, z, nx, ny, nz})
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready OrElse Count = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' The bake's own tree colour, so a tree here and a tree in the kind
        ' legend are the same green.
        Dim rgb = ModelKind.KIND_RGB(ModelKind.KIND_TREE)
        shader.SetVec3("kindColour", New Vector3(rgb(0) / 255.0F, rgb(1) / 255.0F, rgb(2) / 255.0F))
        GL.BindVertexArray(vao)
        GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount,
                                 DrawElementsType.UnsignedInt, IntPtr.Zero, Count)
        GL.BindVertexArray(0)
    End Sub

End Module

Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' THE CHAIN WALK, DRAWN IN THE WORLD.
'''
''' "I want you to draw the connection ray hits in world space in neon blue.
'''  Mark the ends with something. mark where you found a path resolution.
'''  I want to see the points you decide has blocked us as a red cube."
''' - the owner, 2026-09-19.
'''
''' The scope shows the returns; it does not show what the brain MADE of them.
''' Two runs with identical scopes can be chaining those returns into completely
''' different objects, finding different ends, and rejecting different ways
''' past - and none of that is visible in a top-down dial or in a number.
''' Every real diagnosis tonight came from putting the brain's own reasoning on
''' screen next to the world it was reasoning about, and this is that for the
''' one step that decides where the tank goes.
'''
''' WHAT THE COLOURS MEAN:
'''   neon blue line   two neighbouring returns the brain has chained into one
'''                    object. A chain is what it believes is in the way.
'''   yellow post      an END of a chain - where the brain thinks that object
'''                    stops and open ground starts. These are the only places
'''                    it will ever try to go round.
'''   red cube         an end it tried and REJECTED: the hull could not pivot to
'''                    it, or the plank past it was not clear. A wall of red
'''                    cubes is the picture of being trapped.
'''   green post       the way past it chose this tick, drawn out along the
'''                    bearing it will actually steer.
'''
''' Filled by the brain each tick through Begin/Chain/EndPoint/Blocked/Pick and
''' uploaded only when it changes. Off the terminal's critical path entirely:
''' if SHOW is false nothing is recorded and nothing is drawn.
'''
''' Added 2026-09-19 by Tank AI work.
''' </summary>
Module BrainWalkView

    Public SHOW As Boolean = True

    ''' <summary>Off the ground so it does not z-fight the terrain.</summary>
    Private Const LIFT As Single = 0.6F

    ''' <summary>Half-edge of a blocked marker, metres.</summary>
    Private Const CUBE As Single = 0.9F

    ''' <summary>How tall the end and pick posts stand.</summary>
    Private Const POST As Single = 9.0F

    Private ReadOnly CHAIN_RGB As New Vector3(0.35F, 0.95F, 1.00F)   ' neon blue
    Private ReadOnly END_RGB As New Vector3(1.00F, 0.90F, 0.15F)   ' yellow
    ' THREE KINDS OF BLOCKED, because they lead to three different moves.
    ' NOT GREEN. The map is grass and several thousand trees, so the one
    ' colour that cannot be spotted on it is the one a way through was drawn
    ' in. White and magenta appear nowhere in the terrain.
    Private ReadOnly OPEN_RGB As New Vector3(1.00F, 1.00F, 1.00F)   ' white
    Private ReadOnly GRAZE_RGB As New Vector3(1.00F, 0.62F, 0.05F)   ' amber
    Private ReadOnly BLOCK_RGB As New Vector3(1.00F, 0.12F, 0.12F)   ' red
    Private ReadOnly PICK_RGB As New Vector3(1.00F, 0.10F, 1.00F)   ' magenta

    Private shader As BrainShader
    Private vaoL, vboL, vaoP, vboP, vaoC, vboC, vaoK, vboK As Integer
    Private vaoO, vboO, vaoG, vboG, vaoM, vboM As Integer

    ' Lines: the chains. Posts: chain ends. Picks: the chosen way past.
    ' Cubes: rejected ends, as triangles.
    Private ReadOnly lines_ As New List(Of Single)
    Private ReadOnly posts As New List(Of Single)
    Private ReadOnly picks As New List(Of Single)
    Private ReadOnly cubesOpen As New List(Of Single)
    Private ReadOnly cubesGraze As New List(Of Single)
    Private ReadOnly cubes As New List(Of Single)
    Private ReadOnly cubePick As New List(Of Single)
    Private dirty As Boolean = False
    Private saySoFar As Integer = 0

    Public Sub Init()
        shader = New BrainShader("line")
        vaoL = GL.GenVertexArray() : vboL = GL.GenBuffer() : setup(vaoL, vboL)
        vaoP = GL.GenVertexArray() : vboP = GL.GenBuffer() : setup(vaoP, vboP)
        vaoC = GL.GenVertexArray() : vboC = GL.GenBuffer() : setup(vaoC, vboC)
        vaoK = GL.GenVertexArray() : vboK = GL.GenBuffer() : setup(vaoK, vboK)
        vaoO = GL.GenVertexArray() : vboO = GL.GenBuffer() : setup(vaoO, vboO)
        vaoG = GL.GenVertexArray() : vboG = GL.GenBuffer() : setup(vaoG, vboG)
        vaoM = GL.GenVertexArray() : vboM = GL.GenBuffer() : setup(vaoM, vboM)
    End Sub

    Private Sub setup(vao As Integer, vbo As Integer)
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>A fresh tick. Everything from the last one goes - this is what
    ''' the brain believes NOW, and a stale chain drawn over a live one is worse
    ''' than no picture at all.</summary>
    Public Sub Begin()
        If Not SHOW Then Return
        lines_.Clear()
        posts.Clear()
        picks.Clear()
        cubes.Clear()
        cubesOpen.Clear()
        cubesGraze.Clear()
        cubePick.Clear()
        dirty = True
    End Sub

    ''' <summary>Two neighbouring returns the brain has chained into one
    ''' object.</summary>
    Public Sub Chain(a As Vector2, b As Vector2)
        If Not SHOW Then Return
        push(lines_, a, LIFT)
        push(lines_, b, LIFT)
    End Sub

    ''' <summary>Where a chain stops - a place worth trying to go round.</summary>
    Public Sub EndPoint(p As Vector2)
        If Not SHOW Then Return
        push(posts, p, 0.0F)
        push(posts, p, POST)
    End Sub

    ''' <summary>
    ''' A point the walk tested, coloured by what kind of answer it got.
    '''
    ''' 0 the plank is open past here - a way through.
    ''' 1 the plank is touched down one edge only; the hull clears it by
    '''   carrying on. Worth seeing separately because it is a GO, and it
    '''   used to be indistinguishable from a wall.
    ''' 2 the plank is shut across its width. No way between the hits.
    ''' </summary>
    Public Sub Mark(p As Vector2, verdict As Integer)
        If Not SHOW Then Return
        Select Case verdict
            Case 0 : box(cubesOpen, p)
            Case 1 : box(cubesGraze, p)
            Case Else : box(cubes, p)
        End Select
    End Sub

    ''' <summary>The way past it chose, drawn from the hull out along the
    ''' bearing it will actually steer - so a wrong choice is visible as a line
    ''' pointing at the wrong thing rather than as a number to be trusted.</summary>
    ''' <summary>
    ''' THE CHOSEN WAY, AS SOLID GEOMETRY.
    '''
    ''' It was drawn as a thick line and that did nothing: glLineWidth is
    ''' capped at 1.0 in an OpenGL CORE profile, so every width set in this
    ''' file has been silently ignored. Six-pixel chains and a seven-pixel
    ''' pick were all one pixel, which is why making it thicker never helped
    ''' and why the owner kept reporting the aim point missing when it was
    ''' in the buffer every frame.
    '''
    ''' So it gets a CUBE, larger than a blocked marker and in a colour
    ''' nothing in grass or trees shares, drawn last through everything.
    ''' </summary>
    ''' <summary>
    ''' MOVE THE AIM WITHOUT WIPING THE PICTURE.
    '''
    ''' On ticks where the plan holds, the brain does not walk the chains
    ''' again - so calling Begin() there cleared every chain, end and cube
    ''' and left only the aim. The cubes appeared on the ticks that walked
    ''' and vanished on the ticks that did not, which reads as a flicker and
    ''' is really the view being told the world is empty.
    ''' </summary>
    Public Sub RePick(from_ As Vector2, toward As Vector2)
        If Not SHOW Then Return
        picks.Clear()
        cubePick.Clear()
        Pick(from_, toward)
    End Sub

    Public Sub Pick(from_ As Vector2, toward As Vector2)
        If Not SHOW Then Return
        big_box(cubePick, toward)
        push(picks, from_, LIFT)
        push(picks, toward, LIFT)
        push(picks, toward, 0.0F)
        push(picks, toward, POST)
    End Sub

    Private Sub push(into As List(Of Single), p As Vector2, up As Single)
        Dim y = 0.0F
        Try
            y = get_Y_at_XZ(p.X, p.Y)
        Catch
        End Try
        into.Add(p.X) : into.Add(y + up) : into.Add(p.Y)
    End Sub

    ''' <summary>A solid cube as twelve triangles. Solid rather than wireframe
    ''' because these are the answer to "why did it not go there" and they have
    ''' to be readable against a forest at fifty metres.</summary>
    ''' <summary>Half again the size, so the answer is never one of the
    ''' crowd.</summary>
    Private Sub big_box(into As List(Of Single), p As Vector2)
        box(into, p, CUBE * 1.7F)
    End Sub

    Private Sub box(into As List(Of Single), p As Vector2)
        box(into, p, CUBE)
    End Sub

    Private Sub box(into As List(Of Single), p As Vector2, half As Single)
        Dim y = 0.0F
        Try
            y = get_Y_at_XZ(p.X, p.Y)
        Catch
        End Try
        Dim x0 = p.X - half, x1 = p.X + half
        Dim z0 = p.Y - half, z1 = p.Y + half
        Dim y0 = y + 0.2F, y1 = y + 0.2F + half * 2.0F

        ' Eight corners, then the six faces as pairs of triangles.
        Dim c = New Single(,) {
            {x0, y0, z0}, {x1, y0, z0}, {x1, y0, z1}, {x0, y0, z1},
            {x0, y1, z0}, {x1, y1, z0}, {x1, y1, z1}, {x0, y1, z1}}
        Dim faces = New Integer(,) {
            {0, 1, 2}, {0, 2, 3},      ' bottom
            {4, 6, 5}, {4, 7, 6},      ' top
            {0, 4, 5}, {0, 5, 1},      ' -z
            {1, 5, 6}, {1, 6, 2},      ' +x
            {2, 6, 7}, {2, 7, 3},      ' +z
            {3, 7, 4}, {3, 4, 0}}      ' -x
        For f = 0 To 11
            For v = 0 To 2
                Dim k = faces(f, v)
                into.Add(c(k, 0)) : into.Add(c(k, 1)) : into.Add(c(k, 2))
            Next
        Next
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        If lines_.Count = 0 AndAlso posts.Count = 0 AndAlso cubes.Count = 0 AndAlso
           cubesOpen.Count = 0 AndAlso cubesGraze.Count = 0 AndAlso
           cubePick.Count = 0 AndAlso
           picks.Count = 0 Then Return

        If dirty Then
            upload(vaoL, vboL, lines_)
            upload(vaoP, vboP, posts)
            upload(vaoC, vboC, picks)
            upload(vaoK, vboK, cubes)
            upload(vaoO, vboO, cubesOpen)
            upload(vaoG, vboG, cubesGraze)
            upload(vaoM, vboM, cubePick)
            dirty = False
        End If

        If saySoFar < 4 Then
            saySoFar += 1
            LogThis("brain: walkview - chains {0}, posts {1}, picks {2}, " &
                    "cubes {3}/{4}/{5}, show {6}, ready {7}",
                    lines_.Count \ 3, posts.Count \ 3, picks.Count \ 3,
                    cubesOpen.Count \ 3, cubesGraze.Count \ 3, cubes.Count \ 3,
                    SHOW, shader.Ready)
        End If
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' Depth test on so a hill hides what is behind it; depth write off for
        ' the lines so they do not leave a lip hulls clip against. The cubes DO
        ' write - they are solid objects and should occlude properly.
        GL.DepthMask(False)
        ' FAT. The radar draws a hundred and twenty thin blue rays from the
        ' same origin; a two pixel chain line is invisible in that. These are
        ' six pixels and a lighter blue so they read as a different thing.
        GL.LineWidth(6.0F)

        If lines_.Count > 0 Then
            shader.SetVec3("colour", CHAIN_RGB)
            GL.BindVertexArray(vaoL)
            GL.DrawArrays(PrimitiveType.Lines, 0, lines_.Count \ 3)
        End If
        GL.LineWidth(4.0F)
        If posts.Count > 0 Then
            shader.SetVec3("colour", END_RGB)
            GL.BindVertexArray(vaoP)
            GL.DrawArrays(PrimitiveType.Lines, 0, posts.Count \ 3)
        End If

        GL.LineWidth(1.0F)
        GL.DepthMask(True)

        If cubesOpen.Count > 0 Then
            shader.SetVec3("colour", OPEN_RGB)
            GL.BindVertexArray(vaoO)
            GL.DrawArrays(PrimitiveType.Triangles, 0, cubesOpen.Count \ 3)
        End If
        If cubesGraze.Count > 0 Then
            shader.SetVec3("colour", GRAZE_RGB)
            GL.BindVertexArray(vaoG)
            GL.DrawArrays(PrimitiveType.Triangles, 0, cubesGraze.Count \ 3)
        End If
        If cubes.Count > 0 Then
            shader.SetVec3("colour", BLOCK_RGB)
            GL.BindVertexArray(vaoK)
            GL.DrawArrays(PrimitiveType.Triangles, 0, cubes.Count \ 3)
        End If

        ' THE CHOSEN WAY, LAST AND THROUGH EVERYTHING.
        '
        ' It was drawn third of six, thin, with depth testing on - so two
        ' green segments sat behind fifty-two solid red cubes and the owner
        ' reported no aim point at all. It was in the buffer the whole time.
        ' The one thing that must never be hidden is the answer.
        If cubePick.Count > 0 Then
            GL.Disable(EnableCap.DepthTest)
            shader.SetVec3("colour", PICK_RGB)
            GL.BindVertexArray(vaoM)
            GL.DrawArrays(PrimitiveType.Triangles, 0, cubePick.Count \ 3)
            GL.Enable(EnableCap.DepthTest)
        End If

        If picks.Count > 0 Then
            GL.Disable(EnableCap.DepthTest)
            GL.LineWidth(7.0F)
            shader.SetVec3("colour", PICK_RGB)
            GL.BindVertexArray(vaoC)
            GL.DrawArrays(PrimitiveType.Lines, 0, picks.Count \ 3)
            GL.LineWidth(1.0F)
            GL.Enable(EnableCap.DepthTest)
        End If

        GL.BindVertexArray(0)
    End Sub

    Private Sub upload(vao As Integer, vbo As Integer, v As List(Of Single))
        If v.Count = 0 Then Return
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        Dim arr = v.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.DynamicDraw)
        GL.BindVertexArray(0)
    End Sub

End Module

Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' AN OFF-SCREEN TARGET AND THE 2D PRIMITIVES TO FILL IT.
'''
''' "make a FBO called Scope and draw in to a texture and put the texture on
'''  the ortho screen." - the owner, 2026-09-17.
'''
''' WHY RENDER TO A TEXTURE AND NOT STRAIGHT TO THE SCREEN. The scope is a
''' picture with its own coordinate system, its own size and its own opacity.
''' Drawn directly it has to know where on the display it lives, and every
''' primitive inside it carries that offset; drawn into its own target it is
''' just a square picture that starts at 0,0, and WHERE it ends up is one quad
''' decided somewhere else. Moving it becomes changing four numbers instead of
''' auditing every line that draws into it.
'''
''' It also makes the whole panel fade as one thing. Translucency applied per
''' line lets overlapping lines stack up darker where they cross, which looks
''' like the drawing is wrong rather than like it is faint.
'''
''' NO DEPTH BUFFER. This is a flat picture and draw order is the only ordering
''' it has - attaching depth would mean every primitive needed a Z nobody has a
''' reason to choose.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Public Class BrainCanvas

    Public ReadOnly Property Size As Integer = 0
    Private fbo As Integer = 0
    Private tex As Integer = 0

    ''' <summary>The finished picture, for another canvas to draw in.</summary>
    Public ReadOnly Property Texture As Integer
        Get
            Return tex
        End Get
    End Property

    Private Shared lineShader As BrainShader = Nothing
    Private Shared blitShader As BrainShader = Nothing
    Private Shared panelShader As BrainShader = Nothing
    Private Shared worldVao As Integer = 0
    Private Shared worldVbo As Integer = 0
    Private Shared vao As Integer = 0
    Private Shared vbo As Integer = 0
    Private Shared quadVao As Integer = 0
    Private Shared quadVbo As Integer = 0

    ' pos(2) colour(4) = 6 floats
    Private ReadOnly lines As New List(Of Single)
    Private ReadOnly tris As New List(Of Single)

    Public Sub New(sizePx As Integer, name As String)
        _Size = sizePx
        fbo = GL.GenFramebuffer()
        tex = GL.GenTexture()
        GL.BindTexture(TextureTarget.Texture2D, tex)
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                      sizePx, sizePx, 0, PixelFormat.Rgba, PixelType.UnsignedByte,
                      IntPtr.Zero)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                        CInt(TextureMinFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                        CInt(TextureMagFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        CInt(TextureWrapMode.ClampToEdge))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        CInt(TextureWrapMode.ClampToEdge))
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                                FramebufferAttachment.ColorAttachment0,
                                TextureTarget.Texture2D, tex, 0)
        Dim st = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        If st <> FramebufferErrorCode.FramebufferComplete Then
            LogThis("brain: FBO '{0}' is {1} - it will not draw", name, st.ToString())
        Else
            LogThis("brain: FBO '{0}' ready, {1}x{1}", name, sizePx)
        End If
    End Sub

    Private Shared Sub ensure_shared()
        If lineShader IsNot Nothing Then Return
        lineShader = New BrainShader("overlay")
        blitShader = New BrainShader("blit")
        panelShader = New BrainShader("panel")

        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, False, 24, 8)

        quadVao = GL.GenVertexArray()
        quadVbo = GL.GenBuffer()
        GL.BindVertexArray(quadVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 16, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, False, 16, 8)
        worldVao = GL.GenVertexArray()
        worldVbo = GL.GenBuffer()
        GL.BindVertexArray(worldVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, worldVbo)
        ' pos(3) uv(2) = 5 floats, 20 bytes
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 20, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, False, 20, 12)

        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' STAMP ANOTHER CANVAS OVER THIS ONE, full size.
    '''
    ''' What this is for: a scope's rings, hull outline and border are IDENTICAL
    ''' every frame and are most of its vertices. Drawn once into their own
    ''' canvas, they become a single textured quad here - so the per-frame work
    ''' is only the part that actually changed, which is the rays.
    '''
    ''' Drawn IMMEDIATELY, not queued, so it lands under the lines and
    ''' triangles that End() flushes afterwards. That ordering is the whole
    ''' point: chrome beneath, data on top.
    ''' </summary>
    Public Sub DrawCanvas(other As BrainCanvas)
        ensure_shared()
        Dim sz = CSng(_Size)
        Dim q() As Single = {
            0.0F, 0.0F, 0.0F, 1.0F,
            sz, 0.0F, 1.0F, 1.0F,
            sz, sz, 1.0F, 0.0F,
            0.0F, 0.0F, 0.0F, 1.0F,
            sz, sz, 1.0F, 0.0F,
            0.0F, sz, 0.0F, 0.0F}
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        ' SEPARATE BLEND FOR ALPHA, and this is not a detail.
        '
        ' One blend function applied to colour AND alpha SQUARES the alpha in a
        ' render target: dstA = srcA*srcA + dstA*(1-srcA), so a backdrop drawn
        ' at 0.92 lands at 0.85, every line on top inherits that, and the blit
        ' multiplies by its own fade again. The scope came out washed out and
        ' nothing in the code said a number below what was written.
        '
        ' Colour still blends normally; alpha ACCUMULATES - dstA = srcA +
        ' dstA*(1-srcA) - which is what "how opaque is this texel now" means.
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)
        blitShader.Use()
        blitShader.SetVec2("screen", sz, sz)
        Dim fl = GL.GetUniformLocation(blitShader.Id, "fade")
        If fl >= 0 Then GL.Uniform1(fl, 1.0F)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, other.Texture)
        Dim sl = GL.GetUniformLocation(blitShader.Id, "source")
        If sl >= 0 Then GL.Uniform1(sl, 0)
        GL.BindVertexArray(quadVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, q.Length * 4, q,
                      BufferUsageHint.StreamDraw)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6)
        GL.BindVertexArray(0)
    End Sub

    ' ---- the primitives, in this canvas's own pixels ---------------------

    Public Sub Line(x0 As Single, y0 As Single, x1 As Single, y1 As Single,
                    c As Vector4)
        push(lines, x0, y0, c)
        push(lines, x1, y1, c)
    End Sub

    Public Sub Rect(x0 As Single, y0 As Single, x1 As Single, y1 As Single,
                    c As Vector4)
        Line(x0, y0, x1, y0, c)
        Line(x1, y0, x1, y1, c)
        Line(x1, y1, x0, y1, c)
        Line(x0, y1, x0, y0, c)
    End Sub

    Public Sub RectFill(x0 As Single, y0 As Single, x1 As Single, y1 As Single,
                        c As Vector4)
        push(tris, x0, y0, c) : push(tris, x1, y0, c) : push(tris, x1, y1, c)
        push(tris, x0, y0, c) : push(tris, x1, y1, c) : push(tris, x0, y1, c)
    End Sub

    ''' <summary>A ring, as a line loop. Segments scale with radius so a big
    ''' range ring is not visibly a polygon and a small one is not paying for
    ''' sixty vertices.</summary>
    Public Sub Circle(cx As Single, cy As Single, r As Single, c As Vector4)
        Dim seg = Math.Max(12, Math.Min(64, CInt(r * 0.7F)))
        Dim prevX = cx + r, prevY = cy
        For i = 1 To seg
            Dim a = CSng(i / CSng(seg) * Math.PI * 2.0)
            Dim x = cx + r * CSng(Math.Cos(a))
            Dim y = cy + r * CSng(Math.Sin(a))
            Line(prevX, prevY, x, y, c)
            prevX = x : prevY = y
        Next
    End Sub

    Public Sub Disc(cx As Single, cy As Single, r As Single, c As Vector4)
        Dim seg = Math.Max(8, Math.Min(32, CInt(r * 2.0F)))
        For i = 0 To seg - 1
            Dim a0 = CSng(i / CSng(seg) * Math.PI * 2.0)
            Dim a1 = CSng((i + 1) / CSng(seg) * Math.PI * 2.0)
            push(tris, cx, cy, c)
            push(tris, cx + r * CSng(Math.Cos(a0)), cy + r * CSng(Math.Sin(a0)), c)
            push(tris, cx + r * CSng(Math.Cos(a1)), cy + r * CSng(Math.Sin(a1)), c)
        Next
    End Sub

    Private Shared Sub push(v As List(Of Single), x As Single, y As Single, c As Vector4)
        v.Add(x) : v.Add(y)
        v.Add(c.X) : v.Add(c.Y) : v.Add(c.Z) : v.Add(c.W)
    End Sub

    ' ---- the passes -------------------------------------------------------

    ''' <summary>Bind the target and clear it. Everything queued before this is
    ''' discarded, which is the honest behaviour for a frame that never got
    ''' drawn.</summary>
    Public Sub Begin(clear As Vector4)
        ensure_shared()
        lines.Clear()
        tris.Clear()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        GL.Viewport(0, 0, _Size, _Size)
        GL.ClearColor(clear.X, clear.Y, clear.Z, clear.W)
        GL.Clear(ClearBufferMask.ColorBufferBit)
    End Sub

    ''' <summary>
    ''' DRAW WHAT IS QUEUED NOW, and stay bound.
    '''
    ''' Because BrainText draws IMMEDIATELY and this class QUEUES. Text written
    ''' before End() therefore landed underneath every rectangle and line the
    ''' canvas was holding - on the tank card that meant the backing panel was
    ''' painted over its own text, every frame, and no change to the text's
    ''' colour or the atlas could show through a rectangle drawn after it.
    '''
    ''' Call this, then draw text, then End(). Two draw orders meeting in one
    ''' target needs someone to say which comes first.
    ''' </summary>
    Public Sub Flush()
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)
        lineShader.Use()
        lineShader.SetVec2("screen", CSng(_Size), CSng(_Size))
        If tris.Count > 0 Then flush(tris, PrimitiveType.Triangles)
        If lines.Count > 0 Then flush(lines, PrimitiveType.Lines)
        tris.Clear()
        lines.Clear()
    End Sub

    ''' <summary>Flush what was queued and give the screen back. The caller's
    ''' viewport is restored from the values it passes, because GL has no idea
    ''' what it was and guessing it from the window is how a resized window
    ''' ends up rendering into a corner.</summary>
    Public Sub [End](restoreW As Integer, restoreH As Integer)
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)

        lineShader.Use()
        lineShader.SetVec2("screen", CSng(_Size), CSng(_Size))
        If tris.Count > 0 Then flush(tris, PrimitiveType.Triangles)
        If lines.Count > 0 Then flush(lines, PrimitiveType.Lines)

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.Viewport(0, 0, restoreW, restoreH)
    End Sub

    Private Shared Sub flush(v As List(Of Single), mode As PrimitiveType)
        Dim arr = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.StreamDraw)
        GL.DrawArrays(mode, 0, arr.Length \ 6)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' A FLAT RECTANGLE STRAIGHT ONTO THE SCREEN, no target involved.
    '''
    ''' For putting a known colour behind something translucent. Checking
    ''' transparency against the map is guesswork - the map is green here, grey
    ''' there and moving - so a solid patch of one colour is the only way to see
    ''' what the alpha is actually doing.
    ''' </summary>
    Public Shared Sub ScreenRect(x As Single, y As Single, w As Single, h As Single,
                                 c As Vector4, screenW As Single, screenH As Single)
        ensure_shared()
        Dim v As New List(Of Single)
        push(v, x, y, c) : push(v, x + w, y, c) : push(v, x + w, y + h, c)
        push(v, x, y, c) : push(v, x + w, y + h, c) : push(v, x, y + h, c)
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)
        lineShader.Use()
        lineShader.SetVec2("screen", screenW, screenH)
        flush(v, PrimitiveType.Triangles)
    End Sub

    ''' <summary>Hang the finished picture on the screen, in screen pixels.</summary>
    Public Sub Blit(x As Single, y As Single, w As Single, h As Single,
                    screenW As Single, screenH As Single, fade As Single)
        ensure_shared()
        ' V is flipped: a framebuffer's origin is bottom-left and this canvas
        ' draws top-left, so sampling it the obvious way hangs the scope upside
        ' down - which looks like a bug in the scope and is a bug in the blit.
        Dim q() As Single = {
            x, y, 0.0F, 1.0F,
            x + w, y, 1.0F, 1.0F,
            x + w, y + h, 1.0F, 0.0F,
            x, y, 0.0F, 1.0F,
            x + w, y + h, 1.0F, 0.0F,
            x, y + h, 0.0F, 0.0F}

        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Blend)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)
        blitShader.Use()
        blitShader.SetVec2("screen", screenW, screenH)
        Dim fl = GL.GetUniformLocation(blitShader.Id, "fade")
        If fl >= 0 Then GL.Uniform1(fl, fade)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, tex)
        Dim sl = GL.GetUniformLocation(blitShader.Id, "source")
        If sl >= 0 Then GL.Uniform1(sl, 0)

        GL.BindVertexArray(quadVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, q.Length * 4, q,
                      BufferUsageHint.StreamDraw)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>
    ''' STAND THE PICTURE IN THE WORLD, facing the camera, centred above a
    ''' point. The nuTerra tank billboards in one sentence: build the quad from
    ''' the camera's right and up, so it turns to face wherever the view is
    ''' without ever being stored rotated.
    '''
    ''' `metresTall` sizes it in WORLD units, so it shrinks with distance like
    ''' the thing it is labelling. A panel that held its pixel size would stay
    ''' legible at a kilometre and stop belonging to the tank.
    '''
    ''' DEPTH-TESTED BUT NOT DEPTH-WRITING: a panel behind a hill is hidden,
    ''' and two panels overlapping do not punch each other out.
    ''' </summary>
    Public Sub BlitWorld(at As Vector3, metresTall As Single,
                         camRight As Vector3, camUp As Vector3,
                         ByRef viewProj As Matrix4, fade As Single)
        ensure_shared()
        Dim h = metresTall
        Dim w = metresTall            ' the canvas is square
        Dim r = camRight * (w * 0.5F)
        Dim u = camUp * h

        Dim a = at - r + u            ' top-left
        Dim b = at + r + u            ' top-right
        Dim c = at + r                ' bottom-right
        Dim d = at - r                ' bottom-left

        ' V flipped, same reason as the screen blit: the target's origin is
        ' bottom-left and the canvas draws top-left.
        Dim q() As Single = {
            a.X, a.Y, a.Z, 0.0F, 1.0F,
            b.X, b.Y, b.Z, 1.0F, 1.0F,
            c.X, c.Y, c.Z, 1.0F, 0.0F,
            a.X, a.Y, a.Z, 0.0F, 1.0F,
            c.X, c.Y, c.Z, 1.0F, 0.0F,
            d.X, d.Y, d.Z, 0.0F, 0.0F}

        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Enable(EnableCap.Blend)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,
                             BlendingFactorDest.OneMinusSrcAlpha,
                             BlendingFactorSrc.One,
                             BlendingFactorDest.OneMinusSrcAlpha)
        panelShader.Use()
        panelShader.SetMat4("mvp", viewProj)
        Dim fl = GL.GetUniformLocation(panelShader.Id, "fade")
        If fl >= 0 Then GL.Uniform1(fl, fade)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, tex)
        Dim sl = GL.GetUniformLocation(panelShader.Id, "source")
        If sl >= 0 Then GL.Uniform1(sl, 0)

        GL.BindVertexArray(worldVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, worldVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, q.Length * 4, q,
                      BufferUsageHint.StreamDraw)
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6)
        GL.BindVertexArray(0)
        GL.DepthMask(True)
    End Sub

End Class

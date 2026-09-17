Imports System.Drawing
Imports OpenTK.Graphics.OpenGL4

' System.Drawing.Imaging is NOT imported. Both it and OpenGL4 define
' PixelFormat, and importing both makes every use of the name ambiguous - the
' compiler cannot tell a bitmap's layout from a texture's upload format. The
' two uses below are written out in full instead, which also makes it obvious
' at the call site which world each one belongs to.
Imports OpenTK.Mathematics

''' <summary>
''' TEXT IN GL - on the screen in pixels, or standing in the world.
'''
''' "get a gl text renderer in this. so we can render text in ortho or 3d. i
'''  want both" / "i want to draw in to our gl buffers directly. no imGUI shit"
'''  - the owner, 2026-09-17.
'''
''' ITS OWN ATLAS, RASTERISED AT STARTUP. The first draft borrowed ImGui's font
''' texture, which was fewer lines and the wrong answer: it tied every label in
''' the app to a UI library that is meant to be one panel, and a HUD that stops
''' working when the panel is hidden is not a HUD. This rasterises ASCII 32..126
''' into one texture with System.Drawing - already referenced here, and the
''' technique another lane in this repo already uses - and after that nothing in
''' the drawing path knows ImGui exists.
'''
''' MONOSPACE ON PURPOSE. Every cell the same size makes the atlas a grid, the
''' UVs arithmetic instead of a table, and the advance one number. It also means
''' a column of numbers lines up, which is most of what a HUD prints.
'''
''' ONE SHADER FOR BOTH MODES. The only difference between a screen caption and
''' a label standing over a tank is which matrix multiplies it: 2D passes an
''' ortho built from the screen and positions in pixels, 3D passes the camera's
''' viewProj and positions already turned into world-space quads. Two shaders
''' would be two places for the vertex format to drift apart.
'''
''' 3D TEXT IS BILLBOARDED. A label lying flat on the ground is unreadable from
''' anywhere except directly above, and being read from where the camera
''' actually is is the whole job.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainText

    Private shader As BrainShader = Nothing
    Private vao As Integer = 0
    Private vbo As Integer = 0
    Private tex As Integer = 0
    Private ready As Boolean = False

    ' The atlas grid.
    Private Const FIRST As Integer = 32
    Private Const LAST As Integer = 126
    Private Const COLS As Integer = 16
    Private cellW As Integer = 0
    Private cellH As Integer = 0
    Private atlasW As Integer = 0
    Private atlasH As Integer = 0

    ''' <summary>Pixel height of a line as rasterised. Everything scales off
    ''' this, so scale 1.0 is the size it was drawn at and stays crisp.</summary>
    Public ReadOnly Property NativeH As Single
        Get
            Return CSng(cellH)
        End Get
    End Property

    Private ReadOnly flat As New List(Of Single)
    Private ReadOnly world As New List(Of Single)

    Private camRight As Vector3 = Vector3.UnitX
    Private camUp As Vector3 = Vector3.UnitY

    ''' <summary>
    ''' Rasterise the atlas and upload it. Safe to call repeatedly.
    '''
    ''' CLEARTYPE IS TURNED OFF DELIBERATELY. Subpixel antialiasing puts colour
    ''' fringes in the RGB channels, and this samples ALPHA only - so a
    ''' ClearType glyph arrives with its coverage in the wrong channels and
    ''' renders as a faint mess. Grayscale antialiasing puts coverage exactly
    ''' where this needs it.
    ''' </summary>
    Private Sub ensure()
        If ready Then Return

        Using fnt As New Font("Consolas", 22.0F, FontStyle.Regular, GraphicsUnit.Pixel)
            Using probe As New Bitmap(8, 8)
                Using g = Graphics.FromImage(probe)
                    Dim sz = g.MeasureString("W", fnt, Integer.MaxValue,
                                             StringFormat.GenericTypographic)
                    cellW = CInt(Math.Ceiling(sz.Width)) + 2
                    cellH = CInt(Math.Ceiling(sz.Height)) + 2
                End Using
            End Using

            Dim count = LAST - FIRST + 1
            Dim rows = CInt(Math.Ceiling(count / CDbl(COLS)))
            atlasW = COLS * cellW
            atlasH = rows * cellH

            Using bmp As New Bitmap(atlasW, atlasH, Imaging.PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(bmp)
                    ' OPAQUE BLACK, NOT TRANSPARENT. GDI+ antialiases against
                    ' the background it is drawing ON: white text on a
                    ' transparent bitmap blends its edge pixels toward
                    ' transparent-black, so the coverage arrives thin and grey
                    ' and no colour applied later can put it back. That is why
                    ' the text looked washed out.
                    '
                    ' On solid black the antialiasing does what it is for, and
                    ' the result is converted below: LUMINANCE becomes alpha,
                    ' rgb becomes white. The glyph shape ends up in the channel
                    ' the shader actually samples.
                    g.Clear(Color.Black)
                    g.TextRenderingHint = Text.TextRenderingHint.AntiAliasGridFit
                    Using br As New SolidBrush(Color.White)
                        For i = 0 To count - 1
                            Dim ch = ChrW(FIRST + i)
                            Dim cx = (i Mod COLS) * cellW
                            Dim cy = (i \ COLS) * cellH
                            g.DrawString(ch, fnt, br, cx + 1.0F, cy + 1.0F,
                                         StringFormat.GenericTypographic)
                        Next
                    End Using
                End Using

                ' ---- LUMINANCE TO ALPHA ---------------------------------
                '
                ' One pass over a quarter-megapixel bitmap, once, at startup.
                ' Every texel becomes white with the glyph's coverage in alpha,
                ' which is exactly what text.frag samples - and it means the
                ' colour comes entirely from the vertex, so the same atlas
                ' draws orange captions and grey numbers without tinting
                ' either.
                Dim data = bmp.LockBits(New Rectangle(0, 0, atlasW, atlasH),
                                        Imaging.ImageLockMode.ReadWrite,
                                        Imaging.PixelFormat.Format32bppArgb)
                Dim bytes = Math.Abs(data.Stride) * atlasH
                Dim px(bytes - 1) As Byte
                Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, bytes)
                For i = 0 To bytes - 4 Step 4
                    ' BGRA in memory; the three colour bytes are equal here
                    ' because the text was drawn white on black, so any one of
                    ' them IS the coverage.
                    Dim cov = px(i + 2)
                    px(i) = 255 : px(i + 1) = 255 : px(i + 2) = 255
                    px(i + 3) = cov
                Next
                Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, bytes)
                tex = GL.GenTexture()
                GL.BindTexture(TextureTarget.Texture2D, tex)
                ' Bgra because that is the byte order a 32bppArgb Bitmap hands
                ' over on this platform - the same call ImGui's own upload makes.
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                              atlasW, atlasH, 0,
                              OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                              PixelType.UnsignedByte, data.Scan0)
                bmp.UnlockBits(data)
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureMinFilter,
                                CInt(TextureMinFilter.Linear))
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureMagFilter,
                                CInt(TextureMagFilter.Linear))
                ' CLAMP, not repeat: a glyph sampled at the very edge of its
                ' cell would otherwise pull in the neighbouring letter.
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureWrapS,
                                CInt(TextureWrapMode.ClampToEdge))
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureWrapT,
                                CInt(TextureWrapMode.ClampToEdge))
                GL.BindTexture(TextureTarget.Texture2D, 0)
            End Using
        End Using

        shader = New BrainShader("text")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        ' pos(3) uv(2) colour(4) = 9 floats, 36 bytes
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 36, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, False, 36, 12)
        GL.EnableVertexAttribArray(2)
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, False, 36, 20)
        GL.BindVertexArray(0)

        ready = True
        LogThis("brain: text atlas {0}x{1}, cell {2}x{3}, {4} glyph(s)",
                atlasW, atlasH, cellW, cellH, LAST - FIRST + 1)
    End Sub

    ''' <summary>Called once a frame before any Text3D, with the camera's right
    ''' and up. Without them a world label faces wherever it was authored.</summary>
    Public Sub SetCamera(right As Vector3, up As Vector3)
        camRight = right
        camUp = up
    End Sub

    Public Function Width(s As String, scale As Single) As Single
        ensure()
        If String.IsNullOrEmpty(s) Then Return 0.0F
        Return s.Length * cellW * scale
    End Function

    Public Function Height(scale As Single) As Single
        ensure()
        Return cellH * scale
    End Function

    ''' <summary>The cell's UVs. Arithmetic rather than a lookup, which is the
    ''' payoff for a monospace grid.</summary>
    Private Sub uv_of(ch As Char, ByRef u0 As Single, ByRef v0 As Single,
                      ByRef u1 As Single, ByRef v1 As Single)
        Dim code = AscW(ch)
        If code < FIRST OrElse code > LAST Then code = AscW("?"c)
        Dim i = code - FIRST
        Dim cx = (i Mod COLS) * cellW
        Dim cy = (i \ COLS) * cellH
        u0 = cx / CSng(atlasW)
        v0 = cy / CSng(atlasH)
        u1 = (cx + cellW) / CSng(atlasW)
        v1 = (cy + cellH) / CSng(atlasH)
    End Sub

    ''' <summary>Screen text. x, y are pixels from the top-left, y the TOP of
    ''' the line.</summary>
    Public Sub Text2D(s As String, x As Single, y As Single, scale As Single,
                      col As Vector4)
        If String.IsNullOrEmpty(s) Then Return
        ensure()
        Dim w = cellW * scale, h = cellH * scale
        Dim pen = x
        For Each ch In s
            If ch <> " "c Then
                Dim u0, v0, u1, v1 As Single
                uv_of(ch, u0, v0, u1, v1)
                push(flat, pen, y, 0.0F, u0, v0, col)
                push(flat, pen + w, y, 0.0F, u1, v0, col)
                push(flat, pen + w, y + h, 0.0F, u1, v1, col)
                push(flat, pen, y, 0.0F, u0, v0, col)
                push(flat, pen + w, y + h, 0.0F, u1, v1, col)
                push(flat, pen, y + h, 0.0F, u0, v1, col)
            End If
            pen += w
        Next
    End Sub

    ''' <summary>
    ''' World text, facing the camera, centred on `at`.
    '''
    ''' `metres` is the height of a line in world units, so a label keeps its
    ''' size relative to the thing it names rather than its size on screen.
    ''' That is what you want for "this is tank 3" and not what you want for a
    ''' caption - a caption belongs in Text2D.
    ''' </summary>
    Public Sub Text3D(s As String, at As Vector3, metres As Single, col As Vector4)
        If String.IsNullOrEmpty(s) Then Return
        ensure()
        Dim scale = metres / Math.Max(1.0F, CSng(cellH))
        Dim w = cellW * scale, h = cellH * scale
        Dim pen = -(s.Length * w) * 0.5F
        For Each ch In s
            If ch <> " "c Then
                Dim u0, v0, u1, v1 As Single
                uv_of(ch, u0, v0, u1, v1)
                ' The atlas grows DOWNWARD and the world grows UP, so the top
                ' of a glyph is +h. Getting this backwards is the single most
                ' likely reason world labels would ever appear upside down.
                Dim a = at + camRight * pen + camUp * h
                Dim b = at + camRight * (pen + w) + camUp * h
                Dim d = at + camRight * (pen + w)
                Dim e = at + camRight * pen
                push3(world, a, u0, v0, col)
                push3(world, b, u1, v0, col)
                push3(world, d, u1, v1, col)
                push3(world, a, u0, v0, col)
                push3(world, d, u1, v1, col)
                push3(world, e, u0, v1, col)
            End If
            pen += w
        Next
    End Sub

    Private Sub push(v As List(Of Single), x As Single, y As Single, z As Single,
                     u As Single, t As Single, c As Vector4)
        v.Add(x) : v.Add(y) : v.Add(z)
        v.Add(u) : v.Add(t)
        v.Add(c.X) : v.Add(c.Y) : v.Add(c.Z) : v.Add(c.W)
    End Sub

    Private Sub push3(v As List(Of Single), p As Vector3,
                      u As Single, t As Single, c As Vector4)
        push(v, p.X, p.Y, p.Z, u, t, c)
    End Sub

    Public Sub Render2D(screenW As Single, screenH As Single)
        If flat.Count = 0 Then Return
        ensure()
        Dim m = Matrix4.CreateOrthographicOffCenter(0.0F, screenW, screenH, 0.0F,
                                                    -1.0F, 1.0F)
        draw(flat, m, False)
        flat.Clear()
    End Sub

    ''' <summary>World text. Depth-TESTED so a label behind a hill is behind
    ''' it, but it does not WRITE depth - overlapping glyph quads would punch
    ''' holes in each other.</summary>
    Public Sub Render3D(ByRef viewProj As Matrix4)
        If world.Count = 0 Then Return
        ensure()
        draw(world, viewProj, True)
        world.Clear()
    End Sub

    Private Sub draw(v As List(Of Single), ByRef m As Matrix4, depth As Boolean)
        Dim arr = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.StreamDraw)

        shader.Use()
        shader.SetMat4("mvp", m)
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, tex)
        Dim loc = GL.GetUniformLocation(shader.Id, "atlas")
        If loc >= 0 Then GL.Uniform1(loc, 0)

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
        If depth Then
            GL.Enable(EnableCap.DepthTest)
            GL.DepthMask(False)
        Else
            GL.Disable(EnableCap.DepthTest)
        End If

        GL.DrawArrays(PrimitiveType.Triangles, 0, arr.Length \ 9)

        GL.DepthMask(True)
        GL.Enable(EnableCap.DepthTest)
        GL.BindVertexArray(0)
    End Sub

End Module

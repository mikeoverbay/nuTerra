Imports System.Drawing
Imports OpenTK.Mathematics
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' The card over each tank: its ID and two condition bars, drawn once into an
''' off-screen atlas and then hung in the world on a camera-facing quad.
'''
''' TWO STAGES, AND THAT IS THE POINT. Making the marker a TEXTURE rather than
''' geometry means everything on it is ordinary 2D drawing - rectangles, glyphs
''' from an atlas, anything else later - into a framebuffer, with the shaders
''' the app already has. Nothing about a bar chart or a number has to be
''' expressed in a billboard shader, and adding a crew icon or a tier roman
''' numeral later is a few more draws into the same cell rather than a new
''' uniform, a new branch and a new way for the quad to be wrong.
'''
''' ONE ATLAS, NOT ONE TEXTURE PER TANK. The alternative - a texture each, and
''' re-attaching the framebuffer per vehicle - costs an attachment change and a
''' completeness re-validation per card per bake, and a bind per card per
''' frame. A grid of cells in one texture costs a viewport change per card at
''' bake, which is free, and lets all thirty billboards draw from a single
''' bound texture. The cell is the only thing the billboard needs to know.
'''
''' Cards are re-drawn only when their content changes. Baking all thirty every
''' frame would also be cheap, but then "the bar is not moving" and "the bake
''' is not running" look the same, and the dirty check is four comparisons.
''' </summary>
Public Class TankCards
    Implements IDisposable

    ''' <summary>Cell size in pixels. 3.2:1 - wide enough for a two digit ID and
    ''' two bars beside it, short enough that thirty of them do not wallpaper the
    ''' screen.</summary>
    Public Const CELL_W As Integer = 256
    Public Const CELL_H As Integer = 80
    Private Const COLS As Integer = 6

    Private shader As Shader
    Private atlas As GLTexture
    Private fbo As GLFramebuffer
    Private digits As GLTexture
    Private cells As Integer          ' how many the atlas currently holds
    Private rows As Integer

    ''' <summary>What each cell was last drawn with. A card whose four values
    ''' still match is left alone. Named apart from the array it fills because
    ''' VB is case blind and Baked/baked are one identifier to it.</summary>
    Private Structure CardState
        Public id As Integer
        Public hull As Single
        Public crew As Single
        Public team As Integer
        Public valid As Boolean
    End Structure
    Private baked() As CardState

    Private ReadOnly at_colour() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0}

    ''' <summary>
    ''' Make the atlas big enough for n cards, rebuilding it if the count moved.
    ''' </summary>
    Private Function ensure(n As Integer) As Boolean
        If n <= 0 Then Return False
        If atlas IsNot Nothing AndAlso n = cells Then Return True

        atlas?.Dispose()
        fbo?.Dispose()
        atlas = Nothing
        fbo = Nothing

        cells = n
        rows = (n + COLS - 1) \ COLS

        atlas = GLTexture.Create(TextureTarget.Texture2D, "tank_cards")
        ' Linear, and clamped. A card is minified hard at range - the marker is
        ' 80 px tall on screen only when the tank is close - so nearest sampling
        ' makes the bars crawl and the digits break up as the camera moves.
        atlas.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        atlas.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        atlas.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        atlas.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        atlas.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat),
                        COLS * CELL_W, rows * CELL_H)

        fbo = GLFramebuffer.Create("tank_cards")
        fbo.Texture(FramebufferAttachment.ColorAttachment0, atlas, 0)
        If Not fbo.IsComplete Then
            LogThis("tank cards: framebuffer incomplete - markers off")
            atlas.Dispose() : fbo.Dispose()
            atlas = Nothing : fbo = Nothing
            cells = 0
            Return False
        End If
        fbo.DrawBuffers(1, at_colour)

        ReDim baked(n - 1)
        LogThis("tank cards: atlas {0}x{1}, {2} cell(s) of {3}x{4}",
                COLS * CELL_W, rows * CELL_H, n, CELL_W, CELL_H)
        Return True
    End Function

    ''' <summary>
    ''' Redraw any card whose content has changed.
    '''
    ''' Leaves the framebuffer binding and the 2D projection as it found them -
    ''' this runs in the middle of the overlay block, where the default
    ''' framebuffer is bound and PROJECTIONMATRIX is the main ortho, and the
    ''' passes after it assume both.
    ''' </summary>
    Public Sub Bake(instances As List(Of TankInstance))
        If Not ensure(instances.Count) Then Return

        Dim any = False
        For i = 0 To instances.Count - 1
            Dim inst = instances(i)
            Dim b = baked(i)
            If b.valid AndAlso b.id = inst.id AndAlso b.team = CInt(inst.team) AndAlso
               Math.Abs(b.hull - inst.hullHp) < 0.004F AndAlso
               Math.Abs(b.crew - inst.crewHp) < 0.004F Then Continue For

            If Not any Then
                GL_PUSH_GROUP("tank_cards_bake")
                fbo.Bind(FramebufferTarget.Framebuffer)
                GL.Disable(EnableCap.DepthTest)
                GL.DepthMask(False)
                GL.Disable(EnableCap.CullFace)
                GL.Enable(EnableCap.Blend)
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
                any = True
            End If

            draw_card(i, inst)

            baked(i).id = inst.id
            baked(i).hull = inst.hullHp
            baked(i).crew = inst.crewHp
            baked(i).team = CInt(inst.team)
            baked(i).valid = True
        Next

        If any Then
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
            Ortho_main()
            GL_POP_GROUP()
        End If
    End Sub

    ''' <summary>
    ''' One card, into its own cell.
    '''
    ''' The cell is selected with the VIEWPORT rather than by offsetting every
    ''' rectangle, so the layout below is written in plain card pixels - 0,0 at
    ''' the card's top left - and does not have to know where in the atlas it
    ''' landed. Adding a row to the atlas cannot move anything on a card.
    ''' </summary>
    Private Sub draw_card(slot As Integer, inst As TankInstance)
        Dim cx = (slot Mod COLS) * CELL_W
        Dim cy = (slot \ COLS) * CELL_H
        GL.Viewport(cx, cy, CELL_W, CELL_H)
        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(
            0.0F, CELL_W, -CELL_H, 0.0F, -1.0F, 1.0F)
        VIEWMATRIX = Matrix4.Identity

        ' OPAQUE, EDGE TO EDGE. The billboard's fragment stage cuts the shape
        ' and sets the transparency; see tank_billboard.frag for why the bake
        ' must not do either.
        draw_color_rectangle(New RectangleF(0, 0, CELL_W, CELL_H),
                             New Color4(0.06F, 0.07F, 0.08F, 1.0F))

        ' Whose it is, as a stripe rather than a tint on the whole card: a card
        ' tinted red and a hull bar that has gone red are the same colour
        ' saying two different things.
        Dim team_col = If(inst.team = TankTeam.Red,
                          New Color4(0.85F, 0.20F, 0.18F, 1.0F),
                          New Color4(0.25F, 0.75F, 0.30F, 1.0F))
        draw_color_rectangle(New RectangleF(0, 0, 9, CELL_H), team_col)

        draw_id(inst.id)
        draw_bar(112, 14, 132, 26, inst.hullHp, hull_colour(inst.hullHp))
        draw_bar(112, 48, 132, 18, inst.crewHp, New Color4(0.45F, 0.68F, 0.95F, 1.0F))
    End Sub

    ''' <summary>
    ''' Where each glyph's INK sits in mini_numbers.png, measured off the file:
    ''' the first and last column with any opacity, per tenth of the strip.
    '''
    ''' The strip is 154x10 and each glyph is only six or seven pixels of that
    ''' 15.4 wide cell, the rest being padding. Drawing whole cells side by
    ''' side therefore spaces a two digit number out by a glyph's width of
    ''' nothing, and squeezing the cells to close the gap squashes the digits.
    ''' Sampling the ink instead fixes both, and costs nothing: TextRender's
    ''' divisor and index are floats, so a window narrower than a tenth and
    ''' not on a tenth boundary is just a different pair of numbers.
    ''' </summary>
    Private Shared ReadOnly INK_LO() As Single = {6, 20, 35, 50, 66, 81, 96, 111, 126, 142}
    Private Shared ReadOnly INK_HI() As Single = {9, 25, 40, 56, 71, 86, 102, 117, 132, 147}
    Private Const STRIP_W As Single = 154.0F
    Private Const STRIP_H As Single = 10.0F

    ''' <summary>
    ''' The ID, right aligned, from the minimap's digit strip.
    '''
    ''' mini_numbers.png is the app's only glyph sheet, and ten digits is
    ''' exactly and only what an ID of 1..15 needs - so no font work and no
    ''' new shader is involved, just the call the minimap's column labels
    ''' already make.
    '''
    ''' THE STRIP RUNS 1234567890, not 0123456789. It was drawn for the
    ''' minimap's column headings, which start at 1, so the tenth glyph is the
    ''' zero. Indexing it by (digit - '0') is the obvious thing to write and
    ''' it renders every number one too high - 15 comes out as 26 - which
    ''' looks like a placement bug rather than a text one.
    '''
    ''' Right aligned on a fixed edge rather than centred, so a 9 and a 15 do
    ''' not shuffle the number sideways between frames as the roster changes.
    ''' </summary>
    Private Sub draw_id(id As Integer)
        If id <= 0 OrElse digits Is Nothing Then Return
        Dim s = id.ToString()
        Const GH As Single = 40.0F      ' glyph height on the card
        Const TRACK As Single = 5.0F    ' space between digits
        Const RIGHT As Single = 104.0F  ' the fixed right edge
        Dim y0 = (CELL_H - GH) / 2.0F

        ' Laid out with a PER GLYPH advance rather than a fixed one. A fixed
        ' advance has to be as wide as the widest digit, so every 1 in a two
        ' digit number leaves a glyph's width of gap after it and 11 reads as
        ' two separate numbers. Measure the run first, then start it at the
        ' right edge minus its own width.
        Dim widths(s.Length - 1) As Single
        Dim total = 0.0F
        For k = 0 To s.Length - 1
            Dim dd = AscW(s(k)) - AscW("0"c)
            Dim nn = (dd + 9) Mod 10
            widths(k) = GH * (INK_HI(nn) - INK_LO(nn) + 4.0F) / STRIP_H
            total += widths(k)
        Next
        total += TRACK * (s.Length - 1)
        Dim pen = RIGHT - total

        TextRenderShader.Use()
        GL.Uniform4(TextRenderShader("color"), Color4.White)
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(TextRenderShader("col_row"), 1)
        GL.Uniform1(TextRenderShader("mask"), 0)
        digits.BindUnit(0)
        defaultVao.Bind()

        For k = 0 To s.Length - 1
            Dim d = AscW(s(k)) - AscW("0"c)
            If d < 0 OrElse d > 9 Then Continue For
            Dim n = (d + 9) Mod 10                      ' 0 is the LAST glyph

            ' A pixel of margin either side of the ink, so the window cannot
            ' clip a stroke and cannot catch the neighbour's.
            Dim lo = INK_LO(n) - 1.5F
            Dim w = INK_HI(n) - INK_LO(n) + 4.0F
            GL.Uniform1(TextRenderShader("divisor"), STRIP_W / w)
            GL.Uniform1(TextRenderShader("index"), lo / w)

            ' Each glyph at its OWN width: a 1 is four pixels of ink and an 8
            ' is seven, and stretching both to one box makes the 1 a fat bar.
            Dim r As New RectangleF(pen, y0, widths(k), GH)
            GL.Uniform4(TextRenderShader("rect"), r.Left, -r.Top, r.Right, -r.Bottom)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            pen += widths(k) + TRACK
        Next

        TextRenderShader.StopUse()
        GL.BindTextureUnit(0, 0)
    End Sub

    ''' <summary>
    ''' A 0..100% bar: a well, the fill, and a hairline tick at each quarter.
    '''
    ''' The ticks are what make it readable as a PERCENTAGE rather than as a
    ''' coloured stripe. Without them a bar at 60% and one at 70% are the same
    ''' picture at marker size.
    ''' </summary>
    Private Sub draw_bar(x As Single, y As Single, w As Single, h As Single,
                         frac As Single, fill As Color4)
        frac = Math.Min(Math.Max(frac, 0.0F), 1.0F)

        draw_color_rectangle(New RectangleF(x - 2, y - 2, w + 4, h + 4),
                             New Color4(0.02F, 0.02F, 0.02F, 1.0F))
        draw_color_rectangle(New RectangleF(x, y, w, h),
                             New Color4(0.16F, 0.17F, 0.18F, 1.0F))
        If frac > 0.0F Then
            draw_color_rectangle(New RectangleF(x, y, w * frac, h), fill)
        End If
        For q = 1 To 3
            draw_color_rectangle(New RectangleF(x + w * q / 4.0F - 0.5F, y, 1.0F, h),
                                 New Color4(0.0F, 0.0F, 0.0F, 0.45F))
        Next
    End Sub

    ''' <summary>Green through amber to red. The turns are at 60 and 30 percent
    ''' because that is where the bar has to start being alarming, not at the
    ''' arithmetic middle.</summary>
    Private Function hull_colour(f As Single) As Color4
        If f > 0.6F Then
            Dim t = (f - 0.6F) / 0.4F
            Return New Color4(0.95F - 0.55F * t, 0.78F + 0.07F * t, 0.20F, 1.0F)
        ElseIf f > 0.3F Then
            Dim t = (f - 0.3F) / 0.3F
            Return New Color4(0.95F, 0.30F + 0.48F * t, 0.18F + 0.02F * t, 1.0F)
        Else
            Return New Color4(0.90F, 0.16F, 0.14F, 1.0F)
        End If
    End Function

    ''' <summary>
    ''' Hang every card in the world.
    '''
    ''' One texture bound once and one four-vertex draw per tank against the
    ''' empty VAO - there is no per-card buffer to build, so a tank appearing
    ''' or being removed costs nothing but a different loop count.
    ''' </summary>
    Public Sub Draw(instances As List(Of TankInstance), pos As Func(Of TankInstance, Vector3))
        If atlas Is Nothing OrElse instances.Count = 0 Then Return
        If shader Is Nothing Then shader = New Shader("tank_billboard")

        GL_PUSH_GROUP("tank_billboards")
        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        Dim aspect = CSng(CELL_H) / CSng(CELL_W)
        shader.Use()
        atlas.BindUnit(0)
        GL.Uniform1(shader("aspect"), aspect)
        GL.Uniform1(shader("target_px"), TANK_TAG_PX)
        GL.Uniform2(shader("size_clamp"), 0.9F, 14.0F)
        GL.Uniform2(shader("fade"), 320.0F, 620.0F)
        GL.Uniform1(shader("opacity"), 0.86F)
        GL.Uniform1(shader("corner"), 0.16F)
        GL.Uniform2(shader("uv_size"), 1.0F / COLS, 1.0F / rows)
        defaultVao.Bind()

        For i = 0 To instances.Count - 1
            Dim inst = instances(i)
            Dim p = pos(inst)
            GL.Uniform3(shader("anchor"), p.X, p.Y, p.Z)
            ' Clear of the tallest thing on the vehicle, not a fixed height: a
            ' card at 3 m sits inside the E100's turret and a metre over a
            ' Strv's deck.
            GL.Uniform1(shader("lift"), inst.vehicle.topY + 1.6F)
            GL.Uniform2(shader("uv_off"),
                        (i Mod COLS) / CSng(COLS), (i \ COLS) / CSng(rows))
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        Next

        shader.StopUse()
        GL.BindTextureUnit(0, 0)
        GL.BindVertexArray(0)
        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL_POP_GROUP()
    End Sub

    Public Sub New()
        ' Cached by filename inside TextureMgr, so this is the minimap's own
        ' texture rather than a second copy of it.
        digits = TextureMgr.load_png_image_from_file("mini_numbers.png", False, False)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        fbo?.Dispose()
        atlas?.Dispose()
        fbo = Nothing
        atlas = Nothing
        cells = 0
    End Sub
End Class

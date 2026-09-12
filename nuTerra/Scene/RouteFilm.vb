Imports ImGuiNET
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' A big flat top-down panel showing the route resolver working: the grid
''' underneath, the frontier, the branches it threw away. No world, no tanks.
'''
''' WHY IT IS NOT THE 3D RAYS. MapTankRays draws where each hull is going, which
''' is the fleet doing things. The owner asked for the opposite: "lets do a big
''' mini map on the screen sorta thing where you are casting the rays. no
''' tanks.. none of that." He wants to watch the ALGORITHM, and a 3D view of
''' vehicles is the thing he closed the window over twice.
'''
''' THIS SIDE OWNS THE PANEL AND THE TEXTURE, THE RESOLVER OWNS THE PIXELS. The
''' search runs where it runs - not necessarily on the thread holding the GL
''' context - and a texture created or written off that thread is the kind of
''' bug that shows up as a driver crash a hundred frames later. So the contract
''' is a plain byte array and an integer:
'''
'''     SyncLock RouteFilm.sync
'''         RouteFilm.width  = w
'''         RouteFilm.height = h
'''         RouteFilm.pixels = buf     ' BGRA8, w*h*4, ROW 0 AT THE TOP
'''         RouteFilm.frame += 1       ' LAST, after the pixels are in
'''     End SyncLock
'''
''' frame is bumped last on purpose: it is what says the buffer is complete, so
''' a half-written film is never uploaded. Nothing on the producing side touches
''' GL, and this side uploads on the render thread where that is legal.
'''
''' BGRA rather than RGBA because a .NET Bitmap locked at Format32bppArgb is
''' already BGRA in memory and GL takes PixelFormat.Bgra natively, so neither
''' end has to walk the buffer to swap two channels.
'''
''' NEAREST filtering, not linear. The film is a picture of CELLS, and the
''' question being asked of it is whether a frontier fits through a gap. Blurred
''' that is unanswerable at exactly the moment it matters.
''' </summary>
Public Module RouteFilm

    ''' <summary>Held while pixels/width/height/frame are read or written.</summary>
    Public ReadOnly sync As New Object

    Public width As Integer
    Public height As Integer

    ''' <summary>BGRA8, width*height*4, row 0 at the TOP.</summary>
    Public pixels() As Byte

    ''' <summary>Bumped by the producer AFTER the pixels are written. Still 0
    ''' means nothing has ever been supplied, and the panel shows its test card
    ''' rather than an empty box.</summary>
    Public frame As Integer

    Public show As Boolean = False

    Private tex As GLTexture
    Private tex_w, tex_h As Integer
    Private shown_frame As Integer = -1

    ''' <summary>What is on screen, for the caption - so "nothing is happening"
    ''' and "the resolver has not sent anything" are distinguishable.</summary>
    Private is_test_card As Boolean

    ''' <summary>
    ''' Called from the ImGui pass, on the render thread.
    '''
    ''' Uploads only when the frame counter moved, so a resolver that has
    ''' finished costs one texture bind a frame rather than a 4 MB upload.
    ''' </summary>
    Public Sub Draw()
        If Not show Then Return

        Dim w = 0, h = 0
        Dim buf As Byte() = Nothing
        Dim f = 0
        SyncLock sync
            w = width : h = height : buf = pixels : f = frame
        End SyncLock

        ' Nothing has ever arrived: show a card instead of an empty window, so
        ' the panel can be checked - open, sized, oriented - before the other
        ' side has sent a single frame.
        If f = 0 OrElse buf Is Nothing OrElse w <= 0 OrElse h <= 0 OrElse buf.Length < w * h * 4 Then
            w = 256 : h = 256
            buf = test_card(w, h)
            f = -2
            is_test_card = True
        Else
            is_test_card = False
        End If

        If tex Is Nothing OrElse tex_w <> w OrElse tex_h <> h Then
            tex?.Dispose()
            tex = GLTexture.Create(TextureTarget.Texture2D, "RouteFilm")
            tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
            tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
            tex.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat), w, h)
            tex_w = w : tex_h = h
            shown_frame = -1
        End If

        If shown_frame <> f Then
            tex.SubImage2D(0, 0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, buf)
            shown_frame = f
        End If

        ImGui.SetNextWindowSize(New Numerics.Vector2(900, 940), ImGuiCond.FirstUseEver)
        If ImGui.Begin("Route resolver###RouteFilm", show) Then

            If is_test_card Then
                ImGui.TextDisabled("no film yet - this is the test card")
                If ImGui.IsItemHovered() Then
                    ImGui.SetTooltip("Red is the TOP LEFT corner of the film." & vbLf &
                                     "If red is not top left, the producer is" & vbLf &
                                     "sending rows bottom-up and the uv pair" & vbLf &
                                     "below has to flip.")
                End If
            Else
                ImGui.TextDisabled(String.Format("{0} x {1}   frame {2}", w, h, f))
            End If

            ' Square, and as big as the window allows. The film is a top-down
            ' view of a square grid; letting it stretch to the panel would lie
            ' about the shape of every gap in it.
            Dim avail = ImGui.GetContentRegionAvail()
            Dim side = Math.Max(64.0F, Math.Min(avail.X, avail.Y))
            Dim size As New Numerics.Vector2(side, side * h / w)

            ' uv0 TOP-LEFT, uv1 BOTTOM-RIGHT - the opposite of the Textures
            ' viewer's pair. Those are framebuffer attachments, which GL fills
            ' bottom-up; this is a CPU buffer whose row 0 is the top, so
            ' flipping it here would stand the map on its head.
            ImGui.Image(New IntPtr(tex.texture_id), size,
                        New Numerics.Vector2(0.0F, 0.0F),
                        New Numerics.Vector2(1.0F, 1.0F))
        End If
        ImGui.End()
    End Sub

    ''' <summary>
    ''' A card with an unmistakable top-left, so the panel's orientation can be
    ''' settled from a screenshot instead of by eye and argument: RED top left,
    ''' green top right, blue bottom left, white bottom right, and a diagonal
    ''' from the red corner so a 90-degree rotation cannot masquerade as correct.
    ''' </summary>
    Private Function test_card(w As Integer, h As Integer) As Byte()
        Dim b(w * h * 4 - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim o = (y * w + x) * 4
                Dim top = (y < h \ 2), left = (x < w \ 2)
                Dim r As Byte = 30, g As Byte = 30, bl As Byte = 34
                If top AndAlso left Then
                    r = 220 : g = 40 : bl = 40
                ElseIf top Then
                    r = 40 : g = 200 : bl = 60
                ElseIf left Then
                    r = 50 : g = 90 : bl = 220
                Else
                    r = 230 : g = 230 : bl = 230
                End If
                ' The diagonal, drawn last so it reads over all four quadrants.
                If Math.Abs(x - y) <= 1 Then
                    r = 255 : g = 220 : bl = 0
                End If
                b(o) = bl : b(o + 1) = g : b(o + 2) = r : b(o + 3) = 255
            Next
        Next
        Return b
    End Function

    Public Sub Dispose()
        tex?.Dispose()
        tex = Nothing
        tex_w = 0 : tex_h = 0
        shown_frame = -1
    End Sub
End Module

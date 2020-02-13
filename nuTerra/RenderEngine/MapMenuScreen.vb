Imports System.IO
Imports Ionic.Zip
Imports OpenTK.Graphics.OpenGL
Imports OpenTK.Graphics
Imports OpenTK
Imports Tao.DevIl
Module MapMenuScreen

#Region "structurs/vars"

    Public img_grow_speed As Single = 0.02
    Public img_shrink_speed As Single = 0.005
    Public map_texture_ids(0) As Integer

    Public MapPickList() As map_item_
    Public Structure map_item_ : Implements IComparable(Of map_item_)
        Public lt As Vector2
        Public lb As Vector2
        Public rt As Vector2
        Public rb As Vector2

        Public name As String
        Public realname As String

        Public unit_size As Boolean
        Public scale As Single
        Public max_scale As Single
        Public min_scale As Single
        Public selected As Boolean
        Public location As Vector2

        Public Sub grow_shrink()
            Dim lt_ As New Vector2(-60.0F, 36.0F)
            Dim rb_ As New Vector2(120.0F, -72.0F)

            If Me.selected And Not FINISH_MAPS Then
                If Not img_grow_speed + scale >= max_scale Then
                    scale += img_grow_speed
                    unit_size = False
                End If
            Else
                If Not scale - img_shrink_speed <= min_scale Then
                    scale -= img_shrink_speed
                    unit_size = False
                Else
                    unit_size = True
                End If
            End If

            Me.lt = (scale * lt_) + Me.location
            Me.rb = rb_ * scale

        End Sub
        Public Sub draw_box(ByVal textId As Integer)
            Dim rect As New Rectangle(Me.lt.X, -lt.Y, rb.X, -rb.Y)
            draw_image_rectangle(rect, textId)
        End Sub
        Public Sub draw_pick_box(ByVal color_ As Color4)
            Dim rect As New Rectangle(Me.lt.X, -Me.lt.Y, Me.rb.X, -Me.rb.Y)
            draw_color_rectangle(rect, color_)
        End Sub

        Public Function CompareTo(ByVal other As map_item_) As Integer Implements System.IComparable(Of map_item_).CompareTo
            Try
                Return Me.realname.CompareTo(other.realname)

            Catch ex As Exception
                Return 0
            End Try
        End Function
    End Structure

#End Region

    Public Sub make_map_pick_buttons()

        DUMMY_TEXTURE_ID = make_dummy_texture()

        Dim f = System.IO.File.ReadAllLines(Application.StartupPath.ToString + "\data\map_list.txt")
        Dim cnt As Integer = 0
        For Each fi In f
            If fi.Contains("#") Then
                GoTo dontaddthis
            End If
            ReDim Preserve MapPickList(cnt + 1)
            MapPickList(cnt) = New map_item_
            MapPickList(cnt).name = fi
            MapPickList(cnt).max_scale = 1.25F
            MapPickList(cnt).min_scale = 1.0F
            MapPickList(cnt).scale = 1.0F
            Dim a = fi.Split(":")
            MapPickList(cnt).realname = a(1).Replace("Winter ", "Wtr ")
            cnt += 1
dontaddthis:
        Next
        ReDim Preserve MapPickList(cnt - 1)

        Array.Sort(MapPickList)
        Application.DoEvents()

        Using Zip As ZipFile = Ionic.Zip.ZipFile.Read(GAME_PATH & "gui.pkg")
            cnt = 0
            For Each thing In MapPickList
                Dim itm = thing.name
                If Not itm.Contains("#") Then
                    Dim ar = itm.Split(":")
                    Dim entry As ZipEntry = Zip("gui/maps/icons/map/small/" + ar(0))
                    Dim ms As New MemoryStream
                    entry.Extract(ms)
                    'True = hard wired to save in map_texture_ids(cnt)
                    get_tank_image(ms, cnt, True)
                    cnt += 1
                End If
            Next
        End Using
        Using Zip As ZipFile = Ionic.Zip.ZipFile.Read(GAME_PATH & "gui.pkg")
            Dim entry As ZipEntry = Zip("gui/maps/bg.png")
            Dim ms As New MemoryStream
            entry.Extract(ms)
            MAP_SELECT_BACKGROUND_ID = load_image_from_stream(Il.IL_PNG, ms, entry.FileName, False, True)
        End Using
    End Sub

    Public Sub gl_pick_map(ByVal x As Integer, ByVal y As Integer)

        DrawMapPickText.TextRenderer(120, 72)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        Ortho_main()
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)

        GL.Disable(EnableCap.Lighting)
        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.DepthTest)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.ReadBuffer(ReadBufferMode.Back)

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        draw_maps()
        If SHOW_LOADING_SCREEN Then 'for when the draw_maps returns after starting the map_loader
            Return
        End If
        draw_pick_map()


        Dim viewport(4) As Integer
        Dim pixel() As Byte = {0, 0, 0, 0}


        GL.GetInteger(GetPName.Viewport, viewport)
        GL.ReadPixels(x, viewport(3) - y, 1, 1, InternalFormat.Rgba, PixelType.UnsignedByte, pixel)

        Dim hit = pixel(2)
        If hit > 0 Then
            SELECTED_MAP_HIT = hit
            Dim ta = MapPickList(hit - 1).name.Split(":")
            MAP_NAME_NO_PATH = ta(0).Replace(".png", ".pkg")
            'tb1.Text = MapPickList(hit - 1).realname
            Application.DoEvents()
        Else
            SELECTED_MAP_HIT = 0
            'tb1.Text = x.ToString + "   " + y.ToString + vbCrLf + hit.ToString
            Application.DoEvents()
        End If
        Application.DoEvents()
    End Sub

    Public Sub draw_pick_map()

        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        GL.Disable(EnableCap.Texture2D)


        Dim map As Byte = 0

        While map < map_texture_ids.Length

            If SELECTED_MAP_HIT - 1 = map Then
                MapPickList(map).selected = True
            Else
                MapPickList(map).selected = False
            End If
            MapPickList(map).grow_shrink()
            Dim color_ As New Color4(CByte(map + 80), CByte(map + 1), CByte(map + 1), CByte(255))
            MapPickList(map).draw_pick_box(color_)
            map += 1
        End While
        'frmMain.glControl_main.SwapBuffers()

    End Sub

    Public Sub draw_maps()

        If Not _STARTED Then Return

        GL.ClearColor(0.0, 0.0, 0.0, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, TextureEnvMode.Replace)
        GL.AlphaFunc(AlphaFunction.Equal, 1.0)
        GL.ActiveTexture(TextureUnit.Texture0)


        GL.Enable(EnableCap.Texture2D)
        GL.Color4(0.0, 0.0, 0.0, 1.0)

        Dim w = frmMain.glControl_main.Width
        Dim h = frmMain.glControl_main.Height
        If w = 0 Then
            Return
        End If
        GL.FrontFace(FrontFaceDirection.Ccw)

        Dim rect As New RectangleF(0, 0, w, h)

        GL.Disable(EnableCap.AlphaTest)
        Dim position As New PointF(0, 0)


        draw_image_rectangle(rect, MAP_SELECT_BACKGROUND_ID)

        'DrawMapPickText.TextRenderer(120, 72)

        Dim ms_x As Single = 120
        Dim ms_y As Single = -72
        Dim space_x As Single = 15

        Dim w_cnt As Single = 7 'Floor(w / (ms_x + space_x))
        Dim space_cnt As Single = (w_cnt - 1) * space_x
        Dim border As Single = (w - ((w_cnt * ms_x) + space_cnt)) / 2
        Dim map As Integer = 0
        Dim v_cnt = (map_texture_ids.Length - 1) / w_cnt
        If (v_cnt * (ms_x + space_x)) + (border * 2) < w Then
            v_cnt -= 1
        End If
        Dim v_pos As Integer = 0
        Dim vi, hi As Single
        vi = 15
        While map < map_texture_ids.Length - 1
            If w = 0 Then
                Exit While
            End If
            For i = 0 To w_cnt - 1
                If map = map_texture_ids.Length Then
                    Exit While
                End If
                hi = border + (i * (ms_x + space_x))

                MapPickList(map).location = New Vector2(hi + 60.0F, vi + ms_y)

                If SELECTED_MAP_HIT - 1 = map Then
                    MapPickList(map).selected = True
                Else
                    MapPickList(map).selected = False
                End If
                MapPickList(map).grow_shrink()
                MapPickList(map).draw_box(map_texture_ids(map))
                'draw text overlay
                DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
                DrawMapPickText.DrawString(MapPickList(map).realname, monoSmall, Brushes.Black, position)
                GL.Enable(EnableCap.Blend)
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
                Dim tex = DrawMapPickText.Gettexture
                MapPickList(map).draw_box(tex)
                map += 1
            Next
            vi += -space_x + ms_y
        End While
        GL.BindTexture(TextureTarget.Texture2D, 0)


        'this checks to see if there are any images drawn oversize
        Application.DoEvents()
        frmMain.glControl_main.SwapBuffers()

        If FINISH_MAPS Then
            Dim no_stragglers As Boolean = True
            For i = 0 To MapPickList.Length - 2
                If Not MapPickList(i).unit_size Then
                    no_stragglers = False
                End If
            Next
            If no_stragglers Then
                FINISH_MAPS = False
                SHOW_MAPS_SCREEN = False
                BLOCK_MOUSE = False
                SHOW_LOADING_SCREEN = True
                frmMain.map_loader.Enabled = True
            End If
        End If

    End Sub
End Module

Imports System.IO
Imports NGettext
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

NotInheritable Class MapMenuScreen
    Const MAX_NUM_COLUMNS = 7
    Const IMG_SPACE = 15.0
    Const IMG_GROW_SPEED = 2.0
    Const IMG_SHRINK_SPEED = 1.0
    Const IMG_MAX_SCALE = 1.5
    Const IMG_MIN_SCALE = 1.0
    Shared MAP_NAME_COLOR = Color.Gray

    Shared space_cnt As Integer
    Shared border As Integer
    Shared num_columns As Integer
    Shared num_rows As Integer
    Shared scrollpane_y As Integer
    Shared scrollpane_height As Integer

    Shared ReadOnly Property ImgWidth
        Get
            Return 120.0 * My.Settings.UI_map_icon_scale
        End Get
    End Property

    Shared ReadOnly Property ImgHeight
        Get
            Return 72.0 * My.Settings.UI_map_icon_scale
        End Get
    End Property

    Public Shared SelectedMap As MapItem
    Shared MapPickList As New List(Of MapItem)

    Public Shared Sub Invalidate()
        Dim w = frmMain.glControl_main.Width
        scrollpane_y = 0

        num_columns = Math.Max(1, Math.Min(MAX_NUM_COLUMNS, Math.Floor(w / (ImgWidth + IMG_SPACE))))
        num_rows = Math.Ceiling(MapPickList.Count / num_columns)

        space_cnt = (num_columns - 1) * IMG_SPACE
        border = (w - ((num_columns * ImgWidth) + space_cnt)) / 2

        scrollpane_height = num_rows * (ImgHeight + IMG_SPACE) + ImgHeight - IMG_SPACE
    End Sub

    Public Shared Sub Scroll(delta As Integer)
        Dim h = frmMain.glControl_main.Height
        If scrollpane_height < h Then
            Return
        End If
        If delta < 0 Then
            scrollpane_y = Math.Max(scrollpane_y + delta, -(scrollpane_height - h))
        ElseIf delta > 0 Then
            scrollpane_y = Math.Min(scrollpane_y + delta, 0)
        End If
    End Sub

    Class MapItem : Implements IComparable(Of MapItem)
        Public map_image As GLTexture
        Public rect As Rectangle

        Public name As String
        Public realname As String
        Public discription As String

        Private scale As Single = IMG_MIN_SCALE
        Public unit_size As Boolean

        Public Sub calc_rect(location As Point)
            If Me Is SelectedMap And Not FINISH_MAPS Then
                If Not (IMG_GROW_SPEED * DELTA_TIME) + scale >= IMG_MAX_SCALE Then
                    scale += (IMG_GROW_SPEED * DELTA_TIME)
                    unit_size = False
                Else
                    scale = IMG_MAX_SCALE
                End If
            Else
                If Not scale - (IMG_SHRINK_SPEED * DELTA_TIME) <= IMG_MIN_SCALE Then
                    scale -= (IMG_SHRINK_SPEED * DELTA_TIME)
                    unit_size = False
                Else
                    scale = IMG_MIN_SCALE
                    unit_size = True
                End If
            End If

            Dim lt = New Point(-ImgWidth / 2 * scale, -ImgHeight / 2 * scale) + location
            rect = New Rectangle(Math.Max(0, lt.X), lt.Y, ImgWidth * scale, ImgHeight * scale)
        End Sub

        Public Sub draw()
            ' draw box
            draw_image_rectangle(New Rectangle(rect.X, rect.Y + 20, rect.Width, rect.Height - 10), map_image, False)

            ' draw text overlay
            DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))

            Dim d = (scale - IMG_MIN_SCALE) / (IMG_MAX_SCALE - IMG_MIN_SCALE)
            Dim colour = Color.FromArgb(0, CInt(d * 127), CInt(d * 127), CInt(d * 127))
            Dim colourBase = Color.FromArgb(MAP_NAME_COLOR.A + colour.A,
                                            MAP_NAME_COLOR.R + colour.R,
                                            MAP_NAME_COLOR.G + colour.G,
                                            MAP_NAME_COLOR.B - colour.B)
            Dim brush_ = New SolidBrush(colourBase)
            DrawMapPickText.DrawString(realname, lucid_console, brush_, New PointF(0, 0))

            draw_image_rectangle(New Rectangle(rect.X, rect.Y, ImgWidth, 20), DrawMapPickText.Gettexture, False)
        End Sub

        Public Function CompareTo(other As MapItem) As Integer Implements System.IComparable(Of MapItem).CompareTo
            Return realname.CompareTo(other.realname)
        End Function
    End Class

    Public Shared Sub make_map_pick_buttons()
        ' open arenas.mo
        Dim arenas_mo_path = Path.Combine(My.Settings.GamePath, "res/text/lc_messages/arenas.mo")
        Dim arenas_mo_catalog As Catalog
        Using moFileStream = File.OpenRead(arenas_mo_path)
            arenas_mo_catalog = New Catalog(moFileStream)
        End Using

        ' open _list_.xml
        Dim list_xml = ResMgr.openXML("scripts/arena_defs/_list_.xml")
        If list_xml Is Nothing Then
            MsgBox("Unabe to load map list", MsgBoxStyle.Exclamation, "Well Damn!")
            Return
        End If

        ' load map list
        For Each node In list_xml.SelectNodes("map")
            Dim name = node("name").InnerText

            ' skip dummy map
            If name = "1002_ai_test" Then
                Continue For
            End If

            MapPickList.Add(New MapItem With {
                .name = name,
                .realname = arenas_mo_catalog.GetString(String.Format("{0}/name", name)).Replace("Winter ", "Wtr "),
                .discription = arenas_mo_catalog.GetString(String.Format("{0}/description", name))
            })
        Next

        ' sort map list
        MapPickList.Sort()

        ' load map images
        Dim cnt = 0
        For Each thing In MapPickList
            Dim entry = ResMgr.Lookup("gui/maps/icons/map/stats/" + thing.name + ".png")
            If entry Is Nothing Then
                entry = ResMgr.Lookup("gui/maps/icons/map/small/noImage.png")
            End If
            Using ms As New MemoryStream
                entry.Extract(ms)
                thing.map_image = get_map_image(ms, cnt)
            End Using
            cnt += 1
        Next

        ' load background image
        Dim entry2 = ResMgr.Lookup("gui/maps/bg.png")
        Using ms As New MemoryStream
            entry2.Extract(ms)
            MAP_SELECT_BACKGROUND_ID = load_image_from_stream(Il.IL_PNG, ms, entry2.FileName, False, True)
        End Using

        Invalidate()
    End Sub

    Public Shared Sub gl_pick_map()
        ' reset old selected
        SelectedMap = Nothing

        ' find new selected map
        For i = 0 To MapPickList.Count - 1
            If MapPickList(i).rect.Contains(MOUSE) Then
                MAP_NAME_NO_PATH = MapPickList(i).name
                SelectedMap = MapPickList(i)
                Exit For
            End If
        Next

        DrawMapPickText.TextRenderer(ImgWidth, 20)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        Ortho_main()

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.DepthTest)

        Dim w = frmMain.glControl_main.Width
        Dim h = frmMain.glControl_main.Height
        If w = 0 Or h = 0 Then
            Return
        End If

        draw_image_rectangle(New RectangleF(0, 0, w, h), MAP_SELECT_BACKGROUND_ID, False)

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        For i = 0 To MapPickList.Count - 1
            Dim row = i \ num_columns
            Dim column = i Mod num_columns

            Dim hi = column * (ImgWidth + IMG_SPACE) + border + ImgWidth / 2
            Dim vi = scrollpane_y + row * (ImgHeight + IMG_SPACE) + ImgHeight - IMG_SPACE

            MapPickList(i).calc_rect(New Point(hi, vi))

            If MapPickList(i) Is SelectedMap Then
                Continue For
            End If

            MapPickList(i).draw()
        Next

        If SelectedMap IsNot Nothing Then
            SelectedMap.draw()
        End If

        GL.Disable(EnableCap.Blend)

        'make it visible
        frmMain.glControl_main.SwapBuffers()

        'this checks to see if there are any images drawn oversize
        If FINISH_MAPS Then
            Dim no_stragglers = True
            For Each mapItem In MapPickList
                If Not mapItem.unit_size Then
                    no_stragglers = False
                End If
            Next
            If no_stragglers Then
                FINISH_MAPS = False
                SHOW_MAPS_SCREEN = False
                BLOCK_MOUSE = False
                BG_VALUE = 0 'reset bar graph
                SHOW_LOADING_SCREEN = True
                frmMain.map_loader.Enabled = True
            End If
        End If

    End Sub
End Class

Imports System.Globalization
Imports System.IO
Imports NGettext
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module MapMenuScreen

#Region "structurs/vars"

    Public img_grow_speed As Single = 2
    Public img_shrink_speed As Single = 1

    Public SelectedMap As map_item_
    Public MapPickList As List(Of map_item_)

    Public Class map_item_ : Implements IComparable(Of map_item_)
        Public map_texture_id As GLTexture
        Public lt As Vector2
        Public lb As Vector2
        Public rt As Vector2
        Public rb As Vector2

        Public name As String
        Public realname As String
        Public discription As String

        Public unit_size As Boolean
        Public scale As Single
        Public max_scale As Single
        Public min_scale As Single
        Public location As Vector2

        Public Function Contains(pt As Point) As Boolean
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(lt.X + L, -lt.Y, rb.X, -rb.Y)
            Return rect.Contains(pt)
        End Function

        Public Sub grow_shrink()
            Dim lt_ As New Vector2(-60.0F, 36.0F)
            Dim rb_ As New Vector2(120.0F, -72.0F)

            If Me Is SelectedMap And Not FINISH_MAPS Then
                If Not (img_grow_speed * DELTA_TIME) + scale >= max_scale Then
                    scale += (img_grow_speed * DELTA_TIME)
                    unit_size = False
                Else
                    scale = max_scale
                End If
            Else
                If Not scale - (img_shrink_speed * DELTA_TIME) <= min_scale Then
                    scale -= (img_shrink_speed * DELTA_TIME)
                    unit_size = False
                Else
                    scale = min_scale
                    unit_size = True
                End If
            End If

            Me.lt = (scale * lt_) + Me.location
            Me.rb = rb_ * scale

        End Sub

        Public Sub draw_box()
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(Me.lt.X + L, -Me.lt.Y + 20, Me.rb.X, -Me.rb.Y - 10)
            draw_image_rectangle(rect, map_texture_id, False)
        End Sub

        Public Sub draw_text(ByVal textId As GLTexture)
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(Me.lt.X + L, -Me.lt.Y, 120, 20)
            draw_image_rectangle(rect, textId, False)
        End Sub

        Public Sub draw_pick_box(ByVal color_ As Color4)
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(Me.lt.X + L, -Me.lt.Y, Me.rb.X, -Me.rb.Y)
            draw_color_rectangle(rect, color_)
        End Sub

        Public Function CompareTo(ByVal other As map_item_) As Integer Implements System.IComparable(Of map_item_).CompareTo
            Try
                Return Me.realname.CompareTo(other.realname)
            Catch ex As Exception
                Return 0
            End Try
        End Function
    End Class
#End Region

    Public Sub make_map_pick_buttons()
        ' open arenas.mo
        Dim arenas_mo_path = Path.Combine(My.Settings.GamePath, "res/text/lc_messages/arenas.mo")
        Dim arenas_mo_catalog As Catalog
        Using moFileStream = File.OpenRead(arenas_mo_path)
            arenas_mo_catalog = New Catalog(moFileStream, New CultureInfo("en-US"))
        End Using

        ' open _list_.xml
        Dim list_xml = ResMgr.openXML("scripts/arena_defs/_list_.xml")
        If list_xml Is Nothing Then
            MsgBox("Unabe to load map list", MsgBoxStyle.Exclamation, "Well Damn!")
            Return
        End If

        ' load list os maps
        MapPickList = New List(Of map_item_)
        For Each node In list_xml.SelectNodes("map")
            Dim name = node("name").InnerText

            ' skip dummy map
            If name = "1002_ai_test" Then
                Continue For
            End If

            MapPickList.Add(New map_item_ With {
                .name = name,
                .max_scale = 1.5F,
                .min_scale = 1.0F,
                .scale = 1.0F,
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
                thing.map_texture_id = get_map_image(ms, cnt, 0.5)
            End Using
            cnt += 1
        Next

        ' load background image
        Dim entry2 = ResMgr.Lookup("gui/maps/bg.png")
        Using ms As New MemoryStream
            entry2.Extract(ms)
            MAP_SELECT_BACKGROUND_ID = load_image_from_stream(Il.IL_PNG, ms, entry2.FileName, False, True)
        End Using
    End Sub

    Public Sub gl_pick_map(pt As Point)
        ' reset old selected
        SelectedMap = Nothing

        ' find new selected map
        For i = 0 To MapPickList.Count - 1
            If MapPickList(i).Contains(pt) Then
                MAP_NAME_NO_PATH = MapPickList(i).name
                SelectedMap = MapPickList(i)
                Exit For
            End If
        Next

        DrawMapPickText.TextRenderer(120, 20)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        Ortho_main()

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.DepthTest)

        'in case the form is closed while pick map is the current screen
        If Not _STARTED Then Return

        Dim w = frmMain.glControl_main.Width
        Dim h = frmMain.glControl_main.Height
        If w = 0 Or h = 0 Then
            Return
        End If

        draw_image_rectangle(New RectangleF(0, 0, w, h), MAP_SELECT_BACKGROUND_ID, False)

        Dim ms_x As Single = 120
        Dim ms_y As Single = -72
        Dim space_x As Single = 15

        Dim num_columns = Math.Max(1, Math.Min(7, Math.Floor(w / (ms_x + space_x))))
        Dim space_cnt As Single = (num_columns - 1) * space_x
        Dim border As Single = (w - ((num_columns * ms_x) + space_cnt)) / 2

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        For i = 0 To MapPickList.Count - 1
            Dim row = i \ num_columns
            Dim column = i Mod num_columns

            Dim hi = border + (column * (ms_x + space_x))
            Dim vi = 15 + row * (-space_x + ms_y)

            MapPickList(i).location = New Vector2(hi + 60.0F, vi + ms_y)

            If MapPickList(i) Is SelectedMap Then
                Continue For
            End If

            MapPickList(i).grow_shrink()
            MapPickList(i).draw_box()

            'draw text overlay
            DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))

            Dim d = (MapPickList(i).scale - 1.0F) / 0.5F
            Dim gray = Color.Gray
            Dim colour = Color.FromArgb(0, CInt(d * 127), CInt(d * 127), CInt(d * 127))
            Dim colourBase = Color.FromArgb(gray.A + colour.A,
                                            gray.R + colour.R,
                                            gray.G + colour.G,
                                            gray.B - colour.B)
            Dim brush_ = New SolidBrush(colourBase)
            DrawMapPickText.DrawString(MapPickList(i).realname, lucid_console, brush_, New PointF(0, 0))

            Dim tex = DrawMapPickText.Gettexture
            MapPickList(i).draw_text(tex)
        Next

        If SelectedMap IsNot Nothing Then
            Dim i = MapPickList.IndexOf(SelectedMap)
            SelectedMap.grow_shrink()
            SelectedMap.draw_box()

            'draw text overlay
            DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))

            Dim d = (SelectedMap.scale - 1.0F) / 0.5F
            Dim gray = Color.Gray
            Dim colour = Color.FromArgb(0, CInt(d * 127), CInt(d * 127), CInt(d * 127))
            Dim colourBase = Color.FromArgb(gray.A + colour.A,
                                            gray.R + colour.R,
                                            gray.G + colour.G,
                                            gray.B - colour.B)
            Dim brush_ = New SolidBrush(colourBase)
            DrawMapPickText.DrawString(SelectedMap.realname, lucid_console, brush_, New PointF(0, 0))

            Dim tex = DrawMapPickText.Gettexture
            SelectedMap.draw_text(tex)
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
End Module

Imports System.IO
Imports Ionic.Zip
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module MapMenuScreen

#Region "structurs/vars"

    Public info_string As String
    Public os_strings() As String
    Public trans_strings() As String
    Public trans_strings_raw() As Array
    Public trans_flags() As Boolean
    Public hash_tbl() As Integer
    Public Structure indice_
        Public len As Integer
        Public offset As Integer
    End Structure
    Public os_indices() As indice_
    Public hash_indices() As indice_
    Public trans_indices() As indice_
    Dim info_string_length As Integer = 0
    Dim hash_count As Integer = 0
    Dim file_size As Integer = 0
    Dim delta As Integer = 0
    Dim os_string_sec_length As Integer = 0
    Dim magic As Integer
    Dim info_index_offset As Integer
    Dim hash_index_offset As Integer
    Dim info_index_count As Integer
    Dim info_index_string_index As Integer

    Dim enc As New System.Text.UTF8Encoding

    Public map_texture_ids() As GLTexture
    Public img_grow_speed As Single = 1.5
    Public img_shrink_speed As Single = 0.5
    Public MapPickList() As map_item_

    Public Structure map_item_ : Implements IComparable(Of map_item_)
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
        Public selected As Boolean
        Public location As Vector2

        Public Sub grow_shrink()
            Dim lt_ As New Vector2(-60.0F, 36.0F)
            Dim rb_ As New Vector2(120.0F, -72.0F)

            If Me.selected And Not FINISH_MAPS Then
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

        Public Sub draw_box(textId As GLTexture)
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(Me.lt.X + L, -Me.lt.Y + 20, Me.rb.X, -Me.rb.Y - 10)
            draw_image_rectangle(rect, textId)
        End Sub

        Public Sub draw_text(ByVal textId As GLTexture)
            Dim L As Integer
            If lt.X < 0 Then
                L = -lt.X
            Else
                L = 0
            End If
            Dim rect As New Rectangle(Me.lt.X + L, -Me.lt.Y, 120, 20)
            draw_image_rectangle(rect, textId)
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
    End Structure
#End Region

    Public Sub make_map_pick_buttons()

        '==========================================================
        DUMMY_TEXTURE_ID = make_dummy_texture()
        '==========================================================

        open_mo()

        Dim scriptPkg As ZipFile = ZipFile.Read(GAME_PATH + "\scripts.pkg")
        Dim list_entry = scriptPkg("scripts/arena_defs/_list_.xml")
        If list_entry Is Nothing Then
            MsgBox("Unabe to load map list", MsgBoxStyle.Exclamation, "Well Damn!")
            scriptPkg.Dispose()
            Return
        End If
        Dim listMS As New MemoryStream
        list_entry.Extract(listMS)
        If Not openXml_stream(listMS, "map_list") Then
            MsgBox("Failed to open _list_.xml", MsgBoxStyle.Exclamation, "Well Damn!")
            scriptPkg.Dispose()
            listMS.Dispose()
            Return
        End If
        ReDim MapPickList(100)
        Dim t = xmldataset.Tables("map")
        Dim q = From row In t.AsEnumerable
                Select
                    name = row.Field(Of String)("name")

        Dim pos As Integer = 0

        For Each m In q
            MapPickList(pos) = New map_item_
            MapPickList(pos) = New map_item_
            MapPickList(pos).name = m
            MapPickList(pos).max_scale = 1.5F
            MapPickList(pos).min_scale = 1.0F
            MapPickList(pos).scale = 1.0F
            Dim name = find_maps_gui_name(m) ' discriiption is returned in m
            If name IsNot Nothing Then

                MapPickList(pos).realname = name
                MapPickList(pos).discription = m
                MapPickList(pos).realname = MapPickList(pos).realname.Replace("Winter ", "Wtr ")

                pos += 1
            Else
                'Stop
            End If
        Next

        ReDim Preserve MapPickList(pos - 2) '??


        Array.Sort(MapPickList)
        Application.DoEvents()

        Dim cnt = 0

        For Each thing In MapPickList
            Dim entry As ZipEntry = Packages.GUI_PACKAGE("gui/maps/icons/map/stats/" + thing.name + ".png")
            If entry Is Nothing And Packages.GUI_PACKAGE_PART2 IsNot Nothing Then
                entry = Packages.GUI_PACKAGE_PART2("gui/maps/icons/map/stats/" + thing.name + ".png")
            End If
            If entry Is Nothing Then
                entry = Packages.GUI_PACKAGE("gui/maps/icons/map/small/" + "noimage.png")
            End If
            Dim ms2 = New MemoryStream
            entry.Extract(ms2)
            'True = hard wired to save in map_texture_ids(cnt)
            get_tank_image(ms2, cnt, True, 0.5)
            MapPickList(cnt).name += ".pkg"
            cnt += 1
        Next
        Dim entry2 As ZipEntry = Packages.GUI_PACKAGE("gui/maps/bg.png")
        Dim ms As New MemoryStream
        entry2.Extract(ms)
        MAP_SELECT_BACKGROUND_ID = load_image_from_stream(Il.IL_PNG, ms, entry2.FileName, False, True)
    End Sub
    Private Sub open_mo()

        Dim index_os, index_tl As ULong

        Dim t_path = My.Settings.GamePath + "/res/text/lc_messages/arenas.mo"

        Dim data() = System.IO.File.ReadAllBytes(t_path)
        Dim file_size = data.Length


        Dim ms As New MemoryStream(data)
        Dim br As New BinaryReader(ms)
        ms.Position = 0
        Dim magic = br.ReadInt32
        If magic <> &H950412DE Then
            MsgBox("This file is NOT a WoT MO file. Magic number is wrong!", MsgBoxStyle.Exclamation, "Opps")
            Return
        End If

        Dim version = br.ReadInt32
        Dim count = br.ReadInt32
        Dim os_index_start As ULong = br.ReadInt32 'always = 28?
        Dim index_info = br.ReadInt32()
        Dim info_index_offset = index_info

        Dim hash_count = br.ReadUInt32

        Dim index_hash = br.ReadUInt32
        Dim hash_index_offset = index_hash

        Dim unknown3 = br.ReadUInt32
        Dim n_data() As Byte
        'make room for the data
        ReDim os_strings(count)
        ReDim trans_strings(count)
        ReDim trans_strings_raw(count)
        ReDim trans_flags(count)
        ReDim trans_indices(count)
        ReDim os_indices(count)
        ReDim hash_tbl(hash_count)
        ReDim hash_indices(hash_count)
        'get hash table
        ms.Position = index_hash
        For i = 0 To hash_count - 1
            hash_tbl(i) = br.ReadInt32
        Next
        Dim cp = os_index_start 'save .. we need it for loop
        For i = 0 To count - 1
            ms.Position = cp 'restore position
            Dim s_length = br.ReadInt32
            os_string_sec_length += s_length

            index_os = br.ReadInt32
            os_indices(i) = New indice_
            os_indices(i).len = s_length
            os_indices(i).offset = index_os

            ReDim n_data(s_length)
            cp = ms.Position
            ms.Position = index_os
            ReDim n_data(s_length)
            n_data = br.ReadBytes(s_length)
            os_strings(i) = enc.GetString(n_data)
        Next
        index_tl = br.ReadInt32
        ms.Position = index_info ' point at project info index
        info_string_length = br.ReadInt32 ' get project string length
        info_index_count = info_string_length

        index_info = br.ReadInt32 ' get start of project string
        info_index_string_index = index_info

        Dim trans_index = ms.Position ' save start of trans string indexes which is next
        ms.Position = index_info ' move to where project string is stored

        ReDim n_data(info_string_length) 'make room
        n_data = br.ReadBytes(info_string_length) ' read project info
        info_string = enc.GetString(n_data) ' convert to string
        Dim trans_strings_start = ms.Position  ' save start of trans strings - not used.. debug only
        cp = trans_index 'save for loop
        'get trans strings
        For i = 0 To count - 1
            ms.Position = cp 'restore position
            Dim s_length = br.ReadInt32
            index_os = br.ReadInt32

            trans_indices(i) = New indice_
            trans_indices(i).len = s_length
            trans_indices(i).offset = index_os
            ReDim n_data(s_length)
            cp = ms.Position ' save position
            ms.Position = index_os 'set postion
            ReDim n_data(s_length)
            n_data = br.ReadBytes(s_length)
            trans_strings(i) = enc.GetString(n_data)
            trans_strings_raw(i) = n_data
            trans_flags(i) = False
        Next

        ms.Close()
        ms.Dispose()

        'strange but some lines have a lf ctr (10) in them.. This screws up the selection in the string table form.
        For i = 0 To count - 1
            trans_strings(i) = trans_strings(i).Replace(vbLf, " ")
        Next

    End Sub
    Private Function find_maps_gui_name(ByRef mapname As String) As String
        For i = 1 To os_strings.Length - 2
            If os_strings(i).Contains(mapname) Then
                Dim realname = trans_strings(i)
                Dim discription = trans_strings(i - 1)
                mapname = discription
                Return realname
            End If
        Next
        Return ""
    End Function

    Public Sub gl_pick_map(ByVal x As Integer, ByVal y As Integer)

        DrawMapPickText.TextRenderer(120, 20)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer

        Ortho_main()
        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)

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
            description_string = MapPickList(hit - 1).discription
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

    End Sub

    Public Sub draw_maps()

        'in case the form is closed while pick map is the current screen
        If Not _STARTED Then Return

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        Dim w = frmMain.glControl_main.Width
        Dim h = frmMain.glControl_main.Height
        If w = 0 Then
            Return
        End If
        GL.FrontFace(FrontFaceDirection.Ccw)

        Dim rect As New RectangleF(0, 0, w, h)

        Dim position As New PointF(0, 0)


        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        draw_image_rectangle(rect, MAP_SELECT_BACKGROUND_ID)

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

                Dim d = (MapPickList(map).scale - 1.0F) / 0.5F
                Dim gray = Color.Gray
                Dim colour = Color.FromArgb(0, CInt(d * 127), CInt(d * 127), CInt(d * 127))
                Dim colourBase = Color.FromArgb(gray.A + colour.A,
                                                gray.R + colour.R,
                                                gray.G + colour.G,
                                                gray.B - colour.B)
                Dim brush_ = New SolidBrush(colourBase)
                DrawMapPickText.DrawString(MapPickList(map).realname, lucid_console, brush_, position)

                Dim tex = DrawMapPickText.Gettexture
                MapPickList(map).draw_text(tex)
                map += 1
            Next
            vi += -space_x + ms_y
        End While
        map = 0
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

                If MapPickList(map).scale > 1.05 Then ' need to draw the selected map box on top of all others
                    MapPickList(map).draw_box(map_texture_ids(map))
                    'draw text overlay
                    DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))

                    Dim d = (MapPickList(map).scale - 1.0F) / 0.5F
                    Dim gray = Color.Gray
                    Dim colour = Color.FromArgb(0, CInt(d * 127), CInt(d * 127), CInt(d * 127))
                    Dim colourBase = Color.FromArgb(gray.A + colour.A,
                                                    gray.R + colour.R,
                                                    gray.G + colour.G,
                                                    gray.B - colour.B)
                    Dim brush_ = New SolidBrush(colourBase)
                    DrawMapPickText.DrawString(MapPickList(map).realname, lucid_console, brush_, position)

                    Dim tex = DrawMapPickText.Gettexture
                    MapPickList(map).draw_text(tex)
                End If
                map += 1
            Next
            vi += -space_x + ms_y
        End While

        GL.Disable(EnableCap.Blend)

        'make it visible
        frmMain.glControl_main.SwapBuffers()

        'this checks to see if there are any images drawn oversize
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
                BG_VALUE = 0 'reset bar graph
                SHOW_LOADING_SCREEN = True
                frmMain.map_loader.Enabled = True
            End If
        End If

    End Sub
End Module

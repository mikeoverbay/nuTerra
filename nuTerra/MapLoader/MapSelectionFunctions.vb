'Imports System.IO.Compression
Imports System.IO
Imports System.Math
Imports Tao.DevIl
Imports OpenTK.Graphics.OpenGL
Imports Ionic.Zlib
Imports Ionic.Zip

Module MapSelectionFunctions

#Region "structurs/vars"


    Public map_texture_ids(0) As Integer

    Public loadmaplist() As map_item_
    Public Structure map_item_ : Implements IComparable(Of map_item_)
        Public name As String
        Public realname As String
        Public size As Single
        Public grow_shrink As Boolean
        Public direction As Single
        Public delay_time As Integer
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

        Dim f = System.IO.File.ReadAllLines(Application.StartupPath.ToString + "\map_list.txt")
        Dim cnt As Integer = 0
        For Each fi In f
            If fi.Contains("#") Then
                GoTo dontaddthis
            End If
            ReDim Preserve loadmaplist(cnt + 1)
            loadmaplist(cnt) = New map_item_
            loadmaplist(cnt).name = fi
            Dim a = fi.Split(":")
            loadmaplist(cnt).realname = a(1).Replace("Winter ", "Wtr ")
            cnt += 1
dontaddthis:
        Next
        ReDim Preserve loadmaplist(cnt - 1)

        Array.Sort(loadmaplist)
        Application.DoEvents()

        Using Zip As ZipFile = Ionic.Zip.ZipFile.Read(GAME_PATH & "gui.pkg")
            cnt = 0
            For Each thing In loadmaplist
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
        'GC.Collect()
    End Sub

    Public Sub gl_pick_map(ByVal x As Integer, ByVal y As Integer)


        'frmMain.glControl_main.MakeCurrent()
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
        draw_pick_map()


        Dim viewport(4) As Integer
        Dim pixel() As Byte = {0, 0, 0, 0}


        GL.GetInteger(GetPName.Viewport, viewport)
        GL.ReadPixels(x, viewport(3) - y, 1, 1, InternalFormat.Rgba, PixelType.UnsignedByte, pixel)

        Dim hit = pixel(2)
        If hit > 0 Then
            SELECTED_MAP_HIT = hit
            'tb1.Text = loadmaplist(hit - 1).realname
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

        Dim w = frmMain.glControl_main.ClientSize.Width
        Dim h = frmMain.glControl_main.ClientSize.Height
        If w = 0 Then
            Return
        End If
        Dim ms_x As Single = 120
        Dim ms_y As Single = -72
        Dim space_x As Single = 15

        Dim w_cnt As Single = 7 'Floor(w / (ms_x + space_x))
        Dim space_cnt As Single = (w_cnt - 1) * space_x
        Dim border As Single = (w - ((w_cnt * ms_x) + space_cnt)) / 2
        Dim map As Byte = 0
        Dim v_cnt = (map_texture_ids.Length - 1) / w_cnt
        If (v_cnt * (ms_x + space_x)) + (border * 2) < w Then
            v_cnt -= 1
        End If
        Dim v_pos As Integer = 0
        Dim vi, hi As Single

        vi = -30

        GL.Begin(PrimitiveType.Quads)
        While True
            If frmMain.glControl_main.Width = 0 Then
                Exit While
            End If
            For i = 0 To w_cnt - 1
                map += 1
                GL.Color4(CByte(map), CByte(map), CByte(map), CByte(255))
                If map = map_texture_ids.Length Then
                    Exit While
                End If
                hi = border + (i * (ms_x + space_x))

                GL.Vertex3(hi, vi + ms_y, 0.0)

                GL.Vertex3(hi + ms_x, vi + ms_y, 0.0)

                GL.Vertex3(hi + ms_x, vi, 0.0)

                GL.Vertex3(hi, vi, 0.0)

            Next
            vi += -space_x + ms_y
        End While
        GL.End()
        'frmMain.glControl_main.SwapBuffers()

    End Sub

    Public Sub draw_maps()

        If Not _STARTED Then Return
        ' If Not SHOW_MAPS Then Return
        'gl_busy = True

        GL.ClearColor(0.0, 0.0, 0.0, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, TextureEnvMode.Replace)
        GL.AlphaFunc(AlphaFunction.Equal, 1.0)
        GL.ActiveTexture(TextureUnit.Texture0)
        Dim tex = DrawMapPickText.Gettexture

        GL.Enable(EnableCap.Texture2D)
        GL.Color4(0.0, 0.0, 0.0, 1.0)

        Dim w = frmMain.glControl_main.Width
        Dim h = frmMain.glControl_main.Height
        If w = 0 Then
            Return
        End If
        GL.FrontFace(FrontFaceDirection.Ccw)
        GL.BindTexture(TextureTarget.Texture2D, MAP_SELECT_BACKGROUND_ID)


        GL.Begin(PrimitiveType.Quads)

        GL.TexCoord2(0.0F, -1.0F)
        GL.Vertex2(0.0F, 0.0F)

        GL.TexCoord2(0, 0.0F)
        GL.Vertex2(0.0F, -h)

        GL.TexCoord2(1, 0.0F)
        GL.Vertex2(w, -h)

        GL.TexCoord2(1, -1.0F)
        GL.Vertex2(w, 0.0F)
        GL.End()
        GL.BindTexture(TextureTarget.Texture2D, 0)


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
        Dim vi, hi, sz As Single
        For i = 0 To map_texture_ids.Length - 2
            If loadmaplist(i).grow_shrink Then
                loadmaplist(i).delay_time += 1
                If loadmaplist(i).delay_time = 1 Then
                    loadmaplist(i).delay_time = 0
                    If loadmaplist(i).size = 0 Or loadmaplist(i).size = 20 Then
                        loadmaplist(i).grow_shrink = False
                    Else
                        loadmaplist(i).size += loadmaplist(i).direction
                    End If
                End If
            End If
        Next
        vi = -30
        While map < map_texture_ids.Length - 2
            If w = 0 Then
                Exit While
            End If
            For i = 0 To w_cnt - 1
                If map + 1 = map_texture_ids.Length Then
                    Exit While
                End If
                hi = border + (i * (ms_x + space_x))
                GL.BindTexture(TextureTarget.Texture2D, map_texture_ids(map))
                GL.Color3(1.0, 1.0, 1.0)
                If SELECTED_MAP_HIT > 0 And map = SELECTED_MAP_HIT - 1 Then
                    loadmaplist(map).grow_shrink = False
                    GoTo dont_draw
                Else
                    loadmaplist(map).direction = -0.25
                    If loadmaplist(map).size > 0.5 Then
                        loadmaplist(map).grow_shrink = True
                        loadmaplist(map).direction = -0.25
                        If loadmaplist(map).size = 20 Then
                            loadmaplist(map).size = 19.75
                        End If
                    End If

                End If
                sz = loadmaplist(map).size

                GL.Begin(PrimitiveType.Quads)
                GL.TexCoord2(0, 1)
                GL.Vertex2(-sz + hi, -sz + vi + ms_y)

                GL.TexCoord2(1, 1)
                GL.Vertex2(sz + hi + ms_x, -sz + vi + ms_y)

                GL.TexCoord2(1, 0)
                GL.Vertex2(sz + hi + ms_x, sz + vi)

                GL.TexCoord2(0, 0)
                GL.Vertex2(-sz + hi, sz + vi)
                GL.End()

                'draw text overlay
                GL.Enable(EnableCap.AlphaTest)
                Dim position As New PointF(0, 0)
                DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
                DrawMapPickText.DrawString(loadmaplist(map).realname, monoSmall, Brushes.Black, position)
                tex = DrawMapPickText.Gettexture
                GL.BindTexture(TextureTarget.Texture2D, tex)

                GL.Begin(PrimitiveType.Quads)
                GL.TexCoord2(0, 1)
                GL.Vertex2(-sz + hi, -sz + vi + ms_y)

                GL.TexCoord2(1, 1)
                GL.Vertex2(sz + hi + ms_x, -sz + vi + ms_y)

                GL.TexCoord2(1, 0)
                GL.Vertex2(sz + hi + ms_x, sz + vi)

                GL.TexCoord2(0, 0)
                GL.Vertex2(-sz + hi, sz + vi)

                GL.End()
                GL.Disable(EnableCap.AlphaTest)

                Dim cs As Single = loadmaplist(map).size / 40.0!
                'glutPrintBox(-sz + hi, -sz + vi + ms_y, loadmaplist(map).realname, 0.5 + cs, 0.5 + cs, 0.5, 1.0)

dont_draw:

                map += 1
            Next
            vi += -space_x + ms_y
        End While
        GL.BindTexture(TextureTarget.Texture2D, 0)
        vi = -30
        map = 0
        While map < map_texture_ids.Length - 2
            If w = 0 Then
                Exit While
            End If
            For i = 0 To w_cnt - 1
                If map + 1 = map_texture_ids.Length Then
                    Exit While
                End If
                hi = border + (i * (ms_x + space_x))
                GL.BindTexture(TextureTarget.Texture2D, map_texture_ids(map))
                GL.Color3(1.0, 1.0, 1.0)
                If SELECTED_MAP_HIT > 0 And map = SELECTED_MAP_HIT - 1 Then
                    Dim selm = SELECTED_MAP_HIT - 1
                    If loadmaplist(selm).size < 20 And Not loadmaplist(selm).grow_shrink Then
                        loadmaplist(selm).grow_shrink = True
                        loadmaplist(selm).direction = 1.0
                        If loadmaplist(selm).size < 1.0 Then
                            loadmaplist(selm).size = 1.0
                        End If
                    End If
                Else
                    GoTo skip
                End If
                sz = loadmaplist(map).size

                GL.Begin(PrimitiveType.Quads)
                GL.TexCoord2(0, 1)
                GL.Vertex2(-sz + hi, -sz + vi + ms_y)

                GL.TexCoord2(1, 1)
                GL.Vertex2(sz + hi + ms_x, -sz + vi + ms_y)

                GL.TexCoord2(1, 0)
                GL.Vertex2(sz + hi + ms_x, sz + vi)

                GL.TexCoord2(0, 0)
                GL.Vertex2(-sz + hi, sz + vi)

                GL.End()

                'draw text overlay
                GL.Enable(EnableCap.AlphaTest)
                Dim position As New PointF(0, 0)
                DrawMapPickText.clear(Color.FromArgb(0, 0, 0, 255))
                DrawMapPickText.DrawString(loadmaplist(map).realname, monoSmall, Brushes.Black, position)
                tex = DrawMapPickText.Gettexture
                GL.BindTexture(TextureTarget.Texture2D, tex)

                GL.Begin(PrimitiveType.Quads)
                GL.TexCoord2(0, 1)
                GL.Vertex2(-sz + hi, -sz + vi + ms_y)

                GL.TexCoord2(1, 1)
                GL.Vertex2(sz + hi + ms_x, -sz + vi + ms_y)

                GL.TexCoord2(1, 0)
                GL.Vertex2(sz + hi + ms_x, sz + vi)

                GL.TexCoord2(0, 0)
                GL.Vertex2(-sz + hi, sz + vi)

                GL.End()
                GL.Disable(EnableCap.AlphaTest)
                Dim cs As Single = loadmaplist(map).size / 40.0!
                'glutPrintBox(-sz + hi, -sz + vi + ms_y, loadmaplist(map).realname, 0.5 + cs, 0.5 + cs, 0.5, 1.0)

skip:
                map += 1
            Next
            vi += -space_x + ms_y
        End While
        GL.BindTexture(TextureTarget.Texture2D, 0)

        GL.Disable(EnableCap.Texture2D)
        'If selected_map_hit > 0 Then
        '    glutPrintBox(mouse.X, -mouse.Y, loadmaplist(selected_map_hit - 1).realname, 1.0, 1.0, 1.0, 1.0)

        'End If

        frmMain.glControl_main.SwapBuffers()

        'this checks to see if there are any images drawn oversize
        Application.DoEvents()
        If FINISH_MAPS Then
            Dim no_stragglers As Boolean = True
            For i = 0 To loadmaplist.Length - 2
                If loadmaplist(i).size > 0.0 Then
                    no_stragglers = False
                End If
            Next
            If no_stragglers Then
                FINISH_MAPS = False
                SHOW_MAPS = False
                BLOCK_MOUSE = False
                'open_pkg(load_map_name)
            End If
        End If

    End Sub
End Module

Imports System.IO
Imports ImGuiNET
Imports NGettext

NotInheritable Class MapMenuScreen
    Shared ReadOnly MapPickList As New List(Of MapItem)

    Public Shared MAP_TO_LOAD As String
    Public Shared MAP_DESCRIPTION As String

    Class MapItem
        Implements IComparable(Of MapItem)

        Public realname As String
        Public name As String
        Public map_image As GLTexture
        Public description As String

        Public Function CompareTo(other As MapItem) As Integer Implements IComparable(Of MapItem).CompareTo
            Return Me.realname.CompareTo(other.realname)
        End Function
    End Class

    Public Shared Sub Init()
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

            ' skip dummy map
            If name = "1003_cgf_test" Then
                Continue For
            End If

            ' skip dummy map
            If name = "1004_test_physics" Then
                Continue For
            End If

            MapPickList.Add(New MapItem With {
                .name = name,
                .realname = arenas_mo_catalog.GetString(String.Format("{0}/name", name)).Replace("Winter ", "Wtr "),
                .description = arenas_mo_catalog.GetString(String.Format("{0}/description", name)).Replace(" ", " ").Replace("—", "-")
            })
        Next
        MapPickList.Add(New MapItem With {
            .name = "hangar_v3",
            .realname = "hangar_v3",
            .description = "hangar_v3"
        })

        MapPickList.Add(New MapItem With {
            .name = "h31_battle_royale_2020",
            .realname = "h31_battle_royale_2020",
            .description = "h31_battle_royale_2020"
        })

        MapPickList.Add(New MapItem With {
            .name = "h30_newyear_2022",
            .realname = "h30_newyear_2022",
            .description = "h30_newyear_2022"
        }
        )

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
                thing.map_image = TextureMgr.get_map_image(ms, cnt)
            End Using
            cnt += 1
        Next


        ' load background image
        Dim entry2 = ResMgr.Lookup("gui/maps/bg.png")
        Using ms As New MemoryStream
            entry2.Extract(ms)
            MAP_SELECT_BACKGROUND_ID = TextureMgr.load_png_image_from_stream(ms, entry2.FileName, False, True)
        End Using
    End Sub

    Public Shared Sub SubmitUI(viewport As ImGuiViewportPtr)
        Dim w = viewport.Size.X
        Dim h = viewport.Size.Y

        ImGui.SetNextWindowPos(New Numerics.Vector2(0, 40))
        ImGui.SetNextWindowSize(New Numerics.Vector2(w, h - 40))
        If ImGui.Begin("##MapGrid", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings Or ImGuiWindowFlags.NoBringToFrontOnFocus) Then
            Dim column = Math.Clamp(CInt(w / 140), 1, 8)
            If ImGui.BeginTable("##MapGridTable", column, ImGuiTableFlags.NoSavedSettings) Then
                For Each item In MapPickList
                    ImGui.TableNextColumn()
                    ImGui.Text(item.realname)
                    If ImGui.ImageButton(New IntPtr(item.map_image.texture_id), New Numerics.Vector2(120, 72)) Then
                        MAP_TO_LOAD = item.name
                        MAP_DESCRIPTION = item.description
                    End If
                    If ImGui.IsItemHovered() Then
                        ImGui.SetTooltip(item.name)
                    End If
                Next
                ImGui.EndTable()
            End If
            ImGui.End()
        End If
    End Sub
End Class

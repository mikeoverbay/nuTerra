Imports System.IO
Imports System.Linq
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
        ' Short names and descriptions live in arenas.mo, a gettext catalog keyed
        ' "<space>/name" and "<space>/description".
        Dim arenas_mo_path = Path.Combine(My.Settings.GamePath, "res/text/lc_messages/arenas.mo")
        Dim arenas_mo_catalog As Catalog
        Using moFileStream = File.OpenRead(arenas_mo_path)
            arenas_mo_catalog = New Catalog(moFileStream)
        End Using

        ' The list is what is installed, not what the game defines. Driving it
        ' from scripts/arena_defs/_list_.xml offered maps whose data was not
        ' shipped and hid the event and hangar spaces, which are never listed.
        Dim spaces = ResMgr.SpaceNames()
        If spaces.Count = 0 Then
            MsgBox("No maps found in the game packages", MsgBoxStyle.Exclamation, "Well Damn!")
            Return
        End If

        For Each name In spaces
            MapPickList.Add(New MapItem With {
                .name = name,
                .realname = Translate(arenas_mo_catalog, name, "name", PrettyName(name)).Replace("Winter ", "Wtr "),
                .description = Translate(arenas_mo_catalog, name, "description", "").Replace(" ", " ").Replace("—", "-")
            })
        Next

        Disambiguate(MapPickList)
        MapPickList.Sort()
        LogThis("Maps: {0} spaces installed", MapPickList.Count)
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

    '''<summary>
    ''' Variants of a map share one name in arenas.mo - 120_graf_zeppelin and
    ''' 120_graf_zeppelin_scc are both "Nordskar" - which leaves two buttons
    ''' labelled the same. Whatever the folder names do not have in common is
    ''' appended so they can be told apart.
    '''</summary>
    Private Shared Sub Disambiguate(items As List(Of MapItem))
        For Each g In items.GroupBy(Function(i) i.realname).Where(Function(x) x.Count() > 1)
            Dim group = g.ToList()
            Dim shared_len = group(0).name.Length
            For Each item In group
                shared_len = Math.Min(shared_len, CommonPrefix(group(0).name, item.name))
            Next
            For Each item In group
                Dim tail = item.name.Substring(shared_len).Trim("_"c)
                If tail.Length > 0 Then
                    item.realname = String.Format("{0} ({1})", item.realname, tail)
                End If
            Next
        Next
    End Sub

    Private Shared Function CommonPrefix(a As String, b As String) As Integer
        Dim n = 0
        While n < a.Length AndAlso n < b.Length AndAlso a(n) = b(n)
            n += 1
        End While
        Return n
    End Function

    '''<summary>
    ''' Looks up one arenas.mo field. A missing key comes back as the key itself,
    ''' which is what the fallback is for - the event and hangar spaces have no
    ''' entry at all.
    '''</summary>
    Private Shared Function Translate(catalog As Catalog, space As String,
                                      field As String, fallback As String) As String
        Dim key = String.Format("{0}/{1}", space, field)
        Dim text = catalog.GetString(key)
        If String.IsNullOrEmpty(text) OrElse text = key Then
            Return fallback
        End If
        Return text
    End Function

    '''<summary>
    ''' Makes a readable label out of a folder name for the spaces arenas.mo does
    ''' not know: "h33_battle_royale_2021" becomes "Battle Royale 2021".
    '''</summary>
    Private Shared Function PrettyName(space As String) As String
        Dim words = space.Split("_"c).ToList()

        ' drop the leading map number, but not if it is all there is
        If words.Count > 1 AndAlso words(0).All(AddressOf Char.IsDigit) Then
            words.RemoveAt(0)
        ElseIf words.Count > 1 AndAlso words(0).Length > 1 AndAlso
               Char.IsLetter(words(0)(0)) AndAlso words(0).Skip(1).All(AddressOf Char.IsDigit) Then
            words.RemoveAt(0)
        End If

        For i = 0 To words.Count - 1
            If words(i).Length > 0 Then
                words(i) = Char.ToUpper(words(i)(0)) & words(i).Substring(1)
            End If
        Next
        Return String.Join(" ", words)
    End Function

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

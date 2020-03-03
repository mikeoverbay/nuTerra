Imports System.IO
Imports Ionic.Zip
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

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

        Dim f = System.IO.File.ReadAllLines(Application.StartupPath.ToString + "\data\map_list.txt")
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
    End Sub

End Module

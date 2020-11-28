Imports System.Globalization
Imports System.IO
Imports Ionic.Zip
Imports NGettext

Class MapGrid
    Public Class MapInfo
        Property Name As String
        Property MapName As String
        Property MapDescription As String
        Property Image As ImageSource
    End Class

    Private Shared MapList As List(Of MapInfo)
    Private Shared BackgroundImage As ImageSource

    Public Sub New()
        If BackgroundImage Is Nothing Then
            BackgroundImage = ResMgr.LoadPNG("gui/maps/bg.png")
        End If

        If MapList Is Nothing Then
            MapList = LoadMapList()
            If MapList Is Nothing Then
                Return
            End If

            ' sort map list
            MapList.Sort(Function(x, y) x.MapName.CompareTo(y.MapName))
        End If

        InitializeComponent()
        bgImage.Source = BackgroundImage
        mapGridControl.ItemsSource = MapList
    End Sub

    Public Sub MapSelect(o As Object, e As EventArgs)
        Dim map_info = o.DataContext
        DirectCast(Parent, ContentControl).Content = New MapViewport(map_info)
    End Sub

    Private Function CreateResizedImage(source As ImageSource, width As Integer, height As Integer) As ImageSource
        Dim rect As New Rect(0, 0, width, height)

        Dim drawingVisual As New DrawingVisual
        Using drawingContext = DrawingVisual.RenderOpen()
            drawingContext.DrawImage(source, rect)
        End Using

        Dim resizedImage As New RenderTargetBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Default)
        resizedImage.Render(drawingVisual)

        Return resizedImage
    End Function

    Private Function LoadMapList() As List(Of MapInfo)
        Dim arenas_mo_path = Path.Combine(ResMgr.res_path, "text/lc_messages/arenas.mo")
        If Not File.Exists(arenas_mo_path) Then
            MessageBox.Show("Unabe to load arenas.mo!", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return Nothing
        End If

        Dim catalog As Catalog
        Using moFileStream = File.OpenRead(arenas_mo_path)
            catalog = New Catalog(moFileStream, New CultureInfo("en-US"))
        End Using

        Dim script_pkg As New ZipFile(Path.Combine(ResMgr.pkgs_path, "scripts.pkg"))
        Dim list_entry = script_pkg("scripts/arena_defs/_list_.xml")
        If list_entry Is Nothing Then
            MessageBox.Show("Unabe to load _list_.xml!", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return Nothing
        End If

        Dim no_img = ResMgr.LoadPNG("gui/maps/icons/map/small/noimage.png")

        Dim map_list As New List(Of MapInfo)
        Using stream As New MemoryStream
            list_entry.Extract(stream)
            Dim list_xml = ResMgr.openXML(stream)
            Dim nodes = list_xml.SelectNodes("//map")
            For Each node In nodes
                Dim name = node("name").InnerText
                Dim name_l10n = catalog.GetString(String.Format("{0}/name", name))
                Dim description_l10n = catalog.GetString(String.Format("{0}/description", name))

                Dim img = ResMgr.LoadPNG(String.Format("gui/maps/icons/map/stats/{0}.png", name))
                If img Is Nothing Then
                    img = no_img
                Else
                    ' Resize to 120x62
                    img = CreateResizedImage(img, 120, 62)
                End If

                map_list.Add(New MapInfo With {
                    .Name = name,
                    .MapName = name_l10n,
                    .MapDescription = description_l10n,
                    .Image = img
                })
            Next
        End Using

        Return map_list
    End Function
End Class

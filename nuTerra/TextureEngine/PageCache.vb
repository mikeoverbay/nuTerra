Public Class PageCache
    Dim info As VirtualTextureInfo

    ReadOnly indexer As PageIndexer

    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader

    ReadOnly count As Integer

    Dim current As Integer

    ReadOnly lru As LruCollection(Of Page, Point)
    ReadOnly loading As HashSet(Of Page)

    Public Sub New(info As VirtualTextureInfo, atlas As TextureAtlas, loader As PageLoader, indexer As PageIndexer, count As Integer)
        Me.info = info
        Me.atlas = atlas
        Me.loader = loader
        Me.indexer = indexer
        Me.count = count

        loading = New HashSet(Of Page)()
    End Sub

    ' Update the pages's position in the lru
    Public Function Touch(page As Page) As Boolean
        If Not loading.Contains(page) Then
            Return lru.TryGetValue(page, True, Point.Empty)
        End If
        Return False
    End Function

    ' Schedule a load if Not already loaded Or loading
    Public Function Request(request_ As Page) As Boolean
        If Not loading.Contains(request_) Then
            Dim pt = Point.Empty
            If Not lru.TryGetValue(request_, False, pt) Then
                loading.Add(request_)
                loader.Submit(request_)
                Return True
            End If
        End If

        Return False
    End Function

    Public Sub Clear()
        current = 0
    End Sub
End Class

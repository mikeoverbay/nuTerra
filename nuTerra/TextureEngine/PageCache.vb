Public Class PageCache
    Dim info As VirtualTextureInfo

    ReadOnly indexer As PageIndexer

    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader

    ReadOnly count As Integer

    Dim current As Integer

    Private Structure LruPage
        Dim m_page As Page
        Dim m_point As Point
    End Structure

    ReadOnly lru As New List(Of LruPage)
    ReadOnly lru_used As New HashSet(Of Page)
    ReadOnly loading As New HashSet(Of Page)

    Public Sub New(info As VirtualTextureInfo, atlas As TextureAtlas, loader As PageLoader, indexer As PageIndexer, count As Integer)
        Me.info = info
        Me.atlas = atlas
        Me.loader = loader
        Me.indexer = indexer
        Me.count = count
        AddHandler loader.loadComplete, AddressOf LoadComplete
    End Sub

    Public Sub LoadComplete(p As Page, data As Byte())
        loading.Remove(p)
        Dim pt = Point.Empty

        If current = count * count Then
            pt = lru.Last.m_point
            lru.Remove(lru.Last)
        Else
            pt = New Point(current Mod count, current \ count)
            current += 1

            If current = count * count Then
                LogThis("Atlas Full, using LRU")
            End If
        End If

        atlas.uploadPage(pt, data)
        lru.Add(New LruPage With {.m_page = p, .m_point = pt})
    End Sub

    ' Update the pages's position in the lru
    Public Function Touch(page As Page) As Boolean
        If Not loading.Contains(page) Then
            If Not lru_used.Contains(page) Then
                ' Find the page (slow!!) And add it to the back of the list
                For Each it In lru
                    If it.m_page.Equals(page) Then
                        lru.Remove(it)
                        lru.Add(it)
                        Return True
                    End If
                Next
            End If
        End If
        Return False
    End Function

    ' Schedule a load if Not already loaded Or loading
    Public Function Request(request_ As Page) As Boolean
        If Not loading.Contains(request_) Then
            If Not lru_used.Contains(request_) Then
                loading.Add(request_)
                loader.Submit(request_)
                Return True
            End If
        End If

        Return False
    End Function

    Public Sub Clear()
        lru_used.Clear()
        lru.Clear()
        current = 0
    End Sub
End Class

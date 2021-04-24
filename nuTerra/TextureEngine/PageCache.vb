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

    ' These events are used to notify the other systems
    Public Event Removed(p As Page, pt As Point)
    Public Event Added(p As Page, pt As Point)

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
        Dim pt As Point

        If current = count * count Then
            Dim lru_page = lru.First
            pt = lru_page.m_point
            lru_used.Remove(lru_page.m_page)
            lru.RemoveAt(0)
            RaiseEvent Removed(lru_page.m_page, pt)
        Else
            pt = New Point(current Mod count, current \ count)
            current += 1

            If current = count * count Then
                LogThis("Atlas is Full, using LRU")
            End If
        End If

        atlas.uploadPage(pt, data)
        lru.Add(New LruPage With {.m_page = p, .m_point = pt})
        lru_used.Add(p)

        RaiseEvent Added(p, pt)
    End Sub

    ' Update the pages's position in the lru
    Public Function Touch(p As Page) As Boolean
        If Not loading.Contains(p) Then
            If lru_used.Contains(p) Then
                ' Find the page (slow!!) And add it to the back of the list
                For Each it In lru
                    If (it.m_page.Mip = p.Mip) AndAlso (it.m_page.Y = p.Y) AndAlso (it.m_page.X = p.X) Then
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

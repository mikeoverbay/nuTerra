Public Class PageCache
    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader

    ReadOnly count As Integer
    Public current As Integer = 0

    ReadOnly lru As New List(Of Page)
    ReadOnly lru_used As New Dictionary(Of Page, Integer)

    ' These events are used to notify the other systems
    Public Event Removed(p As Page, mapping As Integer)
    Public Event Added(p As Page, mapping As Integer)

    Public Sub New(info As VirtualTextureInfo, atlas As TextureAtlas, loader As PageLoader, indexer As PageIndexer, count As Integer)
        Me.atlas = atlas
        Me.loader = loader
        Me.count = count
        AddHandler loader.loadComplete, AddressOf LoadComplete
    End Sub

    Public Sub LoadComplete(p As Page, color_data As Byte(), normal_data As Byte())
        Dim mapping As Integer

        If current = count * count Then
            Dim lru_page = lru.First
            mapping = lru_used(lru_page)
            lru_used.Remove(lru_page)
            lru.RemoveAt(0)
            RaiseEvent Removed(lru_page, mapping)
        Else
            mapping = current
            current += 1

            If current = count * count Then
                LogThis("Atlas is Full, using LRU")
            End If
        End If

        atlas.uploadPage(mapping, color_data, normal_data)
        lru.Add(p)
        lru_used.Add(p, mapping)

        RaiseEvent Added(p, mapping)
    End Sub

    ' Update the pages's position in the lru
    Public Function Touch(p As Page) As Boolean
        If lru_used.ContainsKey(p) Then
            ' Find the page (slow!!) And add it to the back of the list
            lru.Remove(p)
            lru.Add(p)
            Return True
        End If
        Return False
    End Function

    ' Schedule a load if Not already loaded Or loading
    Public Function Request(request_ As Page) As Boolean
        If Not lru_used.ContainsKey(request_) Then
            loader.Submit(request_)
            Return True
        End If

        Return False
    End Function

    Public Sub Clear()
        lru_used.Clear()
        lru.Clear()
        current = 0
    End Sub
End Class

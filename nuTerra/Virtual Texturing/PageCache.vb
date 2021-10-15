Public Class PageCache
    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader

    ReadOnly num_tiles As Integer
    Public current As Integer = 0

    ReadOnly lru As LRUCache

    ' These events are used to notify the other systems
    Public Event Removed(p As Page, mapping As Integer)
    Public Event Added(p As Page, mapping As Integer)

    Public Sub New(atlas As TextureAtlas, loader As PageLoader, num_tiles As Integer)
        Me.atlas = atlas
        Me.loader = loader
        Me.num_tiles = num_tiles
        Me.lru = New LRUCache(Me.num_tiles)
        AddHandler loader.loadComplete, AddressOf LoadComplete
    End Sub

    Public Sub LoadComplete(p As Page, color_pbo As GLBuffer, normal_pbo As GLBuffer, specular_data As GLTexture)
        Dim mapping As Integer

        If current = num_tiles Then
            Dim lru_node = lru.RemoveLast()
            mapping = lru_node.Value
            RaiseEvent Removed(lru_node.Key, mapping)
        Else
            mapping = current
            current += 1

            If current = num_tiles Then
                LogThis("Atlas is Full, using LRU")
            End If
        End If

        atlas.uploadPage(mapping, color_pbo, normal_pbo, specular_data)
        lru.Add(p, mapping)

        RaiseEvent Added(p, mapping)
    End Sub

    ' Update the pages's position in the lru
    Public Function Touch(p As Page) As Boolean
        Return lru.ContainsKeyUpdate(p)
    End Function

    ' Schedule a load if Not already loaded Or loading
    Public Function Request(request_ As Page) As Boolean
        If Not lru.ContainsKey(request_) Then
            loader.Submit(request_)
            Return True
        End If
        Return False
    End Function

    Public Sub Clear()
        lru.Clear()
        current = 0
    End Sub
End Class

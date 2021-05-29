Public Class LRUCache
    Private ReadOnly _capacity As Integer
    Private ReadOnly _lruList As New LinkedList(Of LRUCacheItem)
    Private ReadOnly _cacheMap As New Dictionary(Of Page, LinkedListNode(Of LRUCacheItem))(New PageEqualityComparer)

    Public Sub New(capacity As Integer)
        _capacity = capacity
    End Sub


    Public Function ContainsKeyUpdate(key As Page) As Boolean
        Dim node As LinkedListNode(Of LRUCacheItem) = Nothing
        If _cacheMap.TryGetValue(key, node) Then
            MoveToTop(node)
            Return True
        End If
        Return False
    End Function

    Public Function ContainsKey(key As Page) As Boolean
        Return _cacheMap.ContainsKey(key)
    End Function

    Private Sub MoveToTop(node As LinkedListNode(Of LRUCacheItem))
        _lruList.Remove(node)
        _lruList.AddFirst(node)
    End Sub

    Public Sub Add(key As Page, val As Integer)
        Dim cacheItem As New LRUCacheItem(key, val)
        Dim node As New LinkedListNode(Of LRUCacheItem)(cacheItem)
        _lruList.AddFirst(node)
        _cacheMap.Add(key, node)
    End Sub

    Public Function RemoveLast() As LRUCacheItem
        Dim node = _lruList.Last
        _lruList.RemoveLast()
        _cacheMap.Remove(node.Value.Key)
        Return node.Value
    End Function

    Public Sub Clear()
        _lruList.Clear()
        _cacheMap.Clear()
    End Sub
End Class

Public Class LRUCacheItem
    Public Sub New(k As Page, v As Integer)
        Key = k
        Value = v
    End Sub

    Public ReadOnly Key As Page
    Public ReadOnly Value As Integer
End Class
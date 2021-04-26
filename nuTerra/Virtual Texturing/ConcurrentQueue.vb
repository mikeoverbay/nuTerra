Public Class ConcurrentQueue(Of T)

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return False
        End Get
    End Property

    Public Sub Enqueue(value As T)

    End Sub

    Public Function TryDequeue(ByRef value As T) As Boolean
        Return True
    End Function
End Class

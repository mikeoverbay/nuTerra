Public Class PageLoader
    Implements IDisposable

    Class ReadState
        Public Page As Page
        Public Data() As Byte
    End Class

    Dim info As VirtualTextureInfo

    ReadOnly readthread As ProcessingThread(Of ReadState)

    Public Sub New(filename As String, indexer As PageIndexer, info As VirtualTextureInfo)
        Me.info = info

        readthread = New ProcessingThread(Of ReadState)()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose

    End Sub

    Public Sub Submit(request As Page)
        Dim state = New ReadState()
        state.Page = request

        readthread.Enqueue(state)
    End Sub

    Public Sub Update(uploadsperframe As Integer)
        readthread.Update(uploadsperframe)
    End Sub
End Class

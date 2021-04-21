Public Class PageTable
    Implements IDisposable

    Dim info As VirtualTextureInfo

    Public Sub New(cache As PageCache, info As VirtualTextureInfo, indexer As PageIndexer)
        Me.info = info
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose

    End Sub

    Public Sub Update()

    End Sub
End Class

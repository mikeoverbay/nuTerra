Imports OpenTK.Graphics.OpenGL

Public Class PageTable
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer
    Public texture As GLTexture

    Public Sub New(cache As PageCache, info As VirtualTextureInfo, indexer As PageIndexer)
        Me.info = info
        Me.indexer = indexer

        texture = CreateTexture(TextureTarget.Texture2DArray, "PageTable")
        texture.Storage2D(1, SizedInternalFormat.Rgba8, info.PageTableSize, info.PageTableSize)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub

    Public Sub Update()
        Dim PageTableSizeLog2 = Math.Log(info.PageTableSize, 2)

        For i = 0 To PageTableSizeLog2
            ' TODO ????
        Next
    End Sub
End Class

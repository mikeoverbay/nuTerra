''' <summary>
''' Based on: http://linedef.com/virtual-texture-demo.html
''' </summary>

Public Class VirtualTexture
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer
    Public pagetable As PageTable
    Public atlas As TextureAtlas
    ReadOnly loader As PageLoader
    ReadOnly cache As PageCache

    ReadOnly atlascount As Integer
    ReadOnly uploadsperframe As Integer

    ReadOnly toload As List(Of PageCount)

    Dim _mipbias As Integer = 4

    Public Property MipBias As Integer
        Get
            Return _mipbias
        End Get
        Set
            _mipbias = Value
            ' LogThis("MipBias: {0}", _mipbias)
        End Set
    End Property

    Public Sub New(info As VirtualTextureInfo, atlassize As Integer, uploadsperframe As Integer)
        Me.info = info

        Me.atlascount = atlassize \ info.TileSize
        Me.uploadsperframe = uploadsperframe

        indexer = New PageIndexer(info)
        toload = New List(Of PageCount)(indexer.Count)

        atlas = New TextureAtlas(info, atlascount, uploadsperframe)

        loader = New PageLoader(indexer, info)

        cache = New PageCache(info, atlas, loader, indexer, atlascount)

        pagetable = New PageTable(cache, info, indexer)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        pagetable.Dispose()
        atlas.Dispose()
        loader.Dispose()
    End Sub

    Public Sub Clear()
        cache.Clear()
    End Sub

    Public Sub Update(requests As Dictionary(Of Page, Integer))
        toload.Clear()

        ' Find out what is already in memory
        ' If it Is, update it's position in the LRU collection
        ' Otherwise add it to the list of pages to load
        Dim touched = 0
        For Each req In requests
            Dim pc = New PageCount With {
                .Page = req.Key,
                .Count = req.Value
            }

            If Not cache.Touch(pc.Page) Then
                toload.Add(pc)
            Else
                touched += 1
            End If
        Next

        ' Check to make sure we don't thrash
        If touched < atlascount * atlascount Then
            ' sort by low res to high res And number of requests
            toload.Sort()

            ' if more pages than will fit in memory or more than update per frame drop high res pages with lowest use count
            Dim loadcount = Math.Min(Math.Min(toload.Count, uploadsperframe), atlascount * atlascount)
            For i = 0 To loadcount - 1
                cache.Request(toload(i).Page)
            Next
        Else
            MipBias -= 1
        End If

        ' Update the page table
        pagetable.Update()
    End Sub
End Class

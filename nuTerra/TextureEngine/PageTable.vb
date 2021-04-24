Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL

<StructLayout(LayoutKind.Sequential)>
Public Structure TableEntry
    Dim cachePageX As Byte
    Dim cachePageY As Byte
    Dim mipLevel As Byte
End Structure

Public Class PageTable
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer
    Public texture As GLTexture

    ReadOnly tableEntryPool As List(Of TableEntry(,))
    ReadOnly quadtree As Quadtree

    Public Sub New(cache As PageCache, info As VirtualTextureInfo, indexer As PageIndexer)
        Me.info = info
        Me.indexer = indexer

        Dim numLevels As Integer = Math.Log(info.PageTableSize, 2) + 1
        Me.quadtree = New Quadtree(New Rectangle(0, 0, info.PageTableSize, info.PageTableSize), numLevels - 1)

        AddHandler cache.Added, AddressOf Me.quadtree.Add
        AddHandler cache.Removed, Sub(p As Page, pt As Point)
                                      Me.quadtree.Remove(p)
                                  End Sub

        tableEntryPool = New List(Of TableEntry(,))

        For i = 0 To numLevels - 1
            Dim arr(indexer.sizes(i) - 1, indexer.sizes(i) - 1) As TableEntry
            tableEntryPool.Add(arr)
        Next

        texture = CreateTexture(TextureTarget.Texture2D, "PageTable")
        texture.Storage2D(numLevels, InternalFormat.Rgb8, info.PageTableSize, info.PageTableSize)
        texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.NearestMipmapNearest)
        texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        texture.Parameter(TextureParameterName.TextureWrapR, TextureWrapMode.ClampToEdge)
        texture.Parameter(TextureParameterName.TextureBaseLevel, 0)
        texture.Parameter(TextureParameterName.TextureMaxLevel, numLevels - 1)

        For l = 0 To numLevels - 1
            Dim handle = GCHandle.Alloc(tableEntryPool(l), GCHandleType.Pinned)
            Dim ptr = handle.AddrOfPinnedObject()
            texture.SubImage2D(l, 0, 0, indexer.sizes(l), indexer.sizes(l), PixelFormat.Rgb, PixelType.UnsignedByte, ptr)
            handle.Free()
        Next
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub

    Public Sub Update()
        Dim numLevels = Math.Log(info.PageTableSize, 2) + 1

        For l = 0 To numLevels - 1
            quadtree.Write(tableEntryPool(l), l)

            Dim handle = GCHandle.Alloc(tableEntryPool(l), GCHandleType.Pinned)
            Dim ptr = handle.AddrOfPinnedObject()
            texture.SubImage2D(l, 0, 0, indexer.sizes(l), indexer.sizes(l), PixelFormat.Rgb, PixelType.UnsignedByte, ptr)
            handle.Free()
        Next
    End Sub
End Class

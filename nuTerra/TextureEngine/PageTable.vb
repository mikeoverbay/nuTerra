﻿Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL

Public Structure TableEntry
    Dim cachePageX As Byte
    Dim cachePageY As Byte
    Dim mipLevel As Byte
    Dim unused As Byte
End Structure

Public Class PageTable
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    ReadOnly indexer As PageIndexer
    Public texture As GLTexture

    ReadOnly tableEntryPool As List(Of TableEntry())
    ReadOnly quadtree As Quadtree

    Public Sub New(cache As PageCache, info As VirtualTextureInfo, indexer As PageIndexer)
        Me.info = info
        Me.indexer = indexer

        Me.quadtree = New Quadtree(New Rectangle(0, 0, info.PageTableSize, info.PageTableSize), Math.Log(info.PageTableSize, 2))

        AddHandler cache.Added, AddressOf Me.quadtree.Add
        AddHandler cache.Removed, Sub(p As Page, pt As Point)
                                      Me.quadtree.Remove(p)
                                  End Sub

        Dim numLevels = Math.Log(info.PageTableSize, 2) + 1
        tableEntryPool = New List(Of TableEntry())

        For i = 0 To numLevels - 1
            Dim arr(indexer.sizes(i) * indexer.sizes(i) - 1) As TableEntry
            tableEntryPool.Add(arr)
        Next

        texture = CreateTexture(TextureTarget.Texture2D, "PageTable")
        texture.Storage2D(numLevels, SizedInternalFormat.Rgba8, info.PageTableSize, info.PageTableSize)

        For l = 0 To numLevels - 1
            For e = 0 To (indexer.sizes(l) * indexer.sizes(l)) - 1
                With tableEntryPool(l)(e)
                    .cachePageX = 0
                    .cachePageY = 0
                    .mipLevel = 0
                    .unused = 255
                End With
            Next

            Dim handle = GCHandle.Alloc(tableEntryPool(l), GCHandleType.Pinned)
            Dim ptr = handle.AddrOfPinnedObject()
            texture.SubImage2D(l, 0, 0, indexer.sizes(l), indexer.sizes(l), PixelFormat.Rgba, PixelType.UnsignedByte, ptr)
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
            texture.SubImage2D(l, 0, 0, indexer.sizes(l), indexer.sizes(l), PixelFormat.Rgba, PixelType.UnsignedByte, ptr)
            handle.Free()
        Next
    End Sub
End Class

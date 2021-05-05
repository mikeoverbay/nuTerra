Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

Public Class PageTable
    Implements IDisposable

    ReadOnly info As VirtualTextureInfo
    Public texture As GLTexture

    ReadOnly tableEntryPool As List(Of UShort(,))
    ReadOnly quadtree As nuTerraCPP.QuadtreeWrap
    Dim quadtreeDirty As Boolean = True

    Public Sub New(cache As PageCache, info As VirtualTextureInfo)
        Me.info = info

        Dim numLevels As Integer = Math.Log(info.PageTableSize, 2) + 1
        Me.quadtree = New nuTerraCPP.QuadtreeWrap(info.PageTableSize, numLevels - 1)

        AddHandler cache.Added, Sub(p As Page, mapping As Integer)
                                    Me.quadtreeDirty = True
                                    Me.quadtree.Add(p.Packed, mapping)
                                End Sub
        AddHandler cache.Removed, Sub(p As Page, mapping As Integer)
                                      Me.quadtreeDirty = True
                                      Me.quadtree.Remove(p.Packed)
                                  End Sub

        tableEntryPool = New List(Of UShort(,))

        For i = 0 To numLevels - 1
            Dim size = info.PageTableSize >> i
            Dim arr(size - 1, size - 1) As UShort
            tableEntryPool.Add(arr)
        Next

        texture = CreateTexture(TextureTarget.Texture2D, "PageTable")
        texture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.NearestMipmapNearest)
        texture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        texture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        texture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
        texture.Parameter(TextureParameterName.TextureBaseLevel, 0)
        texture.Parameter(TextureParameterName.TextureMaxLevel, numLevels - 1)
        texture.Storage2D(numLevels, SizedInternalFormat.R16ui, info.PageTableSize, info.PageTableSize)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        texture.Delete()
    End Sub

    Public Sub Update()
        If Not quadtreeDirty Then
            Return
        End If

        quadtreeDirty = False

        Dim numLevels = Math.Log(info.PageTableSize, 2) + 1
        Dim pageTblTask As New Task(Sub()
                                        quadtree.Write(tableEntryPool(0), 0)
                                    End Sub)
        pageTblTask.Start()

        For l = 1 To numLevels - 1
            quadtree.Write(tableEntryPool(l), l)

            Dim handle = GCHandle.Alloc(tableEntryPool(l), GCHandleType.Pinned)
            Dim ptr = handle.AddrOfPinnedObject()
            Dim size = info.PageTableSize >> l
            texture.SubImage2D(l, 0, 0, size, size, PixelFormat.RedInteger, PixelType.UnsignedShort, ptr)
            handle.Free()
        Next

        pageTblTask.Wait()

        For l = 0 To 0
            Dim handle = GCHandle.Alloc(tableEntryPool(l), GCHandleType.Pinned)
            Dim ptr = handle.AddrOfPinnedObject()
            Dim size = info.PageTableSize >> l
            texture.SubImage2D(l, 0, 0, size, size, PixelFormat.RedInteger, PixelType.UnsignedShort, ptr)
            handle.Free()
        Next
    End Sub
End Class

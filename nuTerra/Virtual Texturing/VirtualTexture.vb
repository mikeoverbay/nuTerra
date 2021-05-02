Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Based on: http://linedef.com/virtual-texture-demo.html
''' </summary>

Public Class VirtualTexture
    Implements IDisposable

    ReadOnly indexer As PageIndexer
    ReadOnly pagetable As PageTable
    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader
    ReadOnly cache As PageCache

    ReadOnly num_tiles As Integer
    ReadOnly uploadsperframe As Integer

    ReadOnly toload As List(Of PageCount)

    Dim _mipbias As Integer = 5

    Public Property MipBias As Integer
        Get
            Return _mipbias
        End Get
        Set
            _mipbias = Value
            ' LogThis("MipBias: {0}", _mipbias)
        End Set
    End Property

    Public Sub New(info As VirtualTextureInfo, num_tiles As Integer, uploadsperframe As Integer)
        Me.num_tiles = num_tiles
        Me.uploadsperframe = uploadsperframe

        indexer = New PageIndexer(info)
        toload = New List(Of PageCount)(indexer.Count)
        atlas = New TextureAtlas(info, num_tiles)
        loader = New PageLoader(indexer, info)
        cache = New PageCache(atlas, loader, num_tiles)
        pagetable = New PageTable(cache, info)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        pagetable.Dispose()
        atlas.Dispose()
        loader.Dispose()
    End Sub

    Public Sub Bind()
#If False Then
        ' SHOULD WE USE MULTI BIND?
        Dim textures() = {
            pagetable.texture.texture_id,
            atlas.color_texture.texture_id,
            atlas.normal_texture.texture_id,
            atlas.specular_texture.texture_id
            }
        GL.BindTextures(0, 4, textures)
#Else
        pagetable.texture.BindUnit(0)
        atlas.color_texture.BindUnit(1)
        atlas.normal_texture.BindUnit(2)
        atlas.specular_texture.BindUnit(3)
#End If
    End Sub

    Public Sub Unbind()
        unbind_textures(4)
    End Sub

    Public Sub DebugDraw(location As Point, size As Point, proj As OpenTK.Matrix4)
        atlas.color_texture.BindUnit(0)

        image2dArrayShader.Use()
        GL.UniformMatrix4(image2dArrayShader("ProjectionMatrix"), False, proj)

        Dim rect = New RectangleF(location.X, location.Y, size.X / 10, size.X / 10)
        GL.Uniform4(image2dArrayShader("rect"),
                    rect.Left,
                    -rect.Bottom,
                    rect.Right,
                    -rect.Top)

        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, num_tiles)

        image2dArrayShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
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
        If touched < num_tiles Then
            ' sort by low res to high res And number of requests
            toload.Sort()

            ' if more pages than will fit in memory or more than update per frame drop high res pages with lowest use count
            Dim loadcount = Math.Min(Math.Min(toload.Count, uploadsperframe), num_tiles)
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

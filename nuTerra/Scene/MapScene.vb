Public Class MapScene
    Implements IDisposable

    ' Get data from gpu
    Public numAfterFrustum(2) As Integer

    ' OpenGL buffers used to draw all map models
    ' For map models only!
    Public materials As GLBuffer
    Public parameters As GLBuffer
    Public parameters_temp As GLBuffer
    Public matrices As GLBuffer
    Public drawCandidates As GLBuffer
    Public verts As GLBuffer
    Public vertsUV2 As GLBuffer
    Public prims As GLBuffer
    Public indirect As GLBuffer
    Public indirect_glass As GLBuffer
    Public indirect_dbl_sided As GLBuffer
    Public lods As GLBuffer

    ' For terrain only!
    Public terrain_matrices As GLBuffer
    Public terrain_indirect As GLBuffer
    Public terrain_vertices As GLBuffer
    Public terrain_indices As GLBuffer

    ' For cull-raster only!
    Public visibles As GLBuffer
    Public visibles_dbl_sided As GLBuffer

    Public allMapModels As GLVertexArray
    Public allTerrainChunks As GLVertexArray

    Public numModelInstances As Integer
    Public indirectDrawCount As Integer

    ' Virtual Texturing
    Public vt As VirtualTexture
    Public vtInfo As VirtualTextureInfo
    Public feedback As FeedbackBuffer

    ReadOnly mapName As String

    Public Sub New(mapName As String)
        Me.mapName = mapName
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        materials?.Dispose()
        parameters?.Dispose()
        parameters_temp?.Dispose()
        matrices?.Dispose()
        drawCandidates?.Dispose()
        verts?.Dispose()
        vertsUV2?.Dispose()
        prims?.Dispose()
        indirect?.Dispose()
        indirect_glass?.Dispose()
        indirect_dbl_sided?.Dispose()
        lods?.Dispose()

        terrain_matrices?.Dispose()
        terrain_indirect?.Dispose()
        terrain_vertices?.Dispose()
        terrain_indices?.Dispose()

        visibles?.Dispose()
        visibles_dbl_sided?.Dispose()

        allMapModels?.Dispose()
        allTerrainChunks?.Dispose()

        vt?.Dispose()
        feedback?.Dispose()
    End Sub

    Public Sub RebuildVTAtlas()
        LogThis("REBUILD ATLAS")

        vtInfo = New VirtualTextureInfo With {
            .TileSize = TILE_SIZE,
            .VirtualTextureSize = TILE_SIZE * VT_NUM_PAGES
            }

        vt?.Dispose()
        vt = New VirtualTexture(vtInfo, NUM_TILES, UPLOADS_PER_FRAME)

        feedback?.Dispose()
        feedback = New FeedbackBuffer(map_scene.vtInfo, FEEDBACK_WIDTH, FEEDBACK_HEIGHT)

        CommonProperties.VirtualTextureSize = vtInfo.VirtualTextureSize
        CommonProperties.AtlasScale = 1.0F / (vtInfo.VirtualTextureSize / vtInfo.TileSize)
        CommonProperties.PageTableSize = vtInfo.PageTableSize
        CommonProperties.update()
    End Sub

End Class

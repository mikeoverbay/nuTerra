Public Class MapTerrain
    Implements IDisposable

    Public matrices As GLBuffer
    Public indirect_buffer As GLBuffer
    Public vertices_buffer As GLBuffer
    Public indices_buffer As GLBuffer
    Public all_chunks_vao As GLVertexArray

    Public vt As VirtualTexture
    Public vtInfo As VirtualTextureInfo
    Public feedback As FeedbackBuffer

    Public Sub RebuildVTAtlas()
        LogThis("REBUILD ATLAS")

        vtInfo = New VirtualTextureInfo With {
            .TileSize = TILE_SIZE,
            .VirtualTextureSize = TILE_SIZE * VT_NUM_PAGES
            }

        vt?.Dispose()
        vt = New VirtualTexture(vtInfo, NUM_TILES, UPLOADS_PER_FRAME)

        feedback?.Dispose()
        feedback = New FeedbackBuffer(vtInfo, FEEDBACK_WIDTH, FEEDBACK_HEIGHT)

        CommonProperties.VirtualTextureSize = vtInfo.VirtualTextureSize
        CommonProperties.AtlasScale = 1.0F / (vtInfo.VirtualTextureSize / vtInfo.TileSize)
        CommonProperties.PageTableSize = vtInfo.PageTableSize
        CommonProperties.update()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        matrices?.Dispose()
        indirect_buffer?.Dispose()
        vertices_buffer?.Dispose()
        indices_buffer?.Dispose()
        all_chunks_vao?.Dispose()

        vt?.Dispose()
        feedback?.Dispose()
    End Sub
End Class

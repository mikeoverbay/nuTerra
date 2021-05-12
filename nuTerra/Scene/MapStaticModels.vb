Public Class MapStaticModels
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

    ' For cull-raster only!
    Public visibles As GLBuffer
    Public visibles_dbl_sided As GLBuffer

    Public allMapModels As GLVertexArray

    Public numModelInstances As Integer
    Public indirectDrawCount As Integer

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

        visibles?.Dispose()
        visibles_dbl_sided?.Dispose()

        allMapModels?.Dispose()
    End Sub
End Class

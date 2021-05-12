Public Class MapScene
    Implements IDisposable

    ReadOnly mapName As String
    Public terrain As New MapTerrain
    Public static_models As New MapStaticModels

    Public Sub New(mapName As String)
        Me.mapName = mapName
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        terrain.Dispose()
        static_models.Dispose()
    End Sub
End Class

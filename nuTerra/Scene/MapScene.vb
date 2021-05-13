Public Class MapScene
    Implements IDisposable

    ReadOnly mapName As String
    Public sky As New MapSky
    Public terrain As New MapTerrain
    Public static_models As New MapStaticModels
    Public water As New MapWater
    Public base_rings As New MapBaseRings
    Public mini_map As New MapMinimap
    Public fog As New MapFog
    Public trees As New MapTrees
    Public cursor As New MapCursor

    Public Sub New(mapName As String)
        Me.mapName = mapName
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        sky.Dispose()
        terrain.Dispose()
        static_models.Dispose()
        water.Dispose()
        base_rings.Dispose()
        mini_map.Dispose()
        fog.Dispose()
        cursor.Dispose()
    End Sub
End Class

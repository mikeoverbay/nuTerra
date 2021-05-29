Public Class MapWater
    Implements IDisposable

    ReadOnly scene As MapScene

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
    End Sub
End Class

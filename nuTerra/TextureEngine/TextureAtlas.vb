Public Class TextureAtlas
    Implements IDisposable

    Dim info As VirtualTextureInfo

    Public Sub New(info As VirtualTextureInfo, atlascount As Integer, uploadsperframe As Integer)
        Me.info = info
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose

    End Sub
End Class

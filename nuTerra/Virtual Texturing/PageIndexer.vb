Public Class PageIndexer
    ReadOnly info As VirtualTextureInfo
    ReadOnly mipcount As Integer
    Public sizes As Integer() ' This stores the sizes Of various mip levels
    Public Count As Integer

    Public Sub New(info As VirtualTextureInfo)
        Me.info = info
        mipcount = Math.Log(info.PageTableSize, 2) + 1

        Count = 0
        ReDim sizes(mipcount - 1)
        For i = 0 To mipcount - 1
            sizes(i) = (info.VirtualTextureSize \ info.TileSize) >> i
            Count += sizes(i) * sizes(i)
        Next
    End Sub

    Public Function IsValid(X As Integer, Y As Integer, Mip As Integer) As Boolean
        If Mip < 0 Then
            Return False
        ElseIf Mip >= mipcount Then
            Return False
        End If

        If X < 0 Then
            Return False
        ElseIf X >= sizes(Mip) Then
            Return False
        End If

        If Y < 0 Then
            Return False
        ElseIf Y >= sizes(Mip) Then
            Return False
        End If

        Return True
    End Function
End Class

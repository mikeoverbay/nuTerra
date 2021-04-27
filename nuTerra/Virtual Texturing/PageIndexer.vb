Public Class PageIndexer
    ReadOnly info As VirtualTextureInfo
    ReadOnly mipcount As Integer
    Public offsets As Integer() ' This stores the offsets To the first page Of the start Of a mipmap level 
    Public sizes As Integer() ' This stores the sizes Of various mip levels
    Public Count As Integer

    Public Sub New(info As VirtualTextureInfo)
        Me.info = info
        mipcount = Math.Log(info.PageTableSize, 2) + 1

        Count = 0
        ReDim sizes(mipcount - 1)
        ReDim offsets(mipcount - 1)
        For i = 0 To mipcount - 1
            sizes(i) = (info.VirtualTextureSize \ info.TileSize) >> i
            offsets(i) = Count
            Count += sizes(i) * sizes(i)
        Next
    End Sub

    Default Public ReadOnly Property Item(page As Page) As Integer
        Get
            If page.Mip < 0 Or page.Mip >= mipcount Then
                Throw New Exception("Page is not valid")
            End If

            Dim offset = offsets(page.Mip)
            Dim stride = sizes(page.Mip)

            Return offset + page.Y * stride + page.X
        End Get
    End Property

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

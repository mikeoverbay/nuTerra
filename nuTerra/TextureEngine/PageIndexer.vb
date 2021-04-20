Public Class PageIndexer
    ReadOnly info As VirtualTextureInfo
    ReadOnly mipcount As Integer
    ReadOnly offsets As Integer() ' This stores the offsets To the first page Of the start Of a mipmap level 
    ReadOnly sizes As Integer() ' This stores the sizes Of various mip levels
    ReadOnly reverse As Page()

    Public Count As Integer

    Public Sub New(info As VirtualTextureInfo)
        Me.info = info
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

    Public Function GetPageFromIndex(index As Integer) As Page
        Return reverse(index)
    End Function

    Public Function IsValid(page As Page) As Boolean
        Return True
    End Function
End Class

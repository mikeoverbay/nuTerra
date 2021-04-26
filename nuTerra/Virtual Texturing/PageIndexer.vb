﻿Public Class PageIndexer
    ReadOnly info As VirtualTextureInfo
    ReadOnly mipcount As Integer
    Public offsets As Integer() ' This stores the offsets To the first page Of the start Of a mipmap level 
    Public sizes As Integer() ' This stores the sizes Of various mip levels
    ReadOnly reverse As Page()

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

        ReDim reverse(Count - 1)
        For i = 0 To mipcount - 1
            Dim size = sizes(i)
            For y = 0 To size - 1
                For x = 0 To size - 1
                    Dim Page As New Page(x, y, i)
                    reverse(Me(Page)) = Page
                Next
            Next
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

    Public Function GetPageFromIndex(index As Integer) As Page
        Return reverse(index)
    End Function

    Public Function IsValid(page As Page) As Boolean
        If page.Mip < 0 Then
            'LogThis(String.Format("Mip level smaller than zero ({0}).", page))
            Return False
        ElseIf page.Mip >= mipcount Then
            'LogThis(String.Format("Mip level larger than max ({1}), ({0}).", page, mipcount))
            Return False
        End If

        If page.X < 0 Then
            'LogThis(String.Format("X smaller than zero ({0}).", page))
            Return False
        ElseIf page.X >= sizes(page.Mip) Then
            'LogThis(String.Format("X larger than max ({1}), ({0}).", page, sizes(page.Mip)))
            Return False
        End If

        If page.Y < 0 Then
            'LogThis(String.Format("Y smaller than zero ({0}).", page))
            Return False
        ElseIf page.Y >= sizes(page.Mip) Then
            'LogThis(String.Format("Y larger than max ({1}), ({0}).", page, sizes(page.Mip)))
            Return False
        End If

        Return True
    End Function
End Class
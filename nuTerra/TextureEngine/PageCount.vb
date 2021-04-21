Imports System

Public Class PageCount
    Implements IComparable(Of PageCount)

    Public Page As Page
    Public Count As Integer

    Public Function CompareTo(other As PageCount) As Integer _
        Implements IComparable(Of PageCount).CompareTo

        If other.Page.Mip <> Page.Mip Then
            Return other.Page.Mip.CompareTo(Page.Mip)
        End If

        Return other.Count.CompareTo(Count)
    End Function
End Class

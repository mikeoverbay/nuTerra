<DebuggerDisplay("( {X}, {Y}, {Mip} )")>
Public Class Page
    Implements IEquatable(Of Page)

    Public X As Integer
    Public Y As Integer
    Public Mip As Integer
    Public Packed As UInteger

    Public Sub New(x As UInteger, y As UInteger, mip As UInteger)
        Me.X = x
        Me.Y = y
        Me.Mip = mip
        Me.Packed = mip Or (y << 8UI) Or (x << 20UI)
    End Sub

    Public Overrides Function ToString() As String
        Return String.Format("{0}, {1}, {2}", X, Y, Mip)
    End Function

    Public Overloads Function Equals(other As Page) As Boolean Implements IEquatable(Of Page).Equals
        Return Packed = other.Packed
    End Function
End Class

Public Class PageEqualityComparer
    Implements IEqualityComparer(Of Page)

    Public Overloads Function GetHashCode(obj As Page) As Integer Implements IEqualityComparer(Of Page).GetHashCode
        Return obj.Packed.GetHashCode()
    End Function

    Public Overloads Function Equals(x As Page, y As Page) As Boolean Implements IEqualityComparer(Of Page).Equals
        Return x.Packed = y.Packed
    End Function
End Class

Imports System.Diagnostics

<DebuggerDisplay("( {X}, {Y}, {Mip} )")>
Public Structure Page
	Implements IComparable(Of Page)

	Public X As Integer
	Public Y As Integer
	Public Mip As Integer

	Public Sub New(x As Integer, y As Integer, mip As Integer)
		Me.X = x
		Me.Y = y
		Me.Mip = mip
	End Sub

	Public Function CompareTo(other As Page) As Integer _
		Implements IComparable(Of Page).CompareTo

		If X <> other.X Then
			Return other.X.CompareTo(X)
		End If

		If Y <> other.Y Then
			Return other.Y.CompareTo(Y)
		End If

		Return other.Mip.CompareTo(Mip)
	End Function

	Public Overrides Function ToString() As String
		Return String.Format("{0}, {1}, {2}", X, Y, Mip)
	End Function
End Structure

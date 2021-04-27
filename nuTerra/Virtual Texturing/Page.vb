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

	ReadOnly Property AsPacked As UInteger
		Get
			Return Mip Or (Y << 8) Or (X << 20)
		End Get
	End Property

	Public Function CompareTo(other As Page) As Integer _
		Implements IComparable(Of Page).CompareTo
		Return other.AsPacked.CompareTo(Me.AsPacked)
	End Function

	Public Overrides Function ToString() As String
		Return String.Format("{0}, {1}, {2}", X, Y, Mip)
	End Function
End Structure

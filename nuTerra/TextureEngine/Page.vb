Imports System.Diagnostics

<DebuggerDisplay("( {X}, {Y}, {Mip} )")>
Public Structure Page
	Public X As Integer
	Public Y As Integer
	Public Mip As Integer

	Public Sub New(x As Integer, y As Integer, mip As Integer)
		Me.X = x
		Me.Y = y
		Me.Mip = mip
	End Sub
End Structure

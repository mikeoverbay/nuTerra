Public Class LruCollection(Of Key, Value)
	Public Function TryGetValue(key As Key, update As Boolean, ByRef value As Value) As Boolean
		Return False
	End Function
End Class

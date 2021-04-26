Public Class Quadtree
	Public Level As Integer
	Public Rectangle As Rectangle
	Public Mapping As Point

	Public Children As Quadtree()

	Public Sub New(rect As Rectangle, level As Integer)
		Me.Level = level
		Me.Rectangle = rect
		ReDim Me.Children(3)
	End Sub

	Public Sub Add(request As Page, mapping As Point)
		Dim scale = 1 << request.Mip
		Dim x = request.X * scale
		Dim y = request.Y * scale

		Dim node = Me
		While request.Mip < node.Level
			For i = 0 To 3
				Dim rect = GetRectangle(node, i)
				If rect.Contains(x, y) Then
					' Create a New one if needed
					If node.Children(i) Is Nothing Then
						node.Children(i) = New Quadtree(rect, node.Level - 1)
						node = node.Children(i)
						Exit For
					Else ' Otherwise traverse the tree
						node = node.Children(i)
						Exit For
					End If
				End If
			Next
		End While
		' We have created the correct node, now set the mapping
		node.Mapping = mapping
	End Sub

	Public Sub Remove(request As Page)
		Dim index As Integer
		Dim node = FindPage(Me, request, index)
		If node IsNot Nothing Then
			node.Children(index) = Nothing
		End If
	End Sub

	Public Sub Write(data As UShort(,), miplevel As Integer)
		Quadtree.Write(Me, data, miplevel)
	End Sub

	' Static Functions
	Shared Function GetRectangle(node As Quadtree, index As Integer) As Rectangle
		Dim x = node.Rectangle.X
		Dim y = node.Rectangle.Y
		Dim w = node.Rectangle.Width \ 2
		Dim h = node.Rectangle.Height \ 2

		Select Case index
			Case 0
				Return New Rectangle(x, y, w, h)
			Case 1
				Return New Rectangle(x + w, y, w, h)
			Case 2
				Return New Rectangle(x + w, y + h, w, h)
			Case 3
				Return New Rectangle(x, y + h, w, h)
		End Select

		Throw New ArgumentOutOfRangeException("index")
	End Function

	Shared Sub Write(node As Quadtree, data As UShort(,), miplevel As Integer)
		If node.Level >= miplevel Then
			Dim rx = node.Rectangle.X >> miplevel
			Dim ry = node.Rectangle.Y >> miplevel
			Dim rw = node.Rectangle.Width >> miplevel
			Dim rh = node.Rectangle.Height >> miplevel

			For i = ry To ry + rh - 1
				For j = rx To rx + rw - 1
					If node.Mapping.X > 32 Then Stop
					If node.Mapping.Y > 64 Then Stop
					If node.Level > 32 Then Stop
					data(i, j) = (node.Mapping.X << 11) Or (node.Mapping.Y << 5) Or node.Level
				Next
			Next

			For Each child In node.Children
				If child IsNot Nothing Then
					Quadtree.Write(child, data, miplevel)
				End If
			Next
		End If
	End Sub

	Shared Function FindPage(node As Quadtree, request As Page, ByRef index As Integer) As Quadtree
		Dim scale = 1 << request.Mip
		Dim x = request.X * scale
		Dim y = request.Y * scale

		' Find the parent of the child we want to remove
		Dim exitloop = False
		While Not exitloop
			exitloop = True

			For i = 0 To 3
				If node.Children(i) IsNot Nothing AndAlso node.Children(i).Rectangle.Contains(x, y) Then
					' We found it
					If request.Mip = node.Level - 1 Then
						index = i
						Return node
					Else ' Check the children
						node = node.Children(i)
						exitloop = False
					End If
				End If
			Next
		End While

		' We couldn't find it so it must not exist anymore
		index = -1
		Return Nothing
	End Function
End Class

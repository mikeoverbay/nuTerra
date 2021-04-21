Imports System.Threading

Public Class ProcessingThread(Of T As Class)
	Dim action As Action(Of T)
	Dim complete As Action(Of T)

	Dim thread As Thread
	Dim semaphore As Semaphore

	Dim actionqueue As ConcurrentQueue(Of T)
	Dim completequeue As ConcurrentQueue(Of T)

	Public Sub Enqueue(element As T)
		actionqueue.Enqueue(element)
		semaphore.Release(1)
	End Sub

	Public Sub Update(count As Integer)
		Dim i = 0
		While i < count And Not completequeue.IsEmpty
			Dim element = CType(Nothing, T)
			If Not completequeue.TryDequeue(element) Then
				Console.WriteLine("Error dequeuing")
				Exit While
			End If

			complete(element)
		End While
	End Sub
End Class

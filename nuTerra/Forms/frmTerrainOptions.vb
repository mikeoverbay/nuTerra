Public Class frmTerrainOptions
    Public Shared Sub Init()
        PerViewData._start = 75
        PerViewData._end = 200
    End Sub

    Private Sub StartNumericUpDown_ValueChanged(sender As Object, e As EventArgs) Handles StartNumericUpDown.ValueChanged
        PerViewData._start = StartNumericUpDown.Value
    End Sub

    Private Sub NumericUpDown1_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown1.ValueChanged
        PerViewData._end = NumericUpDown1.Value
    End Sub
End Class
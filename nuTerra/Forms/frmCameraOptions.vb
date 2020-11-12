Public Class frmCameraOptions
    Private Sub NumericUpDown1_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown1.ValueChanged
        FieldOfView = CSng(Math.PI) * (NumericUpDown1.Value / 180.0F)
    End Sub

    Private Sub NumericUpDown2_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown2.ValueChanged
        PRESPECTIVE_NEAR = NumericUpDown2.Value
    End Sub

    Private Sub NumericUpDown3_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown3.ValueChanged
        PRESPECTIVE_FAR = NumericUpDown3.Value
    End Sub

    Private Sub NumericUpDown4_speed_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown4_speed.ValueChanged
    End Sub

    Private Sub frmCameraOptions_Load(sender As Object, e As EventArgs) Handles Me.Load

    End Sub

    Private Sub frmCameraOptions_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        'My.Settings.Save()
    End Sub
End Class
Public Class frmCameraOptions
    Private Sub NumericUpDown1_ValueChanged(sender As Object, e As EventArgs) Handles FoVNumericUpDown.ValueChanged
        FieldOfView = CSng(Math.PI) * (FoVNumericUpDown.Value / 180.0F)
    End Sub

    Private Sub NumericUpDown2_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown2.ValueChanged
        PRESPECTIVE_NEAR = NumericUpDown2.Value
    End Sub

    Private Sub NumericUpDown3_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown3.ValueChanged
        PRESPECTIVE_FAR = NumericUpDown3.Value
    End Sub

    Private Sub NumericUpDown4_speed_ValueChanged(sender As Object, e As EventArgs) Handles NumericUpDown4_speed.ValueChanged
    End Sub

    Private Sub ResetButton_Click(sender As Object, e As EventArgs) Handles ResetButton.Click
        FoVNumericUpDown.Value = My.Settings.PropertyValues("fov").Property.DefaultValue
        NumericUpDown2.Value = My.Settings.PropertyValues("near").Property.DefaultValue
        NumericUpDown3.Value = My.Settings.PropertyValues("far").Property.DefaultValue
        NumericUpDown4_speed.Value = My.Settings.PropertyValues("speed").Property.DefaultValue
    End Sub
End Class
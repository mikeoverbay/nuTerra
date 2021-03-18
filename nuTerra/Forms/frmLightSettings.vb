Imports System.IO

Public Class frmLightSettings
    Public Shared lighting_terrain_texture As Single
    Public Shared lighting_ambient As Single
    Public Shared lighting_fog_level As Single
    Public Shared lighting_gray_level As Single
    Public Shared lighting_specular_level As Single
    Public Shared lighting_gamma_level As Single

    Public Shared Sub Init()
        lighting_terrain_texture = My.Settings.Bright_level / 50.0!
        lighting_ambient = My.Settings.Ambient_level / 300.0!
        lighting_gamma_level = My.Settings.Gamma_level / 100.0!
        lighting_fog_level = My.Settings.Fog_level / 10000.0! ' yes 10,000
        lighting_specular_level = My.Settings.Specular_level / 100.0!
        lighting_gray_level = 1.0 - (My.Settings.Gray_level / 100.0!)
    End Sub

    Private Sub ResetButton_Click(sender As Object, e As EventArgs) Handles ResetButton.Click
        s_terrain_texture_level.Value = My.Settings.PropertyValues("Bright_Level").Property.DefaultValue
        s_terrain_ambient.Value = My.Settings.PropertyValues("Ambient_level").Property.DefaultValue
        s_gamma.Value = My.Settings.PropertyValues("Gamma_level").Property.DefaultValue
        s_fog_level.Value = My.Settings.PropertyValues("Fog_level").Property.DefaultValue
        s_specular_level.Value = My.Settings.PropertyValues("Specular_level").Property.DefaultValue
        s_gray_level.Value = My.Settings.PropertyValues("Gray_level").Property.DefaultValue
    End Sub

    Private Sub s_specular_level_ValueChanged(sender As Object, e As EventArgs) Handles s_specular_level.ValueChanged
        lighting_specular_level = s_specular_level.Value / 100.0!
    End Sub

    Private Sub s_fog_level_ValueChanged(sender As Object, e As EventArgs) Handles s_fog_level.ValueChanged
        lighting_fog_level = s_fog_level.Value / 10000.0! ' yes 10,000
    End Sub

    Private Sub s_terrain_ambient_ValueChanged(sender As Object, e As EventArgs) Handles s_terrain_ambient.ValueChanged
        lighting_ambient = s_terrain_ambient.Value / 300.0!
    End Sub

    Private Sub s_terrain_texture_level_ValueChanged(sender As Object, e As EventArgs) Handles s_terrain_texture_level.ValueChanged
        lighting_terrain_texture = s_terrain_texture_level.Value / 50.0!
    End Sub

    Private Sub s_gamma_ValueChanged(sender As Object, e As EventArgs) Handles s_gamma.ValueChanged
        lighting_gamma_level = s_gamma.Value / 100.0!
    End Sub

    Private Sub s_gray_level_ValueChanged(sender As Object, e As EventArgs) Handles s_gray_level.ValueChanged
        lighting_gray_level = 1.0 - (s_gray_level.Value / 100.0!)
    End Sub

    Private Sub s_terrain_texture_level_MouseEnter(sender As Object, e As EventArgs) Handles s_terrain_texture_level.MouseEnter
        s_terrain_texture_level.Focus()
    End Sub

    Private Sub s_terrain_ambient_MouseEnter(sender As Object, e As EventArgs) Handles s_terrain_ambient.MouseEnter
        s_terrain_ambient.Focus()
    End Sub

    Private Sub s_fog_level_MouseEnter(sender As Object, e As EventArgs) Handles s_fog_level.MouseEnter
        s_fog_level.Focus()
    End Sub

    Private Sub s_model_level_MouseEnter(sender As Object, e As EventArgs) Handles s_specular_level.MouseEnter
        s_specular_level.Focus()
    End Sub

    Private Sub s_gamma_MouseEnter(sender As Object, e As EventArgs) Handles s_gamma.MouseEnter
        s_gamma.Focus()
    End Sub

    Private Sub s_gray_level_MouseEnter(sender As Object, e As EventArgs) Handles s_gray_level.MouseEnter
        s_gray_level.Focus()
    End Sub
End Class
Imports System.IO

Public Class frmLighting
    Public lighting_terrain_texture, lighting_ambient, lighting_fog_level, gray_level As Single
    Public lighting_specular_level, gamma_level As Single
    Private Sub save_light_settings()
        If Not MAP_LOADED Then
            Return
        End If

        Try
            Dim f = File.Open(Application.StartupPath + "/light_settings/" + MAP_NAME_NO_PATH + ".light", FileMode.Create)
            Dim b_writer As New BinaryWriter(f)
            'the order: all as unsigend bytes
            '	1. texture level
            '	2. ambient
            '	3. gamma
            '	4. fog
            '	5. model level
            '	6. gray level
            '	7. extra
            b_writer.Write(s_terrain_texture_level.Value)
            b_writer.Write(s_terrain_ambient.Value)
            b_writer.Write(s_gamma.Value)
            b_writer.Write(s_fog_level.Value)
            b_writer.Write(s_specular_level.Value)
            b_writer.Write(s_gray_level.Value)
            Dim ext As Integer = 1
            b_writer.Write(ext) ' extras in case we want to add values later and dont wanna trash the settings.
            b_writer.Write(ext)
            ' no need to read the unused bytes but the must be saved before a reload or the form closes.
            b_writer.Dispose()
            f.Close()
        Catch ex As Exception
            MsgBox("I was unable to save the lighting settings!", MsgBoxStyle.Exclamation, "file Access Error...")
        End Try

    End Sub
    Private Function get_light_settings() As Boolean

        If File.Exists(Application.StartupPath + "/light_settings/" + MAP_NAME_NO_PATH + ".light") Then

            Dim f = File.Open(Application.StartupPath + "/light_settings/" + MAP_NAME_NO_PATH + ".light", FileMode.Open)
            Dim b_reader As New BinaryReader(f)
            'the order: all as integer
            '	1. texture level
            '	2. ambient
            '	3. gamma
            '	4. fog
            '	5. model level
            '	6. gray level
            '	7. extra 2
            s_terrain_texture_level.Value = b_reader.ReadInt32
            s_terrain_ambient.Value = b_reader.ReadInt32
            s_gamma.Value = b_reader.ReadInt32
            s_fog_level.Value = b_reader.ReadInt32
            s_specular_level.Value = b_reader.ReadInt32
            s_gray_level.Value = b_reader.ReadInt32
            ' no need to read the unused integers but they must be saved before loading a new map or the form closes.
            lighting_terrain_texture = s_terrain_texture_level.Value / 50.0!
            lighting_ambient = s_terrain_ambient.Value / 300.0!
            lighting_fog_level = s_fog_level.Value / 10000.0! ' yes 10,000
            lighting_specular_level = s_specular_level.Value / 100.0!
            gamma_level = (s_gamma.Value / 100) * 1.0!
            gray_level = 1.0 - (s_gray_level.Value / 100)
            b_reader.Dispose()
            f.Close()
            Return True
        Else
            lighting_terrain_texture = s_terrain_texture_level.Value / 50.0!
            lighting_ambient = s_terrain_ambient.Value / 300.0!
            lighting_fog_level = s_fog_level.Value / 10000.0! ' yes 10,000
            lighting_specular_level = s_specular_level.Value / 100.0!
            gamma_level = (s_gamma.Value / 100) * 1.0!
            gray_level = 1.0 - (s_gray_level.Value / 100)
            Return False

        End If

    End Function

    Private Sub frmLighting_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        save_light_settings()
        e.Cancel = True
        Me.Visible = False
    End Sub

    Private Sub frmLighting_Load(sender As Object, e As EventArgs) Handles Me.Load
        Me.Show()
        While Me.Visible = False
            Application.DoEvents()
        End While
        If Not _STARTED Then Return
        If Not get_light_settings() Then
            Try
                'if we dont have a settings file for this map, load the defaults in My.Settings
                s_terrain_ambient.Value = 50
                s_terrain_texture_level.Value = 50 'bump
                s_gamma.Value = 50
                s_fog_level.Value = 0
                s_gray_level.Value = 0
                s_specular_level.Value = 50 'specular

                get_light_settings()

            Catch ex As Exception

            End Try
        End If

        'frmMain.need_screen_update()
    End Sub

    Private Sub s_terrain_texture_level_MouseEnter(sender As Object, e As EventArgs) Handles s_terrain_texture_level.MouseEnter
        s_terrain_texture_level.Focus()
    End Sub

    Private Sub s_terrain_texture_level_Scroll(sender As Object, e As EventArgs) Handles s_terrain_texture_level.Scroll
        If Not _STARTED Then Return
        lighting_terrain_texture = s_terrain_texture_level.Value / 50.0!
        My.Settings.s_terrian_texture_level = s_terrain_texture_level.Value
        'frmMain.need_screen_update()
    End Sub

    Private Sub s_terrain_ambient_MouseEnter(sender As Object, e As EventArgs) Handles s_terrain_ambient.MouseEnter
        s_terrain_ambient.Focus()
    End Sub

    Private Sub s_terrain_ambient_Scroll(sender As Object, e As EventArgs) Handles s_terrain_ambient.Scroll
        If Not _STARTED Then Return
        lighting_ambient = s_terrain_ambient.Value / 300.0!
        My.Settings.s_terrain_ambient_level = s_terrain_ambient.Value
        'frmMain.need_screen_update()
    End Sub

    Private Sub s_fog_level_MouseEnter(sender As Object, e As EventArgs) Handles s_fog_level.MouseEnter
        s_fog_level.Focus()
    End Sub

    Private Sub s_fog_level_Scroll(sender As Object, e As EventArgs) Handles s_fog_level.Scroll
        If Not _STARTED Then Return
        lighting_fog_level = s_fog_level.Value / 10000.0! ' yes 10,000
        My.Settings.s_fog_level = s_fog_level.Value
        'frmMain.need_screen_update()
    End Sub

    Private Sub s_model_level_MouseEnter(sender As Object, e As EventArgs) Handles s_specular_level.MouseEnter
        s_specular_level.Focus()
    End Sub

    Private Sub s_model_level_Scroll(sender As Object, e As EventArgs) Handles s_specular_level.Scroll
        If Not _STARTED Then Return
        lighting_specular_level = s_specular_level.Value / 100.0!
        My.Settings.s_model_level = s_specular_level.Value
        'frmMain.need_screen_update()
    End Sub

    Private Sub s_gamma_MouseEnter(sender As Object, e As EventArgs) Handles s_gamma.MouseEnter
        s_gamma.Focus()
    End Sub

    Private Sub s_gamma_Scroll(sender As Object, e As EventArgs) Handles s_gamma.Scroll
        If Not _STARTED Then Return
        My.Settings.s_gamma = s_gamma.Value
        gamma_level = (s_gamma.Value / 100) * 1.0!
        'frmMain.need_screen_update()
    End Sub

    Private Sub s_gray_level_MouseEnter(sender As Object, e As EventArgs) Handles s_gray_level.MouseEnter
        s_gray_level.Focus()
    End Sub

    Private Sub s_gray_level_Scroll(sender As Object, e As EventArgs) Handles s_gray_level.Scroll
        If Not _STARTED Then Return
        My.Settings.s_gray_level = s_gray_level.Value
        gray_level = 1.0 - (s_gray_level.Value / 100)
        'frmMain.need_screen_update()
    End Sub
End Class
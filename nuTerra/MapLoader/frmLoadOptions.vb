Public Class frmLoadOptions

    Private Sub m_terrain_CheckedChanged(sender As Object, e As EventArgs) Handles m_terrain.CheckedChanged
        TERRAIN_BLOCK_LOADING = m_terrain.Checked
        If Not TERRAIN_LOADED And MAP_LOADED And TERRAIN_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_trees_CheckedChanged(sender As Object, e As EventArgs) Handles m_trees.CheckedChanged
        TREES_BLOCK_LOADING = m_trees.Checked
        If Not TREES_LOADED And MAP_LOADED And TREES_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_models_CheckedChanged(sender As Object, e As EventArgs) Handles m_models.CheckedChanged
        MODELS_BLOCK_LOADING = m_models.Checked
        If Not MODELS_LOADED And MAP_LOADED And MODELS_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_decals_CheckedChanged(sender As Object, e As EventArgs) Handles m_decals.CheckedChanged
        DECALS_BLOCK_LOADING = m_decals.Checked
        If Not DECALS_LOADED And MAP_LOADED And DECALS_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_water_CheckedChanged(sender As Object, e As EventArgs) Handles m_water.CheckedChanged
        WATER_BLOCK_LOADING = m_water.Checked
        If Not WATER_LOADED And MAP_LOADED And WATER_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_bases_CheckedChanged(sender As Object, e As EventArgs) Handles m_bases.CheckedChanged
        BASES_BLOCK_LOADING = m_bases.Checked
        If Not BASES_LOADED And MAP_LOADED And BASES_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

    Private Sub m_sky_CheckedChanged(sender As Object, e As EventArgs) Handles m_sky.CheckedChanged
        SKY_BLOCK_LOADING = m_sky.Checked
        If Not SKY_LOADED And MAP_LOADED And SKY_BLOCK_LOADING Then
            MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If
        If MAP_LOADED Then
            'frmMain.need_screen_update()
        End If
    End Sub

 
    Private Sub frmLoadOptions_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        e.Cancel = True
        My.Settings.Save()
        Me.Hide()
    End Sub
End Class
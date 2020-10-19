Public Class frmLoadOptions

    Private Sub m_terrain_CheckedChanged(sender As Object, e As EventArgs) Handles m_terrain.CheckedChanged
        DONT_BLOCK_TERRAIN = m_terrain.Checked
        If Not TERRAIN_LOADED And MAP_LOADED And DONT_BLOCK_TERRAIN Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_trees_CheckedChanged(sender As Object, e As EventArgs) Handles m_trees.CheckedChanged
        DONT_BLOCK_TREES = m_trees.Checked
        If Not TREES_LOADED And MAP_LOADED And DONT_BLOCK_TREES Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_models_CheckedChanged(sender As Object, e As EventArgs) Handles m_models.CheckedChanged
        DONT_BLOCK_MODELS = m_models.Checked
        If Not MODELS_LOADED And MAP_LOADED And DONT_BLOCK_MODELS Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_decals_CheckedChanged(sender As Object, e As EventArgs) Handles m_decals.CheckedChanged
        DONT_BLOCK_DECALS = m_decals.Checked
        If Not DECALS_LOADED And MAP_LOADED And DONT_BLOCK_DECALS Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_water_CheckedChanged(sender As Object, e As EventArgs) Handles m_water.CheckedChanged
        DONT_BLOCK_WATER = m_water.Checked
        If Not WATER_LOADED And MAP_LOADED And DONT_BLOCK_WATER Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_bases_CheckedChanged(sender As Object, e As EventArgs) Handles m_bases.CheckedChanged
        DONT_BLOCK_BASES = m_bases.Checked
        If Not BASES_LOADED And MAP_LOADED And DONT_BLOCK_BASES Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

    Private Sub m_sky_CheckedChanged(sender As Object, e As EventArgs) Handles m_sky.CheckedChanged
        DONT_BLOCK_SKY = m_sky.Checked
        If Not SKY_LOADED And MAP_LOADED And DONT_BLOCK_SKY Then
            'MsgBox("You can change this setting but it was never loaded." + vbCrLf + "I can't draw it. It does not exist.", MsgBoxStyle.Exclamation, "Warning!")
        End If

    End Sub

 
    Private Sub frmLoadOptions_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        e.Cancel = True
        My.Settings.Save()
        Me.Hide()
    End Sub

    Private Sub frmLoadOptions_VisibleChanged(sender As Object, e As EventArgs) Handles Me.VisibleChanged
        Me.Location = frmMain.Location
    End Sub
End Class
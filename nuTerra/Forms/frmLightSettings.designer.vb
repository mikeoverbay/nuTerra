<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmLightSettings
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.Label5 = New System.Windows.Forms.Label()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.Label4 = New System.Windows.Forms.Label()
        Me.Label3 = New System.Windows.Forms.Label()
        Me.Label6 = New System.Windows.Forms.Label()
        Me.s_specular_level = New System.Windows.Forms.TrackBar()
        Me.s_gray_level = New System.Windows.Forms.TrackBar()
        Me.s_gamma = New System.Windows.Forms.TrackBar()
        Me.s_fog_level = New System.Windows.Forms.TrackBar()
        Me.s_terrain_ambient = New System.Windows.Forms.TrackBar()
        Me.s_terrain_texture_level = New System.Windows.Forms.TrackBar()
        CType(Me.s_specular_level, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.s_gray_level, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.s_gamma, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.s_fog_level, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.s_terrain_ambient, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.s_terrain_texture_level, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Label5
        '
        Me.Label5.AutoSize = True
        Me.Label5.ForeColor = System.Drawing.Color.Silver
        Me.Label5.Location = New System.Drawing.Point(97, 160)
        Me.Label5.Name = "Label5"
        Me.Label5.Size = New System.Drawing.Size(43, 26)
        Me.Label5.TabIndex = 4
        Me.Label5.Text = "Gamma" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level"
        Me.Label5.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.ForeColor = System.Drawing.Color.Silver
        Me.Label2.Location = New System.Drawing.Point(48, 160)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(45, 26)
        Me.Label2.TabIndex = 2
        Me.Label2.Text = "Ambient" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level"
        Me.Label2.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.ForeColor = System.Drawing.Color.Silver
        Me.Label1.Location = New System.Drawing.Point(4, 160)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(34, 26)
        Me.Label1.TabIndex = 1
        Me.Label1.Text = "Bright" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level"
        Me.Label1.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        'Label4
        '
        Me.Label4.AutoSize = True
        Me.Label4.ForeColor = System.Drawing.Color.Silver
        Me.Label4.Location = New System.Drawing.Point(149, 160)
        Me.Label4.Name = "Label4"
        Me.Label4.Size = New System.Drawing.Size(33, 26)
        Me.Label4.TabIndex = 1
        Me.Label4.Text = "Fog" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level"
        Me.Label4.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        'Label3
        '
        Me.Label3.AutoSize = True
        Me.Label3.ForeColor = System.Drawing.Color.Silver
        Me.Label3.Location = New System.Drawing.Point(252, 160)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(33, 26)
        Me.Label3.TabIndex = 1
        Me.Label3.Text = "Spec" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10)
        Me.Label3.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        'Label6
        '
        Me.Label6.AutoSize = True
        Me.Label6.ForeColor = System.Drawing.Color.Silver
        Me.Label6.Location = New System.Drawing.Point(201, 160)
        Me.Label6.Name = "Label6"
        Me.Label6.Size = New System.Drawing.Size(33, 26)
        Me.Label6.TabIndex = 1
        Me.Label6.Text = "Gray" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Level"
        Me.Label6.TextAlign = System.Drawing.ContentAlignment.TopCenter
        '
        's_specular_level
        '
        Me.s_specular_level.AutoSize = False
        Me.s_specular_level.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_specular_level.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_specular_level.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Specular_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_specular_level.LargeChange = 1
        Me.s_specular_level.Location = New System.Drawing.Point(248, 2)
        Me.s_specular_level.Maximum = 100
        Me.s_specular_level.Name = "s_specular_level"
        Me.s_specular_level.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_specular_level.Size = New System.Drawing.Size(40, 155)
        Me.s_specular_level.TabIndex = 0
        Me.s_specular_level.TickFrequency = 10
        Me.s_specular_level.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_specular_level.Value = Global.nuTerra.My.MySettings.Default.Specular_level
        '
        's_gray_level
        '
        Me.s_gray_level.AutoSize = False
        Me.s_gray_level.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_gray_level.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_gray_level.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Gray_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_gray_level.LargeChange = 1
        Me.s_gray_level.Location = New System.Drawing.Point(197, 2)
        Me.s_gray_level.Maximum = 100
        Me.s_gray_level.Name = "s_gray_level"
        Me.s_gray_level.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_gray_level.Size = New System.Drawing.Size(40, 155)
        Me.s_gray_level.TabIndex = 0
        Me.s_gray_level.TickFrequency = 10
        Me.s_gray_level.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_gray_level.Value = Global.nuTerra.My.MySettings.Default.Gray_level
        '
        's_gamma
        '
        Me.s_gamma.AutoSize = False
        Me.s_gamma.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_gamma.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_gamma.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Gamma_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_gamma.LargeChange = 1
        Me.s_gamma.Location = New System.Drawing.Point(100, 2)
        Me.s_gamma.Maximum = 100
        Me.s_gamma.Name = "s_gamma"
        Me.s_gamma.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_gamma.Size = New System.Drawing.Size(40, 155)
        Me.s_gamma.TabIndex = 5
        Me.s_gamma.TickFrequency = 10
        Me.s_gamma.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_gamma.Value = Global.nuTerra.My.MySettings.Default.Gamma_level
        '
        's_fog_level
        '
        Me.s_fog_level.AutoSize = False
        Me.s_fog_level.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_fog_level.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_fog_level.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Fog_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_fog_level.LargeChange = 1
        Me.s_fog_level.Location = New System.Drawing.Point(146, 2)
        Me.s_fog_level.Maximum = 100
        Me.s_fog_level.Name = "s_fog_level"
        Me.s_fog_level.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_fog_level.Size = New System.Drawing.Size(40, 155)
        Me.s_fog_level.TabIndex = 0
        Me.s_fog_level.TickFrequency = 10
        Me.s_fog_level.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_fog_level.Value = Global.nuTerra.My.MySettings.Default.Fog_level
        '
        's_terrain_ambient
        '
        Me.s_terrain_ambient.AutoSize = False
        Me.s_terrain_ambient.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_terrain_ambient.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_terrain_ambient.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Ambient_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_terrain_ambient.LargeChange = 1
        Me.s_terrain_ambient.Location = New System.Drawing.Point(51, 2)
        Me.s_terrain_ambient.Maximum = 100
        Me.s_terrain_ambient.Name = "s_terrain_ambient"
        Me.s_terrain_ambient.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_terrain_ambient.Size = New System.Drawing.Size(40, 155)
        Me.s_terrain_ambient.TabIndex = 3
        Me.s_terrain_ambient.TickFrequency = 10
        Me.s_terrain_ambient.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_terrain_ambient.Value = Global.nuTerra.My.MySettings.Default.Ambient_level
        '
        's_terrain_texture_level
        '
        Me.s_terrain_texture_level.AutoSize = False
        Me.s_terrain_texture_level.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.s_terrain_texture_level.Cursor = System.Windows.Forms.Cursors.Hand
        Me.s_terrain_texture_level.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "Bright_level", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.s_terrain_texture_level.LargeChange = 1
        Me.s_terrain_texture_level.Location = New System.Drawing.Point(2, 2)
        Me.s_terrain_texture_level.Maximum = 100
        Me.s_terrain_texture_level.Name = "s_terrain_texture_level"
        Me.s_terrain_texture_level.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.s_terrain_texture_level.Size = New System.Drawing.Size(40, 155)
        Me.s_terrain_texture_level.TabIndex = 0
        Me.s_terrain_texture_level.TickFrequency = 10
        Me.s_terrain_texture_level.TickStyle = System.Windows.Forms.TickStyle.Both
        Me.s_terrain_texture_level.Value = Global.nuTerra.My.MySettings.Default.Bright_level
        '
        'frmLighting
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(292, 197)
        Me.Controls.Add(Me.Label3)
        Me.Controls.Add(Me.Label6)
        Me.Controls.Add(Me.s_specular_level)
        Me.Controls.Add(Me.Label4)
        Me.Controls.Add(Me.s_gray_level)
        Me.Controls.Add(Me.s_gamma)
        Me.Controls.Add(Me.s_fog_level)
        Me.Controls.Add(Me.Label5)
        Me.Controls.Add(Me.s_terrain_ambient)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.s_terrain_texture_level)
        Me.ForeColor = System.Drawing.Color.DarkRed
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        Me.MaximizeBox = False
        Me.Name = "frmLighting"
        Me.ShowInTaskbar = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Lighting and fog settings"
        Me.TopMost = True
        CType(Me.s_specular_level, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.s_gray_level, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.s_gamma, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.s_fog_level, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.s_terrain_ambient, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.s_terrain_texture_level, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents s_terrain_texture_level As System.Windows.Forms.TrackBar
    Friend WithEvents s_terrain_ambient As System.Windows.Forms.TrackBar
    Friend WithEvents Label2 As System.Windows.Forms.Label
    Friend WithEvents Label1 As System.Windows.Forms.Label
    Friend WithEvents Label4 As System.Windows.Forms.Label
    Friend WithEvents s_fog_level As System.Windows.Forms.TrackBar
    Friend WithEvents Label3 As System.Windows.Forms.Label
    Friend WithEvents s_gamma As System.Windows.Forms.TrackBar
    Friend WithEvents Label5 As System.Windows.Forms.Label
    Friend WithEvents Label6 As System.Windows.Forms.Label
    Friend WithEvents s_gray_level As System.Windows.Forms.TrackBar
    Friend WithEvents s_specular_level As System.Windows.Forms.TrackBar
End Class

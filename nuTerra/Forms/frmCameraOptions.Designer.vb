<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmCameraOptions
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmCameraOptions))
        Me.Label1 = New System.Windows.Forms.Label()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.Label3 = New System.Windows.Forms.Label()
        Me.Label4 = New System.Windows.Forms.Label()
        Me.NumericUpDown4_speed = New System.Windows.Forms.NumericUpDown()
        Me.NumericUpDown3 = New System.Windows.Forms.NumericUpDown()
        Me.NumericUpDown2 = New System.Windows.Forms.NumericUpDown()
        Me.FoVNumericUpDown = New System.Windows.Forms.NumericUpDown()
        Me.ResetButton = New System.Windows.Forms.Button()
        CType(Me.NumericUpDown4_speed, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.NumericUpDown3, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.NumericUpDown2, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.FoVNumericUpDown, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.ForeColor = System.Drawing.Color.White
        Me.Label1.Location = New System.Drawing.Point(138, 14)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(26, 13)
        Me.Label1.TabIndex = 1
        Me.Label1.Text = "FoV"
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.ForeColor = System.Drawing.Color.White
        Me.Label2.Location = New System.Drawing.Point(138, 41)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(30, 13)
        Me.Label2.TabIndex = 3
        Me.Label2.Text = "Near"
        '
        'Label3
        '
        Me.Label3.AutoSize = True
        Me.Label3.ForeColor = System.Drawing.Color.White
        Me.Label3.Location = New System.Drawing.Point(138, 67)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(22, 13)
        Me.Label3.TabIndex = 5
        Me.Label3.Text = "Far"
        '
        'Label4
        '
        Me.Label4.AutoSize = True
        Me.Label4.ForeColor = System.Drawing.Color.White
        Me.Label4.Location = New System.Drawing.Point(138, 93)
        Me.Label4.Name = "Label4"
        Me.Label4.Size = New System.Drawing.Size(38, 13)
        Me.Label4.TabIndex = 7
        Me.Label4.Text = "Speed"
        '
        'NumericUpDown4_speed
        '
        Me.NumericUpDown4_speed.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "speed", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.NumericUpDown4_speed.DecimalPlaces = 2
        Me.NumericUpDown4_speed.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        Me.NumericUpDown4_speed.Location = New System.Drawing.Point(12, 91)
        Me.NumericUpDown4_speed.Maximum = New Decimal(New Integer() {10, 0, 0, 65536})
        Me.NumericUpDown4_speed.Minimum = New Decimal(New Integer() {1, 0, 0, 65536})
        Me.NumericUpDown4_speed.Name = "NumericUpDown4_speed"
        Me.NumericUpDown4_speed.Size = New System.Drawing.Size(120, 20)
        Me.NumericUpDown4_speed.TabIndex = 6
        Me.NumericUpDown4_speed.Value = Global.nuTerra.My.MySettings.Default.speed
        '
        'NumericUpDown3
        '
        Me.NumericUpDown3.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "far", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.NumericUpDown3.DecimalPlaces = 2
        Me.NumericUpDown3.Increment = New Decimal(New Integer() {2, 0, 0, 0})
        Me.NumericUpDown3.Location = New System.Drawing.Point(12, 65)
        Me.NumericUpDown3.Maximum = New Decimal(New Integer() {50000, 0, 0, 0})
        Me.NumericUpDown3.Minimum = New Decimal(New Integer() {1000, 0, 0, 0})
        Me.NumericUpDown3.Name = "NumericUpDown3"
        Me.NumericUpDown3.Size = New System.Drawing.Size(120, 20)
        Me.NumericUpDown3.TabIndex = 4
        Me.NumericUpDown3.Value = Global.nuTerra.My.MySettings.Default.far
        '
        'NumericUpDown2
        '
        Me.NumericUpDown2.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "near", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.NumericUpDown2.DecimalPlaces = 2
        Me.NumericUpDown2.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        Me.NumericUpDown2.Location = New System.Drawing.Point(12, 39)
        Me.NumericUpDown2.Maximum = New Decimal(New Integer() {5000, 0, 0, 0})
        Me.NumericUpDown2.Minimum = New Decimal(New Integer() {5, 0, 0, 131072})
        Me.NumericUpDown2.Name = "NumericUpDown2"
        Me.NumericUpDown2.Size = New System.Drawing.Size(120, 20)
        Me.NumericUpDown2.TabIndex = 2
        Me.NumericUpDown2.Value = Global.nuTerra.My.MySettings.Default.near
        '
        'FoVNumericUpDown
        '
        Me.FoVNumericUpDown.DataBindings.Add(New System.Windows.Forms.Binding("Value", Global.nuTerra.My.MySettings.Default, "fov", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.FoVNumericUpDown.DecimalPlaces = 2
        Me.FoVNumericUpDown.Location = New System.Drawing.Point(12, 12)
        Me.FoVNumericUpDown.Maximum = New Decimal(New Integer() {179, 0, 0, 0})
        Me.FoVNumericUpDown.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        Me.FoVNumericUpDown.Name = "FoVNumericUpDown"
        Me.FoVNumericUpDown.Size = New System.Drawing.Size(120, 20)
        Me.FoVNumericUpDown.TabIndex = 0
        Me.FoVNumericUpDown.Value = Global.nuTerra.My.MySettings.Default.fov
        '
        'ResetButton
        '
        Me.ResetButton.Location = New System.Drawing.Point(13, 118)
        Me.ResetButton.Name = "ResetButton"
        Me.ResetButton.Size = New System.Drawing.Size(155, 23)
        Me.ResetButton.TabIndex = 8
        Me.ResetButton.Text = "Reset"
        Me.ResetButton.UseVisualStyleBackColor = True
        '
        'frmCameraOptions
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(180, 149)
        Me.Controls.Add(Me.ResetButton)
        Me.Controls.Add(Me.Label4)
        Me.Controls.Add(Me.NumericUpDown4_speed)
        Me.Controls.Add(Me.Label3)
        Me.Controls.Add(Me.NumericUpDown3)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.NumericUpDown2)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.FoVNumericUpDown)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.Name = "frmCameraOptions"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Camera Options"
        Me.TopMost = True
        CType(Me.NumericUpDown4_speed, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.NumericUpDown3, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.NumericUpDown2, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.FoVNumericUpDown, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents FoVNumericUpDown As NumericUpDown
    Friend WithEvents Label1 As Label
    Friend WithEvents NumericUpDown2 As NumericUpDown
    Friend WithEvents Label2 As Label
    Friend WithEvents NumericUpDown3 As NumericUpDown
    Friend WithEvents Label3 As Label
    Friend WithEvents NumericUpDown4_speed As NumericUpDown
    Friend WithEvents Label4 As Label
    Friend WithEvents ResetButton As Button
End Class

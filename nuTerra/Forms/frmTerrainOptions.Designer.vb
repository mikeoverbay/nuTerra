<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmTerrainOptions
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
        Me.StartNumericUpDown = New System.Windows.Forms.NumericUpDown()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.NumericUpDown1 = New System.Windows.Forms.NumericUpDown()
        Me.Label2 = New System.Windows.Forms.Label()
        CType(Me.StartNumericUpDown, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.NumericUpDown1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'StartNumericUpDown
        '
        Me.StartNumericUpDown.DecimalPlaces = 2
        Me.StartNumericUpDown.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        Me.StartNumericUpDown.Location = New System.Drawing.Point(12, 12)
        Me.StartNumericUpDown.Maximum = New Decimal(New Integer() {500, 0, 0, 0})
        Me.StartNumericUpDown.Name = "StartNumericUpDown"
        Me.StartNumericUpDown.Size = New System.Drawing.Size(120, 20)
        Me.StartNumericUpDown.TabIndex = 0
        Me.StartNumericUpDown.Value = New Decimal(New Integer() {75, 0, 0, 0})
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.ForeColor = System.Drawing.Color.White
        Me.Label1.Location = New System.Drawing.Point(138, 14)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(29, 13)
        Me.Label1.TabIndex = 1
        Me.Label1.Text = "Start"
        '
        'NumericUpDown1
        '
        Me.NumericUpDown1.DecimalPlaces = 2
        Me.NumericUpDown1.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        Me.NumericUpDown1.Location = New System.Drawing.Point(12, 43)
        Me.NumericUpDown1.Maximum = New Decimal(New Integer() {500, 0, 0, 0})
        Me.NumericUpDown1.Name = "NumericUpDown1"
        Me.NumericUpDown1.Size = New System.Drawing.Size(120, 20)
        Me.NumericUpDown1.TabIndex = 2
        Me.NumericUpDown1.Value = New Decimal(New Integer() {200, 0, 0, 0})
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.ForeColor = System.Drawing.Color.White
        Me.Label2.Location = New System.Drawing.Point(138, 45)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(26, 13)
        Me.Label2.TabIndex = 3
        Me.Label2.Text = "End"
        '
        'frmTerrainOptions
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(181, 75)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.NumericUpDown1)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.StartNumericUpDown)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        Me.Name = "frmTerrainOptions"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Terrain Options"
        Me.TopMost = True
        CType(Me.StartNumericUpDown, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.NumericUpDown1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents StartNumericUpDown As NumericUpDown
    Friend WithEvents Label1 As Label
    Friend WithEvents NumericUpDown1 As NumericUpDown
    Friend WithEvents Label2 As Label
End Class

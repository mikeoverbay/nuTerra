<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmScreenCap
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmScreenCap))
        Me.GroupBox1 = New System.Windows.Forms.GroupBox()
        Me.rb_jpg = New System.Windows.Forms.RadioButton()
        Me.rb_png = New System.Windows.Forms.RadioButton()
        Me.save_btn = New System.Windows.Forms.Button()
        Me.Save_Dialog = New System.Windows.Forms.SaveFileDialog()
        Me.GroupBox1.SuspendLayout()
        Me.SuspendLayout()
        '
        'GroupBox1
        '
        Me.GroupBox1.Controls.Add(Me.rb_jpg)
        Me.GroupBox1.Controls.Add(Me.rb_png)
        Me.GroupBox1.ForeColor = System.Drawing.SystemColors.ActiveCaption
        Me.GroupBox1.Location = New System.Drawing.Point(18, 12)
        Me.GroupBox1.Name = "GroupBox1"
        Me.GroupBox1.Size = New System.Drawing.Size(73, 69)
        Me.GroupBox1.TabIndex = 0
        Me.GroupBox1.TabStop = False
        Me.GroupBox1.Text = "Format"
        '
        'rb_jpg
        '
        Me.rb_jpg.AutoSize = True
        Me.rb_jpg.Location = New System.Drawing.Point(6, 43)
        Me.rb_jpg.Name = "rb_jpg"
        Me.rb_jpg.Size = New System.Drawing.Size(45, 17)
        Me.rb_jpg.TabIndex = 1
        Me.rb_jpg.Text = "JPG"
        Me.rb_jpg.UseVisualStyleBackColor = True
        '
        'rb_png
        '
        Me.rb_png.AutoSize = True
        Me.rb_png.Checked = True
        Me.rb_png.Location = New System.Drawing.Point(7, 20)
        Me.rb_png.Name = "rb_png"
        Me.rb_png.Size = New System.Drawing.Size(48, 17)
        Me.rb_png.TabIndex = 0
        Me.rb_png.TabStop = True
        Me.rb_png.Text = "PNG"
        Me.rb_png.UseVisualStyleBackColor = True
        '
        'save_btn
        '
        Me.save_btn.Location = New System.Drawing.Point(16, 88)
        Me.save_btn.Name = "save_btn"
        Me.save_btn.Size = New System.Drawing.Size(75, 23)
        Me.save_btn.TabIndex = 1
        Me.save_btn.Text = "Capture"
        Me.save_btn.UseVisualStyleBackColor = True
        '
        'frmScreenCap
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(33, Byte), Integer), CType(CType(33, Byte), Integer), CType(CType(33, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(110, 123)
        Me.Controls.Add(Me.save_btn)
        Me.Controls.Add(Me.GroupBox1)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.Name = "frmScreenCap"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Screen Capture"
        Me.TopMost = True
        Me.GroupBox1.ResumeLayout(False)
        Me.GroupBox1.PerformLayout()
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents GroupBox1 As System.Windows.Forms.GroupBox
    Friend WithEvents rb_jpg As System.Windows.Forms.RadioButton
    Friend WithEvents rb_png As System.Windows.Forms.RadioButton
    Friend WithEvents save_btn As System.Windows.Forms.Button
    Friend WithEvents Save_Dialog As System.Windows.Forms.SaveFileDialog
End Class

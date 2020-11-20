Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmGbufferViewer
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmGbufferViewer))
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.GroupBox1 = New System.Windows.Forms.GroupBox()
        Me.quater_scale = New System.Windows.Forms.RadioButton()
        Me.half_scale = New System.Windows.Forms.RadioButton()
        Me.full_scale = New System.Windows.Forms.RadioButton()
        Me.GroupBox2 = New System.Windows.Forms.GroupBox()
        Me.b_aux = New System.Windows.Forms.RadioButton()
        Me.b_flags = New System.Windows.Forms.RadioButton()
        Me.b_normal = New System.Windows.Forms.RadioButton()
        Me.b_position = New System.Windows.Forms.RadioButton()
        Me.b_color = New System.Windows.Forms.RadioButton()
        Me.b_depth = New System.Windows.Forms.RadioButton()
        Me.w_label = New System.Windows.Forms.Label()
        Me.h_label = New System.Windows.Forms.Label()
        Me.r_cb = New System.Windows.Forms.CheckBox()
        Me.g_cb = New System.Windows.Forms.CheckBox()
        Me.b_cb = New System.Windows.Forms.CheckBox()
        Me.a_cb = New System.Windows.Forms.CheckBox()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.cb_alpha_enable = New System.Windows.Forms.CheckBox()
        Me.GroupBox1.SuspendLayout()
        Me.GroupBox2.SuspendLayout()
        Me.SuspendLayout()
        '
        'Panel1
        '
        Me.Panel1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Panel1.BackColor = System.Drawing.Color.Black
        Me.Panel1.Location = New System.Drawing.Point(1, 0)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(510, 375)
        Me.Panel1.TabIndex = 0
        '
        'GroupBox1
        '
        Me.GroupBox1.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GroupBox1.Controls.Add(Me.quater_scale)
        Me.GroupBox1.Controls.Add(Me.half_scale)
        Me.GroupBox1.Controls.Add(Me.full_scale)
        Me.GroupBox1.ForeColor = System.Drawing.Color.White
        Me.GroupBox1.Location = New System.Drawing.Point(517, 12)
        Me.GroupBox1.Name = "GroupBox1"
        Me.GroupBox1.Size = New System.Drawing.Size(60, 113)
        Me.GroupBox1.TabIndex = 1
        Me.GroupBox1.TabStop = False
        Me.GroupBox1.Text = "Size"
        '
        'quater_scale
        '
        Me.quater_scale.Appearance = System.Windows.Forms.Appearance.Button
        Me.quater_scale.Checked = True
        Me.quater_scale.ForeColor = System.Drawing.Color.Black
        Me.quater_scale.Location = New System.Drawing.Point(7, 77)
        Me.quater_scale.Name = "quater_scale"
        Me.quater_scale.Size = New System.Drawing.Size(45, 23)
        Me.quater_scale.TabIndex = 2
        Me.quater_scale.TabStop = True
        Me.quater_scale.Tag = "0.25"
        Me.quater_scale.Text = "1/4"
        Me.quater_scale.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.quater_scale.UseVisualStyleBackColor = True
        '
        'half_scale
        '
        Me.half_scale.Appearance = System.Windows.Forms.Appearance.Button
        Me.half_scale.ForeColor = System.Drawing.Color.Black
        Me.half_scale.Location = New System.Drawing.Point(7, 48)
        Me.half_scale.Name = "half_scale"
        Me.half_scale.Size = New System.Drawing.Size(45, 23)
        Me.half_scale.TabIndex = 1
        Me.half_scale.TabStop = True
        Me.half_scale.Tag = "0.5"
        Me.half_scale.Text = "1/2"
        Me.half_scale.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.half_scale.UseVisualStyleBackColor = True
        '
        'full_scale
        '
        Me.full_scale.Appearance = System.Windows.Forms.Appearance.Button
        Me.full_scale.ForeColor = System.Drawing.Color.Black
        Me.full_scale.Location = New System.Drawing.Point(7, 19)
        Me.full_scale.Name = "full_scale"
        Me.full_scale.Size = New System.Drawing.Size(45, 23)
        Me.full_scale.TabIndex = 0
        Me.full_scale.Tag = "1.0"
        Me.full_scale.Text = "Full"
        Me.full_scale.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.full_scale.UseVisualStyleBackColor = True
        '
        'GroupBox2
        '
        Me.GroupBox2.Controls.Add(Me.b_aux)
        Me.GroupBox2.Controls.Add(Me.b_flags)
        Me.GroupBox2.Controls.Add(Me.b_normal)
        Me.GroupBox2.Controls.Add(Me.b_position)
        Me.GroupBox2.Controls.Add(Me.b_color)
        Me.GroupBox2.Controls.Add(Me.b_depth)
        Me.GroupBox2.Dock = System.Windows.Forms.DockStyle.Bottom
        Me.GroupBox2.ForeColor = System.Drawing.Color.White
        Me.GroupBox2.Location = New System.Drawing.Point(0, 381)
        Me.GroupBox2.Name = "GroupBox2"
        Me.GroupBox2.Size = New System.Drawing.Size(589, 41)
        Me.GroupBox2.TabIndex = 2
        Me.GroupBox2.TabStop = False
        Me.GroupBox2.Text = "Selected Image"
        '
        'b_aux
        '
        Me.b_aux.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_aux.ForeColor = System.Drawing.Color.Black
        Me.b_aux.Location = New System.Drawing.Point(391, 14)
        Me.b_aux.Name = "b_aux"
        Me.b_aux.Size = New System.Drawing.Size(70, 23)
        Me.b_aux.TabIndex = 6
        Me.b_aux.Tag = "6"
        Me.b_aux.Text = "gAuxColor"
        Me.b_aux.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_aux.UseVisualStyleBackColor = True
        '
        'b_flags
        '
        Me.b_flags.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_flags.ForeColor = System.Drawing.Color.Black
        Me.b_flags.Location = New System.Drawing.Point(315, 14)
        Me.b_flags.Name = "b_flags"
        Me.b_flags.Size = New System.Drawing.Size(70, 23)
        Me.b_flags.TabIndex = 4
        Me.b_flags.Tag = "5"
        Me.b_flags.Text = "Flags"
        Me.b_flags.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_flags.UseVisualStyleBackColor = True
        '
        'b_normal
        '
        Me.b_normal.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_normal.ForeColor = System.Drawing.Color.Black
        Me.b_normal.Location = New System.Drawing.Point(239, 14)
        Me.b_normal.Name = "b_normal"
        Me.b_normal.Size = New System.Drawing.Size(70, 23)
        Me.b_normal.TabIndex = 3
        Me.b_normal.Tag = "4"
        Me.b_normal.Text = "Normals"
        Me.b_normal.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_normal.UseVisualStyleBackColor = True
        '
        'b_position
        '
        Me.b_position.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_position.ForeColor = System.Drawing.Color.Black
        Me.b_position.Location = New System.Drawing.Point(163, 14)
        Me.b_position.Name = "b_position"
        Me.b_position.Size = New System.Drawing.Size(70, 23)
        Me.b_position.TabIndex = 2
        Me.b_position.Tag = "3"
        Me.b_position.Text = "Position"
        Me.b_position.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_position.UseVisualStyleBackColor = True
        '
        'b_color
        '
        Me.b_color.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_color.ForeColor = System.Drawing.Color.Black
        Me.b_color.Location = New System.Drawing.Point(87, 14)
        Me.b_color.Name = "b_color"
        Me.b_color.Size = New System.Drawing.Size(70, 23)
        Me.b_color.TabIndex = 1
        Me.b_color.Tag = "2"
        Me.b_color.Text = "Colors"
        Me.b_color.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_color.UseVisualStyleBackColor = True
        '
        'b_depth
        '
        Me.b_depth.Appearance = System.Windows.Forms.Appearance.Button
        Me.b_depth.Checked = True
        Me.b_depth.ForeColor = System.Drawing.Color.Black
        Me.b_depth.Location = New System.Drawing.Point(11, 14)
        Me.b_depth.Name = "b_depth"
        Me.b_depth.Size = New System.Drawing.Size(70, 23)
        Me.b_depth.TabIndex = 0
        Me.b_depth.TabStop = True
        Me.b_depth.Tag = "1"
        Me.b_depth.Text = "Depth"
        Me.b_depth.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.b_depth.UseVisualStyleBackColor = True
        '
        'w_label
        '
        Me.w_label.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.w_label.AutoSize = True
        Me.w_label.ForeColor = System.Drawing.Color.Silver
        Me.w_label.Location = New System.Drawing.Point(514, 201)
        Me.w_label.Name = "w_label"
        Me.w_label.Size = New System.Drawing.Size(38, 13)
        Me.w_label.TabIndex = 3
        Me.w_label.Text = "Width:"
        '
        'h_label
        '
        Me.h_label.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.h_label.AutoSize = True
        Me.h_label.ForeColor = System.Drawing.Color.Silver
        Me.h_label.Location = New System.Drawing.Point(511, 219)
        Me.h_label.Name = "h_label"
        Me.h_label.Size = New System.Drawing.Size(41, 13)
        Me.h_label.TabIndex = 4
        Me.h_label.Text = "Height:"
        '
        'r_cb
        '
        Me.r_cb.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.r_cb.AutoSize = True
        Me.r_cb.Checked = True
        Me.r_cb.CheckState = System.Windows.Forms.CheckState.Checked
        Me.r_cb.Font = New System.Drawing.Font("Microsoft Sans Serif", 11.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.r_cb.ForeColor = System.Drawing.Color.FromArgb(CType(CType(192, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.r_cb.Location = New System.Drawing.Point(518, 269)
        Me.r_cb.Name = "r_cb"
        Me.r_cb.Size = New System.Drawing.Size(39, 22)
        Me.r_cb.TabIndex = 5
        Me.r_cb.Tag = "0"
        Me.r_cb.Text = "R"
        Me.r_cb.UseVisualStyleBackColor = True
        '
        'g_cb
        '
        Me.g_cb.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.g_cb.AutoSize = True
        Me.g_cb.Checked = True
        Me.g_cb.CheckState = System.Windows.Forms.CheckState.Checked
        Me.g_cb.Font = New System.Drawing.Font("Microsoft Sans Serif", 11.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.g_cb.ForeColor = System.Drawing.Color.FromArgb(CType(CType(0, Byte), Integer), CType(CType(192, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.g_cb.Location = New System.Drawing.Point(518, 297)
        Me.g_cb.Name = "g_cb"
        Me.g_cb.Size = New System.Drawing.Size(40, 22)
        Me.g_cb.TabIndex = 7
        Me.g_cb.Tag = "1"
        Me.g_cb.Text = "G"
        Me.g_cb.UseVisualStyleBackColor = True
        '
        'b_cb
        '
        Me.b_cb.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.b_cb.AutoSize = True
        Me.b_cb.Checked = True
        Me.b_cb.CheckState = System.Windows.Forms.CheckState.Checked
        Me.b_cb.Font = New System.Drawing.Font("Microsoft Sans Serif", 11.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.b_cb.ForeColor = System.Drawing.Color.Blue
        Me.b_cb.Location = New System.Drawing.Point(517, 325)
        Me.b_cb.Name = "b_cb"
        Me.b_cb.Size = New System.Drawing.Size(38, 22)
        Me.b_cb.TabIndex = 8
        Me.b_cb.Tag = "2"
        Me.b_cb.Text = "B"
        Me.b_cb.UseVisualStyleBackColor = True
        '
        'a_cb
        '
        Me.a_cb.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.a_cb.AutoSize = True
        Me.a_cb.Checked = True
        Me.a_cb.CheckState = System.Windows.Forms.CheckState.Checked
        Me.a_cb.Font = New System.Drawing.Font("Microsoft Sans Serif", 11.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.a_cb.ForeColor = System.Drawing.Color.White
        Me.a_cb.Location = New System.Drawing.Point(518, 353)
        Me.a_cb.Name = "a_cb"
        Me.a_cb.Size = New System.Drawing.Size(37, 22)
        Me.a_cb.TabIndex = 9
        Me.a_cb.Tag = "3"
        Me.a_cb.Text = "A"
        Me.a_cb.UseVisualStyleBackColor = True
        '
        'Label1
        '
        Me.Label1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Label1.AutoSize = True
        Me.Label1.ForeColor = System.Drawing.SystemColors.ActiveCaption
        Me.Label1.Location = New System.Drawing.Point(517, 253)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(16, 13)
        Me.Label1.TabIndex = 10
        Me.Label1.Text = "   "
        '
        'cb_alpha_enable
        '
        Me.cb_alpha_enable.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.cb_alpha_enable.Appearance = System.Windows.Forms.Appearance.Button
        Me.cb_alpha_enable.AutoSize = True
        Me.cb_alpha_enable.ForeColor = System.Drawing.Color.Black
        Me.cb_alpha_enable.Location = New System.Drawing.Point(525, 131)
        Me.cb_alpha_enable.Name = "cb_alpha_enable"
        Me.cb_alpha_enable.Size = New System.Drawing.Size(44, 23)
        Me.cb_alpha_enable.TabIndex = 11
        Me.cb_alpha_enable.Text = "Alpha"
        Me.cb_alpha_enable.UseVisualStyleBackColor = True
        '
        'frmGbufferViewer
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(589, 422)
        Me.Controls.Add(Me.cb_alpha_enable)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.a_cb)
        Me.Controls.Add(Me.b_cb)
        Me.Controls.Add(Me.g_cb)
        Me.Controls.Add(Me.r_cb)
        Me.Controls.Add(Me.h_label)
        Me.Controls.Add(Me.w_label)
        Me.Controls.Add(Me.GroupBox2)
        Me.Controls.Add(Me.GroupBox1)
        Me.Controls.Add(Me.Panel1)
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.MinimumSize = New System.Drawing.Size(480, 320)
        Me.Name = "frmGbufferViewer"
        Me.Text = "G-Buffer Texture Viewer"
        Me.TopMost = True
        Me.GroupBox1.ResumeLayout(False)
        Me.GroupBox2.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents Panel1 As System.Windows.Forms.Panel
    Friend WithEvents GroupBox1 As System.Windows.Forms.GroupBox
    Friend WithEvents quater_scale As System.Windows.Forms.RadioButton
    Friend WithEvents half_scale As System.Windows.Forms.RadioButton
    Friend WithEvents full_scale As System.Windows.Forms.RadioButton
    Friend WithEvents GLC As OpenTK.GLControl
    Friend WithEvents GroupBox2 As System.Windows.Forms.GroupBox
    Friend WithEvents b_depth As System.Windows.Forms.RadioButton
    Friend WithEvents w_label As System.Windows.Forms.Label
    Friend WithEvents h_label As System.Windows.Forms.Label
    Friend WithEvents b_color As System.Windows.Forms.RadioButton
    Friend WithEvents b_position As System.Windows.Forms.RadioButton
    Friend WithEvents b_normal As System.Windows.Forms.RadioButton
    Friend WithEvents b_flags As System.Windows.Forms.RadioButton
    Friend WithEvents r_cb As CheckBox
    Friend WithEvents g_cb As CheckBox
    Friend WithEvents b_cb As CheckBox
    Friend WithEvents a_cb As CheckBox
    Friend WithEvents Label1 As Label
    Friend WithEvents b_aux As RadioButton
    Friend WithEvents cb_alpha_enable As CheckBox
End Class

Imports OpenTK.Graphics

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmMain
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmMain))
        Me.glControl_main = New OpenTK.GLControl()
        Me.glControl_utility = New OpenTK.GLControl()
        Me.frmMainMenu = New System.Windows.Forms.MenuStrip()
        Me.m_file = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_help = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_light_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.frmMainMenu.SuspendLayout()
        Me.SuspendLayout()
        '
        'glControl_main
        '
        Me.glControl_main.BackColor = System.Drawing.Color.Red
        Me.glControl_main.Location = New System.Drawing.Point(28, 65)
        Me.glControl_main.Name = "glControl_main"
        Me.glControl_main.Size = New System.Drawing.Size(281, 250)
        Me.glControl_main.TabIndex = 0
        Me.glControl_main.VSync = False
        '
        'glControl_utility
        '
        Me.glControl_utility.Anchor = System.Windows.Forms.AnchorStyles.None
        Me.glControl_utility.BackColor = System.Drawing.Color.Blue
        Me.glControl_utility.Location = New System.Drawing.Point(336, 27)
        Me.glControl_utility.Name = "glControl_utility"
        Me.glControl_utility.Size = New System.Drawing.Size(347, 394)
        Me.glControl_utility.TabIndex = 0
        Me.glControl_utility.VSync = False
        '
        'frmMainMenu
        '
        Me.frmMainMenu.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_file, Me.m_help, Me.m_settings, Me.m_light_settings})
        Me.frmMainMenu.Location = New System.Drawing.Point(0, 0)
        Me.frmMainMenu.Name = "frmMainMenu"
        Me.frmMainMenu.Size = New System.Drawing.Size(686, 24)
        Me.frmMainMenu.TabIndex = 1
        Me.frmMainMenu.Text = "MenuStrip1"
        '
        'm_file
        '
        Me.m_file.Name = "m_file"
        Me.m_file.Size = New System.Drawing.Size(37, 20)
        Me.m_file.Text = "File"
        '
        'm_help
        '
        Me.m_help.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right
        Me.m_help.Image = Global.nuTerra.My.Resources.Resources.question
        Me.m_help.Name = "m_help"
        Me.m_help.Size = New System.Drawing.Size(28, 20)
        '
        'm_settings
        '
        Me.m_settings.Name = "m_settings"
        Me.m_settings.Size = New System.Drawing.Size(61, 20)
        Me.m_settings.Text = "Settings"
        '
        'm_light_settings
        '
        Me.m_light_settings.Name = "m_light_settings"
        Me.m_light_settings.Size = New System.Drawing.Size(91, 20)
        Me.m_light_settings.Text = "Light Settings"
        '
        'frmMain
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.Black
        Me.ClientSize = New System.Drawing.Size(686, 425)
        Me.Controls.Add(Me.glControl_utility)
        Me.Controls.Add(Me.glControl_main)
        Me.Controls.Add(Me.frmMainMenu)
        Me.ForeColor = System.Drawing.Color.White
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.MainMenuStrip = Me.frmMainMenu
        Me.Name = "frmMain"
        Me.Text = "nuTerra"
        Me.frmMainMenu.ResumeLayout(False)
        Me.frmMainMenu.PerformLayout()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents glControl_main As OpenTK.GLControl
    Friend WithEvents frmMainMenu As System.Windows.Forms.MenuStrip
    Friend WithEvents m_file As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_help As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_settings As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents glControl_utility As OpenTK.GLControl
    Friend WithEvents m_light_settings As System.Windows.Forms.ToolStripMenuItem

End Class

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmMain
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
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
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmMain))
        Me.frmMainMenu = New System.Windows.Forms.MenuStrip()
        Me.m_file = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_load_map = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator1 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_Log_File = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator4 = New System.Windows.Forms.ToolStripSeparator()
        Me.ToolStripSeparator5 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_shut_down = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_set_game_path = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator3 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_show_light_pos = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_show_properties = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_light_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_help = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_screen_capture = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_appVersion = New System.Windows.Forms.ToolStripMenuItem()
        Me.startup_delay_timer = New System.Windows.Forms.Timer(Me.components)
        Me.FolderBrowserDialog1 = New System.Windows.Forms.FolderBrowserDialog()
        Me.map_loader = New System.Windows.Forms.Timer(Me.components)
        Me.SplitContainer1 = New System.Windows.Forms.SplitContainer()
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.PropertyGrid1 = New System.Windows.Forms.PropertyGrid()
        Me.frmMainMenu.SuspendLayout()
        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainer1.SuspendLayout()
        Me.SuspendLayout()
        '
        'frmMainMenu
        '
        Me.frmMainMenu.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_file, Me.m_settings, Me.m_light_settings, Me.m_help, Me.m_screen_capture, Me.m_appVersion})
        Me.frmMainMenu.Location = New System.Drawing.Point(0, 0)
        Me.frmMainMenu.Name = "frmMainMenu"
        Me.frmMainMenu.Size = New System.Drawing.Size(956, 24)
        Me.frmMainMenu.TabIndex = 1
        Me.frmMainMenu.Text = "MenuStrip1"
        '
        'm_file
        '
        Me.m_file.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_file.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_load_map, Me.ToolStripSeparator1, Me.m_Log_File, Me.ToolStripSeparator4, Me.ToolStripSeparator5, Me.m_shut_down})
        Me.m_file.ForeColor = System.Drawing.Color.Black
        Me.m_file.Name = "m_file"
        Me.m_file.Size = New System.Drawing.Size(37, 20)
        Me.m_file.Text = "File"
        '
        'm_load_map
        '
        Me.m_load_map.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_load_map.Name = "m_load_map"
        Me.m_load_map.Size = New System.Drawing.Size(152, 22)
        Me.m_load_map.Text = "Load Map"
        '
        'ToolStripSeparator1
        '
        Me.ToolStripSeparator1.Name = "ToolStripSeparator1"
        Me.ToolStripSeparator1.Size = New System.Drawing.Size(149, 6)
        '
        'm_Log_File
        '
        Me.m_Log_File.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_Log_File.Name = "m_Log_File"
        Me.m_Log_File.Size = New System.Drawing.Size(152, 22)
        Me.m_Log_File.Text = "Log File"
        '
        'ToolStripSeparator4
        '
        Me.ToolStripSeparator4.Name = "ToolStripSeparator4"
        Me.ToolStripSeparator4.Size = New System.Drawing.Size(149, 6)
        '
        'ToolStripSeparator5
        '
        Me.ToolStripSeparator5.Name = "ToolStripSeparator5"
        Me.ToolStripSeparator5.Size = New System.Drawing.Size(149, 6)
        '
        'm_shut_down
        '
        Me.m_shut_down.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_shut_down.Name = "m_shut_down"
        Me.m_shut_down.Size = New System.Drawing.Size(152, 22)
        Me.m_shut_down.Text = "Shut Me Down"
        '
        'm_settings
        '
        Me.m_settings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_settings.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_set_game_path, Me.ToolStripSeparator3, Me.m_show_light_pos, Me.m_show_properties})
        Me.m_settings.ForeColor = System.Drawing.Color.Black
        Me.m_settings.Name = "m_settings"
        Me.m_settings.Size = New System.Drawing.Size(61, 20)
        Me.m_settings.Text = "Settings"
        '
        'm_set_game_path
        '
        Me.m_set_game_path.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_set_game_path.Name = "m_set_game_path"
        Me.m_set_game_path.Size = New System.Drawing.Size(321, 22)
        Me.m_set_game_path.Text = "Set Game Path (world_of_tanks folder location)"
        '
        'ToolStripSeparator3
        '
        Me.ToolStripSeparator3.Name = "ToolStripSeparator3"
        Me.ToolStripSeparator3.Size = New System.Drawing.Size(318, 6)
        '
        'm_show_light_pos
        '
        Me.m_show_light_pos.Checked = True
        Me.m_show_light_pos.CheckOnClick = True
        Me.m_show_light_pos.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_show_light_pos.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_show_light_pos.Name = "m_show_light_pos"
        Me.m_show_light_pos.Size = New System.Drawing.Size(321, 22)
        Me.m_show_light_pos.Text = "Show Light Position"
        '
        'm_show_properties
        '
        Me.m_show_properties.Name = "m_show_properties"
        Me.m_show_properties.Size = New System.Drawing.Size(321, 22)
        Me.m_show_properties.Text = "Show Properties"
        '
        'm_light_settings
        '
        Me.m_light_settings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_light_settings.ForeColor = System.Drawing.Color.Black
        Me.m_light_settings.Name = "m_light_settings"
        Me.m_light_settings.Size = New System.Drawing.Size(91, 20)
        Me.m_light_settings.Text = "Light Settings"
        '
        'm_help
        '
        Me.m_help.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right
        Me.m_help.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center
        Me.m_help.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image
        Me.m_help.Image = Global.nuTerra.My.Resources.Resources.question
        Me.m_help.ImageTransparentColor = System.Drawing.Color.Transparent
        Me.m_help.Name = "m_help"
        Me.m_help.Size = New System.Drawing.Size(28, 20)
        Me.m_help.TextImageRelation = System.Windows.Forms.TextImageRelation.Overlay
        '
        'm_screen_capture
        '
        Me.m_screen_capture.Name = "m_screen_capture"
        Me.m_screen_capture.Size = New System.Drawing.Size(99, 20)
        Me.m_screen_capture.Text = "Screen Capture"
        '
        'm_appVersion
        '
        Me.m_appVersion.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right
        Me.m_appVersion.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_appVersion.Name = "m_appVersion"
        Me.m_appVersion.Size = New System.Drawing.Size(96, 20)
        Me.m_appVersion.Text = "Version: 1.0.0.0"
        '
        'startup_delay_timer
        '
        Me.startup_delay_timer.Interval = 1000
        '
        'map_loader
        '
        Me.map_loader.Interval = 30
        '
        'SplitContainer1
        '
        Me.SplitContainer1.BackColor = System.Drawing.SystemColors.ActiveCaption
        Me.SplitContainer1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainer1.Location = New System.Drawing.Point(0, 0)
        Me.SplitContainer1.Name = "SplitContainer1"
        '
        'SplitContainer1.Panel1
        '
        Me.SplitContainer1.Panel1.BackColor = System.Drawing.Color.FromArgb(CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer))
        '
        'SplitContainer1.Panel2
        '
        Me.SplitContainer1.Panel2.BackColor = System.Drawing.Color.FromArgb(CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer))
        Me.SplitContainer1.Panel2Collapsed = True
        Me.SplitContainer1.Size = New System.Drawing.Size(956, 679)
        Me.SplitContainer1.SplitterDistance = 318
        Me.SplitContainer1.TabIndex = 0
        '
        'Panel1
        '
        Me.Panel1.BackgroundImage = CType(resources.GetObject("Panel1.BackgroundImage"), System.Drawing.Image)
        Me.Panel1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center
        Me.Panel1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.Panel1.Location = New System.Drawing.Point(0, 24)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(956, 655)
        Me.Panel1.TabIndex = 2
        '
        'PropertyGrid1
        '
        Me.PropertyGrid1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PropertyGrid1.HelpVisible = False
        Me.PropertyGrid1.Location = New System.Drawing.Point(630, 0)
        Me.PropertyGrid1.Name = "PropertyGrid1"
        Me.PropertyGrid1.PropertySort = System.Windows.Forms.PropertySort.Categorized
        Me.PropertyGrid1.Size = New System.Drawing.Size(326, 655)
        Me.PropertyGrid1.TabIndex = 0
        Me.PropertyGrid1.ToolbarVisible = False
        Me.PropertyGrid1.ViewBackColor = System.Drawing.Color.White
        '
        'frmMain
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.Black
        Me.ClientSize = New System.Drawing.Size(956, 679)
        Me.Controls.Add(Me.Panel1)
        Me.Controls.Add(Me.frmMainMenu)
        Me.Controls.Add(Me.SplitContainer1)
        Me.ForeColor = System.Drawing.Color.Black
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.MainMenuStrip = Me.frmMainMenu
        Me.Name = "frmMain"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "nuTerra"
        Me.frmMainMenu.ResumeLayout(False)
        Me.frmMainMenu.PerformLayout()
        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.SplitContainer1.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents glControl_main As OpenTK.GLControl
    Friend WithEvents frmMainMenu As System.Windows.Forms.MenuStrip
    Friend WithEvents m_file As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_help As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_settings As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_light_settings As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_set_game_path As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents Panel1 As System.Windows.Forms.Panel
    Friend WithEvents startup_delay_timer As System.Windows.Forms.Timer
    Friend WithEvents FolderBrowserDialog1 As System.Windows.Forms.FolderBrowserDialog
    Friend WithEvents m_load_map As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator1 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents m_shut_down As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator3 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents m_show_light_pos As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_Log_File As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator4 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents ToolStripSeparator5 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents map_loader As System.Windows.Forms.Timer
    Friend WithEvents m_screen_capture As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents SplitContainer1 As SplitContainer
    Friend WithEvents PropertyGrid1 As PropertyGrid
    Friend WithEvents m_show_properties As ToolStripMenuItem
    Friend WithEvents m_appVersion As ToolStripMenuItem
End Class

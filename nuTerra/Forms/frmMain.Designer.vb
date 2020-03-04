Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

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
        Me.components = New System.ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmMain))

#If DEBUG Then
        Dim flags = GraphicsContextFlags.ForwardCompatible ' Or GraphicsContextFlags.Debug
#Else
        Dim flags = GraphicsContextFlags.ForwardCompatible
#End If

        ' Disable depth buffer
        Dim mode = New GraphicsMode(ColorFormat.Empty, 0)

        Me.glControl_main = New OpenTK.GLControl(mode, 4, 3, flags)
        Me.frmMainMenu = New System.Windows.Forms.MenuStrip()
        Me.m_file = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_load_map = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator1 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_developer_mode = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator2 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_Log_File = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator4 = New System.Windows.Forms.ToolStripSeparator()
        Me.ToolStripSeparator5 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_shut_down = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_help = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_set_game_path = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator3 = New System.Windows.Forms.ToolStripSeparator()
        Me.m_show_light_pos = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_light_settings = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_developer = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_block_loading = New System.Windows.Forms.ToolStripMenuItem()
        Me.m_show_gbuffer = New System.Windows.Forms.ToolStripMenuItem()
        Me.startup_delay_timer = New System.Windows.Forms.Timer(Me.components)
        Me.FolderBrowserDialog1 = New System.Windows.Forms.FolderBrowserDialog()
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.map_loader = New System.Windows.Forms.Timer(Me.components)
        Me.frmMainMenu.SuspendLayout()
        Me.SuspendLayout()
        '
        'glControl_main
        '
        Me.glControl_main.BackColor = System.Drawing.Color.Red
        Me.glControl_main.Location = New System.Drawing.Point(28, 65)
        Me.glControl_main.Name = "glControl_main"
        Me.glControl_main.Size = New System.Drawing.Size(147, 142)
        Me.glControl_main.TabIndex = 0
        Me.glControl_main.VSync = False
        '
        'frmMainMenu
        '
        Me.frmMainMenu.ImageScalingSize = New System.Drawing.Size(1, 16)
        Me.frmMainMenu.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_file, Me.m_help, Me.m_settings, Me.m_light_settings, Me.m_developer})
        Me.frmMainMenu.Location = New System.Drawing.Point(0, 0)
        Me.frmMainMenu.Name = "frmMainMenu"
        Me.frmMainMenu.Size = New System.Drawing.Size(956, 24)
        Me.frmMainMenu.TabIndex = 1
        Me.frmMainMenu.Text = "MenuStrip1"
        '
        'm_file
        '
        Me.m_file.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_file.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_load_map, Me.ToolStripSeparator1, Me.m_developer_mode, Me.ToolStripSeparator2, Me.m_Log_File, Me.ToolStripSeparator4, Me.ToolStripSeparator5, Me.m_shut_down})
        Me.m_file.ForeColor = System.Drawing.Color.Black
        Me.m_file.Name = "m_file"
        Me.m_file.Size = New System.Drawing.Size(37, 20)
        Me.m_file.Text = "File"
        '
        'm_load_map
        '
        Me.m_load_map.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_load_map.Name = "m_load_map"
        Me.m_load_map.Size = New System.Drawing.Size(161, 22)
        Me.m_load_map.Text = "Load Map"
        '
        'ToolStripSeparator1
        '
        Me.ToolStripSeparator1.Name = "ToolStripSeparator1"
        Me.ToolStripSeparator1.Size = New System.Drawing.Size(158, 6)
        '
        'm_developer_mode
        '
        Me.m_developer_mode.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_developer_mode.Name = "m_developer_mode"
        Me.m_developer_mode.Size = New System.Drawing.Size(161, 22)
        Me.m_developer_mode.Text = "Developer Mode"
        '
        'ToolStripSeparator2
        '
        Me.ToolStripSeparator2.Name = "ToolStripSeparator2"
        Me.ToolStripSeparator2.Size = New System.Drawing.Size(158, 6)
        '
        'm_Log_File
        '
        Me.m_Log_File.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_Log_File.Name = "m_Log_File"
        Me.m_Log_File.Size = New System.Drawing.Size(161, 22)
        Me.m_Log_File.Text = "Log File"
        '
        'ToolStripSeparator4
        '
        Me.ToolStripSeparator4.Name = "ToolStripSeparator4"
        Me.ToolStripSeparator4.Size = New System.Drawing.Size(158, 6)
        '
        'ToolStripSeparator5
        '
        Me.ToolStripSeparator5.Name = "ToolStripSeparator5"
        Me.ToolStripSeparator5.Size = New System.Drawing.Size(158, 6)
        '
        'm_shut_down
        '
        Me.m_shut_down.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_shut_down.Name = "m_shut_down"
        Me.m_shut_down.Size = New System.Drawing.Size(161, 22)
        Me.m_shut_down.Text = "Shut Me Down"
        '
        'm_help
        '
        Me.m_help.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right
        Me.m_help.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image
        Me.m_help.Image = Global.nuTerra.My.Resources.Resources.question
        Me.m_help.Name = "m_help"
        Me.m_help.Size = New System.Drawing.Size(13, 20)
        '
        'm_settings
        '
        Me.m_settings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_settings.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_set_game_path, Me.ToolStripSeparator3, Me.m_show_light_pos})
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
        'm_light_settings
        '
        Me.m_light_settings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_light_settings.ForeColor = System.Drawing.Color.Black
        Me.m_light_settings.Name = "m_light_settings"
        Me.m_light_settings.Size = New System.Drawing.Size(91, 20)
        Me.m_light_settings.Text = "Light Settings"
        '
        'm_developer
        '
        Me.m_developer.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_developer.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.m_block_loading, Me.m_show_gbuffer})
        Me.m_developer.ForeColor = System.Drawing.Color.Black
        Me.m_developer.Name = "m_developer"
        Me.m_developer.Size = New System.Drawing.Size(102, 20)
        Me.m_developer.Text = "Developer Tools"
        Me.m_developer.Visible = False
        '
        'm_block_loading
        '
        Me.m_block_loading.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_block_loading.Name = "m_block_loading"
        Me.m_block_loading.Size = New System.Drawing.Size(195, 22)
        Me.m_block_loading.Text = "Block Loading of Types"
        '
        'm_show_gbuffer
        '
        Me.m_show_gbuffer.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.m_show_gbuffer.Name = "m_show_gbuffer"
        Me.m_show_gbuffer.Size = New System.Drawing.Size(195, 22)
        Me.m_show_gbuffer.Text = "Show Gbuffer Textures"
        '
        'startup_delay_timer
        '
        Me.startup_delay_timer.Interval = 1000
        '
        'Panel1
        '
        Me.Panel1.BackgroundImage = CType(resources.GetObject("Panel1.BackgroundImage"), System.Drawing.Image)
        Me.Panel1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center
        Me.Panel1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.Panel1.Location = New System.Drawing.Point(0, 24)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(956, 549)
        Me.Panel1.TabIndex = 2
        '
        'map_loader
        '
        Me.map_loader.Interval = 30
        '
        'frmMain
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.Black
        Me.ClientSize = New System.Drawing.Size(956, 573)
        Me.Controls.Add(Me.Panel1)
        Me.Controls.Add(Me.glControl_main)
        Me.Controls.Add(Me.frmMainMenu)
        Me.ForeColor = System.Drawing.Color.Black
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.MainMenuStrip = Me.frmMainMenu
        Me.Name = "frmMain"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
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
    Friend WithEvents m_light_settings As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_set_game_path As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents Panel1 As System.Windows.Forms.Panel
    Friend WithEvents startup_delay_timer As System.Windows.Forms.Timer
    Friend WithEvents FolderBrowserDialog1 As System.Windows.Forms.FolderBrowserDialog
    Friend WithEvents m_load_map As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_developer As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_block_loading As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_show_gbuffer As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator1 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents m_developer_mode As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator2 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents m_shut_down As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator3 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents m_show_light_pos As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents m_Log_File As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripSeparator4 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents ToolStripSeparator5 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents map_loader As System.Windows.Forms.Timer

End Class

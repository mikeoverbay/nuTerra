<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmLoadOptions
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmLoadOptions))
        Me.m_sky = New System.Windows.Forms.CheckBox()
        Me.m_bases = New System.Windows.Forms.CheckBox()
        Me.m_water = New System.Windows.Forms.CheckBox()
        Me.m_decals = New System.Windows.Forms.CheckBox()
        Me.m_models = New System.Windows.Forms.CheckBox()
        Me.m_trees = New System.Windows.Forms.CheckBox()
        Me.m_terrain = New System.Windows.Forms.CheckBox()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.SuspendLayout()
        '
        'm_sky
        '
        Me.m_sky.Checked = Global.nuTerra.My.MySettings.Default.load_sky
        Me.m_sky.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_sky.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_sky", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_sky.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_sky.ForeColor = System.Drawing.Color.White
        Me.m_sky.Location = New System.Drawing.Point(12, 217)
        Me.m_sky.Name = "m_sky"
        Me.m_sky.Size = New System.Drawing.Size(70, 24)
        Me.m_sky.TabIndex = 6
        Me.m_sky.Text = "Sky"
        Me.m_sky.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_sky.UseVisualStyleBackColor = True
        '
        'm_bases
        '
        Me.m_bases.Checked = Global.nuTerra.My.MySettings.Default.load_bases
        Me.m_bases.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_bases.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_bases", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_bases.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_bases.ForeColor = System.Drawing.Color.White
        Me.m_bases.Location = New System.Drawing.Point(12, 187)
        Me.m_bases.Name = "m_bases"
        Me.m_bases.Size = New System.Drawing.Size(70, 24)
        Me.m_bases.TabIndex = 5
        Me.m_bases.Text = "Bases"
        Me.m_bases.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_bases.UseVisualStyleBackColor = True
        '
        'm_water
        '
        Me.m_water.Checked = Global.nuTerra.My.MySettings.Default.load_water
        Me.m_water.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_water.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_water", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_water.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_water.ForeColor = System.Drawing.Color.White
        Me.m_water.Location = New System.Drawing.Point(12, 157)
        Me.m_water.Name = "m_water"
        Me.m_water.Size = New System.Drawing.Size(70, 24)
        Me.m_water.TabIndex = 4
        Me.m_water.Text = "Water"
        Me.m_water.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_water.UseVisualStyleBackColor = True
        '
        'm_decals
        '
        Me.m_decals.Checked = Global.nuTerra.My.MySettings.Default.load_decals
        Me.m_decals.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_decals.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_decals", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_decals.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_decals.ForeColor = System.Drawing.Color.White
        Me.m_decals.Location = New System.Drawing.Point(12, 127)
        Me.m_decals.Name = "m_decals"
        Me.m_decals.Size = New System.Drawing.Size(70, 24)
        Me.m_decals.TabIndex = 3
        Me.m_decals.Text = "Decals"
        Me.m_decals.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_decals.UseVisualStyleBackColor = True
        '
        'm_models
        '
        Me.m_models.Checked = Global.nuTerra.My.MySettings.Default.load_models
        Me.m_models.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_models.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_models", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_models.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_models.ForeColor = System.Drawing.Color.White
        Me.m_models.Location = New System.Drawing.Point(12, 97)
        Me.m_models.Name = "m_models"
        Me.m_models.Size = New System.Drawing.Size(70, 24)
        Me.m_models.TabIndex = 2
        Me.m_models.Text = "Models"
        Me.m_models.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_models.UseVisualStyleBackColor = True
        '
        'm_trees
        '
        Me.m_trees.Checked = Global.nuTerra.My.MySettings.Default.load_trees
        Me.m_trees.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_trees.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_trees", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_trees.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_trees.ForeColor = System.Drawing.Color.White
        Me.m_trees.Location = New System.Drawing.Point(12, 67)
        Me.m_trees.Name = "m_trees"
        Me.m_trees.Size = New System.Drawing.Size(70, 24)
        Me.m_trees.TabIndex = 1
        Me.m_trees.Text = "Trees"
        Me.m_trees.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_trees.UseVisualStyleBackColor = True
        '
        'm_terrain
        '
        Me.m_terrain.Checked = Global.nuTerra.My.MySettings.Default.load_terrain
        Me.m_terrain.CheckState = System.Windows.Forms.CheckState.Checked
        Me.m_terrain.DataBindings.Add(New System.Windows.Forms.Binding("Checked", Global.nuTerra.My.MySettings.Default, "load_terrain", True, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged))
        Me.m_terrain.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.m_terrain.ForeColor = System.Drawing.Color.White
        Me.m_terrain.Location = New System.Drawing.Point(12, 37)
        Me.m_terrain.Name = "m_terrain"
        Me.m_terrain.Size = New System.Drawing.Size(70, 24)
        Me.m_terrain.TabIndex = 0
        Me.m_terrain.Text = "Terrain"
        Me.m_terrain.TextAlign = System.Drawing.ContentAlignment.TopLeft
        Me.m_terrain.UseVisualStyleBackColor = True
        '
        'Label1
        '
        Me.Label1.Dock = System.Windows.Forms.DockStyle.Top
        Me.Label1.Location = New System.Drawing.Point(0, 0)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(169, 34)
        Me.Label1.TabIndex = 7
        Me.Label1.Text = "" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "You may need to reload a map!" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10)
        Me.Label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'frmLoadOptions
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer), CType(CType(32, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(169, 252)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.m_sky)
        Me.Controls.Add(Me.m_bases)
        Me.Controls.Add(Me.m_water)
        Me.Controls.Add(Me.m_decals)
        Me.Controls.Add(Me.m_models)
        Me.Controls.Add(Me.m_trees)
        Me.Controls.Add(Me.m_terrain)
        Me.ForeColor = System.Drawing.Color.White
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.Name = "frmLoadOptions"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "Visibility / Allow Loading.."
        Me.TopMost = True
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents m_terrain As System.Windows.Forms.CheckBox
    Friend WithEvents m_trees As System.Windows.Forms.CheckBox
    Friend WithEvents m_models As System.Windows.Forms.CheckBox
    Friend WithEvents m_decals As System.Windows.Forms.CheckBox
    Friend WithEvents m_water As System.Windows.Forms.CheckBox
    Friend WithEvents m_bases As System.Windows.Forms.CheckBox
    Friend WithEvents m_sky As System.Windows.Forms.CheckBox
    Friend WithEvents Label1 As Label
End Class

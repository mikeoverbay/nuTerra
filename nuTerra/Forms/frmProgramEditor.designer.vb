﻿<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class frmProgramEditor
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmProgramEditor))
        Me.recompile_bt = New System.Windows.Forms.Button()
        Me.TabControl1 = New System.Windows.Forms.TabControl()
        Me.TabPage1 = New System.Windows.Forms.TabPage()
        Me.vert_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.vertex_context_menustrip = New System.Windows.Forms.ContextMenuStrip(Me.components)
        Me.ToolStripMenuItem1 = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripMenuItem2 = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripMenuItem3 = New System.Windows.Forms.ToolStripMenuItem()
        Me.TabPage5 = New System.Windows.Forms.TabPage()
        Me.tessControl_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.TabPage6 = New System.Windows.Forms.TabPage()
        Me.tessEvaluation_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.TabPage3 = New System.Windows.Forms.TabPage()
        Me.geo_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.TabPage2 = New System.Windows.Forms.TabPage()
        Me.frag_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.TabPage4 = New System.Windows.Forms.TabPage()
        Me.compute_tb = New FastColoredTextBoxNS.FastColoredTextBox()
        Me.CB1 = New System.Windows.Forms.ComboBox()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.search_btn = New System.Windows.Forms.Button()
        Me.Button1 = New System.Windows.Forms.Button()
        Me.Container_panel = New System.Windows.Forms.Panel()
        Me.nuTerra_Image_panel = New System.Windows.Forms.Panel()
        Me.help = New System.Windows.Forms.Button()
        Me.TabControl1.SuspendLayout()
        Me.TabPage1.SuspendLayout()
        CType(Me.vert_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.vertex_context_menustrip.SuspendLayout()
        Me.TabPage5.SuspendLayout()
        CType(Me.tessControl_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.TabPage6.SuspendLayout()
        CType(Me.tessEvaluation_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.TabPage3.SuspendLayout()
        CType(Me.geo_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.TabPage2.SuspendLayout()
        CType(Me.frag_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.TabPage4.SuspendLayout()
        CType(Me.compute_tb, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.Container_panel.SuspendLayout()
        Me.SuspendLayout()
        '
        'recompile_bt
        '
        Me.recompile_bt.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.recompile_bt.FlatAppearance.BorderColor = System.Drawing.Color.White
        Me.recompile_bt.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gray
        Me.recompile_bt.Font = New System.Drawing.Font("Microsoft Sans Serif", 8.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.recompile_bt.ForeColor = System.Drawing.Color.Black
        Me.recompile_bt.Location = New System.Drawing.Point(498, 558)
        Me.recompile_bt.Margin = New System.Windows.Forms.Padding(0)
        Me.recompile_bt.Name = "recompile_bt"
        Me.recompile_bt.Size = New System.Drawing.Size(97, 53)
        Me.recompile_bt.TabIndex = 0
        Me.recompile_bt.Text = "Compile"
        Me.recompile_bt.UseVisualStyleBackColor = True
        '
        'TabControl1
        '
        Me.TabControl1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TabControl1.Controls.Add(Me.TabPage1)
        Me.TabControl1.Controls.Add(Me.TabPage5)
        Me.TabControl1.Controls.Add(Me.TabPage6)
        Me.TabControl1.Controls.Add(Me.TabPage3)
        Me.TabControl1.Controls.Add(Me.TabPage2)
        Me.TabControl1.Controls.Add(Me.TabPage4)
        Me.TabControl1.ItemSize = New System.Drawing.Size(120, 18)
        Me.TabControl1.Location = New System.Drawing.Point(1, 0)
        Me.TabControl1.Margin = New System.Windows.Forms.Padding(0)
        Me.TabControl1.Name = "TabControl1"
        Me.TabControl1.Padding = New System.Drawing.Point(0, 0)
        Me.TabControl1.SelectedIndex = 0
        Me.TabControl1.Size = New System.Drawing.Size(598, 555)
        Me.TabControl1.TabIndex = 2
        '
        'TabPage1
        '
        Me.TabPage1.Controls.Add(Me.vert_tb)
        Me.TabPage1.Location = New System.Drawing.Point(4, 22)
        Me.TabPage1.Margin = New System.Windows.Forms.Padding(0)
        Me.TabPage1.Name = "TabPage1"
        Me.TabPage1.Size = New System.Drawing.Size(590, 529)
        Me.TabPage1.TabIndex = 0
        Me.TabPage1.Text = "Vertex Program"
        Me.TabPage1.UseVisualStyleBackColor = True
        '
        'vert_tb
        '
        Me.vert_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.vert_tb.AutoIndent = False
        Me.vert_tb.AutoIndentChars = False
        Me.vert_tb.AutoIndentExistingLines = False
        Me.vert_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.vert_tb.BackBrush = Nothing
        Me.vert_tb.BackColor = System.Drawing.Color.Black
        Me.vert_tb.CaretColor = System.Drawing.Color.White
        Me.vert_tb.CharHeight = 14
        Me.vert_tb.CharWidth = 8
        Me.vert_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.vert_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.vert_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.vert_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.vert_tb.ForeColor = System.Drawing.Color.White
        Me.vert_tb.IsReplaceMode = False
        Me.vert_tb.Location = New System.Drawing.Point(0, 0)
        Me.vert_tb.Name = "vert_tb"
        Me.vert_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.vert_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.vert_tb.ServiceColors = CType(resources.GetObject("vert_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.vert_tb.Size = New System.Drawing.Size(590, 529)
        Me.vert_tb.TabIndex = 1
        Me.vert_tb.Zoom = 100
        '
        'vertex_context_menustrip
        '
        Me.vertex_context_menustrip.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem1, Me.ToolStripMenuItem2, Me.ToolStripMenuItem3})
        Me.vertex_context_menustrip.Name = "vertex_context_menustrip"
        Me.vertex_context_menustrip.Size = New System.Drawing.Size(103, 70)
        '
        'ToolStripMenuItem1
        '
        Me.ToolStripMenuItem1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.ToolStripMenuItem1.Name = "ToolStripMenuItem1"
        Me.ToolStripMenuItem1.Size = New System.Drawing.Size(102, 22)
        Me.ToolStripMenuItem1.Text = "Cut"
        '
        'ToolStripMenuItem2
        '
        Me.ToolStripMenuItem2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.ToolStripMenuItem2.Name = "ToolStripMenuItem2"
        Me.ToolStripMenuItem2.Size = New System.Drawing.Size(102, 22)
        Me.ToolStripMenuItem2.Text = "Copy"
        '
        'ToolStripMenuItem3
        '
        Me.ToolStripMenuItem3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
        Me.ToolStripMenuItem3.Name = "ToolStripMenuItem3"
        Me.ToolStripMenuItem3.Size = New System.Drawing.Size(102, 22)
        Me.ToolStripMenuItem3.Text = "Paste"
        '
        'TabPage5
        '
        Me.TabPage5.Controls.Add(Me.tessControl_tb)
        Me.TabPage5.Location = New System.Drawing.Point(4, 22)
        Me.TabPage5.Name = "TabPage5"
        Me.TabPage5.Size = New System.Drawing.Size(590, 529)
        Me.TabPage5.TabIndex = 4
        Me.TabPage5.Text = "TessControl"
        Me.TabPage5.UseVisualStyleBackColor = True
        '
        'tessControl_tb
        '
        Me.tessControl_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.tessControl_tb.AutoIndent = False
        Me.tessControl_tb.AutoIndentChars = False
        Me.tessControl_tb.AutoIndentExistingLines = False
        Me.tessControl_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.tessControl_tb.BackBrush = Nothing
        Me.tessControl_tb.BackColor = System.Drawing.Color.Black
        Me.tessControl_tb.CaretColor = System.Drawing.Color.White
        Me.tessControl_tb.CharHeight = 14
        Me.tessControl_tb.CharWidth = 8
        Me.tessControl_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.tessControl_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.tessControl_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.tessControl_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.tessControl_tb.ForeColor = System.Drawing.Color.White
        Me.tessControl_tb.IsReplaceMode = False
        Me.tessControl_tb.Location = New System.Drawing.Point(0, 0)
        Me.tessControl_tb.Name = "tessControl_tb"
        Me.tessControl_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.tessControl_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.tessControl_tb.ServiceColors = CType(resources.GetObject("tessControl_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.tessControl_tb.Size = New System.Drawing.Size(590, 529)
        Me.tessControl_tb.TabIndex = 2
        Me.tessControl_tb.Zoom = 100
        '
        'TabPage6
        '
        Me.TabPage6.Controls.Add(Me.tessEvaluation_tb)
        Me.TabPage6.Location = New System.Drawing.Point(4, 22)
        Me.TabPage6.Name = "TabPage6"
        Me.TabPage6.Size = New System.Drawing.Size(590, 529)
        Me.TabPage6.TabIndex = 5
        Me.TabPage6.Text = "TessEvaluation"
        Me.TabPage6.UseVisualStyleBackColor = True
        '
        'tessEvaluation_tb
        '
        Me.tessEvaluation_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.tessEvaluation_tb.AutoIndent = False
        Me.tessEvaluation_tb.AutoIndentChars = False
        Me.tessEvaluation_tb.AutoIndentExistingLines = False
        Me.tessEvaluation_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.tessEvaluation_tb.BackBrush = Nothing
        Me.tessEvaluation_tb.BackColor = System.Drawing.Color.Black
        Me.tessEvaluation_tb.CaretColor = System.Drawing.Color.White
        Me.tessEvaluation_tb.CharHeight = 14
        Me.tessEvaluation_tb.CharWidth = 8
        Me.tessEvaluation_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.tessEvaluation_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.tessEvaluation_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.tessEvaluation_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.tessEvaluation_tb.ForeColor = System.Drawing.Color.White
        Me.tessEvaluation_tb.IsReplaceMode = False
        Me.tessEvaluation_tb.Location = New System.Drawing.Point(0, 0)
        Me.tessEvaluation_tb.Name = "tessEvaluation_tb"
        Me.tessEvaluation_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.tessEvaluation_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.tessEvaluation_tb.ServiceColors = CType(resources.GetObject("tessEvaluation_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.tessEvaluation_tb.Size = New System.Drawing.Size(590, 529)
        Me.tessEvaluation_tb.TabIndex = 3
        Me.tessEvaluation_tb.Zoom = 100
        '
        'TabPage3
        '
        Me.TabPage3.Controls.Add(Me.geo_tb)
        Me.TabPage3.Location = New System.Drawing.Point(4, 22)
        Me.TabPage3.Margin = New System.Windows.Forms.Padding(0)
        Me.TabPage3.Name = "TabPage3"
        Me.TabPage3.Size = New System.Drawing.Size(590, 529)
        Me.TabPage3.TabIndex = 2
        Me.TabPage3.Text = "Geometry Program"
        Me.TabPage3.UseVisualStyleBackColor = True
        '
        'geo_tb
        '
        Me.geo_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.geo_tb.AutoIndent = False
        Me.geo_tb.AutoIndentChars = False
        Me.geo_tb.AutoIndentExistingLines = False
        Me.geo_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.geo_tb.BackBrush = Nothing
        Me.geo_tb.BackColor = System.Drawing.Color.Black
        Me.geo_tb.CaretColor = System.Drawing.Color.White
        Me.geo_tb.CharHeight = 14
        Me.geo_tb.CharWidth = 8
        Me.geo_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.geo_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.geo_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.geo_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.geo_tb.ForeColor = System.Drawing.Color.White
        Me.geo_tb.IsReplaceMode = False
        Me.geo_tb.Location = New System.Drawing.Point(0, 0)
        Me.geo_tb.Name = "geo_tb"
        Me.geo_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.geo_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.geo_tb.ServiceColors = CType(resources.GetObject("geo_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.geo_tb.Size = New System.Drawing.Size(590, 529)
        Me.geo_tb.TabIndex = 1
        Me.geo_tb.Zoom = 100
        '
        'TabPage2
        '
        Me.TabPage2.Controls.Add(Me.frag_tb)
        Me.TabPage2.Location = New System.Drawing.Point(4, 22)
        Me.TabPage2.Margin = New System.Windows.Forms.Padding(0)
        Me.TabPage2.Name = "TabPage2"
        Me.TabPage2.Size = New System.Drawing.Size(590, 529)
        Me.TabPage2.TabIndex = 1
        Me.TabPage2.Text = "Fragment Program"
        Me.TabPage2.UseVisualStyleBackColor = True
        '
        'frag_tb
        '
        Me.frag_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.frag_tb.AutoIndent = False
        Me.frag_tb.AutoIndentChars = False
        Me.frag_tb.AutoIndentExistingLines = False
        Me.frag_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.frag_tb.BackBrush = Nothing
        Me.frag_tb.BackColor = System.Drawing.Color.Black
        Me.frag_tb.CaretColor = System.Drawing.Color.White
        Me.frag_tb.CharHeight = 14
        Me.frag_tb.CharWidth = 8
        Me.frag_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.frag_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.frag_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.frag_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.frag_tb.ForeColor = System.Drawing.Color.White
        Me.frag_tb.IsReplaceMode = False
        Me.frag_tb.Location = New System.Drawing.Point(0, 0)
        Me.frag_tb.Name = "frag_tb"
        Me.frag_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.frag_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.frag_tb.ServiceColors = CType(resources.GetObject("frag_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.frag_tb.Size = New System.Drawing.Size(590, 529)
        Me.frag_tb.TabIndex = 0
        Me.frag_tb.Zoom = 100
        '
        'TabPage4
        '
        Me.TabPage4.Controls.Add(Me.compute_tb)
        Me.TabPage4.Location = New System.Drawing.Point(4, 22)
        Me.TabPage4.Margin = New System.Windows.Forms.Padding(0)
        Me.TabPage4.Name = "TabPage4"
        Me.TabPage4.Size = New System.Drawing.Size(590, 529)
        Me.TabPage4.TabIndex = 3
        Me.TabPage4.Text = "Compute Program"
        Me.TabPage4.UseVisualStyleBackColor = True
        '
        'compute_tb
        '
        Me.compute_tb.AutoCompleteBracketsList = New Char() {Global.Microsoft.VisualBasic.ChrW(40), Global.Microsoft.VisualBasic.ChrW(41), Global.Microsoft.VisualBasic.ChrW(123), Global.Microsoft.VisualBasic.ChrW(125), Global.Microsoft.VisualBasic.ChrW(91), Global.Microsoft.VisualBasic.ChrW(93), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(34), Global.Microsoft.VisualBasic.ChrW(39), Global.Microsoft.VisualBasic.ChrW(39)}
        Me.compute_tb.AutoIndent = False
        Me.compute_tb.AutoIndentChars = False
        Me.compute_tb.AutoIndentExistingLines = False
        Me.compute_tb.AutoScrollMinSize = New System.Drawing.Size(27, 14)
        Me.compute_tb.BackBrush = Nothing
        Me.compute_tb.BackColor = System.Drawing.Color.Black
        Me.compute_tb.CaretColor = System.Drawing.Color.White
        Me.compute_tb.CharHeight = 14
        Me.compute_tb.CharWidth = 8
        Me.compute_tb.ContextMenuStrip = Me.vertex_context_menustrip
        Me.compute_tb.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.compute_tb.DisabledColor = System.Drawing.Color.FromArgb(CType(CType(100, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer), CType(CType(180, Byte), Integer))
        Me.compute_tb.Dock = System.Windows.Forms.DockStyle.Fill
        Me.compute_tb.ForeColor = System.Drawing.Color.White
        Me.compute_tb.IsReplaceMode = False
        Me.compute_tb.Location = New System.Drawing.Point(0, 0)
        Me.compute_tb.Name = "compute_tb"
        Me.compute_tb.Paddings = New System.Windows.Forms.Padding(0)
        Me.compute_tb.SelectionColor = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(0, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.compute_tb.ServiceColors = CType(resources.GetObject("compute_tb.ServiceColors"), FastColoredTextBoxNS.ServiceColors)
        Me.compute_tb.Size = New System.Drawing.Size(590, 529)
        Me.compute_tb.TabIndex = 2
        Me.compute_tb.Zoom = 100
        '
        'CB1
        '
        Me.CB1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.CB1.BackColor = System.Drawing.Color.FromArgb(CType(CType(33, Byte), Integer), CType(CType(33, Byte), Integer), CType(CType(33, Byte), Integer))
        Me.CB1.Font = New System.Drawing.Font("Microsoft Sans Serif", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.CB1.ForeColor = System.Drawing.Color.White
        Me.CB1.FormattingEnabled = True
        Me.CB1.Location = New System.Drawing.Point(95, 588)
        Me.CB1.Margin = New System.Windows.Forms.Padding(0)
        Me.CB1.Name = "CB1"
        Me.CB1.Size = New System.Drawing.Size(204, 23)
        Me.CB1.TabIndex = 3
        '
        'Label1
        '
        Me.Label1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.Label1.Font = New System.Drawing.Font("Microsoft Sans Serif", 8.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Label1.ForeColor = System.Drawing.Color.White
        Me.Label1.Location = New System.Drawing.Point(0, 588)
        Me.Label1.Margin = New System.Windows.Forms.Padding(0)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(95, 23)
        Me.Label1.TabIndex = 4
        Me.Label1.Text = "Select Program"
        Me.Label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'search_btn
        '
        Me.search_btn.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.search_btn.FlatAppearance.BorderColor = System.Drawing.Color.White
        Me.search_btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gray
        Me.search_btn.Font = New System.Drawing.Font("Microsoft Sans Serif", 8.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.search_btn.ForeColor = System.Drawing.Color.Black
        Me.search_btn.Location = New System.Drawing.Point(95, 558)
        Me.search_btn.Margin = New System.Windows.Forms.Padding(0)
        Me.search_btn.Name = "search_btn"
        Me.search_btn.Size = New System.Drawing.Size(204, 23)
        Me.search_btn.TabIndex = 5
        Me.search_btn.Text = "Search Google for selected text"
        Me.search_btn.UseVisualStyleBackColor = True
        '
        'Button1
        '
        Me.Button1.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.Button1.FlatAppearance.BorderColor = System.Drawing.Color.White
        Me.Button1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gray
        Me.Button1.Font = New System.Drawing.Font("Microsoft Sans Serif", 8.25!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Button1.ForeColor = System.Drawing.Color.Red
        Me.Button1.Location = New System.Drawing.Point(3, 558)
        Me.Button1.Margin = New System.Windows.Forms.Padding(0)
        Me.Button1.Name = "Button1"
        Me.Button1.Size = New System.Drawing.Size(86, 23)
        Me.Button1.TabIndex = 7
        Me.Button1.Text = "On Top"
        Me.Button1.UseVisualStyleBackColor = True
        '
        'Container_panel
        '
        Me.Container_panel.Controls.Add(Me.nuTerra_Image_panel)
        Me.Container_panel.Controls.Add(Me.recompile_bt)
        Me.Container_panel.Controls.Add(Me.TabControl1)
        Me.Container_panel.Controls.Add(Me.CB1)
        Me.Container_panel.Controls.Add(Me.Label1)
        Me.Container_panel.Controls.Add(Me.search_btn)
        Me.Container_panel.Controls.Add(Me.help)
        Me.Container_panel.Controls.Add(Me.Button1)
        Me.Container_panel.Dock = System.Windows.Forms.DockStyle.Fill
        Me.Container_panel.Location = New System.Drawing.Point(0, 0)
        Me.Container_panel.Name = "Container_panel"
        Me.Container_panel.Size = New System.Drawing.Size(599, 614)
        Me.Container_panel.TabIndex = 8
        '
        'nuTerra_Image_panel
        '
        Me.nuTerra_Image_panel.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.nuTerra_Image_panel.BackgroundImage = Global.nuTerra.My.Resources.Resources.topLogo
        Me.nuTerra_Image_panel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch
        Me.nuTerra_Image_panel.Location = New System.Drawing.Point(302, 557)
        Me.nuTerra_Image_panel.Name = "nuTerra_Image_panel"
        Me.nuTerra_Image_panel.Size = New System.Drawing.Size(152, 48)
        Me.nuTerra_Image_panel.TabIndex = 8
        '
        'help
        '
        Me.help.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.help.BackColor = System.Drawing.Color.Transparent
        Me.help.BackgroundImage = Global.nuTerra.My.Resources.Resources.question
        Me.help.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom
        Me.help.FlatAppearance.BorderColor = System.Drawing.Color.White
        Me.help.FlatAppearance.BorderSize = 0
        Me.help.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer))
        Me.help.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer))
        Me.help.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.help.ForeColor = System.Drawing.Color.White
        Me.help.Location = New System.Drawing.Point(457, 573)
        Me.help.Margin = New System.Windows.Forms.Padding(0)
        Me.help.Name = "help"
        Me.help.Size = New System.Drawing.Size(28, 23)
        Me.help.TabIndex = 6
        Me.help.UseVisualStyleBackColor = False
        '
        'frmProgramEditor
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer), CType(CType(64, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(599, 614)
        Me.Controls.Add(Me.Container_panel)
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.Name = "frmProgramEditor"
        Me.Text = "Edit layer_Fragment.txt"
        Me.TopMost = True
        Me.TabControl1.ResumeLayout(False)
        Me.TabPage1.ResumeLayout(False)
        CType(Me.vert_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.vertex_context_menustrip.ResumeLayout(False)
        Me.TabPage5.ResumeLayout(False)
        CType(Me.tessControl_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.TabPage6.ResumeLayout(False)
        CType(Me.tessEvaluation_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.TabPage3.ResumeLayout(False)
        CType(Me.geo_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.TabPage2.ResumeLayout(False)
        CType(Me.frag_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.TabPage4.ResumeLayout(False)
        CType(Me.compute_tb, System.ComponentModel.ISupportInitialize).EndInit()
        Me.Container_panel.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents recompile_bt As System.Windows.Forms.Button
    Friend WithEvents TabControl1 As System.Windows.Forms.TabControl
    Friend WithEvents TabPage1 As System.Windows.Forms.TabPage
    Friend WithEvents TabPage2 As System.Windows.Forms.TabPage
    Friend WithEvents CB1 As System.Windows.Forms.ComboBox
    Friend WithEvents Label1 As System.Windows.Forms.Label
    Friend WithEvents vert_tb As FastColoredTextBoxNS.FastColoredTextBox
    Friend WithEvents frag_tb As FastColoredTextBoxNS.FastColoredTextBox
    Friend WithEvents search_btn As System.Windows.Forms.Button
    Friend WithEvents TabPage3 As System.Windows.Forms.TabPage
    Friend WithEvents geo_tb As FastColoredTextBoxNS.FastColoredTextBox
    Friend WithEvents vertex_context_menustrip As System.Windows.Forms.ContextMenuStrip
    Friend WithEvents ToolStripMenuItem1 As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripMenuItem2 As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ToolStripMenuItem3 As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents TabPage4 As System.Windows.Forms.TabPage
    Friend WithEvents compute_tb As FastColoredTextBoxNS.FastColoredTextBox
    Friend WithEvents help As Button
    Friend WithEvents Button1 As Button
    Friend WithEvents Container_panel As Panel
    Friend WithEvents nuTerra_Image_panel As Panel
    Friend WithEvents TabPage5 As TabPage
    Friend WithEvents tessControl_tb As FastColoredTextBoxNS.FastColoredTextBox
    Friend WithEvents TabPage6 As TabPage
    Friend WithEvents tessEvaluation_tb As FastColoredTextBoxNS.FastColoredTextBox
End Class

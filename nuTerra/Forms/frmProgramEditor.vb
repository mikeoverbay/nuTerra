#Region "imports"
Imports System.IO
Imports System.String
Imports System.Text
Imports FastColoredTextBoxNS
Imports System.Linq
Imports System.Diagnostics
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Runtime.Serialization.Formatters.Binary
Imports System.Text.RegularExpressions
Imports System.Drawing.Drawing2D
Imports System.ComponentModel
#End Region
Public Class frmProgramEditor
#Region "variables"

    Public CP_parent As UInt32

    Private shader_index As Integer

    Const EM_SETTABSTOPS = &HCB
    Private focused_form As New Control
    Dim LightSalmonStyle As TextStyle = New TextStyle(Brushes.LightSalmon, Nothing, FontStyle.Regular)
    Dim BoldStyle As TextStyle = New TextStyle(Nothing, Nothing, FontStyle.Bold Or FontStyle.Underline)
    Dim GrayStyle As TextStyle = New TextStyle(Brushes.Gray, Nothing, FontStyle.Regular)
    Dim PowderBlueStyle As TextStyle = New TextStyle(Brushes.PowderBlue, Nothing, FontStyle.Regular)
    Dim GreenStyle As TextStyle = New TextStyle(Brushes.Green, Nothing, FontStyle.Regular)
    Dim BrownStyle As TextStyle = New TextStyle(Brushes.Brown, Nothing, FontStyle.Italic)
    Dim MaroonStyle As TextStyle = New TextStyle(Brushes.Maroon, Nothing, FontStyle.Regular)
    Dim GLSLstyle As TextStyle = New TextStyle(Brushes.CornflowerBlue, Nothing, FontStyle.Regular)

    Dim SameWordsStyle As MarkerStyle = New MarkerStyle(New SolidBrush(Color.FromArgb(40, Color.Gray)))
#End Region

    Private Sub frmEditFrag_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        'If MsgBox("Save Shader?", MsgBoxStyle.YesNo, "Save?") = MsgBoxResult.Yes Then
        '	File.WriteAllText(v_app_path, vert_tb.Text)
        'End If
    End Sub

    Private Sub frmEditFrag_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        TabControl1.Width = Me.ClientSize.Width
        TabControl1.Height = Me.ClientSize.Height - 60

        vert_tb.AcceptsTab = True
        geo_tb.AcceptsTab = True
        frag_tb.AcceptsTab = True
        compute_tb.AcceptsTab = True

        For i = 0 To shaders.Count - 1
            CB1.Items.Add(shaders(i).program.ToString("00") + " : " + shaders(i).name)
        Next

        recompile_bt.Enabled = False
        Me.Text = "Shader Editor:"
        CP_parent = Me.Handle
    End Sub

    Sub TextBoxSetTabStopDistance(tb As TextBox, ByVal distance As Long)
        '	SendMessage(tb.Handle, EM_SETTABSTOPS, 1, 4)
    End Sub

    Private Sub recompile_bt_Click(sender As Object, e As EventArgs) Handles recompile_bt.Click

        recompile_bt.Enabled = False

        Dim shader = shaders(shader_index)
        If shader.vertex IsNot Nothing Then
            File.WriteAllText(shader.vertex, vert_tb.Text)
        End If

        If shader.tc IsNot Nothing Then
            File.WriteAllText(shader.tc, tessControl_tb.Text)
        End If

        If shader.te IsNot Nothing Then
            File.WriteAllText(shader.te, tessEvaluation_tb.Text)
        End If

        If shader.geo IsNot Nothing Then
            File.WriteAllText(shader.geo, geo_tb.Text)
        End If

        If shader.fragment IsNot Nothing Then
            File.WriteAllText(shader.fragment, frag_tb.Text)
        End If

        If shader.compute IsNot Nothing Then
            File.WriteAllText(shader.compute, compute_tb.Text)
        End If

        Me.TopMost = False

        shader.UpdateShader()

        If shader Is t_mixerShader Then
            RebuildVTAtlas()
        End If

        reset_focus()
        recompile_bt.Enabled = True
        Me.TopMost = True

    End Sub

    Private Sub CB1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CB1.SelectedIndexChanged
        Dim shader_name As String = CB1.Items(CB1.SelectedIndex)
        Me.Text = "Shader Editor: " + shader_name
        shader_index = CB1.SelectedIndex

        Dim shader = shaders(shader_index)

        If shader.vertex IsNot Nothing Then
            vert_tb.Enabled = True
            vert_tb.Text = File.ReadAllText(shader.vertex)
        Else
            vert_tb.Enabled = False
            vert_tb.Text = "NO VERTEX PROGRAM"
        End If

        If shader.tc IsNot Nothing Then
            tessControl_tb.Enabled = True
            tessControl_tb.Text = File.ReadAllText(shader.tc)
        Else
            tessControl_tb.Text = "NO TESS CONTROL PROGRAM"
            tessControl_tb.Enabled = False
        End If

        If shader.te IsNot Nothing Then
            tessEvaluation_tb.Enabled = True
            tessEvaluation_tb.Text = File.ReadAllText(shader.te)
        Else
            tessEvaluation_tb.Text = "NO TESS EVALUATION PROGRAM"
            tessEvaluation_tb.Enabled = False
        End If

        If shader.geo IsNot Nothing Then
            geo_tb.Enabled = True
            geo_tb.Text = File.ReadAllText(shader.geo)
        Else
            geo_tb.Text = "NO GEOM PROGRAM"
            geo_tb.Enabled = False
        End If

        If shader.fragment IsNot Nothing Then
            frag_tb.Enabled = True
            frag_tb.Text = File.ReadAllText(shader.fragment)
        Else
            frag_tb.Text = "NO FRAG PROGRAM"
            frag_tb.Enabled = False
        End If

        If shader.compute IsNot Nothing Then
            compute_tb.Enabled = True
            compute_tb.Text = File.ReadAllText(shader.compute)
        Else
            compute_tb.Text = "NO COMPUTE PROGRAM"
            compute_tb.Enabled = False
        End If

        recompile_bt.Enabled = True
    End Sub


    Private Sub reset_focus()
        If focused_form IsNot Nothing Then
            focused_form.Focus()
        End If
    End Sub

    Private Sub geo_tb_GotFocus(sender As Object, e As EventArgs) Handles geo_tb.GotFocus
        focused_form = geo_tb
    End Sub

    Private Sub compute_tb_GotFocus(sender As Object, e As EventArgs) Handles compute_tb.GotFocus
        focused_form = compute_tb
    End Sub

    Private Sub frag_tb_TextChanged(sender As Object, e As FastColoredTextBoxNS.TextChangedEventArgs) Handles frag_tb.TextChanged
        CSharpSyntaxHighlight(vert_tb, e) 'custom highlighting
    End Sub

    Private Sub vert_tb_TextChanged(sender As Object, e As FastColoredTextBoxNS.TextChangedEventArgs) Handles vert_tb.TextChanged
        CSharpSyntaxHighlight(vert_tb, e) 'custom highlighting
    End Sub
    Private Sub geo_tb_TextChanged(sender As Object, e As TextChangedEventArgs) Handles geo_tb.TextChanged
        CSharpSyntaxHighlight(geo_tb, e) 'custom highlighting
    End Sub
    Private Sub compute_tb_TextChanged(sender As Object, e As TextChangedEventArgs) Handles compute_tb.TextChanged
        CSharpSyntaxHighlight(geo_tb, e) 'custom highlighting
    End Sub

    Private Sub tessControl_tb_TextChanged(sender As Object, e As TextChangedEventArgs) Handles tessControl_tb.TextChanged
        CSharpSyntaxHighlight(geo_tb, e) 'custom highlighting
    End Sub

    Private Sub tessEvaluation_tb_TextChanged(sender As Object, e As TextChangedEventArgs) Handles tessEvaluation_tb.TextChanged
        CSharpSyntaxHighlight(geo_tb, e) 'custom highlighting
    End Sub

    Private Sub CSharpSyntaxHighlight(ByRef sender As FastColoredTextBox, e As TextChangedEventArgs)
        e.ChangedRange.SetFoldingMarkers("", "")
        sender.LeftBracket = "("c
        sender.RightBracket = ")"c
        sender.LeftBracket2 = ControlChars.NullChar
        sender.RightBracket2 = ControlChars.NullChar
        'clear style of changed range
        e.ChangedRange.ClearStyle(LightSalmonStyle, BoldStyle, GrayStyle, PowderBlueStyle, GreenStyle, BrownStyle)

        'string highlighting
        e.ChangedRange.SetStyle(BrownStyle, """""|@""""|''|@"".*?""|(?<!@)(?<range>"".*?[^\\]"")|'.*?[^\\]'")
        'comment highlighting
        e.ChangedRange.SetStyle(GreenStyle, "//.*$", RegexOptions.Multiline)
        e.ChangedRange.SetStyle(GreenStyle, "(/\*.*?\*/)|(/\*.*)", RegexOptions.Singleline)
        e.ChangedRange.SetStyle(GreenStyle, "(/\*.*?\*/)|(.*\*/)", RegexOptions.Singleline Or RegexOptions.RightToLeft)
        'number highlighting
        e.ChangedRange.SetStyle(PowderBlueStyle, "\b\d+[\.]?\d*([eE]\-?\d+)?[lLdDfF]?\b|\b0x[a-fA-F\d]+\b")
        'attribute highlighting
        e.ChangedRange.SetStyle(GrayStyle, "^\s*(?<range>\[.+?\])\s*$", RegexOptions.Multiline)
        'class name highlighting
        'e.ChangedRange.SetStyle(BoldStyle, "\b(class|struct|enum|interface)\s+(?<range>\w+?)\b")
        'keyword highlighting
        e.ChangedRange.SetStyle(LightSalmonStyle, "\b(if|else|discard|break|switch|case|enum|struct|texture2dArray|" +
                                                    "samplerCube|texture2D|sampler2D|texture|textureLod|EmitVertex|EndPrimitive)\b")
        'GLSL keyword highlighting
        e.ChangedRange.SetStyle(GLSLstyle, GLSL_KEYWORDS, RegexOptions.Singleline)

        'clear folding markers
        e.ChangedRange.ClearFoldingMarkers()

        'set folding markers
        e.ChangedRange.SetFoldingMarkers("{", "}")
        'allow to collapse brackets block
        e.ChangedRange.SetFoldingMarkers("#region\b", "#endregion\b")
        'allow to collapse #region blocks
        e.ChangedRange.SetFoldingMarkers("/\*", "\*/")
        'allow to collapse comment block


    End Sub

    Private Sub search_btn_Click(sender As Object, e As EventArgs) Handles search_btn.Click
        Dim s As String = ""
        Dim tab = TabControl1.SelectedIndex
        Select Case tab
            Case 0
                If vert_tb.SelectedText.Length > 0 Then
                    s = vert_tb.SelectedText
                End If
            Case 1
                If frag_tb.SelectedText.Length > 0 Then
                    s = frag_tb.SelectedText.ToString
                End If
            Case 2
                If geo_tb.SelectedText.Length > 0 Then
                    s = geo_tb.SelectedText.ToString
                End If
        End Select

        If s.Length = 0 Then Return
        'www.opengl.org%2Fsdk%2Fdocs%2Fman%2Fhtml%2Fclamp.xhtml
        Dim s2 As String = "https://www.google.com/?gws_rd=ssl#q=" + s

        System.Diagnostics.Process.Start(s2)
        reset_focus()
    End Sub

    'vetext editor
    Private Sub ToolStripMenuItem1_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem1.Click
        CType(sender.Owner.SourceControl, FastColoredTextBox).Cut()
    End Sub

    Private Sub ToolStripMenuItem2_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem2.Click
        CType(sender.Owner.SourceControl, FastColoredTextBox).Copy()
    End Sub

    Private Sub ToolStripMenuItem3_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem3.Click
        CType(sender.Owner.SourceControl, FastColoredTextBox).Paste()
    End Sub

    Private Sub frmEditFrag_HelpButtonClicked(sender As Object, e As CancelEventArgs) Handles Me.HelpButtonClicked
        Dim p = Application.StartupPath + "\HTML\FCTB_HELP.html"
        Process.Start(p)
    End Sub

    Private Sub help_Click(sender As Object, e As EventArgs) Handles help.Click
        Dim p = Application.StartupPath + "\HTML\FCTB_HELP.html"
        Process.Start(p)
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Me.TopMost = Me.TopMost Xor True
        If Me.TopMost = True Then
            Button1.ForeColor = Color.Red
        Else
            Button1.ForeColor = Color.Gray
        End If

    End Sub


    Private Sub Container_panel_MouseDoubleClick(sender As Object, e As MouseEventArgs) Handles Container_panel.MouseDoubleClick
        If frmMain.WindowState = FormWindowState.Minimized Then
            Return
        End If
        If CP_parent = Me.Handle Then
            'frmMain.SplitContainer1.Panel2 IS NOT parent!
            frmMain.panel_2_occupied = True

            If (frmMain.ClientSize.Width - Me.ClientSize.Width) - frmMain.SplitContainer1.SplitterWidth < 50 Then
                MsgBox("Edit window to WIDE to insert in Main Window!", MsgBoxStyle.Exclamation, "To Wide..")
                Return
            End If
            CP_parent = frmMain.Handle
            frmMain.SplitContainer1.Panel2Collapsed = False
            frmMain.SplitContainer1.SplitterDistance = frmMain.ClientSize.Width - Container_panel.Width - frmMain.SplitContainer1.SplitterWidth
            frmMain.SP2_Width = Container_panel.Width
            Container_panel.Parent = frmMain.SplitContainer1.Panel2

            Me.Hide()
            Container_panel.Show()

            frmMain.PropertyGrid1.Hide()

            TabControl1.Focus()
            FBOm.oldWidth = -1.0
            frmMain.resize_fbo_main()
        Else
            'frmMain.SplitContainer1.Panel2 is parent!
            Dim ss = frmMain.PG_width
            frmMain.panel_2_occupied = False
            frmMain.PropertyGrid1.Show()
            If frmMain.m_show_properties.Checked Then
                frmMain.SP2_Width = frmMain.PropertyGrid1.Width
                frmMain.SplitContainer1.SplitterDistance = frmMain.ClientSize.Width - frmMain.PG_width - frmMain.SplitContainer1.SplitterWidth
                frmMain.SplitContainer1.Panel2Collapsed = False
            Else
                frmMain.SplitContainer1.Panel2Collapsed = True
            End If
            CP_parent = Me.Handle
            Me.Show()
            Container_panel.Parent = Me
            TabControl1.Focus()
            FBOm.oldWidth = -1.0
            frmMain.resize_fbo_main()
        End If

    End Sub

End Class

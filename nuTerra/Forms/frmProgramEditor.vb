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

    Private f_app_path As String
    Private v_app_path As String
    Private g_app_path As String
    Private c_app_path As String
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
        TabControl1.Height = Me.ClientSize.Height - CB1.Height - 5
        recompile_bt.Location = New Point(recompile_bt.Location.X, TabControl1.Height + 3)
        search_btn.Location = New Point(search_btn.Location.X, TabControl1.Height + 3)

        vert_tb.AcceptsTab = True
        geo_tb.AcceptsTab = True
        frag_tb.AcceptsTab = True
        compute_tb.AcceptsTab = True

        For i = 0 To shaders.Count - 1
            CB1.Items.Add(shaders(i).program.ToString("00") + " : " + shaders(i).name)
        Next

        recompile_bt.Enabled = False
        Me.Text = "Shader Editor:"
    End Sub
    Sub TextBoxSetTabStopDistance(tb As TextBox, ByVal distance As Long)
        '	SendMessage(tb.Handle, EM_SETTABSTOPS, 1, 4)
    End Sub
    Private Sub recompile_bt_Click(sender As Object, e As EventArgs) Handles recompile_bt.Click

        recompile_bt.Enabled = False

        If shaders(shader_index).vertex IsNot Nothing Then
            File.WriteAllText(v_app_path, vert_tb.Text)
        End If

        If shaders(shader_index).fragment IsNot Nothing Then
            File.WriteAllText(f_app_path, frag_tb.Text)
        End If

        If shaders(shader_index).geo IsNot Nothing Then
            File.WriteAllText(g_app_path, geo_tb.Text)
        End If

        If shaders(shader_index).compute IsNot Nothing Then
            File.WriteAllText(c_app_path, compute_tb.Text)
        End If

        Me.TopMost = False

        shaders(shader_index).UpdateShader()

        reset_focus()
        recompile_bt.Enabled = True
        Me.TopMost = True

    End Sub

    Private Sub CB1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CB1.SelectedIndexChanged
        Dim shader As String = CB1.Items(CB1.SelectedIndex)
        Me.Text = "Shader Editor: " + shader
        shader_index = CB1.SelectedIndex
        v_app_path = shaders(shader_index).vertex
        g_app_path = shaders(shader_index).geo
        f_app_path = shaders(shader_index).fragment
        c_app_path = shaders(shader_index).compute

        If shaders(shader_index).vertex IsNot Nothing Then
            vert_tb.Enabled = True
            vert_tb.Text = File.ReadAllText(v_app_path)
        Else
            vert_tb.Enabled = False
            vert_tb.Text = "NO VERTEX PROGRAM"
        End If

        If shaders(shader_index).fragment IsNot Nothing Then
            frag_tb.Enabled = True
            frag_tb.Text = File.ReadAllText(f_app_path)
        Else
            frag_tb.Text = "NO FRAG PROGRAM"
            frag_tb.Enabled = False
        End If

        If shaders(shader_index).geo IsNot Nothing Then
            geo_tb.Enabled = True
            geo_tb.Text = File.ReadAllText(g_app_path)
        Else
            geo_tb.Text = "NO GEOM PROGRAM"
            geo_tb.Enabled = False
        End If

        If shaders(shader_index).compute IsNot Nothing Then
            compute_tb.Enabled = True
            compute_tb.Text = File.ReadAllText(c_app_path)
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
    Private Sub frag_tb_GotFocus(sender As Object, e As EventArgs)
        focused_form = frag_tb
    End Sub

    Private Sub vert_tb_GotFocus(sender As Object, e As EventArgs)
        focused_form = vert_tb
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
        vert_tb.Cut()
    End Sub

    Private Sub ToolStripMenuItem2_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem2.Click
        vert_tb.Copy()
    End Sub

    Private Sub ToolStripMenuItem3_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem3.Click
        vert_tb.Paste()
    End Sub
    'fragment
    Private Sub ToolStripMenuItem4_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem4.Click
        frag_tb.Cut()
    End Sub

    Private Sub ToolStripMenuItem5_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem5.Click
        frag_tb.Copy()
    End Sub

    Private Sub ToolStripMenuItem6_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem6.Click
        frag_tb.Paste()
    End Sub

    Private Sub ToolStripMenuItem7_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem7.Click
        geo_tb.Cut()
    End Sub

    Private Sub ToolStripMenuItem8_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem8.Click
        geo_tb.Copy()
    End Sub

    Private Sub ToolStripMenuItem9_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem9.Click
        geo_tb.Paste()
    End Sub

    Private Sub ToolStripMenuItem10_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem10.Click
        compute_tb.Cut()
    End Sub

    Private Sub ToolStripMenuItem11_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem11.Click
        compute_tb.Copy()
    End Sub

    Private Sub ToolStripMenuItem12_Click(sender As Object, e As EventArgs) Handles ToolStripMenuItem12.Click
        compute_tb.Paste()
    End Sub

    Private Sub frmEditFrag_HelpButtonClicked(sender As Object, e As CancelEventArgs) Handles Me.HelpButtonClicked
        Dim p = Application.StartupPath + "\HTML\FCTB_HELP.html"
        Process.Start(p)
    End Sub

    Private Sub help_Click(sender As Object, e As EventArgs) Handles help.Click
        Dim p = Application.StartupPath + "\HTML\FCTB_HELP.html"
        Process.Start(p)
    End Sub

    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBox1.CheckedChanged
        Me.TopMost = CheckBox1.Checked
        If Me.TopMost = True Then
            CheckBox1.ForeColor = Color.Red
        Else
            CheckBox1.ForeColor = Color.Black
        End If
    End Sub
End Class

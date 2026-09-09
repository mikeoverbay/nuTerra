Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports ImGuiNET
Imports ImGuiColorTextEditNet
Imports ImGuiColorTextEditNet.Syntax
Imports OpenTK.Graphics.OpenGL4
Imports Num = System.Numerics

''' <summary>
''' The in-app shader IDE. One tab per stage of the selected program, each an
''' editable text box with GLSL highlighting drawn over it, a shader picker
''' grouped by folder, Compile and Exit.
'''
''' SAFETY. Compile first assembles a THROWAWAY program from temp copies of
''' the edited text (assemble_shader, the same path every startup compile
''' takes). Only a program that compiles and links is allowed to replace the
''' running one, and only then are the files written - the bin copy the app
''' reads and the source copy under the project's shaders folder, so the
''' change survives a rebuild. A failed compile touches nothing on disk and
''' the frame keeps rendering with the old program.
'''
''' The copy saved when a shader is opened is what Revert restores. Exit and
''' switching shaders ask about it when the last compile failed.
'''
''' Highlighting: InputTextMultiline cannot colour its own text, so the input
''' is drawn with a transparent text colour and the coloured tokens are
''' painted over it, line by line, in the same monospaced font at the same
''' scroll. The cursor and selection are ImGui's own.
''' </summary>
Public Class ShaderIDE

    Public Shared Open As Boolean = False

    Private Class Stage
        Public label As String        ' "vert", "frag", ...
        Public ext As String          ' ".vert"
        Public binPath As String      ' the file the app compiles
        Public srcPath As String      ' the project copy, or Nothing when not found
        Public text As String = ""
        Public original As String = ""
        Public dirty As Boolean
        ''' <summary>True when the file on disk started with a UTF-8 BOM.
        ''' Remembered so writing it back does not quietly change bytes the
        ''' operator never touched - see WriteText.</summary>
        Public hadBom As Boolean
        ''' <summary>The editor widget. It keeps its own text, cursor,
        ''' selection and undo stack across frames, so it is per stage and
        ''' lives as long as the stage does.</summary>
        Public editor As TextEditor
        ''' <summary>The editor's Version at the last pull. It ticks on every
        ''' modification, so comparing it is how we know to read AllText back
        ''' - which joins every line and is not something to do per frame.
        ''' </summary>
        Public lastVersion As Long
    End Class

    Private Shared current As Shader = Nothing
    Private Shared stages As New List(Of Stage)
    Private Shared status As String = "pick a shader"
    Private Shared errorText As String = ""
    Private Shared lastBuildOk As Boolean = True
    Private Shared everCompiled As Boolean = False
    Private Shared pendingClose As Boolean = False
    Private Shared pendingSwitch As Shader = Nothing
    Private Shared askRevert As Boolean = False

    ''' <summary>The button row plus the separators around it, pixels.</summary>
    Private Const BUTTON_ROW_H As Single = 48.0F
    ''' <summary>The compiler-output box when it is shown, pixels.</summary>
    Private Const ERROR_BOX_H As Single = 80.0F

    ' ---- colours (ABGR as ImGui packs them) --------------------------------
    Private Shared ReadOnly C_DEFAULT As UInteger = Pack(214, 214, 214)
    Private Shared ReadOnly C_KEYWORD As UInteger = Pack(86, 156, 214)
    Private Shared ReadOnly C_TYPE As UInteger = Pack(78, 201, 176)
    Private Shared ReadOnly C_BUILTIN As UInteger = Pack(220, 220, 140)
    Private Shared ReadOnly C_NUMBER As UInteger = Pack(181, 206, 168)
    Private Shared ReadOnly C_COMMENT As UInteger = Pack(106, 153, 85)
    Private Shared ReadOnly C_PREPROC As UInteger = Pack(197, 134, 192)
    Private Shared ReadOnly C_STRING As UInteger = Pack(206, 145, 120)
    Private Shared ReadOnly C_GUTTER As UInteger = Pack(110, 118, 128)

    Private Shared Function Pack(r As Integer, g As Integer, b As Integer) As UInteger
        Return CUInt(&HFF000000UI Or (CUInt(b) << 16) Or (CUInt(g) << 8) Or CUInt(r))
    End Function


    ' ------------------------------------------------------------------------
    ''' <summary>Draw the window. Called once per ImGui frame from Window.vb.</summary>
    Public Shared Sub Draw()
        If Not Open Then Return

        Dim vp = ImGui.GetMainViewport()
        ImGui.SetNextWindowSize(New Num.Vector2(Math.Min(1100.0F, vp.WorkSize.X - 80.0F),
                                                Math.Min(760.0F, vp.WorkSize.Y - 80.0F)), ImGuiCond.FirstUseEver)
        ImGui.SetNextWindowPos(vp.WorkPos + New Num.Vector2(40.0F, 40.0F), ImGuiCond.FirstUseEver)
        Dim keepOpen = True
        If ImGui.Begin("Shader IDE###ShaderIDE", keepOpen, ImGuiWindowFlags.NoCollapse) Then
            ' Drag it back on screen if it is not. The size and position are
            ' remembered in imgui.ini between runs, and a stored position from a
            ' bigger window - or one saved while the app was a different shape -
            ' can leave the title bar above the top edge. There is no way to
            ' grab a title bar that is not on screen, so without this the window
            ' is stuck off the top for good and the shader picker is the first
            ' thing to disappear under it. Only applied when it is actually out
            ' of bounds, so dragging still works normally.
            Dim sz = ImGui.GetWindowSize()
            Dim fit = New Num.Vector2(Math.Min(sz.X, vp.WorkSize.X), Math.Min(sz.Y, vp.WorkSize.Y))
            If fit.X <> sz.X OrElse fit.Y <> sz.Y Then ImGui.SetWindowSize(fit)

            Dim pos = ImGui.GetWindowPos()
            Dim onScreen = New Num.Vector2(Math.Min(Math.Max(pos.X, vp.WorkPos.X), vp.WorkPos.X + vp.WorkSize.X - fit.X),
                                           Math.Min(Math.Max(pos.Y, vp.WorkPos.Y), vp.WorkPos.Y + vp.WorkSize.Y - fit.Y))
            If onScreen.X <> pos.X OrElse onScreen.Y <> pos.Y Then ImGui.SetWindowPos(onScreen)

            DrawPicker()
            ImGui.Separator()
            DrawTabs()
            ImGui.Separator()
            DrawBottomBar()
            DrawRevertModal()
        End If
        ImGui.End()

        ' The title-bar close goes through the same guard as the Exit button.
        If Not keepOpen Then RequestClose()
    End Sub

    ' ------------------------------------------------------------------------
    Private Shared Sub DrawPicker()
        Dim preview = If(current Is Nothing, "select a shader...", FolderOf(current) & " / " & current.name)
        ImGui.SetNextItemWidth(420)
        If ImGui.BeginCombo("##shader_pick", preview) Then
            ' Grouped by the folder the files live in under shaders\.
            Dim groups As New SortedDictionary(Of String, List(Of Shader))(StringComparer.OrdinalIgnoreCase)
            For Each s In shaders
                Dim f = FolderOf(s)
                If f = "" Then Continue For
                If Not groups.ContainsKey(f) Then groups(f) = New List(Of Shader)
                groups(f).Add(s)
            Next
            For Each g In groups
                ImGui.TextDisabled(g.Key)
                g.Value.Sort(Function(a, b) String.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase))
                For Each s In g.Value
                    ' Selection is marked in the text: ImGui.Selectable with more
                    ' than one argument is ambiguous from VB.
                    Dim mark = If(s Is current, "> ", "   ")
                    If ImGui.Selectable(mark & s.name) Then RequestSelect(s)
                Next
            Next
            ImGui.EndCombo()
        End If
        ImGui.SameLine()
        ImGui.TextDisabled(If(current Is Nothing, "", StagesSummary()))
    End Sub

    Private Shared Function StagesSummary() As String
        Dim sb As New StringBuilder
        For Each st In stages
            If sb.Length > 0 Then sb.Append("  ")
            sb.Append(st.label)
            If st.dirty Then sb.Append("*")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>The folder name under shaders\ of a program's first stage file.</summary>
    Private Shared Function FolderOf(s As Shader) As String
        For Each p In {s.vertex, s.fragment, s.geo, s.compute, s.tc, s.te}
            If String.IsNullOrEmpty(p) Then Continue For
            Dim dir = Path.GetDirectoryName(p)
            Dim root = Path.Combine(Application.StartupPath, "shaders")
            If dir.Length <= root.Length Then Return "(root)"
            Return dir.Substring(root.Length).Trim("\"c, "/"c)
        Next
        Return ""
    End Function

    ' ------------------------------------------------------------------------
    Private Shared Sub DrawTabs()
        If current Is Nothing Then
            ImGui.TextDisabled("No shader selected.")
            ImGui.Dummy(New Num.Vector2(0, ImGui.GetContentRegionAvail().Y - BottomBarHeight()))
            Return
        End If
        If ImGui.BeginTabBar("##stages", ImGuiTabBarFlags.None) Then
            For Each st In stages
                Dim title = st.label & If(st.dirty, "*", "") & "###tab_" & st.label
                ' The plain overload, deliberately. Selecting a tab from code
                ' needs ImGuiTabItemFlags.SetSelected, and every ImGui.NET
                ' BeginTabItem overload that takes flags also takes p_open by
                ' reference - it has no way to pass the null ImGui reads as "no
                ' close button", so asking for the flag would put an X on every
                ' tab. The failing stage is named in the status line instead.
                If ImGui.BeginTabItem(title) Then
                    DrawEditor(st)
                    ImGui.EndTabItem()
                End If
            Next
            ImGui.EndTabBar()
        End If
    End Sub

    ''' <summary>The editor widget for one stage.</summary>
    Private Shared Sub DrawEditor(st As Stage)
        ' Measured HERE, inside the tab, not before the tab bar was submitted -
        ' the bar is an item like any other and has already taken its ~26 px out
        ' of the content region by now. Taking the height earlier left the
        ' editor a tab-bar too tall and pushed the button row off the bottom
        ' edge of the window. Asking at the point of use cannot drift.
        Dim height = Math.Max(80.0F, ImGui.GetContentRegionAvail().Y - BottomBarHeight())

        Dim mono = ImGuiController.HAS_MONO
        If mono Then ImGui.PushFont(ImGuiController.MONO_FONT)

        ' The widget draws its own gutter, cursor, selection and highlight, and
        ' handles its own keys - so there is nothing to paint over it and no
        ' scroll to chase. Width 0 is ImGui's "fill what is left".
        st.editor.Renderer.IsShowingWhitespace = False
        st.editor.Render("##src_" & st.label, New Num.Vector2(0, height))

        ' Pull the text back only when it actually changed. Version ticks on
        ' every edit; AllText joins every line, so reading it per frame would
        ' cost the same as the megabyte copy this widget replaced.
        If st.editor.Version <> st.lastVersion Then
            st.lastVersion = st.editor.Version
            st.text = st.editor.AllText
            st.dirty = (st.text <> st.original)
        End If

        If mono Then ImGui.PopFont()
        If Not mono Then ImGui.TextDisabled("(no monospaced font found - the editor is drawn in the UI font)")
    End Sub

    ' ------------------------------------------------------------------------
    Private Shared Sub DrawBottomBar()
        Dim okColor = New Num.Vector4(0.55F, 0.85F, 0.55F, 1)
        Dim badColor = New Num.Vector4(0.95F, 0.45F, 0.45F, 1)

        If ImGui.Button("Compile", New Num.Vector2(120, 0)) Then Compile()
        If ImGui.IsItemHovered() Then ImGui.SetTooltip("Assembles a throwaway program from the edited text first." & vbLf &
                                                        "Only a program that links replaces the running one and" & vbLf &
                                                        "writes the files (bin and project copy).")
        ImGui.SameLine()
        If ImGui.Button("Reload from disk", New Num.Vector2(140, 0)) Then
            If current IsNot Nothing Then LoadStages(current)
        End If
        ImGui.SameLine()
        If ImGui.Button("Revert to opened copy", New Num.Vector2(180, 0)) Then RevertToOriginal()
        ImGui.SameLine()
        If ImGui.Button("Exit", New Num.Vector2(100, 0)) Then RequestClose()
        ImGui.SameLine()
        ImGui.TextColored(If(lastBuildOk, okColor, badColor), status)

        ' The compiler's words, scrollable - and ONLY when it said something.
        ' Drawn unconditionally it is an empty box the height of five lines,
        ' filled with the theme's frame colour, sitting under the buttons with
        ' nothing to explain it. The status line beside the buttons already
        ' says whether the last compile passed.
        If errorText <> "" Then
            Dim errBuf = errorText
            ImGui.PushStyleColor(ImGuiCol.Text, If(lastBuildOk, New Num.Vector4(0.75F, 0.75F, 0.75F, 1), badColor))
            ImGui.InputTextMultiline("##errors", errBuf, CUInt(Math.Max(1, errBuf.Length + 1)),
                                     New Num.Vector2(-1, ERROR_BOX_H), ImGuiInputTextFlags.ReadOnly)
            ImGui.PopStyleColor()
        End If
    End Sub

    ''' <summary>What DrawTabs must leave below the editor: the button row, and
    ''' the compiler's box when there is one. Both callers ask, so the editor
    ''' cannot drift out of step with what is actually drawn.</summary>
    Private Shared Function BottomBarHeight() As Single
        Return BUTTON_ROW_H + If(errorText = "", 0.0F, ERROR_BOX_H + 8.0F)
    End Function

    ' ------------------------------------------------------------------------
    ''' <summary>Read every stage the program has, from the bin copy, and remember the text as the revert point.</summary>
    Private Shared Sub LoadStages(s As Shader)
        stages.Clear()
        For Each pair In {New With {.p = s.vertex, .l = "vert"}, New With {.p = s.tc, .l = "tesc"}, New With {.p = s.te, .l = "tese"},
                          New With {.p = s.geo, .l = "geom"}, New With {.p = s.compute, .l = "comp"}, New With {.p = s.fragment, .l = "frag"}}
            If String.IsNullOrEmpty(pair.p) OrElse Not File.Exists(pair.p) Then Continue For
            Dim st As New Stage With {.label = pair.l, .ext = Path.GetExtension(pair.p), .binPath = pair.p}
            st.srcPath = SourcePathFor(pair.p)
            st.text = File.ReadAllText(pair.p)
            st.hadBom = StartsWithBom(pair.p)
            st.original = st.text
            st.dirty = False
            st.editor = NewEditor(st.text)
            st.lastVersion = st.editor.Version
            stages.Add(st)
        Next
        current = s
        lastBuildOk = True
        everCompiled = False
        errorText = ""
        status = String.Format("{0}: {1} stage(s) loaded{2}", s.name, stages.Count,
                               If(stages.Count > 0 AndAlso stages(0).srcPath Is Nothing, "  (project copy not found - bin only)", ""))
    End Sub

    ''' <summary>Does the file begin with a UTF-8 BOM?</summary>
    Private Shared Function StartsWithBom(path As String) As Boolean
        Try
            Dim head(2) As Byte
            Using fs = File.OpenRead(path)
                If fs.Read(head, 0, 3) < 3 Then Return False
            End Using
            Return head(0) = &HEF AndAlso head(1) = &HBB AndAlso head(2) = &HBF
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Write a stage back the way it was read.
    '''
    ''' File.ReadAllText eats a UTF-8 BOM and File.WriteAllText does not put one
    ''' back, so the plain pair silently drops three bytes off the front of
    ''' every file the IDE saves or reverts. That showed up as every touched
    ''' shader appearing modified in git with one changed line and no visible
    ''' difference. The IDE has no business changing bytes nobody asked it to.
    ''' </summary>
    Private Shared Sub WriteText(path As String, text As String, bom As Boolean)
        File.WriteAllText(path, text, New Text.UTF8Encoding(bom))
    End Sub

    ''' <summary>
    ''' A fresh editor holding <paramref name="text"/>.
    '''
    ''' The highlighter is the library's C-style one. It is the ONLY one that
    ''' ships: the package carries a LanguageDefinition.Glsl() but nothing in
    ''' it consumes a LanguageDefinition - the regex-driven highlighting was
    ''' never ported from the C++ original - and ISyntaxHighlighter cannot be
    ''' implemented here, because its Colorize takes a Span(Of Glyph) and VB
    ''' has no way to name a ByRef-like type in a signature. So GLSL gets
    ''' C's comments, strings, numbers, preprocessor and keywords, and its own
    ''' types and builtins - vec3, normalize - read as plain identifiers.
    ''' Recovering those means a C# assembly for the one interface.
    ''' </summary>
    Private Shared Function NewEditor(text As String) As TextEditor
        Dim ed As New TextEditor()
        ed.SyntaxHighlighter = New CStyleHighlighter(True)
        ed.Options.TabSize = 4
        ed.Options.IndentWithSpaces = False
        ed.AllText = If(text, "")

        ' Keep the colours the hand-rolled highlighter used, so the swap does
        ' not also change what the code looks like.
        ed.SetColor(PaletteIndex.Default, C_DEFAULT)
        ed.SetColor(PaletteIndex.Keyword, C_KEYWORD)
        ed.SetColor(PaletteIndex.KnownIdentifier, C_TYPE)
        ed.SetColor(PaletteIndex.Identifier, C_DEFAULT)
        ed.SetColor(PaletteIndex.PreprocIdentifier, C_BUILTIN)
        ed.SetColor(PaletteIndex.Number, C_NUMBER)
        ed.SetColor(PaletteIndex.String, C_STRING)
        ed.SetColor(PaletteIndex.CharLiteral, C_STRING)
        ed.SetColor(PaletteIndex.Comment, C_COMMENT)
        ed.SetColor(PaletteIndex.MultiLineComment, C_COMMENT)
        ed.SetColor(PaletteIndex.Preprocessor, C_PREPROC)

        ' The marker's tooltip is the compiler's own line, verbatim.
        ed.ErrorMarkers.ErrorMarkerFormatter = Function(o As Object) Convert.ToString(o)
        Return ed
    End Function

    ''' <summary>Put <paramref name="text"/> into both the stage and its
    ''' editor. Anything that changes the text from outside the editor - a
    ''' load, a revert - has to come through here, or the widget keeps showing
    ''' what it had and the next keystroke writes the stale copy back.</summary>
    Private Shared Sub SetStageText(st As Stage, text As String)
        st.text = text
        If st.editor Is Nothing Then
            st.editor = NewEditor(text)
        Else
            st.editor.AllText = text
        End If
        st.lastVersion = st.editor.Version
    End Sub

    ''' <summary>
    ''' bin\...\shaders\X\y.frag -> &lt;project&gt;\shaders\X\y.frag. The bin sits
    ''' three folders under the project (bin\Debug\net8.0-windows), so walk up
    ''' and check the file really exists there; Nothing when it does not.
    ''' </summary>
    Private Shared Function SourcePathFor(binPath As String) As String
        Try
            Dim binRoot = Path.GetFullPath(Path.Combine(Application.StartupPath, "shaders"))
            Dim full = Path.GetFullPath(binPath)
            If Not full.StartsWith(binRoot, StringComparison.OrdinalIgnoreCase) Then Return Nothing
            Dim rel = full.Substring(binRoot.Length).TrimStart("\"c, "/"c)
            Dim projRoot = Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "..", "..", "shaders"))
            Dim src = Path.Combine(projRoot, rel)
            Return If(File.Exists(src), src, Nothing)
        Catch
            Return Nothing
        End Try
    End Function

    ' ------------------------------------------------------------------------
    ''' <summary>Try-compile from temp copies; on success write the files and rebuild the live program.</summary>
    Private Shared Sub Compile()
        If current Is Nothing OrElse stages.Count = 0 Then Return
        Dim tmpDir = Path.Combine(Path.GetTempPath(), "nuTerra", "shader_ide")
        Directory.CreateDirectory(tmpDir)

        Dim tmp As New Dictionary(Of String, String)
        For Each st In stages
            Dim p = Path.Combine(tmpDir, current.name & st.ext)
            WriteText(p, st.text, st.hadBom)
            tmp(st.label) = p
        Next
        Dim pick = Function(l As String) If(tmp.ContainsKey(l), tmp(l), Nothing)

        LAST_SHADER_ERROR = ""
        Dim trial = assemble_shader(pick("vert"), pick("tesc"), pick("tese"), pick("geom"), pick("comp"), pick("frag"),
                                    current.name & " (ide)", current.DefinesCopy)
        ClearErrorMarkers()
        If trial = 0 Then
            lastBuildOk = False
            everCompiled = True
            errorText = LAST_SHADER_ERROR.Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
            status = "compile FAILED - nothing written, the old program is still running"
            MarkErrors(errorText)
            Return
        End If
        GL.DeleteProgram(trial)

        ' It links. Now the real files, then the live program from them.
        Dim wrote As New StringBuilder
        For Each st In stages
            WriteText(st.binPath, st.text, st.hadBom)
            If st.srcPath IsNot Nothing Then WriteText(st.srcPath, st.text, st.hadBom)
            st.dirty = False
            wrote.Append(st.label).Append(" ")
        Next
        LAST_SHADER_ERROR = ""
        current.UpdateShader()
        If current.program = 0 Then
            ' Should not happen - the same text just linked - but say so loudly.
            lastBuildOk = False
            everCompiled = True
            errorText = LAST_SHADER_ERROR
            status = "trial linked but the live rebuild failed - see the log"
            Return
        End If
        lastBuildOk = True
        everCompiled = True
        errorText = ""
        status = String.Format("compiled OK {0:HH:mm:ss}  - wrote {1}{2}", Date.Now, wrote.ToString().Trim(),
                               If(stages(0).srcPath Is Nothing, " (bin only)", " (bin + project)"))
    End Sub

    Private Shared Sub ClearErrorMarkers()
        For Each st In stages
            If st.editor IsNot Nothing Then st.editor.ErrorMarkers.SetErrorMarkers(New Dictionary(Of Integer, Object))
        Next
    End Sub

    ''' <summary>
    ''' Put the driver's complaints on the lines they name.
    '''
    ''' ShaderLoader.gl_error prefixes each stage's log with "&lt;name&gt;_vertex
    ''' didn't compile!" and the like, so the text arrives already divided by
    ''' stage - that header is what says WHICH editor a line number belongs to,
    ''' and without it a number is just a number.
    '''
    ''' Two line formats, because the vendors disagree: NVIDIA writes
    ''' "0(123) : error C1503:" and the Mesa / AMD family writes
    ''' "ERROR: 0:123: ...". Anything that matches neither is still shown in
    ''' the box below; it simply gets no marker.
    ''' </summary>
    Private Shared Sub MarkErrors(text As String)
        If String.IsNullOrEmpty(text) OrElse stages.Count = 0 Then Return

        Dim marks As New Dictionary(Of String, Dictionary(Of Integer, Object))
        Dim stage As Stage = Nothing
        Dim firstBad As String = Nothing
        Dim firstLine As Integer = -1

        For Each raw In text.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
            Dim line = raw.Trim()
            If line.Length = 0 Then Continue For

            If line.IndexOf("didn't compile!", StringComparison.OrdinalIgnoreCase) >= 0 Then
                stage = StageForHeader(line)
                Continue For
            End If
            If stage Is Nothing Then Continue For

            ' Line 0 is the driver saying "the file", not a line - an EOF
            ' error reports it. There is nothing to put a marker on, so the
            ' message stays in the box below and out of the gutter.
            Dim n = LineNumberIn(line)
            If n <= 0 Then Continue For
            If Not marks.ContainsKey(stage.label) Then marks(stage.label) = New Dictionary(Of Integer, Object)
            ' First complaint per line wins - later ones are usually knock-ons
            ' of the same mistake and the marker shows one string.
            If Not marks(stage.label).ContainsKey(n) Then marks(stage.label)(n) = line
            If firstBad Is Nothing Then
                firstBad = stage.label
                firstLine = n
            End If
        Next

        For Each st In stages
            If st.editor Is Nothing Then Continue For
            If marks.ContainsKey(st.label) Then st.editor.ErrorMarkers.SetErrorMarkers(marks(st.label))
        Next

        ' Say where it went wrong, and park that editor at the line so the
        ' tab is already showing it when it is clicked.
        If firstBad IsNot Nothing Then
            For Each st In stages
                If st.label = firstBad AndAlso st.editor IsNot Nothing Then st.editor.ScrollToLine(firstLine)
            Next
            status = String.Format("compile FAILED in {0} at line {1} - nothing written, the old program is still running",
                                   firstBad, firstLine)
        End If
    End Sub

    ''' <summary>Which stage a "... didn't compile!" header is talking about.
    ''' The vertex, fragment and geometry logs are named by gl_error from the
    ''' program name; the tessellation ones carry the file name, so match on
    ''' the extension there.</summary>
    Private Shared Function StageForHeader(header As String) As Stage
        Dim h = header.ToLowerInvariant()
        Dim want As String = Nothing
        If h.Contains("_vertex") OrElse h.Contains(".vert") Then
            want = "vert"
        ElseIf h.Contains("_fragment") OrElse h.Contains(".frag") Then
            want = "frag"
        ElseIf h.Contains("_geo") OrElse h.Contains(".geom") Then
            want = "geom"
        ElseIf h.Contains(".tesc") Then
            want = "tesc"
        ElseIf h.Contains(".tese") Then
            want = "tese"
        ElseIf h.Contains("_comp") OrElse h.Contains(".comp") Then
            want = "comp"
        End If
        If want Is Nothing Then Return Nothing
        For Each st In stages
            If st.label = want Then Return st
        Next
        Return Nothing
    End Function

    ''' <summary>The source line a driver message names, or -1.</summary>
    Private Shared Function LineNumberIn(line As String) As Integer
        ' NVIDIA: "0(123) : error C1503: ..." - and the source-string index in
        ' front is NOT always there. A whole-file complaint comes back as
        ' "(0) : error C0000: syntax error, unexpected $end" with nothing before
        ' the bracket, so the index has to be optional or every message from
        ' this driver is skipped. Seen on a real GeForce, not guessed.
        Dim m = Text.RegularExpressions.Regex.Match(line, "^\s*\d*\((\d+)\)\s*:")
        If m.Success Then Return CInt(m.Groups(1).Value)
        ' Mesa / AMD: ERROR: 0:123: ...
        m = Text.RegularExpressions.Regex.Match(line, "^(?:ERROR|WARNING):\s*\d+:(\d+):")
        If m.Success Then Return CInt(m.Groups(1).Value)
        Return -1
    End Function

    ''' <summary>Put the opened copy back - in the editor, on disk, and in the live program.</summary>
    Private Shared Sub RevertToOriginal()
        If current Is Nothing Then Return
        For Each st In stages
            SetStageText(st, st.original)
            st.dirty = False
            WriteText(st.binPath, st.original, st.hadBom)
            If st.srcPath IsNot Nothing Then WriteText(st.srcPath, st.original, st.hadBom)
        Next
        LAST_SHADER_ERROR = ""
        current.UpdateShader()
        lastBuildOk = (current.program <> 0)
        errorText = If(lastBuildOk, "", LAST_SHADER_ERROR)
        status = If(lastBuildOk, "reverted to the copy saved when the shader was opened", "reverted, but the original does not compile - see the log")
    End Sub

    ' ------------------------------------------------------------------------
    Private Shared Sub RequestClose()
        If everCompiled AndAlso Not lastBuildOk Then
            pendingClose = True
            askRevert = True
        Else
            Open = False
        End If
    End Sub

    Private Shared Sub RequestSelect(s As Shader)
        If s Is current Then Return
        If everCompiled AndAlso Not lastBuildOk Then
            pendingSwitch = s
            askRevert = True
        Else
            LoadStages(s)
        End If
    End Sub

    ''' <summary>The safeguard: the last compile failed, so ask before leaving the shader.</summary>
    Private Shared Sub DrawRevertModal()
        If askRevert Then
            ImGui.OpenPopup("Last compile failed###ide_revert")
            askRevert = False
        End If
        Dim opened = True
        If ImGui.BeginPopupModal("Last compile failed###ide_revert", opened, ImGuiWindowFlags.AlwaysAutoResize) Then
            ImGui.Text("The last compile of " & If(current Is Nothing, "", current.name) & " failed.")
            ImGui.Text("The files on disk hold the last text that compiled during this session.")
            ImGui.Text("Revert them to the copy saved when the shader was opened?")
            ImGui.Separator()
            If ImGui.Button("Revert, then continue", New Num.Vector2(180, 0)) Then
                RevertToOriginal()
                FinishPending()
                ImGui.CloseCurrentPopup()
            End If
            ImGui.SameLine()
            If ImGui.Button("Keep files, continue", New Num.Vector2(180, 0)) Then
                FinishPending()
                ImGui.CloseCurrentPopup()
            End If
            ImGui.SameLine()
            If ImGui.Button("Cancel", New Num.Vector2(100, 0)) Then
                pendingClose = False
                pendingSwitch = Nothing
                ImGui.CloseCurrentPopup()
            End If
            ImGui.EndPopup()
        End If
    End Sub

    Private Shared Sub FinishPending()
        If pendingClose Then
            pendingClose = False
            Open = False
        ElseIf pendingSwitch IsNot Nothing Then
            Dim s = pendingSwitch
            pendingSwitch = Nothing
            LoadStages(s)
        End If
    End Sub

End Class

Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports ImGuiNET
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
        Public lines As New List(Of List(Of Token))   ' highlight cache
        Public cachedFor As String = Nothing
    End Class

    Private Structure Token
        Public col As Integer
        Public text As String
        Public color As UInteger
    End Structure

    Private Shared current As Shader = Nothing
    Private Shared stages As New List(Of Stage)
    Private Shared status As String = "pick a shader"
    Private Shared errorText As String = ""
    Private Shared lastBuildOk As Boolean = True
    Private Shared everCompiled As Boolean = False
    Private Shared pendingClose As Boolean = False
    Private Shared pendingSwitch As Shader = Nothing
    Private Shared askRevert As Boolean = False
    ''' <summary>The smallest native buffer an editor asks for. The real one
    ''' is sized to the file - see DrawEditor.</summary>
    Private Const MIN_TEXT_CAP As Long = 64 * 1024
    ''' <summary>Space either side of the line numbers, pixels.</summary>
    Private Const GUTTER_PAD As Single = 6.0F

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

    Private Shared ReadOnly KEYWORDS As New HashSet(Of String)({
        "if", "else", "for", "while", "do", "return", "discard", "break", "continue", "switch", "case", "default",
        "in", "out", "inout", "uniform", "layout", "const", "flat", "smooth", "noperspective", "varying", "attribute",
        "buffer", "shared", "subroutine", "precision", "highp", "mediump", "lowp", "struct", "true", "false",
        "binding", "location", "std140", "std430", "early_fragment_tests", "readonly", "writeonly", "coherent", "volatile", "restrict"})
    Private Shared ReadOnly TYPES As New HashSet(Of String)({
        "void", "bool", "int", "uint", "float", "double",
        "vec2", "vec3", "vec4", "ivec2", "ivec3", "ivec4", "uvec2", "uvec3", "uvec4", "bvec2", "bvec3", "bvec4", "dvec2", "dvec3", "dvec4",
        "mat2", "mat3", "mat4", "mat2x2", "mat3x3", "mat4x4", "mat3x4", "mat4x3",
        "sampler1D", "sampler2D", "sampler3D", "samplerCube", "sampler2DArray", "samplerCubeArray", "sampler2DShadow", "samplerCubeArrayShadow", "sampler2DArrayShadow",
        "isampler2D", "usampler2D", "image2D", "atomic_uint"})
    Private Shared ReadOnly BUILTINS As New HashSet(Of String)({
        "gl_Position", "gl_FragCoord", "gl_VertexID", "gl_InstanceID", "gl_FragDepth", "gl_PointSize", "gl_BaseInstanceARB", "gl_DrawID",
        "texture", "textureLod", "texelFetch", "textureSize", "textureGrad", "textureProj",
        "normalize", "dot", "cross", "mix", "clamp", "pow", "exp", "exp2", "log", "log2", "sqrt", "inversesqrt",
        "max", "min", "abs", "sign", "length", "distance", "reflect", "refract", "smoothstep", "step", "fract", "floor", "ceil", "round", "mod",
        "sin", "cos", "tan", "asin", "acos", "atan", "inverse", "transpose", "dFdx", "dFdy", "fwidth", "any", "all", "not",
        "greaterThan", "lessThan", "equal", "atomicCounterIncrement", "imageStore", "imageLoad", "barrier", "main"})

    ' ------------------------------------------------------------------------
    ''' <summary>Draw the window. Called once per ImGui frame from Window.vb.</summary>
    Public Shared Sub Draw()
        If Not Open Then Return

        ImGui.SetNextWindowSize(New Num.Vector2(1100, 760), ImGuiCond.FirstUseEver)
        Dim keepOpen = True
        If ImGui.Begin("Shader IDE###ShaderIDE", keepOpen, ImGuiWindowFlags.NoCollapse) Then
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
            ImGui.Dummy(New Num.Vector2(0, ImGui.GetContentRegionAvail().Y - 130))
            Return
        End If
        Dim editorH = ImGui.GetContentRegionAvail().Y - 130
        If ImGui.BeginTabBar("##stages", ImGuiTabBarFlags.None) Then
            For Each st In stages
                Dim title = st.label & If(st.dirty, "*", "") & "###tab_" & st.label
                If ImGui.BeginTabItem(title) Then
                    DrawEditor(st, editorH)
                    ImGui.EndTabItem()
                End If
            Next
            ImGui.EndTabBar()
        End If
    End Sub

    ''' <summary>The line-number gutter, the editable box, and the highlight
    ''' painted over it.</summary>
    Private Shared Sub DrawEditor(st As Stage, height As Single)
        Dim mono = ImGuiController.HAS_MONO
        If mono Then ImGui.PushFont(ImGuiController.MONO_FONT)

        Dim label = "##src_" & st.label

        ' The gutter is RESERVED here, before the box, rather than painted over
        ' it - so the numbers and the text can never overlap however long a
        ' line gets - and its width follows the line count, because a
        ' four-figure shader needs a wider column than a two-figure one. The
        ' tokens are wanted for the count anyway; EnsureHighlight is the same
        ' call the paint makes and returns immediately the second time.
        EnsureHighlight(st)
        Dim charW = ImGui.CalcTextSize("M").X
        Dim digits = Math.Max(3, st.lines.Count.ToString().Length)
        Dim gutterW = charW * digits + 2.0F * GUTTER_PAD
        Dim gutterX = ImGui.GetCursorScreenPos().X
        ImGui.Dummy(New Num.Vector2(gutterW, height))
        ImGui.SameLine(0.0F, 0.0F)

        Dim size As New Num.Vector2(-1, height)

        ' The input draws its own text transparent; the overlay below draws it
        ' coloured. Cursor and selection stay ImGui's, in the normal colours.
        ImGui.PushStyleColor(ImGuiCol.Text, New Num.Vector4(0, 0, 0, 0))
        Dim buf = st.text
        ' Sized to the FILE, not a flat megabyte. This overload copies the text
        ' into a native buffer of the size asked for - and a second one beside
        ' it to diff against - on every frame the editor is open, so a fixed
        ' 1 MB cap meant two megabyte allocations and two megabyte copies per
        ' stage per frame while typing. Twice the text plus a page of headroom
        ' leaves room to paste into and grows with what is there.
        Dim cap = CUInt(Math.Max(MIN_TEXT_CAP, CLng(st.text.Length) * 2L + 4096L))
        Dim edited = ImGui.InputTextMultiline(label, buf, cap, size, ImGuiInputTextFlags.AllowTabInput)
        ImGui.PopStyleColor()
        If edited Then
            st.text = buf
            st.dirty = (st.text <> st.original)
        End If

        ' Re-enter the input's own child window to paint at its scroll.
        '
        ' BY LABEL, not by ID. A child window's identity is its TITLE, and
        ' BeginChildEx builds that title two different ways: "parent/name_id"
        ' when it is given a name, "parent/id" when it is not. InputTextEx
        ' makes its multiline child with BeginChildEx(label, id, ...) - it
        ' passes the label deliberately, so the window is readable in the
        ' metrics window - and the ImGui.BeginChild overload that takes an
        ' ImGuiID passes no name at all. Matching only the id therefore misses:
        ' the two titles differ, so instead of re-entering the editor's child
        ' this opened a SECOND child at the parent's cursor, below the box.
        ' Every line of highlight landed in that strip at the bottom of the
        ' window and the editor - whose own text is drawn transparent - looked
        ' empty. The string overload hashes this same label for the id and
        ' passes it as the name, so both halves of the title match and this is
        ' an append to the window the input already opened.
        '
        ' Appending is a supported path, not a trick: EndChild checks
        ' BeginCount > 1 and skips re-emitting the item into the parent, and
        ' position, size and flags are only applied on a window's first Begin
        ' of the frame - so the geometry stays the input's, and the flags below
        ' matter only in the case where the input was clipped away entirely.
        '
        ' The numbers cannot be painted in here with the text: this child clips
        ' to the text area and the gutter is outside it. What the paint works
        ' out about scroll and geometry is carried back out instead, so the two
        ' read the same origin and cannot drift apart.
        Dim haveView = False
        Dim originY As Single = 0.0F, lineH As Single = 0.0F
        Dim firstLine As Integer = 0, lastLine As Integer = -1
        Dim boxTop As Single = 0.0F, boxBot As Single = 0.0F

        Dim flags = ImGuiWindowFlags.NoScrollbar Or ImGuiWindowFlags.NoScrollWithMouse Or ImGuiWindowFlags.NoNav Or ImGuiWindowFlags.NoInputs
        If ImGui.BeginChild(label, New Num.Vector2(0, 0), ImGuiChildFlags.None, flags) Then
            Dim dl = ImGui.GetWindowDrawList()
            Dim pad = ImGui.GetStyle().FramePadding
            Dim origin = ImGui.GetWindowPos() + pad - New Num.Vector2(ImGui.GetScrollX(), ImGui.GetScrollY())
            lineH = ImGui.GetTextLineHeight()
            Dim viewTop = ImGui.GetWindowPos().Y
            Dim viewBot = viewTop + ImGui.GetWindowSize().Y

            firstLine = Math.Max(0, CInt(Math.Floor((viewTop - origin.Y) / lineH)) - 1)
            lastLine = Math.Min(st.lines.Count - 1, CInt(Math.Ceiling((viewBot - origin.Y) / lineH)) + 1)
            For i = firstLine To lastLine
                Dim y = origin.Y + i * lineH
                For Each t In st.lines(i)
                    dl.AddText(New Num.Vector2(origin.X + t.col * charW, y), t.color, t.text)
                Next
            Next

            originY = origin.Y
            boxTop = viewTop
            boxBot = viewBot
            haveView = True
        End If
        ImGui.EndChild()

        ' The numbers, in the PARENT window's draw list, on the text's own
        ' baseline and the text's own scroll - so a number cannot slide off its
        ' line - clipped to the box so they stop at its top and bottom edges,
        ' and right aligned, which is how an editor sets them. Only the lines
        ' the paint above decided were visible.
        If haveView Then
            Dim gdl = ImGui.GetWindowDrawList()
            gdl.PushClipRect(New Num.Vector2(gutterX, boxTop),
                             New Num.Vector2(gutterX + gutterW, boxBot), True)
            For i = firstLine To lastLine
                Dim n = (i + 1).ToString()
                gdl.AddText(New Num.Vector2(gutterX + gutterW - GUTTER_PAD - n.Length * charW,
                                            originY + i * lineH), C_GUTTER, n)
            Next
            gdl.PopClipRect()
        End If

        If mono Then ImGui.PopFont()
        If Not mono Then ImGui.TextDisabled("(no monospaced font found - highlight positions are approximate)")
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

        ' The compiler's words, scrollable.
        Dim errBuf = If(errorText = "", "", errorText)
        ImGui.PushStyleColor(ImGuiCol.Text, If(lastBuildOk, New Num.Vector4(0.75F, 0.75F, 0.75F, 1), badColor))
        ImGui.InputTextMultiline("##errors", errBuf, CUInt(Math.Max(1, errBuf.Length + 1)),
                                 New Num.Vector2(-1, 80), ImGuiInputTextFlags.ReadOnly)
        ImGui.PopStyleColor()
    End Sub

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
            st.original = st.text
            st.dirty = False
            stages.Add(st)
        Next
        current = s
        lastBuildOk = True
        everCompiled = False
        errorText = ""
        status = String.Format("{0}: {1} stage(s) loaded{2}", s.name, stages.Count,
                               If(stages.Count > 0 AndAlso stages(0).srcPath Is Nothing, "  (project copy not found - bin only)", ""))
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
            File.WriteAllText(p, st.text)
            tmp(st.label) = p
        Next
        Dim pick = Function(l As String) If(tmp.ContainsKey(l), tmp(l), Nothing)

        LAST_SHADER_ERROR = ""
        Dim trial = assemble_shader(pick("vert"), pick("tesc"), pick("tese"), pick("geom"), pick("comp"), pick("frag"),
                                    current.name & " (ide)", current.DefinesCopy)
        If trial = 0 Then
            lastBuildOk = False
            everCompiled = True
            errorText = LAST_SHADER_ERROR.Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
            status = "compile FAILED - nothing written, the old program is still running"
            Return
        End If
        GL.DeleteProgram(trial)

        ' It links. Now the real files, then the live program from them.
        Dim wrote As New StringBuilder
        For Each st In stages
            File.WriteAllText(st.binPath, st.text)
            If st.srcPath IsNot Nothing Then File.WriteAllText(st.srcPath, st.text)
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

    ''' <summary>Put the opened copy back - in the editor, on disk, and in the live program.</summary>
    Private Shared Sub RevertToOriginal()
        If current Is Nothing Then Return
        For Each st In stages
            st.text = st.original
            st.dirty = False
            File.WriteAllText(st.binPath, st.original)
            If st.srcPath IsNot Nothing Then File.WriteAllText(st.srcPath, st.original)
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

    ' ------------------------------------------------------------------------
    ' Highlighting. Re-tokenised only when the text changed; block comments
    ' carry across lines, everything else is per line.
    ' ------------------------------------------------------------------------
    Private Shared Sub EnsureHighlight(st As Stage)
        If ReferenceEquals(st.cachedFor, st.text) Then Return
        st.lines.Clear()
        Dim inBlock = False
        For Each raw In st.text.Split(ControlChars.Lf)
            Dim line = raw.TrimEnd(ControlChars.Cr)
            st.lines.Add(TokenizeLine(line, inBlock))
        Next
        st.cachedFor = st.text
    End Sub

    Private Shared Function TokenizeLine(line As String, ByRef inBlock As Boolean) As List(Of Token)
        Dim toks As New List(Of Token)
        Dim n = line.Length
        Dim i = 0
        ' Tabs: ImGui renders a tab as up to 4 columns; keep the overlay in step
        ' by expanding to spaces for both measurement and drawing.
        If line.IndexOf(ControlChars.Tab) >= 0 Then
            Dim sb As New StringBuilder
            For Each ch In line
                If ch = ControlChars.Tab Then sb.Append(" "c, 4 - (sb.Length Mod 4)) Else sb.Append(ch)
            Next
            line = sb.ToString() : n = line.Length
        End If
        Dim trimmed = line.TrimStart()
        If Not inBlock AndAlso trimmed.StartsWith("#") Then
            toks.Add(New Token With {.col = 0, .text = line, .color = C_PREPROC})
            Return toks
        End If
        While i < n
            If inBlock Then
                Dim e = line.IndexOf("*/", i, StringComparison.Ordinal)
                Dim stop_ = If(e < 0, n, e + 2)
                toks.Add(New Token With {.col = i, .text = line.Substring(i, stop_ - i), .color = C_COMMENT})
                i = stop_
                If e >= 0 Then inBlock = False
                Continue While
            End If
            Dim c = line(i)
            If c = "/"c AndAlso i + 1 < n AndAlso line(i + 1) = "/"c Then
                toks.Add(New Token With {.col = i, .text = line.Substring(i), .color = C_COMMENT})
                Exit While
            End If
            If c = "/"c AndAlso i + 1 < n AndAlso line(i + 1) = "*"c Then
                inBlock = True
                Continue While
            End If
            If c = """"c Then
                Dim e = line.IndexOf(""""c, i + 1)
                Dim stop_ = If(e < 0, n, e + 1)
                toks.Add(New Token With {.col = i, .text = line.Substring(i, stop_ - i), .color = C_STRING})
                i = stop_
                Continue While
            End If
            If Char.IsLetter(c) OrElse c = "_"c Then
                Dim s = i
                While i < n AndAlso (Char.IsLetterOrDigit(line(i)) OrElse line(i) = "_"c)
                    i += 1
                End While
                Dim w = line.Substring(s, i - s)
                Dim col = C_DEFAULT
                If TYPES.Contains(w) Then
                    col = C_TYPE
                ElseIf KEYWORDS.Contains(w) Then
                    col = C_KEYWORD
                ElseIf BUILTINS.Contains(w) OrElse w.StartsWith("gl_") Then
                    col = C_BUILTIN
                End If
                toks.Add(New Token With {.col = s, .text = w, .color = col})
                Continue While
            End If
            If Char.IsDigit(c) OrElse (c = "."c AndAlso i + 1 < n AndAlso Char.IsDigit(line(i + 1))) Then
                Dim s = i
                While i < n AndAlso (Char.IsLetterOrDigit(line(i)) OrElse line(i) = "."c)
                    i += 1
                End While
                toks.Add(New Token With {.col = s, .text = line.Substring(s, i - s), .color = C_NUMBER})
                Continue While
            End If
            ' punctuation and spaces: emit runs in the default colour
            Dim ps = i
            While i < n AndAlso Not (Char.IsLetterOrDigit(line(i)) OrElse line(i) = "_"c OrElse line(i) = "/"c OrElse line(i) = """"c)
                i += 1
            End While
            If i = ps Then i += 1   ' a lone '/' that is not a comment
            toks.Add(New Token With {.col = ps, .text = line.Substring(ps, i - ps), .color = C_DEFAULT})
        End While
        Return toks
    End Function
End Class

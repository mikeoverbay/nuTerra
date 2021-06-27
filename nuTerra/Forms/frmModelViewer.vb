Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics
Imports OpenTK.Windowing.Common
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL
Imports FastColoredTextBoxNS
Imports System.Text.RegularExpressions

Public Class frmModelViewer

    Public SELECTED_MODEL_TO_VIEW As Integer
    Dim MD, ZM, MM, ZOOM As Boolean
    Dim MP As New Point(0, 0)

    Dim MV_LOOK_AT_X As Single
    Dim MV_LOOK_AT_Y As Single
    Dim MV_LOOK_AT_Z As Single
    Dim V_RADIUS As Single
    Dim ROTATION_Z As Single
    Dim ROTATION_X As Single
    Dim MV_CAM_POS As Vector3
    Dim PROJECT As Matrix4
    Dim VIEW As Matrix4
    Dim VIEWPROJECT As Matrix4
    Dim keyWords As String = ""
    Dim filterlist() As String
    Dim colors(5) As System.Drawing.Color
    Dim view_started As Boolean

    Public Model_Loaded As Boolean
    Public modelIndirectBuffer As GLBuffer
    Public modelDrawCount As Integer

    Public MODEL_NAME_MODELVIEWER As String

    Public Shared Sub update_model_indirect_buffer()
        Dim indirectCommands(frmModelViewer.modelDrawCount - 1) As DrawElementsIndirectCommand
        Dim size = frmModelViewer.modelDrawCount * Marshal.SizeOf(indirectCommands(0))
        GL.GetNamedBufferSubData(frmModelViewer.modelIndirectBuffer.buffer_id, IntPtr.Zero, size, indirectCommands)

        For i = 0 To frmModelViewer.SplitContainer1.Panel1.Controls.Count - 1
            Dim cb As CheckBox = frmModelViewer.SplitContainer1.Panel1.Controls(i)
            indirectCommands(i).instanceCount = If(cb.Checked, 1, 0)
        Next

        GL.NamedBufferSubData(frmModelViewer.modelIndirectBuffer.buffer_id, IntPtr.Zero, size, indirectCommands)
    End Sub

    Public Sub draw_model_view()

        If Model_Loaded Then
            frmMain.glControl_main.Context.MakeCurrent()
            set_prespective_view_ModelViewer()

            GL.ClearColor(0.55, 0.27, 0.00, 0.0F)
            GL.ClearDepth(1.0F)
            GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

            GL.Enable(EnableCap.DepthTest)
            GL.Disable(EnableCap.CullFace)

            ModelViewerShader.Use()

            GL.Uniform3(ModelViewerShader("lightColor"), 0.5F, 0.5F, 0.7F)
            GL.Uniform3(ModelViewerShader("viewPos"), MV_CAM_POS.X, MV_CAM_POS.Y, MV_CAM_POS.Z)
            GL.UniformMatrix4(ModelViewerShader("viewProjMat"), False, VIEWPROJECT)

            modelIndirectBuffer.Bind(BufferTarget.DrawIndirectBuffer)
            map_scene.static_models.allMapModels.Bind()
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, modelDrawCount, 0)

            ModelViewerShader.StopUse()
            GL.Disable(EnableCap.CullFace)

            'test text
            Ortho_modelViewer()
            draw_text_mv(MODEL_NAME_MODELVIEWER, 5.0, 5.0, Color4.Wheat, False, True)

            'frmMain.glControl_main.SwapBuffers()
            glControl_modelView.SwapBuffers()

            ' RESTORE
            GL.ClearDepth(0.0F)

            frmMain.glControl_main.Context.MakeCurrent()
        End If

    End Sub

    Private Sub draw_text_mv(ByRef text As String,
                         ByVal locX As Single,
                         ByVal locY As Single,
                         ByRef color As Color4,
                         ByRef center As Boolean,
                         ByRef mask As Integer)
        ' text, loc X, loc Y, color, Center text at X location,
        ' mask 1 = drak background.

        '=======================================================================
        'draw text at location.
        '=======================================================================
        'setup
        If text Is Nothing Then Return

        GL.Enable(EnableCap.Blend)
        TextRenderShader.Use()
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECT)
        GL.Uniform1(TextRenderShader("divisor"), 162.0F) 'atlas size
        ASCII_ID.BindUnit(0)
        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        GL.Uniform4(TextRenderShader("color"), color)
        GL.Uniform1(TextRenderShader("mask"), mask)
        '=======================================================================
        'draw text
        Dim cntr = 0
        If center Then
            cntr = text.Length * 10.0F / 2.0F
        End If
        Dim cnt As Integer = 0
        defaultVao.Bind()
        For Each l In text
            Dim idx = ASCII_CHARACTERS.IndexOf(l) + 1
            Dim tp = (locX + cnt * 10.0) - cntr
            GL.Uniform1(TextRenderShader("index"), CSng(idx))
            Dim rect As New RectangleF(tp, locY, 10.0F, 15.0F)
            GL.Uniform4(TextRenderShader("rect"),
                      rect.Left,
                      -rect.Top,
                      rect.Right,
                      -rect.Bottom)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            cnt += 1
        Next
        GL.Disable(EnableCap.Blend)
        TextRenderShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

#Region "form events"

    Private Sub frmModelViewer_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim glSettings As New OpenTK.WinForms.GLControlSettings With {
            .API = ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible
        }

#If DEBUG Then
        glSettings.Flags = glSettings.Flags Or ContextFlags.Debug
#End If

        Me.glControl_modelView = New OpenTK.WinForms.GLControl(glSettings)

        glControl_modelView.Dock = DockStyle.Fill
        TabPage1.Controls.Add(Me.glControl_modelView)
        '-----------------------------------------------------------------------------------------
        Me.Show()
        Application.DoEvents()
        Application.DoEvents()
        Application.DoEvents()
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        Me.KeyPreview = True    'So I catch keyboard before despatching it
        '-----------------------------------------------------------------------------------------
        get_filter_strings()
        set_styles()

        V_RADIUS = -30.0
        ROTATION_X = -Math.PI * 0.25
        ROTATION_Z = Math.PI * 0.25

        view_started = True
    End Sub

    Private Sub frmModelViewer_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If Model_Loaded Then
            modelIndirectBuffer = Nothing
            Model_Loaded = False
        End If
        e.Cancel = True
        Me.Visible = False
        frmMain.glControl_main.Context.MakeCurrent()
    End Sub

    Private Sub frmModelViewer_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown

        Select Case e.KeyCode
            Case Keys.ControlKey
                ZM = True
            Case Keys.ShiftKey
                MM = True
        End Select

    End Sub

    Private Sub frmModelViewer_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        ZM = False
        MM = False
    End Sub
#End Region

#Region "Mouse"

    Private Sub glControl_modelView_MouseDown(sender As Object, e As MouseEventArgs) Handles glControl_modelView.MouseDown
        If e.Button = MouseButtons.Left Then
            MD = True
            MP.X = e.X
            MP.Y = e.Y
        End If
        If e.Button = MouseButtons.Right Then
            ZOOM = True
            MP.X = e.X
            MP.Y = e.Y
        End If
        If e.Button = MouseButtons.Middle Then
            MD = True
            MM = True
            MP.X = e.X
            MP.Y = e.Y
        End If

    End Sub

    Private Sub glControl_modelView_MouseMove(sender As Object, e As MouseEventArgs) Handles glControl_modelView.MouseMove
        Dim dead As Integer = 5
        Dim t As Single
        Dim M_Speed As Single = 0.4
        Dim ms As Single = 0.2F * V_RADIUS ' distance away changes speed.. THIS WORKS WELL!
        If MD Then
            If e.X > (MP.X + dead) Then
                If e.X - MP.X > 100 Then t = (1.0F * M_Speed)
            Else : t = CSng(Sin((e.X - MP.X) / 100)) * M_Speed
                If Not ZM Then
                    If MM Then ' check for modifying flag
                        MV_LOOK_AT_X -= ((t * ms) * (Cos(ROTATION_Z)))
                        MV_LOOK_AT_Z -= ((t * ms) * (-Sin(ROTATION_Z)))
                    Else
                        ROTATION_Z -= t
                    End If
                    If ROTATION_Z > (2 * PI) Then ROTATION_Z -= (2 * PI)
                    MP.X = e.X
                End If
            End If
            If e.X < (MP.X - dead) Then
                If MP.X - e.X > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((MP.X - e.X) / 100)) * M_Speed
                If Not ZM Then
                    If MM Then ' check for modifying flag
                        MV_LOOK_AT_X += ((t * ms) * (Cos(ROTATION_Z)))
                        MV_LOOK_AT_Z += ((t * ms) * (-Sin(ROTATION_Z)))
                    Else
                        ROTATION_Z += t
                    End If
                    If ROTATION_Z < 0 Then ROTATION_Z += (2 * PI)
                    MP.X = e.X
                End If
            End If
            ' ------- Y moves ----------------------------------
            If e.Y > (MP.Y + dead) Then
                If e.Y - MP.Y > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((e.Y - MP.Y) / 100)) * M_Speed
                If ZM Then
                    MV_LOOK_AT_Y -= (t * ms)
                Else
                    If MM Then ' check for modifying flag
                        MV_LOOK_AT_Z -= ((t * ms) * (Cos(ROTATION_Z)))
                        MV_LOOK_AT_X -= ((t * ms) * (Sin(ROTATION_Z)))
                    Else
                        If ROTATION_X - t < -PI / 2.0 Then
                            ROTATION_X = (-PI / 2.0) + 0.001
                        Else
                            ROTATION_X -= t
                        End If
                    End If
                End If
                MP.Y = e.Y
            End If
            If e.Y < (MP.Y - dead) Then
                If MP.Y - e.Y > 100 Then t = (M_Speed)
            Else : t = CSng(Sin((MP.Y - e.Y) / 100)) * M_Speed
                If ZM Then
                    MV_LOOK_AT_Y += (t * ms)
                Else
                    If MM Then ' check for modifying flag
                        MV_LOOK_AT_Z += ((t * ms) * (Cos(ROTATION_Z)))
                        MV_LOOK_AT_X += ((t * ms) * (Sin(ROTATION_Z)))
                    Else
                        ROTATION_X += t
                    End If
                    If ROTATION_X > 1.3 Then ROTATION_X = 1.3
                End If
                MP.Y = e.Y
            End If
            Return
        End If
        If ZOOM Then

            ' zoom is factored in to Cam radius
            Dim vrad = V_RADIUS
            If e.Y < (MP.Y - dead) Then
                If e.Y - MP.Y > 100 Then t = (10)
            Else : t = CSng(Sin((e.Y - MP.Y) / 100)) * 12 * My.Settings.speed
                If vrad + (t * (vrad * 0.2)) < -200.0 Then
                    vrad = -200.0F
                Else
                    vrad += (t * (vrad * 0.2))
                End If
                MP.Y = e.Y
            End If
            If e.Y > (MP.Y + dead) Then
                If MP.Y - e.Y > 100 Then t = (10)
            Else : t = CSng(Sin((MP.Y - e.Y) / 100)) * 12 * My.Settings.speed
                vrad -= (t * (vrad * 0.2))    ' zoom is factored in to Cam radius
                If vrad > -0.01 Then vrad = -0.01
            End If
            If vrad > -0.1 Then vrad = -0.1
            V_RADIUS = vrad
            MP.Y = e.Y
            Return

        End If
        MP.X = e.X
        MP.Y = e.Y
    End Sub

    Private Sub glControl_modelView_MouseUp(sender As Object, e As MouseEventArgs) Handles glControl_modelView.MouseUp
        MD = False
        ZOOM = False
        MM = False
    End Sub

    Private Sub glControl_modelView_MouseEnter(sender As Object, e As EventArgs) Handles glControl_modelView.MouseEnter
        Me.glControl_modelView.Focus()
    End Sub
#End Region

    Public Sub Ortho_modelViewer()
        GL.Viewport(0, 0, glControl_modelView.ClientSize.Width, glControl_modelView.ClientSize.Height)
        PROJECT = Matrix4.CreateOrthographicOffCenter(0.0F, glControl_modelView.Width, -glControl_modelView.Height, 0.0F, -300.0F, 300.0F)
        VIEW = Matrix4.Identity
    End Sub

    Public Sub set_prespective_view_ModelViewer()
        GL.Viewport(0, 0, glControl_modelView.ClientSize.Width, glControl_modelView.ClientSize.Height)
        Dim sin_x, cos_x, cos_y, sin_y As Single
        Dim cam_x, cam_y, cam_z As Single

        sin_x = Sin(ROTATION_Z)
        cos_x = Cos(ROTATION_Z)
        cos_y = Cos(ROTATION_X)
        sin_y = Sin(ROTATION_X)
        cam_y = sin_y * V_RADIUS
        cam_x = cos_y * sin_x * V_RADIUS
        cam_z = cos_y * cos_x * V_RADIUS

        Dim MV_LOOK_Y = MV_LOOK_AT_Y
        MV_CAM_POS.X = cam_x + MV_LOOK_AT_X
        MV_CAM_POS.Y = cam_y + MV_LOOK_Y
        MV_CAM_POS.Z = cam_z + MV_LOOK_AT_Z

        Dim target As New Vector3(MV_LOOK_AT_X, MV_LOOK_Y, MV_LOOK_AT_Z)

        PROJECT = Matrix4.CreatePerspectiveFieldOfView(
                                   FieldOfView,
                                   glControl_modelView.ClientSize.Width / CSng(glControl_modelView.ClientSize.Height),
                                   My.Settings.near, My.Settings.far)
        VIEW = Matrix4.LookAt(MV_CAM_POS, target, Vector3.UnitY)
        VIEWPROJECT = VIEW * PROJECT

    End Sub

    Private Sub get_filter_strings()
        Dim ts = IO.File.ReadAllText(Application.StartupPath + "\data\filtered_strings.txt")
        filterlist = ts.Split(ControlChars.CrLf.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
        set_keywords()
    End Sub

    Private Sub set_keywords()
        Keywords = "\b("
        For Each s In filterlist
            If InStr(s, "#") = 0 Then
                If s.Length > 2 Then
                    Keywords += s + "|"
                End If
            End If
        Next
        'this is needed because of the last | in the load loop!
        keyWords += "colour)\b"
    End Sub
    Private Sub set_styles()

        get_color_settings()

        FastColoredTextBox1.Styles(0) = New TextStyle(getBrush(0), Nothing, Drawing.FontStyle.Regular)
        FastColoredTextBox1.Styles(1) = New TextStyle(getBrush(1), Nothing, Drawing.FontStyle.Regular)
        FastColoredTextBox1.Styles(2) = New TextStyle(getBrush(2), Nothing, Drawing.FontStyle.Bold)
        FastColoredTextBox1.Styles(3) = New TextStyle(getBrush(3), Nothing, Drawing.FontStyle.Bold)
        FastColoredTextBox1.Styles(4) = New TextStyle(getBrush(4), Nothing, Drawing.FontStyle.Regular)
        Dim e As New TextChangedEventArgs(New FastColoredTextBoxNS.Range(FastColoredTextBox1))

        SyntaxHighlight(FastColoredTextBox1, e)
        FastColoredTextBox1.Refresh()
        Application.DoEvents()

    End Sub
    Private Function getBrush(Id As Integer) As SolidBrush
        Dim br As SolidBrush
        Dim c As Color
        c = colors(Id)
        br = New SolidBrush(c)
        Return br
    End Function
    Private Sub get_color_settings()
        'numeric
        colors(0) = Color.White

        'tags
        colors(1) = Color.LightBlue

        'property strings
        colors(2) = Color.LightYellow

        'property names
        colors(3) = Color.Silver

        'everything else
        colors(4) = Color.Green
    End Sub

    Private Sub m_on_top_Click(sender As Object, e As EventArgs) Handles m_on_top.Click
        Me.TopMost = Me.TopMost Xor True
        If Me.TopMost Then m_on_top.ForeColor = Color.Red Else m_on_top.ForeColor = Color.Black
    End Sub

    Private Sub m_reset_view_Click(sender As Object, e As EventArgs) Handles m_reset_view.Click
        MV_LOOK_AT_X = 0.0
        MV_LOOK_AT_Y = 0.0
        MV_LOOK_AT_Z = 0.0
        V_RADIUS = -10.0
    End Sub

    Private Sub FastColoredTextBox1_TextChanged(sender As Object, E As TextChangedEventArgs) Handles FastColoredTextBox1.TextChanged
        SyntaxHighlight(sender, E)
    End Sub

    Private Sub ToolStripMenuItem1_Click(sender As Object, e As EventArgs) Handles m_help.Click
        Dim p = Application.StartupPath + "\HTML\FCTB_HELP.html"
        Process.Start(p)
    End Sub

    Private Sub SyntaxHighlight(ByRef sender As FastColoredTextBox, e As TextChangedEventArgs)
        e.ChangedRange.SetFoldingMarkers("", "")
        sender.LeftBracket = "("c
        sender.RightBracket = ")"c
        sender.LeftBracket2 = ControlChars.NullChar
        sender.RightBracket2 = ControlChars.NullChar
        'clear style of changed range
        e.ChangedRange.ClearStyle(FastColoredTextBox1.Styles(0), FastColoredTextBox1.Styles(1),
                                  FastColoredTextBox1.Styles(2), FastColoredTextBox1.Styles(3), FastColoredTextBox1.Styles(4))

        'string highlighting
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(4), "(.*?)")
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(2), "(?<=\<Texture\>).*?(?=\<\/Texture\>)", RegexOptions.Multiline)

        'XML tags
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(1), "(<.[^(><.)]+>)", RegexOptions.Multiline)

        'keyword highlighting
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(3), keyWords)


        'number highlighting
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(0), "\b\d+[\.]?\d*([eE]\-?\d+)?[lLdDfF]?\b|\b0x[a-fA-F\d]+\b")
        'clear folding markers
        e.ChangedRange.ClearFoldingMarkers()


    End Sub



End Class
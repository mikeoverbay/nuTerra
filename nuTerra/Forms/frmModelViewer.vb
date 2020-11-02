Imports System.Globalization
Imports System.IO
Imports System.Math
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows
Imports OpenTK.Graphics
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
Imports FastColoredTextBoxNS
Imports System.Text.RegularExpressions

Public Class frmModelViewer

    Public SELECTED_MODEL_TO_VIEW As Integer
    Dim MD, ZM, MM, ZOOM As Boolean
    Dim MP As New Point(0, 0)

    Dim LOOK_AT_X As Single
    Dim LOOK_AT_Y As Single
    Dim LOOK_AT_Z As Single
    Dim V_RADIUS As Single
    Dim ROTATION_Z As Single
    Dim ROTATION_X As Single
    Dim CAM_POS As Vector3
    Dim PROJECT As Matrix4
    Dim VIEW As Matrix4
    Dim VIEWPROJECT As Matrix4
    Dim keyWords As String = ""
    Dim filterlist() As String
    Dim colors(5) As System.Drawing.Color

    Public Sub draw_model_view()
        glControl_modelView.MakeCurrent()
        set_prespective_view_ModelViewer()
        GL.ClearColor(0.0F, 0.0F, 0.3F, 0.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        glControl_modelView.SwapBuffers()
    End Sub

#Region "form events"

    Private Sub frmModelViewer_Load(sender As Object, e As EventArgs) Handles MyBase.Load


        ' Init main gl-control
        Dim flags As GraphicsContextFlags
#If DEBUG Then
        flags = GraphicsContextFlags.ForwardCompatible Or GraphicsContextFlags.Debug
#Else
        flags = GraphicsContextFlags.ForwardCompatible
#End If

        Me.glControl_modelView = New OpenTK.GLControl(New GraphicsMode(ColorFormat.Empty, 0), 4, 5, flags)
        Me.glControl_modelView.VSync = False
        TabPage1.Controls.Add(Me.glControl_modelView)
        glControl_modelView.Dock = DockStyle.Fill
        '-----------------------------------------------------------------------------------------
        Me.Show()
        '-----------------------------------------------------------------------------------------
        '-----------------------------------------------------------------------------------------
        Me.KeyPreview = True    'So I catch keyboard before despatching it
        '-----------------------------------------------------------------------------------------
        get_filter_strings()
        set_styles()


    End Sub
    Private Sub frmModelViewer_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If Keys.Control Then
            ZM = True
        End If
        If Keys.Shift Then
            MM = True
        End If
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

    End Sub

    Private Sub glControl_modelView_MouseMove(sender As Object, e As MouseEventArgs) Handles glControl_modelView.MouseMove
        Dim dead As Integer = 5
        Dim t As Single
        Dim M_Speed As Single = 0.8
        Dim ms As Single = 0.2F * V_RADIUS ' distance away changes speed.. THIS WORKS WELL!
        If MD Then
            If e.X > (MP.X + dead) Then
                If e.X - MP.X > 100 Then t = (1.0F * M_Speed)
            Else : t = CSng(Sin((e.X - MP.X) / 100)) * M_Speed
                If Not ZM Then
                    If MM Then ' check for modifying flag
                        LOOK_AT_X -= ((t * ms) * (Cos(ROTATION_Z)))
                        LOOK_AT_Z -= ((t * ms) * (-Sin(ROTATION_Z)))
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
                        LOOK_AT_X += ((t * ms) * (Cos(ROTATION_Z)))
                        LOOK_AT_Z += ((t * ms) * (-Sin(ROTATION_Z)))
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
                    LOOK_AT_Y -= (t * ms)
                Else
                    If MM Then ' check for modifying flag
                        LOOK_AT_Z -= ((t * ms) * (Cos(ROTATION_Z)))
                        LOOK_AT_X -= ((t * ms) * (Sin(ROTATION_Z)))
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
                    LOOK_AT_Y += (t * ms)
                Else
                    If MM Then ' check for modifying flag
                        LOOK_AT_Z += ((t * ms) * (Cos(ROTATION_Z)))
                        LOOK_AT_X += ((t * ms) * (Sin(ROTATION_Z)))
                    Else
                        ROTATION_X += t
                    End If
                    If ROTATION_X > 1.3 Then ROTATION_X = 1.3
                End If
                MP.Y = e.Y
            End If
            draw_model_view()
            Return
        End If
        If MOVE_CAM_Z Then
            If e.Y > (MP.Y + dead) Then
                If e.Y - MP.Y > 100 Then t = (10)
            Else : t = CSng(Sin((e.Y - MP.Y) / 100)) * 12
                V_RADIUS += (t * (V_RADIUS * 0.2))    ' zoom is factored in to Cam radius
                MP.Y = e.Y
            End If
            If e.Y < (MP.Y - dead) Then
                If MP.Y - e.Y > 100 Then t = (10)
            Else : t = CSng(Sin((MP.Y - e.Y) / 100)) * 12
                V_RADIUS -= (t * (V_RADIUS * 0.2))    ' zoom is factored in to Cam radius
                If V_RADIUS > -0.01 Then V_RADIUS = -0.01
                MP.Y = e.Y
            End If
            If V_RADIUS > -0.1 Then V_RADIUS = -0.1
            draw_model_view()
            Return
        End If
        MP.X = e.X
        MP.Y = e.Y
        draw_model_view()
    End Sub

    Private Sub glControl_modelView_MouseUp(sender As Object, e As MouseEventArgs) Handles glControl_modelView.MouseUp
        MD = False
        ZOOM = False

    End Sub

    Private Sub glControl_modelView_MouseEnter(sender As Object, e As EventArgs) Handles glControl_modelView.MouseEnter
        Me.glControl_modelView.Focus()
    End Sub
#End Region


    Public Sub set_prespective_view_ModelViewer()
        Dim sin_x, cos_x, cos_y, sin_y As Single
        Dim cam_x, cam_y, cam_z As Single

        sin_x = Sin(ROTATION_Z)
        cos_x = Cos(ROTATION_Z)
        cos_y = Cos(ROTATION_X)
        sin_y = Sin(ROTATION_X)
        cam_y = sin_y * V_RADIUS
        cam_x = cos_y * sin_x * V_RADIUS
        cam_z = cos_y * cos_x * V_RADIUS

        Dim LOOK_Y = CURSOR_Y + LOOK_AT_Y
        CAM_POS.X = cam_x + LOOK_AT_X
        CAM_POS.Y = cam_y + LOOK_Y
        CAM_POS.Z = cam_z + LOOK_AT_Z

        Dim target As New Vector3(LOOK_AT_X, LOOK_Y, LOOK_AT_Z)

        PROJECT = Matrix4.CreatePerspectiveFieldOfView(
                                   FieldOfView,
                                   glControl_modelView.ClientSize.Width / CSng(glControl_modelView.ClientSize.Height),
                                   PRESPECTIVE_NEAR, PRESPECTIVE_FAR)
        VIEW = Matrix4.LookAt(CAM_POSITION, target, Vector3.UnitY)
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
        Keywords += "diffuseMap2|primitiveGroup|/primitiveGroup)\b"
    End Sub
    Private Sub set_styles()
        get_color_settings()
        FastColoredTextBox1.Styles(0) = New TextStyle(getBrush(0), Nothing, Drawing.FontStyle.Regular)
        FastColoredTextBox1.Styles(1) = New TextStyle(getBrush(1), Nothing, Drawing.FontStyle.Bold)
        FastColoredTextBox1.Styles(2) = New TextStyle(getBrush(2), Nothing, Drawing.FontStyle.Bold)
        FastColoredTextBox1.Styles(3) = New TextStyle(getBrush(3), Nothing, Drawing.FontStyle.Regular)
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
        colors(0) = My.Settings.numeric
        colors(1) = My.Settings.tags
        colors(2) = My.Settings.textures
        colors(3) = My.Settings.props
        colors(4) = My.Settings.allothers
    End Sub


    Private Sub FastColoredTextBox1_TextChanged(sender As Object, E As TextChangedEventArgs) Handles FastColoredTextBox1.TextChanged
        SyntaxHighlight(sender, E)
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
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(3), Keywords)


        'number highlighting
        e.ChangedRange.SetStyle(FastColoredTextBox1.Styles(0), "\b\d+[\.]?\d*([eE]\-?\d+)?[lLdDfF]?\b|\b0x[a-fA-F\d]+\b")
        'clear folding markers
        e.ChangedRange.ClearFoldingMarkers()


    End Sub

    Private Sub frmModelViewer_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        draw_model_view()
    End Sub

    Private Sub frmModelViewer_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        draw_model_view()
    End Sub
End Class
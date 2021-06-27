
Imports System.Math
Imports System
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime
Imports System.Runtime.InteropServices


Public Class frmGbufferViewer
    Private image_scale As Single = 0.25
    Public Viewer_Image_ID As Integer = 1
    Dim PROJECTIONMATRIX_GLC As Matrix4
    Dim GLC_VA As GLVertexArray
    Dim MASK As UInt32 = &HF
    ReadOnly maskList() As Integer = {1, 2, 4, 8}

    Private mouse_pos As New Point
    Private mouse_down As Boolean = False
    Private mouse_delta As New Point

    Public vBox As New l_
    Public sBox As New l_
    Public Structure l_
        Public location As Point
        Public Width As Single
        Public Height As Single
    End Structure

    Public rect_location As New Point
    Public rect_size As New Point
    Public old_rect_size As New Point
    Public Zoom_Factor As Single = 0.25
    Public old_h, old_w As Integer

    Private Sub frmTestView_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Me.Hide()
        e.Cancel = True ' if we close this form, we lose the event handlers added at load time!!
    End Sub

    Private Sub frmTestView_Load(sender As Object, e As EventArgs) Handles Me.Load
        Dim glSettings As New OpenTK.WinForms.GLControlSettings With {
            .API = ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible
        }
        Me.GLC = New OpenTK.WinForms.GLControl()

        Me.GLC.Dock = DockStyle.Fill
        Me.Panel1.Controls.Add(Me.GLC)

        Me.Show()
        AddHandler full_scale.CheckedChanged, AddressOf CheckedChanged
        AddHandler half_scale.CheckedChanged, AddressOf CheckedChanged
        AddHandler quater_scale.CheckedChanged, AddressOf CheckedChanged

        AddHandler b_depth.CheckedChanged, AddressOf image_changed
        AddHandler b_color.CheckedChanged, AddressOf image_changed
        AddHandler b_position.CheckedChanged, AddressOf image_changed
        AddHandler b_normal.CheckedChanged, AddressOf image_changed
        AddHandler b_flags.CheckedChanged, AddressOf image_changed
        AddHandler b_aux.CheckedChanged, AddressOf image_changed
        AddHandler b_vt1.CheckedChanged, AddressOf image_changed
        AddHandler b_shadow.CheckedChanged, AddressOf image_changed

        Viewer_Image_ID = CInt(b_color.Tag)

        AddHandler r_cb.CheckedChanged, AddressOf change_mask
        AddHandler g_cb.CheckedChanged, AddressOf change_mask
        AddHandler b_cb.CheckedChanged, AddressOf change_mask
        AddHandler a_cb.CheckedChanged, AddressOf change_mask


        GLC.MakeCurrent()
        GLC_VA = GLVertexArray.Create("GLC_VA")
        update_screen()

        'order important?
        b_color.PerformClick()

        Zoom_Factor = image_scale
        Dim img_width = frmMain.glControl_main.Width
        Dim img_height = frmMain.glControl_main.Height
        old_w = img_width
        old_h = img_height
        rect_size.X = img_width * image_scale
        rect_size.Y = img_height * image_scale
        old_rect_size = New Point(old_w, old_h)
        rect_location = New Point(0, 0)

    End Sub

    Private Sub CheckedChanged(sender As Object, e As EventArgs)
        image_scale = CSng(sender.tag)
        Zoom_Factor = image_scale
        Dim img_width = frmMain.glControl_main.Width
        Dim img_height = frmMain.glControl_main.Height
        old_w = img_width
        old_h = img_height
        rect_size.X = img_width * image_scale
        rect_size.Y = img_height * image_scale
        old_rect_size = New Point(old_w, old_h)
        rect_location = New Point(0, 0)

    End Sub
    Private Sub image_changed(sender As Object, e As EventArgs)
        Viewer_Image_ID = CInt(sender.tag)
    End Sub
    Private Sub set_viewPort()
        GL.Viewport(0, 0, GLC.ClientSize.Width, GLC.ClientSize.Height)
        PROJECTIONMATRIX_GLC = Matrix4.CreateOrthographicOffCenter(0.0F, GLC.ClientSize.Width, -GLC.ClientSize.Height, 0.0F, -300.0F, 300.0F)
    End Sub

    Public Sub update_screen()
        If GLC Is Nothing Then Return

        GLC.MakeCurrent()
        Dim img_width, img_height As Integer
        set_viewPort()

        GL.ClearColor(0.0, 0.0, 0.0, 0.0)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Disable(EnableCap.DepthTest)

        GL.Disable(EnableCap.Blend)

        img_width = frmMain.glControl_main.Width
        img_height = frmMain.glControl_main.Height

        h_label.Text = "Height:" + height.ToString("0000")
        w_label.Text = "Width:" + width.ToString("0000")

        img_width *= image_scale
        img_height *= image_scale

        GLC_VA.Bind()

        Select Case Viewer_Image_ID
            Case b_depth.Tag
                toLinearShader.Use()

                MainFBO.gDepth.BindUnit(0)
                GL.Uniform1(toLinearShader("reversed"), CInt(True))
                GL.Uniform1(toLinearShader("far"), My.Settings.far)
                GL.Uniform1(toLinearShader("near"), My.Settings.near)
                GL.UniformMatrix4(toLinearShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)

                Dim rect As New RectangleF(rect_location.X, rect_location.Y, rect_size.X, rect_size.Y)

                GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
                toLinearShader.StopUse()

            Case b_color.Tag
                draw_image(MainFBO.gColor, 0)

            Case b_position.Tag
                draw_image(MainFBO.gPosition, 0)

            Case b_normal.Tag
                draw_image(MainFBO.gNormal, 1)

            Case b_flags.Tag
                draw_image(MainFBO.gGMF, 0)

            Case b_aux.Tag
                draw_image(MainFBO.gAUX_Color, 0)

            Case b_vt1.Tag
                If map_scene.terrain.vt IsNot Nothing Then
                    draw_checker_board(CHECKER_BOARD)
                    map_scene.terrain.vt.DebugDraw(rect_location, rect_size, PROJECTIONMATRIX_GLC)
                End If

            Case b_shadow.Tag
                toLinearShader.Use()

                ShadowMappingFBO.depth_tex.BindUnit(0)
                GL.Uniform1(toLinearShader("reversed"), CInt(True))
                GL.Uniform1(toLinearShader("far"), ShadowMappingFBO.FAR)
                GL.Uniform1(toLinearShader("near"), ShadowMappingFBO.NEAR)
                GL.UniformMatrix4(toLinearShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)

                Dim rect As New RectangleF(rect_location.X, rect_location.Y, rect_size.X, rect_size.X)

                GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
                toLinearShader.StopUse()
        End Select

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GLC.SwapBuffers()  ' swap back to front

        'switch back to main context
        frmMain.glControl_main.MakeCurrent()

    End Sub

    Private Sub draw_image(image As GLTexture, is_normal As Integer)

        draw_checker_board(CHECKER_BOARD)

        If cb_alpha_enable.Checked Then
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One)
            GL.Enable(EnableCap.Blend)
        End If
        colorMaskShader.Use()

        image.BindUnit(0)
        GL.Uniform1(colorMaskShader("isNormal"), 0) ' gNormal is no longer a float.
        GL.Uniform1(colorMaskShader("mask"), MASK)
        GL.UniformMatrix4(colorMaskShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)



        Dim rect As New RectangleF(rect_location.X, rect_location.Y, rect_size.X, rect_size.Y)

        GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        colorMaskShader.StopUse()
        GL.Disable(EnableCap.Blend)

    End Sub

    Public Sub draw_checker_board(image As GLTexture)

        image2dShader.Use()
        Dim ps = Panel1.Size
        Dim p As New Vector2(320.0F, 320.0F)
        Dim v As New Vector2(CSng(GLC.ClientSize.Width), CSng(GLC.ClientSize.Height))
        Dim s As New Vector2(v.X / p.X, v.Y / p.Y)

        image.BindUnit(0)
        GL.Uniform2(image2dShader("uv_scale"), s.X, s.Y)

        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)

        Dim rect As New RectangleF(0F, 0F, CSng(GLC.ClientSize.Width), CSng(GLC.ClientSize.Height))

        GL.Uniform4(image2dShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        image2dShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Private Sub frmTestView_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        update_screen()
    End Sub

    Private Sub frmTestView_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        update_screen()

    End Sub

    Private Sub change_mask(sender As Object, e As EventArgs)
        Dim cb As CheckBox = DirectCast(sender, CheckBox)
        MASK = MASK Xor maskList(CInt(cb.Tag))
        Label1.Text = MASK.ToString("X")
    End Sub

    Private Sub GLC_MouseDown(sender As Object, e As MouseEventArgs) Handles GLC.MouseDown
        mouse_down = True
        mouse_delta = e.Location
    End Sub

    Private Sub GLC_MouseMove(sender As Object, e As MouseEventArgs) Handles GLC.MouseMove
        If mouse_down Then
            Dim p As New Point
            p = e.Location - mouse_delta
            rect_location += p
            mouse_delta = e.Location
            sBox.location = rect_location
            sBox.Height = rect_size.Y
            sBox.Width = rect_size.X
            'draw(True)
            Return
        End If
        mouse_pos = e.Location
    End Sub

    Private Sub GLC_MouseUp(sender As Object, e As MouseEventArgs) Handles GLC.MouseUp
        mouse_down = False
        mouse_pos = e.Location

    End Sub

    Private Sub GLC_MouseWheel(sender As Object, e As MouseEventArgs) Handles GLC.MouseWheel
        mouse_pos = e.Location
        mouse_delta = e.Location

        If e.Delta > 0 Then
            img_scale_up()
        Else
            img_scale_down()
        End If
    End Sub
    Public Sub img_scale_up()

        If Zoom_Factor >= 8.0 Then
            Zoom_Factor = 8.0
            Return 'to big and the t_bmp creation will hammer memory.
        End If
        Dim amt As Single = 0.125
        Zoom_Factor += amt
        Dim z = (Zoom_Factor / 1.0) * 100.0
        'zoom.Text = "Zoom:" + vbCrLf + z.ToString("000") + "%"
        Application.DoEvents()
        'this bit of math zooms the texture around the mouses center during the resize.
        'old_w and old_h is the original size of the image in width and height
        'mouse_pos is current mouse position in the window.

        Dim offset As New Point
        Dim old_size_w, old_size_h As Double
        old_size_w = (old_w * (Zoom_Factor - amt))
        old_size_h = (old_h * (Zoom_Factor - amt))

        offset = rect_location - (mouse_pos)

        rect_size.X = Zoom_Factor * old_w
        rect_size.Y = Zoom_Factor * old_h

        Dim delta_x As Double = CDbl(offset.X / old_size_w)
        Dim delta_y As Double = CDbl(offset.Y / old_size_h)

        Dim x_offset = delta_x * (rect_size.X - old_size_w)
        Dim y_offset = delta_y * (rect_size.Y - old_size_h)

        rect_location.X += CInt(x_offset)
        rect_location.Y += CInt(y_offset)

        sBox.location = rect_location
        sBox.Height = rect_size.Y
        sBox.Width = rect_size.X
        'draw(True)
    End Sub
    Public Sub img_scale_down()
        If Zoom_Factor <= 0.25 Then
            Zoom_Factor = 0.25
            Return
        End If
        Dim amt As Single = 0.125
        Zoom_Factor -= amt
        Dim z = (Zoom_Factor / 1.0) * 100.0
        'zoom.Text = "Zoom:" + vbCrLf + z.ToString("000") + "%"
        Application.DoEvents()

        'this bit of math zooms the texture around the mouses center during the resize.
        'old_w and old_h is the original size of the image in width and height
        'mouse_pos is current mouse position in the window.

        Dim offset As New Point
        Dim old_size_w, old_size_h As Double

        old_size_w = (old_w * (Zoom_Factor - amt))
        old_size_h = (old_h * (Zoom_Factor - amt))

        offset = rect_location - (mouse_pos)

        rect_size.X = Zoom_Factor * old_w
        rect_size.Y = Zoom_Factor * old_h

        Dim delta_x As Double = CDbl(offset.X / (rect_size.X + (rect_size.X - old_size_w)))
        Dim delta_y As Double = CDbl(offset.Y / (rect_size.Y + (rect_size.Y - old_size_h)))

        Dim x_offset = delta_x * (rect_size.X - old_size_w)
        Dim y_offset = delta_y * (rect_size.Y - old_size_h)

        rect_location.X += -CInt(x_offset)
        rect_location.Y += -CInt(y_offset)

        sBox.location = rect_location
        sBox.Height = rect_size.Y
        sBox.Width = rect_size.X

        'draw(True)
    End Sub

End Class
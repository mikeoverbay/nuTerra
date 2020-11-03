
Imports System.Math
Imports System
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports System.Runtime
Imports System.Runtime.InteropServices


Public Class frmGbufferViewer
    Private image_scale As Single = 0.25
    Public Viewer_Image_ID As Integer = -1
    Dim PROJECTIONMATRIX_GLC As Matrix4
    Dim GLC_VA As Integer
    Dim MASK As UInt32 = &HF
    Dim maskList() As Integer = {1, 2, 4, 8}

    Private Sub frmTestView_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Me.Hide()
        e.Cancel = True ' if we close this form, we lose the event handlers added at load time!!
    End Sub

    Private Sub frmTestView_Load(sender As Object, e As EventArgs) Handles Me.Load


        Me.GLC = New OpenTK.GLControl(New GraphicsMode(ColorFormat.Empty, 0), 4, 5, GraphicsContextFlags.ForwardCompatible)
        Me.GLC.VSync = False
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

        Viewer_Image_ID = CInt(b_color.Tag)

        AddHandler r_cb.CheckedChanged, AddressOf change_mask
        AddHandler g_cb.CheckedChanged, AddressOf change_mask
        AddHandler b_cb.CheckedChanged, AddressOf change_mask
        AddHandler a_cb.CheckedChanged, AddressOf change_mask


        GLC.MakeCurrent()
        GL.CreateVertexArrays(1, GLC_VA)
        update_screen()
    End Sub


    Private Sub CheckedChanged(sender As Object, e As EventArgs)
        image_scale = CSng(sender.tag)
        update_screen()
    End Sub
    Private Sub image_changed(sender As Object, e As EventArgs)
        Viewer_Image_ID = CInt(sender.tag)
        update_screen()
    End Sub
    Private Sub set_viewPort()
        GL.Viewport(0, 0, GLC.ClientSize.Width, GLC.ClientSize.Height)
        PROJECTIONMATRIX_GLC = Matrix4.CreateOrthographicOffCenter(0.0F, GLC.ClientSize.Width, -GLC.ClientSize.Height, 0.0F, -300.0F, 300.0F)
    End Sub

    Public Sub update_screen()
        If GLC Is Nothing Then Return

        GLC.MakeCurrent()
        Dim width, height As Integer
        set_viewPort()

        GL.ClearColor(0.0, 0.0, 0.0, 0.0)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Disable(EnableCap.DepthTest)
        'select image and shader by selected radio button
        GL.Disable(EnableCap.Blend)
        'all gBuffer textures are the same size. so we can do this now

        GL.GetTextureLevelParameter(FBOm.gColor, 0, GetTextureParameter.TextureWidth, width)
        GL.GetTextureLevelParameter(FBOm.gColor, 0, GetTextureParameter.TextureHeight, height)

        h_label.Text = "Height:" + height.ToString("0000")
        w_label.Text = "Width:" + width.ToString("0000")

        width *= image_scale
        height *= image_scale

        Dim er = GL.GetError
        GL.BindVertexArray(GLC_VA)
        Dim er2 = GL.GetError

        Select Case Viewer_Image_ID
            Case 1
                toLinearShader.Use()

                GL.BindTextureUnit(0, FBOm.gDepth)
                GL.Uniform1(toLinearShader("imageMap"), 0)
                GL.Uniform1(toLinearShader("far"), PRESPECTIVE_FAR)
                GL.Uniform1(toLinearShader("near"), PRESPECTIVE_NEAR)
                GL.UniformMatrix4(toLinearShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)

                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(toLinearShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                toLinearShader.StopUse()

            Case 2
                colorMaskShader.Use()

                GL.Uniform1(colorMaskShader("colorMap"), 0)
                GL.Uniform1(colorMaskShader("isNormal"), 0)
                GL.Uniform1(colorMaskShader("mask"), MASK)

                GL.BindTextureUnit(0, FBOm.gColor)
                'GL.BindTextureUnit(0, CC_LUT_ID)

                GL.UniformMatrix4(colorMaskShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                Dim rect As New RectangleF(0, 0, width, height)

                GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                colorMaskShader.StopUse()

            Case 3
                colorMaskShader.Use()

                GL.BindTextureUnit(0, FBOm.gPosition)
                GL.Uniform1(colorMaskShader("isNormal"), 0)
                GL.Uniform1(colorMaskShader("mask"), MASK)

                GL.Uniform1(colorMaskShader("colorMap"), 0)
                GL.UniformMatrix4(colorMaskShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                Dim rect As New RectangleF(0, 0, width, height)

                GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                colorMaskShader.StopUse()

            Case 4
                colorMaskShader.Use()
                GL.Uniform1(colorMaskShader("isNormal"), 1)
                GL.Uniform1(colorMaskShader("mask"), MASK)

                GL.BindTextureUnit(0, FBOm.gNormal)
                GL.Uniform1(normalOffsetShader("imageMap"), 0)
                GL.UniformMatrix4(normalOffsetShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)

                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(normalOffsetShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                colorMaskShader.StopUse()

            Case 5
                colorMaskShader.Use()

                GL.BindTextureUnit(0, FBOm.gGMF)
                GL.Uniform1(colorMaskShader("isNormal"), 0)
                GL.Uniform1(colorMaskShader("mask"), MASK)

                GL.Uniform1(colorMaskShader("colorMap"), 0)
                GL.UniformMatrix4(colorMaskShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                Dim rect As New RectangleF(0, 0, width, height)

                GL.Uniform4(colorMaskShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                colorMaskShader.StopUse()


            Case 6
                colorMaskShader.Use()

                GL.BindTextureUnit(0, FBOm.gAUX_Color)
                GL.Uniform1(colorMaskShader("isNormal"), 0)
                GL.Uniform1(colorMaskShader("mask"), MASK)

                GL.Uniform1(colorMaskShader("colorMap"), 0)
                GL.UniformMatrix4(colorMaskShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                Dim rect As New RectangleF(0, 0, width, height)

                GL.Uniform4(colorMaskShader("rect"),
                                    rect.Left,
                                    -rect.Top,
                                    rect.Right,
                                    -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                colorMaskShader.StopUse()

        End Select
        GL.BindTextureUnit(0, 0)

        GLC.SwapBuffers()  ' swap back to front

        'switch back to main context
        frmMain.glControl_main.MakeCurrent()
        'GL.BindVertexArray(defaultVao)
    End Sub
    Private Sub frmTestView_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        update_screen()
    End Sub

    Private Sub frmTestView_ResizeBegin(sender As Object, e As EventArgs) Handles Me.ResizeBegin

    End Sub

    Private Sub frmTestView_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        update_screen()

    End Sub

    Private Sub b_flags_CheckedChanged(sender As Object, e As EventArgs) Handles b_flags.CheckedChanged
        update_screen()
    End Sub

    Private Sub change_mask(sender As Object, e As EventArgs)
        Dim cb As CheckBox = DirectCast(sender, CheckBox)
        MASK = MASK Xor maskList(CInt(cb.Tag))
        Label1.Text = MASK.ToString("X")
    End Sub

End Class
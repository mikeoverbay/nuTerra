
Imports System.Math
Imports System
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL



Public Class frmGbufferViewer
    Private image_scale As Single = 0.25
    Private image_id As Integer = -1
    Dim PROJECTIONMATRIX_GLC As Matrix4
    Dim GLC_VA As Integer
    Private Sub frmTestView_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Me.Hide()
        e.Cancel = True ' if we close this form, we lose the event handlers added at load time!!
    End Sub

    Private Sub frmTestView_Load(sender As Object, e As EventArgs) Handles Me.Load
        Me.Show()
        AddHandler full_scale.CheckedChanged, AddressOf CheckedChanged
        AddHandler half_scale.CheckedChanged, AddressOf CheckedChanged
        AddHandler quater_scale.CheckedChanged, AddressOf CheckedChanged

        AddHandler b_depth.CheckedChanged, AddressOf image_changed
        AddHandler b_color.CheckedChanged, AddressOf image_changed
        AddHandler b_position.CheckedChanged, AddressOf image_changed
        AddHandler b_normal.CheckedChanged, AddressOf image_changed
        AddHandler b_flags.CheckedChanged, AddressOf image_changed
        image_id = CInt(b_depth.Tag)

        GLC.MakeCurrent()
        GL.GenVertexArrays(1, GLC_VA)
        update_screen()

    End Sub


    Private Sub CheckedChanged(sender As Object, e As EventArgs)
        image_scale = CSng(sender.tag)
        update_screen()
    End Sub
    Private Sub image_changed(sender As Object, e As EventArgs)
        image_id = CInt(sender.tag)
        update_screen()
    End Sub
    Private Sub set_viewPort()
        GL.Viewport(0, 0, GLC.ClientSize.Width, GLC.ClientSize.Height)
        PROJECTIONMATRIX_GLC = Matrix4.CreateOrthographicOffCenter(0.0F, GLC.ClientSize.Width, -GLC.ClientSize.Height, 0.0F, -300.0F, 300.0F)
    End Sub
    Public Sub update_screen()

        GLC.MakeCurrent()
        Dim width, height As Integer
        set_viewPort()

        GL.ClearColor(0.0, 0.0, 0.0, 0.0)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Disable(EnableCap.DepthTest)
        GL.ActiveTexture(TextureUnit.Texture0)
        'select image and shader by selected radio button
        GL.Disable(EnableCap.Blend)
        'all gBuffer textures are the same size. so we can do this now
        GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)

        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, width)
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, height)

        h_label.Text = "Height:" + height.ToString("0000")
        w_label.Text = "Width:" + width.ToString("0000")

        width *= image_scale
        height *= image_scale

        Dim er = GL.GetError
        GL.BindVertexArray(GLC_VA)
        Dim er2 = GL.GetError

        Select Case image_id
            Case 1
                toLinearShader.Use()

                GL.Uniform1(toLinearShader("imageMap"), 0)
                GL.Uniform1(toLinearShader("far"), PRESPECTIVE_FAR)
                GL.Uniform1(toLinearShader("near"), PRESPECTIVE_NEAR)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gDepth)
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
                image2dFlipShader.Use()

                GL.Uniform1(image2dFlipShader("imageMap"), 0)
                GL.UniformMatrix4(image2dFlipShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)
                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(image2dFlipShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                image2dFlipShader.StopUse()

            Case 3
                image2dFlipShader.Use()

                GL.Uniform1(image2dFlipShader("imageMap"), 0)
                GL.UniformMatrix4(image2dFlipShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gPosition)

                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(image2dFlipShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                image2dFlipShader.StopUse()

            Case 4
                normalOffsetShader.Use()
                GL.Uniform1(normalOffsetShader("imageMap"), 0)
                GL.UniformMatrix4(normalOffsetShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gNormal)

                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(normalOffsetShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                normalOffsetShader.StopUse()

            Case 5
                image2dFlipShader.Use()

                GL.Uniform1(image2dFlipShader("imageMap"), 0)
                GL.UniformMatrix4(image2dFlipShader("ProjectionMatrix"), False, PROJECTIONMATRIX_GLC)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gGMF)

                Dim rect As New RectangleF(0, 0, width, height)
                GL.Uniform4(image2dFlipShader("rect"),
                            rect.Left,
                            -rect.Top,
                            rect.Right,
                            -rect.Bottom)

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                image2dFlipShader.StopUse()

        End Select
        GL.BindTexture(TextureTarget.Texture2D, 0)

        GLC.SwapBuffers()  ' swap back to front

        'switch back to main context
        frmMain.glControl_main.MakeCurrent()
        GL.BindVertexArray(defaultVao)

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
End Class
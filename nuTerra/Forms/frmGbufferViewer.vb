
Imports System.Math
Imports System
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL



Public Class frmGbufferViewer
    Private image_scale As Single = 0.25
    Private image_id As Integer = -1

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
        GL.MatrixMode(MatrixMode.Projection) 'Select Projection
        GL.LoadIdentity() 'Reset The Matrix
        GL.Ortho(0, GLC.ClientSize.Width, -GLC.ClientSize.Height, 0, 30.0, -30.0) 'Select Ortho Mode
        GL.MatrixMode(MatrixMode.Modelview)    'Select Modelview Matrix
        GL.LoadIdentity() 'Reset The Matrix

    End Sub
    Public Sub update_screen()
        'If Not MAP_LOADED Then Return
        GLC.MakeCurrent()
        Dim width, height As Integer
        set_viewPort()

        GL.ClearColor(0.0, 0.0, 0.0, 0.0)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Disable(EnableCap.DepthTest)
        GL.Enable(EnableCap.Texture2D)
        GL.ActiveTexture(TextureUnit.Texture0)
        'select image and shader by selected radio button
        GL.Disable(EnableCap.Blend)
        Select Case image_id
            Case 1
                GL.UseProgram(shader_list.toLinear_shader)
                GL.Uniform1(toLinear_text_id, 0)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gDepth)
            Case 2
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gColor)
            Case 3
                'GL.BindTexture(TextureTarget.Texture2D, gPosition)
            Case 4
                GL.UseProgram(shader_list.normalOffset_shader)
                GL.Uniform1(normalOffset_text_id, 0)
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gNormal)
            Case 5
                GL.BindTexture(TextureTarget.Texture2D, FBOm.gGMF)

        End Select

        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, width)
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, height)
        h_label.Text = "Height:" + height.ToString("0000")
        w_label.Text = "Width:" + width.ToString("0000")
        width *= image_scale
        height *= image_scale
        GL.Begin(PrimitiveType.Quads)

        GL.TexCoord2(0.0, 1.0)
        GL.Vertex2(0.0, 0.0)

        GL.TexCoord2(1.0, 1.0)
        GL.Vertex2(width, 0.0)

        GL.TexCoord2(1.0, 0.0)
        GL.Vertex2(width, -height)

        GL.TexCoord2(0.0, 0.0)
        GL.Vertex2(0.0, -height)
        GL.End()

        GL.BindTexture(TextureTarget.Texture2D, 0)
        GL.UseProgram(0)

        GLC.SwapBuffers()  ' swap back to front

        'switch back to main context
        frmMain.glControl_main.MakeCurrent()
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
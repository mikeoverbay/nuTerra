Imports Tao.DevIl
Imports System.IO
Imports OpenTK.Graphics.OpenGL

Public Class frmScreenCap

    Private Sub save_btn_Click(sender As Object, e As EventArgs) Handles save_btn.Click
        Select Case True
            Case rb_png.Checked
                Save_Dialog.Filter = "PNG|*.png"
                Save_Dialog.Title = "Save PNG"
            Case rb_jpg.Checked
                Save_Dialog.Filter = "JPG|*.jpg"
                Save_Dialog.Title = "Save JPG"
            Case rb_dds.Checked
                Il.ilSetInteger(Il.IL_DXTC_FORMAT, Il.IL_DXT5)
                Save_Dialog.Filter = "DDS|*.dds"
                Save_Dialog.Title = "Save DDS"
        End Select
        If Not Save_Dialog.ShowDialog = Windows.Forms.DialogResult.OK Then
            Me.Close()
        End If
        draw_scene()
        frmMain.glControl_main.MakeCurrent()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        Il.ilEnable(Il.IL_FILE_OVERWRITE)
        Dim Id As Integer = Il.ilGenImage
        Il.ilBindImage(Id)
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1)
        Dim er = Il.ilGetError
        Il.ilTexImage(FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT, 0, 3, Il.IL_RGB, Il.IL_UNSIGNED_BYTE, Nothing)
        er = GL.GetError
        GL.ReadPixels(0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT, InternalFormat.Rgb, PixelType.UnsignedByte, Il.ilGetData())
        er = GL.GetError
        GL.PixelStore(PixelStoreParameter.PackAlignment, 4)
        GL.ReadBuffer(ReadBufferMode.Front)


        Dim p = Save_Dialog.FileName
        Dim status As Boolean
        Select Case True
            Case rb_png.Checked
                status = Il.ilSave(Il.IL_PNG, p)
            Case rb_jpg.Checked
                status = Il.ilSave(Il.IL_JPG, p)
            Case rb_dds.Checked
                status = Il.ilSave(Il.IL_DDS, p)
        End Select
        Il.ilBindImage(0)
        Il.ilDeleteImage(Id)
        If Not status Then
            MsgBox("Failed to save " + p, MsgBoxStyle.Exclamation, "File Save Failed!")
        End If
        Me.Close()

    End Sub
End Class
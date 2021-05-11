Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports System.Drawing.Imaging

Public Class frmScreenCap

    Private Sub save_btn_Click(sender As Object, e As EventArgs) Handles save_btn.Click
        Select Case True
            Case rb_png.Checked
                Save_Dialog.Filter = "PNG|*.png"
                Save_Dialog.Title = "Save PNG"
            Case rb_jpg.Checked
                Save_Dialog.Filter = "JPG|*.jpg"
                Save_Dialog.Title = "Save JPG"
        End Select

        If Not Save_Dialog.ShowDialog = Windows.Forms.DialogResult.OK Then
            Me.Close()
        End If

        draw_scene()

        frmMain.glControl_main.MakeCurrent()

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        GL.PixelStore(PixelStoreParameter.PackAlignment, 1)

        Using bmp As New Bitmap(MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT, Imaging.PixelFormat.Format24bppRgb)
            Dim bitmapData = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                          ImageLockMode.WriteOnly,
                                          bmp.PixelFormat)

            GL.ReadPixels(0, 0, MainFBO.SCR_WIDTH, MainFBO.SCR_HEIGHT, OpenGL.PixelFormat.Bgr, PixelType.UnsignedByte, bitmapData.Scan0)

            bmp.UnlockBits(bitmapData)
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY)
            bmp.Save(Save_Dialog.FileName, If(rb_jpg.Checked, ImageFormat.Jpeg, ImageFormat.Png))
        End Using

        GL.PixelStore(PixelStoreParameter.PackAlignment, 4)
        GL.ReadBuffer(ReadBufferMode.Front)

        Me.Close()
    End Sub
End Class
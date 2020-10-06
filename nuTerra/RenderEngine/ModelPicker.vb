Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ModelPicker
    Sub PickModel()
        ' get viewport size
        Dim viewport(3) As Integer
        GL.GetInteger(GetPName.Viewport, viewport)

        ' get pixel
        Dim pixel(0) As UShort
        GL.NamedFramebufferReadBuffer(FBO_main.mainFBO, ReadBufferMode.ColorAttachment4)
        GL.ReadPixels(MOUSE.X, viewport(3) - MOUSE.Y, 1, 1, PixelFormat.Red, PixelFormat.UnsignedShort, pixel)

        Dim index = pixel(0)

        If index > 0 Then
            PICKED_STRING = PICK_DICTIONARY(index - 1)
        Else
            PICKED_STRING = "0"
        End If

    End Sub
End Module

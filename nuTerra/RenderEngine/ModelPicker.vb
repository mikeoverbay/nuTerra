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
        Dim pixel(1) As UInt16
        GL.NamedFramebufferReadBuffer(FBO_main.mainFBO, ReadBufferMode.ColorAttachment4)
        GL.ReadPixels(MOUSE.X, viewport(3) - MOUSE.Y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, pixel)
        Dim index As UInt32 = pixel(0) ' + (pixel(1) * 255)

        If index > 0 And index < 65535 Then
            PICKED_STRING = "ID " + index.ToString + " " + PICK_DICTIONARY(index - 1)
        Else
            PICKED_STRING = "0" ' May just want this to be ""
        End If


    End Sub
End Module

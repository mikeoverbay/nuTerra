Imports OpenTK.Graphics.OpenGL4

Module ModelPicker
    Sub PickModel()

        ' get viewport size
        Dim viewport(3) As Integer
        GL.GetInteger(GetPName.Viewport, viewport)
        ' get pixel
        Dim pixel(1) As UInt16
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment4)
        GL.ReadPixels(Window.mouse_last_pos.X, viewport(3) - Window.mouse_last_pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, pixel)
        Dim index As UInt32 = pixel(0) ' + (pixel(1) * 255)

        If index > 0 AndAlso index < 65535 Then
            PICKED_STRING = "ID " + index.ToString + " : " + PICK_DICTIONARY(index - 1)
            PICKED_MODEL_INDEX = index
        Else
            PICKED_STRING = "0" ' May just want this to be ""
            PICKED_MODEL_INDEX = 0
        End If


    End Sub
End Module

Imports System.Math
Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ModelPicker
    Sub PickModel()
        FBOm.attach_CNGP()

        ' Then blit out framebuffer to the default buffer
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, FBO_main.mainFBO)
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0)
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0)
        ' clear to blue to distinguish between draw framebuffer; color:blue
        GL.ClearColor(0.0F, 0.0F, 1.0F, 1.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)
        GL.BlitFramebuffer(0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                           0, 0, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT,
                           ClearBufferMask.ColorBufferBit, TextureMinFilter.Nearest)

        GL.ReadBuffer(ReadBufferMode.Back)
        Dim viewport(4) As Integer
        Dim pixel() As Byte = {0, 0}
        GL.GetInteger(GetPName.Viewport, viewport)
        GL.ReadPixels(MOUSE.X, viewport(3) - MOUSE.Y, 1, 1,
                      PixelFormat.Rgba, InternalFormat.Rgba8, pixel)
        'Dim index As UInt16 = (CUInt(pixel(1) * 256) + pixel(0))
        Dim index = pixel(0)
        PICKED_STRING = index.ToString
        'Return
        If index > 0 Then
            PICKED_STRING = PICK_DICTIONARY(index - 1)
        Else
            PICKED_STRING = "0"
        End If

    End Sub
End Module

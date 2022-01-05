Imports OpenTK.Graphics.OpenGL4

NotInheritable Class ModelPicker
    Shared _PICK_MODELS As Boolean

    Public Shared Property PICK_MODELS As Boolean
        Get
            Return _PICK_MODELS
        End Get
        Set(value As Boolean)
            If value Then
                modelShader.SetDefine("PICK_MODELS")
            Else
                modelShader.UnsetDefine("PICK_MODELS")
            End If
            _PICK_MODELS = value
        End Set
    End Property

    Public Shared Sub PickModel()

        ' get viewport size
        Dim viewport(3) As Integer
        GL.GetInteger(GetPName.Viewport, viewport)
        ' get pixel
        Dim pixel(1) As UInt16
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment4)
        GL.ReadPixels(Window.mouse_last_pos.X, viewport(3) - Window.mouse_last_pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, pixel)
        Dim index As UInt32 = pixel(0) ' + (pixel(1) * 255)

        If index > 0 AndAlso index < 65535 Then
            map_scene.PICKED_STRING = index.ToString + ": " + map_scene.PICK_DICTIONARY(index - 1)
            map_scene.PICKED_MODEL_INDEX = index
        Else
            map_scene.PICKED_STRING = ""
            map_scene.PICKED_MODEL_INDEX = 0
        End If


    End Sub
End Class

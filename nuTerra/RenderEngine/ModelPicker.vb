Imports OpenTK.Graphics.OpenGL4

NotInheritable Class ModelPicker
    Shared _enabled As Boolean

    ' Tree picks live above the model-id range: id = base + species index + 1.
    ' R16UI holds 65535; no map comes near 60000 model instances.
    Public Const TREE_PICK_BASE As Integer = 60000

    Public Shared Property Enabled As Boolean
        Get
            Return _enabled
        End Get
        Set(value As Boolean)
            If value <> _enabled Then
                If value Then
                    modelShader.SetDefine("PICK_MODELS")
                    treeShader.SetDefine("PICK_MODELS")
                Else
                    modelShader.UnsetDefine("PICK_MODELS")
                    treeShader.UnsetDefine("PICK_MODELS")
                End If
                _enabled = value
            End If
        End Set
    End Property

    Public Shared Sub PickModel()
        ' get pixel
        Dim pixel(1) As UInt16
        MainFBO.fbo.ReadBuffer(ReadBufferMode.ColorAttachment4)
        GL.ReadPixels(Window.mouse_last_pos.X, MainFBO.height - Window.mouse_last_pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, pixel)
        Dim index As UInt32 = pixel(0) ' + (pixel(1) * 255)

        If index > TREE_PICK_BASE AndAlso index < 65535 Then
            ' Tree band: one id per species.
            map_scene.PICKED_STRING = "tree: " + map_scene.trees.pick_name(CInt(index) - TREE_PICK_BASE - 1)
            map_scene.PICKED_MODEL_INDEX = 0
        ElseIf index > 0 AndAlso index < 65535 Then
            map_scene.PICKED_STRING = index.ToString + ": " + map_scene.PICK_DICTIONARY(index - 1)
            map_scene.PICKED_MODEL_INDEX = index
        Else
            map_scene.PICKED_STRING = ""
            map_scene.PICKED_MODEL_INDEX = 0
        End If
    End Sub
End Class

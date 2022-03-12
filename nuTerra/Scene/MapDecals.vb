Imports OpenTK.Graphics.OpenGL4

Public Class MapDecals
    Implements IDisposable

    ReadOnly scene As MapScene

    Public decals_ssbo As GLBuffer
    Public decals_count As Integer

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub draw_boxes()
        GL_PUSH_GROUP("draw_boxes")

        defaultVao.Bind()

        boxDecalsShader.Use()
        GL.DrawArrays(PrimitiveType.Points, 0, decals_count)
        boxDecalsShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        decals_ssbo?.Dispose()
    End Sub
End Class

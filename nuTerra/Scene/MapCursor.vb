Imports System.IO
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapCursor
    Implements IDisposable

    ReadOnly scene As MapScene

    Public CURSOR_TEXTURE_ID As GLTexture

    Public Sub New(scene As MapScene)
        Me.scene = scene
        CURSOR_TEXTURE_ID = load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\Cursor.png"), True, False)
    End Sub

    Public Sub draw_map_cursor()
        GL_PUSH_GROUP("draw_map_cursor")

        DecalProject.Use()

        GL.Uniform3(DecalProject("color_in"), 0.4F, 0.3F, 0.3F)

        CURSOR_TEXTURE_ID.BindUnit(0)
        MainFBO.gDepth.BindUnit(1)
        MainFBO.gGMF.BindUnit(2)

        ' Track the terrain at Y
        Dim model_X = Matrix4.CreateTranslation(scene.camera.U_LOOK_AT_X, CURSOR_Y, scene.camera.U_LOOK_AT_Z)
        Dim model_S = Matrix4.CreateScale(25.0F, 50.0F, 25.0F)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        'GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DecalProject("DecalMatrix"), False, rotate * model_S * model_X)

        CUBE_VAO.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DecalProject.StopUse()

        ' UNBIND
        unbind_textures(3)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        CURSOR_TEXTURE_ID?.Dispose()
    End Sub
End Class

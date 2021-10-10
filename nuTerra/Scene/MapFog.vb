Imports System.IO
Imports System.Math
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapFog
    Implements IDisposable

    ReadOnly scene As MapScene

    Public NOISE_id As GLTexture
    Public uv_location As New Vector2

    Public Sub New(scene As MapScene)
        Me.scene = scene
        NOISE_id = load_png_image_from_file(Path.Combine(Application.StartupPath, "Resources\noise.png"), True, True)
    End Sub

    Public Sub global_fog()
        GL_PUSH_GROUP("perform_Fog_Noise_pass")

        Dim s = 0.03F * DELTA_TIME ' <---- How fast the fog moves

        'this is in the game data somewhere!
        Dim move_vector = New Vector2(0.3, 0.7) ' <----  Direction the fog moves

        uv_location += move_vector * s '<----  do the math;

        DeferredFogShader.Use()

        GL.Uniform1(DeferredFogShader("uv_scale"), 4.0F)
        GL.Uniform2(DeferredFogShader("move_vector"), uv_location.X, uv_location.Y)

        NOISE_id.BindUnit(0)
        MainFBO.gDepth.BindUnit(1)
        MainFBO.gPosition.BindUnit(2)
        MainFBO.gColor.BindUnit(3)
        'FBOm.gColor_2.BindUnit(4)

        map_center.X = 100.0F * (theMap.bounds_minX + theMap.bounds_maxX) / 2.0F
        map_center.Y = 1.0F
        map_center.Z = 100.0F * (theMap.bounds_minY + theMap.bounds_maxY) / 2.0F
        map_center.X += 50.0F
        map_center.Z += 50.0F

        scale.X = 100.0F * (Abs(theMap.bounds_minX) + Abs(theMap.bounds_maxX) + 1.0F)
        scale.Y = 1000.0F
        scale.Z = 100.0F * (Abs(theMap.bounds_minY) + Abs(theMap.bounds_maxY) + 1.0F)

        'scale *= 0.1
        Dim model_X = Matrix4.CreateTranslation(map_center)
        Dim model_S = Matrix4.CreateScale(scale)

        ' I spent 2 hours making boxes in AC3D and no matter what, it still needs rotated!
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        'GL.Enable(EnableCap.CullFace)

        GL.UniformMatrix4(DeferredFogShader("DecalMatrix"), False, rotate * model_S * model_X)

        CUBE_VAO.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        DeferredFogShader.StopUse()

        ' MULTI UNBIND
        GL.BindTextures(0, 4, {0, 0, 0, 0})

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        NOISE_id?.Dispose()
    End Sub
End Class

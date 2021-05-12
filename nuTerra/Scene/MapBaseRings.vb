Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Public Class MapBaseRings
    Implements IDisposable

    Public Sub draw_base_rings_deferred()
        If Not BASE_RINGS_LOADED Then
            Return
        End If
        GL_PUSH_GROUP("draw_terrain_base_rings_deferred")

        BaseRingProjectorDeferred.Use()

        GL.Disable(EnableCap.CullFace)

        MainFBO.gDepth.BindUnit(0)
        MainFBO.gGMF.BindUnit(1)
        MainFBO.gPosition.BindUnit(2)

        'constants
        GL.Uniform1(BaseRingProjectorDeferred("radius"), 50.0F)
        GL.Uniform1(BaseRingProjectorDeferred("thickness"), 2.0F)
        Dim rotate = Matrix4.CreateRotationX(1.570796)
        Dim scale = Matrix4.CreateScale(120.0F, 25.0F, 120.0F)

        ' base 1 ring

        Dim model_X = Matrix4.CreateTranslation(-TEAM_1.X, T1_Y, TEAM_1.Z)

        GL.Uniform3(BaseRingProjectorDeferred("ring_center"), -TEAM_1.X, TEAM_1.Y, TEAM_1.Z)
        GL.UniformMatrix4(BaseRingProjectorDeferred("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjectorDeferred("color"), New Color4(0.0F, 128.0F, 0.0F, 0.5F))

        CUBE_VAO.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        'base 2 ring
        model_X = Matrix4.CreateTranslation(-TEAM_2.X, T2_Y, TEAM_2.Z)

        'check in side of cube
        If cube_point_intersection(rotate, scale, model_X, CAM_POSITION) Then
            GL.Uniform1(BaseRingProjectorDeferred("front"), CInt(True))
        Else
            GL.Uniform1(BaseRingProjectorDeferred("front"), CInt(False))
        End If

        GL.Uniform3(BaseRingProjectorDeferred("ring_center"), -TEAM_2.X, TEAM_2.Y, TEAM_2.Z)
        GL.UniformMatrix4(BaseRingProjectorDeferred("ModelMatrix"), False, rotate * scale * model_X)
        GL.Uniform4(BaseRingProjectorDeferred("color"), New Color4(128.0F, 0.0F, 0.0F, 0.5F))

        CUBE_VAO.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)

        BaseRingProjectorDeferred.StopUse()

        ' UNBIND
        unbind_textures(3)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
    End Sub
End Class

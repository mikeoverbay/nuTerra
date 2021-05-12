Imports OpenTK.Graphics.OpenGL4
Imports OpenTK

Public Class MapSky
    Implements IDisposable

    Public skybox_mdl As XModel
    Public texture As GLTexture

    Public Sub draw_sky()
        If Not DONT_BLOCK_SKY Then Return

        GL_PUSH_GROUP("Draw_SkyDome")

        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        MainFBO.attach_CNGP()

        SkyDomeShader.Use()

        GL.Enable(EnableCap.CullFace)

        map_scene.sky.texture.BindUnit(0)

        map_scene.sky.skybox_mdl.vao.Bind()
        GL.DrawElements(PrimitiveType.Triangles, map_scene.sky.skybox_mdl.indices_count * 3, DrawElementsType.UnsignedShort, 0)

        SkyDomeShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL.DepthMask(True)

        draw_sun()

        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Public Sub draw_sun()

        MainFBO.attach_C()

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        Dim matrix = Matrix4.CreateTranslation(LIGHT_POS)

        FF_BillboardShader.Use()
        GL.Uniform1(FF_BillboardShader("colorMap"), 0)
        GL.UniformMatrix4(FF_BillboardShader("matrix"), False, matrix)
        'GL.Uniform3(FF_BillboardShader("color"), SUN_RENDER_COLOR.X / 100.0F, SUN_RENDER_COLOR.Y / 100.0F, SUN_RENDER_COLOR.Z / 100.0F)
        GL.Uniform3(FF_BillboardShader("color"), 1.0F, 1.0F, 1.0F)
        GL.Uniform1(FF_BillboardShader("scale"), SUN_SCALE * 6)

        SUN_TEXTURE_ID.BindUnit(0)

        GL.Uniform4(FF_BillboardShader("rect"), -0.5F, -0.5F, 0.5F, 0.5F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FF_BillboardShader.StopUse()

        GL.Disable(EnableCap.Blend)

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        skybox_mdl?.vao.Dispose()
        texture?.Dispose()
    End Sub
End Class

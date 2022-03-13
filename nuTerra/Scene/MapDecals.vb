Imports OpenTK.Graphics.OpenGL4

Public Class MapDecals
    Implements IDisposable

    ReadOnly scene As MapScene

    Public decals_ssbo As GLBuffer
    Public decals_count As Integer

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub draw_decals()
        GL_PUSH_GROUP("draw_decals")
        'Missing a lot a decals and not sure why.
        'GL.Disable(EnableCap.DepthTest)

        GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd)
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.SrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Enable(EnableCap.Blend)

        GL.DepthMask(False)

        CUBE_VAO.Bind()
        MainFBO.gDepth.BindUnit(0)
        MainFBO.gGMF.BindUnit(1)

        boxDecalsColorShader.Use()
        MainFBO.attach_C()
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 14, decals_count)
        boxDecalsColorShader.StopUse()

        GL.Disable(EnableCap.Blend)

        boxDecalsNormalShader.Use()

        MainFBO.attach_N()
        'MainFBO.attach_Depth()
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 14, decals_count)
        boxDecalsNormalShader.StopUse()


        GL.DepthMask(True)


        'GL.Enable(EnableCap.DepthTest)

        ' UNBIND
        unbind_textures(2)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        decals_ssbo?.Dispose()
    End Sub
End Class

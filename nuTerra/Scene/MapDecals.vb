﻿Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Structure DecalGLInfo
    Dim matrix As Matrix4
    Dim color_tex As GLTexture
    Dim normal_tex As GLTexture
End Structure


Public Class MapDecals
    Implements IDisposable

    ReadOnly scene As MapScene

    Public all_decals As List(Of DecalGLInfo)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub draw_decals()
        GL_PUSH_GROUP("draw_decals")

        CUBE_VAO.Bind()
        MainFBO.gDepth.BindUnit(0)
        MainFBO.gGMF.BindUnit(1)

        GL.Enable(EnableCap.Blend)

        MainFBO.attach_CN()

        boxDecalsColorShader.Use()

        For Each decal In all_decals
            GL.UniformMatrix4(boxDecalsColorShader("mvp"), False, decal.matrix * map_scene.camera.PerViewData.viewProj)
            decal.color_tex.BindUnit(2)
            decal.normal_tex.BindUnit(3)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)
        Next

        boxDecalsColorShader.StopUse()

        GL.Disable(EnableCap.Blend)

        ' UNBIND
        unbind_textures(4)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        all_decals = Nothing
    End Sub
End Class

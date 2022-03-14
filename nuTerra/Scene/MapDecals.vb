Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Structure DecalGLInfo
    Dim matrix As Matrix4
    Dim color_tex As GLTexture
    Dim normal_tex As GLTexture
    Dim gSurfaceNormal As GLTexture
    Dim offset As Vector2
    Dim scale As Vector2
    Dim influence As Int32
    Dim color_only As Int32
    Dim flag3 As UInt32
    Dim material_type As Single
    Dim winding As UInt32
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

        MainFBO.attach_CN()

        MainFBO.gDepth.BindUnit(0)
        MainFBO.gGMF.BindUnit(1)
        MainFBO.gSurfaceNormal.BindUnit(4)
        MainFBO.gPosition.BindUnit(5)

        'GL.Disable(EnableCap.CullFace)

        GL.Enable(EnableCap.Blend)
        GL.DepthMask(False) ' stops decals from Z fighting

        boxDecalsColorShader.Use()
        ''-- scale up y some so terrain doesn't clip it.
        Dim mat = Matrix4.Identity
        mat.M22 = 1.0

        For Each decal In all_decals
            GL.UniformMatrix4(boxDecalsColorShader("mvp"), False, mat * decal.matrix * map_scene.camera.PerViewData.viewProj)

            'because the fucking winding order is wrong on some decals, we have to switch based on determinate 
            GL.FrontFace(decal.winding)

            decal.color_tex.BindUnit(3)
            decal.normal_tex.BindUnit(2)
            GL.Uniform2(boxDecalsColorShader("offset"), decal.offset.X, decal.offset.Y)
            GL.Uniform2(boxDecalsColorShader("scale"), decal.scale.X, decal.scale.Y)
            GL.Uniform1(boxDecalsColorShader("enfluence"), decal.influence)


            GL.Uniform1(boxDecalsColorShader("mtype"), decal.material_type)


            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)
        Next

        boxDecalsColorShader.StopUse()

        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)

        ' UNBIND
        unbind_textures(5)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        all_decals = Nothing
    End Sub
End Class

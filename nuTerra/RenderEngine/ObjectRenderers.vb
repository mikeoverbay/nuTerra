Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ObjectRenderers

    Public Sub draw_the_light(ByVal location As Vector3)


    End Sub

    Public Sub draw_cross_hair()
        CrossHairShader.Use()
        Dim LOOK_Y = CURSOR_Y + U_LOOK_AT_Y

        Dim position = Matrix4.CreateTranslation(U_LOOK_AT_X, LOOK_Y, U_LOOK_AT_Z)
        Dim MVPM = position * VIEWMATRIX * PROJECTIONMATRIX
        GL.UniformMatrix4(CrossHairShader("ProjectionMatrix"), False, MVPM)

        GL.BindTextureUnit(0, CROSS_HAIR_TEXTURE)
        GL.Uniform1(CrossHairShader("colorMap"), 0)

        GL.Uniform4(CrossHairShader("shade"), 1.0F, 1.0F, 1.0F, 1.0F)
        GL.Uniform1(CrossHairShader("time"), CROSS_HAIR_TIME) 'animation

        GL.BindVertexArray(CROSS_HAIR.mdl_VAO)
        GL.DrawElements(PrimitiveType.Triangles,
                        CROSS_HAIR.indice_count * 3,
                        DrawElementsType.UnsignedShort,
                        0)

        'GL.BindVertexArray(0)
        CrossHairShader.StopUse()

        GL.BindTextureUnit(0, 0)
    End Sub


End Module

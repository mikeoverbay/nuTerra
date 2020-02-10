Imports System.Math
Imports System.Runtime.InteropServices.Marshal
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module ObjectRenderers

    Public Sub draw_one_damn_moon(ByVal location As Vector3)

        Dim model = Matrix4.CreateTranslation(location.X, location.Y, location.Z)

        Dim scale_ As Single = 30.0
        Dim sMat = Matrix4.CreateScale(scale_)

        Dim MVPM = sMat * model * MODELVIEWMATRIX * PROJECTIONMATRIX
        colorOnlyShader.Use()

        GL.Uniform3(colorOnlyShader("color"), 1.0F, 1.0F, 0.0F)

        GL.UniformMatrix4(colorOnlyShader("ProjectionMatrix"), False, MVPM)

        GL.BindVertexArray(MOON.mdl_VAO)

        GL.DrawElements(PrimitiveType.Triangles, (MOON.indice_count * 3), DrawElementsType.UnsignedShort, MOON.index_buffer16)

        GL.BindVertexArray(0)
        colorOnlyShader.StopUse()
    End Sub

    Public Sub draw_cross_hair()

        Dim position = Matrix4.CreateTranslation(U_LOOK_AT_X, U_LOOK_AT_Y, U_LOOK_AT_Z)



        CrossHairShader.Use()

        Dim MVPM = position * MODELVIEWMATRIX * PROJECTIONMATRIX
        GL.UniformMatrix4(CrossHairShader("ProjectionMatrix"), False, MVPM)

        GL.Uniform1(CrossHairShader("colorMap"), 0)

        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, CROSS_HAIR_TEXTURE)
        GL.Uniform4(CrossHairShader("shade"), 1.0F, 0.0F, 0.0F, 1.0F)

        GL.BindVertexArray(CROSS_HAIR.mdl_VAO)

        GL.DrawElements(PrimitiveType.Triangles, (CROSS_HAIR.indice_count * 3), DrawElementsType.UnsignedShort, CROSS_HAIR.index_buffer16)

        GL.BindVertexArray(0)

        GL.BindTexture(TextureTarget.Texture2D, 0)

        CrossHairShader.StopUse()
    End Sub


End Module

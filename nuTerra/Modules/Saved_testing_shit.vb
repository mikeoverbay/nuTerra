Module Saved_testing_shit

#If 0 Then
    '====================================================
    ' temp drawing of BB to debug the culling code.
    '====================================================
        GL.UseProgram(shader_list.colorOnly_shader)

        GL.EnableClientState(ArrayCap.VertexArray)
        For z = 0 To MODEL_INDEX_LIST.Length - 2
    Dim idx = MODEL_INDEX_LIST(z).model_index

            If Not MAP_MODELS(idx).mdl(0).junk Then

    Dim modelMatrix = MODEL_INDEX_LIST(z).matrix
    Dim MVM = MODELVIEWMATRIX
    Dim MVPM = MVM * PROJECTIONMATRIX

                GL.UseProgram(shader_list.colorOnly_shader)

                GL.Uniform3(colorOnly_color_id, 1.0F, 1.0F, 0.0F)

                GL.UniformMatrix4(colorOnly_PrjMatrix_id, False, MVPM)

                GL.VertexPointer(3, VertexPointerType.Float, 12, MODEL_INDEX_LIST(z).ta)
                GL.DrawArrays(PrimitiveType.LineStrip, 0, 10)
            End If

        Next
        GL.DisableClientState(ArrayCap.VertexArray)

        GL.UseProgram(0)

#End If

End Module

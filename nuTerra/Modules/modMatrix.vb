Imports OpenTK

Module modMatrix
    Public Function Transform_vertex_by_Matrix4(ByRef v As Vector3, ByRef m As Matrix4) As Vector3
        Dim mm = New Matrix3(m)
        Dim vo As Vector3
        vo = Vector3.Transform(mm, v)

        vo.X += m.M41
        vo.Y += m.M42
        vo.Z += m.M43
        Return vo
    End Function

    Public Function UnProject(ByRef vec As Vector4) As Vector4

        vec.Z = 0
        vec.W = 1.0F

        Dim viewInv As Matrix4 = Matrix4.Invert(VIEWMATRIX)
        Dim projInv As Matrix4 = Matrix4.Invert(PROJECTIONMATRIX)

        Vector4.Transform(vec, projInv, vec)
        Vector4.Transform(vec, viewInv, vec)

        If vec.W > Single.Epsilon OrElse vec.W < Single.Epsilon Then
            vec.X /= vec.W
            vec.Y /= vec.W
            vec.Z /= vec.W
        End If

        Return vec
    End Function

    Public Function UnProject_Chunk(ByRef vec As Vector4, ByRef model As Matrix4) As Vector4

        Dim pos As Vector4

        Vector4.Transform(vec, model, pos)
        Vector4.Transform(pos, VIEWMATRIX_Saved, pos)
        Vector4.Transform(pos, PROJECTIONMATRIX_Saved, pos)

        pos.X /= pos.W
        pos.Y /= pos.W

        pos.X = ((pos.X + 1.0F) * 0.5F) * FBOm.SCR_WIDTH
        pos.Y = ((1.0 - pos.Y) * 0.5F) * FBOm.SCR_HEIGHT

        Return pos

    End Function
End Module

Imports OpenTK.Graphics.OpenGL4
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

    Public Sub Transform_BB(ByRef mm As MODEL_INDEX_LIST_)
    End Sub

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


End Module

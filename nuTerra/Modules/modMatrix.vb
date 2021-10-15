Imports OpenTK.Mathematics

Module modMatrix
    Public Function Transform_vertex_by_Matrix4(ByRef v As Vector3, ByRef m As Matrix4) As Vector3
        Dim mm = New Matrix3(m)
        Dim vo = Vector3.TransformRow(v, mm)

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

        Vector4.TransformRow(vec, projInv, vec)
        Vector4.TransformRow(vec, viewInv, vec)

        If vec.W > Single.Epsilon OrElse vec.W < Single.Epsilon Then
            vec.X /= vec.W
            vec.Y /= vec.W
            vec.Z /= vec.W
        End If

        Return vec
    End Function

    Public Function UnProject_Chunk(ByRef vec As Vector4, ByRef model As Matrix4) As Vector4

        Dim pos As Vector4

        Vector4.TransformRow(vec, model, pos)
        Vector4.TransformRow(pos, map_scene.camera.PerViewData.view, pos)
        Vector4.TransformRow(pos, map_scene.camera.PerViewData.projection, pos)

        pos.X /= pos.W
        pos.Y /= pos.W

        pos.X = ((pos.X + 1.0F) * 0.5F) * MainFBO.width
        pos.Y = ((1.0 - pos.Y) * 0.5F) * MainFBO.height

        Return pos

    End Function
End Module

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
End Module

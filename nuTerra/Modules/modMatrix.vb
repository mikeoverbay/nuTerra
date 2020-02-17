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
        'creates a transformed bounding box for frustum clipping.
        Dim v1, v2, v3, v4, v5, v6, v7, v8 As Vector3
        v1.Z = mm.BB_Max.Z : v2.Z = mm.BB_Max.Z : v3.Z = mm.BB_Max.Z : v4.Z = mm.BB_Max.Z

        v5.Z = mm.BB_Min.Z : v6.Z = mm.BB_Min.Z : v7.Z = mm.BB_Min.Z : v8.Z = mm.BB_Min.Z

        v1.X = mm.BB_Min.X : v6.X = mm.BB_Min.X : v7.X = mm.BB_Min.X : v4.X = mm.BB_Min.X

        v5.X = mm.BB_Max.X : v8.X = mm.BB_Max.X : v3.X = mm.BB_Max.X : v2.X = mm.BB_Max.X

        v4.Y = mm.BB_Max.Y : v7.Y = mm.BB_Max.Y : v8.Y = mm.BB_Max.Y : v3.Y = mm.BB_Max.Y

        v6.Y = mm.BB_Min.Y : v5.Y = mm.BB_Min.Y : v1.Y = mm.BB_Min.Y : v2.Y = mm.BB_Min.Y

        mm.BB(0) = v1
        mm.BB(1) = v2
        mm.BB(2) = v3
        mm.BB(3) = v4
        mm.BB(4) = v5
        mm.BB(5) = v6
        mm.BB(6) = v7
        mm.BB(7) = v8

        For i = 0 To 7
            mm.BB(i) = Transform_vertex_by_Matrix4(mm.BB(i), mm.matrix)
        Next
    End Sub
End Module

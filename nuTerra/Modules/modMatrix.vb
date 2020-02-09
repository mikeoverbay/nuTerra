Imports OpenTK

Module modMatrix
    Public Function translate_to(ByRef v As Vector3, ByRef m As Matrix4) As Vector3
        Dim vo As Vector3

        vo.X = (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z)
        vo.Y = (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z)
        vo.Z = (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z)

        vo.X += m.M41
        vo.Y += m.M42
        vo.Z += m.M43
        Return vo
    End Function

    Public Sub get_translated_bb_model(ByRef mm As model_matrix_list_)
        'creates a transformed bounding box for screen clipping.
        Dim v1, v2, v3, v4, v5, v6, v7, v8 As Vector3
        v1.z = mm.BB_Max.z : v2.z = mm.BB_Max.z : v3.z = mm.BB_Max.z : v4.z = mm.BB_Max.z
        v5.z = mm.BB_Min.z : v6.z = mm.BB_Min.z : v7.z = mm.BB_Min.z : v8.z = mm.BB_Min.z

        v1.x = mm.BB_Min.x : v6.x = mm.BB_Min.x : v7.x = mm.BB_Min.x : v4.x = mm.BB_Min.x
        v5.x = mm.BB_Max.x : v8.x = mm.BB_Max.x : v3.x = mm.BB_Max.x : v2.x = mm.BB_Max.x

        v4.y = mm.BB_Max.y : v7.y = mm.BB_Max.y : v8.y = mm.BB_Max.y : v3.y = mm.BB_Max.y
        v6.y = mm.BB_Min.y : v5.y = mm.BB_Min.y : v1.y = mm.BB_Min.y : v2.y = mm.BB_Min.y

        mm.BB(0) = v1
        mm.BB(1) = v2
        mm.BB(2) = v3
        mm.BB(3) = v4
        mm.BB(4) = v5
        mm.BB(5) = v6
        mm.BB(6) = v7
        mm.BB(7) = v8

        For i = 0 To 7
            mm.BB(i) = translate_to(mm.BB(i), mm.matrix)
        Next
    End Sub
End Module

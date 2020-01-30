Imports System.Math
Module modMatrix
    Public Function rotate_only(ByVal v As vect3, ByVal m() As Single) As vect3
        Dim vo As vect3
        vo.x = (m(0) * v.x) + (m(4) * v.y) + (m(8) * v.z)
        vo.y = (m(1) * v.x) + (m(5) * v.y) + (m(9) * v.z)
        vo.z = (m(2) * v.x) + (m(6) * v.y) + (m(10) * v.z)
        Return vo
    End Function
    Public Function translate_to(ByVal v As vect3, ByVal m() As Single) As vect3
        Dim vo As vect3
        vo.x = (m(0) * v.x) + (m(4) * v.y) + (m(8) * v.z)
        vo.y = (m(1) * v.x) + (m(5) * v.y) + (m(9) * v.z)
        vo.z = (m(2) * v.x) + (m(6) * v.y) + (m(10) * v.z)

        vo.x += m(12)
        vo.y += m(13)
        vo.z += m(14)
        Return vo
    End Function
    Public Function translate_only(ByVal v As vect3, ByVal m() As Single) As vect3
        Dim vo As vect3
        vo.x += m(12)
        vo.y += m(13)
        vo.z += m(14)
        Return vo
    End Function
    Private Function transform(ByRef m() As Single, ByVal v As vertex_data, ByRef scale As Single, ByRef k As Integer) As vertex_data
        Dim vo As vertex_data
        v.x *= scale
        v.y *= scale
        vo.x = (m(0) * v.x) + (m(4) * v.y) + (m(8) * v.z)
        vo.y = (m(1) * v.x) + (m(5) * v.y) + (m(9) * v.z)
        vo.z = (m(2) * v.x) + (m(6) * v.y) + (m(10) * v.z)

        vo.u = v.u
        vo.v = v.v * -1.0

        vo.x += m(12)
        vo.y += m(13)
        vo.z += m(14)

        Return vo
    End Function

    Public Sub get_translated_bb_model(ByRef mm As model_matrix_list_)
        'creates a transformed bounding box for screen clipping.
        Dim v1, v2, v3, v4, v5, v6, v7, v8 As vect3
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

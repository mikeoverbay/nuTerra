
Imports System.Math
Imports OpenTK
Module modFrustum
    Public frustum(6, 4) As Single

    Public CULLED_COUNT As Integer


    Public Sub check_terrain_visible()
        'If Not m_terrain_ And Not terrain_loaded Then Return
        'For i = 0 To test_count
        '    maplist(i).visible = CubeInFrustum(maplist(i).BB)
        'Next
    End Sub

    Public Sub check_road_decals_visible()
        'If Not m_decals_ And Not decals_loaded Then Return
        'For k = 0 To road_decals.Length - 1
        '    For i = 0 To road_decals(k).road_decal_list.Length - 1
        '        If road_decals(k).road_decal_list(i).good Then
        '            Dim l1 = Abs(eyeX - road_decals(k).road_decal_list(i).matrix(12))
        '            Dim l2 = Abs(eyeZ - road_decals(k).road_decal_list(i).matrix(14))
        '            Dim d = l1 ^ 2 + l2 ^ 2
        '            If d > 160000 Then
        '                road_decals(k).road_decal_list(i).visible = False
        '            Else
        '                road_decals(k).road_decal_list(i).visible = CubeInFrustum(road_decals(k).road_decal_list(i).BB)
        '            End If
        '        End If
        '    Next
        'Next
    End Sub

    Public Sub check_decals_visible()
        'If Not m_decals_ And Not decals_loaded Then Return
        'For i = 0 To DECAL_INDEX_LIST.Length - 1
        '    If DECAL_INDEX_LIST(i).good Then
        '        'DECAL_INDEX_LIST(i).visible = CubeInFrustum(DECAL_INDEX_LIST(i).BB)
        '        Dim l1 = Abs(eyeX - DECAL_INDEX_LIST(i).matrix(12))
        '        Dim l2 = Abs(eyeZ - DECAL_INDEX_LIST(i).matrix(14))
        '        Dim d = l1 ^ 2 + l2 ^ 2
        '        If d > 160000 Then
        '            DECAL_INDEX_LIST(i).visible = False
        '        Else
        '            DECAL_INDEX_LIST(i).visible = CubeInFrustum(DECAL_INDEX_LIST(i).BB)
        '        End If

        '    End If
        'Next
    End Sub
    Public Sub check_models_visible()
        If Not MODELS_BLOCK_LOADING And Not MODELS_LOADED Then Return
        For m As UInt32 = 0 To MODEL_INDEX_LIST.Length - 2
            Dim idx = MODEL_INDEX_LIST(m).model_index
            If Not MAP_MODELS(idx).mdl(0).junk Then
                MODEL_INDEX_LIST(m).Culled = CubeInFrustum(MODEL_INDEX_LIST(m).BB)
            End If

        Next
    End Sub
    Public Sub check_trees_visible()
        'If Not m_trees_ And Not trees_loaded Then Return
        'For t = 0 To treeCache.Length - 2
        '    For tree As UInt32 = 0 To treeCache(t).tree_cnt - 1
        '        treeCache(t).BB(tree).visible = CubeInFrustum(treeCache(t).BB(tree).BB)
        '    Next
        'Next
    End Sub

    Public Sub ExtractFrustum()
        culled_count = 0
        Dim proj(16) As Single
        Dim modl(16) As Single
        Dim t As Single


        Dim clip As New Matrix4
        ' Combine the two matrices (multiply projection by modelview) 
        clip = MODELVIEWMATRIX * PROJECTIONMATRIX


        ' Extract the numbers for the RIGHT plane 
        frustum(0, 0) = clip.M14 - clip.M11
        frustum(0, 1) = clip.M24 - clip.M21
        frustum(0, 2) = clip.M34 - clip.M31
        frustum(0, 3) = clip.M44 - clip.M41

        ' Normalize the result 
        t = Sqrt(frustum(0, 0) * frustum(0, 0) + frustum(0, 1) * frustum(0, 1) + frustum(0, 2) * frustum(0, 2))
        frustum(0, 0) /= t
        frustum(0, 1) /= t
        frustum(0, 2) /= t
        frustum(0, 3) /= t

        ' Extract the numbers for the LEFT plane 
        frustum(1, 0) = clip.M14 + clip.M11
        frustum(1, 1) = clip.M24 + clip.M21
        frustum(1, 2) = clip.M34 + clip.M31
        frustum(1, 3) = clip.M44 + clip.M41

        ' Normalize the result 
        t = Sqrt(frustum(1, 0) * frustum(1, 0) + frustum(1, 1) * frustum(1, 1) + frustum(1, 2) * frustum(1, 2))
        frustum(1, 0) /= t
        frustum(1, 1) /= t
        frustum(1, 2) /= t
        frustum(1, 3) /= t

        ' Extract the BOTTOM plane 
        frustum(2, 0) = clip.M14 + clip.M12
        frustum(2, 1) = clip.M24 + clip.M22
        frustum(2, 2) = clip.M34 + clip.M32
        frustum(2, 3) = clip.M44 + clip.M42

        ' Normalize the result 
        t = Sqrt(frustum(2, 0) * frustum(2, 0) + frustum(2, 1) * frustum(2, 1) + frustum(2, 2) * frustum(2, 2))
        frustum(2, 0) /= t
        frustum(2, 1) /= t
        frustum(2, 2) /= t
        frustum(2, 3) /= t

        ' Extract the TOP plane 
        frustum(3, 0) = clip.M14 - clip.M12
        frustum(3, 1) = clip.M24 - clip.M22
        frustum(3, 2) = clip.M34 - clip.M32
        frustum(3, 3) = clip.M44 - clip.M42

        ' Normalize the result 
        t = Sqrt(frustum(3, 0) * frustum(3, 0) + frustum(3, 1) * frustum(3, 1) + frustum(3, 2) * frustum(3, 2))
        frustum(3, 0) /= t
        frustum(3, 1) /= t
        frustum(3, 2) /= t
        frustum(3, 3) /= t

        ' Extract the FAR plane 
        frustum(4, 0) = clip.M14 - clip.M13
        frustum(4, 1) = clip.M24 - clip.M23
        frustum(4, 2) = clip.M34 - clip.M33
        frustum(4, 3) = clip.M44 - clip.M43

        ' Normalize the result 
        t = Sqrt(frustum(4, 0) * frustum(4, 0) + frustum(4, 1) * frustum(4, 1) + frustum(4, 2) * frustum(4, 2))
        frustum(4, 0) /= t
        frustum(4, 1) /= t
        frustum(4, 2) /= t
        frustum(4, 3) /= t

        ' Extract the NEAR plane 
        frustum(5, 0) = clip.M14 + clip.M13
        frustum(5, 1) = clip.M24 + clip.M23
        frustum(5, 2) = clip.M34 + clip.M33
        frustum(5, 3) = clip.M44 + clip.M43

        ' Normalize the result 
        t = Sqrt(frustum(5, 0) * frustum(5, 0) + frustum(5, 1) * frustum(5, 1) + frustum(5, 2) * frustum(5, 2))
        frustum(5, 0) /= t
        frustum(5, 1) /= t
        frustum(5, 2) /= t
        frustum(5, 3) /= t
        'tb1.text = ""
        'Return
#If 1 Then

        Dim str As String = ""
        For i = 0 To 5
            str += frustum(i, 0).ToString("000.0000") + "   " + frustum(i, 1).ToString("000.0000") + "   " + frustum(i, 2).ToString("000.0000") + "   " + frustum(i, 3).ToString("000.0000") + vbCrLf
        Next
#End If


    End Sub

    Public Function CubeInFrustum(ByRef bb() As Vector3) As Boolean
        If bb Is Nothing Then
            Return False
        End If
        For p = 0 To 5
            If (frustum(p, 0) * (bb(0).x) + frustum(p, 1) * (bb(0).y) + frustum(p, 2) * (bb(0).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(1).x) + frustum(p, 1) * (bb(1).y) + frustum(p, 2) * (bb(1).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(2).x) + frustum(p, 1) * (bb(2).y) + frustum(p, 2) * (bb(2).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(3).x) + frustum(p, 1) * (bb(3).y) + frustum(p, 2) * (bb(3).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(4).x) + frustum(p, 1) * (bb(4).y) + frustum(p, 2) * (bb(4).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(5).x) + frustum(p, 1) * (bb(5).y) + frustum(p, 2) * (bb(5).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(6).x) + frustum(p, 1) * (bb(6).y) + frustum(p, 2) * (bb(6).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(7).x) + frustum(p, 1) * (bb(7).y) + frustum(p, 2) * (bb(7).z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            culled_count += 1
            Return True
        Next
        Return False
    End Function

End Module

Imports System.Math
Imports OpenTK

Module modFrustum
    Public frustum(6, 4) As Single

    Public CULLED_COUNT As Integer

    Public Sub cull_terrain()
        If DONT_BLOCK_TERRAIN And Not TERRAIN_LOADED Then Return
        For i = 0 To theMap.v_data.Length - 1
            theMap.render_set(i).visible = Not CubeInFrustum(theMap.v_data(i).BB)
        Next
    End Sub

    Public Sub ExtractFrustum()
        CULLED_COUNT = 0
        Dim proj(16) As Single
        Dim modl(16) As Single
        Dim t As Single


        Dim clip As New Matrix4
        ' Combine the two matrices (multiply projection by modelview) 
        clip = Matrix4.Mult(PerFrameData.view, PerFrameData.projection)


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
        Dim str As String = ""
        For i = 0 To 5
            str += frustum(i, 0).ToString("000.0000") + "   " + frustum(i, 1).ToString("000.0000") + "   " + frustum(i, 2).ToString("000.0000") + "   " + frustum(i, 3).ToString("000.0000") + vbCrLf
        Next


    End Sub

    Public Function CubeInFrustum(ByRef bb() As Vector3) As Boolean
        If bb Is Nothing Then
            Return False
        End If
        For p = 0 To 5
            If (frustum(p, 0) * (bb(0).X) + frustum(p, 1) * (bb(0).Y) + frustum(p, 2) * (bb(0).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(1).X) + frustum(p, 1) * (bb(1).Y) + frustum(p, 2) * (bb(1).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(2).X) + frustum(p, 1) * (bb(2).Y) + frustum(p, 2) * (bb(2).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(3).X) + frustum(p, 1) * (bb(3).Y) + frustum(p, 2) * (bb(3).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(4).X) + frustum(p, 1) * (bb(4).Y) + frustum(p, 2) * (bb(4).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(5).X) + frustum(p, 1) * (bb(5).Y) + frustum(p, 2) * (bb(5).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(6).X) + frustum(p, 1) * (bb(6).Y) + frustum(p, 2) * (bb(6).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            If (frustum(p, 0) * (bb(7).X) + frustum(p, 1) * (bb(7).Y) + frustum(p, 2) * (bb(7).Z) + frustum(p, 3) > 0) Then
                Continue For
            End If
            CULLED_COUNT += 1
            Return True
        Next
        Return False
    End Function

End Module
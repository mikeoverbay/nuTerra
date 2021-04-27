Imports System.Math
Imports OpenTK

Module modFrustum
    Public frustum(6) As Vector4

    Public Sub cull_terrain()
        For i = 0 To theMap.v_data.Length - 1
            theMap.render_set(i).visible = Not CubeInFrustum(theMap.v_data(i).BB)

            '=======================================================================================
            'First, find out what chunks are to be drawn as LQ global_AM texturing only.
            '=======================================================================================
            If theMap.render_set(i).visible Then
                Dim l1 = Abs(theMap.chunks(i).location.X - CAM_POSITION.X) 'x
                Dim l2 = Abs(theMap.v_data(i).avg_heights - CAM_POSITION.Y) 'y
                Dim l3 = Abs(theMap.chunks(i).location.Y - CAM_POSITION.Z) 'z
                Dim v As New Vector3(l1, l2, l3)
                Dim l = v.Length
                If l > 100.0F Then 'This value is the distance at which the chunk drawing is swapped.
                    theMap.render_set(i).quality = TerrainQuality.LQ
                Else
                    theMap.render_set(i).quality = If(USE_TESSELLATION, TerrainQuality.HQ, TerrainQuality.LQ)
                End If
            End If
        Next
    End Sub

    Public Sub ExtractFrustum()
        ' Combine the two matrices (multiply projection by modelview)
        Dim clip = PerViewData.viewProj

        ' Extract the numbers for the RIGHT plane
        frustum(0).X = clip.M14 - clip.M11
        frustum(0).Y = clip.M24 - clip.M21
        frustum(0).Z = clip.M34 - clip.M31
        frustum(0).W = clip.M44 - clip.M41

        ' Normalize the result
        frustum(0).Normalize()

        ' Extract the numbers for the LEFT plane
        frustum(1).X = clip.M14 + clip.M11
        frustum(1).Y = clip.M24 + clip.M21
        frustum(1).Z = clip.M34 + clip.M31
        frustum(1).W = clip.M44 + clip.M41

        ' Normalize the result
        frustum(1).Normalize()

        ' Extract the BOTTOM plane
        frustum(2).X = clip.M14 + clip.M12
        frustum(2).Y = clip.M24 + clip.M22
        frustum(2).Z = clip.M34 + clip.M32
        frustum(2).W = clip.M44 + clip.M42

        ' Normalize the result
        frustum(2).Normalize()

        ' Extract the TOP plane
        frustum(3).X = clip.M14 - clip.M12
        frustum(3).Y = clip.M24 - clip.M22
        frustum(3).Z = clip.M34 - clip.M32
        frustum(3).W = clip.M44 - clip.M42

        ' Normalize the result
        frustum(3).Normalize()

        ' Extract the FAR plane
        frustum(4).X = clip.M14 - clip.M13
        frustum(4).Y = clip.M24 - clip.M23
        frustum(4).Z = clip.M34 - clip.M33
        frustum(4).W = clip.M44 - clip.M43

        ' Normalize the result
        frustum(4).Normalize()

        ' Extract the NEAR plane
        frustum(5).X = clip.M14 + clip.M13
        frustum(5).Y = clip.M24 + clip.M23
        frustum(5).Z = clip.M34 + clip.M33
        frustum(5).W = clip.M44 + clip.M43

        ' Normalize the result
        frustum(5).Normalize()
    End Sub

    Public Function CubeInFrustum(bb() As Vector3) As Boolean
        For p = 0 To 5
            If (frustum(p).X * (bb(0).X) + frustum(p).Y * (bb(0).Y) + frustum(p).Z * (bb(0).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(1).X) + frustum(p).Y * (bb(1).Y) + frustum(p).Z * (bb(1).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(2).X) + frustum(p).Y * (bb(2).Y) + frustum(p).Z * (bb(2).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(3).X) + frustum(p).Y * (bb(3).Y) + frustum(p).Z * (bb(3).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(4).X) + frustum(p).Y * (bb(4).Y) + frustum(p).Z * (bb(4).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(5).X) + frustum(p).Y * (bb(5).Y) + frustum(p).Z * (bb(5).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(6).X) + frustum(p).Y * (bb(6).Y) + frustum(p).Z * (bb(6).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            If (frustum(p).X * (bb(7).X) + frustum(p).Y * (bb(7).Y) + frustum(p).Z * (bb(7).Z) + frustum(p).W > 0) Then
                Continue For
            End If
            Return True
        Next
        Return False
    End Function

End Module
Imports System.Math
Imports OpenTK.Mathematics

Module modFrustum
    Public frustum(5) As Vector4

    Public Sub cull_terrain()
        For i = 0 To theMap.v_data.Length - 1
            theMap.render_set(i).visible = Not CubeInFrustum(theMap.v_data(i).BB)

            '=======================================================================================
            'First, find out what chunks are to be drawn as LQ global_AM texturing only.
            '=======================================================================================
            If theMap.render_set(i).visible Then
                Dim l1 = Abs(theMap.chunks(i).location.X - map_scene.camera.CAM_POSITION.X) 'x
                Dim l2 = Abs(theMap.v_data(i).avg_heights - map_scene.camera.CAM_POSITION.Y) 'y
                Dim l3 = Abs(theMap.chunks(i).location.Y - map_scene.camera.CAM_POSITION.Z) 'z
                Dim v As New Vector3(l1, l2, l3)
                Dim l = v.Length
                If l > 300.0F Then 'This value is the distance at which the chunk drawing is swapped.
                    theMap.render_set(i).quality = TerrainQuality.LQ
                Else
                    theMap.render_set(i).quality = If(USE_TESSELLATION, TerrainQuality.HQ, TerrainQuality.LQ)
                End If
            End If
        Next
    End Sub

    Public Sub ExtractFrustum()
        ' Combine the two matrices (multiply projection by modelview)
        Dim clip = map_scene.camera.PerViewData.viewProj

        ' Extract the numbers for the RIGHT plane
        frustum(0) = clip.Column3 - clip.Column0

        ' Normalize the result
        frustum(0).Normalize()

        ' Extract the numbers for the LEFT plane
        frustum(1) = clip.Column3 + clip.Column0

        ' Normalize the result
        frustum(1).Normalize()

        ' Extract the BOTTOM plane
        frustum(2) = clip.Column3 + clip.Column1

        ' Normalize the result
        frustum(2).Normalize()

        ' Extract the TOP plane
        frustum(3) = clip.Column3 - clip.Column1

        ' Normalize the result
        frustum(3).Normalize()

        ' Extract the FAR plane
        frustum(4) = clip.Column3 - clip.Column2

        ' Normalize the result
        frustum(4).Normalize()

        ' Extract the NEAR plane
        frustum(5) = clip.Column3 + clip.Column2

        ' Normalize the result
        frustum(5).Normalize()
    End Sub

    Public Function CubeInFrustum(bb() As Vector3) As Boolean
        For p = 0 To 5
            If Vector3.Dot(frustum(p).Xyz, bb(0)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(1)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(2)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(3)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(4)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(5)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(6)) + frustum(p).W > 0 Then
                Continue For
            End If
            If Vector3.Dot(frustum(p).Xyz, bb(7)) + frustum(p).W > 0 Then
                Continue For
            End If
            Return True
        Next
        Return False
    End Function

End Module
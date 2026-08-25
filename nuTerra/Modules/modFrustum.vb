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
                ' Distance to the chunk's BOX, not to its origin corner. The
                ' corner of a 100 m chunk can be ~141 m from ground that chunk
                ' owns, so measuring the corner made the HQ set depend on where
                ' each chunk's origin happened to sit relative to the camera -
                ' the chunk underfoot could fail its own test while a diagonal
                ' neighbour passed, and the pattern shifted as the camera moved
                ' and turned. The 300 m threshold hid the error inside its
                ' slack; 60 m exposed it. Nearest-point distance is zero for
                ' the chunk you stand on, always.
                Dim cam = map_scene.camera.CAM_POSITION
                Dim l1 = Max(0.0F, Max(theMap.v_data(i).BB_Min.X - cam.X, cam.X - theMap.v_data(i).BB_Max.X))
                Dim l2 = Max(0.0F, Max(theMap.v_data(i).BB_Min.Y - cam.Y, cam.Y - theMap.v_data(i).BB_Max.Y))
                Dim l3 = Max(0.0F, Max(theMap.v_data(i).BB_Min.Z - cam.Z, cam.Z - theMap.v_data(i).BB_Max.Z))
                Dim l = New Vector3(l1, l2, l3).Length
                ' 60 m, the game's own tessellation envelope (g_tessDistanceRcp
                ' = 1/60). The tese fades displacement to zero by 60, so a
                ' chunk crossing this line has nothing left to pop.
                If l > 60.0F Then
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

    '''<summary>
    ''' Axis aligned box against the frustum, the same test the models are culled
    ''' with in cull.comp: for each plane take the box corner furthest along the
    ''' plane normal, and if even that one is outside then the whole box is.
    '''
    ''' Note the sense is the opposite of CubeInFrustum, which returns True for a
    ''' box that is out.
    '''</summary>
    Public Function BoxInFrustum(bmin As Vector3, bmax As Vector3) As Boolean
        For p = 0 To 5
            Dim n = frustum(p).Xyz
            Dim v As Vector3
            v.X = If(n.X > 0.0F, bmax.X, bmin.X)
            v.Y = If(n.Y > 0.0F, bmax.Y, bmin.Y)
            v.Z = If(n.Z > 0.0F, bmax.Z, bmin.Z)
            If Vector3.Dot(n, v) + frustum(p).W < 0.0F Then
                Return False
            End If
        Next
        Return True
    End Function

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
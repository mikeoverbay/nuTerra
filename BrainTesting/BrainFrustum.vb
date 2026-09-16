Imports OpenTK.Mathematics

''' <summary>
''' View-space clipping: six planes out of a view-projection, and a box test.
'''
''' "just view space clipping of opject" - the owner. This is that, on the
''' CPU. nuTerra has a GPU version in cull.comp, but reaching it means the
''' shader system and the indirect batching in MapStaticModels; this app draws
''' thousands of instances, not hundreds of thousands, and a plane test per
''' instance is cheaper than the machinery to avoid it.
'''
''' Added 2026-09-16 by nuTerra work.
''' </summary>
Module BrainFrustum

    ''' <summary>
    ''' The six clip planes, as Vector4(a, b, c, d) with the normal pointing
    ''' INWARD, so a point is inside when a*x + b*y + c*z + d >= 0 on all six.
    '''
    ''' Gribb-Hartmann: the rows of the view-projection added to and subtracted
    ''' from its fourth row give the planes directly, with no inverse and no
    ''' corner reconstruction. OpenTK stores matrices row-major and multiplies
    ''' row-vector-first, so the "rows" here are the COLUMNS of the textbook
    ''' form - getting that backwards yields a frustum that culls exactly what
    ''' it should keep, which looks like geometry failing to load.
    ''' </summary>
    Public Function FromViewProj(ByRef m As Matrix4) As Vector4()
        Dim p(5) As Vector4
        ' left, right, bottom, top, near, far
        p(0) = New Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)
        p(1) = New Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)
        p(2) = New Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)
        p(3) = New Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)
        p(4) = New Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43)
        p(5) = New Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)

        For i = 0 To 5
            ' Normalised so the plane equation is a real distance. Not needed
            ' for a pure in/out test, but it is needed the moment anyone wants
            ' a margin, and an unnormalised plane makes that margin meaningless
            ' in a way nothing reports.
            Dim n As New Vector3(p(i).X, p(i).Y, p(i).Z)
            Dim len = n.Length
            If len > 0.0F Then p(i) /= len
        Next
        Return p
    End Function

    ''' <summary>
    ''' Is any part of this local-space box, placed by this matrix, inside?
    '''
    ''' The box is transformed into world space as its eight CORNERS rather
    ''' than by transforming the min and max: a rotated box's min/max are not
    ''' the transform of its min/max, and using them would cull buildings that
    ''' are plainly on screen whenever the placement carries a rotation - which
    ''' on a WoT map is most of them.
    '''
    ''' Conservative: a box is rejected only when it is wholly outside one
    ''' plane. A box straddling two planes' outsides but inside neither alone
    ''' survives, which is the standard false positive and costs one draw.
    ''' </summary>
    Public Function BoxVisible(planes As Vector4(), ByRef model As Matrix4,
                               lo As Vector3, hi As Vector3) As Boolean
        Dim c(7) As Vector3
        c(0) = transform(model, New Vector3(lo.X, lo.Y, lo.Z))
        c(1) = transform(model, New Vector3(hi.X, lo.Y, lo.Z))
        c(2) = transform(model, New Vector3(lo.X, hi.Y, lo.Z))
        c(3) = transform(model, New Vector3(hi.X, hi.Y, lo.Z))
        c(4) = transform(model, New Vector3(lo.X, lo.Y, hi.Z))
        c(5) = transform(model, New Vector3(hi.X, lo.Y, hi.Z))
        c(6) = transform(model, New Vector3(lo.X, hi.Y, hi.Z))
        c(7) = transform(model, New Vector3(hi.X, hi.Y, hi.Z))

        For i = 0 To 5
            Dim pl = planes(i)
            Dim out = 0
            For j = 0 To 7
                If pl.X * c(j).X + pl.Y * c(j).Y + pl.Z * c(j).Z + pl.W < 0.0F Then out += 1
            Next
            If out = 8 Then Return False
        Next
        Return True
    End Function

    Private Function transform(ByRef m As Matrix4, v As Vector3) As Vector3
        Dim r = New Vector4(v, 1.0F) * m
        Return New Vector3(r.X, r.Y, r.Z)
    End Function

End Module

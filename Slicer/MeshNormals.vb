Imports OpenTK.Mathematics

''' <summary>
''' Smooth vertex normals by Inigo Quilez's method.
'''
''' https://iquilezles.org/articles/normals/
'''
''' THE WHOLE TRICK IS THE NORMALIZE YOU DO NOT DO. The obvious way to average
''' face normals is to normalize each face normal and add it in. Do not: the
''' length of a cross product is proportional to the area of the triangle that
''' produced it, so accumulating the RAW cross product area-weights the average
''' for free. A big face influences its corner more than a sliver does, which is
''' what you want, and it costs one fewer square root per face than doing it
''' wrong.
'''
''' His listing, verbatim, for comparison:
'''
'''     const vec3 e1 = vert[ia].pos - vert[ib].pos;
'''     const vec3 e2 = vert[ic].pos - vert[ib].pos;
'''     const vec3 no = cross( e1, e2 );
'''     vert[ia].normal += no;
'''     vert[ib].normal += no;
'''     vert[ic].normal += no;
'''
''' ONE DELIBERATE DEPARTURE: the sign. His edges run from the middle vertex
''' (a-b and c-b), which for a counter-clockwise triangle yields the INWARD
''' normal - work it through with A(0,0,0) B(1,0,0) C(0,1,0) and his cross comes
''' out (0,0,-1) where the outward normal is (0,0,+1). This reader has already
''' flipped winding to counter-clockwise when it negated X, so the edges here
''' run from the FIRST vertex instead, giving outward normals. The area
''' weighting - the actual contribution of the article - is untouched.
'''
''' NOTE FOR nuTerra: `ChunkFunctions.make_normals_indi32` credits this article
''' and then calls `no.Normalize()` before accumulating, which throws the area
''' weighting away. It is inert there - a 65x65 terrain grid has equal-area
''' triangles, so weighting by area weights everything the same - but it must
''' not be copied to a mesh whose triangles vary in size, which is every mesh
''' in this app.
''' </summary>
Public NotInheritable Class MeshNormals

    ''' <summary>
    ''' One normal per vertex. Vertices that no triangle references, or whose
    ''' accumulated normal cancels to nothing, come back as +Y rather than as a
    ''' zero vector - a zero normal normalizes to NaN and turns every pixel that
    ''' touches it black.
    ''' </summary>
    Public Shared Function Compute(pos As Vector3(), idx As Integer()) As Vector3()
        Dim n(Math.Max(pos.Length - 1, 0)) As Vector3
        If pos Is Nothing OrElse idx Is Nothing Then Return n

        Dim t = 0
        While t + 2 < idx.Length
            Dim ia = idx(t), ib = idx(t + 1), ic = idx(t + 2)
            If ia >= 0 AndAlso ib >= 0 AndAlso ic >= 0 AndAlso
               ia < pos.Length AndAlso ib < pos.Length AndAlso ic < pos.Length Then
                ' NOT normalized - see above. This is the whole method.
                Dim e1 = pos(ib) - pos(ia)
                Dim e2 = pos(ic) - pos(ia)
                Dim no = Vector3.Cross(e1, e2)
                n(ia) += no
                n(ib) += no
                n(ic) += no
            End If
            t += 3
        End While

        For i = 0 To n.Length - 1
            If n(i).LengthSquared > 0.000000001F Then
                n(i).Normalize()
            Else
                n(i) = Vector3.UnitY
            End If
        Next
        Return n
    End Function
End Class

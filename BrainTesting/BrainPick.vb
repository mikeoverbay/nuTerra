Imports OpenTK.Mathematics

''' <summary>
''' Click a thing, find out what it is.
'''
''' The owner, 2026-09-16: "i have no picker to get model names". He was
''' chasing whether the map draws crushed models, and could not name the thing
''' he was looking at - so the question could not even be asked properly.
'''
''' NO ID BUFFER, NO EXTRA PASS. nuTerra picks by rendering object ids to an
''' integer attachment and reading the texel back. That is the right design
''' there, where the id layer is wanted for the flight bake anyway. Here it
''' would be a second draw of every model every frame, paid on every frame to
''' serve the rare one where a button is clicked - and this app exists to come
''' up fast. A ray against the boxes costs nothing until the click happens.
'''
''' ORIENTED BOXES, NOT THE AABB. The ray goes into MODEL space through the
''' inverse of the placement matrix and meets the asset's own bbMin/bbMax
''' there. Testing the world AABB instead would pick a rotated fence from well
''' off its end - the same over-claim that cost the nav grid 6,000 false
''' blockers before it rasterised oriented quads.
'''
''' IT ALSO SAYS WHAT THE PARTS ARE. The identifiers - d_ destructible, n_
''' non-destructible, s_ static structure - hang off the primitive group's
''' ORIGINAL space.bin material id, which the loader used to remap away
''' before anything could read it. PrimitiveGroup keeps the original
''' alongside now, so a click can answer it.
'''
''' The counts are PER PART and do not reduce to one verdict: a shed is 14
''' d_ against 24 n_, planks destructible and frame not.
'''
''' Added 2026-09-16 by nuTerra work, on the owner's ask.
''' </summary>
Module BrainPick

    Public Structure Hit
        Public ok As Boolean
        ''' <summary>"model", "tank" or "tree" - what kind of thing was hit,
        ''' not what kind of terrain it keys as.</summary>
        Public what As String
        Public name As String
        Public kind As Byte
        ''' <summary>Metres from the eye along the ray.</summary>
        Public dist As Single
        Public point As Vector3
        ''' <summary>Extent of the thing hit, metres, for the report.</summary>
        Public size As Vector3
        ''' <summary>Parts by identifier prefix - see BrainModels.Mesh.</summary>
        Public pD, pN, pS, pOther As Integer
    End Structure

    ''' <summary>
    ''' Pick at a pixel.
    '''
    ''' THE RAY COMES FROM THE INVERSE OF viewProj, not from the camera's
    ''' angles. Rebuilding a look direction from yaw and pitch is a second
    ''' copy of the camera, and a picker that disagrees with the picture by a
    ''' degree points at the wrong building at 400 m.
    ''' </summary>
    Public Function At(ByRef viewProj As Matrix4, mx As Single, my As Single,
                       w As Integer, h As Integer) As Hit
        Dim res As New Hit With {.dist = Single.MaxValue}
        If w <= 0 OrElse h <= 0 Then Return res

        Dim inv As Matrix4
        Try
            inv = Matrix4.Invert(viewProj)
        Catch
            ' A singular viewProj means the camera is not set up yet. Saying so
            ' beats returning a ray that points anywhere.
            LogThis("brain: pick - the view matrix will not invert")
            Return res
        End Try

        ' Pixel to NDC. Y flips: the window counts down from the top, clip
        ' space counts up from the bottom.
        Dim ndx = 2.0F * mx / w - 1.0F
        Dim ndy = 1.0F - 2.0F * my / h

        Dim near_ = unproject(inv, ndx, ndy, -1.0F)
        Dim far_ = unproject(inv, ndx, ndy, 1.0F)
        Dim dir = far_ - near_
        If dir.LengthSquared <= 0.0F Then Return res
        dir.Normalize()

        BrainModels.PickInto(near_, dir, res)
        pick_tanks(near_, dir, res)

        res.ok = res.dist < Single.MaxValue
        If res.ok Then res.point = near_ + dir * res.dist
        Return res
    End Function

    Private Function unproject(ByRef inv As Matrix4, x As Single, y As Single, z As Single) As Vector3
        ' Row-vector convention, the same one the shaders are fed - see the
        ' note in BrainRender about what GLSL sees.
        Dim p = New Vector4(x, y, z, 1.0F) * inv
        If Math.Abs(p.W) < 0.0000001F Then Return New Vector3(p.X, p.Y, p.Z)
        Return New Vector3(p.X / p.W, p.Y / p.W, p.Z / p.W)
    End Function

    ''' <summary>
    ''' Ray against an oriented box, in the box's own space.
    '''
    ''' Returns the NEAR hit, or -1. A ray starting inside the box returns the
    ''' near value as negative and is reported as a hit at zero, because
    ''' standing inside a building is still standing in that building.
    ''' </summary>
    Public Function RayBox(origin As Vector3, dir As Vector3, ByRef m As Matrix4,
                           lo As Vector3, hi As Vector3) As Single
        Dim inv As Matrix4
        Try
            inv = Matrix4.Invert(m)
        Catch
            ' A placement scaled to nothing on an axis. Not pickable, not fatal.
            Return -1.0F
        End Try

        Dim o = (New Vector4(origin, 1.0F) * inv).Xyz
        ' A DIRECTION IS NOT A POINT: w = 0, so the translation does not apply.
        ' Using 1.0 here moves the ray's direction by the placement's position
        ' and every pick lands somewhere absurd.
        Dim d = (New Vector4(dir, 0.0F) * inv).Xyz

        Dim tmin As Single = -Single.MaxValue
        Dim tmax As Single = Single.MaxValue
        For a = 0 To 2
            Dim od = d(a), oo = o(a), l = lo(a), hh = hi(a)
            If Math.Abs(od) < 0.0000001F Then
                ' Parallel to this slab: a miss only if it starts outside it.
                If oo < l OrElse oo > hh Then Return -1.0F
            Else
                Dim t1 = (l - oo) / od
                Dim t2 = (hh - oo) / od
                If t1 > t2 Then Dim tt = t1 : t1 = t2 : t2 = tt
                tmin = Math.Max(tmin, t1)
                tmax = Math.Min(tmax, t2)
                If tmin > tmax Then Return -1.0F
            End If
        Next
        If tmax < 0.0F Then Return -1.0F
        Return Math.Max(0.0F, tmin)
    End Function

    Private Sub pick_tanks(origin As Vector3, dir As Vector3, ByRef res As Hit)
        If BrainTanks.Bodies Is Nothing Then Return
        For Each b In BrainTanks.Bodies
            ' The hull box, placed where the hull is. Heading only - a hull
            ' does not roll in this sim.
            Dim m = Matrix4.CreateRotationY(b.headingRad)
            m.Row3 = New Vector4(b.spawn.X, b.y, b.spawn.Y, 1.0F)
            Dim t = RayBox(origin, dir, m, -b.half, b.half)
            If t < 0.0F OrElse t >= res.dist Then Continue For
            res.dist = t
            res.what = "tank"
            res.name = String.Format("{0}  (id {1}, team {2})", b.tag, b.id, b.team)
            res.kind = 0
            res.size = b.half * 2.0F
        Next
    End Sub

    ''' <summary>Say what was hit, in one line.</summary>
    Public Sub Report(h As Hit)
        If Not h.ok Then
            LogThis("brain: pick - nothing there")
            Return
        End If
        LogThis("brain: pick {0} [{1}] {2}  at ({3:0.0}, {4:0.0}, {5:0.0})  {6:0.0} m away  " &
                "size {7:0.0} x {8:0.0} x {9:0.0}",
                h.what, If(h.what = "tank", "hull", ModelKind.KIND_NAMES(h.kind And 7)),
                h.name, h.point.X, h.point.Y, h.point.Z, h.dist,
                h.size.X, h.size.Y, h.size.Z)
        If h.what = "model" AndAlso (h.pD + h.pN + h.pS + h.pOther) > 0 Then
            LogThis("brain:      parts - {0} destructible (d_), {1} not (n_), " &
                    "{2} static (s_), {3} unnamed",
                    h.pD, h.pN, h.pS, h.pOther)
        End If
    End Sub

End Module

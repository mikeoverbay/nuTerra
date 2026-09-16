Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Drawing the hulls. Flat, in team colour, standing where they spawned.
'''
''' NOT MapTanks.Draw - that one binds materials, lights, armour and recoil,
''' and none of those exist here. This is the geometry half, which is all a
''' brain needs to look at.
'''
''' Added 2026-09-16 by nuTerra work, stage 4 of docs/brain_testing_plan.md.
''' </summary>
Module BrainTankDraw

    Private shader As BrainShader

    ''' <summary>Team colours. Deliberately NOT the kind palette: a tank is not
    ''' a map object, and using ModelKind's colours would say it was. Green and
    ''' red are the game's own sides and the names TankTeam already uses.</summary>
    Private ReadOnly TEAM_1_RGB As New Vector3(0.35F, 0.75F, 0.35F)
    Private ReadOnly TEAM_2_RGB As New Vector3(0.85F, 0.30F, 0.25F)

    Public Sub Init()
        shader = New BrainShader("model")
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)

        For Each b In BrainTanks.Bodies
            If b.vehicle Is Nothing OrElse b.vehicle.parts Is Nothing Then Continue For

            shader.SetVec3("kindColour", If(b.team = 1, TEAM_1_RGB, TEAM_2_RGB))

            ' THE SAME COMPOSITION MapTanks USES, and it has to be: a hull posed
            ' by a second recipe drifts from the one the rest of the project
            ' draws the moment either changes. Scale for the X mirror, then
            ' heading, then place.
            Dim world =
                Matrix4.CreateScale(If(TankRoster.MirrorX, -1.0F, 1.0F), 1.0F, 1.0F) *
                Matrix4.CreateRotationY(b.headingRad) *
                Matrix4.CreateTranslation(b.spawn.X, b.y, b.spawn.Y)

            For Each part In b.vehicle.parts
                If part.meshes Is Nothing Then Continue For
                Dim partModel = Matrix4.CreateTranslation(part.offset) * world

                For Each m In part.meshes
                    If m.vao Is Nothing Then Continue For

                    ' VERBATIM FROM MapTanks.DrawDepth, and both halves are
                    ' load bearing. The Z flip is why skinned parts face the
                    ' right way. The base-vertex-zero rule is the bug that
                    ' silently dropped group 1 of all 31 multi-group meshes on
                    ' the roster: BigWorld group indices ALREADY include
                    ' startVertex, so passing it again asks GL for vertices
                    ' past the end of the buffer and rasterises nothing.
                    Dim model = partModel
                    If TankRoster.FlipSkinnedZ AndAlso m.layout.offBoneIdx >= 0 Then
                        model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * partModel
                    End If
                    shader.SetMat4("model", model)

                    m.vao.Bind()
                    Dim itype = If(m.index32, DrawElementsType.UnsignedInt,
                                   DrawElementsType.UnsignedShort)
                    Dim isz = If(m.index32, 4, 2)
                    For gi = 0 To m.groups.Count - 1
                        Dim g = m.groups(gi)
                        GL.DrawElements(PrimitiveType.Triangles, g.nPrimitives * 3, itype,
                                        New IntPtr(g.startIndex * isz))
                    Next
                Next
            Next
        Next
        GL.BindVertexArray(0)
    End Sub

End Module

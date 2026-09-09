Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' The tank module's face to the rest of nuTerra. The core touches it in two
''' places only: MapScene owns one (construct / dispose) and draw_scene calls
''' Draw() once, after the static models. Everything else - the vehicle XML,
''' the primitives, the visuals, the textures, the shader - lives under
''' nuTerra\Tanks and shaders\Tanks.
'''
''' Loading is lazy, on the first Draw after the map is up, so no load-order
''' hook is needed in the map loader.
''' </summary>
Public Class MapTanks
    Implements IDisposable

    ''' <summary>Master switch. Off draws nothing and loads nothing.</summary>
    Public Shared Enabled As Boolean = True

    ''' <summary>
    ''' nuTerra's world is the game's mirrored in X for display. On, the tank
    ''' takes the same mirror so it faces the same way as the buildings around
    ''' it. Left settable because it is a verification step, not a known.
    ''' </summary>
    Public Shared MirrorX As Boolean = True

    ''' <summary>Which normal encoding the .vert unpacks: 1 = 8/8/8 unsigned bytes (what the VB exporter uses for BPVT streams), 0 = 11/10/10 signed.</summary>
    Public Shared NormalMode As Integer = 1

    ''' <summary>
    ''' Skinned parts - the gun and the chassis, the streams with bone bytes -
    ''' are stored with Z reversed relative to the hull and turret. Both
    ''' exporters carry this rule: the VB loader negates Z for chassis and gun
    ''' (ModTankLoader.vb, unpackNormal_8_8_8, xmlget_mode 1 and 4) and the
    ''' Python viewer flips Z for unskinned parts only. First seen here as the
    ''' gun drawn pointing out over the spade. Verified on the still that
    ''' followed; left as a flag so the A/B is one line.
    ''' </summary>
    Public Shared FlipSkinnedZ As Boolean = True

    Private ReadOnly scene As MapScene
    Private shader As Shader
    Private loaded As Boolean
    Private failed As Boolean
    Public vehicles As New List(Of TankVehicle)
    Public instances As New List(Of TankInstance)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>The test set: one A88_M53_55 at the centre of base 1, team green, facing +Z.</summary>
    Private Sub Load()
        loaded = True
        Try
            shader = New Shader("tank_gbuffer")
            Dim v = TankVehicle.Load("usa", "A88_M53_55")
            If v Is Nothing Then
                failed = True
                Return
            End If
            vehicles.Add(v)

            ' Base 1, the way the ring draws it: X negated, height from the terrain.
            Dim x = -TEAM_1.X
            Dim z = TEAM_1.Z
            Dim y = get_Y_at_XZ(x, z)
            instances.Add(New TankInstance With {
                .vehicle = v, .position = New Vector3(x, y, z), .headingRad = 0.0F,
                .team = TankTeam.Green, .label = "M53/M55"})
            LogThis("tank: placed {0} at base 1 ({1:0.00}, {2:0.00}, {3:0.00})", v.tag, x, y, z)
        Catch ex As Exception
            failed = True
            LogThis("tank: load failed - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Draw every placed tank into the G-buffer with the model shaders' own
    ''' convention, so the deferred pass lights, shadows and fogs it like any
    ''' model. Runs after draw_models, which leaves depth at Greater (reversed
    ''' Z) and the CNGPA attachments; we take CNGP for the draw and hand CNGPA
    ''' back. Depth WRITES on: the models had a pre-pass, we do not.
    ''' </summary>
    Public Sub Draw()
        If Not Enabled Then Return
        If Not MAP_LOADED OrElse map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return
        If Not loaded Then Load()
        If failed OrElse instances.Count = 0 OrElse shader Is Nothing Then Return

        GL_PUSH_GROUP("draw_tanks")
        MainFBO.attach_CNGP()
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Greater)
        GL.DepthMask(True)
        ' Culling off until the handedness is settled by eye - a mirrored
        ' world flips the winding, and a wrong guess here hides every face.
        GL.Disable(EnableCap.CullFace)

        shader.Use()
        GL.Uniform1(shader("normal_mode"), NormalMode)

        For Each inst In instances
            Dim world = Matrix4.CreateScale(If(MirrorX, -1.0F, 1.0F), 1.0F, 1.0F) *
                        Matrix4.CreateRotationY(inst.headingRad) *
                        Matrix4.CreateTranslation(inst.position)
            For Each part In inst.vehicle.parts
                Dim partModel = Matrix4.CreateTranslation(part.offset) * world
                For Each m In part.meshes
                    Dim model = partModel
                    If FlipSkinnedZ AndAlso m.layout.offBoneIdx >= 0 Then
                        model = Matrix4.CreateScale(1.0F, 1.0F, -1.0F) * partModel
                    End If
                    GL.UniformMatrix4(shader("u_model"), False, model)
                    Dim mat = part.MaterialFor(m)
                    BindMaterial(mat)
                    m.vao.Bind()
                    Dim itype = If(m.index32, DrawElementsType.UnsignedInt, DrawElementsType.UnsignedShort)
                    Dim isz = If(m.index32, 4, 2)
                    For Each g In m.groups
                        GL.DrawElementsBaseVertex(PrimitiveType.Triangles, g.nPrimitives * 3, itype,
                                                  New IntPtr(g.startIndex * isz), g.startVertex)
                    Next
                Next
            Next
        Next

        shader.StopUse()
        GL.BindVertexArray(0)
        MainFBO.attach_CNGPA()
        GL_POP_GROUP()
    End Sub

    ''' <summary>Units 0..3: AM, ANM, GMM, AO. A missing map binds nothing and the shader is told.</summary>
    Private Sub BindMaterial(mat As TankMaterial)
        Dim hasD = 0, hasN = 0, hasG = 0, hasA = 0
        If mat IsNot Nothing Then
            If mat.texDiffuse IsNot Nothing Then mat.texDiffuse.BindUnit(0) : hasD = 1
            If mat.texNormal IsNot Nothing Then mat.texNormal.BindUnit(1) : hasN = 1
            If mat.texGMM IsNot Nothing Then mat.texGMM.BindUnit(2) : hasG = 1
            If mat.texAO IsNot Nothing Then mat.texAO.BindUnit(3) : hasA = 1
            GL.Uniform1(shader("alpha_test"), If(mat.alphaTestEnable, 1, 0))
            GL.Uniform1(shader("alpha_ref"), mat.alphaReference / 255.0F)
            GL.Uniform1(shader("normal_dxt1"), If(mat.useNormalPackDXT1, 1, 0))
        Else
            GL.Uniform1(shader("alpha_test"), 0)
            GL.Uniform1(shader("normal_dxt1"), 0)
        End If
        GL.Uniform4(shader("has_maps"), hasD, hasN, hasG, hasA)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each v In vehicles
            v.Dispose()
        Next
        vehicles.Clear()
        instances.Clear()
    End Sub
End Class

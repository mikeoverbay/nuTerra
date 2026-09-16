Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' The base rings, like nuTerra's.
'''
''' SAME 50 m AND SAME COLOURS - green for team 1, red for team 2, from
''' MapBaseRings. nuTerra projects them onto the terrain in a deferred pass;
''' there is no deferred pass here, so the ring is built as a BAND OF
''' TRIANGLES that follows the ground, sampling the height at every segment.
''' The result reads the same and costs one draw.
'''
''' Built once at load rather than each frame: the ground does not move, and
''' the alternative is 128 height samples a frame for a circle that is always
''' in the same place.
'''
''' Added 2026-09-16 by nuTerra work, on the owner's ask.
''' </summary>
Module BrainRings

    Private Const RADIUS As Single = 50.0F      ' MapBaseRings' own radius
    Private Const WIDTH As Single = 2.0F        ' band thickness, metres
    Private Const SEGMENTS As Integer = 160
    ''' <summary>Lifted off the ground so it does not z-fight the terrain it
    ''' lies on. 0.25 m is under a tank's ground clearance, so a hull still
    ''' looks like it is standing ON the ground rather than above the ring.</summary>
    Private Const LIFT As Single = 0.25F

    Private ReadOnly TEAM_1_RGB As New Vector3(0.0F, 0.75F, 0.15F)
    Private ReadOnly TEAM_2_RGB As New Vector3(0.80F, 0.10F, 0.10F)

    Private shader As BrainShader
    Private vao1, vao2, vbo1, vbo2 As Integer
    Private count As Integer = 0

    Public Sub Init()
        shader = New BrainShader("line")
    End Sub

    Public Sub Build()
        count = 0
        If Not BrainTanks.HasBases Then
            LogThis("brain: no ctf bases on this map - no rings")
            Return
        End If
        build_one(BrainTanks.Base1, vao1, vbo1)
        build_one(BrainTanks.Base2, vao2, vbo2)
        LogThis("brain: base rings at ({0:0.0}, {1:0.0}) and ({2:0.0}, {3:0.0}), {4} m",
                BrainTanks.Base1.X, BrainTanks.Base1.Y,
                BrainTanks.Base2.X, BrainTanks.Base2.Y, RADIUS)
    End Sub

    ''' <summary>A closed triangle strip: inner and outer vertex per segment,
    ''' each at the terrain height under it.</summary>
    Private Sub build_one(centre As Vector2, ByRef vao As Integer, ByRef vbo As Integer)
        Dim v As New List(Of Single)
        For i = 0 To SEGMENTS
            Dim a = (i Mod SEGMENTS) / CSng(SEGMENTS) * MathHelper.TwoPi
            Dim ca = CSng(Math.Cos(a)), sa = CSng(Math.Sin(a))
            For Each r In {RADIUS - WIDTH * 0.5F, RADIUS + WIDTH * 0.5F}
                Dim x = centre.X + ca * r
                Dim z = centre.Y + sa * r
                Dim y = 0.0F
                Try
                    y = get_Y_at_XZ(x, z)
                Catch
                End Try
                v.Add(x) : v.Add(y + LIFT) : v.Add(z)
            Next
        Next
        count = (SEGMENTS + 1) * 2

        vao = GL.GenVertexArray()
        GL.BindVertexArray(vao)
        vbo = GL.GenBuffer()
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        Dim arr = v.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr, BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready OrElse count = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)

        ' Depth test ON so a hill in front hides the ring, but depth WRITE off:
        ' the band lies on the ground and would otherwise leave a lip in the
        ' depth buffer that hulls standing on the base clip against.
        GL.DepthMask(False)

        shader.SetVec3("colour", TEAM_1_RGB)
        GL.BindVertexArray(vao1)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, count)

        shader.SetVec3("colour", TEAM_2_RGB)
        GL.BindVertexArray(vao2)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, count)

        GL.DepthMask(True)
        GL.BindVertexArray(0)
    End Sub

End Module

Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' GO HERE. A crosshair the owner drops on the ground, and the thing the first
''' real brain drives at.
'''
''' "i want a go here crosshair i can place with enter at the look at point. I
''' want the tank to seek it" - the owner, 2026-09-16.
'''
''' PLACED AT THE CAMERA'S LOOK-AT, not at the mouse. The camera already has a
''' point on the ground that the owner aimed it at, and reusing it means the
''' goal lands exactly where he was looking rather than wherever a cursor
''' happened to be. It also means the goal can be placed without the picker,
''' which is the one part of this app that has never been fired.
'''
''' IT IS NOT A PATH. The brain gets a point and nothing else - no route, no
''' waypoints, no corridor. That is deliberate: a tank that can cross open
''' ground to a point it can see is the smallest thing worth calling a brain,
''' and everything harder is measured against whether it beats this.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Module BrainGoal

    ''' <summary>Where to go, or nothing. World XZ.</summary>
    Public Target As Vector2 = Vector2.Zero
    Public HasTarget As Boolean = False

    ''' <summary>
    ''' THE TARGET IS THE LOOK-AT POINT, live.
    '''
    ''' "use MY LOOK AT POINT AS TARGET!" - the owner. Placing a goal once
    ''' and leaving it made the camera and the target two different things to
    ''' keep track of; with this on, wherever he is looking IS where the tank
    ''' is going, and steering the test is steering the camera.
    '''
    ''' Alt still places one - it pins the goal where the camera is now, so a
    ''' scenario can be set up and then watched from somewhere else.
    ''' </summary>
    Public Follow As Boolean = True

    ''' <summary>Called every frame: keep the goal on the camera.</summary>
    Public Sub FollowCamera()
        If Not Follow Then Return
        Dim t = BrainRender.Cam.Target
        Target = New Vector2(t.X, t.Z)
        HasTarget = True
    End Sub

    ''' <summary>Arm span of the cross, metres.</summary>
    Private Const ARM As Single = 6.0F
    Private Const LIFT As Single = 0.4F

    Private shader As BrainShader
    Private vao, vbo As Integer
    Private verts As Integer = 0
    Private built_at As Vector2 = New Vector2(Single.MaxValue, Single.MaxValue)

    ' THE CURSOR - where the camera is looking, which is where Enter will
    ' put the goal. Its own buffer because it moves with every camera
    ' nudge while the goal stays put, and sharing one would rebuild the
    ' goal for nothing on every frame the view drifts.
    Private cur_vao, cur_vbo As Integer
    Private cur_verts As Integer = 0
    Private cur_at As Vector2 = New Vector2(Single.MaxValue, Single.MaxValue)

    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        cur_vao = GL.GenVertexArray()
        cur_vbo = GL.GenBuffer()
    End Sub

    ''' <summary>Drop it where the camera is looking.</summary>
    ''' <summary>Pin it here and stop following.</summary>
    Public Sub PlaceAtLookAt()
        Follow = False
        Dim t = BrainRender.Cam.Target
        Target = New Vector2(t.X, t.Z)
        HasTarget = True
        built_at = New Vector2(Single.MaxValue, Single.MaxValue)   ' force rebuild
        LogThis("brain: go here ({0:0.0}, {1:0.0}) - standable {2}",
                Target.X, Target.Y,
                BrainNav.Standable(Target.X, Target.Y, BrainNav.TRACE_R))
    End Sub

    Public Sub Clear()
        HasTarget = False
        verts = 0
    End Sub

    Private Sub build()
        Dim v As New List(Of Single)
        ' A cross, plus a small diamond round it so it reads as a marker and
        ' not as two stray lines lying on the ground.
        seg(v, Target.X - ARM, Target.Y, Target.X + ARM, Target.Y)
        seg(v, Target.X, Target.Y - ARM, Target.X, Target.Y + ARM)
        Dim d = ARM * 0.55F
        seg(v, Target.X - d, Target.Y, Target.X, Target.Y - d)
        seg(v, Target.X, Target.Y - d, Target.X + d, Target.Y)
        seg(v, Target.X + d, Target.Y, Target.X, Target.Y + d)
        seg(v, Target.X, Target.Y + d, Target.X - d, Target.Y)

        Dim arr = v.ToArray()
        verts = arr.Length \ 3
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        built_at = Target
    End Sub

    ''' <summary>One segment, both ends dropped onto the terrain under them.</summary>
    Private Sub seg(v As List(Of Single), ax As Single, az As Single,
                    bx As Single, bz As Single)
        For Each p In {New Vector2(ax, az), New Vector2(bx, bz)}
            Dim y = 0.0F
            Try
                ' BrainNav.Ground, NOT nuTerra's get_Y_at_XZ_fast.
                '
                ' The fast one returns 0.0 when mapBoard is Nothing, and this
                ' app never fills mapBoard - it builds its own terrain and
                ' reads it through BrainNav. Swapping to it for speed silently
                ' put every ray end at y = 0, which is UNDER the ground: the
                ' rays did not stop being drawn, they were drawn inside the
                ' hill. Nothing errored and nothing logged.
                '
                ' The speed came from the movement cache above, not from this
                ' call. Borrowing another app's fast path was never the win.
                y = BrainNav.Ground(p.X, p.Y)
            Catch
            End Try
            v.Add(p.X) : v.Add(y + LIFT) : v.Add(p.Y)
        Next
    End Sub

    ''' <summary>A small ring at the camera's look-at, drawn always.
    '''
    ''' Deliberately a different SHAPE from the goal, not just a different
    ''' colour: this one says where a goal WOULD go and the other says where
    ''' one is, and two crosses in two colours is a thing you have to stop
    ''' and read.
    ''' </summary>
    Public Sub DrawCursor(ByRef viewProj As Matrix4)
        If shader Is Nothing OrElse Not shader.Ready Then Return
        Dim t = BrainRender.Cam.Target
        Dim here As New Vector2(t.X, t.Z)
        If (here - cur_at).LengthSquared > 0.01F Then
            Dim v As New List(Of Single)
            Const R As Single = 3.0F
            Const N As Integer = 24
            For i = 0 To N - 1
                Dim a0 = i / CSng(N) * MathHelper.TwoPi
                Dim a1 = (i + 1) / CSng(N) * MathHelper.TwoPi
                seg(v, here.X + CSng(Math.Cos(a0)) * R, here.Y + CSng(Math.Sin(a0)) * R,
                       here.X + CSng(Math.Cos(a1)) * R, here.Y + CSng(Math.Sin(a1)) * R)
            Next
            ' A stalk, so it is findable when the ring is edge-on.
            seg(v, here.X, here.Y, here.X, here.Y)
            Dim arr = v.ToArray()
            cur_verts = arr.Length \ 3
            GL.BindVertexArray(cur_vao)
            GL.BindBuffer(BufferTarget.ArrayBuffer, cur_vbo)
            GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                          BufferUsageHint.DynamicDraw)
            GL.EnableVertexAttribArray(0)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
            GL.BindVertexArray(0)
            cur_at = here
        End If
        If cur_verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        GL.DepthMask(False)
        shader.SetVec3("colour", New Vector3(0.95F, 0.95F, 0.95F))
        GL.BindVertexArray(cur_vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, cur_verts)
        GL.BindVertexArray(0)
        GL.DepthMask(True)
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not HasTarget OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        If built_at <> Target Then build()
        If verts = 0 Then Return

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        GL.DepthMask(False)
        ' Amber, so it is not one of the radar's colours - the goal and what
        ' the radar found are different kinds of thing and should not have to
        ' be told apart by position.
        shader.SetVec3("colour", New Vector3(1.0F, 0.75F, 0.2F))
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.DepthMask(True)
    End Sub

End Module

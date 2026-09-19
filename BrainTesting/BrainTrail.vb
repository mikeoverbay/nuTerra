Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' WHERE IT HAS BEEN, AND HOW HARD IT WAS TURNING WHILE IT GOT THERE.
'''
''' "save and draw the path as it progresses. Angular changes per path that are
'''  lower are a higher score." - the owner, 2026-09-18.
'''
''' A scorecard says a run turned 4,000 degrees. It cannot say WHERE, and where
''' is the whole question - four thousand degrees spread evenly over half a
''' kilometre is a tank following a valley, and the same four thousand at one
''' spot is a tank spinning in a gateway. Those are opposite problems and they
''' produce the same number.
'''
''' So the trail is drawn in two colours. Calm where the hull was turning less
''' than HOT_DEG_PER_M, hot where it was turning more. The shape of the red
''' tells you what the number cannot: a red smear is a pirouette, a red corner
''' is a corner, and a trail with no red at all is the thing we are trying to
''' build.
'''
''' DEGREES PER METRE, not degrees. Degrees alone punishes a long run for being
''' long. What is wanted is how much steering it cost to cover ground, and that
''' is a rate - so a brain that drives 500 m round a hill scores better than one
''' that drives 100 m in a spiral, which is correct.
'''
''' POINTS EVERY STEP_M, not every tick. At 500 fps a tick is two centimetres;
''' recording those would be half a million vertices to say the same thing as
''' four hundred. The TURN is still accumulated every tick, because that is the
''' measurement and sampling it would under-report exactly the fast wobble
''' worth catching.
'''
''' Added 2026-09-18 by Tank AI work.
''' </summary>
Module BrainTrail

    Public SHOW As Boolean = True

    ''' <summary>Metres between recorded points.</summary>
    Private Const STEP_M As Single = 0.75F

    ''' <summary>Off the ground, so it does not z-fight the terrain.</summary>
    Private Const LIFT As Single = 0.35F

    ''' <summary>Above this many degrees of turn per metre, the segment is
    ''' drawn hot. Six is about a lane change; a corner taken properly is
    ''' under it and a hunt for a heading is well over.</summary>
    Private Const HOT_DEG_PER_M As Single = 6.0F

    ' PINK, because the map is green. The first version drew a green trail
    ' on grass through a forest and the owner had to hunt for his own path.
    ' A diagnostic you cannot find is not a diagnostic.
    Private ReadOnly CALM_RGB As New Vector3(1.00F, 0.20F, 0.75F)
    ' And yellow where it was turning hard - the two are far apart in hue
    ' AND in brightness, so the difference survives a dark hillside.
    Private ReadOnly HOT_RGB As New Vector3(1.00F, 0.95F, 0.15F)

    ''' <summary>Total heading change over the run, in degrees, and the ground
    ''' covered. The score is the ratio.</summary>
    Public TurnDeg As Single = 0.0F
    Public Metres As Single = 0.0F

    Public Function TurnPerMetre() As Single
        If Metres < 1.0F Then Return 0.0F
        Return TurnDeg / Metres
    End Function

    Private shader As BrainShader
    Private vaoC, vboC, vaoH, vboH As Integer
    Private ReadOnly calm As New List(Of Single)
    Private ReadOnly hot As New List(Of Single)
    Private dirty As Boolean = False

    Private started As Boolean = False
    Private lastPos As Vector2
    Private lastHead As Single
    Private markPos As Vector2
    Private turnSinceMark As Single = 0.0F

    Public Sub Init()
        shader = New BrainShader("line")
        vaoC = GL.GenVertexArray() : vboC = GL.GenBuffer()
        vaoH = GL.GenVertexArray() : vboH = GL.GenBuffer()
        setup(vaoC, vboC)
        setup(vaoH, vboH)
    End Sub

    Private Sub setup(vao As Integer, vbo As Integer)
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
    End Sub

    ''' <summary>A fresh run draws a fresh trail. Two runs on one trail is two
    ''' brains' paths in one picture with no way to tell them apart.</summary>
    Public Sub Reset()
        calm.Clear()
        hot.Clear()
        TurnDeg = 0.0F
        Metres = 0.0F
        turnSinceMark = 0.0F
        started = False
        dirty = True
    End Sub

    ''' <summary>
    ''' One tick. Turn accumulates every time; a point lands every STEP_M.
    '''
    ''' The segment's colour comes from the turning done SINCE THE LAST POINT
    ''' divided by the metre or so it took - so it is the rate along that
    ''' stretch of road, not the run's average, and a single bad corner does
    ''' not tint the whole trail.
    ''' </summary>
    Public Sub Note(pos As Vector2, headingRad As Single)
        If Not started Then
            started = True
            lastPos = pos
            markPos = pos
            lastHead = headingRad
            Return
        End If

        Dim d = (pos - lastPos).Length
        Metres += d
        Dim turned = Math.Abs(MathHelper.RadiansToDegrees(wrap_pi(headingRad - lastHead)))
        TurnDeg += turned
        turnSinceMark += turned
        lastPos = pos
        lastHead = headingRad

        Dim span = (pos - markPos).Length
        If span < STEP_M Then Return

        Dim rate = turnSinceMark / Math.Max(0.01F, span)
        Dim into = If(rate > HOT_DEG_PER_M, hot, calm)
        push(into, markPos)
        push(into, pos)
        markPos = pos
        turnSinceMark = 0.0F
        dirty = True
    End Sub

    Private Sub push(into As List(Of Single), p As Vector2)
        Dim y = 0.0F
        Try
            y = get_Y_at_XZ(p.X, p.Y)
        Catch
        End Try
        into.Add(p.X) : into.Add(y + LIFT) : into.Add(p.Y)
    End Sub

    ''' <summary>Angles into -pi..pi. Local, because a heading that wrapped
    ''' past north would otherwise register as a 360 degree turn and the score
    ''' would climb by a full circle every lap.</summary>
    Private Function wrap_pi(a As Single) As Single
        While a > Math.PI
            a -= CSng(Math.PI * 2.0)
        End While
        While a < -Math.PI
            a += CSng(Math.PI * 2.0)
        End While
        Return a
    End Function

    Public Sub Draw(ByRef viewProj As Matrix4)
        If Not SHOW OrElse shader Is Nothing OrElse Not shader.Ready Then Return
        If calm.Count = 0 AndAlso hot.Count = 0 Then Return

        If dirty Then
            upload(vaoC, vboC, calm)
            upload(vaoH, vboH, hot)
            dirty = False
        End If

        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        ' Depth test on so a hill hides the trail behind it, depth write off so
        ' the line does not leave a lip hulls clip against - BrainRings'
        ' reasoning, and it holds for the same reason.
        GL.DepthMask(False)
        GL.LineWidth(2.0F)

        If calm.Count > 0 Then
            shader.SetVec3("colour", CALM_RGB)
            GL.BindVertexArray(vaoC)
            GL.DrawArrays(PrimitiveType.Lines, 0, calm.Count \ 3)
        End If
        If hot.Count > 0 Then
            shader.SetVec3("colour", HOT_RGB)
            GL.BindVertexArray(vaoH)
            GL.DrawArrays(PrimitiveType.Lines, 0, hot.Count \ 3)
        End If

        GL.LineWidth(1.0F)
        GL.DepthMask(True)
        GL.BindVertexArray(0)
    End Sub

    Private Sub upload(vao As Integer, vbo As Integer, v As List(Of Single))
        If v.Count = 0 Then Return
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        Dim arr = v.ToArray()
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr,
                      BufferUsageHint.DynamicDraw)
        GL.BindVertexArray(0)
    End Sub

End Module

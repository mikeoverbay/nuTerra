Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' The viewer. Orbit a building, step through the library, switch LOD, solo a
''' part.
'''
'''     drag          orbit
'''     wheel         zoom
'''     left / right  previous / next building
'''     up / down     solo one part, or back to all
'''     [ / ]         coarser / finer LOD
'''     W             wireframe
'''     R             reload
'''     F             re-frame the camera
'''     Esc           quit
'''
''' The shaders are embedded rather than shipped as files. SrtViewer keeps its
''' in a folder and copies them at build; there are two here and twenty lines
''' between them, and an exe that cannot fail to find its own shaders is worth
''' more than the ability to edit them without a rebuild.
''' </summary>
Public Class ViewerWindow
    Inherits GameWindow

    Private Const VERT As String =
        "#version 330 core" & vbLf &
        "layout(location = 0) in vec3 a_pos;" & vbLf &
        "layout(location = 1) in vec3 a_nrm;" & vbLf &
        "uniform mat4 u_mvp;" & vbLf &
        "out vec3 v_nrm;" & vbLf &
        "out vec3 v_world;" & vbLf &
        "void main() {" & vbLf &
        "    v_nrm = a_nrm;" & vbLf &
        "    v_world = a_pos;" & vbLf &
        "    gl_Position = u_mvp * vec4(a_pos, 1.0);" & vbLf &
        "}" & vbLf

    ' Two-sided lighting on purpose: building meshes are open shells with
    ' single-sided walls, so a face turned away from the key light is common and
    ' must not read as a hole. abs() on the diffuse term, not a clamp.
    Private Const FRAG As String =
        "#version 330 core" & vbLf &
        "in vec3 v_nrm;" & vbLf &
        "in vec3 v_world;" & vbLf &
        "uniform vec3 u_tint;" & vbLf &
        "uniform int  u_flat;" & vbLf &
        "out vec4 o_col;" & vbLf &
        "void main() {" & vbLf &
        "    if (u_flat == 1) { o_col = vec4(u_tint, 1.0); return; }" & vbLf &
        "    vec3 n = normalize(v_nrm);" & vbLf &
        "    vec3 l = normalize(vec3(0.45, 0.8, 0.35));" & vbLf &
        "    float d = abs(dot(n, l));" & vbLf &
        "    float sky = 0.30 + 0.20 * n.y;" & vbLf &
        "    vec3 c = u_tint * (0.35 * sky + 0.75 * d);" & vbLf &
        "    o_col = vec4(pow(c, vec3(1.0 / 2.2)), 1.0);" & vbLf &
        "}" & vbLf

    Private ReadOnly pkg As PkgIndex
    Private ReadOnly library As BuildingLibrary
    Private ReadOnly assets As List(Of BuildingAsset)

    Private assetIndex As Integer = 0
    Private lodIndex As Integer = 0
    Private soloPart As Integer = -1          ' -1 = draw everything
    Private wireframe As Boolean = False

    ' one upload per loaded LOD
    Private vao As Integer = 0
    Private vbo As Integer = 0
    Private ebo As Integer = 0
    Private shader As Integer = 0
    Private uMvp As Integer, uTint As Integer, uFlat As Integer

    Private Class DrawPart
        Public Name As String
        Public First As Integer
        Public Count As Integer
        Public Tint As Vector3
    End Class
    Private ReadOnly parts As New List(Of DrawPart)
    Private totalVerts As Integer, totalTris As Integer

    ' camera
    Private yaw As Single = 0.7F
    Private pitch As Single = 0.35F
    Private dist As Single = 40.0F
    Private target As Vector3 = Vector3.Zero
    Private dragging As Boolean = False

    Public Sub New(index As PkgIndex, bl As BuildingLibrary, startAsset As Integer)
        MyBase.New(GameWindowSettings.Default,
                   New NativeWindowSettings With {
                       .Size = New Vector2i(1280, 800),
                       .Title = "BuildingSlicer",
                       .APIVersion = New Version(3, 3),
                       .Profile = ContextProfile.Core})
        pkg = index
        library = bl
        assets = bl.Assets.Values.ToList()
        assetIndex = Math.Max(0, Math.Min(startAsset, assets.Count - 1))
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        GL.ClearColor(0.13F, 0.14F, 0.16F, 1.0F)
        GL.Enable(EnableCap.DepthTest)
        shader = BuildShader()
        uMvp = GL.GetUniformLocation(shader, "u_mvp")
        uTint = GL.GetUniformLocation(shader, "u_tint")
        uFlat = GL.GetUniformLocation(shader, "u_flat")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
        ebo = GL.GenBuffer()
        LoadCurrent()
    End Sub

    Private Function BuildShader() As Integer
        Dim vs = GL.CreateShader(ShaderType.VertexShader)
        GL.ShaderSource(vs, VERT)
        GL.CompileShader(vs)
        CheckShader(vs, "vertex")
        Dim fs = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fs, FRAG)
        GL.CompileShader(fs)
        CheckShader(fs, "fragment")
        Dim p = GL.CreateProgram()
        GL.AttachShader(p, vs)
        GL.AttachShader(p, fs)
        GL.LinkProgram(p)
        Dim ok As Integer
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then Throw New Exception("shader link failed: " & GL.GetProgramInfoLog(p))
        GL.DeleteShader(vs)
        GL.DeleteShader(fs)
        Return p
    End Function

    Private Shared Sub CheckShader(s As Integer, what As String)
        Dim ok As Integer
        GL.GetShader(s, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception(what & " shader: " & GL.GetShaderInfoLog(s))
    End Sub

    ''' <summary>
    ''' Load every part of the current asset's current LOD into one buffer pair.
    '''
    ''' Normals are DERIVED from the triangles rather than read from the file.
    ''' The vertex carries a packed 8-8-8 normal, but decoding it is a second
    ''' thing that can be wrong, and a face normal cannot be: it falls straight
    ''' out of the winding, which the X mirror already had to get right. If the
    ''' derived shading looks correct then the positions and the winding are
    ''' both right, which is exactly what wants proving first.
    ''' </summary>
    Private Sub LoadCurrent()
        parts.Clear()
        totalVerts = 0 : totalTris = 0

        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return
        lodIndex = Math.Max(0, Math.Min(lodIndex, lods.Count - 1))
        Dim lod = lods(lodIndex)

        Dim verts As New List(Of Single)      ' px py pz nx ny nz
        Dim idx As New List(Of Integer)
        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        Dim any = False

        Dim palette = {
            New Vector3(0.82F, 0.79F, 0.73F), New Vector3(0.74F, 0.66F, 0.58F),
            New Vector3(0.66F, 0.72F, 0.78F), New Vector3(0.79F, 0.72F, 0.62F),
            New Vector3(0.70F, 0.76F, 0.68F), New Vector3(0.80F, 0.70F, 0.70F)}

        For Each part In asset.PartsAt(lod)
            Dim primPath = PrimitivesPathFor(part)
            Dim raw = pkg.ReadPath(primPath)
            If raw Is Nothing Then Continue For

            Dim meshes As List(Of PrimMesh)
            Try
                meshes = PrimitivesFile.Parse(raw)
            Catch
                Continue For
            End Try

            For Each m In meshes
                If m.Positions.Length = 0 OrElse m.Indices.Length < 3 Then Continue For

                ' accumulate face normals per vertex
                Dim nrm(m.Positions.Length - 1) As Vector3
                Dim t = 0
                While t + 2 < m.Indices.Length
                    Dim i0 = m.Indices(t), i1 = m.Indices(t + 1), i2 = m.Indices(t + 2)
                    If i0 >= 0 AndAlso i2 < m.Positions.Length AndAlso
                       i1 >= 0 AndAlso i1 < m.Positions.Length AndAlso
                       i0 < m.Positions.Length AndAlso i2 >= 0 Then
                        Dim fn = Vector3.Cross(m.Positions(i1) - m.Positions(i0),
                                               m.Positions(i2) - m.Positions(i0))
                        nrm(i0) += fn : nrm(i1) += fn : nrm(i2) += fn
                    End If
                    t += 3
                End While

                Dim baseVert = totalVerts
                For i = 0 To m.Positions.Length - 1
                    Dim p = m.Positions(i)
                    Dim n = nrm(i)
                    If n.LengthSquared > 0.000000001F Then n.Normalize() Else n = Vector3.UnitY
                    verts.Add(p.X) : verts.Add(p.Y) : verts.Add(p.Z)
                    verts.Add(n.X) : verts.Add(n.Y) : verts.Add(n.Z)
                    lo = Vector3.ComponentMin(lo, p)
                    hi = Vector3.ComponentMax(hi, p)
                    any = True
                Next

                Dim first = idx.Count
                For i = 0 To m.Indices.Length - 1
                    Dim v = m.Indices(i)
                    If v < 0 OrElse v >= m.Positions.Length Then v = 0
                    idx.Add(baseVert + v)
                Next

                parts.Add(New DrawPart With {
                    .Name = m.Name,
                    .First = first,
                    .Count = m.Indices.Length,
                    .Tint = palette(parts.Count Mod palette.Length)})

                totalVerts += m.Positions.Length
                totalTris += m.TriangleCount
            Next
        Next

        If Not any Then
            Console.WriteLine("{0} lod{1}: no geometry could be read", asset.Name, lod)
            Return
        End If

        target = (lo + hi) * 0.5F
        Dim span = (hi - lo).Length
        dist = Math.Max(span * 0.9F, 2.0F)

        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.StaticDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.BindVertexArray(0)

        Console.WriteLine("{0}  lod{1}  {2} part(s)  {3:N0} verts  {4:N0} tris  span {5:F1} m",
                          asset.Name, lod, parts.Count, totalVerts, totalTris, span)
        Title = String.Format("BuildingSlicer - {0}  lod{1}  [{2}/{3}]  {4:N0} tris",
                              asset.Name, lod, assetIndex + 1, assets.Count, totalTris)
    End Sub

    ''' <summary>
    ''' Where a part's geometry lives. The .model names its visual without an
    ''' extension, and the primitives sit beside it under the same stem - so the
    ''' visual is what to trust. Only fall back to the .model's own path when a
    ''' file left the key out.
    ''' </summary>
    Private Shared Function PrimitivesPathFor(part As BuildingPart) As String
        If Not String.IsNullOrEmpty(part.Visual) Then
            Return part.Visual.Replace("\"c, "/"c).ToLowerInvariant() & ".primitives_processed"
        End If
        Return part.Path.Substring(0, part.Path.Length - ".model".Length) & ".primitives_processed"
    End Function

    Protected Overrides Sub OnRenderFrame(e As FrameEventArgs)
        MyBase.OnRenderFrame(e)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        If parts.Count > 0 Then
            Dim aspect = CSng(Math.Max(ClientSize.X, 1)) / Math.Max(ClientSize.Y, 1)
            Dim eye = target + New Vector3(
                CSng(Math.Cos(pitch) * Math.Sin(yaw)) * dist,
                CSng(Math.Sin(pitch)) * dist,
                CSng(Math.Cos(pitch) * Math.Cos(yaw)) * dist)
            Dim view = Matrix4.LookAt(eye, target, Vector3.UnitY)
            Dim proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45.0F), aspect, Math.Max(dist * 0.001F, 0.02F), dist * 10.0F + 500.0F)
            Dim mvp = view * proj

            GL.UseProgram(shader)
            GL.UniformMatrix4(uMvp, False, mvp)
            GL.BindVertexArray(vao)
            GL.PolygonMode(MaterialFace.FrontAndBack,
                           If(wireframe, PolygonMode.Line, PolygonMode.Fill))

            For i = 0 To parts.Count - 1
                If soloPart >= 0 AndAlso i <> soloPart Then Continue For
                Dim tc = parts(i).Tint
                GL.Uniform3(uTint, tc.X, tc.Y, tc.Z)
                GL.Uniform1(uFlat, If(wireframe, 1, 0))
                GL.DrawElements(PrimitiveType.Triangles, parts(i).Count,
                                DrawElementsType.UnsignedInt, parts(i).First * 4)
            Next

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
            GL.BindVertexArray(0)
        End If

        SwapBuffers()
    End Sub

    Protected Overrides Sub OnUpdateFrame(e As FrameEventArgs)
        MyBase.OnUpdateFrame(e)
        Dim k = KeyboardState

        If k.IsKeyDown(Keys.Escape) Then Close()

        If k.IsKeyPressed(Keys.Right) Then
            assetIndex = (assetIndex + 1) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.Left) Then
            assetIndex = (assetIndex - 1 + assets.Count) Mod assets.Count
            lodIndex = 0 : soloPart = -1 : LoadCurrent()
        End If

        If k.IsKeyPressed(Keys.RightBracket) Then
            lodIndex += 1 : soloPart = -1 : LoadCurrent()
        ElseIf k.IsKeyPressed(Keys.LeftBracket) Then
            lodIndex = Math.Max(0, lodIndex - 1) : soloPart = -1 : LoadCurrent()
        End If

        If k.IsKeyPressed(Keys.Down) Then
            soloPart += 1
            If soloPart >= parts.Count Then soloPart = -1
            ReportSolo()
        ElseIf k.IsKeyPressed(Keys.Up) Then
            soloPart -= 1
            If soloPart < -1 Then soloPart = parts.Count - 1
            ReportSolo()
        End If

        If k.IsKeyPressed(Keys.W) Then wireframe = Not wireframe
        If k.IsKeyPressed(Keys.R) Then LoadCurrent()
        If k.IsKeyPressed(Keys.F) Then dist = Math.Max(dist, 2.0F)

        ' orbit
        Dim m = MouseState
        If m.IsButtonDown(MouseButton.Left) Then
            If dragging Then
                yaw -= m.Delta.X * 0.008F
                pitch = Math.Max(-1.5F, Math.Min(1.5F, pitch + m.Delta.Y * 0.008F))
            End If
            dragging = True
        Else
            dragging = False
        End If
        If m.ScrollDelta.Y <> 0 Then
            dist *= CSng(Math.Pow(0.88, m.ScrollDelta.Y))
            dist = Math.Max(0.2F, dist)
        End If
    End Sub

    Private Sub ReportSolo()
        If soloPart < 0 Then
            Console.WriteLine("  all {0} parts", parts.Count)
        Else
            Console.WriteLine("  part {0}/{1}  {2}  {3:N0} tris",
                              soloPart + 1, parts.Count, parts(soloPart).Name,
                              parts(soloPart).Count \ 3)
        End If
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)
    End Sub
End Class

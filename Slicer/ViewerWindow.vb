Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

''' <summary>
''' The viewer. Orbit a building, step through the library, switch LOD, solo a
''' part - and cut it.
'''
'''     drag          orbit                     S      slicing on / off
'''     wheel         zoom                      , .    move the plane
'''     left / right  previous / next building  shift  move it 10x
'''     [ / ]         coarser / finer LOD       X Y Z  cut axis
'''     up / down     solo one part             K      keep below / above / both
'''     W             wireframe                 C      cut outline on / off
'''     R             reload                    Esc    quit
'''
''' The shaders are embedded rather than shipped as files. SrtViewer keeps its
''' in a folder and copies them at build; there are two here and thirty lines
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
        "void main() {" & vbLf &
        "    v_nrm = a_nrm;" & vbLf &
        "    gl_Position = u_mvp * vec4(a_pos, 1.0);" & vbLf &
        "}" & vbLf

    ' Two-sided lighting on purpose: only 4.6% of these meshes are watertight,
    ' so a face turned away from the key light is normal and must not read as a
    ' hole. abs() on the diffuse term, not a clamp. A cut makes this matter
    ' more, not less - it exposes interior backfaces by design.
    Private Const FRAG As String =
        "#version 330 core" & vbLf &
        "in vec3 v_nrm;" & vbLf &
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
    Private ReadOnly assets As List(Of BuildingAsset)
    Private ReadOnly settings As SliceSettings

    Private assetIndex As Integer = 0
    Private lodIndex As Integer = 0
    Private soloPart As Integer = -1
    Private wireframe As Boolean = False

    ' slicing
    Private slicing As Boolean = True
    Private showCut As Boolean = True
    Private planeNudge As Single = 0.0F        ' metres, on top of settings.Offset
    Private axisOverride As String = Nothing
    Private keepOverride As String = Nothing

    Private vao, vbo, ebo, shader As Integer
    Private cutVao, cutVbo As Integer
    Private cutVertCount As Integer = 0
    Private uMvp, uTint, uFlat As Integer

    ''' <summary>A part as it came out of the packages, kept CPU-side so the
    ''' plane can be moved without re-reading a single byte.</summary>
    Private Class RawPart
        Public Name As String
        Public Pos As Vector3()
        Public Idx As Integer()
    End Class
    Private ReadOnly rawParts As New List(Of RawPart)

    Private Class DrawPart
        Public Name As String
        Public First As Integer
        Public Count As Integer
        Public Tint As Vector3
    End Class
    Private ReadOnly parts As New List(Of DrawPart)

    Private boundsMin, boundsMax As Vector3
    Private totalVerts, totalTris, clippedTris, cutSegs As Integer

    Private yaw As Single = 0.7F
    Private pitch As Single = 0.35F
    Private dist As Single = 40.0F
    Private target As Vector3 = Vector3.Zero
    Private dragging As Boolean = False

    Public Sub New(index As PkgIndex, bl As BuildingLibrary, startAsset As Integer, cfg As SliceSettings)
        MyBase.New(GameWindowSettings.Default,
                   New NativeWindowSettings With {
                       .Size = New Vector2i(1280, 800),
                       .Title = "Slicer",
                       .APIVersion = New Version(3, 3),
                       .Profile = ContextProfile.Core})
        pkg = index
        assets = bl.Assets.Values.ToList()
        settings = cfg
        assetIndex = Math.Max(0, Math.Min(startAsset, assets.Count - 1))
        lodIndex = Math.Max(0, cfg.Lod)
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        GL.ClearColor(0.13F, 0.14F, 0.16F, 1.0F)
        GL.Enable(EnableCap.DepthTest)
        shader = BuildShader()
        uMvp = GL.GetUniformLocation(shader, "u_mvp")
        uTint = GL.GetUniformLocation(shader, "u_tint")
        uFlat = GL.GetUniformLocation(shader, "u_flat")
        vao = GL.GenVertexArray() : vbo = GL.GenBuffer() : ebo = GL.GenBuffer()
        cutVao = GL.GenVertexArray() : cutVbo = GL.GenBuffer()
        LoadCurrent()
    End Sub

    Private Function BuildShader() As Integer
        Dim vs = GL.CreateShader(ShaderType.VertexShader)
        GL.ShaderSource(vs, VERT) : GL.CompileShader(vs) : CheckShader(vs, "vertex")
        Dim fs = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fs, FRAG) : GL.CompileShader(fs) : CheckShader(fs, "fragment")
        Dim p = GL.CreateProgram()
        GL.AttachShader(p, vs) : GL.AttachShader(p, fs) : GL.LinkProgram(p)
        Dim ok As Integer
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then Throw New Exception("shader link failed: " & GL.GetProgramInfoLog(p))
        GL.DeleteShader(vs) : GL.DeleteShader(fs)
        Return p
    End Function

    Private Shared Sub CheckShader(s As Integer, what As String)
        Dim ok As Integer
        GL.GetShader(s, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception(what & " shader: " & GL.GetShaderInfoLog(s))
    End Sub

    ''' <summary>Read the current asset's current LOD out of the packages. Only
    ''' called when the asset or LOD changes - moving the plane does not touch
    ''' this, which is what lets the plane be dragged.</summary>
    Private Sub LoadCurrent()
        rawParts.Clear()
        Dim asset = assets(assetIndex)
        Dim lods = asset.Lods
        If lods.Count = 0 Then Return
        lodIndex = Math.Max(0, Math.Min(lodIndex, lods.Count - 1))
        Dim lod = lods(lodIndex)

        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        Dim any = False
        Dim wanted = If(settings.Parts, "all").Trim().ToLowerInvariant()

        For Each part In asset.PartsAt(lod)
            If wanted <> "all" AndAlso Not part.Name.ToLowerInvariant().Contains(wanted) Then Continue For
            Dim raw = pkg.ReadPath(PrimitivesPathFor(part))
            If raw Is Nothing Then Continue For
            Dim meshes As List(Of PrimMesh)
            Try
                meshes = PrimitivesFile.Parse(raw)
            Catch
                Continue For
            End Try
            For Each m In meshes
                If m.Positions.Length = 0 OrElse m.Indices.Length < 3 Then Continue For
                rawParts.Add(New RawPart With {.Name = m.Name, .Pos = m.Positions, .Idx = m.Indices})
                For Each p In m.Positions
                    lo = Vector3.ComponentMin(lo, p)
                    hi = Vector3.ComponentMax(hi, p)
                    any = True
                Next
            Next
        Next

        If Not any Then
            Console.WriteLine("{0} lod{1}: no geometry could be read", asset.Name, lod)
            parts.Clear()
            Return
        End If

        boundsMin = lo : boundsMax = hi
        target = (lo + hi) * 0.5F
        dist = Math.Max((hi - lo).Length * 0.9F, 2.0F)
        planeNudge = 0.0F
        Rebuild()
        Console.WriteLine("{0}  lod{1}  {2} mesh(es)  {3:N0} tris  span {4:F1} m",
                          asset.Name, lod, rawParts.Count, totalTris, (hi - lo).Length)
    End Sub

    Private Function EffectiveAxis() As String
        Return If(axisOverride, settings.Axis)
    End Function

    Private Function EffectiveKeep() As String
        Return If(keepOverride, settings.Keep).Trim().ToLowerInvariant()
    End Function

    Private Function AxisNormal() As Vector3
        Select Case EffectiveAxis().Trim().ToLowerInvariant()
            Case "x" : Return New Vector3(1, 0, 0)
            Case "y" : Return New Vector3(0, 1, 0)
            Case "z" : Return New Vector3(0, 0, 1)
            Case Else : Return settings.EffectiveNormal()
        End Select
    End Function

    ''' <summary>
    ''' Slice (or not) and upload. Cheap enough to call while a key is held.
    '''
    ''' Normals are DERIVED from the winding rather than read from the packed
    ''' 8-8-8 in the vertex, and after a cut they have to be: a clipped triangle
    ''' has corners that did not exist in the source, so there is no stored
    ''' normal to read for them.
    ''' </summary>
    Private Sub Rebuild()
        parts.Clear()
        totalVerts = 0 : totalTris = 0 : clippedTris = 0 : cutSegs = 0

        Dim verts As New List(Of Single)
        Dim idx As New List(Of Integer)
        Dim cuts As New List(Of Single)

        Dim palette = {
            New Vector3(0.82F, 0.79F, 0.73F), New Vector3(0.74F, 0.66F, 0.58F),
            New Vector3(0.66F, 0.72F, 0.78F), New Vector3(0.79F, 0.72F, 0.62F),
            New Vector3(0.70F, 0.76F, 0.68F), New Vector3(0.80F, 0.70F, 0.70F)}

        Dim n = AxisNormal()
        Dim planeD = MeshSlicer.PlaneDistance(settings, boundsMin, boundsMax, planeNudge)
        Dim keep = EffectiveKeep()
        ' On-plane tolerance proportional to the model, so a 206 m dam and an
        ' 8 m shed behave the same.
        Dim eps = Math.Max((boundsMax - boundsMin).Length * 0.000001F, 0.000001F)

        For Each rp In rawParts
            Dim pos = rp.Pos
            Dim tri = rp.Idx

            If slicing Then
                Dim res As SliceResult
                If keep = "above" Then
                    res = MeshSlicer.Clip(pos, tri, -n, -planeD, eps)
                ElseIf keep = "both" Then
                    ' Both halves, each clipped, so the mesh is re-triangulated
                    ' at the plane and the cut outline still falls out. Looks
                    ' like the whole building, but it has been cut.
                    Dim a = MeshSlicer.Clip(pos, tri, n, planeD, eps)
                    Dim b = MeshSlicer.Clip(pos, tri, -n, -planeD, eps)
                    Dim shift = a.Positions.Count
                    For Each p In b.Positions
                        a.Positions.Add(p)
                    Next
                    For Each i In b.Indices
                        a.Indices.Add(i + shift)
                    Next
                    a.CutEdges.AddRange(b.CutEdges)
                    a.TrianglesClipped += b.TrianglesClipped
                    res = a
                Else
                    res = MeshSlicer.Clip(pos, tri, n, planeD, eps)
                End If

                If res.Positions.Count = 0 Then Continue For
                pos = res.Positions.ToArray()
                tri = res.Indices.ToArray()
                clippedTris += res.TrianglesClipped
                cutSegs += res.CutSegments
                For Each p In res.CutEdges
                    cuts.Add(p.X) : cuts.Add(p.Y) : cuts.Add(p.Z)
                    cuts.Add(0.0F) : cuts.Add(1.0F) : cuts.Add(0.0F)   ' normal unused, flat shaded
                Next
            End If

            If pos.Length = 0 OrElse tri.Length < 3 Then Continue For

            Dim nrm(pos.Length - 1) As Vector3
            Dim t = 0
            While t + 2 < tri.Length
                Dim i0 = tri(t), i1 = tri(t + 1), i2 = tri(t + 2)
                If i0 >= 0 AndAlso i0 < pos.Length AndAlso i1 >= 0 AndAlso i1 < pos.Length AndAlso
                   i2 >= 0 AndAlso i2 < pos.Length Then
                    Dim fn = Vector3.Cross(pos(i1) - pos(i0), pos(i2) - pos(i0))
                    nrm(i0) += fn : nrm(i1) += fn : nrm(i2) += fn
                End If
                t += 3
            End While

            Dim baseVert = totalVerts
            For i = 0 To pos.Length - 1
                Dim nv = nrm(i)
                If nv.LengthSquared > 0.000000001F Then nv.Normalize() Else nv = Vector3.UnitY
                verts.Add(pos(i).X) : verts.Add(pos(i).Y) : verts.Add(pos(i).Z)
                verts.Add(nv.X) : verts.Add(nv.Y) : verts.Add(nv.Z)
            Next
            Dim first = idx.Count
            For i = 0 To tri.Length - 1
                Dim vi = tri(i)
                If vi < 0 OrElse vi >= pos.Length Then vi = 0
                idx.Add(baseVert + vi)
            Next
            parts.Add(New DrawPart With {
                .Name = rp.Name, .First = first, .Count = tri.Length,
                .Tint = palette(parts.Count Mod palette.Length)})
            totalVerts += pos.Length
            totalTris += tri.Length \ 3
        Next

        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.DynamicDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.BindVertexArray(0)

        cutVertCount = cuts.Count \ 6
        GL.BindVertexArray(cutVao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, cutVbo)
        GL.BufferData(BufferTarget.ArrayBuffer, cuts.Count * 4, cuts.ToArray(), BufferUsageHint.DynamicDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 24, 0)
        GL.EnableVertexAttribArray(1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, False, 24, 12)
        GL.BindVertexArray(0)

        UpdateTitle(planeD)
    End Sub

    Private Sub UpdateTitle(planeD As Single)
        Dim asset = assets(assetIndex)
        Dim lod = If(asset.Lods.Count > 0, asset.Lods(Math.Min(lodIndex, asset.Lods.Count - 1)), 0)
        Title = String.Format(
            "Slicer - {0}  lod{1}  [{2}/{3}]  {4:N0} tris{5}",
            asset.Name, lod, assetIndex + 1, assets.Count, totalTris,
            If(slicing,
               String.Format("   CUT {0} {1} @ {2:F2} m   {3:N0} clipped, {4:N0} cut edges",
                             EffectiveAxis().ToUpperInvariant(), EffectiveKeep(), planeD, clippedTris, cutSegs),
               "   cut OFF"))
    End Sub

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
                MathHelper.DegreesToRadians(45.0F), aspect,
                Math.Max(dist * 0.001F, 0.02F), dist * 10.0F + 500.0F)
            Dim mvp = view * proj

            GL.UseProgram(shader)
            GL.UniformMatrix4(uMvp, False, mvp)
            GL.BindVertexArray(vao)
            GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))
            For i = 0 To parts.Count - 1
                If soloPart >= 0 AndAlso i <> soloPart Then Continue For
                Dim tc = parts(i).Tint
                GL.Uniform3(uTint, tc.X, tc.Y, tc.Z)
                GL.Uniform1(uFlat, If(wireframe, 1, 0))
                GL.DrawElements(PrimitiveType.Triangles, parts(i).Count,
                                DrawElementsType.UnsignedInt, parts(i).First * 4)
            Next
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)

            ' The cut outline last, flat and unlit, with depth test off - the
            ' point of it is to show WHERE the plane met the surface, including
            ' the parts of the cut that sit behind a wall.
            If showCut AndAlso slicing AndAlso cutVertCount > 0 Then
                GL.Disable(EnableCap.DepthTest)
                GL.BindVertexArray(cutVao)
                GL.Uniform1(uFlat, 1)
                GL.Uniform3(uTint, 1.0F, 0.35F, 0.1F)
                GL.LineWidth(2.0F)
                GL.DrawArrays(PrimitiveType.Lines, 0, cutVertCount)
                GL.Enable(EnableCap.DepthTest)
            End If
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

        ' ---- the cut ----
        Dim dirty = False
        If k.IsKeyPressed(Keys.S) Then slicing = Not slicing : dirty = True
        If k.IsKeyPressed(Keys.C) Then showCut = Not showCut
        If k.IsKeyPressed(Keys.X) Then axisOverride = "x" : dirty = True
        If k.IsKeyPressed(Keys.Y) Then axisOverride = "y" : dirty = True
        If k.IsKeyPressed(Keys.Z) Then axisOverride = "z" : dirty = True

        If k.IsKeyPressed(Keys.K) Then
            Select Case EffectiveKeep()
                Case "below" : keepOverride = "above"
                Case "above" : keepOverride = "both"
                Case Else : keepOverride = "below"
            End Select
            dirty = True
        End If

        ' The step scales with the model, so one press is a useful move on an
        ' 8 m shed and on a 206 m dam alike.
        Dim span = (boundsMax - boundsMin).Length
        Dim stepM = Math.Max(span * 0.004F, 0.005F)
        Dim fast = k.IsKeyDown(Keys.LeftShift) OrElse k.IsKeyDown(Keys.RightShift)
        Dim amount = If(fast, stepM * 10.0F, stepM)
        If k.IsKeyDown(Keys.Period) Then planeNudge += amount : dirty = True
        If k.IsKeyDown(Keys.Comma) Then planeNudge -= amount : dirty = True

        If dirty AndAlso rawParts.Count > 0 Then Rebuild()

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
            dist = Math.Max(0.2F, dist * CSng(Math.Pow(0.88, m.ScrollDelta.Y)))
        End If
    End Sub

    Private Sub ReportSolo()
        If soloPart < 0 Then
            Console.WriteLine("  all {0} parts", parts.Count)
        Else
            Console.WriteLine("  part {0}/{1}  {2}  {3:N0} tris",
                              soloPart + 1, parts.Count, parts(soloPart).Name, parts(soloPart).Count \ 3)
        End If
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y)
    End Sub
End Class

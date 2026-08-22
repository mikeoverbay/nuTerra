Imports System.IO
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

Public Class ViewerWindow
    Inherits GameWindow

    Private Class Part
        Public Vao As Integer
        Public Vbo As Integer
        Public Ebo As Integer
        Public IndexCount As Integer
        Public Stride As Integer
        Public Tint As Vector3
        ''' <summary>Bark tiles past 0..1; foliage cards stay inside it.</summary>
        Public UsesBark As Boolean
        '''<summary>GL texture the file declares for this part, 0 when it declares none.</summary>
        Public Tex As Integer
        Public FlatUV As Boolean
        Public Kind As SrtFile.PartKind
        ''' <summary>False when the normals were derived rather than read from the file.</summary>
        Public HasNormals As Boolean
        Public Lod As Integer
        ''' <summary>Geometry type index within a LOD, ie i mod period.</summary>
        Public Slot As Integer
    End Class

    Private ReadOnly files As List(Of String)
    Private ReadOnly pkg As PkgIndex
    Private fileIndex As Integer

    Private srt As SrtFile
    Private parts As New List(Of Part)
    Private program As Integer
    Private texFoliage As Integer
    Private texBark As Integer
    Private texWhite As Integer
    '''<summary>One entry per distinct texture the current file names.</summary>
    Private ReadOnly texByName As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    Private lodFilter As Integer = 0     ' -1 = every LOD
    Private lodCount As Integer = 1
    ''' <summary>Bend bones are not surface geometry, so they stay hidden by default.</summary>
    Private showBones As Boolean = False

    ' camera
    Private yaw As Single = 0.7F
    Private pitch As Single = 0.25F
    Private dist As Single = 20.0F
    Private target As Vector3
    Private dragging As Boolean
    Private lastMouse As Vector2

    ' view options
    Private wireframe As Boolean
    Private shadeMode As Integer = 0     ' index into ShadeModes
    Private Shared ReadOnly ShadeModes() As String = {"textured", "flat", "uv", "normals"}
    ''' <summary>uMode value the shader uses for unlit wireframe.</summary>
    Private Const WIRE_MODE As Integer = 4
    Private alphaTest As Boolean = True
    Private soloPart As Integer = -1      ' -1 = all draw calls

    ''' <summary>Light grey, so wires read clearly against the dark background.</summary>
    Private Shared ReadOnly WireColour As New Vector3(0.82F, 0.82F, 0.82F)

    Private Shared ReadOnly PartColours() As Vector3 = {
        New Vector3(0.55F, 0.78F, 0.35F), New Vector3(0.85F, 0.55F, 0.30F),
        New Vector3(0.40F, 0.65F, 0.90F), New Vector3(0.90F, 0.80F, 0.35F),
        New Vector3(0.75F, 0.45F, 0.80F), New Vector3(0.45F, 0.85F, 0.75F),
        New Vector3(0.90F, 0.45F, 0.50F), New Vector3(0.60F, 0.60F, 0.60F)}

    Public Sub New(files As List(Of String), pkg As PkgIndex, startIndex As Integer)
        MyBase.New(GameWindowSettings.Default,
                   New NativeWindowSettings With {
                        .Size = New Vector2i(1280, 860),
                        .APIVersion = New Version(4, 5),
                        .Profile = ContextProfile.Core,
                        .Title = "SRT Viewer"})
        Me.files = files
        Me.pkg = pkg
        Me.fileIndex = startIndex
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        GL.ClearColor(0.13F, 0.15F, 0.18F, 1.0F)
        GL.Enable(EnableCap.DepthTest)
        program = BuildProgram()
        texWhite = DdsLoader.White()
        LoadCurrent()
    End Sub

    Private Function BuildProgram() As Integer
        Dim dir = Path.Combine(AppContext.BaseDirectory, "shaders")
        Dim vs = Compile(ShaderType.VertexShader, File.ReadAllText(Path.Combine(dir, "srt.vert")))
        Dim fs = Compile(ShaderType.FragmentShader, File.ReadAllText(Path.Combine(dir, "srt.frag")))
        Dim p = GL.CreateProgram()
        GL.AttachShader(p, vs) : GL.AttachShader(p, fs)
        GL.LinkProgram(p)
        Dim ok As Integer
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then Throw New Exception("link failed: " & GL.GetProgramInfoLog(p))
        GL.DeleteShader(vs) : GL.DeleteShader(fs)
        Return p
    End Function

    Private Shared Function Compile(kind As ShaderType, src As String) As Integer
        Dim s = GL.CreateShader(kind)
        GL.ShaderSource(s, src)
        GL.CompileShader(s)
        Dim ok As Integer
        GL.GetShader(s, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception(kind.ToString() & ": " & GL.GetShaderInfoLog(s))
        Return s
    End Function

    Private Sub ClearParts()
        For Each p In parts
            GL.DeleteVertexArray(p.Vao)
            GL.DeleteBuffer(p.Vbo)
            GL.DeleteBuffer(p.Ebo)
        Next
        parts.Clear()
        For Each t In texByName.Values
            If t <> 0 Then GL.DeleteTexture(t)
        Next
        texByName.Clear()
        texFoliage = 0
        texBark = 0
    End Sub

    Private Sub LoadCurrent()
        ClearParts()
        soloPart = -1

        Dim name = files(fileIndex)
        Try
            If pkg IsNot Nothing AndAlso Not File.Exists(name) Then
                srt = SrtFile.FromBytes(pkg.Read(pkg.Lookup(name)), name)
            Else
                srt = SrtFile.Load(name)
            End If
        Catch ex As Exception
            Console.WriteLine("failed to read {0}: {1}", name, ex.Message)
            Return
        End Try

        Console.WriteLine("--------------------------------------------------")
        Console.WriteLine("{0}", name)
        Console.WriteLine("  magic {0}   bbox {1:F2} {2:F2} {3:F2} .. {4:F2} {5:F2} {6:F2}",
                          srt.Magic, srt.BoundsMin(0), srt.BoundsMin(1), srt.BoundsMin(2),
                          srt.BoundsMax(0), srt.BoundsMax(1), srt.BoundsMax(2))
        Console.WriteLine("  foliage {0}", If(srt.FoliageTexture, "(none)"))
        Console.WriteLine("  bark    {0}", If(srt.BarkTexture, "(none)"))
        If srt.Solved Then
            Console.WriteLine("  {0} draw calls, {1} LOD(s), {2} triangles ({3} surface, {4} bones+collision)",
                              srt.DrawCalls.Count, srt.LodCount, srt.TotalTriangles,
                              srt.RenderableTriangles, srt.TotalTriangles - srt.RenderableTriangles)
            For i = 0 To srt.DrawCalls.Count - 1
                Dim dc = srt.DrawCalls(i)
                Console.WriteLine("    [{0}] lod{1} type{9} {2,-9} nv={3,-6} stride={4,-3} nrm={8,-8} tris={5,-6} dup={6,-5} degen={7} tex={10}",
                                  i, dc.Lod, dc.Kind.ToString(),
                                  dc.VertexCount, dc.Stride, dc.TriangleCount,
                                  dc.DuplicateVerts, dc.DegenerateTris,
                                  If(dc.HasNormals, "file", "derived"), dc.TypeId,
                                  If(dc.Declared, If(dc.DiffuseTexture = "", "(none)", dc.DiffuseTexture), "?"))
            Next
        Else
            Console.WriteLine("  NOT SOLVED: {0}", srt.Notes)
        End If

        ' textures
        If pkg IsNot Nothing Then
            Dim dir = Path.GetDirectoryName(name).Replace("\", "/")
            ' Load whatever the file actually names, one copy each. The two
            ' atlases below are only the fallback for assets whose render states
            ' could not be read.
            For Each dc In srt.DrawCalls
                If dc.DiffuseTexture <> "" AndAlso Not texByName.ContainsKey(dc.DiffuseTexture) Then
                    texByName(dc.DiffuseTexture) = LoadTex(dir, dc.DiffuseTexture)
                End If
            Next
            texFoliage = LoadTex(dir, srt.FoliageTexture)
            texBark = LoadTex(dir, srt.BarkTexture)
            texByName(If(srt.FoliageTexture, "")) = texFoliage
            texByName(If(srt.BarkTexture, "")) = texBark
        End If

        lodCount = Math.Max(1, srt.LodCount)
        If lodFilter >= lodCount Then lodFilter = 0

        For i = 0 To srt.DrawCalls.Count - 1
            Dim p = MakePart(srt.DrawCalls(i), PartColours(i Mod PartColours.Length))
            p.Lod = srt.DrawCalls(i).Lod
            p.Slot = srt.DrawCalls(i).Slot
            parts.Add(p)
        Next

        ' frame the model
        target = New Vector3((srt.BoundsMin(0) + srt.BoundsMax(0)) * 0.5F,
                             (srt.BoundsMin(1) + srt.BoundsMax(1)) * 0.5F,
                             (srt.BoundsMin(2) + srt.BoundsMax(2)) * 0.5F)
        Dim ext = Math.Max(srt.BoundsMax(0) - srt.BoundsMin(0),
                  Math.Max(srt.BoundsMax(1) - srt.BoundsMin(1),
                           srt.BoundsMax(2) - srt.BoundsMin(2)))
        dist = Math.Max(1.0F, ext * 1.25F)
        UpdateTitle()
    End Sub

    Private Function LoadTex(dir As String, leaf As String) As Integer
        If pkg Is Nothing OrElse leaf Is Nothing Then Return 0
        Dim full = dir & "/" & leaf
        Dim e = pkg.LookupHD(full)
        If e Is Nothing Then Return 0
        Return DdsLoader.FromBytes(pkg.Read(e), full)
    End Function

    Private Function MakePart(dc As SrtFile.DrawCall, tint As Vector3) As Part
        Const FLOATS = 8      ' position 3, texcoord 2, normal 3
        Dim interleaved(dc.VertexCount * FLOATS - 1) As Single
        For v = 0 To dc.VertexCount - 1
            interleaved(v * FLOATS + 0) = dc.Positions(v * 3 + 0)
            interleaved(v * FLOATS + 1) = dc.Positions(v * 3 + 1)
            interleaved(v * FLOATS + 2) = dc.Positions(v * 3 + 2)
            interleaved(v * FLOATS + 3) = dc.TexCoords(v * 2 + 0)
            interleaved(v * FLOATS + 4) = dc.TexCoords(v * 2 + 1)
            interleaved(v * FLOATS + 5) = dc.Normals(v * 3 + 0)
            interleaved(v * FLOATS + 6) = dc.Normals(v * 3 + 1)
            interleaved(v * FLOATS + 7) = dc.Normals(v * 3 + 2)
        Next

        ' Branch and trunk geometry tiles its bark texture, so its UVs run well
        ' outside 0..1. Foliage cards are cut from an atlas and stay inside it.
        Dim uvMax = 0.0F
        For k = 0 To dc.TexCoords.Length - 1
            uvMax = Math.Max(uvMax, Math.Abs(dc.TexCoords(k)))
        Next

        Dim p As New Part With {.IndexCount = dc.IndexCount, .Stride = dc.Stride,
                                .Tint = tint, .UsesBark = (dc.Kind = SrtFile.PartKind.Skin),
                                .Tex = If(dc.DiffuseTexture <> "" AndAlso texByName.ContainsKey(dc.DiffuseTexture),
                                          texByName(dc.DiffuseTexture), 0),
                                .FlatUV = dc.FlatUV, .Kind = dc.Kind,
                                .HasNormals = dc.HasNormals}
        GL.CreateVertexArrays(1, p.Vao)
        GL.CreateBuffers(1, p.Vbo)
        GL.CreateBuffers(1, p.Ebo)
        GL.NamedBufferData(p.Vbo, interleaved.Length * 4, interleaved, BufferUsageHint.StaticDraw)
        GL.NamedBufferData(p.Ebo, dc.Indices.Length * 4, dc.Indices, BufferUsageHint.StaticDraw)

        GL.VertexArrayVertexBuffer(p.Vao, 0, p.Vbo, IntPtr.Zero, FLOATS * 4)
        GL.VertexArrayElementBuffer(p.Vao, p.Ebo)
        GL.EnableVertexArrayAttrib(p.Vao, 0)
        GL.VertexArrayAttribFormat(p.Vao, 0, 3, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(p.Vao, 0, 0)
        GL.EnableVertexArrayAttrib(p.Vao, 1)
        GL.VertexArrayAttribFormat(p.Vao, 1, 2, VertexAttribType.Float, False, 12)
        GL.VertexArrayAttribBinding(p.Vao, 1, 0)
        GL.EnableVertexArrayAttrib(p.Vao, 2)
        GL.VertexArrayAttribFormat(p.Vao, 2, 3, VertexAttribType.Float, False, 20)
        GL.VertexArrayAttribBinding(p.Vao, 2, 0)
        Return p
    End Function

    ''' <summary>
    ''' Steps the solo selection through the parts of the LOD currently being
    ''' shown, so cycling does not walk the same geometry twice over in LOD1.
    ''' Returns -1 once it steps off the start.
    ''' </summary>
    Private Function NextSolo(current As Integer, dir As Integer) As Integer
        Dim eligible As New List(Of Integer)
        For i = 0 To parts.Count - 1
            If lodFilter < 0 OrElse parts(i).Lod = lodFilter Then eligible.Add(i)
        Next
        If eligible.Count = 0 Then Return -1

        Dim at = eligible.IndexOf(current)
        If current < 0 Then
            Return If(dir > 0, eligible(0), -1)
        End If
        If at < 0 Then Return eligible(0)

        Dim [next] = at + dir
        If [next] < 0 Then Return -1
        If [next] >= eligible.Count Then Return eligible(eligible.Count - 1)
        Return eligible([next])
    End Function

    Private Sub UpdateTitle()
        Dim shown As String
        If soloPart >= 0 AndAlso soloPart < parts.Count Then
            Dim sp = parts(soloPart)
            shown = String.Format("part {0} (lod{1} slot{2} {3}, stride {4}, {5})",
                                  soloPart, sp.Lod, sp.Slot, sp.Kind.ToString(), sp.Stride,
                                  If(sp.HasNormals, "normals", "no normals"))
        ElseIf lodFilter < 0 Then
            shown = "all LODs"
        Else
            shown = "LOD " & lodFilter & "/" & (lodCount - 1)
        End If
        Title = String.Format("SRT Viewer  [{0}/{1}]  {2}   {3} surface tris ({4} bone), {5} draw calls, {6}   {7}{8}{9}",
                              fileIndex + 1, files.Count, Path.GetFileName(files(fileIndex)),
                              srt.RenderableTriangles, srt.TotalTriangles - srt.RenderableTriangles,
                              srt.DrawCalls.Count, shown & "  [" & ShadeModes(shadeMode) & "]",
                              If(wireframe, "wire ", ""),
                              If(showBones, "bones ", ""),
                              If(srt.Solved, "", "UNSOLVED"))
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, Size.X, Size.Y)
    End Sub

    Protected Overrides Sub OnRenderFrame(args As FrameEventArgs)
        MyBase.OnRenderFrame(args)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        Dim eye As New Vector3(
            target.X + dist * CSng(Math.Cos(pitch) * Math.Sin(yaw)),
            target.Y + dist * CSng(Math.Sin(pitch)),
            target.Z + dist * CSng(Math.Cos(pitch) * Math.Cos(yaw)))
        Dim view = Matrix4.LookAt(eye, target, Vector3.UnitY)
        Dim proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(50.0F),
                        Math.Max(0.1F, CSng(Size.X) / Math.Max(1, Size.Y)), 0.02F, 4000.0F)
        Dim vp = view * proj

        GL.UseProgram(program)
        GL.UniformMatrix4(GL.GetUniformLocation(program, "uViewProj"), False, vp)

        ' Wireframe is for reading structure, so draw it unlit in light grey and
        ' let it show through itself rather than being occluded by nearer faces.
        If wireframe Then
            GL.Disable(EnableCap.DepthTest)
        Else
            GL.Enable(EnableCap.DepthTest)
        End If
        GL.Uniform1(GL.GetUniformLocation(program, "uMode"), If(wireframe, WIRE_MODE, shadeMode))
        GL.Uniform1(GL.GetUniformLocation(program, "uTex"), 0)
        GL.PolygonMode(MaterialFace.FrontAndBack, If(wireframe, PolygonMode.Line, PolygonMode.Fill))

        For i = 0 To parts.Count - 1
            Dim p = parts(i)
            If soloPart >= 0 Then
                If i <> soloPart Then Continue For
            Else
                If lodFilter >= 0 AndAlso p.Lod <> lodFilter Then Continue For
                If (p.Kind = SrtFile.PartKind.Bones OrElse p.Kind = SrtFile.PartKind.Collision) _
                   AndAlso Not showBones Then Continue For
            End If

            Dim tex = p.Tex
            If tex = 0 Then tex = If(p.UsesBark, texBark, texFoliage)
            If tex = 0 Then tex = texFoliage
            Dim textured = (Not wireframe) AndAlso shadeMode = 0 AndAlso tex <> 0
            GL.BindTextureUnit(0, If(textured, tex, texWhite))
            ' A part with no real UVs samples a single texel. Alpha testing it
            ' would discard the whole thing, which is what made these look blank.
            Dim doAlpha = (Not wireframe) AndAlso alphaTest AndAlso textured AndAlso
                          Not p.UsesBark AndAlso Not p.FlatUV
            GL.Uniform1(GL.GetUniformLocation(program, "uAlphaTest"), If(doAlpha, 1, 0))

            ' flat UV parts get their debug colour so they are visible at all
            Dim tint = If(textured AndAlso Not p.FlatUV, Vector3.One, p.Tint)
            If wireframe Then tint = WireColour
            GL.Uniform3(GL.GetUniformLocation(program, "uTint"), tint.X, tint.Y, tint.Z)
            GL.BindVertexArray(p.Vao)
            GL.DrawElements(PrimitiveType.Triangles, p.IndexCount, DrawElementsType.UnsignedInt, 0)
        Next

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill)
        GL.Enable(EnableCap.DepthTest)
        SwapBuffers()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseButtonEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = MouseButton.Left Then
            dragging = True
            lastMouse = New Vector2(MouseState.X, MouseState.Y)
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseButtonEventArgs)
        MyBase.OnMouseUp(e)
        If e.Button = MouseButton.Left Then dragging = False
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseMoveEventArgs)
        MyBase.OnMouseMove(e)
        If Not dragging Then Return
        Dim cur As New Vector2(e.X, e.Y)
        Dim d = cur - lastMouse
        lastMouse = cur
        yaw -= d.X * 0.01F
        pitch = Math.Clamp(pitch + d.Y * 0.01F, -1.5F, 1.5F)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseWheelEventArgs)
        MyBase.OnMouseWheel(e)
        dist = Math.Max(0.2F, dist * CSng(Math.Pow(0.9, e.OffsetY)))
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyboardKeyEventArgs)
        MyBase.OnKeyDown(e)
        Select Case e.Key
            Case Keys.Escape
                Close()
            Case Keys.W
                wireframe = Not wireframe
            Case Keys.T
                shadeMode = (shadeMode + 1) Mod ShadeModes.Length
            Case Keys.A
                alphaTest = Not alphaTest
            Case Keys.Right, Keys.PageDown
                fileIndex = (fileIndex + 1) Mod files.Count
                LoadCurrent()
            Case Keys.P
                If soloPart >= 0 AndAlso soloPart < srt.DrawCalls.Count Then
                    Dim dc = srt.DrawCalls(soloPart)
                    Dim delta = If(KeyboardState.IsKeyDown(Keys.LeftShift), -2, 2)
                    dc.PosOffset = Math.Max(0, Math.Min(dc.Stride - 6, dc.PosOffset + delta))
                    dc.DuplicateVerts = 0
                    srt.ReadBlock(dc)
                    Dim old = parts(soloPart)
                    GL.DeleteVertexArray(old.Vao) : GL.DeleteBuffer(old.Vbo) : GL.DeleteBuffer(old.Ebo)
                    Dim np = MakePart(dc, PartColours(soloPart Mod PartColours.Length))
                    np.Lod = old.Lod
                    parts(soloPart) = np
                    Console.WriteLine("  part {0}: pos@+{1}  dup={2} degen={3}/{4}",
                                      soloPart, dc.PosOffset, dc.DuplicateVerts,
                                      dc.DegenerateTris, dc.TriangleCount)
                End If
            Case Keys.B
                showBones = Not showBones
            Case Keys.Left, Keys.PageUp
                fileIndex = (fileIndex - 1 + files.Count) Mod files.Count
                LoadCurrent()
            Case Keys.Up
                soloPart = NextSolo(soloPart, 1)
            Case Keys.Down
                soloPart = NextSolo(soloPart, -1)
            Case Keys.L
                lodFilter += 1
                If lodFilter >= lodCount Then lodFilter = -1
            Case Keys.R
                LoadCurrent()
        End Select
        UpdateTitle()
    End Sub

    Protected Overrides Sub OnUnload()
        ClearParts()
        GL.DeleteProgram(program)
        MyBase.OnUnload()
    End Sub
End Class

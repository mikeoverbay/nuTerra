Imports System.IO
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Compile and link one GLSL program. That is all.
'''
''' NOT nuTerra's ShaderLoader, which is 687 lines because it carries an
''' #include system, a hot-reload watcher and the in-app shader IDE. This app
''' has three shaders with no includes between them, so the loader is thirty
''' lines and the ninety per cent that would be dead weight is absent.
'''
''' SHADERS VALIDATE AT RUNTIME ONLY - the house rule. A compile error here is
''' printed with the source line numbers and the program is left at 0, so the
''' frame draws nothing rather than drawing with a stale program and looking
''' like a geometry bug.
'''
''' Added 2026-09-15 by nuTerra work.
''' </summary>
Public Class BrainShader

    Public ReadOnly Property Id As Integer = 0
    Private ReadOnly uniforms As New Dictionary(Of String, Integer)
    Private ReadOnly tag As String

    Public Sub New(name As String)
        tag = name
        Dim dir = IO.Path.Combine(AppContext.BaseDirectory, "shaders")
        Dim vs = compile(IO.Path.Combine(dir, name & ".vert"), ShaderType.VertexShader)
        Dim fs = compile(IO.Path.Combine(dir, name & ".frag"), ShaderType.FragmentShader)
        If vs = 0 OrElse fs = 0 Then Return

        Dim p = GL.CreateProgram()
        GL.AttachShader(p, vs)
        GL.AttachShader(p, fs)
        GL.LinkProgram(p)

        Dim ok = 0
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then
            LogThis("brain: shader {0} failed to link:{1}{2}", name, vbLf, GL.GetProgramInfoLog(p))
            GL.DeleteProgram(p)
        Else
            _Id = p
        End If

        ' Detached and deleted either way: they are owned by the program now,
        ' and leaking them on the failure path is how a shader edit session
        ' runs out of objects.
        GL.DetachShader(p, vs) : GL.DetachShader(p, fs)
        GL.DeleteShader(vs) : GL.DeleteShader(fs)
    End Sub

    Private Function compile(path As String, kind As ShaderType) As Integer
        If Not File.Exists(path) Then
            LogThis("brain: shader source missing: {0}", path)
            Return 0
        End If
        Dim s = GL.CreateShader(kind)
        GL.ShaderSource(s, File.ReadAllText(path))
        GL.CompileShader(s)

        Dim ok = 0
        GL.GetShader(s, ShaderParameter.CompileStatus, ok)
        If ok <> 0 Then Return s

        ' The log names a line; print the file so the number means something.
        LogThis("brain: {0} failed to compile:{1}{2}", IO.Path.GetFileName(path), vbLf,
                GL.GetShaderInfoLog(s))
        GL.DeleteShader(s)
        Return 0
    End Function

    Public ReadOnly Property Ready As Boolean
        Get
            Return _Id <> 0
        End Get
    End Property

    Public Sub Use()
        GL.UseProgram(_Id)
    End Sub

    ''' <summary>Uniform location, looked up once. A miss is cached too - and
    ''' said ONCE, not every frame, because a per-frame complaint about a
    ''' uniform the optimiser removed is the exact noise this project has been
    ''' asked not to produce.</summary>
    Public Function Loc(name As String) As Integer
        Dim l = 0
        If uniforms.TryGetValue(name, l) Then Return l
        l = GL.GetUniformLocation(_Id, name)
        uniforms(name) = l
        If l < 0 Then LogThis("brain: {0} has no uniform '{1}'", tag, name)
        Return l
    End Function

    Public Sub SetMat4(name As String, ByRef m As Matrix4)
        Dim l = Loc(name)
        If l >= 0 Then GL.UniformMatrix4(l, False, m)
    End Sub

    Public Sub SetVec3(name As String, v As Vector3)
        Dim l = Loc(name)
        If l >= 0 Then GL.Uniform3(l, v.X, v.Y, v.Z)
    End Sub

    Public Sub SetFloat(name As String, x As Single)
        GL.Uniform1(GL.GetUniformLocation(id, name), x)
    End Sub

    Public Sub SetVec2(name As String, x As Single, y As Single)
        Dim l = Loc(name)
        If l >= 0 Then GL.Uniform2(l, x, y)
    End Sub

End Class

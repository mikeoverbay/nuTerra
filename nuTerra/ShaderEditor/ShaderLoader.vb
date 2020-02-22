Imports System.IO
Imports OpenTK.Graphics.OpenGL

Module ShaderLoader

    Public SHADER_PATHS() As String
#Region "shader_storage"

    Private Function get_shader(ByRef name As String) As String
        For Each n In SHADER_PATHS
            If n.Contains(name) Then
                Return n
            End If
        Next
        Return ""
    End Function

    Public Class Shader
        Private is_used As Boolean
        Private loaded As Boolean
        Private program As Integer
        Private uniforms As Dictionary(Of String, Integer)

        Public name As String
        Public fragment As String
        Public vertex As String
        Public geo As String
        Public compute As String

        Default ReadOnly Property Item(uniformName As String) As Integer
            Get
#If DEBUG Then
                If Not loaded Then
                    'Stop ' For debugging
                End If
                If Not is_used Then
                    'Stop ' For debugging
                End If
#End If
                If Not uniforms.ContainsKey(uniformName) Then
                    ' Some glsl compilers can optimize some uniforms if they are not used internally in the shader
                    ' Stop ' For debugging
                    Return -1
                End If
                Return uniforms(uniformName)
            End Get
        End Property

        Sub Use()
#If DEBUG Then
            If Not loaded Then
                'Stop ' For debugging
            End If
            If is_used Then
                'Stop ' For debugging
            End If
            is_used = True
#End If
            GL.UseProgram(program)
        End Sub

        Sub StopUse()
#If DEBUG Then
            If Not loaded Then
                'Stop ' For debugging
            End If
            If Not is_used Then
                Stop ' For debugging
            End If
            is_used = False
#End If
            GL.UseProgram(0)
        End Sub

        Sub UpdateShader()
            uniforms = New Dictionary(Of String, Integer)
            loaded = False
            is_used = False

            If program > 0 Then
                GL.UseProgram(0)
                GL.DeleteProgram(program)
                Dim status_code As Integer
                GL.GetShader(program, ShaderParameter.DeleteStatus, status_code)
                Debug.Assert(status_code = 0)
                GL.Finish()
            End If

            program = assemble_shader(vertex, geo, compute, fragment, name)

            If program = 0 Then
                ' Stop ' For debugging
                Return
            End If

            Dim numActiveUniforms As Integer
            GL.GetProgram(program, GetProgramParameterName.ActiveUniforms, numActiveUniforms)

            For i = 0 To numActiveUniforms - 1
                Dim uniformName As String = GL.GetActiveUniformName(program, i)
                If uniformName.StartsWith("gl_") Then
                    ' Skip internal glsl uniforms
                    Continue For
                End If
                Me.uniforms(uniformName) = i
            Next
            loaded = True
        End Sub

        Sub New(name As String)
            Me.name = name
            is_used = False
            loaded = False
            Dim failed_1, failed_2, failed_3, failed_4 As Boolean
            vertex = get_shader(String.Format("{0}.vert", name))
            If Not File.Exists(vertex) Then
                failed_1 = True
                vertex = Nothing
            End If

            geo = get_shader(String.Format("{0}.geom", name))
            If Not File.Exists(geo) Then
                failed_2 = True
                geo = Nothing
            End If

            compute = get_shader(String.Format("{0}.comp", name))
            If Not File.Exists(compute) Then
                failed_3 = True
                compute = Nothing
            End If

            fragment = get_shader(String.Format("{0}.frag", name))
            If Not File.Exists(fragment) Then
                failed_4 = True
                fragment = Nothing
            End If

            'check if our shader was found
            If failed_1 And failed_2 And failed_3 And failed_4 Then
                MsgBox(name + " was not found.", MsgBoxStyle.Exclamation, "Oh No!!")
                Return
            End If
            UpdateShader()
        End Sub
    End Class

    Public shaders As List(Of Shader)

    Public image2dShader As Shader
    Public image2dFlipShader As Shader
    Public rect2dShader As Shader
    Public cullShader As Shader
    Public coloredline2dShader As Shader
    Public colorOnlyShader As Shader
    Public CrossHairShader As Shader
    Public deferredShader As Shader
    Public gWriterShader As Shader
    Public MiniMapRingsShader As Shader
    Public modelShader As Shader
    Public normalShader As Shader
    Public normalOffsetShader As Shader
    Public SkyDomeShader As Shader
    Public testShader As Shader
    Public TerrainShader As Shader
    Public toLinearShader As Shader
#End Region

#Region "Compiler code"
    ''' <summary>
    ''' Finds the shaders in the folder and
    ''' calls the assemble_shader fucntion to build them
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub build_shaders()
        image2dShader = New Shader("image2d")
        image2dFlipShader = New Shader("image2dFlip")
        rect2dShader = New Shader("rect2d")
        cullShader = New Shader("cull")
        coloredline2dShader = New Shader("coloredLine2d")
        colorOnlyShader = New Shader("colorOnly")
        CrossHairShader = New Shader("CrossHair")
        deferredShader = New Shader("deferred")
        gWriterShader = New Shader("gWriter")
        MiniMapRingsShader = New Shader("MiniMapRings")
        modelShader = New Shader("model")
        normalShader = New Shader("normal")
        normalOffsetShader = New Shader("normalOffset")
        SkyDomeShader = New Shader("skyDome")
        testShader = New Shader("test")
        TerrainShader = New Shader("Terrain")
        toLinearShader = New Shader("toLinear")

        shaders = New List(Of Shader)
        shaders.Add(image2dShader)
        shaders.Add(image2dFlipShader)
        shaders.Add(rect2dShader)
        shaders.Add(cullShader)
        shaders.Add(coloredline2dShader)
        shaders.Add(colorOnlyShader)
        shaders.Add(CrossHairShader)
        shaders.Add(deferredShader)
        shaders.Add(gWriterShader)
        shaders.Add(MiniMapRingsShader)
        shaders.Add(modelShader)
        shaders.Add(normalShader)
        shaders.Add(normalOffsetShader)
        shaders.Add(SkyDomeShader)
        shaders.Add(testShader)
        shaders.Add(TerrainShader)
        shaders.Add(toLinearShader)
    End Sub

    Public Function assemble_shader(v As String,
                                    g As String,
                                    c As String,
                                    f As String,
                                    name As String) As Integer
        Dim status_code As Integer

        Dim program As Integer = GL.CreateProgram()
        If program = 0 Then
            Return 0
        End If

        ' Compile vertex shader
        Dim vertexObject As Integer = 0
        If v IsNot Nothing Then

            vertexObject = GL.CreateShader(ShaderType.VertexShader)

            Using vs_s As New StreamReader(v)
                Dim vs As String = vs_s.ReadToEnd()
                GL.ShaderSource(vertexObject, vs)
            End Using

            GL.CompileShader(vertexObject)

            ' Get & check status after compile
            GL.GetShader(vertexObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(vertexObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteProgram(program)
                gl_error(name + "_vertex didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If
        End If

        ' Compile fragment shader
        Dim fragmentObject As Integer = 0
        If f IsNot Nothing Then
            fragmentObject = GL.CreateShader(ShaderType.FragmentShader)

            Using fs_s As New StreamReader(f)
                Dim fs As String = fs_s.ReadToEnd
                GL.ShaderSource(fragmentObject, fs)
            End Using

            GL.CompileShader(fragmentObject)

            ' Get & check status after compile
            GL.GetShader(fragmentObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(fragmentObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(fragmentObject)
                GL.DeleteProgram(program)
                gl_error(name + "_fragment didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If
        End If

        ' Compile geom shader
        Dim geomObject As Integer = 0
        If g IsNot Nothing Then
            geomObject = GL.CreateShader(ShaderType.GeometryShader)

            Using gs_s As New StreamReader(g)
                Dim gs As String = gs_s.ReadToEnd()
                GL.ShaderSource(geomObject, gs)
            End Using

            GL.CompileShader(geomObject)

            ' Get & check status after compile
            GL.GetShader(geomObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(geomObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(fragmentObject)
                GL.DeleteShader(geomObject)
                GL.DeleteProgram(program)
                gl_error(name + "_geo didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If


            'If name.Contains("raytrace") Then
            '    GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryInputTypeExt, AssemblyProgramParameterArb), All.Triangles)
            '    GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryOutputTypeExt, AssemblyProgramParameterArb), All.LineStrip)
            '    GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryVerticesOutExt, AssemblyProgramParameterArb), 6)
            'End If

            If name.Contains("normal") Then
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryInputTypeExt, AssemblyProgramParameterArb), All.Triangles)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryOutputTypeExt, AssemblyProgramParameterArb), All.LineStrip)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryVerticesOutExt, AssemblyProgramParameterArb), 18)
            End If

        End If
        ' Compile Compute shader
        Dim computeObject As Integer = 0
        If c IsNot Nothing Then
            computeObject = GL.CreateShader(ShaderType.ComputeShader)

            Using cs_s As New StreamReader(c)
                Dim cs As String = cs_s.ReadToEnd()
                GL.ShaderSource(computeObject, cs)
            End Using

            GL.CompileShader(computeObject)

            ' Get & check status after compile
            GL.GetShader(computeObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(computeObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(fragmentObject)
                GL.DeleteShader(geomObject)
                GL.DeleteShader(computeObject)
                GL.DeleteProgram(program)
                gl_error(name + "_compute didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If

        End If
        ' attach shader objects
        If vertexObject Then
            GL.AttachShader(program, vertexObject)
        End If

        If geomObject Then
            GL.AttachShader(program, geomObject)
        End If

        If computeObject Then
            GL.AttachShader(program, computeObject)
        End If

        If fragmentObject Then
            GL.AttachShader(program, fragmentObject)
        End If

        If name = "cull" Then
            Dim vars() = {"CulledPosition"}
            GL.TransformFeedbackVaryings(program, 1, vars, TransformFeedbackMode.InterleavedAttribs)
        End If

        ' link program
        GL.LinkProgram(program)

        ' Get & check status after link
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, status_code)
        If status_code = 0 Then
            Dim info = GL.GetProgramInfoLog(program)
            gl_error(name + " Would not link!" + vbCrLf + info.ToString)
        End If

        ' detach shader objects
        If vertexObject Then
            GL.DetachShader(program, vertexObject)
        End If
        If geomObject Then
            GL.DetachShader(program, geomObject)
        End If
        If computeObject Then
            GL.DetachShader(program, computeObject)
        End If
        If fragmentObject Then
            GL.DetachShader(program, fragmentObject)
        End If

        ' delete shader objects
        If vertexObject Then
            GL.DeleteShader(vertexObject)
        End If
        If geomObject Then
            GL.DeleteShader(geomObject)
        End If
        If computeObject Then
            GL.DeleteShader(computeObject)
        End If
        If fragmentObject Then
            GL.DeleteShader(fragmentObject)
        End If

        Return program
    End Function

    Public Sub gl_error(s As String)
        s = s.Replace(vbLf, vbCrLf)
        s.Replace("0(", vbCrLf + "(")
        frmShaderError.Show()
        frmShaderError.er_tb.Text += s
        frmShaderError.er_tb.SelectionLength = 0
        frmShaderError.er_tb.SelectionStart = 0
    End Sub

#End Region
End Module

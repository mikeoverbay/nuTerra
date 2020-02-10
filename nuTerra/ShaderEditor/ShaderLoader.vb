Imports System.IO
Imports OpenTK.Graphics.OpenGL

Module ShaderLoader

#Region "shader_storage"

    Public Class Shader
        Private is_used As Boolean
        Private loaded As Boolean
        Private program As Integer
        Private uniforms As Dictionary(Of String, Integer)

        Public name As String
        Public has_geo As Boolean
        Public fragment As String
        Public vertex As String
        Public geo As String

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

            program = assemble_shader(vertex, geo, fragment, name, has_geo)

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

            vertex = String.Format("{0}\shaders\{1}_vertex.glsl", Application.StartupPath, name)
            fragment = String.Format("{0}\shaders\{1}_fragment.glsl", Application.StartupPath, name)
            geo = String.Format("{0}\shaders\{1}_geo.glsl", Application.StartupPath, name)

            Me.has_geo = File.Exists(geo)
            If Not File.Exists(vertex) Or Not File.Exists(fragment) Then
                Return
            End If

            UpdateShader()
        End Sub
    End Class

    Public shaders As List(Of Shader)

    Public image2dShader As Shader
    Public rect2dShader As Shader
    'Public basicShader As Shader
    Public colorOnlyShader As Shader
    Public CrossHairShader As Shader
    Public deferredShader As Shader
    Public gWriterShader As Shader
    Public modelShader As Shader
    Public normalShader As Shader
    Public normalOffsetShader As Shader
    Public toLinearShader As Shader
    Public wireframeShader As Shader

#End Region

#Region "Compiler code"
    ''' <summary>
    ''' Finds the shaders in the folder and
    ''' calls the assemble_shader fucntion to build them
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub build_shaders()
        image2dShader = New Shader("image2d")
        rect2dShader = New Shader("rect2d")
        'basicShader = New Shader("basic")
        colorOnlyShader = New Shader("colorOnly")
        CrossHairShader = New Shader("CrossHair")
        deferredShader = New Shader("deferred")
        gWriterShader = New Shader("gWriter")
        modelShader = New Shader("MDL")
        normalShader = New Shader("normal")
        normalOffsetShader = New Shader("normalOffset")
        toLinearShader = New Shader("toLinear")
        wireframeShader = New Shader("wireframe")

        shaders = New List(Of Shader)
        shaders.Add(image2dShader)
        shaders.Add(rect2dShader)
        'shaders.Add(basicShader)
        shaders.Add(colorOnlyShader)
        shaders.Add(CrossHairShader)
        shaders.Add(deferredShader)
        shaders.Add(gWriterShader)
        shaders.Add(modelShader)
        shaders.Add(normalShader)
        shaders.Add(normalOffsetShader)
        shaders.Add(toLinearShader)
        shaders.Add(wireframeShader)
    End Sub

    Public Function assemble_shader(v As String, g As String, f As String, name As String, has_geo As Boolean) As Integer
        Dim status_code As Integer

        Dim program As Integer = GL.CreateProgram()
        If program = 0 Then
            Return 0
        End If

        ' Compile vertex shader
        Dim vertexObject As Integer = GL.CreateShader(ShaderType.VertexShader)

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

        ' Compile fragment shader
        Dim fragmentObject As Integer = GL.CreateShader(ShaderType.FragmentShader)

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

        ' Compile geom shader
        Dim geomObject As Integer
        If has_geo Then
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

            If name.Contains("raytrace") Then
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryInputTypeExt, AssemblyProgramParameterArb), All.Triangles)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryOutputTypeExt, AssemblyProgramParameterArb), All.LineStrip)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryVerticesOutExt, AssemblyProgramParameterArb), 6)
            End If

            If name.Contains("normal") Then
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryInputTypeExt, AssemblyProgramParameterArb), All.Triangles)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryOutputTypeExt, AssemblyProgramParameterArb), All.LineStrip)
                GL.Ext.ProgramParameter(program, DirectCast(ExtGeometryShader4.GeometryVerticesOutExt, AssemblyProgramParameterArb), 18)
            End If
        End If

        ' attach shader objects
        GL.AttachShader(program, fragmentObject)
        If has_geo Then
            GL.AttachShader(program, geomObject)
        End If
        GL.AttachShader(program, vertexObject)

        ' link program
        GL.LinkProgram(program)

        ' Get & check status after link
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, status_code)
        If status_code = 0 Then
            Dim info = GL.GetProgramInfoLog(program)
            gl_error(name + " Would not link!" + vbCrLf + info.ToString)
        End If

        ' detach shader objects
        GL.DetachShader(program, fragmentObject)
        If has_geo Then
            GL.DetachShader(program, geomObject)
        End If
        GL.DetachShader(program, vertexObject)

        ' delete shader objects
        GL.DeleteShader(fragmentObject)
        If has_geo Then
            GL.DeleteShader(geomObject)
        End If
        GL.DeleteShader(vertexObject)

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

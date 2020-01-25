#Region "imports"
Imports System
Imports System.Text
Imports System.String
Imports System.IO
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
#End Region
Module ShaderLoader

#Region "shader_storage"

    Public shaders As New shaders__
    Public Structure shaders__
        Public shader() As shaders_
        Public Function f(ByVal name As String) As Integer
            For Each s In shader
                If s.shader_name = name Then
                    Return s.shader_id
                End If
            Next
            Return Nothing
        End Function
    End Structure

    Public Structure shaders_
        Public fragment As String
        Public vertex As String
        Public geo As String
        Public shader_name As String
        Public shader_id As Integer
        Public has_geo As Boolean
        Public Sub set_call_id(ByVal id As Integer)
            Try
                CallByName(shader_list, Me.shader_name, CallType.Set, Me.shader_id)

            Catch ex As Exception
                MsgBox("missing member from shader_list:" + Me.shader_name, MsgBoxStyle.Exclamation, "Oops!")
                End
            End Try
        End Sub
    End Structure
#End Region

    Public shader_list As New Shader_list_
    Public Class Shader_list_
        Public basic_shader As Integer
    End Class

    '----------------------------------------------------------------------------
    Public basic_text_id As Integer
    Private Sub set_basic_varaibles()
        basic_text_id = GL.GetUniformLocation(shader_list.basic_shader, "colorMap")
    End Sub
    Public Sub set_shader_variables()
        set_basic_varaibles()
    End Sub
    Public GL_TRUE As Integer = 1
    Public GL_FALSE As Integer = 0

Public Function get_GL_error_string(ByVal e As ErrorCode) As String
        Return [Enum].GetName(GetType(ErrorCode), e)
    End Function

    Public Sub build_shaders()
        'I'm tired of all the work every time I add a shader.
        'So... Im going to automate the process.. Hey.. its a computer for fucks sake!
        '
        'It is important that there be ONLY shaders in the \shaders folder.
        'There can be only one '_' character in the names and the leading has to match the shader_list name!
        '
        Dim f_list() As String = IO.Directory.GetFiles(Application.StartupPath + "\shaders\", "*fragment.glsl")
        Dim v_list() As String = IO.Directory.GetFiles(Application.StartupPath + "\shaders\", "*vertex.glsl")
        Dim g_list() As String = IO.Directory.GetFiles(Application.StartupPath + "\shaders\", "*geo.glsl")
        Array.Sort(f_list)
        Array.Sort(v_list)
        Array.Sort(g_list)
        ReDim shaders.shader(f_list.Length - 1)
        With shaders
            'we go through and find the shaders based on the second part of their names.
            For i = 0 To f_list.Length - 1
                .shader(i) = New shaders_
                With .shader(i)
                    Dim fn As String = Path.GetFileNameWithoutExtension(f_list(i))
                    Dim ar = fn.Split("_")
                    .shader_name = ar(0) + "_shader"
                    .fragment = f_list(i)
                    .vertex = v_list(i)
                    .geo = ""
                    For Each g In g_list ' find matching geo if there is one.. usually there wont be
                        If g.Contains(ar(0)) Then
                            .geo = g
                            .has_geo = True ' found a matching geo so we need to set this true
                        End If
                    Next
                    .shader_id = -1
                    .set_call_id(-1)
                End With
            Next

        End With
        Dim fs As String
        Dim vs As String
        Dim gs As String

        For i = 0 To shaders.shader.Length - 1
            With shaders.shader(i)
                vs = .vertex
                fs = .fragment
                gs = .geo
                Dim id = assemble_shader(vs, gs, fs, .shader_id, .shader_name, .has_geo)
                .set_call_id(id)
                .shader_id = id

                'Debug.WriteLine(.shader_name + "  Id:" + .shader_id.ToString)
            End With
        Next

    End Sub
    Public Function assemble_shader(v As String, g As String, f As String, ByRef shader As Integer, ByRef name As String, ByRef has_geo As Boolean) As Integer
        Dim vs(1) As String
        Dim gs(1) As String
        Dim fs(1) As String
        Dim vertexObject As Integer
        Dim geoObject As Integer
        Dim fragmentObject As Integer
        Dim status_code As Integer
        Dim info As String = ""
        Dim info_l As Integer

        If shader > 0 Then
            GL.UseProgram(0)
            GL.DeleteProgram(shader)
            GL.GetShader(shader, ShaderParameter.DeleteStatus, status_code)
            GL.Finish()
        End If

        Dim e = GL.GetError
        If e <> 0 Then
            Dim s = get_GL_error_string(e)
            Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
            'MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
        End If
        'have a hard time with files remaining open.. hope this fixes it! (yep.. it did)
        Using vs_s As New StreamReader(v)
            vs(0) = vs_s.ReadToEnd
            vs_s.Close()
            vs_s.Dispose()
        End Using
        Using fs_s As New StreamReader(f)
            fs(0) = fs_s.ReadToEnd
            fs_s.Close()
            fs_s.Dispose()
        End Using
        If has_geo Then
            Using gs_s As New StreamReader(g)
                gs(0) = gs_s.ReadToEnd
                gs_s.Close()
                gs_s.Dispose()
            End Using
        End If


        vertexObject = GL.CreateShader(ShaderType.VertexShader)
        fragmentObject = GL.CreateShader(ShaderType.FragmentShader)
        '--------------------------------------------------------------------
        shader = GL.CreateProgram()

        ' Compile vertex shader
        GL.ShaderSource(vertexObject, 1, vs, vs(0).Length)
        GL.CompileShader(vertexObject)
        GL.GetShaderInfoLog(vertexObject, 8192, info_l, info)
        GL.GetShader(vertexObject, ShaderParameter.CompileStatus, status_code)
        If Not status_code = GL_TRUE Then
            GL.DeleteShader(vertexObject)
            gl_error(name + "_vertex didn't compile!" + vbCrLf + info.ToString)
            'Return
        End If

        e = GL.GetError
        If e <> 0 Then
            Dim s = get_GL_error_string(e)
            Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
            MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
        End If

        If has_geo Then
            'geo
            geoObject = GL.CreateShader(ShaderType.GeometryShader)
            GL.ShaderSource(geoObject, 1, gs, gs(0).Length)
            GL.CompileShader(geoObject)
            GL.GetShaderInfoLog(geoObject, 8192, info_l, info)
            GL.GetShader(geoObject, ShaderParameter.CompileStatus, status_code)
            If Not status_code = GL_TRUE Then
                GL.DeleteShader(geoObject)
                gl_error(name + "_geo didn't compile!" + vbCrLf + info.ToString)
                'Return
            End If
            e = GL.GetError
            If e <> 0 Then
                Dim s = get_GL_error_string(e)
                Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
                MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
            End If
            If name.Contains("raytrace") Then

                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryInputType, All.Triangles)
                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryOutputType, All.LineStrip)
                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryVerticesOut, 6)
            End If
            If name.Contains("normal") Then
                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryInputType, All.Triangles)
                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryOutputType, All.LineStrip)
                GL.Ext.ProgramParameter(shader, AssemblyProgramParameterArb.GeometryVerticesOut, 18)
            End If

            e = GL.GetError
            If e <> 0 Then
                Dim s = get_GL_error_string(e)
                Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
                MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
            End If

        End If

        ' Compile fragment shader

        GL.ShaderSource(fragmentObject, 1, fs, fs(0).Length)
        GL.CompileShader(fragmentObject)
        GL.GetShaderInfoLog(fragmentObject, 8192, info_l, info)
        GL.GetShader(fragmentObject, ShaderParameter.CompileStatus, status_code)

        If Not status_code = GL_TRUE Then
            GL.DeleteShader(fragmentObject)
            gl_error(name + "_fragment didn't compile!" + vbCrLf + info.ToString)
            'Return
        End If
        e = GL.GetError
        If e <> 0 Then
            Dim s = get_GL_error_string(e)
            Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
            MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
        End If

        'attach shader objects
        GL.AttachShader(shader, fragmentObject)
        If has_geo Then
            GL.AttachShader(shader, geoObject)
        End If
        GL.AttachShader(shader, vertexObject)

        'link program
        GL.LinkProgram(shader)

        ' detach shader objects
        GL.DetachShader(shader, fragmentObject)
        If has_geo Then
            GL.DetachShader(shader, geoObject)
        End If
        GL.DetachShader(shader, vertexObject)

        e = GL.GetError
        If e <> 0 Then
            Dim s = get_GL_error_string(e)
            Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
            MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
        End If

        'no idea how to get link status in OpenTK :(
        GL.GetProgram(shader, GetProgramParameterName.LinkStatus, status_code)
        If Not status_code = GL_TRUE Then
            GL.DeleteShader(fragmentObject)
            gl_error(name + " Would not link!" + vbCrLf + info.ToString)
        End If
        'delete shader objects
        GL.DeleteShader(fragmentObject)
        GL.GetShader(fragmentObject, ShaderParameter.CompileStatus, status_code)
        If has_geo Then
            GL.DeleteShader(geoObject)
            GL.GetShader(geoObject, ShaderParameter.CompileStatus, status_code)
        End If
        GL.DeleteShader(vertexObject)
        GL.GetShader(vertexObject, ShaderParameter.CompileStatus, status_code)
        e = GL.GetError
        If e <> 0 Then
            'aways throws a error after deletion even though the status shows them as deleted.. ????
            Dim s = get_GL_error_string(e)
            Dim ms As String = System.Reflection.MethodBase.GetCurrentMethod().Name
            'MsgBox("Function: " + ms + vbCrLf + "Error! " + s, MsgBoxStyle.Exclamation, "OpenGL Issue")
        End If
        vs(0) = Nothing
        fs(0) = Nothing
        If has_geo Then
            gs(0) = Nothing
        End If
        GC.Collect()
        GC.WaitForFullGCComplete()

        Return shader
    End Function

    Public Sub gl_error(s As String)
        s = s.Replace(vbLf, vbCrLf)
        s.Replace("0(", vbCrLf + "(")
        frmShaderError.Show()
        frmShaderError.er_tb.Text += s
        frmShaderError.er_tb.SelectionLength = 0
        frmShaderError.er_tb.SelectionStart = 0
    End Sub
End Module

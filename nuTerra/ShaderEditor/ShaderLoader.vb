﻿Imports System.IO
Imports OpenTK.Graphics.OpenGL

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
        'Keep these in alphabetic order :)
        Public basic_shader As Integer
        Public colorOnly_shader As Integer
        Public Deferred_shader As Integer
        Public gWriter_shader As Integer
        Public MDL_shader As Integer
        Public normalOffset_shader As Integer
        Public testList_shader As Integer
        Public toLinear_shader As Integer
    End Class

    'template to copy and add new uniforms
    '----------------------------------------------------------------------------
    Public template_text_id As Integer
    Private Sub set_template_varaibles()
        template_text_id = GL.GetUniformLocation(Nothing, "colorMap")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public basic_text_id As Integer
    Private Sub set_basic_varaibles()
        basic_text_id = GL.GetUniformLocation(shader_list.basic_shader, "colorMap")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public colorOnly_color_id, colorOnly_ModelMatrix_id, colorOnly_PrjMatrix_id As Integer
    Private Sub set_colorOnly_varaibles()
        colorOnly_color_id = GL.GetUniformLocation(shader_list.colorOnly_shader, "color")
        colorOnly_ModelMatrix_id = GL.GetUniformLocation(shader_list.colorOnly_shader, "ModelMatrix")
        colorOnly_PrjMatrix_id = GL.GetUniformLocation(shader_list.colorOnly_shader, "ProjectionMatrix")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public deferred_gColor_id, deferred_gNormal_id, deferred_gGMF_id As Integer
    Public deferred_gDepth_id, deferred_lightPos, deferred_ModelMatrix As Integer
    Public deferred_ProjectionMatrix, deferred_ViewPort As Integer
    Private Sub set_deferred_varaibles()
        deferred_gColor_id = GL.GetUniformLocation(shader_list.Deferred_shader, "gColor")
        deferred_gNormal_id = GL.GetUniformLocation(shader_list.Deferred_shader, "gNormal")
        deferred_gGMF_id = GL.GetUniformLocation(shader_list.Deferred_shader, "gGMF")
        deferred_gDepth_id = GL.GetUniformLocation(shader_list.Deferred_shader, "gDepth")

        deferred_lightPos = GL.GetUniformLocation(shader_list.Deferred_shader, "LightPos")
        deferred_ModelMatrix = GL.GetUniformLocation(shader_list.Deferred_shader, "ModelMatrix")
        deferred_ProjectionMatrix = GL.GetUniformLocation(shader_list.Deferred_shader, "ProjectionMatrix")
        deferred_ViewPort = GL.GetUniformLocation(shader_list.Deferred_shader, "ProjectionMatrix")
        deferred_ViewPort = GL.GetUniformLocation(shader_list.Deferred_shader, "viewport")

    End Sub
    '----------------------------------------------------------------------------\

    '----------------------------------------------------------------------------
    Public gWriter_textureMap_id, gWriter_normalMap_id, gWriter_GMF_id, gWriter_ModelMatrix_id As Integer
    Public gWriter_WorldNormal_id As Integer
    Public gWriter_ProjectionMatrix_id, gWriter_nMap_type_id As Integer
    Private Sub set_gWriter_varaibles()
        gWriter_textureMap_id = GL.GetUniformLocation(shader_list.gWriter_shader, "colorMap")
        gWriter_normalMap_id = GL.GetUniformLocation(shader_list.gWriter_shader, "normalMap")
        gWriter_GMF_id = GL.GetUniformLocation(shader_list.gWriter_shader, "GMF_Map")
        gWriter_ModelMatrix_id = GL.GetUniformLocation(shader_list.gWriter_shader, "ModelMatrix")
        gWriter_ProjectionMatrix_id = GL.GetUniformLocation(shader_list.gWriter_shader, "ProjectionMatrix")
        gWriter_WorldNormal_id = GL.GetUniformLocation(shader_list.gWriter_shader, "modelNormalMatrix")
        gWriter_nMap_type_id = GL.GetUniformLocation(shader_list.gWriter_shader, "nMap_type")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public MDL_textureMap_id, MDL_normalMap_id, MDL_GMF_id As Integer
    Public MDL_modelMatrix_id, MDL_modelNormalMatrix_id, MDL_modelViewProjection_id As Integer
    Public MDL_nMap_type_id As Integer
    Public MDL_attribute_names() = {"vertexPosition", "vertexNormal", "vertexTexCoord1", "vertexTangent", "vertexBinormal", "vertexTexCoord2"}
    Public MDL_attribute_locations(MDL_attribute_names.Length - 1) As Integer
    Private Sub set_MDL_varaibles()
        For i = 0 To MDL_attribute_names.Length - 1
            MDL_attribute_locations(i) = GL.GetAttribLocation(shader_list.MDL_shader, MDL_attribute_names(i))
        Next
        MDL_textureMap_id = GL.GetUniformLocation(shader_list.MDL_shader, "colorMap")
        MDL_normalMap_id = GL.GetUniformLocation(shader_list.MDL_shader, "normalMap")
        MDL_GMF_id = GL.GetUniformLocation(shader_list.MDL_shader, "GMF_Map")
        MDL_modelMatrix_id = GL.GetUniformLocation(shader_list.MDL_shader, "modelMatrix")
        MDL_modelNormalMatrix_id = GL.GetUniformLocation(shader_list.MDL_shader, "modelNormalMatrix")
        MDL_modelViewProjection_id = GL.GetUniformLocation(shader_list.MDL_shader, "modelViewProjection")
        MDL_nMap_type_id = GL.GetUniformLocation(shader_list.MDL_shader, "nMap_type")
    End Sub
    '----------------------------------------------------------------------------

    Public testList_textureMap_id, testList_normalMap_id, testList_GMF_id As Integer
    Public testList_modelMatrix_id, testList_modelNormalMatrix_id, testList_modelViewProjection_id As Integer
    Public testList_nMap_type_id, testList_has_uv2_id As Integer
    'Public testList_attribute_names() = {"vertex_in", "normal_in", "uv1_in", "tangent_in", "Binormal_in", "uv2_in"}
    'Public testList_attribute_locations(testList_attribute_names.Length - 1)
    Private Sub set_testList_varaibles()
        'For i = 0 To testList_attribute_names.Length - 1
        '    testList_attribute_locations(i) = GL.GetAttribLocation(shader_list.testList_shader, testList_attribute_names(i))
        'Next
        testList_textureMap_id = GL.GetUniformLocation(shader_list.testList_shader, "colorMap")
        testList_normalMap_id = GL.GetUniformLocation(shader_list.testList_shader, "normalMap")
        testList_GMF_id = GL.GetUniformLocation(shader_list.testList_shader, "GMF_Map")
        testList_modelMatrix_id = GL.GetUniformLocation(shader_list.testList_shader, "modelMatrix")
        testList_modelNormalMatrix_id = GL.GetUniformLocation(shader_list.testList_shader, "modelNormalMatrix")
        testList_modelViewProjection_id = GL.GetUniformLocation(shader_list.testList_shader, "modelViewProjection")
        testList_nMap_type_id = GL.GetUniformLocation(shader_list.testList_shader, "nMap_type")
        testList_has_uv2_id = GL.GetUniformLocation(shader_list.testList_shader, "has_uv2")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public normalOffset_text_id As Integer
    Private Sub set_normalOffset_varaibles()
        normalOffset_text_id = GL.GetUniformLocation(shader_list.normalOffset_shader, "normalMap")
    End Sub
    '----------------------------------------------------------------------------

    '----------------------------------------------------------------------------
    Public toLinear_text_id As Integer
    Private Sub set_toLinear_varaibles()
        toLinear_text_id = GL.GetUniformLocation(shader_list.toLinear_shader, "depthMap")
    End Sub
    '----------------------------------------------------------------------------


    ''' <summary>
    ''' This sub calls all the subs to set each shaders uniforms.
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub set_uniform_variables()
        'Keep these in alphabetic order :)
        set_basic_varaibles()
        set_colorOnly_varaibles()
        set_deferred_varaibles()
        set_gWriter_varaibles()
        set_MDL_varaibles()
        set_normalOffset_varaibles()
        set_testList_varaibles()
        set_toLinear_varaibles()
    End Sub


    Public GL_TRUE As Integer = 1
    Public GL_FALSE As Integer = 0

    Public Function get_GL_error_string(ByVal e As ErrorCode) As String
        Return [Enum].GetName(GetType(ErrorCode), e)
    End Function
#Region "Compiler code"
    ''' <summary>
    ''' Finds the shaders in the folder and
    ''' calls the assemble_shader fucntion to build them
    ''' </summary>
    ''' <remarks></remarks>
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
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryInputType, ProgramParameterName), All.Triangles)
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryOutputType, ProgramParameterName), All.LineStrip)
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryVerticesOut, ProgramParameterName), 6)
            End If

            If name.Contains("normal") Then
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryInputType, ProgramParameterName), All.Triangles)
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryOutputType, ProgramParameterName), All.LineStrip)
                GL.ProgramParameter(shader, DirectCast(ProgramParameter.GeometryVerticesOut, ProgramParameterName), 18)
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

        ' link program
        GL.LinkProgram(shader)

        GL.GetProgram(shader, GetProgramParameterName.LinkStatus, status_code)
        If status_code = GL_FALSE Then
            gl_error(name + " Would not link!" + vbCrLf + info.ToString)
        End If

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

        'delete shader objects
        GL.DeleteShader(fragmentObject)
        If has_geo Then
            GL.DeleteShader(geoObject)
        End If
        GL.DeleteShader(vertexObject)
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

#End Region
End Module

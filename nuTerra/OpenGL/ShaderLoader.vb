﻿Imports System.IO
Imports OpenTK.Graphics.OpenGL

Module ShaderLoader
    Private incPaths() As String = {"/"}
    Private incPathLengths() As Integer = {1}

    Private watcher As FileSystemWatcher

    Public SHADER_PATHS() As String
#Region "shader_storage"

    Private Function get_shader(ByRef name As String) As String
        For Each n In SHADER_PATHS
            If Path.GetFileName(n).ToLower = name.ToLower Then
                Return n
            End If
        Next
        Return ""
    End Function

    Public Class Shader
        Private defines As New Dictionary(Of String, String)
        Private is_used As Boolean
        Private loaded As Boolean
        Public program As Integer
        Private uniforms As Dictionary(Of String, Integer)

        Public name As String
        Public fragment As String
        Public tc As String
        Public te As String
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

        Sub SetDefine(key As String, Optional value As String = "")
            defines(key) = value
            UpdateShader()
        End Sub

        Sub UnsetDefine(key As String)
            defines.Remove(key)
            UpdateShader()
        End Sub

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
                'Stop ' For debugging
            End If
            is_used = False
#End If
            ' GL.UseProgram(0)
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
            End If

            program = assemble_shader(vertex, tc, te, geo, compute, fragment, name, defines)

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
                Me.uniforms(uniformName) = GL.GetUniformLocation(program, uniformName)
            Next
            loaded = True
        End Sub

        Sub New(name As String)
            shaders.Add(Me)

            Me.name = name
            is_used = False
            loaded = False

            vertex = get_shader(String.Format("{0}.vert", name))
            If Not File.Exists(vertex) Then
                vertex = Nothing
            End If

            tc = get_shader(String.Format("{0}.tesc", name))
            If Not File.Exists(tc) Then
                tc = Nothing
            End If

            te = get_shader(String.Format("{0}.tese", name))
            If Not File.Exists(te) Then
                te = Nothing
            End If

            geo = get_shader(String.Format("{0}.geom", name))
            If Not File.Exists(geo) Then
                geo = Nothing
            End If

            compute = get_shader(String.Format("{0}.comp", name))
            If Not File.Exists(compute) Then
                compute = Nothing
            End If

            fragment = get_shader(String.Format("{0}.frag", name))
            If Not File.Exists(fragment) Then
                fragment = Nothing
            End If

            UpdateShader()
        End Sub
    End Class

    Public shaders As New List(Of Shader)
    Public BaseRingProjector As Shader
    Public BaseRingProjectorDeferred As Shader
    Public boxShader As Shader
    Public boxDecalsColorShader As Shader
    Public cullShader As Shader
    Public cullLodClearShader As Shader
    Public cullRasterShader As Shader
    Public cullInvalidateShader As Shader
    Public colorCorrectShader As Shader
    Public coloredline2dShader As Shader
    Public colorMaskShader As Shader
    Public colorOnlyShader As Shader
    Public DecalProject As Shader
    Public deferredShader As Shader
    Public DeferredFogShader As Shader
    Public FF_BillboardShader As Shader
    Public FXAAShader As Shader
    Public frustumShader As Shader
    Public glassPassShader As Shader
    Public gWriterShader As Shader
    Public image2dArrayShader As Shader
    Public image2dFlipShader As Shader
    Public image2dShader As Shader
    Public MegaMixerShader As Shader
    Public MiniMapRingsShader As Shader
    Public mDepthWriteShader As Shader
    Public modelShader As Shader
    Public modelGlassShader As Shader
    Public ModelViewerShader As Shader
    Public normalShader As Shader
    Public normalOffsetShader As Shader
    Public outlandShader As Shader
    Public outlandNormalsShader As Shader
    Public rect2dShader As Shader
    Public SkyDomeShader As Shader
    Public t_mixerShader As Shader
    Public TerrainGrids As Shader
    Public TerrainNormals As Shader
    Public TerrainNormalsHQ As Shader
    Public TerrainHQShader As Shader
    Public TerrainLQShader As Shader
    Public TerrainVTMIPShader As Shader
    Public toLinearShader As Shader
    Public TextRenderShader As Shader
    'particle shaders
    Public explode_type_1_shader As Shader
    'shadow shaders
    Public terrainDepthShader As Shader
    Public Terrain_light As Shader
    Public mDepthWrite_light As Shader
    Public imguiShader As Shader
#End Region

#Region "Compiler code"
    ''' <summary>
    ''' Finds the shaders in the folder and
    ''' calls the assemble_shader fucntion to build them
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub build_shaders()
        'get directory of all shader files
        SHADER_PATHS = Directory.GetFiles(Application.StartupPath + "\shaders\", "*.*", SearchOption.AllDirectories)

        If File.Exists("shaders\common.h") Then
            Dim code = File.ReadAllText("shaders\common.h")
            Dim name = "/common.h"
            GL.Arb.NamedString(ArbShadingLanguageInclude.ShaderIncludeArb, name.Length, name, code.Length, code)
        End If

        'Try and keep these in alphabetical order 
        BaseRingProjector = New Shader("BaseRingProjector")
        BaseRingProjectorDeferred = New Shader("BaseRingProjectorDeferred")
        boxShader = New Shader("box")
        boxDecalsColorShader = New Shader("box_decals_color")
        cullShader = New Shader("cull")
        cullLodClearShader = New Shader("cullLodClear")
        cullRasterShader = New Shader("cull-raster")
        cullInvalidateShader = New Shader("cull-invalidate")
        colorCorrectShader = New Shader("colorCorrect")
        coloredline2dShader = New Shader("coloredLine2d")
        colorMaskShader = New Shader("ColorMask")
        'unused: colorOnlyShader = New Shader("colorOnly")
        DecalProject = New Shader("DecalProject")
        DeferredFogShader = New Shader("DeferredFog")
        deferredShader = New Shader("deferred")
        FF_BillboardShader = New Shader("FF_billboard")
        FXAAShader = New Shader("FXAA")
        frustumShader = New Shader("frustum")
        image2dArrayShader = New Shader("image2dArray")
        image2dFlipShader = New Shader("image2dFlip")
        image2dShader = New Shader("image2d")
        glassPassShader = New Shader("glassPass")
        'unused: gWriterShader = New Shader("gWriter")
        MegaMixerShader = New Shader("MegaMixer")
        MiniMapRingsShader = New Shader("MiniMapRings")
        mDepthWriteShader = New Shader("mDepthWrite")
        modelShader = New Shader("model")
        modelGlassShader = New Shader("modelGlass")
        ModelViewerShader = New Shader("ModelViewer")
        normalShader = New Shader("normal")
        normalOffsetShader = New Shader("normalOffset")
        outlandShader = New Shader("outland")
        outlandNormalsShader = New Shader("outlandNormals")
        rect2dShader = New Shader("rect2d")
        SkyDomeShader = New Shader("skyDome")
        t_mixerShader = New Shader("t_mixer")
        TerrainGrids = New Shader("TerrainGrids")
        TerrainNormals = New Shader("TerrainNormals")
        TerrainNormalsHQ = New Shader("TerrainNormalsHQ")

        TerrainLQShader = New Shader("TerrainLQ")
        TerrainHQShader = New Shader("TerrainHQ") ' High Quality + Tessellation

        TerrainVTMIPShader = New Shader("TerrainVTMIP")

        TextRenderShader = New Shader("TextRender")
        toLinearShader = New Shader("toLinear")
        'particle shaders
        explode_type_1_shader = New Shader("explode_type_1_")
        'shadow shaders
        mDepthWrite_light = New Shader("mDepthWrite_light")
        imguiShader = New Shader("imgui")

        watcher = New FileSystemWatcher(Path.GetFullPath("shaders"))
        AddHandler watcher.Changed, AddressOf OnChanged
        watcher.IncludeSubdirectories = True
        watcher.EnableRaisingEvents = True
        watcher.Filter = "*"
    End Sub

    Private Sub OnChanged(sender As Object, e As FileSystemEventArgs)
        LogThis($"File changed: {e.FullPath}")
        main_window.SHADER_CHANGED = True
    End Sub

    Public Function assemble_shader(v As String,
                                    tc As String,
                                    te As String,
                                    g As String,
                                    c As String,
                                    f As String,
                                    name As String,
                                    defines As Dictionary(Of String, String)) As Integer
        Dim status_code As Integer

        Dim program As Integer = GL.CreateProgram()
        LabelObject(ObjectLabelIdentifier.Program, program, name)

        If program = 0 Then
            Return 0
        End If

        ' Compile vertex shader
        Dim vertexObject As Integer = 0
        If v IsNot Nothing Then
            vertexObject = GL.CreateShader(ShaderType.VertexShader)

            Using vs_s As New StreamReader(v)
                Dim vs = vs_s.ReadLine() & vbCrLf
                For Each item In defines
                    vs += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                vs += vs_s.ReadToEnd()
                GL.ShaderSource(vertexObject, vs)
            End Using

            GL.Arb.CompileShaderInclude(vertexObject, incPaths.Length, incPaths, incPathLengths)

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

        ' Compile tess control shader
        Dim tessControlObject As Integer = 0
        If tc IsNot Nothing Then
            tessControlObject = GL.CreateShader(ShaderType.TessControlShader)

            Using tcs_s As New StreamReader(tc)
                Dim tcs = tcs_s.ReadLine() & vbCrLf
                For Each item In defines
                    tcs += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                tcs += tcs_s.ReadToEnd()
                GL.ShaderSource(tessControlObject, tcs)
            End Using

            GL.Arb.CompileShaderInclude(tessControlObject, incPaths.Length, incPaths, incPathLengths)

            ' Get & check status after compile
            GL.GetShader(tessControlObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(tessControlObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(tessControlObject)
                GL.DeleteProgram(program)
                gl_error(tc + " didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If
        End If

        ' Compile tess evaluation shader
        Dim tessEvaluationObject As Integer = 0
        If te IsNot Nothing Then
            tessEvaluationObject = GL.CreateShader(ShaderType.TessEvaluationShader)

            Using tes_s As New StreamReader(te)
                Dim tes = tes_s.ReadLine() & vbCrLf
                For Each item In defines
                    tes += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                tes += tes_s.ReadToEnd()
                GL.ShaderSource(tessEvaluationObject, tes)
            End Using

            GL.Arb.CompileShaderInclude(tessEvaluationObject, incPaths.Length, incPaths, incPathLengths)

            ' Get & check status after compile
            GL.GetShader(tessEvaluationObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(tessEvaluationObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(tessControlObject)
                GL.DeleteShader(tessEvaluationObject)
                GL.DeleteProgram(program)
                gl_error(te + " didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If
        End If

        ' Compile fragment shader
        Dim fragmentObject As Integer = 0
        If f IsNot Nothing Then
            fragmentObject = GL.CreateShader(ShaderType.FragmentShader)

            Using fs_s As New StreamReader(f)
                Dim fs = fs_s.ReadLine() & vbCrLf
                For Each item In defines
                    fs += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                fs += fs_s.ReadToEnd()
                GL.ShaderSource(fragmentObject, fs)
            End Using

            GL.Arb.CompileShaderInclude(fragmentObject, incPaths.Length, incPaths, incPathLengths)

            ' Get & check status after compile
            GL.GetShader(fragmentObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(fragmentObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(tessControlObject)
                GL.DeleteShader(tessEvaluationObject)
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
                Dim gs = gs_s.ReadLine() & vbCrLf
                For Each item In defines
                    gs += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                gs += gs_s.ReadToEnd()
                GL.ShaderSource(geomObject, gs)
            End Using

            GL.Arb.CompileShaderInclude(geomObject, incPaths.Length, incPaths, incPathLengths)

            ' Get & check status after compile
            GL.GetShader(geomObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(geomObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(tessControlObject)
                GL.DeleteShader(tessEvaluationObject)
                GL.DeleteShader(fragmentObject)
                GL.DeleteShader(geomObject)
                GL.DeleteProgram(program)
                gl_error(name + "_geo didn't compile!" + vbCrLf + info.ToString)
                Return 0
            End If
        End If

        ' Compile Compute shader
        Dim computeObject As Integer = 0
        If c IsNot Nothing Then
            computeObject = GL.CreateShader(ShaderType.ComputeShader)

            Using cs_s As New StreamReader(c)
                Dim cs = cs_s.ReadLine() & vbCrLf
                For Each item In defines
                    cs += String.Format("#define {0} {1}" & vbCrLf, item.Key, item.Value)
                Next
                cs += cs_s.ReadToEnd()
                GL.ShaderSource(computeObject, cs)
            End Using

            GL.Arb.CompileShaderInclude(computeObject, incPaths.Length, incPaths, incPathLengths)

            ' Get & check status after compile
            GL.GetShader(computeObject, ShaderParameter.CompileStatus, status_code)
            If status_code = 0 Then
                Dim info = GL.GetShaderInfoLog(computeObject)
                GL.DeleteShader(vertexObject)
                GL.DeleteShader(tessControlObject)
                GL.DeleteShader(tessEvaluationObject)
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

        If tessControlObject Then
            GL.AttachShader(program, tessControlObject)
        End If

        If tessEvaluationObject Then
            GL.AttachShader(program, tessEvaluationObject)
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
        If tessControlObject Then
            GL.DetachShader(program, tessControlObject)
        End If
        If tessEvaluationObject Then
            GL.DetachShader(program, tessEvaluationObject)
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
        If tessControlObject Then
            GL.DeleteShader(tessControlObject)
        End If
        If tessEvaluationObject Then
            GL.DeleteShader(tessEvaluationObject)
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
        LogThis(s.Replace(vbLf, vbCrLf))
    End Sub

#End Region
End Module

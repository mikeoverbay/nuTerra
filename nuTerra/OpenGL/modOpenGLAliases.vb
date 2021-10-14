Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Public Module modOpenGLAliases
    Public Const GL_REPRESENTATIVE_FRAGMENT_TEST_NV As EnableCap = 37759
    Public Const GL_GPU_MEM_INFO_CURRENT_AVAILABLE_MEM_NVX As GetPName = &H9049
    Public Const GL_DEBUG_TOOL_EXT = &H6789

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub CheckGLError()
#If DEBUG Then
        Dim err_code = GL.GetError
        If err_code > 0 Then
            LogThis("GL Error {0}", err_code.ToString)
            'Stop
        End If
#End If
    End Sub

    <Conditional("DEBUG")>
    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub LabelObject(objLabelIdent As ObjectLabelIdentifier, glObject As Integer, name As String)
        GL.ObjectLabel(objLabelIdent, glObject, name.Length, name)
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub unbind_textures(count As Integer)
        GL.BindTextures(0, count, Unsafe.NullRef(Of Integer))
    End Sub
End Module

Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Public Module modOpenGLAliases
    Public Const GL_REPRESENTATIVE_FRAGMENT_TEST_NV As EnableCap = 37759

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
#If False Then
        ' SHOULD WE USE MULTI UNBIND?
        GL.BindTextures(0, count, 0)
#Else
        For i = 0 To count - 1
            GL.BindTextureUnit(i, 0)
        Next
#End If
    End Sub
End Module

Imports OpenTK.Graphics.OpenGL4
Imports System.Runtime.InteropServices

''' <summary>
''' THE ONE THING ImGuiController NEEDS THAT THIS APP DOES NOT HAVE.
'''
''' BrainTesting links about 25 nuTerra files because nuTerra's types are
''' Friend and a project reference cannot see them. ImGuiController is now one
''' of them - it is self-contained apart from a single global, `imguiShader`,
''' which in nuTerra is a ShaderLoader Shader.
'''
''' Rather than fork the controller - which would put a second copy of 420
''' lines on the wrong side of the one-source rule the owner set today - this
''' supplies that global, backed by BrainShader and reading the same
''' shaders/imgui.vert and .frag this app loads everything else from.
'''
''' THE SHAPE IS NOT MINE TO CHOOSE. The controller calls it three ways:
'''
'''     imguiShader.Use()
'''     imguiShader("projection_matrix")      a DEFAULT property, uniform id
'''     imguiShader.StopUse()
'''
''' so those three exist and nothing else does. A shim that grew a fourth
''' method would be a second shader class competing with BrainShader, which is
''' the thing it exists to avoid.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Public Class ImGuiShaderShim

    ''' <summary>
    ''' ONE SHADER PER GL CONTEXT, keyed on the context itself.
    '''
    ''' The node editor window has its own, unshared context. GL programs do
    ''' not cross unshared contexts, so a single cached shader would hand that
    ''' window the MAIN window's program id - a number that means nothing
    ''' there. Nothing would draw, and nothing would say why: GL does not
    ''' error on a program name it has never heard of, it just renders
    ''' nothing.
    '''
    ''' Duplicated device objects, not duplicated source - both entries compile
    ''' the same shaders/imgui.vert and .frag.
    ''' </summary>
    Private ReadOnly byCtx As New Dictionary(Of IntPtr, BrainShader)

    <DllImport("opengl32.dll")>
    Private Shared Function wglGetCurrentContext() As IntPtr
    End Function

    Public ReadOnly Property Ready As Boolean
        Get
            Dim sh = cur()
            Return sh IsNot Nothing AndAlso sh.Ready
        End Get
    End Property

    ''' <summary>The shader for whichever context is current, compiled on first
    ''' use in it - the context has to exist before a shader can be built in
    ''' it, and a module initialiser runs before any window.</summary>
    Private Function cur() As BrainShader
        Dim c = wglGetCurrentContext()
        Dim sh As BrainShader = Nothing
        If Not byCtx.TryGetValue(c, sh) Then
            sh = New BrainShader("imgui")
            byCtx(c) = sh
        End If
        Return sh
    End Function

    Public Sub Use()
        Dim sh = cur()
        If sh.Ready Then sh.Use()
    End Sub

    Public Sub StopUse()
        GL.UseProgram(0)
    End Sub

    ''' <summary>A uniform's location. The controller indexes the shader
    ''' directly, so this is a Default property rather than a named one.</summary>
    Default Public ReadOnly Property Item(name As String) As Integer
        Get
            Dim sh = cur()
            If Not sh.Ready Then Return -1
            Return sh.Loc(name)
        End Get
    End Property

End Class

''' <summary>The global the linked controller reaches for.</summary>
Module BrainImGuiGlobals
    Public imguiShader As New ImGuiShaderShim()
End Module

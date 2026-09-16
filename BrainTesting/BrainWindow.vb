Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop

''' <summary>
''' The window. At stage 0 it opens, clears and closes - that is the whole of
''' it, and proving that much stands up is the point of the stage.
'''
''' Added 2026-09-15 by nuTerra work, stage 0 of docs\brain_testing_plan.md.
''' </summary>
Public Class BrainWindow
    Inherits GameWindow

    Public Sub New()
        MyBase.New(New GameWindowSettings With {
                       .RenderFrequency = 0.0,
                       .UpdateFrequency = 0.0
                   }, settings())
        VSync = VSyncMode.Off
    End Sub

    ''' <summary>
    ''' The context this app asks for.
    '''
    ''' A REAL DEPTH BUFFER, unlike nuTerra, which asks for DepthBits = 0
    ''' because everything it draws goes through its own G-buffer and the
    ''' default framebuffer only ever receives a resolved image. This app draws
    ''' terrain and buildings straight to the back buffer, so it needs a depth
    ''' attachment there or the ground sorts by draw order and buildings show
    ''' through hills.
    '''
    ''' 4.5 core to match nuTerra: the shaders and the compute cull that get
    ''' linked in later stages are written against it.
    ''' </summary>
    Private Shared Function settings() As NativeWindowSettings
        If HALF_SIZE_WINDOW Then
            SCR_WIDTH = Math.Max(320, SCR_WIDTH \ 2)
            SCR_HEIGHT = Math.Max(240, SCR_HEIGHT \ 2)
        End If

        If FULLSCREEN_WINDOW Then
            Dim b = System.Windows.Forms.Screen.PrimaryScreen.Bounds
            SCR_WIDTH = b.Width
            SCR_HEIGHT = b.Height
        End If

        Dim s As New NativeWindowSettings With {
            .Size = New Vector2i(SCR_WIDTH, SCR_HEIGHT),
            .API = ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible,
            .DepthBits = 24,
            .StencilBits = 0,
            .Title = window_title()
        }
#If DEBUG Then
        s.Flags = s.Flags Or ContextFlags.Debug
#End If
        If FULLSCREEN_WINDOW Then
            s.WindowBorder = WindowBorder.Hidden
            s.Location = New Vector2i(0, 0)
        End If
        Return s
    End Function

    ''' <summary>
    ''' The owner's tag goes FIRST, because a taskbar button truncates from the
    ''' end - a tag at the back is the part that disappears, and the tag is the
    ''' only thing that tells his four windows apart.
    '''
    ''' NOT called `title`: VB is case blind, so that name IS NativeWindow's
    ''' own Title property and silently shadows it.
    ''' </summary>
    Private Shared Function window_title() As String
        If OWNER_TAG.Trim() = "" Then Return APP_NAME
        Return OWNER_TAG.Trim() & " - " & APP_NAME
    End Function

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()

        Dim major = GL.GetInteger(GetPName.MajorVersion)
        Dim minor = GL.GetInteger(GetPName.MinorVersion)
        If major < 4 OrElse (major = 4 AndAlso minor < 3) Then
            ' Said and then quit, rather than limping on to fail inside a
            ' shader compile where the real cause is three screens away.
            MsgBox("Brain Testing needs OpenGL 4.3 or newer." & vbLf &
                   "This driver reports " & major & "." & minor & ".")
            Close()
            Return
        End If

        Console.WriteLine("GL {0}  |  {1}",
                          GL.GetString(StringName.Version),
                          GL.GetString(StringName.Renderer))
        If STARTUP_MAP IsNot Nothing Then
            Console.WriteLine("map from the command line: {0}", STARTUP_MAP)
        End If

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.ClearColor(0.16F, 0.17F, 0.19F, 1.0F)
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        GL.Viewport(0, 0, e.Width, e.Height)
        SCR_WIDTH = e.Width
        SCR_HEIGHT = e.Height
    End Sub

    Protected Overrides Sub OnRenderFrame(e As FrameEventArgs)
        MyBase.OnRenderFrame(e)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        SwapBuffers()
    End Sub

    Protected Overrides Sub OnUpdateFrame(e As FrameEventArgs)
        MyBase.OnUpdateFrame(e)
        If KeyboardState.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape) Then Close()
    End Sub

End Class

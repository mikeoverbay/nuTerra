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

    ''' <summary>
    ''' Draw one frame NOW, from inside a blocking load.
    '''
    ''' The linked terrain builders call this so a long load still paints -
    ''' without it the whole load is a single frozen frame. Same purpose as
    ''' nuTerra's Window.ForceRender.
    ''' </summary>
    Public Sub ForceRender()
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)
        DrawWorld()
        SwapBuffers()
        ProcessEvents(0.0)
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        main_window = Me

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

        ' ONE TAG, AND ONLY ONE. The linked loaders are chatty - thirty
        ' vehicles announce their chassis, turret, gun, aim limits and gun
        ' timing, about 150 lines - and they carry the "tank:" tag, which
        ' nuTerra's gate KEEPS. So opening LOG_EVERYTHING to let this app
        ' narrate its own startup brought all of that with it.
        '
        ' Narrowing LOG_KEEP to "brain:" is the right lever: this app says
        ' what it is doing, the loaders it borrowed stay quiet, and `verbose`
        ' on the command line hands the firehose back when a load is being
        ' debugged. "I don't want any spam in the outout debug win."
        If LOG_VERBOSE Then
            LOG_EVERYTHING = True
        Else
            LOG_KEEP = New String() {"brain:"}
        End If

        BrainWorld.Init()
        If STARTUP_MAP IsNot Nothing AndAlso BrainWorld.Ready AndAlso
           Not BrainWorld.HasSpace(STARTUP_MAP) Then
            Dim near = BrainWorld.NearMisses(STARTUP_MAP, 6)
            LogThis("brain: no installed space called {0}{1}", STARTUP_MAP,
                    If(near.Count = 0, "", " - did you mean: " & String.Join(", ", near)))
        End If

        BrainRender.Init()

        If BrainWorld.Ready AndAlso STARTUP_MAP IsNot Nothing Then
            If BrainWorld.LoadMap(STARTUP_MAP) Then
                ' AFTER the terrain, so MAP_SIZE is real. Framing against the
                ' default would put the camera somewhere arbitrary on a map
                ' whose extent is not known until the chunks are counted.
                BrainRender.Cam.FrameMap(Math.Max(MAP_SIZE.X, MAP_SIZE.Y) * 100.0F)
            End If
            BrainTanks.ReadArena(STARTUP_MAP)
            BrainTanks.LoadAll(TANK_PER_TEAM)
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
        DrawWorld()
        SwapBuffers()

        ' A COUPLE OF FRAMES IN, not the first. The first frame can land before
        ' the driver has the buffers it was promised, and a black capture would
        ' read as "nothing draws" when the truth is "nothing drew YET".
        frames += 1
        If SHOT_PATH <> "" AndAlso frames = 3 Then
            Capture(SHOT_PATH)
            Close()
        End If
    End Sub

    Private frames As Integer = 0

    ''' <summary>
    ''' The back buffer to a PNG, and a COUNT of how much of it is not the
    ''' clear colour.
    '''
    ''' The count is the point for an agent: a picture proves nothing to a
    ''' session that cannot look at it, and "94% of pixels differ from the
    ''' background" is a measurement that does. The owner gets the picture;
    ''' the log gets the number.
    ''' </summary>
    Private Sub Capture(path As String)
        Dim w = ClientSize.X, h = ClientSize.Y
        Dim px(w * h * 4 - 1) As Byte
        GL.ReadBuffer(ReadBufferMode.Back)
        GL.ReadPixels(0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, px)

        ' The clear colour, as bytes, to measure coverage against.
        Dim cr = CByte(0.16F * 255), cg = CByte(0.17F * 255), cb = CByte(0.19F * 255)
        Dim drawn = 0
        For i = 0 To w * h - 1
            Dim b = px(i * 4), g2 = px(i * 4 + 1), r = px(i * 4 + 2)
            If Math.Abs(CInt(r) - cr) > 6 OrElse Math.Abs(CInt(g2) - cg) > 6 OrElse
               Math.Abs(CInt(b) - cb) > 6 Then drawn += 1
        Next

        Try
            Using bmp As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
                Dim d = bmp.LockBits(New Rectangle(0, 0, w, h),
                                     Imaging.ImageLockMode.WriteOnly,
                                     Imaging.PixelFormat.Format32bppArgb)
                ' GL reads bottom-up; a bitmap is top-down. Copy row by row in
                ' reverse rather than flipping afterwards.
                For y = 0 To h - 1
                    Runtime.InteropServices.Marshal.Copy(
                        px, (h - 1 - y) * w * 4,
                        IntPtr.Add(d.Scan0, y * d.Stride), w * 4)
                Next
                bmp.UnlockBits(d)
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(IO.Path.GetFullPath(path)))
                bmp.Save(path, Imaging.ImageFormat.Png)
            End Using
            LogThis("brain: shot {0} ({1}x{2}), {3:0.0}% of pixels drawn",
                    path, w, h, 100.0 * drawn / (w * h))
        Catch ex As Exception
            LogThis("brain: could not write {0} - {1}", path, ex.Message)
        End Try
    End Sub

    ''' <summary>Everything the frame draws, in ONE place so the normal frame
    ''' and ForceRender cannot drift apart.</summary>
    Private Sub DrawWorld()
        Dim aspect = CSng(Math.Max(SCR_WIDTH, 1)) / CSng(Math.Max(SCR_HEIGHT, 1))
        BrainRender.DrawTerrain(aspect)
    End Sub

    Protected Overrides Sub OnUpdateFrame(e As FrameEventArgs)
        MyBase.OnUpdateFrame(e)
        If KeyboardState.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape) Then Close()
        BrainRender.Cam.Update(CSng(e.Time), KeyboardState)
    End Sub

End Class

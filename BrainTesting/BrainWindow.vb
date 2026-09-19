Imports ImGuiNET
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
    ''' <summary>The title carries the controls, because there is nowhere else
    ''' to put them in an app with no text rendering.</summary>
    Private Shared Function window_title() As String
        Const KEYS As String = "   [Space] run/stop sim   [F5] reload   [Esc] quit"
        If OWNER_TAG.Trim() = "" Then Return APP_NAME & KEYS
        Return OWNER_TAG.Trim() & " - " & APP_NAME & KEYS
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
        BrainPanel.Init(ClientSize.X, ClientSize.Y)

        ' THE NODE EDITOR'S WINDOW, made now and hidden until it is wanted.
        ' Built here because its GL context is created by asking the CURRENT
        ' context for an extension, so one has to be live - and after the
        ' panel, because it builds an ImGui controller of its own and wants
        ' the main one to exist first so it can hand the context back to it.
        BrainNodeForm.Start(Me)

        If BrainWorld.Ready AndAlso STARTUP_MAP IsNot Nothing Then
            If BrainWorld.LoadMap(STARTUP_MAP) Then
                ' AFTER the terrain, so MAP_SIZE is real. Framing against the
                ' default would put the camera somewhere arbitrary on a map
                ' whose extent is not known until the chunks are counted.
                BrainRender.Cam.FrameMap(Math.Max(MAP_SIZE.X, MAP_SIZE.Y) * 100.0F)
                BrainModels.Build()
                BrainTrees.Build()
                If BrainNav.NAV_AUDIT Then BrainTrunks.Measure()
                If LOOK_AT IsNot Nothing Then
                    BrainRender.Cam.LookAt(LOOK_AT(0), LOOK_AT(1),
                                           get_Y_at_XZ(LOOK_AT(0), LOOK_AT(1)), LOOK_AT(2))
                End If
            End If
            BrainTanks.ReadArena(STARTUP_MAP)
            ' QUEUED, NOT LOADED. One vehicle a frame from OnRenderFrame, so
            ' the world is on screen and the camera is live while they arrive.
            BrainTanks.BeginLoad(TANK_PER_TEAM)
            BrainRings.Build()

                ' The nav grid AFTER the models, because it is rasterised from
                ' their footprints, and after the terrain, because it samples
                ' slope. Both are up by here.
                ' THE PROJECT'S OWN ANSWER FIRST. nuTerra cuts a 1 m square map
                ' out of the flight bake and the tank driving reads it; this
                ' app reading anything else would be two maps for one question.
                ' The footprint rasteriser is the fallback for a map with no
                ' bake yet, and the log says which one answered.
                If Not BrainNav.LoadSquares(STARTUP_MAP) Then BrainNav.Build()

                ' NOW the trees can be drawn by whether a hull gets through
                ' them. Built earlier, decided here - the grid did not exist
                ' when their geometry went up.
                BrainTrees.MarkDrivable()
                If BrainNav.NAV_AUDIT Then BrainNav.MaterialAudit()

                ' THE SIM STARTS AT LAUNCH - the owner's ask. What it DOES is
                ' Tank AI's: BrainSim.Brain is NullBrain until their code sets
                ' it, and NullBrain parks everything. `sim=0` holds it back for
                ' a run where the world is the thing being looked at.
                ' The sim starts when the hulls are all in - see OnRenderFrame.
                ' Starting it on a half-loaded roster would hand a brain a
                ' different number of tanks each frame.
        End If

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.ClearColor(0.16F, 0.17F, 0.19F, 1.0F)
    End Sub

    Protected Overrides Sub OnTextInput(e As TextInputEventArgs)
        MyBase.OnTextInput(e)
        BrainPanel.PressChar(ChrW(e.Unicode))
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseWheelEventArgs)
        MyBase.OnMouseWheel(e)
        BrainPanel.Scroll(New Vector2(e.OffsetX, e.OffsetY))
    End Sub

    Protected Overrides Sub OnUnload()
        ' Flush the black box. A CSV cut off mid-row is a run nobody can read,
        ' and the last rows are the ones that say how it ended.
        BrainSim.Halt()
        MyBase.OnUnload()
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)
        BrainPanel.Resized(e.Width, e.Height)
        GL.Viewport(0, 0, e.Width, e.Height)
        SCR_WIDTH = e.Width
        SCR_HEIGHT = e.Height
    End Sub

    Protected Overrides Sub OnRenderFrame(e As FrameEventArgs)
        MyBase.OnRenderFrame(e)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        ' THE GOAL RIDES THE CAMERA, unless it has been pinned with alt. Done
        ' before the world is drawn so the crosshair and the tank's target are
        ' the same thing on the same frame.
        BrainGoal.FollowCamera()

        DrawWorld()

        ' THE PANEL OVER THE WORLD, before the swap. Drawn every frame
        ' whatever the sim is doing - a control that vanishes when the
        ' thing it controls stops is the one you need most.
        Dim act = BrainPanel.Draw(Me, CSng(e.Time))
        If act <> BrainPanel.Action.None Then do_panel(act)

        ' AND THE NODE EDITOR, in its own window and its own context. It makes
        ' both of ours current again before it returns, so the swap below is
        ' still this window's.
        BrainNodeForm.Frame(CSng(e.Time))

        SwapBuffers()

        ' A COUPLE OF FRAMES IN, not the first. The first frame can land before
        ' the driver has the buffers it was promised, and a black capture would
        ' read as "nothing draws" when the truth is "nothing drew YET".
        frames += 1
        If frames = 1 Then
            ' THE NUMBER THAT MATTERS. Not the window handle - that exists
            ' before anything is drawn - but the first frame with the world in
            ' it, which is when the app is actually up.
            LogThis("brain: WORLD ON SCREEN at {0:0.00}s", boot.Elapsed.TotalSeconds)
        End If

        ' ONE VEHICLE A FRAME, after the frame is on screen. A frame is drawn
        ' between each, so the app is draggable while the roster arrives.
        If BrainTanks.Loading Then
            BrainTanks.LoadStep()
            If Not BrainTanks.Loading Then
                ' Everything that needs the finished roster, in order.
                BrainNav.SelfCheck()
                ' RESTORE FIRST, THEN REMEMBER. The scenario becomes the
                ' opening position, so Reset goes back to the setup rather
                ' than to wherever the roster happened to spawn - which is
                ' what Reset is FOR on an evening of one scenario tried
                ' twenty ways.
                If RESTORE_ON_START Then BrainPanel.RestoreSnapshot()
                ' Before anything can drive: once it does, Body.spawn is
                ' the LIVE position and the opening one is gone.
                BrainPanel.RememberSpawns()
                If LEARN_ON_START Then
                    ' Straight into it. The goal came back with the
                    ' scenario, so there is nothing to place.
                    BrainSim.Brain = pick_brain()
                    If Not BrainGoal.HasTarget Then
                        LogThis("brain: learning asked for but no goal in the " &
                                "snapshot - press alt to place one")
                    End If
                    BrainSim.Start()
                ElseIf BRAIN_ON Then
                    BrainSim.Start()
                End If
            End If
        End If

        ' ---- the timed run, if one was asked for -------------------------
        If RUN_SECS > 0.0F AndAlso Not scored Then
            If BrainSim.Running Then
                If Not runClock.IsRunning Then runClock.Start()
                If runClock.Elapsed.TotalSeconds >= RUN_SECS Then score_and_quit()
                ' Or give up on it: no ground gained for BAIL_S, after a
                ' fair start. The row still gets written - a failure that
                ' reports itself is worth as much as a success.
                If BAIL_S > 0.0F AndAlso
                   runClock.Elapsed.TotalSeconds > 12.0 AndAlso
                   BrainReport.SinceGain > BAIL_S Then
                    LogThis("brain: giving up - no ground gained for {0:0}s",
                            BrainReport.SinceGain)
                    score_and_quit()
                End If
            ElseIf runClock.IsRunning Then
                ' It stopped early - arrived, or threw. Score what there is
                ' rather than waiting out a clock nothing is driving.
                score_and_quit()
            ElseIf boot.Elapsed.TotalSeconds > RUN_SECS + 40.0F Then
                LogThis("brain: timed run - the sim never started")
                score_and_quit()
            End If
        End If

        ' The shot waits for the roster. Capturing at frame 3 would photograph
        ' a map with two tanks on it and call it thirty.
        If SHOT_PATH <> "" AndAlso Not BrainTanks.Loading AndAlso frames >= 3 Then
            If shot_due() Then
                Capture(SHOT_PATH)
                Close()
            End If
        End If
    End Sub

    Private frames As Integer = 0
    Private ReadOnly boot As Stopwatch = Stopwatch.StartNew()

    ''' <summary>Counts only while the sim is actually running.</summary>
    Private ReadOnly shotClock As New Stopwatch()
    Private ReadOnly runClock As New Stopwatch()
    ''' <summary>Time since the last held-space step.</summary>
    Private ReadOnly holdClock As New Stopwatch()
    Private scored As Boolean = False

    ''' <summary>One scorecard line, then out. Written to the log rather
    ''' than to a file, because the thing comparing two runs is reading
    ''' stdout and a file would be one more thing to keep in step.</summary>
    Private Sub score_and_quit()
        scored = True
        Dim at = BrainReport.StartPos
        If BrainTanks.Bodies IsNot Nothing AndAlso
           BrainTanks.Bodies.Count > BrainRadar.HULL Then
            at = BrainTanks.Bodies(BrainRadar.HULL).spawn
        End If
        ' "brain: " IS NOT DECORATION. LogThis gates on the FORMAT STRING's
        ' prefix, not on the finished line, so LogThis("{0}", card) built the
        ' scorecard, matched nothing in LOG_KEEP and dropped it - four scored
        ' runs that printed no score and left no error.
        LogThis("brain: {0}", BrainReport.Scorecard(
            If(BrainSim.Brain Is Nothing, "none", BrainSim.Brain.Name),
            CSng(runClock.Elapsed.TotalSeconds), at, BrainGoal.Target, 5.0F))
        write_row(at)
        BrainSim.Halt()
        Close()
    End Sub

    ''' <summary>Append this run to the sweep's file, header first if the
    ''' file is new. Failure here must not lose the run - the console line
    ''' is already out.</summary>
    Private Sub write_row(at As Vector2)
        If SCORE_FILE = "" Then Return
        Try
            Dim dir_ = IO.Path.GetDirectoryName(IO.Path.GetFullPath(SCORE_FILE))
            IO.Directory.CreateDirectory(dir_)
            If Not IO.File.Exists(SCORE_FILE) Then
                IO.File.AppendAllText(SCORE_FILE,
                                      BrainReport.ROW_HEADER & Environment.NewLine)
            End If
            IO.File.AppendAllText(SCORE_FILE, BrainReport.ScoreRow(
                If(BrainSim.Brain Is Nothing, "none", BrainSim.Brain.Name),
                CSng(runClock.Elapsed.TotalSeconds), at, BrainGoal.Target) &
                Environment.NewLine)
        Catch ex As Exception
            LogThis("brain: could not write the score row - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Is it time for the shot.
    '''
    ''' `shotat=` exists because most of this HUD is empty until a brain
    ''' tick has happened. BrainRadar.LAST starts Nothing and only Scan
    ''' fills it, so the scope, the ahead view and everything else built on
    ''' the returns draw nothing at all before the first tick - and a shot
    ''' taken then looks exactly like a view that does not work.
    '''
    ''' THE CEILING IS NOT OPTIONAL. Without a goal there is nothing to
    ''' drive at and BrainSim never starts, so a clock that waits for it
    ''' waits forever - and a shot that never arrives reads as a hang. It
    ''' fires anyway and says why, because a photograph of a stopped sim is
    ''' still an answer and silence is not.
    ''' </summary>
    Private Function shot_due() As Boolean
        If SHOT_AFTER_S <= 0.0F Then Return True

        If BrainSim.Running Then
            If Not shotClock.IsRunning Then
                shotClock.Start()
                LogThis("brain: shot armed - holding {0:0.0}s of running sim",
                        SHOT_AFTER_S)
            End If
            If shotClock.Elapsed.TotalSeconds >= SHOT_AFTER_S Then Return True
        End If

        ' Waited the whole time over again and it never started.
        If boot.Elapsed.TotalSeconds > SHOT_AFTER_S + 40.0F Then
            LogThis("brain: shot no longer waiting - sim running={0}",
                    BrainSim.Running)
            Return True
        End If
        Return False
    End Function

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

        ' ---- THE CARD OVER THE TANK ----------------------------------------
        '
        ' After the terrain and inside the same depth buffer, so a card behind
        ' a hill is behind it. The camera's right and up come out of the view
        ' matrix rather than being recomputed from yaw and pitch: the matrix is
        ' what the world was actually drawn with, and a second derivation of
        ' the same basis is a second thing that can disagree with it.
        Dim vp = BrainRender.Cam.ViewProj(aspect)
        Dim view = Matrix4.LookAt(BrainRender.Cam.Eye, BrainRender.Cam.Target,
                                  Vector3.UnitY)
        Dim right = New Vector3(view.M11, view.M21, view.M31)
        Dim up = New Vector3(view.M12, view.M22, view.M32)
        BrainText.SetCamera(right, up)
        BrainTankState.Draw(vp, right, up, SCR_WIDTH, SCR_HEIGHT)
        BrainText.Render3D(vp)

        ' THE SCOPE, LAST, in its own ortho pass over everything. It is an
        ' instrument rather than part of the scene, so nothing in the world
        ' should ever be in front of it.
        BrainScope.Draw(SCR_WIDTH, SCR_HEIGHT)
        BrainAheadScope.Draw(SCR_WIDTH, SCR_HEIGHT)
    End Sub

    ''' <summary>Where the left button went down, and how far the cursor has
    ''' travelled since.</summary>
    Private pressAt As Vector2
    Private pressTravel As Single
    Private wasDown As Boolean

    ''' <summary>
    ''' Pick on RELEASE, and only if the cursor barely moved.
    '''
    ''' A left DRAG is the camera orbit. Picking on press would fire on the
    ''' first frame of every orbit, so the log would fill with whatever the
    ''' user happened to start the drag on - and the one thing this app must
    ''' not do is spam the output window.
    '''
    ''' TRAVEL IS ACCUMULATED, not measured press-to-release. A drag that
    ''' circles back to where it started has a displacement of zero and is
    ''' still emphatically a drag.
    ''' </summary>
    Private Sub pick_if_clicked()
        ' Not while the pointer belongs to a panel. A click on a button would
        ' otherwise also pick whatever happened to be behind it, and the log
        ' would fill with reports nobody asked for.
        If ImGui.GetIO().WantCaptureMouse Then
            wasDown = False
            Return
        End If
        Dim down = MouseState.IsButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left)
        Dim here = New Vector2(MouseState.X, MouseState.Y)
        If down Then
            If Not wasDown Then
                pressAt = here
                pressTravel = 0.0F
            Else
                pressTravel += (here - pressAt).Length
                pressAt = here
            End If
        ElseIf wasDown Then
            If pressTravel <= 4.0F Then
                Dim aspect = CSng(ClientSize.X) / Math.Max(1, ClientSize.Y)
                Dim vp = BrainRender.Cam.ViewProj(aspect)
                BrainPick.Report(BrainPick.At(vp, here.X, here.Y, ClientSize.X, ClientSize.Y))
            End If
        End If
        wasDown = down
    End Sub

    ''' <summary>
    ''' The controls. The owner, 2026-09-16: "can we get some controls. Run/stop
    ''' sim, reload data, quit."
    '''
    ''' KEYS, NOT BUTTONS. This app has no UI framework - no ImGui, no text
    ''' rendering - and adding one to put three buttons on screen would be the
    ''' single largest thing in it, in an app whose point is coming up fast.
    ''' The bindings are in the window title instead, where they cost nothing
    ''' and cannot scroll away.
    '''
    ''' IsKeyPressed, NOT IsKeyDown: it is true only on the frame the key goes
    ''' down. IsKeyDown would toggle the sim sixty times a second for as long
    ''' as the key was held, which reads as the sim refusing to start.
    ''' </summary>
    Private Sub handle_keys()
        Dim k = KeyboardState
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape) OrElse
           k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Q) Then
            Close()
            Return
        End If

        ' SPACE STEPS WHEN WE ARE STEPPING. With ticks= set the whole point
        ' is to look at one decision at a time, and the key that normally
        ' starts the sim would throw away the picture being looked at. Without
        ' ticks= it is the run/stop toggle it has always been.
        If TICK_LIMIT > 0 Then
            ' HOLD TO KEEP STEPPING, a tick every quarter second. Tapping
            ' space a hundred times to find where it went wrong is how a
            ' thing that only happens at tick forty never gets seen.
            If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space) Then
                BrainSim.StepOnce()
                holdClock.Restart()
            ElseIf k.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space) Then
                If holdClock.Elapsed.TotalSeconds >= 0.25 Then
                    BrainSim.StepOnce()
                    holdClock.Restart()
                End If
            End If
        ElseIf k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space) Then
            toggle_sim()
        End If
        ' ALT PLACES THE GOAL. "shift messes with mouse so use alt to place
        ' a goal spot" - the owner: the camera already takes shift, and a
        ' modifier that does two things is a modifier you cannot use.
        ' Enter stays because it costs nothing and was the first binding.
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftAlt) OrElse
           k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightAlt) OrElse
           k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter) OrElse
           k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEnter) Then go_here()
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.F5) Then reload_data()
        ' C chases, shift+C swings round behind it as well.
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.C) Then
            If k.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftShift) OrElse
               k.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightShift) Then
                BrainRender.Cam.ChaseTrail = Not BrainRender.Cam.ChaseTrail
                If BrainRender.Cam.ChaseTrail Then BrainRender.Cam.Chase = True
                LogThis("brain: chase cam trailing {0}",
                        If(BrainRender.Cam.ChaseTrail, "on", "off"))
            Else
                BrainRender.Cam.Chase = Not BrainRender.Cam.Chase
                LogThis("brain: chase cam {0}", If(BrainRender.Cam.Chase, "on", "off"))
            End If
        End If

        ' STEP THE TANK, one square at a time. The camera owns WASD and
        ' E/Q, so the arrows are free - and they are the right shape for
        ' this: up and down walk, left and right aim.
        ' T steps the BRAIN one tick and stops again - the walk view holds
        ' that tick's picture, so a single decision can be looked at.
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.T) Then
            BrainSim.StepOnce()
        End If
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up) Then step_tank(1.0F)
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down) Then step_tank(-1.0F)
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left) Then turn_tank(-15.0F)
        If k.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right) Then turn_tank(15.0F)
    End Sub

    ''' <summary>
    ''' Start the sim, or stop it.
    '''
    ''' REFUSES WHILE THE ROSTER IS STILL ARRIVING, for the same reason the
    ''' automatic start waits: a brain handed a different number of hulls each
    ''' frame is being told the world changed when it did not.
    ''' </summary>
    Private Sub toggle_sim()
        If BrainSim.Running Then
            BrainSim.Halt()
            Return
        End If
        If BrainTanks.Loading Then
            LogThis("brain: the roster is still loading - sim not started")
            Return
        End If
        BrainSim.Start()
    End Sub

    ''' <summary>
    ''' Re-read what nuTerra writes, without restarting this app.
    '''
    ''' THE SQUARE MAP AND THE TREE COLOURS, because those are the files that
    ''' change under a running Brain Testing: nuTerra rebakes, cuts a fresh
    ''' <map>_squares.u8, and this app is looking at the old one until it is
    ''' told. That round trip - rebake there, F5 here - is the whole point.
    '''
    ''' WHAT IT DOES NOT RELOAD is the world and the tanks. Terrain, models and
    ''' vehicles are seconds of loading and do not change while the app is up;
    ''' re-reading the arena would also move every hull back to spawn, which is
    ''' not what "reload data" should do to a run in progress.
    '''
    ''' STOPS THE SIM FIRST if one is running. Swapping the grid under a brain
    ''' mid-step means it planned against one map and is judged against
    ''' another, and the black box would carry both without saying so.
    ''' </summary>
    ' GO HERE. Drops the crosshair where the camera is looking and puts the
    ' seeking brain in, so one press both sets the goal and starts the run -
    ' the owner asked to place it and have the tank seek it, not to place it
    ' and then go and find a second key.
    ' What a button asked for. The panel returns intent and this does it,
    ' so starting and stopping the world stays in one place.
    Private Sub do_panel(a As BrainPanel.Action)
        Select Case a
            Case BrainPanel.Action.RunStop
                toggle_sim()
            Case BrainPanel.Action.Reset
                ' STOP FIRST. Restoring positions under a running sim means
                ' the next tick drives from the old state into the new one,
                ' and the reset is half undone before it is seen.
                If BrainSim.Running Then BrainSim.Halt()
                BrainPanel.RestoreSpawns()
                ' THE GOAL STAYS. It is not run state, it is the question being
                ' asked - and a reset that also forgets where we were going
                ' means pressing Run does nothing, because the board's first
                ' rule is No Goal -> Stop. Keeping it makes reset-then-run a
                ' repeatable experiment: same start, same destination, so two
                ' runs can actually be compared. Alt still moves it.
                ' AND THE BRAIN'S OWN STATE. Putting the tank back without this
                ' left it believing it was part way through backing out of
                ' something that is no longer in front of it.
                Dim gb = TryCast(BrainSim.Brain, GraphBrain)
                If gb IsNot Nothing Then gb.ResetState()
            Case BrainPanel.Action.Shot
                Capture(BrainPanel.ShotPath())
            Case BrainPanel.Action.Snapshot
                BrainPanel.WriteSnapshot()
            Case BrainPanel.Action.Restore
                ' Same reason Reset stops first: a restore under a running
                ' sim is driven out of by the next tick.
                If BrainSim.Running Then BrainSim.Halt()
                BrainPanel.RestoreSnapshot()
            Case BrainPanel.Action.Cam
                Dim s = BrainPanel.CamArg()
                Try
                    ClipboardString = s
                Catch
                End Try
                LogThis("brain: {0}   (copied)", s)
        End Select
    End Sub

    ''' <summary>
    ''' One square forward or back, and stop the sim doing it too.
    '''
    ''' A scanner you can walk a metre at a time is how you find the place
    ''' where its answer changes - the last square that reads FLAT before a
    ''' doorway, the first that reads broken. Watching that happen a metre at
    ''' a time says more about the fit than any number of driven runs.
    '''
    ''' It refuses to step into ground the hull cannot stand on, using the
    ''' same radius the driver uses - a probe that can walk through walls
    ''' would be reading a world the tank does not live in.
    ''' </summary>
    Private Sub step_tank(sign As Single)
        If BrainTanks.Bodies Is Nothing OrElse
           BrainTanks.Bodies.Count <= BrainRadar.HULL Then Return
        If BrainSim.Running Then BrainSim.Halt()
        Dim i = BrainRadar.HULL
        Dim b = BrainTanks.Bodies(i)
        Dim d = BrainNav.CellSize * sign
        Dim nx = b.spawn.X + CSng(Math.Sin(b.headingRad)) * d
        Dim nz = b.spawn.Y + CSng(Math.Cos(b.headingRad)) * d
        Dim fit = CSng(Math.Sqrt(b.half.X * b.half.X + b.half.Z * b.half.Z)) + 0.3F
        If Not BrainNav.Standable(nx, nz, fit) Then
            LogThis("brain: step {0} refused - no room at ({1:0.0}, {2:0.0})",
                    If(sign > 0, "forward", "back"), nx, nz)
            Return
        End If
        b.spawn = New Vector2(nx, nz)
        b.y = BrainNav.Ground(nx, nz)
        BrainTanks.Bodies(i) = b
        Dim s = BrainRadar.FitSurface(BrainRadar.Scan(b.spawn, b.headingRad))
        LogThis("brain: step {0} -> ({1:0.0}, {2:0.0})  {3}",
                If(sign > 0, "forward", "back"), nx, nz, s.verdict)
    End Sub

    ''' <summary>Aim it, so the scan can be swept across a thing.</summary>
    Private Sub turn_tank(deg As Single)
        If BrainTanks.Bodies Is Nothing OrElse
           BrainTanks.Bodies.Count <= BrainRadar.HULL Then Return
        If BrainSim.Running Then BrainSim.Halt()
        Dim i = BrainRadar.HULL
        Dim b = BrainTanks.Bodies(i)
        b.headingRad += MathHelper.DegreesToRadians(deg)
        BrainTanks.Bodies(i) = b
        Dim s = BrainRadar.FitSurface(BrainRadar.Scan(b.spawn, b.headingRad))
        LogThis("brain: heading {0:0} deg  {1}",
                MathHelper.RadiansToDegrees(b.headingRad), s.verdict)
    End Sub

    ''' <summary>Whichever brain is switched on. One place, so the two
    ''' call sites cannot drift apart and leave the checkbox lying about
    ''' which one is driving.</summary>
    Private Function pick_brain() As IBrain
        If USE_GRAPH Then Return New GraphBrain()
        Return New RangeBrain()
    End Function

    Private Sub go_here()
        BrainGoal.PlaceAtLookAt()
        ' THE BRAIN. There is one now - RangeBrain - and this is where a goal
        ' placed by hand puts it to work.
        If Not (TypeOf BrainSim.Brain Is RangeBrain) Then
            BrainSim.Brain = pick_brain()
        End If
        If Not BrainSim.Running Then BrainSim.Start()
    End Sub

    Private Sub reload_data()
        Dim was_running = BrainSim.Running
        If was_running Then BrainSim.Halt()

        If STARTUP_MAP Is Nothing Then
            LogThis("brain: reload - no map to reload for")
            Return
        End If

        Dim before = BrainNav.Marked
        If Not BrainNav.LoadSquares(STARTUP_MAP) Then BrainNav.Build()
        BrainTrees.MarkDrivable()
        LogThis("brain: reloaded - {0:N0} blocked cell(s), was {1:N0}{2}",
                BrainNav.Marked, before,
                If(was_running, ". The sim was stopped - press Space to run it again", ""))
    End Sub

    Protected Overrides Sub OnUpdateFrame(e As FrameEventArgs)
        MyBase.OnUpdateFrame(e)
        handle_keys()
        ' ONE CALL, and the cursor is never grabbed. See BrainCamera - this is
        ' nuTerra's camera_mouse_update by way of Exporter Studio.
        BrainSim.Tick(CSng(e.Time))

        ' ---- THE UI GETS FIRST REFUSAL ON THE MOUSE ------------------------
        '
        ' "stop mouse from affecting main window when I am in a window"
        '
        ' WantCaptureMouse is ImGui's own answer to "is the pointer mine" - it
        ' is true over any panel, and true while a drag that STARTED on a panel
        ' is still held even after the cursor has left it. That second part is
        ' the one worth having: dragging a node across the graph and off its
        ' edge should not hand the rest of the gesture to the camera.
        '
        ' Asking it rather than testing the cursor against panel rectangles
        ' means there is one answer, ImGui's, and no second list of where the
        ' windows are to keep in step.
        Dim uiHasMouse = ImGui.GetIO().WantCaptureMouse
        If Not uiHasMouse Then
            BrainRender.Cam.Update(CSng(e.Time), MouseState, KeyboardState)
        End If

        ' THE CHASE, AFTER the mouse has had its say and before the frame is
        ' drawn. Update owns yaw, pitch and distance; this owns only where the
        ' orbit is centred, so the two cannot fight over the same field.
        If BrainRender.Cam.Chase AndAlso BrainTanks.Bodies IsNot Nothing AndAlso
           BrainTanks.Bodies.Count > BrainRadar.HULL Then
            Dim cb = BrainTanks.Bodies(BrainRadar.HULL)
            BrainRender.Cam.ChaseTo(cb.spawn.X, cb.spawn.Y, cb.y,
                                    cb.headingRad, CSng(e.Time))
        End If
        pick_if_clicked()
    End Sub

End Class

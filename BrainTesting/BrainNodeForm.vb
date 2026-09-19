Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports ImGuiNET
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Windowing.Desktop

''' <summary>
''' THE NODE EDITOR'S OWN WINDOW: A BORDERLESS FORM WITH ITS OWN GL CONTEXT.
'''
''' "add a new windows form at start up. build a rendering context for it. draw
'''  everything using that context. they don't have to share graphics. only
'''  data" - the owner, 2026-09-17.
'''
''' NOTHING IS SHARED WITH THE MAIN WINDOW except the node graph itself, which
''' is module state in BrainNodes and needs no help crossing over. No shared GL
''' context, no shared ImGui context, no shared font atlas, no shared buffers.
''' That is the whole reason this is simple: two independent renderers that
''' happen to read the same list, instead of one renderer trying to be in two
''' places. The multi-viewport attempt that came before this shared everything
''' and spent the evening on it.
'''
''' WHAT "ITS OWN CONTEXT" COSTS. GL objects do not cross unshared contexts, so
''' this window needs its own copy of everything the ImGui renderer uses - its
''' own ImGuiController (own VAO, buffers and font texture, built while this
''' context is current) and its own compiled shader, which is why the shim in
''' BrainImGuiShim keeps one per context. Duplicated device objects, not
''' duplicated CODE.
'''
''' HOW IT MOVES. The Form has no border, so there is nothing to drag. The
''' ImGui window covers it and has a title bar, so ImGui does the dragging and
''' the drift is passed on as a move command: each frame the window reports how
''' far it has wandered from the top left, the Form moves by exactly that, and
''' the window is put back to 0,0 IN THE SAME FRAME. Nothing is lost and
''' nothing lags - reset it next frame instead and half the mouse deltas go in
''' the bin, which reads as a window that moves at half speed.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainNodeForm

    Private form As Canvas = Nothing

    ''' <summary>The app's own window, kept only to hand its GL context
    ''' back. Every path out of here makes it current again.</summary>
    Private mainWnd As GameWindow = Nothing
    Private ctl As ImGuiController = Nothing

    ''' <summary>This window's ImGui context. Every frame switches to it and
    ''' switches back - the main window's context must be current again before
    ''' Frame returns or the panel draws into the wrong one.</summary>
    Private ctx As IntPtr = IntPtr.Zero

    ''' <summary>Where the window was before it was maximised, to put it back.
    ''' Kept here and not read off the form, which by then has been moved.</summary>
    Private restoreTo As Drawing.Rectangle = Drawing.Rectangle.Empty

    Private started As Boolean = False
    Private shown As Boolean = False

    Private Const START_W As Integer = 1000
    Private Const START_H As Integer = 420

    ''' <summary>
    ''' Make the window at startup, as asked. It stays hidden until the node
    ''' editor is switched on, so the cost of existing is a hidden HWND.
    '''
    ''' MUST be called with the main GL context current: building the context
    ''' looks up a WGL extension, and that needs some context to be current.
    ''' </summary>
    Public Sub Start(wnd As GameWindow)
        If started Then Return
        started = True
        mainWnd = wnd
        Try
            form = New Canvas()
            form.ClientSize = New Drawing.Size(START_W, START_H)
            Dim scr = Screen.PrimaryScreen.WorkingArea
            form.Location = New Drawing.Point(
                scr.X + (scr.Width - START_W) \ 2,
                scr.Y + (scr.Height - START_H) \ 2)
            ' Touching Handle is what actually creates the OS window. The
            ' context needs a window to sit on, and Show() comes much later.
            Dim unused = form.Handle
            If Not form.Setup() Then
                LogThis("brain: node window has no GL context - staying in the main window")
                form.Dispose() : form = Nothing
                Return
            End If

            ' ---- its own ImGui, built in its own context --------------------
            Dim mainCtx = ImGui.GetCurrentContext()
            form.MakeCurrent()
            ' The controller's constructor creates an ImGui context AND the GL
            ' objects. Both land where they are wanted precisely because this
            ' is the current pair right now.
            ctl = New ImGuiController(START_W, START_H)
            ctx = ImGui.GetCurrentContext()

            ' HERE, and not on the first frame that draws them. A GL texture
            ' belongs to the context that made it, and this is the only moment
            ' this window's context is current with nothing else going on.
            BrainIcons.Warm("window-min", "window-max", "window-restore")

            ' CLOSE THE FRAME THE CONSTRUCTOR OPENED. Its last two lines are
            ' NewFrame and _frameBegun = True, because its normal caller drives
            ' it through Update, which ends the previous frame before starting
            ' the next. This window does not use Update - it runs its own
            ' NewFrame and its own Render - so that opening frame is left
            ' hanging, and the first NewFrame here walks into
            ' "Forgot to call Render() or EndFrame() at the end of the previous
            ' frame?". Ended and thrown away: there is nothing in it.
            ImGui.EndFrame()
            ImGui.SetCurrentContext(mainCtx)
            mainWnd.MakeCurrent()

            BrainNodes.Hosted = True
            ' Something on the board from the very first frame - see
            ' EnsureSomething. An empty editor reads as a broken one.
            BrainNodes.EnsureSomething()
            LogThis("brain: node window up - own context, {0}x{1}", START_W, START_H)
        Catch ex As Exception
            LogThis("brain: node window failed - {0}", ex.Message)
            form = Nothing
        End Try
    End Sub

    ''' <summary>
    ''' One frame of the node editor, in its own context.
    '''
    ''' Called from the main loop AFTER the main window's UI, and it hands the
    ''' main GL and ImGui contexts back before it returns, whatever happened.
    ''' </summary>
    Public Sub Frame(dt As Single)
        If form Is Nothing OrElse ctl Is Nothing Then Return

        ' The switch lives in BrainPanel with the other view toggles. Hidden
        ' costs one comparison a frame.
        If Not BrainNodes.SHOW Then
            If shown Then
                form.Hide()
                shown = False
            End If
            Return
        End If
        If Not shown Then
            form.Show()
            shown = True
        End If

        do_window_cmd()

        ' MINIMISED MEANS NO CLIENT AREA. Drawing into a zero-sized window
        ' wastes a frame at best; ImGui also has to be told a display size, and
        ' zero is not one it accepts.
        If form.WindowState = FormWindowState.Minimized Then Return

        Dim mainCtx = ImGui.GetCurrentContext()

        ' An ImGui frame that is started and not finished poisons the NEXT
        ' one, and the assert it throws names the frame that failed rather
        ' than the one that broke it. So a throw between NewFrame and Render
        ' closes the frame on the way out.
        Dim begun = False
        Try
            form.MakeCurrent()
            ImGui.SetCurrentContext(ctx)

            Dim w = form.PixelsWide
            Dim h = form.PixelsHigh
            ctl.WindowResized(w, h)

            Dim io = ImGui.GetIO()
            io.DisplaySize = New System.Numerics.Vector2(w, h)
            io.DisplayFramebufferScale = System.Numerics.Vector2.One
            ' ImGui asserts on a delta that is not strictly positive.
            io.DeltaTime = Math.Max(dt, 0.001F)
            form.PostInput(io)

            ImGui.NewFrame()
            begun = True
            BrainNodes.Draw(w, h)
            ImGui.Render()
            begun = False

            GL.Disable(EnableCap.DepthTest)
            GL.Disable(EnableCap.CullFace)
            GL.Viewport(0, 0, w, h)
            GL.ClearColor(0.055F, 0.07F, 0.09F, 1.0F)
            GL.Clear(ClearBufferMask.ColorBufferBit)
            ctl.RenderViewport(ImGui.GetMainViewport())
            form.Present()

            ' ---- THE MOVE COMMAND ------------------------------------------
            '
            ' BrainNodes reported how far its window drifted while it was being
            ' dragged, and put itself back. The Form makes that real.
            If BrainNodes.HostDX <> 0.0F OrElse BrainNodes.HostDY <> 0.0F Then
                form.Location = keep_on_screen(
                    form.Location.X + CInt(BrainNodes.HostDX),
                    form.Location.Y + CInt(BrainNodes.HostDY))
                BrainNodes.HostDX = 0.0F
                BrainNodes.HostDY = 0.0F
            End If

            ' And the same for a resize, which needs no reset - the window's
            ' size IS the client size, so the Form simply follows it.
            Dim wantW = CInt(BrainNodes.HostW)
            Dim wantH = CInt(BrainNodes.HostH)
            If wantW > 0 AndAlso wantH > 0 AndAlso
               (wantW <> w OrElse wantH <> h) Then
                form.ClientSize = New Drawing.Size(wantW, wantH)
            End If
        Catch ex As Exception
            LogThis("brain: node window frame - {0}", ex.Message)
            If begun Then
                Try
                    ImGui.EndFrame()
                Catch
                End Try
            End If
        Finally
            ' ALWAYS BOTH, whatever happened. A context left current is the
            ' bug that does not fail where it happens.
            Try
                ImGui.SetCurrentContext(mainCtx)
                mainWnd.MakeCurrent()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' Carry out whatever the window buttons asked for.
    '''
    ''' Maximise goes to the WORKING AREA of the monitor the window is actually
    ''' on - not the primary one, and not the full bounds, which would put it
    ''' under the taskbar with its own close box beneath the clock.
    ''' </summary>
    Private Sub do_window_cmd()
        Dim cmd = BrainNodes.HostCmd
        If cmd = 0 Then Return
        BrainNodes.HostCmd = 0
        Try
            If cmd = 1 Then
                form.WindowState = FormWindowState.Minimized
                Return
            End If

            If BrainNodes.HostMaxed Then
                If restoreTo.Width > 0 Then form.Bounds = restoreTo
                BrainNodes.HostMaxed = False
            Else
                restoreTo = form.Bounds
                form.Bounds = Screen.FromControl(form).WorkingArea
                BrainNodes.HostMaxed = True
            End If
            ' The form moved itself, so the window has to follow for one frame
            ' instead of the other way round.
            BrainNodes.HostResync = True
        Catch ex As Exception
            LogThis("brain: node window command - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Somewhere the title bar can still be reached.
    '''
    ''' The ImGui title bar is the only handle this window has, so a drag that
    ''' puts it above the desktop strands the window - nothing left to grab,
    ''' and only a restart brings it back.
    '''
    ''' The TOP is the only hard edge. Pushed off the left, right or bottom the
    ''' title bar is still on screen and can be dragged home; pushed off the
    ''' top it is gone. A strip is kept visible on the other three so the
    ''' window cannot be posted into a corner either.
    ''' </summary>
    Private Function keep_on_screen(x As Integer, y As Integer) As Drawing.Point
        Const STRIP As Integer = 90      ' enough title bar to get hold of
        Dim v = SystemInformation.VirtualScreen
        Dim nx = Math.Max(v.Left - form.Width + STRIP, Math.Min(v.Right - STRIP, x))
        Dim ny = Math.Max(v.Top, Math.Min(v.Bottom - STRIP, y))
        Return New Drawing.Point(nx, ny)
    End Function

    ''' <summary>Close it for good, at shutdown.</summary>
    Public Sub Stop_()
        Try
            If form IsNot Nothing Then
                form.Teardown()
                form.Dispose()
                form = Nothing
            End If
        Catch
        End Try
    End Sub

End Module

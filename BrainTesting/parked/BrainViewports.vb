Imports System.Runtime.InteropServices
Imports ImGuiNET
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Windowing.Desktop
Imports System.Windows.Forms

''' <summary>
''' PANELS IN REAL, FRAMELESS WINDOWS FORMS.
'''
''' "this needs to be a new windows form no frame that this sits in" - the
''' owner, 2026-09-17.
'''
''' This is ImGui's multi-viewport machinery with a BrainNodeForm on the end of
''' it instead of an OpenTK window. ImGui decides when a panel leaves the main
''' window and when it comes back; this supplies the windows it asks for, and
''' each one is a System.Windows.Forms.Form with no frame and a shared GL
''' context. See BrainNodeForm for why the context is built by hand.
'''
''' WHY THE MACHINERY AT ALL, when the ask was just a Form. Because a Form on
''' its own is a rectangle. Everything that makes it USEFUL - the panel knowing
''' it has moved out, the mouse crossing between windows, the panel merging
''' back when dragged home, the close taking the window with it - is work ImGui
''' already does once viewports are on. Driving the Form from underneath it
''' gets the Form the owner asked for without writing any of that twice.
'''
''' WHY IT LIVES HERE AND NOT IN ImGuiController. The platform callbacks
''' register on the ImGui CONTEXT, so anything holding that context can fill
''' ImGuiPlatformIO. nuTerra's file needed three additions - one to draw a
''' viewport's list, because RenderImDrawData is Private, and two opt-in flags
''' that must be read before its own constructor finishes. All three default
''' to off and nothing it gained runs unless something calls it.
'''
''' THE DANGEROUS PART, written down so nobody has to find it twice: these
''' callbacks are invoked FROM NATIVE CODE. An exception thrown inside one does
''' not propagate - it takes the process down with no managed stack to read.
''' Every one is wrapped and returns something harmless rather than unwinding
''' into C.
'''
''' AND THE CONTEXT MUST COME BACK. Drawing a viewport makes that window's GL
''' context current. Leaving it current is the classic multi-viewport bug and
''' it does not fail where it happens - the main window draws into the wrong
''' context one frame in N and it reads as a driver fault. Every path here ends
''' with the main window current again.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainViewports

    Private main As GameWindow = Nothing
    Private ctl As ImGuiController = Nothing

    ''' <summary>Enable has run. NOT the same as "it worked" - see live.</summary>
    Private on_ As Boolean = False

    ''' <summary>The callbacks are in and there are windows to drive. EndFrame
    ''' keys off THIS, so a refused Enable costs nothing per frame.</summary>
    Private live As Boolean = False

    ''' <summary>The host viewport's id. It has no Form - it IS the app's own
    ''' window - so every callback answers for it out of `main` instead.
    ''' </summary>
    Private mainId As UInteger = 0

    ''' <summary>Delegates handed to native code must outlive the call. A local
    ''' delegate is collected as soon as the managed side forgets it, and the
    ''' crash lands later, inside ImGui, pointing at nothing.</summary>
    Private ReadOnly keepAlive As New List(Of Object)

    ''' <summary>One Form per torn-off viewport, keyed by the viewport's id.
    ''' The main viewport is NOT in here.</summary>
    Private ReadOnly forms As New Dictionary(Of UInteger, BrainNodeForm)

    ''' <summary>Ids whose window was asked to close. Handed back to ImGui as
    ''' PlatformRequestClose so ImGui destroys the viewport properly, rather
    ''' than the Form vanishing while ImGui still believes in it.</summary>
    Private ReadOnly closing As New HashSet(Of UInteger)

    ' ---- the C signatures, written out ------------------------------------
    '
    ' These match dear imgui's own declarations. Platform_Get* return ImVec2 BY
    ' VALUE there, which is what a managed Vector2 return marshals as.
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub VpFn(vp As IntPtr)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub VpSetVec2Fn(vp As IntPtr, v As System.Numerics.Vector2)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function VpGetVec2Fn(vp As IntPtr) As System.Numerics.Vector2
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function VpGetBoolFn(vp As IntPtr) As Byte
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub VpStrFn(vp As IntPtr, title As IntPtr)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub VpArgFn(vp As IntPtr, arg As IntPtr)

    Private Function keep(d As [Delegate]) As IntPtr
        keepAlive.Add(d)
        Return Marshal.GetFunctionPointerForDelegate(d)
    End Function

    ''' <summary>Turn it on. Once, after the controller and the window exist.</summary>
    Public Sub Enable(window As GameWindow, controller As ImGuiController)
        If on_ Then Return
        on_ = True
        main = window
        ctl = controller

        Dim io = ImGui.GetIO()

        ' THE CONFIG FLAG IS NOT OURS TO SET HERE. ImGui aborts the process if
        ' ViewportsEnable appears after the first NewFrame, and by the time
        ' anything can call this, frames have gone by. It is set through
        ' ImGuiController.WANT_VIEWPORTS instead, before the controller is
        ' built. If it is off, the callbacks below would never be called - so
        ' say so and leave, rather than half-arming it.
        If (io.ConfigFlags And ImGuiConfigFlags.ViewportsEnable) = 0 Then
            LogThis("brain: viewports OFF - WANT_VIEWPORTS was not set before the controller")
            Return
        End If

        ' The backend flags are NOT set here. They have to be on before the
        ' first NewFrame or ImGui clears ViewportsEnable again, so the
        ' controller sets all three together when WANT_VIEWPORTS is on. This
        ' is the other end of that promise: the callbacks, now.

        Dim pio = ImGui.GetPlatformIO()
        pio.Platform_CreateWindow = keep(New VpFn(AddressOf on_create))
        pio.Platform_DestroyWindow = keep(New VpFn(AddressOf on_destroy))
        pio.Platform_ShowWindow = keep(New VpFn(AddressOf on_show))
        pio.Platform_SetWindowPos = keep(New VpSetVec2Fn(AddressOf on_set_pos))
        pio.Platform_GetWindowPos = keep(New VpGetVec2Fn(AddressOf on_get_pos))
        pio.Platform_SetWindowSize = keep(New VpSetVec2Fn(AddressOf on_set_size))
        pio.Platform_GetWindowSize = keep(New VpGetVec2Fn(AddressOf on_get_size))
        pio.Platform_SetWindowFocus = keep(New VpFn(AddressOf on_focus))
        pio.Platform_GetWindowFocus = keep(New VpGetBoolFn(AddressOf on_has_focus))
        pio.Platform_GetWindowMinimized = keep(New VpGetBoolFn(AddressOf on_minimized))
        pio.Platform_SetWindowTitle = keep(New VpStrFn(AddressOf on_title))
        pio.Platform_RenderWindow = keep(New VpArgFn(AddressOf on_render))
        pio.Platform_SwapBuffers = keep(New VpArgFn(AddressOf on_swap))

        mainId = ImGui.GetMainViewport().ID

        ' Without this the panel cannot leave the window. See push_monitors.
        Try
            push_monitors()
        Catch ex As Exception
            LogThis("brain: monitors failed - {0} - panels will stay inside", ex.Message)
        End Try

        live = True
        LogThis("brain: viewports ON - panels get their own frameless form")
    End Sub

    ''' <summary>
    ''' Called once a frame, AFTER the controller's Render - ImGui.Render has to
    ''' have run or the torn-off viewports have no draw lists yet.
    '''
    ''' The main window's own SwapBuffers happens after this, back in
    ''' OnRenderFrame, on the context this restores.
    ''' </summary>
    Public Sub EndFrame()
        If Not live Then Return
        Try
            hand_back_closes()
            ImGui.UpdatePlatformWindows()
            ImGui.RenderPlatformWindowsDefault()
        Catch ex As Exception
            LogThis("brain: viewport frame threw - {0}", ex.Message)
        End Try
        ' ALWAYS, whatever happened above. See the note at the top of the file.
        Try
            main.MakeCurrent()
        Catch
        End Try
    End Sub

    ''' <summary>Turn a close request into the one ImGui understands, so the
    ''' window goes away by ImGui destroying the viewport rather than the Form
    ''' disappearing while ImGui still believes in it.</summary>
    Private Sub hand_back_closes()
        If closing.Count = 0 Then Return
        Dim pio = ImGui.GetPlatformIO()
        For i = 0 To pio.Viewports.Size - 1
            Dim vp = pio.Viewports(i)
            If closing.Contains(vp.ID) Then vp.PlatformRequestClose = True
        Next
        closing.Clear()
    End Sub

    ' ---- the desktop -------------------------------------------------------

    ''' <summary>cimgui's own entry point, redeclared to hand back a plain
    ''' address. The managed binding returns a POINTER type, which VB has no
    ''' way to hold, and the monitor list has to be written into that struct.
    ''' </summary>
    <DllImport("cimgui", CallingConvention:=CallingConvention.Cdecl,
               EntryPoint:="igGetPlatformIO")>
    Private Function platform_io_addr() As IntPtr
    End Function

    ''' <summary>
    ''' TELL ImGui HOW BIG THE DESKTOP IS. This is what was missing when the
    ''' node editor would not come out of the main window.
    '''
    ''' With an empty monitor list ImGui builds a FALLBACK monitor out of the
    ''' main viewport - the app's own window - and then clamps every window to
    ''' stay inside it. So the drag works, the panel slides to the edge, and
    ''' stops. It was never refusing to tear off; it had a desktop exactly one
    ''' window wide and was keeping the panel on screen, which is the same code
    ''' that stops a window being lost off the side of a monitor.
    '''
    ''' THE VECTOR IS imgui'S, so it gets imgui's allocator. Handing it memory
    ''' from any other heap works perfectly right up to DestroyContext, which
    ''' frees it with IM_FREE and corrupts the heap on the way out - a crash at
    ''' shutdown, a mile from the cause.
    '''
    ''' Read once. A monitor plugged in later would need this run again.
    ''' </summary>
    Private Sub push_monitors()
        Dim pio = platform_io_addr()
        If pio = IntPtr.Zero Then
            LogThis("brain: no platform io - monitors not set, panels will stay inside")
            Return
        End If

        Dim off = CInt(Marshal.OffsetOf(GetType(ImGuiPlatformIO), "Monitors"))
        Dim stride = Marshal.SizeOf(GetType(ImGuiPlatformMonitor))

        ' ImVector is { Integer Size, Integer Capacity, IntPtr Data }.
        Dim old = Marshal.ReadIntPtr(pio, off + 8)
        If old <> IntPtr.Zero Then ImGui.MemFree(old)

        ' PRIMARY FIRST - ImGui treats monitor 0 as the one to fall back on.
        Dim all = Screen.AllScreens
        Dim screens As New List(Of Screen)
        For Each sc In all
            If sc.Primary Then screens.Add(sc)
        Next
        For Each sc In all
            If Not sc.Primary Then screens.Add(sc)
        Next
        If screens.Count = 0 Then Return

        Dim data = ImGui.MemAlloc(CUInt(stride * screens.Count))
        For i = 0 To screens.Count - 1
            Dim b = screens(i).Bounds
            Dim w = screens(i).WorkingArea
            Dim m As New ImGuiPlatformMonitor With {
                .MainPos = New System.Numerics.Vector2(b.X, b.Y),
                .MainSize = New System.Numerics.Vector2(b.Width, b.Height),
                .WorkPos = New System.Numerics.Vector2(w.X, w.Y),
                .WorkSize = New System.Numerics.Vector2(w.Width, w.Height),
                .DpiScale = 1.0F
            }
            Marshal.StructureToPtr(m, IntPtr.Add(data, i * stride), False)
        Next

        Marshal.WriteInt32(pio, off + 0, screens.Count)
        Marshal.WriteInt32(pio, off + 4, screens.Count)
        Marshal.WriteIntPtr(pio, off + 8, data)

        Dim first = screens(0).Bounds
        LogThis("brain: {0} monitor(s) to ImGui, primary {1}x{2}",
                screens.Count, first.Width, first.Height)
    End Sub

    ' ---- the callbacks ----------------------------------------------------

    Private Function vp_of(p As IntPtr) As ImGuiViewportPtr
        Return New ImGuiViewportPtr(p)
    End Function

    Private Function form_of(p As IntPtr) As BrainNodeForm
        Dim f As BrainNodeForm = Nothing
        If forms.TryGetValue(vp_of(p).ID, f) Then Return f
        Return Nothing
    End Function

    Private Sub on_create(p As IntPtr)
        Try
            Dim vp = vp_of(p)
            Dim id = vp.ID
            Dim f As New BrainNodeForm()
            f.Location = New Drawing.Point(CInt(vp.Pos.X), CInt(vp.Pos.Y))
            f.ClientSize = New Drawing.Size(Math.Max(64, CInt(vp.Size.X)),
                                            Math.Max(64, CInt(vp.Size.Y)))
            ' Touching Handle is what actually creates the OS window, and the
            ' context needs a window to sit on. Show() comes later, from
            ' Platform_ShowWindow.
            Dim unused = f.Handle
            AddHandler f.CloseRequested, Sub() closing.Add(id)
            If Not f.Setup() Then
                LogThis("brain: viewport {0} has no context - not adding it", id)
                f.Dispose()
                Return
            End If
            forms(id) = f
        Catch ex As Exception
            LogThis("brain: viewport create failed - {0}", ex.Message)
        End Try
        ' Setup hands the main context back itself, but an exception on the way
        ' through would not have. Make sure.
        Try
            main.MakeCurrent()
        Catch
        End Try
    End Sub

    Private Sub on_destroy(p As IntPtr)
        Try
            Dim id = vp_of(p).ID
            Dim f As BrainNodeForm = Nothing
            If forms.TryGetValue(id, f) Then
                forms.Remove(id)
                f.Teardown()
                f.Dispose()
            End If
        Catch ex As Exception
            LogThis("brain: viewport destroy failed - {0}", ex.Message)
        End Try
        Try
            main.MakeCurrent()
        Catch
        End Try
    End Sub

    Private Sub on_show(p As IntPtr)
        Try
            Dim f = form_of(p)
            If f IsNot Nothing Then f.Show()
        Catch
        End Try
    End Sub

    Private Sub on_set_pos(p As IntPtr, v As System.Numerics.Vector2)
        Try
            Dim f = form_of(p)
            If f IsNot Nothing Then f.Location = New Drawing.Point(CInt(v.X), CInt(v.Y))
        Catch
        End Try
    End Sub

    Private Function on_get_pos(p As IntPtr) As System.Numerics.Vector2
        Try
            ' THE HOST VIEWPORT IS ASKED LIKE ANY OTHER and has no Form. Answer
            ' from the app's own window - miss this and ImGui is told the host
            ' is at the origin and nothing lines up.
            If vp_of(p).ID = mainId Then
                Return New System.Numerics.Vector2(main.Location.X, main.Location.Y)
            End If
            Dim f = form_of(p)
            If f IsNot Nothing Then
                Return New System.Numerics.Vector2(f.Location.X, f.Location.Y)
            End If
        Catch
        End Try
        Return New System.Numerics.Vector2(0.0F, 0.0F)
    End Function

    Private Sub on_set_size(p As IntPtr, v As System.Numerics.Vector2)
        Try
            Dim f = form_of(p)
            If f IsNot Nothing Then
                f.ClientSize = New Drawing.Size(Math.Max(1, CInt(v.X)),
                                                Math.Max(1, CInt(v.Y)))
            End If
        Catch
        End Try
    End Sub

    Private Function on_get_size(p As IntPtr) As System.Numerics.Vector2
        Try
            If vp_of(p).ID = mainId Then
                Return New System.Numerics.Vector2(main.ClientSize.X, main.ClientSize.Y)
            End If
            Dim f = form_of(p)
            If f IsNot Nothing Then
                Return New System.Numerics.Vector2(f.PixelsWide, f.PixelsHigh)
            End If
        Catch
        End Try
        Return New System.Numerics.Vector2(64.0F, 64.0F)
    End Function

    Private Sub on_focus(p As IntPtr)
        Try
            Dim f = form_of(p)
            If f IsNot Nothing Then f.Activate()
        Catch
        End Try
    End Sub

    Private Function on_has_focus(p As IntPtr) As Byte
        Try
            If vp_of(p).ID = mainId Then Return If(main.IsFocused, CByte(1), CByte(0))
            Dim f = form_of(p)
            If f IsNot Nothing AndAlso f.ContainsFocus Then Return 1
        Catch
        End Try
        Return 0
    End Function

    Private Function on_minimized(p As IntPtr) As Byte
        Try
            Dim f = form_of(p)
            If f IsNot Nothing AndAlso f.WindowState = FormWindowState.Minimized Then Return 1
        Catch
        End Try
        Return 0
    End Function

    Private Sub on_title(p As IntPtr, title As IntPtr)
        Try
            Dim f = form_of(p)
            If f IsNot Nothing Then f.Text = Marshal.PtrToStringUTF8(title)
        Catch
        End Try
    End Sub

    Private Sub on_render(p As IntPtr, arg As IntPtr)
        Try
            Dim f = form_of(p)
            If f Is Nothing OrElse Not f.Ready Then Return
            f.MakeCurrent()
            Dim vp = vp_of(p)
            GL.Viewport(0, 0, f.PixelsWide, f.PixelsHigh)
            If (vp.Flags And ImGuiViewportFlags.NoRendererClear) = 0 Then
                GL.ClearColor(0.055F, 0.07F, 0.09F, 1.0F)
                GL.Clear(ClearBufferMask.ColorBufferBit)
            End If
            ctl.RenderViewport(vp)
        Catch ex As Exception
            LogThis("brain: viewport render failed - {0}", ex.Message)
        End Try
    End Sub

    Private Sub on_swap(p As IntPtr, arg As IntPtr)
        Try
            Dim f = form_of(p)
            If f Is Nothing OrElse Not f.Ready Then Return
            f.MakeCurrent()
            f.Present()
        Catch ex As Exception
            LogThis("brain: viewport swap failed - {0}", ex.Message)
        End Try
    End Sub

End Module

Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports ImGuiNET

''' <summary>
''' THE BORDERLESS FORM AND THE GL CONTEXT ON IT.
'''
''' Split out from BrainNodeForm because the two do different jobs: that module
''' runs a frame, this is the window and the plumbing under it. Win32 and WGL
''' declarations are noisy and belong behind one door.
'''
''' WHY WGL BY HAND AND NOT OpenTK.GLControl, which exists and does exactly
''' this: its 4.0.2 package wants OpenTK.Windowing.Desktop 4.9.3 and this app
''' is pinned to 4.7.1. Taking it would drag the whole app forward two minor
''' versions, and BrainTesting compiles about twenty-five of nuTerra's own
''' source files - so the bump would land in another session's lane, for a
''' node editor window. A hundred lines of WGL is the cheaper side of that.
'''
''' THE CONTEXT IS NOT SHARED, on purpose. That means it needs a 4.5 CORE
''' context, which plain wglCreateContext cannot give - it hands back a 2.1
''' compatibility context, and ImGui's renderer needs VAOs. The real one comes
''' from wglCreateContextAttribsARB, which is itself an extension and so can
''' only be looked up while some other context is already current. That is why
''' Setup has to be called with the main window's context live.
'''
''' INPUT IS BUFFERED, NOT POSTED. WinForms events arrive during the message
''' pump, which is the middle of the MAIN window's frame - the current ImGui
''' context at that moment is the main one, so posting an event there would
''' send this window's clicks to the panel. The handlers write into plain
''' fields and PostInput drains them once the right context is current.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Public Class Canvas
    Inherits Form

    ' ---- win32 and WGL ----------------------------------------------------
    <StructLayout(LayoutKind.Sequential)>
    Private Structure PIXELFORMATDESCRIPTOR
        Public nSize As UShort
        Public nVersion As UShort
        Public dwFlags As UInteger
        Public iPixelType As Byte
        Public cColorBits As Byte
        Public cRedBits As Byte, cRedShift As Byte
        Public cGreenBits As Byte, cGreenShift As Byte
        Public cBlueBits As Byte, cBlueShift As Byte
        Public cAlphaBits As Byte, cAlphaShift As Byte
        Public cAccumBits As Byte
        Public cAccumRedBits As Byte, cAccumGreenBits As Byte
        Public cAccumBlueBits As Byte, cAccumAlphaBits As Byte
        Public cDepthBits As Byte, cStencilBits As Byte
        Public cAuxBuffers As Byte
        Public iLayerType As Byte, bReserved As Byte
        Public dwLayerMask As UInteger, dwVisibleMask As UInteger, dwDamageMask As UInteger
    End Structure

    Private Const PFD_DRAW_TO_WINDOW As UInteger = &H4UI
    Private Const PFD_SUPPORT_OPENGL As UInteger = &H20UI
    Private Const PFD_DOUBLEBUFFER As UInteger = &H1UI

    Private Const CTX_MAJOR As Integer = &H2091
    Private Const CTX_MINOR As Integer = &H2092
    Private Const CTX_PROFILE_MASK As Integer = &H9126
    Private Const CTX_CORE_BIT As Integer = &H1

    <DllImport("user32.dll")> Private Shared Function GetDC(h As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function ReleaseDC(h As IntPtr, dc As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function ChoosePixelFormat(dc As IntPtr, ByRef p As PIXELFORMATDESCRIPTOR) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetPixelFormat(dc As IntPtr, f As Integer, ByRef p As PIXELFORMATDESCRIPTOR) As Boolean
    End Function
    <DllImport("gdi32.dll", EntryPoint:="SwapBuffers")> Private Shared Function gdiSwapBuffers(dc As IntPtr) As Boolean
    End Function
    <DllImport("opengl32.dll")> Private Shared Function wglDeleteContext(rc As IntPtr) As Boolean
    End Function
    <DllImport("opengl32.dll")> Private Shared Function wglMakeCurrent(dc As IntPtr, rc As IntPtr) As Boolean
    End Function
    <DllImport("opengl32.dll")> Private Shared Function wglGetCurrentContext() As IntPtr
    End Function
    <DllImport("opengl32.dll")> Private Shared Function wglGetProcAddress(name As String) As IntPtr
    End Function

    <UnmanagedFunctionPointer(CallingConvention.Winapi)>
    Private Delegate Function CreateCtxAttribs(dc As IntPtr, share As IntPtr,
                                               <[In]> attribs As Integer()) As IntPtr

    ' ---- state -------------------------------------------------------------
    Private hdc As IntPtr = IntPtr.Zero
    Private hrc As IntPtr = IntPtr.Zero

    Private mx As Single = -1.0F
    Private my As Single = -1.0F
    Private ReadOnly btn As Boolean() = {False, False, False}
    Private wheel As Single = 0.0F
    Private ReadOnly keyQ As New List(Of KeyEvent)
    Private ReadOnly charQ As New List(Of Char)

    Private Structure KeyEvent
        Public k As ImGuiKey
        Public down As Boolean
    End Structure

    Public ReadOnly Property Ready As Boolean
        Get
            Return hrc <> IntPtr.Zero
        End Get
    End Property

    Public ReadOnly Property PixelsWide As Integer
        Get
            Return Math.Max(64, ClientSize.Width)
        End Get
    End Property

    Public ReadOnly Property PixelsHigh As Integer
        Get
            Return Math.Max(64, ClientSize.Height)
        End Get
    End Property

    Public Sub New()
        ' NO BORDER. The ImGui window covers the whole client and brings its own
        ' title bar and close box, so an OS caption would be a second one.
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        Text = "Brain graph"
        KeyPreview = True
        ' The GL context owns every pixel. Letting WinForms paint the
        ' background as well gives a grey flash on every resize.
        SetStyle(ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.Opaque Or
                 ControlStyles.UserPaint, True)
        UpdateStyles()
    End Sub

    ''' <summary>Build the context. Call with the MAIN context current - see the
    ''' note at the top about wglCreateContextAttribsARB.</summary>
    Public Function Setup() As Boolean
        If hrc <> IntPtr.Zero Then Return True

        hdc = GetDC(Handle)
        If hdc = IntPtr.Zero Then Return False

        Dim pfd As New PIXELFORMATDESCRIPTOR With {
            .nSize = CUShort(Marshal.SizeOf(GetType(PIXELFORMATDESCRIPTOR))),
            .nVersion = 1,
            .dwFlags = PFD_DRAW_TO_WINDOW Or PFD_SUPPORT_OPENGL Or PFD_DOUBLEBUFFER,
            .iPixelType = 0,
            .cColorBits = 32,
            .cAlphaBits = 8,
            .cDepthBits = 0,
            .cStencilBits = 0
        }
        Dim fmt = ChoosePixelFormat(hdc, pfd)
        If fmt = 0 OrElse Not SetPixelFormat(hdc, fmt, pfd) Then
            LogThis("brain: node window - no usable pixel format")
            Return False
        End If

        Dim proc = wglGetProcAddress("wglCreateContextAttribsARB")
        If proc = IntPtr.Zero Then
            LogThis("brain: node window - no wglCreateContextAttribsARB")
            Return False
        End If
        Dim make = Marshal.GetDelegateForFunctionPointer(Of CreateCtxAttribs)(proc)

        ' share = Zero. Nothing is shared with the main window.
        Dim attribs = {CTX_MAJOR, 4, CTX_MINOR, 5,
                       CTX_PROFILE_MASK, CTX_CORE_BIT, 0}
        hrc = make(hdc, IntPtr.Zero, attribs)
        If hrc = IntPtr.Zero Then
            LogThis("brain: node window - could not create a 4.5 core context")
            Return False
        End If
        Return True
    End Function

    Public Sub MakeCurrent()
        If hrc <> IntPtr.Zero Then wglMakeCurrent(hdc, hrc)
    End Sub

    Public Sub Present()
        If hdc <> IntPtr.Zero Then gdiSwapBuffers(hdc)
    End Sub

    Public Sub Teardown()
        Try
            If hrc <> IntPtr.Zero Then
                If wglGetCurrentContext() = hrc Then wglMakeCurrent(IntPtr.Zero, IntPtr.Zero)
                wglDeleteContext(hrc)
                hrc = IntPtr.Zero
            End If
            If hdc <> IntPtr.Zero Then
                ReleaseDC(Handle, hdc)
                hdc = IntPtr.Zero
            End If
        Catch ex As Exception
            LogThis("brain: node window teardown - {0}", ex.Message)
        End Try
    End Sub

    ' ---- input -------------------------------------------------------------

    ''' <summary>Drain what the handlers collected into ImGui. Called from the
    ''' frame, with this window's ImGui context current.</summary>
    Public Sub PostInput(io As ImGuiIOPtr)
        io.AddMousePosEvent(mx, my)
        io.AddMouseButtonEvent(0, btn(0))
        io.AddMouseButtonEvent(1, btn(1))
        io.AddMouseButtonEvent(2, btn(2))
        If wheel <> 0.0F Then
            io.AddMouseWheelEvent(0.0F, wheel)
            wheel = 0.0F
        End If
        For Each e In keyQ
            io.AddKeyEvent(e.k, e.down)
        Next
        keyQ.Clear()
        For Each c In charQ
            io.AddInputCharacter(AscW(c))
        Next
        charQ.Clear()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        mx = e.X : my = e.Y
        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        ' Off the window is not "at the last place it was seen": a button that
        ' is still hovered after the pointer has gone stays lit.
        mx = -Single.MaxValue : my = -Single.MaxValue
        MyBase.OnMouseLeave(e)
    End Sub

    Private Sub set_btn(b As MouseButtons, down As Boolean)
        Select Case b
            Case MouseButtons.Left : btn(0) = down
            Case MouseButtons.Right : btn(1) = down
            Case MouseButtons.Middle : btn(2) = down
        End Select
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        mx = e.X : my = e.Y
        set_btn(e.Button, True)
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        mx = e.X : my = e.Y
        set_btn(e.Button, False)
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        wheel += e.Delta / 120.0F
        MyBase.OnMouseWheel(e)
    End Sub

    Private Shared Function map_key(k As Keys) As ImGuiKey
        Select Case k
            Case Keys.Delete : Return ImGuiKey.Delete
            Case Keys.Back : Return ImGuiKey.Backspace
            Case Keys.Escape : Return ImGuiKey.Escape
            Case Keys.Enter : Return ImGuiKey.Enter
            Case Keys.Tab : Return ImGuiKey.Tab
            Case Keys.Left : Return ImGuiKey.LeftArrow
            Case Keys.Right : Return ImGuiKey.RightArrow
            Case Keys.Up : Return ImGuiKey.UpArrow
            Case Keys.Down : Return ImGuiKey.DownArrow
            Case Keys.Home : Return ImGuiKey.Home
            Case Keys.End : Return ImGuiKey.End
            Case Keys.ControlKey : Return ImGuiKey.LeftCtrl
            Case Keys.ShiftKey : Return ImGuiKey.LeftShift
            Case Keys.Menu : Return ImGuiKey.LeftAlt
            Case Else : Return ImGuiKey.None
        End Select
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Dim k = map_key(e.KeyCode)
        If k <> ImGuiKey.None Then keyQ.Add(New KeyEvent With {.k = k, .down = True})
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        Dim k = map_key(e.KeyCode)
        If k <> ImGuiKey.None Then keyQ.Add(New KeyEvent With {.k = k, .down = False})
        MyBase.OnKeyUp(e)
    End Sub

    Protected Overrides Sub OnKeyPress(e As KeyPressEventArgs)
        If Not Char.IsControl(e.KeyChar) Then charQ.Add(e.KeyChar)
        MyBase.OnKeyPress(e)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Alt+F4 hides it rather than destroying the context behind the
        ' renderer's back. The node editor's own close box does the same thing
        ' through BrainNodes.SHOW.
        If e.CloseReason <> CloseReason.ApplicationExitCall Then
            e.Cancel = True
            BrainNodes.SHOW = False
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class

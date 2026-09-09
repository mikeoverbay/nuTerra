Imports System.Runtime.CompilerServices
Imports ImGuiNET
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

Public Class ImGuiController
    Implements IDisposable

    Private _frameBegun As Boolean

    Private _vertexArray As GLVertexArray
    Private _vertexBuffer As GLBuffer
    Private _vertexBufferSize As Integer
    Private _indexBuffer As GLBuffer
    Private _indexBufferSize As Integer

    Private _fontTexture As GLTexture

    Private _windowWidth As Integer
    Private _windowHeight As Integer

    Private _scaleFactor As System.Numerics.Vector2 = System.Numerics.Vector2.One

    ''' <summary>The monospaced font the shader IDE edits in, when Consolas was found.</summary>
    Public Shared MONO_FONT As ImFontPtr
    Public Shared HAS_MONO As Boolean = False

    Public Sub New(width As Integer, height As Integer)

        _windowWidth = width
        _windowHeight = height

        Dim context = ImGui.CreateContext()
        ImGui.SetCurrentContext(context)
        Dim io = ImGui.GetIO()

        io.Fonts.AddFontFromFileTTF(System.IO.Path.Combine(Application.StartupPath, "resources", "SourceSans3-Regular.ttf"), 20.0F, Nothing, io.Fonts.GetGlyphRangesCyrillic())

        ' A monospaced face for the shader IDE. Its highlight is painted over
        ' the text box column by column, which only lines up in a fixed-pitch
        ' font. Consolas ships with Windows; when it is missing the IDE says so
        ' and falls back to the UI font.
        Dim mono = "C:\Windows\Fonts\consola.ttf"
        If System.IO.File.Exists(mono) Then
            MONO_FONT = io.Fonts.AddFontFromFileTTF(mono, 17.0F)
            HAS_MONO = True
        End If

        io.BackendFlags = io.BackendFlags Or ImGuiBackendFlags.RendererHasVtxOffset

        CreateDeviceResources()

        SetPerFrameImGuiData(1.0F / 60.0F)

        ImGui.NewFrame()
        _frameBegun = True
    End Sub

    Public Sub WindowResized(width As Integer, height As Integer)
        _windowWidth = width
        _windowHeight = height
    End Sub

    Public Sub DestroyDeviceObjects()
        IDisposable_Dispose()
    End Sub

    Public Sub CreateDeviceResources()
        _vertexArray = GLVertexArray.Create("ImGui")

        _vertexBufferSize = 10000
        _indexBufferSize = 2000

        _vertexBuffer = GLBuffer.Create(BufferTarget.ArrayBuffer, "ImGui")
        _indexBuffer = GLBuffer.Create(BufferTarget.ElementArrayBuffer, "ImGui")

        GL.NamedBufferData(_vertexBuffer.buffer_id, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw)
        GL.NamedBufferData(_indexBuffer.buffer_id, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw)

        RecreateFontDeviceTexture()

        _vertexArray.VertexBuffer(0, _vertexBuffer, IntPtr.Zero, Unsafe.SizeOf(Of ImDrawVert))
        _vertexArray.ElementBuffer(_indexBuffer)

        _vertexArray.EnableAttrib(0)
        _vertexArray.AttribBinding(0, 0)
        _vertexArray.AttribFormat(0, 2, VertexAttribType.Float, False, 0)

        _vertexArray.EnableAttrib(1)
        _vertexArray.AttribBinding(1, 0)
        _vertexArray.AttribFormat(1, 2, VertexAttribType.Float, False, 8)

        _vertexArray.EnableAttrib(2)
        _vertexArray.AttribBinding(2, 0)
        _vertexArray.AttribFormat(2, 4, VertexAttribType.UnsignedByte, True, 16)
    End Sub

    Public Sub RecreateFontDeviceTexture()
        Dim io = ImGui.GetIO()
        Dim pixels As IntPtr
        Dim width As Integer
        Dim height As Integer
        Dim bytesPerPixel As Integer
        io.Fonts.GetTexDataAsRGBA32(pixels, width, height, bytesPerPixel)

        _fontTexture = GLTexture.Create(TextureTarget.Texture2D, "ImGui Text Atlas")
        _fontTexture.Storage2D(1, SizedInternalFormat.Rgba8, width, height)
        _fontTexture.SubImage2D(0, 0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels)
        _fontTexture.Parameter(TextureParameterName.TextureMaxLevel, 0)
        _fontTexture.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        _fontTexture.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        _fontTexture.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        _fontTexture.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

        io.Fonts.SetTexID(_fontTexture.texture_id)

        io.Fonts.ClearTexData()
    End Sub

    Public Sub Render()
        If _frameBegun Then
            _frameBegun = False
            ImGui.Render()
            RenderImDrawData(ImGui.GetDrawData())
        End If
    End Sub

    Public Sub Update(wnd As GameWindow, deltaSeconds As Single)
        If _frameBegun Then
            ImGui.Render()
        End If

        SetPerFrameImGuiData(deltaSeconds)
        UpdateImGuiInput(wnd)

        _frameBegun = True
        ImGui.NewFrame()
    End Sub

    Private Sub SetPerFrameImGuiData(deltaSeconds As Single)
        Dim io = ImGui.GetIO()
        io.DisplaySize = New System.Numerics.Vector2(
                _windowWidth / _scaleFactor.X,
                _windowHeight / _scaleFactor.Y)
        io.DisplayFramebufferScale = _scaleFactor
        ' Floored, never zero. ImGui asserts "Need a positive DeltaTime!" on
        ' anything not strictly positive from 1.88 on; 1.87 let exactly zero
        ' through, which is why this only started firing on the upgrade.
        ' ForceRender's own signature defaults time to 0.0 - it repaints the
        ' window mid-load, where there is no frame time to speak of - so a zero
        ' really does arrive here. The floor lives at this one choke point
        ' rather than in each caller, and is an epsilon rather than a plausible
        ' frame so nothing time-based lurches forward during a load.
        io.DeltaTime = Math.Max(deltaSeconds, 0.0001F)
    End Sub

    ReadOnly PressedChars As New List(Of Char)

    Private Sub UpdateImGuiInput(wnd As GameWindow)
        Dim io = ImGui.GetIO()

        Dim MouseState = wnd.MouseState
        Dim KeyboardState = wnd.KeyboardState

        ' Events, not fields. io.KeysDown and io.KeyMap were removed in Dear
        ' ImGui 1.90 along with the whole legacy key path; a backend now posts
        ' what happened and ImGui keeps the state. AddKeyEvent and its mouse
        ' counterparts drop a repeat that matches what they already hold, so
        ' sweeping every key each frame costs no queue traffic when nothing
        ' moved.
        io.AddMousePosEvent(MouseState.X, MouseState.Y)
        io.AddMouseButtonEvent(0, MouseState(MouseButton.Left))
        io.AddMouseButtonEvent(1, MouseState(MouseButton.Right))
        io.AddMouseButtonEvent(2, MouseState(MouseButton.Middle))

        For Each kv In KEY_PAIRS
            io.AddKeyEvent(kv.Value, KeyboardState.IsKeyDown(kv.Key))
        Next

        For Each c In PressedChars
            io.AddInputCharacter(AscW(c))
        Next
        PressedChars.Clear()

        ' Ctrl / Alt / Shift / Super are NOT set here any more. Since 1.89
        ' ImGui derives io.KeyCtrl and the rest from the Left/Right key states
        ' during NewFrame, and those go through KEY_PAIRS above like every
        ' other key. Writing them by hand as well would be a second source of
        ' truth that disagrees for one frame whenever the two paths differ.
    End Sub

    ''' <summary>
    ''' Every OpenTK key this backend forwards, paired with the ImGuiKey it
    ''' means, worked out once at class load rather than per frame.
    ''' </summary>
    Private Shared ReadOnly KEY_PAIRS As KeyValuePair(Of Keys, ImGuiKey)() = BuildKeyPairs()

    Private Shared Function BuildKeyPairs() As KeyValuePair(Of Keys, ImGuiKey)()
        Dim pairs As New List(Of KeyValuePair(Of Keys, ImGuiKey))
        For Each k As Keys In [Enum].GetValues(GetType(Keys))
            If k = Keys.Unknown Then Continue For
            Dim ik = ToImGuiKey(k)
            If ik <> ImGuiKey.None Then pairs.Add(New KeyValuePair(Of Keys, ImGuiKey)(k, ik))
        Next
        Return pairs.ToArray()
    End Function

    ''' <summary>
    ''' OpenTK's key to ImGui's. ImGuiKey.None for anything ImGui has no name
    ''' for - those keys are simply not forwarded, which is what the legacy
    ''' KeyMap did by omission.
    ''' </summary>
    Private Shared Function ToImGuiKey(k As Keys) As ImGuiKey
        Select Case k
            Case Keys.Tab : Return ImGuiKey.Tab
            Case Keys.Left : Return ImGuiKey.LeftArrow
            Case Keys.Right : Return ImGuiKey.RightArrow
            Case Keys.Up : Return ImGuiKey.UpArrow
            Case Keys.Down : Return ImGuiKey.DownArrow
            Case Keys.PageUp : Return ImGuiKey.PageUp
            Case Keys.PageDown : Return ImGuiKey.PageDown
            Case Keys.Home : Return ImGuiKey.Home
            Case Keys.End : Return ImGuiKey.End
            Case Keys.Insert : Return ImGuiKey.Insert
            Case Keys.Delete : Return ImGuiKey.Delete
            Case Keys.Backspace : Return ImGuiKey.Backspace
            Case Keys.Space : Return ImGuiKey.Space
            Case Keys.Enter : Return ImGuiKey.Enter
            Case Keys.Escape : Return ImGuiKey.Escape
            Case Keys.Apostrophe : Return ImGuiKey.Apostrophe
            Case Keys.Comma : Return ImGuiKey.Comma
            Case Keys.Minus : Return ImGuiKey.Minus
            Case Keys.Period : Return ImGuiKey.Period
            Case Keys.Slash : Return ImGuiKey.Slash
            Case Keys.Semicolon : Return ImGuiKey.Semicolon
            Case Keys.Equal : Return ImGuiKey.Equal
            Case Keys.LeftBracket : Return ImGuiKey.LeftBracket
            Case Keys.Backslash : Return ImGuiKey.Backslash
            Case Keys.RightBracket : Return ImGuiKey.RightBracket
            Case Keys.GraveAccent : Return ImGuiKey.GraveAccent
            Case Keys.CapsLock : Return ImGuiKey.CapsLock
            Case Keys.ScrollLock : Return ImGuiKey.ScrollLock
            Case Keys.NumLock : Return ImGuiKey.NumLock
            Case Keys.PrintScreen : Return ImGuiKey.PrintScreen
            Case Keys.Pause : Return ImGuiKey.Pause
            Case Keys.LeftShift : Return ImGuiKey.LeftShift
            Case Keys.LeftControl : Return ImGuiKey.LeftCtrl
            Case Keys.LeftAlt : Return ImGuiKey.LeftAlt
            Case Keys.LeftSuper : Return ImGuiKey.LeftSuper
            Case Keys.RightShift : Return ImGuiKey.RightShift
            Case Keys.RightControl : Return ImGuiKey.RightCtrl
            Case Keys.RightAlt : Return ImGuiKey.RightAlt
            Case Keys.RightSuper : Return ImGuiKey.RightSuper
            Case Keys.Menu : Return ImGuiKey.Menu
            Case Keys.D0 : Return ImGuiKey._0
            Case Keys.D1 : Return ImGuiKey._1
            Case Keys.D2 : Return ImGuiKey._2
            Case Keys.D3 : Return ImGuiKey._3
            Case Keys.D4 : Return ImGuiKey._4
            Case Keys.D5 : Return ImGuiKey._5
            Case Keys.D6 : Return ImGuiKey._6
            Case Keys.D7 : Return ImGuiKey._7
            Case Keys.D8 : Return ImGuiKey._8
            Case Keys.D9 : Return ImGuiKey._9
            Case Keys.A : Return ImGuiKey.A
            Case Keys.B : Return ImGuiKey.B
            Case Keys.C : Return ImGuiKey.C
            Case Keys.D : Return ImGuiKey.D
            Case Keys.E : Return ImGuiKey.E
            Case Keys.F : Return ImGuiKey.F
            Case Keys.G : Return ImGuiKey.G
            Case Keys.H : Return ImGuiKey.H
            Case Keys.I : Return ImGuiKey.I
            Case Keys.J : Return ImGuiKey.J
            Case Keys.K : Return ImGuiKey.K
            Case Keys.L : Return ImGuiKey.L
            Case Keys.M : Return ImGuiKey.M
            Case Keys.N : Return ImGuiKey.N
            Case Keys.O : Return ImGuiKey.O
            Case Keys.P : Return ImGuiKey.P
            Case Keys.Q : Return ImGuiKey.Q
            Case Keys.R : Return ImGuiKey.R
            Case Keys.S : Return ImGuiKey.S
            Case Keys.T : Return ImGuiKey.T
            Case Keys.U : Return ImGuiKey.U
            Case Keys.V : Return ImGuiKey.V
            Case Keys.W : Return ImGuiKey.W
            Case Keys.X : Return ImGuiKey.X
            Case Keys.Y : Return ImGuiKey.Y
            Case Keys.Z : Return ImGuiKey.Z
            Case Keys.F1 : Return ImGuiKey.F1
            Case Keys.F2 : Return ImGuiKey.F2
            Case Keys.F3 : Return ImGuiKey.F3
            Case Keys.F4 : Return ImGuiKey.F4
            Case Keys.F5 : Return ImGuiKey.F5
            Case Keys.F6 : Return ImGuiKey.F6
            Case Keys.F7 : Return ImGuiKey.F7
            Case Keys.F8 : Return ImGuiKey.F8
            Case Keys.F9 : Return ImGuiKey.F9
            Case Keys.F10 : Return ImGuiKey.F10
            Case Keys.F11 : Return ImGuiKey.F11
            Case Keys.F12 : Return ImGuiKey.F12
            Case Keys.KeyPad0 : Return ImGuiKey.Keypad0
            Case Keys.KeyPad1 : Return ImGuiKey.Keypad1
            Case Keys.KeyPad2 : Return ImGuiKey.Keypad2
            Case Keys.KeyPad3 : Return ImGuiKey.Keypad3
            Case Keys.KeyPad4 : Return ImGuiKey.Keypad4
            Case Keys.KeyPad5 : Return ImGuiKey.Keypad5
            Case Keys.KeyPad6 : Return ImGuiKey.Keypad6
            Case Keys.KeyPad7 : Return ImGuiKey.Keypad7
            Case Keys.KeyPad8 : Return ImGuiKey.Keypad8
            Case Keys.KeyPad9 : Return ImGuiKey.Keypad9
            Case Keys.KeyPadDecimal : Return ImGuiKey.KeypadDecimal
            Case Keys.KeyPadDivide : Return ImGuiKey.KeypadDivide
            Case Keys.KeyPadMultiply : Return ImGuiKey.KeypadMultiply
            Case Keys.KeyPadSubtract : Return ImGuiKey.KeypadSubtract
            Case Keys.KeyPadAdd : Return ImGuiKey.KeypadAdd
            Case Keys.KeyPadEnter : Return ImGuiKey.KeypadEnter
            Case Keys.KeyPadEqual : Return ImGuiKey.KeypadEqual
            Case Else : Return ImGuiKey.None
        End Select
    End Function

    Public Sub PressChar(keyChar As Char)
        PressedChars.Add(keyChar)
    End Sub

    Public Sub MouseScroll(offset As Vector2)
        ' An event rather than an assignment: two notches inside one frame add
        ' up instead of the second overwriting the first.
        ImGui.GetIO().AddMouseWheelEvent(offset.X, offset.Y)
    End Sub

    Private Sub RenderImDrawData(draw_data As ImDrawDataPtr)
        If draw_data.CmdListsCount = 0 Then
            Return
        End If

        For i = 0 To draw_data.CmdListsCount - 1
            Dim cmd_list = draw_data.CmdLists(i)

            Dim vertexSize = cmd_list.VtxBuffer.Size * Unsafe.SizeOf(Of ImDrawVert)
            If vertexSize > _vertexBufferSize Then
                Dim newSize = CInt(Math.Max(_vertexBufferSize * 1.5F, vertexSize))
                GL.NamedBufferData(_vertexBuffer.buffer_id, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw)
                _vertexBufferSize = newSize
            End If


            Dim indexSize = cmd_list.IdxBuffer.Size * Unsafe.SizeOf(Of UShort)
            If indexSize > _indexBufferSize Then
                Dim newSize = CInt(Math.Max(_indexBufferSize * 1.5F, indexSize))
                GL.NamedBufferData(_indexBuffer.buffer_id, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw)
                _indexBufferSize = newSize
            End If
        Next

        Dim IO = ImGui.GetIO()
        Dim mvp = Matrix4.CreateOrthographicOffCenter(
                0.0F,
                IO.DisplaySize.X,
                IO.DisplaySize.Y,
                0.0F,
                -1.0F,
                1.0F)

        imguiShader.Use()
        GL.UniformMatrix4(imguiShader("projection_matrix"), False, mvp)
        GL.Uniform1(imguiShader("in_fontTexture"), 0)

        _vertexArray.Bind()

        draw_data.ScaleClipRects(IO.DisplayFramebufferScale)

        GL.Enable(EnableCap.Blend)
        GL.Enable(EnableCap.ScissorTest)
        GL.BlendEquation(BlendEquationMode.FuncAdd)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Disable(EnableCap.CullFace)
        GL.Disable(EnableCap.DepthTest)

        For n = 0 To draw_data.CmdListsCount - 1
            Dim cmd_list = draw_data.CmdLists(n)

            GL.NamedBufferSubData(_vertexBuffer.buffer_id, IntPtr.Zero, cmd_list.VtxBuffer.Size * Unsafe.SizeOf(Of ImDrawVert), cmd_list.VtxBuffer.Data)

            GL.NamedBufferSubData(_indexBuffer.buffer_id, IntPtr.Zero, cmd_list.IdxBuffer.Size * Unsafe.SizeOf(Of UShort), cmd_list.IdxBuffer.Data)

            For cmd_i = 0 To cmd_list.CmdBuffer.Size - 1
                Dim pcmd = cmd_list.CmdBuffer(cmd_i)
                If pcmd.UserCallback <> IntPtr.Zero Then
                    Throw New NotImplementedException()
                Else
                    GL.BindTextureUnit(0, CInt(pcmd.TextureId))

                    Dim clip = pcmd.ClipRect
                    GL.Scissor(CInt(clip.X), _windowHeight - CInt(clip.W), CInt(clip.Z - clip.X), CInt(clip.W - clip.Y))

                    If (IO.BackendFlags And ImGuiBackendFlags.RendererHasVtxOffset) <> 0 Then
                        GL.DrawElementsBaseVertex(PrimitiveType.Triangles, CInt(pcmd.ElemCount), DrawElementsType.UnsignedShort, New IntPtr(pcmd.IdxOffset * Unsafe.SizeOf(Of UShort)), pcmd.VtxOffset)
                    Else
                        GL.DrawElements(BeginMode.Triangles, CInt(pcmd.ElemCount), DrawElementsType.UnsignedShort, CInt(pcmd.IdxOffset) * Unsafe.SizeOf(Of UShort))
                        Stop
                    End If
                End If
            Next
        Next

        imguiShader.StopUse()

        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.ScissorTest)
    End Sub

    Private Sub IDisposable_Dispose() Implements IDisposable.Dispose
        _fontTexture?.Dispose()
    End Sub
End Class

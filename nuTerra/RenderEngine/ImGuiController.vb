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

    Public Sub New(width As Integer, height As Integer)

        _windowWidth = width
        _windowHeight = height

        Dim context = ImGui.CreateContext()
        ImGui.SetCurrentContext(context)
        Dim IO = ImGui.GetIO()
        IO.Fonts.AddFontDefault()

        IO.BackendFlags = IO.BackendFlags Or ImGuiBackendFlags.RendererHasVtxOffset

        CreateDeviceResources()
        SetKeyMappings()

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
        io.DeltaTime = deltaSeconds
    End Sub

    ReadOnly PressedChars As New List(Of Char)

    Private Sub UpdateImGuiInput(wnd As GameWindow)
        Dim io = ImGui.GetIO()

        Dim MouseState = wnd.MouseState
        Dim KeyboardState = wnd.KeyboardState

        Unsafe.AsRef(io.MouseDown(0)) = MouseState(MouseButton.Left)
        Unsafe.AsRef(io.MouseDown(1)) = MouseState(MouseButton.Right)
        Unsafe.AsRef(io.MouseDown(2)) = MouseState(MouseButton.Middle)

        Dim screenPoint = New Vector2i(MouseState.X, MouseState.Y)
        Dim point = screenPoint
        io.MousePos = New System.Numerics.Vector2(point.X, point.Y)

        For Each key In [Enum].GetValues(GetType(Keys))
            If key = Keys.Unknown Then
                Continue For
            End If
            Unsafe.AsRef(io.KeysDown.Item(key)) = KeyboardState.IsKeyDown(key)
        Next

        For Each c In PressedChars
            io.AddInputCharacter(AscW(c))
        Next
        PressedChars.Clear()

        io.KeyCtrl = KeyboardState.IsKeyDown(Keys.LeftControl) OrElse KeyboardState.IsKeyDown(Keys.RightControl)
        io.KeyAlt = KeyboardState.IsKeyDown(Keys.LeftAlt) OrElse KeyboardState.IsKeyDown(Keys.RightAlt)
        io.KeyShift = KeyboardState.IsKeyDown(Keys.LeftShift) OrElse KeyboardState.IsKeyDown(Keys.RightShift)
        io.KeySuper = KeyboardState.IsKeyDown(Keys.LeftSuper) OrElse KeyboardState.IsKeyDown(Keys.RightSuper)
    End Sub

    Public Sub PressChar(keyChar As Char)
        PressedChars.Add(keyChar)
    End Sub

    Public Sub MouseScroll(offset As Vector2)
        Dim IO = ImGui.GetIO()

        IO.MouseWheel = offset.Y
        IO.MouseWheelH = offset.X
    End Sub

    Private Shared Sub SetKeyMappings()
        Dim io = ImGui.GetIO()
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Tab))) = CInt(Keys.Tab)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.LeftArrow))) = CInt(Keys.Left)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.RightArrow))) = CInt(Keys.Right)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.UpArrow))) = CInt(Keys.Up)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.DownArrow))) = CInt(Keys.Down)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.PageUp))) = CInt(Keys.PageUp)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.PageDown))) = CInt(Keys.PageDown)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Home))) = CInt(Keys.Home)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.End))) = CInt(Keys.End)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Delete))) = CInt(Keys.Delete)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Backspace))) = CInt(Keys.Backspace)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Enter))) = CInt(Keys.Enter)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Escape))) = CInt(Keys.Escape)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.A))) = CInt(Keys.A)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.C))) = CInt(Keys.C)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.V))) = CInt(Keys.V)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.X))) = CInt(Keys.X)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Y))) = CInt(Keys.Y)
        Unsafe.AsRef(io.KeyMap(CInt(ImGuiKey.Z))) = CInt(Keys.Z)
    End Sub

    Private Sub RenderImDrawData(draw_data As ImDrawDataPtr)
        If draw_data.CmdListsCount = 0 Then
            Return
        End If

        For i = 0 To draw_data.CmdListsCount - 1
            Dim cmd_list = draw_data.CmdListsRange(i)

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
            Dim cmd_list = draw_data.CmdListsRange(n)

            GL.NamedBufferSubData(_vertexBuffer.buffer_id, IntPtr.Zero, cmd_list.VtxBuffer.Size * Unsafe.SizeOf(Of ImDrawVert), cmd_list.VtxBuffer.Data)

            GL.NamedBufferSubData(_indexBuffer.buffer_id, IntPtr.Zero, cmd_list.IdxBuffer.Size * Unsafe.SizeOf(Of UShort), cmd_list.IdxBuffer.Data)

            Dim vtx_offset = 0
            Dim idx_offset = 0

            For cmd_i = 0 To cmd_list.CmdBuffer.Size - 1
                Dim pcmd = cmd_list.CmdBuffer(cmd_i)
                If pcmd.UserCallback <> IntPtr.Zero Then
                    Throw New NotImplementedException()
                Else
                    GL.BindTextureUnit(0, CInt(pcmd.TextureId))

                    Dim clip = pcmd.ClipRect
                    GL.Scissor(CInt(clip.X), _windowHeight - CInt(clip.W), CInt(clip.Z - clip.X), CInt(clip.W - clip.Y))

                    If (IO.BackendFlags And ImGuiBackendFlags.RendererHasVtxOffset) <> 0 Then
                        GL.DrawElementsBaseVertex(PrimitiveType.Triangles, CInt(pcmd.ElemCount), DrawElementsType.UnsignedShort, New IntPtr(idx_offset * Unsafe.SizeOf(Of UShort)), vtx_offset)
                    Else
                        GL.DrawElements(BeginMode.Triangles, CInt(pcmd.ElemCount), DrawElementsType.UnsignedShort, CInt(pcmd.IdxOffset) * Unsafe.SizeOf(Of UShort))
                        Stop
                    End If
                End If

                idx_offset += CInt(pcmd.ElemCount)
            Next
            vtx_offset += cmd_list.VtxBuffer.Size
        Next

        imguiShader.StopUse()

        GL.Disable(EnableCap.Blend)
        GL.Disable(EnableCap.ScissorTest)
    End Sub

    Private Sub IDisposable_Dispose() Implements IDisposable.Dispose
        _fontTexture?.Dispose()
    End Sub
End Class

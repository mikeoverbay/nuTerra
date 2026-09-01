Imports System.Drawing.Imaging
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports ImGuiNET
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports OpenTK.Windowing
Imports OpenTK.Windowing.Common
Imports OpenTK.Windowing.Desktop
Imports OpenTK.Windowing.GraphicsLibraryFramework

Public Class Window
    Inherits GameWindow

    ' HACK
    Public Shared SCR_WIDTH As Integer = 1200
    Public Shared SCR_HEIGHT As Integer = 800
    Public Shared mouse_last_pos As Point
    Private NEED_TO_INVALIDATE_VIEWPORT As Boolean = True
    Private NEED_TO_DO_SCREEN_CAPTURE As Boolean = False
    Public SHADER_CHANGED As Boolean = False
    Private SCREEN_CAPTURE_FILENAME As String = Nothing
    Private fps_timer As New Stopwatch

    Private _controller As ImGuiController

    Private SHOW_SETTINGS_WINDOW As Boolean
    Private SHOW_TEXTURES_VIEWER_WINDOW As Boolean
    Private prev_SHOW_SETTINGS_WINDOW As Boolean = False

    ' Position and size of the menu bar window, recorded each frame so the
    ' panels below can be parked underneath it. Named for the bar, not for what
    ' reads them - there is a real stats window now and the old name invited a
    ' mix-up.
    Private menubar_size As System.Numerics.Vector2 = System.Numerics.Vector2.Zero
    Private menubar_pos As System.Numerics.Vector2 = System.Numerics.Vector2.Zero

    '''<summary>Set when the Stats button opens the panel, so it reappears under
    ''' the bar rather than wherever it was last dragged - which may be off the
    ''' edge of a since-resized window, i.e. gone.</summary>
    Private reset_stats_pos As Boolean = True

    ' Mouse camera state. OnMouseMove accumulates raw pixel travel here;
    ' camera_mouse_update consumes it once per frame.
    Private mouse_dx As Single
    Private mouse_dy As Single

    ' Rotation damping, transcribed from three.js OrbitControls (MIT,
    ' mrdoob/three.js, examples/jsm/controls/OrbitControls.js). The whole
    ' mechanism is one pending-delta accumulator per axis: mouse input adds
    ' into it, and every frame the camera takes dampingFactor of what is
    ' pending while the remainder decays -
    '
    '     spherical.theta += sphericalDelta.theta * dampingFactor;
    '     ...
    '     sphericalDelta.theta *= ( 1 - dampingFactor );
    '
    ' Smoothed drag and release-coast are the same three lines: while
    ' dragging the pending pool fills faster than it drains, and whatever is
    ' still pending at release plays out as momentum.
    Private rot_delta_x As Single
    Private rot_delta_y As Single

    ' Zoom rides the same mechanism, in the log domain: the old step was
    ' radius *= (1 + 2.4 * dy), so the pending pool holds the exponent and
    ' each frame applies exp(pending * f). Sign can never flip and the
    ' compounding stays exact however the frames slice it.
    Private zoom_delta As Single

    ' Pan (middle / Ctrl+left drag) pends in WORLD metres, converted from
    ' screen axes at input time - OrbitControls does the same with its
    ' panOffset - so a coasting pan holds its direction even if the camera
    ' turns during the glide.
    Private pan_delta_x As Single
    Private pan_delta_z As Single

    ' camera_mouse_update's own clock. It runs in OnUpdateFrame, which spins
    ' unthrottled - measured 127,000 calls per second - so DELTA_TIME (the
    ' RENDER frame's ~5 ms) is the wrong dt by a factor of ~600 and made
    ' every damping attempt feel direct. Real elapsed time per call keeps the
    ' math right at any cadence.
    Private rot_clock As New Stopwatch

    Private Shared Function GetGLSettings() As NativeWindowSettings
        ' Harness runs open at half size: a quarter of the pixels to render,
        ' read back and analyse per iteration, and nothing in the FX maths
        ' depends on resolution.
        If HALF_SIZE_WINDOW Then
            SCR_WIDTH = Math.Max(320, SCR_WIDTH \ 2)
            SCR_HEIGHT = Math.Max(240, SCR_HEIGHT \ 2)
        End If

        Dim setting As New NativeWindowSettings With {
            .Size = New Vector2i(SCR_WIDTH, SCR_HEIGHT),
            .API = Common.ContextAPI.OpenGL,
            .APIVersion = New Version(4, 5),
            .Profile = ContextProfile.Core,
            .Flags = ContextFlags.ForwardCompatible,
            .DepthBits = 0,
            .AlphaBits = 0,
            .StencilBits = 0,
            .Title = Application.ProductName
        }
#If DEBUG Then
        setting.Flags = setting.Flags Or ContextFlags.Debug
#End If

        ' BEGIN HACK
        Dim appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)
        Dim bmpIcon = appIcon.ToBitmap()

        Dim data = bmpIcon.LockBits(New Rectangle(0, 0, bmpIcon.Width, bmpIcon.Height),
                                    Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb)

        Dim numbytes = data.Stride * bmpIcon.Height
        Dim bytes(numbytes) As Byte

        Marshal.Copy(data.Scan0, bytes, 0, numbytes)

        ' BGR TO RGB
        For i = 0 To numbytes - 1 Step 4
            Dim tmp = bytes(i)
            bytes(i) = bytes(i + 2)
            bytes(i + 2) = tmp
        Next

        setting.Icon = New Input.WindowIcon(New Input.Image(bmpIcon.Width, bmpIcon.Height, bytes))

        bmpIcon.UnlockBits(data)
        ' END HACK

        Return setting
    End Function

    Public Sub New()
        MyBase.New(
            New GameWindowSettings With {
                .IsMultiThreaded = True,
                .RenderFrequency = 0.0,
                .UpdateFrequency = 0.0
            }, GetGLSettings())
        VSync = VSyncMode.Off
    End Sub

    Protected Overrides Sub OnLoad()
        MyBase.OnLoad()
        'Check context:
        Dim majorVersion = GL.GetInteger(GetPName.MajorVersion)
        Dim minorVersion = GL.GetInteger(GetPName.MinorVersion)
        If majorVersion < 4 Or (majorVersion = 4 AndAlso minorVersion < 3) Then
            MsgBox("A graphics card and driver with support for OpenGL 4.3 or higher is required.")
            Application.Exit()
            Return
        End If

        Dim launch_timer As New Stopwatch

        '-----------------------------------------------------------------------------------------
        'need a work area on users disc
        TEMP_STORAGE = Path.Combine(Path.GetTempPath, "nuTerra")
        If Not Directory.Exists(TEMP_STORAGE) Then
            Directory.CreateDirectory(TEMP_STORAGE)
        End If
        LogThis("{0}ms Temp storage is located at: {1}", launch_timer.ElapsedMilliseconds, TEMP_STORAGE)

        ' Put the shipped per-map settings in place before any map can load.
        modMapSettings.SeedWorkFolder()

        LogThis("Vendor: {0}", GL.GetString(StringName.Vendor))
        LogThis("Renderer: {0}", GL.GetString(StringName.Renderer))
        LogThis("Version: {0}", GL.GetString(StringName.Version))
        LogThis("GLSL Version: {0}", GL.GetString(StringName.ShadingLanguageVersion))

        Dim extensions As New List(Of String)
        Dim numExt As Integer = GL.GetInteger(GetPName.NumExtensions)
        For i = 0 To numExt - 1
            extensions.Add(GL.GetString(StringNameIndexed.Extensions, i))
        Next

        Dim requied_extensions = {
            "GL_ARB_vertex_type_10f_11f_11f_rev",
            "GL_ARB_compute_variable_group_size",
            "GL_ARB_shading_language_include",
            "GL_ARB_bindless_texture",
            "GL_ARB_multi_draw_indirect", 'core since 4.3
            "GL_ARB_direct_state_access", 'core since 4.5
            "GL_ARB_clip_control", 'core since 4.5
            "GL_ARB_indirect_parameters", 'core since 4.6
            "GL_ARB_shader_draw_parameters", 'core since 4.6
            "GL_ARB_shader_atomic_counter_ops" 'core since 4.6
        }

        Dim unsupported_ext As New List(Of String)
        For Each ext In requied_extensions
            If Not extensions.Contains(ext) Then
                unsupported_ext.Add(ext)
            End If
        Next

        ' https://renderdoc.org/docs/getting_started/faq.html#can-i-tell-via-the-graphics-apis-if-renderdoc-Is-present-at-runtime
        Dim debug_tool = GL.IsEnabled(GL_DEBUG_TOOL_EXT)
        GL.GetError() ' Clear last error

        ' skip checks if we are in RenderDoc 
        If Not debug_tool Then
            If unsupported_ext.Count > 0 Then
                MsgBox(String.Format(
                       "A graphics card and driver with support for {0} is required.", String.Join(" ", unsupported_ext)))
                Application.Exit()
                Return
            End If
        End If

        '-----------------------------------------------------------------------------------------
        'Any relevant info the user could use.
        GLCapabilities.Init(extensions)
        '-----------------------------------------------------------------------------------------

        USE_REPRESENTATIVE_TEST = GLCapabilities.has_GL_NV_representative_fragment_test

#If DEBUG Then
        ' Just check
        Debug.Assert(extensions.Contains("GL_KHR_debug"))
        Debug.Assert(extensions.Contains("GL_ARB_debug_output"))

        If GL.GetInteger(GetPName.ContextFlags) And ContextFlagMask.ContextFlagDebugBit Then
            LogThis("Setup Debug Output Callback")
            SetupDebugOutputCallback()
        End If
#End If

        ' Set depth to [0..1] range instead of [-1..1]
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne)

        ' Enable depth clamping
        GL.Enable(EnableCap.DepthClamp)

        GL.ClearColor(0.0F, 0.0F, 0.0F, 0.0F)
        GL.ClearDepth(0.0F)

        '-----------------------------------------------------------------------------------------
        'Check if the game path is set
        If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
            MsgBox("Path to game is not set!" + vbCrLf +
                    "Lets set it now.", MsgBoxStyle.OkOnly, "Game Path not set")
            m_set_game_path()

            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("This application will be closed because game was not found!")
                Application.Exit()
                Return
            End If
        End If

        LogThis("{0}ms Game Path: {1}", launch_timer.ElapsedMilliseconds, My.Settings.GamePath)

        ' Create default VAO
        defaultVao = GLVertexArray.Create("defaultVao")

        make_cube() ' used for many draw functions

        CommonPropertiesBuffer = GLBuffer.Create(BufferTarget.UniformBuffer, "CommonProperties")
        CommonPropertiesBuffer.StorageNullData(
            Marshal.SizeOf(CommonProperties),
            BufferStorageFlags.DynamicStorageBit)
        CommonPropertiesBuffer.BindBase(2)

        CommonProperties.Init()
        FieldOfView = CSng(Math.PI) * (My.Settings.fov / 180.0F)

        'Get block state of things we want to block loading to speed things up for testing/debugging
        DONT_BLOCK_BASES = My.Settings.load_bases
        DONT_BLOCK_DECALS = My.Settings.load_decals
        DONT_BLOCK_MODELS = My.Settings.load_models
        DONT_BLOCK_SKY = My.Settings.load_sky
        DONT_BLOCK_TERRAIN = My.Settings.load_terrain
        DONT_BLOCK_OUTLAND = My.Settings.load_outland
        DONT_BLOCK_TREES = My.Settings.load_trees
        DONT_BLOCK_WATER = My.Settings.load_water

        ' Tessellation persists now. It was session-only for years and so
        ' effectively always off - meanwhile the game's terrain shaders carry
        ' hull/domain stages unconditionally: it tessellates always, out to
        ' ~60 m. On by default to match.
        USE_TESSELLATION = My.Settings.use_tessellation

        ' Decal edge fading: on, using DecalEdgeProbe's own classification.
        ' Set here rather than relying on the field initialisers so the startup
        ' state is unambiguous and sits next to the flags it belongs with.
        DECAL_EDGE_FADE = True
        ' USE_SH_AMBIENT is restored from My.Settings by CommonProperties.Init()

        ' Everything modMapSettings manages now has its startup value, so this is
        ' the point to record what a map with no saved file should fall back to.
        ' Must come before the first load_map.
        modMapSettings.CaptureDefaults()

        ShadowMappingFBO.FBO_Initialize()
        LogThis("{0}ms FBO ShadowMapping Created.", launch_timer.ElapsedMilliseconds)

        MiniMapFBO.FBO_Initialize(240) '<- default start up size
        LogThis("{0}ms FBO Mini Created.", launch_timer.ElapsedMilliseconds)

        build_shaders()
        LogThis("{0}ms Shaders Built.", launch_timer.ElapsedMilliseconds)

        load_assets()
        LogThis("{0}ms Assets Loaded.", launch_timer.ElapsedMilliseconds)

        '-----------------------------------------------------------------------------------------
        LogThis("{0}ms Starting Update Thread", launch_timer.ElapsedMilliseconds)

        SHOW_MAPS_SCREEN = True '<---- Un-rem to show map menu at startup.

        _controller = New ImGuiController(ClientSize.X, ClientSize.Y)

        If STARTUP_MAP IsNot Nothing Then
            MapMenuScreen.MAP_TO_LOAD = STARTUP_MAP
            MapMenuScreen.MAP_DESCRIPTION = STARTUP_MAP
        End If

        fps_timer.Start()
    End Sub

    Private Sub m_set_game_path()
        Dim FolderBrowserDialog1 As New FolderBrowserDialog

        'Sets the game path folder
try_again:
        If FolderBrowserDialog1.ShowDialog = DialogResult.OK Then
            My.Settings.GamePath = FolderBrowserDialog1.SelectedPath
            If Not Directory.Exists(Path.Combine(My.Settings.GamePath, "res")) Then
                MsgBox("Wrong Folder Path!" + vbCrLf +
                       "You need to point at the World_of_Tanks folder!",
                        MsgBoxStyle.Exclamation, "Wrong Path!")
                GoTo try_again
            End If
        End If
    End Sub

    Protected Overrides Sub OnResize(e As ResizeEventArgs)
        MyBase.OnResize(e)

        Dim OLD_SCR_WIDTH = SCR_WIDTH
        Dim OLD_SCR_HEIGHT = SCR_HEIGHT

        SCR_WIDTH = Math.Max(1, ClientSize.X)
        SCR_HEIGHT = Math.Max(1, ClientSize.Y)

        If OLD_SCR_WIDTH <> SCR_WIDTH OrElse OLD_SCR_HEIGHT <> SCR_HEIGHT Then
            NEED_TO_INVALIDATE_VIEWPORT = True
        End If

        If Not IsMultiThreaded Then
            ForceRender()
        End If
    End Sub

    Protected Overrides Sub OnRenderFrame(args As FrameEventArgs)
        MyBase.OnRenderFrame(args)

        DELTA_TIME = args.Time
        If Not FREEZE_FX Then
            FX_TIME += DELTA_TIME
            If FX_TIME > 3600.0F Then FX_TIME -= 3600.0F
        End If

        ' Particles ride the same freeze as the rest of the FX so a frozen
        ' frame really is reproducible.
        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso Not FREEZE_FX Then
            map_scene.particles.Update(CSng(DELTA_TIME))
        End If

        ' Unattended Snapshot. Counted in frames, not seconds, so it cannot
        ' fire before the cull buckets have been filled at least once.
        If AUTO_SNAP_FRAMES > 0 AndAlso MAP_LOADED Then
            AUTO_SNAP_FRAMES -= 1
            ' One frame earlier than the Snapshot, so the readback's own stall
            ' cannot distort the timings the Snapshot prints.
            If AUTO_SNAP_FRAMES = 1 Then FX_DIFF_THIS_FRAME = True
            If AUTO_SNAP_FRAMES = 0 Then
                write_log_snapshot()
                If AUTO_SNAP_QUIT Then Close()
            End If
        End If

        If fps_timer.ElapsedMilliseconds > 1000 Then
            fps_timer.Restart()
            FPS_TIME = FPS_COUNTER
            FPS_COUNTER = 0
        End If

        ForceRender(args.Time)

        If MapMenuScreen.MAP_TO_LOAD IsNot Nothing Then
            Dim map_name = MapMenuScreen.MAP_TO_LOAD
            MapMenuScreen.MAP_TO_LOAD = Nothing
            load_map(map_name)

            ' Both names in the title - the friendly one to read, the space name
            ' because that is what the settings file and the packages use.
            Dim pretty = MapMenuScreen.MAP_REALNAME
            If String.IsNullOrEmpty(pretty) OrElse pretty = map_name Then
                Title = String.Format("{0} - {1}", Application.ProductName, map_name)
            Else
                Title = String.Format("{0} - {1} ({2})", Application.ProductName, pretty, map_name)
            End If
        End If
    End Sub

    Public Sub ForceRender(Optional time As Single = 0.0)
        If SHADER_CHANGED Then
            SHADER_CHANGED = False

            For Each sh In shaders
                sh.UpdateShader()
            Next
        End If

        If NEED_TO_INVALIDATE_VIEWPORT Then
            _controller.WindowResized(SCR_WIDTH, SCR_HEIGHT)
            MainFBO.Initialize(SCR_WIDTH, SCR_HEIGHT)

            NEED_TO_INVALIDATE_VIEWPORT = False
        End If

        If map_scene IsNot Nothing Then
            map_scene.camera.check_postion_for_update()
        End If

        draw_scene()

        If SCREEN_CAPTURE_FILENAME IsNot Nothing Then
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1)

            Using bmp As New Bitmap(MainFBO.width, MainFBO.height, Imaging.PixelFormat.Format24bppRgb)
                Dim bitmapData = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                          ImageLockMode.WriteOnly,
                                          bmp.PixelFormat)

                GL.ReadPixels(0, 0, MainFBO.width, MainFBO.height, OpenGL.PixelFormat.Bgr, PixelType.UnsignedByte, bitmapData.Scan0)

                bmp.UnlockBits(bitmapData)
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY)
                bmp.Save(SCREEN_CAPTURE_FILENAME, ImageFormat.Png)
            End Using

            GL.PixelStore(PixelStoreParameter.PackAlignment, 4)
            GL.ReadBuffer(ReadBufferMode.Front)

            SCREEN_CAPTURE_FILENAME = Nothing
        End If

        _controller.Update(Me, CSng(time))
        Dim viewport = ImGui.GetMainViewport()

        If SHOW_MAPS_SCREEN Then
            MapMenuScreen.SubmitUI(viewport)
        End If

        If SHOW_LOADING_SCREEN Then
            ImGui.SetNextWindowPos(viewport.Pos)
            ImGui.SetNextWindowSize(viewport.Size)
            If ImGui.Begin("##Dummy ProgressBar Window", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings) Then
                ImGui.ProgressBar(BG_VALUE / BG_MAX_VALUE, New Numerics.Vector2(-1.0F, 0.0F))
                ImGui.Text(BG_TEXT)
                For Each line In split_sentences(MapMenuScreen.MAP_DESCRIPTION)
                    ImGui.TextWrapped(line)
                Next
            End If
        Else
            SubmitUI(viewport)
        End If

        _controller.Render()

        SwapBuffers()
        FPS_COUNTER += 1
    End Sub

    '''<summary>
    ''' What survived clipping this frame, against what there is. The model
    ''' numbers come back from the cull compute shader's atomic counters, the
    ''' tree number from the CPU box test, and the terrain number from the per
    ''' chunk visible flag.
    '''</summary>
    ''' <summary>
    ''' Breaks a map description into sentences on ". ", putting the period back
    ''' on each one. A map blurb is a single run-on paragraph otherwise, and
    ''' TextWrapped gives it no structure to hang on.
    ''' </summary>
    Private Function split_sentences(text As String) As List(Of String)
        Dim out As New List(Of String)
        If String.IsNullOrWhiteSpace(text) Then Return out

        Dim parts = text.Split(New String() {". "}, StringSplitOptions.None)
        For i = 0 To parts.Length - 1
            Dim p = parts(i).Trim()
            If p.Length = 0 Then Continue For

            ' the split ate the period on everything but the last piece, and the
            ' last one may already end in its own punctuation
            If i < parts.Length - 1 Then
                p &= "."
            ElseIf Not p.EndsWith(".") AndAlso Not p.EndsWith("!") AndAlso Not p.EndsWith("?") Then
                p &= "."
            End If

            out.Add(p)
        Next
        Return out
    End Function

    ' Burnt orange, #CC5500. Opaque enough to kill whatever is behind it.
    Private Shared ReadOnly SLAB_COLOUR As New Numerics.Vector4(0.8F, 0.333F, 0.0F, 0.88F)
    Private Shared ReadOnly SLAB_TEXT As New Numerics.Vector4(1.0F, 1.0F, 1.0F, 1.0F)

    ''' <summary>
    ''' Draws a line of text on a filled slab. The toolbar window is NoBackground,
    ''' so the stats were being read against whatever terrain happened to be under
    ''' them - unreadable over bright ground, and worse over the sky. A solid
    ''' backing gives them one constant surface instead.
    ''' </summary>
    Private Sub text_on_slab(s As String)
        Const PAD_X As Single = 5.0F
        Const PAD_Y As Single = 2.0F

        Dim pos = ImGui.GetCursorScreenPos()
        Dim size = ImGui.CalcTextSize(s)

        ' Submitted before the text so it lands behind it - a draw list is
        ' painted in submission order.
        ImGui.GetWindowDrawList().AddRectFilled(
            New Numerics.Vector2(pos.X - PAD_X, pos.Y - PAD_Y),
            New Numerics.Vector2(pos.X + size.X + PAD_X, pos.Y + size.Y + PAD_Y),
            ImGui.GetColorU32(SLAB_COLOUR), 3.0F)

        ImGui.TextColored(SLAB_TEXT, s)
    End Sub

    ''' <summary>
    ''' Dumps the current render state to the log and forces it to disk.
    '''
    ''' The flush is the point. LogThis is Console.WriteLine, and when stdout is
    ''' redirected to a file that stream is buffered - so a line written mid
    ''' session can sit unwritten indefinitely, which makes the log useless for
    ''' anything but post-mortem. This writes a marked block and flushes it, so
    ''' the file can be read while the app is still running.
    '''
    ''' Diagnostics that only fire at map load - the sun shadow bake in
    ''' particular - can be re-taken from here at any time.
    ''' </summary>
    ''' <summary>
    ''' The render stats panel. Everything that used to be crammed onto the menu
    ''' bar, plus per-pass GPU time, in a window that can be dragged out of the
    ''' way of whatever is being looked at.
    '''
    ''' Deliberately translucent: it sits over the scene it is describing, and an
    ''' opaque panel hides the thing whose cost it is reporting.
    ''' </summary>
    ' Colour key for the VT page debug view (Settings -> VT -> Page debug
    ' view). Must match VTDebugColor in shaders/common.h.
    Private Shared ReadOnly VT_DEBUG_COLORS As Single()() = {
        New Single() {1.0F, 0.15F, 0.15F}, New Single() {1.0F, 0.55F, 0.1F},
        New Single() {1.0F, 1.0F, 0.15F}, New Single() {0.25F, 0.9F, 0.2F},
        New Single() {0.15F, 0.9F, 0.9F}, New Single() {0.2F, 0.4F, 1.0F},
        New Single() {0.6F, 0.3F, 1.0F}, New Single() {1.0F, 0.25F, 1.0F},
        New Single() {0.6F, 0.4F, 0.2F}, New Single() {0.9F, 0.9F, 0.9F},
        New Single() {0.35F, 0.35F, 0.35F}}

    Private Sub draw_vt_debug_key()
        If Not VT_PAGE_DEBUG Then Return
        ImGui.SetNextWindowPos(New System.Numerics.Vector2(12, 90), ImGuiCond.FirstUseEver)
        ImGui.SetNextWindowSize(New System.Numerics.Vector2(230, 395), ImGuiCond.FirstUseEver)
        If ImGui.Begin("VT page key", VT_PAGE_DEBUG) Then
            ' Flip the overlay without leaving the window - the scene keeps
            ' rendering normally underneath, so before/after is one click.
            ImGui.Checkbox("Colour overlay", VT_PAGE_DEBUG_COLOR)
            ImGui.SliderFloat("Blend", VT_PAGE_DEBUG_MIX, 0.0F, 1.0F)
            ' Greyscale mipfract instead of page colours: the moving bands ARE
            ' the trilinear blend sweeping between two coarse pages.
            ImGui.Checkbox("Show mip blend", VT_DEBUG_MIPFRACT)
            ' Affects REAL rendering, not just the overlay: snap trilinear to
            ' the nearest page. Flicker gone with this on = the morph between
            ' independently-baked coarse mips is the flicker.
            ImGui.Checkbox("Nearest mip (test)", VT_NEAREST_MIP)
            ImGui.Separator()
            ImGui.TextUnformatted("colour = resident page mip")
            ImGui.TextUnformatted("checker = page cells")
            ImGui.Separator()
            For m = 0 To VT_DEBUG_COLORS.Length - 1
                Dim c = VT_DEBUG_COLORS(m)
                ImGui.ColorButton("##vtkey" & m,
                                  New System.Numerics.Vector4(c(0), c(1), c(2), 1.0F))
                ImGui.SameLine()
                ' One page covers 256 * 2^mip virtual texels per side.
                ImGui.TextUnformatted(String.Format("mip {0}  ({1}^2 texels)", m, TILE_SIZE << m))
            Next
        End If
        ImGui.End()
    End Sub

    Private Sub draw_stats_window()
        If Not SHOW_STATS_WINDOW Then Return

        ' Grey, mostly transparent, and a little rounder than the default so it
        ' reads as an overlay rather than a dialog.
        ImGui.PushStyleColor(ImGuiCol.WindowBg, New System.Numerics.Vector4(0.1F, 0.1F, 0.1F, 0.55F))
        ImGui.PushStyleColor(ImGuiCol.TitleBg, New System.Numerics.Vector4(0.15F, 0.15F, 0.15F, 0.7F))
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, New System.Numerics.Vector4(0.2F, 0.2F, 0.2F, 0.8F))
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6.0F)

        ImGui.SetNextWindowSizeConstraints(New System.Numerics.Vector2(260, 0),
                                           New System.Numerics.Vector2(600, 900))

        ' Park it under the menu bar every time it is opened. A window dragged
        ' near an edge and then left there is simply gone after the main window
        ' is resized smaller, with no way back to it - so the open action always
        ' puts it somewhere on screen rather than restoring where it was.
        If reset_stats_pos Then
            Dim vp = ImGui.GetMainViewport()
            Dim pos = New System.Numerics.Vector2(vp.WorkPos.X + 10.0F, vp.WorkPos.Y + 10.0F)
            If menubar_size.LengthSquared > 0 Then
                pos = New System.Numerics.Vector2(menubar_pos.X, menubar_pos.Y + menubar_size.Y + 5.0F)
            End If

            ' Clamp into the viewport, in case the bar itself is near an edge.
            pos.X = Math.Min(Math.Max(pos.X, vp.WorkPos.X), vp.WorkPos.X + Math.Max(vp.WorkSize.X - 280.0F, 0.0F))
            pos.Y = Math.Min(Math.Max(pos.Y, vp.WorkPos.Y), vp.WorkPos.Y + Math.Max(vp.WorkSize.Y - 120.0F, 0.0F))

            ImGui.SetNextWindowPos(pos)
            reset_stats_pos = False
        End If

        If ImGui.Begin("Render stats", SHOW_STATS_WINDOW, ImGuiWindowFlags.AlwaysAutoResize) Then
            ImGui.Text(String.Format("FPS {0}", FPS_TIME))
            ImGui.Text(String.Format("VRAM {0} / {1} mb",
                                     GLCapabilities.memory_usage, GLCapabilities.total_mem_mb))

            If MAP_LOADED AndAlso Not SHOW_MAPS_SCREEN Then
                ImGui.Separator()
                ImGui.TextUnformatted(clip_counts())

                ImGui.Separator()
                ' GL_TIME_ELAPSED per pass, read a frame late so asking does not
                ' stall the pipeline it is measuring. See modGpuTimers.
                ImGui.Text("GPU time per pass (ms)")

                Dim sections = modGpuTimers.Sections
                If sections.Count = 0 Then
                    ImGui.TextDisabled("   measuring...")
                Else
                    Dim total = modGpuTimers.TotalMs
                    Dim worst = 0.0
                    For Each sec In sections
                        worst = Math.Max(worst, sec.avg_ms)
                    Next

                    If ImGui.BeginTable("##passtimes", 3, ImGuiTableFlags.SizingFixedFit) Then
                        For Each sec In sections
                            ImGui.TableNextRow()
                            ImGui.TableSetColumnIndex(0)
                            ImGui.TextUnformatted(sec.name)
                            ImGui.TableSetColumnIndex(1)
                            ' The most expensive pass in red, so the thing worth
                            ' looking at is findable without reading the numbers.
                            If sec.avg_ms >= worst AndAlso worst > 0.0 Then
                                ImGui.TextColored(New System.Numerics.Vector4(1.0F, 0.5F, 0.4F, 1.0F),
                                                  String.Format("{0,7:0.000}", sec.avg_ms))
                            Else
                                ImGui.TextUnformatted(String.Format("{0,7:0.000}", sec.avg_ms))
                            End If
                            ImGui.TableSetColumnIndex(2)
                            Dim pct = If(total > 0.0, sec.avg_ms / total * 100.0, 0.0)
                            ImGui.TextDisabled(String.Format("{0,5:0.0}%", pct))
                        Next
                        ImGui.EndTable()
                    End If

                    ImGui.Separator()
                    ' Timed passes only. The gap to the frame time is everything
                    ' not bracketed - post, minimap, ImGui itself, and present.
                    ImGui.Text(String.Format("timed total {0:0.000} ms", total))
                End If
            End If

            ImGui.End()
        End If

        ImGui.PopStyleVar()
        ImGui.PopStyleColor(3)

        ' Closing with the title bar X has to stop the queries too.
        If Not SHOW_STATS_WINDOW Then
            modGpuTimers.Enabled = False
            modGpuTimers.Reset()
        End If
    End Sub

    Private Sub write_log_snapshot()
        ' Tee the block into %TEMP%\nuTerra\snapshot.txt (latest snapshot
        ' wins) so it can be read from outside the console window.
        LOG_TEE = New System.Text.StringBuilder
        Try
            write_log_snapshot_body()
        Finally
            Try
                Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "nuTerra")
                IO.Directory.CreateDirectory(dir)
                IO.File.WriteAllText(IO.Path.Combine(dir, "snapshot.txt"), LOG_TEE.ToString())
            Catch
                ' a locked or unwritable temp file must never break Snapshot
            End Try
            LOG_TEE = Nothing
        End Try
    End Sub

    Private Sub write_log_snapshot_body()
        LogThis("================ SNAPSHOT {0} ================", Date.Now.ToString("HH:mm:ss"))

        If Not MAP_LOADED OrElse map_scene Is Nothing Then
            LogThis("  no map loaded")
            Console.Out.Flush()
            Return
        End If

        LogThis("  map: {0}", MAP_NAME_NO_PATH)
        ' Printed in the exact form the launch argument takes, so a saved
        ' snapshot can be pasted straight back onto the command line and the
        ' identical view comes up again.
        LogThis("  cam={0:0.####},{1:0.####},{2:0.####},{3:0.####},{4:0.####},{5:0.####}",
                map_scene.camera.VIEW_RADIUS, map_scene.camera.CAM_X_ANGLE,
                map_scene.camera.CAM_Y_ANGLE, map_scene.camera.LOOK_AT_X,
                map_scene.camera.LOOK_AT_Y, map_scene.camera.LOOK_AT_Z)
        LogThis("  fps: {0}   vram: {1} of {2} mb", FPS_TIME,
                GLCapabilities.memory_usage, GLCapabilities.total_mem_mb)
        LogThis("  {0}", clip_counts())

        ' The shadow mix, both halves, as deferred.frag multiplies them.
        LogThis("  shadow mix: live={0} strength={1:0.00}   baked={2} strength={3:0.00}",
                ShadowMappingFBO.Enabled, CommonProperties.SHADOW_STRENGTH,
                BAKED_SHADOW_ENABLED, CommonProperties.HORIZON_STRENGTH)
        LogThis("  cascades: {0} x {1}^2, every {2} frames, splits 20/75/250",
                ShadowMappingFBO.CASCADES, ShadowMappingFBO.WIDTH, ShadowMappingFBO.FRAME_STEP)
        LogThis("  sun: LIGHT_POS {0:0.0} {1:0.0} {2:0.0}  (len {3:0.0})",
                LIGHT_POS.X, LIGHT_POS.Y, LIGHT_POS.Z, LIGHT_POS.Length)

        map_scene.sun_shadow.LogSnapshot()

        LogThis("  water: loaded={0} draw={1}", map_scene.WATER_LOADED, DONT_BLOCK_WATER)

        ' The probe field's state, including what the FX pass gets as opposed to
        ' the deferred pass. offset is printed as deferred/fx because they are
        ' deliberately different: the 1.5 m wall push is wrong for smoke cards.
        ' Without this line a control run has no in-band proof the FX pass was
        ' actually handed the same field as the ground.
        LogThis("  sh grid: loaded={0} use={1} fx={2} mix={3:0.00} offset={4:0.0}/{5:0.0} fade={6:0.0} centre=({7:0.#},{8:0.#}) size=({9:0.#},{10:0.#})",
                SH_GRID_LOADED, USE_SH_GRID, USE_SH_GRID_FX, SH_GRID_MIX,
                SH_GRID_OFFSET, SH_GRID_OFFSET_FX, SH_GRID_FADE,
                SH_GRID_CENTRE.X, SH_GRID_CENTRE.Z, SH_GRID_SIZE.X, SH_GRID_SIZE.Z)

        ' FX diagnostics: the four cull buckets (fx is the volumetric pass's
        ' draw count this frame), and whether anything upset GL since the last
        ' time someone asked.
        With map_scene.static_models
            LogThis("  model buckets: opaque={0} dbl={1} glass={2} fx={3}  (of {4} candidates)",
                    .numAfterFrustum(0), .numAfterFrustum(1), .numAfterFrustum(2), .numAfterFrustum(3),
                    .indirectDrawCount)
            ' Churn in the FX draw order since the last snapshot. A handful is
            ' normal (culling changes the list); a large number while the
            ' camera moves means sort-order swaps, which flicker overlaps.
            LogThis("  fx sort: order changes since last snapshot={0}", .fx_sort_order_changes)
            .fx_sort_order_changes = 0
            ' Name every FX model in the bucket - the picker cannot.
            .LogFxBucket()
        End With
        Dim gl_err = GL.GetError()
        LogThis("  glGetError: {0}", gl_err)

        Console.Out.Flush()
    End Sub

    Private Function clip_counts() As String
        Dim models = 0, model_total = 0
        If map_scene IsNot Nothing AndAlso map_scene.MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            Dim sm = map_scene.static_models
            models = sm.numAfterFrustum(0) + sm.numAfterFrustum(1) + sm.numAfterFrustum(2)
            model_total = sm.numModelInstances
        End If

        Dim chunks = 0, chunk_total = 0
        If theMap.render_set IsNot Nothing Then
            chunk_total = theMap.render_set.Length
            For i = 0 To chunk_total - 1
                If theMap.render_set(i).visible Then chunks += 1
            Next
        End If

        ' Outland cull blocks drawn/total per cascade - the counters are only
        ' fresh while Draw_outland runs, so show them only when it does.
        Dim outland = ""
        If map_scene IsNot Nothing AndAlso map_scene.OUTLAND_LOADED AndAlso DONT_BLOCK_OUTLAND AndAlso
           map_scene.terrain.outland_near_blocks IsNot Nothing Then
            With map_scene.terrain
                outland = String.Format(" | outland blocks {0}/{1}+{2}/{3}",
                                        .outland_near_blocks_drawn, .outland_near_blocks.Length,
                                        .outland_far_blocks_drawn,
                                        If(.outland_far_blocks Is Nothing, 0, .outland_far_blocks.Length))
            End With
        End If

        ' The model figure counts draw commands that survived culling, not
        ' instances, so it is not a ratio of model_total and is not shown as one.
        Return String.Format("| model draws {0} of {1} instances | trees {2}/{3} lods {6} | chunks {4}/{5}",
                             models, model_total, TREES_DRAWN, TREES_TOTAL, chunks, chunk_total,
                             If(TREES_LOD_TEXT = "", "-", TREES_LOD_TEXT)) & outland
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyboardKeyEventArgs)
        MyBase.OnKeyDown(e)

        If _controller IsNot Nothing AndAlso ImGui.GetIO().WantCaptureKeyboard Then
            Return
        End If

        Select Case e.Key
            Case Keys.A
                WASD_VECTOR.X = -3.0F
            Case Keys.D
                WASD_VECTOR.X = 3.0F
            Case Keys.W
                WASD_VECTOR.Y = -3.0F
            Case Keys.S
                WASD_VECTOR.Y = 3.0F
            Case Keys.LeftShift
                Z_MOVE = True
            Case Keys.LeftControl
                MOVE_MOD = True
            Case Keys.Equal
                If MINI_MAP_NEW_SIZE < 640 Then mini_map_new_size +=20
            Case Keys.Minus
                If MINI_MAP_NEW_SIZE > 240 Then MINI_MAP_NEW_SIZE -= 20
        End Select
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyboardKeyEventArgs)
        MyBase.OnKeyUp(e)

        If _controller IsNot Nothing AndAlso ImGui.GetIO().WantCaptureKeyboard Then
            Return
        End If

        Z_MOVE = False
        MOVE_MOD = False
        Select Case e.Key
            Case Keys.A
                WASD_VECTOR.X = 0
            Case Keys.D
                WASD_VECTOR.X = 0
            Case Keys.W
                WASD_VECTOR.Y = 0
            Case Keys.S
                WASD_VECTOR.Y = 0
        End Select
    End Sub

    Private Sub WASD_movement()
        If WASD_VECTOR.X <> 0 OrElse WASD_VECTOR.Y <> 0 Then
            WASD_SPEED += DELTA_TIME * 0.01
            If WASD_SPEED > 0.025F Then
                WASD_SPEED = 0F
                Dim MAX = -200.0F
                If MAX < map_scene.camera.VIEW_RADIUS Then
                    MAX = map_scene.camera.VIEW_RADIUS
                End If
                Dim ms As Single = 0.2F * MAX ' distance away changes speed.. THIS WORKS WELL!
                Dim t = WASD_VECTOR.X * ms * 0.003

                If WASD_VECTOR.X <> 0 Then
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (-Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                End If

                t = WASD_VECTOR.Y * ms * 0.003F

                If WASD_VECTOR.Y <> 0 Then
                    map_scene.camera.LOOK_AT_Z -= ((t * ms) * (Math.Cos(map_scene.camera.CAM_X_ANGLE)))
                    map_scene.camera.LOOK_AT_X -= ((t * ms) * (Math.Sin(map_scene.camera.CAM_X_ANGLE)))
                End If

            End If
        End If
    End Sub

    Private Sub load_assets()
        ' Init packages
        ResMgr.Init(My.Settings.GamePath)

        'Loads the textures for the map selection routines
        MapMenuScreen.Init()

        CHECKER_BOARD = TextureMgr.load_png_image_from_file("CheckerPatternPaper.png", False, False)
        DIRECTION_TEXTURE_ID = TextureMgr.load_png_image_from_file("direction.png", True, False)
        nuTERRA_BG_IMAGE = TextureMgr.load_png_image_from_file("earth.png", False, True)

        DUMMY_TEXTURE_ID = TextureMgr.make_dummy_texture()
        make_dummy_4_layer_atlas()

        TextureMgr.imgTbl.Clear()
    End Sub

    Protected Overrides Sub OnUpdateFrame(args As FrameEventArgs)
        MyBase.OnUpdateFrame(args)

        ' All mouse camera response, once per frame - before the ImGui-capture
        ' early-out so a flung rotation keeps coasting across the UI.
        camera_mouse_update()

        Dim io = ImGui.GetIO()
        If _controller IsNot Nothing AndAlso (io.WantCaptureKeyboard OrElse io.WantCaptureMouse) Then
            Return
        End If

        If Not IsFocused Then
            Return
        End If

        Dim input = KeyboardState
        Dim mouse = MouseState

        If mouse.IsButtonDown(MouseButton.Left) Then
            If MINI_MOUSE_CAPTURED Then
                'User clicked on the mini so lets move to that locations in world space
                map_scene.camera.LOOK_AT_X = MINI_WORLD_MOUSE_POSITION.X
                map_scene.camera.LOOK_AT_Z = MINI_WORLD_MOUSE_POSITION.Y
            End If
        End If

        If mouse.IsButtonDown(MouseButton.Right) Then
            MOVE_CAM_Z = True
        End If

        If mouse.IsButtonDown(MouseButton.Middle) Then
            MOVE_MOD = True
            M_DOWN = True
        End If

        If mouse.IsButtonDown(MouseButton.Left) Then
            M_DOWN = True
        End If

        ' HACK!
        mouse_last_pos = New Point(mouse.Position.X, mouse.Position.Y)

        WASD_movement()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseMoveEventArgs)
        MyBase.OnMouseMove(e)

        Dim io = ImGui.GetIO()
        If _controller IsNot Nothing AndAlso (io.WantCaptureKeyboard OrElse io.WantCaptureMouse) Then
            Return
        End If

        If BLOCK_MOUSE Then Return

        M_MOUSE.X = e.X
        M_MOUSE.Y = e.Y

        ' The camera no longer responds here. The old per-event handler mixed
        ' a 5 px dead zone, sin() shaping and a once-per-frame last-position
        ' hack into visible stepping - and its single-line If/Else bindings
        ' meant half the branches applied from the other side's Else. All the
        ' response now lives in camera_mouse_update, once per frame; this only
        ' accumulates the frame's mouse travel.
        mouse_dx += e.DeltaX
        mouse_dy += e.DeltaY
    End Sub

    ''' <summary>
    ''' Per-frame mouse camera control, fed by the deltas OnMouseMove
    ''' accumulates. Runs every update, before the ImGui capture early-out,
    ''' so a flung rotation keeps coasting while the cursor crosses the UI.
    '''
    '''   rotate (left drag)      - velocity chases the mouse rate (ROT_ACCEL)
    '''                             and coasts to a stop on release (ROT_COAST)
    '''   pan (middle/Ctrl drag)  - direct, as before
    '''   height (Shift drag)     - direct
    '''   zoom (right drag)       - direct, radius-scaled
    '''
    ''' The velocity is radians per SECOND and every response is dt-corrected:
    ''' this app runs uncapped, and per-frame impulses at 200+ fps are so
    ''' small that the first accel/coast attempt was imperceptible.
    ''' </summary>
    Private Sub camera_mouse_update()
        If map_scene Is Nothing Then
            mouse_dx = 0
            mouse_dy = 0
            Return
        End If

        ' NOT DELTA_TIME: that is the render frame's time, and this runs in
        ' the unthrottled update loop - see rot_clock.
        Dim dt As Single = 0.016F
        If rot_clock.IsRunning Then
            dt = Math.Clamp(CSng(rot_clock.Elapsed.TotalSeconds), 0.000001F, 0.1F)
        End If
        rot_clock.Restart()
        ' 100 px of travel ~ "speed" radians - the same scale the old
        ' handler's sin(d/100) gave for ordinary moves.
        Dim dx = mouse_dx / 100.0F * My.Settings.speed
        Dim dy = mouse_dy / 100.0F * My.Settings.speed
        mouse_dx = 0
        mouse_dy = 0

        Dim ms As Single = 0.2F * map_scene.camera.VIEW_RADIUS ' distance away changes speed.. THIS WORKS WELL!
        Dim PITCH_MIN As Single = CSng(-PI / 2.0F + 0.001F)
        Dim PITCH_MAX As Single = 1.3F

        If M_DOWN Then
            If Z_MOVE Then
                map_scene.camera.LOOK_AT_Y -= dy * ms
            ElseIf MOVE_MOD Then
                ' Pan input joins the pool, already rotated into world axes.
                Dim ca = CSng(Math.Cos(map_scene.camera.CAM_X_ANGLE))
                Dim sa = CSng(Math.Sin(map_scene.camera.CAM_X_ANGLE))
                pan_delta_x -= (dx * ms) * ca + (dy * ms) * sa
                pan_delta_z -= (dx * ms) * -sa + (dy * ms) * ca
            Else
                ' rotateLeft / rotateUp: input only ever adds to the pending
                ' delta. (OrbitControls scales by 2pi/clientHeight; dx and dy
                ' already carry this app's traditional px/100 * speed scale.)
                rot_delta_x -= dx
                rot_delta_y -= dy
            End If
        ElseIf MOVE_CAM_Z Then
            ' Right drag: zoom input joins the pending pool (log-scale, same
            ' 12 * 0.2 sensitivity the direct handler used). Applied below.
            zoom_delta += dy * 12.0F * 0.2F
        End If

        ' The OrbitControls update(), verbatim apart from one deviation: the
        ' factor is dt-corrected (theirs is per-render-frame; this app runs
        ' uncapped, and a fixed per-frame factor at 200+ fps drains the pool
        ' before it can be felt - the same mistake that killed the first two
        ' attempts at this feature).
        Dim f = 1.0F - CSng(Math.Pow(1.0F - Math.Min(ROT_DAMPING, 0.999F), dt * 60.0F))
        With map_scene.camera
            .CAM_X_ANGLE += rot_delta_x * f
            .CAM_Y_ANGLE = Math.Clamp(.CAM_Y_ANGLE + rot_delta_y * f, PITCH_MIN, PITCH_MAX)

            If .CAM_X_ANGLE > (2 * PI) Then .CAM_X_ANGLE -= (2 * PI)
            If .CAM_X_ANGLE < 0 Then .CAM_X_ANGLE += (2 * PI)
        End With
        rot_delta_x *= (1.0F - f)
        rot_delta_y *= (1.0F - f)
        ' Rest snap, the same treatment zoom and pan get below. Without it the
        ' pool only ever decays exponentially, so the camera keeps crawling
        ' sub-pixel for seconds after release - the VT feedback slides with it
        ' and re-bakes a trickle of far pages, popping the big distant patches
        ' the whole time the view "settles in". 0.0005 rad is under one screen
        ' pixel of remaining travel, so the truncation is invisible.
        If Math.Abs(rot_delta_x) < 0.0005F Then rot_delta_x = 0
        If Math.Abs(rot_delta_y) < 0.0005F Then rot_delta_y = 0

        ' Zoom: same apply-and-decay. VIEW_RADIUS is negative and the exp
        ' factor is positive, so the sign never flips; the clamps are the old
        ' handler's, and hitting one kills the pending so it cannot grind.
        If zoom_delta <> 0 Then
            With map_scene.camera
                Dim vrad = .VIEW_RADIUS * CSng(Math.Exp(zoom_delta * f))
                If vrad < .MAX_ZOOM_OUT Then
                    vrad = .MAX_ZOOM_OUT
                    zoom_delta = 0
                ElseIf vrad > -0.1F Then
                    vrad = -0.1F
                    zoom_delta = 0
                End If
                .VIEW_RADIUS = vrad
            End With
            zoom_delta *= (1.0F - f)
            If Math.Abs(zoom_delta) < 0.00001F Then zoom_delta = 0
        End If

        ' Pan: same apply-and-decay, in world metres.
        If pan_delta_x <> 0 OrElse pan_delta_z <> 0 Then
            map_scene.camera.LOOK_AT_X += pan_delta_x * f
            map_scene.camera.LOOK_AT_Z += pan_delta_z * f
            pan_delta_x *= (1.0F - f)
            pan_delta_z *= (1.0F - f)
            If Math.Abs(pan_delta_x) < 0.0001F Then pan_delta_x = 0
            If Math.Abs(pan_delta_z) < 0.0001F Then pan_delta_z = 0
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseButtonEventArgs)
        MyBase.OnMouseUp(e)

        Dim io = ImGui.GetIO()
        If io.WantCaptureKeyboard OrElse io.WantCaptureMouse Then
            Return
        End If

        M_DOWN = False
        MOVE_CAM_Z = False
        MOVE_MOD = False
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseWheelEventArgs)
        MyBase.OnMouseWheel(e)

        _controller.MouseScroll(e.Offset)
    End Sub

    Protected Overrides Sub OnTextInput(e As TextInputEventArgs)
        MyBase.OnTextInput(e)

        _controller.PressChar(ChrW(e.Unicode))
    End Sub

    Private Sub SubmitUI(viewport As ImGuiViewportPtr)
        If CLEAN_VIEW Then Return

        ImGui.SetNextWindowPos(viewport.Pos)
        ' AlwaysAutoResize matters here: without a size of its own this window
        ' auto-fits for its first couple of frames and then latches. It is created
        ' on the map picker holding nothing but the buttons, so once a map loads
        ' the FPS and clip counts would be drawn and then clipped straight back
        ' out again. Auto-resizing every frame lets it grow when they appear.
        If ImGui.Begin("##Dummy Window 1", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings Or ImGuiWindowFlags.AlwaysAutoResize) Then
            If ImGui.Button("Load map") Then
                'Runs Map picking code.
                SHOW_MAPS_SCREEN = True
            End If
            ImGui.SameLine()
            If ImGui.Button("Settings") Then
                SHOW_SETTINGS_WINDOW = True
            End If
            ImGui.SameLine()
            If ImGui.Button("Textures viewer") Then
                SHOW_TEXTURES_VIEWER_WINDOW = True
            End If
            ImGui.SameLine()
            If ImGui.Button("Screen Capture") Then
                NEED_TO_DO_SCREEN_CAPTURE = True
            End If
            ImGui.SameLine()
            If ImGui.Button("Snapshot") Then
                write_log_snapshot()
            End If
            ImGui.SameLine()
            ' The readouts used to live on this bar. They are a panel of their
            ' own now - the bar is menu items only, and this is the switch.
            If ImGui.Button(If(SHOW_STATS_WINDOW, "Hide stats", "Stats")) Then
                SHOW_STATS_WINDOW = Not SHOW_STATS_WINDOW
                ' Always come back somewhere visible.
                reset_stats_pos = True
                ' Issuing timer queries is not free, so they are only live while
                ' something is looking at them.
                modGpuTimers.Enabled = SHOW_STATS_WINDOW
                If Not SHOW_STATS_WINDOW Then modGpuTimers.Reset()
            End If

            menubar_pos = ImGui.GetWindowPos()
            menubar_size = ImGui.GetWindowSize()
            ImGui.End()
        End If

        ' Draw Terrain IDs
        If SHOW_CHUNK_IDs AndAlso DONT_BLOCK_TERRAIN Then
            ImGui.SetNextWindowPos(viewport.Pos)
            ImGui.SetNextWindowSize(viewport.Size)
            If ImGui.Begin("##Dummy Window 2", Nothing, ImGuiWindowFlags.NoBackground Or ImGuiWindowFlags.NoDecoration Or ImGuiWindowFlags.NoMove Or ImGuiWindowFlags.NoSavedSettings Or ImGuiWindowFlags.NoInputs) Then
                map_scene.terrain.draw_terrain_ids()
                ImGui.End()
            End If
        End If

        draw_stats_window()
        draw_vt_debug_key()

        If SHOW_SETTINGS_WINDOW Then
            If Not prev_SHOW_SETTINGS_WINDOW AndAlso menubar_size.LengthSquared > 0 Then
                Dim pos = menubar_pos + New System.Numerics.Vector2(0, menubar_size.Y + 5)
                ImGui.SetNextWindowPos(pos)
            End If
            If ImGui.Begin("Settings", SHOW_SETTINGS_WINDOW) Then
                If ImGui.CollapsingHeader("Export Map") Then
                    ImGui.Checkbox("Export STLs", EXPORT_STL_MAP)
                End If
                If ImGui.CollapsingHeader("Camera") Then
                    ImGui.SliderFloat("Speed", My.Settings.speed, 0.001, 1.0)
                    ' The OrbitControls dampingFactor. Low = heavy, glidey.
                    ' 1.0 = the old direct 1:1 rotation.
                    ImGui.SliderFloat("Rotation damping", ROT_DAMPING, 0.01, 1.0)
                    ' Preset degrees only. Projection rebuilds every frame,
                    ' so this is instant, and it persists on exit.
                    If ImGui.BeginCombo("FOV", CInt(My.Settings.fov).ToString) Then
                        For Each deg In {35, 40, 45, 50, 55, 60, 65, 70}
                            If ImGui.Selectable(deg.ToString) Then
                                My.Settings.fov = deg
                                FieldOfView = CSng(Math.PI) * (deg / 180.0F)
                            End If
                        Next
                        ImGui.EndCombo()
                    End If
                End If
                If ImGui.CollapsingHeader("Section Visibility") Then
                    ImGui.Checkbox("SH ambient", USE_SH_AMBIENT)
                    ImGui.Checkbox("Draw bases", DONT_BLOCK_BASES)
                    ImGui.Checkbox("Draw decals", DONT_BLOCK_DECALS)
                    ' shown inverted: ticking it turns fading off, so the box sits
                    ' unchecked in the normal case
                    Dim no_edge_fade = Not DECAL_EDGE_FADE
                    If ImGui.Checkbox("Disable decal edge fade", no_edge_fade) Then
                        DECAL_EDGE_FADE = Not no_edge_fade
                    End If
                    ImGui.Checkbox("Draw models", DONT_BLOCK_MODELS)
                    ' The whole FX pass, meshes and cards together, which is how
                    ' modRender brackets them. Independent of DONT_BLOCK_MODELS -
                    ' hiding the models leaves the fire and smoke drawing. It does
                    ' still need MODELS_LOADED: the FX meshes are model geometry and
                    ' the load itself sits inside DONT_BLOCK_MODELS (MapLoader.vb:65),
                    ' so a map loaded with models off has no FX to show.
                    ImGui.Checkbox("Draw FX", DONT_BLOCK_FX)
                    If DONT_BLOCK_FX Then
                        ImGui.Checkbox("   Particle cards as wireframe", PARTICLES_WIRE)
                        ' Glow. Only possible because the FX accumulate into a
                        ' float buffer - the halo is built from energy the old
                        ' Rgba8 path had already flattened away.
                        '
                        ' Shape is hard wired (see modGlobalVars); the sliders
                        ' that tuned strength, radius, passes and threshold are
                        ' deliberately gone. This is on/off only.
                        ImGui.Checkbox("   Glow", FX_GLOW)
                    End If
                    ImGui.Checkbox("Draw sky", DONT_BLOCK_SKY)
                    ImGui.Checkbox("Draw terrain", DONT_BLOCK_TERRAIN)
                    ImGui.Checkbox("Draw Outland", DONT_BLOCK_OUTLAND)
                    ' A/B lever: re-bakes the outland albedo on toggle (fast,
                    ' a few fullscreen passes) so the seam tint can be judged
                    ' live against the playfield.
                    If ImGui.Checkbox("Outland global tint", OUTLAND_GLOBAL_TINT) Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso map_scene.OUTLAND_LOADED Then
                            map_scene.terrain.bake_outland_albedo()
                        End If
                    End If
                    ' Residual global at range (the seam is always 100%).
                    ' Re-bake on release so dragging is not a bake storm.
                    ImGui.SliderFloat("Global at range", OUTLAND_GLOBAL_BASE, 0.0F, 1.0F)
                    If ImGui.IsItemDeactivatedAfterEdit() Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso map_scene.OUTLAND_LOADED Then
                            map_scene.terrain.bake_outland_albedo()
                        End If
                    End If
                    ' The noise_texture candidate as detailAlbedoSml - applies
                    ' at draw, so both of these are instant.
                    ImGui.Checkbox("Outland detail (test)", OUTLAND_USE_DETAIL)
                    ImGui.SliderFloat("Detail repeats", OUTLAND_DETAIL_TILES, 1.0F, 256.0F)
                    ' Per-pixel shine/metal from the cascade NM's R/B against
                    ' the constant path - instant A/B.
                    ImGui.Checkbox("Outland PBR from NM", OUTLAND_PBR_NM)
                    ' Specular intensity of the constant path - dial the sun
                    ' response until the sheet matches the field. Instant.
                    ImGui.SliderFloat("Outland spec", OUTLAND_SPEC, 0.0F, 0.6F)
                    ImGui.Checkbox("Draw trees", DONT_BLOCK_TREES)
                    ImGui.Checkbox("Draw water", DONT_BLOCK_WATER)
                    ' Multiplier on the authored water-fog density (BWWa
                    ' +0x70): >1 = murkier sooner, <1 = clearer. Instant.
                    ImGui.SliderFloat("Water fog x", WATER_FOG_MUL, 0.25F, 4.0F)
                    ' Trim for the water plane, saved per map. The packages
                    ' author exact heights, so anything nonzero here is taste.
                    Dim v_wy = WATER_Y_OFFSET
                    If ImGui.SliderFloat("Water height trim", v_wy, -2.0, 2.0) Then
                        WATER_Y_OFFSET = v_wy
                    End If
                    ' Masks water off model surfaces this close under the plane
                    ' - boat decks and hull interiors. Tune just deeper than
                    ' the decks; too deep starts cutting water off submerged
                    ' hull sides seen through the surface.
                    Dim v_wx = WATER_EXCLUDE_BAND
                    If ImGui.SliderFloat("Water exclude depth", v_wx, 0.0, 4.0) Then
                        WATER_EXCLUDE_BAND = v_wx
                    End If
                End If
                If ImGui.CollapsingHeader("Pick Models") Then
                    ImGui.Checkbox("Enabled##Object picking", ModelPicker.Enabled)
                    If ModelPicker.Enabled AndAlso map_scene IsNot Nothing AndAlso map_scene.PICKED_STRING <> "" Then
                        ImGui.TextWrapped(map_scene.PICKED_STRING)
                    End If
                End If
                If ImGui.CollapsingHeader("Overlays") Then
                    ImGui.Checkbox("Draw terrain wire", WIRE_TERRAIN)
                    ImGui.Checkbox("Draw model wire", WIRE_MODELS)
                    ' Vertex colour as albedo, on every model. Toggling
                    ' RECOMPILES the model program - the view is #ifdef-gated so
                    ' that with it off the shader is byte for byte the one that
                    ' ships. Same mechanism ModelPicker uses for PICK_MODELS.
                    If ImGui.Checkbox("Show vertex colours", SHOW_VERTEX_COLOURS) Then
                        If SHOW_VERTEX_COLOURS Then
                            modelShader.SetDefine("SHOW_VERTEX_COLOURS")
                        Else
                            modelShader.UnsetDefine("SHOW_VERTEX_COLOURS")
                        End If
                    End If
                    ImGui.Checkbox("Draw bounding boxes", SHOW_BOUNDING_BOXES)
                    If SHOW_BOUNDING_BOXES Then
                        ImGui.Checkbox("   volumetric/GFX only", BOXES_VOLUMETRIC_ONLY)
                    End If
                    ImGui.Checkbox("Draw chunks", SHOW_CHUNKS)
                    ImGui.Checkbox("Draw grid", SHOW_GRID)
                    ImGui.Checkbox("Draw border", SHOW_BORDER)
                    ImGui.Checkbox("Draw chunk ids", SHOW_CHUNK_IDs)
                    ImGui.Checkbox("Draw cursor", SHOW_CURSOR)
                    ImGui.Checkbox("Draw test textures", CommonProperties.SHOW_TEST_TEXTURES)
                    Dim items = {"None", "Face", "Vertex"}
                    If ImGui.BeginCombo("Draw normals", items(NORMAL_DISPLAY_MODE)) Then
                        If ImGui.Selectable(items(0)) Then
                            NORMAL_DISPLAY_MODE = 0
                        End If
                        If ImGui.Selectable(items(1)) Then
                            NORMAL_DISPLAY_MODE = 1
                        End If
                        If ImGui.Selectable(items(2)) Then
                            NORMAL_DISPLAY_MODE = 2
                        End If
                        ImGui.EndCombo()
                    End If
                End If
                If ImGui.CollapsingHeader("Culling") Then
                    ImGui.Checkbox("Raster culling", USE_RASTER_CULLING)
                End If
                If ImGui.CollapsingHeader("Terrain") Then
                    ImGui.Checkbox("Use tessellation", USE_TESSELLATION)
                    ImGui.SliderFloat("Tessellation Level", CommonProperties.tess_level, 0.0, 8.0)

                    ' Width of the terrain height blend. Starts at the game's
                    ' hardcoded 0.05, NOT the map-authored BWT2 value - the game
                    ' does not use the authored one here, and at 0.3 the band is
                    ' wide enough that all eight layers contribute and the mix
                    ' averages out. Small is a crisp edge following the height
                    ' maps, large is a cross-fade.
                    ' Needs Rebuild VT to show, the mix is baked into the pages.
                    Dim v_bh = CommonProperties.BLEND_HEIGHT
                    If ImGui.SliderFloat("Blend Height", v_bh, 0.01, 1.0) Then
                        CommonProperties.BLEND_HEIGHT = v_bh
                    End If
                    ImGui.Text(String.Format("   live={0:0.###}   game={1:0.###}   map authored={2:0.###} (unused)   disabled={3:0.###}",
                                             CommonProperties.BLEND_HEIGHT,
                                             TCommonProperties.GAME_BLEND_HEIGHT,
                                             CommonProperties.blend_height_authored,
                                             CommonProperties.disabled_blend_height))
                    ' Exponent on layer height before the blend. 1.0 is the game's
                    ' behaviour; below 1 stops the winning texture sitting heavy.
                    Dim v_hc = CommonProperties.HEIGHT_CONTRAST
                    If ImGui.SliderFloat("Height Contrast", v_hc, 0.25, 12.0) Then
                        CommonProperties.HEIGHT_CONTRAST = v_hc
                    End If

                    ' Per-page micro -> macro fade. 0 is the old behaviour.
                    Dim v_mf = CommonProperties.MACRO_FADE
                    If ImGui.SliderFloat("Macro Fade / mip", v_mf, 0.0, 1.0) Then
                        CommonProperties.MACRO_FADE = v_mf
                    End If

                    ' The strength slider lives under PBR shading now, with the
                    ' rest of the lighting. This is the bake operation only.
                    ' No atlas rebuild - the bake is sampled in the final render,
                    ' so the new depth is picked up on the very next frame.
                    If ImGui.Button("Re-bake sun shadow") Then
                        map_scene?.sun_shadow.Bake()
                    End If

                    If ImGui.Button("Rebuild VT##blend") Then
                        map_scene?.terrain.RebuildVTAtlas()
                    End If
                End If
                If ImGui.CollapsingHeader("Shadow Mapping") Then
                    ' The live cascades have no controls any more. They are off
                    ' at startup (CommonProperties.Init) and the map-wide bake
                    ' carries everything including trees, so there is nothing for
                    ' a checkbox to switch between. ShadowMappingPass, the FBO and
                    ' the shaders are all still there - shadow_mapping and
                    ' shadow_strength also still save per map - so restoring the
                    ' two controls here and setting USE_SHADOW_MAPPING back to 1
                    ' is the whole job if trees start animating.

                    ' Baked map-wide shadow. Toggling only has to re-bake the sun
                    ' depth now - the shadow is sampled per frame in deferred.frag
                    ' and never enters a VT page, so the atlas is untouched and
                    ' this is instant rather than a full rebuild.
                    If ImGui.Checkbox("Baked (map-wide)", BAKED_SHADOW_ENABLED) Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing Then
                            If BAKED_SHADOW_ENABLED Then
                                map_scene.sun_shadow.Bake()
                            Else
                                map_scene.sun_shadow.ready = False
                            End If
                        End If
                    End If

                    ' Moment Shadow Maps against PCF, same bake either way, so
                    ' this is a straight A/B. Needs a re-bake: MSM wants a colour
                    ' attachment and a mip chain the depth-only path never built.
                    If ImGui.Checkbox("Moment shadow maps (A/B)", MSM_SHADOW_ENABLED) Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso BAKED_SHADOW_ENABLED Then
                            map_scene.sun_shadow.Bake()
                        End If
                    End If
                    If MSM_SHADOW_ENABLED Then
                        ' Raise if the reconstruction goes unstable over flat
                        ' ground - the Hankel matrix is singular where depth is
                        ' constant, and open terrain is exactly that.
                        Dim v_mb = MSM_MOMENT_BIAS
                        If ImGui.SliderFloat("  moment bias", v_mb, 0.0, 0.01) Then
                            MSM_MOMENT_BIAS = v_mb
                        End If
                    End If

                    ImGui.Separator()
                    ' Wet-surface reflections. A cubemap can only ever show sky;
                    ' this marches the reflected ray through the frame that was
                    ' just drawn, so it can put actual geometry in a puddle.
                    ' Not gated on sun shadow - a puddle in shade still reflects
                    ' the building above it, it just must not glint.
                    ImGui.Checkbox("SSR (wet reflections)", SSR_ENABLED)
                    If SSR_ENABLED Then
                        Dim v_si = SSR_INTENSITY
                        If ImGui.SliderFloat("  SSR strength", v_si, 0.0, 2.0) Then
                            SSR_INTENSITY = v_si
                        End If
                        Dim v_sn = SSR_STEPS
                        If ImGui.SliderInt("  SSR steps", v_sn, 8, 96) Then
                            SSR_STEPS = v_sn
                        End If
                        ' Too large smears a reflection across a depth gap, too
                        ' small drops thin geometry like railings.
                        Dim v_st = SSR_THICKNESS
                        If ImGui.SliderFloat("  SSR thickness m", v_st, 0.1, 6.0) Then
                            SSR_THICKNESS = v_st
                        End If
                        Dim v_ss2 = SSR_STRIDE
                        If ImGui.SliderFloat("  SSR stride m", v_ss2, 0.05, 2.0) Then
                            SSR_STRIDE = v_ss2
                        End If
                    End If
                    ImGui.Separator()

                    ' Penumbra shaping. Applies to both paths, so switching
                    ' between them compares the filtering and nothing else.
                    ' Raising LO also crushes the light leak on the moment path.
                    Dim v_plo = SHADOW_PENUMBRA_LO
                    If ImGui.SliderFloat("Penumbra clip lo", v_plo, 0.0, 0.95) Then
                        SHADOW_PENUMBRA_LO = v_plo
                    End If
                    Dim v_phi = SHADOW_PENUMBRA_HI
                    If ImGui.SliderFloat("Penumbra clip hi", v_phi, 0.05, 1.0) Then
                        SHADOW_PENUMBRA_HI = v_phi
                    End If

                    If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso map_scene.sun_shadow.ready Then
                        ImGui.Text(String.Format("   baked {0}x{0}", map_scene.sun_shadow.size))
                    End If

                    If ImGui.Button(If(SHOW_SUN_SHADOW_VIEWER, "Hide shadow map", "View shadow map")) Then
                        SHOW_SUN_SHADOW_VIEWER = Not SHOW_SUN_SHADOW_VIEWER
                    End If
                    If SHOW_SUN_SHADOW_VIEWER Then
                        ' The map occupies a thin slice of the depth range, so
                        ' without a stretch the panel is a flat grey wash.
                        Dim v_lo = SHADOW_VIEW_LO
                        If ImGui.SliderFloat("Depth lo", v_lo, 0.0, 1.0) Then
                            SHADOW_VIEW_LO = v_lo
                        End If
                        Dim v_hi = SHADOW_VIEW_HI
                        If ImGui.SliderFloat("Depth hi", v_hi, 0.0, 1.0) Then
                            SHADOW_VIEW_HI = v_hi
                        End If
                        ImGui.Text("   all black = sun camera misses the map")
                    End If
                End If
                If ImGui.CollapsingHeader("PBR shading") Then
                    ' Read into a local, slide that, write back only on change.
                    ' Passing the property straight to a ByRef parameter relies on
                    ' VB's copy-back, which is easy to get wrong and impossible to
                    ' see failing - this way the write is explicit.
                    Dim v_amb = CommonProperties.AMBIENT
                    If ImGui.SliderFloat("Ambient Level", v_amb, 0.0, 0.4) Then
                        CommonProperties.AMBIENT = v_amb
                    End If
                    ImGui.Text(String.Format("   AMBIENT={0:0.0000}  SH loaded={1}  SH on={2}  sh0={3:0.00} {4:0.00} {5:0.00}",
                                             CommonProperties.AMBIENT, SH_AMBIENT_LOADED, USE_SH_AMBIENT,
                                             SH_AMBIENT(0).X, SH_AMBIENT(0).Y, SH_AMBIENT(0).Z))

                    ' ---- specular model -------------------------------------
                    ImGui.Separator()
                    If ImGui.Checkbox("PBR specular (game model)", PBR_SPEC) Then
                    End If
                    ImGui.Text("   GGX + Schlick-Gaussian F + Smith-Schlick Vis")
                    ImGui.Text("   env LUT indexed (alphaRoughness, NdotV)")

                    ' ---- SH probe FIELD -------------------------------------
                    ' WIRED INTO THE LIGHTING: deferred.frag blends the field
                    ' over the flat global probe by sh_grid_mix. This comment
                    ' used to say it was not, which is how the mix slider went
                    ' missing for a while without anyone noticing the feature
                    ' was still live. modRender.vb carries the same warning.
                    ' "show probe field" paints the raw field instead, so its
                    ' placement can be checked independently of the shading.
                    ImGui.Separator()
                    If SH_GRID_LOADED Then
                        ImGui.Checkbox("SH probe grid", USE_SH_GRID)
                        ImGui.Checkbox("   light FX from the field", USE_SH_GRID_FX)
                        If USE_SH_GRID_FX Then
                            ImGui.SliderFloat("      FX normal offset m", SH_GRID_OFFSET_FX, 0.0F, 5.0F)
                        End If

                        ' Swaps the whole lighting program for probe_field.frag.
                        ' Nothing about this view can touch the real shading.
                        ImGui.Checkbox("   show probe field", SH_GRID_DEBUG)
                        If SH_GRID_DEBUG Then
                            ImGui.SliderFloat("      exposure", SH_GRID_EXPOSURE, 0.02F, 2.0F)
                            ImGui.Checkbox("      probe lattice", SH_GRID_SHOW_LATTICE)
                            ImGui.Text("      red = outside box, amber = above bake")
                        End If

                        ' 0 = global probe alone, 1 = the field exactly, above 1
                        ' exaggerates how far the field departs from the flat
                        ' global probe.
                        '
                        ' This slider was deleted as collateral in 64a15b4, a
                        ' commit about particles and decal ordering. The value
                        ' stayed live at its 0.5 default the whole time, so the
                        ' field was permanently at half strength with no way to
                        ' reach it - the parked-feature shape exactly.
                        ImGui.SliderFloat("   probe mix", SH_GRID_MIX, 0.0F, 3.0F)

                        ' Both reshape the field's departure from the global
                        ' probe BEFORE the mix, so where they agree nothing
                        ' moves. Curve below 1 lifts the dark end - probes next
                        ' to geometry bake very dark and a straight mix drove
                        ' contact shade to near black. Floor is the blunt
                        ' version: the field may not go under this fraction of
                        ' the global probe. 1.0 / 0.0 is the identity.
                        ImGui.SliderFloat("   probe curve", SH_GRID_CURVE, 0.2F, 2.0F)
                        ImGui.SliderFloat("   probe floor", SH_GRID_FLOOR, 0.0F, 1.0F)
                        ImGui.SliderFloat("   normal offset m", SH_GRID_OFFSET, 0.0F, 5.0F)
                        ImGui.Text(String.Format("   {0:0.#} m box, {1:0.00} m spacing, fade {2:0.#} m",
                                                 SH_GRID_SIZE.X, SH_GRID_SPACING, SH_GRID_FADE))
                        ImGui.Text(String.Format("   grid probe sh0={0:0.00} {1:0.00} {2:0.00}",
                                                 SH_GRID_SH9(0).X, SH_GRID_SH9(0).Y, SH_GRID_SH9(0).Z))
                    Else
                        ImGui.Text("SH probe grid: not loaded for this map")
                    End If
                    ImGui.Separator()

                    Dim v_bright = CommonProperties.BRIGHTNESS
                    If ImGui.SliderFloat("Bright Level", v_bright, 0.0, 2.0) Then
                        CommonProperties.BRIGHTNESS = v_bright
                    End If

                    Dim v_spec = CommonProperties.SPECULAR
                    If ImGui.SliderFloat("Spec Level", v_spec, 0.0, 1.0) Then
                        CommonProperties.SPECULAR = v_spec
                    End If

                    Dim v_gray = CommonProperties.GRAY_LEVEL
                    If ImGui.SliderFloat("Gray Level", v_gray, 0.0, 1.0) Then
                        CommonProperties.GRAY_LEVEL = v_gray
                    End If

                    ' Multiplier on the map's sunLightColor, used at full chroma.
                    Dim v_sun = CommonProperties.SUN_STRENGTH
                    If ImGui.SliderFloat("Sun Strength", v_sun, 0.0, 3.0) Then
                        CommonProperties.SUN_STRENGTH = v_sun
                    End If

                    ' The shadow mix, moved here from Terrain and Shadow Mapping
                    ' where it sat as two sliders writing the same value.
                    '
                    ' 1 is the full shadow. Below that it lifts the shadow back
                    ' toward lit, and deferred.frag applies it ONLY where the sun
                    ' actually reaches - a fully occluded pixel stays fully
                    ' occluded at any setting. It softens a penumbra; it can no
                    ' longer put sunlight inside a shadow.
                    Dim v_hz = CommonProperties.HORIZON_STRENGTH
                    If ImGui.SliderFloat("Shadow Mix", v_hz, 0.0, 1.0) Then
                        CommonProperties.HORIZON_STRENGTH = v_hz
                    End If
                    If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso map_scene.sun_shadow.ready Then
                        ImGui.Text(String.Format("   baked {0}x{0}", map_scene.sun_shadow.size))
                    Else
                        ImGui.Text("   no baked sun shadow")
                    End If

                    ' 0 = grey ambient at the same level, 1 = the probe's own colour.
                    Dim v_asat = CommonProperties.AMBIENT_SAT
                    If ImGui.SliderFloat("Ambient Sat", v_asat, 0.0, 1.0) Then
                        CommonProperties.AMBIENT_SAT = v_asat
                    End If

                    ' 0 = white sun, 1 = sunLightColor at full chroma.
                    Dim v_tint = CommonProperties.SUN_TINT
                    If ImGui.SliderFloat("Sun Tint", v_tint, 0.0, 1.0) Then
                        CommonProperties.SUN_TINT = v_tint
                    End If

                    ' Gain of the tone curve. 2.61 is where the scene currently
                    ' sits, a bit past the middle of this range, so there is room
                    ' to go darker or to push the shadows up without clipping.
                    Dim v_expo = CommonProperties.TONEMAP_EXPOSURE
                    If ImGui.SliderFloat("Tone Exposure", v_expo, 0.5, 4.0) Then
                        CommonProperties.TONEMAP_EXPOSURE = v_expo
                    End If

                    Dim v_gamma = CommonProperties.GAMMA_LEVEL
                    If ImGui.SliderFloat("Gamma Level", v_gamma, 0.0, 1.0) Then
                        CommonProperties.GAMMA_LEVEL = v_gamma
                    End If

                    Dim v_fog = CommonProperties.FOG_LEVEL
                    If ImGui.SliderFloat("Fog Level", v_fog, 0.0, 1.0) Then
                        CommonProperties.FOG_LEVEL = v_fog
                    End If
                End If
                If ImGui.CollapsingHeader("Save Map Settings") Then
                    If MAP_LOADED Then
                        ImGui.Text("Map: " & MAP_NAME_NO_PATH)
                        If ImGui.Button("Save settings for this map") Then
                            modMapSettings.Save(MAP_NAME_NO_PATH)
                        End If
                        ImGui.Text("Saves to:")
                        ImGui.TextWrapped(modMapSettings.SaveFilePathFor(MAP_NAME_NO_PATH))
                        If modMapSettings.LAST_RESULT <> "" Then
                            ImGui.Separator()
                            ImGui.TextWrapped(modMapSettings.LAST_RESULT)
                        End If
                    Else
                        ImGui.Text("Load a map first.")
                    End If
                End If
                If ImGui.CollapsingHeader("Minimap") Then
                    ImGui.Checkbox("Enabled##Minimap", DONT_HIDE_MINIMAP)
                    ImGui.SliderInt("Size", MINI_MAP_NEW_SIZE, 128, 640)
                End If
                If ImGui.CollapsingHeader("FXAA") Then
                    ImGui.Checkbox("Enabled##FXAA", FXAA_enable)
                End If
                If ImGui.CollapsingHeader("VT") Then
                    ImGui.SliderInt("Feedback width ", FEEDBACK_WIDTH, 1, 128)
                    ImGui.SliderInt("Feedback height ", FEEDBACK_HEIGHT, 1, 128)
                    ImGui.SliderInt("Tile Size ", TILE_SIZE, 1, 8192)
                    ImGui.SliderInt("Num pages ", VT_NUM_PAGES, 1, 4096)
                    ImGui.SliderInt("Num tiles ", NUM_TILES, 1, 2048)
                    ImGui.SliderInt("Uploads per frame ", UPLOADS_PER_FRAME, 1, 64)
                    If ImGui.Button("Rebuild VT") Then
                        map_scene?.terrain.RebuildVTAtlas()
                    End If
                    ' Terrain coloured by the resident page's mip, with a key
                    ' window - the instrument for the settling-flicker hunt.
                    ImGui.Checkbox("Page debug view", VT_PAGE_DEBUG)
                End If
                ImGui.Separator()
                If ImGui.Button(String.Format("Version {0}", Application.ProductVersion)) Then
                    Using proc As New Process
                        proc.StartInfo.UseShellExecute = True
                        proc.StartInfo.FileName = "https://github.com/mikeoverbay/nuTerra/releases"
                        proc.Start()
                    End Using
                End If
                If ImGui.Button("View Help") Then
                    Using proc As New Process
                        proc.StartInfo.UseShellExecute = True
                        proc.StartInfo.FileName = Path.Combine(Application.StartupPath, "HTML", "index.html")
                        proc.Start()
                    End Using
                End If
                ImGui.End()
                prev_SHOW_SETTINGS_WINDOW = True
            End If
        Else
            prev_SHOW_SETTINGS_WINDOW = False
        End If

        If CommonProperties.SHOW_TEST_TEXTURES Then
            If ImGui.Begin("Test textures") Then
                Dim colors() As Numerics.Vector4 = {
                    New Numerics.Vector4(1.0, 0, 0, 1.0),'Color4.Red,
                    New Numerics.Vector4(0, 1.0, 0, 1.0),'Color4.Green,
                    New Numerics.Vector4(0, 0, 1.0, 1.0),'Color4.Blue,
                    New Numerics.Vector4(1.0, 1.0, 0, 1.0),'Color4.Yellow,
                    New Numerics.Vector4(0.5, 0, 0.5, 1.0),'Color4.Purple,
                    New Numerics.Vector4(1.0, 0.64453125, 0, 1.0),'Color4.Orange,
                    New Numerics.Vector4(1.0, 0.49609375, 0.3125, 1.0),'Color4.Coral,
                    New Numerics.Vector4(0.75, 0.75, 0.75, 1.0)'Color4.Silver
                }
                For i = 0 To 7
                    ImGui.TextColored(colors(i), String.Format("Texture {0}", i + 1))
                Next
            End If
        End If

        If SHOW_TEXTURES_VIEWER_WINDOW Then
            If ImGui.Begin("Textures viewer", SHOW_TEXTURES_VIEWER_WINDOW) Then
                Dim size As New Numerics.Vector2
                size.X = ImGui.GetContentRegionAvail().X
                size.Y = ClientSize.Y * (size.X / ClientSize.X)
                Dim uv0 = New Numerics.Vector2(0.0, 1.0)
                Dim uv1 = New Numerics.Vector2(1.0, 0.0)

                ' gColor and gGMF go through their opaque views - both carry a
                ' mask in alpha and ImGui blends, so the raw textures draw as
                ' empty. The views share the same storage; only alpha differs.
                ImGui.Text("gColor  (rgb albedo, a = water mix)")
                ImGui.Image(New IntPtr(MainFBO.gColor_opaque), size, uv0, uv1)
                ImGui.Text("gSurfaceNormal")
                ImGui.Image(New IntPtr(MainFBO.gSurfaceNormal.texture_id), size, uv0, uv1)
                ImGui.Text("gNormal")
                ImGui.Image(New IntPtr(MainFBO.gNormal.texture_id), size, uv0, uv1)
                ImGui.Text("gGMF  (r gloss, g metal/spec, b flag, a wetness)")
                ImGui.Image(New IntPtr(MainFBO.gGMF_opaque), size, uv0, uv1)
                ImGui.Text("gPosition")
                ImGui.Image(New IntPtr(MainFBO.gPosition.texture_id), size, uv0, uv1)
                ImGui.End()
            End If
        End If
    End Sub

    Public Overrides Sub ProcessEvents()
        MyBase.ProcessEvents()

        If NEED_TO_DO_SCREEN_CAPTURE Then
            NEED_TO_DO_SCREEN_CAPTURE = False
            Dim Save_Dialog = New SaveFileDialog()
            Save_Dialog.Filter = "PNG|*.png"
            Save_Dialog.Title = "Save PNG"
            If Save_Dialog.ShowDialog() = Windows.Forms.DialogResult.OK Then
                SCREEN_CAPTURE_FILENAME = Save_Dialog.FileName
            End If
        End If
    End Sub
End Class

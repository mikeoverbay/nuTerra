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
    Private NEED_TO_PICK_RECORD_DIR As Boolean = False
    Private NEED_TO_CONFIRM_CAPTURE As Boolean = False
    Public SHADER_CHANGED As Boolean = False
    Private SCREEN_CAPTURE_FILENAME As String = Nothing
    Private fps_timer As New Stopwatch

    ''' <summary>
    ''' Wall clock time spent on the capture in progress, paused along with it.
    '''
    ''' Reset when a capture starts and left standing when one ends, so the
    ''' finished figure is still readable afterwards and is cleared by the next
    ''' run rather than by the end of this one.
    ''' </summary>
    Private record_clock As New Stopwatch

    Private _controller As ImGuiController

    Private SHOW_SETTINGS_WINDOW As Boolean
    Private SHOW_TEXTURES_VIEWER_WINDOW As Boolean
    Private SHOW_FLIGHT_RENDER_WINDOW As Boolean

    ' Capture rates offered in Flight Recorder. Three, not a slider: the number
    ' decides how many frames a route costs and how long the encode takes, and
    ' the useful answers are 15 for a rough pass, 30 for a finished video and 60
    ' for something that will be slowed down. Shared arrays so the combo is not
    ' rebuilding them every frame.
    Private Shared ReadOnly FPS_VALUES() As Integer = {15, 30, 60}
    Private Shared ReadOnly FPS_LABELS() As String = {"15", "30", "60"}

    ' Capture sizes. 16:9 throughout, every height EVEN, each a clean fraction
    ' of 1920x1080 - H.264 refuses odd dimensions, and the 1920x1009 that
    ' started all of this came from a window someone had dragged to a size.
    ' A fixed list is how that stops happening by accident.
    '
    ' Index 0 leaves the window exactly as it is.
    Private Shared ReadOnly CAP_SIZES_W() As Integer = {0, 640, 960, 1280, 1600, 1920}
    Private Shared ReadOnly CAP_SIZES_H() As Integer = {0, 360, 540, 720, 900, 1080}
    Private Shared ReadOnly CAP_SIZE_LABELS() As String =
        {"Window", "640 x 360", "960 x 540", "1280 x 720", "1600 x 900", "1920 x 1080"}

    ' Window geometry held across a capture. See CAPTURE_W - none of this is
    ' persisted, so even a crash mid-capture cannot strand the window.
    Private saved_win_size As Vector2i
    Private saved_win_pos As Vector2i
    Private saved_win_border As WindowBorder
    Private win_resized_for_capture As Boolean = False
    Private prev_RECORD_FLIGHT As Boolean = False
    Private prev_SHOW_SETTINGS_WINDOW As Boolean = False

    ''' <summary>
    ''' Put Flight Recorder back where it belongs on the next frame.
    '''
    ''' Set by the menu button, cleared by the panel that consumes it. A saved
    ''' layout can leave the panel off the side of the screen, or shrunk small
    ''' enough to hide the controls at the bottom of it - and in both cases
    ''' pressing the button that opens it is how someone asks for it back.
    ''' </summary>
    Private RESET_FLIGHT_RENDER_LAYOUT As Boolean = False

    ''' <summary>
    ''' What the output folder currently holds, sampled at most once a second.
    '''
    ''' Both of these come off the disk, and the panel that shows them is
    ''' rebuilt every frame. Reading capture.txt and listing a directory sixty
    ''' times a second to print one number and one file name is a cost that
    ''' never announces itself - it just sits in the frame time forever.
    ''' </summary>
    Private folder_facts_dir As String = Nothing
    Private folder_facts_at As DateTime = DateTime.MinValue
    Private folder_fps As Integer = 0
    Private folder_newest_mp4 As String = Nothing

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

        ' Borderless at the monitor's size rather than an exclusive fullscreen
        ' mode switch. A mode switch would fight the ImGui viewports and can
        ' leave the desktop resolution changed if the process is killed - and a
        ' capture run is exactly the thing likely to be killed part way.
        If FULLSCREEN_WINDOW Then
            Dim b = System.Windows.Forms.Screen.PrimaryScreen.Bounds
            SCR_WIDTH = b.Width
            SCR_HEIGHT = b.Height
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

        If FULLSCREEN_WINDOW Then
            setting.WindowBorder = WindowBorder.Hidden
            setting.Location = New Vector2i(0, 0)
        End If

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

        ' The clock everything animated runs on. Worked out once, here, so the
        ' FX, the particles, the fog and the water cannot disagree about how much
        ' time this frame was worth.
        '
        '   recording or fixed step -> exactly one video frame
        '   waiting for the VT      -> nothing at all
        '   otherwise               -> the real frame time
        ANIM_DELTA = CSng(DELTA_TIME)
        If RECORD_FLIGHT OrElse FLY_FIXED_STEP OrElse RECORD_STILL > 0 Then
            ANIM_DELTA = 1.0F / CSng(Math.Max(1, CAPTURE_FPS))
        End If
        If (RECORD_FLIGHT OrElse RECORD_STILL > 0) AndAlso
           (RECORD_HOLD OrElse RECORD_PAUSED) Then ANIM_DELTA = 0.0F
        ANIM_TIME += ANIM_DELTA

        If Not FREEZE_FX Then
            FX_TIME += ANIM_DELTA
            If FX_TIME > 3600.0F Then FX_TIME -= 3600.0F
        End If

        ' Particles ride the same freeze as the rest of the FX so a frozen
        ' frame really is reproducible.
        '
        ' SKIPPED outright at zero rather than called with dt = 0. Update treats
        ' a non-positive dt as a load hitch and substitutes 0.016, so passing
        ' zero to hold the smoke still would advance it 16 ms instead.
        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso
           Not FREEZE_FX AndAlso ANIM_DELTA > 0.0F Then
            map_scene.particles.Update(ANIM_DELTA)
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
            ' See save_record_frame: GDI+ pads 24bpp rows to 4 bytes and only
            ' alignment 4 matches it. A still keeps its odd dimensions though -
            ' nothing encodes it, and a screenshot should be what was on screen.
            GL.PixelStore(PixelStoreParameter.PackAlignment, 4)

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

        ' Frame sequence capture, for video.
        '
        ' Deliberately HERE, in the same place as the single shot above: after
        ' draw_scene and before _controller.Render, so the settings window and the
        ' menu bar are not baked into the recording. Capturing the finished
        ' backbuffer instead would put the UI in every frame.
        ' MAP_LOADED is not optional here. Without it a still capture started from
        ' the command line begins on the LOADING SCREEN - 180 frames of a
        ' progress bar over the earth, which measures as a moving, sharpening
        ' image and looks like working FX until you view one.
        ' Drive the capture clock from here, rather than from each of the
        ' places that start, pause, resume or stop a capture - there are five
        ' of those, and a clock that misses one reads wrong for the rest of the
        ' run. Start and Stop are both no-ops on a Stopwatch already in that
        ' state, so asking every frame costs less than remembering.
        If RECORD_FLIGHT AndAlso Not RECORD_PAUSED Then
            record_clock.Start()
        Else
            record_clock.Stop()
        End If

        ' Hand the window back the instant a capture stops, however it stopped.
        If prev_RECORD_FLIGHT AndAlso Not RECORD_FLIGHT Then restore_window_size()
        prev_RECORD_FLIGHT = RECORD_FLIGHT

        If MAP_LOADED AndAlso map_scene IsNot Nothing AndAlso
           (RECORD_STILL > 0 OrElse (RECORD_FLIGHT AndAlso map_scene.camera.FLYING)) Then
            save_record_frame()
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
        map_scene.cam_path.LogSnapshot()

        ' Pooled water rides entirely on wet-flagged decals, so a map that
        ' authors none can never show any. Worth reading off a snapshot before
        ' anyone goes hunting the shader for it - fifteen of the 21 maps
        ' surveyed are a legitimate zero.
        Dim wet_n = 0, dec_n = 0
        If map_scene.decals IsNot Nothing AndAlso map_scene.decals.all_decals IsNot Nothing Then
            dec_n = map_scene.decals.all_decals.Count
            For Each d_ In map_scene.decals.all_decals
                If d_.wet = CUInt(1) Then wet_n += 1
            Next
        End If
        LogThis("  water: loaded={0} draw={1}   wet decals {2} of {3}",
                map_scene.WATER_LOADED, DONT_BLOCK_WATER, wet_n, dec_n)

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
            ' Capture control. Both are inert unless something is recording, so
            ' neither steals a key from ordinary use - and Escape in particular
            ' does NOT quit, it only stops the capture.
            Case Keys.F9
                start_path_studio()
            Case Keys.Space
                If RECORD_FLIGHT OrElse RECORD_STILL > 0 Then
                    RECORD_PAUSED = Not RECORD_PAUSED
                    LogThis("record: {0} at frame {1}",
                            If(RECORD_PAUSED, "paused", "resumed"), RECORD_FRAME_INDEX)
                End If
            Case Keys.Escape
                If RECORD_FLIGHT OrElse RECORD_STILL > 0 Then
                    LogThis("record: stopped by Escape at frame {0}", RECORD_FRAME_INDEX)
                    RECORD_FLIGHT = False
                    RECORD_STILL = 0
                    RECORD_HOLD = False
                    RECORD_PAUSED = False
                    If RECORD_STOP_AT_END Then FLY_CAM_PATH = False
                End If
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
            If ImGui.Button("Flight Recorder") Then
                SHOW_FLIGHT_RENDER_WINDOW = True
                ' Every press, not just the first. The panel is consulted while
                ' a capture is set up and it should always be findable.
                RESET_FLIGHT_RENDER_LAYOUT = True
            End If
            ImGui.SameLine()
            If ImGui.Button("Path Studio") Then
                start_path_studio()
            End If
            If ImGui.IsItemHovered() Then ImGui.SetTooltip("F9")
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
                If ImGui.CollapsingHeader("Display") Then
                    ' Screen sync. OpenTK's VSync property is the GLFW swap
                    ' interval, which has to be set on the thread holding the
                    ' context - and SubmitUI runs inside OnRenderFrame, so this
                    ' is that thread. Setting it from anywhere else would either
                    ' do nothing or hit the wrong context.
                    '
                    ' Off since the window was constructed, and that default is
                    ' deliberate: a frame rate capped at the refresh hides what a
                    ' change actually costs, which is no good when the stats
                    ' panel is the point. Turn it on to LOOK at the map - without
                    ' it the image tears across every pan.
                    Dim v_vs = (VSync <> VSyncMode.Off)
                    If ImGui.Checkbox("Screen sync (VSync)", v_vs) Then
                        VSync = If(v_vs, VSyncMode.On, VSyncMode.Off)
                    End If
                    ImGui.SameLine()
                    ImGui.TextDisabled(If(VSync = VSyncMode.Off, "uncapped", "capped to refresh"))

                    ' The capture controls used to sit here. They are their own
                    ' window now - Flight Recorder, on the menu bar beside Screen
                    ' Capture - because rendering a flight is a job of its own,
                    ' not a display preference.
                End If
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

                    ImGui.Separator()

                    ' Re-read the file when FLY is switched ON.
                    '
                    ' The path is loaded once at map load, and Path Studio is a
                    ' separate program writing the same file - so without this
                    ' the only way to fly a route just saved next door was to
                    ' reload the whole map. Load also rewinds travelled, so it
                    ' starts at the beginning rather than wherever the last
                    ' flight stopped.
                    If ImGui.Checkbox("FLY", FLY_CAM_PATH) AndAlso FLY_CAM_PATH Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing Then
                            map_scene.cam_path.Load(MAP_NAME_NO_PATH)
                        End If
                    End If

                    ' Show Path re-reads too, but ONLY when not flying. Load
                    ' rewinds travelled, so doing it mid-flight would snap the
                    ' camera back to the start of the route - switching an
                    ' overlay on should not move the shot.
                    If ImGui.Checkbox("Show Path", SHOW_CAM_PATH) AndAlso
                       SHOW_CAM_PATH AndAlso Not FLY_CAM_PATH Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing Then
                            map_scene.cam_path.Load(MAP_NAME_NO_PATH)
                        End If
                    End If

                    ' Same file, same reload rule, so it lives here rather than
                    ' under Overlays - that group is for the renderer's own
                    ' structures, and these are authored content.
                    If ImGui.Checkbox("Show Lights", SHOW_CAM_LIGHTS) AndAlso
                       SHOW_CAM_LIGHTS AndAlso Not FLY_CAM_PATH Then
                        If MAP_LOADED AndAlso map_scene IsNot Nothing Then
                            map_scene.cam_path.Load(MAP_NAME_NO_PATH)
                        End If
                    End If
                    If ImGui.IsItemHovered() Then
                        ImGui.SetTooltip("The lights placed in Path Studio, drawn at their range." & vbLf &
                                         "Nothing is lit by them yet - this is what was authored.")
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
                If ImGui.CollapsingHeader("Water") Then
                    ' Pooled water from the global map's wet channel. Alpha is
                    ' how much sky survives looking straight DOWN at it - the
                    ' Fresnel takes it to a mirror at a grazing angle whatever
                    ' this is set to. Depth is how much of the bed survives
                    ' under full water; lower is deeper and darker.
                    Dim v_wd = WATER_DEPTH
                    If ImGui.SliderFloat("Water depth", v_wd, 0.0, 1.0) Then
                        WATER_DEPTH = v_wd
                    End If

                    ' A water body yields where the global map already says wet - the
                    ' terrain path draws pooled water there, and a body on top is a
                    ' second, disagreeing water on the same pixels. Also drops water
                    ' that has no bed behind it, which from below covered the SKY.
                    ImGui.Checkbox("Water yields to wet terrain", WATER_MASK_WET)
                    If WATER_MASK_WET Then
                        Dim v_wm = WATER_MASK_MIN
                        If ImGui.SliderFloat("  yield above wetness", v_wm, 0.0, 1.0) Then
                            WATER_MASK_MIN = v_wm
                        End If
                    End If

                End If

                If ImGui.CollapsingHeader("Shadow Mapping") Then
                    ' TWO shadow systems, one box each, and they do not touch.
                    '
                    '   Cascades  - live, re-rendered every FRAME_STEP frames from
                    '               the camera, four splits at 20/75/250 m.
                    '   Baked     - one map-wide render from the sun at load,
                    '               covering the whole arena at a fixed texel.
                    '
                    ' deferred.frag multiplies the two factors, so either can be on
                    ' without the other and both on is legal.
                    '
                    ' Moments is NOT a third system and never was. It is how the
                    ' BAKED map is stored - four power moments instead of a depth
                    ' map - so it lives under Baked and does nothing without it.

                    ' Live cascades. Off at startup in CommonProperties.Init; the
                    ' pass, FBO and shaders were always here, this is the control
                    ' that went missing when the bake took over tree shadows.
                    Dim v_csm = ShadowMappingFBO.Enabled
                    If ImGui.Checkbox("Cascades (live)", v_csm) Then
                        ' The setter writes USE_SHADOW_MAPPING and pushes the UBO,
                        ' so nothing else has to happen here.
                        ShadowMappingFBO.Enabled = v_csm
                    End If
                    If ShadowMappingFBO.Enabled Then
                        Dim v_ss = CommonProperties.SHADOW_STRENGTH
                        If ImGui.SliderFloat("  cascade strength", v_ss, 0.0, 1.0) Then
                            CommonProperties.SHADOW_STRENGTH = v_ss
                            CommonProperties.update()
                        End If
                    End If

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

                    ' Only offered when there is a bake for it to change the
                    ' format of. Shown nested rather than greyed out, the same way
                    ' the moment bias below is only shown when moments are on -
                    ' a box that cannot do anything is worse than no box.
                    If BAKED_SHADOW_ENABLED Then
                        ' Moment Shadow Maps against PCF, same bake either way, so
                        ' this is a straight A/B of the FILTERING. Needs a re-bake:
                        ' MSM wants a colour attachment and a mip chain the
                        ' depth-only path never built.
                        If ImGui.Checkbox("  store as moment maps (A/B)", MSM_SHADOW_ENABLED) Then
                            If MAP_LOADED AndAlso map_scene IsNot Nothing Then
                                map_scene.sun_shadow.Bake()
                            End If
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

        If SHOW_FLIGHT_RENDER_WINDOW Then
            ' Tall enough for everything in it. At 300 the panel grew past its
            ' own height as controls were added and Build MP4 sat below its
            ' bottom edge - present, reachable only by scrolling, and so
            ' effectively missing.
            Dim want_size = New System.Numerics.Vector2(380, 500)

            If RESET_FLIGHT_RENDER_LAYOUT Then
                RESET_FLIGHT_RENDER_LAYOUT = False

                ' Top left, under the menu bar rather than beneath it - the same
                ' placement Settings uses when it opens. menubar_* are read a
                ' few lines above this in the same frame, so they are current;
                ' the fallback is only for the frame before the bar has drawn
                ' once.
                Dim pos = If(menubar_size.LengthSquared > 0,
                             menubar_pos + New System.Numerics.Vector2(0, menubar_size.Y + 5.0F),
                             New System.Numerics.Vector2(0, 47))

                ' No condition argument on either call, so both apply NOW and
                ' override whatever imgui.ini remembered.
                '
                ' The size is reset along with the position deliberately. A
                ' panel left shrunk hides the buttons at the bottom of it, which
                ' is indistinguishable from their not existing - that is exactly
                ' how Build MP4 came to be reported missing when it was three
                ' rows below the visible edge.
                ImGui.SetNextWindowPos(pos)
                ImGui.SetNextWindowSize(want_size)
            Else
                ImGui.SetNextWindowPos(New System.Numerics.Vector2(260, 90), ImGuiCond.FirstUseEver)
                ImGui.SetNextWindowSize(want_size, ImGuiCond.FirstUseEver)
            End If

            ' Progress goes in the title bar so it stays readable with the panel
            ' collapsed, and from further away than the body text.
            '
            ' Everything after ### is the window's ID and is not drawn. Without
            ' it, a caption that changes every frame would be an ID that changes
            ' every frame, and ImGui would treat each tick as a brand new window
            ' - losing its position, size and collapsed state continuously.
            Dim title = "Flight Recorder###FlightRender"
            If ENCODE_RUNNING Then
                title = String.Format("Flight Recorder  encoding {0}/{1}###FlightRender",
                                      ENCODE_DONE, ENCODE_TOTAL)
            Else
            ' RECORD_FRAME_INDEX survives the end of a capture and is zeroed by
            ' the start of the next, so the finished tally stays up to be read
            ' and is cleared when a new run begins rather than the instant this
            ' one stops.
                If RECORD_FLIGHT OrElse RECORD_FRAME_INDEX > 0 Then
                ' Only a flight has a known end. A still run counts down against
                ' its own total, and quoting the lap length at it would be a
                ' denominator the capture is not working towards.
                Dim total = If(RECORD_FLIGHT, lap_frames(), 0)
                Dim el = record_clock.Elapsed
                ' Hours only once there are any. Yesterday's lap took 85 minutes,
                ' so they do happen, but a leading 0: on every short capture is
                ' noise in a title bar.
                Dim clock = If(el.TotalHours >= 1.0,
                               String.Format("{0}:{1:00}:{2:00}",
                                             CInt(Math.Floor(el.TotalHours)), el.Minutes, el.Seconds),
                               String.Format("{0}:{1:00}", el.Minutes, el.Seconds))
                title = String.Format("Flight Recorder  {0}{1}  {2}{3}###FlightRender",
                                      RECORD_FRAME_INDEX,
                                      If(total > 0, "/" & total.ToString(), ""),
                                      clock,
                                      If(RECORD_PAUSED, "  PAUSED", ""))
                End If
            End If

            If ImGui.Begin(title, SHOW_FLIGHT_RENDER_WINDOW) Then

                ' Frames per second the capture represents. It is NOT a speed
                ' limit - the app renders as fast as it can and each frame
                ' stands for 1/fps of video whatever it cost. It sets how far
                ' the flight and everything animated advance per frame, so it
                ' decides both the length of the file and how many frames the
                ' route costs.
                Dim fps_idx = Array.IndexOf(FPS_VALUES, CAPTURE_FPS)
                ' A value from a settings file or still= that is not on the list
                ' would index -1 and throw, so fall back to the default rather
                ' than trusting what came in.
                If fps_idx < 0 Then fps_idx = Array.IndexOf(FPS_VALUES, 30)
                If ImGui.Combo("fps", fps_idx, FPS_LABELS, FPS_LABELS.Length) Then
                    CAPTURE_FPS = FPS_VALUES(fps_idx)
                    save_render_settings()
                End If

                ' Matched on WIDTH rather than on a stored index, so the list
                ' can gain or lose an entry without a settings file silently
                ' meaning a different size than it did before.
                Dim cap_idx = Array.IndexOf(CAP_SIZES_W, CAPTURE_W)
                If cap_idx < 0 Then cap_idx = 0
                If ImGui.Combo("size", cap_idx, CAP_SIZE_LABELS, CAP_SIZE_LABELS.Length) Then
                    CAPTURE_W = CAP_SIZES_W(cap_idx)
                    CAPTURE_H = CAP_SIZES_H(cap_idx)
                    save_render_settings()
                End If
                If ImGui.IsItemHovered() Then
                    ImGui.SetTooltip("The window is resized to this while capturing and put back" & vbLf &
                                     "afterwards - size, position and border. 1920 x 1080 drops the" & vbLf &
                                     "border, because a 1080 client will not fit under a title bar" & vbLf &
                                     "on a 1080 screen.")
                End If

                ' The gate. Each frame waits for the terrain to finish streaming
                ' and the flight is frozen while it waits, so the pages have a
                ' still view to catch up with.
                If ImGui.Checkbox("Wait VT", WAIT_VT) Then save_render_settings()
                If ImGui.IsItemHovered() Then
                    ImGui.SetTooltip("On: sharp terrain in every frame, about half the throughput." & vbLf &
                                     "Off: shoots immediately, terrain resolves during the video.")
                End If

                If ImGui.Checkbox("Fixed step flight", FLY_FIXED_STEP) Then save_render_settings()
                If ImGui.Checkbox("Stop flying at the end", RECORD_STOP_AT_END) Then save_render_settings()
                If ImGui.Checkbox("Hide HUD while capturing", RECORD_HIDE_HUD) Then save_render_settings()
                If ImGui.IsItemHovered() Then
                    ImGui.SetTooltip("The minimap and the shadow viewer are drawn into the scene," & vbLf &
                                     "so unlike the ImGui panels they would end up in the video.")
                End If

                ' Where the frames go.
                '
                ' The dialog cannot open from here. This runs inside the render
                ' frame, and a modal Win32 dialog would block the GL thread part
                ' way through building one - so it raises a flag and ProcessEvents
                ' opens it between frames, exactly as Screen Capture does.
                If ImGui.Button("Browse...") Then
                    NEED_TO_PICK_RECORD_DIR = True
                End If
                ImGui.SameLine()

                ' Straight from here rather than deferred like the Browse dialog.
                ' Explorer is launched and forgotten - nothing modal, nothing to
                ' block the render thread on - so there is no reason to wait for
                ' ProcessEvents.
                If ImGui.Button("Open Path") Then
                    Try
                        ' Created on demand. On a first run the folder does not
                        ' exist until the first frame is written, and Explorer
                        ' would just report a missing path.
                        IO.Directory.CreateDirectory(RECORD_DIR)
                        ' UseShellExecute MUST be set. It defaults to False on
                        ' .NET 6, and without it this tries to EXECUTE the
                        ' directory instead of opening it, and throws.
                        System.Diagnostics.Process.Start(
                            New System.Diagnostics.ProcessStartInfo(RECORD_DIR) With {
                                .UseShellExecute = True})
                    Catch ex As Exception
                        LogThis("record: cannot open {0} - {1}", RECORD_DIR, ex.Message)
                    End Try
                End If
                ImGui.TextWrapped(RECORD_DIR)

                ' Sits with the folder controls rather than the other
                ' checkboxes: it is a statement about that folder, not about
                ' how the flight is shot.
                If ImGui.Checkbox("Keep PNGs", RECORD_KEEP_PNGS) Then save_render_settings()
                If ImGui.IsItemHovered() Then
                    ImGui.SetTooltip("Off: the frames are deleted once the mp4 has been built." & vbLf &
                                     "On: they are kept, for a second render or a look at a frame." & vbLf &
                                     vbLf &
                                     "Either way the folder is cleared when a capture STARTS -" & vbLf &
                                     "that is housekeeping, not a preference.")
                End If

                ImGui.Separator()

                ' Assemble whatever is already in the folder.
                '
                ' A capture ends by doing this itself, so the button is for the
                ' folder that is already full - a run whose encode failed, one
                ' shot before this existed, or a second render of the same
                ' frames at a different rate.
                If Not ENCODE_RUNNING AndAlso Not RECORD_FLIGHT Then
                    ' Cached, not read per frame - see refresh_folder_facts.
                    refresh_folder_facts(RECORD_DIR)

                    ' The rate the frames were SHOT at wins over the combo.
                    '
                    ' The combo says what the NEXT capture will run at, which is
                    ' a different question from what these frames already are.
                    ' Reading it here once produced a 97 second video of a 48
                    ' second flight, at half speed, with nothing to say so.
                    Dim use_fps = If(folder_fps > 0, folder_fps, CAPTURE_FPS)

                    If ImGui.Button("Build MP4", New System.Numerics.Vector2(150, 0)) Then
                        start_mp4_encode(RECORD_DIR, use_fps)
                    End If
                    If ImGui.IsItemHovered() Then
                        ImGui.SetTooltip("Assemble frame_*.png in the folder above into an mp4." & vbLf &
                                         "The frames are left alone.")
                    End If

                    ' Play the newest render in whatever the system uses for mp4.
                    '
                    ' Only drawn when there is one, and only outside an encode:
                    ' the newest file DURING an encode is the one being written,
                    ' which has no index yet and opens in nothing. That is the
                    ' 0xC00D36C4 an in-progress file gives, and offering it
                    ' would be offering that error.
                    If folder_newest_mp4 IsNot Nothing Then
                        ImGui.SameLine()
                        If ImGui.Button("Play MP4", New System.Numerics.Vector2(120, 0)) Then
                            Try
                                ' UseShellExecute is what hands the file to the
                                ' registered player. Without it .NET tries to
                                ' EXECUTE the mp4 and throws - the same trap the
                                ' Open Path button documents above.
                                System.Diagnostics.Process.Start(
                                    New System.Diagnostics.ProcessStartInfo(folder_newest_mp4) With {
                                        .UseShellExecute = True})
                            Catch ex As Exception
                                LogThis("play: cannot open {0} - {1}", folder_newest_mp4, ex.Message)
                            End Try
                        End If
                        If ImGui.IsItemHovered() Then
                            ImGui.SetTooltip("Play " & IO.Path.GetFileName(folder_newest_mp4))
                        End If
                    End If

                    ' Say which rate, and where it came from. A number that
                    ' silently disagrees with the combo above it has to explain
                    ' itself on screen, not in a log.
                    If folder_fps > 0 Then
                        ImGui.Text(String.Format("{0} fps  (as captured)", folder_fps))
                    Else
                        ImGui.Text(String.Format("{0} fps  (folder does not say - using the combo)",
                                                 use_fps))
                    End If

                    ' A capture that broke the settle rule says so here, and
                    ' keeps saying it until the next one starts. Buried in a log
                    ' it would be found after the video had been kept.
                    If RECORD_FORCED_FRAMES > 0 Then
                        ImGui.TextColored(New System.Numerics.Vector4(1.0F, 0.55F, 0.2F, 1.0F),
                                          String.Format("{0} frames shot with the VT unsettled",
                                                        RECORD_FORCED_FRAMES))
                    End If

                    ' Name what Play would open. Two renders of one flight differ
                    ' only in the timestamp in the name, so "the newest one" is
                    ' not something to have to take on trust.
                    If folder_newest_mp4 IsNot Nothing Then
                        ImGui.TextWrapped("newest: " & IO.Path.GetFileName(folder_newest_mp4))
                    End If
                ElseIf ENCODE_RUNNING Then
                    ImGui.Text(String.Format("Encoding {0} / {1}", ENCODE_DONE, ENCODE_TOTAL))
                    If ENCODE_TOTAL > 0 Then
                        ImGui.ProgressBar(ENCODE_DONE / CSng(ENCODE_TOTAL),
                                          New System.Numerics.Vector2(-1.0F, 0.0F))
                    End If
                End If

                If ENCODE_MESSAGE IsNot Nothing Then
                    ImGui.TextWrapped(ENCODE_MESSAGE)
                End If

                ImGui.Separator()

                ' Rewinds to the start of the path, starts flying, starts
                ' writing. By hand it meant ticking FLY first and catching the
                ' path wherever it already was, so a capture began part way
                ' round.
                If ImGui.Button(If(RECORD_FLIGHT, "Stop capture", "Start capture"), New System.Numerics.Vector2(150, 0)) Then
                    If RECORD_FLIGHT Then
                        RECORD_FLIGHT = False
                        RECORD_HOLD = False
                    ElseIf map_scene IsNot Nothing AndAlso map_scene.cam_path.loaded Then
                        ' Ask first, and start from the answer.
                        '
                        ' A capture runs unattended for minutes, throws away
                        ' whatever frames were in the folder, and is only found
                        ' to be wrong once it is already spent. One click is too
                        ' little standing between that and a stray press. The
                        ' dialog cannot open from here - see Browse - so this
                        ' raises a flag and ProcessEvents does the asking, and
                        ' the starting, between frames.
                        NEED_TO_CONFIRM_CAPTURE = True
                    End If
                End If

                ' One frame at the held camera. Waits for the VT exactly as a
                ' flight frame does, so a still is a fair picture of the terrain
                ' rather than of whatever had streamed by the time it was asked
                ' for. Goes to a "still" subfolder, never into a flight's frames.
                ' No Stop counterpart: one frame completes on the next pass,
                ' so a button to interrupt it could never be pressed in time.
                If ImGui.Button("Capture still", New System.Numerics.Vector2(150, 0)) Then
                    RECORD_FRAME_INDEX = 0
                    RECORD_HOLD = False
                    RECORD_STILL = 1
                End If

                ImGui.Separator()

                If map_scene IsNot Nothing AndAlso map_scene.cam_path.loaded Then
                    Dim spd = 12.0F
                    If map_scene.cam_path.points IsNot Nothing AndAlso map_scene.cam_path.points.Length > 0 Then
                        spd = Math.Max(0.1F, map_scene.cam_path.points(0).speed)
                    End If
                    Dim secs = map_scene.cam_path.total_len / spd
                    ImGui.Text(String.Format("path {0:0} m, {1:0} s, {2} frames",
                                             map_scene.cam_path.total_len, secs,
                                             CInt(Math.Ceiling(secs * CAPTURE_FPS))))
                Else
                    ImGui.TextDisabled("no path loaded")
                End If

                If RECORD_FLIGHT OrElse RECORD_STILL > 0 Then
                    ImGui.SameLine()
                    If ImGui.Button(If(RECORD_PAUSED, "Resume", "Pause")) Then
                        RECORD_PAUSED = Not RECORD_PAUSED
                    End If

                    ImGui.Text(String.Format("{0} frames written{1}",
                                             RECORD_FRAME_INDEX,
                                             If(RECORD_STILL > 0, String.Format(", {0} to go", RECORD_STILL), "")))
                    If RECORD_PAUSED Then
                        ImGui.TextDisabled("PAUSED - space to resume")
                    ElseIf RECORD_HOLD Then
                        ImGui.TextDisabled("waiting for the VT")
                    End If
                End If
                ImGui.TextDisabled("space pauses, escape stops")
            End If
            ImGui.End()
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

    ''' <summary>
    ''' One numbered PNG per rendered frame, for encoding into video afterwards.
    '''
    ''' Same readback the single screen capture uses. It stalls the pipeline hard
    ''' and PNG encoding is not quick, so this runs well below real time - which
    ''' is the point rather than a cost. Nothing here is sampling a clock, so a
    ''' frame that took 200 ms to write is still exactly one frame of output.
    ''' </summary>
    ' Consecutive settled VT updates required before shooting. 30, not 3: the
    ' feedback buffer runs a frame or more behind the render, the bias ratchets a
    ' step at a time, and pages upload at uploadsperframe - so convergence is a
    ' process with a tail, not an event. A short run catches the middle of it.
    Private Const RECORD_SETTLE_RUN As Integer = 30

    ' Give up waiting after this many frames and shoot anyway. Generous, because
    ' the FIRST frame of a capture has the whole view to stream where later ones
    ' only have the 20 cm the camera moved. Without a ceiling at all, a map whose
    ' working set never fits at the finest bias thrashes forever and an
    ' unattended capture silently stops producing frames.
    Private Const RECORD_WAIT_MAX As Integer = 900

    Private record_wait_frames As Integer

    ''' <summary>
    ''' Frames one lap of the loaded route costs at the current capture rate,
    ''' or 0 when there is no closed route to measure.
    '''
    ''' One function rather than the same arithmetic in two places. The stop
    ''' test and the confirmation prompt have to agree, and two copies of a
    ''' formula reading CAPTURE_FPS are exactly the pair that quietly stops
    ''' agreeing.
    ''' </summary>
    Private Function lap_frames() As Integer
        If map_scene Is Nothing Then Return 0
        Dim cp = map_scene.cam_path
        If cp Is Nothing OrElse Not cp.closed Then Return 0
        If cp.total_len <= 0.0F Then Return 0
        If cp.points Is Nothing OrElse cp.points.Length = 0 Then Return 0
        Dim spd = Math.Max(0.1F, cp.points(0).speed)
        Return CInt(Math.Ceiling(cp.total_len / spd * CAPTURE_FPS))
    End Function

    ''' <summary>
    ''' Assemble a folder of frames into an mp4, on a background thread.
    '''
    ''' Off the render thread because it takes minutes: run inline it would
    ''' freeze the window solid, and the app would look hung at exactly the
    ''' moment it is doing the thing that was asked for.
    '''
    ''' The name carries the date and the rate, so successive renders never
    ''' overwrite each other and no video is ever lost to a second capture.
    ''' It also puts the frame rate in the file name, where it can be checked
    ''' against what was intended.
    ''' </summary>
    ''' <summary>Name of the note a capture leaves beside its frames.</summary>
    Private Const CAPTURE_INFO As String = "capture.txt"

    ''' <summary>
    ''' Record what the frames in this folder are, and what produced them.
    '''
    ''' A PNG sequence carries no frame rate - the rate is a decision made when
    ''' it is played, and nothing in the files remembers the one it was shot
    ''' for. Assembling later from whatever the fps combo happened to say is how
    ''' a 60 fps capture became a 97 second video of a 48 second flight.
    '''
    ''' Everything else here is for the same reason one step out: a folder of
    ''' 3000 PNGs found weeks later should be able to say which map it is, which
    ''' route, how fast the camera flew it and whether the terrain was allowed to
    ''' finish streaming - because two captures that differ only in Wait VT look
    ''' identical in a file listing and are not the same footage at all.
    '''
    ''' Plain key=value text on purpose: as much for a person opening it as for
    ''' the code reading it back. frames=0 means a capture that has started and
    ''' not yet finished.
    ''' </summary>
    Private Sub write_capture_info(dir As String, w As Integer, h As Integer, frames As Integer)
        Try
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("# nuTerra flight capture")
            sb.AppendLine("fps=" & CAPTURE_FPS)
            sb.AppendLine(String.Format("size={0}x{1}", w, h))
            sb.AppendLine("frames=" & frames)
            sb.AppendLine("map=" & MAP_NAME_NO_PATH)
            sb.AppendLine(String.Format("started={0:yyyy-MM-dd HH:mm:ss}", DateTime.Now))

            ' The route, so the footage can be tied back to the path that made
            ' it - a .campath is edited and re-saved constantly.
            If map_scene IsNot Nothing AndAlso map_scene.cam_path IsNot Nothing AndAlso
               map_scene.cam_path.loaded Then
                Dim cp = map_scene.cam_path
                sb.AppendLine(String.Format("path_len_m={0:0.0}", cp.total_len))
                sb.AppendLine("path_points=" & If(cp.points Is Nothing, 0, cp.points.Length))
                sb.AppendLine("path_closed=" & If(cp.closed, 1, 0))
                If cp.points IsNot Nothing AndAlso cp.points.Length > 0 Then
                    sb.AppendLine(String.Format("speed_mps={0:0.00}", cp.points(0).speed))
                End If
                ' Empty, not Nothing, when no file was involved - the field is
                ' initialised to "" so a null check would write a blank line.
                If Not String.IsNullOrEmpty(cp.source_file) Then
                    sb.AppendLine("path_file=" & cp.source_file)
                End If
            End If

            ' The switches that change what the footage looks like. Wait VT in
            ' particular is the difference between sharp terrain and terrain
            ' resolving on camera, and nothing in the frames says which it was.
            sb.AppendLine("wait_vt=" & If(WAIT_VT, 1, 0))
            ' What the gate was set to, and whether it ever gave way. A capture
            ' with forced_frames above 0 contains terrain that had not finished
            ' streaming, which is exactly the thing Wait VT is turned on to
            ' prevent - and it is invisible in the frames until it is looked at.
            sb.AppendLine("vt_settle_run=" & RECORD_SETTLE_RUN)
            sb.AppendLine("vt_wait_max=" & RECORD_WAIT_MAX)
            sb.AppendLine("forced_frames=" & RECORD_FORCED_FRAMES)
            sb.AppendLine("hide_hud=" & If(RECORD_HIDE_HUD, 1, 0))
            sb.AppendLine("fixed_step=" & If(FLY_FIXED_STEP, 1, 0))
            sb.AppendLine("stop_at_end=" & If(RECORD_STOP_AT_END, 1, 0))
            sb.AppendLine("keep_pngs=" & If(RECORD_KEEP_PNGS, 1, 0))

            IO.File.WriteAllText(IO.Path.Combine(dir, CAPTURE_INFO), sb.ToString())
        Catch ex As Exception
            ' Losing the note costs the default rate later, never the capture.
            LogThis("record: could not write {0} - {1}", CAPTURE_INFO, ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' The rate the frames in a folder were CAPTURED at, or 0 when the folder
    ''' does not say - a sequence shot before this note existed, or copied in
    ''' from somewhere else.
    ''' </summary>
    Private Function captured_fps(dir As String) As Integer
        Try
            Dim p = IO.Path.Combine(dir, CAPTURE_INFO)
            If Not IO.File.Exists(p) Then Return 0
            For Each line In IO.File.ReadAllLines(p)
                Dim t = line.Trim()
                If t.StartsWith("fps=", StringComparison.OrdinalIgnoreCase) Then
                    Dim v As Integer
                    If Integer.TryParse(t.Substring(4).Trim(), v) AndAlso v > 0 Then Return v
                End If
            Next
        Catch
            ' Unreadable is the same as absent: fall back to the combo.
        End Try
        Return 0
    End Function

    ''' <summary>
    ''' The most recently written mp4 in a folder, or Nothing.
    '''
    ''' By write time rather than by name: the names carry a timestamp, but a
    ''' folder can also hold files put there by hand, and the newest thing is
    ''' what someone means by "the one I just made".
    ''' </summary>
    Private Function newest_mp4(dir As String) As String
        Try
            Dim best As String = Nothing
            Dim best_t = DateTime.MinValue
            For Each f In IO.Directory.GetFiles(dir, "*.mp4")
                Dim t = IO.File.GetLastWriteTimeUtc(f)
                If t > best_t Then
                    best_t = t
                    best = f
                End If
            Next
            Return best
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Re-sample the output folder, no more than once a second.
    '''
    ''' Also re-samples immediately when the folder itself changes, so picking a
    ''' new one in Browse does not show a second of the old one's facts.
    ''' </summary>
    Private Sub refresh_folder_facts(dir As String)
        If dir = folder_facts_dir AndAlso
           (DateTime.Now - folder_facts_at).TotalSeconds < 1.0 Then Return

        folder_facts_dir = dir
        folder_facts_at = DateTime.Now
        folder_fps = captured_fps(dir)
        folder_newest_mp4 = newest_mp4(dir)
    End Sub

    ''' <summary>
    ''' The next free still_NNN.png in a folder, counting up from 000.
    '''
    ''' Scanned for the first gap rather than kept in a counter, because the
    ''' FOLDER outlives the process: stills taken in an earlier session are
    ''' still sitting there, and a counter starting from zero would write
    ''' straight over them.
    '''
    ''' Numbered rather than timestamped so the files sort and read the way a
    ''' set of stills should. Naming them from the frame index cannot work - it
    ''' restarts at zero for every capture, so every still would be still_000.
    ''' </summary>
    Private Function next_still_path(dir As String) As String
        Try
            For i = 0 To 9999
                Dim p = IO.Path.Combine(dir, String.Format("still_{0:000}.png", i))
                If Not IO.File.Exists(p) Then Return p
            Next
        Catch
        End Try
        ' 10000 stills in one folder is not a real case, but silently failing to
        ' save would be. Fall back to the clock rather than lose the frame.
        Return IO.Path.Combine(dir, String.Format("still_{0:yyyyMMdd_HHmmss_fff}.png", DateTime.Now))
    End Function

    ''' <summary>
    ''' Delete every captured frame in a folder. Returns how many went.
    '''
    ''' One implementation for both ends of the job - housekeeping when a
    ''' capture starts, and the tidy-up when an mp4 has been built from them -
    ''' so the two can never disagree about what counts as a frame.
    '''
    ''' frame_*.png only, never *.*. The mp4 lives in this folder too, and so
    ''' does capture.txt and any still_NNN.png; a sweep that took those would
    ''' destroy the very thing the frames existed to produce.
    ''' </summary>
    ''' <summary>
    ''' Write the Flight Recorder panel's settings to disk, immediately.
    '''
    ''' Called from each control as it CHANGES, not on the way out. The exit
    ''' path only runs on a clean shutdown, and this app is force killed often
    ''' enough - to free the exe for a build, or when a capture is abandoned -
    ''' that "saved on exit" means "usually lost". That is exactly how the
    ''' output folder kept reverting to C: after being pointed at G:.
    '''
    ''' My.Settings.Save rewrites the whole user.config, so this is a file write
    ''' per toggle. That is the right trade: these are things a person clicks a
    ''' handful of times, not values that move per frame.
    ''' </summary>
    ''' <summary>
    ''' Is this exactly one of the sizes the combo offers?
    '''
    ''' The panel finds its selection by matching CAPTURE_W against the table,
    ''' and falls back to "Window" when it cannot. Without this check a settings
    ''' file holding an unlisted size would show "Window" in the combo while the
    ''' capture ran at that size - the UI and the behaviour disagreeing, with
    ''' the UI being the one that is wrong.
    ''' </summary>
    Public Shared Function is_offered_capture_size(w As Integer, h As Integer) As Boolean
        For i = 0 To CAP_SIZES_W.Length - 1
            If CAP_SIZES_W(i) = w AndAlso CAP_SIZES_H(i) = h Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' Put the window at the capture size, remembering what it was.
    '''
    ''' A 1080-tall CLIENT area does not fit under a title bar on a 1080 screen
    ''' - it needs about 1111px of screen - so at that size the border comes off
    ''' for the duration. Moved to the top left corner so the whole client area
    ''' is certainly on screen; a resized window left where it was can hang off
    ''' an edge, and what hangs off is part of the frame.
    ''' </summary>
    Private Sub apply_capture_size()
        win_resized_for_capture = False
        If CAPTURE_W <= 0 OrElse CAPTURE_H <= 0 Then Return
        If ClientSize.X = CAPTURE_W AndAlso ClientSize.Y = CAPTURE_H Then Return

        saved_win_size = New Vector2i(ClientSize.X, ClientSize.Y)
        saved_win_pos = Location
        saved_win_border = WindowBorder
        win_resized_for_capture = True

        Try
            If CAPTURE_H >= 1080 Then WindowBorder = WindowBorder.Hidden
            Location = New Vector2i(0, 0)
            Size = New Vector2i(CAPTURE_W, CAPTURE_H)
            LogThis("record: window {0}x{1} -> {2}x{3} for the capture",
                    saved_win_size.X, saved_win_size.Y, CAPTURE_W, CAPTURE_H)
        Catch ex As Exception
            LogThis("record: could not resize the window - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Give the window back exactly as it was - size, position and border.
    '''
    ''' Called from the RECORD_FLIGHT transition rather than from each of the
    ''' places a capture can end. There are four of those - the lap finishing,
    ''' Stop, Esc and the error path - and the one that gets forgotten is the
    ''' one that leaves the window 640x360 with the menu out of reach.
    ''' </summary>
    Private Sub restore_window_size()
        If Not win_resized_for_capture Then Return
        win_resized_for_capture = False
        Try
            WindowBorder = saved_win_border
            Size = saved_win_size
            Location = saved_win_pos
            LogThis("record: window restored to {0}x{1}", saved_win_size.X, saved_win_size.Y)
        Catch ex As Exception
            LogThis("record: could not restore the window - {0}", ex.Message)
        End Try
    End Sub

    Private Sub save_render_settings()
        Try
            My.Settings.capture_fps = CAPTURE_FPS
            My.Settings.wait_vt = WAIT_VT
            My.Settings.record_hud = RECORD_HIDE_HUD
            My.Settings.fixed_step = FLY_FIXED_STEP
            My.Settings.stop_at_end = RECORD_STOP_AT_END
            My.Settings.keep_pngs = RECORD_KEEP_PNGS
            My.Settings.capture_w = CAPTURE_W
            My.Settings.capture_h = CAPTURE_H
            My.Settings.Save()
        Catch ex As Exception
            ' Never let a settings write break the panel it was drawn from.
            LogThis("record: could not save render settings - {0}", ex.Message)
        End Try
    End Sub

    Private Function clear_frames(dir As String) As Integer
        Dim gone = 0
        Try
            For Each f In IO.Directory.GetFiles(dir, "frame_*.png")
                Try
                    IO.File.Delete(f)
                    gone += 1
                Catch ex As Exception
                    ' One locked file - an open viewer, a sync client - is not a
                    ' reason to abandon the job. Name it and carry on.
                    LogThis("record: could not delete {0} - {1}",
                            IO.Path.GetFileName(f), ex.Message)
                End Try
            Next
        Catch ex As Exception
            LogThis("record: could not clear {0} - {1}", dir, ex.Message)
        End Try
        Return gone
    End Function

    Private Sub start_mp4_encode(dir As String, fps As Integer)
        If ENCODE_RUNNING Then Return

        Dim out_path = IO.Path.Combine(
            dir, String.Format("flight_{0:yyyy-MM-dd_HHmm}_{1}fps.mp4", DateTime.Now, fps))

        Dim total = 0
        Try
            If IO.Directory.Exists(dir) Then
                total = IO.Directory.GetFiles(dir, "frame_*.png").Length
            End If
        Catch
        End Try
        If total = 0 Then
            ENCODE_MESSAGE = "no frames to assemble"
            LogThis("encode: {0}", ENCODE_MESSAGE)
            Return
        End If

        ENCODE_TOTAL = total
        ENCODE_DONE = 0
        ENCODE_MESSAGE = Nothing
        ENCODE_RUNNING = True
        LogThis("encode: {0} frames at {1} fps -> {2}", total, fps, IO.Path.GetFileName(out_path))

        Dim started_at = DateTime.Now
        Threading.Tasks.Task.Run(
            Sub()
                Dim err As String = Nothing
                Try
                    err = Mp4Encoder.EncodeFolder(dir, fps, out_path,
                                                  Sub(n) ENCODE_DONE = n)
                Catch ex As Exception
                    err = ex.Message
                End Try

                Dim took = DateTime.Now - started_at
                If err Is Nothing Then
                    Dim mb = 0L
                    Try : mb = New IO.FileInfo(out_path).Length \ (1024L * 1024L) : Catch : End Try
                    ENCODE_MESSAGE = String.Format("{0}  ({1} MB, {2:0} s)",
                                                   IO.Path.GetFileName(out_path), mb, took.TotalSeconds)
                    LogThis("encode: done - {0}", ENCODE_MESSAGE)

                    ' The frames have served their purpose. Deleted here and
                    ' ONLY here - after a successful encode, never after a
                    ' failed one, because a failure is exactly when they are
                    ' still needed to try again.
                    If Not RECORD_KEEP_PNGS Then
                        Dim gone = clear_frames(dir)
                        LogThis("encode: cleared {0} frames from {1}", gone, dir)
                        ENCODE_MESSAGE &= String.Format("  -  {0} frames cleared", gone)
                    End If
                Else
                    ENCODE_MESSAGE = "failed: " & err
                    LogThis("encode: {0}", ENCODE_MESSAGE)
                    ' A half written mp4 has no index and opens in nothing. It
                    ' would sit in the folder looking like a result.
                    Try
                        If IO.File.Exists(out_path) Then IO.File.Delete(out_path)
                    Catch
                    End Try
                End If
                ENCODE_RUNNING = False
            End Sub)
    End Sub

    ''' <summary>
    ''' Second chance before a capture, and the folder flush that goes with it.
    '''
    ''' Runs from ProcessEvents, not the UI pass, for the same reason the folder
    ''' picker does: a modal Win32 dialog opened part way through building a
    ''' frame blocks the GL thread inside it.
    ''' </summary>
    Private Sub confirm_and_start_capture()
        If map_scene Is Nothing OrElse Not map_scene.cam_path.loaded Then Return

        ' Not while an assembly is running. Starting a capture clears the
        ' folder, and the encoder is part way through reading the frames that
        ' would be deleted - the video would end wherever the deletion caught
        ' up with it, and nothing about the file would say so.
        If ENCODE_RUNNING Then
            System.Windows.Forms.MessageBox.Show(
                "An mp4 is still being assembled from the frames in this folder." & vbCrLf & vbCrLf &
                "Starting a capture now would delete them out from under it.",
                "Flight Recorder",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning)
            Return
        End If

        Dim existing As String() = New String() {}
        Try
            If IO.Directory.Exists(RECORD_DIR) Then
                ' frame_*.png, never *.*. The finished mp4 usually lives in this
                ' same folder, and a flush that swept that away would destroy
                ' the one thing the frames exist to produce.
                existing = IO.Directory.GetFiles(RECORD_DIR, "frame_*.png")
            End If
        Catch ex As Exception
            LogThis("record: cannot list {0} - {1}", RECORD_DIR, ex.Message)
        End Try

        ' Say what is about to happen in the terms it happens in - how many
        ' frames, how long that is, where they land, what it costs. A prompt
        ' that only asks "are you sure" moves the click without informing it.
        Dim lap = lap_frames()
        Dim msg As New Text.StringBuilder()
        msg.AppendLine("Start capture?")
        msg.AppendLine()
        If lap > 0 Then
            msg.AppendLine(String.Format("{0} frames at {1} fps - {2:0.0} seconds of video",
                                         lap, CAPTURE_FPS, lap / CSng(Math.Max(1, CAPTURE_FPS))))
        End If
        msg.AppendLine("To: " & RECORD_DIR)
        If CAPTURE_W > 0 AndAlso CAPTURE_H > 0 Then
            msg.AppendLine(String.Format("The window will resize to {0} x {1}, and back afterwards.",
                                         CAPTURE_W, CAPTURE_H))
        End If
        msg.AppendLine()

        If existing.Length = 0 Then
            msg.AppendLine("The folder has no frames in it to clear.")
        Else
            msg.AppendLine(String.Format("Clearing the {0} frames already there.", existing.Length))
        End If
        msg.AppendLine()
        ' Say what happens to the NEW frames too. The line above is about what
        ' is being destroyed now; this is what will be left behind afterwards,
        ' and they are governed by different things.
        If RECORD_KEEP_PNGS Then
            msg.AppendLine("The new frames will be KEPT after the mp4 is built.")
        Else
            msg.AppendLine("The new frames will be deleted once the mp4 is built.")
        End If

        ' Defaulting to No. The costly answer should never be the one a return
        ' key already under a finger will give.
        If System.Windows.Forms.MessageBox.Show(
               msg.ToString(), "Flight Recorder",
               System.Windows.Forms.MessageBoxButtons.YesNo,
               System.Windows.Forms.MessageBoxIcon.Question,
               System.Windows.Forms.MessageBoxDefaultButton.Button2) <>
           System.Windows.Forms.DialogResult.Yes Then
            LogThis("record: capture cancelled")
            Return
        End If

        ' ALWAYS clear first, whatever Keep PNGs says.
        '
        ' This is housekeeping, not a preference. Frames are numbered from zero
        ' every run, so a shorter capture landing on a longer one's frames
        ' leaves the old tail in place and an encoder splices the end of a
        ' previous flight onto this one, with nothing in the files admitting it.
        ' Keep PNGs decides whether the NEW frames survive once the mp4 is
        ' built - a different question, asked at the other end of the job.
        If existing.Length > 0 Then
            Dim gone = clear_frames(RECORD_DIR)
            LogThis("record: cleared {0} of {1} frames from {2}",
                    gone, existing.Length, RECORD_DIR)
        End If

        map_scene.cam_path.travelled = 0.0F

        ' Resize BEFORE the settled run is cleared, not after. The resize
        ' rebuilds MainFBO and with it the VT feedback buffer, which invalidates
        ' the run for the same reason the teleport below does - clearing first
        ' and then resizing would leave the gate reading a count earned at the
        ' old resolution.
        apply_capture_size()

        ' The rewind above is a teleport, so the VT's settled run describes a
        ' view that no longer exists - and it is high, because the camera has
        ' been sitting still while this panel was being set up. Left alone it
        ' waves frame 0 straight through the settle gate, and the first frame
        ' of every capture is shot against terrain that was never requested.
        '
        ' Cleared here rather than inside the gate: the gate's job is to wait
        ' for a run, and it cannot tell a run that was earned at this position
        ' from one earned at the last one.
        If map_scene.terrain IsNot Nothing AndAlso map_scene.terrain.vt IsNot Nothing Then
            map_scene.terrain.vt.ResetSettled()
        End If

        RECORD_FRAME_INDEX = 0
        RECORD_HOLD = False
        RECORD_FORCED_FRAMES = 0
        ' Cleared here, at the start, rather than at the end of the last run.
        record_clock.Reset()
        ' The route overlay is drawn along the exact line being flown. Left on,
        ' it is a coloured streak through every frame of the video.
        SHOW_CAM_PATH = False
        FLY_CAM_PATH = True
        RECORD_FLIGHT = True
    End Sub

    Private Sub save_record_frame()
        Try
            ' Paused writes nothing and touches no counter, so the sequence
            ' picks up exactly where it left off.
            If RECORD_PAUSED Then Return

            ' Wait for the virtual texture before shooting. Terrain arrives over
            ' several frames, so without this the early part of a flight is
            ' recorded mid-stream and the ground visibly sharpens on playback.
            ' No virtual texture yet means NOT settled, not "no objection". The
            ' terrain object exists long before its vt does, so defaulting this
            ' optimistically waved the capture through during load.
            '
            ' WAIT_VT off skips the wait entirely - Integer.MaxValue rather than
            ' a branch around the block, so the counters below still reset the
            ' same way on every path through.
            Dim settled = 0
            If Not WAIT_VT Then
                settled = Integer.MaxValue
            ElseIf map_scene.terrain IsNot Nothing AndAlso map_scene.terrain.vt IsNot Nothing Then
                settled = map_scene.terrain.vt.SettledFrames
            End If

            If settled < RECORD_SETTLE_RUN AndAlso record_wait_frames < RECORD_WAIT_MAX Then
                record_wait_frames += 1
                RECORD_HOLD = True
                Return
            End If

            If record_wait_frames >= RECORD_WAIT_MAX Then
                ' Counted, not just logged. This frame breaks the settle rule,
                ' and whether the finished video contains any such frames is a
                ' property of the footage - it has to outlive the log buffer.
                RECORD_FORCED_FRAMES += 1
                LogThis("record: frame {0} shot with the VT still unsettled after {1} frames",
                        RECORD_FRAME_INDEX, record_wait_frames)
            End If

            RECORD_HOLD = False
            record_wait_frames = 0

            ' A still test goes in its own folder. Sharing the flight's
            ' folder would interleave frame numbers with a 24k sequence.
            Dim dir = If(RECORD_STILL > 0, IO.Path.Combine(RECORD_DIR, "still"), RECORD_DIR)

            ' H.264 has no odd dimensions. In a window the framebuffer is
            ' whatever the client area happens to be - 1920x1009 in the run
            ' that found this - and libx264 and Media Foundation BOTH refuse
            ' it outright, so an entire sequence becomes unencodable after the
            ' fact, with no way to tell until the capture is already spent.
            '
            ' Round DOWN rather than up. Dropping a row is a genuine crop of
            ' what was rendered; padding invents a black line and scaling to
            ' fit resamples every frame in the lap.
            Dim cap_w = MainFBO.width - (MainFBO.width Mod 2)
            Dim cap_h = MainFBO.height - (MainFBO.height Mod 2)

            If RECORD_FRAME_INDEX = 0 Then
                IO.Directory.CreateDirectory(dir)
                ' Say the size ONCE, at the top of the sequence. The dimensions
                ' decide whether the frames can be assembled at all, and finding
                ' that out from the encoder afterwards is too late.
                LogThis("record: {0}x{1} at {2} fps -> {3}", cap_w, cap_h, CAPTURE_FPS, dir)
                write_capture_info(dir, cap_w, cap_h, 0)
            End If

            ' Where this frame goes, decided before the readback so the log
            ' below can name it. A still is numbered in its own sequence; a
            ' flight frame is numbered by its place in the video.
            Dim out_file = If(RECORD_STILL > 0,
                              next_still_path(dir),
                              IO.Path.Combine(dir, String.Format("frame_{0:00000000}.png",
                                                                 RECORD_FRAME_INDEX)))

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
            ' Alignment 4, not 1. GDI+ pads every 24bpp row out to a 4 byte
            ' boundary, and alignment 4 is what makes GL pad it the same way.
            ' At 1 the two agree only when the width is a multiple of 4: 1920
            ' is, which is why this never showed, but a 1918 wide window would
            ' have sheared every row. Now that widths other than 1920 reach
            ' here, that stops being hypothetical.
            GL.PixelStore(PixelStoreParameter.PackAlignment, 4)

            Using bmp As New Bitmap(cap_w, cap_h, Imaging.PixelFormat.Format24bppRgb)
                Dim bits = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                        ImageLockMode.WriteOnly, bmp.PixelFormat)
                GL.ReadPixels(0, 0, cap_w, cap_h,
                              OpenGL.PixelFormat.Bgr, PixelType.UnsignedByte, bits.Scan0)
                bmp.UnlockBits(bits)
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY)
                bmp.Save(out_file, ImageFormat.Png)
            End Using

            GL.PixelStore(PixelStoreParameter.PackAlignment, 4)
            GL.ReadBuffer(ReadBufferMode.Front)

            RECORD_FRAME_INDEX += 1

            ' One frame and done. Returns before the lap check below, which is
            ' about the flight and means nothing here.
            If RECORD_STILL > 0 Then
                RECORD_STILL = 0
                LogThis("record: still saved - {0}", out_file)
                Return
            End If

            ' Stop after one lap. A closed path never ends on its own, and an
            ' unattended capture that runs until the disk fills is worse than
            ' one that stops a frame early.
            '
            ' Through lap_frames, which is what the confirmation quoted too:
            ' the number agreed to has to be the number written.
            Dim lap = lap_frames()
            If lap > 0 AndAlso RECORD_FRAME_INDEX >= lap Then
                RECORD_FLIGHT = False
                RECORD_HOLD = False
                ' Land it. Left flying, the camera carries on round a route
                ' that is already recorded, and the last thing on screen is
                ' not the last thing in the file.
                If RECORD_STOP_AT_END Then FLY_CAM_PATH = False
                LogThis("record: lap complete, {0} frames at {1} fps in {2}",
                        RECORD_FRAME_INDEX, CAPTURE_FPS, RECORD_DIR)
                If RECORD_FORCED_FRAMES > 0 Then
                    LogThis("record: WARNING {0} of {1} frames were shot with the VT unsettled",
                            RECORD_FORCED_FRAMES, RECORD_FRAME_INDEX)
                End If
                ' Again, now that the count is known. Written at the start too,
                ' so a capture that is stopped or crashes still leaves a folder
                ' that says what is in it - just with frames=0 meaning unknown.
                write_capture_info(RECORD_DIR, cap_w, cap_h, RECORD_FRAME_INDEX)
                ' Straight into the assembly. The frames exist to become a
                ' video and the capture knows the rate they were shot at -
                ' leaving that to be remembered later is how the first one
                ' came out at 60 fps when it was captured for 30.
                start_mp4_encode(RECORD_DIR, CAPTURE_FPS)
            End If

        Catch ex As Exception
            ' Stop rather than throw one error per frame for the rest of the lap.
            RECORD_FLIGHT = False
            LogThis("record: stopped after {0} frames - {1}", RECORD_FRAME_INDEX, ex.Message)
        End Try
    End Sub

    ' <summary>
    ''' Start Path Studio - F9, or the button on the menu bar.
    '''
    ''' One of only two things nuTerra and Path Studio share; the other is the
    ''' .campath file Path Studio writes and MapCamPath reads. Deliberately no
    ''' deeper coupling than launching an exe.
    '''
    ''' Nothing is redirected and nothing is waited on. Path Studio owns its own
    ''' window and reports its own errors - including a missing Python, which is
    ''' its launcher's job to explain, not this one's.
    ''' </summary>
    Private Sub start_path_studio()
        Try
            Dim exe = find_path_studio()
            If exe Is Nothing Then
                LogThis("Path Studio: PathStudio.exe not found beside nuTerra or in the solution")
                Return
            End If
            Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(exe) With {
                .UseShellExecute = False,
                .WorkingDirectory = IO.Path.GetDirectoryName(exe)})
            LogThis("Path Studio: started {0}", exe)
        Catch ex As Exception
            LogThis("Path Studio: could not start - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' PathStudio.exe, installed beside nuTerra or built elsewhere in the tree.
    '''
    ''' Beside the exe is the installed layout. The bin paths are what make this
    ''' work from a development build, where the two projects have separate
    ''' output folders - and Debug is checked before Release because a developer
    ''' running nuTerra from Debug means the Debug one.
    ''' </summary>
    Private Function find_path_studio() As String
        Dim dir = New IO.DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim here = IO.Path.Combine(dir.FullName, "PathStudio.exe")
            If IO.File.Exists(here) Then Return here
            For Each cfg In {"Debug", "Release"}
                Dim built = IO.Path.Combine(dir.FullName, "PathStudio", "bin", cfg,
                                            "net6.0-windows", "PathStudio.exe")
                If IO.File.Exists(built) Then Return built
            Next
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    Public Overrides Sub ProcessEvents()
        MyBase.ProcessEvents()

        If NEED_TO_PICK_RECORD_DIR Then
            NEED_TO_PICK_RECORD_DIR = False
            Using dlg As New FolderBrowserDialog()
                dlg.Description = "Where to write the captured frames"
                dlg.UseDescriptionForTitle = True
                ' Start where the current setting points, when it still exists -
                ' otherwise the dialog opens at the desktop and the drive with
                ' the room on it has to be found again every time.
                ' Open somewhere real. On a first run the folder has not been
                ' created yet - it is made on the first frame - so fall back to
                ' its parent rather than dropping the user at the desktop.
                If IO.Directory.Exists(RECORD_DIR) Then
                    dlg.SelectedPath = RECORD_DIR
                Else
                    Dim parent = IO.Path.GetDirectoryName(RECORD_DIR)
                    If parent IsNot Nothing AndAlso IO.Directory.Exists(parent) Then
                        dlg.SelectedPath = parent
                    End If
                End If
                If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                    RECORD_DIR = dlg.SelectedPath
                    LogThis("record: output folder set to {0}", RECORD_DIR)
                    ' Persist HERE, and not only in the exit path.
                    '
                    ' A capture run is exactly when this app is least likely to
                    ' shut down politely - it gets force killed to free the exe
                    ' for a build, or it dies mid lap - and My.Settings.Save on
                    ' the way out never runs in either case. The folder then
                    ' reverts silently, and the next several gigabytes land on
                    ' the default drive with nothing on screen saying so.
                    '
                    ' Choosing a folder is a deliberate act by the user. It
                    ' should survive on its own, the instant it is made, rather
                    ' than depending on how the process happens to end. Saved
                    ' even when a -dir argument started this run: a UI pick is a
                    ' decision, where the command line switch is a one off.
                    Try
                        My.Settings.record_dir = RECORD_DIR
                        My.Settings.Save()
                    Catch ex As Exception
                        ' Never let a settings write take the capture down.
                        LogThis("record: could not persist output folder - {0}", ex.Message)
                    End Try
                End If
            End Using
        End If

        If NEED_TO_CONFIRM_CAPTURE Then
            NEED_TO_CONFIRM_CAPTURE = False
            confirm_and_start_capture()
        End If

        If NEED_TO_DO_SCREEN_CAPTURE Then
            NEED_TO_DO_SCREEN_CAPTURE = False
            Dim Save_Dialog = New SaveFileDialog()
            Save_Dialog.Filter = "PNG|*.png"
            Save_Dialog.Title = "Save PNG"
            If Save_Dialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                SCREEN_CAPTURE_FILENAME = Save_Dialog.FileName
            End If
        End If
    End Sub
End Class

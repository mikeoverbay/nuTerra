Imports System.Reflection

Module Program
    Public main_window As Window

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        ' preload
        Dim asm = Assembly.Load("nuTerraCPP")

        ' A one-off out= must not become the saved preference, so it is
        ' remembered and skipped when the settings are written back.
        Dim record_dir_from_cli = False

        ' nuTerra.exe <map_name> [cam=r,ax,ay,lx,ly,lz] [freezefx] [clean]
        '                        [snap|snapquit] [settle=N]
        '
        ' settle=N overrides how many frames to wait after MAP_LOADED before the
        ' automatic Snapshot fires. The default 150 is about 2.5 s at 60 fps,
        ' which is NOT long enough for a particle column to fill: the emitters
        ' run at 1-5 per second against 3-6 s lifetimes, so a steady-state column
        ' needs several seconds of simulation before it is representative.
        ' Capturing at 150 photographs a column that is still building.
        '
        ' Note the sim advances on REAL dt while this counts FRAMES, so the
        ' amount of smoke at capture depends on the frame rate unless the
        ' timestep is pinned. For a bit-exact before/after, pin the timestep as
        ' well as raising this.
        '
        ' The cam form is exactly what Snapshot prints, so a view can be set up
        ' by hand, saved, and reproduced verbatim on every later launch. That
        ' makes an automated before/after screenshot diff meaningful - without
        ' a fixed viewpoint the camera moves between runs and the comparison is
        ' worthless.
        For Each a In args
            If a.StartsWith("cam=", StringComparison.OrdinalIgnoreCase) Then
                Dim parts = a.Substring(4).Split(","c)
                If parts.Length = 6 Then
                    Dim v(5) As Single
                    Dim ok = True
                    For i = 0 To 5
                        If Not Single.TryParse(parts(i), Globalization.NumberStyles.Float,
                                               Globalization.CultureInfo.InvariantCulture, v(i)) Then ok = False
                    Next
                    If ok Then STARTUP_CAM = v
                End If
            ElseIf a.Equals("freezefx", StringComparison.OrdinalIgnoreCase) Then
                FREEZE_FX = True
            ElseIf a.Equals("clean", StringComparison.OrdinalIgnoreCase) Then
                CLEAN_VIEW = True
            ElseIf a.Equals("half", StringComparison.OrdinalIgnoreCase) Then
                HALF_SIZE_WINDOW = True
            ElseIf a.Equals("noglow", StringComparison.OrdinalIgnoreCase) Then
                ' Turn the FX glow off, so a run WITH and a run WITHOUT this
                ' argument are an A/B of the glow at one camera. There is no
                ' other headless way to move it - FX_GLOW's only other writer
                ' is the ImGui checkbox.
                FX_GLOW = False
            ' glowradius= / glowpasses= / glowstrength= are gone with the
            ' sliders - those three are Const now and cannot be assigned.
            ' noglow survives because FX_GLOW is still a real toggle, and it is
            ' the only headless way to A/B the glow at all.
            ElseIf a.Equals("gridfx", StringComparison.OrdinalIgnoreCase) Then
                ' Light the FX volumetrics from the baked probe field. Off by
                ' default, so a run WITHOUT this argument is the bit-identical
                ' negative control for free.
                USE_SH_GRID_FX = True
            ElseIf a.StartsWith("gridfxoffset=", StringComparison.OrdinalIgnoreCase) Then
                ' Only for A/B'ing the normal push against 0. See
                ' SH_GRID_OFFSET_FX - 0 is the shipped answer.
                Dim gf As Single
                If Single.TryParse(a.Substring(13), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, gf) Then
                    SH_GRID_OFFSET_FX = gf
                End If
            ElseIf a.Equals("blackfx", StringComparison.OrdinalIgnoreCase) Then
                BLACK_BEFORE_FX = True
            ElseIf a.Equals("fullscreen", StringComparison.OrdinalIgnoreCase) Then
                FULLSCREEN_WINDOW = True
            ElseIf a.Equals("fly", StringComparison.OrdinalIgnoreCase) Then
                ' Start on the baked path the moment the map finishes loading.
                FLY_CAM_PATH = True
            ElseIf a.Equals("record", StringComparison.OrdinalIgnoreCase) Then
                ' Implies fly - the recorder only writes while the flight is
                ' actually driving the camera, so 'record' alone would sit there
                ' producing nothing.
                FLY_CAM_PATH = True
                RECORD_FLIGHT = True
            ElseIf a.StartsWith("out=", StringComparison.OrdinalIgnoreCase) Then
                RECORD_DIR = a.Substring(4)
                record_dir_from_cli = True
            ElseIf a.StartsWith("still=", StringComparison.OrdinalIgnoreCase) Then
                ' Take ONE still from wherever the camera starts, no flight.
                ' Pair it with cam= to shoot the same view twice.
                '
                ' The count is parsed and then ignored - still capture is a
                ' single frame now, not a burst. The argument keeps its shape so
                ' an existing script still runs instead of failing to parse, and
                ' any positive number means the same thing: take one.
                Dim sn As Integer
                If Integer.TryParse(a.Substring(6), sn) AndAlso sn > 0 Then
                    RECORD_STILL = 1
                End If
            ElseIf a.StartsWith("settle=", StringComparison.OrdinalIgnoreCase) Then
                ' Parsed independently of snap/snapquit and applied after the
                ' loop, so the order of the arguments on the command line does
                ' not matter.
                Dim n As Integer
                If Integer.TryParse(a.Substring(7), n) AndAlso n > 0 Then
                    SETTLE_FRAMES = n
                End If
            ElseIf a.Equals("snap", StringComparison.OrdinalIgnoreCase) Then
                AUTO_SNAP_FRAMES = 150
            ElseIf a.Equals("snapquit", StringComparison.OrdinalIgnoreCase) Then
                AUTO_SNAP_FRAMES = 150
                AUTO_SNAP_QUIT = True
            ElseIf STARTUP_MAP Is Nothing Then
                STARTUP_MAP = a
            End If
        Next

        ' Applied after the loop so "settle=600 snapquit" and "snapquit
        ' settle=600" behave the same. Only meaningful when a snap was asked
        ' for at all.
        If SETTLE_FRAMES > 0 AndAlso AUTO_SNAP_FRAMES > 0 Then
            AUTO_SNAP_FRAMES = SETTLE_FRAMES
        End If

        If My.Settings.UpgradeRequired Then
            My.Settings.Upgrade()
            My.Settings.UpgradeRequired = False
            My.Settings.Save()
        End If

        ' AFTER the upgrade, or a fresh install reads the pre-upgrade store. The
        ' guard is what makes the setting survive a first run: an install with no
        ' record_dir yet falls through to the default already in RECORD_DIR
        ' rather than being handed an empty string.
        If Not record_dir_from_cli AndAlso
           Not String.IsNullOrWhiteSpace(My.Settings.record_dir) Then
            RECORD_DIR = My.Settings.record_dir
        End If

        ' The rest of the Flight Recorder panel. Restored AFTER the command line
        ' has been parsed would undo an explicit switch, so anything settable
        ' from the command line has to guard itself the way record_dir does -
        ' none of these are, today.
        '
        ' capture_fps is validated rather than trusted: the combo can only
        ' offer 15, 30 and 60, but a hand-edited user.config can hold anything,
        ' and a rate the UI cannot represent would show an empty combo and
        ' encode at a speed nothing asked for.
        If My.Settings.capture_fps = 15 OrElse My.Settings.capture_fps = 30 OrElse
           My.Settings.capture_fps = 60 Then
            CAPTURE_FPS = My.Settings.capture_fps
        End If
        WAIT_VT = My.Settings.wait_vt
        RECORD_HIDE_HUD = My.Settings.record_hud
        FLY_FIXED_STEP = My.Settings.fixed_step
        RECORD_STOP_AT_END = My.Settings.stop_at_end
        RECORD_KEEP_PNGS = My.Settings.keep_pngs

        ' Validated against the offered list, not trusted. A hand-edited config
        ' could otherwise ask for a size the combo cannot show, and the capture
        ' would run at a resolution nothing in the UI admits to - the combo
        ' would fall back to displaying "Window" while the capture used
        ' something else entirely.
        If Window.is_offered_capture_size(My.Settings.capture_w, My.Settings.capture_h) Then
            CAPTURE_W = My.Settings.capture_w
            CAPTURE_H = My.Settings.capture_h
        End If

        main_window = New Window
        main_window.Run()

        ' Per-map settings first - it reads the live values, and only writes
        ' when something actually moved since the map was loaded.
        If MAP_LOADED Then
            modMapSettings.SaveIfChanged(MAP_NAME_NO_PATH)
        End If

        If Not record_dir_from_cli Then My.Settings.record_dir = RECORD_DIR
        My.Settings.capture_fps = CAPTURE_FPS
        My.Settings.wait_vt = WAIT_VT
        My.Settings.record_hud = RECORD_HIDE_HUD
        My.Settings.fixed_step = FLY_FIXED_STEP
        My.Settings.stop_at_end = RECORD_STOP_AT_END
        My.Settings.keep_pngs = RECORD_KEEP_PNGS
        My.Settings.capture_w = CAPTURE_W
        My.Settings.capture_h = CAPTURE_H
        My.Settings.use_tessellation = USE_TESSELLATION
        CommonProperties.SaveToSettings()
        My.Settings.Save()
    End Sub
End Module

Imports System.Reflection

Module Program
    Public main_window As Window

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        ' preload
        Dim asm = Assembly.Load("nuTerraCPP")

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
            ElseIf a.StartsWith("still=", StringComparison.OrdinalIgnoreCase) Then
                ' Record N frames from wherever the camera starts, no flight.
                ' Pair it with cam= to test the same view twice.
                Dim sn As Integer
                If Integer.TryParse(a.Substring(6), sn) AndAlso sn > 0 Then
                    RECORD_STILL = sn
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

        main_window = New Window
        main_window.Run()

        ' Per-map settings first - it reads the live values, and only writes
        ' when something actually moved since the map was loaded.
        If MAP_LOADED Then
            modMapSettings.SaveIfChanged(MAP_NAME_NO_PATH)
        End If

        My.Settings.use_tessellation = USE_TESSELLATION
        CommonProperties.SaveToSettings()
        My.Settings.Save()
    End Sub
End Module

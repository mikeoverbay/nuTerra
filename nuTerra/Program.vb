Imports System.Reflection

Module Program
    Public main_window As Window

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        ' preload
        Dim asm = Assembly.Load("nuTerraCPP")

        ' nuTerra.exe <map_name> [cam=r,ax,ay,lx,ly,lz] [freezefx] [clean] [snap|snapquit]
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
            ElseIf a.Equals("blackfx", StringComparison.OrdinalIgnoreCase) Then
                BLACK_BEFORE_FX = True
            ElseIf a.Equals("snap", StringComparison.OrdinalIgnoreCase) Then
                AUTO_SNAP_FRAMES = 150
            ElseIf a.Equals("snapquit", StringComparison.OrdinalIgnoreCase) Then
                AUTO_SNAP_FRAMES = 150
                AUTO_SNAP_QUIT = True
            ElseIf STARTUP_MAP Is Nothing Then
                STARTUP_MAP = a
            End If
        Next

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

Imports System.Reflection

Module Program
    Public main_window As Window

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        ' preload
        Dim asm = Assembly.Load("nuTerraCPP")

        ' Optional: nuTerra.exe <map_name> loads that map straight away.
        If args.Length > 0 Then
            STARTUP_MAP = args(0)
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

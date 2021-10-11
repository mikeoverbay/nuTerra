Module Program
    Public main_window As Window

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        If My.Settings.UpgradeRequired Then
            My.Settings.Upgrade()
            My.Settings.UpgradeRequired = False
            My.Settings.Save()
        End If

        main_window = New Window
        main_window.Run()
    End Sub
End Module

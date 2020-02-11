Imports System.IO
Imports System.Runtime.CompilerServices

Module modUtilities

    ' Allows us to split by strings. Not just characters.
    <Extension()> _
    Public Function Split(ByVal input As String, _
                          ByVal ParamArray delimiter As String()) As String()
        Return input.Split(delimiter, StringSplitOptions.None)
        Dim a(0) As String
        a(0) = input
        Return a
    End Function

    Public Sub load_destructibles()
        Dim script_pkg As Ionic.Zip.ZipFile = Nothing
        Dim ms As New MemoryStream
        Try
            script_pkg = Ionic.Zip.ZipFile.Read(GAME_PATH & "scripts.pkg")
            Dim script As Ionic.Zip.ZipEntry = script_pkg("scripts\destructibles.xml")
            script.Extract(ms)
            ms.Position = 0
            Dim bdata As Byte()
            Dim br As BinaryReader = New BinaryReader(ms)
            bdata = br.ReadBytes(br.BaseStream.Length)
            Dim des As MemoryStream = New MemoryStream(bdata, 0, bdata.Length)
            des.Write(bdata, 0, bdata.Length)
            openXml_stream(des, "destructibles.xml")
            des.Close()
            des.Dispose()
        Catch ex As Exception
            MsgBox("Something failed loading the scripts\destructibles.xml file", MsgBoxStyle.Exclamation, "Dammit!")
        End Try
        script_pkg.Dispose()
        ms.Dispose()
        Dim entry, mName As DataTable
        entry = xmldataset.Tables("entry")
        mName = xmldataset.Tables("matName")
        Dim q = From fname_ In entry.AsEnumerable Join mat In mName On _
                fname_.Field(Of Int32)("entry_ID") Equals mat.Field(Of Int32)("entry_ID") _
                              Select _
                  filename = fname_.Field(Of String)("filename"), _
                  mat = mat.Field(Of String)("matName_Text")

        DESTRUCTABLE_DATA_TABLE = New DataTable("destructibles")
        DESTRUCTABLE_DATA_TABLE.Columns.Add("filename", System.Type.GetType("System.String"))
        DESTRUCTABLE_DATA_TABLE.Columns.Add("mat_name", System.Type.GetType("System.String"))

        For Each it In q
            If it.mat IsNot Nothing Then
                Dim row = DESTRUCTABLE_DATA_TABLE.NewRow
                row(0) = it.filename.Replace("model", "visual").ToLower
                row(1) = it.mat.Replace("model", "visual").ToLower
                DESTRUCTABLE_DATA_TABLE.Rows.Add(row)
            End If
        Next
        '---------------------------------------
    End Sub

    Public Function can_this_be_broken(ByVal s As String) As Boolean

        Dim q = From d In DESTRUCTABLE_DATA_TABLE.AsEnumerable
                Where d.Field(Of String)("filename") = s
                Select dname = d.Field(Of String)("mat_name")

        If q.Count > 0 Then
            Return False 'we do not want to ignore this one.
        End If
        Return True ' not on list so probably dont want to draw it?
    End Function
End Module

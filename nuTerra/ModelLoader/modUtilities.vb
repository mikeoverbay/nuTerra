Imports System
Imports System.IO

Module modUtilities
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

        dest_buildings.filename = New List(Of String)
        dest_buildings.matName = New List(Of String)
        For Each it In q
            If it.mat IsNot Nothing Then

                If InStr(it.filename, "bld_Construc") = 0 Then
                    dest_buildings.filename.Add(it.filename.Replace("model", "visual").ToLower)
                    dest_buildings.matName.Add(it.mat.ToLower)
                End If
            End If
        Next
        '---------------------------------------
    End Sub
End Module

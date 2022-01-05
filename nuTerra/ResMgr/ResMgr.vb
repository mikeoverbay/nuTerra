Imports System.IO
Imports System.Xml
Imports Ionic.Zip

NotInheritable Class ResMgr
    Shared RES_MODS_PATH As String
    Shared ReadOnly FILENAME_TO_ZIP_ENTRY As New Dictionary(Of String, ZipEntry)
    Shared ReadOnly FILE_EXTENSIONS_TO_USE As New HashSet(Of String)({
            ".dds", ".model", ".primitives_processed",
            ".visual_processed", ".cdata_processed",
            ".bin", ".xml", ".png", ".settings", ".srt",
            ".texformat", ".atlas_processed"
            })

    Public Shared Sub Init(wot_path As String)
        Dim xDoc As New XmlDocument
        xDoc.Load(Path.Combine(wot_path, "paths.xml"))
        Dim first_path = xDoc.SelectSingleNode("//Paths/Path").InnerText.Remove(0, 2)
        RES_MODS_PATH = Path.Combine(wot_path, first_path)

        For Each pkgNode In xDoc.SelectNodes("//Paths/Packages/Package")
            Dim pkg = pkgNode.InnerText.Remove(0, 2)

            If Path.GetFileName(pkg).StartsWith("vehicles_") Then
                ' ignore vehicle packages
                Continue For
            End If

            If Path.GetFileName(pkg).StartsWith("audioww-") Then
                ' ignore audio packages
                Continue For
            End If

            Dim pkgPath = Path.Combine(wot_path, pkg)
            Using entry As New ZipFile(pkgPath)
                For Each file In entry.Entries
                    If file.IsDirectory Then
                        Continue For
                    End If
                    Dim lowered_fn = file.FileName.ToLower
                    If FILE_EXTENSIONS_TO_USE.Contains(Path.GetExtension(lowered_fn)) Then
                        If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
                            Continue For
                        End If
                        FILENAME_TO_ZIP_ENTRY.Add(lowered_fn, file)
                    End If
                Next
            End Using
        Next

    End Sub

    Public Shared Function Lookup(filename As String) As ZipEntry
        If File.Exists(Path.Combine(RES_MODS_PATH, filename)) Then
            Dim tmpZip As New ZipFile
            tmpZip.AddFile(Path.Combine(RES_MODS_PATH, filename), filename)
            Dim tmpMs As New MemoryStream
            tmpZip.Save(tmpMs)
            tmpMs.Position = 0
            tmpZip = ZipFile.Read(tmpMs)
            Return tmpZip.Entries(0)
        End If

        Dim lowered_fn = filename.ToLower.Replace("\", "/")
        If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
            Return FILENAME_TO_ZIP_ENTRY(lowered_fn)
        End If

        If Not FILE_EXTENSIONS_TO_USE.Contains(Path.GetExtension(lowered_fn)) Then
            Stop
        End If

        LogThis("file not found: {0}", filename)
        Return Nothing
    End Function

    Public Shared Function openXML(filepath As String) As XmlElement
        Dim entry = Lookup(filepath)
        Return openXML(entry)
    End Function

    Public Shared Function openXML(entry As ZipEntry) As XmlElement
        If entry Is Nothing Then
            Return Nothing
        End If
        Using ms As New MemoryStream
            entry.Extract(ms)
            Return openXML(ms)
        End Using
    End Function

    Public Shared Function openXML(ms As MemoryStream) As XmlElement
        Dim xDoc As New XmlDocument
        ms.Position = 0

        Dim reader As New BinaryReader(ms)
        Dim magic = reader.ReadUInt32()

        If magic = PackedSection.Packed_Header Then
            reader.ReadSByte()
            Dim PS As New PackedSection
            Dim dictionary = PS.readDictionary(reader)
            Dim xmlroot = xDoc.CreateNode(XmlNodeType.Element, "root", "")
            PS.readElement(reader, xmlroot, xDoc, dictionary)
            xDoc.AppendChild(xmlroot)
        Else
            ms.Position = 0
            xDoc.Load(ms)
        End If

        Return xDoc.DocumentElement
    End Function
End Class

Imports System.IO
Imports System.Xml
Imports Ionic.Zip
Imports System.IO.Directory

NotInheritable Class ResMgr
    Shared FILENAME_TO_ZIP_ENTRY As New Dictionary(Of String, ZipEntry)
    Shared FILE_EXTENSIONS_TO_USE As New HashSet(Of String)({
            ".dds", ".model", ".primitives_processed",
            ".visual_processed", ".model", ".cdata_processed",
            ".bin", ".xml", ".png", ".settings", ".srt",
            ".texformat", ".atlas_processed"
            })

    Function SearchForFiles(ByVal RootFolder As String, ByVal FileFilter() As String) As List(Of String)
        Dim ReturnedData As New List(Of String)                             'List to hold the search results
        Dim FolderStack As New Stack(Of String)                             'Stack for searching the folders
        FolderStack.Push(RootFolder)                                        'Start at the specified root folder
        Do While FolderStack.Count > 0                                      'While there are things in the stack
            Dim ThisFolder As String = FolderStack.Pop                      'Grab the next folder to process
            Try                                                             'Use a try to catch any errors
                For Each SubFolder In GetDirectories(ThisFolder)            'Loop through each sub folder in this folder
                    FolderStack.Push(SubFolder)                             'Add to the stack for further processing
                Next                                                        'Process next sub folder
                For Each FileExt In FileFilter                              'For each File filter specified
                    ReturnedData.AddRange(GetFiles(ThisFolder, FileExt))    'Search for and return the matched file names
                Next                                                        'Process next FileFilter
            Catch ex As Exception                                           'For simplicity sake
            End Try                                                         'We'll ignore the errors
        Loop                                                                'Process next folder in the stack
        Return ReturnedData                                                 'Return the list of files that match
    End Function

    Public Shared Sub Init(wot_res_path As String)
        For Each pkgPath In Directory.GetFiles(wot_res_path, "*.pkg")
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
        Dim lowered_fn = filename.ToLower.Replace("\", "/")
        If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
            Return FILENAME_TO_ZIP_ENTRY(lowered_fn)
        End If

        If Not FILE_EXTENSIONS_TO_USE.Contains(Path.GetExtension(lowered_fn)) Then
            Stop
        End If

        LogThis("file not found: " + filename)
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

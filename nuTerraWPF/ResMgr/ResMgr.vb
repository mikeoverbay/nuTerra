Imports System.IO
Imports System.Xml
Imports Ionic.Zip

Public NotInheritable Class ResMgr
    Public Shared res_path As String
    Public Shared pkgs_path As String

    Private Shared PS As New PackedSection

    Private Shared GUI_PACKAGE As ZipFile
    Private Shared GUI_PACKAGE_PART2 As ZipFile

    Public Shared Function Init(wot_path As String) As Boolean
        res_path = Path.Combine(wot_path, "res")
        pkgs_path = Path.Combine(res_path, "packages")

        'needed to load image elements
        If File.Exists(Path.Combine(pkgs_path, "gui.pkg")) Then
            'old WoT version
            GUI_PACKAGE = New ZipFile(Path.Combine(pkgs_path, "gui.pkg"))
        ElseIf File.Exists(Path.Combine(pkgs_path, "gui-part1.pkg")) And File.Exists(Path.Combine(pkgs_path, "gui-part2.pkg")) Then
            'new WoT version ~v1.10
            GUI_PACKAGE = New ZipFile(Path.Combine(pkgs_path, "gui-part1.pkg"))
            GUI_PACKAGE_PART2 = New ZipFile(Path.Combine(pkgs_path, "gui-part2.pkg"))
        Else
            Return False
        End If
        Return True
    End Function

    Public Shared Function LoadPNG(path As String) As ImageSource
        path = path.ToLower()
        If Not path.EndsWith(".png") Then
            Stop
            Return Nothing
        End If
        Dim entry = GUI_PACKAGE.Item(path)
        If entry Is Nothing Then
            entry = GUI_PACKAGE_PART2.Item(path)
        End If
        If entry IsNot Nothing Then
            Using stream As New MemoryStream
                entry.Extract(stream)
                Dim decoder = PngBitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
                Dim frame = decoder.Frames.First()
                frame.Freeze()
                Return frame
            End Using
        End If
        Return Nothing
    End Function

    Public Shared Function openXML(stream As MemoryStream) As XmlDocument
        Dim xDoc As New XmlDocument
        stream.Position = 0

        Dim reader As New BinaryReader(stream)
        Dim magic = reader.ReadUInt32()

        If magic = PackedSection.Packed_Header Then
            reader.ReadSByte()
            Dim dictionary = PS.readDictionary(reader)
            Dim xmlroot = xDoc.CreateNode(XmlNodeType.Element, "root", "")
            PS.readElement(reader, xmlroot, xDoc, dictionary)
            xDoc.AppendChild(xmlroot)
        Else
            stream.Position = 0
            xDoc.Load(stream)
        End If

        Return xDoc
    End Function
End Class

Imports System.IO
Imports System.Xml
Imports Ionic.Zip

Public NotInheritable Class ResMgr
    Private Shared PS As New PackedSection

    Public Shared Function openXML(filepath As String) As XmlElement
        Dim entry = Packages.Lookup(filepath)
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



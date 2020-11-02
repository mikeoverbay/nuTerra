Imports System.IO
Imports System.Text
Imports System.Xml
Imports Ionic.Zip

Module vis_main
    Public xmldataset As New DataSet
    Public xml_name As String
    Public PackedFileName As String = ""
    Public ReadOnly sver As String = "0.5"
    Public ReadOnly stitle As String = "WoT Mod Tools "
    Public PS As New Packed_Section()
    Public PF As New Primitive_File()
    Public xDoc As New XmlDocument
    Public ReadOnly Binary_Header As UInt32 = &H42A14E65UI

    Private Function FormatXml(sUnformattedXml As String) As String
        'load unformatted xml into a dom
        Dim ts As String = sUnformattedXml.Replace("><", ">" + vbCrLf + "<")
        sUnformattedXml = ts
        Dim xd As New XmlDocument()
        xd.LoadXml(sUnformattedXml)

        'will hold formatted xml

        Dim sb As New StringBuilder()

        'pumps the formatted xml into the StringBuilder above

        Dim sw As New StringWriter(sb)

        'does the formatting

        Dim xtw As XmlTextWriter = Nothing

        Try
            'point the xtw at the StringWriter

            xtw = New XmlTextWriter(sw)

            'we want the output formatted

            xtw.Formatting = Formatting.Indented

            'get the dom to dump its contents into the xtw 

            xd.WriteTo(xtw)
        Catch
        Finally
            'clean up even if error

            If xtw IsNot Nothing Then
                xtw.Close()
            End If
        End Try


        Return sb.ToString()
    End Function

    Public Sub DecodePackedFile(reader As BinaryReader)
        reader.ReadSByte()
        Dim dictionary As List(Of String) = PS.readDictionary(reader)

        Dim xmlroot As XmlNode = xDoc.CreateNode(XmlNodeType.Element, PackedFileName, "")
        xDoc.OuterXml.Replace("><", ">" + vbCrLf + "<")
        xDoc.OuterXml.Replace(">" + vbLf + "<", ">" + vbCrLf + "<")


        PS.readElement(reader, xmlroot, xDoc, dictionary)
        Dim xml_string As String = xmlroot.InnerXml


        Dim fileS As New MemoryStream
        Dim fbw As New BinaryWriter(fileS)
        fileS.Position = 0


        xDoc.AppendChild(xmlroot)
        Dim Id = xmlroot.Name + "/gameplayTypes"
        Dim node As XmlElement = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            Dim n2 = node.SelectSingleNode("assault")
            If n2 IsNot Nothing Then
                node.RemoveChild(n2)
            End If
        End If
        If node IsNot Nothing Then
            Dim n2 = node.SelectSingleNode("assault2")
            If n2 IsNot Nothing Then
                node.RemoveChild(n2)
            End If
        End If
        node = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            Dim n2 = node.SelectSingleNode("domination")
            If n2 IsNot Nothing Then
                node.RemoveChild(n2)
            End If
        End If
        node = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            Dim n2 = node.SelectSingleNode("fallout")
            If n2 IsNot Nothing Then
                node.RemoveChild(n2)
            End If
        End If
        node = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            Dim n2 = node.SelectSingleNode("fallout2")
            If n2 IsNot Nothing Then
                node.RemoveChild(n2)
            End If
        End If
        Id = xmlroot.Name + "/trees"
        node = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            node.ParentNode.RemoveChild(node)
        End If
        Id = xmlroot.Name + "/fallingAtoms"
        node = xDoc.SelectSingleNode(Id)
        If node IsNot Nothing Then
            node.ParentNode.RemoveChild(node)
        End If

        fileS.Position = 0
        xDoc.Save(fileS)

        Dim fbr As New BinaryReader(fileS)
        fileS.Position = 0
        TheXML_String = fbr.ReadChars(fileS.Length)

        TheXML_String = TheXML_String.Replace("<primitiveGroup> ", "<primitiveGroup>")
        For i = 90 To 0 Step -1
            Dim ast = TheXML_String.Replace("<primitiveGroup>" + i.ToString, "<primitiveGroup>" + ControlChars.CrLf.ToCharArray() + "<PG_ID>" + i.ToString + "</PG_ID>")
            TheXML_String = ast
        Next
        TheXML_String = TheXML_String.Replace("><", ">" + vbCrLf + "<")
        TheXML_String = TheXML_String.Replace(">" + vbLf + "<", ">" + vbCrLf + "<")

        fileS.Position = 0
        Try
            xmldataset.ReadXml(fileS)

        Catch ex As Exception
            MsgBox("File: " + GAME_PATH + MAP_NAME_NO_PATH + " XML arena deff will not load." + vbCrLf +
                      "Please report this bug.", MsgBoxStyle.Exclamation, "packed XML file Error...")
        End Try


        fbr.Close()
        fbw.Close()
        fileS.Close()
        fileS.Dispose()

    End Sub

    Public Sub ReadPrimitiveFile(file As String)
        Dim F As New FileStream(file, FileMode.Open, FileAccess.Read)
        Dim reader As New BinaryReader(F)

        Dim ptiComment As XmlComment = xDoc.CreateComment("DO NOT SAVE THIS FILE! THIS CODE IS JUST FOR INFORMATION PUPORSES!")

        Dim xmlprimitives As XmlNode = xDoc.CreateNode(XmlNodeType.Element, "primitives", "")

        PF.ReadPrimitives(reader, xmlprimitives, xDoc)

        xDoc.AppendChild(ptiComment)
        xDoc.AppendChild(xmlprimitives)

    End Sub

    Public TheXML_String As String = ""
    Public Function openXml_stream(ByVal f As MemoryStream, ByVal PackedFileName_in As String) As Boolean
        xDoc = New XmlDocument
        f.Position = 0
        xmldataset.Clear()
        While xmldataset.Tables.Count > 0
            xmldataset.Reset()
        End While
        PackedFileName = "File_" & PackedFileName_in.ToLower()
        Dim reader As New BinaryReader(f)
        Dim head As UInt32 = reader.ReadUInt32()
        If head = Packed_Section.Packed_Header Then
            DecodePackedFile(reader)
        ElseIf head = Binary_Header Then
        Else
            If Not PackedFileName.Contains(".xml") Then
                PackedFileName &= ".xml"
            End If
        End If
        reader.Close()
        Return True
    End Function


    Public Sub setupthedome()
        'Will be reworked
	End Sub

    Public Function TransformXML(xmlString As String, xlsString As String) As MemoryStream
        Dim memStream As MemoryStream = Nothing
        Try
            ' Create a xml-document from the sent-in xml-string
            Dim xmlDoc As New XmlDocument
            xmlDoc.LoadXml(xmlString)

            ' Load the xls into another document
            Dim xslDoc As New XmlDocument
            xslDoc.LoadXml(xlsString)

            ' Create a transformation
            Dim trans As New System.Xml.Xsl.XslCompiledTransform
            trans.Load(xslDoc)

            ' Create a memory stream for output
            memStream = New MemoryStream()

            ' Do the transformation according to the XSLT and save the result in our memory stream
            trans.Transform(xmlDoc, Nothing, memStream)
            memStream.Position = 0
        Catch ex As Exception
            Throw ex
        End Try

        Return memStream
    End Function


End Module



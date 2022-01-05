Imports System.IO
Imports System.Text
Imports System.Xml

Public Class PackedSection
    Public Shared ReadOnly Packed_Header As UInt32 = &H62A14E45UI

    Public Class DataDescriptor
        Public ReadOnly address As Integer
        Public ReadOnly [end] As Integer
        Public ReadOnly type As Integer

        Public Sub New(ByVal [end] As Integer, ByVal type As Integer, ByVal address As Integer)
            Me.[end] = [end]
            Me.type = type
            Me.address = address
        End Sub
    End Class

    Public Class ElementDescriptor
        Public ReadOnly nameIndex As Integer
        Public ReadOnly dataDescriptor As DataDescriptor

        Public Sub New(nameIndex As Integer, dataDescriptor As DataDescriptor)
            Me.nameIndex = nameIndex
            Me.dataDescriptor = dataDescriptor
        End Sub
    End Class

    Public Function readStringTillZero(reader As BinaryReader) As String
        Dim builder As New StringBuilder
        Dim c As Char = reader.ReadChar()
        While c <> vbNullChar
            builder.Append(c)
            c = reader.ReadChar()
        End While
        Return builder.ToString()
    End Function

    Public Function readDictionary(reader As BinaryReader) As List(Of String)
        Dim dictionary As New List(Of String)()
        Dim counter As Integer = 0
        Dim text As String = readStringTillZero(reader)

        While Not (text.Length = 0)
            dictionary.Add(text)
            text = readStringTillZero(reader)
            counter += 1
        End While
        Return dictionary
    End Function

    Public Function readDataDescriptor(reader As BinaryReader) As DataDescriptor
        Dim selfEndAndType As Integer = reader.ReadInt32()
        Return New DataDescriptor(selfEndAndType And &HFFFFFFF, selfEndAndType >> 28, CInt(reader.BaseStream.Position))
    End Function

    Public Function readElementDescriptors(reader As BinaryReader, number As Integer) As ElementDescriptor()
        Dim elements As ElementDescriptor() = New ElementDescriptor(number - 1) {}
        For i As Integer = 0 To number - 1
            Dim nameIndex As Integer = reader.ReadInt16()
            Dim dataDescriptor As DataDescriptor = readDataDescriptor(reader)
            elements(i) = New ElementDescriptor(nameIndex, dataDescriptor)
        Next
        Return elements
    End Function

    Public Function readString(reader As BinaryReader, lengthInBytes As Integer) As String
        Dim rString As New String(reader.ReadChars(lengthInBytes), 0, lengthInBytes)
        Return rString
    End Function

    Public Function readNumber(reader As BinaryReader, lengthInBytes As Integer) As String
        Dim Number As String
        Select Case lengthInBytes
            Case 1
                Number = reader.ReadSByte().ToString()
            Case 2
                Number = reader.ReadInt16().ToString()
            Case 4
                Number = reader.ReadInt32().ToString()
            Case 8
                Number = reader.ReadInt64().ToString()
            Case Else
                Number = "0"
        End Select
        Return Number
    End Function

    Public Shared Function readFloats(reader As BinaryReader, lengthInBytes As Integer) As String
        Dim n As Integer = lengthInBytes / 4
        Dim sb As New StringBuilder
        For i As Integer = 0 To n - 1
            If i <> 0 Then
                sb.Append(" ")
            End If
            Dim rFloat As Single = reader.ReadSingle()
            sb.Append(rFloat.ToString("0.000000"))
        Next
        Return sb.ToString()
    End Function

    Public Function readBoolean(reader As BinaryReader, lengthInBytes As Integer) As Boolean
        Dim bool As Boolean = lengthInBytes = 1
        If bool Then
            If reader.ReadSByte() <> 1 Then
                Throw New System.ArgumentException("Boolean error")
            End If
        End If
        Return bool
    End Function

    Public Function readBase64(ByVal reader As BinaryReader, lengthInBytes As Integer) As String
        Dim bytes = reader.ReadBytes(lengthInBytes)
        Return Convert.ToBase64String(bytes)
    End Function

    Public Function readAndToHex(reader As BinaryReader, lengthInBytes As Integer) As String
        Dim bytes As SByte() = New SByte(lengthInBytes - 1) {}
        For i As Integer = 0 To lengthInBytes - 1
            bytes(i) = reader.ReadSByte()
        Next
        Dim sb As New StringBuilder("[ ")
        For Each b As Byte In bytes
            sb.Append(Convert.ToString((b And &HFF), 16))
            sb.Append(" ")
        Next
        sb.Append("]L:")
        sb.Append(lengthInBytes)

        Return sb.ToString()
    End Function

    Public Function readData(reader As BinaryReader, ByRef dictionary As List(Of String), ByRef element As XmlNode, ByRef xDoc As XmlDocument, offset As Integer, ByRef dataDescriptor As DataDescriptor) As Integer
        Dim lengthInBytes As Integer = dataDescriptor.[end] - offset
        If dataDescriptor.type = &H0 Then
            ' Element                
            readElement(reader, element, xDoc, dictionary)
        ElseIf dataDescriptor.type = &H1 Then
            ' String

            element.InnerText = readString(reader, lengthInBytes)
        ElseIf dataDescriptor.type = &H2 Then
            ' Integer number
            element.InnerText = readNumber(reader, lengthInBytes)
        ElseIf dataDescriptor.type = &H3 Then
            ' Floats
            Dim str As String = readFloats(reader, lengthInBytes)

            Dim strData As String() = str.Split(" "c)
            If strData.Length = 12 Then
                Dim row0 As XmlNode = xDoc.CreateElement("row0")
                Dim row1 As XmlNode = xDoc.CreateElement("row1")
                Dim row2 As XmlNode = xDoc.CreateElement("row2")
                Dim row3 As XmlNode = xDoc.CreateElement("row3")
                row0.InnerText = strData(0) + " " + strData(1) + " " + strData(2)
                row1.InnerText = strData(3) + " " + strData(4) + " " + strData(5)
                row2.InnerText = strData(6) + " " + strData(7) + " " + strData(8)
                row3.InnerText = strData(9) + " " + strData(10) + " " + strData(11)
                element.AppendChild(row0)
                element.AppendChild(row1)
                element.AppendChild(row2)
                element.AppendChild(row3)
            Else
                element.InnerText = str
            End If
        ElseIf dataDescriptor.type = &H4 Then
            ' Boolean

            If readBoolean(reader, lengthInBytes) Then
                element.InnerText = "true"
            Else
                element.InnerText = "false"

            End If
        ElseIf dataDescriptor.type = &H5 Then
            ' Base64
            element.InnerText = readBase64(reader, lengthInBytes)
        Else
            Throw New System.ArgumentException("Unknown type of """ + element.Name + ": " + dataDescriptor.ToString() + " " + readAndToHex(reader, lengthInBytes))
        End If

        Return dataDescriptor.[end]
    End Function

    Public Sub readElement(reader As BinaryReader, ByRef element As XmlNode, ByRef xDoc As XmlDocument, ByRef dictionary As List(Of String))
        Dim childrenNmber As Integer = reader.ReadInt16()
        Dim selfDataDescriptor As DataDescriptor = readDataDescriptor(reader)
        Dim children As ElementDescriptor() = readElementDescriptors(reader, childrenNmber)

        Dim offset As Integer = readData(reader, dictionary, element, xDoc, 0, selfDataDescriptor)

        For Each elementDescriptor As ElementDescriptor In children
            Dim child As XmlNode = xDoc.CreateElement(dictionary(elementDescriptor.nameIndex))
            offset = readData(reader, dictionary, child, xDoc, offset, elementDescriptor.dataDescriptor)
            element.AppendChild(child)
        Next

    End Sub
End Class
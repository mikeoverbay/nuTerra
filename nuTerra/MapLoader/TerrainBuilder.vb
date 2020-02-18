Imports System.IO
Imports Ionic.Zip
Imports Tao.DevIl

Module TerrainBuilder
    '=======================================================================
    'move this to Modules/modTypeStructures.vb when we are done debugging
    Public NotInheritable Class theMap
        Public Shared chunks = Nothing
        Public Shared MINI_MAP_ID As Integer

    End Class
    Public Structure chunk_
        Public name As String
        Public cdata() As Byte
        Public heights_data() As Byte
        Public blend_textures_data() As Byte
        Public dominateTestures_data() As Byte
        Public holes_data() As Byte
        Public layers_data() As Byte
        Public normals_data() As Byte
    End Structure
    '=======================================================================
    Public Sub Create_Terrain()
        get_all_chunk_file_data()
    End Sub
    Public Sub get_all_chunk_file_data()
        'Reads and stores the contents of each cdata_processed

        Dim chunks(15 * 15) As chunk_ 'I don't expect any maps larger than 225 chunks
        Dim cnt As Integer = 0
        For Each entry In MAP_PACKAGE.Entries

            'find mini_map
            If entry.FileName.Contains("mmap.dds") Then
                Dim ms As New MemoryStream
                entry.Extract(ms)
                theMap.MINI_MAP_ID = load_image_from_stream(Il.IL_DDS, ms, entry.FileName, False, False)
            End If

            'find cdata chunks
            If entry.FileName.Contains("cdata") Then

                chunks(cnt).name = Path.GetFileNameWithoutExtension(entry.FileName)

                Dim ms As New MemoryStream
                entry.Extract(ms)

                ReDim chunks(cnt).cdata(ms.Length)
                ms.Position = 0
                Dim br As New BinaryReader(ms)
                chunks(cnt).cdata = br.ReadBytes(ms.Length)

                Dim cms As New MemoryStream(chunks(cnt).cdata)
                cms.Position = 0
                Using t2 As Ionic.Zip.ZipFile = Ionic.Zip.ZipFile.Read(cms)

                    Dim stream = New MemoryStream
                    br = New BinaryReader(stream)

                    Dim blend = t2("terrain2/blend_textures")
                    blend.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).blend_textures_data = br.ReadBytes(stream.Length)

                    Dim dominate = t2("terrain2/dominanttextures")
                    dominate.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).dominateTestures_data = br.ReadBytes(stream.Length)

                    Dim heights = t2("terrain2/heights")
                    heights.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).heights_data = br.ReadBytes(stream.Length)

                    Dim layers = t2("terrain2/layers")
                    layers.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).layers_data = br.ReadBytes(stream.Length)

                    Dim normals = t2("terrain2/normals")
                    normals.Extract(stream)
                    stream.Position = 0
                    chunks(cnt).normals_data = br.ReadBytes(stream.Length)
                End Using
                chunks(cnt).cdata = Nothing ' Free up memory
                cnt += 1

            End If
        Next
        ReDim Preserve chunks(cnt - 1)

    End Sub
End Module

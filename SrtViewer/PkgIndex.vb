Imports System.IO
Imports System.Xml
Imports System.IO.Compression

''' <summary>
''' Minimal read only index of the World of Tanks packages, just enough to pull
''' .srt files and their textures. Deliberately independent of nuTerra's ResMgr so
''' the viewer stays standalone.
''' </summary>
Public Class PkgIndex

    Private ReadOnly map As New Dictionary(Of String, ZipArchiveEntry)(StringComparer.OrdinalIgnoreCase)
    ' Held open on purpose: a ZipArchiveEntry is only valid while its
    ' ZipArchive lives, and the entries in `map` outlive TryOpen.
    Private ReadOnly zips As New List(Of ZipArchive)

    Public Property GamePath As String
    Public ReadOnly Property Count As Integer
        Get
            Return map.Count
        End Get
    End Property

    Public Shared Function TryOpen(gamePath As String) As PkgIndex
        If String.IsNullOrEmpty(gamePath) Then Return Nothing
        Dim px = IO.Path.Combine(gamePath, "paths.xml")
        If Not File.Exists(px) Then Return Nothing

        Dim idx As New PkgIndex With {.GamePath = gamePath}
        Dim doc As New XmlDocument
        doc.Load(px)

        For Each node As XmlNode In doc.SelectNodes("//Paths/Packages/Package")
            Dim rel = node.InnerText.Trim()
            If rel.StartsWith("./") Then rel = rel.Remove(0, 2)
            Dim leaf = IO.Path.GetFileName(rel)
            ' vehicles and audio hold nothing we need and cost a lot to index
            If leaf.StartsWith("vehicles_", StringComparison.OrdinalIgnoreCase) Then Continue For
            If leaf.StartsWith("audioww-", StringComparison.OrdinalIgnoreCase) Then Continue For

            Dim full = IO.Path.Combine(gamePath, rel)
            If Not File.Exists(full) Then Continue For

            Try
                Dim fs As New FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim z As New ZipArchive(fs, ZipArchiveMode.Read, leaveOpen:=False)
                idx.zips.Add(z)
                For Each e In z.Entries
                    ' A directory entry is one whose name ends in "/" - there is no
                    ' IsDirectory on ZipArchiveEntry. The extension test below would
                    ' reject them anyway; kept so the intent stays readable.
                    If e.FullName.EndsWith("/") Then Continue For
                    Dim ext = IO.Path.GetExtension(e.FullName).ToLower
                    If ext <> ".srt" AndAlso ext <> ".dds" Then Continue For
                    Dim key = e.FullName.Replace("\", "/").ToLower
                    If Not idx.map.ContainsKey(key) Then idx.map.Add(key, e)
                Next
            Catch
                ' skip anything unreadable
            End Try
        Next
        Return idx
    End Function

    Public Function Lookup(name As String) As ZipArchiveEntry
        If name Is Nothing Then Return Nothing
        Dim key = name.Replace("\", "/").ToLower
        Dim e As ZipArchiveEntry = Nothing
        If map.TryGetValue(key, e) Then Return e
        Return Nothing
    End Function

    ''' <summary>
    ''' Prefers the high resolution variant. HD textures live in the *_hd.pkg
    ''' packages under the same path with an _hd suffix, at twice the resolution.
    ''' </summary>
    Public Function LookupHD(name As String) As ZipArchiveEntry
        If name Is Nothing Then Return Nothing
        If name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) AndAlso
           Not name.EndsWith("_hd.dds", StringComparison.OrdinalIgnoreCase) Then
            Dim hd = name.Substring(0, name.Length - 4) & "_hd.dds"
            Dim e = Lookup(hd)
            If e IsNot Nothing Then Return e
        End If
        Return Lookup(name)
    End Function

    Public Function Read(entry As ZipArchiveEntry) As Byte()
        If entry Is Nothing Then Return Nothing
        Using src = entry.Open()
            Using ms As New MemoryStream
                src.CopyTo(ms)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>Every .srt in the packages, sorted.</summary>
    Public Function AllSrt() As List(Of String)
        Dim out As New List(Of String)
        For Each kv In map
            If kv.Key.EndsWith(".srt") Then out.Add(kv.Key)
        Next
        out.Sort()
        Return out
    End Function
End Class

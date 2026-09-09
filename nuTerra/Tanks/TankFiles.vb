Imports System.IO
Imports System.IO.Compression
Imports System.Xml

''' <summary>
''' Where the tank module gets its bytes. The core's ResMgr deliberately skips
''' every vehicles_*.pkg (ResMgr.vb:46), so this keeps its own index of exactly
''' those, built once from the same paths.xml the core reads, and falls back to
''' ResMgr for everything else (item_defs live in scripts.pkg, which the core
''' does index). Archives are shared with the core through PkgEntry.ArchiveFor,
''' so nothing is opened twice.
'''
''' HD textures: the *_hd.pkg packages carry the same paths with an _hd suffix
''' on the file name. LoadTexture tries that first, the way ResMgr.LookupHD
''' does for map content.
''' </summary>
Public Module TankFiles

    Private indexed As Boolean
    ' lowercase, forward-slashed entry name -> (package path, entry name as stored)
    Private ReadOnly index As New Dictionary(Of String, KeyValuePair(Of String, String))

    Private Sub BuildIndex()
        indexed = True
        Try
            Dim wot = My.Settings.GamePath
            Dim xDoc As New XmlDocument
            xDoc.Load(Path.Combine(wot, "paths.xml"))
            Dim pkgs = 0, files = 0
            For Each pkgNode As XmlNode In xDoc.SelectNodes("//Paths/Packages/Package")
                Dim pkg = pkgNode.InnerText.Remove(0, 2)
                If Not Path.GetFileName(pkg).StartsWith("vehicles_") Then Continue For
                Dim pkgPath = Path.Combine(wot, pkg)
                If Not File.Exists(pkgPath) Then Continue For
                Dim a = PkgEntry.ArchiveFor(pkgPath)
                For Each e In a.Entries
                    If e.Name = "" Then Continue For   ' folder entries
                    Dim key = e.FullName.Replace("\", "/").ToLowerInvariant()
                    If Not index.ContainsKey(key) Then
                        index(key) = New KeyValuePair(Of String, String)(pkgPath, e.FullName)
                        files += 1
                    End If
                Next
                pkgs += 1
            Next
            LogThis("tank files: {0} vehicle package(s), {1} entries indexed", pkgs, files)
        Catch ex As Exception
            LogThis("tank files: index failed - {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>A vehicle-package entry, else whatever the core finds, else Nothing.</summary>
    Public Function Lookup(path As String) As PkgEntry
        If Not indexed Then BuildIndex()
        Dim key = path.Replace("\", "/").ToLowerInvariant()
        Dim hit As KeyValuePair(Of String, String) = Nothing
        If index.TryGetValue(key, hit) Then Return PkgEntry.FromPackage(hit.Key, hit.Value)
        Return ResMgr.Lookup(path)
    End Function

    ''' <summary>Only the vehicle packages; Nothing without falling back to the core.</summary>
    Public Function LookupOwn(path As String) As PkgEntry
        If Not indexed Then BuildIndex()
        Dim key = path.Replace("\", "/").ToLowerInvariant()
        Dim hit As KeyValuePair(Of String, String) = Nothing
        If index.TryGetValue(key, hit) Then Return PkgEntry.FromPackage(hit.Key, hit.Value)
        Return Nothing
    End Function

    Public Function ReadAll(path As String) As Byte()
        Dim e = Lookup(path)
        If e Is Nothing Then Return Nothing
        Using ms As New MemoryStream()
            e.Extract(ms)
            Return ms.ToArray()
        End Using
    End Function

    ''' <summary>The DDS at path, HD variant preferred, through the core's own uploader (which caches by name).</summary>
    Public Function LoadTexture(path As String) As GLTexture
        If String.IsNullOrEmpty(path) Then Return Nothing
        Dim fn = path.Replace("\", "/")
        Dim hd = fn
        Dim dot = fn.LastIndexOf("."c)
        If dot > 0 Then hd = fn.Substring(0, dot) & "_hd" & fn.Substring(dot)
        For Each candidate In {hd, fn}
            ' The _hd probe stays inside this module's index: asking the core
            ' for a name that is not there makes it log a miss for every part.
            Dim e = If(candidate Is hd, LookupOwn(candidate), Lookup(candidate))
            If e Is Nothing Then Continue For
            Dim ms As New MemoryStream
            e.Extract(ms)
            Dim name = candidate
            Return TextureMgr.load_dds_image_from_stream(ms, name)
        Next
        LogThis("tank files: texture not found {0}", fn)
        Return Nothing
    End Function
End Module

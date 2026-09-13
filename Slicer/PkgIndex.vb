Imports System.IO
Imports System.IO.Compression
Imports System.Xml

''' <summary>
''' A read-only index of every World of Tanks package, built once at startup.
'''
''' Standalone on purpose - nuTerra's ResMgr does the same job but pulls in the
''' whole app to do it. Same rule as SrtViewer\PkgIndex.vb, and the same trap:
''' a ZipArchiveEntry is only valid while its ZipArchive is alive, so the
''' archives are held open for the life of the program rather than closed in a
''' Using block. The entries in `map` outlive the loop that made them.
'''
''' Unlike ResMgr this one scans EVERY package, vehicles and audio included.
''' The whole question the Slicer exists to answer is "where are the buildings",
''' and skipping a package because its name suggests it holds tanks is assuming
''' the answer. Run it once, measure, and then skip on evidence - which is what
''' --skip-vehicles is for, and why FoundOutsideBuildingRoot is reported.
''' </summary>
Public Class PkgIndex

    ''' <summary>One file inside one package.</summary>
    Public Structure Entry
        Public Path As String            ' lowered, forward slashed
        Public Original As String        ' exactly as the package spells it
        Public Pkg As String             ' package leaf name
        Public Zip As ZipArchiveEntry
    End Structure

    Private ReadOnly map As New Dictionary(Of String, Entry)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly zips As New List(Of ZipArchive)

    Public Property GamePath As String
    Public Property PackagesScanned As Integer
    Public Property PackagesSkipped As Integer
    Public Property EntriesSeen As Long
    Public Property DuplicatesIgnored As Integer
    Public Property ScanMilliseconds As Long

    Public ReadOnly Property Count As Integer
        Get
            Return map.Count
        End Get
    End Property

    ''' <summary>
    ''' What gets kept. Everything else in a package is passed over without
    ''' being recorded - the index is for finding models, not for cataloguing
    ''' 57,958 GUI bitmaps.
    ''' </summary>
    Private Shared ReadOnly KEEP As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".model", ".visual_processed", ".primitives_processed", ".havok",
        ".dds"}

    Public Shared Function TryOpen(gamePath As String, skipVehicles As Boolean) As PkgIndex
        If String.IsNullOrEmpty(gamePath) Then Return Nothing
        Dim pathsXml = Path.Combine(gamePath, "paths.xml")
        If Not File.Exists(pathsXml) Then Return Nothing

        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim idx As New PkgIndex With {.GamePath = gamePath}

        Dim doc As New XmlDocument
        doc.Load(pathsXml)

        For Each node As XmlNode In doc.SelectNodes("//Paths/Packages/Package")
            Dim rel = node.InnerText.Trim()
            If rel.StartsWith("./") Then rel = rel.Remove(0, 2)
            Dim leaf = Path.GetFileName(rel)

            If skipVehicles AndAlso (leaf.StartsWith("vehicles_", StringComparison.OrdinalIgnoreCase) OrElse
                                     leaf.StartsWith("audioww-", StringComparison.OrdinalIgnoreCase)) Then
                idx.PackagesSkipped += 1
                Continue For
            End If

            Dim full = Path.Combine(gamePath, rel)
            If Not File.Exists(full) Then
                idx.PackagesSkipped += 1
                Continue For
            End If

            Try
                Dim fs As New FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim z As New ZipArchive(fs, ZipArchiveMode.Read, leaveOpen:=False)
                idx.zips.Add(z)
                idx.PackagesScanned += 1

                For Each e In z.Entries
                    ' A directory entry is one whose name ends in "/" - there is
                    ' no IsDirectory on ZipArchiveEntry. The extension test would
                    ' reject them anyway; kept so the intent stays readable.
                    If e.FullName.EndsWith("/") Then Continue For
                    idx.EntriesSeen += 1
                    If Not KEEP.Contains(Path.GetExtension(e.FullName)) Then Continue For

                    Dim key = e.FullName.Replace("\"c, "/"c).ToLowerInvariant()
                    If idx.map.ContainsKey(key) Then
                        ' First package wins, the same rule ResMgr uses. The HD
                        ' packages restate paths the base ones already carry.
                        idx.DuplicatesIgnored += 1
                        Continue For
                    End If
                    idx.map.Add(key, New Entry With {
                        .Path = key, .Original = e.FullName, .Pkg = leaf, .Zip = e})
                Next
            Catch
                ' An unreadable package is skipped rather than fatal - one bad
                ' file should not stop the other two hundred being indexed.
                idx.PackagesSkipped += 1
            End Try
        Next

        sw.Stop()
        idx.ScanMilliseconds = sw.ElapsedMilliseconds
        Return idx
    End Function

    Public Function Lookup(name As String) As Entry?
        If name Is Nothing Then Return Nothing
        Dim key = name.Replace("\"c, "/"c).ToLowerInvariant()
        Dim e As Entry = Nothing
        If map.TryGetValue(key, e) Then Return e
        Return Nothing
    End Function

    Public Function Read(e As Entry) As Byte()
        If e.Zip Is Nothing Then Return Nothing
        Using src = e.Zip.Open()
            Using ms As New MemoryStream(CInt(Math.Max(e.Zip.Length, 0)))
                src.CopyTo(ms)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Public Function ReadPath(name As String) As Byte()
        Dim e = Lookup(name)
        If Not e.HasValue Then Return Nothing
        Return Read(e.Value)
    End Function

    ''' <summary>Every indexed entry whose path ends with this extension.</summary>
    Public Function AllWithExtension(ext As String) As List(Of Entry)
        Dim out As New List(Of Entry)
        For Each kv In map
            If kv.Key.EndsWith(ext, StringComparison.OrdinalIgnoreCase) Then out.Add(kv.Value)
        Next
        out.Sort(Function(a, b) String.CompareOrdinal(a.Path, b.Path))
        Return out
    End Function
End Class

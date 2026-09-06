Imports System.IO
Imports System.IO.Compression

''' <summary>
''' One readable blob: an entry inside a World of Tanks package, a loose file on
''' disk, or bytes already in hand. Replaces DotNetZip's ZipEntry.
'''
''' THE POINT OF THE INDIRECTION IS LIFETIME.
'''
''' A DotNetZip ZipEntry can reopen its own archive by path, so ResMgr indexed
''' every package inside a Using block and kept the entries after the ZipFile was
''' disposed. A System.IO.Compression ZipArchiveEntry is only valid while its
''' ZipArchive is alive, so porting that code one-for-one compiles cleanly and
''' then throws on the first texture lookup.
'''
''' So an entry here holds the package PATH and the entry NAME, not a live
''' handle, and the archives are opened once on first use and kept open for the
''' life of the process. Nothing downstream had to change: every call site
''' outside this class only ever used .Extract(stream).
''' </summary>
Public NotInheritable Class PkgEntry

    Private Enum Kind
        InPackage
        LooseFile
        Bytes
    End Enum

    Private ReadOnly _kind As Kind
    Private ReadOnly _pkgPath As String
    Private ReadOnly _entryName As String
    Private ReadOnly _bytes As Byte()

    ''' <summary>Path as the package names it, forward slashes, original case.</summary>
    Public ReadOnly Property FileName As String

    Private Sub New(k As Kind, pkg As String, name As String, raw As Byte())
        _kind = k
        _pkgPath = pkg
        _entryName = name
        _bytes = raw
        FileName = name
    End Sub

    Public Shared Function FromPackage(pkgPath As String, entryName As String) As PkgEntry
        Return New PkgEntry(Kind.InPackage, pkgPath, entryName, Nothing)
    End Function

    ''' <summary>
    ''' A loose file, used for res_mods overrides. This replaces a genuinely odd
    ''' piece of the old code: to hand back a ZipEntry for a file on disk, ResMgr
    ''' built a NEW zip in memory, added the file to it, saved it, re-read it and
    ''' returned entry zero. That was the only place anything WROTE a zip, and it
    ''' existed purely because ZipEntry was the currency. It is a file read now.
    ''' </summary>
    Public Shared Function FromFile(path As String, name As String) As PkgEntry
        Return New PkgEntry(Kind.LooseFile, path, name, Nothing)
    End Function

    ''' <summary>
    ''' Bytes already extracted - used for entries of a NESTED archive, where the
    ''' outer blob is itself a zip held in memory and the archive cannot be kept
    ''' open beyond the caller.
    ''' </summary>
    Public Shared Function FromBytes(raw As Byte(), name As String) As PkgEntry
        Return New PkgEntry(Kind.Bytes, Nothing, name, raw)
    End Function

    ''' <summary>
    ''' Write this entry's contents to dest. Same contract as DotNetZip's
    ''' Extract: the stream is written from its current position and is NOT
    ''' rewound, because callers set Position = 0 themselves afterwards.
    ''' </summary>
    Public Sub Extract(dest As Stream)
        Select Case _kind
            Case Kind.Bytes
                dest.Write(_bytes, 0, _bytes.Length)

            Case Kind.LooseFile
                Using fs As New FileStream(_pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    fs.CopyTo(dest)
                End Using

            Case Else
                Dim e = ArchiveFor(_pkgPath).GetEntry(_entryName)
                If e Is Nothing Then
                    Throw New FileNotFoundException(
                        String.Format("'{0}' is no longer in {1}", _entryName, _pkgPath))
                End If
                ' ZipArchive is not thread safe and the entry stream is a live
                ' view onto the shared archive, so the copy happens under the
                ' same lock that hands out the archive.
                SyncLock _archives
                    Using src = e.Open()
                        src.CopyTo(dest)
                    End Using
                End SyncLock
        End Select
    End Sub

    '--------------------------------------------------------------------------
    ' Open archives, kept for the life of the process.
    '
    ' The packages are large and a map load pulls thousands of entries, so
    ' reopening per extract is not an option. They are opened read-only with
    ' FileShare.Read so the game can be running at the same time.
    '--------------------------------------------------------------------------
    Private Shared ReadOnly _archives As New Dictionary(Of String, ZipArchive)(
        StringComparer.OrdinalIgnoreCase)

    Public Shared Function ArchiveFor(pkgPath As String) As ZipArchive
        SyncLock _archives
            Dim a As ZipArchive = Nothing
            If _archives.TryGetValue(pkgPath, a) Then Return a
            Dim fs As New FileStream(pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            a = New ZipArchive(fs, ZipArchiveMode.Read, leaveOpen:=False)
            _archives.Add(pkgPath, a)
            Return a
        End SyncLock
    End Function

    Public Shared Sub CloseAll()
        SyncLock _archives
            For Each a In _archives.Values
                Try
                    a.Dispose()
                Catch
                End Try
            Next
            _archives.Clear()
        End SyncLock
    End Sub
End Class

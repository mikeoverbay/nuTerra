Imports System.IO
Imports System.Xml
Imports System.IO.Compression

NotInheritable Class ResMgr
    Shared RES_MODS_PATH As String
    Shared ReadOnly FILENAME_TO_ZIP_ENTRY As New Dictionary(Of String, PkgEntry)
    ' .vfxbin is a particle effect definition, .effbin the wrapper naming its
    ' forward/deferred .vfx. Indexed so the particle loader can find them.
    Shared ReadOnly FILE_EXTENSIONS_TO_USE As New HashSet(Of String)({
            ".dds", ".model", ".primitives_processed",
            ".visual_processed", ".cdata_processed",
            ".bin", ".xml", ".png", ".settings", ".srt",
            ".texformat", ".atlas_processed",
            ".vfxbin", ".effbin"
            })

    Public Shared Sub Init(wot_path As String)
        Dim xDoc As New XmlDocument
        xDoc.Load(Path.Combine(wot_path, "paths.xml"))
        Dim first_path = xDoc.SelectSingleNode("//Paths/Path").InnerText.Remove(0, 2)
        RES_MODS_PATH = Path.Combine(wot_path, first_path)

        For Each pkgNode In xDoc.SelectNodes("//Paths/Packages/Package")
            Dim pkg = pkgNode.InnerText.Remove(0, 2)

            If Path.GetFileName(pkg).StartsWith("vehicles_") Then
                ' ignore vehicle packages
                Continue For
            End If

            If Path.GetFileName(pkg).StartsWith("audioww-") Then
                ' ignore audio packages
                Continue For
            End If

            Dim pkgPath = Path.Combine(wot_path, pkg)

            ' The archive is opened once here and DELIBERATELY left open - see
            ' PkgEntry. It used to be a Using block, which worked only because a
            ' DotNetZip ZipEntry could reopen its own file; a ZipArchiveEntry
            ' cannot, and the entries indexed here outlive this loop by the whole
            ' run of the program.
            '
            ' A directory entry in a zip is one whose name ends in "/" - there is
            ' no IsDirectory flag on ZipArchiveEntry. The extension filter below
            ' would reject them anyway; the test is kept so the intent stays
            ' readable.
            Dim archive = PkgEntry.ArchiveFor(pkgPath)
            For Each file In archive.Entries
                Dim raw_name = file.FullName
                If raw_name.EndsWith("/") Then
                    Continue For
                End If
                Dim lowered_fn = raw_name.ToLower
                If FILE_EXTENSIONS_TO_USE.Contains(Path.GetExtension(lowered_fn)) Then
                    If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
                        Continue For
                    End If
                    FILENAME_TO_ZIP_ENTRY.Add(lowered_fn, PkgEntry.FromPackage(pkgPath, raw_name))
                End If
            Next
        Next

    End Sub

    '''<summary>
    ''' Every space that actually has terrain data in the installed packages.
    ''' The game's arena list names maps that are not shipped and misses the
    ''' event and hangar spaces that are, so the menu is built from this instead.
    '''</summary>
    Public Shared Function SpaceNames() As List(Of String)
        Const PREFIX = "spaces/"
        Const SUFFIX = "/space.bin"

        Dim found As New List(Of String)
        For Each key In FILENAME_TO_ZIP_ENTRY.Keys
            If key.StartsWith(PREFIX) AndAlso key.EndsWith(SUFFIX) Then
                Dim name = key.Substring(PREFIX.Length, key.Length - PREFIX.Length - SUFFIX.Length)
                If Not name.Contains("/") Then found.Add(name)
            End If
        Next
        found.Sort()
        Return found
    End Function

    Public Shared Function Lookup(filename As String) As PkgEntry
        ' A res_mods override wins over the packaged file.
        '
        ' This used to build a NEW zip in memory, add the loose file to it, save
        ' it, re-read it and return entry zero - the only place anything in the
        ' program wrote a zip, and it existed purely because ZipEntry was the
        ' currency every caller expected. It is a file read now.
        Dim mod_path = Path.Combine(RES_MODS_PATH, filename)
        If File.Exists(mod_path) Then
            Return PkgEntry.FromFile(mod_path, filename)
        End If

        Dim lowered_fn = filename.ToLower.Replace("\", "/")
        If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
            Return FILENAME_TO_ZIP_ENTRY(lowered_fn)
        End If

        If Not FILE_EXTENSIONS_TO_USE.Contains(Path.GetExtension(lowered_fn)) Then
            Stop
        End If

        LogThis("file not found: {0}", filename)
        Return Nothing
    End Function

    ''' <summary>
    ''' Dictionary-only probe. Unlike Lookup this does not log or break on a
    ''' miss, so it is safe to use for speculative lookups.
    ''' </summary>
    Private Shared Function LookupQuiet(filename As String) As PkgEntry
        Dim lowered_fn = filename.ToLower.Replace("\", "/")
        If FILENAME_TO_ZIP_ENTRY.ContainsKey(lowered_fn) Then
            Return FILENAME_TO_ZIP_ENTRY(lowered_fn)
        End If
        Return Nothing
    End Function

    ''' <summary>
    ''' The one entry in a folder whose name ends with a given suffix, or Nothing.
    '''
    ''' Some assets are named with a content hash the caller cannot know - the SH
    ''' probe grid ships as &lt;hash&gt;_sh_grid.dds, and while 80 of the 64-odd
    ''' installed spaces use an all-zero hash, maps with several environments
    ''' (Murovanka, North America) give each one its own. The environment folder
    ''' holds exactly one of each kind, so match on the suffix and take it.
    ''' </summary>
    Public Shared Function LookupBySuffix(folder As String, suffix As String) As PkgEntry
        Dim f = folder.ToLower.Replace("\", "/")
        If Not f.EndsWith("/") Then f &= "/"
        Dim s = suffix.ToLower

        For Each kv In FILENAME_TO_ZIP_ENTRY
            If kv.Key.StartsWith(f) AndAlso kv.Key.EndsWith(s) Then
                ' Direct children only - no nested folder between the two.
                If Not kv.Key.Substring(f.Length).Contains("/") Then
                    Return kv.Value
                End If
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Looks up a texture, preferring the high resolution variant when the game
    ''' ships one. HD textures live in the *_hd.pkg packages under the same path
    ''' with an "_hd" suffix, at twice the base resolution. Terrain tiles under
    ''' maps/landscape have no HD variant - they are already 1024x1024 - so this
    ''' simply falls through to the base file for them.
    ''' </summary>
    Public Shared Function LookupHD(filename As String) As PkgEntry
        If filename.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) AndAlso
           Not filename.EndsWith("_hd.dds", StringComparison.OrdinalIgnoreCase) Then
            Dim hd = filename.Substring(0, filename.Length - 4) & "_hd.dds"
            Dim hd_entry = LookupQuiet(hd)
            If hd_entry IsNot Nothing Then
                Return hd_entry
            End If
        End If
        Return Lookup(filename)
    End Function

    Public Shared Function openXML(filepath As String) As XmlElement
        Dim entry = Lookup(filepath)
        Return openXML(entry)
    End Function

    Public Shared Function openXML(entry As PkgEntry) As XmlElement
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

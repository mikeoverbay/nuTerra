Imports System.IO
Imports System.Security.Cryptography

''' <summary>
''' Our edits to the game's own textures, applied as the file is read.
'''
''' The lamp glass cut lives in the RED channel of the street lamps' normal map,
''' which is Wargaming's art. Shipping a copy of it - however wrapped up - is
''' shipping their texture, and .gitignore says in as many words not to:
''' "extracted textures and models are redistributable game content". So the
''' pristine file is read from the player's own packages and OUR change is laid
''' over it here.
'''
''' assets/lamp_glass.ntpx holds only the DXT5 colour halves of the blocks the
''' cut touches - 658 of them across both resolutions, 8 KiB in total. With no
''' dimensions, no header and no picture it is not a texture and cannot be made
''' into one; it is a list of endpoint pairs that means nothing without the game
''' file it patches. Built by tools/make_lamp_patch.py, which carries the why.
'''
''' The payload is scrambled so the file is not something a texture viewer will
''' open. That is obfuscation and nothing more - the unwrap is in this source
''' tree - and it is not pretending otherwise.
'''
''' EVERY ENTRY IS FINGERPRINTED. A patch names the size and MD5 of the file it
''' was built against, and a mismatch is refused rather than applied. Wargaming
''' re-exporting the texture would leave the block offsets pointing at different
''' pixels, and a silent half-patch is far worse than no patch: it would corrupt
''' the normal map of every street lamp on the map and look like a shader bug.
''' </summary>
Public Module TexturePatch

    Private Structure Patch
        Public size As Integer
        Public md5 As Byte()
        Public offsets As Integer()
        Public payload As Byte()      ' 8 bytes per offset, packed end to end
    End Structure

    Private ReadOnly PATCHES As New Dictionary(Of String, Patch)
    ''' <summary>Source byte length -> the patches built against a file that
    ''' size. The cheap first test; see Apply.</summary>
    Private ReadOnly BY_SIZE As New Dictionary(Of Integer, List(Of String))
    Private loaded As Boolean = False
    Private applied As Integer = 0

    Private Const MAGIC As String = "NTPX"
    Private ReadOnly KEY As Byte() = Text.Encoding.UTF8.GetBytes("nuTerra lamp glass v1")

    ''' <summary>
    ''' Read every .ntpx in assets/. Called once; a missing folder is not an
    ''' error, it just means nothing is patched and the lamps keep their glass.
    ''' </summary>
    Public Sub Init()
        If loaded Then Return
        loaded = True

        Dim dir = Path.Combine(AppContext.BaseDirectory, "assets")
        If Not Directory.Exists(dir) Then
            LogThis("texture patch: no assets folder at {0} - nothing to apply", dir)
            Return
        End If

        For Each f In Directory.GetFiles(dir, "*.ntpx")
            Try
                read_one(f)
            Catch ex As Exception
                ' A bad patch file must never stop a map loading. Without it the
                ' texture is simply the one the game ships.
                LogThis("texture patch: {0} could not be read ({1}) - ignored",
                        Path.GetFileName(f), ex.Message)
            End Try
        Next
        LogThis("texture patch: {0} texture(s) have a patch waiting", PATCHES.Count)
    End Sub

    ' Named path, not file: VB is case insensitive, so a parameter called
    ' `file` IS System.IO.File and File.ReadAllBytes resolves against a String.
    Private Sub read_one(path_in As String)
        Dim raw = File.ReadAllBytes(path_in)
        If raw.Length < 20 Then Throw New InvalidDataException("too short")
        If Text.Encoding.ASCII.GetString(raw, 0, 4) <> MAGIC Then Throw New InvalidDataException("not a patch file")

        Dim version = BitConverter.ToInt32(raw, 4)
        Dim count = BitConverter.ToInt32(raw, 8)
        If version <> 1 Then Throw New InvalidDataException("version " & version.ToString())

        ' The salt is derived from the header, so the same bytes never encode
        ' two different files the same way.
        Dim salt(7) As Byte
        Array.Copy(raw, 12, salt, 0, 8)
        Dim body = descramble(raw, 20, raw.Length - 20, salt)

        Dim p = 0
        For i = 0 To count - 1
            Dim nlen = BitConverter.ToUInt16(body, p) : p += 2
            Dim name = Text.Encoding.UTF8.GetString(body, p, nlen).ToLowerInvariant().Replace("\", "/") : p += nlen
            Dim src_size = BitConverter.ToInt32(body, p) : p += 4
            Dim md5sum(15) As Byte
            Array.Copy(body, p, md5sum, 0, 16) : p += 16
            Dim nblocks = BitConverter.ToInt32(body, p) : p += 4

            Dim offs(nblocks - 1) As Integer
            Dim pay(nblocks * 8 - 1) As Byte
            For b = 0 To nblocks - 1
                offs(b) = BitConverter.ToInt32(body, p) : p += 4
                Array.Copy(body, p, pay, b * 8, 8) : p += 8
            Next

            PATCHES(name) = New Patch With {.size = src_size, .md5 = md5sum,
                                            .offsets = offs, .payload = pay}
            If Not BY_SIZE.ContainsKey(src_size) Then BY_SIZE(src_size) = New List(Of String)
            BY_SIZE(src_size).Add(name)
            LogThis("texture patch: {0} - {1} block(s) for a {2} byte source",
                    name, nblocks, src_size)
        Next
    End Sub

    Private Function descramble(raw As Byte(), at As Integer, len As Integer, salt As Byte()) As Byte()
        Dim seed(KEY.Length + salt.Length - 1) As Byte
        Array.Copy(KEY, 0, seed, 0, KEY.Length)
        Array.Copy(salt, 0, seed, KEY.Length, salt.Length)
        Dim k As Byte()
        Using sha = SHA256.Create()
            k = sha.ComputeHash(seed)
        End Using
        Dim out(len - 1) As Byte
        For i = 0 To len - 1
            out(i) = raw(at + i) Xor k(i Mod k.Length)
        Next
        Return out
    End Function

    ''' <summary>
    ''' Apply the patch for this texture, if there is one, to the bytes already
    ''' in the stream. True when something was changed.
    '''
    ''' MATCHED ON CONTENT, NOT ON NAME. ResMgr.LookupHD silently upgrades a
    ''' request: ask for <name>.dds and you can be handed <name>_hd.dds, so the
    ''' path the caller passes is not necessarily the file in the stream. A
    ''' res_mods override moves it again. Keying on the name got the SD patch
    ''' offered the HD file, and only the size check stopped it being written
    ''' into the wrong pixels.
    '''
    ''' Size first, because it is free and almost always says no. Only when some
    ''' patch was built against a file of exactly this length is an MD5 taken -
    ''' so the common case costs one dictionary probe, not a hash of every
    ''' texture on the map.
    ''' </summary>
    Public Function Apply(texture_path As String, ms As MemoryStream) As Boolean
        If PATCHES.Count = 0 Then Return False

        Dim n = CInt(ms.Length)
        Dim candidates As List(Of String) = Nothing
        If Not BY_SIZE.TryGetValue(n, candidates) Then Return False

        Dim buf = ms.GetBuffer()
        Dim have As Byte()
        Using hasher = MD5.Create()
            have = hasher.ComputeHash(buf, 0, n)
        End Using

        ' pkey, not key: KEY is the module's scramble seed and VB does not
        ' distinguish the two, so a loop variable called key is an assignment
        ' to a ReadOnly Byte().
        For Each pkey In candidates
            Dim pat = PATCHES(pkey)
            Dim same = True
            For i = 0 To 15
                If have(i) <> pat.md5(i) Then
                    same = False
                    Exit For
                End If
            Next
            If Not same Then Continue For

            For b = 0 To pat.offsets.Length - 1
                Array.Copy(pat.payload, b * 8, buf, pat.offsets(b), 8)
            Next
            ms.Position = 0
            applied += 1
            LogThis("texture patch: {0} block(s) applied to {1} ({2} bytes, requested as {3})",
                    pat.offsets.Length, pkey, n, If(texture_path, "?"))
            Return True
        Next

        ' Silent unless this really was one of our targets. 524416 bytes is an
        ' ordinary size - a 512x1024 DXT5 or a 1024x1024 DXT1 with no mips - so
        ' dozens of unrelated textures land in the same size bucket every load
        ' and a line each would bury the one that matters. When the NAME matches
        ' too, though, this is a patch that should have applied and did not:
        ' that is what a re-exported texture looks like, and the cut would
        ' otherwise just quietly not appear.
        If texture_path IsNot Nothing Then
            Dim want = IO.Path.GetFileName(texture_path.ToLowerInvariant())
            For Each pkey In candidates
                If IO.Path.GetFileName(pkey) = want Then
                    LogThis("texture patch: {0} is the right size but not the file the patch was built from - NOT applied", pkey)
                    Exit For
                End If
            Next
        End If
        Return False
    End Function

    '''<summary>How many textures have been patched this session.</summary>
    Public ReadOnly Property AppliedCount As Integer
        Get
            Return applied
        End Get
    End Property
End Module

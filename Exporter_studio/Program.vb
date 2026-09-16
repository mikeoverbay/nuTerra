Imports System.IO
Imports OpenTK.Mathematics

''' <summary>
''' Exporter_studio - finds the buildings in the World of Tanks packages.
'''
''' Standalone, like SrtViewer: it shares no code with nuTerra so it can be run
''' and proven in seconds without a map load, and so the format readers in here
''' stay honest reference implementations.
'''
'''     Exporter_studio                          scan and summarise
'''     Exporter_studio --list                   every building, one line each
'''     Exporter_studio --list --filter cathedral
'''     Exporter_studio --asset hd_bld_eu_225_cathedral    every LOD and part
'''     Exporter_studio --csv buildings.csv      one row per part
'''     Exporter_studio --failures               show anything that would not parse
'''     Exporter_studio --skip-vehicles          faster; skips vehicles_/audioww- packages
'''     Exporter_studio --game "C:\Games\World_of_Tanks_NA"
''' </summary>
Module Program

    Private Function GuessGamePath(explicitPath As String) As String
        If Not String.IsNullOrEmpty(explicitPath) Then Return explicitPath
        Dim guesses = {
            "C:\Games\World_of_Tanks_NA",
            "C:\Games\World_of_Tanks",
            "C:\Program Files\World_of_Tanks",
            "C:\Program Files (x86)\World_of_Tanks"}
        For Each g In guesses
            If File.Exists(Path.Combine(g, "paths.xml")) Then Return g
        Next
        Return Nothing
    End Function

    ' STA because the export button opens a Windows Save As dialog, and the
    ' common file dialogs are COM single-threaded-apartment. Without this the
    ' first click throws instead of opening anything. GLFW does not care
    ' which apartment it is in.
    <STAThread>
    Sub Main(args As String())
        Dim gameArg As String = Nothing
        Dim filter As String = Nothing
        Dim assetArg As String = Nothing
        Dim csvPath As String = Nothing
        Dim doList = False, doFailures = False, skipVehicles = False, doView = False
        Dim settingsPath As String = Nothing
        Dim shotPath As String = Nothing
        Dim checkCount As Integer = -1
        Dim doShell = False
        Dim shotAngle As String = Nothing
        Dim shotCut = False
        Dim uiInShot = False
        Dim findPattern As String = Nothing
        Dim debugView = 0
        Dim hidePattern As String = Nothing
        Dim exportNow As String = Nothing
        Dim glbCheck As String = Nothing
        Dim startSize As New Vector2i(0, 0)
        Dim bakeDir As String = Nothing
        Dim objPath As String = Nothing
        Dim bakePx As Integer = 2048
        Dim exportCount As Integer = -1
        Dim openPath As String = Nothing
        Dim matCount As Integer = -1
        Dim identPattern As String = Nothing
        Dim showSettings = False, saveSettings = False
        Dim setArgs As New List(Of String)

        Dim i = 0
        While i < args.Length
            Select Case args(i).ToLowerInvariant()
                Case "--game"
                    i += 1 : If i < args.Length Then gameArg = args(i)
                Case "--filter"
                    i += 1 : If i < args.Length Then filter = args(i).ToLowerInvariant()
                Case "--asset"
                    i += 1 : If i < args.Length Then assetArg = args(i).ToLowerInvariant()
                Case "--csv"
                    i += 1 : If i < args.Length Then csvPath = args(i)
                Case "--settings"
                    i += 1 : If i < args.Length Then settingsPath = args(i)
                Case "--set"
                    i += 1 : If i < args.Length Then setArgs.Add(args(i))
                Case "--show-settings"
                    showSettings = True
                Case "--save-settings"
                    saveSettings = True
                Case "--obj"
                    i += 1 : If i < args.Length Then objPath = args(i)
                Case "--bake"
                    i += 1 : If i < args.Length Then bakeDir = args(i)
                Case "--bake-size"
                    i += 1 : If i < args.Length Then Integer.TryParse(args(i), bakePx)
                Case "--shot-cut"
                    shotCut = True
                Case "--shot-angle"
                    i += 1 : If i < args.Length Then shotAngle = args(i)
                Case "--mat"
                    matCount = 0
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), matCount) Then i += 1 Else matCount = 0
                Case "--idents"
                    i += 1 : If i < args.Length Then identPattern = args(i)
                Case "--open"
                    i += 1 : If i < args.Length Then openPath = args(i)
                Case "--export"
                    exportCount = 0
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), exportCount) Then i += 1 Else exportCount = 0
                Case "--out"
                    i += 1 : If i < args.Length Then setArgs.Add("out.dir=" & args(i))
                Case "--shell"
                    doShell = True
                Case "--check"
                    checkCount = 0
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), checkCount) Then i += 1 Else checkCount = 0
                Case "--shot"
                    i += 1 : If i < args.Length Then shotPath = args(i)
                Case "--ui"
                    uiInShot = True
                Case "--find"
                    i += 1 : If i < args.Length Then findPattern = args(i)
                Case "--debug"
                    i += 1 : If i < args.Length Then Integer.TryParse(args(i), debugView)
                Case "--hide"
                    i += 1 : If i < args.Length Then hidePattern = args(i)
                Case "--export-now"
                    i += 1 : If i < args.Length Then exportNow = args(i)
                Case "--glb-check"
                    i += 1 : If i < args.Length Then glbCheck = args(i)
                Case "--size"
                    ' WxH, so a layout can be checked at sizes other than the
                    ' one it was written at. Anchoring bugs only show up small.
                    i += 1
                    If i < args.Length Then
                        Dim wh = args(i).ToLowerInvariant().Split("x"c)
                        Dim ww = 0, hh = 0
                        If wh.Length = 2 AndAlso Integer.TryParse(wh(0), ww) AndAlso Integer.TryParse(wh(1), hh) Then
                            startSize = New Vector2i(ww, hh)
                        End If
                    End If
                Case "--view"
                    doView = True
                Case "--list"
                    doList = True
                Case "--failures"
                    doFailures = True
                Case "--skip-vehicles"
                    skipVehicles = True
                Case "--help", "-h", "/?"
                    Usage() : Return
                Case Else
                    Console.WriteLine("unknown argument: {0}", args(i))
                    Usage() : Return
            End Select
            i += 1
        End While

        ' --glb-check reads a .glb back and reports what is in it. The round
        ' trip is the only check that catches a writer and a reader agreeing
        ' on something wrong, and it needs no game install - so it runs before
        ' anything else and exits.
        If glbCheck IsNot Nothing Then
            Try
                Dim gm = GlbFile.Read(glbCheck)
                Console.WriteLine("glb  {0}", IO.Path.GetFullPath(glbCheck))
                Console.WriteLine("  generator {0}", gm.Generator)
                Console.WriteLine("  {0}", gm.Describe())
                Dim maxIdx = 0
                For Each v In gm.Indices
                    If v > maxIdx Then maxIdx = v
                Next
                Console.WriteLine("  highest index {0:N0} against {1:N0} verts -> {2}",
                                  maxIdx, gm.Positions.Count,
                                  If(maxIdx < gm.Positions.Count, "in range", "OUT OF RANGE"))
                For Each g In gm.Groups.Take(4)
                    Console.WriteLine("    {0,-28} {1:N0} tris", g.Material, g.IndexCount \ 3)
                Next
            Catch ex As Exception
                Console.WriteLine("glb-check failed: {0}", ex.Message)
                Environment.ExitCode = 2
            End Try
            Return
        End If

        ' ---- settings -----------------------------------------------------
        ' Loaded before anything else so --show-settings costs nothing: the
        ' settings do not depend on the game install or on a scan.
        If settingsPath Is Nothing Then
            settingsPath = IO.Path.Combine(AppContext.BaseDirectory, SliceSettings.DefaultFileName)
            ' The app was renamed from Slicer, and a settings file written
            ' under the old name is still worth reading. Ignoring it would
            ' silently restore every default - which reads as the settings
            ' having stopped working rather than as a rename.
            If Not File.Exists(settingsPath) Then
                Dim legacy = IO.Path.Combine(AppContext.BaseDirectory, SliceSettings.LegacyFileName)
                If File.Exists(legacy) Then
                    Console.WriteLine("settings: reading {0} (the pre-rename name)", SliceSettings.LegacyFileName)
                    settingsPath = legacy
                End If
            End If
        End If
        Dim settings = SliceSettings.Load(settingsPath)

        For Each kv In setArgs
            Dim eq = kv.IndexOf("="c)
            If eq < 0 Then
                Console.WriteLine("--set wants key=value, got ""{0}""", kv)
                Environment.ExitCode = 2
                Return
            End If
            If Not settings.Apply(kv.Substring(0, eq), kv.Substring(eq + 1)) Then
                Console.WriteLine("--set: unknown key ""{0}"". --show-settings lists them all.", kv.Substring(0, eq))
                Environment.ExitCode = 2
                Return
            End If
        Next

        If saveSettings Then
            settings.Save(settingsPath)
            Console.WriteLine("wrote {0}", IO.Path.GetFullPath(settingsPath))
        End If

        If showSettings Then
            Console.WriteLine("settings  {0}{1}", IO.Path.GetFullPath(settingsPath),
                              If(File.Exists(settingsPath), "", "   (not on disk - showing defaults)"))
            Console.WriteLine()
            settings.Describe()
            Return
        End If

        ' A broken setting should stop the run before it reads 218 packages.
        Dim problems = settings.Validate()
        If problems.Count > 0 Then
            Console.WriteLine("settings  {0}", IO.Path.GetFullPath(settingsPath))
            Console.WriteLine()
            Console.WriteLine("UNUSABLE SETTINGS ({0}):", problems.Count)
            For Each prob In problems
                Console.WriteLine("  - {0}", prob)
            Next
            Environment.ExitCode = 2
            Return
        End If

        Dim gamePath = GuessGamePath(gameArg)
        If gamePath Is Nothing Then
            Console.WriteLine("No World of Tanks install found. Pass --game <path to the folder holding paths.xml>.")
            Environment.ExitCode = 2
            Return
        End If

        Console.WriteLine("game      {0}", gamePath)
        Console.WriteLine("indexing packages{0} ...", If(skipVehicles, " (skipping vehicles_/audioww-)", ""))
        Dim pkg = PkgIndex.TryOpen(gamePath, skipVehicles)
        If pkg Is Nothing Then
            Console.WriteLine("Could not read {0}", Path.Combine(gamePath, "paths.xml"))
            Environment.ExitCode = 2
            Return
        End If

        Console.WriteLine("packages  {0} scanned, {1} skipped", pkg.PackagesScanned, pkg.PackagesSkipped)
        Console.WriteLine("entries   {0:N0} seen, {1:N0} indexed, {2:N0} duplicate paths ignored",
                          pkg.EntriesSeen, pkg.Count, pkg.DuplicatesIgnored)
        Console.WriteLine("index in  {0:N0} ms", pkg.ScanMilliseconds)
        Console.WriteLine()

        ' --open skips the BUILDING SCAN and wraps the one model the caller
        ' named. The package index is still built - the file still has to be
        ' read out of a pkg - but the 4,893-model classification pass is not
        ' run, and neither is this app's opinion about what counts as a
        ' building. That is the launched-from-nuTerra path: its picker
        ' already knows the exact primitives path, so there is nothing to
        ' search for, and the model it hands over may well be a rock.
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim library As BuildingLibrary
        If openPath IsNot Nothing Then
            library = BuildingLibrary.ForSingleModel(ResolveOpenPath(pkg, openPath))
        Else
            library = BuildingLibrary.Scan(pkg)
        End If
        sw.Stop()

        Console.WriteLine("BUILDINGS")
        Console.WriteLine("  assets           {0:N0}", library.Assets.Count)
        Console.WriteLine("  mesh models      {0:N0}  ({1:N0} parsed, {2:N0} unparsable)",
                          library.ModelsFound, library.ModelsParsed, library.ModelsUnparsable)
        Console.WriteLine("  havok proxies    {0:N0}  (collision hulls, not geometry)", library.HavokFound)
        Console.WriteLine("  scanned in       {0:N0} ms", sw.ElapsedMilliseconds)

        ' The two null controls. Both are expected to be empty; saying so out
        ' loud every run is the point, because an empty result you never printed
        ' is indistinguishable from a check you forgot to make.
        Console.WriteLine()
        Console.WriteLine("  cross-check, folder against the game's own bld_ prefix:")
        Console.WriteLine("    named bld_ but outside content/buildings   {0}",
                          If(library.NamedBldOutsideRoot.Count = 0, "none", library.NamedBldOutsideRoot.Count.ToString()))
        For Each s In library.NamedBldOutsideRoot
            Console.WriteLine("      {0}", s)
        Next
        Console.WriteLine("    inside content/buildings but not bld_      {0}",
                          If(library.InRootNotNamedBld.Count = 0, "none", library.InRootNotNamedBld.Count.ToString()))
        For Each s In library.InRootNotNamedBld
            Console.WriteLine("      {0}", s)
        Next
        Console.WriteLine("    under content/buildings, shape not recognised   {0}",
                          If(library.OddShaped = 0, "none", library.OddShaped.ToString()))

        ' Shape of the library, so the numbers below have something to sit in.
        Dim lodHist As New SortedDictionary(Of Integer, Integer)
        Dim partHist As New SortedDictionary(Of Integer, Integer)
        Dim withBox = 0, withoutBox = 0, nodefull = 0
        For Each a In library.Assets.Values
            Dim n = a.Lods.Count
            lodHist(n) = lodHist.GetValueOrDefault(n) + 1
            Dim p = a.PartsAt(0).Count
            partHist(p) = partHist.GetValueOrDefault(p) + 1
            For Each pt In a.Parts
                If pt.HasBox Then withBox += 1 Else withoutBox += 1
                If pt.Visual IsNot Nothing AndAlso Not pt.Nodeless Then nodefull += 1
            Next
        Next
        Console.WriteLine()
        Console.WriteLine("  LODs per asset   {0}", Join(lodHist.Select(Function(kv) kv.Value & " assets have " & kv.Key).ToArray(), ", "))
        Console.WriteLine("  parts at lod0    {0} assets are a single mesh, {1} are multi-part (up to {2})",
                          partHist.GetValueOrDefault(1), library.Assets.Count - partHist.GetValueOrDefault(1),
                          If(partHist.Count = 0, 0, partHist.Keys.Max()))
        Console.WriteLine("  visibility box   {0:N0} models carry one, {1:N0} do not", withBox, withoutBox)
        Console.WriteLine("  node trees       {0:N0} models are nodefull (animated), the rest nodeless", nodefull)

        If doFailures Then
            Console.WriteLine()
            Console.WriteLine("FAILURES ({0})", library.Failures.Count)
            For Each fail In library.Failures
                Console.WriteLine("  {0}", fail)
            Next
        End If

        ' The per-part dump is for browsing, not for a job that is producing
        ' files - it buried the export report under ninety lines of parts.
        If assetArg IsNot Nothing AndAlso exportCount < 0 AndAlso checkCount < 0 Then
            DumpAsset(library, assetArg)
        ElseIf doList Then
            ListAssets(library, filter)
        Else
            Console.WriteLine()
            Console.WriteLine("  10 largest by lod0 footprint:")
            Dim rows = SizedRows(library)
            For Each r In rows.OrderByDescending(Function(t) t.Item2).Take(10)
                Console.WriteLine("    {0,8:F1} m wide {1,7:F1} m tall   {2}", r.Item2, r.Item3, r.Item1)
            Next
            Console.WriteLine()
            Console.WriteLine("  --list for all {0}, --asset <name> for one, --csv <file> to save.", library.Assets.Count)
        End If

        If csvPath IsNot Nothing Then WriteCsv(library, csvPath)

        If checkCount >= 0 Then MeshCheck.RunSweep(pkg, library, settings, checkCount, filter)

        If matCount >= 0 Then VisualFile.Report(pkg, library, If(assetArg, filter), matCount)
        If identPattern IsNot Nothing Then VisualFile.ReportIdentifiers(pkg, library, identPattern)

        If exportCount >= 0 Then
            MeshExport.ExportAssets(pkg, library, settings, If(assetArg, filter), exportCount)
        End If

        If doView OrElse shotPath IsNot Nothing OrElse doShell OrElse bakeDir IsNot Nothing OrElse objPath IsNot Nothing Then
            ' --asset picks the building to open on; without one it starts at
            ' the first and the arrow keys walk the library.
            Dim startAt = 0
            If assetArg IsNot Nothing Then
                Dim ordered = library.Assets.Values.ToList()
                For n = 0 To ordered.Count - 1
                    If ordered(n).Name.ToLowerInvariant().Contains(assetArg) Then
                        startAt = n
                        Exit For
                    End If
                Next
            End If
            Console.WriteLine()
            Console.WriteLine("viewer: drag orbit, wheel zoom, left/right building, [ ] LOD,")
            Console.WriteLine("        up/down solo a part, W wireframe, R reload, Esc quit")
            Console.WriteLine("browser: click the box and type to search - * is a wildcard and there may")
            Console.WriteLine("        be several (*eu*house*). Double-click a row to load THAT one model.")
            Console.WriteLine("        / focuses the box, Enter loads, Tab hides the panel.")
            Console.WriteLine()
            Using win As New ViewerWindow(pkg, library, startAt, settings, shotPath, doShell, shotAngle, shotCut, bakeDir, bakePx, objPath, uiInShot, findPattern, debugView, hidePattern, exportNow, startSize)
                win.Run()
            End Using
        End If
    End Sub


    ''' <summary>
    ''' Turn whatever --open was handed into a path this index can find, and say
    ''' out loud when it cannot.
    '''
    ''' --open is the seam with nuTerra: its "Open in Exporter Studio" button
    ''' passes the path ModelInfo captured from space.bin, and that value does
    ''' NOT arrive in one shape. Measured against the index, the bare
    ''' package-relative form works and every decorated form failed silently:
    '''
    '''     content/.../x.model                     found
    '''     /content/.../x.model                    NOT found
    '''     res/content/.../x.model                 NOT found
    '''     ./content/.../x.model                   NOT found
    '''     C:/Games/.../packages/content/.../x.model   NOT found
    '''
    ''' All four failures produced the same message the genuine-absence case
    ''' produces - "no geometry could be read" - which is why this took a report
    ''' from the other side rather than being caught here. A receiver that
    ''' cannot find a file should say what it looked for.
    '''
    ''' So: try the exact key, then fall back to a segment-aligned SUFFIX match
    ''' over the index, which finds the entry whatever prefix it arrived with.
    ''' The extension is normalised too - a bare `.primitives` is the space.bin
    ''' spelling and `.primitives_processed` is the file that exists. nuTerra
    ''' corrects that on its side and is right to, because passing the real
    ''' filename beats a spelling the receiver has to forgive; this handles it
    ''' anyway, because a button that half works is worse than one that does not.
    ''' </summary>
    Private Function ResolveOpenPath(pkg As PkgIndex, given As String) As String
        If pkg Is Nothing OrElse String.IsNullOrWhiteSpace(given) Then Return given

        Dim p = given.Replace("\"c, "/"c).Trim().ToLowerInvariant()

        ' Cut to the package root. The index is keyed from `content/`, and a
        ' caller may hand over `/content/...`, `res/content/...`, `./content/...`
        ' or a full `C:/Games/.../packages/content/...` - all four of which an
        ' exact lookup rejects as firmly as a file that does not exist.
        Dim at = p.IndexOf("/content/", StringComparison.Ordinal)
        If at >= 0 Then
            p = p.Substring(at + 1)
        ElseIf p.StartsWith("content/", StringComparison.Ordinal) Then
            ' already rooted
        End If

        If p.EndsWith("/vertices", StringComparison.Ordinal) Then p = p.Substring(0, p.Length - 9)
        If p.EndsWith(".primitives", StringComparison.Ordinal) Then p &= "_processed"

        ' The stem, with whichever of the three sibling extensions it carried
        ' taken off. No extension at all is fine - a stem is what we want.
        Dim stem = p
        For Each ext In {".primitives_processed", ".visual_processed", ".model"}
            If stem.EndsWith(ext, StringComparison.Ordinal) Then
                stem = stem.Substring(0, stem.Length - ext.Length)
                Exit For
            End If
        Next

        ' The file that has to exist for anything to draw.
        Dim prim = stem & ".primitives_processed"
        If pkg.Lookup(prim).HasValue Then Return stem

        Dim n = 0
        Dim hit = pkg.LookupBySuffix(prim, n)
        If hit.HasValue Then
            Dim resolved = hit.Value.Path
            resolved = resolved.Substring(0, resolved.Length - ".primitives_processed".Length)
            Console.WriteLine("--open: resolved {0}", resolved)
            Console.WriteLine("        from     {0}", given)
            Return resolved
        End If

        Console.WriteLine("--open: NOTHING MATCHES {0}", given)
        Console.WriteLine("        looked for      {0}", prim)
        If n > 1 Then
            Console.WriteLine("        {0} entries end with that path - too ambiguous to pick one", n)
        Else
            Console.WriteLine("        and no indexed entry ends with it either.")
            ' The leaf on its own, as a last hint: it is usually a wrong FOLDER
            ' rather than a wrong name, and saying where the name does live
            ' turns a dead end into an obvious fix.
            Dim segs = prim.Split("/"c)
            Dim leaf = segs(segs.Length - 1)
            Dim near = 0
            Dim shown = 0
            For Each e In pkg.AllWithExtension(".primitives_processed")
                If Not e.Path.EndsWith("/" & leaf, StringComparison.Ordinal) Then Continue For
                near += 1
                If shown < 3 Then
                    Console.WriteLine("        but that name exists at {0}", e.Path)
                    shown += 1
                End If
            Next
            If near = 0 Then Console.WriteLine("        the file name itself is nowhere in the packages.")
        End If
        Return stem
    End Function

    ''' <summary>(asset name, footprint, height) for every asset whose lod0 has a box.</summary>
    Private Function SizedRows(library As BuildingLibrary) As List(Of Tuple(Of String, Single, Single))
        Dim out As New List(Of Tuple(Of String, Single, Single))
        For Each a In library.Assets.Values
            Dim lo, hi As Vector3
            Dim lod = If(a.Lods.Count > 0, a.Lods(0), 0)
            If a.Box(lod, lo, hi) Then
                Dim s = hi - lo
                out.Add(Tuple.Create(a.Name, Math.Max(s.X, s.Z), s.Y))
            End If
        Next
        Return out
    End Function

    Private Sub ListAssets(library As BuildingLibrary, filter As String)
        Console.WriteLine()
        Console.WriteLine("{0,-46} {1,4} {2,5}  {3}", "asset", "LODs", "parts", "lod0 size (m)      package")
        Dim shown = 0
        For Each a In library.Assets.Values
            If filter IsNot Nothing AndAlso Not a.Name.ToLowerInvariant().Contains(filter) Then Continue For
            Dim lod = If(a.Lods.Count > 0, a.Lods(0), 0)
            Dim lo, hi As Vector3
            Dim size = "        -        "
            If a.Box(lod, lo, hi) Then
                Dim s = hi - lo
                size = String.Format("{0,5:F1}x{1,5:F1}x{2,5:F1}", s.X, s.Y, s.Z)
            End If
            Console.WriteLine("{0,-46} {1,4} {2,5}  {3}  {4}",
                              a.Name, a.Lods.Count, a.PartsAt(lod).Count, size, String.Join(",", a.Pkgs))
            shown += 1
        Next
        Console.WriteLine()
        Console.WriteLine("{0} shown{1}", shown, If(filter Is Nothing, "", " matching """ & filter & """"))
    End Sub

    Private Sub DumpAsset(library As BuildingLibrary, name As String)
        Dim hits = library.Assets.Values.Where(Function(a) a.Name.ToLowerInvariant().Contains(name)).ToList()
        If hits.Count = 0 Then
            Console.WriteLine()
            Console.WriteLine("no building matching ""{0}""", name)
            Return
        End If
        For Each a In hits
            Console.WriteLine()
            Console.WriteLine("{0}", a.Name)
            Console.WriteLine("  root      {0}/{1}", a.Root, a.State)
            Console.WriteLine("  packages  {0}", String.Join(", ", a.Pkgs))
            For Each lod In a.Lods
                Dim lo, hi As Vector3
                Dim boxTxt = "no box"
                If a.Box(lod, lo, hi) Then
                    Dim s = hi - lo
                    boxTxt = String.Format("{0:F2} x {1:F2} x {2:F2} m", s.X, s.Y, s.Z)
                End If
                Console.WriteLine("  lod{0}  {1,3} part(s)   {2}", lod, a.PartsAt(lod).Count, boxTxt)
                For Each p In a.PartsAt(lod)
                    Dim ps = "no box"
                    If p.HasBox Then
                        Dim s = p.Size
                        ps = String.Format("{0,6:F2} x {1,6:F2} x {2,6:F2}", s.X, s.Y, s.Z)
                    End If
                    Console.WriteLine("        {0,-52} {1}  {2}", p.Name, ps, If(p.Nodeless, "nodeless", "nodefull"))
                Next
            Next
        Next
    End Sub

    Private Sub WriteCsv(library As BuildingLibrary, csvFile As String)
        Using w As New StreamWriter(csvFile, False, Text.Encoding.UTF8)
            w.WriteLine("asset,lod,part,pkg,has_box,min_x,min_y,min_z,max_x,max_y,max_z,size_x,size_y,size_z,nodeless,visual,path")
            For Each a In library.Assets.Values
                For Each p In a.Parts.OrderBy(Function(x) x.Lod).ThenBy(Function(x) x.Name)
                    Dim s = p.Size
                    w.WriteLine(String.Join(",", {
                        Csv(a.Name), p.Lod.ToString(), Csv(p.Name), Csv(p.Pkg),
                        If(p.HasBox, "1", "0"),
                        F(p.BoxMin.X), F(p.BoxMin.Y), F(p.BoxMin.Z),
                        F(p.BoxMax.X), F(p.BoxMax.Y), F(p.BoxMax.Z),
                        F(s.X), F(s.Y), F(s.Z),
                        If(p.Nodeless, "1", "0"), Csv(p.Visual), Csv(p.Path)}))
                Next
            Next
        End Using
        Console.WriteLine()
        Console.WriteLine("wrote {0}", IO.Path.GetFullPath(csvFile))
    End Sub

    Private Function F(v As Single) As String
        Return v.ToString("0.####", Globalization.CultureInfo.InvariantCulture)
    End Function

    Private Function Csv(s As String) As String
        If s Is Nothing Then Return ""
        If s.IndexOfAny(New Char() {","c, """"c, vbCr, vbLf}) >= 0 Then
            Return """" & s.Replace("""", """""") & """"
        End If
        Return s
    End Function

    Private Sub Usage()
        Console.WriteLine("Exporter_studio - finds the buildings in the World of Tanks packages")
        Console.WriteLine()
        Console.WriteLine("  --view               open the 3D viewer")
        Console.WriteLine("  --shot <file.png>    render one frame of the bottom fill and exit")
        Console.WriteLine("  --check [n]          watertightness before and after the bottom fill")
        Console.WriteLine("  --open <pkg path>    work on one model by its package path, no scan")
        Console.WriteLine("  --mat [n]            dump materials, shaders and textures")
        Console.WriteLine("  --export [n]         write buildings as STL/OBJ (all, or the first n)")
        Console.WriteLine("  --out <dir>          where to write them")
        Console.WriteLine("  --shell              rebuild the set model into an exterior shell")
        Console.WriteLine("  --shot-angle <a>     iso | front | bottom  (default bottom)")
        Console.WriteLine("  --shot-cut           keep the cut on in the shot")
        Console.WriteLine("  --bake <dir>         bake the maps into UV2 space as PNG + MTL")
        Console.WriteLine("  --bake-size <px>     bake resolution, default 2048")
        Console.WriteLine("  --obj <file.obj>     load an exported OBJ back and look at it")
        Console.WriteLine("  --find <pattern>     open on the first matching model; * wildcards, any number")
        Console.WriteLine("  --ui                 keep the browser panel in a --shot")
        Console.WriteLine("  --hide <pattern>     switch off parts whose name or .model matches; * wildcards")
        Console.WriteLine("  --idents <pattern>   every material identifier matching, with counts and kinds")
        Console.WriteLine("  --export-now <vis|all>  press the viewer''s export button once on load")
        Console.WriteLine("  --show-settings      print the slice settings and exit")
        Console.WriteLine("  --save-settings      write exporter_studio.settings (a commented template)")
        Console.WriteLine("  --set key=value      override one setting for this run")
        Console.WriteLine("  --settings <file>    use a settings file other than the default")
        Console.WriteLine("  --list               one line per building")
        Console.WriteLine("  --filter <text>      only buildings whose name contains <text>")
        Console.WriteLine("  --asset <name>       every LOD and part of one building")
        Console.WriteLine("  --csv <file>         one row per part")
        Console.WriteLine("  --failures           list anything that would not parse")
        Console.WriteLine("  --skip-vehicles      skip vehicles_/audioww- packages (faster)")
        Console.WriteLine("  --game <path>        the folder holding paths.xml")
    End Sub
End Module

Imports System.IO
Imports OpenTK.Mathematics

''' <summary>
''' Building Slicer - finds the buildings in the World of Tanks packages.
'''
''' Standalone, like SrtViewer: it shares no code with nuTerra so it can be run
''' and proven in seconds without a map load, and so the format readers in here
''' stay honest reference implementations.
'''
'''     Slicer                          scan and summarise
'''     Slicer --list                   every building, one line each
'''     Slicer --list --filter cathedral
'''     Slicer --asset hd_bld_eu_225_cathedral    every LOD and part
'''     Slicer --csv buildings.csv      one row per part
'''     Slicer --failures               show anything that would not parse
'''     Slicer --skip-vehicles          faster; skips vehicles_/audioww- packages
'''     Slicer --game "C:\Games\World_of_Tanks_NA"
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

    Sub Main(args As String())
        Dim gameArg As String = Nothing
        Dim filter As String = Nothing
        Dim assetArg As String = Nothing
        Dim csvPath As String = Nothing
        Dim doList = False, doFailures = False, skipVehicles = False, doView = False

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

        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim library = BuildingLibrary.Scan(pkg)
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

        If assetArg IsNot Nothing Then
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

        If doView Then
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
            Console.WriteLine()
            Using win As New ViewerWindow(pkg, library, startAt)
                win.Run()
            End Using
        End If
    End Sub

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
        Console.WriteLine("Building Slicer - finds the buildings in the World of Tanks packages")
        Console.WriteLine()
        Console.WriteLine("  --view               open the 3D viewer")
        Console.WriteLine("  --list               one line per building")
        Console.WriteLine("  --filter <text>      only buildings whose name contains <text>")
        Console.WriteLine("  --asset <name>       every LOD and part of one building")
        Console.WriteLine("  --csv <file>         one row per part")
        Console.WriteLine("  --failures           list anything that would not parse")
        Console.WriteLine("  --skip-vehicles      skip vehicles_/audioww- packages (faster)")
        Console.WriteLine("  --game <path>        the folder holding paths.xml")
    End Sub
End Module

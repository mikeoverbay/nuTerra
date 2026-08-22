Imports System.IO
Imports System.Linq

''' <summary>
''' Standalone viewer for SpeedTree .srt files, so tree loading can be worked on
''' without starting nuTerra and loading a whole map.
'''
'''   SrtViewer.exe                          browse every .srt in the game packages
'''   SrtViewer.exe some\tree.srt            open one file from disk
'''   SrtViewer.exe vegetation/... .srt      open one file from the packages
'''   SrtViewer.exe --game "C:\Games\..."    point at a different install
'''   SrtViewer.exe --filter maple           only files whose path contains "maple"
'''   SrtViewer.exe --report                 decode everything, print stats, no window
'''
''' Controls: drag orbit, wheel zoom, arrows change file, up/down solo a draw call,
''' W wireframe, T cycle textured / flat / UV, A toggle alpha test, R reload.
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
        Dim explicitGame As String = Nothing
        Dim filter As String = Nothing
        Dim reportOnly = False
        Dim direct As New List(Of String)

        Dim i = 0
        While i < args.Length
            Select Case args(i).ToLower
                Case "--game"
                    i += 1
                    If i < args.Length Then explicitGame = args(i)
                Case "--filter"
                    i += 1
                    If i < args.Length Then filter = args(i).ToLower
                Case "--report"
                    reportOnly = True
                Case Else
                    direct.Add(args(i))
            End Select
            i += 1
        End While

        Dim gamePath = GuessGamePath(explicitGame)
        Dim pkg As PkgIndex = Nothing
        If gamePath IsNot Nothing Then
            Console.WriteLine("indexing packages under {0} ...", gamePath)
            pkg = PkgIndex.TryOpen(gamePath)
            If pkg IsNot Nothing Then
                Console.WriteLine("  {0} .srt/.dds entries indexed", pkg.Count)
            End If
        Else
            Console.WriteLine("no World of Tanks install found; textures and package browsing are off")
        End If

        Dim files As New List(Of String)
        If direct.Count > 0 Then
            files.AddRange(direct)
        ElseIf pkg IsNot Nothing Then
            files.AddRange(pkg.AllSrt())
        End If

        If filter IsNot Nothing Then
            files = files.FindAll(Function(f) f.ToLower.Contains(filter))
        End If

        If files.Count = 0 Then
            Console.WriteLine("nothing to show. Pass a .srt path, or point --game at an install.")
            Return
        End If
        Console.WriteLine("{0} file(s)", files.Count)

        If reportOnly Then
            Report(files, pkg)
            OrderingReport(files, pkg)
            Return
        End If

        Using w As New ViewerWindow(files, pkg, 0)
            w.Run()
        End Using
    End Sub

    ''' <summary>Batch decode: how much of the library the reader actually handles.</summary>
    Private Sub Report(files As List(Of String), pkg As PkgIndex)
        Dim solved = 0, unsolved = 0, broken = 0
        Dim tris As Long = 0
        Dim worst As New List(Of String)

        For Each f In files
            Dim s As SrtFile = Nothing
            Try
                If pkg IsNot Nothing AndAlso Not File.Exists(f) Then
                    s = SrtFile.FromBytes(pkg.Read(pkg.Lookup(f)), f)
                Else
                    s = SrtFile.Load(f)
                End If
            Catch ex As Exception
                broken += 1
                Continue For
            End Try

            If s Is Nothing OrElse Not s.Solved Then
                unsolved += 1
                If worst.Count < 25 Then worst.Add(f)
                Continue For
            End If

            solved += 1
            tris += s.TotalTriangles
        Next

        Console.WriteLine()
        Console.WriteLine("=== SRT decode report ===")
        Console.WriteLine("  files      : {0}", files.Count)
        Console.WriteLine("  solved     : {0}  ({1:F0}%)", solved, 100.0 * solved / Math.Max(1, files.Count))
        Console.WriteLine("  unsolved   : {0}", unsolved)
        Console.WriteLine("  unreadable : {0}", broken)
        Console.WriteLine("  triangles  : {0:N0}", tris)
        If worst.Count > 0 Then
            Console.WriteLine()
            Console.WriteLine("  first unsolved:")
            For Each w In worst
                Console.WriteLine("    {0}", w)
            Next
        End If
    End Sub
    '''<summary>
    ''' Are draw calls in the order the file's own type ids say they are, and do
    ''' the LODs of one asset line up with each other?
    '''
    ''' Three things are checked. Ids that do not increase through a LOD would
    ''' mean the geometry blocks are stored in a different order from the table.
    ''' A pair key used twice inside one LOD would mean the pairing is wrong. And
    ''' a pair whose two LODs classify differently means one of them is being
    ''' drawn as the wrong thing - the same trunk cannot be bark in LOD0 and a
    ''' leaf card in LOD1.
    '''</summary>
    Private Sub OrderingReport(files As List(Of String), pkg As PkgIndex)
        Dim checked = 0, unordered = 0, dupKey = 0, kindSplit = 0, unpaired = 0, declared = 0
        Dim examples As New List(Of String)

        For Each f In files
            Dim s = TryLoad(f, pkg)
            If s Is Nothing OrElse Not s.Solved Then Continue For
            checked += 1
            If s.DrawCalls.Count > 0 AndAlso s.DrawCalls(0).Declared Then declared += 1

            ' ids must climb through each LOD
            Dim bad = False
            For lod = 0 To s.LodCount - 1
                Dim seq = s.DrawCalls.Where(Function(d) d.Lod = lod AndAlso d.TypeId >= 0).
                                      Select(Function(d) d.TypeId).ToList()
                For i = 1 To seq.Count - 1
                    If seq(i) <= seq(i - 1) Then bad = True
                Next
            Next
            If bad Then
                unordered += 1
                If examples.Count < 20 Then examples.Add("  out of order   " & f)
                Continue For
            End If

            ' a pair key must not appear twice within one LOD
            Dim clash = False
            For lod = 0 To s.LodCount - 1
                ' -1 means unpaired, and several parts may legitimately be unpaired
                Dim keys = s.DrawCalls.Where(Function(d) d.Lod = lod AndAlso d.PairKey >= 0).
                                       Select(Function(d) d.PairKey).ToList()
                If keys.Distinct().Count() <> keys.Count Then clash = True
            Next
            If clash Then
                dupKey += 1
                If examples.Count < 20 Then examples.Add("  key clash     " & f)
                Continue For
            End If

            unpaired += s.DrawCalls.Where(Function(d) d.PairKey < 0).Count()

            ' the two LODs of one pair must agree on what they are
            Dim split = False
            For Each g In s.DrawCalls.Where(Function(d) d.PairKey >= 0).GroupBy(Function(d) d.PairKey)
                If g.Select(Function(d) d.Kind).Distinct().Count() > 1 Then split = True
            Next
            If split Then
                kindSplit += 1
                If examples.Count < 20 Then examples.Add("  kind mismatch " & f)
            End If
        Next

        Console.WriteLine()
        Console.WriteLine("=== ordering ===")
        Console.WriteLine("  kinds from file    : {0}", declared)
        Console.WriteLine("  kinds guessed      : {0}", checked - declared)
        Console.WriteLine("  checked            : {0}", checked)
        Console.WriteLine("  type ids not rising: {0}", unordered)
        Console.WriteLine("  pair key reused    : {0}", dupKey)
        Console.WriteLine("  unpaired parts     : {0}", unpaired)
        Console.WriteLine("  LODs disagree      : {0}", kindSplit)
        If examples.Count > 0 Then
            Console.WriteLine()
            For Each e In examples
                Console.WriteLine(e)
            Next
        End If
    End Sub

    Private Function TryLoad(f As String, pkg As PkgIndex) As SrtFile
        Try
            If pkg IsNot Nothing AndAlso Not File.Exists(f) Then
                Return SrtFile.FromBytes(pkg.Read(pkg.Lookup(f)), f)
            End If
            Return SrtFile.Load(f)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function
End Module

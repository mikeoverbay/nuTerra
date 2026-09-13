Imports System.IO
Imports System.Globalization
Imports OpenTK.Mathematics

''' <summary>
''' What the slicer needs to know before it cuts anything.
'''
''' Every default in here was chosen against a measurement of the shipped
''' building library rather than picked as a round number, and the two that
''' matter most are forced by the same finding: THESE MESHES ARE NOT SOLIDS.
''' Measured over 261 lod0 meshes:
'''
'''     watertight (no boundary, no non-manifold edges)   12 of 261   4.6%
'''     edges after welding    manifold 83.14%  boundary 15.21%  non-manifold 1.65%
'''     edges on raw indices   manifold 48.81%  boundary 51.17%  non-manifold 0.02%
'''     vertices               2,940,023 raw -> 1,611,718 welded  (45.2% duplicates)
'''
''' So a cut through one of these buildings usually meets an OPEN boundary, not
''' a closed loop - which is why `CapOpenSpans` exists as its own decision and
''' why `WeldTolerance` is not allowed to be zero. See the README.
'''
''' The file format is deliberately plain `key = value` text with `#` comments:
''' no JSON dependency, and the owner can edit it in Notepad without the app
''' running. Unknown keys are kept and written back rather than dropped, so a
''' newer build's settings survive a round trip through an older one.
''' </summary>
Public Class SliceSettings

    ' ---- plane -----------------------------------------------------------
    ''' <summary>x, y, z or custom. Y is the default because a building sliced
    ''' horizontally gives floors, which is the cut anyone asks for first.</summary>
    Public Property Axis As String = "y"
    ''' <summary>Only consulted when Axis = custom. Normalised on load.</summary>
    Public Property Normal As Vector3 = New Vector3(0, 1, 0)
    ''' <summary>Metres along the normal from the origin point.</summary>
    Public Property Offset As Single = 0.0F
    ''' <summary>"auto" puts the first plane through the centre of the model's
    ''' bounding box. Anything else is parsed as x,y,z in metres.</summary>
    Public Property Origin As String = "auto"

    ' ---- how many --------------------------------------------------------
    ''' <summary>single = one cut. stack = repeated cuts along the normal.</summary>
    Public Property Mode As String = "single"
    ''' <summary>Metres between planes in stack mode. 2.0 m is roughly a storey,
    ''' and the shipped buildings run 8 m to 206 m tall, so it gives a handful
    ''' of slices on a house and a lot on the dam.</summary>
    Public Property Spacing As Single = 2.0F
    ''' <summary>0 = as many as fit the bounding box.</summary>
    Public Property Count As Integer = 0

    ' ---- what comes back -------------------------------------------------
    ''' <summary>below, above or both. geometry3Sharp's MeshPlaneCut deletes the
    ''' POSITIVE side in place, so "both" costs two cuts on two copies with
    ''' opposite normals - it is not free, which is why it is not the default.</summary>
    Public Property Keep As String = "below"
    ''' <summary>Fill the closed loops the cut leaves behind.</summary>
    Public Property Cap As Boolean = True
    ''' <summary>
    ''' Try to close an OPEN span before capping it.
    '''
    ''' Off by default, and this is the setting to understand before trusting a
    ''' result: only 4.6% of these meshes are watertight, so most cuts produce
    ''' open spans rather than closed loops, and closing one means inventing
    ''' geometry that was never authored. Leaving it off gives an honest open
    ''' edge; turning it on gives a solid-looking result that is partly a guess.
    ''' </summary>
    Public Property CapOpenSpans As Boolean = False

    ' ---- robustness ------------------------------------------------------
    ''' <summary>
    ''' Metres. Vertices closer than this are treated as one before cutting.
    '''
    ''' NOT OPTIONAL, and zero is rejected. 45.2% of the vertices in these
    ''' meshes are duplicates sitting at the same position, split apart for UV
    ''' seams and hard normals. Cut without welding and the topology reads
    ''' 51.17% boundary edges instead of 15.21% - every seam looks like a hole,
    ''' and the cut loops come back shredded into fragments. 1e-5 m is 10
    ''' microns, far below any real feature on a building and far above float32
    ''' noise at these coordinates.
    ''' </summary>
    Public Property WeldTolerance As Single = 0.00001F
    ''' <summary>Discard zero-area triangles before cutting. They are the usual
    ''' source of a degenerate cut loop.</summary>
    Public Property DropDegenerate As Boolean = True

    ' ---- scope -----------------------------------------------------------
    Public Property Lod As Integer = 0
    ''' <summary>"all", or a substring matched against the part name.</summary>
    Public Property Parts As String = "all"
    ''' <summary>The 69 havok/*.hkt.model files are collision proxies, not
    ''' render geometry. They are likely CLOSER to solid than the render meshes,
    ''' so they may one day be the better thing to cut - but nothing reads their
    ''' geometry yet, so this does nothing until that reader exists.</summary>
    Public Property IncludeHavok As Boolean = False

    ' ---- output ----------------------------------------------------------
    Public Property OutDir As String = "slices"
    Public Property OutFormat As String = "obj"

    ''' <summary>Keys read from a file that this build does not know about.
    ''' Kept so writing the file back does not silently drop a newer build's
    ''' settings.</summary>
    Private ReadOnly unknown As New List(Of String)

    Public Shared ReadOnly DefaultFileName As String = "slicer.settings"

    ''' <summary>The plane normal this configuration actually means.</summary>
    Public Function EffectiveNormal() As Vector3
        Select Case Axis.Trim().ToLowerInvariant()
            Case "x" : Return New Vector3(1, 0, 0)
            Case "y" : Return New Vector3(0, 1, 0)
            Case "z" : Return New Vector3(0, 0, 1)
            Case Else
                Dim n = Normal
                If n.LengthSquared < 0.000001F Then Return New Vector3(0, 1, 0)
                n.Normalize()
                Return n
        End Select
    End Function

    ''' <summary>Parsed Origin, or Nothing when it is "auto".</summary>
    Public Function ExplicitOrigin() As Vector3?
        If Origin Is Nothing OrElse Origin.Trim().ToLowerInvariant() = "auto" Then Return Nothing
        Dim v = ParseVec(Origin)
        Return v
    End Function

    ''' <summary>
    ''' Complain about anything that cannot work. Returns the problems; an empty
    ''' list means the settings are usable.
    ''' </summary>
    Public Function Validate() As List(Of String)
        Dim bad As New List(Of String)
        Dim ax = Axis.Trim().ToLowerInvariant()
        If ax <> "x" AndAlso ax <> "y" AndAlso ax <> "z" AndAlso ax <> "custom" Then
            bad.Add("plane.axis must be x, y, z or custom (got """ & Axis & """)")
        End If
        If ax = "custom" AndAlso Normal.LengthSquared < 0.000001F Then
            bad.Add("plane.normal is zero length, so plane.axis = custom has no direction")
        End If
        Dim md = Mode.Trim().ToLowerInvariant()
        If md <> "single" AndAlso md <> "stack" Then
            bad.Add("slice.mode must be single or stack (got """ & Mode & """)")
        End If
        If md = "stack" AndAlso Spacing <= 0.0F Then
            bad.Add("slice.spacing must be greater than 0 in stack mode (got " & Spacing & ")")
        End If
        If Count < 0 Then bad.Add("slice.count cannot be negative")
        Dim kp = Keep.Trim().ToLowerInvariant()
        If kp <> "below" AndAlso kp <> "above" AndAlso kp <> "both" Then
            bad.Add("result.keep must be below, above or both (got """ & Keep & """)")
        End If
        ' The one that is not a matter of taste - see the WeldTolerance remarks.
        If WeldTolerance <= 0.0F Then
            bad.Add("mesh.weldTolerance must be greater than 0. " &
                    "45.2% of vertices in these meshes are duplicates at the same position; " &
                    "cutting without welding turns every UV seam into a hole.")
        End If
        If Lod < 0 Then bad.Add("scope.lod cannot be negative")
        If CapOpenSpans AndAlso Not Cap Then
            bad.Add("result.capOpenSpans is on but result.cap is off, so nothing will be capped")
        End If
        Return bad
    End Function

    ' ---------------------------------------------------------------- io --
    Public Shared Function Load(path As String) As SliceSettings
        Dim s As New SliceSettings
        If Not File.Exists(path) Then Return s
        For Each raw In File.ReadAllLines(path)
            Dim line = raw.Trim()
            If line.Length = 0 OrElse line.StartsWith("#") Then Continue For
            Dim eq = line.IndexOf("="c)
            If eq < 0 Then Continue For
            Dim k = line.Substring(0, eq).Trim().ToLowerInvariant()
            Dim v = line.Substring(eq + 1).Trim()
            ' strip a trailing comment
            Dim hash = v.IndexOf("#"c)
            If hash >= 0 Then v = v.Substring(0, hash).Trim()
            If Not s.Apply(k, v) Then s.unknown.Add(line)
        Next
        Return s
    End Function

    ''' <summary>Set one key. False when the key is not one of ours.</summary>
    Public Function Apply(key As String, value As String) As Boolean
        Select Case key.Trim().ToLowerInvariant()
            Case "plane.axis" : Axis = value
            Case "plane.normal" : Normal = If(ParseVec(value), Normal)
            Case "plane.offset" : Offset = ParseF(value, Offset)
            Case "plane.origin" : Origin = value
            Case "slice.mode" : Mode = value
            Case "slice.spacing" : Spacing = ParseF(value, Spacing)
            Case "slice.count" : Count = ParseI(value, Count)
            Case "result.keep" : Keep = value
            Case "result.cap" : Cap = ParseB(value, Cap)
            Case "result.capopenspans" : CapOpenSpans = ParseB(value, CapOpenSpans)
            Case "mesh.weldtolerance" : WeldTolerance = ParseF(value, WeldTolerance)
            Case "mesh.dropdegenerate" : DropDegenerate = ParseB(value, DropDegenerate)
            Case "scope.lod" : Lod = ParseI(value, Lod)
            Case "scope.parts" : Parts = value
            Case "scope.includehavok" : IncludeHavok = ParseB(value, IncludeHavok)
            Case "out.dir" : OutDir = value
            Case "out.format" : OutFormat = value
            Case Else : Return False
        End Select
        Return True
    End Function

    Public Sub Save(path As String)
        Dim w As New Text.StringBuilder
        w.AppendLine("# Slicer settings. Plain key = value, # starts a comment.")
        w.AppendLine("# Defaults are measured against the shipped building library - see Slicer/README.md.")
        w.AppendLine()
        w.AppendLine("# --- the plane ---------------------------------------------------")
        w.AppendLine("plane.axis            = " & Axis & "            # x | y | z | custom")
        w.AppendLine("plane.normal          = " & FmtVec(Normal) & "        # only when axis = custom")
        w.AppendLine("plane.offset          = " & Fmt(Offset) & "            # metres along the normal")
        w.AppendLine("plane.origin          = " & Origin & "         # auto = bounding-box centre, or x,y,z")
        w.AppendLine()
        w.AppendLine("# --- how many ----------------------------------------------------")
        w.AppendLine("slice.mode            = " & Mode & "       # single | stack")
        w.AppendLine("slice.spacing         = " & Fmt(Spacing) & "            # metres between planes in stack mode")
        w.AppendLine("slice.count           = " & Count & "              # 0 = as many as fit the bounds")
        w.AppendLine()
        w.AppendLine("# --- what comes back ---------------------------------------------")
        w.AppendLine("result.keep           = " & Keep & "        # below | above | both")
        w.AppendLine("result.cap            = " & LCase(Cap.ToString()) & "           # fill closed cut loops")
        w.AppendLine("# Only 4.6% of these meshes are watertight, so most cuts leave an OPEN")
        w.AppendLine("# span rather than a closed loop. Closing one invents geometry that was")
        w.AppendLine("# never authored - off gives an honest open edge.")
        w.AppendLine("result.capOpenSpans   = " & LCase(CapOpenSpans.ToString()) & "          # close open spans before capping")
        w.AppendLine()
        w.AppendLine("# --- robustness --------------------------------------------------")
        w.AppendLine("# NOT optional. 45.2% of vertices are duplicates at the same position,")
        w.AppendLine("# split for UV seams. Unwelded, topology reads 51% boundary edges")
        w.AppendLine("# instead of 15% and every cut loop comes back shredded.")
        w.AppendLine("mesh.weldTolerance    = " & Fmt(WeldTolerance) & "      # metres, must be > 0")
        w.AppendLine("mesh.dropDegenerate   = " & LCase(DropDegenerate.ToString()) & "           # drop zero-area triangles first")
        w.AppendLine()
        w.AppendLine("# --- scope -------------------------------------------------------")
        w.AppendLine("scope.lod             = " & Lod)
        w.AppendLine("scope.parts           = " & Parts & "            # all, or a substring of the part name")
        w.AppendLine("scope.includeHavok    = " & LCase(IncludeHavok.ToString()) & "          # collision proxies; no reader for them yet")
        w.AppendLine()
        w.AppendLine("# --- output ------------------------------------------------------")
        w.AppendLine("out.dir               = " & OutDir)
        w.AppendLine("out.format            = " & OutFormat)
        If unknown.Count > 0 Then
            w.AppendLine()
            w.AppendLine("# Keys this build does not recognise, preserved verbatim:")
            For Each u In unknown
                w.AppendLine(u)
            Next
        End If
        File.WriteAllText(path, w.ToString())
    End Sub

    Public Sub Describe()
        Console.WriteLine("SLICE SETTINGS")
        Console.WriteLine("  plane         {0}  normal {1}  offset {2} m  origin {3}",
                          Axis, FmtVec(EffectiveNormal()), Fmt(Offset), Origin)
        Console.WriteLine("  slices        {0}{1}", Mode,
                          If(Mode.Trim().ToLowerInvariant() = "stack",
                             String.Format("  every {0} m, {1}", Fmt(Spacing),
                                           If(Count = 0, "as many as fit", Count & " of them")), ""))
        Console.WriteLine("  keep          {0}   cap {1}{2}", Keep, LCase(Cap.ToString()),
                          If(CapOpenSpans, " (open spans closed - invents geometry)", ""))
        Console.WriteLine("  weld          {0} m   drop degenerate {1}", Fmt(WeldTolerance), LCase(DropDegenerate.ToString()))
        Console.WriteLine("  scope         lod{0}  parts {1}{2}", Lod, Parts,
                          If(IncludeHavok, "  + havok proxies", ""))
        Console.WriteLine("  output        {0}  as {1}", OutDir, OutFormat)
        Dim bad = Validate()
        If bad.Count > 0 Then
            Console.WriteLine()
            Console.WriteLine("  PROBLEMS ({0}):", bad.Count)
            For Each b In bad
                Console.WriteLine("    - {0}", b)
            Next
        End If
    End Sub

    ' ------------------------------------------------------------ parsing --
    Private Shared Function ParseF(s As String, fallback As Single) As Single
        Dim v As Single
        If Single.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return fallback
    End Function

    Private Shared Function ParseI(s As String, fallback As Integer) As Integer
        Dim v As Integer
        If Integer.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then Return v
        Return fallback
    End Function

    Private Shared Function ParseB(s As String, fallback As Boolean) As Boolean
        Select Case s.Trim().ToLowerInvariant()
            Case "true", "yes", "on", "1" : Return True
            Case "false", "no", "off", "0" : Return False
        End Select
        Return fallback
    End Function

    Private Shared Function ParseVec(s As String) As Vector3?
        If s Is Nothing Then Return Nothing
        Dim bits = s.Split(New Char() {","c, " "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)
        If bits.Length < 3 Then Return Nothing
        Dim x, y, z As Single
        If Single.TryParse(bits(0), NumberStyles.Float, CultureInfo.InvariantCulture, x) AndAlso
           Single.TryParse(bits(1), NumberStyles.Float, CultureInfo.InvariantCulture, y) AndAlso
           Single.TryParse(bits(2), NumberStyles.Float, CultureInfo.InvariantCulture, z) Then
            Return New Vector3(x, y, z)
        End If
        Return Nothing
    End Function

    Private Shared Function Fmt(v As Single) As String
        Return v.ToString("0.#####", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function FmtVec(v As Vector3) As String
        Return Fmt(v.X) & "," & Fmt(v.Y) & "," & Fmt(v.Z)
    End Function
End Class

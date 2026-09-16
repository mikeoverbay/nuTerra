''' <summary>
''' Entry point and command line.
'''
''' THE SAME ARGUMENTS AS nuTerra, on the owner's instruction - "all the same
''' cmd line args". About fifteen of nuTerra's forty-odd drive subsystems this
''' app does not have (lamp fog, the glow, the flight recorder, the bake), and
''' those are PARSED AND IGNORED rather than rejected, so an existing script or
''' a habit does not fail here. Ignoring them SILENTLY would be the trap: a run
''' with `nolampshadow` would look like it did something. So they are counted
''' and named once at startup.
'''
''' Added 2026-09-15 by nuTerra work, stage 0 of docs\brain_testing_plan.md.
''' </summary>
Module Program

    ''' <summary>
    ''' nuTerra arguments with no subsystem here, matched WHOLE.
    '''
    ''' Whole-word and prefix lists kept apart on purpose. A `Contains` test
    ''' over one merged list is the substring bug this project has paid for
    ''' four times in a day - see DIRECTIVES rule on structured names. An
    ''' argument is a structured token; it gets a structured test.
    ''' </summary>
    Private ReadOnly INERT_WHOLE As String() = {
        "kinddump", "rebake", "navdump", "treedump", "treetrace",
        "freezefx", "noglow", "gridfx", "blackfx", "fly", "record",
        "placer=1", "uv2audit", "nolampfog", "lampdebug", "nolampshadow",
        "snap", "snapquit"}

    ''' <summary>The same, matched as `name=` prefixes.</summary>
    Private ReadOnly INERT_PREFIX As String() = {
        "set=", "gridfxoffset=", "out=", "still=", "modelao=", "lightgain=",
        "scanlights=", "findmodel=", "foggain=", "fogfall=", "fogdens=",
        "fogphase=", "fogsteps=", "lampbias=", "lampsoft=", "lampnbias=",
        "falloff=", "settle="}

    Sub Main(args As String())
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)

        Dim ignored As New List(Of String)

        For Each a In args
            If a.StartsWith("cam=", StringComparison.OrdinalIgnoreCase) Then
                ' r,ax,ay,lx,ly,lz - six or it is not a camera, and a partial
                ' parse would silently place the view somewhere nobody asked
                ' for. All six or none.
                Dim parts = a.Substring(4).Split(","c)
                If parts.Length = 6 Then
                    Dim v(5) As Single
                    Dim ok = True
                    For i = 0 To 5
                        If Not Single.TryParse(parts(i), Globalization.NumberStyles.Float,
                                               Globalization.CultureInfo.InvariantCulture, v(i)) Then ok = False
                    Next
                    If ok Then STARTUP_CAM = v
                End If

            ElseIf a.StartsWith("owner=", StringComparison.OrdinalIgnoreCase) Then
                OWNER_TAG = a.Substring(6)

            ElseIf a.Equals("tanks", StringComparison.OrdinalIgnoreCase) Then
                TANK_AUTOLOAD = True

            ElseIf a.StartsWith("solo=", StringComparison.OrdinalIgnoreCase) Then
                TANK_SOLO_TAG = a.Substring(5)

            ElseIf a.StartsWith("perteam=", StringComparison.OrdinalIgnoreCase) Then
                Dim per_team As Integer
                If Integer.TryParse(a.Substring(8), per_team) AndAlso per_team > 0 Then
                    TANK_PER_TEAM = per_team
                End If

            ElseIf a.Equals("sim", StringComparison.OrdinalIgnoreCase) Then
                ' START THE SIM FROM THE COMMAND LINE. The owner's ask,
                ' 2026-09-15: "It should be able to start the sim with a arg."
                ' `sim` is Tank AI work's word for it - their button in nuTerra
                ' says SIM - so it is the word here too rather than a second
                ' name for one thing.
                BRAIN_ON = True

            ElseIf a.StartsWith("brain=", StringComparison.OrdinalIgnoreCase) OrElse
                   a.StartsWith("sim=", StringComparison.OrdinalIgnoreCase) OrElse
                   a.StartsWith("ai=", StringComparison.OrdinalIgnoreCase) Then
                ' `ai=` is kept as a synonym because that is what every
                ' existing note and script says. `brain=` is the name this app
                ' uses for the thing, and the owner's word for it.
                Dim eq = a.IndexOf("="c)
                Dim on_off As Integer
                If Integer.TryParse(a.Substring(eq + 1), on_off) Then
                    BRAIN_ON = (on_off <> 0)
                End If

            ElseIf a.Equals("half", StringComparison.OrdinalIgnoreCase) Then
                HALF_SIZE_WINDOW = True

            ElseIf a.Equals("fullscreen", StringComparison.OrdinalIgnoreCase) Then
                FULLSCREEN_WINDOW = True

            ElseIf a.Equals("clean", StringComparison.OrdinalIgnoreCase) Then
                CLEAN_VIEW = True

            ElseIf a.Equals("hullbox", StringComparison.OrdinalIgnoreCase) Then
                HULL_BOX_TABLE = True

            ElseIf a.Equals("verbose", StringComparison.OrdinalIgnoreCase) Then
                LOG_VERBOSE = True

            ElseIf is_inert(a) Then
                ignored.Add(a)

            ElseIf STARTUP_MAP Is Nothing Then
                ' Last, so a bare word is only read as a map name once every
                ' known form has had its chance. Otherwise a mistyped switch
                ' becomes "the map" and the failure names the wrong thing.
                STARTUP_MAP = a

            Else
                ignored.Add(a)
            End If
        Next

        Console.WriteLine("{0}{1}", APP_NAME,
                          If(OWNER_TAG = "", "", "  [" & OWNER_TAG & "]"))
        If ignored.Count > 0 Then
            Console.WriteLine(
                "ignored {0} argument(s) - no subsystem here for them: {1}",
                ignored.Count, String.Join(" ", ignored))
        End If

        Using w As New BrainWindow()
            w.Run()
        End Using
    End Sub

    ''' <summary>Is this one of nuTerra's arguments that does nothing here?
    ''' Whole-word for the flags, prefix for the `name=value` forms - never a
    ''' bare substring search.</summary>
    Private Function is_inert(a As String) As Boolean
        For Each w In INERT_WHOLE
            If a.Equals(w, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        For Each p In INERT_PREFIX
            If a.StartsWith(p, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

End Module

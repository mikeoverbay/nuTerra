Imports System.IO
Imports System.Text
Imports OpenTK.Mathematics

''' <summary>
''' The black box, with Tank AI work's columns.
'''
''' THE SAME HEADER AS TankLog, character for character, and to the same
''' folder. That is the whole point: identical spawns plus identical columns
''' is the only way two different brains are ever comparable, and a harness
''' whose log needed its own reader would have thrown that away on day one.
''' Their idea; this is Brain Testing's writer for it.
'''
''' WHY A SECOND WRITER AND NOT THEIR FILE. TankLog.Note takes a TankInstance
''' and reads inst.drive - the old brain's state, which is an empty stub here
''' on purpose. Linking it would compile and then write nothing. So the format
''' is shared and the source of the numbers is not, which is the honest split.
''' If the header ever changes over there it must change here; that is written
''' on both sides.
'''
''' DRIFTED ONCE ALREADY, on 2026-09-16: Tank AI work appended blk_ahead,
''' blk_rear and stuck_s in 90b44018 and told me, which is the only reason
''' this still matches. Their reason for the three is worth keeping - the ray
''' columns record the MEASUREMENT and the driver steered on something else,
''' and the file could not show the two disagreeing; that gap cost a morning.
''' Append-only, so an old reader still parses.
'''
''' THIS HEADER IS A COPY OF SOMEONE ELSE'S CONTRACT and copies drift. If
''' TankLog's columns move again this one must move with them, or two runs
''' stop being comparable in exactly the way a shared header exists to
''' prevent. Noted in both files.
'''
''' Two kinds of row, at different rates, exactly as they reasoned it:
'''   EVENT  - the brain changed what it is doing. Written that frame.
'''   SAMPLE - a heartbeat, so travel and heading have a trace between events.
''' Thirty hulls at sixty frames would be 1,800 rows a second logged blindly.
'''
''' Added 2026-09-16 by nuTerra work.
''' </summary>
Module BrainLog

    ''' <summary>Where other sessions can reach it - TankLog's own folder, not
    ''' %TEMP%. The owner could not get at a path file when it lived there, and
    ''' a log nobody can open is not a log.</summary>
    Public Const LOG_DIR As String = "C:\nuTerra_shared\tank_logs"

    ''' <summary>Heartbeat rows a second, per hull, between events. TankLog's
    ''' rate, so the two files have the same density.</summary>
    Private Const SAMPLE_HZ As Double = 5.0

    Public Enabled As Boolean = True

    Private writer As StreamWriter = Nothing
    Private path_ As String = ""
    Private ReadOnly lastWhy As New Dictionary(Of Integer, String)
    Private ReadOnly lastAt As New Dictionary(Of Integer, Double)
    Private ReadOnly travelled As New Dictionary(Of Integer, Single)
    Private ReadOnly lastPos As New Dictionary(Of Integer, Vector2)

    Public Sub StartRun(mapName As String, brainName As String)
        Close()
        If Not Enabled Then Return
        Try
            Directory.CreateDirectory(LOG_DIR)
            ' The BRAIN'S NAME is in the filename, which TankLog's is not - it
            ' only ever had one. Comparing two brains means telling their files
            ' apart without opening them.
            path_ = Path.Combine(LOG_DIR,
                String.Format("{0}_{1:yyyyMMdd_HHmmss}_brain_{2}.csv",
                              mapName, DateTime.Now, safe(brainName)))
            writer = New StreamWriter(path_, False, Encoding.UTF8)
            ' TankLog's header, verbatim. The ray columns are written empty
            ' until a brain casts rays - a column that exists and is blank says
            ' "not measured", where a missing column says "different format".
            writer.WriteLine("t_s,row,tank,team,x,z,heading_deg,speed_ms," &
                             "travelled_m,why,start_id,wp_at,wp_of," &
                             "d_fl,d_fr,d_rl,d_rr,d_front,d_rear,d_right,d_left," &
                             "s_fl,s_fr,s_rl,s_rr,s_front,s_rear,s_right,s_left," &
                             "blk_ahead,blk_rear,stuck_s")
            writer.Flush()
            LogThis("brain: black box writing {0}", path_)
        Catch ex As Exception
            writer = Nothing
            LogThis("brain: could not open the black box at {0} - {1}", path_, ex.Message)
        End Try
    End Sub

    Public Sub Close()
        Try
            If writer IsNot Nothing Then
                writer.Flush()
                writer.Dispose()
                LogThis("brain: black box closed {0}", path_)
            End If
        Catch
        End Try
        writer = Nothing
        lastWhy.Clear()
        lastAt.Clear()
        travelled.Clear()
        lastPos.Clear()
    End Sub

    ''' <summary>
    ''' Offer a frame. Writes a row per hull only on a change or a heartbeat.
    '''
    ''' The decision lives HERE and not at the call site, so there is one rule
    ''' and a caller cannot get it subtly wrong - TankLog's reasoning, and it
    ''' holds for the same reason.
    ''' </summary>
    Public Sub Note(inp As BrainInput, outp As BrainOutput, t_s As Double)
        If writer Is Nothing OrElse inp.hulls Is Nothing Then Return
        Try
            For i = 0 To inp.hulls.Length - 1
                Dim hull = inp.hulls(i)

                ' Travel accumulates every frame whether or not a row is
                ' written - it is a total, and sampling it would under-report
                ' exactly the quiet driving the heartbeat exists to catch.
                Dim prev As Vector2
                If lastPos.TryGetValue(hull.id, prev) Then
                    Dim d = (hull.pos - prev).Length
                    travelled(hull.id) = If(travelled.ContainsKey(hull.id), travelled(hull.id), 0.0F) + d
                Else
                    travelled(hull.id) = 0.0F
                End If
                lastPos(hull.id) = hull.pos

                Dim why = If(outp.why IsNot Nothing AndAlso i < outp.why.Length,
                             If(outp.why(i), ""), "")
                Dim changed = Not (lastWhy.ContainsKey(hull.id) AndAlso lastWhy(hull.id) = why)
                Dim due = (Not lastAt.ContainsKey(hull.id)) OrElse
                          (t_s - lastAt(hull.id)) >= (1.0 / SAMPLE_HZ)
                If Not changed AndAlso Not due Then Continue For

                lastWhy(hull.id) = why
                lastAt(hull.id) = t_s

                Dim tgt = If(outp.target IsNot Nothing AndAlso i < outp.target.Length,
                             outp.target(i), New Vector2(0.0F, 0.0F))
                Dim row = If(outp.row IsNot Nothing AndAlso i < outp.row.Length, outp.row(i), 0)

                writer.WriteLine(String.Format(
                    Globalization.CultureInfo.InvariantCulture,
                    "{0:0.000},{1},{2},{3},{4:0.00},{5:0.00},{6:0.0},{7:0.00}," &
                    "{8:0.0},{9},{10},{11},{12}," &
                    ",,,,,,,," & ",,,,,,,," & ",,",
                    t_s, If(changed, "EVENT", "SAMPLE"), hull.id, hull.team,
                    hull.pos.X, hull.pos.Y,
                    MathHelper.RadiansToDegrees(hull.headingRad), hull.speed,
                    travelled(hull.id), csv(why), hull.id, row, 0))
            Next
            writer.Flush()
        Catch ex As Exception
            LogThis("brain: black box write failed - {0}", ex.Message)
            Close()
        End Try
    End Sub

    ''' <summary>A comma in a reason would shift every column after it.</summary>
    Private Function csv(s As String) As String
        If s Is Nothing Then Return ""
        Return s.Replace(","c, ";"c).Replace(ChrW(10), " ").Replace(ChrW(13), " ")
    End Function

    Private Function safe(s As String) As String
        If String.IsNullOrEmpty(s) Then Return "unnamed"
        For Each c In IO.Path.GetInvalidFileNameChars()
            s = s.Replace(c, "_"c)
        Next
        Return s
    End Function

End Module

Imports System.IO
Imports System.Text
Imports OpenTK.Mathematics

''' <summary>
''' A black box for every hull: what each ray said, and what the tank did about
''' it.
'''
''' THE OWNER'S ASK: "you need to write a log for each tank and look at how it
''' reacted to each ray impact... travel distance and heading and ray hits will
''' tell us lots." And: put it in the shared folder so other sessions can help
''' digest it.
'''
''' WHY THIS AND NOT MORE LogThis LINES. Everything we have learned about the
''' driving this week came from reading the app's log after the fact, and every
''' time the answer was in a number the log did not print - the hit distance it
''' computed and discarded, the run length it never said, the waypoint index it
''' reached. Prose lines are written for the question somebody had that day.
''' This writes the whole row every time, so the question can be asked later.
'''
''' CSV, DELIBERATELY. Another session, or a person with a spreadsheet, can
''' answer "which tanks stopped with a clear front ray" without being told how
''' the format works. A bespoke binary would be smaller and nobody would read
''' it.
'''
''' WHAT IT WRITES, AND WHEN. Two kinds of row, because the two questions have
''' different sampling rates:
'''
'''   EVENT  - any ray changes state, or the drive changes what it is doing.
'''            This is "how it reacted to each ray impact", and it is the row
'''            that matters. Written the frame it happens.
'''   SAMPLE - a heartbeat at SAMPLE_HZ, so travel, heading and speed have a
'''            continuous trace between events. Without it a tank that drove
'''            quietly for ten seconds leaves no evidence it moved.
'''
''' Thirty hulls at sixty frames would be 1,800 rows a second if it logged
''' blindly; state changes plus a 5 Hz heartbeat is roughly 150, and the file
''' stays readable.
'''
''' Added 2026-09-15 by Tank AI work.
''' </summary>
Public Module TankLog

    ''' <summary>Write the black box. Off by default: a diagnostic that is
    ''' always on is a diagnostic nobody reads, and this one costs disk.</summary>
    Public TANK_LOG As Boolean = True

    ''' <summary>Heartbeat rows a second, per hull, between events.</summary>
    Private Const SAMPLE_HZ As Double = 5.0

    ''' <summary>Where other sessions can reach it. Not %TEMP% - the owner
    ''' could not get at the path file when it lived there, and a log nobody
    ''' can open is not a log.</summary>
    Public Const LOG_DIR As String = "C:\nuTerra_shared\tank_logs"

    Private writer As StreamWriter = Nothing
    Private path_ As String = ""
    Private started As DateTime = DateTime.MinValue

    ' Per hull, what we last wrote - so a row is only written when something
    ' changed, rather than sixty times a second saying the same thing.
    Private ReadOnly lastStates As New Dictionary(Of TankInstance, String)
    Private ReadOnly lastWhy As New Dictionary(Of TankInstance, Integer)
    Private ReadOnly lastAt As New Dictionary(Of TankInstance, Double)
    Private ReadOnly lastDist As New Dictionary(Of TankInstance, Single)

    Public ReadOnly Property FilePath As String
        Get
            Return path_
        End Get
    End Property

    ''' <summary>
    ''' Start a file for this run. One per SIM press, named for when it started,
    ''' so two runs are never mixed in one file - comparing two runs is the
    ''' whole point and it cannot be done inside a single stream.
    ''' </summary>
    Public Sub StartRun(mapName As String)
        Close()
        If Not TANK_LOG Then Return
        Try
            Directory.CreateDirectory(LOG_DIR)
            started = DateTime.Now
            path_ = Path.Combine(LOG_DIR, String.Format("{0}_{1:yyyyMMdd_HHmmss}_tanks.csv",
                                                        mapName, started))
            writer = New StreamWriter(path_, False, Encoding.UTF8)
            ' A header, because the next reader is not necessarily us.
            ' blk_ahead / blk_rear / stuck_s ADDED 2026-09-16, and they are
            ' the three columns that would have saved a morning.
            '
            ' The ray columns record the MEASUREMENT, through RayState and the
            ' STOP_M table. The drive does not steer on that: it steers on
            ' BlockedAhead and RearBlocked, which were asking a different
            ' question at a different range. So the log drew eight clear rays
            ' while the hull sat still, and the file contained no way to tell
            ' that the code disagreed with it. Recording what the drive was
            ' actually told closes that gap - if the decision and the
            ' measurement ever diverge again it is one column subtraction, not
            ' an afternoon in the source.
            writer.WriteLine("t_s,row,tank,team,x,z,heading_deg,speed_ms," &
                             "travelled_m,why,start_id,wp_at,wp_of," &
                             "d_fl,d_fr,d_rl,d_rr,d_front,d_rear,d_right,d_left," &
                             "s_fl,s_fr,s_rl,s_rr,s_front,s_rear,s_right,s_left," &
                             "blk_ahead,blk_rear,stuck_s")
            writer.Flush()
            LogThis("tank log: writing {0}", path_)
        Catch ex As Exception
            writer = Nothing
            LogThis("tank log: could not open {0} - {1}", path_, ex.Message)
        End Try
    End Sub

    Public Sub Close()
        Try
            If writer IsNot Nothing Then
                writer.Flush()
                writer.Dispose()
                LogThis("tank log: closed {0}", path_)
            End If
        Catch
        End Try
        writer = Nothing
        lastStates.Clear()
        lastWhy.Clear()
        lastAt.Clear()
        lastDist.Clear()
    End Sub

    ''' <summary>
    ''' Offer one hull's state. Writes only on a change or a heartbeat.
    '''
    ''' Called once per hull per frame; the decision about whether this frame is
    ''' worth recording lives HERE rather than at the call site, so there is one
    ''' rule and the caller cannot get it subtly wrong.
    ''' </summary>
    Public Sub Note(inst As TankInstance, others As List(Of TankInstance))
        If writer Is Nothing OrElse inst Is Nothing Then Return

        Dim d = TankSim.RayHitDistances(inst, others)
        Dim st(TankSim.RAY_COUNT - 1) As Integer
        Dim key As New StringBuilder(TankSim.RAY_COUNT)
        For i = 0 To TankSim.RAY_COUNT - 1
            st(i) = TankSim.RayState(i, d(i))
            key.Append(st(i))
        Next
        ' The two decisions, not the eight measurements. Both are cached per
        ' frame inside TankSim, so asking costs a dictionary lookup.
        Dim blkAhead = TankSim.BlockedAhead(inst, others)
        Dim blkRear = TankSim.RearBlocked(inst, others)
        key.Append(If(blkAhead, "A"c, "-"c))
        key.Append(If(blkRear, "R"c, "-"c))

        Dim stateKey = key.ToString()
        Dim why = CInt(inst.drive.stopReason)

        Dim now_ = (DateTime.Now - started).TotalSeconds
        Dim was As String = Nothing
        Dim wasWhy = -1
        Dim wasAt = -999.0
        lastStates.TryGetValue(inst, was)
        lastWhy.TryGetValue(inst, wasWhy)
        lastAt.TryGetValue(inst, wasAt)

        Dim changed = (was Is Nothing) OrElse (was <> stateKey) OrElse (wasWhy <> why)
        Dim due = (now_ - wasAt) >= (1.0 / SAMPLE_HZ)
        If Not changed AndAlso Not due Then Return

        ' TRAVEL SINCE THE LAST ROW, not since the run started. The owner wants
        ' to see how far a hull got between one ray impact and the next, and a
        ' running total makes that a subtraction the reader has to do.
        Dim prevDist = inst.trackDistance
        lastDist.TryGetValue(inst, prevDist)
        Dim moved = inst.trackDistance - prevDist

        ' RunAt / RunOf, which is what master's TankSim exposes. My own
        ' Progress() and StartIdOf() went when I took master's file whole in the
        ' merge, and that was the right call - this is the same two numbers
        ' through the API that survived.
        Dim wpAt = TankSim.RunAt(inst)
        Dim runPts = TankSim.RunOf(inst)
        Dim wpOf = If(runPts Is Nothing, 0, runPts.Count)
        Dim heading = inst.headingRad * 180.0F / CSng(Math.PI)
        Dim inv = Globalization.CultureInfo.InvariantCulture

        Try
            writer.WriteLine(String.Format(inv,
                "{0:0.000},{1},{2},{3},{4:0.0},{5:0.0},{6:0.0},{7:0.00},{8:0.00}," &
                "{9},{10},{11},{12}," &
                "{13:0.0},{14:0.0},{15:0.0},{16:0.0},{17:0.0},{18:0.0},{19:0.0},{20:0.0}," &
                "{21},{22},{23},{24},{25},{26},{27},{28}," &
                "{29},{30},{31:0.00}",
                now_, If(changed, "EVENT", "SAMPLE"), inst.label,
                If(inst.team = TankTeam.Green, 1, 2),
                inst.position.X, inst.position.Z, heading, inst.drive.speed, moved,
                inst.drive.stopReason.ToString(), TankSim.StartIdFor(inst),
                wpAt, wpOf,
                Cap(d(TankSim.R_FL)), Cap(d(TankSim.R_FR)),
                Cap(d(TankSim.R_RL)), Cap(d(TankSim.R_RR)),
                Cap(d(TankSim.R_FRONT)), Cap(d(TankSim.R_REAR)),
                Cap(d(TankSim.R_RIGHT)), Cap(d(TankSim.R_LEFT)),
                st(TankSim.R_FL), st(TankSim.R_FR), st(TankSim.R_RL),
                st(TankSim.R_RR), st(TankSim.R_FRONT), st(TankSim.R_REAR),
                st(TankSim.R_RIGHT), st(TankSim.R_LEFT),
                If(blkAhead, 1, 0), If(blkRear, 1, 0), inst.drive.stuckS))
        Catch
            ' A failed write must not take the sim with it.
        End Try

        lastStates(inst) = stateKey
        lastWhy(inst) = why
        lastAt(inst) = now_
        lastDist(inst) = inst.trackDistance
    End Sub

    ''' <summary>MaxValue reads badly in a spreadsheet; the ray's own reach is
    ''' the honest "saw nothing" value.</summary>
    Private Function Cap(v As Single) As Single
        If v >= TankSim.RAY_LEN_M Then Return TankSim.RAY_LEN_M
        Return v
    End Function

    ''' <summary>Flush without closing, so a run being watched is readable
    ''' while it is still going.</summary>
    Public Sub Tick()
        If writer Is Nothing Then Return
        Try
            writer.Flush()
        Catch
        End Try
    End Sub

End Module

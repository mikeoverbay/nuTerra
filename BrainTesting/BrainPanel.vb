Imports System.IO
Imports ImGuiNET
Imports OpenTK.Mathematics
Imports OpenTK.Windowing.Desktop

''' <summary>
''' THE CONTROLS, on the left over the viewport.
'''
''' "I need to take pics. I need to reset. I need to take data snapshots. I
''' need to send cam position. Think about what we need and add it. I want
''' buttons on left side on top of view port" - the owner, 2026-09-16.
'''
''' WHAT A LONG NIGHT NEEDS, which is what he asked me to think about. Every
''' button here answers a question that comes up repeatedly when you are
''' watching one tank try something:
'''
'''   Run / Stop      the same as Space, but visible - so the state of the sim
'''                   is readable without remembering what you last pressed
'''   Reset           hulls back where they started, goal cleared, sim stopped.
'''                   The single most-used control in an evening of scenarios,
'''                   because every scenario begins the same way
'''   Shot            the window to a PNG, timestamped, no overwriting
'''   Snapshot        the NUMBERS behind what is on screen, to a text file -
'''                   hull, goal, and all 30 rays with the cell each landed in.
'''                   A picture proves it looked wrong; this says why
'''   Cam             the camera as a `cam=` argument, logged and copied to the
'''                   clipboard, so the exact view can be relaunched or handed
'''                   to another session. "I need to send cam position"
'''   Radar           the rays off, for when they are in the way of looking at
'''                   the tank itself
'''
''' THE PANEL DOES NOT ACT. It returns what was pressed and BrainWindow does
''' it. Capture and the sim controls live there and are private to it; a panel
''' that reached into them would be a second place that starts and stops the
''' world.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Module BrainPanel

    Public Enum Action
        None = 0
        RunStop
        Reset
        Shot
        Snapshot
        Restore
        Cam
    End Enum

    Private ctl As ImGuiController = Nothing

    ''' <summary>Smoothed frames a second. A frame COUNT says how long the
    ''' app has been up, which is never the question; the rate is what tells
    ''' you whether the last change cost anything.</summary>
    Private fps As Double = 0.0
    Private origins As List(Of BrainTanks.Body) = Nothing

    ''' <summary>Where pictures and snapshots go. The shared folder, so another
    ''' session can read what happened without being handed it.</summary>
    Public Const OUT_DIR As String = "C:\nuTerra_shared\tank_ai_work\brain"

    Public Sub Init(width As Integer, height As Integer)
        ctl = New ImGuiController(width, height)
    End Sub

    Public Sub Resized(width As Integer, height As Integer)
        If ctl IsNot Nothing Then ctl.WindowResized(width, height)
    End Sub

    Public Sub PressChar(c As Char)
        If ctl IsNot Nothing Then ctl.PressChar(c)
    End Sub

    Public Sub Scroll(offset As Vector2)
        If ctl IsNot Nothing Then ctl.MouseScroll(offset)
    End Sub

    ''' <summary>Remember where everything started, so Reset has something to
    ''' go back to. Movement is written into Body.spawn, so the opening
    ''' positions are gone the moment anything drives - they have to be taken
    ''' before that, once the roster is complete.</summary>
    Public Sub RememberSpawns()
        If BrainTanks.Bodies Is Nothing Then Return
        origins = New List(Of BrainTanks.Body)(BrainTanks.Bodies)
        LogThis("brain: panel remembered {0} opening position(s)", origins.Count)
    End Sub

    Public ReadOnly Property HasSpawns As Boolean
        Get
            Return origins IsNot Nothing AndAlso origins.Count > 0
        End Get
    End Property

    ''' <summary>Put everything back. The one control used most in an evening.</summary>
    Public Sub RestoreSpawns()
        If origins Is Nothing Then Return
        For i = 0 To Math.Min(origins.Count, BrainTanks.Bodies.Count) - 1
            BrainTanks.Bodies(i) = origins(i)
        Next
        LogThis("brain: reset - {0} hull(s) back to their opening positions",
                Math.Min(origins.Count, BrainTanks.Bodies.Count))
    End Sub

    ''' <summary>The camera as the argument that would recreate it.</summary>
    Public Function CamArg() As String
        Dim t = BrainRender.Cam.Target
        Dim d = BrainRender.Cam.Dist
        Return String.Format(Globalization.CultureInfo.InvariantCulture,
                             "cam={0:0.0},{1:0.0},{2:0.0}", t.X, t.Z, d)
    End Function

    ''' <summary>Everything behind the picture, as text.</summary>
    Public Function WriteSnapshot() As String
        Try
            Directory.CreateDirectory(OUT_DIR)
            Dim p = Path.Combine(OUT_DIR,
                                 String.Format("{0:yyyyMMdd_HHmmss}_snap.txt", DateTime.Now))
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("# Brain Testing snapshot " & DateTime.Now.ToString("s"))
            sb.AppendLine("map=" & STARTUP_MAP)
            sb.AppendLine(CamArg())
            sb.AppendLine("brain=" & If(BrainSim.Brain Is Nothing, "none", BrainSim.Brain.Name))
            sb.AppendLine("running=" & BrainSim.Running.ToString())
            sb.AppendLine("frame=" & BrainSim.Frame.ToString())
            If BrainGoal.HasTarget Then
                sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                            "goal={0:0.0},{1:0.0}",
                                            BrainGoal.Target.X, BrainGoal.Target.Y))
            Else
                sb.AppendLine("goal=none")
            End If

            sb.AppendLine()
            sb.AppendLine("# hulls: id,team,tag,x,z,heading_deg,y")
            If BrainTanks.Bodies IsNot Nothing Then
                For Each b In BrainTanks.Bodies
                    sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                                "{0},{1},{2},{3:0.0},{4:0.0},{5:0.0},{6:0.0}",
                                                b.id, b.team, b.tag, b.spawn.X, b.spawn.Y,
                                                MathHelper.RadiansToDegrees(b.headingRad), b.y))
                Next
            End If

            sb.AppendLine()
            sb.AppendLine("# radar off hull " & BrainRadar.HULL.ToString() &
                          ": ray,arc,angle_deg,dist_m,found,cell_row,cell_col,x,z")
            Dim hits = BrainRadar.LAST
            If hits IsNot Nothing Then
                For i = 0 To hits.Length - 1
                    Dim q = hits(i)
                    sb.AppendLine(String.Format(Globalization.CultureInfo.InvariantCulture,
                                                "{0},{1},{2:0.0},{3:0.00},{4},{5},{6},{7:0.0},{8:0.0}",
                                                i, If(q.front, "front", "rear"),
                                                MathHelper.RadiansToDegrees(q.angle),
                                                q.dist, q.found, q.row, q.col, q.at.X, q.at.Y))
                Next
            End If

            File.WriteAllText(p, sb.ToString())
            LogThis("brain: snapshot -> {0}", p)
            Return p
        Catch ex As Exception
            LogThis("brain: snapshot failed - {0}", ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Put the tank and the camera back where a snapshot found them.
    '''
    ''' "now i want to save the tank and the cam position when I click
    ''' Snapshot" - the owner. A snapshot that can only be read is a
    ''' record; one that can be restored is a SCENARIO, and a scenario is
    ''' what makes two runs comparable - same tank, same ground, same view,
    ''' one thing changed.
    '''
    ''' Reads the newest file in OUT_DIR, so the button means "back to where
    ''' I last saved" without a file dialog this app has no way to show.
    ''' </summary>
    Public Function RestoreSnapshot() As String
        Try
            If Not Directory.Exists(OUT_DIR) Then Return ""
            Dim newest As String = ""
            Dim best = DateTime.MinValue
            For Each f In Directory.GetFiles(OUT_DIR, "*_snap.txt")
                Dim t = File.GetLastWriteTime(f)
                If t > best Then best = t : newest = f
            Next
            If newest = "" Then
                LogThis("brain: no snapshot to restore")
                Return ""
            End If

            Dim inv = Globalization.CultureInfo.InvariantCulture
            Dim hulls As New List(Of String())
            Dim in_hulls = False
            For Each line In File.ReadAllLines(newest)
                Dim s = line.Trim()
                If s = "" Then Continue For
                If s.StartsWith("# hulls") Then in_hulls = True : Continue For
                If s.StartsWith("#") Then in_hulls = False : Continue For
                If s.StartsWith("cam=") Then
                    Dim f3 = s.Substring(4).Split(","c)
                    Dim cx, cz, cd As Single
                    If f3.Length >= 3 AndAlso
                       Single.TryParse(f3(0), Globalization.NumberStyles.Float, inv, cx) AndAlso
                       Single.TryParse(f3(1), Globalization.NumberStyles.Float, inv, cz) AndAlso
                       Single.TryParse(f3(2), Globalization.NumberStyles.Float, inv, cd) Then
                        BrainRender.Cam.LookAt(cx, cz, BrainNav.Ground(cx, cz), cd)
                    End If
                ElseIf s.StartsWith("goal=") AndAlso Not s.EndsWith("none") Then
                    Dim f2 = s.Substring(5).Split(","c)
                    Dim gx, gz As Single
                    If f2.Length >= 2 AndAlso
                       Single.TryParse(f2(0), Globalization.NumberStyles.Float, inv, gx) AndAlso
                       Single.TryParse(f2(1), Globalization.NumberStyles.Float, inv, gz) Then
                        BrainGoal.Target = New Vector2(gx, gz)
                        BrainGoal.HasTarget = True
                    End If
                ElseIf in_hulls Then
                    hulls.Add(s.Split(","c))
                End If
            Next

            Dim put = 0
            For Each row In hulls
                ' id,team,tag,x,z,heading_deg,y
                If row.Length < 7 Then Continue For
                Dim id As Integer, x, z, hd As Single
                If Not Integer.TryParse(row(0), id) Then Continue For
                If Not Single.TryParse(row(3), Globalization.NumberStyles.Float, inv, x) Then Continue For
                If Not Single.TryParse(row(4), Globalization.NumberStyles.Float, inv, z) Then Continue For
                If Not Single.TryParse(row(5), Globalization.NumberStyles.Float, inv, hd) Then Continue For
                For i = 0 To BrainTanks.Bodies.Count - 1
                    Dim b = BrainTanks.Bodies(i)
                    If b.id <> id Then Continue For
                    b.spawn = New Vector2(x, z)
                    b.headingRad = MathHelper.DegreesToRadians(hd)
                    b.y = BrainNav.Ground(x, z)
                    BrainTanks.Bodies(i) = b
                    put += 1
                    Exit For
                Next
            Next
            LogThis("brain: restored {0} hull(s) and the camera from {1}",
                    put, Path.GetFileName(newest))
            Return newest
        Catch ex As Exception
            LogThis("brain: restore failed - {0}", ex.Message)
            Return ""
        End Try
    End Function

    Public Function ShotPath() As String
        Directory.CreateDirectory(OUT_DIR)
        Return Path.Combine(OUT_DIR,
                            String.Format("{0:yyyyMMdd_HHmmss}_shot.png", DateTime.Now))
    End Function

    ''' <summary>Build the panel. Returns what was pressed, if anything.</summary>
    Public Function Draw(wnd As GameWindow, dt As Single) As Action
        If ctl Is Nothing Then Return Action.None
        ctl.Update(wnd, dt)

        ' Exponentially smoothed, because the raw per-frame number is
        ' unreadable - it flickers across a range wide enough that you
        ' cannot tell 40 from 60 by looking.
        If dt > 0.0F Then fps = If(fps = 0.0, 1.0 / dt, fps * 0.92 + (1.0 / dt) * 0.08)

        Dim act = Action.None

        ImGui.SetNextWindowPos(New System.Numerics.Vector2(12, 12), ImGuiCond.Once)
        ImGui.SetNextWindowSize(New System.Numerics.Vector2(232, 0), ImGuiCond.Once)
        ImGui.Begin("Brain")

        ' WHAT IT IS WAITING FOR, first and in colour. "if it is waiting to
        ' start, it would be good to know" - and every one of these states
        ' looks identical from outside: a tank sitting still because the roster
        ' is loading, because nothing pressed Run, because there is no goal, or
        ' because it arrived. Naming which one is the difference between
        ' waiting and debugging.
        Dim msg As String
        Dim col As System.Numerics.Vector4
        If BrainTanks.Loading Then
            msg = "loading the roster..."
            col = New System.Numerics.Vector4(1.0F, 0.8F, 0.3F, 1.0F)
        ElseIf Not BrainGoal.HasTarget Then
            msg = "WAITING - no goal. alt places one"
            col = New System.Numerics.Vector4(1.0F, 0.75F, 0.2F, 1.0F)
        ElseIf Not BrainSim.Running Then
            msg = "WAITING - stopped. Run, or space"
            col = New System.Numerics.Vector4(1.0F, 0.75F, 0.2F, 1.0F)
        Else
            Dim w = "running"
            If BrainSim.LastWhy IsNot Nothing AndAlso
               BrainSim.LastWhy.Length > BrainRadar.HULL Then
                w = BrainSim.LastWhy(BrainRadar.HULL)
            End If
            msg = "running - " & w
            col = New System.Numerics.Vector4(0.5F, 1.0F, 0.6F, 1.0F)
        End If
        ImGui.TextColored(col, msg)
        ' THE RATES, right under the state. They were at the bottom of a long
        ' panel, which is the same as not being there.
        ImGui.Text(String.Format("{0:0} fps   ai {1:0.00} ms", fps, BrainSim.TickMs))
        ImGui.Separator()

        If ImGui.Button(If(BrainSim.Running, "Stop  [space]", "Run  [space]"),
                        New System.Numerics.Vector2(210, 26)) Then act = Action.RunStop
        If ImGui.Button("Reset", New System.Numerics.Vector2(210, 26)) Then act = Action.Reset

        ImGui.Separator()
        If ImGui.Button("Shot  (png)", New System.Numerics.Vector2(210, 24)) Then act = Action.Shot
        If ImGui.Button("Snapshot  (save tank+cam)", New System.Numerics.Vector2(210, 24)) Then act = Action.Snapshot
        If ImGui.Button("Restore  (last snapshot)", New System.Numerics.Vector2(210, 24)) Then act = Action.Restore
        If ImGui.Button("Cam  (copy)", New System.Numerics.Vector2(210, 24)) Then act = Action.Cam

        ImGui.Separator()
        Dim show = BrainRadar.SHOW
        If ImGui.Checkbox("Radar", show) Then BrainRadar.SHOW = show
        Dim scope = BrainScope.SHOW
        If ImGui.Checkbox("Scope", scope) Then BrainScope.SHOW = scope
        Dim graph = BrainNodes.SHOW
        If ImGui.Checkbox("Brain graph  (G)", graph) Then BrainNodes.SHOW = graph
        ' WHICH BRAIN IS DRIVING. Swapped live rather than on restart, so
        ' the same goal and the same spot can be handed to both - two runs
        ' from different places do not compare.
        Dim ong = USE_GRAPH
        If ImGui.Checkbox("Drive from the board", ong) Then
            USE_GRAPH = ong
            BrainSim.Brain = If(USE_GRAPH, CType(New GraphBrain(), IBrain),
                                           CType(New RangeBrain(), IBrain))
        End If
        ImGui.TextDisabled("  " & BrainSim.Brain.Name)

        Dim chase = BrainRender.Cam.Chase
        If ImGui.Checkbox("Chase cam  (C)", chase) Then BrainRender.Cam.Chase = chase
        Dim trail = BrainRender.Cam.ChaseTrail
        If ImGui.Checkbox("  trail heading  (shift+C)", trail) Then
            BrainRender.Cam.ChaseTrail = trail
            If trail Then BrainRender.Cam.Chase = True
        End If

        ImGui.Separator()
        ImGui.TextDisabled("alt: pin goal    arrows: step / aim")
        Dim n = If(BrainTanks.Bodies Is Nothing, 0, BrainTanks.Bodies.Count)
        ImGui.Text(String.Format("hulls {0}", n))
        If n > BrainRadar.HULL Then
            Dim b = BrainTanks.Bodies(BrainRadar.HULL)
            ImGui.Text(String.Format(Globalization.CultureInfo.InvariantCulture,
                                     "tank {0:0.0}, {1:0.0}  {2:0}deg",
                                     b.spawn.X, b.spawn.Y,
                                     MathHelper.RadiansToDegrees(b.headingRad)))
        End If
        If BrainGoal.HasTarget Then
            ImGui.Text(String.Format(Globalization.CultureInfo.InvariantCulture,
                                     "goal {0:0.0}, {1:0.0}",
                                     BrainGoal.Target.X, BrainGoal.Target.Y))
        Else
            ImGui.TextDisabled("no goal")
        End If
        Dim hits = BrainRadar.LAST
        If hits IsNot Nothing Then
            Dim found = 0
            For Each q In hits
                If q.found Then found += 1
            Next
            ImGui.Text(String.Format("rays {0}/{1} on ground", found, hits.Length))
        End If

        ImGui.End()

        ' THE SCOPE, anchored bottom right. Drawn inside the same ImGui frame
        ' as the panel - a second Render() would start a frame that was never
        ' begun.
        ' THE NODE EDITOR DRAWS IN ITS OWN WINDOW NOW - its own form, its own
        ' GL context, its own ImGui context. See BrainNodeForm. What stays
        ' here is the key that opens it, because a hidden window runs no
        ' frames and so could never read the key that brings it back.
        '
        ' Not while a text field has the keyboard, or typing a G into a name
        ' would toggle the editor it was being typed into.
        If Not ImGui.GetIO().WantTextInput AndAlso ImGui.IsKeyPressed(ImGuiKey.G) Then
            BrainNodes.SHOW = Not BrainNodes.SHOW
        End If

        ctl.Render()
        Return act
    End Function

End Module

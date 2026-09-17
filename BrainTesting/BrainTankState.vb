Imports OpenTK.Mathematics

''' <summary>
''' WHAT THE TANK IS DOING, ON A CARD ABOVE THE TANK.
'''
''' "make an FBO called TankState and draw it over the tanks location like we
'''  did the bill board over the tanks in nuTerra" - the owner, 2026-09-17.
'''
''' THE POINT IS THAT IT IS IN THE WORLD. The state has been readable all
''' evening - it is on the heartbeat, it is on the panel - and both of those
''' make you look AWAY from the tank to read them, then back, by which time the
''' thing you were trying to explain has happened. A card over the hull puts the
''' words and the behaviour in one glance. "Backing" written above a tank that
''' is reversing is a confirmation; written above one that is sitting still it
''' is the bug, immediately.
'''
''' IT IS ITS OWN FBO, not text drawn straight into the world, and that buys
''' three things: the background panel and the text composite as ONE picture so
''' overlapping glyphs cannot double-darken the backing; the whole card fades
''' with distance as a unit; and the layout is done in comfortable pixel
''' coordinates rather than in metres.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainTankState

    Public SHOW As Boolean = True

    ''' <summary>How tall the card stands in the world. Roughly a hull length,
    ''' which keeps it legible without burying the tank it describes.</summary>
    Public TallM As Single = 7.0F

    ''' <summary>Metres above the hull's own height. Clear of the turret, under
    ''' the point where it stops reading as attached to anything.</summary>
    Public LiftM As Single = 4.5F

    Private canvas As BrainCanvas = Nothing
    Private Const PX As Integer = 256

    Private ReadOnly BACK As New Vector4(0.02F, 0.05F, 0.09F, 0.72F)
    Private ReadOnly EDGE As New Vector4(0.28F, 0.46F, 0.62F, 0.9F)
    Private ReadOnly TITLE As New Vector4(1.0F, 0.72F, 0.24F, 1.0F)
    Private ReadOnly BODY As New Vector4(0.86F, 0.92F, 0.98F, 1.0F)
    Private ReadOnly GOOD As New Vector4(0.25F, 0.88F, 0.25F, 1.0F)
    Private ReadOnly WARN As New Vector4(1.0F, 0.86F, 0.24F, 1.0F)

    ''' <summary>
    ''' Draw the card and stand it over the hull.
    '''
    ''' Called after the world, while the depth buffer still holds it, so the
    ''' card can be hidden by terrain in front of it.
    ''' </summary>
    Public Sub Draw(ByRef viewProj As Matrix4, camRight As Vector3, camUp As Vector3,
                    screenW As Integer, screenH As Integer)
        If Not SHOW Then Return
        If BrainTanks.Bodies Is Nothing OrElse BrainTanks.Bodies.Count <= BrainRadar.HULL Then Return
        Dim b = BrainTanks.Bodies(BrainRadar.HULL)

        If canvas Is Nothing Then canvas = New BrainCanvas(PX, "TankState")

        ' ---- the card ------------------------------------------------------
        canvas.Begin(New Vector4(0.0F, 0.0F, 0.0F, 0.0F))
        canvas.RectFill(0.0F, 0.0F, PX, PX, BACK)
        canvas.Rect(1.0F, 1.0F, PX - 1.0F, PX - 1.0F, EDGE)

        Dim sc = 1.0F
        Dim lh = BrainText.Height(sc) + 2.0F
        Dim y = 8.0F

        BrainText.Text2D(b.tag, 10.0F, y, sc, TITLE)
        y += lh + 2.0F
        canvas.Line(8.0F, y, PX - 8.0F, y, EDGE)
        y += 6.0F

        BrainText.Text2D(BrainSim.Brain.Name, 10.0F, y, sc, BODY)
        y += lh

        ' WHY, wrapped by hand. A card that runs its text off the edge is worse
        ' than one that says less: the part that gets cut is the end, and the
        ' end of a why is the part that explains it.
        Dim why = BrainReport.LastWhy
        If why Is Nothing Then why = "-"
        For Each part In wrap(why, 26)
            BrainText.Text2D(part, 10.0F, y, sc, BODY)
            y += lh
        Next

        y += 4.0F
        Dim spd = Math.Abs(BrainReport.LastSpeed)
        BrainText.Text2D(String.Format("{0:0.0} m/s", spd), 10.0F, y, sc,
                         If(spd < 0.2F, WARN, GOOD))
        y += lh

        ' A throttle bar, because a number and a bar fail differently: the bar
        ' shows a value pinned at its floor without anyone reading it.
        Dim thr = BrainReport.LastThrottle
        Dim barY = y + 4.0F
        canvas.Rect(10.0F, barY, PX - 10.0F, barY + 10.0F, EDGE)
        Dim w = (PX - 22.0F) * Math.Min(1.0F, Math.Abs(thr))
        If w > 1.0F Then
            canvas.RectFill(11.0F, barY + 1.0F, 11.0F + w, barY + 9.0F,
                            If(thr < 0.0F, WARN, GOOD))
        End If

        ' The text goes into the card, so it is flushed while the FBO is bound
        ' and in the card's own pixel space.
        ' GEOMETRY DOWN FIRST, THEN TEXT ON TOP.
        '
        ' BrainText draws immediately and the canvas QUEUES, so
        ' without this every rectangle and line below lands over the
        ' words - on the card that meant the backing panel painting
        ' out its own text, every frame.
        canvas.Flush()
        BrainText.Render2D(PX, PX)
        canvas.End(screenW, screenH)

        ' ---- and stand it over the hull -------------------------------------
        Dim at = New Vector3(b.spawn.X, b.y + LiftM, b.spawn.Y)
        canvas.BlitWorld(at, TallM, camRight, camUp, viewProj, 1.0F)
    End Sub

    ''' <summary>Break a line at spaces, falling back to a hard cut for a word
    ''' longer than the card.</summary>
    Private Function wrap(s As String, cols As Integer) As List(Of String)
        Dim outp As New List(Of String)
        Dim cur = ""
        For Each word In s.Split(" "c)
            If cur.Length = 0 Then
                cur = word
            ElseIf cur.Length + 1 + word.Length <= cols Then
                cur &= " " & word
            Else
                outp.Add(cur)
                cur = word
            End If
            While cur.Length > cols
                outp.Add(cur.Substring(0, cols))
                cur = cur.Substring(cols)
            End While
            If outp.Count >= 3 Then Exit For
        Next
        If cur.Length > 0 AndAlso outp.Count < 3 Then outp.Add(cur)
        Return outp
    End Function

End Module

Imports OpenTK.Mathematics

''' <summary>
''' TEMPORARY TEST VIEW: the scan from here, the scan from up the road, and a
''' line between each pair of ray endpoints.
'''
''' "scan where were are and the forward placed at and connect their endpoints
'''  as lines. I just want to see what it looks like. its temp testing." - the
''' owner, 2026-09-18.
'''
''' THIS IS A LOOK, NOT A FEATURE. It exists to answer one question - what does
''' the parallax between two scan origins actually look like - and it should be
''' deleted the moment that question is answered. Off by default, switched from
''' the panel beside Radar and Scope, or `ahead` on the command line.
'''
''' WHAT THE LINES MEAN. Ray i from here and ray i from ahead point the same way
''' in the world, so the line between where they stopped is how that direction's
''' return MOVED when the viewpoint moved. Long lines are near things, short
''' lines are far things - the same reason near trees fly past a car window and
''' distant hills do not. A direction whose line barely moves is a long way off
''' and safe to decide about early; one that swings hard is close, and deciding
''' about it from here is deciding about something you have not really seen yet.
''' A BLUE DOT WITH NO LINE is the payoff: something the scan from up the road
''' can see and the scan from here cannot.
'''
''' THE CHROME DRAWS BEFORE THE SCAN DOES. First version returned early when
''' BrainRadar.LAST was Nothing, which is its state until a brain tick calls
''' Scan - so before Run the whole view drew nothing at all, not even a frame,
''' and the checkbox read as a dead switch. An instrument with no signal shows
''' an empty dial and says so; it does not vanish.
'''
''' IT COSTS TWO EXTRA SCANS A FRAME and puts the near one back afterwards,
''' because Scan fills the one global LAST and WAYS that the brain reads. That
''' is fine for a test view at 800 FPS and is the first thing to kill if it ever
''' stops being one.
'''
''' Added 2026-09-18 by Tank AI work.
''' </summary>
Module BrainAheadScope

    Public SHOW As Boolean = False

    ''' <summary>How far up the road the second scan is taken. The turn budget
    ''' says a forty degree swing at speed needs about eighteen metres, so this
    ''' defaults to roughly where the decision has to be made.</summary>
    Public AheadM As Single = 20.0F

    Private Const PX As Integer = 320
    Private live As BrainCanvas = Nothing

    ' OPAQUE. At 0.55 the map showed straight through it and the treeline
    ' behind the window was indistinguishable from the returns drawn in it -
    ' which on a view whose entire content is scattered dots is fatal. The
    ' scope gets away with translucency because its content is a dense orange
    ' fan; this one is twenty dots and a few lines.
    Private ReadOnly BACKDROP As New Vector4(0.03F, 0.05F, 0.09F, 0.94F)
    Private ReadOnly FRAME As New Vector4(0.42F, 0.50F, 0.58F, 1.0F)
    Private ReadOnly RING As New Vector4(0.26F, 0.36F, 0.48F, 0.9F)
    Private ReadOnly NEAR_INK As New Vector4(1.0F, 0.55F, 0.15F, 1.0F)
    Private ReadOnly AHEAD_INK As New Vector4(0.35F, 0.75F, 1.0F, 1.0F)
    ' A tie is coloured by its verdict, from BrainRadar.Compare. Grey for a
    ' wall seen twice, bright for a direction that OPENED - the payoff - and
    ' red for one that shut. Three colours because there are three answers, and
    ' the one worth looking at should not have to be picked out of a crowd.
    Private ReadOnly TIE As New Vector4(0.62F, 0.76F, 0.88F, 0.85F)
    Private ReadOnly OPEN_INK As New Vector4(0.45F, 1.0F, 0.45F, 0.95F)
    Private ReadOnly SHUT_INK As New Vector4(1.0F, 0.35F, 0.30F, 0.85F)
    ' Rays Compare did not count - astern, or nothing found from either place.
    ' Drawn, because the owner wants the whole sweep tied, but faint: they are
    ' the shape of the scan rather than anything it learned.
    Private ReadOnly MISS_INK As New Vector4(0.46F, 0.58F, 0.72F, 0.60F)
    Private ReadOnly HULL_INK As New Vector4(0.95F, 0.95F, 0.95F, 0.9F)
    Private ReadOnly GREY As New Vector4(0.62F, 0.68F, 0.74F, 0.9F)

    Private Function Mid_() As Single
        Return PX * 0.5F
    End Function

    ''' <summary>Metres to pixels. Both scans share one scale or the lines
    ''' between them would be meaningless.</summary>
    Private Function Scale_() As Single
        Return (PX * 0.5F - 8.0F) / Math.Max(10.0F, BrainRadar.REACH_M + AheadM)
    End Function

    Public Sub Draw(screenW As Integer, screenH As Integer)
        If Not SHOW Then Return
        If live Is Nothing Then live = New BrainCanvas(PX, "AheadScope")

        Dim sc = Scale_()
        Dim m = Mid_()
        Dim ay = m - AheadM * sc

        ' ---- the dial, whether or not there is anything to put on it -------
        live.Begin(BACKDROP)
        live.RectFill(0.0F, 0.0F, PX, PX, BACKDROP)
        live.Rect(0.5F, 0.5F, PX - 0.5F, PX - 0.5F, FRAME)

        For d = 10 To CInt(BrainRadar.REACH_M + AheadM) Step 10
            live.Circle(m, m, d * sc, RING)
        Next

        ' The hull to scale, the second origin AheadM up the board, and the
        ' stalk between them - so the offset is read off the picture rather
        ' than taken on trust from the caption.
        Dim hw = 1.7F * sc, hl = 3.5F * sc
        live.Rect(m - hw, m - hl, m + hw, m + hl, HULL_INK)
        live.Line(m, m, m, ay, HULL_INK)
        live.Circle(m, ay, 4.0F, AHEAD_INK)

        ' ---- and the two scans, if there has been a scan --------------------
        Dim note As String = "no scan yet - press Run"
        Dim nNear = 0, nAhead = 0, nOuts = 0
        Dim gain As Single = 0.0F, bear As Single = 0.0F, agree As Single = 0.0F

        Dim near_ = BrainRadar.LAST
        Dim have = near_ IsNot Nothing AndAlso
                   BrainTanks.Bodies IsNot Nothing AndAlso
                   BrainTanks.Bodies.Count > BrainRadar.HULL

        If have Then
            Dim b = BrainTanks.Bodies(BrainRadar.HULL)

            ' record:=False - LAST and WAYS stay the brain's. This used to scan
            ' a THIRD time purely to put them back, because every Scan wrote
            ' them; the switch retired that.
            Dim r = 1.94F
            Dim fwd = New Vector2(CSng(Math.Sin(b.headingRad)), CSng(Math.Cos(b.headingRad)))
            Dim at = b.spawn + fwd * AheadM
            Dim far = BrainRadar.Scan(at, b.headingRad, r, False)

            ' THE SAME RULE THE BRAIN USES, not a second copy of it. The whole
            ' point of a test view is to show what the thing being tested
            ' actually decided, and a view with its own arithmetic shows what a
            ' different program decided.
            ' NOT `px` - PX is the canvas size and VB is CASE-INSENSITIVE, so
            ' the local would swallow the constant. The same trap the
            ' radar records about `rays` and RAYS.
            Dim both = BrainRadar.Compare(near_, far, AheadM)

            ' Ties first, so the returns themselves stay readable on top. Each
            ' one coloured by what the pair MEANS: an opening is a direction
            ' that got further away as we closed on it, which no surface facing
            ' us can do - so it is a way past whatever stopped the near ray.
            ' EVERY RAY, HIT OR NOT.
            '
            ' A ray that found nothing still went somewhere - out to the full
            ' reach - and "clear that way as far as I can see" is a reading,
            ' not an absence of one. Requiring BOTH ends to be a hit drew one
            ' line out of a hundred and twenty on open ground and hid the very
            ' geometry this view exists to show.
            '
            ' Compare already counts a miss as the full reach, which is where
            ' the biggest openings come from - a wall from here that simply is
            ' not there from up the road. The picture now matches the
            ' arithmetic instead of showing a subset of it.
            Dim n = Math.Min(near_.Length, far.Length)
            For i = 0 To n - 1
                Dim col = MISS_INK
                If both.verdict IsNot Nothing AndAlso i < both.verdict.Length Then
                    Select Case both.verdict(i)
                        Case 1 : col = TIE
                        Case 2 : col = OPEN_INK
                        Case 3 : col = SHUT_INK
                    End Select
                    If both.verdict(i) = 2 Then nOuts += 1
                End If
                live.Line(rx_(near_(i), m, sc), ry_(near_(i), m, sc),
                          rx_(far(i), m, sc), ry_(far(i), ay, sc), col)
            Next

            For i = 0 To near_.Length - 1
                If Not near_(i).found Then Continue For
                live.Disc(px_(near_(i), m, sc), py_(near_(i), m, sc), 2.2F, NEAR_INK)
                nNear += 1
            Next
            For i = 0 To far.Length - 1
                If Not far(i).found Then Continue For
                live.Disc(px_(far(i), m, sc), py_(far(i), ay, sc), 2.2F, AHEAD_INK)
                nAhead += 1
            Next

            ' WHERE THE BEST WAY OUT IS, as a spoke off the hull. Not checked
            ' for reachability here - that is the brain's question and it asks
            ' it from the sample point, not from this one.
            If both.gain > 0.5F Then
                Dim hx = m + CSng(Math.Sin(both.bearing)) * (PX * 0.44F)
                Dim hy = m - CSng(Math.Cos(both.bearing)) * (PX * 0.44F)
                live.Line(m, m, hx, hy, OPEN_INK)
                live.Circle(hx, hy, 5.0F, OPEN_INK)
            End If

            audit(near_, far)

            gain = both.gain
            bear = MathHelper.RadiansToDegrees(both.bearing)
            agree = both.agree
            note = "here / " & AheadM.ToString("0") & " m up"
        End If

        ' GEOMETRY DOWN FIRST, THEN TEXT ON TOP. BrainText draws immediately
        ' and the canvas queues, so without the flush every line lands over the
        ' words.
        live.Flush()
        BrainText.Text2D(note, 8.0F, 6.0F, 0.8F, HULL_INK)
        If have Then
            ' The counts are the measurement. A picture says "sparse"; these
            ' say how sparse, and a blue count above the orange one is the
            ' whole case for looking ahead at all.
            ' `outs` replaced a tie count. Now that every ray draws a line the
            ' tie count is just the ray count, which says nothing; the number
            ' of directions that OPENED is what the view is for.
            BrainText.Text2D(String.Format("near {0:00}  ahead {1:00}  outs {2:00}",
                                           nNear, nAhead, nOuts),
                             8.0F, PX - 38.0F, 0.7F, GREY)
            ' AND THE TWO NUMBERS THAT DECIDE THINGS. `open` is how much
            ' further a step forward lets us see and which way; `agree` is what
            ' share of the forward rays read the same from both places, which
            ' is how much of this picture will still be true when we get there.
            ' Short enough to fit 320 px at this scale. The long form ran off
            ' the right edge and took the agree figure with it - which is the
            ' half that says whether to trust the other half.
            BrainText.Text2D(String.Format("out {0:0}m @{1:0}deg  agree {2:0}%",
                                           gain, bear, agree * 100.0F),
                             8.0F, PX - 22.0F, 0.7F,
                             If(gain > 0.5F, OPEN_INK, GREY))
        End If
        BrainText.Render2D(PX, PX)
        live.End(screenW, screenH)

        ' ---- same corner as the scope, directly under it -------------------
        '
        ' The scope is anchored top right; so is this. Put a scope-width to the
        ' LEFT it depended on the window being wide enough for two side by side,
        ' and on somebody looking where there had never been anything before.
        ' Stacked, it moves with the scope when the window resizes and it is
        ' where you already are.
        Dim shown = BrainScope.ShownPx
        Dim gap = 14.0F
        Dim qx = screenW - shown - BrainScope.Margin
        Dim qy = BrainScope.Margin + shown + gap
        BrainCanvas.ScreenRect(qx - 10.0F, qy - 10.0F,
                               shown + 20.0F, shown + 20.0F,
                               New Vector4(0.0F, 0.0F, 0.0F, 0.55F), screenW, screenH)
        live.Blit(qx, qy, shown, shown, screenW, screenH, 1.0F)
    End Sub

    ''' <summary>
    ''' TEMPORARY AUDIT: is a straight tie really open ground.
    '''
    ''' "straight connections are wide open paths. You can check it" - the
    ''' owner, and it is worth checking rather than agreeing with, because the
    ''' geometry says it is true for one reason and ALMOST true for another.
    '''
    ''' The tie for ray i, in board coordinates with y up the heading, is
    '''
    '''     (k*sin a,  L + k*cos a)      where k = d_far - d_near
    '''
    ''' so it is vertical exactly when k*sin(a) is small - which happens when
    ''' k is near zero, or when the ray points nearly straight up or down the
    ''' board. And k lands near zero in TWO different worlds:
    '''
    '''   BOTH RAYS MISSED. Nothing out to the full reach from either place,
    '''   so both ends sit at the reach and the tie is the baseline itself.
    '''   That is the owner's reading, and it is wide open.
    '''
    '''   BOTH RAYS HIT AT THE SAME RANGE. A surface running ALONG the heading
    '''   - a corridor wall - does not change its distance as you slide past
    '''   it. Vertical tie, and the opposite of open.
    '''
    ''' So the rule holds only if the second case is rare in practice. This
    ''' counts them and says which. Delete it with the rest of the file.
    ''' </summary>
    Private Sub audit(near_ As BrainRadar.Hit(), far As BrainRadar.Hit())
        If auditsDone >= 8 Then Return
        If auditClock.IsRunning AndAlso auditClock.Elapsed.TotalSeconds < 1.0 Then Return
        auditClock.Restart()
        auditsDone += 1

        Dim vOpen = 0, vWall = 0, vMixed = 0
        Dim oOpen = 0, oWall = 0, oMixed = 0
        Dim n = Math.Min(near_.Length, far.Length)
        For i = 0 To n - 1
            Dim rn = reach_(near_(i)), rf = reach_(far(i))
            ' The sideways part of the tie. Under a metre and a half reads as
            ' straight at this scale - about five pixels.
            Dim across = Math.Abs((rf - rn) * CSng(Math.Sin(near_(i).angle)))
            Dim straight = across <= 1.5F

            Dim kind = 1                                   ' both hit
            If Not near_(i).found AndAlso Not far(i).found Then
                kind = 0                                   ' both missed
            ElseIf near_(i).found <> far(i).found Then
                kind = 2                                   ' one of each
            End If

            If straight Then
                Select Case kind
                    Case 0 : vOpen += 1
                    Case 1 : vWall += 1
                    Case Else : vMixed += 1
                End Select
            Else
                Select Case kind
                    Case 0 : oOpen += 1
                    Case 1 : oWall += 1
                    Case Else : oMixed += 1
                End Select
            End If
        Next

        Dim vTot = vOpen + vWall + vMixed
        LogThis("brain: ahead audit - straight {0} of {1}: open {2} ({3:0}%), " &
                "both-hit {4}, one-sided {5} | bent {6}: open {7}, both-hit {8}, " &
                "one-sided {9}",
                vTot, n, vOpen, If(vTot > 0, vOpen * 100.0F / vTot, 0.0F),
                vWall, vMixed, oOpen + oWall + oMixed, oOpen, oWall, oMixed)
    End Sub

    Private auditsDone As Integer = 0
    Private ReadOnly auditClock As New Diagnostics.Stopwatch()

    ''' <summary>
    ''' HOW FAR THAT RAY GOT, counting a miss as the full reach.
    '''
    ''' A ray that found nothing is not a ray with no answer - the answer is
    ''' "clear that way as far as I can see", and it is the answer the biggest
    ''' openings are made of. BrainRadar.Compare uses exactly this fallback, so
    ''' a line drawn here ends where the arithmetic thinks it ends.
    ''' </summary>
    Private Function reach_(q As BrainRadar.Hit) As Single
        Return If(q.found, q.dist, BrainRadar.REACH_M)
    End Function

    ''' <summary>A ray's endpoint on screen, miss or hit.</summary>
    Private Function rx_(q As BrainRadar.Hit, ox As Single, sc As Single) As Single
        Return ox + CSng(Math.Sin(q.angle)) * reach_(q) * sc
    End Function

    Private Function ry_(q As BrainRadar.Hit, oy As Single, sc As Single) As Single
        Return oy - CSng(Math.Cos(q.angle)) * reach_(q) * sc
    End Function

    ''' <summary>A return's screen x. The angle is relative to the nose and
    ''' both scans share a heading, so the same rotation serves both; only the
    ''' origin differs, and on x the two origins are the same.</summary>
    Private Function px_(q As BrainRadar.Hit, ox As Single, sc As Single) As Single
        Return ox + CSng(Math.Sin(q.angle)) * q.dist * sc
    End Function

    ''' <summary>A return's screen y, from whichever origin took it.</summary>
    Private Function py_(q As BrainRadar.Hit, oy As Single, sc As Single) As Single
        Return oy - CSng(Math.Cos(q.angle)) * q.dist * sc
    End Function

End Module

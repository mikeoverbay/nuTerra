Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' NO WAY OUT: seal the mouth of a dead end so nothing drives into it twice.
'''
''' The owner's rule, 2026-09-25: "If all hits from -80 to + 80 deg off the
''' nose are hit and there is no path, I want to draw blockers from the left or
''' right hit to the other on a copy of our baked map. reload the bake map at
''' reset. To be clear, I also not want to draw all the the hits, I want a line
''' for left hit to right hit. either way."
'''
''' WHY A CHORD AND NOT THE HITS. Stamping every return paints the WALLS of the
''' pocket, which the bake already knows about - it changes nothing, because the
''' walls were never what the planner drove through. The mouth is what it drove
''' through. One line across the opening, left edge to right edge, is the whole
''' fact: "there is nothing behind this". Two cells wide instead of two hundred,
''' and it is the only version that actually stops a second attempt.
'''
''' TWO MOMENTS, AND THAT SPLIT IS THE WHOLE SAFETY OF IT.
'''
'''   LOOK  - the arc is closed and the walk found nothing. Record the chord.
'''           Write NOTHING. This is the "no way out" flag.
'''   FREE  - the hull has backed clear. NOW stamp the recorded chord.
'''
''' The rays can only see both edges of the mouth while the hull is facing into
''' the pocket, so the chord has to be captured then. But a chord written then
''' lands BEHIND the hull's own nose and seals it in - and that is not a
''' hypothetical. MarkBlocked did exactly this, was reverted twice, and was
''' finally deleted, because a tank that marks the ground around itself can no
''' longer path anywhere and the failure looks like a broken planner rather than
''' a self-inflicted wall. So: see it from the inside, write it from the
''' outside.
'''
''' AND A GUARD ON TOP OF THAT ORDERING, because ordering is an argument and a
''' guard is a check. Before any cell is written, the chord is measured against
''' where the hull actually is. If it passes within a hull width, the stamp is
''' refused and says so. Belt and braces on the one mistake this feature is
''' capable of making.
'''
''' "A COPY OF OUR BAKED MAP" - it already is one. BrainNav.occ is an in-memory
''' copy that LoadMap re-reads from the .blk, so stamping it never touches the
''' file and a reload is the undo. No second buffer, no extra 31 MB.
'''
''' STAMPING IS OFF BY DEFAULT: tune=trap=1 turns it on. The flag is logged
''' either way, so where and how often it fires can be watched before anything
''' is allowed to write to the map. The owner asked for it in that order.
''' </summary>
Module BrainTrap

    ''' <summary>Half the forward arc that must be solid, degrees. The owner's
    ''' -80 to +80.</summary>
    Private Const ARC_DEG As Single = 80.0F

    ''' <summary>Cells stamped this run. Reset reads it to decide whether the
    ''' bake needs re-reading at all - a reload is a 2800x2800 file and most
    ''' resets have nothing to undo.</summary>
    Public Stamped As Integer = 0
    Public Flags As Integer = 0

    ''' <summary>
    ''' ONE PENDING CHORD PER HULL, keyed by the seat.
    '''
    ''' It was a single set of module fields and five brains shared it. Measured:
    ''' hull 3 raised the flag at (110.8, -307.5), and the very next hull to run
    ''' a walk was 338 m away, so the staleness test dropped hull 3's chord
    ''' before it could ever be written. Three flags, three drops, nothing ever
    ''' stamped - and the log read as though one tank had teleported.
    '''
    ''' Same lesson as BrainRadar.HULL and BrainGoal before it: ANY state the
    ''' crew touches has to be per-seat, because "the brain" is five brains
    ''' taking turns. Look and Free are both called from inside a brain's tick
    ''' with the seat set, so BrainRadar.HULL is the key and no caller has to
    ''' pass one.
    ''' </summary>
    Private Class Pending
        Public leftAt, rightAt, sawFrom As Vector2
        Public sawHeading As Single
        ''' <summary>A PLACE TO DRIVE TO, on the open side. Recorded while the
        ''' rays can still see it - see open_spot.</summary>
        Public openAt As Vector2
        Public haveOpen As Boolean
    End Class

    Private ReadOnly held As New Dictionary(Of Integer, Pending)

    ''' <summary>Every cell this rule newly blocked, in world XZ, so it can be
    ''' drawn. Cleared by Forget, which Reset calls right before it re-reads the
    ''' bake - so the boxes and the blockers vanish together and there is never
    ''' a box standing over ground that is open again.</summary>
    Private ReadOnly marks As New List(Of Vector2)

    ''' <summary>
    ''' EVERY CELL THIS RULE STAMPED, AS A SET, SO IT CANNOT READ ITS OWN
    ''' OUTPUT AS EVIDENCE.
    '''
    ''' Without this the rule feeds on itself and the boxes make it obvious.
    ''' Measured on road 28's trap: chord 1 went down 65 cells across the mouth
    ''' at 48 degrees; the hull turned; the arc ahead was now solid partly
    ''' BECAUSE OF CHORD 1; flag 2; chord 2, longer and further out; and on
    ''' through eight chords and 393 cells, each ring bigger than the last. The
    ''' clearance guard passed every time - 13.8 m rising to 35.7 m - because
    ''' the tank was never enclosed at the moment of writing. It was being
    ''' fenced in one ring at a time by its own conclusions.
    '''
    ''' So a ray that lands on one of our own cells counts as a GAP for the
    ''' purposes of detection. The cell stays blocked - the planner must still
    ''' refuse to drive through it, that is the entire point - but it is not
    ''' allowed to be the proof that closes the arc. A rule whose input includes
    ''' its own output is not a rule, it is a feedback loop.
    '''
    ''' Keyed row * width + col, the same flat index BrainNav uses, and Hit
    ''' already carries row and col so the test is one lookup per ray.
    ''' </summary>
    Private ReadOnly ours As New HashSet(Of Integer)

    ''' <summary>
    ''' WHERE EACH HULL SEALED A DEAD END, AND THEREFORE WANTS TO TURN.
    '''
    ''' The held chord is gone the instant it is written, so it cannot be what
    ''' the board tests to decide "turn away from this". This is the latch that
    ''' outlives the write: set on a successful stamp, cleared once the hull has
    ''' got clear of the place. Per hull, like everything else here.
    ''' </summary>
    Private ReadOnly sealedAt As New Dictionary(Of Integer, Vector2)
    ''' <summary>The go-around point that went with each seal, and how many
    ''' ticks the board has been asked about it. The count exists because a
    ''' latch that only clears by DISTANCE can hold the board for ever if the
    ''' hull does not move - which is exactly what happened when this rung drove
    ''' Turn To at zero throttle.</summary>
    Private ReadOnly sealedTo As New Dictionary(Of Integer, Vector2)
    Private ReadOnly sealedFor As New Dictionary(Of Integer, Integer)

    ''' <summary>
    ''' TRUE WHILE THIS HULL SHOULD TURN TOWARD THE OPEN SIDE.
    '''
    ''' "everything in front is a wall and junk. we used to find the side with a
    ''' angle that was on the correct side of our nose" - the owner,
    ''' 2026-09-25. The board still has that rung: Deepest Ray feeding Turn To.
    ''' It had simply become unreachable, sitting below both the committed
    ''' backing state and the objective, so a tank that spent 75% of its ticks
    ''' backing never once got to it.
    '''
    ''' This is the test that lets it back in, and it is deliberately narrow: it
    ''' is only true just after this hull proved a dead end and sealed it, and
    ''' only until it is clear of the spot. A broad "am I stuck" test at that
    ''' priority would outrank the objective across the whole map.
    '''
    ''' CLEARED BY DISTANCE, on read, because the position is what the caller
    ''' has and a metre count is the honest measure of "somewhere else".
    ''' </summary>
    Public Function TurnOut(pos As Vector2) As Boolean
        Dim me_ = BrainRadar.HULL
        Dim was As Vector2 = Nothing
        If Not sealedAt.TryGetValue(me_, was) Then Return False
        If (pos - was).Length > BrainTune.Get_("trapaway", 25.0F) Then
            drop(me_, "is clear of the dead end it sealed")
            Return False
        End If

        ' AND A HARD CEILING ON HOW LONG IT MAY HOLD THE BOARD.
        '
        ' Distance alone is not a release: a rung that does not move the hull
        ' keeps its own precondition true for ever. That is not hypothetical -
        ' this rung first drove Turn To, which is `thr = 0.0F`, so the tank
        ' pivoted on the spot and could never reach the 25 m that would have
        ' freed it. The ceiling means the worst case is a few seconds lost, not
        ' a run.
        Dim n = 0
        sealedFor.TryGetValue(me_, n)
        n += 1
        sealedFor(me_) = n
        If n > CInt(BrainTune.Get_("trapfor", 600.0F)) Then
            drop(me_, "has held the turn-out for " & n & " ticks without getting clear")
            Return False
        End If
        Return True
    End Function

    Private Sub drop(hull As Integer, why As String)
        sealedAt.Remove(hull)
        sealedTo.Remove(hull)
        sealedFor.Remove(hull)
        LogThis("brain: hull {0} {1} - back to the objective.", hull, why)
    End Sub

    ''' <summary>
    ''' THE BEARING TO A PLACE, not to the most open direction.
    '''
    ''' "we turn to the lonest ray to find the edge but never places a point
    ''' next to it to go around?" - the owner, 2026-09-25, reading the board.
    ''' Exactly right, and it was worse than that: the rung drove Turn To, which
    ''' commands zero throttle, so it found the edge and then pivoted at it for
    ''' ever.
    '''
    ''' A bearing is not a plan. This returns a bearing toward a POINT recorded
    ''' on the open side when the dead end was found, so Drive Heading has
    ''' somewhere to go and the hull leaves the pocket instead of admiring it.
    '''
    ''' Relative to the nose, because every bearing on this board is.
    ''' Nothing when there is no latch or no spot, so the Gate above declines
    ''' and the objective gets its tick back.
    ''' </summary>
    Public Function PastEdge(pos As Vector2, headingRad As Single) As Object
        Dim me_ = BrainRadar.HULL
        Dim p As Vector2 = Nothing
        If Not sealedTo.TryGetValue(me_, p) Then Return Nothing
        Dim d = p - pos
        If d.Length < 0.5F Then Return Nothing
        Return CObj(wrap(CSng(Math.Atan2(d.X, d.Y)) - headingRad))
    End Function

    ''' <summary>
    ''' THE DEEPEST RAY OVER THE WHOLE SCAN, as a place short of what it hit.
    '''
    ''' Over ALL of it, not the forward hemisphere, and that is the point.
    ''' deeper_side() - the board's existing pick - only considers rays with
    ''' `front` set, and front is |angle| &lt;= 90 deg. But this rule only fires
    ''' when 160 degrees of that hemisphere is ALREADY SOLID, so asking it for a
    ''' heading is asking for the least-bad direction out of a set just proved to
    ''' be all wall. The way out of a pocket is past the beam edge, often behind,
    ''' and a forward-only measurement cannot say so.
    '''
    ''' SHORT OF THE HIT by a standoff, because the ray's own end is the
    ''' obstacle. A point two metres before it is open ground by construction -
    ''' the ray got there.
    ''' </summary>
    Private Function open_spot(hits As BrainRadar.Hit(), pos As Vector2,
                               headingRad As Single, ByRef spot As Vector2) As Boolean
        If hits Is Nothing Then Return False
        Dim best = -1.0F
        Dim bestA = 0.0F
        For Each hh In hits
            ' Our own chords are not walls for detection and they are not
            ' openings either - a ray that stops on one tells us nothing about
            ' the world, so it cannot be the deepest.
            If ours.Contains(hh.row * BrainNav.CellCols + hh.col) Then Continue For
            Dim reach = If(hh.found, hh.drive, BrainRadar.REACH_M)
            If reach > best Then
                best = reach
                bestA = hh.angle
            End If
        Next
        Dim stand = BrainTune.Get_("trapstand", 3.0F)
        If best < stand + 2.0F Then Return False
        Dim go = best - stand
        ' NOSE-RELATIVE TO WORLD. Hit.angle is "relative to the nose, radians" -
        ' its own comment - so it has to be turned into the world frame before it
        ' can name a place. Using it raw put the point wherever the tank would
        ' have been looking if it were facing north, which is a bug that would
        ' have read as the go-around driving somewhere arbitrary.
        Dim a = bestA + headingRad
        spot = pos + New Vector2(CSng(Math.Sin(a)), CSng(Math.Cos(a))) * go
        Return True
    End Function
    Private shader As BrainShader
    Private vao, vbo, verts As Integer

    ''' <summary>
    ''' ONE LOOK AT A CLOSED ARC. Called where the walk's verdict is known,
    ''' because "there is no path" is that verdict and nowhere else knows it.
    '''
    ''' Records; never writes. Returns True while the flag is up, so a caller
    ''' can log or gate on it without asking twice.
    ''' </summary>
    Public Function Look(hits As BrainRadar.Hit(), pos As Vector2,
                         headingRad As Single, noPath As Boolean) As Boolean
        If hits Is Nothing Then Return False

        ' A HELD CHORD IS NOT FORGOTTEN BECAUSE THE NEXT WALK IS OPTIMISTIC.
        '
        ' The first version cleared it the moment a walk found a path, and
        ' measured on road 28's trap that threw the finding away every time:
        ' the flag rose three separate times at (110.7, -307.5) and the chord
        ' was never written once, while the tank drove 3.8 km inside a 121 m
        ' box. Of course a later walk finds a path - it finds one into the same
        ' pocket, which is how the tank got there three times.
        '
        ' "the arc was closed and there was nowhere to go" is a fact about a
        ' PLACE. It stops being relevant when the hull is somewhere else, so
        ' distance is what clears it, not a change of mind.
        Dim me_ = BrainRadar.HULL
        Dim mine As Pending = Nothing
        held.TryGetValue(me_, mine)

        If mine IsNot Nothing AndAlso
           (pos - mine.sawFrom).Length > BrainTune.Get_("trapstale", 60.0F) Then
            LogThis("brain: hull {0} no-way-out chord dropped - {1:0} m from where it " &
                    "saw the dead end, so it is a different place now.",
                    me_, (pos - mine.sawFrom).Length)
            held.Remove(me_)
            mine = Nothing
        End If

        ' NO PATH IS HALF THE RULE. A closed arc with a way out is a wall the
        ' go-around is about to drive round, which is the system working - so
        ' no NEW chord is taken, but any chord already held stands.
        If Not noPath Then Return mine IsNot Nothing

        Dim arc = MathHelper.DegreesToRadians(ARC_DEG)
        Dim far_ = BrainTune.Get_("trapm", BrainRadar.REACH_M)
        Dim n = 0
        Dim loA = Single.MaxValue, hiA = Single.MinValue
        Dim loP As Vector2 = Nothing, hiP As Vector2 = Nothing

        For Each hh In hits
            If Math.Abs(hh.angle) > arc Then Continue For
            n += 1
            ' A RAY THAT FOUND NOTHING, OR FOUND IT A LONG WAY OFF, IS A GAP -
            ' and one gap is a way out, so the rule fails on the first of them.
            If Not hh.found OrElse hh.dist > far_ Then Return False
            ' OUR OWN CHORD IS NOT A WALL FOR THIS TEST - see `ours`.
            If ours.Contains(hh.row * BrainNav.CellCols + hh.col) Then Return False
            If hh.angle < loA Then
                loA = hh.angle
                loP = hh.at
            End If
            If hh.angle > hiA Then
                hiA = hh.angle
                hiP = hh.at
            End If
        Next

        ' A THIN SCAN IS NOT A CLOSED ARC. With 120 rays over 360 degrees the
        ' forward 160 should return about 53; a handful means the scan itself
        ' failed and every one of them happening to hit proves nothing.
        If n < CInt(BrainTune.Get_("trapmin", 20.0F)) Then Return False

        Dim first = mine Is Nothing
        If first Then
            mine = New Pending()
            held(me_) = mine
        End If
        mine.leftAt = loP
        mine.rightAt = hiP
        mine.sawFrom = pos
        mine.sawHeading = headingRad
        mine.haveOpen = open_spot(hits, pos, headingRad, mine.openAt)

        If first Then
            Flags += 1
            LogThis("brain: NO WAY OUT #{0} - hull {12}, {1} rays across {2:0} deg all " &
                    "blocked inside {3:0} m and the walk found nothing. At ({4:0.0}, " &
                    "{5:0.0}) facing {6:0} deg. Mouth from ({7:0.0}, {8:0.0}) to " &
                    "({9:0.0}, {10:0.0}), {11:0.0} m across. Chord held, NOT drawn - it " &
                    "goes down once the hull is clear of it.",
                    Flags, n, ARC_DEG * 2.0F, far_, pos.X, pos.Y,
                    MathHelper.RadiansToDegrees(headingRad),
                    loP.X, loP.Y, hiP.X, hiP.Y, (hiP - loP).Length, me_)
        End If
        Return True
    End Function

    ''' <summary>
    ''' THE HULL IS CLEAR - draw the chord now, if it is safe to.
    '''
    ''' Called when the tank leaves its backing state. That is the moment it is
    ''' outside the pocket it was recorded looking into, which is the only
    ''' moment the line can be drawn without the tank being on the wrong side
    ''' of it.
    ''' </summary>
    ''' <summary>Returns True only when cells were actually written, because
    ''' the caller has to throw away everything it decided against the old map
    ''' and must not do that on a refusal or with stamping switched off.</summary>
    Public Function Free(hits As BrainRadar.Hit(), pos As Vector2,
                        headingRad As Single, radius As Single) As Boolean
        Dim me_ = BrainRadar.HULL
        Dim mine As Pending = Nothing
        If Not held.TryGetValue(me_, mine) Then Return False
        held.Remove(me_)

        ' ---- THE LINE IS MEASURED FROM HERE, NOT FROM WHERE IT WAS SEEN ----
        '
        ' "draw the baked line after it backs up, not before and force it to re
        '  eval the hits" - the owner, 2026-09-25, and it is the better way
        ' round. The chord captured while nose-in is the mouth as seen from
        ' INSIDE, a couple of metres from the wall, where the two extreme rays
        ' are whatever happened to be either side of the hull. Backed out, the
        ' whole mouth is in view and its real edges can be measured.
        '
        ' The scan runs every tick unconditionally, so "re eval the hits" is
        ' simply using the ones from this tick. The remembered pair stays as the
        ' fallback for the case where the reverse has taken the hull so far out
        ' that nothing reads as blocked any more - a chord is still wanted then,
        ' and the view from inside is the only one there was.
        Dim leftAt = mine.leftAt, rightAt = mine.rightAt
        Dim fresh = span_now(hits, leftAt, rightAt)
        If mine.haveOpen Then
            ' And the way out is re-measured too, for the same reason: from out
            ' here the open side is a fact about the world rather than a guess
            ' made from inside a pocket.
            Dim spot As Vector2 = Nothing
            If open_spot(hits, pos, headingRad, spot) Then mine.openAt = spot
        End If
        ' ---- IS THE CHORD AHEAD OF THE NOSE? ------------------------------
        '
        ' DISTANCE WAS THE WRONG TEST and it had stopped the feature working
        ' entirely. The guard asked "is the hull far enough from the line" and
        ' wanted radius + 4 m; on the owner's scenario the answer was 2.3 to
        ' 5.2 m every time, so seventeen dead ends were found and not one chord
        ' was ever drawn.
        '
        ' The reason is geometric and it is not a tuning problem. The chord runs
        ' between the extreme blocked rays of a 160 degree arc, so its ends sit
        ' at about 80 degrees left and right - very nearly BESIDE the hull. A
        ' line between two points either side of you passes through you. No
        ' standoff makes that safe, and a bigger one only refuses more.
        '
        ' What actually distinguishes a safe chord from a self-enclosing one is
        ' which SIDE of the hull it lies on. A line ahead of the nose seals what
        ' is in front; the tank reverses away from it and is free. A line beside
        ' or behind is the MarkBlocked failure - the tank writes a wall across
        ' its own exit - and that is what was reverted twice and deleted.
        '
        ' So: the chord's midpoint must be AHEAD, and the hull must still clear
        ' the line by its own footprint so the cell it stands in is not the cell
        ' being blocked. Two cheap tests, each answering its own question.
        Dim nose = New Vector2(CSng(Math.Sin(headingRad)), CSng(Math.Cos(headingRad)))
        Dim mid = (leftAt + rightAt) * 0.5F
        Dim ahead = Vector2.Dot(mid - pos, nose)
        Dim d = seg_dist(pos, leftAt, rightAt)
        Dim clear_ = radius + BrainTune.Get_("trapclear", 0.5F)

        If ahead <= BrainTune.Get_("trapahead", 1.0F) OrElse d < clear_ Then
            ' REFUSED, AND SAID OUT LOUD WITH BOTH NUMBERS, because they fail for
            ' different reasons and a single figure cannot say which.
            LogThis("brain: hull {0} no-way-out chord REFUSED - midpoint {1:0.0} m " &
                    "{2} the nose, hull {3:0.0} m off the line (needs {4:0.0}). {5}",
                    me_, Math.Abs(ahead), If(ahead > 0.0F, "ahead of", "BEHIND"),
                    d, clear_,
                    If(ahead <= 0.0F,
                       "A line behind the nose walls the tank in - that is what " &
                       "MarkBlocked was deleted for.",
                       "Too close to write without blocking the cell we stand in."))
            Return False
        End If

        ' ON BY DEFAULT NOW THAT IT IS VISIBLE. It was off while the flag was
        ' only a log line, because a rule that writes to the map unseen is a
        ' rule nobody can check. The boxes are the check - "no new blockers I
        ' dont think.. I can tell" was the owner unable to tell, which is a
        ' fair complaint about an invisible effect. tune=trap=0 turns it off.
        If Not BrainTune.On_("trap", True) Then
            LogThis("brain: hull {1} no-way-out chord ready and CLEAR ({0:0.0} m off " &
                    "the hull), not drawn - tune=trap=1 turns the stamping on.", d, me_)
            Return False
        End If

        Dim cells = stamp_line(leftAt, rightAt)
        LogThis("brain: hull {0} chord measured {1} - ({2:0.0}, {3:0.0}) to ({4:0.0}, " &
                "{5:0.0}), {6:0.0} m across",
                me_, If(fresh, "FROM HERE, backed out", "from the remembered view inside"),
                leftAt.X, leftAt.Y, rightAt.X, rightAt.Y, (rightAt - leftAt).Length)
        Stamped += cells
        LogThis("brain: hull {7} no-way-out chord DRAWN - {0} cell(s) from ({1:0.0}, " &
                "{2:0.0}) to ({3:0.0}, {4:0.0}), hull {5:0.0} m clear. {6} cell(s) " &
                "stamped this run; Reset re-reads the bake and they go.",
                cells, leftAt.X, leftAt.Y, rightAt.X, rightAt.Y, d, Stamped, me_)
        If cells > 0 Then
            sealedAt(me_) = pos
            sealedFor(me_) = 0
            If mine.haveOpen Then
                sealedTo(me_) = mine.openAt
                ' HOW FAR, AND WHICH WAY. Without this the go-around is a bearing
                ' in a log and there is no way to tell a real way out from a spot
                ' six metres away still inside the pocket - which is the
                ' difference between this rule working and it looking like it
                ' works.
                Dim toSpot = mine.openAt - pos
                LogThis("brain: hull {0} go-around point ({1:0.0}, {2:0.0}) - {3:0.0} m " &
                        "off at {4:0} deg from the nose, {5:0.0} m from where the chord " &
                        "went down.",
                        me_, mine.openAt.X, mine.openAt.Y, toSpot.Length,
                        MathHelper.RadiansToDegrees(wrap(
                            CSng(Math.Atan2(toSpot.X, toSpot.Y)) - mine.sawHeading)),
                        (mine.openAt - mine.sawFrom).Length)
            Else
                sealedTo.Remove(me_)
                LogThis("brain: hull {0} sealed a dead end and has NOWHERE to go - no ray " &
                        "in the whole scan reaches past the standoff. Backing is the only " &
                        "move left.", me_)
            End If
        End If
        Return cells > 0
    End Function

    ''' <summary>Drop any held chord. Called by Reset, so a flag raised by the
    ''' run being thrown away cannot land on the next one.</summary>
    Public Sub Forget()
        held.Clear()
        marks.Clear()
        ours.Clear()
        sealedAt.Clear()
        sealedTo.Clear()
        sealedFor.Clear()
        verts = 0
        Stamped = 0
        Flags = 0
    End Sub

    ''' <summary>
    ''' THE BLOCKED SPAN IN FRONT, AS SEEN RIGHT NOW.
    '''
    ''' The extremes of what is blocked across the forward arc, from wherever the
    ''' hull is standing when it is asked. Not the same question Look asks: Look
    ''' needs EVERY ray blocked before it will believe in a dead end, because
    ''' one gap is a way out. This only needs the edges of what IS blocked, so it
    ''' works from outside the pocket where most of the arc is open sky.
    '''
    ''' False when there is too little blocked to call it a mouth, which leaves
    ''' the caller with the view it remembered from inside.
    ''' </summary>
    Private Function span_now(hits As BrainRadar.Hit(), ByRef loP As Vector2,
                              ByRef hiP As Vector2) As Boolean
        If hits Is Nothing Then Return False
        Dim arc = MathHelper.DegreesToRadians(ARC_DEG)
        Dim far_ = BrainTune.Get_("trapm", BrainRadar.REACH_M)
        Dim loA = Single.MaxValue, hiA = Single.MinValue
        Dim n = 0
        For Each hh In hits
            If Math.Abs(hh.angle) > arc Then Continue For
            If Not hh.found OrElse hh.dist > far_ Then Continue For
            If ours.Contains(hh.row * BrainNav.CellCols + hh.col) Then Continue For
            n += 1
            If hh.angle < loA Then
                loA = hh.angle
                loP = hh.at
            End If
            If hh.angle > hiA Then
                hiA = hh.angle
                hiP = hh.at
            End If
        Next
        If n < CInt(BrainTune.Get_("trapspan", 6.0F)) Then Return False
        Return (hiP - loP).Length > BrainTune.Get_("trapwide", 4.0F)
    End Function

    ''' <summary>
    ''' Stamp every cell the line passes through, at half-cell steps.
    '''
    ''' Half a cell rather than a full one because a line crossing a grid
    ''' diagonally skips cells at full-cell steps and leaves a dotted wall with
    ''' gaps the planner will happily thread. Same reason BrainCourse steps at
    ''' half a cell.
    ''' </summary>
    Private Function stamp_line(a As Vector2, b As Vector2) As Integer
        Dim cell = Math.Max(0.05F, BrainNav.CellSize)
        Dim len_ = (b - a).Length
        Dim steps = Math.Max(1, CInt(Math.Ceiling(len_ / (cell * 0.5F))))
        Dim got = 0
        For s = 0 To steps
            Dim p = a + (b - a) * (CSng(s) / steps)
            ' ONLY THE CELLS THAT WERE ACTUALLY NEW. StampBlocked returns False
            ' for a cell the bake already blocked, and at quarter-metre steps
            ' most samples land in a cell a previous sample already took - so
            ' this list is the line of blockers that did not exist before, which
            ' is exactly what is worth drawing.
            Dim c = BrainNav.ColOf(p.X), rr = BrainNav.RowOf(p.Y)
            If BrainNav.StampBlocked(c, rr) Then
                got += 1
                marks.Add(p)
                ours.Add(rr * BrainNav.CellCols + c)
            End If
        Next
        verts = 0
        Return got
    End Function

    ''' <summary>
    ''' A 3 m BOX ON EVERY CELL THIS RULE ADDED.
    '''
    ''' "draw a box at each point on the new line of blockers at 3m tall" - the
    ''' owner, 2026-09-25, after being unable to tell whether anything had been
    ''' stamped. He was right that nothing had, and right that he could not
    ''' tell: setting bit 0 of a grid cell changes what the planner believes and
    ''' nothing on screen at all.
    '''
    ''' Cell-footprint wireframe boxes rather than solid wafers, because these
    ''' are a DIAGNOSTIC - you want to see the terrain through them and see
    ''' exactly which cells they sit on. Cyan, and nothing else in this app is
    ''' cyan: the roads are green, the course red, the trail pink and yellow,
    ''' the walk blue and white and red, the start posts orange and the road
    ''' ends magenta. A colour of its own means "the brain put this here", which
    ''' is a different claim from anything the bake says.
    ''' </summary>
    Public Sub Init()
        shader = New BrainShader("line")
        vao = GL.GenVertexArray()
        vbo = GL.GenBuffer()
    End Sub

    Public Sub Draw(ByRef viewProj As Matrix4)
        If marks.Count = 0 Then Return
        If shader Is Nothing OrElse Not shader.Ready Then Return
        If verts = 0 Then build()
        If verts = 0 Then Return
        shader.Use()
        shader.SetMat4("viewProj", viewProj)
        shader.SetVec3("colour", New Vector3(0.15F, 0.95F, 1.0F))
        GL.LineWidth(2.0F)
        GL.BindVertexArray(vao)
        GL.DrawArrays(PrimitiveType.Lines, 0, verts)
        GL.BindVertexArray(0)
        GL.LineWidth(1.0F)
    End Sub

    Private Sub build()
        Dim v As New List(Of Single)
        Dim h = BrainTune.Get_("traptall", 3.0F)
        Dim r = Math.Max(0.05F, BrainNav.CellSize) * 0.5F
        For Each p In marks
            box(v, p, r, BrainNav.Ground(p.X, p.Y), h)
        Next
        Dim arr = v.ToArray()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * 4, arr, BufferUsageHint.StaticDraw)
        GL.EnableVertexAttribArray(0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, False, 12, 0)
        GL.BindVertexArray(0)
        verts = arr.Length \ 3
    End Sub

    ''' <summary>Twelve edges: the four uprights and the two squares. Written
    ''' out rather than looped because a box is four corners and reading it as
    ''' four corners is easier than reading a loop that indexes them.</summary>
    Private Sub box(v As List(Of Single), p As Vector2, r As Single,
                    y0 As Single, tall As Single)
        Dim y1 = y0 + tall
        Dim xs = New Single() {p.X - r, p.X + r, p.X + r, p.X - r}
        Dim zs = New Single() {p.Y - r, p.Y - r, p.Y + r, p.Y + r}
        For i = 0 To 3
            Dim j = (i + 1) Mod 4
            ' bottom, top, and the upright at this corner
            seg(v, xs(i), y0, zs(i), xs(j), y0, zs(j))
            seg(v, xs(i), y1, zs(i), xs(j), y1, zs(j))
            seg(v, xs(i), y0, zs(i), xs(i), y1, zs(i))
        Next
    End Sub

    Private Sub seg(v As List(Of Single), ax As Single, ay As Single, az As Single,
                    bx As Single, by_ As Single, bz As Single)
        v.Add(ax) : v.Add(ay) : v.Add(az)
        v.Add(bx) : v.Add(by_) : v.Add(bz)
    End Sub

    ''' <summary>Distance from a point to a segment. The guard's whole
    ''' arithmetic: a chord that is far from the hull in the middle can still
    ''' pass close to it at one end, so the nearest point on the segment is the
    ''' only honest measure.</summary>
    ''' <summary>Into -pi..pi. The go-around log printed "-201 deg" for a
    ''' bearing of +159 because it subtracted two angles and showed the result -
    ''' the returned bearing was always wrapped, so this was a display fault
    ''' only, but a reader cannot tell those apart and -201 reads as broken.
    ''' One wrap now, used by both.</summary>
    Private Function wrap(a As Single) As Single
        Dim x = a
        While x > Math.PI
            x -= CSng(Math.PI * 2.0)
        End While
        While x < -Math.PI
            x += CSng(Math.PI * 2.0)
        End While
        Return x
    End Function

    Private Function seg_dist(p As Vector2, a As Vector2, b As Vector2) As Single
        Dim ab = b - a
        Dim L2 = ab.LengthSquared
        If L2 < 0.000001F Then Return (p - a).Length
        Dim t = Math.Max(0.0F, Math.Min(1.0F, Vector2.Dot(p - a, ab) / L2))
        Return ((a + ab * t) - p).Length
    End Function

End Module

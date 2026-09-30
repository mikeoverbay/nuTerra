Imports OpenTK.Mathematics

''' <summary>
''' CAN WE GET THERE AT ALL? The one question the brain has never asked.
'''
''' "Can you tell me why we can not see that the entire path is blocked or can we
''' tell?" - the owner, 2026-09-28.
'''
''' We can tell, exactly, and the reason nothing does is that EVERY TEST THE
''' BRAIN OWNS IS LOCAL. The radar reaches REACH_M, forty metres. The corridor,
''' the plank, the walk and its candidate ends all work inside that. The
''' no-way-out rule fires on a closed arc within it. So from the brain's point of
''' view "blocked ahead" and "blocked everywhere" produce the identical reading,
''' and no amount of looking harder at forty metres can separate them. It is not
''' that the information is hidden; it is that the question was never asked.
'''
''' BrainNav already holds the whole map - 2800 x 2800 cells at half a metre,
''' bit 0 blocked - so a flood fill from the GOAL answers it outright.
'''
''' FROM THE GOAL, NOT FROM THE HULL, and that is the whole value. A fill from
''' the hull answers one yes-or-no and is thrown away. A fill from the goal
''' leaves a DISTANCE FIELD: every free cell on the map labelled with how far it
''' is from the goal THROUGH THE WALLS rather than across them. Then
''' "is the path blocked" is one lookup, and the tank gets something it has never
''' had - a true distance to the goal. Straight-line range is what sends it into
''' dead ends in the first place, because a pocket mouth always scores better
''' than going round.
'''
''' THE HULL'S WIDTH IS BAKED IN BY DILATION. A cell counts as passable only if
''' nothing within the driving radius is blocked, and that is done once, as two
''' separable sliding-window passes over rows then columns, rather than a box
''' test per cell. Nine-by-nine per cell over 7.8 million cells is 635 million
''' reads; two sliding passes is about 16 million. A SQUARE footprint, which is
''' not an approximation here - BrainNav.Standable tests an axis-aligned box too,
''' so this agrees with the test the sim actually refuses moves by.
'''
''' ON DEMAND, NEVER PER TICK. The fill costs tens of milliseconds and about
''' 23 MB, so it runs when the goal moves or when someone asks - not sixty times
''' a second. A previous 31 MB per-cell cache was deleted from this project for
''' being exactly that kind of waste; this is different in that it is built once
''' per question and answers something nothing else can.
'''
''' FOUR-CONNECTED. Reachability is identical either way; the distances run up to
''' 41% long on diagonals. That is fine for "can we get there" and for ranking
''' two candidates, and would not be fine for quoting a route length, so it is
''' not quoted as one.
''' </summary>
Module BrainReach

    ''' <summary>
    ''' ONE FIELD PER GOAL, KEPT. Five hulls on five roads want five answers,
    ''' and each of them moves its goal along its road as it arrives.
    '''
    ''' A single field cannot serve that: report_reach built one at Start while
    ''' BrainGoal.Target is written in six places, so every hull past its first
    ''' waypoint was reading a field for somebody else's destination. Correct
    ''' with one pinned goal, which is why the sweeps never caught it.
    ''' </summary>
    Private Class Field
        ''' <summary>Nothing when this goal cannot be flooded at all - a
        ''' negative result worth keeping, or every tick retries it.</summary>
        Public dist() As UShort
        Public reached As Integer
        Public at As Vector2
    End Class

    ''' <summary>
    ''' EVERYTHING THAT DEPENDS ON THE HULL'S WIDTH AND NOT ON THE GOAL.
    '''
    ''' Which cells a hull fits in is a function of its radius alone, so the
    ''' dilated, coarsened passable set is built once per width and every goal
    ''' floods over the same copy. That is what makes a field per waypoint
    ''' affordable: of the 205 ms a field used to cost, almost none was the
    ''' flood - fit_mask runs four sliding passes over the FINE grid, about 31
    ''' million cell reads, against 490,000 cells touched once by the BFS.
    ''' Measured after the split: 204 ms for the set, 11 ms for a flood.
    '''
    ''' A LEVEL PER WIDTH, not one slot. Five tanks are five different radii, and
    ''' with a single slot each hull's tick invalidated the last one's set and
    ''' took the field cache down with it - measured in a five-a-side match as
    ''' 410 dilations and 410 floods in ninety seconds, with zero cache hits.
    ''' The fields hang off the level because a field flooded over one passable
    ''' set says nothing about another.
    ''' </summary>
    Private Class Level
        Public pass_() As Boolean
        ''' <summary>The bucket's radius, which is the requested one rounded
        ''' UP - never down. Rounding down would claim a hull fits where it does
        ''' not, and this answer is used to drive.</summary>
        Public r As Single
        Public fields As New Dictionary(Of Integer, Field)
        Public order As New List(Of Integer)
    End Class

    Private levels As New Dictionary(Of Integer, Level)
    Private curL As Level = Nothing
    Private cur As Field = Nothing

    Private w, h As Integer
    Private fw, fh, grain As Integer
    Private cell As Single

    ''' <summary>Cells the selected field could touch from its goal.</summary>
    Public Reached As Integer = 0
    Public Ms As Long = 0
    ''' <summary>Floods paid for, lookups served from the cache, and passable
    ''' sets built. A build count that climbs with the frame counter means the
    ''' key is wrong - which is how the five-hull thrash above was caught.
    ''' </summary>
    Public Builds As Integer = 0
    Public Hits As Integer = 0
    Public Sets As Integer = 0
    ''' <summary>WHY A LOOKUP CAME BACK WITH NOTHING, counted by reason. A -1
    ''' from At() is indistinguishable from "no route" without these, and
    ''' guessing which branch fired cost half an hour once.</summary>
    Public NoNav As Integer = 0
    Public NoGrid As Integer = 0
    Public OffMap As Integer = 0
    Private sayNoNav, sayNoGrid, sayOffMap As Integer

    Public ReadOnly Property Ready As Boolean
        Get
            Return cur IsNot Nothing AndAlso cur.dist IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' METRES TO THE SELECTED GOAL THROUGH THE WALLS, or -1 where there is no
    ''' route. Reads whichever field Use last selected.
    ''' </summary>
    Public Function At(p As Vector2) As Single
        If cur Is Nothing OrElse cur.dist Is Nothing Then Return -1.0F
        Dim fc = BrainNav.ColOf(p.X), fr = BrainNav.RowOf(p.Y)
        If fc < 0 OrElse fr < 0 OrElse fc >= fw OrElse fr >= fh Then Return -1.0F
        Dim c = fc \ grain, r = fr \ grain
        If c < 0 OrElse r < 0 OrElse c >= w OrElse r >= h Then Return -1.0F
        Dim d = cur.dist(r * w + c)
        If d = UShort.MaxValue Then Return -1.0F
        Return d * cell * grain
    End Function

    ''' <summary>True when a hull standing here has no route to the selected
    ''' goal at all.</summary>
    Public Function Trapped(p As Vector2) As Boolean
        Return Ready AndAlso At(p) < 0.0F
    End Function

    ''' <summary>
    ''' SELECT THE FIELD FOR THIS HULL'S GOAL, flooding it if we have not seen it.
    '''
    ''' Called once per hull per tick, before anything reads At - so the answer
    ''' belongs to the hull that asked rather than to whoever flooded first.
    '''
    ''' Keyed by COARSE CELL, not by position: a goal that shuffles half a metre
    ''' is the same goal, and keying on the Vector2 would flood afresh on every
    ''' jitter.
    ''' </summary>
    Public Function Use(goal As Vector2, radius As Single) As Boolean
        cur = Nothing
        If Not BrainNav.Ready Then
            NoNav += 1
            If sayNoNav < 2 Then
                sayNoNav += 1
                LogThis("brain: reach - asked for a field before BrainNav was ready.")
            End If
            Return False
        End If
        If Not ensure_grid() Then
            NoGrid += 1
            If sayNoGrid < 2 Then
                sayNoGrid += 1
                LogThis("brain: reach - no grid: {0} x {1} cells.",
                        BrainNav.CellCols, BrainNav.CellRows)
            End If
            Return False
        End If
        curL = ensure_level(radius)
        If curL Is Nothing Then Return False

        Dim gc = BrainNav.ColOf(goal.X) \ grain
        Dim gr = BrainNav.RowOf(goal.Y) \ grain
        If gc < 0 OrElse gr < 0 OrElse gc >= w OrElse gr >= h Then
            OffMap += 1
            If sayOffMap < 3 Then
                sayOffMap += 1
                LogThis("brain: reach - the goal ({0:0.0}, {1:0.0}) is off the grid: " &
                        "coarse cell {2},{3} of {4} x {5}.",
                        goal.X, goal.Y, gc, gr, w, h)
            End If
            Return False
        End If
        Dim key = gr * w + gc

        Dim f As Field = Nothing
        If curL.fields.TryGetValue(key, f) Then
            Hits += 1
            cur = f
            If f.dist Is Nothing Then Return False
            Reached = f.reached
            Return True
        End If

        f = flood(curL, key, goal)
        keep(curL, key, f)
        cur = f
        If f.dist Is Nothing Then Return False
        Reached = f.reached
        Return True
    End Function

    ''' <summary>The old name, kept because report_reach calls it and means
    ''' exactly this.</summary>
    Public Function Build(goal As Vector2, radius As Single) As Boolean
        Return Use(goal, radius)
    End Function

    ''' <summary>Grid geometry, and a wipe if the map changed under us.</summary>
    Private Function ensure_grid() As Boolean
        Dim nfw = BrainNav.CellCols, nfh = BrainNav.CellRows
        If nfw <= 0 OrElse nfh <= 0 Then Return False
        Dim g = Math.Max(1, CInt(BrainTune.Get_("reachstep", 4.0F)))
        If nfw = fw AndAlso nfh = fh AndAlso g = grain Then Return True
        ' A different world. Everything flooded over the old one is about it.
        levels.Clear()
        curL = Nothing
        cur = Nothing
        fw = nfw
        fh = nfh
        grain = g
        w = (fw + grain - 1) \ grain
        h = (fh + grain - 1) \ grain
        cell = Math.Max(0.05F, BrainNav.CellSize)
        Return True
    End Function

    ''' <summary>
    ''' The level for this hull width, built once.
    '''
    ''' BUCKETED, because five tanks differ by centimetres and a dilation each
    ''' is 204 ms each. Rounded UP to the bucket so the answer stays
    ''' conservative - a hull is never told it fits somewhere narrower than it
    ''' is.
    ''' </summary>
    Private Function ensure_level(radius As Single) As Level
        Dim q = Math.Max(0.05F, BrainTune.Get_("reachbucket", 0.25F))
        Dim bucket = Math.Max(1, CInt(Math.Ceiling(radius / q)))
        Dim lv As Level = Nothing
        If levels.TryGetValue(bucket, lv) Then Return lv

        Dim r = bucket * q
        Dim sw = Stopwatch.StartNew()

        ' ---- COARSE, BECAUSE THIS FIELD IS STRATEGIC --------------------
        '
        ' The flood answers "which way is the goal, allowing for walls". The rays
        ' answer "what is in front of me right now". Only the second needs
        ' half-metre resolution.
        '
        ' THE DILATION STAYS FINE, and that is what makes the sampling safe. The
        ' fine mask is already grown by the hull's half-width, so a wall one cell
        ' thick is about four metres thick in the mask - wider than the two-metre
        ' sample spacing. Sampling the centre of each coarse block therefore
        ' cannot step over a wall the hull would hit; it is asking "does the hull
        ' fit HERE", which is the right question at any resolution.
        Dim fine = fit_mask(r)
        Dim n = w * h
        lv = New Level With {.r = r}
        ReDim lv.pass_(n - 1)
        For rr = 0 To h - 1
            Dim fr = Math.Min(fh - 1, rr * grain + grain \ 2)
            For c = 0 To w - 1
                Dim fc = Math.Min(fw - 1, c * grain + grain \ 2)
                lv.pass_(rr * w + c) = fine(fr * fw + fc)
            Next
        Next
        levels(bucket) = lv
        Sets += 1
        LogThis("brain: reach - passable set for a hull {0:0.0} m wide built in " &
                "{1} ms, {2} x {3} cells at {4:0.0} m. {5} width(s) held; every " &
                "goal at this width floods over this copy.",
                r * 2.0F, sw.ElapsedMilliseconds, w, h, cell * grain, levels.Count)
        Return lv
    End Function

    ''' <summary>Flood out from one goal. The only per-goal cost.</summary>
    Private Function flood(lv As Level, key As Integer, goal As Vector2) As Field
        Dim f As New Field With {.at = goal}
        If Not lv.pass_(key) Then
            ' THE GOAL ITSELF IS INSIDE SOMETHING. Said plainly, because every
            ' hull will then report "no route" and the cause is the goal, not the
            ' map and not the driving. Cached as a failure so it is said once
            ' rather than every tick.
            LogThis("brain: reach - THE GOAL AT ({0:0.0}, {1:0.0}) IS NOT PASSABLE for a " &
                    "hull {2:0.0} m wide. Every route will read as blocked because there " &
                    "is nowhere to arrive.", goal.X, goal.Y, lv.r * 2.0F)
            Return f
        End If

        Dim sw = Stopwatch.StartNew()
        Dim n = w * h
        Dim d(n - 1) As UShort
        For i = 0 To n - 1
            d(i) = UShort.MaxValue
        Next

        Dim q As New Queue(Of Integer)
        d(key) = 0
        q.Enqueue(key)
        Dim seen = 1
        While q.Count > 0
            Dim i = q.Dequeue()
            Dim dv = d(i)
            If dv >= UShort.MaxValue - 1 Then Continue While
            Dim nd = CUShort(dv + 1US)
            Dim c = i Mod w, r = i \ w
            If c > 0 Then step_(lv, d, i - 1, nd, q, seen)
            If c < w - 1 Then step_(lv, d, i + 1, nd, q, seen)
            If r > 0 Then step_(lv, d, i - w, nd, q, seen)
            If r < h - 1 Then step_(lv, d, i + w, nd, q, seen)
        End While

        f.dist = d
        f.reached = seen
        Builds += 1
        Ms = sw.ElapsedMilliseconds
        LogThis("brain: reach - flooded from ({0:0.0}, {1:0.0}) in {2} ms. " &
                "{3:N0} of {4:N0} cells reach it, {5:0}% of the map. " &
                "{6} field(s) held at this width, {7} flood(s) paid for, " &
                "{8} lookup(s) free, {9} set(s) built.",
                goal.X, goal.Y, Ms, seen, n, 100.0 * seen / n,
                lv.fields.Count + 1, Builds, Hits, Sets)
        Return f
    End Function

    Private Sub step_(lv As Level, d() As UShort, j As Integer, nd As UShort,
                      q As Queue(Of Integer), ByRef seen As Integer)
        If Not lv.pass_(j) Then Return
        If d(j) <= nd Then Return
        d(j) = nd
        seen += 1
        q.Enqueue(j)
    End Sub

    ''' <summary>
    ''' Hold the last few fields at this width, oldest out first.
    '''
    ''' A cap rather than everything, because a road has many waypoints and
    ''' nothing needs the field for one we left behind. At about 1 MB each the cap
    ''' is about tidiness rather than memory - the 0.5 m grid would have made each
    ''' one 16 MB and this cache impossible, which is what the coarsening bought.
    ''' </summary>
    Private Sub keep(lv As Level, key As Integer, f As Field)
        lv.fields(key) = f
        lv.order.Add(key)
        Dim cap = Math.Max(2, CInt(BrainTune.Get_("reachkeep", 12.0F)))
        While lv.order.Count > cap
            Dim old = lv.order(0)
            lv.order.RemoveAt(0)
            ' Only if it has not been asked for again since.
            If Not lv.order.Contains(old) Then lv.fields.Remove(old)
        End While
    End Sub

    ''' <summary>
    ''' Cells a hull of this radius fits in, by dilating the blocked set.
    '''
    ''' TWO SEPARABLE PASSES. A cell is blocked-for-the-hull if any cell within r
    ''' in its ROW is blocked, then if any within r in its COLUMN is. Composing
    ''' those gives the square footprint, at 2n reads instead of the (2r+1)^2 a
    ''' box test per cell would cost - on this map, 16 million against 635
    ''' million.
    ''' </summary>
    Private Function fit_mask(radius As Single) As Boolean()
        Dim n = fw * fh
        Dim r = Math.Max(0, CInt(Math.Ceiling(radius / cell)))
        Dim rowBad(n - 1) As Boolean
        Dim bad(n - 1) As Boolean

        ' rows
        For y = 0 To fh - 1
            Dim base_ = y * fw
            Dim run = 0
            ' one forward sweep carrying "how many cells since the last block"
            For x = 0 To fw - 1
                If BrainNav.BlockedCell(x, y) Then run = 0 Else run += 1
                rowBad(base_ + x) = (run <= r)
            Next
            run = 0
            For x = fw - 1 To 0 Step -1
                If BrainNav.BlockedCell(x, y) Then run = 0 Else run += 1
                If run <= r Then rowBad(base_ + x) = True
            Next
        Next

        ' columns, over the row result
        For x = 0 To fw - 1
            Dim run = 0
            For y = 0 To fh - 1
                If rowBad(y * fw + x) Then run = 0 Else run += 1
                bad(y * fw + x) = (run <= r)
            Next
            run = 0
            For y = fh - 1 To 0 Step -1
                If rowBad(y * fw + x) Then run = 0 Else run += 1
                If run <= r Then bad(y * fw + x) = True
            Next
        Next

        Dim pass(n - 1) As Boolean
        For i = 0 To n - 1
            pass(i) = Not bad(i)
        Next
        Return pass
    End Function

    ''' <summary>Drop every field. The passable set survives - it does not
    ''' depend on any goal, so the next Use pays for a flood and not a
    ''' dilation.</summary>
    ''' <summary>Drop every flooded field. The passable sets survive - they
    ''' depend on the hull's width and not on any goal, so the next Use pays
    ''' for a flood and not a dilation.</summary>
    Public Sub Forget()
        For Each lv In levels.Values
            lv.fields.Clear()
            lv.order.Clear()
        Next
        cur = Nothing
        Reached = 0
    End Sub

End Module

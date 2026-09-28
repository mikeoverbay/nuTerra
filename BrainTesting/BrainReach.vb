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

    Private dist() As UShort
    Private w, h As Integer
    Private cell As Single
    Private builtFor As Vector2
    Private have As Boolean = False

    ''' <summary>Cells the fill could touch from the goal. The size of the world
    ''' that is actually connected to it.</summary>
    Public Reached As Integer = 0
    Public Ms As Long = 0

    Public ReadOnly Property Ready As Boolean
        Get
            Return have
        End Get
    End Property

    ''' <summary>
    ''' METRES TO THE GOAL THROUGH THE WALLS, or -1 where there is no route.
    '''
    ''' -1 is the answer to the owner's question. Not "far", not "blocked ahead":
    ''' no route exists from this cell to that goal, for a hull this wide.
    ''' </summary>
    Public Function At(p As Vector2) As Single
        If Not have Then Return -1.0F
        Dim c = BrainNav.ColOf(p.X), r = BrainNav.RowOf(p.Y)
        If c < 0 OrElse r < 0 OrElse c >= w OrElse r >= h Then Return -1.0F
        Dim d = dist(r * w + c)
        If d = UShort.MaxValue Then Return -1.0F
        Return d * cell
    End Function

    ''' <summary>True when a hull standing here has no route to the goal at
    ''' all.</summary>
    Public Function Trapped(p As Vector2) As Boolean
        Return have AndAlso At(p) < 0.0F
    End Function

    ''' <summary>
    ''' Build the field for a goal, unless it is already built for that goal.
    '''
    ''' The guard is a cell, not an epsilon: a goal that shuffles half a metre
    ''' does not change which cells can reach it, and rebuilding on every jitter
    ''' would put a 50 ms fill inside the frame loop by accident.
    ''' </summary>
    Public Function Build(goal As Vector2, radius As Single) As Boolean
        If Not BrainNav.Ready Then Return False
        If have AndAlso (goal - builtFor).Length < BrainNav.CellSize Then Return True

        Dim sw = Stopwatch.StartNew()
        w = BrainNav.CellCols
        h = BrainNav.CellRows
        cell = Math.Max(0.05F, BrainNav.CellSize)
        If w <= 0 OrElse h <= 0 Then Return False

        Dim n = w * h
        Dim pass = fit_mask(radius)
        If dist Is Nothing OrElse dist.Length <> n Then ReDim dist(n - 1)
        For i = 0 To n - 1
            dist(i) = UShort.MaxValue
        Next

        Dim gc = BrainNav.ColOf(goal.X), gr = BrainNav.RowOf(goal.Y)
        If gc < 0 OrElse gr < 0 OrElse gc >= w OrElse gr >= h Then
            LogThis("brain: reach - the goal is off the map, nothing to fill")
            have = False
            Return False
        End If

        Dim start_ = gr * w + gc
        If Not pass(start_) Then
            ' THE GOAL ITSELF IS INSIDE SOMETHING. Said plainly, because every
            ' hull will then report "no route" and the cause is the goal, not the
            ' map and not the driving.
            LogThis("brain: reach - THE GOAL AT ({0:0.0}, {1:0.0}) IS NOT PASSABLE for a " &
                    "hull {2:0.0} m wide. Every route will read as blocked because there " &
                    "is nowhere to arrive.", goal.X, goal.Y, radius * 2.0F)
            have = False
            Return False
        End If

        ' ---- the fill -------------------------------------------------------
        Dim q As New Queue(Of Integer)
        dist(start_) = 0
        q.Enqueue(start_)
        Dim seen = 1
        While q.Count > 0
            Dim i = q.Dequeue()
            Dim d = dist(i)
            If d >= UShort.MaxValue - 1 Then Continue While
            Dim nd = CUShort(d + 1US)
            Dim c = i Mod w, r = i \ w
            If c > 0 Then step_(i - 1, nd, pass, q, seen)
            If c < w - 1 Then step_(i + 1, nd, pass, q, seen)
            If r > 0 Then step_(i - w, nd, pass, q, seen)
            If r < h - 1 Then step_(i + w, nd, pass, q, seen)
        End While

        Reached = seen
        builtFor = goal
        have = True
        Ms = sw.ElapsedMilliseconds
        LogThis("brain: reach - filled from the goal at ({0:0.0}, {1:0.0}) in {2} ms. " &
                "{3:N0} of {4:N0} cells can reach it, {5:0}% of the map, for a hull " &
                "{6:0.0} m wide.",
                goal.X, goal.Y, Ms, seen, n, 100.0 * seen / n, radius * 2.0F)
        Return True
    End Function

    Private Sub step_(j As Integer, nd As UShort, pass As Boolean(),
                      q As Queue(Of Integer), ByRef seen As Integer)
        If Not pass(j) Then Return
        If dist(j) <= nd Then Return
        dist(j) = nd
        seen += 1
        q.Enqueue(j)
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
        Dim n = w * h
        Dim r = Math.Max(0, CInt(Math.Ceiling(radius / cell)))
        Dim rowBad(n - 1) As Boolean
        Dim bad(n - 1) As Boolean

        ' rows
        For y = 0 To h - 1
            Dim base_ = y * w
            Dim run = 0
            ' one forward sweep carrying "how many cells since the last block"
            For x = 0 To w - 1
                If BrainNav.BlockedCell(x, y) Then run = 0 Else run += 1
                rowBad(base_ + x) = (run <= r)
            Next
            run = 0
            For x = w - 1 To 0 Step -1
                If BrainNav.BlockedCell(x, y) Then run = 0 Else run += 1
                If run <= r Then rowBad(base_ + x) = True
            Next
        Next

        ' columns, over the row result
        For x = 0 To w - 1
            Dim run = 0
            For y = 0 To h - 1
                If rowBad(y * w + x) Then run = 0 Else run += 1
                bad(y * w + x) = (run <= r)
            Next
            run = 0
            For y = h - 1 To 0 Step -1
                If rowBad(y * w + x) Then run = 0 Else run += 1
                If run <= r Then bad(y * w + x) = True
            Next
        Next

        Dim pass(n - 1) As Boolean
        For i = 0 To n - 1
            pass(i) = Not bad(i)
        Next
        Return pass
    End Function

    Public Sub Forget()
        have = False
        Reached = 0
    End Sub

End Module

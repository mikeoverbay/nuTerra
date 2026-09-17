Imports OpenTK.Mathematics

''' <summary>
''' WHAT IT THINKS IT SEES - rendered to a texture, hung on the screen in ortho.
'''
''' "Scope needs to be drawn in its own quad on the main in a ortho pass. we
'''  draw the radar in to the FBO and show that texture in ortho. We could pre
'''  create our scope window and save to a texture once and reuse it. we just
'''  draw over it with our data" - the owner, 2026-09-17.
'''
''' TWO CANVASES, AND THE SPLIT IS THE POINT. The rings, the hull outline and
''' the border are IDENTICAL on every frame of every run, and they are most of
''' the vertices in the picture. They are drawn ONCE into `chrome` and stamped
''' into `live` as a single textured quad; only the rays, the barrier and the
''' ways are rebuilt per frame. The per-frame cost becomes the part that
''' actually changed.
'''
''' NOT AN ImGui WINDOW ANY MORE. It was, and that was wrong for an instrument
''' twice over: it could only exist while a UI library was mid-frame, and it
''' came with a title bar, a drag handle and a resize grip that nobody wants on
''' a gauge. Now it is a quad.
'''
''' NOT THE MAP AGAIN. The rays are already drawn on the world in 3D; drawing
''' them a second time would only be a smaller copy. This is the tank's OWN
''' frame - nose up, hull in the middle - so the picture is what the brain has
''' rather than what the owner has. The two disagreeing is the interesting case,
''' and it cannot be seen at all while both pictures are the same picture.
'''
''' THE VERDICT IS THE POINT. A scope that draws the returns tells you what it
''' measured; the line of text tells you what it CONCLUDED, and those come apart
''' exactly when the conclusion is wrong. A flat wall drawn as a clean arc of
''' dots with "broken up" written under it is a bug you can see in one glance
''' and would never find in a log.
'''
''' Added 2026-09-16 by Tank AI work. Moved off ImGui to its own FBO 2026-09-17.
''' </summary>
Module BrainScope

    Public SHOW As Boolean = True

    ''' <summary>Rendered square. 320 is enough that 3-degree ray spacing is
    ''' still distinct at the rim.</summary>
    Private Const PX As Integer = 320

    ''' <summary>Where it hangs, and how big, in screen pixels.</summary>
    Public ShownPx As Single = 300.0F
    Public Margin As Single = 12.0F
    ' 1.0: the backdrop's own alpha is what makes it see-through, and
    ' multiplying a second time here was half of why it vanished.
    Public Fade As Single = 1.0F

    ''' <summary>A solid olive patch behind the scope, for reading the alpha.
    ''' The map is green here, grey there and moving, so it cannot tell you
    ''' what the transparency is doing; one known colour can. Off when the
    ''' answer is settled.</summary>
    Public TestBack As Boolean = False
    Private ReadOnly OLIVE As New Vector4(0.42F, 0.42F, 0.11F, 1.0F)

    Private chrome As BrainCanvas = Nothing
    Private live As BrainCanvas = Nothing
    Private chromeDone As Boolean = False

    ' ---- the palette, carried over from the ImGui version ----------------
    ''' <summary>Dark red, kept. It went in as a debugging colour so the FBO's
    ''' extents and the quad's edge were unmistakable against the map, and it
    ''' reads better than the blue it replaced - against terrain that is green,
    ''' grey and brown, a red instrument is the one thing on screen that cannot
    ''' be mistaken for scenery.
    '''
    ''' 0.55 rather than 0.82: chosen against a solid olive patch, which is the
    ''' only way to judge an alpha - the map is green here, grey there and
    ''' moving, so it cannot tell you what a number is doing.</summary>
    Private ReadOnly BACKDROP As New Vector4(0.16F, 0.02F, 0.03F, 0.55F)
    Private ReadOnly ORANGE As New Vector4(0.8F, 0.33F, 0.06F, 1.0F)
    ''' <summary>A ray that found nothing. DARKER, not fainter - see the note
    ''' on solid data lines below.</summary>
    Private ReadOnly ORANGE_DIM As New Vector4(0.40F, 0.17F, 0.03F, 1.0F)
    ''' <summary>
    ''' The range rings and the border. Lifted from 0.13/0.19/0.25, where they
    ''' were darker than the backdrop they sit on, so a scale meant to be read
    ''' at a glance had to be hunted for.
    '''
    ''' OPAQUE. A translucent ring picks up whatever it crosses in the world,
    ''' so the scale reads patchy exactly where the ground is busy - which is
    ''' where you are looking. The scale is CHROME, not part of the picture:
    ''' the backdrop is what lets the world through, and the ruler drawn on it
    ''' has no business being see-through.
    ''' </summary>
    Private ReadOnly INK As New Vector4(0.42F, 0.50F, 0.58F, 1.0F)
    Private ReadOnly GREY As New Vector4(0.71F, 0.71F, 0.71F, 1.0F)
    Private ReadOnly GREEN As New Vector4(0.25F, 0.88F, 0.25F, 1.0F)
    Private ReadOnly GREEN_NO As New Vector4(0.20F, 0.36F, 0.20F, 1.0F)
    ''' <summary>
    ''' EVERY DATA LINE IS SOLID. The backdrop is the only translucent thing
    ''' in the picture.
    '''
    ''' Translucent data over a translucent backdrop compounds: a line at 0.38
    ''' on a backdrop at 0.55 is showing at a fifth of its colour, and where
    ''' two lines cross it is not - so the picture gains a brightness that
    ''' means nothing and loses one that did. Worse, whatever is behind the
    ''' scope in the world shows THROUGH the measurement, which is the one
    ''' thing an instrument must never let happen.
    '''
    ''' Where a distinction used to be carried by alpha - a miss against a
    ''' return, a gap that does not fit against one that does - it is carried
    ''' by a DARKER shade at full opacity instead. Same reading, no compounding.
    ''' </summary>
    Private ReadOnly BLUE As New Vector4(0.31F, 0.63F, 1.0F, 1.0F)
    Private ReadOnly YELLOW As New Vector4(1.0F, 0.86F, 0.24F, 1.0F)
    Private ReadOnly TXT As New Vector4(1.0F, 0.62F, 0.2F, 1.0F)

    ''' <summary>
    ''' WHICH WAY IS RIGHT ON THIS PICTURE.
    '''
    ''' A top-down view has a handedness the 3D one does not: looking DOWN at a
    ''' tank facing away from you, its right hand is on your right - but the
    ''' scope looks down at a tank facing UP the screen, and that flips it.
    '''
    ''' Drawn without this, every return appeared on the wrong side and the
    ''' scope quietly disagreed with the 3D rays about the same scan. One
    ''' constant, applied to every X.
    ''' </summary>
    Private Const XS As Single = -1.0F

    Private ReadOnly Property Mid As Single
        Get
            Return PX * 0.5F
        End Get
    End Property

    Private ReadOnly Property Scale As Single
        Get
            ' Six pixels of margin so a return at exactly REACH_M lands inside
            ' the border rather than on it.
            Return (PX * 0.5F - 6.0F) / BrainRadar.REACH_M
        End Get
    End Property

    Private Function sx(angle As Single, dist As Single) As Single
        Return Mid + XS * CSng(Math.Sin(angle)) * dist * Scale
    End Function

    Private Function sy(angle As Single, dist As Single) As Single
        Return Mid - CSng(Math.Cos(angle)) * dist * Scale
    End Function

    ''' <summary>The parts that never change: backdrop, rings, hull, nose.
    ''' Built once, on the first frame with a GL context.</summary>
    Private Sub build_chrome(screenW As Integer, screenH As Integer)
        If chromeDone Then Return
        If chrome Is Nothing Then chrome = New BrainCanvas(PX, "ScopeChrome")

        ' Cleared to the same dark red rather than to nothing, so any gap
        ' between the clear and the backdrop rect shows up as a seam instead
        ' of as transparency that looks intentional.
        chrome.Begin(New Vector4(0.16F, 0.02F, 0.03F, 0.55F))
        chrome.RectFill(0.0F, 0.0F, PX, PX, BACKDROP)
        chrome.Rect(0.5F, 0.5F, PX - 0.5F, PX - 0.5F, INK)

        ' Rings every 10 m. Five would be a grid rather than a scale now the
        ' reach is forty.
        For r = 10 To CInt(BrainRadar.REACH_M) Step 10
            chrome.Circle(Mid, Mid, r * Scale, INK)
        Next

        Dim hw = 1.7F * Scale, hl = 3.5F * Scale
        chrome.Rect(Mid - hw, Mid - hl, Mid + hw, Mid + hl, ORANGE)
        chrome.Line(Mid, Mid, Mid, Mid - hl - 6.0F, ORANGE)

        chrome.End(screenW, screenH)
        chromeDone = True
    End Sub

    ''' <summary>Draw the scope and hang it in the corner. Called after the
    ''' world, in its own ortho pass.</summary>
    Public Sub Draw(screenW As Integer, screenH As Integer)
        If Not SHOW Then Return
        build_chrome(screenW, screenH)
        If live Is Nothing Then live = New BrainCanvas(PX, "Scope")

        Dim hits = BrainRadar.LAST

        live.Begin(New Vector4(0.0F, 0.0F, 0.0F, 0.0F))
        live.DrawCanvas(chrome)

        ' ---- the corridor planks: ONLY while planking -----------------------
        If Not BrainRadar.SCANNING AndAlso BrainRadar.LANE_OFF IsNot Nothing Then
            For i = 0 To BrainRadar.LANE_OFF.Length - 1
                Dim lx = Mid + XS * BrainRadar.LANE_OFF(i) * Scale
                Dim y1 = Mid - BrainRadar.LANE_LEN(i) * Scale
                Dim col = If(BrainRadar.LANE_HIT(i), YELLOW, BLUE)
                live.Line(lx, Mid, lx, y1, col)
                If BrainRadar.LANE_HIT(i) Then live.Disc(lx, y1, 2.6F, YELLOW)
            Next
        End If

        ' ---- the returns: while SCANNING, or while a plank is touching ------
        '
        ' Held for the whole of scan mode rather than re-decided each frame.
        ' Gating this on LANE_ACTIVE alone made the rays flash off the moment
        ' the hull turned enough for its planks to come clear - which is
        ' precisely when the scan is doing the work worth watching.
        If hits IsNot Nothing AndAlso
           (BrainRadar.SCANNING OrElse BrainRadar.LANE_ACTIVE) Then
            For Each q In hits
                live.Line(Mid, Mid, sx(q.angle, q.dist), sy(q.angle, q.dist),
                          If(q.found, ORANGE, ORANGE_DIM))
                If q.found Then
                    live.Disc(sx(q.angle, q.dist), sy(q.angle, q.dist), 2.0F, ORANGE)
                End If
            Next
        End If

        ' ---- the barrier: neighbours the hull cannot pass between ----------
        If hits IsNot Nothing Then
            For i = 0 To hits.Length - 1
                If Not hits(i).linked Then Continue For
                Dim j = (i + 1) Mod hits.Length
                live.Line(sx(hits(i).angle, hits(i).dist),
                          sy(hits(i).angle, hits(i).dist),
                          sx(hits(j).angle, hits(j).dist),
                          sy(hits(j).angle, hits(j).dist), GREY)
            Next
        End If

        ' ---- the ways through, as the scan ruled on them -------------------
        For Each wy In BrainRadar.WAYS
            If hits Is Nothing OrElse wy.ia >= hits.Length OrElse wy.ib >= hits.Length Then
                Continue For
            End If
            Dim qa = hits(wy.ia), qb = hits(wy.ib)
            Dim ax = sx(qa.angle, qa.dist), ay = sy(qa.angle, qa.dist)
            Dim bx = sx(qb.angle, qb.dist), by = sy(qb.angle, qb.dist)
            live.Line(ax, ay, bx, by, If(wy.fits, GREEN, GREEN_NO))
            If wy.fits Then
                live.Disc((ax + bx) * 0.5F, (ay + by) * 0.5F, 3.4F, GREEN)
                live.Circle((ax + bx) * 0.5F, (ay + by) * 0.5F, 6.0F, GREEN)
            End If
        Next

        ' ---- what it thinks, in words --------------------------------------
        ' NUMBERS, ZERO-PADDED, IN FIXED SLOTS.
        '
        ' The verdict used to be a sentence, and a sentence changes LENGTH -
        ' "FLAT 9.3 m, face 56 deg" becoming "TWO things - a gap in the
        ' returns" moves every character after it. On a readout updating sixty
        ' times a second that reads as the whole line jumping, and the eye
        ' spends its time tracking the text instead of reading it.
        '
        ' Padded to two digits for the same reason at a smaller scale: 9
        ' becoming 10 shifts everything to its right. The font is monospace, so
        ' with the width fixed the digits change in place and nothing else
        ' moves. A gauge should be still except where the value is.
        Dim s = BrainRadar.FitSurface(hits)
        BrainText.Text2D(String.Format("Gaps: {0:00}", s.gaps),
                         8.0F, PX - 42.0F, 0.8F, TXT)
        BrainText.Text2D(String.Format("Returns: {0:00}  Turns: {1:00}",
                                       s.used, s.turns),
                         8.0F, PX - 26.0F, 0.7F, GREY)
        ' GEOMETRY DOWN FIRST, THEN TEXT ON TOP.
        '
        ' BrainText draws immediately and the canvas QUEUES, so
        ' without this every rectangle and line below lands over the
        ' words - on the card that meant the backing panel painting
        ' out its own text, every frame.
        live.Flush()
        BrainText.Render2D(PX, PX)

        live.End(screenW, screenH)

        ' ---- and hang it in the corner --------------------------------------
        Dim qx = screenW - ShownPx - Margin
        If TestBack Then
            ' Slightly larger than the scope, so the edge of the quad is
            ' visible against the patch as well as the fill.
            BrainCanvas.ScreenRect(qx - 10.0F, Margin - 10.0F,
                                   ShownPx + 20.0F, ShownPx + 20.0F,
                                   OLIVE, screenW, screenH)
        End If
        live.Blit(qx, Margin, ShownPx, ShownPx, screenW, screenH, Fade)
    End Sub

End Module

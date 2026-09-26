Imports OpenTK.Mathematics

''' <summary>
''' A NAME OVER EVERY TANK: which hull it is, and which road it drew.
'''
''' "can I get a ID on each tank for discussion" then "we do billboards in
''' nuTerra. We may want that. just smaller" - the owner, 2026-09-25.
'''
''' A BILLBOARD, NOT A SCREEN-SPACE OVERLAY, and BrainText.Text3D already is
''' one: it builds its quads along the camera's right and up - handed to it
''' once a frame by SetCamera, taken from the view matrix the world was
''' actually drawn with - so the text faces the viewer without any of it
''' knowing about yaw or pitch. It is drawn in the same depth pass as the
''' world, so a label on a tank behind a hill is behind the hill, which is
''' the whole reason to prefer this over projecting to pixels: the label
''' tells you where the tank IS, including when the answer is "out of sight".
''' It does not WRITE depth, so overlapping glyph quads do not punch holes in
''' each other.
'''
''' SIZE HELD AT A CONSTANT ANGLE, then clamped. A billboard measured purely
''' in metres is unreadable from the 300 m zoom this app is usually driven at
''' and absurd from 20 m, and both of those are one scroll wheel apart. So
''' the height is proportional to the distance from the eye, which is exactly
''' what keeps an object the same size on screen, and then clamped at both
''' ends: never smaller than a real 0.8 m label, never larger than 6 m, so up
''' close it settles into a plausible physical size rather than growing into
''' the sky. `tune=tagm=` moves the angle, `tune=tagmin=` / `tune=tagmax=`
''' the clamps.
'''
''' WHY THE HULL INDEX AND NOT THE VEHICLE NAME. Every log line in this app
''' says "hull 0", "hull 3" - the crew roster, the arrival lines, the stall
''' report, the road assignment. A label showing "Object 260" would be a
''' second naming scheme to translate in your head while reading the output
''' window, so the badge shows the number the logs already use and the
''' vehicle tag stays where it is, on the card.
'''
''' I toggles it.
''' </summary>
Module BrainTankTags

    Public SHOW As Boolean = True

    ''' <summary>The tank being followed. Warm and bright, because with ten of
    ''' these on screen the first question is always "which one am I on".</summary>
    Private ReadOnly MINE As New Vector4(1.0F, 0.94F, 0.55F, 1.0F)
    ''' <summary>A tank that is driving. Green, the same family as its road -
    ''' the badge and the line it is following read as one thing.</summary>
    Private ReadOnly DRIVER As New Vector4(0.55F, 0.95F, 0.65F, 0.95F)
    ''' <summary>Parked. Grey and faded, so the other team stays legible
    ''' without competing with the tanks that are doing something.</summary>
    Private ReadOnly PARKED As New Vector4(0.62F, 0.62F, 0.66F, 0.65F)

    ''' <summary>
    ''' Draw every badge. Call AFTER BrainText.SetCamera and BEFORE
    ''' BrainText.Render3D - SetCamera because a billboard with no camera basis
    ''' faces wherever it was authored, and Render3D because that is what
    ''' flushes and clears the world buffer these quads go into.
    ''' </summary>
    Public Sub Draw()
        If Not SHOW Then Return
        If BrainTanks.Bodies Is Nothing Then Return

        Dim crew = TryCast(BrainSim.Brain, BrainCrew)
        Dim eye = BrainRender.Cam.Eye
        Dim ang = BrainTune.Get_("tagm", 0.013F)
        Dim lo = BrainTune.Get_("tagmin", 0.8F)
        Dim hi = BrainTune.Get_("tagmax", 6.0F)

        For i = 0 To BrainTanks.Bodies.Count - 1
            Dim b = BrainTanks.Bodies(i)

            ' CLEAR OF THE TURRET, not of the ground. half.Y is the hull's own
            ' half-height, so twice it is the roof - and these hulls run 4.6 to
            ' 7.9 m long with rather different heights, so a fixed lift off the
            ' terrain would bury the badge on one tank and float it on another.
            Dim at As New Vector3(b.spawn.X,
                                  b.y + b.half.Y * 2.0F + BrainTune.Get_("taglift", 2.0F),
                                  b.spawn.Y)

            Dim m = BrainText.Steady(at, eye, ang, lo, hi)
            Dim road = BrainPath.RoadOf(i)
            Dim s = If(road >= 0,
                       String.Format("H{0}  road {1}", i, road),
                       String.Format("H{0}", i))

            Dim col = PARKED
            If i = BrainWatch.Hull Then
                col = MINE
            ElseIf crew IsNot Nothing AndAlso crew.Driving.Contains(i) Then
                col = DRIVER
            End If

            BrainText.Text3D(s, at, m, col)
        Next
    End Sub

End Module

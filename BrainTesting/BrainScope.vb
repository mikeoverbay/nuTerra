Imports ImGuiNET
Imports OpenTK.Mathematics

''' <summary>
''' WHAT IT THINKS IT SEES, drawn small in the corner.
'''
''' "I want you to draw what it sees in a window in the bottom right corner
''' that is anchored. Very dark blue back and burnt orange lines. I want it
''' saying what it thinks it sees." - the owner, 2026-09-16.
'''
''' NOT THE MAP AGAIN. The rays are already drawn on the world in 3D; drawing
''' them a second time would only be a smaller copy. This is the tank's OWN
''' frame - nose up, hull at the bottom - so the picture is what the brain has
''' rather than what the owner has. The two disagreeing is the interesting
''' case, and it cannot be seen at all while both pictures are the same
''' picture.
'''
''' THE VERDICT IS THE POINT. A scope that draws the returns tells you what it
''' measured; the line of text tells you what it CONCLUDED, and those come
''' apart exactly when the conclusion is wrong. A flat wall drawn as a clean
''' arc of dots with "broken up" written under it is a bug you can see in one
''' glance and would never find in a log.
'''
''' Added 2026-09-16 by Tank AI work.
''' </summary>
Module BrainScope

    Public SHOW As Boolean = True

    ''' <summary>
    ''' A SQUARE PLOT WITH THE HULL IN THE MIDDLE OF IT.
    '''
    ''' It used to sit the hull 72% of the way down and scale to 66% of the
    ''' height, because the scan was two arcs and the front one was worth four
    ''' times the pixels of the rear. A full 360 sweep has no favoured
    ''' direction: anything off-centre crops the side the tank happens to be
    ''' turning toward, and a non-square plot means a metre left is a different
    ''' number of pixels from a metre ahead - which makes a round wall look
    ''' oval and a corner look like a curve.
    '''
    ''' So: square, centred, and the scale set from the half-width so the full
    ''' REACH_M radius lands just inside the frame in every direction.
    ''' </summary>
    Private Const W As Single = 300.0F
    Private Const PLOT As Single = W - 20.0F
    Private Const TEXT_H As Single = 78.0F
    Private Const H As Single = PLOT + TEXT_H
    Private Const MARGIN As Single = 12.0F

    ''' <summary>
    ''' WHICH WAY IS RIGHT ON THIS PICTURE.
    '''
    ''' The scope is a top-down view of the hull's own frame, and a top-down
    ''' view has a handedness the 3D one does not: looking DOWN at a tank
    ''' facing away from you, its right hand is on your right - but the scope
    ''' looks down at a tank facing UP the screen, and that flips it.
    '''
    ''' Drawn without this, every return appeared on the wrong side: a wall on
    ''' the tank's left drew right, and the scope quietly disagreed with the
    ''' 3D rays about the same scan. One constant, applied to every X, so the
    ''' two pictures cannot drift apart again.
    ''' </summary>
    Private Const XS As Single = -1.0F

    ''' <summary>Burnt orange, and the dark blue behind it. ImGui packs colour
    ''' as ABGR, which is why these read backwards.</summary>
    Private Const ORANGE As UInteger = &HFF0F55CCUI      ' 204, 85, 15
    Private Const ORANGE_DIM As UInteger = &H600F55CCUI
    Private Const ORANGE_HOT As UInteger = &HFF3399FFUI  ' brighter, for the fit
    Private Const INK As UInteger = &HFF203040UI         ' grid lines

    ''' <summary>Light grey for the barrier joining adjacent returns.
    '''
    ''' Not orange, and the reason is what the two things ARE: a return is a
    ''' measurement and the line between two of them is an inference - true
    ''' only as far as "the hull does not fit between these". Drawing the
    ''' inference in the same colour as the evidence invites reading the
    ''' outline as if the scan had traced it, which it never did.</summary>
    Private Const GREY As UInteger = &HFFB4B4B4UI        ' 180, 180, 180

    ''' <summary>Green for a gap the hull fits through - the go areas.</summary>
    Private Const GREEN As UInteger = &HFF40E040UI        ' 64, 224, 64

    ''' <summary>A gap wide enough by its chord that the hull's corridor will
    ''' not clear. Drawn faintly rather than dropped: "we are trying to go thru
    ''' gaps we wont fit" was invisible precisely because the rejected ones
    ''' left no mark.</summary>
    Private Const GREEN_NO As UInteger = &H50406040UI     ' dim, translucent

    ''' <summary>Blue for the corridor planks - the box the hull is about to
    ''' sweep, drawn so "can we clear" can be checked by eye against the thing
    ''' it is deciding about rather than trusted.</summary>
    Private Const BLUE As UInteger = &HFFFFA050UI         ' 80, 160, 255
    ''' <summary>
    ''' YELLOW for a plank that hit.
    '''
    ''' It went blue-violet, then red, then this. The violet was a packing
    ''' mistake - &HFFFF6060 is RGB(96, 96, 255), since ImGui packs ABGR and
    ''' the red byte is the LOW one - and red was a bad pick on its own terms:
    ''' against a near-black blue field with burnt-orange returns it goes muddy
    ''' at line width, and it reads as an alarm when the plank is only
    ''' reporting.
    '''
    ''' Yellow separates from both the orange returns and the blue planks at a
    ''' glance, which is the entire job of this colour.
    ''' </summary>
    Private Const YELLOW As UInteger = &HFF3CDCFFUI       ' 255, 220, 60

    Public Sub Draw(displayW As Single, displayH As Single)
        If Not SHOW Then Return

        ImGui.SetNextWindowPos(New System.Numerics.Vector2(displayW - MARGIN,
                                                           displayH - MARGIN),
                               ImGuiCond.Always,
                               New System.Numerics.Vector2(1.0F, 1.0F))
        ImGui.SetNextWindowSize(New System.Numerics.Vector2(W, H), ImGuiCond.Always)
        ImGui.PushStyleColor(ImGuiCol.WindowBg,
                             New System.Numerics.Vector4(0.015F, 0.03F, 0.10F, 0.96F))
        ImGui.PushStyleColor(ImGuiCol.Border,
                             New System.Numerics.Vector4(0.10F, 0.18F, 0.30F, 1.0F))
        ImGui.Begin("Scope",
                    ImGuiWindowFlags.NoResize Or ImGuiWindowFlags.NoMove Or
                    ImGuiWindowFlags.NoCollapse Or ImGuiWindowFlags.NoScrollbar)

        Dim hits = BrainRadar.LAST
        Dim s = BrainRadar.FitSurface(hits)

        Dim p0 = ImGui.GetCursorScreenPos()
        Dim plotW = PLOT
        Dim plotH = PLOT
        Dim dl = ImGui.GetWindowDrawList()

        ' HULL IN THE MIDDLE, NOSE UP. This said the opposite until the sweep
        ' went to 360 - that centring it "would waste half its pixels on the
        ' rear arc, which is 15 rays that matter far less than the 15 in
        ' front". True of two arcs and false of a circle: there are 60 rays
        ' behind now, they are what the reverse is steered by, and there is no
        ' longer a direction worth cropping.
        Dim cx = p0.X + plotW * 0.5F
        Dim cy = p0.Y + plotH * 0.5F
        ' Six pixels of margin so a return at exactly REACH_M draws inside the
        ' border rather than on it.
        Dim scale = (plotW * 0.5F - 6.0F) / BrainRadar.REACH_M

        ' Range rings every 5 m, so a distance can be read off rather than
        ' guessed.
        For r = 5 To CInt(BrainRadar.REACH_M) Step 5
            dl.AddCircle(New System.Numerics.Vector2(cx, cy), r * scale, INK, 48, 1.0F)
        Next

        ' THE RAYS ONLY WHEN SOMETHING IS IN THE WAY. "hide the scanner rays
        ' until plank is active" - with a clear corridor there is no decision
        ' being made, and 120 rays drawn over the planks bury the one thing
        ' worth watching under the thing that is merely always true.
        If hits IsNot Nothing AndAlso BrainRadar.LANE_ACTIVE Then
            For Each q In hits
                ' Hull frame: angle 0 is the nose, which is UP on screen.
                Dim a = q.angle
                Dim ex = cx + XS * CSng(Math.Sin(a)) * q.dist * scale
                Dim ey = cy - CSng(Math.Cos(a)) * q.dist * scale
                dl.AddLine(New System.Numerics.Vector2(cx, cy),
                           New System.Numerics.Vector2(ex, ey),
                           If(q.found, ORANGE, ORANGE_DIM), 1.0F)
                If q.found Then
                    dl.AddCircleFilled(New System.Numerics.Vector2(ex, ey), 2.2F, ORANGE, 8)
                End If
            Next
        End If

        ' ---- THE BARRIER: NEIGHBOURS THE HULL CANNOT PASS BETWEEN ----------
        '
        ' Drawn before the fit and after the returns, so it reads as the shape
        ' the returns imply rather than as decoration over them. Every segment
        ' here is a pair of adjacent hits closer together than the tank is
        ' wide: joined up, they are the outline of what is actually in the way,
        ' and every place the outline BREAKS is a gap wide enough to drive
        ' through. That is the whole point of drawing it - the openings are the
        ' gaps in this line, and they can be seen rather than inferred.
        If hits IsNot Nothing Then
            For i = 0 To hits.Length - 1
                If Not hits(i).linked Then Continue For
                Dim j = (i + 1) Mod hits.Length
                Dim ax2 = cx + XS * CSng(Math.Sin(hits(i).angle)) * hits(i).dist * scale
                Dim ay2 = cy - CSng(Math.Cos(hits(i).angle)) * hits(i).dist * scale
                Dim bx2 = cx + XS * CSng(Math.Sin(hits(j).angle)) * hits(j).dist * scale
                Dim by2 = cy - CSng(Math.Cos(hits(j).angle)) * hits(j).dist * scale
                dl.AddLine(New System.Numerics.Vector2(ax2, ay2),
                           New System.Numerics.Vector2(bx2, by2), GREY, 2.2F)
            Next
        End If

        ' ---- THE CORRIDOR: THE BOX WE ARE ABOUT TO SWEEP -------------------
        '
        ' Parallel planks the width of the hull plus one either side, drawn in
        ' the hull's own frame straight up the screen because that is exactly
        ' what they are - straight out from the corners, parallel, not fanned.
        ' Drawn UNDER the returns so it reads as the question and they read as
        ' the answer. A plank that stopped early goes hot, so which side is
        ' blocked is visible without reading a log line.
        If BrainRadar.LANE_OFF IsNot Nothing Then
            For i = 0 To BrainRadar.LANE_OFF.Length - 1
                Dim lx = cx + XS * BrainRadar.LANE_OFF(i) * scale
                Dim y0 = cy
                Dim y1 = cy - BrainRadar.LANE_LEN(i) * scale
                dl.AddLine(New System.Numerics.Vector2(lx, y0),
                           New System.Numerics.Vector2(lx, y1),
                           If(BrainRadar.LANE_HIT(i), YELLOW, BLUE), 1.4F)
                If BrainRadar.LANE_HIT(i) Then
                    dl.AddCircleFilled(New System.Numerics.Vector2(lx, y1),
                                       2.6F, YELLOW, 8)
                End If
            Next
        End If

        ' ---- THE WAYS THROUGH, AS THE SCAN RULED ON THEM -------------------
        '
        ' Drawn from BrainRadar.WAYS rather than re-derived here, so the
        ' picture and the decision cannot disagree. This used to walk the hits
        ' itself with the hull width written in as a literal - 1.65 plus 0.3 -
        ' which was right for this tank and would have quietly lied about any
        ' other.
        '
        ' GREEN is a gap the hull's own corridor cleared. GREY-GREEN is a gap
        ' whose chord is wide enough and whose corridor is NOT clear: the case
        ' that was sending the tank at openings it could never fit, drawn so it
        ' can be seen being rejected instead of silently not appearing.
        For Each wy In BrainRadar.WAYS
            If hits Is Nothing OrElse wy.ia >= hits.Length OrElse wy.ib >= hits.Length Then
                Continue For
            End If
            Dim qa = hits(wy.ia), qb = hits(wy.ib)
            Dim ax3 = cx + XS * CSng(Math.Sin(qa.angle)) * qa.dist * scale
            Dim ay3 = cy - CSng(Math.Cos(qa.angle)) * qa.dist * scale
            Dim bx3 = cx + XS * CSng(Math.Sin(qb.angle)) * qb.dist * scale
            Dim by3 = cy - CSng(Math.Cos(qb.angle)) * qb.dist * scale
            Dim col = If(wy.fits, GREEN, GREEN_NO)
            dl.AddLine(New System.Numerics.Vector2(ax3, ay3),
                       New System.Numerics.Vector2(bx3, by3), col, If(wy.fits, 2.4F, 1.2F))
            If wy.fits Then
                Dim mx = (ax3 + bx3) * 0.5F
                Dim my = (ay3 + by3) * 0.5F
                dl.AddCircleFilled(New System.Numerics.Vector2(mx, my), 3.4F, GREEN, 10)
                dl.AddCircle(New System.Numerics.Vector2(mx, my), 6.0F, GREEN, 12, 1.2F)
            End If
        Next

        ' THE WALL IT FITTED, if it fitted one. Drawn as the line the maths
        ' says is there - so a fit that is wrong is wrong ON TOP of the returns
        ' it was fitted to, which is the only way to see it.
        If s.valid AndAlso s.flat AndAlso s.dist > 0.0F AndAlso s.dist < BrainRadar.REACH_M Then
            Dim nx = CSng(Math.Sin(s.normalRad)), nz = CSng(Math.Cos(s.normalRad))
            ' A point on the wall, and the direction along it.
            Dim px = nx * s.dist, pz = nz * s.dist
            Dim tx = -nz, tz = nx
            Dim half = BrainRadar.REACH_M
            Dim ax = cx + XS * (px - tx * half) * scale
            Dim ay = cy - (pz - tz * half) * scale
            Dim bx = cx + XS * (px + tx * half) * scale
            Dim by = cy - (pz + tz * half) * scale
            dl.AddLine(New System.Numerics.Vector2(ax, ay),
                       New System.Numerics.Vector2(bx, by), ORANGE_HOT, 1.6F)
        End If

        ' THE TURNING POINT - the nearest return, where the sequence stops
        ' shrinking and starts growing. On one flat surface there is exactly
        ' one, and it is the wall's closest point: a distance that owes nothing
        ' to the line fit, so the two can be checked against each other.
        If s.turnRay >= 0 AndAlso hits IsNot Nothing AndAlso s.turnRay < hits.Length Then
            Dim t = hits(s.turnRay)
            Dim tx = cx + XS * CSng(Math.Sin(t.angle)) * t.dist * scale
            Dim ty = cy - CSng(Math.Cos(t.angle)) * t.dist * scale
            dl.AddLine(New System.Numerics.Vector2(tx - 5.0F, ty),
                       New System.Numerics.Vector2(tx + 5.0F, ty), ORANGE_HOT, 1.6F)
            dl.AddLine(New System.Numerics.Vector2(tx, ty - 5.0F),
                       New System.Numerics.Vector2(tx, ty + 5.0F), ORANGE_HOT, 1.6F)
        End If

        ' The edge, if one was seen. The thing the whole fit is for.
        If s.edgeRay >= 0 AndAlso hits IsNot Nothing AndAlso s.edgeRay < hits.Length Then
            Dim q = hits(s.edgeRay)
            Dim ex = cx + XS * CSng(Math.Sin(q.angle)) * q.dist * scale
            Dim ey = cy - CSng(Math.Cos(q.angle)) * q.dist * scale
            dl.AddCircle(New System.Numerics.Vector2(ex, ey), 6.0F, ORANGE_HOT, 12, 1.6F)
        End If

        ' The hull, to scale, nose up.
        Dim hw = 1.7F * scale, hl = 3.5F * scale
        dl.AddRect(New System.Numerics.Vector2(cx - hw, cy - hl),
                   New System.Numerics.Vector2(cx + hw, cy + hl), ORANGE, 0.0F, 0, 1.4F)
        dl.AddLine(New System.Numerics.Vector2(cx, cy),
                   New System.Numerics.Vector2(cx, cy - hl - 6.0F), ORANGE, 1.4F)

        ImGui.Dummy(New System.Numerics.Vector2(plotW, plotH))

        ' WHAT IT THINKS, in words.
        ImGui.PushStyleColor(ImGuiCol.Text,
                             New System.Numerics.Vector4(1.0F, 0.55F, 0.15F, 1.0F))
        ImGui.TextWrapped(s.verdict)
        ImGui.PopStyleColor()
        If s.valid Then
            ImGui.TextDisabled(String.Format("{0} returns   {1} turn(s){2}",
                                             s.used, s.turns,
                                             If(s.gapped, "   GAP", "")))
            ImGui.TextDisabled(String.Format("residual {0:0.00} m   nearest {1:0.0} m",
                                             s.residual, s.turnDist))
        End If

        ImGui.End()
        ImGui.PopStyleColor(2)
    End Sub

End Module

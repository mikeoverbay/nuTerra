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

    Private Const W As Single = 300.0F
    Private Const H As Single = 330.0F
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
        Dim plotW = W - 20.0F
        Dim plotH = H - 76.0F
        Dim dl = ImGui.GetWindowDrawList()

        ' HULL AT THE BOTTOM, NOSE UP. A scope that put the tank in the middle
        ' would waste half its pixels on the rear arc, which is 15 rays that
        ' matter far less than the 15 in front.
        Dim cx = p0.X + plotW * 0.5F
        Dim cy = p0.Y + plotH * 0.72F
        Dim scale = (plotH * 0.66F) / BrainRadar.REACH_M

        ' Range rings every 5 m, so a distance can be read off rather than
        ' guessed.
        For r = 5 To CInt(BrainRadar.REACH_M) Step 5
            dl.AddCircle(New System.Numerics.Vector2(cx, cy), r * scale, INK, 48, 1.0F)
        Next

        If hits IsNot Nothing Then
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

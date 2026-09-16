Imports OpenTK.Mathematics

''' <summary>One drawable piece of the loaded model, as the visual names it.</summary>
Public Class PartRow
    ''' <summary>The material `identifier` out of the .visual_processed -
    ''' `s_nd0`, `s_wall_0`, `n_wood0_1`. This is the name the game gives the
    ''' piece, which is what the owner asked to see, rather than anything this
    ''' app invents.</summary>
    Public Property Ident As String = ""
    ''' <summary>The .model this group came from. Shown beside the
    ''' identifier because the identifiers repeat and are not, on their own,
    ''' enough to tell one row from another.</summary>
    Public Property MeshName As String = ""
    Public Property Fx As String = ""
    Public Property Tris As Integer
    ''' <summary>Index into the viewer's pbrParts, so a toggle does not have to
    ''' search for the thing it just clicked.</summary>
    Public Property Index As Integer
    Public Property Hidden As Boolean
End Class

''' <summary>
''' The right-hand panel: every part of the loaded model, and a switch to hide
''' each one.
'''
''' A SECOND PANEL RATHER THAN A TAB on the first. The left panel picks WHICH
''' model, this one picks what you see OF it, and they are used together - you
''' find a wall, then hide the roof that is covering it. Putting them in one
''' panel would mean losing the list every time you wanted the parts.
'''
''' ONE ROW PER PRIMITIVE GROUP, NOT PER MESH, and the distinction is the whole
''' reason this is useful. A .visual_processed carries one material per
''' primitive group, and a group is a contiguous run of the index buffer - so a
''' single mesh is routinely several materials. hd_bld_eu_049_thouse is 9 meshes
''' and 43 groups; its roof alone is three groups across two different shaders.
''' A per-mesh list would hide the roof in one click and never let you look at
''' the tiles separately, which is the thing worth looking at.
'''
''' The label is the material's own `identifier`. Those repeat across a model -
''' s_nd0 appears on most buildings and often several times on one - so the mesh
''' name and the triangle count ride alongside to tell two rows apart. Renaming
''' them to something friendlier was considered and rejected: the owner asked
''' for the name the visual gives, and an invented name cannot be matched
''' against the file when something looks wrong.
''' </summary>
Public Class PartsPanel

    Public Const PANEL_W As Integer = 300
    Private Const PAD As Integer = 8

    Public Property Visible As Boolean = True
    Public Property Scroll As Integer = 0
    ''' <summary>Shown on the format row; the viewer owns the value.</summary>
    Public Property ExportFormat As String = "obj"
    ''' <summary>One line of result under the buttons - the viewer writes it
    ''' after an export so the answer appears where the button was, not only
    ''' in a console nobody is looking at.</summary>
    Public Property LastExport As String = ""
    Public ReadOnly Rows As New List(Of PartRow)

    Private rowH As Integer = 18
    Private listTop As Integer = 0
    Private lastPanelH As Integer = 0

    ''' <summary>Left edge in window pixels, set every draw. The panel is
    ''' RIGHT-ALIGNED, so this moves whenever the window is resized - a hit test
    ''' against a remembered value answers for a panel that is no longer
    ''' there.</summary>
    Private panelX As Integer = 0
    Private panelW As Integer = PANEL_W

    ''' <summary>Row rect of the eye toggle, relative to the panel's left edge.
    ''' Clicking the NAME does something different from clicking the box, so the
    ''' two have to be told apart.</summary>
    Private Const BOX_X As Integer = PAD
    Private Const BOX_W As Integer = 13

    Public ReadOnly Property HiddenCount As Integer
        Get
            Dim n = 0
            For Each r In Rows
                If r.Hidden Then n += 1
            Next
            Return n
        End Get
    End Property

    Public Sub Clear()
        Rows.Clear()
        Scroll = 0
    End Sub

    Public Sub Add(ident As String, meshName As String, fx As String, tris As Integer, index As Integer)
        Rows.Add(New PartRow With {
            .Ident = If(ident, ""), .MeshName = If(meshName, ""),
            .Fx = If(fx, ""), .Tris = tris, .Index = index, .Hidden = False})
    End Sub

    Public Sub ShowAll()
        For Each r In Rows
            r.Hidden = False
        Next
    End Sub

    Public Sub HideAll()
        For Each r In Rows
            r.Hidden = True
        Next
    End Sub

    ''' <summary>Show only this row. The quickest way to answer "what IS that
    ''' piece" on a model with forty of them.</summary>
    Public Sub Solo(i As Integer)
        If i < 0 OrElse i >= Rows.Count Then Return
        For k = 0 To Rows.Count - 1
            Rows(k).Hidden = (k <> i)
        Next
    End Sub

    Public Function VisibleRows(panelH As Integer) As Integer
        ' Four rows of chrome below the list - show/hide, format, and the two
        ' export buttons - plus the hidden-count line and the padding.
        Return Math.Max(1, (panelH - listTop - PAD * 3 - rowH * 5) \ Math.Max(1, rowH))
    End Function

    Public Sub ClampScroll()
        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, Rows.Count - VisibleRows(lastPanelH)))
    End Sub

    Public Sub ScrollBy(lines As Integer)
        Scroll += lines
        ClampScroll()
    End Sub

    Public Function HitsPanel(x As Single, y As Single) As Boolean
        Return Visible AndAlso Rows.Count > 0 AndAlso x >= panelX AndAlso y >= 0
    End Function

    ''' <summary>Row under this pixel, or -1.</summary>
    Public Function RowAtPixel(x As Single, y As Single) As Integer
        If Not HitsPanel(x, y) Then Return -1
        If y < listTop Then Return -1
        Dim i = Scroll + CInt(Math.Floor((y - listTop) / rowH))
        If i < 0 OrElse i >= Rows.Count Then Return -1
        If i >= Scroll + VisibleRows(lastPanelH) Then Return -1
        Return i
    End Function

    ''' <summary>True when the click landed on the toggle box rather than on the
    ''' name beside it.</summary>
    Public Function HitsBox(x As Single) As Boolean
        Dim lx = x - panelX
        Return lx >= BOX_X - 2 AndAlso lx <= BOX_X + BOX_W + 2
    End Function

    ''' <summary>Y of the show all / hide all row. Everything below it is the
    ''' export block, so this is computed from the bottom up and the two must
    ''' agree - a hit test and a draw that disagree by one row is a button
    ''' that looks right and fires the one beneath it.</summary>
    Public Function BulkRowY(panelH As Integer) As Integer
        Return ExportRowY(panelH, 0) - rowH - PAD
    End Function

    ''' <summary>The two footer buttons: all on, all off.</summary>
    Public Function HitsShowAll(x As Single, y As Single) As Boolean
        If Not Visible OrElse Rows.Count = 0 Then Return False
        Dim by = BulkRowY(lastPanelH)
        Return y >= by AndAlso y < by + rowH AndAlso
               x >= panelX + PAD AndAlso x < panelX + panelW \ 2 - 2
    End Function

    Public Function HitsHideAll(x As Single, y As Single) As Boolean
        If Not Visible OrElse Rows.Count = 0 Then Return False
        Dim by = BulkRowY(lastPanelH)
        Return y >= by AndAlso y < by + rowH AndAlso
               x >= panelX + panelW \ 2 + 2 AndAlso x < panelX + panelW - PAD
    End Function

    ''' <summary>The part name with the shared asset prefix dropped:
    ''' `hd_bld_eu_049_thouse_roof_01` becomes `roof_01`. Every row carries
    ''' the same prefix, so it distinguishes nothing and costs the width that
    ''' does.</summary>
    Private Function ShortPart(nm As String) As String
        If String.IsNullOrEmpty(nm) Then Return ""
        Dim common = nm
        For Each r In Rows
            common = SharedHead(common, r.MeshName)
            If common.Length = 0 Then Exit For
        Next
        Dim cut = common.Length
        If cut > 0 AndAlso cut < nm.Length Then Return nm.Substring(cut).TrimStart("_"c)
        Return nm
    End Function

    Private Shared Function SharedHead(a As String, b As String) As String
        If a Is Nothing OrElse b Is Nothing Then Return ""
        Dim n = Math.Min(a.Length, b.Length)
        Dim i = 0
        While i < n AndAlso Char.ToLowerInvariant(a(i)) = Char.ToLowerInvariant(b(i))
            i += 1
        End While
        Return a.Substring(0, i)
    End Function


    ' ---- the export menu, under show all / hide all ----
    '
    ' Three stacked rows at the very bottom, so the visibility switches and the
    ' thing that consumes them are one block: you hide what you do not want and
    ' write the rest without moving the mouse across the window.

    Public Function ExportRowY(panelH As Integer, which As Integer) As Integer
        ' 0 = format, 1 = export visible, 2 = export all
        Return panelH - PAD - rowH * (3 - which) - EXPORT_GAP * (2 - which)
    End Function

    Private Const EXPORT_GAP As Integer = 3

    Public Function HitsExportFormat(x As Single, y As Single) As Boolean
        Return HitsExportRow(x, y, 0)
    End Function

    Public Function HitsExportVisible(x As Single, y As Single) As Boolean
        Return HitsExportRow(x, y, 1)
    End Function

    Public Function HitsExportAll(x As Single, y As Single) As Boolean
        Return HitsExportRow(x, y, 2)
    End Function

    Private Function HitsExportRow(x As Single, y As Single, which As Integer) As Boolean
        If Not Visible OrElse Rows.Count = 0 Then Return False
        Dim ry = ExportRowY(lastPanelH, which)
        Return y >= ry AndAlso y < ry + rowH AndAlso
               x >= panelX + PAD AndAlso x < panelX + panelW - PAD
    End Function

    Public Sub Draw(ui As UiOverlay, windowW As Integer, panelH As Integer,
                    mouseX As Single, mouseY As Single)
        If Not Visible OrElse Rows.Count = 0 Then Return
        rowH = ui.Font.CellH + 4
        panelW = Math.Min(PANEL_W, Math.Max(80, windowW - 160))
        panelX = windowW - panelW
        lastPanelH = panelH
        listTop = PAD + rowH + 6

        Dim bg As New Vector4(0.07F, 0.08F, 0.1F, 0.94F)
        Dim edge As New Vector4(0.28F, 0.3F, 0.34F, 1.0F)
        Dim fg As New Vector4(0.84F, 0.86F, 0.9F, 1.0F)
        Dim dim_ As New Vector4(0.48F, 0.51F, 0.56F, 1.0F)
        Dim off As New Vector4(0.36F, 0.38F, 0.42F, 1.0F)
        Dim accent As New Vector4(1.0F, 0.55F, 0.15F, 1.0F)
        Dim hoverBg As New Vector4(0.15F, 0.16F, 0.2F, 1.0F)

        ui.Rect(panelX, 0, panelW, panelH, bg)
        ui.Rect(panelX, 0, 1, panelH, edge)

        ui.TextClipped(panelX + PAD, PAD + 3,
                       String.Format("PARTS  {0}", Rows.Count), panelW - PAD * 2, dim_)

        Dim vis = VisibleRows(panelH)
        Dim y = listTop
        For i = Scroll To Math.Min(Rows.Count, Scroll + vis) - 1
            Dim r = Rows(i)
            Dim over = (mouseX >= panelX AndAlso mouseY >= y AndAlso mouseY < y + rowH)
            If over Then ui.Rect(panelX, y, panelW, rowH, hoverBg)

            ' The toggle. Filled when the part is on screen, hollow when it is
            ' not - readable at a glance down a column of forty.
            Dim bx = panelX + BOX_X
            Dim by = y + 3
            If r.Hidden Then
                ui.Frame(bx, by, BOX_W, BOX_W, off)
            Else
                ui.Rect(bx, by, BOX_W, BOX_W, accent)
                ui.Frame(bx, by, BOX_W, BOX_W, accent)
            End If

            Dim tx = panelX + BOX_X + BOX_W + 6
            Dim room = panelW - (BOX_X + BOX_W + 6) - PAD

            ' The source .model, right-aligned and dim, with the asset's own
            ' name trimmed off the front - every row on a building repeats it
            ' and it would eat the width that tells the rows apart.
            Dim tail = ShortPart(r.MeshName)
            Dim tailW = ui.Font.Width(tail)
            If tail.Length > 0 AndAlso tailW < room - ui.Font.CellW * 6 Then
                ui.Text(panelX + panelW - PAD - tailW, y + 2, tail, If(r.Hidden, off, dim_))
                room -= tailW + ui.Font.CellW
            End If
            ui.TextClipped(tx, y + 2, r.Ident, room, If(r.Hidden, off, fg))
            y += rowH
        Next

        If Rows.Count > vis Then
            Dim trackY = listTop
            Dim trackH = vis * rowH
            Dim thumbH = Math.Max(18, CInt(trackH * (vis / CDbl(Rows.Count))))
            Dim maxScroll = Math.Max(1, Rows.Count - vis)
            Dim thumbY = trackY + CInt((trackH - thumbH) * (Scroll / CDbl(maxScroll)))
            ui.Rect(panelX + panelW - 5, trackY, 3, trackH, New Vector4(0.16F, 0.17F, 0.2F, 1.0F))
            ui.Rect(panelX + panelW - 5, thumbY, 3, thumbH, New Vector4(0.45F, 0.48F, 0.54F, 1.0F))
        End If

        ' Footer: the two bulk actions, and what is hidden right now.
        '
        ' BulkRowY, the SAME call the hit tests make. This was written once as
        ' `panelH - rowH - PAD` here while the hit tests had already moved up
        ' to make room for the export block, and the result drew `export all`
        ' straight over `show all` - two labels in one row of pixels. One
        ' function, both callers.
        Dim fy = BulkRowY(panelH)
        ui.Rect(panelX, fy - rowH - PAD, panelW, panelH - fy + rowH + PAD,
                New Vector4(0.1F, 0.11F, 0.13F, 1.0F))
        Dim halfW = panelW \ 2 - PAD - 2
        ui.Frame(panelX + PAD, fy, halfW, rowH, edge)
        ui.TextClipped(panelX + PAD + 6, fy + 2, "show all", halfW - 10, fg)
        ui.Frame(panelX + panelW \ 2 + 2, fy, halfW, rowH, edge)
        ui.TextClipped(panelX + panelW \ 2 + 8, fy + 2, "hide all", halfW - 10, fg)

        Dim hid = HiddenCount
        If hid > 0 Then
            ui.TextClipped(panelX + PAD, fy - rowH,
                           String.Format("{0} hidden", hid), panelW - PAD * 2, accent)
        End If

        ' ---- export ----
        Dim ey0 = ExportRowY(panelH, 0)
        Dim fullW = panelW - PAD * 2
        ui.Frame(panelX + PAD, ey0, fullW, rowH, edge)
        ui.TextClipped(panelX + PAD + 6, ey0 + 2, "format: " & ExportFormat, fullW - 10, dim_)

        Dim ey1 = ExportRowY(panelH, 1)
        ui.Rect(panelX + PAD, ey1, fullW, rowH, New Vector4(0.17F, 0.2F, 0.26F, 1.0F))
        ui.Frame(panelX + PAD, ey1, fullW, rowH, accent)
        ui.TextClipped(panelX + PAD + 6, ey1 + 2,
                       If(hid > 0, String.Format("export visible ({0})", Rows.Count - hid), "export visible"),
                       fullW - 10, fg)

        Dim ey2 = ExportRowY(panelH, 2)
        ui.Frame(panelX + PAD, ey2, fullW, rowH, edge)
        ui.TextClipped(panelX + PAD + 6, ey2 + 2,
                       String.Format("export all ({0})", Rows.Count), fullW - 10, fg)

        If Not String.IsNullOrEmpty(LastExport) Then
            ui.TextClipped(panelX + PAD, ey2 + rowH + 3, LastExport, fullW, dim_)
        End If
    End Sub
End Class

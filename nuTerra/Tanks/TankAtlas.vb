Imports OpenTK.Mathematics

''' <summary>One flipbook inside the master atlas: where it sits and how it is
''' cut up.</summary>
Public Structure AtlasGrid
    Public x0 As Integer, y0 As Integer, x1 As Integer, y1 As Integer
    Public cols As Integer, rows As Integer
    ''' <summary>Frames run DOWN the first column then down the next, rather
    ''' than across. Only the gun flash is laid out that way.</summary>
    Public columnMajor As Boolean
    Public frames As Integer
End Structure

''' <summary>
''' WoT's particle atlas, and the flipbooks cut out of it.
'''
''' ONE TEXTURE, NOT SLICED. eff_tex.dds is a single 4096 square holding every
''' particle animation the game uses, and the obvious move - cut each grid into
''' its own texture array at load - means decoding BC blocks on the CPU to crop
''' them. There is no need: the grids are all block aligned and the frame
''' rectangle is four numbers, so the atlas is uploaded whole, once, and each
''' particle carries the sub-rectangle it samples. Nothing is decoded, nothing
''' is copied, and adding a set later is a row in the table below.
'''
''' THE BYTES ARE THE USER'S. Every frame here is Wargaming's artwork, read out
''' of the installation already on this machine. It is never written to disk by
''' nuTerra and never enters the repository - the same rule TEPY states for its
''' extracted flipbooks.
'''
''' AND IT IS ALREADY LOADED. MapParticles pulls the same atlas for the map's
''' own effects, so this asks TextureMgr for it by the same name and gets that
''' texture back rather than uploading a second copy.
'''
''' The rectangles come from TEPY's catalogue, which found them by eye: the
''' .vfx files that ought to name them store atlas-table indices rather than UV
''' rectangles, and a probe of them turns up simulation parameters and nothing
''' that maps to a rectangle. So these are measured, not derived, and the note
''' on each says what it holds.
''' </summary>
Public Module TankAtlas

    Private Const PATH As String = "particles/content_deferred/PFX_textures/eff_tex.dds"
    Private Const SIZE As Single = 4096.0F

    Private m_tex As GLTexture
    Private tried As Boolean

    ''' <summary>The muzzle flame: 8 frames at 256x128. Column major, and the
    ''' right column is the left one mirrored - so only the left four are
    ''' distinct artwork and this uses all eight, which is what TEPY does.
    ''' </summary>
    Public ReadOnly GUN_FLASH As New AtlasGrid With {
        .x0 = 1024, .y0 = 2560, .x1 = 1536, .y1 = 3072,
        .cols = 2, .rows = 4, .columnMajor = True, .frames = 8}

    ''' <summary>Pale smoke, 64 frames at 128. The gunpowder cloud under a
    ''' muzzle flash and the dust off a light impact.</summary>
    Public ReadOnly SMOKE_WHITE As New AtlasGrid With {
        .x0 = 1024, .y0 = 0, .x1 = 2048, .y1 = 1024,
        .cols = 8, .rows = 8, .columnMajor = False, .frames = 64}

    ''' <summary>Fireball into black smoke, 32 frames at 128. TEPY's note: the
    ''' cleaner orange-bloom variant, for an OBJECT hit.</summary>
    Public ReadOnly EXPLOSION_FIRE As New AtlasGrid With {
        .x0 = 1024, .y0 = 1024, .x1 = 2048, .y1 = 1536,
        .cols = 8, .rows = 4, .columnMajor = False, .frames = 32}

    ''' <summary>The same sequence in brown, rougher: the ground-impact
    ''' variant. Terrain hits take this one.</summary>
    Public ReadOnly EXPLOSION_DUST As New AtlasGrid With {
        .x0 = 1024, .y0 = 1536, .x1 = 2048, .y1 = 2048,
        .cols = 8, .rows = 4, .columnMajor = False, .frames = 32}

    ''' <summary>The atlas, or Nothing when the install did not yield it -
    ''' every caller falls back to an untextured puff rather than failing.
    ''' </summary>
    Public ReadOnly Property texture As GLTexture
        Get
            If Not tried Then
                tried = True
                ' THE SAME CALL MapParticles MAKES, so the two share one
                ' texture. TextureMgr keys its cache on the name, so whichever
                ' asks first pays for it and the other gets the same object;
                ' going through TankFiles instead would miss that cache and
                ' upload a second 4096 square of the same bytes.
                Dim p = PATH
                m_tex = TextureMgr.find_and_load_texture_from_pkgs(p)
                If m_tex Is Nothing Then
                    LogThis("tank fx: {0} not found - particles stay untextured", PATH)
                Else
                    LogThis("tank fx: particle atlas loaded from {0}", PATH)
                End If
            End If
            Return m_tex
        End Get
    End Property

    ''' <summary>
    ''' The UV rectangle of one frame, returned as (u0, v_bottom, u1, v_top) -
    ''' the order the sprite's own corners want, not ascending.
    '''
    ''' V IS NOT FLIPPED, and that is worth stating because the opposite is the
    ''' natural guess. MapParticles samples this same atlas and settles it:
    ''' TextureMgr uploads the DDS rows unflipped, so the file's TOP row lands
    ''' on v = 0 and rows walk DOWN in sampler v. The catalogue's rectangles are
    ''' measured from the top left, which is therefore the same direction - a
    ''' pixel y maps straight to y / 4096.
    '''
    ''' What DOES have to be swapped is which end of the rectangle each corner
    ''' of the sprite takes. The quad's lower edge has to sample the frame's
    ''' LAST row, so the larger v comes back second and the smaller fourth;
    ''' handing them back in ascending order stands every frame on its head.
    ''' </summary>
    Public Function FrameUV(g As AtlasGrid, frame As Integer) As Vector4
        Dim n = Math.Max(g.frames, 1)
        frame = Math.Min(Math.Max(frame, 0), n - 1)

        Dim col As Integer, row As Integer
        If g.columnMajor Then
            col = frame \ g.rows
            row = frame Mod g.rows
        Else
            col = frame Mod g.cols
            row = frame \ g.cols
        End If

        Dim fw = (g.x1 - g.x0) / CSng(g.cols)
        Dim fh = (g.y1 - g.y0) / CSng(g.rows)
        Dim px0 = g.x0 + col * fw
        Dim py0 = g.y0 + row * fh

        Return New Vector4(px0 / SIZE,
                           (py0 + fh) / SIZE,
                           (px0 + fw) / SIZE,
                           py0 / SIZE)
    End Function
End Module

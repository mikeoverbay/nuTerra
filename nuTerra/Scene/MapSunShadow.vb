Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4
Imports System.Drawing

Imports OpenTK.Mathematics

''' <summary>
''' One orthographic depth render of the whole map from the sun, baked at map load
''' and sampled per frame in deferred.frag.
'''
''' This and the live cascades are two halves of one system, split by what moves:
''' the cascades carry trees, because trees will be animated and nothing that
''' moves can live in a bake that happens once; this carries everything static, at
''' every distance, where the furthest cascade stops at 250 m.
'''
''' WHAT GOES IN, and what deliberately does not:
'''
'''   terrain chunks   yes - draw_terrain, theMap.render_set
'''   static models    yes - draw_models, indirect_shadow_mapping
'''   trees            NO  - they animate; the cascades have them
'''   outland          NO  - it is backdrop outside the playable footprint, and
'''                          the ortho box below is fitted to the terrain grid.
'''                          Enclosing the outland would blow the box up and cost
'''                          texel density everywhere that matters, to shadow
'''                          scenery nobody drives on.
'''
''' It is sampled in the final render rather than baked into the VT pages, which
''' is where it started. A page is built once, long before anything is drawn on
''' top of it, so a shadow written there landed ahead of the projected decals -
''' and ahead of the ambient/direct split, which is what made shade read as black
''' instead of sky-lit. Sampling in deferred.frag puts it after both and reaches
''' the static models, which a terrain page never could. It costs four taps per
''' lit pixel; the old way was free but wrong in three places.
'''
''' Size is chosen to hit TARGET_TEXEL over the fitted box, capped at MAX_SIZE.
''' The box is fitted to the map's silhouette at the current sun angle and squared
''' up, so near/far bracket the geometry instead of standing three times deeper
''' than it - which is what left a 16 bit buffer with about eleven usable bits and
''' the stair-stepped edges that went with them.
''' </summary>
Public Class MapSunShadow
    Implements IDisposable

    ReadOnly scene As MapScene

    '''<summary>
    ''' Target metres per texel. This, not MAX_SIZE, is what actually decides the
    ''' size - MAX_SIZE only caps it. At 0.15 a ~1900 m box asks for 12953 and
    ''' lands on 16384, so raising the cap alone changed nothing. 0.05 asks for
    ''' ~38900 and pins it to the cap on any real map.
    '''</summary>
    Public Shared TARGET_TEXEL As Single = 0.05F

    '''<summary>
    ''' Cap on the bake's edge length. Doubling an edge quadruples the texels, so
    ''' this is the one constant here that can bankrupt the card: at 16 bit,
    ''' 8192 is 128 MiB, 16384 is 512 MiB and 32768 is 2 GiB.
    '''
    ''' Clamped twice before it is used - against GL_MAX_TEXTURE_SIZE, and against
    ''' VRAM_BUDGET below. Taking "max texture size" literally on this card would
    ''' ask for 2 GiB of an 8 GB board with the VT atlas and cascades already in
    ''' it, and an allocation that big does not fail cleanly - it thrashes, which
    ''' would read as "the baked shadow costs frame time" when it does not.
    '''</summary>
    Public Shared MAX_SIZE As Integer = 32768

    '''<summary>
    ''' Ceiling on the bake as a fraction of total VRAM. The size steps back down
    ''' a power of two at a time until it fits, and says so in the log.
    '''</summary>
    Public Shared VRAM_BUDGET As Single = 0.25F
    Public Shared MIN_SIZE As Integer = 2048

    '''<summary>
    ''' Cap on the bake when MSM is on. Moments are pre-blurred and mipmapped, so
    ''' resolution buys far less than it does for PCF - the filtering is what
    ''' makes the edge, not the texel count. 4096 at RGBA32F is ~341 MiB with the
    ''' mip chain, against 2 GiB for the 32768 depth map it replaces.
    '''</summary>
    Public Shared MSM_MAX_SIZE As Integer = 4096

    Public fbo As GLFramebuffer
    Public depth_tex As GLTexture

    '''<summary>Four power moments per texel, blurred and mipmapped at bake time.</summary>
    Public moment_tex As GLTexture
    Public msm_ready As Boolean
    Public size As Integer
    Public sun_view_proj As Matrix4
    Public ready As Boolean

    ' What the last bake actually used. Kept so a snapshot can re-report the map
    ' long after the bake, rather than only at load time when nobody is looking.
    Public bake_centre As Vector3
    Public bake_near As Single
    Public bake_far As Single
    Public bake_ortho_w As Single

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Private Shared Function pow2_at_least(v As Single) As Integer
        ' Whatever MAX_SIZE says, the driver has the final word. Asking for more
        ' than GL_MAX_TEXTURE_SIZE fails the allocation rather than degrading.
        Dim cap = MAX_SIZE
        If GLCapabilities.maxTextureSize > 0 Then
            cap = Math.Min(cap, GLCapabilities.maxTextureSize)
        End If

        Dim s = MIN_SIZE
        While s < v AndAlso s < cap
            s *= 2
        End While
        s = Math.Min(s, cap)

        ' Step back down until it fits the VRAM budget. Better a smaller bake
        ' than an allocation that evicts everything else on the card.
        If GLCapabilities.total_mem_mb > 0 Then
            Dim budget = CLng(GLCapabilities.total_mem_mb) * 1024L * 1024L
            budget = CLng(budget * VRAM_BUDGET)
            Dim asked = s
            While s > MIN_SIZE AndAlso depth_bytes(s) > budget
                s \= 2
            End While
            If s <> asked Then
                LogThis("sun shadow: {0}x{0} would be {1} MiB, over the {2} MiB budget - using {3}x{3} ({4} MiB)",
                        asked, depth_bytes(asked) \ (1024L * 1024L),
                        budget \ (1024L * 1024L), s, depth_bytes(s) \ (1024L * 1024L))
            End If
        End If

        Return s
    End Function

    '''<summary>Bytes the depth target occupies at a given edge length.</summary>
    Private Shared Function depth_bytes(edge As Integer) As Long
        Return CLng(edge) * CLng(edge) * 2L   ' DepthComponent16
    End Function

    ''' <summary>
    ''' Renders the map's depth from the sun. Call once a map is loaded, and again
    ''' whenever the sun moves - the result is only valid for one sun direction.
    ''' </summary>
    Public Sub Bake()
        ready = False

        ' The terrain's true world footprint, taken from the same expressions
        ' PageLoader.LoadPage uses to place a page. Note the asymmetry - X has no
        ' offset, Z is shifted back one chunk - which is exactly why deriving
        ' this by hand kept putting the centre half a chunk out.
        Dim wx_min = 100.0F * b_x_min
        Dim wx_max = 100.0F * (b_x_max + 1)
        Dim wz_min = 100.0F * (b_y_min - 1)
        Dim wz_max = 100.0F * b_y_max

        If wx_max - wx_min <= 0.0F OrElse wz_max - wz_min <= 0.0F Then
            LogThis("sun shadow: map extent is zero - skipped")
            Return
        End If

        ' Models stand above MAX_MAP_HEIGHT and some overhang the edge chunks, so
        ' pad rather than clip them out of their own shadow.
        Const EDGE_MARGIN As Single = 100.0F
        Const HEIGHT_MARGIN As Single = 250.0F

        Dim box_min As New Vector3(wx_min - EDGE_MARGIN, MIN_MAP_HEIGHT - EDGE_MARGIN, wz_min - EDGE_MARGIN)
        Dim box_max As New Vector3(wx_max + EDGE_MARGIN, MAX_MAP_HEIGHT + HEIGHT_MARGIN, wz_max + EDGE_MARGIN)
        Dim centre = (box_min + box_max) * 0.5F

        ' LIGHT_POS must already be set for this map - load_map calls
        ' set_light_pos immediately above the bake. The guard is here because the
        ' failure is silent and total: normalizing a zero vector gives NaN, the
        ' view matrix goes all-NaN, every vertex goes NaN, and the depth map comes
        ' back exactly as cleared. Better a visibly wrong sun than a blank bake.
        Dim dir = LIGHT_POS
        If dir.LengthSquared < 1.0E-6F Then
            LogThis("sun shadow: LIGHT_POS is zero at bake time - using a fallback direction")
            dir = New Vector3(0.4F, 1.0F, 0.3F)
        End If
        Dim light_dir = Vector3.Normalize(dir)

        Dim up = If(Math.Abs(light_dir.Y) > 0.99F, New Vector3(0.0F, 0.0F, 1.0F), New Vector3(0.0F, 1.0F, 0.0F))

        ' Pull the eye clear of the box along the sun axis. For an orthographic
        ' projection the eye distance changes no framing at all - only whether
        ' near/far bracket the geometry - so the diagonal is simply a distance
        ' that cannot be inside the box whatever angle the sun is at.
        Dim span = (box_max - box_min).Length
        Dim eye = centre + light_dir * span
        Dim view = Matrix4.LookAt(eye, centre, up)

        ' Fit the box to the map's actual silhouette from this sun angle instead
        ' of assuming the worst-case diagonal on every axis. The old
        ' -half..half by 0..span*2+extent box was roughly three times deeper
        ' than the map, which is what left the measured depths sitting in a
        ' fraction of the range with the rest of the bits doing nothing.
        Dim lo As New Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue)
        Dim hi As New Vector3(Single.MinValue, Single.MinValue, Single.MinValue)
        For c = 0 To 7
            Dim corner As New Vector3(If((c And 1) = 0, box_min.X, box_max.X),
                                      If((c And 2) = 0, box_min.Y, box_max.Y),
                                      If((c And 4) = 0, box_min.Z, box_max.Z))
            Dim v = (New Vector4(corner, 1.0F) * view).Xyz
            lo = Vector3.ComponentMin(lo, v)
            hi = Vector3.ComponentMax(hi, v)
        Next

        ' Square the box up around its own centre. A non-square box on a square
        ' texture gives anisotropic texels, and the coarse axis is what a shadow
        ' edge then staircases along.
        Dim ortho_w = Math.Max(hi.X - lo.X, hi.Y - lo.Y)
        Dim mid_x = (lo.X + hi.X) * 0.5F
        Dim mid_y = (lo.Y + hi.Y) * 0.5F
        Dim half = ortho_w * 0.5F

        ' Size from the box we actually ended up with, so TARGET_TEXEL means what
        ' it says. Sizing off the raw map extent measured a box we no longer use.
        Dim want = pow2_at_least(ortho_w / TARGET_TEXEL)
        If MSM_SHADOW_ENABLED Then want = Math.Min(want, MSM_MAX_SIZE)

        ' Rebuild the targets when the size moves, and whenever the method moves -
        ' MSM needs a colour attachment the depth-only path does not have.
        If depth_tex Is Nothing OrElse size <> want OrElse msm_ready <> MSM_SHADOW_ENABLED Then
            Dispose_gl()
            size = want
            create_target()
        End If

        ' OpenTK's LookAt looks down -Z, so distance from the eye is -z and the
        ' nearest corner carries the largest z.
        Dim near_d = -hi.Z
        Dim far_d = -lo.Z

        Dim proj = Matrix4.CreateOrthographicOffCenter(
            mid_x - half, mid_x + half,
            mid_y - half, mid_y + half,
            near_d, far_d)

        ' ClipDepthMode.ZeroToOne - remap whatever -1..1 OpenTK produced onto
        ' 0..1. Written as a rescale rather than derived constants so it stays
        ' correct whatever convention the ortho used.
        proj.M33 *= 0.5F
        proj.M43 = (proj.M43 + 1.0F) * 0.5F

        sun_view_proj = view * proj

        Dim extent = Math.Max(wx_max - wx_min, wz_max - wz_min)

        bake_centre = centre
        bake_near = near_d
        bake_far = far_d
        bake_ortho_w = ortho_w

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, size, size)
        GL.ClearDepth(1.0)
        GL.Clear(ClearBufferMask.DepthBufferBit)

        ' Plain depth ordering here, not the reversed-Z the main pass uses. Both
        ' ClearDepth and DepthFunc are global and set once at startup for
        ' reversed-Z, so they have to go back exactly as they were - leaving
        ' ClearDepth at 1.0 makes every later clear fail DepthFunc.Greater and
        ' the whole scene disappears behind the sky.
        GL.DepthFunc(DepthFunction.Less)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        GL.Disable(EnableCap.CullFace)
        GL.Enable(EnableCap.PolygonOffsetFill)
        GL.PolygonOffset(1.5F, 4.0F)

        draw_terrain()
        draw_models()

        If MSM_SHADOW_ENABLED Then filter_moments()

        GL.Disable(EnableCap.PolygonOffsetFill)
        GL.Enable(EnableCap.CullFace)

        ' restore the reversed-Z state the rest of the engine assumes
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        ready = True
        LogThis("sun shadow: baked {0}x{0} 16 ({1:0} MiB) over {2:0} m box ({3:0.000} m per texel), depth {4:0}..{5:0} m = {6:0} m deep, map {7:0} m",
                size, depth_bytes(size) / (1024L * 1024L),
                ortho_w, ortho_w / size, near_d, far_d, far_d - near_d, extent)

        report_depths(centre)
    End Sub

    ''' <summary>
    ''' Re-reports the current bake on demand. Same numbers Bake() logs, but
    ''' available at any time rather than only at map load.
    ''' </summary>
    Public Sub LogSnapshot()
        If Not ready OrElse depth_tex Is Nothing Then
            LogThis("  sun shadow: not baked (BAKED_SHADOW_ENABLED={0})", BAKED_SHADOW_ENABLED)
            Return
        End If

        LogThis("  sun shadow: {0}x{0} 16 ({1:0} MiB), box {2:0} m ({3:0.000} m per texel), depth {4:0}..{5:0} m = {6:0} m deep",
                size, depth_bytes(size) / (1024L * 1024L),
                bake_ortho_w, bake_ortho_w / size, bake_near, bake_far, bake_far - bake_near)
        report_depths(bake_centre)
    End Sub

    ''' <summary>
    ''' Reads a block out of the middle of the baked map and reports what is in
    ''' it, next to what the map centre should project to. These two numbers say
    ''' which side of the shadow is broken:
    '''
    '''   all 1.0          - nothing was drawn, the camera misses the map
    '''   all near 0.0     - something is sitting on the near plane
    '''   measured ~= expected - the bake is right and the comparison is at fault
    '''   measured /= expected - the matrix or its upload is wrong
    ''' </summary>
    Private Sub report_depths(centre As Vector3)
        Const N As Integer = 256
        If size < N Then Return

        Dim px(N * N - 1) As Single
        Dim off = (size - N) \ 2
        GL.GetTextureSubImage(depth_tex.texture_id, 0,
                              off, off, 0, N, N, 1,
                              PixelFormat.DepthComponent, PixelType.Float,
                              px.Length * 4, px)

        Dim lo = Single.MaxValue, hi As Single = Single.MinValue
        Dim sum As Double = 0
        Dim cleared = 0
        For Each v In px
            If v < lo Then lo = v
            If v > hi Then hi = v
            sum += v
            If v >= 1.0F Then cleared += 1
        Next

        Const SCALE As Single = 1.0F
        ' What the map centre ought to land on, by the same matrix the shader uses.
        ' No 0.5 + 0.5 here - sun_view_proj now emits 0..1 directly.
        Dim c = New Vector4(centre, 1.0F) * sun_view_proj
        Dim expected = c.Z / c.W

        LogThis("sun shadow depths (centre {0}x{0}): min={1:0.0000} max={2:0.0000} mean={3:0.0000} cleared={4:0.0}%  expected~{5:0.0000}",
                N, lo / SCALE, hi / SCALE, (sum / px.Length) / SCALE,
                100.0 * cleared / px.Length, expected)
    End Sub

    Private Sub draw_terrain()
        If Not scene.TERRAIN_LOADED Then Return

        sunDepthTerrainShader.Use()
        GL.UniformMatrix4(sunDepthTerrainShader("sunViewProj"), False, sun_view_proj)

        scene.terrain.all_chunks_vao.Bind()
        scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)

        Dim drawn = 0
        For i = 0 To theMap.render_set.Length - 1
            GL.DrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                                    New IntPtr(i * Marshal.SizeOf(Of DrawElementsIndirectCommand)))
            drawn += 1
        Next
        LogThis("sun shadow: drew terrain ({0} chunks)", drawn)

        sunDepthTerrainShader.StopUse()
    End Sub

    Private Sub draw_models()
        If Not scene.MODELS_LOADED OrElse Not DONT_BLOCK_MODELS Then Return

        sunDepthModelShader.Use()
        GL.UniformMatrix4(sunDepthModelShader("sunViewProj"), False, sun_view_proj)

        scene.static_models.allMapModels.Bind()
        scene.static_models.indirect_shadow_mapping.Bind(BufferTarget.DrawIndirectBuffer)
        LogThis("sun shadow: drew models ({0} commands)", scene.static_models.indirectShadowMappingDrawCount)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                                     IntPtr.Zero, scene.static_models.indirectShadowMappingDrawCount, 0)

        sunDepthModelShader.StopUse()
    End Sub

    Private Sub create_target()
        depth_tex = GLTexture.Create(TextureTarget.Texture2D, "SunShadowDepth")
        depth_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        depth_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
        depth_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
        ' Outside the map reads as fully lit rather than fully shadowed.
        Dim border() As Single = {1.0F, 1.0F, 1.0F, 1.0F}
        GL.TextureParameter(depth_tex.texture_id, TextureParameterName.TextureBorderColor, border)
        depth_tex.Parameter(TextureParameterName.TextureCompareMode, CInt(TextureCompareMode.CompareRefToTexture))
        depth_tex.Parameter(TextureParameterName.TextureCompareFunc, CInt(All.Lequal))
        ' Back to 16 bit, now that the box is fitted. The original problem was
        ' never the format on its own - it was 16 bits spread over a range three
        ' times deeper than the map, leaving about eleven usable bits. With
        ' near/far bracketing the geometry the range is ~1988 m, so a 16 bit
        ' level is ~0.030 m, still several times finer than the texel footprint
        ' at any size this thing will ever be. 32f was buying precision that
        ' nothing downstream could resolve, at double the memory - and memory is
        ' the binding constraint at these edge lengths.
        depth_tex.Storage2D(1, DirectCast(InternalFormat.DepthComponent16, SizedInternalFormat), size, size)

        fbo = GLFramebuffer.Create("SunShadowFBO")
        fbo.Texture(FramebufferAttachment.DepthAttachment, depth_tex, 0)

        msm_ready = MSM_SHADOW_ENABLED
        If msm_ready Then
            ' RGBA32F for the prototype. The moments are the thing being proven;
            ' 16F halves this and the paper's 4x8 quantisation quarters it again,
            ' but neither is worth debugging until the method itself is known
            ' good on this geometry.
            moment_tex = GLTexture.Create(TextureTarget.Texture2D, "SunShadowMoments")
            moment_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            moment_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            moment_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
            moment_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
            moment_tex.Storage2D(mip_levels(size), SizedInternalFormat.Rgba32f, size, size)

            fbo.Texture(FramebufferAttachment.ColorAttachment0, moment_tex, 0)
            GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.ColorAttachment0)
            GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)
        Else
            GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.None)
            GL.NamedFramebufferReadBuffer(fbo.fbo_id, ReadBufferMode.None)
        End If

        If Not fbo.IsComplete Then
            LogThis("sun shadow: FBO incomplete at {0}x{0}", size)
        End If
    End Sub

    '''<summary>Mip levels for a square power-of-two edge.</summary>
    Private Shared Function mip_levels(edge As Integer) As Integer
        Dim n = 1
        While edge > 1
            edge \= 2
            n += 1
        End While
        Return n
    End Function

    ''' <summary>
    ''' Separable Gaussian over the moment map, then the mip chain.
    '''
    ''' This is the step PCF cannot have. A depth comparison has to be compared
    ''' before it is averaged, so a depth map can be neither blurred nor mipped,
    ''' and every bit of softness has to be paid for in taps on every frame
    ''' forever. Power moments are linear, so both are exact, and both happen
    ''' once - here - for a map that never changes.
    '''
    ''' The scratch target is allocated and thrown away per bake rather than
    ''' held: at RGBA32F it is the same size as the moment map itself, and a
    ''' re-bake is rare enough that the allocation is cheaper than the residency.
    ''' </summary>
    Private Sub filter_moments()
        If moment_tex Is Nothing Then Return

        GL_PUSH_GROUP("MapSunShadow::filter_moments")

        Dim tmp = GLTexture.Create(TextureTarget.Texture2D, "SunShadowMomentsTmp")
        tmp.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        tmp.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        tmp.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        tmp.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        tmp.Storage2D(1, SizedInternalFormat.Rgba32f, size, size)

        Dim blur_fbo = GLFramebuffer.Create("SunShadowMomentBlur")
        GL.NamedFramebufferReadBuffer(blur_fbo.fbo_id, ReadBufferMode.None)

        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        GL.Disable(EnableCap.CullFace)
        GL.Viewport(0, 0, size, size)

        msmBlurShader.Use()
        Dim step_uv = 1.0F / CSng(size)

        ' Horizontal: moment_tex -> tmp
        blur_fbo.Texture(FramebufferAttachment.ColorAttachment0, tmp, 0)
        GL.NamedFramebufferDrawBuffer(blur_fbo.fbo_id, DrawBufferMode.ColorAttachment0)
        blur_fbo.Bind(FramebufferTarget.Framebuffer)
        moment_tex.BindUnit(0)
        GL.Uniform2(msmBlurShader("direction"), step_uv, 0.0F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        ' Vertical: tmp -> moment_tex
        blur_fbo.Texture(FramebufferAttachment.ColorAttachment0, moment_tex, 0)
        GL.NamedFramebufferDrawBuffer(blur_fbo.fbo_id, DrawBufferMode.ColorAttachment0)
        blur_fbo.Bind(FramebufferTarget.Framebuffer)
        tmp.BindUnit(0)
        GL.Uniform2(msmBlurShader("direction"), 0.0F, step_uv)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        msmBlurShader.StopUse()
        GL.BindTextureUnit(0, 0)

        ' Minification filtering, which is the other thing PCF cannot do.
        GL.GenerateTextureMipmap(moment_tex.texture_id)

        blur_fbo.Dispose()
        tmp.Dispose()

        GL.Enable(EnableCap.CullFace)
        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    ''' <summary>
    ''' Draws the baked depth map as a screen overlay, to check by eye that the
    ''' sun camera actually frames the map.
    '''
    ''' What a correct bake looks like: the map's silhouette in mid greys, lighter
    ''' where ground is closer to the sun, with terrain features readable. All
    ''' black means nothing was drawn - the camera is not pointed at the map, or
    ''' the ortho box misses it. Flat uniform grey means the depth range is far
    ''' wider than the map, so nearly all precision is being wasted.
    ''' </summary>
    Public Sub DebugDraw(rect As RectangleF)
        If Not ready OrElse depth_tex Is Nothing Then Return

        GL_PUSH_GROUP("MapSunShadow::DebugDraw")

        ' The texture is a shadow sampler for its real job. Reading it as plain
        ' data through sampler2D is undefined unless comparison is off, so it goes
        ' off for the draw and back immediately after - t_mixer needs it back on.
        depth_tex.Parameter(TextureParameterName.TextureCompareMode, CInt(TextureCompareMode.None))

        shadowViewShader.Use()
        depth_tex.BindUnit(0)

        GL.UniformMatrix4(shadowViewShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(shadowViewShader("rect"),
                    rect.Left, -rect.Top, rect.Right, -rect.Bottom)

        ' The map sits in a thin slice of a depth range sized to clear the tallest
        ' thing on it, so stretch that slice out or the panel is a flat wash.
        GL.Uniform1(shadowViewShader("lo"), SHADOW_VIEW_LO)
        GL.Uniform1(shadowViewShader("hi"), SHADOW_VIEW_HI)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        shadowViewShader.StopUse()
        GL.BindTextureUnit(0, 0)

        depth_tex.Parameter(TextureParameterName.TextureCompareMode, CInt(TextureCompareMode.CompareRefToTexture))

        GL_POP_GROUP()
    End Sub

    Private Sub Dispose_gl()
        depth_tex?.Dispose()
        depth_tex = Nothing
        moment_tex?.Dispose()
        moment_tex = Nothing
        msm_ready = False
        fbo?.Dispose()
        fbo = Nothing
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose_gl()
    End Sub
End Class

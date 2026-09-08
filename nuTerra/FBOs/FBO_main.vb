Imports OpenTK.Graphics.OpenGL4

Public Class MainFBO
    Public Shared fbo As GLFramebuffer

    Public Shared width As Integer
    Public Shared height As Integer

    Public Shared gPick As GLRenderbuffer
    Public Shared gColor_2 As GLRenderbuffer
    Public Shared gSurfaceNormal As GLTexture
    Public Shared gColor As GLTexture
    Public Shared gNormal As GLTexture
    Public Shared gGMF As GLTexture
    Public Shared gDepth As GLTexture
    Public Shared gPosition As GLTexture
    Public Shared gAUX_Color As GLTexture

    ''' <summary>
    ''' The FX pass's own accumulation buffer, Rgba16f, plus the framebuffer
    ''' that targets it. It SHARES gDepth with the main FBO, so the FX still
    ''' depth-test against the scene exactly as before (both FX passes run
    ''' DepthMask(False), so nothing writes depth and sharing is safe).
    '''
    ''' Why the FX composite through here instead of straight into gColor:
    ''' gColor is Rgba8, and draw_fx runs AFTER deferred.frag has already
    ''' tonemapped. Every additive card therefore summed into a fixed-point
    ''' buffer that CLAMPS at 1.0 after each blend. Fire is roughly
    ''' (1.0, 0.6, 0.2), so the sum hit the ceiling in red first, green then
    ''' climbed to meet it, and orange turned into yellow and then white -
    ''' measured at a third of all fire pixels pinned at R=G=255, against one
    ''' such pixel in the game's own frame.
    '''
    ''' Accumulating in float16 first removes the per-step clamp. This is
    ''' sound rather than merely convenient: premultiplied "over" is
    ''' ASSOCIATIVE, so compositing the FX among themselves and then over the
    ''' scene gives the same answer as compositing them one at a time over the
    ''' scene. Additive materials emit alpha 0, so they add and attenuate
    ''' nothing, in either arrangement.
    ''' </summary>
    Public Shared gFX_HDR As GLTexture
    Public Shared fx_fbo As GLFramebuffer

    ''' <summary>
    ''' Glow ping-pong pair and the framebuffer that drives them, at
    ''' BLOOM_DIV of the main resolution.
    '''
    ''' Reduced resolution is not only a cost saving - it is what gives the
    ''' glow its radius. The blur is a fixed 9 tap kernel, so its reach in
    ''' SCREEN pixels is whatever one texel here is worth. At full resolution
    ''' the same kernel would be a barely visible smudge.
    ''' </summary>
    Public Shared gFX_BloomA As GLTexture
    ''' <summary>Viewer-only alpha-1 views of the two glow buffers. See the
    ''' note where they are made.</summary>
    Public Shared gFX_HDR_opaque As Integer
    Public Shared gFX_BloomA_opaque As Integer
    Public Shared gFX_BloomB As GLTexture
    Public Shared bloom_fbo As GLFramebuffer
    Public Shared bloom_width As Integer
    Public Shared bloom_height As Integer

    ''' <summary>Glow works at 1/this of the main buffer on each axis.</summary>
    Public Const BLOOM_DIV As Integer = 4

    ''' <summary>
    ''' Views onto gColor and gGMF with alpha forced to 1, for the Textures
    ''' viewer ONLY. Both buffers carry a MASK in alpha - water mix in gColor,
    ''' wetness in gGMF - and ImGui.Image alpha-blends, so on a dry map the
    ''' whole of gGMF drew as empty and gColor lost every model. A texture view
    ''' shares the SAME storage and overrides only the swizzle, so nothing that
    ''' samples the real texture can be affected.
    ''' </summary>
    Public Shared gColor_opaque As Integer
    Public Shared gGMF_opaque As Integer
    '========================
    ' Color Attachments
    ' color     = 0
    ' normal    = 1
    ' GMM       = 2
    ' Position  = 3
    ' Pick      = 4
    ' Aux_Color = 5
    ' gColor_2  = 6
    ' SurfaceNormal = 7
    '========================
    ' The FX accumulation buffer's only draw buffer. Both FX fragment shaders
    ' declare a single layout(location = 0) out, so one entry is all they need.
    Private Shared attach_FX_only() As DrawBuffersEnum = {
        DrawBuffersEnum.ColorAttachment0
    }

    Private Shared attach_Color_Normal_GMF() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment1,
        FramebufferAttachment.ColorAttachment2,
        FramebufferAttachment.ColorAttachment3,
        FramebufferAttachment.ColorAttachment7,
        FramebufferAttachment.ColorAttachment4
    }
    Private Shared attach_Color_Normal_GMF_aux_fmask() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment1,
        FramebufferAttachment.ColorAttachment2,
        FramebufferAttachment.ColorAttachment3,
        FramebufferAttachment.ColorAttachment5,
        FramebufferAttachment.ColorAttachment4
    }
    Private Shared attach_Color_SurfaceNormal_Normal_GMF_Position() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment1,
        FramebufferAttachment.ColorAttachment2,
        FramebufferAttachment.ColorAttachment3,
        FramebufferAttachment.ColorAttachment7
    }
    Private Shared attach_Color() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0
    }
    Private Shared attach_ColorNormal() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment1
    }
    ' Colour + normal + gGMF, for the decal pass. gGMF is ColorAttachment2
    ' and every G-buffer writer declares it at output location 2, so it has
    ' to sit at index 2 here for a decal to reach it at all. The decal pass
    ' masks it to alpha only, and only while a wet decal draws.
    Private Shared attach_ColorNormalGMF() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment1,
        FramebufferAttachment.ColorAttachment2
    }
    Private Shared attach_Color_1_2() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment6
    }
    Private Shared attach_Color_GMF() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment0,
        FramebufferAttachment.ColorAttachment2
    }
    Private Shared attach_Normal() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment1
    }
    Private Shared attach_Color_2() As DrawBuffersEnum = {
        FramebufferAttachment.ColorAttachment6
    }

    Public Shared Sub Initialize(_width As Integer, _height As Integer)
        width = _width
        height = _height

        delete_textures_and_fbo()
        create_textures()

        If Not create_fbo() Then
            MsgBox("Failed to create main FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
            End
        End If
    End Sub

    Public Shared Sub delete_textures_and_fbo()
        ' Views first - they borrow the storage the textures below own.
        delete_view(gColor_opaque)
        delete_view(gGMF_opaque)
        delete_view(gFX_HDR_opaque)
        delete_view(gFX_BloomA_opaque)

        ' as the name says
        gColor?.Dispose()
        gSurfaceNormal?.Dispose()
        gAUX_Color?.Dispose()
        gNormal?.Dispose()
        gGMF?.Dispose()
        gDepth?.Dispose()
        gPick?.Dispose()
        gColor_2?.Dispose()
        gPosition?.Dispose()
        gFX_HDR?.Dispose()
        gFX_BloomA?.Dispose()
        gFX_BloomB?.Dispose()
        bloom_fbo?.Dispose()
        ' Before fbo: fx_fbo borrows gDepth, which fbo owns.
        fx_fbo?.Dispose()
        fbo?.Dispose()
    End Sub

    Public Shared Sub create_textures()
        ' gColor ------------------------------------------------------------------------------------------
        ' RGBA8
        gColor = GLTexture.Create(TextureTarget.Texture2D, "gColor")
        gColor.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

        ' gSurfaceNormal ------------------------------------------------------------------------------------------
        ' RGB
        gSurfaceNormal = GLTexture.Create(TextureTarget.Texture2D, "gSurfaceNormal")
        gSurfaceNormal.Storage2D(1, DirectCast(InternalFormat.Rgb8, SizedInternalFormat), width, height)

        ' AUX_gColor -----------------------------------------------------------------------------------
        ' RGBA8
        gAUX_Color = GLTexture.Create(TextureTarget.Texture2D, "AUX_gColor")
        gAUX_Color.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

        ' gNormal ------------------------------------------------------------------------------------------
        ' 3 color : normal in RGB
        gNormal = GLTexture.Create(TextureTarget.Texture2D, "gNormal")
        gNormal.Storage2D(1, DirectCast(InternalFormat.Rgb8, SizedInternalFormat), width, height)

        ' gGM_Flag ------------------------------------------------------------------------------------------
        ' 4 color int : GM in RG : Flag in b : Wetness in a
        gGMF = GLTexture.Create(TextureTarget.Texture2D, "gGMF")
        gGMF.Storage2D(1, DirectCast(InternalFormat.Rgba8, SizedInternalFormat), width, height)

        ' gPosition ------------------------------------------------------------------------------------------
        ' RGB16F
        gPosition = GLTexture.Create(TextureTarget.Texture2D, "gPosition")
        gPosition.Storage2D(1, DirectCast(InternalFormat.Rgb16f, SizedInternalFormat), width, height)

        ' gDepth ------------------------------------------------------------------------------------------
        ' DepthComponent32f
        gDepth = GLTexture.Create(TextureTarget.Texture2D, "gDepth")
        gDepth.Storage2D(1, DirectCast(PixelInternalFormat.DepthComponent32f, SizedInternalFormat), width, height)

        ' gPick ------------------------------------------------------------------------------------------
        ' R16 uInt
        gPick = GLRenderbuffer.Create("gPick")
        gPick.Storage(RenderbufferStorage.R16ui, width, height)

        ' gColor_2 ------------------------------------------------------------------------------------------
        ' RGBA8
        gColor_2 = GLRenderbuffer.Create("gColor_2")
        gColor_2.Storage(RenderbufferStorage.Rgba8, width, height)

        ' gFX_HDR ------------------------------------------------------------------------------------------
        ' RGBA16F - the FX accumulation target. Float on purpose: the whole
        ' point is that sums above 1.0 survive to be rolled off with their hue
        ' intact instead of being clamped channel by channel.
        gFX_HDR = GLTexture.Create(TextureTarget.Texture2D, "gFX_HDR")
        gFX_HDR.Storage2D(1, DirectCast(InternalFormat.Rgba16f, SizedInternalFormat), width, height)
        ' LINEAR, and it matters: fx_bright reads this at 1/BLOOM_DIV to build
        ' the glow, so the filter is half of that downsample. On Nearest it was
        ' point sampling one pixel per 4x4 block and dropping the other fifteen,
        ' which is what made the bloom FLICKER whenever the camera moved: a bulb
        ' core a few pixels wide either landed on the sampled pixel or missed it
        ' entirely, and the hard threshold downstream turned that into a halo
        ' that popped on and off. fx_bright's own comment claimed Linear all
        ' along; it just was not true here.
        '
        ' Safe for the other reader: fx_composite takes this with texelFetch,
        ' which ignores filter state entirely.
        gFX_HDR.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        gFX_HDR.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        gFX_HDR.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        gFX_HDR.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)

        ' Glow ping-pong. LINEAR, unlike gFX_HDR: these are read at a different
        ' resolution than they are written, so the filter IS the down and
        ' upsample. Nearest here would make the glow blocky.
        ' ClampToEdge so a bright fire at the screen edge does not wrap its
        ' glow around to the far side.
        bloom_width = Math.Max(1, width \ BLOOM_DIV)
        bloom_height = Math.Max(1, height \ BLOOM_DIV)
        gFX_BloomA = make_bloom_target("gFX_BloomA")
        gFX_BloomB = make_bloom_target("gFX_BloomB")

        ' Viewer-only views. Must come after the textures they borrow.
        gColor_opaque = make_opaque_view(gColor)
        gGMF_opaque = make_opaque_view(gGMF)
        ' The FX pair need this MORE than the two above, not less: gFX_HDR is
        ' written with alpha ZERO on purpose - the additive contract, adds light
        ' and covers nothing - and gFX_BloomA's alpha is blurred depth. ImGui
        ' alpha-blends, so both drew as empty and read as "the buffer has no
        ' content" when in fact they were full.
        gFX_HDR_opaque = make_opaque_view(gFX_HDR)
        gFX_BloomA_opaque = make_opaque_view(gFX_BloomA)
    End Sub

    ''' <summary>One half of the glow ping-pong pair.</summary>
    Private Shared Function make_bloom_target(name As String) As GLTexture
        Dim t = GLTexture.Create(TextureTarget.Texture2D, name)
        t.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        t.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        t.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        t.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        t.Storage2D(1, DirectCast(InternalFormat.Rgba16f, SizedInternalFormat), bloom_width, bloom_height)
        Return t
    End Function

    ''' <summary>
    ''' A view onto an existing immutable texture with ALPHA SWIZZLED TO 1, for
    ''' the texture viewer. ImGui alpha-blends what it draws, and several of
    ''' these buffers carry something other than coverage in alpha - gFX_HDR is
    ''' written alpha ZERO by the additive contract, gFX_BloomA's alpha is
    ''' blurred depth - so without this they draw as nothing and read as "the
    ''' buffer is empty" when in fact they are full.
    '''
    ''' GenTextures, not CreateTextures: glTextureView requires a name that has
    ''' never had storage of its own. The view then points at the source's
    ''' storage, so there is no copy and no way for this to alter what the
    ''' shaders read.
    '''
    ''' THE FORMAT COMES FROM THE SOURCE, never from the caller. glTextureView
    ''' only permits a view inside the source's own format class, and this used
    ''' to take it as a parameter defaulting to Rgba8 - a 32-bit class format,
    ''' and therefore illegal over the 64-bit Rgba16f bloom targets. That
    ''' default does not fail visibly: glTextureView raises INVALID_OPERATION
    ''' and leaves the generated NAME with no object behind it, the swizzle
    ''' below then fails on the same name, and ImGui's bind of an invalid name
    ''' is a no-op that leaves the PREVIOUS texture bound - so the panel
    ''' cheerfully painted ImGui's own font atlas into the gFX_BloomA slot.
    ''' </summary>
    Private Shared Function make_opaque_view(src As GLTexture) As Integer
        Dim id As Integer
        GL.GenTextures(1, id)
        ' Same numeric enum values, two different OpenTK enum types.
        Dim fmt = CType(CInt(src.storage_format), PixelInternalFormat)
        GL.TextureView(id, TextureTarget.Texture2D, src.texture_id,
                       fmt, 0, 1, 0, 1)
        GL.TextureParameter(id, TextureParameterName.TextureSwizzleA, CInt(All.One))
        Return id
    End Function

    ''' <summary>Delete a view and clear the handle. A view borrows its source's
    ''' storage, so every one must go BEFORE the texture it borrows.</summary>
    Private Shared Sub delete_view(ByRef id As Integer)
        If id <> 0 Then
            GL.DeleteTexture(id)
            id = 0
        End If
    End Sub

    Public Shared Function create_fbo() As Boolean
        fbo = GLFramebuffer.Create("mainFBO")

        ' attach our render buffer textures.
        fbo.Texture(FramebufferAttachment.ColorAttachment0, gColor, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment1, gNormal, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment2, gGMF, 0)
        fbo.Texture(FramebufferAttachment.ColorAttachment3, gPosition, 0)
        fbo.Renderbuffer(FramebufferAttachment.ColorAttachment4, RenderbufferTarget.Renderbuffer, gPick)
        fbo.Texture(FramebufferAttachment.ColorAttachment5, gAUX_Color, 0)
        fbo.Renderbuffer(FramebufferAttachment.ColorAttachment6, RenderbufferTarget.Renderbuffer, gColor_2)
        fbo.Texture(FramebufferAttachment.ColorAttachment7, gSurfaceNormal, 0)

        fbo.Texture(FramebufferAttachment.DepthAttachment, gDepth, 0)

        If Not fbo.IsComplete Then
            Return False
        End If

        ' The FX accumulation framebuffer. One colour attachment and the SHARED
        ' depth texture - shared, not copied, so the FX depth-test against the
        ' scene the main pass just drew. Both FX passes run DepthMask(False),
        ' so neither can write through this alias.
        fx_fbo = GLFramebuffer.Create("fxFBO")
        fx_fbo.Texture(FramebufferAttachment.ColorAttachment0, gFX_HDR, 0)
        fx_fbo.Texture(FramebufferAttachment.DepthAttachment, gDepth, 0)
        fx_fbo.DrawBuffers(1, attach_FX_only)

        If Not fx_fbo.IsComplete Then
            Return False
        End If

        ' The glow framebuffer swaps its colour attachment between the two
        ' bloom targets as the passes ping-pong, so it is created here without
        ' one. No depth: the glow passes are full-screen quads with the depth
        ' test off. ReadBuffer None, as the moment blur does, so nothing can
        ' read back from an attachment that is about to be written.
        bloom_fbo = GLFramebuffer.Create("bloomFBO")
        GL.NamedFramebufferReadBuffer(bloom_fbo.fbo_id, ReadBufferMode.None)

        attach_CNGP()

        Return True ' No errors! all is good! :)
    End Function


    Public Shared Sub attach_CNGP()
        'attach our render buffer textures.
        If ModelPicker.Enabled Then
            fbo.DrawBuffers(6, attach_Color_Normal_GMF)
        Else
            fbo.DrawBuffers(5, attach_Color_Normal_GMF)
        End If
    End Sub

    Public Shared Sub attach_CNGPA()
        'attach our render buffer textures.
        If ModelPicker.Enabled Then
            fbo.DrawBuffers(6, attach_Color_Normal_GMF_aux_fmask)
        Else
            fbo.DrawBuffers(5, attach_Color_Normal_GMF_aux_fmask)
        End If
    End Sub

    Public Shared Sub attach_CSNGP()
        'attach our render buffer textures.
        fbo.DrawBuffers(5, attach_Color_SurfaceNormal_Normal_GMF_Position)

    End Sub

    Public Shared Sub attach_C()
        fbo.DrawBuffers(1, attach_Color)
    End Sub
    Public Shared Sub attach_CN()
        fbo.DrawBuffers(2, attach_ColorNormal)
    End Sub
    Public Shared Sub attach_CNG()
        fbo.DrawBuffers(3, attach_ColorNormalGMF)
    End Sub
    Public Shared Sub attach_C1_and_C2()
        fbo.DrawBuffers(2, attach_Color_1_2)
    End Sub
    Public Shared Sub attach_C2()
        fbo.DrawBuffers(1, attach_Color_2)
    End Sub

    Public Shared Sub attach_C_no_Depth()
        fbo.Texture(FramebufferAttachment.DepthAttachment, Nothing, 0)
        fbo.DrawBuffers(1, attach_Color)
    End Sub

    Public Shared Sub attach_Depth()
        fbo.Texture(FramebufferAttachment.DepthAttachment, gDepth, 0)
    End Sub

    Public Shared Sub attach_CF()
        fbo.DrawBuffers(2, attach_Color_GMF)
    End Sub

    Public Shared Sub attach_N()
        'This will be used to write to the normals during decal rendering. No depth needed.
        fbo.DrawBuffers(1, attach_Normal)
    End Sub
End Class

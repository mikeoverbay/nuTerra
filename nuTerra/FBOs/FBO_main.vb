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
        If gColor_opaque <> 0 Then
            GL.DeleteTexture(gColor_opaque)
            gColor_opaque = 0
        End If
        If gGMF_opaque <> 0 Then
            GL.DeleteTexture(gGMF_opaque)
            gGMF_opaque = 0
        End If

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
        gFX_HDR.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        gFX_HDR.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        gFX_HDR.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        gFX_HDR.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)

        ' Viewer-only views. Must come after the textures they borrow.
        gColor_opaque = make_opaque_view(gColor)
        gGMF_opaque = make_opaque_view(gGMF)
    End Sub

    ''' <summary>
    ''' An Rgba8 view onto an existing immutable texture, alpha swizzled to 1.
    '''
    ''' GenTextures, not CreateTextures: glTextureView requires a name that has
    ''' never had storage of its own. The view then points at the source's
    ''' storage, so there is no copy and no way for this to alter what the
    ''' shaders read.
    ''' </summary>
    Private Shared Function make_opaque_view(src As GLTexture) As Integer
        Dim id As Integer
        GL.GenTextures(1, id)
        GL.TextureView(id, TextureTarget.Texture2D, src.texture_id,
                       PixelInternalFormat.Rgba8, 0, 1, 0, 1)
        GL.TextureParameter(id, TextureParameterName.TextureSwizzleA, CInt(All.One))
        Return id
    End Function

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

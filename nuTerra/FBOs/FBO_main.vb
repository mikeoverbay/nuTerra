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

Imports OpenTK.Graphics.OpenGL
Imports OpenTK

Module FBO_shadowBaker_mod
    Public FBO_ShadowBaker_ID As Integer = 0

    ' This is used to prerender the terrain.
    ' The gGmmArray is only here for future use. Decals if there are rendered during at this time.

    ''' <summary>
    ''' Creates the mix FBO
    ''' </summary>
    Public NotInheritable Class FBO_ShadowBaker
        Public Shared depth_map_size As Integer = 512
        Public Shared gBakerColorArray, shadow_map, gDepth, gDepthMask As GLTexture
        Public Shared texture_size As Point
        Public Shared LayerCount As Integer
        Public Shared mipCount As Integer
        Private Shared attactments() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0}

        Public Shared Sub FBO_Initialize(ByVal size As Point)
            texture_size = size

            frmMain.glControl_main.MakeCurrent()

            delete_textures_and_fbo()
            create_textures()

            If Not create_fbo() Then
                MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                End
            End If

        End Sub

        Public Shared Function FBO_Make_Ready_For_Shadow_writes() As Boolean

            GL.NamedFramebufferTexture(FBO_ShadowBaker_ID, FramebufferAttachment.ColorAttachment0, shadow_map.texture_id, 0)
            'Need a deepth attachment
            GL.NamedFramebufferTexture(FBO_ShadowBaker_ID, FramebufferAttachment.DepthAttachment, gDepth.texture_id, 0)

            GL.NamedFramebufferDrawBuffers(FBO_ShadowBaker_ID, 1, attactments)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_ShadowBaker_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If
            Return True ' No errors! all is good! :)
        End Function

        Public Shared Function FBO_Make_Ready_For_mask_writes(ByVal layer As Integer) As Boolean

            GL.NamedFramebufferTextureLayer(FBO_ShadowBaker_ID, FramebufferAttachment.ColorAttachment0, gBakerColorArray.texture_id, 0, layer)
            'dont need depth attachment for creating shadow masks
            GL.NamedFramebufferTexture(FBO_ShadowBaker_ID, FramebufferAttachment.DepthAttachment, gDepthMask.texture_id, 0)

            GL.NamedFramebufferDrawBuffers(FBO_ShadowBaker_ID, 1, attactments)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_ShadowBaker_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If
            Return True ' No errors! all is good! :)

        End Function


        Public Shared Sub attach_depth_texture()
            GL.NamedFramebufferDrawBuffers(FBO_ShadowBaker_ID, 1, attactments)
            GL.NamedFramebufferTexture(FBO_ShadowBaker_ID, FramebufferAttachment.ColorAttachment0, shadow_map.texture_id, 0)

            Dim er2 = GL.GetError
            If er2 <> 0 Then
                Stop
            End If
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gBakerColorArray IsNot Nothing Then gBakerColorArray.Delete()
            If shadow_map IsNot Nothing Then shadow_map.Delete()
            If gDepth IsNot Nothing Then gDepth.Delete()
            If gDepthMask IsNot Nothing Then gDepthMask.Delete()
            If FBO_ShadowBaker_ID > 0 Then GL.DeleteFramebuffer(FBO_ShadowBaker_ID)
        End Sub

        Public Shared Sub clean_up()
            If shadow_map IsNot Nothing Then shadow_map.Delete()
            If gDepth IsNot Nothing Then gDepth.Delete()
            If gDepthMask IsNot Nothing Then gDepthMask.Delete()
            If FBO_ShadowBaker_ID > 0 Then GL.DeleteFramebuffer(FBO_ShadowBaker_ID)
        End Sub

        Public Shared Sub create_textures()
            'we should initialize layers for each mipmap level
            'For mip = 0 To mipCount - 1

            ' gColorArray ------------------------------------------------------------------------------------------
            gBakerColorArray = CreateTexture(TextureTarget.Texture2DArray, "gBakerColorArray")
            gBakerColorArray.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            gBakerColorArray.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            gBakerColorArray.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
            gBakerColorArray.Parameter(TextureParameterName.TextureBaseLevel, 0)
            gBakerColorArray.Parameter(TextureParameterName.TextureMaxLevel, mipCount - 1)
            gBakerColorArray.Parameter(TextureParameterName.TextureWrapS, TextureParameterName.ClampToBorder)
            gBakerColorArray.Parameter(TextureParameterName.TextureWrapT, TextureParameterName.ClampToBorder)
            gBakerColorArray.Storage3D(mipCount, SizedInternalFormat.R8, texture_size.X, texture_size.Y, LayerCount)

            ' gBakerShadowDepth ------------------------------------------------------------------------------------
            shadow_map = CreateTexture(TextureTarget.Texture2D, "shadow_map")
            shadow_map.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
            shadow_map.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            shadow_map.Parameter(TextureParameterName.TextureWrapS, TextureParameterName.ClampToBorder)
            shadow_map.Parameter(TextureParameterName.TextureWrapT, TextureParameterName.ClampToBorder)
            shadow_map.Storage2D(1, SizedInternalFormat.Rg32f, depth_map_size, depth_map_size)

            ' gDepth ------------------------------------------------------------------------------------------
            ' DepthComponent32f
            gDepth = CreateTexture(TextureTarget.Texture2D, "gDepth")
            gDepth.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gDepth.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gDepth.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gDepth.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gDepth.Storage2D(1, DirectCast(PixelInternalFormat.DepthComponent24, SizedInternalFormat), depth_map_size, depth_map_size)

            ' gDepthMask ------------------------------------------------------------------------------------------
            ' DepthComponent32f
            gDepthMask = CreateTexture(TextureTarget.Texture2D, "gDepthMask")
            gDepthMask.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gDepthMask.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gDepthMask.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
            gDepthMask.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
            gDepthMask.Storage2D(1, DirectCast(PixelInternalFormat.DepthComponent24, SizedInternalFormat), texture_size.X, texture_size.Y)
        End Sub

        Public Shared Function create_fbo() As Boolean
            FBO_ShadowBaker_ID = CreateFramebuffer("ShadowBaker")

            'attach our textureArray to colorAttachment0, mip 0 and level 0
            GL.NamedFramebufferTextureLayer(FBO_ShadowBaker_ID, FramebufferAttachment.ColorAttachment0, gBakerColorArray.texture_id, 0, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_ShadowBaker_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function

        Public Shared Sub make_mips()
            gBakerColorArray.GenerateMipmap()
        End Sub


    End Class

End Module

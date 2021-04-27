Imports OpenTK.Graphics.OpenGL
Imports OpenTK

Module FBO_Mega
    Public FBO_Mega_ID As Integer = 0

    ' This is used to prerender the terrain.
    ' The gGmmArray is only here for future use. Decals if there are rendered during at this time.

    ''' <summary>
    ''' Creates the mix FBO
    ''' </summary>
    Public NotInheritable Class FBO_Mega_set
        Public Shared gColor, gNormal, gGmm As GLTexture
        Public Shared max_mip_level As Integer = 1
        Private Shared width As Integer
        Private Shared height As Integer
        Private Shared attactments() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0, FramebufferAttachment.ColorAttachment1, FramebufferAttachment.ColorAttachment2}

        Public Shared Sub FBO_Initialize(_width As Integer, _height As Integer)
            width = _width
            height = _height

            frmMain.glControl_main.MakeCurrent()

            delete_textures_and_fbo()
            create_textures()

            If Not create_fbo() Then
                MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                End
            End If

        End Sub

        Public Shared Sub attach()
            GL.NamedFramebufferDrawBuffers(FBO_Mega_ID, 3, attactments)
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment1, gNormal.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment2, gGmm.texture_id, 0)

            Dim er2 = GL.GetError
            If er2 <> 0 Then Stop
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gColor IsNot Nothing Then gColor.Delete()
            If gNormal IsNot Nothing Then gNormal.Delete()
            If gGmm IsNot Nothing Then gGmm.Delete()
            If FBO_Mega_ID > 0 Then GL.DeleteFramebuffer(FBO_Mega_ID)
        End Sub

        Public Shared Sub create_textures()
            ' gColor ------------------------------------------------------------------------------------------
            gColor = CreateTexture(TextureTarget.Texture2D, "FBO_Mega_gColor")
            gColor.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gColor.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gColor.Parameter(TextureParameterName.TextureMaxLevel, max_mip_level)
            gColor.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

            ' gNormal ------------------------------------------------------------------------------------------
            gNormal = CreateTexture(TextureTarget.Texture2D, "FBO_Mega_gNormal")
            gNormal.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gNormal.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gNormal.Parameter(TextureParameterName.TextureMaxLevel, max_mip_level)
            gNormal.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

            ' gGmmArray ------------------------------------------------------------------------------------------
            gGmm = CreateTexture(TextureTarget.Texture2D, "FBO_Mega_gGmm")
            gGmm.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            gGmm.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            gGmm.Parameter(TextureParameterName.TextureMaxLevel, max_mip_level)
            gGmm.Storage2D(1, SizedInternalFormat.Rgba8, width, height)
        End Sub

        Public Shared Function create_fbo() As Boolean
            FBO_Mega_ID = CreateFramebuffer("Mega")

            'attach our textures
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment0, gColor.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment1, gNormal.texture_id, 0)
            GL.NamedFramebufferTexture(FBO_Mega_ID, FramebufferAttachment.ColorAttachment2, gGmm.texture_id, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_Mega_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True
        End Function

    End Class

End Module

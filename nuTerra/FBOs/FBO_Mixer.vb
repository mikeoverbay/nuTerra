Imports OpenTK.Graphics.OpenGL
Imports OpenTK

Module FBO_Mixer
    Public FBO_Mixer_ID As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBO_mixer_set
        Private Shared old_texture_size As Point
        Public Shared gColorArray, gNormalArray, gGmmArray As GLTexture
        Public Shared texture_size As Point
        Public Shared LayerCount As Integer
        Public Shared mipCount As Integer
        Private Shared attactments() As DrawBuffersEnum = {FramebufferAttachment.ColorAttachment0, FramebufferAttachment.ColorAttachment1, FramebufferAttachment.ColorAttachment2}

        Public Shared Sub FBO_Initialize(ByVal size As Point)
            texture_size = size

            frmMain.glControl_main.MakeCurrent()

            ' Stop changing the size becuase of excessive window resize calls.
            If texture_size <> old_texture_size Then

                delete_textures_and_fbo()

                'mipCount = 1 + Math.Floor(Math.Log(Math.Max(texture_size.X, texture_size.Y), 2))

                create_arraytextures()

                If Not create_fbo() Then
                    MsgBox("Failed to create mini FBO" + vbCrLf + "I must shut down!", MsgBoxStyle.Exclamation, "We're Screwed!")
                    End
                End If
                'set new size
                old_texture_size = texture_size
            End If
        End Sub

        Public Shared Sub attach_array_layer(ByVal layer As Integer)
            GL.NamedFramebufferDrawBuffers(FBO_Mixer_ID, 3, attactments)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment0, gColorArray.texture_id, 0, layer)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment1, gNormalArray.texture_id, 0, layer)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment2, gGmmArray.texture_id, 0, layer)

            GL.Finish() 'make sure we are done

            Dim er2 = GL.GetError
            If er2 <> 0 Then
                Stop
            End If
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gColorArray IsNot Nothing Then gColorArray.Delete()
            If gNormalArray IsNot Nothing Then gNormalArray.Delete()
            If gGmmArray IsNot Nothing Then gGmmArray.Delete()
            If FBO_Mixer_ID > 0 Then GL.DeleteFramebuffer(FBO_Mixer_ID)
        End Sub

        Public Shared Sub create_arraytextures()
            'we should initialize layers for each mipmap level
            'For mip = 0 To mipCount - 1

            ' gColorArray ------------------------------------------------------------------------------------------
            gColorArray = CreateTexture(TextureTarget.Texture2DArray, "gColorArray")
            gColorArray.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            gColorArray.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            gColorArray.Parameter(TextureParameterName.TextureBaseLevel, 0)
            gColorArray.Parameter(TextureParameterName.TextureMaxLevel, mipCount - 1)
            gColorArray.Parameter(TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            gColorArray.Parameter(TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)
            gColorArray.Storage3D(mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)

            ' gNormalArray ------------------------------------------------------------------------------------------
            gNormalArray = CreateTexture(TextureTarget.Texture2DArray, "gNormalArray")
            gNormalArray.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            gNormalArray.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            gNormalArray.Parameter(TextureParameterName.TextureBaseLevel, 0)
            gNormalArray.Parameter(TextureParameterName.TextureMaxLevel, mipCount - 1)
            gNormalArray.Parameter(TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            gNormalArray.Parameter(TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)
            gNormalArray.Storage3D(mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)

            ' gGmmArray ------------------------------------------------------------------------------------------
            gGmmArray = CreateTexture(TextureTarget.Texture2DArray, "gGmmArray")
            gGmmArray.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            gGmmArray.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            gGmmArray.Parameter(TextureParameterName.TextureBaseLevel, 0)
            gGmmArray.Parameter(TextureParameterName.TextureMaxLevel, mipCount - 1)
            gGmmArray.Parameter(TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            gGmmArray.Parameter(TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)
            gGmmArray.Storage3D(mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)
        End Sub

        Public Shared Function create_fbo() As Boolean
            FBO_Mixer_ID = CreateFramebuffer("Mixer")

            'attach our textureArray to colorAttachment0, mip 0 and level 0
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment0, gColorArray.texture_id, 0, 0)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment1, gNormalArray.texture_id, 0, 0)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment2, gGmmArray.texture_id, 0, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_Mixer_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function

        Public Shared Sub make_mips()
            gColorArray.GenerateMipmap()
            gNormalArray.GenerateMipmap()
            gGmmArray.GenerateMipmap()
        End Sub


    End Class

End Module

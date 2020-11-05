Imports OpenTK.Graphics.OpenGL
Imports OpenTK

Module FBO_Mixer
    Public FBO_Mixer_ID As Integer = 0

    ''' <summary>
    ''' Creates the main rendering FBO
    ''' </summary>
    Public NotInheritable Class FBO_mixer_set
        Private Shared old_texture_size As Point
        Public Shared gColorArray, gNormalArray, gGmmArray As Integer
        Public Shared texture_size As Point
        Public Shared LayerCount As Integer
        Public Shared mipCount As Integer
        Public Shared Sub FBO_Initialize(ByVal size As Point)
            texture_size = size

            frmMain.glControl_main.MakeCurrent()

            ' Stop changing the size becuase of excessive window resize calls.
            If texture_size <> old_texture_size Then

                delete_textures_and_fbo()

                mipCount = 2 '1 + Math.Floor(Math.Log(Math.Max(texture_size.X, texture_size.Y), 2))

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
            Dim er1 = GL.GetError
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment0, gColorArray, 0, layer)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment1, gNormalArray, 0, layer)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment2, gGmmArray, 0, layer)
            GL.Finish() 'make sure we are done
            Dim er2 = GL.GetError
            If er2 <> 0 Then
                Stop
            End If
        End Sub

        Public Shared Sub delete_textures_and_fbo()
            'as the name says
            If gColorArray > 0 Then
                GL.DeleteTexture(gColorArray)
            End If
            If gNormalArray > 0 Then
                GL.DeleteTexture(gNormalArray)
            End If
            If gGmmArray > 0 Then
                GL.DeleteTexture(gGmmArray)
            End If
            If FBO_Mixer_ID > 0 Then
                GL.DeleteFramebuffer(FBO_Mixer_ID)
            End If

            GL.Finish() '<-- Make sure they are gone!
        End Sub

        Public Shared Sub create_arraytextures()
            Dim er1 = GL.GetError

            Const target = TextureTarget.Texture2DArray
            gColorArray = CreateTexture(target, "gColorArray")
            gNormalArray = CreateTexture(target, "gNormalArray")
            gGmmArray = CreateTexture(target, "gGmmArray")
            'we should initialize layers for each mipmap level
            'For mip = 0 To mipCount - 1

            ' gColorArray ------------------------------------------------------------------------------------------
            TextureParameter(target, gColorArray, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            TextureParameter(target, gColorArray, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            TextureParameter(target, gColorArray, TextureParameterName.TextureBaseLevel, 0)
            TextureParameter(target, gColorArray, TextureParameterName.TextureMaxLevel, mipCount - 1)
            TextureParameter(target, gColorArray, TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            TextureParameter(target, gColorArray, TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)

            GL.TextureStorage3D(gColorArray, mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)
            Dim er2 = GL.GetError

            ' gNormalArray ------------------------------------------------------------------------------------------
            TextureParameter(target, gNormalArray, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            TextureParameter(target, gNormalArray, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            TextureParameter(target, gNormalArray, TextureParameterName.TextureBaseLevel, 0)
            TextureParameter(target, gNormalArray, TextureParameterName.TextureMaxLevel, mipCount - 1)
            TextureParameter(target, gNormalArray, TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            TextureParameter(target, gNormalArray, TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)

            GL.TextureStorage3D(gNormalArray, mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)
            Dim er3 = GL.GetError

            ' gGmmArray ------------------------------------------------------------------------------------------
            TextureParameter(target, gGmmArray, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            TextureParameter(target, gGmmArray, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            TextureParameter(target, gGmmArray, TextureParameterName.TextureBaseLevel, 0)
            TextureParameter(target, gGmmArray, TextureParameterName.TextureMaxLevel, mipCount - 1)
            TextureParameter(target, gGmmArray, TextureParameterName.TextureWrapS, TextureParameterName.ClampToEdge)
            TextureParameter(target, gGmmArray, TextureParameterName.TextureWrapT, TextureParameterName.ClampToEdge)

            GL.TextureStorage3D(gGmmArray, mipCount - 1, SizedInternalFormat.Rgba8, texture_size.X, texture_size.Y, LayerCount)
            Dim er4 = GL.GetError
            Dim er5 = GL.GetError

            'Next
            GL.Finish() 'make sure we are done
            'If er2 <> 0 Then
            '    Stop
            'End If
        End Sub

        Public Shared Function create_fbo() As Boolean
            FBO_Mixer_ID = CreateFramebuffer("Mixer")

            'attach our textureArray to colorAttachment0, mip 0 and level 0
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment0, gColorArray, 0, 0)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment1, gNormalArray, 0, 0)
            GL.NamedFramebufferTextureLayer(FBO_Mixer_ID, FramebufferAttachment.ColorAttachment2, gGmmArray, 0, 0)

            Dim FBOHealth = GL.CheckNamedFramebufferStatus(FBO_Mixer_ID, FramebufferTarget.Framebuffer)

            If FBOHealth <> FramebufferStatus.FramebufferComplete Then
                Return False
            End If

            Return True ' No errors! all is good! :)
        End Function
    End Class


End Module

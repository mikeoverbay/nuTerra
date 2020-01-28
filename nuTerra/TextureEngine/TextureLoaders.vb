
Imports System.Math
Imports System
Imports System.IO
Imports Tao.DevIl

Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module TextureLoaders
    Public Function load_image_from_file(ByRef fn As String)
        If Not File.Exists(fn) Then
            MsgBox("Can't find :" + fn, MsgBoxStyle.Exclamation, "Oh my!")
            Return Nothing
        End If
        Dim image_id As Integer
        Dim texID As UInt32
        texID = Ilu.iluGenImage()
        Il.ilBindImage(texID)
        Dim success = 0
        If Path.GetExtension(fn).ToLower = ".png" Then
            Il.ilLoad(Il.IL_PNG, fn)
        End If
        If Path.GetExtension(fn).ToLower = ".dds" Then
            Il.ilLoad(Il.IL_DDS, fn)
        End If
        If Path.GetExtension(fn).ToLower = ".jpg" Then
            Il.ilLoad(Il.IL_JPG, fn)
        End If
        success = Il.ilGetError
        If success = Il.IL_NO_ERROR Then
            'Ilu.iluFlipImage()
            'Ilu.iluMirror()
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)

            Dim OK As Boolean = Il.ilConvertImage(Il.IL_RGBA, Il.IL_UNSIGNED_BYTE)

            GL.GenTextures(1, image_id)
            GL.Enable(EnableCap.Texture2D)
            GL.BindTexture(TextureTarget.Texture2D, image_id)

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)

            GL.BindTexture(TextureTarget.Texture2D, 0)
            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)
            Return image_id
        Else
            MsgBox("Failed to load :" + fn, MsgBoxStyle.Exclamation, "Shit!!")
        End If
        Return Nothing
    End Function

End Module

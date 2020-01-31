
Imports System.Math
Imports System.Windows
Imports System
Imports System.IO
Imports Tao.DevIl
Imports System.Drawing.Imaging
Imports System.Drawing
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities

Module TextureLoaders

    Private imgTbl As New DataSet1.ImageTableDataTable

#Region "imgTbl routines"

    Private Sub add_image(fn As String, id As Integer)
        Dim r = imgTbl.NewRow
        r("Name") = fn
        r("ID") = id
        imgTbl.Rows.Add(r)
    End Sub

    Public Function image_exists(ByVal fn As String) As Integer
        Dim q = From d In imgTbl.AsEnumerable _
                Where d.Field(Of String)("Name") = fn _
                Select id = d.Field(Of Integer)("Id")
        If q.Count > 0 Then
            Return q(0)
        End If
        Return -1
    End Function

#End Region

    Public Sub get_start_ID_for_texture_Deletion()
        'Finds the first texture Id after the static IDs we want to keep.
        'It is used to delete all map/decal/model related texture Ids.
        FIRST_UNUSED_TEXTURE = GL.GenTexture
        GL.DeleteTexture(FIRST_UNUSED_TEXTURE)
        GL.Finish() 'We must make sure we are done deleting!!!
    End Sub

    Public Function load_image_from_stream(ByRef imageType As Integer, ByRef ms As MemoryStream, ByRef fn As String, ByRef MIPS As Boolean, ByRef NEAREST As Boolean)
        'imageType = il.IL_imageType : ms As MemoryStream : filename as string : Create Mipmaps if True : NEAREST = True / LINEAR if False
        'File name is needed to check if this file has been loaded already

        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id > -1 Then
            Return image_id
        End If

        ms.Position = 0

        Dim texID As UInt32
        Dim textIn(ms.Length) As Byte
        ms.Read(textIn, 0, ms.Length)

        texID = Ilu.iluGenImage()
        Il.ilBindImage(texID)
        Dim success = Il.ilGetError
        Il.ilLoadL(imageType, textIn, textIn.Length)
        success = Il.ilGetError
        If success = Il.IL_NO_ERROR Then
            'Ilu.iluFlipImage()
            'Ilu.iluMirror()
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)

            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE)
            'success = Il.ilConvertImage(Il.IL_RGBA, Il.IL_UNSIGNED_BYTE)

            GL.GenTextures(1, image_id)
            GL.Enable(EnableCap.Texture2D)
            GL.BindTexture(TextureTarget.Texture2D, image_id)


            If NEAREST And Not MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            If Not NEAREST And Not MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            If MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())
            GL.BindTexture(TextureTarget.Texture2D, 0)
            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)

            'this image was not found on the list so we must add it.
            add_image(fn, image_id)

            Return image_id
        Else
            MsgBox("Failed to load @ load_image_from_stream", MsgBoxStyle.Exclamation, "Shit!!")
        End If
        Return Nothing



    End Function

    Public Function load_image_from_file(ByRef imageType As Integer, ByRef fn As String, ByRef MIPS As Boolean, ByRef NEAREST As Boolean)
        'imageType = il.IL_imageType : File path/name : Create Mipmaps if True : NEAREST = True / LINEAR if False

        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id > -1 Then
            Return image_id
        End If

        If Not File.Exists(fn) Then
            MsgBox("Can't find :" + fn, MsgBoxStyle.Exclamation, "Oh my!")
            Return Nothing
        End If

        Dim texID As UInt32
        texID = Ilu.iluGenImage()
        Il.ilBindImage(texID)
        Dim success = 0

        Il.ilLoad(imageType, fn)
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

            If NEAREST And Not MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            If Not NEAREST And Not MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            If MIPS Then
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            End If
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())

            If MIPS Then
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)
            End If

            GL.BindTexture(TextureTarget.Texture2D, 0)
            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)

            'This image was not found on the list so we must add it.
            add_image(fn, image_id)

            Return image_id
        Else
            MsgBox("Failed to load @ load_image_from_file", MsgBoxStyle.Exclamation, "Shit!!")
        End If
        Return Nothing
    End Function

    Public Function make_dummy_texture() As Integer
        'Used to attach to shaders that must have a texture but it doesn't exist such as GMM maps.
        Dim dummy As Integer
        dummy = GL.GenTexture
        Dim b As New Bitmap(2, 2, Imaging.PixelFormat.Format32bppArgb)
        Dim g As Graphics = Graphics.FromImage(b)
        g.Clear(Color.Black)
        Dim bitmapData = b.LockBits(New Rectangle(0, 0, 2, _
                             2), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb)

        GL.BindTexture(TextureTarget.Texture2D, dummy)

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Nearest)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, b.Width, b.Height, 0, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, bitmapData.Scan0)

        b.UnlockBits(bitmapData) ' Unlock The Pixel Data From Memory
        GL.BindTexture(TextureTarget.Texture2D, 0)
        b.Dispose()
        g.Dispose()
        GC.Collect()
        Return dummy
    End Function

    Public Function get_tank_image(ByVal ms As MemoryStream, ByVal index As Integer, ByVal make_id As Boolean) As Bitmap
        'all these should be unique textures.. No need to check if they already have been loaded.

        'Dim s As String = ""
        's = Gl.glGetError
        Dim image_id As Integer = -1
        'Dim app_local As String = Application.StartupPath.ToString
        ms.Position = 0
        Dim texID As UInt32
        Dim textIn(ms.Length) As Byte
        ms.Read(textIn, 0, ms.Length)

        texID = Ilu.iluGenImage() ' /* Generation of one image name */
        Il.ilBindImage(texID) '; /* Binding of image name */
        Dim success = Il.ilGetError
        Il.ilLoadL(Il.IL_PNG, textIn, textIn.Length)
        success = Il.ilGetError
        If success = Il.IL_NO_ERROR Then
            'Ilu.iluFlipImage()
            'Ilu.iluMirror()
            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)
            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE)

            If make_id Then

                image_id = GL.GenTexture
                GL.Enable(EnableCap.Texture2D)

                GL.BindTexture(TextureTarget.Texture2D, image_id)

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())
                'GL.GenerateMipmap(GenerateMipmapTarget.Texture2D)

                GL.BindTexture(TextureTarget.Texture2D, 0)
                Il.ilBindImage(0)
                'ilu.iludeleteimage(texID)
                ReDim Preserve map_texture_ids(index + 1)
                map_texture_ids(index) = image_id

                GL.BindTexture(TextureTarget.Texture2D, 0)
                Il.ilBindImage(0)
                Ilu.iluDeleteImage(texID)
                GL.Finish()
                Return Nothing
            Else
                ' Create the bitmap.
                Dim Bitmapi = New System.Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)
                Dim rect As Rectangle = New Rectangle(0, 0, width, height)

                ' Store the DevIL image data into the bitmap.
                Dim bitmapData As BitmapData = Bitmapi.LockBits(rect, ImageLockMode.WriteOnly, Imaging.PixelFormat.Format32bppArgb)

                Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE)
                Il.ilCopyPixels(0, 0, 0, width, height, 1, Il.IL_BGRA, Il.IL_UNSIGNED_BYTE, bitmapData.Scan0)
                Bitmapi.UnlockBits(bitmapData)

                Return Bitmapi

            End If
        Else
            MsgBox("Unable to load texture @ get_tank_image", MsgBoxStyle.Exclamation, "SHIT!!")
        End If
        Return Nothing
    End Function

End Module

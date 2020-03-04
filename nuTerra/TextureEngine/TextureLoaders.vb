﻿Imports System.Drawing.Imaging
Imports System.IO
Imports Ionic.Zip
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module TextureLoaders

    Public imgTbl As New DataSet1.ImageTableDataTable

#Region "imgTbl routines"

    Private Sub add_image(fn As String, id As Integer)
        Dim r = imgTbl.NewRow
        r("Name") = fn
        r("ID") = id
        imgTbl.Rows.Add(r)
    End Sub

    Public Function image_exists(ByVal fn As String) As Integer
        'search datatable for the textures name.
        'If its found, return the texture's GL id.
        Dim q = From d In imgTbl.AsEnumerable
                Where d.Field(Of String)("Name") = fn
                Select id = d.Field(Of Integer)("Id")
        If q.Count > 0 Then
            Return q(0)
        End If
        Return -1
    End Function

#End Region

    Public Sub get_start_ID_for_Components_Deletion()
        'Finds the first texture Id after the static IDs we want to keep.
        'It is used to delete all map/decal/model related texture Ids.
        FIRST_UNUSED_TEXTURE = GL.GenTexture
        GL.DeleteTexture(FIRST_UNUSED_TEXTURE)
        GL.Finish() 'We must make sure we are done deleting!!!

        FIRST_UNUSED_VB_OBJECT = GL.GenVertexArray()
        GL.DeleteVertexArray(FIRST_UNUSED_VB_OBJECT)
        GL.Finish() 'We must make sure we are done deleting!!!

        FIRST_UNUSED_V_BUFFER = GL.GenBuffer()
        GL.DeleteBuffer(FIRST_UNUSED_V_BUFFER)
        GL.Finish() 'We must make sure we are done deleting!!!


    End Sub

    Public Function find_and_load_texture_from_pkgs(ByRef fn As String) As Integer
        fn = fn.Replace("\", "/") ' fix path issue
        'finds and loads and returns the GL texture ID.
        fn = fn.Replace(".png", ".dds")
        fn = fn.Replace(".atlas", ".atlas_processed")
        Dim id = image_exists(fn) 'check if this has been loaded.
        If id > 0 Then
            Return id
        End If
        Dim entry As ZipEntry = search_pkgs(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we want mips and linear filtering
            id = load_image_from_stream(Il.IL_DDS, ms, fn, True, False)
            Return id
        End If
        Return -1 ' Didn't find it, return -1
    End Function

    Public Function load_image_from_gui_pkg(ByVal fn As String)
        Dim entry = GUI_PACKAGE(fn)
        If entry Is Nothing Then
            MsgBox("Unable to find " + fn, MsgBoxStyle.Exclamation, "Shit!!")
            Return -1
        End If
        Dim ms As New MemoryStream
        entry.Extract(ms)
        If fn.Contains(".png") Then
            Return load_image_from_stream(Il.IL_PNG, ms, fn, False, True)
        End If
        If fn.Contains(".dds") Then
            Return load_image_from_stream(Il.IL_DDS, ms, fn, False, True)
        End If
        MsgBox("file Type?" + fn, MsgBoxStyle.Exclamation, "Shit!!")
        Return -1
    End Function

    Public Function find_and_load_UI_texture_from_pkgs(ByRef fn As String) As Integer
        'This will NOT replace PNG with DDS in the file name.
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn)
        If id > 0 Then Return id
        Dim entry As ZipEntry = search_pkgs(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we do not want mips and linear filtering
            If fn.Contains(".dds") Then
                Return load_image_from_stream(Il.IL_DDS, ms, fn, False, True)
            End If
            If fn.Contains(".png") Then
                Return load_image_from_stream(Il.IL_PNG, ms, fn, False, True)
            End If
        End If
        Return -1 ' Didn't find it, return -1
    End Function

    Public Function load_t2_normals_from_stream(br As BinaryReader, w As Integer, h As Integer) As Integer
        Dim image_id As Integer

        GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)
        GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
        GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)

        GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.MirroredRepeat)
        GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.MirroredRepeat)

        Dim data = br.ReadBytes(w * h)
        Dim sizedFormat = DirectCast(InternalFormat.CompressedRgbaS3tcDxt5Ext, SizedInternalFormat)
        Dim pixelFormat = DirectCast(InternalFormat.CompressedRgbaS3tcDxt5Ext, OpenGL.PixelFormat)
        GL.TextureStorage2D(image_id, 2, sizedFormat, w, h)
        GL.CompressedTextureSubImage2D(image_id, 0, 0, 0, w, h, pixelFormat, w * h, data)

        GL.GenerateTextureMipmap(image_id)

        Return image_id
    End Function

    Public Function load_image_from_stream(ByRef imageType As Integer, ByRef ms As MemoryStream, ByRef fn As String, ByRef MIPS As Boolean, ByRef NEAREST As Boolean) As Integer
        'imageType = il.IL_imageType : ms As MemoryStream : filename as string : Create Mipmaps if True : NEAREST = True / LINEAR if False
        'File name is needed to add to our list of loaded textures

        Dim image_id As Integer

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
            Dim result = Il.ilConvertImage(Il.IL_RGBA, Il.IL_UNSIGNED_BYTE)

            GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)

            Dim maxAniso As Single
            GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, maxAniso)
            If NEAREST And Not MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If
            If Not NEAREST And Not MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If
            If MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
                GL.TextureParameter(image_id, DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
            End If
            GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TextureStorage2D(image_id, If(MIPS, 4, 1), SizedInternalFormat.Rgba8, width, height)
            GL.TextureSubImage2D(image_id, 0, 0, 0, width, height, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())

            If MIPS Then
                GL.GenerateTextureMipmap(image_id)
            End If

            Il.ilBindImage(0)
            Ilu.iluDeleteImage(texID)

            If fn.Length = 0 Then Return image_id '<- so we can load with out saving in the cache.
            'Other wise, add it to the cache.
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

            GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)

            If NEAREST And Not MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If
            If Not NEAREST And Not MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If
            If MIPS Then
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If
            GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TextureStorage2D(image_id, If(MIPS, 4, 1), SizedInternalFormat.Rgba8, width, height)
            GL.TextureSubImage2D(image_id, 0, 0, 0, width, height, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, Il.ilGetData())

            If MIPS Then
                GL.GenerateTextureMipmap(image_id)
            End If

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
        Dim b As New Bitmap(2, 2, Imaging.PixelFormat.Format32bppArgb)
        Dim g As Drawing.Graphics = Drawing.Graphics.FromImage(b)
        g.Clear(Color.Black)
        Dim bitmapData = b.LockBits(New Rectangle(0, 0, 2,
                             2), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb)

        GL.CreateTextures(TextureTarget.Texture2D, 1, dummy)

        GL.TextureParameter(dummy, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
        GL.TextureParameter(dummy, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
        GL.TextureParameter(dummy, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
        GL.TextureParameter(dummy, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

        GL.TextureStorage2D(dummy, 1, SizedInternalFormat.Rgba8, b.Width, b.Height)
        GL.TextureSubImage2D(dummy, 0, 0, 0, b.Width, b.Height, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, bitmapData.Scan0)

        b.UnlockBits(bitmapData) ' Unlock The Pixel Data From Memory
        b.Dispose()
        g.Dispose()
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
            Il.ilConvertImage(Il.IL_BGR, Il.IL_UNSIGNED_BYTE)

            If make_id Then
                GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)

                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

                GL.TextureStorage2D(image_id, 2, DirectCast(InternalFormat.Rgb8, SizedInternalFormat), width, height)
                GL.TextureSubImage2D(image_id, 0, 0, 0, width, height, OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, Il.ilGetData())

                GL.GenerateTextureMipmap(image_id)

                Il.ilBindImage(0)
                'ilu.iludeleteimage(texID)
                ReDim Preserve map_texture_ids(index)
                map_texture_ids(index) = image_id

                Il.ilBindImage(0)
                Ilu.iluDeleteImage(texID)
                GL.Finish()
                Return Nothing
            Else
                ' Create the bitmap.
                Dim Bitmapi As New Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)
                Dim rect As New Rectangle(0, 0, width, height)

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

    Public Sub make_test_texture()
        If TEST_TEXTURE_ID > 0 Then
            GL.DeleteTexture(TEST_TEXTURE_ID)
        End If

        GL.CreateTextures(TextureTarget.Texture2D, 1, TEST_TEXTURE_ID)

        GL.TextureParameter(TEST_TEXTURE_ID, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        GL.TextureParameter(TEST_TEXTURE_ID, TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        GL.TextureParameter(TEST_TEXTURE_ID, TextureParameterName.TextureWrapS, TextureWrapMode.ClampToBorder)
        GL.TextureParameter(TEST_TEXTURE_ID, TextureParameterName.TextureWrapT, TextureWrapMode.ClampToBorder)
        GL.TextureStorage2D(TEST_TEXTURE_ID, 1, SizedInternalFormat.Rgba32f, FBOm.SCR_WIDTH, FBOm.SCR_HEIGHT)
        GL.BindImageTexture(0, TEST_TEXTURE_ID, 0, False, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f)
    End Sub
End Module

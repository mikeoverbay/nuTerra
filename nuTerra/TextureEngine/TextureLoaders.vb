Imports System.Drawing.Imaging
Imports System.IO
Imports Ionic.Zip
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl

Module TextureLoaders

    Public imgTbl As New DataSet1.ImageTableDataTable

#Region "imgTbl routines"

    Public Sub add_image(fn As String, id As Integer)
        Dim r = imgTbl.NewRow
        r("Name") = fn
        r("ID") = id
        imgTbl.Rows.Add(r)
        'Debug.WriteLine(fn)
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

    Public Function find_and_load_UI_texture_from_pkgs(ByRef fn As String) As Integer
        'This will NOT replace PNG with DDS in the file name.
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn)
        If id > 0 Then
            Return id

        End If
        Dim entry As ZipEntry = search_pkgs(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we do not want mips and linear filtering
            If fn.Contains(".dds") Then
                Return load_dds_image_from_stream(ms, fn)
            End If
            If fn.Contains(".png") Then
                Return load_image_from_stream(Il.IL_PNG, ms, fn, False, True)
            End If
        End If
        Return -1 ' Didn't find it, return -1
    End Function

    Public Function load_t2_texture_from_stream(br As BinaryReader, w As Integer, h As Integer) As Integer
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

    Public Class DDSHeader
        Public height As Int32
        Public width As Int32
        Public mipMapCount As Int32
        Public flags As Int32
        Public FourCC As String
        Public caps As UInt32

        ReadOnly Property is_uncompressed As Boolean
            Get
                Return (flags And &H40) <> 0
            End Get
        End Property

        ReadOnly Property gl_format As InternalFormat
            Get
                Debug.Assert((flags And &H4) <> 0)

                Select Case FourCC
                    Case "DXT1"
                        Return InternalFormat.CompressedRgbaS3tcDxt1Ext
                    Case "DXT3"
                        Return InternalFormat.CompressedRgbaS3tcDxt3Ext
                    Case "DXT5"
                        Return InternalFormat.CompressedRgbaS3tcDxt5Ext
                    Case Else ' DX10 ?
                        Stop
                        Return -1
                End Select
            End Get
        End Property

        ReadOnly Property gl_block_size As Integer
            Get
                Debug.Assert((flags And &H4) <> 0)

                Select Case FourCC
                    Case "DXT1"
                        Return 8
                    Case "DXT3"
                        Return 16
                    Case "DXT5"
                        Return 16
                    Case Else ' DX10 ?
                        Stop
                        Return -1
                End Select
            End Get
        End Property
    End Class

    Public Function get_dds_header(br As BinaryReader) As DDSHeader
        Dim header As New DDSHeader
        Dim file_code = br.ReadChars(4)
        Debug.Assert(file_code = "DDS ")
        Dim header_size = br.ReadUInt32()
        Debug.Assert(header_size = 124)
        br.ReadUInt32() ' flags
        header.height = br.ReadInt32()
        header.width = br.ReadInt32()
        br.ReadUInt32() ' pitchOrLinearSize
        br.ReadUInt32() ' depth
        header.mipMapCount = br.ReadInt32()
        br.ReadBytes(44) ' reserved1
        br.ReadUInt32() ' Size
        header.flags = br.ReadUInt32()
        header.FourCC = br.ReadChars(4)
        br.ReadUInt32() ' RGBBitCount
        br.ReadUInt32() ' RBitMask
        br.ReadUInt32() ' GBitMask
        br.ReadUInt32() ' BBitMask
        br.ReadUInt32() ' ABitMask
        header.caps = br.ReadUInt32()
        Return header
    End Function

    ' Based on https://gist.github.com/tilkinsc/13191c0c1e5d6b25fbe79bbd2288a673
    Public Function load_dds_image_from_stream(ms As MemoryStream, fn As String) As Integer
        Dim image_id As Integer

        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Dim dds_header = get_dds_header(br)

            Select Case dds_header.caps
                Case &H1000
                    Debug.Assert(dds_header.mipMapCount = 0) ' Just Check
                Case &H401008
                    Debug.Assert(dds_header.mipMapCount > 0) ' Just Check
                Case Else
                    Debug.Assert(False) ' Cubemap ?
            End Select

            Dim format As SizedInternalFormat = dds_header.gl_format
            Dim blockSize = dds_header.gl_block_size

            ms.Position = 128

            GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)
            Dim maxAniso As Single
            GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, maxAniso)

            If dds_header.mipMapCount = 0 Then
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.TextureStorage2D(image_id, 1, format, dds_header.width, dds_header.height)

                Dim size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * blockSize
                Dim data = br.ReadBytes(size)
                GL.CompressedTextureSubImage2D(image_id, 0, 0, 0, dds_header.width, dds_header.height, DirectCast(format, OpenGL.PixelFormat), size, data)
            Else
                GL.TextureParameter(image_id, DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.TextureStorage2D(image_id, dds_header.mipMapCount, format, dds_header.width, dds_header.height)

                Dim w = dds_header.width
                Dim h = dds_header.height
                Dim mipMapCount = dds_header.mipMapCount

                For i = 0 To dds_header.mipMapCount - 1
                    If (w = 0 Or h = 0) Then
                        mipMapCount -= 1
                        Continue For
                    End If

                    Dim size = ((w + 3) \ 4) * ((h + 3) \ 4) * blockSize
                    Dim data = br.ReadBytes(size)
                    GL.CompressedTextureSubImage2D(image_id, i, 0, 0, w, h, DirectCast(format, OpenGL.PixelFormat), size, data)
                    w /= 2
                    h /= 2
                Next
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, mipMapCount - 1)
            End If
        End Using
        If fn.Length = 0 Then Return image_id
        add_image(fn, image_id)
        Return image_id
    End Function

    Public Function load_image_from_stream(ByRef imageType As Integer, ByRef ms As MemoryStream, ByRef fn As String, ByRef MIPS As Boolean, ByRef NEAREST As Boolean) As Integer
        'imageType = il.IL_imageType : ms As MemoryStream : filename as string : Create Mipmaps if True : NEAREST = True / LINEAR if False
        'File name is needed to add to our list of loaded textures

        If imageType = Il.IL_DDS Then
            Return load_dds_image_from_stream(ms, fn)
        End If

        Dim image_id As Integer

        ms.Position = 0

        GC.Collect()
        GC.WaitForFullGCComplete()

        Dim imgStore(ms.Length) As Byte
        ms.Read(imgStore, 0, ms.Length)

        Dim texID As UInt32
        texID = Ilu.iluGenImage()
        Il.ilBindImage(texID)
        Dim er0 = GL.GetError
        Dim success = Il.ilGetError
        Il.ilLoadL(imageType, imgStore, ms.Length)
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

            Dim glerror As OpenTK.Graphics.OpenGL.ErrorCode = GL.GetError
            If glerror > 0 Then

                get_GL_error_string(glerror)
                MsgBox(get_GL_error_string(glerror), MsgBoxStyle.Exclamation, "GL Error")

            End If
            Return image_id
        Else
            MsgBox("Failed to load @ load_image_from_stream", MsgBoxStyle.Exclamation, "Shit!!")
        End If
        Return Nothing
    End Function

    Public Function load_image_from_file(imageType As Integer, fn As String, MIPS As Boolean, NEAREST As Boolean)
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
        'Used to attach to shaders that must have a texture but it doesn't
        'like blend maps or terrain textures.
        Dim dummy As Integer
        Dim b As New Bitmap(2, 2, Imaging.PixelFormat.Format32bppArgb)
        Dim g As Drawing.Graphics = Drawing.Graphics.FromImage(b)
        g.Clear(Color.FromArgb(0, 0, 0, 0))
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

    Public Function get_tank_image(ByVal ms As MemoryStream, ByVal index As Integer, ByVal make_id As Boolean, ByVal scale As Single) As Bitmap
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

            Dim width As Integer = Il.ilGetInteger(Il.IL_IMAGE_WIDTH)
            Dim height As Integer = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT)
            width = Math.Floor(width * scale) + 2
            height = Math.Floor(height * scale)
            Ilu.iluScale(width, height, 1)

            Il.ilConvertImage(Il.IL_BGR, Il.IL_UNSIGNED_BYTE)


            If make_id Then
                GL.CreateTextures(TextureTarget.Texture2D, 1, image_id)
                GL.ObjectLabel(ObjectLabelIdentifier.Texture, image_id, -1, String.Format("TEX-TANK-IMAGE-{0}", index))

                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

                GL.TextureStorage2D(image_id, 2, DirectCast(InternalFormat.Rgb8, SizedInternalFormat), width, height)
                GL.TextureSubImage2D(image_id, 0, 0, 0, width, height, OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, Il.ilGetData())

                GL.GenerateTextureMipmap(image_id)

                Il.ilBindImage(0)
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

End Module

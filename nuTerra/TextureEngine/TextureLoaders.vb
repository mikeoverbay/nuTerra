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
        Dim image_id As Integer = CreateTexture(TextureTarget.Texture2D, "blend_Tex")

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
        Public Enum DdsPixelFormatFlag
            AlphaFlag = &H1
            FourCCFlag = &H4
            RGBFlag = &H40
            RGBAFlag = RGBFlag Or AlphaFlag
            YUVFlag = &H200
            LuminanceFlag = &H20000
        End Enum

        Public Enum DdsCaps2Flags
            CubemapFlag = &H200
            CubemapPositiveXFlag = &H400
            CubemapNegativeXFlag = &H800
            CubemapPositiveYFlag = &H1000
            CubemapNegativeYFlag = &H2000
            CubemapPositiveZFlag = &H4000
            CubemapNegativeZFlag = &H8000
            VolumeFlag = &H200000
        End Enum

        Public height As Int32
        Public width As Int32
        Public mipMapCount As Int32
        Public depth As Int32
        Public flags As DdsPixelFormatFlag
        Public FourCC As String
        Public rgbBitCount As UInt32
        Public redMask As UInt32
        Public greenMask As UInt32
        Public blueMask As UInt32
        Public alphaMask As UInt32
        Public caps As UInt32
        Public caps2 As DdsCaps2Flags

        Public Class FormatInfo
            Public pixel_format As OpenGL.PixelFormat
            Public texture_format As SizedInternalFormat
            Public pixel_type As PixelType
            Public components As Integer
            Public compressed As Boolean
        End Class

        ReadOnly Property format_info As FormatInfo
            Get
                Debug.Assert(flags.HasFlag(DdsPixelFormatFlag.FourCCFlag))
                Select Case FourCC
                    Case "DXT1"
                        Return New FormatInfo With {
                            .pixel_format = -1, ' no source type
                            .texture_format = InternalFormat.CompressedRgbaS3tcDxt1Ext,
                            .pixel_type = -1, ' no pixel type
                            .components = 8,
                            .compressed = True
                        }
                    Case "DXT3"
                        Return New FormatInfo With {
                            .pixel_format = -1, ' no source type
                            .texture_format = InternalFormat.CompressedRgbaS3tcDxt3Ext,
                            .pixel_type = -1, ' no pixel type
                            .components = 16,
                            .compressed = True
                        }
                    Case "DXT5"
                        Return New FormatInfo With {
                            .pixel_format = -1, ' no source type
                            .texture_format = InternalFormat.CompressedRgbaS3tcDxt5Ext,
                            .pixel_type = -1, ' no pixel type
                            .components = 16,
                            .compressed = True
                        }
                    Case "t" & vbNullChar & vbNullChar & vbNullChar
                        ' DXGI_FORMAT_R32G32B32A32_FLOAT 
                        Return New FormatInfo With {
                            .pixel_format = OpenGL.PixelFormat.Rgba,
                            .texture_format = InternalFormat.Rgba32f,
                            .pixel_type = PixelType.Float,
                            .components = 16,
                            .compressed = False
                        }
                    Case Else
                        Stop
                        Return Nothing
                End Select
            End Get
        End Property

        ReadOnly Property faces As Integer
            Get
                Dim AllCubemapFaceFlags() = {
                    DdsCaps2Flags.CubemapPositiveXFlag, DdsCaps2Flags.CubemapNegativeXFlag,
                    DdsCaps2Flags.CubemapPositiveYFlag, DdsCaps2Flags.CubemapNegativeYFlag,
                    DdsCaps2Flags.CubemapPositiveZFlag, DdsCaps2Flags.CubemapNegativeZFlag
                }
                Dim result = 0
                For Each flag In AllCubemapFaceFlags
                    If caps2.HasFlag(flag) Then
                        result += 1
                    End If
                Next
                Return result
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
        header.depth = br.ReadUInt32() ' depth
        header.mipMapCount = br.ReadInt32()
        br.ReadBytes(44) ' reserved1
        br.ReadUInt32() ' Size
        header.flags = br.ReadUInt32()
        header.FourCC = br.ReadChars(4)
        header.rgbBitCount = br.ReadUInt32()
        header.redMask = br.ReadUInt32()
        header.greenMask = br.ReadUInt32()
        header.blueMask = br.ReadUInt32()
        header.alphaMask = br.ReadUInt32()
        header.caps = br.ReadUInt32()
        header.caps2 = br.ReadUInt32()
        Return header
    End Function

    ' Based on https://gist.github.com/tilkinsc/13191c0c1e5d6b25fbe79bbd2288a673
    Public Function load_dds_image_from_stream(ms As MemoryStream, fn As String) As Integer
        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id > -1 Then
            Debug.WriteLine(fn)
            Return image_id
        End If
        Dim e1 = GL.GetError()

        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Dim dds_header = get_dds_header(br)
            ms.Position = 128

            'Select Case dds_header.caps
            '    Case &H1000
            '        Debug.Assert(dds_header.mipMapCount = 0) ' Just Check
            '    Case &H401008
            '        Debug.Assert(dds_header.mipMapCount > 0) ' Just Check
            '    Case Else
            '        Debug.Assert(False) ' Cubemap ?
            'End Select

            image_id = CreateTexture(TextureTarget.Texture2D, fn)

            'If image_id = 356 Then Stop
            Dim maxAniso As Single = 3.0F
            'GL.GetFloat(ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, maxAniso)
            Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(dds_header.width, dds_header.height), 2))

            Dim format_info = dds_header.format_info
            If dds_header.mipMapCount = 0 Or dds_header.mipMapCount = 1 Then
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, numLevels)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.TextureStorage2D(image_id, numLevels, format_info.texture_format, dds_header.width, dds_header.height)

                Dim size As Integer
                If format_info.compressed Then
                    size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                Else
                    size = dds_header.width * dds_header.height * format_info.components
                End If
                Dim data = br.ReadBytes(size)

                If format_info.compressed Then
                    GL.CompressedTextureSubImage2D(image_id, 0, 0, 0, dds_header.width, dds_header.height, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                Else
                    GL.TextureSubImage2D(image_id, 0, 0, 0, dds_header.width, dds_header.height, format_info.pixel_format, format_info.pixel_type, data)
                End If

                'added 10/4/2020
                GL.GenerateTextureMipmap(image_id)

            Else
                'GL.TextureParameter(image_id, DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
                GL.TextureParameter(image_id, TextureParameterName.TextureBaseLevel, 0)
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
                GL.TextureParameter(image_id, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                GL.TextureParameter(image_id, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                GL.TextureParameter(image_id, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                GL.TextureStorage2D(image_id, dds_header.mipMapCount, format_info.texture_format, dds_header.width, dds_header.height)

                Dim w = dds_header.width
                Dim h = dds_header.height
                Dim mipMapCount = dds_header.mipMapCount

                For i = 0 To dds_header.mipMapCount - 1
                    If (w = 0 Or h = 0) Then
                        mipMapCount -= 1
                        Continue For
                    End If

                    Dim size As Integer
                    If format_info.compressed Then
                        size = ((w + 3) \ 4) * ((h + 3) \ 4) * format_info.components
                    Else
                        size = w * h * format_info.components
                    End If
                    Dim data = br.ReadBytes(size)

                    If format_info.compressed Then
                        GL.CompressedTextureSubImage2D(image_id, i, 0, 0, w, h, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                    Else
                        GL.TextureSubImage2D(image_id, i, 0, 0, w, h, format_info.pixel_format, format_info.pixel_type, data)
                    End If

                    w /= 2
                    h /= 2
                Next
                GL.TextureParameter(image_id, TextureParameterName.TextureMaxLevel, mipMapCount - 1)
            End If

            Dim e2 = GL.GetError()
            If e2 > 0 Then
                Stop
            End If
        End Using
        If fn.Length = 0 Then Return image_id
        add_image(fn, image_id)
        Return image_id
    End Function

    Public Function load_image_from_stream(ByRef imageType As Integer, ByRef ms As MemoryStream, ByRef fn As String, ByRef MIPS As Boolean, ByRef NEAREST As Boolean) As Integer
        'imageType = il.IL_imageType : ms As MemoryStream : filename as string : Create Mipmaps if True : NEAREST = True / LINEAR if False
        'File name is needed to add to our list of loaded textures
        Dim image_id As Integer
        If imageType = Il.IL_DDS Then
            image_id = load_dds_image_from_stream(ms, fn)
            If image_id > 0 Then Return image_id
        End If

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

            image_id = CreateTexture(TextureTarget.Texture2D, fn)

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

            Dim glerror = GL.GetError
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

            image_id = CreateTexture(TextureTarget.Texture2D, fn)

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

        dummy = CreateTexture(TextureTarget.Texture2D, "Dummy_Texture")

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
                image_id = CreateTexture(TextureTarget.Texture2D, String.Format("tank_img_{0}", index))

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

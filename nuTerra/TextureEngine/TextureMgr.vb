Imports System.Drawing.Imaging
Imports System.IO
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Hjg.Pngcs

NotInheritable Class TextureMgr
    Public Shared imgTbl As New Dictionary(Of String, GLTexture)

#Region "imgTbl routines"

    Public Shared Sub add_image(fn As String, id As GLTexture)
        imgTbl(fn) = id
    End Sub

    Public Shared Function image_exists(fn As String) As GLTexture
        If imgTbl.ContainsKey(fn) Then Return imgTbl(fn)
        Return Nothing
    End Function

#End Region

    Public Shared Function find_and_load_texture_from_pkgs(ByRef fn As String) As GLTexture
        fn = fn.Replace("\", "/") ' fix path issue
        'finds and loads and returns the GL texture ID.
        fn = fn.Replace(".png", ".dds")
        fn = fn.Replace(".atlas", ".atlas_processed")
        Dim id = image_exists(fn) 'check if this has been loaded.
        If id IsNot Nothing Then
            Return id
        End If
        Dim entry = ResMgr.LookupHD(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we want mips and linear filtering
            id = load_dds_image_from_stream(ms, fn)
            Return id
        End If
        Return Nothing ' Didn't find it
    End Function

    Public Shared Function find_and_load_texture_from_pkgs_No_Suffix_change(ByRef fn As String, ByVal heightMap As Boolean) As GLTexture
        fn = fn.Replace("\", "/") ' fix path issue
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn) 'check if this has been loaded.
        If id IsNot Nothing Then
            Return id
        End If
        Dim entry = ResMgr.LookupHD(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we do not want mips and linear filtering
            If fn.Contains(".dds") Then
                Return load_dds_image_from_stream(ms, fn)
            End If
            If fn.Contains(".png") Then
                Return load_16bit_grayscale_png_from_stream(ms, heightMap)
            End If
        End If
        Return Nothing ' Didn't find it
    End Function

    Public Shared Function find_and_load_UI_texture_from_pkgs(ByRef fn As String) As GLTexture
        'This will NOT replace PNG with DDS in the file name.
        'finds and loads and returns the GL texture ID.
        Dim id = image_exists(fn)
        If id IsNot Nothing Then
            Return id
        End If

        Dim entry = ResMgr.Lookup(fn)
        If entry IsNot Nothing Then
            Dim ms As New MemoryStream
            entry.Extract(ms)
            'we do not want mips and linear filtering
            If fn.Contains(".dds") Then
                Return load_dds_image_from_stream(ms, fn)
            End If
            If fn.Contains(".png") Then
                Return load_png_image_from_stream(ms, fn, False, True)
            End If
        End If
        Return Nothing ' Didn't find it
    End Function

    Public Shared Function load_t2_texture_from_stream(br As BinaryReader, w As Integer, h As Integer) As GLTexture
        Dim image_id = GLTexture.Create(TextureTarget.Texture2D, "blend_Tex")

        image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
        image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
        image_id.Parameter(TextureParameterName.TextureMaxLevel, 1)
        image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)

        image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.MirroredRepeat)
        image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.MirroredRepeat)

        Dim data = br.ReadBytes(w * h)

        Dim sizedFormat = DirectCast(InternalFormat.CompressedRgbaS3tcDxt5Ext, SizedInternalFormat)
        Dim pixelFormat = DirectCast(InternalFormat.CompressedRgbaS3tcDxt5Ext, OpenGL.PixelFormat)

        image_id.Storage2D(2, sizedFormat, w, h)
        image_id.CompressedSubImage2D(0, 0, 0, w, h, pixelFormat, w * h, data)

        image_id.GenerateMipmap()

        Return image_id
    End Function

    Public Class DDSHeader
        ''' <summary>DXGI format from the DX10 extension block, 0 when there is
        ''' none. Only meaningful when FourCC is "DX10".</summary>
        Public dxgiFormat As UInteger
        ''' <summary>Byte offset of the first mip: 128 normally, 148 when a DX10
        ''' extension block sits in between.</summary>
        Public data_start As Integer = 128

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

        ''' <summary>
        ''' The GL format behind a DX10 file's DXGI enum.
        '''
        ''' Block sizes drop straight into `components`, which the loader uses as
        ''' bytes per 4x4 block: BC7 and BC6H are 16 like DXT5, BC4 is 8 like
        ''' DXT1, so the size arithmetic upstream needs nothing.
        '''
        ''' Everything here is core GL 4.2 or older, and the engine asks for 4.5.
        ''' </summary>
        Private Function dx10_format_info() As FormatInfo
            Select Case dxgiFormat
                Case 98UI, 99UI   ' BC7_UNORM, BC7_UNORM_SRGB
                    ' 777 of the 786 DX10 files. sRGB is taken as UNORM on
                    ' purpose: everything else here is uploaded unconverted and
                    ' the shaders do their own decode, so honouring it on this
                    ' one path would make these textures the odd ones out.
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = InternalFormat.CompressedRgbaBptcUnorm,
                        .pixel_type = -1,
                        .components = 16,
                        .compressed = True
                    }
                Case 95UI, 96UI   ' BC6H_UF16, BC6H_SF16
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = If(dxgiFormat = 95UI,
                                             InternalFormat.CompressedRgbBptcUnsignedFloat,
                                             InternalFormat.CompressedRgbBptcSignedFloat),
                        .pixel_type = -1,
                        .components = 16,
                        .compressed = True
                    }
                Case 80UI, 81UI   ' BC4_UNORM, BC4_SNORM
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = If(dxgiFormat = 80UI,
                                             InternalFormat.CompressedRedRgtc1,
                                             InternalFormat.CompressedSignedRedRgtc1),
                        .pixel_type = -1,
                        .components = 8,
                        .compressed = True
                    }
                Case 83UI, 84UI   ' BC5_UNORM, BC5_SNORM
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = If(dxgiFormat = 83UI,
                                             InternalFormat.CompressedRgRgtc2,
                                             InternalFormat.CompressedSignedRgRgtc2),
                        .pixel_type = -1,
                        .components = 16,
                        .compressed = True
                    }
                Case 71UI, 72UI   ' BC1
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = InternalFormat.CompressedRgbaS3tcDxt1Ext,
                        .pixel_type = -1,
                        .components = 8,
                        .compressed = True
                    }
                Case 74UI, 75UI   ' BC2
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = InternalFormat.CompressedRgbaS3tcDxt3Ext,
                        .pixel_type = -1,
                        .components = 16,
                        .compressed = True
                    }
                Case 77UI, 78UI   ' BC3
                    Return New FormatInfo With {
                        .pixel_format = -1,
                        .texture_format = InternalFormat.CompressedRgbaS3tcDxt5Ext,
                        .pixel_type = -1,
                        .components = 16,
                        .compressed = True
                    }
                Case 61UI         ' R8_UNORM - three files, all patterns
                    Return New FormatInfo With {
                        .pixel_format = OpenGL.PixelFormat.Red,
                        .texture_format = InternalFormat.R8,
                        .pixel_type = PixelType.UnsignedByte,
                        .components = 1,
                        .compressed = False
                    }
                Case 28UI, 29UI   ' R8G8B8A8_UNORM(_SRGB)
                    Return New FormatInfo With {
                        .pixel_format = OpenGL.PixelFormat.Rgba,
                        .texture_format = InternalFormat.Rgba8,
                        .pixel_type = PixelType.UnsignedByte,
                        .components = 4,
                        .compressed = False
                    }
                Case 87UI         ' B8G8R8A8_UNORM
                    Return New FormatInfo With {
                        .pixel_format = OpenGL.PixelFormat.Bgra,
                        .texture_format = InternalFormat.Rgba8,
                        .pixel_type = PixelType.UnsignedByte,
                        .components = 4,
                        .compressed = False
                    }
                Case 10UI         ' R16G16B16A16_FLOAT
                    Return New FormatInfo With {
                        .pixel_format = OpenGL.PixelFormat.Rgba,
                        .texture_format = InternalFormat.Rgba16f,
                        .pixel_type = PixelType.HalfFloat,
                        .components = 8,
                        .compressed = False
                    }
                Case 2UI          ' R32G32B32A32_FLOAT
                    Return New FormatInfo With {
                        .pixel_format = OpenGL.PixelFormat.Rgba,
                        .texture_format = InternalFormat.Rgba32f,
                        .pixel_type = PixelType.Float,
                        .components = 16,
                        .compressed = False
                    }
                Case Else
                    LogThis("dds: unhandled DXGI format {0} - texture skipped", dxgiFormat)
                    Return Nothing
            End Select
        End Function

        ReadOnly Property format_info As FormatInfo
            Get
                If flags.HasFlag(DdsPixelFormatFlag.FourCCFlag) Then
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
                        Case "q" & vbNullChar & vbNullChar & vbNullChar
                            ' DXGI_FORMAT_R16G16B16A16_FLOAT
                            Return New FormatInfo With {
                                .pixel_format = OpenGL.PixelFormat.Rgba,
                                .texture_format = InternalFormat.Rgba16f,
                                .pixel_type = PixelType.HalfFloat,
                                .components = 8,
                                .compressed = False
                            }
                        Case "ATI1"
                            ' BC4 under its pre-DX10 name. Four files ship this way.
                            Return New FormatInfo With {
                                .pixel_format = -1,
                                .texture_format = InternalFormat.CompressedRedRgtc1,
                                .pixel_type = -1,
                                .components = 8,
                                .compressed = True
                            }
                        Case "r" & vbNullChar & vbNullChar & vbNullChar
                            ' D3DFMT_R32F (114)
                            Return New FormatInfo With {
                                .pixel_format = OpenGL.PixelFormat.Red,
                                .texture_format = InternalFormat.R32f,
                                .pixel_type = PixelType.Float,
                                .components = 4,
                                .compressed = False
                            }
                        Case "DX10"
                            Return dx10_format_info()

                        Case Else
                            ' Named, not Stop. A breakpoint helps nobody in a
                            ' release build, and Nothing here is dereferenced
                            ' downstream - so an unknown format used to crash
                            ' somewhere else entirely, with nothing saying what
                            ' it was.
                            LogThis("dds: unhandled fourCC {0} ({1}) - texture skipped",
                                    FourCC, BitConverter.ToString(Text.Encoding.ASCII.GetBytes(FourCC)))
                            Return Nothing
                    End Select
                Else
                    If rgbBitCount = 24 AndAlso redMask = &HFF0000 AndAlso greenMask = &HFF00 AndAlso blueMask = &HFF AndAlso alphaMask = &H0 Then
                        Return New FormatInfo With {
                            .pixel_format = OpenGL.PixelFormat.Bgr,
                            .texture_format = InternalFormat.Rgb8,
                            .pixel_type = PixelType.UnsignedByte,
                            .components = 3,
                            .compressed = False
                        }
                    ElseIf rgbBitCount = 32 AndAlso redMask = &HFF0000 AndAlso greenMask = &HFF00 AndAlso blueMask = &HFF AndAlso alphaMask = &HFF000000UI Then
                        Return New FormatInfo With {
                            .pixel_format = OpenGL.PixelFormat.Bgra,
                            .texture_format = InternalFormat.Rgba8,
                            .pixel_type = PixelType.UnsignedByte,
                            .components = 4,
                            .compressed = False
                        }
                    ElseIf rgbBitCount = 32 AndAlso redMask = &HFF AndAlso greenMask = &HFF00 AndAlso blueMask = &HFF0000 AndAlso alphaMask = &HFF000000UI Then
                        Return New FormatInfo With {
                            .pixel_format = OpenGL.PixelFormat.Rgba,
                            .texture_format = InternalFormat.Rgba8,
                            .pixel_type = PixelType.UnsignedByte,
                            .components = 4,
                            .compressed = False
                        }
                    End If
                End If
                Stop
                Return Nothing
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

    Public Shared Function get_dds_header(br As BinaryReader) As DDSHeader
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

        ' The DX10 extension. When the fourCC is literally "DX10" the real format
        ' is a DXGI enum in a 20 byte block AFTER the 124 byte header, and the
        ' pixels start at 148 rather than 128.
        '
        ' That offset is the trap. Every other branch here fails loudly on a
        ' format it does not know; getting the DATA START wrong instead succeeds
        ' and produces a texture built from the wrong bytes - every mip shifted
        ' by 20 - which looks like a corrupt asset rather than a loader bug.
        '
        ' 786 of the 106594 DDS files in the packages are DX10, and 777 of those
        ' are BC7. The rest of the tail is 5 BC4, 3 R8 and one BC6H.
        header.data_start = 128
        header.dxgiFormat = 0UI
        If header.FourCC = "DX10" Then
            br.BaseStream.Position = 128
            header.dxgiFormat = br.ReadUInt32()
            br.ReadUInt32()   ' resourceDimension
            br.ReadUInt32()   ' miscFlag
            br.ReadUInt32()   ' arraySize
            br.ReadUInt32()   ' miscFlags2
            header.data_start = 148
        End If

        Return header
    End Function

    ' Based on https://gist.github.com/tilkinsc/13191c0c1e5d6b25fbe79bbd2288a673
    Public Shared Function load_dds_image_from_stream(ms As MemoryStream, fn As String) As GLTexture
        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id IsNot Nothing Then
            ' Debug.WriteLine(fn)
            Return image_id
        End If
        Dim e1 = GL.GetError()

        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Dim dds_header = get_dds_header(br)
            ' 148 for a DX10 file, 128 otherwise - see DDSHeader.data_start.
            ms.Position = dds_header.data_start

            'Select Case dds_header.caps
            '    Case &H1000
            '        Debug.Assert(dds_header.mipMapCount = 0) ' Just Check
            '    Case &H401008
            '        Debug.Assert(dds_header.mipMapCount > 0) ' Just Check
            '    Case Else
            '        Debug.Assert(False) ' Cubemap ?
            'End Select

            image_id = GLTexture.Create(TextureTarget.Texture2D, fn)

            'If image_id = 356 Then Stop
            Dim maxAniso As Single = 4.0F
            Dim numLevels As Integer = 1 + Math.Floor(Math.Log(Math.Max(dds_header.width, dds_header.height), 2))

            Dim format_info = dds_header.format_info
            If dds_header.mipMapCount = 0 Or dds_header.mipMapCount = 1 Then
                image_id.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
                image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
                image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
                image_id.Parameter(TextureParameterName.TextureMaxLevel, numLevels - 1)
                image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                image_id.Storage2D(numLevels, format_info.texture_format, dds_header.width, dds_header.height)

                Dim size As Integer
                If format_info.compressed Then
                    size = ((dds_header.width + 3) \ 4) * ((dds_header.height + 3) \ 4) * format_info.components
                Else
                    size = dds_header.width * dds_header.height * format_info.components
                End If
                Dim data = br.ReadBytes(size)

                If format_info.compressed Then
                    image_id.CompressedSubImage2D(0, 0, 0, dds_header.width, dds_header.height, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                Else
                    image_id.SubImage2D(0, 0, 0, dds_header.width, dds_header.height, format_info.pixel_format, format_info.pixel_type, data)
                End If

                'added 10/4/2020
                image_id.GenerateMipmap()

            Else
                If dds_header.width <> dds_header.height Then
                    dds_header.mipMapCount -= 1
                End If
                image_id.Parameter(DirectCast(ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, TextureParameterName), maxAniso)
                image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
                image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
                image_id.Parameter(TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
                image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
                image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)
                image_id.Storage2D(dds_header.mipMapCount, format_info.texture_format, dds_header.width, dds_header.height)

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

                    If data.Length = 0 Then Stop
                    If format_info.compressed Then
                        image_id.CompressedSubImage2D(i, 0, 0, w, h, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                    Else
                        image_id.SubImage2D(i, 0, 0, w, h, format_info.pixel_format, format_info.pixel_type, data)
                    End If

                    ' INTEGER division, clamped at 1. GL defines mip level i as
                    ' max(1, floor(w / 2^i)), and VB's / is FLOATING POINT - so
                    ' assigning it back to an Integer ROUNDS. A 7 wide level
                    ' became 4 instead of 3 and an 11 became 6 instead of 5, which
                    ' is both the wrong size passed to CompressedSubImage2D and
                    ' the wrong number of bytes read, so every remaining mip in
                    ' that file was read from the wrong offset.
                    '
                    ' The clamp matters for non-square textures: 8x2 goes 4x1 then
                    ' 2x0 without it, and GL wants 2x1.
                    w = Math.Max(1, w \ 2)
                    h = Math.Max(1, h \ 2)
                Next
                image_id.Parameter(TextureParameterName.TextureMaxLevel, mipMapCount - 1)
            End If

            Dim e2 = GL.GetError()
            If e2 > 0 Then
                ' Say WHICH texture and what shape it was. This trap was a bare
                ' Stop, so it only spoke to a debugger - and the answer needed is
                ' always the file, which the break alone does not give.
                LogThis("texture: GL {0} loading {1} ({2}x{3}, {4} mips, {5}{6})",
                        e2, fn, dds_header.width, dds_header.height,
                        dds_header.mipMapCount,
                        If(format_info.compressed, "compressed ", ""),
                        dds_header.FourCC.Trim(ChrW(0)))
                Stop
            End If
        End Using
        If fn.Length = 0 Then Return image_id
        add_image(fn, image_id)
        Return image_id
    End Function

    Public Shared Function load_16bit_grayscale_png_from_stream(ByRef ms As MemoryStream, ByVal heightMap As Boolean) As GLTexture
        'we wont check for if this is loaded already.. It cant be. They are unique.
        ms.Position = 0
        Dim data(100) As Byte
        Dim cols As UInt32
        Dim cnt As Integer = 0
        Dim sizeX, sizeY As Integer
        Using ms
            Dim rdr As New PngReader(ms) ' create png from stream 's'
            Dim iInfo = rdr.ImgInfo
            Dim channels = iInfo.Channels
            Dim bytesRow = iInfo.BytesPerRow
            sizeX = iInfo.Cols
            sizeY = iInfo.Rows
            cols = iInfo.Cols

            ReDim data(sizeX * sizeY * 2 - 1)
            Dim iline As ImageLine  ' create place to hold a scan line
            For i = 0 To iInfo.Cols - 1
                iline = rdr.ReadRow(i)
                For j = 0 To iline.Scanline.Length - 1
                    'get the line and convert from word to byte and save in our buffer 'data'
                    Dim bytes() As Byte = BitConverter.GetBytes(iline.Scanline(j))
                    'THESE MAY NEED TO BE FLIPPED Endiness.
                    data(cnt) = bytes(0)
                    cnt += 1
                    data(cnt) = bytes(1)
                    cnt += 1
                Next
            Next
            ms.Close()
            ms.Dispose()
        End Using

        If heightMap Then
            'r16 ushort
            Dim image_id = GLTexture.Create(TextureTarget.Texture2D, "outland_height")

            image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)

            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            image_id.Storage2D(1, SizedInternalFormat.R16, sizeX, sizeY)
            image_id.SubImage2D(0, 0, 0, sizeX, sizeY, OpenGL4.PixelFormat.Red, PixelType.UnsignedShort, data)

            Return image_id
        Else
            'rgba4444
            Dim image_id = GLTexture.Create(TextureTarget.Texture2D, "outland_tilemap")

            image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)

            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            image_id.Storage2D(1, InternalFormat.Rgba4, sizeX, sizeY)
            image_id.SubImage2D(0, 0, 0, sizeX, sizeY, OpenGL4.PixelFormat.Rgba, PixelType.UnsignedShort4444, data)

            Return image_id
        End If

    End Function

    Public Shared Function load_png_image_from_stream(ms As MemoryStream, fn As String, MIPS As Boolean, NEAREST As Boolean) As GLTexture
        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id IsNot Nothing Then
            Return image_id
        End If

        ms.Position = 0
        Using bmp As New Bitmap(ms)
            Dim bitmapData = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                          ImageLockMode.ReadOnly,
                                          bmp.PixelFormat)

            Dim sizedFmt As SizedInternalFormat
            Dim pixelFmt As OpenGL.PixelFormat
            Select Case bmp.PixelFormat
                Case Imaging.PixelFormat.Format32bppArgb
                    sizedFmt = SizedInternalFormat.Rgba8
                    pixelFmt = OpenGL.PixelFormat.Bgra
                Case Imaging.PixelFormat.Format24bppRgb
                    sizedFmt = InternalFormat.Rgb8
                    pixelFmt = OpenGL.PixelFormat.Bgr
                Case Else
                    Stop
            End Select

            image_id = GLTexture.Create(TextureTarget.Texture2D, fn)

            image_id.Storage2D(If(MIPS, 4, 1), sizedFmt, bmp.Width, bmp.Height)

            image_id.SubImage2D(0, 0, 0, bmp.Width, bmp.Height, pixelFmt, PixelType.UnsignedByte, bitmapData.Scan0)
            image_id.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            image_id.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            If MIPS Then
                image_id.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
                image_id.Parameter(TextureParameterName.TextureBaseLevel, 0)
                image_id.Parameter(TextureParameterName.TextureMaxLevel, 4 - 1)
                image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
                image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
                image_id.GenerateMipmap()

            ElseIf NEAREST Then
                image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
                image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)

            Else
                image_id.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
                image_id.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            End If

            bmp.UnlockBits(bitmapData)

            'This image was not found on the list so we must add it.
            add_image(fn, image_id)

            Return image_id
        End Using
    End Function


    Public Shared Function load_dds_image_from_file(fn As String) As GLTexture
        fn = Path.Combine(Application.StartupPath, "resources", fn)

        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id IsNot Nothing Then
            Return image_id
        End If

        If Not File.Exists(fn) Then
            MsgBox("Can't find :" + fn, MsgBoxStyle.Exclamation, "Oh my!")
            Return Nothing
        End If

        Using ms As New MemoryStream(File.ReadAllBytes(fn))
            Return load_dds_image_from_stream(ms, fn)
        End Using
    End Function

    Public Shared Function load_png_image_from_file(fn As String, MIPS As Boolean, NEAREST As Boolean) As GLTexture
        fn = Path.Combine(Application.StartupPath, "resources", fn)

        'Check if this image has already been loaded.
        Dim image_id = image_exists(fn)
        If image_id IsNot Nothing Then
            Return image_id
        End If

        If Not File.Exists(fn) Then
            MsgBox("Can't find :" + fn, MsgBoxStyle.Exclamation, "Oh my!")
            Return Nothing
        End If

        Using ms As New MemoryStream(File.ReadAllBytes(fn))
            Return load_png_image_from_stream(ms, fn, MIPS, NEAREST)
        End Using
    End Function

    Public Shared Function make_dummy_texture() As GLTexture
        'Used to attach to shaders that must have a texture but it doesn't
        'like blend maps or terrain textures.
        Using bmp As New Bitmap(2, 2, Imaging.PixelFormat.Format32bppArgb)
            Using gfx = Drawing.Graphics.FromImage(bmp)
                gfx.Clear(Color.FromArgb(0, 0, 0, 0))

                Dim bitmapData = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                            ImageLockMode.ReadOnly,
                                            bmp.PixelFormat)

                Dim dummy = GLTexture.Create(TextureTarget.Texture2D, "Dummy_Texture")

                dummy.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
                dummy.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
                dummy.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
                dummy.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

                dummy.Storage2D(1, SizedInternalFormat.Rgba8, bmp.Width, bmp.Height)
                dummy.SubImage2D(0, 0, 0, bmp.Width, bmp.Height, OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, bitmapData.Scan0)

                ' Unlock The Pixel Data From Memory
                bmp.UnlockBits(bitmapData)

                Return dummy
            End Using
        End Using
    End Function

    Public Shared Function get_map_image(ms As MemoryStream, index As Integer) As GLTexture
        'all these should be unique textures.. No need to check if they already have been loaded.

        ms.Position = 0
        Using bmp = New Bitmap(ms)
            Dim bitmapData = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadOnly,
                                    bmp.PixelFormat)

            Dim sizedFmt As SizedInternalFormat
            Dim pixelFmt As OpenGL.PixelFormat
            Select Case bmp.PixelFormat
                Case Imaging.PixelFormat.Format32bppArgb
                    sizedFmt = SizedInternalFormat.Rgba8
                    pixelFmt = OpenGL.PixelFormat.Bgra
                Case Imaging.PixelFormat.Format24bppRgb
                    sizedFmt = InternalFormat.Rgb8
                    pixelFmt = OpenGL.PixelFormat.Bgr
                Case Else
                    Stop
            End Select

            Dim image = GLTexture.Create(TextureTarget.Texture2D, String.Format("map_img_{0}", index))

            image.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
            image.Parameter(TextureParameterName.TextureBaseLevel, 0)
            image.Parameter(TextureParameterName.TextureMaxLevel, 1)
            image.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            image.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
            image.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            image.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            image.Storage2D(2, sizedFmt, bmp.Width, bmp.Height)
            image.SubImage2D(0, 0, 0, bmp.Width, bmp.Height, pixelFmt, PixelType.UnsignedByte, bitmapData.Scan0)

            ' Unlock The Pixel Data From Memory
            bmp.UnlockBits(bitmapData)

            image.GenerateMipmap()

            Return image
        End Using
    End Function

    Public Shared Function OpenDDS(path As String) As GLTexture
        Dim image_id = image_exists(path)
        If image_id IsNot Nothing Then
            Return image_id
        End If
        Dim entry = ResMgr.LookupHD(path)
        If entry Is Nothing Then
            Return Nothing
        End If

        Dim ms As New MemoryStream
        entry.Extract(ms)

        Return load_dds_image_from_stream(ms, path)
    End Function

    Public Shared Sub ClearCache()
        'Clear texture cache so we dont returned non-existent textures.
        For Each tex In imgTbl
            tex.Value.Dispose()
        Next
        imgTbl.Clear()
    End Sub
End Class
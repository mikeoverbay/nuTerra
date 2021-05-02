Imports System.IO
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Module modCubeMapLoader
    Public Sub load_cube_and_cube_map()
        If CUBE_TEXTURE_ID IsNot Nothing Then CUBE_TEXTURE_ID.Delete()

        'find our cube in teh maps package

        Dim entry = ResMgr.Lookup(CUBE_TEXTURE_PATH)
        If entry Is Nothing Then
            LogThis("cube not found {0}", CUBE_TEXTURE_PATH)
            Return
        End If
        Dim ms As New MemoryStream
        entry.Extract(ms)

        load_dds_cubemap_from_stream(ms)
    End Sub

    ' see https://github.com/fendevel/Guide-to-Modern-OpenGL-Functions#uploading-cube-maps
    Public Sub load_dds_cubemap_from_stream(ms As MemoryStream)
        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Dim dds_header = get_dds_header(br)

            Debug.Assert((dds_header.caps2 And &H200) = &H200) ' Cubemap ?

            Dim faces = dds_header.faces
            Debug.Assert(faces = 6)

            Dim format_info = dds_header.format_info

            ms.Position = 128

            CUBE_TEXTURE_ID = CreateTexture(TextureTarget.TextureCubeMap, "CubeMap")

            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureLodBias, GLOBAL_MIP_BIAS)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureBaseLevel, 0)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            CUBE_TEXTURE_ID.Storage2D(dds_header.mipMapCount, format_info.texture_format, dds_header.width, dds_header.height)

            Dim mipMapCount = dds_header.mipMapCount
            For face = 0 To faces - 1
                Dim w = dds_header.width
                Dim h = dds_header.height

                For i = 0 To dds_header.mipMapCount - 1
                    If (w = 0 Or h = 0) Then
                        Continue For
                    End If

                    Dim size = ((w + 3) \ 4) * ((h + 3) \ 4) * format_info.components
                    Dim data = br.ReadBytes(size)
                    CUBE_TEXTURE_ID.CompressedSubImage3D(i, 0, 0, face, w, h, 1, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                    w /= 2
                    h /= 2
                Next
            Next
            CUBE_TEXTURE_ID.Parameter(TextureParameterName.TextureMaxLevel, mipMapCount - 1)
        End Using
    End Sub

End Module

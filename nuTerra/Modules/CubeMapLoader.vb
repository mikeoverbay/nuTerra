﻿Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
Imports System.IO

Module CubeMapLoader
    Public Sub load_cube_and_cube_map()

        If CUBE_TEXTURE_ID > 0 Then
            GL.DeleteTexture(CUBE_TEXTURE_ID)
            GL.Finish()
        End If

        'find our cube in teh maps package

        Dim entry = Packages.MAP_PACKAGE(CUBE_TEXTURE_PATH)
        If entry Is Nothing Then
            LogThis("cube not found " + CUBE_TEXTURE_PATH)
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

            Const target = TextureTarget.TextureCubeMap
            CUBE_TEXTURE_ID = CreateTexture(target, "CubeMap")

            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureBaseLevel, 0)
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            TextureStorage2D(target, CUBE_TEXTURE_ID, dds_header.mipMapCount, format_info.texture_format, dds_header.width, dds_header.height)

            Dim e1 = GL.GetError()
            If e1 > 0 Then
                Stop
            End If

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
                    GL.CompressedTextureSubImage3D(CUBE_TEXTURE_ID, i, 0, 0, face, w, h, 1, DirectCast(format_info.texture_format, OpenGL.PixelFormat), size, data)
                    w /= 2
                    h /= 2
                Next
            Next
            TextureParameter(target, CUBE_TEXTURE_ID, TextureParameterName.TextureMaxLevel, mipMapCount - 1)

        End Using
        Dim e2 = GL.GetError()
        If e2 > 0 Then
            Stop
        End If

    End Sub

End Module

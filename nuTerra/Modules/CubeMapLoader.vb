Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL
Imports Tao.DevIl
Imports System.IO

Module CubeMapLoader
    Public Sub load_cube_and_cube_map()

        Dim TextureTargets() As Integer =
            {
               OpenGL.TextureTarget.TextureCubeMapNegativeX, OpenGL.TextureTarget.TextureCubeMapNegativeY,
               OpenGL.TextureTarget.TextureCubeMapNegativeZ, OpenGL.TextureTarget.TextureCubeMapPositiveX,
               OpenGL.TextureTarget.TextureCubeMapPositiveY, OpenGL.TextureTarget.TextureCubeMapPositiveZ
            }

        Dim iPath As String = Application.StartupPath + "\resources\cube\cubemap_m00_c0"

        GL.Enable(EnableCap.TextureCubeMap)
        If CUBE_TEXTURE_ID > 0 Then
            GL.DeleteTexture(CUBE_TEXTURE_ID)
            GL.Finish()
        End If
        GL.GenTextures(1, CUBE_TEXTURE_ID)
        GL.BindTexture(OpenGL.TextureTarget.TextureCubeMap, CUBE_TEXTURE_ID)

        'find our cube in teh maps package

        Dim entry = Packages.MAP_PACKAGE(CUBE_TEXTURE_PATH)
        If entry Is Nothing Then
            LogThis("cube not found " + CUBE_TEXTURE_PATH)
            Return
        End If
        Dim ms As New MemoryStream
        entry.Extract(ms)

        Dim imgStore(ms.Length) As Byte
        load_dds_cubemap_from_stream(ms)


    End Sub
    Public Sub load_dds_cubemap_from_stream(ms As MemoryStream)
        'Check if this image has already been loaded.
        GL.GenTextures(1, CUBE_TEXTURE_ID)

        ms.Position = 0
        Using br As New BinaryReader(ms, System.Text.Encoding.ASCII)
            Dim dds_header = get_dds_header(br)

            'Select Case dds_header.caps
            '    Case &H1000
            '        Debug.Assert(dds_header.mipMapCount = 0) ' Just Check
            '    Case &H401008
            '        Debug.Assert(dds_header.mipMapCount > 0) ' Just Check
            '    Case Else
            'End Select
            Debug.Assert(dds_header.caps2 And &H200 - &H200) ' Cubemap ?

            Dim format As SizedInternalFormat = dds_header.gl_format
            Dim blockSize = dds_header.gl_block_size

            ms.Position = 128

            GL.CreateTextures(TextureTarget.TextureCubeMap, 1, CUBE_TEXTURE_ID)
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, CUBE_TEXTURE_ID, -1, String.Format("TEX-{0}", "CubeMap"))

            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureBaseLevel, 0)
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureMaxLevel, dds_header.mipMapCount - 1)
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureMagFilter, TextureMinFilter.Linear)
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureMinFilter, TextureMinFilter.LinearMipmapLinear)
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureWrapS, TextureWrapMode.Repeat)
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureWrapT, TextureWrapMode.Repeat)

            GL.TextureStorage3D(CUBE_TEXTURE_ID, dds_header.mipMapCount, format, dds_header.width, dds_header.height, 6)


            Dim e1 = GL.GetError()
            If e1 > 0 Then
                Stop
            End If

            Dim w = dds_header.width
            Dim h = dds_header.height
            Dim mipMapCount = dds_header.mipMapCount
            For d = 0 To dds_header.depth

                For i = 0 To dds_header.mipMapCount - 1
                    If (w = 0 Or h = 0) Then
                        mipMapCount -= 1
                        Continue For
                    End If

                    Dim size = ((w + 3) \ 4) * ((h + 3) \ 4) * blockSize
                    Dim data = br.ReadBytes(size)
                    GL.CompressedTextureSubImage3D(CUBE_TEXTURE_ID, i, 0, 0, 0, w, h, d, DirectCast(format, OpenGL.PixelFormat), size, data)
                    w /= 2
                    h /= 2
                Next
            Next
            GL.TextureParameter(CUBE_TEXTURE_ID, TextureParameterName.TextureMaxLevel, mipMapCount - 1)

        End Using
        Dim e2 = GL.GetError()
        If e2 > 0 Then
            Stop
        End If

    End Sub

End Module

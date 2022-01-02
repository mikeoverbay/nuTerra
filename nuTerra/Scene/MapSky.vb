Imports System.IO
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Class MapSky
    Implements IDisposable

    ReadOnly scene As MapScene

    Public skybox_mdl As XModel
    Public texture As GLTexture

    Public SUN_TEXTURE_PATH As String
    Public SUN_TEXTURE_ID As GLTexture
    Public CUBE_TEXTURE_ID As GLTexture
    Public CUBE_TEXTURE_PATH As String

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub draw_sky()
        If Not DONT_BLOCK_SKY Then Return

        GL_PUSH_GROUP("Draw_SkyDome")

        GL.Disable(EnableCap.DepthTest)
        GL.DepthMask(False)
        MainFBO.attach_CNGP()

        SkyDomeShader.Use()

        GL.Enable(EnableCap.CullFace)

        texture.BindUnit(0)

        skybox_mdl.vao.Bind()
        GL.DrawElements(PrimitiveType.Triangles, skybox_mdl.indices_count * 3, DrawElementsType.UnsignedShort, 0)

        SkyDomeShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)

        GL.DepthMask(True)

        draw_sun()

        GL.Enable(EnableCap.DepthTest)

        GL_POP_GROUP()
    End Sub

    Public Sub draw_sun()

        MainFBO.attach_C()

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)

        Dim matrix = Matrix4.CreateTranslation(LIGHT_POS)

        FF_BillboardShader.Use()
        GL.Uniform1(FF_BillboardShader("colorMap"), 0)
        GL.UniformMatrix4(FF_BillboardShader("matrix"), False, matrix)
        'GL.Uniform3(FF_BillboardShader("color"), SUN_RENDER_COLOR.X / 100.0F, SUN_RENDER_COLOR.Y / 100.0F, SUN_RENDER_COLOR.Z / 100.0F)
        GL.Uniform3(FF_BillboardShader("color"), 1.0F, 1.0F, 1.0F)
        GL.Uniform1(FF_BillboardShader("scale"), SUN_SCALE * 6)

        SUN_TEXTURE_ID.BindUnit(0)

        GL.Uniform4(FF_BillboardShader("rect"), -0.5F, -0.5F, 0.5F, 0.5F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        FF_BillboardShader.StopUse()

        GL.Disable(EnableCap.Blend)

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Public Sub load_cube_and_cube_map()
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
            Dim dds_header = TextureMgr.get_dds_header(br)

            Debug.Assert((dds_header.caps2 And &H200) = &H200) ' Cubemap ?

            Dim faces = dds_header.faces
            Debug.Assert(faces = 6)

            Dim format_info = dds_header.format_info

            ms.Position = 128

            CUBE_TEXTURE_ID = GLTexture.Create(TextureTarget.TextureCubeMap, "CubeMap")

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

    Public Sub Dispose() Implements IDisposable.Dispose
        skybox_mdl?.vao.Dispose()
        texture?.Dispose()
        CUBE_TEXTURE_ID?.Dispose()
    End Sub
End Class

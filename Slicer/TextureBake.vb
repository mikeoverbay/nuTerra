Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Bakes a material into a flat map in UV2 space, so the model can be rendered
''' by anything.
'''
''' THE IDEA IS THE OWNER'S, from Tank Exporter's textureBuilder shaders: mix the
''' atlas once, write it to an FBO, and ship the result. A tiled WoT material is
''' three tiles height-blended through a mask plus a dirt layer, and no
''' application outside the game is going to reproduce that. Bake it and the
''' problem disappears - one texture, one UV set, renders in Blender or a print
''' preview with no WoT knowledge at all.
'''
''' HOW THE BAKE WORKS. The mesh is rasterised with its UV2 AS THE POSITION -
''' gl_Position = (uv2 * 2 - 1) - so the framebuffer IS the UV2 square, and each
''' pixel of the output is the point of the surface that lands there. While that
''' happens the shader samples the source maps at UV1, which is where they are
''' actually addressed. The output is therefore the material, resampled from the
''' tiling space it was authored in into the unwrap space the model exports in.
'''
''' THIS IS NEEDED FOR PBS_ext TOO, which is not obvious. A PBS_ext material has
''' flat maps already, so the temptation is to copy the DDS across and be done -
''' but those maps are addressed by UV1, and the exported mesh carries UV2. Ship
''' the source texture and it lines up with nothing. Every material gets baked,
''' for the same reason.
'''
''' SEAMS. A triangle's pixels stop at the triangle's edge, so the gap between
''' two UV islands is never written and samples as background. Bilinear filtering
''' then drags that background in along every island edge. The usual fix is to
''' dilate the result outward a few pixels; this does not do that yet, and it is
''' listed as a known limit rather than hidden - at 2048 with islands well inside
''' the square it is a thin artefact, but it IS there.
''' </summary>
Public Class BakeResult
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Pixels As Byte()        ' RGB, top-down, ready for PngWriter
    Public Property Coverage As Single      ' fraction of the square actually written
End Class

Public NotInheritable Class TextureBake

    ' Position comes from UV2; UV1 rides along for sampling. The V flip puts the
    ' result the same way up as the OBJ's own vt, which is flipped on write.
    Private Const VERT As String =
        "#version 330 core" & vbLf &
        "layout(location = 0) in vec2 a_uv1;" & vbLf &
        "layout(location = 1) in vec2 a_uv2;" & vbLf &
        "out vec2 v_uv1;" & vbLf &
        "void main() {" & vbLf &
        "    v_uv1 = a_uv1;" & vbLf &
        "    gl_Position = vec4(a_uv2 * 2.0 - 1.0, 0.0, 1.0);" & vbLf &
        "}" & vbLf

    ''' <summary>
    ''' One source map, sampled at UV1 and written through unchanged, which is
    ''' what a PBS_ext material needs. The tiled composite is a separate shader
    ''' when it lands.
    '''
    ''' u_swizzle rebuilds a normal map. A DXT5nm normal keeps X in ALPHA and Y
    ''' in GREEN with Z implied - written out as-is the PNG is meaningless to
    ''' any other tool, so it is turned back into an ordinary RGB normal map
    ''' that Blender and every slicer understand.
    ''' </summary>
    Private Const FRAG As String =
        "#version 330 core" & vbLf &
        "in vec2 v_uv1;" & vbLf &
        "uniform sampler2D u_src;" & vbLf &
        "uniform int u_swizzle;" & vbLf &
        "out vec4 o_col;" & vbLf &
        "void main() {" & vbLf &
        "    vec4 c = texture(u_src, v_uv1);" & vbLf &
        "    if (u_swizzle == 1) {" & vbLf &
        "        vec2 xy = vec2(c.a, c.g) * 2.0 - 1.0;" & vbLf &
        "        float z = sqrt(max(0.0, 1.0 - dot(xy, xy)));" & vbLf &
        "        c = vec4(xy * 0.5 + 0.5, z * 0.5 + 0.5, 1.0);" & vbLf &
        "    }" & vbLf &
        "    o_col = vec4(c.rgb, 1.0);" & vbLf &
        "}" & vbLf

    Private Shared program As Integer = 0

    Private Shared Sub EnsureProgram()
        If program <> 0 Then Return
        Dim vs = GL.CreateShader(ShaderType.VertexShader)
        GL.ShaderSource(vs, VERT) : GL.CompileShader(vs)
        Dim ok As Integer
        GL.GetShader(vs, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception("bake vertex: " & GL.GetShaderInfoLog(vs))
        Dim fs = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fs, FRAG) : GL.CompileShader(fs)
        GL.GetShader(fs, ShaderParameter.CompileStatus, ok)
        If ok = 0 Then Throw New Exception("bake fragment: " & GL.GetShaderInfoLog(fs))
        program = GL.CreateProgram()
        GL.AttachShader(program, vs) : GL.AttachShader(program, fs)
        GL.LinkProgram(program)
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, ok)
        If ok = 0 Then Throw New Exception("bake link: " & GL.GetProgramInfoLog(program))
        GL.DeleteShader(vs) : GL.DeleteShader(fs)
    End Sub

    ''' <summary>
    ''' Bake one source texture through one mesh's UV1 -> UV2 mapping.
    '''
    ''' `uv1`/`uv2` are per-vertex and must be the same length; `idx` indexes
    ''' both. `swizzle` rebuilds a DXT5nm normal into a plain RGB one.
    ''' </summary>
    Public Shared Function Bake(uv1 As Vector2(), uv2 As Vector2(), idx As Integer(),
                                srcTex As Integer, size As Integer,
                                swizzle As Boolean) As BakeResult
        EnsureProgram()
        If size < 64 Then size = 64

        Dim fbo = GL.GenFramebuffer()
        Dim tex = GL.GenTexture()
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        GL.BindTexture(TextureTarget.Texture2D, tex)
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0,
                      PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Linear))
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                TextureTarget.Texture2D, tex, 0)
        If GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) <> FramebufferErrorCode.FramebufferComplete Then
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
            GL.DeleteFramebuffer(fbo) : GL.DeleteTexture(tex)
            Return Nothing
        End If

        ' Interleave the two UV sets for the draw.
        Dim n = Math.Min(uv1.Length, uv2.Length)
        Dim verts(n * 4 - 1) As Single
        For i = 0 To n - 1
            verts(i * 4) = uv1(i).X
            verts(i * 4 + 1) = uv1(i).Y
            verts(i * 4 + 2) = uv2(i).X
            verts(i * 4 + 3) = uv2(i).Y
        Next

        Dim vao = GL.GenVertexArray()
        Dim vbo = GL.GenBuffer()
        Dim ebo = GL.GenBuffer()
        GL.BindVertexArray(vao)
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo)
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * 4, verts, BufferUsageHint.StreamDraw)
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo)
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StreamDraw)
        GL.EnableVertexAttribArray(0) : GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, False, 16, 0)
        GL.EnableVertexAttribArray(1) : GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, False, 16, 8)

        Dim wasDepth = GL.IsEnabled(EnableCap.DepthTest)
        Dim wasCull = GL.IsEnabled(EnableCap.CullFace)
        ' No depth and no culling: this is a UV-space rasterisation, so
        ' "in front" is meaningless and an island wound either way must still
        ' be written.
        GL.Disable(EnableCap.DepthTest)
        GL.Disable(EnableCap.CullFace)
        GL.Viewport(0, 0, size, size)
        ' Magenta clear, so anything the bake did NOT cover is obvious in the
        ' output instead of passing for black.
        GL.ClearColor(1.0F, 0.0F, 1.0F, 1.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit)

        GL.UseProgram(program)
        GL.Uniform1(GL.GetUniformLocation(program, "u_src"), 0)
        GL.Uniform1(GL.GetUniformLocation(program, "u_swizzle"), If(swizzle, 1, 0))
        GL.ActiveTexture(TextureUnit.Texture0)
        GL.BindTexture(TextureTarget.Texture2D, srcTex)
        GL.DrawElements(PrimitiveType.Triangles, idx.Length, DrawElementsType.UnsignedInt, 0)

        Dim buf(size * size * 4 - 1) As Byte
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1)
        GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, buf)

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)
        GL.UseProgram(0)
        GL.BindVertexArray(0)
        GL.DeleteBuffer(vbo) : GL.DeleteBuffer(ebo) : GL.DeleteVertexArray(vao)
        GL.DeleteFramebuffer(fbo) : GL.DeleteTexture(tex)
        If wasDepth Then GL.Enable(EnableCap.DepthTest)
        If wasCull Then GL.Enable(EnableCap.CullFace)

        ' GL hands back rows bottom-up; PNG wants top-down.
        Dim rgb(size * size * 3 - 1) As Byte
        Dim covered = 0
        For y = 0 To size - 1
            Dim src = (size - 1 - y) * size * 4
            For x = 0 To size - 1
                Dim s = src + x * 4
                Dim d = (y * size + x) * 3
                rgb(d) = buf(s) : rgb(d + 1) = buf(s + 1) : rgb(d + 2) = buf(s + 2)
                ' Magenta means untouched. Counting it gives a coverage figure,
                ' which is the number that says whether the bake actually hit
                ' the model or quietly missed.
                If Not (buf(s) = 255 AndAlso buf(s + 1) = 0 AndAlso buf(s + 2) = 255) Then covered += 1
            Next
        Next

        Return New BakeResult With {.Width = size, .Height = size, .Pixels = rgb,
                                    .Coverage = covered / CSng(size * size)}
    End Function
End Class

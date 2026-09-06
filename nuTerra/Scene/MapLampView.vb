Imports OpenTK.Graphics.OpenGL4

Imports OpenTK.Mathematics

''' <summary>
''' Four orthographic views of one model - top, left, right and iso - with a 3D
''' cursor, drawn into a single texture for an ImGui panel.
'''
''' What it is FOR: the light catalogue needs to know where a lamp's bulb sits
''' on its post, and that number is currently a guess (6.50 m for every model,
''' taken from one lamp on one map). Reading it off the mesh is the only honest
''' way to get it, and there are 19 street lamp models to read.
'''
''' Untextured on purpose. A diffuse map is in the way here: a dark painted
''' fixture on a dark painted pole hides the very silhouette being measured.
''' Grey and one light shows the form.
'''
''' ORTHOGRAPHIC, also on purpose. These views are for MEASURING - a
''' perspective view has no fixed scale, so a height read off one is worth
''' nothing. Ortho makes screen distance proportional to metres, which is what
''' lets the cursor be placed by eye and trusted.
'''
''' Draws from the MAP's shared vertex and index buffers via MODEL_GEOM, so a
''' map has to be loaded. That is the cheap route: the geometry is already
''' resident, and the CPU-side arrays are Erased at load, so re-reading the
''' .primitives would mean a second loader path for no gain.
''' </summary>
Public Class MapLampView
    Implements IDisposable

    ''' <summary>Edge of one of the four panes, pixels. The texture is 2x2 of
    ''' these.</summary>
    Public Shared PANE As Integer = 320

    Public fbo As GLFramebuffer
    Public color_tex As GLTexture
    Private depth_rb As GLRenderbuffer

    Private cursor_vao As GLVertexArray
    Private cursor_vbo As GLBuffer

    ''' <summary>The 3D cursor, in MODEL space - the same frame the catalogue's
    ''' bulb offset is in, so what is read here is what gets typed in.</summary>
    Public cursor As Vector3 = Vector3.Zero

    ''' <summary>Which model is on show. -1 for none.</summary>
    Public model_id As Integer = -1

    ''' <summary>Frames the views. Set from the model's bounds on each change.</summary>
    Public bounds_min As Vector3
    Public bounds_max As Vector3
    Public ready As Boolean

    Public Sub New()
    End Sub

    ''' <summary>
    ''' Point the inspector at a model and frame it. Cheap - no geometry moves.
    ''' </summary>
    Public Sub Show(id As Integer)
        model_id = -1
        ready = False
        If MAP_MODELS Is Nothing OrElse id < 0 OrElse id >= MAP_MODELS.Length Then Return
        If Not MODEL_GEOM.ContainsKey(id) Then Return

        Dim vb = MAP_MODELS(id).visibilityBounds
        bounds_min = New Vector3(Math.Min(vb.Row0.X, vb.Row1.X),
                                 Math.Min(vb.Row0.Y, vb.Row1.Y),
                                 Math.Min(vb.Row0.Z, vb.Row1.Z))
        bounds_max = New Vector3(Math.Max(vb.Row0.X, vb.Row1.X),
                                 Math.Max(vb.Row0.Y, vb.Row1.Y),
                                 Math.Max(vb.Row0.Z, vb.Row1.Z))

        ' A lamp post is tall and thin, so the cursor starts at the top of the
        ' box on the centre line - within a metre of the bulb on every street
        ' lamp in the game, which saves most of the dragging.
        cursor = New Vector3((bounds_min.X + bounds_max.X) * 0.5F,
                             bounds_max.Y,
                             (bounds_min.Z + bounds_max.Z) * 0.5F)
        model_id = id
        ready = True
    End Sub

    ''' <summary>Half the size of the box, plus a margin, for the ortho extent.</summary>
    Private Function frame_radius() As Single
        Dim d = bounds_max - bounds_min
        Return Math.Max(0.5F, Math.Max(d.X, Math.Max(d.Y, d.Z)) * 0.62F)
    End Function

    Private Function centre() As Vector3
        Return (bounds_min + bounds_max) * 0.5F
    End Function

    ''' <summary>
    ''' View-projection for one pane. 0 top, 1 left, 2 right, 3 iso.
    '''
    ''' All four are orthographic and all four share one radius, so the panes
    ''' are at the SAME SCALE as each other. That is what makes a height read
    ''' off the left view agree with the one read off the right.
    ''' </summary>
    Public Function pane_matrix(which As Integer) As Matrix4
        Dim c = centre()
        Dim r = frame_radius()
        Dim eye As Vector3
        Dim up As Vector3 = Vector3.UnitY

        Select Case which
            Case 0  ' top, looking down
                eye = c + New Vector3(0.0F, r * 3.0F, 0.0F)
                up = New Vector3(0.0F, 0.0F, -1.0F)
            Case 1  ' left, looking along +X
                eye = c + New Vector3(-r * 3.0F, 0.0F, 0.0F)
            Case 2  ' right, looking along -X
                eye = c + New Vector3(r * 3.0F, 0.0F, 0.0F)
            Case Else ' iso
                eye = c + New Vector3(r * 2.2F, r * 1.8F, r * 2.2F)
        End Select

        Dim view = Matrix4.LookAt(eye, c, up)
        Dim proj = Matrix4.CreateOrthographic(r * 2.0F, r * 2.0F, 0.01F, r * 12.0F)
        Return view * proj
    End Function

    ''' <summary>Where the camera sits for a pane - the specular needs it.</summary>
    Private Function pane_eye(which As Integer) As Vector3
        Dim c = centre()
        Dim r = frame_radius()
        Select Case which
            Case 0 : Return c + New Vector3(0.0F, r * 3.0F, 0.0F)
            Case 1 : Return c + New Vector3(-r * 3.0F, 0.0F, 0.0F)
            Case 2 : Return c + New Vector3(r * 3.0F, 0.0F, 0.0F)
            Case Else : Return c + New Vector3(r * 2.2F, r * 1.8F, r * 2.2F)
        End Select
    End Function

    ''' <summary>
    ''' Render all four panes into the one texture. Call once a frame while the
    ''' panel is open; it binds and restores its own framebuffer.
    ''' </summary>
    Public Sub Render()
        If Not ready OrElse map_scene Is Nothing Then Return
        If Not MODEL_GEOM.ContainsKey(model_id) Then Return

        create_target()
        build_cursor()

        GL_PUSH_GROUP("MapLampView::Render")

        fbo.Bind(FramebufferTarget.Framebuffer)

        ' Plain depth ordering, and put it BACK. The engine runs reversed-Z
        ' globally - same trap the shadow bakes have.
        GL.DepthFunc(DepthFunction.Less)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)
        ' Hollow shells with no bottom faces: culling turns a view from
        ' underneath into a hole. The shader lights both sides instead.
        GL.Disable(EnableCap.CullFace)

        GL.ClearColor(0.12F, 0.13F, 0.16F, 1.0F)
        GL.ClearDepth(1.0)
        GL.Viewport(0, 0, PANE * 2, PANE * 2)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        Dim ranges = MODEL_GEOM(model_id)

        For pane = 0 To 3
            Dim px = (pane Mod 2) * PANE
            Dim py = (1 - (pane \ 2)) * PANE
            GL.Viewport(px, py, PANE, PANE)

            Dim vp = pane_matrix(pane)
            Dim eye = pane_eye(pane)

            ' --- the model -------------------------------------------------
            lampViewShader.Use()
            GL.UniformMatrix4(lampViewShader("mvp"), False, vp)
            GL.Uniform3(lampViewShader("view_pos"), eye.X, eye.Y, eye.Z)
            GL.Uniform3(lampViewShader("base_color"), 0.72F, 0.72F, 0.75F)
            ' Fixed relative to the VIEW, not the world, so every pane is lit
            ' the same way and a face that reads as bright in one is not black
            ' in the next.
            Dim ld = Vector3.Normalize(eye - centre() + New Vector3(0.3F, 0.9F, 0.2F))
            GL.Uniform3(lampViewShader("light_dir"), ld.X, ld.Y, ld.Z)

            map_scene.static_models.allMapModels.Bind()
            For Each g In ranges
                GL.DrawElementsBaseVertex(PrimitiveType.Triangles, g.count,
                                          DrawElementsType.UnsignedInt,
                                          New IntPtr(CLng(g.firstIndex) * 4L), g.baseVertex)
            Next
            lampViewShader.StopUse()

            ' --- the cursor ------------------------------------------------
            ' Depth test OFF so it is never buried inside the mesh. The whole
            ' point is to place it against geometry, which means seeing it
            ' when it is inside the lamp housing.
            GL.Disable(EnableCap.DepthTest)
            lampCursorShader.Use()
            GL.UniformMatrix4(lampCursorShader("mvp"), False, vp)
            cursor_vao.Bind()
            GL.Uniform4(lampCursorShader("line_color"), 1.0F, 0.85F, 0.2F, 1.0F)
            GL.DrawArrays(PrimitiveType.Lines, 0, 6)
            lampCursorShader.StopUse()
            GL.Enable(EnableCap.DepthTest)
        Next

        ' restore the engine's global state
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)
        GL.Enable(EnableCap.CullFace)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0)

        GL_POP_GROUP()
    End Sub

    ''' <summary>Three axis-aligned segments through the cursor, sized to the
    ''' model so they stay readable whatever it is.</summary>
    Private Sub build_cursor()
        Dim r = frame_radius() * 0.18F
        Dim v() As Single = {
            cursor.X - r, cursor.Y, cursor.Z, cursor.X + r, cursor.Y, cursor.Z,
            cursor.X, cursor.Y - r, cursor.Z, cursor.X, cursor.Y + r, cursor.Z,
            cursor.X, cursor.Y, cursor.Z - r, cursor.X, cursor.Y, cursor.Z + r}

        If cursor_vbo Is Nothing Then
            cursor_vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "lampCursorVbo")
            ' DynamicStorageBit, because the cursor moves. The immutable
            ' Storage overloads the rest of the engine uses would make this a
            ' write-once buffer and the crosshair would never leave the origin.
            cursor_vbo.StorageNullData(v.Length * 4, BufferStorageFlags.DynamicStorageBit)
            cursor_vao = GLVertexArray.Create("lampCursorVao")
            cursor_vao.VertexBuffer(0, cursor_vbo, IntPtr.Zero, 3 * 4)
            cursor_vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
            cursor_vao.AttribBinding(0, 0)
            cursor_vao.EnableAttrib(0)
        End If
        GL.NamedBufferSubData(cursor_vbo.buffer_id, IntPtr.Zero, v.Length * 4, v)
    End Sub

    Private Sub create_target()
        If color_tex IsNot Nothing Then Return

        color_tex = GLTexture.Create(TextureTarget.Texture2D, "LampViewColor")
        color_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_tex.Storage2D(1, SizedInternalFormat.Rgba8, PANE * 2, PANE * 2)

        depth_rb = GLRenderbuffer.Create("LampViewDepth")
        depth_rb.Storage(RenderbufferStorage.DepthComponent24, PANE * 2, PANE * 2)

        fbo = GLFramebuffer.Create("LampViewFBO")
        fbo.Texture(FramebufferAttachment.ColorAttachment0, color_tex, 0)
        fbo.Renderbuffer(FramebufferAttachment.DepthAttachment,
                         RenderbufferTarget.Renderbuffer, depth_rb)
        GL.NamedFramebufferDrawBuffer(fbo.fbo_id, DrawBufferMode.ColorAttachment0)

        If Not fbo.IsComplete Then
            LogThis("lamp view: framebuffer incomplete")
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        color_tex?.Dispose() : color_tex = Nothing
        depth_rb?.Dispose() : depth_rb = Nothing
        fbo?.Dispose() : fbo = Nothing
        cursor_vao?.Dispose() : cursor_vao = Nothing
        cursor_vbo?.Dispose() : cursor_vbo = Nothing
        GC.SuppressFinalize(Me)
    End Sub
End Class

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
''' Draws from LAMP_MESHES - its OWN small position+normal copy of each light
''' model, taken alongside the main upload at load time. The models stay in the
''' shared buffers and render exactly as before; nothing is moved out of the
''' main path.
'''
''' Its own buffers rather than a range into the shared ones, because the shared
''' vertex buffer is 56 bytes a vertex in a layout the main shaders expect, and
''' a viewer reading it would have to match that layout and stay matched. 24
''' bytes of position and normal owes the renderer nothing.
''' </summary>
Public Class MapLampView
    Implements IDisposable

    ''' <summary>Current render target size, in pixels. Follows the ImGui
    ''' pane it is drawn into, so it changes whenever the splitter moves or the
    ''' window is resized.</summary>
    Public tex_w As Integer = 0
    Public tex_h As Integer = 0

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
        If Not LAMP_MESHES.ContainsKey(id) Then Return

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
    ''' <summary>
    ''' Draw into the target at the given size. Called with the ImGui pane's
    ''' available region, so the surface always matches the pane exactly rather
    ''' than being scaled into it.
    '''
    ''' Always clears, even with no model. An empty pane that is a LIVE cleared
    ''' surface and an empty pane that is a dead texture look identical, and
    ''' only one of them means the GL side is working.
    ''' </summary>
    Public Sub Render(w As Integer, h As Integer)
        If w < 8 OrElse h < 8 Then Return

        create_target(w, h)
        If fbo Is Nothing Then Return

        GL_PUSH_GROUP("MapLampView::Render")

        ' SAVE EVERYTHING THIS PASS TOUCHES.
        '
        ' Learned the hard way: an earlier version bound framebuffer 0 on the
        ' way out and left depth test on and blending off. That was harmless
        ' while it early-returned with no model loaded, and took the whole UI
        ' down the moment it started running every frame - ImGui draws blended
        ' with the depth test off, into whatever framebuffer was bound.
        '
        ' Binding 0 is the specific mistake: it assumes the default framebuffer
        ' was the target, and it is not - the engine renders into MainFBO. Ask
        ' what was bound and put THAT back.
        Dim prev_fbo = GL.GetInteger(GetPName.FramebufferBinding)
        Dim prev_vp(3) As Integer
        GL.GetInteger(GetPName.Viewport, prev_vp)
        Dim was_blend = GL.IsEnabled(EnableCap.Blend)
        Dim was_depth = GL.IsEnabled(EnableCap.DepthTest)
        Dim was_cull = GL.IsEnabled(EnableCap.CullFace)

        fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Disable(EnableCap.Blend)
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.DepthMask(True)
        GL.ClearColor(0.10F, 0.11F, 0.14F, 1.0F)
        GL.ClearDepth(1.0)
        GL.Viewport(0, 0, tex_w, tex_h)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        If ready AndAlso LAMP_MESHES.ContainsKey(model_id) Then
            draw_model_panes()
        End If

        ' Back exactly as found. The reversed-Z pair are global engine state,
        ' the rest belongs to whatever pass was interrupted.
        GL.DepthFunc(DepthFunction.Greater)
        GL.ClearDepth(0.0F)
        If was_blend Then GL.Enable(EnableCap.Blend) Else GL.Disable(EnableCap.Blend)
        If was_depth Then GL.Enable(EnableCap.DepthTest) Else GL.Disable(EnableCap.DepthTest)
        If was_cull Then GL.Enable(EnableCap.CullFace) Else GL.Disable(EnableCap.CullFace)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prev_fbo)
        GL.Viewport(prev_vp(0), prev_vp(1), prev_vp(2), prev_vp(3))
        GL_POP_GROUP()
    End Sub

    Private Sub draw_model_panes()
        build_cursor()

        ' Hollow shells with no bottom faces: culling turns a view from
        ' underneath into a hole. The shader lights both sides instead.
        GL.Disable(EnableCap.CullFace)

        Dim meshes = LAMP_MESHES(model_id)
        Dim half_w = tex_w \ 2
        Dim half_h = tex_h \ 2

        For pane = 0 To 3
            Dim px = (pane Mod 2) * half_w
            Dim py = (1 - (pane \ 2)) * half_h
            GL.Viewport(px, py, half_w, half_h)

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

            For Each mesh In meshes
                mesh.vao.Bind()
                GL.DrawElements(PrimitiveType.Triangles, mesh.index_count,
                                DrawElementsType.UnsignedInt, IntPtr.Zero)
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

    ''' <summary>
    ''' Make or remake the target at w x h.
    '''
    ''' Recreated whenever the size changes, because the pane is resizable and
    ''' a fixed target would either be scaled - blurring the very lines this is
    ''' meant to measure - or cropped.
    ''' </summary>
    Private Sub create_target(w As Integer, h As Integer)
        If color_tex IsNot Nothing AndAlso tex_w = w AndAlso tex_h = h Then Return

        color_tex?.Dispose() : color_tex = Nothing
        depth_rb?.Dispose() : depth_rb = Nothing
        fbo?.Dispose() : fbo = Nothing
        tex_w = w
        tex_h = h

        color_tex = GLTexture.Create(TextureTarget.Texture2D, "LampViewColor")
        color_tex.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Linear)
        color_tex.Parameter(TextureParameterName.TextureWrapS, TextureWrapMode.ClampToEdge)
        color_tex.Parameter(TextureParameterName.TextureWrapT, TextureWrapMode.ClampToEdge)
        color_tex.Storage2D(1, SizedInternalFormat.Rgba8, tex_w, tex_h)

        depth_rb = GLRenderbuffer.Create("LampViewDepth")
        depth_rb.Storage(RenderbufferStorage.DepthComponent24, tex_w, tex_h)

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

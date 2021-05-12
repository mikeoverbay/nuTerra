Imports System.Math
Imports System.IO
Imports OpenTK
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL4

Public Class MapMinimap
    Implements IDisposable

    Public TEAM_1_ICON_ID As GLTexture
    Public TEAM_2_ICON_ID As GLTexture
    Public MINI_MAP_ID As GLTexture
    Public MINI_NUMBERS_ID As GLTexture
    Public MINI_LETTERS_ID As GLTexture
    Public MINI_TRIM_VERT_ID As GLTexture
    Public MINI_TRIM_HORZ_ID As GLTexture

    Public Sub New()
        MINI_LETTERS_ID = load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\mini_letters.png"), False, False)
        MINI_NUMBERS_ID = load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\mini_numbers.png"), False, False)
        MINI_TRIM_VERT_ID = load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\mini_trim_vert.png"), False, False)
        MINI_TRIM_HORZ_ID = load_png_image_from_file(Path.Combine(Application.StartupPath, "resources\mini_trim_horz.png"), False, False)
    End Sub

    Public Sub draw_mini_map()
        'check if we have the mini map loaded.
        If MINI_MAP_ID Is Nothing Then
            Return
        End If
        GL_PUSH_GROUP("draw_mini_map")

        GL.DepthMask(False)
        GL.Disable(EnableCap.DepthTest)

        '===========================================================================
        ' Animate map growth
        'need to control this so it is not affected by frame rate!
        Dim s = CInt(150 * DELTA_TIME)
        If MINI_MAP_SIZE <> MINI_MAP_NEW_SIZE Then
            If MINI_MAP_SIZE < MINI_MAP_NEW_SIZE Then
                MINI_MAP_SIZE += s
                If MINI_MAP_SIZE > MINI_MAP_NEW_SIZE Then
                    MINI_MAP_SIZE = MINI_MAP_NEW_SIZE
                End If
            Else
                If MINI_MAP_SIZE > MINI_MAP_NEW_SIZE Then
                    MINI_MAP_SIZE -= s
                    If MINI_MAP_SIZE < MINI_MAP_NEW_SIZE Then
                        MINI_MAP_SIZE = MINI_MAP_NEW_SIZE
                    End If
                End If
            End If
            'sized changed so we must resize the FBOmini
            MiniMapFBO.FBO_Initialize(MINI_MAP_SIZE)
            MiniMapFBO.fbo.Bind(FramebufferTarget.Framebuffer)
            Ortho_MiniMap(MINI_MAP_SIZE)
            MiniMapFBO.attach_gcolor()
            'render to gcolor and blit it to the screeenTexture buffer
            GL.ClearColor(0.0, 0.0, 0.5, 0.0)
            GL.Clear(ClearBufferMask.ColorBufferBit)
            Draw_mini()
            draw_mini_position()
        Else
            MiniMapFBO.fbo.Bind(FramebufferTarget.Framebuffer)
            Ortho_MiniMap(MINI_MAP_SIZE)
            MiniMapFBO.attach_gcolor()
            draw_mini_position()
        End If

        get_world_Position_In_Minimap_Window(M_POS)
        '===========================================================================
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) '================
        '===========================================================================
        GL.Disable(EnableCap.Blend)
        Ortho_main()
        Dim size = frmMain.glControl_main.Size
        Dim cx = size.Width - MINI_MAP_SIZE
        Dim cy = size.Height - MINI_MAP_SIZE
        draw_image_rectangle(New RectangleF(cx, cy,
                                                MINI_MAP_SIZE, MINI_MAP_SIZE),
                                                MiniMapFBO.gColor)

        '=======================================================================
        'draw mini map legends
        '=======================================================================
        'setup
        GL.Enable(EnableCap.Blend)
        TextRenderShader.Use()
        GL.Uniform4(TextRenderShader("color"), Color4.White)
        GL.UniformMatrix4(TextRenderShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(TextRenderShader("divisor"), 1.0F) 'atlas size
        GL.Uniform1(TextRenderShader("index"), 0.0F)
        GL.Uniform1(TextRenderShader("mask"), 0)

        '=======================================================================
        'draw horz trim
        MINI_TRIM_HORZ_ID.BindUnit(0)
        Dim rect As New RectangleF(cx - 12, cy - 12, 640 + 12, 16.0F)
        GL.Uniform4(TextRenderShader("rect"),
                  rect.Left,
                  -rect.Top,
                  rect.Right,
                  -rect.Bottom)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'draw vert trim
        MINI_TRIM_VERT_ID.BindUnit(0)
        rect = New RectangleF(cx - 12, cy - 12, 16.0F, 640 + 12.0F)
        GL.Uniform4(TextRenderShader("rect"),
                 rect.Left,
                 -rect.Top,
                 rect.Right,
                 -rect.Bottom)
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        '=======================================================================

        'row
        '=======================================================================
        GL.Uniform1(TextRenderShader("divisor"), 10.0F) 'atlas size

        GL.Uniform1(TextRenderShader("col_row"), 1) 'draw row
        MINI_NUMBERS_ID.BindUnit(0)

        Dim index! = 0
        Dim cnt! = 10.0F
        Dim step_s! = MINI_MAP_SIZE / 10.0F
        For xp = cx To cx + MINI_MAP_SIZE Step step_s
            GL.Uniform1(TextRenderShader("index"), index)

            rect = New RectangleF(xp + (step_s / 2.0F) - 8, cy - 11, 16.0F, 10.0F)
            GL.Uniform4(TextRenderShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
        Next
        'column
        '=======================================================================
        index = 0
        GL.Uniform1(TextRenderShader("col_row"), 0) 'draw row
        MINI_LETTERS_ID.BindUnit(0)

        cnt! = 10.0F
        step_s! = MINI_MAP_SIZE / 10.0F
        For yp = cy To cy + MINI_MAP_SIZE Step step_s
            GL.Uniform1(TextRenderShader("index"), index)

            rect = New RectangleF(cx - 9, yp + (step_s / 2) - 6, 8.0F, 12.0F)
            GL.Uniform4(TextRenderShader("rect"),
                        rect.Left,
                        -rect.Top,
                        rect.Right,
                        -rect.Bottom)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
            index += 1.0F
        Next
        TextRenderShader.StopUse()
        GL.BindTextureUnit(0, 0)


        GL_POP_GROUP()
    End Sub

    Public Sub Draw_mini()
        GL_PUSH_GROUP("Draw_mini")

        '======================================================
        'Draw all the shit on top of this image
        draw_minimap_texture()
        '======================================================

        GL.Enable(EnableCap.Blend)

        '======================================================
        If BASE_RINGS_LOADED Then
            draw_mini_base_rings()
        End If
        '======================================================

        '======================================================
        If BASE_RINGS_LOADED Then
            draw_mini_base_ids()
        End If
        '======================================================

        '======================================================
        draw_mini_grids_lines()
        '======================================================

        'now, bilt this to screenTexture
        MiniMapFBO.attach_both()
        MiniMapFBO.blit_to_screenTexture()
        MiniMapFBO.attach_gcolor()

        GL_POP_GROUP()
    End Sub

    Private Sub draw_minimap_texture()
        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        Dim rect As New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)
        image2dShader.Use()
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        MINI_MAP_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
                    rect.Left,
                    -rect.Top,
                    rect.Right,
                    -rect.Bottom)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        image2dShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Public Sub draw_mini_base_ids()
        GL_PUSH_GROUP("draw_mini_base_ids")

        'need to scale with the map
        Dim i_size = 30.0F

        Dim pos_t1 As New RectangleF(-TEAM_1.X + i_size, -TEAM_1.Z - i_size, -i_size * 2, i_size * 2)
        Dim pos_t2 As New RectangleF(-TEAM_2.X + i_size, -TEAM_2.Z - i_size, -i_size * 2, i_size * 2)

        image2dShader.Use()

        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        'Icon 1
        TEAM_1_ICON_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t1.Left,
            pos_t1.Top,
            pos_t1.Right,
            pos_t1.Bottom)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        'Icon 2
        TEAM_2_ICON_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos_t2.Left,
            pos_t2.Top,
            pos_t2.Right,
            pos_t2.Bottom)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        ' UNBIND
        GL.BindTextureUnit(0, 0)
        image2dShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub draw_mini_base_rings()
        GL_PUSH_GROUP("draw_mini_base_rings")

        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)
        'draw base rings
        MiniMapRingsShader.Use()
        'constants

        GL.UniformMatrix4(MiniMapRingsShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(MiniMapRingsShader("radius"), 50.0F)
        GL.Uniform1(MiniMapRingsShader("thickness"), 2.5F)

        Dim m_size = New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)

        GL.Uniform4(MiniMapRingsShader("rect"),
            m_size.Left,
            -m_size.Top,
            m_size.Right,
            -m_size.Bottom)

        GL.Uniform2(MiniMapRingsShader("center"), TEAM_2.X, TEAM_2.Z)
        GL.Uniform4(MiniMapRingsShader("color"), Color4.DarkRed)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        GL.Uniform2(MiniMapRingsShader("center"), TEAM_1.X, TEAM_1.Z)
        GL.Uniform4(MiniMapRingsShader("color"), Color4.DarkGreen)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        MiniMapRingsShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub draw_mini_position()
        GL.Enable(EnableCap.Blend)
        GL_PUSH_GROUP("draw_mini_position")

        MiniMapFBO.attach_both()
        MiniMapFBO.blit_to_gBuffer() ' copy prerendered to screenTexture
        MiniMapFBO.attach_gcolor()
        'GoTo skip

        image2dShader.Use()
        GL.Uniform2(image2dShader("uv_scale"), 1.0F, 1.0F)

        Dim i_size = 32
        Dim pos As New RectangleF(-i_size, -i_size, i_size * 2, i_size * 2)

        Dim model_X = Matrix4.CreateTranslation(U_LOOK_AT_X, -U_LOOK_AT_Z, 0.0F)
        Dim model_R = Matrix4.CreateRotationZ(U_CAM_X_ANGLE)
        Dim modelMatrix = model_R * model_X

        DIRECTION_TEXTURE_ID.BindUnit(0)
        GL.UniformMatrix4(image2dShader("ProjectionMatrix"), False, modelMatrix * PROJECTIONMATRIX)
        GL.Uniform4(image2dShader("rect"),
            pos.Left,
            pos.Top,
            pos.Right,
            pos.Bottom)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        image2dShader.StopUse()

        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)

        'draw ring around pointer 
        MiniMapRingsShader.Use()
        'constants

        GL.UniformMatrix4(MiniMapRingsShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform1(MiniMapRingsShader("radius"), 40.0F)
        GL.Uniform1(MiniMapRingsShader("thickness"), 3.0F)

        Dim m_size = New RectangleF(MAP_BB_UR.X, MAP_BB_UR.Y, -w, -h)

        GL.Uniform4(MiniMapRingsShader("rect"),
            m_size.Left,
            -m_size.Top,
            m_size.Right,
            -m_size.Bottom)

        GL.Uniform2(MiniMapRingsShader("center"), -U_LOOK_AT_X, U_LOOK_AT_Z)
        GL.Uniform4(MiniMapRingsShader("color"), Color4.White)

        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        MiniMapRingsShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub draw_mini_grids_lines()
        GL_PUSH_GROUP("draw_mini_grids_lines")

        Dim w = Abs(MAP_BB_BL.X - MAP_BB_UR.X)
        Dim h = Abs(MAP_BB_BL.Y - MAP_BB_UR.Y)

        coloredline2dShader.Use()

        Dim co As Color4
        co = Color4.GhostWhite
        co.A = 0.5F ' tone down the brightness some

        GL.UniformMatrix4(coloredline2dShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(coloredline2dShader("color"), co)
        'vertical lines
        Dim step_size! = w / 10

        For x = MAP_BB_BL.X + step_size! To MAP_BB_UR.X - step_size! Step step_size!
            Dim pos As New RectangleF(x - 0.5, MAP_BB_BL.Y, 0.0F, h)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)

            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next
        'horizonal lines
        For y = MAP_BB_BL.Y + step_size! To MAP_BB_UR.Y - step_size! Step step_size!
            Dim pos As New RectangleF(MAP_BB_BL.X - 0.5, y, w, 0.0F)
            GL.Uniform4(coloredline2dShader("rect"),
                        pos.Left,
                        -pos.Top,
                        pos.Right,
                        -pos.Bottom)
            defaultVao.Bind()
            GL.DrawArrays(PrimitiveType.Lines, 0, 2)
        Next

        coloredline2dShader.StopUse()

        GL_POP_GROUP()
    End Sub

    Public Sub get_world_Position_In_Minimap_Window(ByRef pos As Vector2)
        MINI_MOUSE_CAPTURED = False

        Dim left = MainFBO.SCR_WIDTH - MINI_MAP_SIZE
        Dim top = MainFBO.SCR_HEIGHT - MINI_MAP_SIZE
        'Are we over the minimap?
        If M_MOUSE.X < left Then Return
        If M_MOUSE.Y < top Then Return

        pos.X = ((M_MOUSE.X - left) / MINI_MAP_SIZE) * 2.0 - 1.0
        pos.Y = ((M_MOUSE.Y - top) / MINI_MAP_SIZE) * 2.0 - 1.0
        Dim pos_v = New Vector4(pos.X, pos.Y, 0.0F, 0.0F)
        Dim world = UnProject(pos_v)
        MINI_WORLD_MOUSE_POSITION.X = world.X
        MINI_WORLD_MOUSE_POSITION.Y = -world.Y
        MINI_MOUSE_CAPTURED = True
        Return
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        TEAM_1_ICON_ID?.Dispose()
        TEAM_2_ICON_ID?.Dispose()
        MINI_MAP_ID?.Dispose()
        MINI_NUMBERS_ID?.Dispose()
        MINI_LETTERS_ID?.Dispose()
        MINI_TRIM_VERT_ID?.Dispose()
        MINI_TRIM_HORZ_ID?.Dispose()
    End Sub
End Class

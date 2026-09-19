Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Drawing. Terrain as meshes, flat, and nothing else yet.
'''
''' Added 2026-09-15 by nuTerra work, stage 2 of docs/brain_testing_plan.md.
''' </summary>
Module BrainRender

    Public Cam As New BrainCamera
    Private terrainShader As BrainShader

    ''' <summary>Indices per chunk, from build_Terrain_VAO's own indirect
    ''' command: 64 x 64 cells, two triangles each, three indices a triangle -
    ''' 24,576. Not recomputed here; the builder writes it into the command and
    ''' this is only the number the draw count is checked against.</summary>
    Private Const INDICES_PER_CHUNK As Integer = 24576

    ''' <summary>The matrix the LAST frame was actually drawn with. Kept
    ''' because a depth sample read back next frame has to be unprojected with
    ''' the matrix that produced it - the camera moves between frames, and the
    ''' chase moves it even while the mouse is busy elsewhere.</summary>
    Public LastVP As Matrix4 = Matrix4.Identity

    Public Sub Init()
        terrainShader = New BrainShader("terrain")
        BrainModels.Init()
        BrainTankDraw.Init()
        BrainRings.Init()
        BrainTrail.Init()
        BrainWalkView.Init()
        BrainRadar.Init()
        BrainGoal.Init()
        BrainStart.Init()

        ' The camera asks the same sampler the hulls and the nav grid ask, so
        ' the point it looks at is on the surface they all agree about.
        Cam.GroundAt = Function(x, z) BrainNav.Ground(x, z)
        BrainTrees.Init()
        If terrainShader.Ready Then LogThis("brain: shaders ready")
    End Sub

    ''' <summary>
    ''' Every chunk in ONE call.
    '''
    ''' MultiDrawElementsIndirect, exactly as nuTerra issues it: the builder
    ''' already wrote one command per chunk with that chunk's baseVertex and
    ''' its index as baseInstance, and the vertex shader reads
    ''' gl_BaseInstanceARB to pick its own matrix out of the SSBO. So a 196
    ''' chunk map is one draw call, not 196.
    '''
    ''' The indices are UNSIGNED SHORT. build_Terrain_VAO sizes that buffer at
    ''' six bytes per element because an element there is a triangle - three
    ''' shorts - and reading it as int would draw a quarter of the map from
    ''' the wrong end of the buffer.
    ''' </summary>
    Public Sub DrawTerrain(aspect As Single)
        If terrainShader Is Nothing OrElse Not terrainShader.Ready Then Return
        If map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return
        If map_scene.terrain.all_chunks_vao Is Nothing Then Return
        If theMap.chunks Is Nothing OrElse theMap.chunks.Length = 0 Then Return

        ' FRONT FACE DEFINED, NOT INHERITED. This app never set it, so it ran
        ' on GL's implicit default and any pass that changed it - ImGui, the
        ' node editor's own context - left the next frame's state a guess.
        ' CW, AND THE TERRAIN DATA SAYS SO. ChunkFunctions.vb:169-178 builds
        ' each quad as BL,TR,TL and BL,BR,TR - its own labels - so j rising
        ' goes DOWN the grid, dz is negative, and (A-C)x(B-C) comes out -Y.
        ' Seen from above that is clockwise. nuTerra defaults to Ccw at
        ' modRender.vb:30 but never culls these passes, so its default was
        ' never evidence about this geometry.
        '
        ' ON ITS OWN THIS CHANGES NOTHING ON SCREEN: nothing in this app is
        ' culled, and the shaders do not read gl_FrontFacing. It makes the
        ' state defined so that turning culling on is one variable and not two.
        GL.FrontFace(FrontFaceDirection.Cw)

        ' DEPTH STATE IS SET HERE, EVERY FRAME, NOT INHERITED.
        '
        ' It was enabled once in OnLoad and never again. BrainCanvas disables
        ' DepthTest at five places and restores it at one - and the scope and
        ' the tank card both draw through BrainCanvas, AFTER this pass, every
        ' frame. So the disable leaked into the next frame and from frame two
        ' onward the whole scene drew with no depth test at all: terrain,
        ' buildings, trees, hulls, rays and cubes in painter's order, last one
        ' drawn winning regardless of distance.
        '
        ' That is also why the winding looked wrong. With no depth test you
        ' see the back faces of anything that should have been occluded.
        '
        ' A pass that needs the state it runs under must set it. Inheriting it
        ' across a frame boundary from a UI pass is not a state machine, it is
        ' a race with one participant.
        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.DepthMask(True)

        Dim vp = Cam.ViewProj(aspect)
        LastVP = vp
        issue_terrain(vp)

        ' Buildings after the terrain: both write depth and neither blends, so
        ' the order is free - but ground first means a hill already occludes
        ' what is behind it before a single wall is issued.
        BrainModels.Draw(vp)

        ' Hulls last. They are small, they sit on ground already drawn, and
        ' anything that goes wrong with them is easiest to see against a world
        ' that is known to be right.
        ' Trees before the rings: both write depth, and a ring drawn after is
        ' one less thing hidden behind a canopy on a base.
        BrainTrees.Draw(vp)
        BrainRings.Draw(vp)
        BrainTrail.Draw(vp)
        BrainWalkView.Draw(vp)
        BrainTankDraw.Draw(vp)

        ' THE RADAR OVER THE HULLS, because it is about the hull and the
        ' ground together - a ray hidden behind the tank casting it is the
        ' one reading you cannot check.
        BrainRadar.Update()
        BrainRadar.Draw(vp)
        BrainStart.Draw(vp)
        BrainGoal.Draw(vp)
        BrainGoal.DrawCursor(vp)
    End Sub


    ''' <summary>The terrain, issued. Factored out because the pick pass draws
    ''' it a second time with colour writes off, and two copies of a
    ''' MultiDrawElementsIndirect set-up is two things to keep in step.</summary>
    Private Sub issue_terrain(ByRef vp As Matrix4)
        terrainShader.Use()
        terrainShader.SetMat4("viewProj", vp)

        ' Binding 0 is this app's own choice - nuTerra's shaders take the
        ' binding from a constant in common.h, and this app does not include
        ' that header. The number only has to agree with terrain.vert.
        map_scene.terrain.matrices.BindBase(0)

        map_scene.terrain.all_chunks_vao.Bind()
        map_scene.terrain.indirect_buffer.Bind(BufferTarget.DrawIndirectBuffer)
        GL.MultiDrawElementsIndirect(PrimitiveType.Triangles,
                                     DrawElementsType.UnsignedShort,
                                     IntPtr.Zero, theMap.chunks.Length, 0)
        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, 0)
    End Sub

    ''' <summary>
    ''' THE PICK PASS: terrain into DEPTH ONLY, then the marker solid in its
    ''' own flat colour, and read back one pixel and one depth.
    '''
    ''' Into the BACK BUFFER, before the visible frame is drawn. DrawWorld
    ''' clears and redraws immediately after and nothing is presented until
    ''' SwapBuffers, so none of this reaches the screen - no FBO needed for a
    ''' pass that lasts less than a frame.
    '''
    ''' THE TERRAIN IS DRAWN FOR ITS DEPTH ALONE, colour writes masked off. It
    ''' is there so the marker is occluded by ground between it and the eye -
    ''' a marker behind a hill must not be grabbable through the hill - and so
    ''' the depth read gives the ground's own Y under the cursor.
    '''
    ''' DEPTH BEATS ARITHMETIC. Intersecting a ray with a horizontal plane
    ''' needs the height to guess the plane and the plane to find the height;
    ''' two passes converge on gentle ground and land short on a cliff. The
    ''' depth buffer already holds the answer the frame was drawn with.
    ''' </summary>
    ''' <param name="withMarker">Draw the marker square into the pass.
    ''' TRUE to ask WHAT is under the cursor; FALSE while dragging, when the
    ''' question is where the GROUND is - with the square drawn it sits under
    ''' the cursor by definition and the depth read returns the square, so the
    ''' marker would follow itself and never move.</param>
    Public Function PickAt(mx As Integer, my As Integer, w As Integer, h As Integer,
                           withMarker As Boolean,
                           ByRef rgb As Vector3, ByRef world As Vector3) As Boolean
        If terrainShader Is Nothing OrElse Not terrainShader.Ready Then Return False
        If map_scene Is Nothing OrElse Not map_scene.TERRAIN_LOADED Then Return False
        If mx < 0 OrElse my < 0 OrElse mx >= w OrElse my >= h Then Return False

        Dim aspect = CSng(Math.Max(w, 1)) / CSng(Math.Max(h, 1))
        Dim vp = Cam.ViewProj(aspect)

        ' GL COUNTS Y FROM THE BOTTOM, the window from the top.
        Dim gy = h - 1 - my

        ' SCISSORED TO THREE PIXELS. One pixel is read, so every fragment
        ' outside that box is work thrown away - and the clear is charged for
        ' the whole window too. The terrain's VERTEX cost still stands; this
        ' removes the fill and the clear, which is the bulk of it at 4K.
        GL.Enable(EnableCap.ScissorTest)
        GL.Scissor(Math.Max(0, mx - 1), Math.Max(0, gy - 1), 3, 3)

        GL.Enable(EnableCap.DepthTest)
        GL.DepthFunc(DepthFunction.Less)
        GL.DepthMask(True)
        GL.Disable(EnableCap.Blend)
        GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F)
        GL.Clear(ClearBufferMask.ColorBufferBit Or ClearBufferMask.DepthBufferBit)

        GL.ColorMask(False, False, False, False)
        issue_terrain(vp)
        GL.ColorMask(True, True, True, True)

        If withMarker Then BrainStart.DrawPick(vp)

        ' ONE READ WHEN ONE ANSWER IS WANTED. Every ReadPixels stalls the
        ' pipeline until the GPU catches up, so a second one costs a second
        ' full sync. The colour is only asked for on the frame the button goes
        ' down - while dragging the question is the depth alone.
        rgb = Vector3.Zero
        If withMarker Then
            Dim px(3) As Byte
            GL.ReadPixels(mx, gy, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, px)
            rgb = New Vector3(px(0) / 255.0F, px(1) / 255.0F, px(2) / 255.0F)
        End If

        Dim dz(0) As Single
        GL.ReadPixels(mx, gy, 1, 1, PixelFormat.DepthComponent, PixelType.Float, dz)
        GL.Disable(EnableCap.ScissorTest)
        world = Vector3.Zero
        Dim got = False
        If dz(0) < 1.0F Then got = BrainPick.FromDepth(vp, mx, my, dz(0), w, h, world)
        Return got
    End Function



End Module

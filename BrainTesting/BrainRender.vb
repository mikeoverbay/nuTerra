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

    Public Sub Init()
        terrainShader = New BrainShader("terrain")
        BrainModels.Init()
        BrainTankDraw.Init()
        BrainRings.Init()
        BrainTrail.Init()
        BrainWalkView.Init()
        BrainRadar.Init()
        BrainGoal.Init()

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

        terrainShader.Use()
        Dim vp = Cam.ViewProj(aspect)
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
        BrainGoal.Draw(vp)
        BrainGoal.DrawCursor(vp)
    End Sub

End Module

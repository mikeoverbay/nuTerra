﻿Imports OpenTK.Graphics.OpenGL4

Public Class MapScene
    Implements IDisposable

    ReadOnly mapName As String
    Public sky As New MapSky
    Public terrain As New MapTerrain
    Public static_models As New MapStaticModels
    Public water As New MapWater
    Public base_rings As New MapBaseRings
    Public mini_map As New MapMinimap
    Public fog As New MapFog
    Public trees As New MapTrees
    Public cursor As New MapCursor

    Public Sub New(mapName As String)
        Me.mapName = mapName
    End Sub

    Public Sub ShadowMappingPass()
        GL_PUSH_GROUP("MapScene::ShadowMappingPass")

        ShadowMappingFBO.fbo.Bind(FramebufferTarget.Framebuffer)
        GL.Viewport(0, 0, ShadowMappingFBO.WIDTH, ShadowMappingFBO.HEIGHT)
        GL.ClearDepth(1.0)
        GL.Clear(ClearBufferMask.DepthBufferBit)
        GL.DepthFunc(DepthFunction.Less)

        GL.CullFace(CullFaceMode.Front)

        GL.Enable(EnableCap.PolygonOffsetFill)
        GL.PolygonOffset(4.0F, 4.0F)

        If MODELS_LOADED AndAlso DONT_BLOCK_MODELS Then
            static_models.shadow_mapping_pass()
        End If

        If TERRAIN_LOADED AndAlso DONT_BLOCK_TERRAIN Then
            terrain.shadow_mapping_pass()
        End If

        ' Restore reversed-z depth
        GL.ClearDepth(0.0)
        GL.DepthFunc(DepthFunction.Greater)

        GL.Disable(EnableCap.PolygonOffsetFill)
        GL.CullFace(CullFaceMode.Back)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        sky.Dispose()
        terrain.Dispose()
        static_models.Dispose()
        water.Dispose()
        base_rings.Dispose()
        mini_map.Dispose()
        fog.Dispose()
        cursor.Dispose()
    End Sub
End Class

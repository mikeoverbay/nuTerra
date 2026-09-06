Imports System.IO
Imports System.Math
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapFog
    Implements IDisposable

    ReadOnly scene As MapScene

    Public NOISE_id As GLTexture
    Public uv_location As New Vector2

    Public Sub New(scene As MapScene)
        Me.scene = scene
        NOISE_id = TextureMgr.load_png_image_from_file("noise.png", True, True)
    End Sub

    Public Sub global_fog()
        GL_PUSH_GROUP("perform_Fog_Noise_pass")

        ' ANIM_DELTA, not DELTA_TIME - see modGlobalVars. On a capture frame
        ' the two differ by about 17x.
        Dim s = 0.03F * ANIM_DELTA ' <---- How fast the fog moves

        'this is in the game data somewhere!
        Dim move_vector = New Vector2(0.3, 0.7) ' <----  Direction the fog moves

        uv_location += move_vector * s '<----  do the math;

        DeferredFogShader.Use()

        GL.Uniform1(DeferredFogShader("uv_scale"), 4.0F)
        GL.Uniform2(DeferredFogShader("move_vector"), uv_location.X, uv_location.Y)

        ' The fog's own shape. Distance and height come from gPosition in the
        ' shader; these decide how fast and how high. The floor is relative to
        ' the map's mean height so one number reads the same on every map.
        GL.Uniform1(DeferredFogShader("fog_density"), FOG_DENSITY)
        GL.Uniform1(DeferredFogShader("fog_height"), FOG_HEIGHT)
        GL.Uniform1(DeferredFogShader("fog_floor"), CommonProperties.MEAN + FOG_FLOOR_OFFSET)
        GL.Uniform1(DeferredFogShader("fog_noise"), FOG_NOISE)
        GL.Uniform1(DeferredFogShader("fog_sky"), FOG_SKY)
        ' Tint: the map's own colour unless every override component is set.
        If FOG_TINT_R >= 0.0F AndAlso FOG_TINT_G >= 0.0F AndAlso FOG_TINT_B >= 0.0F Then
            GL.Uniform3(DeferredFogShader("fog_tint_ovr"), FOG_TINT_R, FOG_TINT_G, FOG_TINT_B)
        Else
            GL.Uniform3(DeferredFogShader("fog_tint_ovr"),
                        CommonProperties.fog_tint.X, CommonProperties.fog_tint.Y, CommonProperties.fog_tint.Z)
        End If

        NOISE_id.BindUnit(0)
        MainFBO.gDepth.BindUnit(1)
        MainFBO.gPosition.BindUnit(2)
        MainFBO.gColor.BindUnit(3)
        ' FX coverage, so smoke over the sky is not fogged out of existence.
        ' Same gate as the FX block in modRender: when it did not run this
        ' frame the buffer is stale and the shader must ignore it.
        MainFBO.gFX_HDR.BindUnit(4)
        GL.Uniform1(DeferredFogShader("fx_cover"),
                    If(map_scene.MODELS_LOADED AndAlso DONT_BLOCK_FX, 1.0F, 0.0F))
        'FBOm.gColor_2.BindUnit(4)

        ' A full-screen quad, the same way FXAA draws. The map-sized box this
        ' used to draw only fogged the pixels it covered on screen, so the
        ' outland beyond the bounds and the sky above the box could go
        ' unfogged depending on where the camera stood. Every pixel has a fog.
        GL.Disable(EnableCap.CullFace)
        GL.UniformMatrix4(DeferredFogShader("ProjectionMatrix"), False, PROJECTIONMATRIX)
        GL.Uniform4(DeferredFogShader("rect"), 0.0F, CSng(-MainFBO.height), CSng(MainFBO.width), 0.0F)
        defaultVao.Bind()
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

        DeferredFogShader.StopUse()

        ' MULTI UNBIND
        unbind_textures(5)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        NOISE_id?.Dispose()
    End Sub
End Class

Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Every GFX_model placement on the map, drawn as a camera facing quad.
'''
''' GFX models are the particle system's mesh side - fire sheets, smoke columns,
''' distortion cards. They are placed like any other static model, so their
''' transforms are already in MODEL_INDEX_LIST; the only thing that marks them
''' out is the "GFX_models" folder in their primitives path.
'''
''' This is scaffolding for the particle work: it shows where effects belong
''' before anything can parse a .vfxbin.
''' </summary>
Public Class MapGfxMarkers
    Implements IDisposable

    ReadOnly scene As MapScene

    Public Class Marker
        Public position As Vector3
        Public name As String
    End Class

    Public markers As New List(Of Marker)

    ''' <summary>Half extent of a marker quad, world units.</summary>
    Public Shared size As Single = 1.5F

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    ''' <summary>
    ''' Walks the model placements and keeps the ones whose geometry lives under
    ''' particles/.../GFX_models/. The path is on the render set - the
    ''' base_model_holder_.primitive_name field next to it is never assigned.
    ''' </summary>
    Public Sub Collect()
        markers.Clear()

        If MODEL_INDEX_LIST Is Nothing OrElse MAP_MODELS Is Nothing Then
            Return
        End If

        ' one lookup per distinct model rather than per placement
        Dim is_gfx(MAP_MODELS.Length - 1) As Boolean
        Dim gfx_name(MAP_MODELS.Length - 1) As String

        For i = 0 To MAP_MODELS.Length - 1
            Dim lods = MAP_MODELS(i).modelLods
            If lods Is Nothing OrElse lods.Length = 0 Then Continue For
            Dim sets = lods(0).render_sets
            If sets Is Nothing OrElse sets.Count = 0 Then Continue For

            Dim path = sets(0).verts_name
            If String.IsNullOrEmpty(path) Then Continue For

            If path.IndexOf("GFX_models", StringComparison.OrdinalIgnoreCase) >= 0 Then
                is_gfx(i) = True
                gfx_name(i) = IO.Path.GetFileNameWithoutExtension(path.Replace("/"c, "\"c))
            End If
        Next

        For Each entry In MODEL_INDEX_LIST
            If entry.model_index < 0 OrElse entry.model_index >= is_gfx.Length Then Continue For
            If Not is_gfx(entry.model_index) Then Continue For

            markers.Add(New Marker With {
                .position = entry.matrix.Row3.Xyz,
                .name = gfx_name(entry.model_index)
            })
        Next

        LogThis("GFX markers: {0} placements from {1} distinct models",
                markers.Count, is_gfx.Count(Function(b) b))
    End Sub

    Public Sub draw()
        If markers.Count = 0 Then Return

        GL_PUSH_GROUP("draw_gfx_markers")

        MainFBO.attach_C()

        GL.Enable(EnableCap.Blend)
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha)
        GL.Disable(EnableCap.CullFace)
        GL.DepthMask(False)

        gfxMarkerShader.Use()
        GL.Uniform1(gfxMarkerShader("size"), size)
        GL.Uniform3(gfxMarkerShader("color"), 1.0F, 0.45F, 0.1F)

        defaultVao.Bind()
        For Each m In markers
            GL.Uniform3(gfxMarkerShader("center"), m.position.X, m.position.Y, m.position.Z)
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)
        Next

        gfxMarkerShader.StopUse()

        GL.DepthMask(True)
        GL.Enable(EnableCap.CullFace)
        GL.Disable(EnableCap.Blend)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        markers.Clear()
    End Sub
End Class

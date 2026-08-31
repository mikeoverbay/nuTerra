Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Structure DecalGLInfo
    Dim matrix As Matrix4
    Dim color_tex As GLTexture
    Dim normal_tex As GLTexture
    Dim gSurfaceNormal As GLTexture
    Dim offset As Vector2
    Dim scale As Vector2
    Dim influence As UInt32
    Dim visibility As UInt32
    Dim v1 As UInt32
    Dim v2 As UInt32
    Dim winding As UInt32
    Dim wet As UInt32
    ''' <summary>
    ''' Authored draw order, from the WGSD decal record - see
    ''' modSpaceBinFunctions.vb:181. all_decals is sorted by this at load, and
    ''' draw_decals composites in list order, so this is the real draw order.
    ''' </summary>
    Dim priority As UInt32
    ''' <summary>
    ''' Position in the WGSD file, kept only to break ties in that sort.
    ''' List(Of T).Sort is introsort and therefore unstable; without a tie-break
    ''' the equal-priority decals - which is most of them - would land in a
    ''' different order on every load and the frame would stop being
    ''' reproducible.
    ''' </summary>
    Dim load_index As Int32
End Structure


Public Class MapDecals
    Implements IDisposable

    ReadOnly scene As MapScene

    Public all_decals As List(Of DecalGLInfo)

    Public Sub New(scene As MapScene)
        Me.scene = scene
    End Sub

    Public Sub draw_decals()
        GL_PUSH_GROUP("draw_decals")

        CUBE_VAO.Bind()

        MainFBO.attach_CN()

        MainFBO.gDepth.BindUnit(0)
        MainFBO.gGMF.BindUnit(1)
        MainFBO.gGMF.BindUnit(6)

        MainFBO.gSurfaceNormal.BindUnit(4)

        GL.Disable(EnableCap.CullFace)

        GL.Enable(EnableCap.Blend)
        GL.DepthMask(False) ' stops decals from Z fighting

        'We do not want to write in to the alpha of color.
        'It screws up decal normal mappping because alpha in gColor is wetness.
        GL.ColorMask(True, True, True, False)

        boxDecalsColorShader.Use()
        ''-- scale up y some so terrain doesn't clip it.
        Dim mat = Matrix4.Identity
        mat.M22 = 1.0

        ' gSurfaceNormal is VIEW space in every writer, so the decal's projection
        ' axis has to be rotated into view space before it can be dotted against
        ' it. The view matrix is a LookAt and carries no scale, so the plain
        ' upper-left 3x3 is the right rotation for a direction. Loop-invariant.
        Dim view3 As Matrix3 = New Matrix3(map_scene.camera.PerViewData.view)

        For Each decal In all_decals
            Dim m As Matrix4 = mat * decal.matrix
            GL.UniformMatrix4(boxDecalsColorShader("mvp"), False, m * map_scene.camera.PerViewData.viewProj)

            ' Row2 is the world-space image of decal-local +Z under OpenTK's
            ' row-vector convention - the projection axis. Taken from the same
            ' matrix that feeds mvp, so it already carries build_decals' DirectX
            ' to OpenGL element flips; local (0,0,1) is invariant under that
            ' sign change, so do NOT compensate for it again here.
            '
            ' It must be normalized - decal boxes are non-uniformly scaled and
            ' the raw row length varies by orders of magnitude. A degenerate row
            ' uploads zero, which the shader reads as "skip the gate".
            Dim axis As Vector3 = Vector3.TransformRow(m.Row2.Xyz, view3)
            If axis.LengthSquared > 0.000000000001F Then
                axis.Normalize()
            Else
                axis = Vector3.Zero
            End If
            GL.Uniform3(boxDecalsColorShader("decal_axis"), axis.X, axis.Y, axis.Z)

            ' The decal's UV TANGENT - the direction tuv.s increases along -
            ' uploaded for the same reason decal_axis is: so the fragment stage
            ' never has to derive a frame from screen-space derivatives. It used
            ' to, from a UV reconstructed out of the depth buffer, and the
            ' Jacobian it divided by collapses at grazing angles - which painted
            ' a 1-pixel checkerboard across the ground. See get_tbn.
            '
            ' Row0 is local +X, the same convention Row2 is local +Z above. The
            ' shader builds tuv as -(local.xy + 0.5) * scale + offset, so s runs
            ' along local -X, and flips once more when uv_wrapping.X is negative.
            Dim tan_sign As Single = If(decal.scale.X < 0.0F, 1.0F, -1.0F)
            Dim tangent As Vector3 = Vector3.TransformRow(m.Row0.Xyz * tan_sign, view3)
            If tangent.LengthSquared > 0.000000000001F Then
                tangent.Normalize()
            Else
                tangent = Vector3.Zero
            End If
            GL.Uniform3(boxDecalsColorShader("decal_tangent"), tangent.X, tangent.Y, tangent.Z)

            'because the fucking winding order is wrong on some decals, we have to switch based on determinate 
            GL.FrontFace(decal.winding)

            decal.color_tex.BindUnit(3)
            decal.normal_tex.BindUnit(2)

            GL.Uniform2(boxDecalsColorShader("offset"), decal.offset.X, decal.offset.Y)
            GL.Uniform2(boxDecalsColorShader("scale"), decal.scale.X, decal.scale.Y)

            GL.Uniform1(boxDecalsColorShader("influence"), decal.influence)

            ' Every decal fades. DecalEdgeProbe used to decide this per texture
            ' by measuring how close its content ran to the border, but the call
            ' was not worth the machinery - fading everything reads better and
            ' the textures it declined to fade lose nothing by it.
            GL.Uniform1(boxDecalsColorShader("edge_fade"), If(DECAL_EDGE_FADE, 1UI, 0UI))

            GL.Uniform1(boxDecalsColorShader("v1"), decal.v1)
            GL.Uniform1(boxDecalsColorShader("v2"), decal.v2)
            GL.Uniform1(boxDecalsColorShader("vis"), decal.visibility)

            GL.Uniform1(boxDecalsColorShader("wet"), decal.wet)

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 14)
        Next

        boxDecalsColorShader.StopUse()

        GL.Disable(EnableCap.Blend)
        GL.DepthMask(True)
        GL.ColorMask(True, True, True, True)

        ' UNBIND
        unbind_textures(5)

        GL_POP_GROUP()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        all_decals = Nothing
    End Sub
End Class

Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapCamera
    Implements IDisposable

    ReadOnly scene As MapScene

    Public CAM_POSITION As Vector3
    Public CAM_TARGET As Vector3

    ''' <summary>Bank, radians, positive banks right. Set by flight playback
    ''' and zero the rest of the time - the orbit rig has no roll of its own
    ''' and nothing else should be tilting the horizon.</summary>
    Public CAM_ROLL As Single

    ' camara start up position
    Public VIEW_RADIUS As Single = -500.0F
    Public CAM_X_ANGLE As Single = PI / 4.0F
    Public CAM_Y_ANGLE As Single = -PI / 4.0F

    Public LOOK_AT_X As Single
    Public LOOK_AT_Y As Single
    Public LOOK_AT_Z As Single

    Public U_VIEW_RADIUS As Single
    Public U_CAM_X_ANGLE As Single
    Public U_CAM_Y_ANGLE As Single

    Public U_LOOK_AT_X As Single
    Public U_LOOK_AT_Y As Single
    Public U_LOOK_AT_Z As Single
    Public MAX_ZOOM_OUT As Single = -2000.0F 'must be negitive

    <StructLayout(LayoutKind.Sequential)>
    Public Structure TPerViewData
        Public view As Matrix4
        Public projection As Matrix4
        Public viewProj As Matrix4
        Public invViewProj As Matrix4
        Public invView As Matrix4
        Public cameraPos As Vector3
        Public pad1 As UInt32
        Public resolution As Vector2
    End Structure
    Public PerViewData As New TPerViewData
    Public PerViewDataBuffer As GLBuffer

    Public Sub New(scene As MapScene)
        Me.scene = scene

        PerViewDataBuffer = GLBuffer.Create(BufferTarget.UniformBuffer, "MapCamera::PerViewDataBuffer")
        PerViewDataBuffer.StorageNullData(
            Marshal.SizeOf(PerViewData),
            BufferStorageFlags.DynamicStorageBit)
        PerViewDataBuffer.BindBase(1)
    End Sub

    Public Sub check_postion_for_update()
        Dim halfPI = PI * 0.5F
        If LOOK_AT_X <> U_LOOK_AT_X Then
            U_LOOK_AT_X = LOOK_AT_X
        End If
        If LOOK_AT_Y <> U_LOOK_AT_Y Then
            U_LOOK_AT_Y = LOOK_AT_Y
        End If
        If LOOK_AT_Z <> U_LOOK_AT_Z Then
            U_LOOK_AT_Z = LOOK_AT_Z
        End If
        If CAM_X_ANGLE <> U_CAM_X_ANGLE Then
            U_CAM_X_ANGLE = CAM_X_ANGLE
        End If
        If CAM_Y_ANGLE <> U_CAM_Y_ANGLE Then
            If CAM_Y_ANGLE > 1.3 Then
                U_CAM_Y_ANGLE = 1.3
                CAM_Y_ANGLE = U_CAM_Y_ANGLE
            End If
            If CAM_Y_ANGLE < -halfPI Then
                U_CAM_Y_ANGLE = -halfPI + 0.001
                CAM_Y_ANGLE = U_CAM_Y_ANGLE
            End If
            U_CAM_Y_ANGLE = CAM_Y_ANGLE
        End If
        If VIEW_RADIUS <> U_VIEW_RADIUS Then
            U_VIEW_RADIUS = VIEW_RADIUS
        End If

        CURSOR_Y = get_Y_at_XZ(U_LOOK_AT_X, U_LOOK_AT_Z)

    End Sub

    Public REVERSE As New Matrix4(
        New Vector4(1, 0, 0, 0),
        New Vector4(0, 1, 0, 0),
        New Vector4(0, 0, -1, 0),
        New Vector4(0, 0, 1, 1)
    )

    Public Sub set_prespective_view()
        Dim W = MainFBO.width
        Dim H = MainFBO.height

        PROJECTIONMATRIX = Matrix4.CreateOrthographicOffCenter(0.0F, W, -H, 0.0F, -300.0F, 300.0F)
        Dim sin_x, cos_x, cos_y, sin_y As Single
        Dim cam_x, cam_y, cam_z As Single

        sin_x = Math.Sin(U_CAM_X_ANGLE)
        cos_x = Math.Cos(U_CAM_X_ANGLE)
        cos_y = Math.Cos(U_CAM_Y_ANGLE)
        sin_y = Math.Sin(U_CAM_Y_ANGLE)
        cam_y = sin_y * VIEW_RADIUS
        cam_x = cos_y * sin_x * VIEW_RADIUS
        cam_z = cos_y * cos_x * VIEW_RADIUS

        Dim LOOK_Y = CURSOR_Y + U_LOOK_AT_Y
        CAM_POSITION.X = cam_x + U_LOOK_AT_X
        CAM_POSITION.Y = cam_y + LOOK_Y
        CAM_POSITION.Z = cam_z + U_LOOK_AT_Z

        ' Keep the eye above the ground.
        '
        ' 2.5 m - a tall person - hard wired rather than exposed, because it is
        ' a physical constant of standing on the map, not a look to be tuned.
        '
        ' The camera already sampled the terrain at the PIVOT (CURSOR_Y above);
        ' it just never did it for the eye, so orbiting low or pushing the
        ' radius in put the viewpoint underground and the frame filled with the
        ' terrain backface. get_Y_at_XZ_fast is the no-scan lookup, cheap enough
        ' to call per frame.
        '
        ' The TARGET is deliberately left alone. Lifting only the eye tilts the
        ' view slightly as it slides up the terrain, which reads as the camera
        ' riding the ground - moving the pivot instead would swing the whole
        ' framing and feel like the map moved.
        If MAP_LOADED Then
            Const EYE_CLEARANCE As Single = 2.5F
            Dim ground = get_Y_at_XZ_fast(CAM_POSITION.X, CAM_POSITION.Z) + EYE_CLEARANCE
            If CAM_POSITION.Y < ground Then CAM_POSITION.Y = ground
        End If

        CAM_TARGET = New Vector3(U_LOOK_AT_X, LOOK_Y, U_LOOK_AT_Z)

        ' Flight playback replaces the orbit rig outright rather than driving it.
        '
        ' Driving it looked tidier - set LOOK_AT and the two angles so the eye
        ' lands on the path point - but the pivot's height is CURSOR_Y plus
        ' U_LOOK_AT_Y, and CURSOR_Y is the terrain under the MOUSE. The flight
        ' would have been quietly offset by wherever the pointer happened to be.
        '
        ' The ground clamp above is skipped with it on purpose: the path is
        ' already 5 m above terrain by construction and re-clamping here would
        ' fight its descent into a dip using a different height function from
        ' the one that planned it.
        '
        ' Roll is read and NOT applied. The view matrix below is a LookAt with a
        ' fixed world up, which has no roll axis - banking needs that replaced
        ' with a full basis. The data is there; the rig is not, yet.
        CAM_ROLL = 0.0F
        If FLY_CAM_PATH AndAlso scene.cam_path IsNot Nothing AndAlso scene.cam_path.loaded Then
            Dim fpos As Vector3
            Dim fh, ft, fr As Single
            If scene.cam_path.Sample(DELTA_TIME, fpos, fh, ft, fr) Then
                Dim look As New Vector3(CSng(Math.Cos(ft) * Math.Sin(fh)),
                                        CSng(Math.Sin(ft)),
                                        CSng(Math.Cos(ft) * Math.Cos(fh)))
                CAM_POSITION = fpos
                CAM_TARGET = fpos + look * 50.0F
                CAM_ROLL = fr * CAM_ROLL_SCALE
            End If
        End If

        PerViewData.projection = Matrix4.CreatePerspectiveFieldOfView(
                                   FieldOfView,
                                   W / H,
                                   My.Settings.near, My.Settings.far) * REVERSE
        PerViewData.cameraPos = CAM_POSITION
        ' Roll about the VIEW axis, by rotating the up vector around forward
        ' rather than by post-multiplying a Z rotation onto the finished view.
        '
        ' Same result, but this way the roll is part of the basis LookAt builds,
        ' so everything downstream that reads PerViewData.view - the deferred
        ' resolve, SSR, the sky, billboards - banks with it and stays consistent.
        ' A rotation bolted on afterwards would tilt the image while leaving the
        ' reconstructed view rays pointing the old way.
        '
        ' Sign is MEASURED, not derived. Forcing +0.35 rad and looking at the
        ' frame put the horizon higher on the right, which is a right bank - so
        ' positive rolls right, matching what the .campath format documents. The
        ' chain of conventions through OpenTK's LookAt basis is not worth
        ' trusting; one screenshot settles it.
        Dim up = Vector3.UnitY
        If Math.Abs(CAM_ROLL) > 0.0001F Then
            Dim fwd = CAM_TARGET - CAM_POSITION
            If fwd.LengthSquared > 1.0E-8F Then
                fwd = Vector3.Normalize(fwd)
                up = Vector3.Normalize(Vector3.Transform(
                        up, Quaternion.FromAxisAngle(fwd, CAM_ROLL)))
            End If
        End If
        PerViewData.view = Matrix4.LookAt(CAM_POSITION, CAM_TARGET, up)
        PerViewData.viewProj = PerViewData.view * PerViewData.projection
        PerViewData.invViewProj = PerViewData.viewProj.Inverted()
        PerViewData.invView = PerViewData.view.Inverted()

        PerViewData.resolution.X = W
        PerViewData.resolution.Y = H
        GL.NamedBufferSubData(PerViewDataBuffer.buffer_id, IntPtr.Zero, Marshal.SizeOf(PerViewData), PerViewData)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        PerViewDataBuffer?.Dispose()
    End Sub
End Class

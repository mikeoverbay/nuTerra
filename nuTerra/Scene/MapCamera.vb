Imports System.Runtime.InteropServices
Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

Public Class MapCamera
    Implements IDisposable

    ReadOnly scene As MapScene

    Public CAM_POSITION As Vector3
    Public CAM_TARGET As Vector3

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

        CAM_TARGET = New Vector3(U_LOOK_AT_X, LOOK_Y, U_LOOK_AT_Z)

        PerViewData.projection = Matrix4.CreatePerspectiveFieldOfView(
                                   FieldOfView,
                                   W / H,
                                   My.Settings.near, My.Settings.far) * REVERSE
        PerViewData.cameraPos = CAM_POSITION
        PerViewData.view = Matrix4.LookAt(CAM_POSITION, CAM_TARGET, Vector3.UnitY)
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

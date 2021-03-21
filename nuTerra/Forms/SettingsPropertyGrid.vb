Imports System.ComponentModel

Public Class SettingsPropertyGrid
    Const MIN_FOV = 1
    Const MAX_FOV = 179.0

    Const MIN_NEAR = 0.0
    Const MAX_NEAR = 1000.0

    Const MIN_FAR = 0.0
    Const MAX_FAR = 100000.0

    Const MIN_SPEED = 0.0
    Const MAX_SPEED = 10000.0

    Public Sub New()
        PerViewData._start = 75
        PerViewData._end = 200
    End Sub

    <DisplayName("FoV"), Category("Camera")>
    Public Property Camera_FoV As Decimal
        Set(value As Decimal)
            If MIN_FOV <= value And value <= MAX_FOV Then
                My.Settings.fov = value
                FieldOfView = CSng(Math.PI) * (value / 180.0F)
            End If
        End Set
        Get
            Return My.Settings.fov
        End Get
    End Property

    <DisplayName("Near"), Category("Camera")>
    Public Property Camera_Near As Decimal
        Set(value As Decimal)
            If MIN_NEAR <= value And value <= MAX_NEAR Then
                My.Settings.near = value
                PRESPECTIVE_NEAR = value
            End If
        End Set
        Get
            Return My.Settings.near
        End Get
    End Property

    <DisplayName("Far"), Category("Camera")>
    Public Property Camera_Far As Decimal
        Set(value As Decimal)
            If MIN_FAR <= value And value <= MAX_FAR Then
                My.Settings.far = value
                PRESPECTIVE_FAR = value
            End If
        End Set
        Get
            Return My.Settings.far
        End Get
    End Property

    <DisplayName("Speed"), Category("Camera")>
    Public Property Camera_Speed As Decimal
        Set(value As Decimal)
            If MIN_SPEED <= value And value <= MAX_SPEED Then
                My.Settings.speed = value
            End If
        End Set
        Get
            Return My.Settings.speed
        End Get
    End Property

    <DisplayName("Start"), Category("Terrain")>
    Public Property Terrain_Start As Decimal
        Set(value As Decimal)
            PerViewData._start = value
        End Set
        Get
            Return PerViewData._start
        End Get
    End Property

    <DisplayName("End"), Category("Terrain")>
    Public Property Terrain_End As Decimal
        Set(value As Decimal)
            PerViewData._end = value
        End Set
        Get
            Return PerViewData._end
        End Get
    End Property

    <DisplayName("Map Icon Scale"), Category("User Interface")>
    Public Property UI_map_icon_scale As Single
        Set(value As Single)
            If value > 0.0 Then
                My.Settings.UI_map_icon_scale = value
            End If
        End Set
        Get
            Return My.Settings.UI_map_icon_scale
        End Get
    End Property

End Class

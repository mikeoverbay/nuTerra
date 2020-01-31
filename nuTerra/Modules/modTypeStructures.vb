
Module modTypeStructures
    Public Structure vect4
        Public x As Single
        Public y As Single
        Public z As Single
        Public w As Single
    End Structure
    Public Structure vect3
        Public x As Single
        Public y As Single
        Public z As Single
    End Structure
    Public Structure vect2
        Public x As Single
        Public y As Single
    End Structure
    '--------------------------------------------------------
    Public water As New water_model_
    Public Structure water_model_
        Public displayID_cube As Integer
        Public displayID_plane As Integer
        Public textureID As Integer
        Public normalID As Integer
        Public aspect As Single
        Public size_ As vect3
        Public position As vect3
        Public orientation As Single
        Public type As String
        Public IsWater As Boolean
        Public foam_id As Integer
        Public lbl As vect3
        Public lbr As vect3
        Public ltl As vect3
        Public ltr As vect3
        Public rbl As vect3
        Public rbr As vect3
        Public rtl As vect3
        Public rtr As vect3
        Public BB() As vect3
        Public matrix() As Single
    End Structure
    '--------------------------------------------------------
    Public Structure matrix_
        Public matrix() As Single
    End Structure
    '--------------------------------------------------------
    Public Structure vertex_data
        Public x As Single
        Public y As Single
        Public z As Single
        Public u As Single
        Public v As Single
        Public nx As Single
        Public ny As Single
        Public nz As Single
        Public map As Integer
        Public t As vect3
        Public bt As vect3
        Public hole As Single
    End Structure

    Public triangle_holder As New mappedFile_

End Module

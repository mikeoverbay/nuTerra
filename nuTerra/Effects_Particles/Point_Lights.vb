Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports OpenTK.Graphics
Imports System.Math
Imports System.Runtime.InteropServices

Module Point_Lights
    'just a test. we can use these for different types of lights
    Public LIGHTS As Light_group_
    Public ll As New point_light_
    Public Structure Light_group_
        Public UBO_id As Integer
        Public lights As Dictionary(Of Integer, point_light_)
        Public light_count As Integer
        Public gl_light_array() As point_light_

        Public Sub create_ubo_Buffer()
            UBO_id = GL.GenBuffer

            GL.BindBuffer(BufferTarget.UniformBuffer, UBO_id)
            GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, Marshal.SizeOf(ll) - light_count, gl_light_array)
            GL.BindBuffer(BufferTarget.UniformBuffer, 0)


        End Sub

        Public Function add_light(ByRef light As point_light_) As Integer
            lights.Add(light_count, light)
            light_count += 1
            update_lights()
            Return light_count
        End Function

        Public Sub remove_light_at(ByVal index As Integer)
            If lights.ContainsKey(index) Then
                lights.Remove(index)
                light_count -= 1
                update_lights()
            End If
        End Sub

        Public Sub clear()
            lights.Clear()
            light_count = 0
            Erase gl_light_array
        End Sub

        Public Sub update_lights()
            ReDim gl_light_array(light_count)
            For i = 0 To lights.Count - 1
                gl_light_array(i) = New point_light_
                gl_light_array(i) = lights.Item(i)
            Next
        End Sub
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure point_light_
        Public location As Vector3
        Public color As Vector3
        Public level As Single
        Public fallOff As Single
    End Structure



End Module

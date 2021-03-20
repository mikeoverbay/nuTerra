Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module Point_Lights
    'just a test. we can use these for different types of lights
    Public LIGHTS As Light_group_

    Public Structure Light_group_

        Public light_SSBO As GLBuffer
        Public gl_light_array() As point_light_
        Public index As Integer
        Public Const max_light_count As Integer = 250

        Public Sub init()
            ReDim gl_light_array(max_light_count)
            index = 0
        End Sub

        Public Sub create_SSBO_Buffer()
            If light_SSBO Is Nothing Then
                light_SSBO = CreateBuffer(BufferTarget.ShaderStorageBuffer, "Lights")
                BufferStorage(light_SSBO, gl_light_array.Length * Marshal.SizeOf(Of point_light_), gl_light_array, BufferStorageFlags.DynamicStorageBit)
                light_SSBO.BindBase(7)
            Else
                GL.NamedBufferSubData(light_SSBO.buffer_id, IntPtr.Zero, gl_light_array.Length * Marshal.SizeOf(Of point_light_), gl_light_array)
            End If
        End Sub

        Public Function add_light(ByRef light As point_light_) As Integer
            If index = max_light_count Then
                Throw New Exception("Ran out of light slots!")
                Return index
            End If
            gl_light_array(index) = light
            index += 1
            Return index - 1 ' return id for this light.. for what, I have no idea

        End Function

    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure point_light_
        Public location As Vector3
        Public level As Single
        Public color As Vector3
        Public fallOff As Single
        Public inUse As UInt32
        Private pad0 As Integer
        Private pad1 As Integer
        Private pad2 As Integer
    End Structure

End Module

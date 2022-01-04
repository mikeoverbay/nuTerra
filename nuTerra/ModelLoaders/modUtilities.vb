Imports System.IO
Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Module modUtilities
    Public CUBE_VAO As GLVertexArray

    Public Sub LogThis(entry As String, ParamArray args() As Object)
#If DEBUG Then
        Debug.Print(entry, args)
#End If
        Console.WriteLine(entry, args)
    End Sub

    Public Sub make_cube()
        Dim verts() As Single = {
             0.5, 0.5, 0.5,
            -0.5, 0.5, 0.5,
            0.5, -0.5, 0.5,
            -0.5, -0.5, 0.5,
            -0.5, -0.5, -0.5,
            -0.5, 0.5, 0.5,
            -0.5, 0.5, -0.5,
            0.5, 0.5, 0.5,
            0.5, 0.5, -0.5,
            0.5, -0.5, 0.5,
            0.5, -0.5, -0.5,
            -0.5, -0.5, -0.5,
            0.5, 0.5, -0.5,
            -0.5, 0.5, -0.5
        }

        Dim vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "CUBE")
        vbo.Storage(verts.Length * 4, verts, BufferStorageFlags.None)

        CUBE_VAO = GLVertexArray.Create("CUBE")
        CUBE_VAO.VertexBuffer(0, vbo, IntPtr.Zero, 12)
        CUBE_VAO.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        CUBE_VAO.AttribBinding(0, 0)
        CUBE_VAO.EnableAttrib(0)
    End Sub
End Module

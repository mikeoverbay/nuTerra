Imports System.IO
Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Module modUtilities
    Public CUBE_VAO As GLVertexArray

    Public Sub LogThis(entry As String, ParamArray args() As Object)
#If DEBUG Then
        Debug.Print(entry, args)
#End If

        'Writes to the log and immediately saves it.
        nuTerra_LOG.AppendLine(String.Format(entry, args))
        'File.WriteAllText(Path.Combine(TEMP_STORAGE, "nuTerra_Log.txt"), nuTerra_LOG.ToString)
    End Sub

    ' Allows us to split by strings. Not just characters.
    <Extension()>
    Public Function Split(ByVal input As String,
                          ParamArray delimiter As String()) As String()
        Return input.Split(delimiter, StringSplitOptions.None)
        Dim a(0) As String
        a(0) = input
        Return a
    End Function

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

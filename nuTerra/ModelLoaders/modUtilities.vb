Imports System.IO
Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Module modUtilities
    Public CUBE_VAO As Integer

    Public Sub LogThis(entry As String)
#If DEBUG Then
        Debug.Print(entry)
#End If

        'Writes to the log and immediately saves it.
        nuTerra_LOG.AppendLine(entry)
        File.WriteAllText(Path.Combine(TEMP_STORAGE, "nuTerra_Log.txt"), nuTerra_LOG.ToString)
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

        Dim vbo As Integer
        GL.CreateVertexArrays(1, CUBE_VAO)
        GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, CUBE_VAO, -1, "CUBE")

        GL.CreateBuffers(1, vbo)
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vbo, -1, "CUBE_VBO")
        GL.NamedBufferStorage(vbo, verts.Length * 4, verts, BufferStorageFlags.None)

        GL.VertexArrayVertexBuffer(CUBE_VAO, 0, vbo, IntPtr.Zero, 12)
        GL.VertexArrayAttribFormat(CUBE_VAO, 0, 3, VertexAttribType.Float, False, 0)
        GL.VertexArrayAttribBinding(CUBE_VAO, 0, 0)
        GL.EnableVertexArrayAttrib(CUBE_VAO, 0)
    End Sub
End Module

Imports Assimp
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

Public Class XModel
    Public vao As GLVertexArray
    Public indices_count As Integer

    Private Structure _vertex
        Public v As Vector3
        Public n As Vector3
        Public uv As Vector2
    End Structure

    Public Shared Function load_from_file(file_ As String) As XModel
        Dim importer As New AssimpContext

        Dim scene = importer.ImportFile(file_)
        Dim mesh = scene.Meshes(0)

        Dim vbuff(mesh.VertexCount - 1) As _vertex
        For i = 0 To mesh.VertexCount - 1
            vbuff(i).v.X = mesh.Vertices(i).X
            vbuff(i).v.Y = mesh.Vertices(i).Y
            vbuff(i).v.Z = mesh.Vertices(i).Z
            vbuff(i).n.X = mesh.Normals(i).X
            vbuff(i).n.Y = mesh.Normals(i).Y
            vbuff(i).n.Z = mesh.Normals(i).Z
            vbuff(i).uv.X = mesh.TextureCoordinateChannels(0)(i).X
            vbuff(i).uv.Y = 1.0F - mesh.TextureCoordinateChannels(0)(i).Y
        Next

        Dim index_buffer16(mesh.FaceCount - 1) As vect3_16
        For i = 0 To mesh.FaceCount - 1
            index_buffer16(i).x = mesh.Faces(i).Indices(2)
            index_buffer16(i).y = mesh.Faces(i).Indices(1)
            index_buffer16(i).z = mesh.Faces(i).Indices(0)
        Next

        Dim result As New XModel
        result.indices_count = index_buffer16.Length

        'Create VAO id
        result.vao = GLVertexArray.Create(file_)

        Dim mBuffer = GLBuffer.Create(BufferTarget.ArrayBuffer, file_)

        Dim vbuff_offset = New IntPtr(index_buffer16.Length * 6)
        mBuffer.StorageNullData(
            index_buffer16.Length * 6 + vbuff.Length * 32,
            BufferStorageFlags.DynamicStorageBit)
        GL.NamedBufferSubData(mBuffer.buffer_id, IntPtr.Zero, index_buffer16.Length * 6, index_buffer16)
        GL.NamedBufferSubData(mBuffer.buffer_id, vbuff_offset, vbuff.Length * 32, vbuff)

        result.vao.VertexBuffer(0, mBuffer, vbuff_offset, 32)
        result.vao.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        result.vao.AttribBinding(0, 0)
        result.vao.EnableAttrib(0)

        result.vao.VertexBuffer(1, mBuffer, vbuff_offset, 32)
        result.vao.AttribFormat(1, 3, VertexAttribType.Float, False, 12)
        result.vao.AttribBinding(1, 1)
        result.vao.EnableAttrib(1)

        result.vao.VertexBuffer(2, mBuffer, vbuff_offset, 32)
        result.vao.AttribFormat(2, 2, VertexAttribType.Float, False, 24)
        result.vao.AttribBinding(2, 2)
        result.vao.EnableAttrib(2)

        result.vao.ElementBuffer(mBuffer)
        Return result
    End Function
End Class

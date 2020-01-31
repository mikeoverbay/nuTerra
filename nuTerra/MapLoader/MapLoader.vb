
Imports System.IO
Imports System.IO.Compression
Imports OpenTK.Graphics.OpenGL


Module MapLoader

    Public Sub load_map(ByVal package_name As String)
        'For now, we are going to hard wire this name
        'until a map selection interface is writen.
        MAP_NAME_NO_PATH = "19_monastery.pkg"

        'first, we clear out the previous map data



    End Sub

    Public Sub remove_map_data()
        'Used to delete all images and display lists.

        'remove map loaded textures
        Dim LAST_TEXTURE = GL.GenTexture - 1 'get last texture created.
        Dim t_count = LAST_TEXTURE - FIRST_UNUSED_TEXTURE
        GL.DeleteTextures(t_count, FIRST_UNUSED_TEXTURE)
        GL.Finish() ' make sure we are done before moving on

    End Sub


End Module

Imports System.Runtime.InteropServices
Imports OpenTK
Imports OpenTK.Graphics.OpenGL
Imports System.IO


Module MakeMega
    'these files MUST remain open while the map is loaded.
    Public megaHDL_AM As FileStream = Nothing 'albedo
    Public megaHDL_NM As FileStream = Nothing  'normal
    Public megaHDL_GMM As FileStream = Nothing  'gloss/metal


    'The basic idea of this is:
    '1. Render to the mix fbo at a high resolution. 4096 x 4096. May need to be higher.
    '2. Create mips from this texture. Max of 5 total.
    '3. Copy the texture as 128 x 128 tile blocks from the fbo texture to disc.
    '4. Update the LUT texture at the proper pixel with the index of the texture on disc.
    '5. DO this for each mip level also.
    'The LUT is a uint texture, 32 x 32 with 5 mip levels. Each pixel in the each Mip stores the location index
    'of the texture on disc.
    'The LUT we shall call megaLUT and it will reside in each render_set of theMAP structure.

    Public Sub close_megas()
        'Used when Terra is shut down
        If megaHDL_AM IsNot Nothing Then
            megaHDL_AM.Close()
        End If
        If megaHDL_NM IsNot Nothing Then
            megaHDL_NM.Close()
        End If
        If megaHDL_GMM IsNot Nothing Then
            megaHDL_GMM.Close()
        End If
    End Sub
    Public Sub prallocate_disc_space()

        'Close if they are exist already. This atomatically deletes them per options.
        close_megas()

        Dim chunk_count = theMap.render_set.Length - 1
        'each chunk will be need:
        Dim MAX_RES As Integer = 4096
        ' 4096 x 4096
        Dim L0 = MAX_RES
        Dim L1 = MAX_RES / 2
        Dim L2 = MAX_RES / 4
        Dim L3 = MAX_RES / 8
        Dim L4 = MAX_RES / 16
        ' Calculate disc space requirement 
        Dim total_space As Long = ((L0 + 3) / 4) * ((L0 + 3) / 4) * 16 * chunk_count
        total_space += ((L1 + 3) / 4) * ((L1 + 3) / 4) * 16 * chunk_count
        total_space += ((L2 + 3) / 4) * ((L2 + 3) / 4) * 16 * chunk_count
        total_space += ((L3 + 3) / 4) * ((L3 + 3) / 4) * 16 * chunk_count
        total_space += ((L4 + 3) / 4) * ((L4 + 3) / 4) * 16 * chunk_count

        reserver_space(megaHDL_AM, total_space, "megaAM.bin")
        reserver_space(megaHDL_NM, total_space, "megaNM.bin")
        reserver_space(megaHDL_GMM, total_space, "megaGMM.bin")
    End Sub
    Private Sub reserver_space(f As FileStream, size As Long, filename As String)

        Dim buffer_size As Integer = 256 * 256 * 16 ' this can affect speed.. it will need tweaked later

        Dim options = IO.FileOptions.WriteThrough Or FileOptions.RandomAccess Or FileOptions.DeleteOnClose

        f = System.IO.File.Create(TEMP_STORAGE + "\" + filename, buffer_size, options)
        f.Seek(size - 1, SeekOrigin.Begin)
        f.WriteByte(0)

    End Sub
End Module

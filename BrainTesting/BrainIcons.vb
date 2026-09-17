Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' SMALL PNG ICONS, UPLOADED AS GL TEXTURES FOR ImGui TO DRAW.
'''
''' "I have a stash of icons" - the owner, 2026-09-17, pointing at 2,226 16px
''' PNGs. Three of them are in icons/ - see the README there.
'''
''' THE CONTEXT MATTERS AND IS THE WHOLE REASON THIS IS NOT A ONE-LINER. A GL
''' texture belongs to the context that made it, and the node editor's window
''' has its own, unshared one. Loading an icon during the main window's frame
''' would hand back a texture id that means nothing over there, and GL would
''' not complain - it would draw nothing, or draw whatever else happens to own
''' that id. So Load is called with the node window's context current, and the
''' cache is keyed by context as well as by name for the day something else
''' wants an icon too.
'''
''' MONO BY DEFAULT, and that is a judgement worth explaining. These icons were
''' drawn for light-grey toolbars: measured against this app's title bar, the
''' minus is 72/255 mean luminance and would be a smudge, while the window
''' glyphs are 162-188 and fine. Rather than ship a set where one of the three
''' is invisible, the RGB is forced to white at load and the colour comes from
''' ImGuiCol_Text at draw time - so they match the close box ImGui draws beside
''' them, on any theme. Set MONO to False to get the artwork as drawn.
'''
''' Added 2026-09-17 by Tank AI work.
''' </summary>
Module BrainIcons

    ''' <summary>Whiten on load and tint on draw. See the note above - this is
    ''' what stops a dark icon vanishing into a dark title bar.</summary>
    Public MONO As Boolean = True

    ''' <summary>Keyed by context AND name. Two contexts asking for the same
    ''' icon are two different textures, and telling them apart by name alone
    ''' is the bug this file exists to avoid.</summary>
    Private ReadOnly cache As New Dictionary(Of String, Integer)

    <DllImport("opengl32.dll")>
    Private Function wglGetCurrentContext() As IntPtr
    End Function

    ''' <summary>
    ''' The texture id for an icon in icons/, or 0 if it is not there.
    '''
    ''' ZERO IS A REAL ANSWER, not an error to shout about. The stash is the
    ''' owner's and lives on a drive that may not be mounted; every caller has
    ''' a drawn fallback, so a missing file costs the artwork and nothing else.
    ''' </summary>
    Public Function Tex(name As String) As Integer
        Dim key = wglGetCurrentContext().ToString() & "|" & name
        Dim id As Integer = 0
        If cache.TryGetValue(key, id) Then Return id

        Try
            Dim path = IO.Path.Combine(Application.StartupPath, "icons", name & ".png")
            If Not IO.File.Exists(path) Then
                cache(key) = 0
                Return 0
            End If

            Using bmp As New Bitmap(path)
                Dim rect As New Rectangle(0, 0, bmp.Width, bmp.Height)
                Dim data = bmp.LockBits(rect, ImageLockMode.ReadOnly,
                                        System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                Dim bytes(bmp.Width * bmp.Height * 4 - 1) As Byte
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)
                bmp.UnlockBits(data)

                ' System.Drawing's 32bppArgb is B,G,R,A in memory - which is
                ' why the upload below says Bgra rather than Rgba.
                If MONO Then
                    For i = 0 To bytes.Length - 1 Step 4
                        bytes(i) = 255 : bytes(i + 1) = 255 : bytes(i + 2) = 255
                    Next
                End If

                id = GL.GenTexture()
                GL.BindTexture(TextureTarget.Texture2D, id)
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                              bmp.Width, bmp.Height, 0,
                              OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                              PixelType.UnsignedByte, bytes)
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.Linear))
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Linear))
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureWrapS, CInt(TextureWrapMode.ClampToEdge))
                GL.TexParameter(TextureTarget.Texture2D,
                                TextureParameterName.TextureWrapT, CInt(TextureWrapMode.ClampToEdge))
                GL.BindTexture(TextureTarget.Texture2D, 0)
            End Using
        Catch ex As Exception
            LogThis("brain: icon {0} - {1}", name, ex.Message)
            id = 0
        End Try

        cache(key) = id
        Return id
    End Function

    ''' <summary>Pull a set in now, while the right context is current, rather
    ''' than on the first frame that wants them.</summary>
    Public Sub Warm(ParamArray names() As String)
        Dim got = 0
        For Each n In names
            If Tex(n) <> 0 Then got += 1
        Next
        LogThis("brain: icons {0}/{1} loaded", got, names.Length)
    End Sub

End Module

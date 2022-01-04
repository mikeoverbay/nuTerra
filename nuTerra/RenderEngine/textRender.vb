Imports OpenTK.Graphics.OpenGL4

Module textRender
    Public lucid_console As New Font("Lucide Console", 14, FontStyle.Bold, GraphicsUnit.Pixel)
    Public Const ASCII_CHARACTERS = "!" & Chr(34) & "#$%&" & Chr(39) & "()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_`abcdefghijklmnopqrstuvwxyz{|}~абвгдеёжзийклмнопрстуфхцчшщъыьэюяАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ"

    Public Function build_ascii_characters() As GLTexture
        Const width = 1620
        Const height = 15

        Dim tex = GLTexture.Create(TextureTarget.Texture2D, "ASCII_CHARACTERS")
        tex.Storage2D(1, SizedInternalFormat.Rgba8, width, height)

        Dim bmp As New Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)
        Dim gfx = Graphics.FromImage(bmp)
        gfx.TextRenderingHint = Text.TextRenderingHint.AntiAlias

        Dim mono As New Font(FontFamily.GenericMonospace, 12.15, FontStyle.Bold)

        Dim brush = New SolidBrush(Color.White)
        gfx.DrawString(ASCII_CHARACTERS, mono, brush, New PointF(7, -2))

        Dim data = bmp.LockBits(New Rectangle(0, 0, width, height),
                                Imaging.ImageLockMode.ReadOnly,
                                Imaging.PixelFormat.Format32bppArgb)
        tex.SubImage2D(0, 0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0)
        bmp.UnlockBits(data)

        ' FOR DEBUG:
        ' bmp.Save("ascii_characters.bmp", Imaging.ImageFormat.Bmp)

        Return tex
    End Function


End Module

Imports OpenTK.Graphics.OpenGL

Module textRender
    Public lucid_console As New Font("Lucide Console", 14, FontStyle.Bold, GraphicsUnit.Pixel)
    Public Const ASCII_CHARACTERS = "!" & Chr(34) & "#$%&" & Chr(39) & "()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_`abcdefghijklmnopqrstuvwxyz{|}~абвгдеёжзийклмнопрстуфхцчшщъыьэюяАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ"

    Public Function build_ascii_characters() As GLTexture
        Const width = 1620
        Const height = 15

        Dim tex = CreateTexture(TextureTarget.Texture2D, "ASCII_CHARACTERS")
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

    Public Class DrawText_
        Private bmp As Bitmap
        Private gfx As System.Drawing.Graphics
        Private texture_ As GLTexture
        Private dirty_region As Rectangle

        Public Sub TextRenderer(ByVal width As Integer, ByVal height As Integer)
            If width = 0 Then
                Throw New Exception("Width = 0")
            End If
            If height = 0 Then
                Throw New Exception("Height = 0")
            End If
            If OpenTK.Graphics.GraphicsContext.CurrentContext Is Nothing Then
                Throw New Exception("No current GL context")
            End If
            Me.bmp = Nothing
            Me.gfx = Nothing
            Me.bmp = New Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            Me.gfx = System.Drawing.Graphics.FromImage(Me.bmp)
            Me.gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias
            If Me.texture_ IsNot Nothing Then Me.texture_.Delete()

            Me.texture_ = CreateTexture(TextureTarget.Texture2D, "text")

            Me.texture_.Parameter(TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            Me.texture_.Parameter(TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            Me.texture_.Storage2D(1, SizedInternalFormat.Rgba8, width, height)
        End Sub

        Public Sub clear(ByRef color As Color)
            Me.gfx.Clear(color)
            Me.dirty_region = New Rectangle(0, 0, Me.bmp.Width, Me.bmp.Height)
        End Sub
        Public Sub DrawString(ByVal text As String, ByRef font As Font, ByRef brush As Brush, ByRef point As PointF)
            Me.gfx.Clear(Color.FromArgb(125, 0, 0, 0))
            Me.gfx.DrawString(text, font, brush, point)
            Dim size = Me.gfx.MeasureString(text, font)
            Me.dirty_region = Rectangle.Round(RectangleF.Union(Me.dirty_region, New RectangleF(point, size)))
            Me.dirty_region = Rectangle.Intersect(Me.dirty_region, New Rectangle(0, 0, Me.bmp.Width, Me.bmp.Height))

        End Sub

        Public ReadOnly Property Gettexture() As GLTexture
            Get
                uploadBitmap()
                Return Me.texture_
            End Get

        End Property

        Private Sub uploadBitmap()

            If Me.dirty_region <> RectangleF.Empty Then
                Dim Data = Me.bmp.LockBits(Me.dirty_region,
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                Me.texture_.SubImage2D(0,
                        Me.dirty_region.X, Me.dirty_region.Y, Me.dirty_region.Width, Me.dirty_region.Height,
                        PixelFormat.Bgra, PixelType.UnsignedByte, Data.Scan0)
                Me.bmp.UnlockBits(Data)
                Me.dirty_region = Rectangle.Empty
            End If

        End Sub
    End Class


End Module

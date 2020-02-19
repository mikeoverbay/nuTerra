Imports OpenTK.Graphics.OpenGL

Module textRender
    'Public f_family = New FontFamily("Lucide Console")
    Public lucid_console As New Font("Lucide Console", 14, FontStyle.Bold, GraphicsUnit.Pixel)
    Public serif As New Font(FontFamily.GenericSerif, 12)
    Public sans As New Font(FontFamily.GenericSansSerif, 12)
    Public mono As New Font(FontFamily.GenericMonospace, 12)
    Public monoSmall As New Font(FontFamily.GenericMonospace, 9.5)
    Public DrawText As New DrawText_
    Public DrawMapPickText As New DrawText_

    Public Structure DrawText_
        Private bmp As Bitmap
        Private gfx As System.Drawing.Graphics
        Private texture_ As Integer
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
            If Me.texture_ > 0 Then
                GL.DeleteTexture(Me.texture_)
            End If
            Me.texture_ = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, Me.texture_)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TexImage2D(TextureTarget.Texture2D, 0, _
                          PixelInternalFormat.Rgba, width, height, 0, _
                          PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
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

        Public ReadOnly Property Gettexture() As Integer
            Get
                uploadBitmap()
                Return Me.texture_
            End Get

        End Property

        Private Sub uploadBitmap()

            If Me.dirty_region <> RectangleF.Empty Then
                Dim Data = Me.bmp.LockBits(Me.dirty_region, _
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, _
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb)

                GL.BindTexture(TextureTarget.Texture2D, Me.texture_)
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, _
                        Me.dirty_region.X, Me.dirty_region.Y, Me.dirty_region.Width, Me.dirty_region.Height, _
                        PixelFormat.Bgra, PixelType.UnsignedByte, Data.Scan0)
                Me.bmp.UnlockBits(Data)
                Me.dirty_region = Rectangle.Empty
            End If

        End Sub
    End Structure


End Module

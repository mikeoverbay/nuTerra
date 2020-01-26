#Region "imports"

Imports System.Math
Imports System
Imports System.Globalization
Imports System.Threading

Imports OpenTK.GLControl
Imports OpenTK
Imports OpenTK.Platform.Windows
Imports OpenTK.Graphics
Imports OpenTK.Graphics.OpenGL

Imports Config = OpenTK.Configuration
Imports Utilities = OpenTK.Platform.Utilities
#End Region

Module textRender

    Public serif As New Font(FontFamily.GenericSerif, 12)
    Public sans As New Font(FontFamily.GenericSansSerif, 12)
    Public mono As New Font(FontFamily.GenericMonospace, 12)

    NotInheritable Class DrawText
        Shared bmp As Bitmap
        Shared gfx As System.Drawing.Graphics
        Shared texture_ As Integer
        Shared dirty_region As Rectangle
        Shared disposed As Boolean

        Shared Sub TextRenderer(ByVal width As Integer, ByVal height As Integer)
            If width = 0 Then
                Throw New Exception("Width = 0")
            End If
            If height = 0 Then
                Throw New Exception("Height = 0")
            End If
            If OpenTK.Graphics.GraphicsContext.CurrentContext Is Nothing Then
                Throw New Exception("No current GL context")
            End If
            bmp = Nothing
            gfx = Nothing
            bmp = New Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            gfx = System.Drawing.Graphics.FromImage(bmp)
            gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel
            If texture_ > 0 Then
                GL.DeleteTexture(texture_)
            End If
            texture_ = GL.GenTexture
            GL.BindTexture(TextureTarget.Texture2D, texture_)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, TextureMinFilter.Nearest)
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, TextureMagFilter.Nearest)
            GL.TexImage2D(TextureTarget.Texture2D, 0, _
                          PixelInternalFormat.Rgba, width, height, 0, _
                          PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero)
        End Sub

        Shared Sub clear(ByRef color As Color)
            gfx.Clear(color)
            dirty_region = New Rectangle(0, 0, bmp.Width, bmp.Height)
        End Sub
        Shared Sub DrawString(ByVal text As String, ByRef font As Font, ByRef brush As Brush, ByRef point As PointF)

            gfx.DrawString(text, font, brush, point)
            Dim size = gfx.MeasureString(text, font)
            dirty_region = Rectangle.Round(RectangleF.Union(dirty_region, New RectangleF(point, size)))
            dirty_region = Rectangle.Intersect(dirty_region, New Rectangle(0, 0, bmp.Width, bmp.Height))

        End Sub

        Shared ReadOnly Property Gettexture() As Integer
            Get
                uploadBitmap()
                Return texture_
            End Get

        End Property

        Shared Sub uploadBitmap()

            If dirty_region <> RectangleF.Empty Then
                Dim Data = bmp.LockBits(dirty_region, _
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, _
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb)

                GL.BindTexture(TextureTarget.Texture2D, texture_)
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, _
                        dirty_region.X, dirty_region.Y, dirty_region.Width, dirty_region.Height, _
                        PixelFormat.Bgra, PixelType.UnsignedByte, Data.Scan0)
                bmp.UnlockBits(Data)
                dirty_region = Rectangle.Empty
            End If

        End Sub
    End Class

End Module

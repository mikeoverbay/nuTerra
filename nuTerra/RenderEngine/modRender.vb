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

Module modRender
    Public PI As Single = 3.14159274F
    Public angle As Single = 0
    Public Sub draw_scene()
        If _STOPGL Then Return

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer
        frmMain.glControl_main.MakeCurrent()

        Main_ortho()

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.UseProgram(shader_list.basic_shader)
        Dim x, y As Single
        Dim cx = frmMain.glControl_main.Width / 2
        Dim cy = -frmMain.glControl_main.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 150.0F + cx
            y = Sin(k + j) * 150.0F + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle += 0.00003
            If angle > PI * 2 / 40 Then
                angle = 0
            End If
        Next
        GL.UseProgram(0)
        frmMain.glControl_main.SwapBuffers()

    End Sub

End Module

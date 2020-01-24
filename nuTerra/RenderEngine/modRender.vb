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
    Public angle1, angle2 As Single
    Public Sub draw_scene()

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0) ' Use default buffer


        '-------------------------------------------------------
        '1st glControl
        frmMain.glControl_main.MakeCurrent()
        Main_ortho_main()

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        GL.UseProgram(shader_list.basic_shader)
        Dim x, y As Single
        Dim cx = frmMain.glControl_main.Width / 4
        Dim cy = -frmMain.glControl_main.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle1
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 150.0F + cx
            y = Sin(k + j) * 150.0F + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle1 += 0.00003
            If angle1 > PI * 2 / 40 Then
                angle1 = 0
            End If
        Next

        frmMain.glControl_main.SwapBuffers()

        '-------------------------------------------------------
        '2nd glControl
        frmMain.glControl_utility.MakeCurrent()
        Main_ortho_utility()

        GL.ClearColor(Color.DarkBlue)
        GL.Clear(ClearBufferMask.DepthBufferBit Or ClearBufferMask.ColorBufferBit)

        cx = frmMain.glControl_utility.Width / 2
        cy = -frmMain.glControl_utility.Height / 2
        For k = 0 To PI * 2.0F Step (PI * 2 / 40.0F)
            Dim j = angle2
            GL.Begin(PrimitiveType.Lines)
            x = Cos(k + j) * 150.0F + cx
            y = Sin(k + j) * 150.0F + cy
            GL.Vertex2(cx, cy)
            GL.Vertex2(x, y)
            GL.End()
            angle2 += 0.00001
            If angle2 > PI * 2 / 40 Then
                angle2 = 0
            End If
        Next
        GL.UseProgram(0)
        frmMain.glControl_utility.SwapBuffers()

    End Sub

End Module

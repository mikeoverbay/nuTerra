Imports System.IO
Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Module modUtilities
    Public CUBE_VAO As GLVertexArray

    ' When non-null, every LogThis line is also appended here. Snapshot uses
    ' it to tee its block into %TEMP%\nuTerra\snapshot.txt - the console is a
    ' window an agent cannot read.
    Public LOG_TEE As System.Text.StringBuilder

    ''' <summary>
    ''' Prefixes that survive the gate below - the TANK PATH functions.
    '''
    ''' Matched on the format string's opening, which is why every tank
    ''' log line in this project starts with one of these tags. Not in the
    ''' list, deliberately: `tank files:`, `tank fx:`, `tank cards:` and
    ''' `tank shadow:` are tank subsystems but not PATH, and the owner asked
    ''' for the path functions.
    ''' </summary>
    Private ReadOnly LOG_KEEP As String() = {
        "tank:", "tank ai:", "tank routes:", "tank nav:",
        "tank squares:", "tank rays:", "tank sim:", "tank comm:"}

    ''' <summary>Let everything through again. Off is the owner's ask,
    ''' 2026-09-13: "remove all debug out writes for everything but the tank
    ''' path functions". Set true to get the old firehose back for a
    ''' session - nothing was deleted, so it all returns.</summary>
    Public LOG_EVERYTHING As Boolean = False

    ''' <summary>
    ''' Write one line, IF it is a tank path line.
    '''
    ''' Gated at the sink rather than by deleting 439 call sites. Same
    ''' visible result - nothing else writes - and three things a delete
    ''' would have cost: the diagnostics come back by flipping one
    ''' Boolean, no neighbouring line gets caught in a 40-file edit, and
    ''' the lines that found tonight's bugs still exist to be turned on.
    '''
    ''' Added 2026-09-13 by nuTerra work.
    ''' </summary>
    Public Sub LogThis(entry As String, ParamArray args() As Object)
        If Not LOG_EVERYTHING Then
            If entry Is Nothing Then Return
            Dim keep = False
            For Each tag In LOG_KEEP
                If entry.StartsWith(tag, StringComparison.OrdinalIgnoreCase) Then
                    keep = True
                    Exit For
                End If
            Next
            If Not keep Then Return
        End If
#If DEBUG Then
        Debug.Print(entry, args)
#End If
        Console.WriteLine(entry, args)
        LOG_TEE?.AppendFormat(entry, args).AppendLine()
    End Sub

    Public Sub make_cube()
        Dim verts() As Single = {
             0.5, 0.5, 0.5,
            -0.5, 0.5, 0.5,
            0.5, -0.5, 0.5,
            -0.5, -0.5, 0.5,
            -0.5, -0.5, -0.5,
            -0.5, 0.5, 0.5,
            -0.5, 0.5, -0.5,
            0.5, 0.5, 0.5,
            0.5, 0.5, -0.5,
            0.5, -0.5, 0.5,
            0.5, -0.5, -0.5,
            -0.5, -0.5, -0.5,
            0.5, 0.5, -0.5,
            -0.5, 0.5, -0.5
        }

        Dim vbo = GLBuffer.Create(BufferTarget.ArrayBuffer, "CUBE")
        vbo.Storage(verts.Length * 4, verts, BufferStorageFlags.None)

        CUBE_VAO = GLVertexArray.Create("CUBE")
        CUBE_VAO.VertexBuffer(0, vbo, IntPtr.Zero, 12)
        CUBE_VAO.AttribFormat(0, 3, VertexAttribType.Float, False, 0)
        CUBE_VAO.AttribBinding(0, 0)
        CUBE_VAO.EnableAttrib(0)
    End Sub
End Module

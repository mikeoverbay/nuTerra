Imports System.Diagnostics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' What the tank half of a frame actually costs, on the GPU and on the CPU.
'''
''' WHY BOTH CLOCKS. A stopwatch round a draw call measures how long it took to
''' SUBMIT the work, not how long the GPU spent on it - the driver queues and
''' returns immediately, so a pass that costs 30 ms of GPU can read as 0.2 ms
''' of CPU. Timing only the CPU is how a renderer gets blamed for being slow
''' when it is waiting, and how a shader gets away with being slow because
''' nobody measured it. So: GL timer queries for the GPU, Stopwatch for the
''' CPU, both reported.
'''
''' READ A FRAME LATE, ON PURPOSE. glGetQueryObject on a query issued this
''' frame blocks until the GPU reaches it, which converts a measurement into a
''' stall and changes the thing being measured. The pairs are double buffered
''' and the previous frame's result is collected, which costs one frame of lag
''' and nothing else.
'''
''' THIS IS A DIAGNOSTIC, NOT A FEATURE. It is off unless TANK_PROBE is set,
''' and it stays out of everything that is not under Tanks/ - whole-frame
''' timing belongs to whoever owns the render loop.
'''
''' Added 2026-09-13 by Tank AI work.
''' </summary>
Public Module TankProbe

    ''' <summary>Measure and report. Off by default - the queries are cheap but
    ''' they are not free, and a diagnostic left running is a diagnostic
    ''' nobody reads.</summary>
    Public TANK_PROBE As Boolean = False

    ''' <summary>Seconds between log lines.</summary>
    Private Const REPORT_S As Double = 2.0

    Private Class Slot
        Public ReadOnly q(1) As Integer         ' two query ids, one a frame
        Public cur As Integer = 0
        Public issued As Boolean = False
        Public gpuNs As Double = 0.0            ' running total this window
        Public cpuMs As Double = 0.0
        Public calls As Integer = 0
    End Class

    Private ReadOnly slots As New Dictionary(Of String, Slot)
    Private ReadOnly sw As New Stopwatch
    Private ReadOnly window As New Stopwatch

    ''' <summary>Start timing a named pass. Pairs with Stop.</summary>
    Public Sub StartPass(name As String)
        If Not TANK_PROBE Then Return
        Dim s As Slot = Nothing
        If Not slots.TryGetValue(name, s) Then
            s = New Slot()
            GL.GenQueries(2, s.q)
            slots(name) = s
        End If
        ' Collect the OTHER buffer's result - it was issued last frame and is
        ' finished by now, so this never waits.
        Dim other = 1 - s.cur
        If s.issued Then
            Dim ready = 0
            GL.GetQueryObject(s.q(other), GetQueryObjectParam.QueryResultAvailable, ready)
            If ready <> 0 Then
                Dim ns As Long = 0
                GL.GetQueryObject(s.q(other), GetQueryObjectParam.QueryResult, ns)
                s.gpuNs += ns
            End If
        End If
        GL.BeginQuery(QueryTarget.TimeElapsed, s.q(s.cur))
        sw.Restart()
        If Not window.IsRunning Then window.Start()
    End Sub

    ''' <summary>Finish the pass started by StartPass.</summary>
    Public Sub StopPass(name As String)
        If Not TANK_PROBE Then Return
        Dim s As Slot = Nothing
        If Not slots.TryGetValue(name, s) Then Return
        GL.EndQuery(QueryTarget.TimeElapsed)
        sw.[Stop]()
        s.cpuMs += sw.Elapsed.TotalMilliseconds
        s.calls += 1
        s.issued = True
        s.cur = 1 - s.cur
    End Sub

    ''' <summary>A CPU-only stretch - the AI, which issues no GL at all.</summary>
    Public Sub CpuOnly(name As String, ms As Double)
        If Not TANK_PROBE Then Return
        Dim s As Slot = Nothing
        If Not slots.TryGetValue(name, s) Then
            s = New Slot()
            slots(name) = s
        End If
        s.cpuMs += ms
        s.calls += 1
        If Not window.IsRunning Then window.Start()
    End Sub

    ''' <summary>
    ''' Report and reset, every REPORT_S. Called once a frame from the tank
    ''' renderer; does nothing until the window is up.
    ''' </summary>
    Public Sub Tick()
        If Not TANK_PROBE Then Return
        If Not window.IsRunning OrElse window.Elapsed.TotalSeconds < REPORT_S Then Return
        Dim secs = window.Elapsed.TotalSeconds
        Dim frames = 0
        For Each kv In slots
            frames = Math.Max(frames, kv.Value.calls)
        Next
        If frames = 0 Then
            window.Restart()
            Return
        End If
        LogThis("tank probe: {0:0.0} s, {1} frame(s), {2:0.0} fps over the window",
                secs, frames, frames / secs)
        For Each kv In slots
            Dim s = kv.Value
            If s.calls = 0 Then Continue For
            LogThis("  {0,-18} gpu {1,7:0.000} ms/frame   cpu {2,7:0.000} ms/frame",
                    kv.Key, s.gpuNs / 1000000.0 / s.calls, s.cpuMs / s.calls)
            s.gpuNs = 0.0
            s.cpuMs = 0.0
            s.calls = 0
        Next
        window.Restart()
    End Sub

End Module

Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Per-pass GPU timing, for the stats overlay.
'''
''' These are GL_TIME_ELAPSED queries, so they measure what the GPU actually
''' spent on a pass - not how long the driver took to accept the commands. A CPU
''' stopwatch around a draw call measures submission and nothing else, which on
''' a deferred renderer is close to meaningless.
'''
''' Reading a query in the frame that issued it stalls until the GPU catches up,
''' which is exactly the thing that would make the measurement change the result.
''' So each section keeps two query objects and alternates: the value shown is
''' from the previous frame's slot, which is guaranteed complete by the time it
''' is asked for. One frame of latency, no pipeline bubble.
'''
''' GL_TIME_ELAPSED cannot nest - there is one active query per target - so
''' sections must be flat. Begin without a matching Finish, or a Begin inside
''' another, will produce a GL error rather than a wrong number.
''' </summary>
Public Module modGpuTimers

    Public Class Section
        Public name As String
        Public queries() As Integer = {0, 0}
        Public issued() As Boolean = {False, False}
        '''<summary>Last completed measurement, milliseconds.</summary>
        Public ms As Double
        '''<summary>Smoothed, so the readout is legible rather than a blur.</summary>
        Public avg_ms As Double
    End Class

    '''<summary>Off by default - the queries cost nothing to declare but the
    ''' driver does real work per query, so they are only issued when looked at.</summary>
    Public Enabled As Boolean = False

    Private ReadOnly order As New List(Of Section)
    Private ReadOnly lookup As New Dictionary(Of String, Section)
    Private slot As Integer = 0
    Private active As Section = Nothing

    '''<summary>Sections in the order they were first seen, i.e. pass order.</summary>
    Public ReadOnly Property Sections As List(Of Section)
        Get
            Return order
        End Get
    End Property

    Public ReadOnly Property TotalMs As Double
        Get
            Dim t As Double = 0
            For Each s In order
                t += s.avg_ms
            Next
            Return t
        End Get
    End Property

    '''<summary>Call once per frame, before any Begin.</summary>
    Public Sub NewFrame()
        If active IsNot Nothing Then
            ' A pass returned early between Begin and Finish. Close it rather
            ' than leaving the query open into the next frame, which would make
            ' every later Begin fail.
            GL.EndQuery(QueryTarget.TimeElapsed)
            active.issued(slot) = True
            active = Nothing
        End If
        slot = 1 - slot
    End Sub

    Public Sub [Begin](name As String)
        If Not Enabled Then Return
        If active IsNot Nothing Then Return ' nested - ignore, see the summary

        Dim s As Section = Nothing
        If Not lookup.TryGetValue(name, s) Then
            s = New Section With {.name = name}
            s.queries(0) = GL.GenQuery()
            s.queries(1) = GL.GenQuery()
            lookup(name) = s
            order.Add(s)
        End If

        ' Harvest whatever this slot was holding from the frame before last.
        If s.issued(slot) Then
            Dim ready = 0
            GL.GetQueryObject(s.queries(slot), GetQueryObjectParam.QueryResultAvailable, ready)
            If ready <> 0 Then
                Dim ns As Long = 0
                GL.GetQueryObject(s.queries(slot), GetQueryObjectParam.QueryResult, ns)
                s.ms = ns / 1000000.0
                ' Exponential smoothing. Raw per-frame GPU times jitter enough
                ' to be unreadable at 200 fps.
                s.avg_ms = If(s.avg_ms = 0.0, s.ms, s.avg_ms * 0.9 + s.ms * 0.1)
            End If
            s.issued(slot) = False
        End If

        GL.BeginQuery(QueryTarget.TimeElapsed, s.queries(slot))
        active = s
    End Sub

    Public Sub Finish()
        If active Is Nothing Then Return
        GL.EndQuery(QueryTarget.TimeElapsed)
        active.issued(slot) = True
        active = Nothing
    End Sub

    '''<summary>Drops every measurement. Call when the map changes - the old
    ''' averages describe a scene that is no longer on screen.</summary>
    Public Sub Reset()
        For Each s In order
            s.ms = 0
            s.avg_ms = 0
            s.issued(0) = False
            s.issued(1) = False
        Next
    End Sub

End Module

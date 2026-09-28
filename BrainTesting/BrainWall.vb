Imports OpenTK.Mathematics

''' <summary>
''' A WALL IS A CHAIN OF BLOCKERS WITH NO DOORS IN IT.
'''
''' "new test. start to end angle of a continues connected blockers? ... no
''' doors basically in the chain" - the owner, 2026-09-28.
'''
''' This is a better statement of "there is no way through" than the one the
''' no-way-out rule uses, and the data for it was already on every ray.
'''
''' WHAT linked ALREADY MEANS. BrainRadar sets it per ray and its own comment is
''' the definition: "linked means the hull does NOT fit: both rays found
''' something and" the gap between them is too narrow to pass. Two returns a
''' metre apart with open sky between them are never linked. So a run of
''' consecutive linked rays IS a continuous connected barrier with no door in
''' it, measured at the hull's own width, and the angle from the first ray of
''' that run to the last is the owner's start-to-end angle.
'''
''' WHY IT BEATS "ALL 54 RAYS BLOCKED". The no-way-out rule requires every ray
''' across 160 degrees to return something inside 40 m. A wall whose ends the
''' rays just sneak past fails that test and is not reported, even though
''' nothing can drive through it. And a scatter of unconnected returns - trees,
''' rubble, a tank - passes it while being full of doors. The chain test
''' separates those two: one asks how much is blocked, this asks whether what is
''' blocked is JOINED UP.
'''
''' WRAPS ROUND THE CIRCLE. The scan is 120 rays over 360 degrees and a wall does
''' not care where the array begins, so a chain running through the rear must be
''' found as one chain rather than two. Scanning twice round and capping the run
''' at one full turn is the whole of it.
'''
''' COSTS NOTHING. One pass over 120 booleans, no geometry, no grid reads. This
''' can run every tick where BrainReach's flood fill cannot - which is the point
''' of having both: the fill is the global truth at 400 ms, this is the local
''' read at no cost.
''' </summary>
Module BrainWall

    ''' <summary>The widest doorless chain in the last scan.</summary>
    Public SpanDeg As Single = 0.0F
    ''' <summary>Its two ends, degrees off the nose.</summary>
    Public FromDeg As Single = 0.0F
    Public ToDeg As Single = 0.0F
    ''' <summary>How many rays it covers. A one-ray "chain" is a single return,
    ''' not a barrier.</summary>
    Public Rays As Integer = 0

    ''' <summary>
    ''' Measure the widest run of consecutive linked rays.
    '''
    ''' Returns the span in degrees, and leaves the ends in FromDeg/ToDeg.
    ''' </summary>
    Public Function Widest(hits As BrainRadar.Hit()) As Single
        SpanDeg = 0.0F
        FromDeg = 0.0F
        ToDeg = 0.0F
        Rays = 0
        If hits Is Nothing OrElse hits.Length < 3 Then Return 0.0F

        Dim n = hits.Length
        Dim bestLen = 0, bestStart = -1
        Dim runLen = 0, runStart = 0

        ' TWICE ROUND, so a chain that straddles the start of the array is found
        ' whole. Capped at n so a fully enclosed hull reports one turn and not
        ' two.
        For k = 0 To 2 * n - 1
            Dim i = k Mod n
            If hits(i).linked Then
                If runLen = 0 Then runStart = i
                runLen += 1
                If runLen > n Then runLen = n
                If runLen > bestLen Then
                    bestLen = runLen
                    bestStart = runStart
                End If
            Else
                runLen = 0
            End If
        Next

        If bestLen <= 1 OrElse bestStart < 0 Then Return 0.0F

        ' A run of L linked rays spans L+1 RAYS - linked(i) joins ray i to the
        ' next one - so the far end is one past the last link.
        Dim iEnd = (bestStart + bestLen) Mod n
        FromDeg = MathHelper.RadiansToDegrees(hits(bestStart).angle)
        ToDeg = MathHelper.RadiansToDegrees(hits(iEnd).angle)
        Rays = bestLen + 1

        ' FROM THE RAY COUNT, not by subtracting the two bearings. The ends can
        ' straddle the wrap, where a subtraction gives the complement - a wall
        ' covering 300 degrees would report 60 and read as a doorway.
        SpanDeg = 360.0F * bestLen / n
        Return SpanDeg
    End Function

    ''' <summary>
    ''' Is this bearing inside the widest doorless chain?
    '''
    ''' The question that matters about a wall: not how big it is, but whether it
    ''' is between us and where we want to go. Answered against the last Widest.
    ''' </summary>
    Public Function Blocks(hits As BrainRadar.Hit(), bearingRad As Single) As Boolean
        If Rays <= 1 Then Return False
        Dim b = MathHelper.RadiansToDegrees(bearingRad)
        ' Walk from the chain's start by its own span, comparing in wrapped
        ' degrees, so a chain straddling the rear still contains what it
        ' contains.
        Dim off = wrap360(b - FromDeg)
        Return off <= SpanDeg
    End Function

    Private Function wrap360(a As Single) As Single
        Dim x = a
        While x < 0.0F
            x += 360.0F
        End While
        While x >= 360.0F
            x -= 360.0F
        End While
        Return x
    End Function

End Module

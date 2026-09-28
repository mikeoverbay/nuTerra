Imports OpenTK.Mathematics

''' <summary>
''' NO WAY OUT: report a dead end. Do not write one.
'''
''' "remove the wall placed in the bake data. This is not the way." - the owner,
''' 2026-09-28, after watching a run that "worked until it turned in to a trap".
'''
''' THIS MODULE USED TO STAMP. It drew a chord across the mouth of a dead end
''' into BrainNav's in-memory copy of the bake, so the planner would not re-enter
''' it, and drew cyan boxes on the cells it had changed. All of that is gone.
'''
''' WHY IT WAS THE WRONG MECHANISM, recorded so it is not rebuilt a fourth time.
''' MarkBlocked did this and was reverted twice and finally deleted. This version
''' survived longer because it was more careful - written only after backing
''' clear, guarded on being ahead of the nose, refusing to read its own chords as
''' evidence - and it still ended up making the tank worse, because the fault is
''' not in the care taken. A brain that edits the map is answering "how do I get
''' out of here" by changing the world instead of changing its mind. Every such
''' edit is a permanent, invisible constraint derived from one hull width, one
''' goal and one moment of not knowing what to do, and it outlives all three.
'''
''' WHAT IS KEPT is the detection, because it costs one pass over the rays and
''' says something true: the forward arc is solid and the walk found nothing. As
''' a LOG LINE it is a diagnostic. As a map write it was a decision the brain was
''' not entitled to make.
'''
''' BrainWall is the better tool for the same question and does not touch
''' anything: it measures the widest run of connected blockers with no door in
''' it, and reports both ends, which is a go-around bearing rather than a wall.
''' </summary>
Module BrainTrap

    ''' <summary>Half the forward arc that must be solid, degrees.</summary>
    Private Const ARC_DEG As Single = 80.0F

    Public Flags As Integer = 0

    ''' <summary>
    ''' Look at a closed arc and say so. Records nothing, writes nothing.
    '''
    ''' Returns True while the condition holds, so a caller can gate on it
    ''' without asking twice - but nothing acts on it now, by instruction.
    ''' </summary>
    Public Function Look(hits As BrainRadar.Hit(), pos As Vector2,
                         headingRad As Single, noPath As Boolean) As Boolean
        If hits Is Nothing OrElse Not noPath Then Return False

        Dim arc = MathHelper.DegreesToRadians(ARC_DEG)
        Dim far_ = BrainTune.Get_("trapm", BrainRadar.REACH_M)
        Dim n = 0
        Dim loA = Single.MaxValue, hiA = Single.MinValue
        Dim loP As Vector2 = Nothing, hiP As Vector2 = Nothing

        For Each hh In hits
            If Math.Abs(hh.angle) > arc Then Continue For
            n += 1
            ' One gap is a way out, so the rule fails on the first of them.
            If Not hh.found OrElse hh.dist > far_ Then Return False
            If hh.angle < loA Then
                loA = hh.angle
                loP = hh.at
            End If
            If hh.angle > hiA Then
                hiA = hh.angle
                hiP = hh.at
            End If
        Next

        ' A thin scan is not a closed arc: 120 rays over 360 degrees should
        ' return about 53 across the forward 160, and a handful all happening to
        ' hit proves nothing.
        If n < CInt(BrainTune.Get_("trapmin", 20.0F)) Then Return False

        Flags += 1
        If Flags <= 8 Then
            LogThis("brain: hull {0} DEAD END - {1} rays across {2:0} deg all blocked " &
                    "inside {3:0} m and the walk found nothing. At ({4:0.0}, {5:0.0}) " &
                    "facing {6:0} deg, mouth {7:0.0} m across. Reported only - nothing " &
                    "is written to the map.",
                    BrainRadar.HULL, n, ARC_DEG * 2.0F, far_, pos.X, pos.Y,
                    MathHelper.RadiansToDegrees(headingRad), (hiP - loP).Length)
        End If
        Return True
    End Function

    ''' <summary>Nothing is held any more, so there is nothing to turn out of.
    ''' Kept so the No Way Out node still compiles; it is unwired by default.</summary>
    Public Function TurnOut(pos As Vector2) As Boolean
        Return False
    End Function

    Public Sub Forget()
        Flags = 0
    End Sub

End Module

''' <summary>
''' WHICH TANK THE CAMERA IS ON. TAB moves it.
'''
''' "I want the tap key to switch which tank I am following" - the owner,
''' 2026-09-25.
'''
''' NO NEW STATE, and that is the whole design. The hull being followed is
''' BrainRadar.HULL and has been all along: the chase camera centres on
''' Bodies(BrainRadar.HULL), the panel reads its speed and its reason, the
''' ahead-scope draws its box, the walk view draws its rays. Storing a second
''' "who am I watching" beside it would give the app two answers to one
''' question and a bug the first time they disagreed.
'''
''' So this module owns no field. It is the one PLACE that writes
''' BrainRadar.HULL from the keyboard, and the rules about what may be
''' followed live here rather than being spelled out again in the key handler.
'''
''' WHAT IT CYCLES: the crew's seats - the tanks that are actually driving -
''' rather than the whole roster. Following a parked hull is a blank screen
''' with a tank in it, and the reason to switch views is to see what another
''' DRIVER is doing. With no crew installed it falls back to every hull,
''' because then nothing is driving and cycling the roster is still the
''' useful thing.
'''
''' The dependency runs one way, BrainWatch -> BrainCrew. The crew reads
''' BrainRadar.HULL directly and has never heard of this module.
''' </summary>
Module BrainWatch

    ''' <summary>The hull being followed. A property over BrainRadar.HULL, not
    ''' a copy of it.</summary>
    Public ReadOnly Property Hull As Integer
        Get
            Return BrainRadar.HULL
        End Get
    End Property

    ''' <summary>Everything that may be followed: the drivers if there are
    ''' any, else the roster.</summary>
    Private Function candidates() As List(Of Integer)
        Dim crew = TryCast(BrainSim.Brain, BrainCrew)
        If crew IsNot Nothing AndAlso crew.Driving.Count > 0 Then
            Return New List(Of Integer)(crew.Driving)
        End If
        Dim all As New List(Of Integer)
        If BrainTanks.Bodies IsNot Nothing Then
            For i = 0 To BrainTanks.Bodies.Count - 1
                all.Add(i)
            Next
        End If
        Return all
    End Function

    ''' <summary>Follow this hull. Refuses a hull that does not exist rather
    ''' than leaving the camera chasing an index off the end of the roster.</summary>
    Public Sub Look(i As Integer)
        If BrainTanks.Bodies Is Nothing OrElse i < 0 OrElse
           i >= BrainTanks.Bodies.Count Then Return
        BrainRadar.HULL = i
        say()
    End Sub

    ''' <summary>
    ''' TAB: the next driver round, wrapping.
    '''
    ''' From the CURRENT hull's position in the list, so it steps in hull
    ''' order however the camera got where it is - and if the current hull is
    ''' not in the list at all (watching a parked tank, then the crew mans up)
    ''' the first driver is the answer, not a crash.
    ''' </summary>
    Public Sub Next_()
        step_(1)
    End Sub

    ''' <summary>SHIFT+TAB: the other way. Five tanks is few enough that
    ''' forwards would do, but going back one after overshooting is the whole
    ''' reason a person taps a key twice.</summary>
    Public Sub Prev()
        step_(-1)
    End Sub

    Private Sub step_(by As Integer)
        Dim c = candidates()
        If c.Count = 0 Then Return
        Dim at = c.IndexOf(BrainRadar.HULL)
        Dim nxt As Integer
        If at < 0 Then
            nxt = If(by > 0, 0, c.Count - 1)
        Else
            nxt = (at + by + c.Count) Mod c.Count
        End If
        If c(nxt) = BrainRadar.HULL Then Return
        BrainRadar.HULL = c(nxt)
        say()
    End Sub

    ''' <summary>
    ''' One line, naming the tank rather than an index.
    '''
    ''' A number on its own is no use on a screen with five of them: the
    ''' vehicle tag and the road it is driving are what tell you which of the
    ''' shapes out there you are now sitting behind.
    ''' </summary>
    Private Sub say()
        If BrainTanks.Bodies Is Nothing OrElse
           BrainRadar.HULL >= BrainTanks.Bodies.Count Then Return
        Dim b = BrainTanks.Bodies(BrainRadar.HULL)
        LogThis("brain: following hull {0} - {1}, team {2}, road {3}, at ({4:0.0}, {5:0.0})",
                BrainRadar.HULL, b.tag, b.team,
                BrainPath.RoadOf(BrainRadar.HULL), b.spawn.X, b.spawn.Y)
    End Sub

End Module

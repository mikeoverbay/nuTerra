''' <summary>
''' Every global the command line writes into, and nothing else.
'''
''' DELIBERATELY NOT nuTerra's modGlobalVars, which is 1,888 lines. The names
''' here match nuTerra's on purpose: a file linked in from over there refers to
''' STARTUP_MAP unqualified, and a VB Module's members are reachable unqualified
''' across the assembly, so the linked file resolves against this one and needs
''' no edit.
'''
''' If modGlobalVars itself is ever linked, these collide and the compiler says
''' so. That is the intended failure - loud, at build time, naming the symbol -
''' rather than two stores of the same setting drifting apart at runtime, which
''' is the shape of bug this project has paid for before.
'''
''' Added 2026-09-15 by nuTerra work, stage 0 of docs\brain_testing_plan.md.
''' </summary>
Module BrainGlobals

    ''' <summary>The app's name, in one place. It is the window title, it is
    ''' what the owner calls it, and it is what a screenshot has to say so he
    ''' can tell this window from nuTerra's at a glance.</summary>
    Public Const APP_NAME As String = "Brain Testing"

    ' ---- the window ------------------------------------------------------

    Public SCR_WIDTH As Integer = 1600
    Public SCR_HEIGHT As Integer = 900
    Public HALF_SIZE_WINDOW As Boolean = False
    Public FULLSCREEN_WINDOW As Boolean = False

    ''' <summary>Hide the HUD. Same meaning as nuTerra's, so a habit carries
    ''' over.</summary>
    Public CLEAN_VIEW As Boolean = False

    ''' <summary>Which session opened this window, from `owner=`. Several
    ''' sessions run these apps at once and the windows are otherwise
    ''' identical - the owner could not tell whose was whose, and asked for
    ''' the tag. Never the model name: every session is Opus, so that is the
    ''' one tag that cannot distinguish anything.</summary>
    Public OWNER_TAG As String = ""

    ' ---- the world -------------------------------------------------------

    ''' <summary>Map named on the command line, loaded without the menu.
    ''' Nothing when the menu is to be shown.</summary>
    Public STARTUP_MAP As String = Nothing

    ''' <summary>r, ax, ay, lx, ly, lz - exactly what nuTerra's Snapshot
    ''' prints, so a view set up by hand over there reproduces here.</summary>
    Public STARTUP_CAM As Single() = Nothing

    ' ---- the bodies ------------------------------------------------------

    Public TANK_AUTOLOAD As Boolean = False
    Public TANK_SOLO_TAG As String = Nothing
    Public TANK_PER_TEAM As Integer = 15

    ''' <summary>Run the brain, or leave every hull standing. Both directions
    ''' from the command line (`brain=1` / `brain=0`) rather than a bare flag
    ''' that can only switch it on - a switch that cannot say "off" stops
    ''' working the day the default changes to meet it.</summary>
    Public BRAIN_ON As Boolean = False

End Module

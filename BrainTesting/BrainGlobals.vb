Imports OpenTK.Mathematics

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
    ' NOTE, 2026-09-15: the window and world globals that used to live here
    ' (STARTUP_MAP, TANK_PER_TEAM, CLEAN_VIEW and the rest) were removed when
    ' nuTerra's own modGlobalVars was linked in for the terrain builders. Two
    ' declarations of one setting is the drift this project keeps paying for,
    ' and the compiler will not let it happen quietly - it is an ambiguity
    ' error naming the symbol. What is left here is what only this app has.

    ''' <summary>The app's name, in one place. It is the window title, it is
    ''' what the owner calls it, and it is what a screenshot has to say so he
    ''' can tell this window from nuTerra's at a glance.</summary>
    Public Const APP_NAME As String = "Brain Testing"

    ' ---- the window ------------------------------------------------------

    Public SCR_WIDTH As Integer = 1600
    Public SCR_HEIGHT As Integer = 900

    ''' <summary>Hide the HUD. Same meaning as nuTerra's, so a habit carries
    ''' over.</summary>

    ''' <summary>Which session opened this window, from `owner=`. Several
    ''' sessions run these apps at once and the windows are otherwise
    ''' identical - the owner could not tell whose was whose, and asked for
    ''' the tag. Never the model name: every session is Opus, so that is the
    ''' one tag that cannot distinguish anything.</summary>
    Public OWNER_TAG As String = ""

    ' ---- the world -------------------------------------------------------

    ''' <summary>Map named on the command line, loaded without the menu.
    ''' Nothing when the menu is to be shown.</summary>

    ''' <summary>r, ax, ay, lx, ly, lz - exactly what nuTerra's Snapshot
    ''' prints, so a view set up by hand over there reproduces here.</summary>

    ' ---- the bodies ------------------------------------------------------

    ''' <summary>Run the brain, or leave every hull standing. Both directions
    ''' from the command line (`brain=1` / `brain=0`) rather than a bare flag
    ''' that can only switch it on - a switch that cannot say "off" stops
    ''' working the day the default changes to meet it.</summary>
    ''' <summary>ON BY DEFAULT, 2026-09-16: "We need to just start the sim at
    ''' start but that need to be under Tank AI's control." The sim running is
    ''' this app's normal state; what it does is the installed brain's, and the
    ''' shipped one parks everything. `sim=0` turns it off.</summary>
    Public BRAIN_ON As Boolean = True

    ''' <summary>Print the per-tank hull box table once at load. Off by
    ''' default: it is thirty lines, and it answers a question asked once.
    ''' `hullbox` on the command line asks it.</summary>
    Public HULL_BOX_TABLE As Boolean = False

    ''' <summary>Hand back every log line the linked nuTerra files emit.
    ''' `verbose` on the command line. Off, this app prints only its own
    ''' "brain:" lines.</summary>
    Public LOG_VERBOSE As Boolean = False

    ''' <summary>Write one frame here and quit. Empty means run normally.</summary>
    Public SHOT_PATH As String = ""

    ''' <summary>
    ''' Hold the shot until the sim has been RUNNING this many seconds.
    '''
    ''' Zero keeps the old behaviour - the first frame with the roster on
    ''' it, which answers "does it draw". Anything above zero answers a
    ''' different question: what does an INSTRUMENT look like. Most of them
    ''' here show nothing at all until the brain has scanned once, because
    ''' they read BrainRadar.LAST and only a brain tick ever fills it.
    ''' </summary>
    Public SHOT_AFTER_S As Single = 0.0F

    ''' <summary>Drive this many seconds, print a scorecard, quit. Zero
    ''' runs normally. `runfor=` on the command line, and the whole point
    ''' of it is that two brains get the same window.</summary>
    Public RUN_SECS As Single = 0.0F

    ''' <summary>Append one CSV row here when a timed run ends.</summary>
    Public SCORE_FILE As String = ""

    ''' <summary>
    ''' FOLLOW CAM AT THIS DISTANCE, with the trailing heading on. Metres.
    '''
    ''' Separate from topdown= on purpose. That one turns the trail on too,
    ''' but forces pitch to -1.5708 and gives a bird's eye that rotates under
    ''' you - which is the view ChaseTrail's own comment warns about. This
    ''' leaves the pitch alone, so the camera sits behind the hull rather
    ''' than over it.
    ''' </summary>
    Public TRAIL_M As Single = 0.0F

    ''' <summary>Start maximised. NOT fullscreen: the border and title bar
    ''' stay, and with several sessions running this app the title is how
    ''' the owner tells the windows apart.</summary>
    Public MAXIMIZED_WINDOW As Boolean = False

    ''' <summary>End a run that has not improved its closest approach for
    ''' this many seconds. Zero waits the whole clock out. A failure that
    ''' costs twenty seconds instead of seventy is three times as many
    ''' questions asked in an hour.</summary>
    Public BAIL_S As Single = 0.0F

    ''' <summary>Run this many brain ticks and then stop, leaving the last
    ''' one on screen to be looked at. The walk view only clears when the
    ''' walk runs, so a halted sim holds its picture.</summary>
    Public TICK_LIMIT As Integer = 0

    ''' <summary>Metres above the hull for the top-down chase view, or zero
    ''' for the ordinary camera. Applied AFTER the snapshot restores, because
    ''' LookAt hard-sets pitch and distance and would throw it away.</summary>
    Public TOP_DOWN_M As Single = 0.0F

    ''' <summary>x, z, distance - where to point the camera. Nothing means
    ''' frame the whole map.</summary>
    Public LOOK_AT As Single() = Nothing

    ''' <summary>Draw only the n_ (non-destructible) parts of map models.
    ''' The owner's call: the d_ half is scenery a tank drives through, and
    ''' drawing it shows a wall where the nav grid has open ground.</summary>
    Public MODELS_N_ONLY As Boolean = True

    ''' <summary>Come up in the last snapshot: tank, camera and goal.
    ''' An evening of scenarios starts the same way every time, and typing
    ''' the camera in and then clicking Restore is two steps for one
    ''' intention.</summary>
    Public RESTORE_ON_START As Boolean = False

    ''' <summary>
    ''' WHERE TO DRIVE, from the command line: base1, base2, or a bare x,z.
    '''
    ''' "start and green base" - the owner, 2026-09-19. A scenario named on
    ''' the command line is repeatable; a goal placed by hand is not, and the
    ''' snapshot holding the last one is whatever file happened to be newest
    ''' in the shared folder.
    ''' </summary>
    Public GOAL_ARG As String = ""

    ''' <summary>Install the brain and run, the moment the roster is up.
    ''' With `restore` the goal comes back with the scenario, so there is
    ''' nothing left to press.</summary>
    Public LEARN_ON_START As Boolean = False

    ''' <summary>
    ''' Drive from the node board instead of RangeBrain.
    '''
    ''' OFF. The graph is drawn FROM RangeBrain, so the only way to know
    ''' whether it drives as well as the thing it describes is to run both
    ''' and compare scorecards - which needs the incumbent still there.
    ''' </summary>
    Public USE_GRAPH As Boolean = False



    ''' <summary>Heightmap edge in samples. nuTerra keeps this in MapLoader.vb,
    ''' a 112 KB file this app does not link; the value is the same 64 and the
    ''' chunk reader compares against it.</summary>
    Public HEIGHTMAPSIZE As Integer = 64

    ''' <summary>The window, so the linked loaders can call ForceRender() to
    ''' keep a frame alive during a long load - the same thing nuTerra's
    ''' Program.main_window is for.</summary>
    Public main_window As BrainWindow

    ''' <summary>
    ''' Every model the space declares, indexed by the space.bin's own model
    ''' index. THE BUILDINGS - this is what stage 3 draws.
    '''
    ''' nuTerra keeps it in MapLoader.vb, a 112 KB file this app does not link
    ''' (it is the whole map load: decals, water, trees, particles). The
    ''' space.bin reader fills this array and is linked, so the declaration
    ''' lives here instead. Same name and type, so that file compiles
    ''' unchanged.
    ''' </summary>
    Public MAP_MODELS() As mdl_

    ''' <summary>One model as the space declares it: its LOD chain, and the
    ''' bounds the game culls it by. Copied exactly from MapLoader.vb - the
    ''' linked space.bin reader fills both fields by name.</summary>
    Public Structure mdl_
        Public modelLods() As base_model_holder_
        Public visibilityBounds As Matrix2x3
    End Structure

End Module

Imports System.Text
Imports OpenTK.Mathematics

Module modGlobalVars
    Public map_scene As MapScene

    Public BG_MAX_VALUE As Integer
    Public BG_VALUE As Integer
    Public BG_TEXT As String

    ' VT params
    ' Overlay showing the baked sun depth map, for checking the sun camera
    ' actually frames the map.
    Public SHOW_SUN_SHADOW_VIEWER As Boolean = False
    Public SHOW_STATS_WINDOW As Boolean = False

    ' Mouse damping (Settings -> Camera), the three.js OrbitControls model:
    ' each frame the camera takes this fraction of the pending mouse delta
    ' (rotate, zoom and pan all ride it) and the rest carries over. Rides the
    ' per-map settings as "mouse_damp". OrbitControls ships 0.05; 1.0 is the
    ' old direct 1:1 response.
    Public ROT_DAMPING As Single = 0.1F

    '''<summary>
    ''' Metres added to every water surface at draw time. The data has no such
    ''' field - Fishing Bay authors exact heights and its mesh matches them to
    ''' the millimetre - so this is a viewer-side trim for judging by eye,
    ''' saved per map.
    '''</summary>
    Public WATER_Y_OFFSET As Single = 0.0F

    '''<summary>
    ''' Water is masked out where the pixel beneath it is a MODEL within this
    ''' many metres below the surface - which is what a boat interior is. The
    ''' game does this properly with excluded_water hull volumes baked into the
    ''' models; those are not parsed, and the G-buffer flag channel plus a depth
    ''' band is the viewer's stand-in. 0 disables. Saved per map.
    '''</summary>
    Public WATER_EXCLUDE_BAND As Single = 1.5F

    '''<summary>
    ''' Screen space reflections on wet surfaces. Reflects the geometry in the
    ''' frame that was just drawn, which is the only thing on hand that knows
    ''' where the buildings are - a cubemap cannot, it is the sky.
    '''</summary>
    Public SSR_ENABLED As Boolean = True
    Public SSR_INTENSITY As Single = 0.7F
    Public SSR_STEPS As Integer = 32
    '''<summary>Metres a hit may sit behind the ray and still count. Too large
    ''' smears reflections across depth gaps; too small drops thin geometry.</summary>
    Public SSR_THICKNESS As Single = 1.5F
    '''<summary>Metres per march step, before the per-step growth.</summary>
    Public SSR_STRIDE As Single = 0.35F
    Public SHADOW_VIEW_LO As Single = 0.0F
    Public SHADOW_VIEW_HI As Single = 1.0F

    ' Map-wide sun shadow baked into the VT page. ON by default - it measured
    ' 222 fps against 218 with no shadows at all, so at steady state it is very
    ' nearly free, and it is the only thing shadowing terrain and static models
    ' now that the live cascades carry trees alone.
    '
    ' The cost it does have is at bake time (4 extra taps per page texel), and a
    ' page resolves it at its own mip, so neighbouring pages at different mips
    ' can seam at a chunk edge. Both are open problems, neither is a reason to
    ' ship it off.
    '
    ' This default matters more than it looks: modMapSettings.Load only applies
    ' keys the file actually contains, so every settings file written before the
    ' baked_shadow key existed falls through to whatever this says.
    Public BAKED_SHADOW_ENABLED As Boolean = True

    '''<summary>
    ''' Use Moment Shadow Maps for the baked shadow instead of PCF over a depth
    ''' map. Moments are linear, so the map can be blurred and mipmapped once at
    ''' bake time and sampled with a single trilinear fetch - which is the only
    ''' way to get soft edges and correct minification without spending taps
    ''' every frame. Toggling re-bakes.
    '''</summary>
    Public MSM_SHADOW_ENABLED As Boolean = False

    ''' <summary>Mix toward a well-conditioned distribution. Raise if the solve
    ''' goes unstable over flat ground; lower for tighter contact shadows.</summary>
    Public MSM_MOMENT_BIAS As Single = 0.0003F

    ''' <summary>
    ''' Penumbra shaping, applied to both shadow paths before the light sees them.
    ''' Filtering decides how far a transition reaches; these decide how much of
    ''' that reach is visible, so the footprint can stay wide - which is what
    ''' keeps minification and antialiasing well behaved - while the edge reads
    ''' tight. Narrowing the band sharpens; closing it would re-alias, so the
    ''' shader keeps a floor between them.
    '''
    ''' LO doubles as the light-leak control on the moment path.
    ''' </summary>
    Public SHADOW_PENUMBRA_LO As Single = 0.15F
    Public SHADOW_PENUMBRA_HI As Single = 1.0F
    Public UPLOADS_PER_FRAME As Integer = 1
    ' Instrument (default off): per-bake page identity + per-frame VT stats
    ' in the log, for VT churn hunts.
    Public VT_BAKE_TRACE As Boolean
    ' Trace every tree species' draw-call classification at load (the part
    ' classifier is heuristic and has misfiled trunks before).
    Public TREES_DECODE_TRACE As Boolean
    ' Multiplier on the authored water-fog inverse depth (BWWa +0x70) -
    ' above 1 the water goes opaque sooner, below 1 it clears up.
    Public WATER_FOG_MUL As Single = 1.0F
    ' Per-pixel outland PBR from the cascade normal map (R = shine, B =
    ' metal) instead of constants. Default OFF: on Sand River R is the
    ' cutout mask (~0.91) and B is dead - it runs the sun spec hot.
    Public OUTLAND_PBR_NM As Boolean = False
    ' Outland specular intensity (gGMF.y) for the constant path - the field's
    ' sand pages run ~0.1-0.2 through this deferred; dial to match by eye.
    Public OUTLAND_SPEC As Single = 0.15F
    ' The candidate real detailAlbedoSml (TerrainSettings1.noise_texture) on
    ' the outland, A/B against the exact-no-op neutral. Repeats is a live
    ' slider because the game's factor (64 / g_chunks * uvScale) is engine-fed.
    Public OUTLAND_USE_DETAIL As Boolean = True
    Public OUTLAND_DETAIL_TILES As Single = 64.0F
    ' Blend the map-wide global_AM into the outland albedo bake, the same way
    ' t_mixer bakes it into every playfield VT page - without it the field is
    ' pulled toward the global map's tone and the outland stays raw tile
    ' colour: a visible colour step at the map edge.
    Public OUTLAND_GLOBAL_TINT As Boolean = True
    ' How much raw global remains blended in at full distance (the seam is
    ' always 100% global, matching the game's distant-terrain shader which
    ' writes g_globalAlbedoMap verbatim).
    Public OUTLAND_GLOBAL_BASE As Single = 0.35F
    ' Background outland decimation: threshold edge collapse per cull block
    ' after the weld, swapping in a new index buffer when done (91-94% fewer
    ' triangles at eps 0.25 in the offline prototype). eps is the max mean
    ' surface deviation in metres; the far cascade runs at 2x.
    Public OUTLAND_DECIMATE As Boolean = True
    Public OUTLAND_DECIMATE_EPS As Single = 0.25F
    ' Debug view: terrain tinted by the resident VT page's mip (checker =
    ' page cells), with a colour-key window. Settings -> VT shows the window;
    ' the window's own checkbox flips the overlay, its slider sets the blend.
    Public VT_PAGE_DEBUG As Boolean
    Public VT_PAGE_DEBUG_COLOR As Boolean = True
    Public VT_PAGE_DEBUG_MIX As Single = 0.45F
    ' Overlay shows mipfract greyscale instead of page colours - the moving
    ' bands are the trilinear blend sweeping between two coarse pages.
    Public VT_DEBUG_MIPFRACT As Boolean
    ' Test lever, affects REAL rendering: snap the VT trilinear blend to the
    ' nearest page mip. If the settling flicker dies with this on, the
    ' between-mips morph is the carrier.
    Public VT_NEAREST_MIP As Boolean
    ' 2048 is the hard ceiling - Texture2DArray depth. At TILE_SIZE 256 the atlas
    ' costs 256KB a tile (64 colour + 64 normal + 128 uncompressed specular), so
    ' this is a 512MB resident set. Bigger cache means fewer evictions, which
    ' means fewer re-bakes - and the bake is the expensive part.
    Public NUM_TILES As Integer = 2048
    Public VT_NUM_PAGES As Integer = 1024
    Public TILE_SIZE As Integer = 256
    Public FEEDBACK_WIDTH As Integer = 32
    Public FEEDBACK_HEIGHT As Integer = 32

    ' https://www.khronos.org/registry/OpenGL/extensions/NV/NV_representative_fragment_test.txt
    Public USE_REPRESENTATIVE_TEST As Boolean

    ' https://github.com/nvpro-samples/gl_occlusion_culling
    Public USE_RASTER_CULLING As Boolean = True

    Public WORK_GROUP_SIZE As Integer = 32

    Public USE_TESSELLATION As Boolean = False

    'Shading
    Public DUMMY_ATLAS As GLTexture
    Public FXAA_enable As Boolean = True
    Public FXAA_text As String = "FXAA On"
    Public TIME_OF_DAY As Single
    Public SUN_SCALE As Single
    Public RIPPLE_FRAME_NUMBER As Integer
    ''' <summary>Seconds since launch, for FX UV animation. Wrapped at an hour
    ''' so Single precision never gets grainy; every consumer uses it inside
    ''' fract()-like scrolling where the wrap is invisible.</summary>
    Public FX_TIME As Single

    ''' <summary>
    ''' Freeze the FX clock. The volumetric UV scroll rides FX_TIME, so two
    ''' captures of the same scene never match pixel for pixel while it runs -
    ''' which makes an automated before/after diff useless. Frozen, the only
    ''' thing that can move between two shots is the change under test.
    ''' </summary>
    Public FREEZE_FX As Boolean = False

    ''' <summary>
    ''' Camera handed in on the command line, applied once the map is up.
    ''' Format: cam=radius,xangle,yangle,lookX,lookY,lookZ - exactly the six
    ''' values Snapshot prints, so a saved snapshot can be pasted straight back.
    ''' </summary>
    Public STARTUP_CAM As Single() = Nothing

    ''' <summary>
    ''' Frames to wait after the map is up before firing Snapshot on its own,
    ''' then quitting. Zero means never. The harness cannot click the button,
    ''' so without this every automated run is judged from pixels alone - and
    ''' pixels cannot say whether a draw was even issued.
    ''' </summary>
    Public AUTO_SNAP_FRAMES As Integer = 0
    Public AUTO_SNAP_QUIT As Boolean = False

    ''' <summary>
    ''' Override for AUTO_SNAP_FRAMES, from the settle=N launch argument.
    ''' Zero means use the default. Raised when a capture has to wait for a
    ''' particle column to reach steady state - the emitters run at 1-5 per
    ''' second against 3-6 s lifetimes, so the default 150 frames catches a
    ''' column that is still filling.
    ''' </summary>
    Public SETTLE_FRAMES As Integer = 0

    ''' <summary>
    ''' Set for one frame to make the FX pass measure itself: the colour buffer
    ''' is read back either side of draw_fx and the difference logged. Two
    ''' glReadPixels stall the pipeline hard, so this is never left on.
    ''' </summary>
    Public FX_DIFF_THIS_FRAME As Boolean = False

    ''' <summary>Open at half the usual size - harness runs only.</summary>
    ''' <summary>
    ''' Draw the card particle system. OFF until the frame-blacking issue in
    ''' MapParticles.Draw is found - see the note at the call site.
    ''' </summary>
    Public PARTICLES_ENABLED As Boolean = True

    ''' <summary>
    ''' Draw particle cards as untextured wireframe, coloured by age (green new,
    ''' red old). Separates "is the simulation flowing correctly" from "are the
    ''' sprites right" - which is how the size bug was found: textured, a wrong
    ''' size track just looked like grey soup. Toggle in Settings -> Debug.
    ''' </summary>
    Public PARTICLES_WIRE As Boolean = False

    ''' <summary>Particle effect placements read from space.bin's BWPs section.</summary>
    Public PFX_PLACEMENTS As List(Of modParticles.PfxPlacement) = Nothing

    Public HALF_SIZE_WINDOW As Boolean = False

    ''' <summary>
    ''' Clear the colour buffer to black immediately before the FX pass, so the
    ''' frame contains the effects and nothing else. Done as a clear rather than
    ''' by editing deferred.frag: no shader is touched, and a rebuild cannot
    ''' silently undo it (copying shaders/ over bin/ did exactly that).
    ''' </summary>
    Public BLACK_BEFORE_FX As Boolean = False

    ''' <summary>
    ''' Strip the on-screen furniture - minimap and menu bar - for harness runs.
    ''' A frame meant to show one faint effect against black is ruined by UI
    ''' chrome: it dominates any pixel statistic taken over the whole frame.
    ''' Applied AFTER the per-map settings load, which would otherwise switch
    ''' the minimap back on.
    ''' </summary>
    Public CLEAN_VIEW As Boolean = False
    Public RIPPLE_MASK_TIME As Single
    Public MAX_MAP_HEIGHT As Single
    Public MIN_MAP_HEIGHT As Single
    Public MEAN_MAP_HEIGHT As Double
    Public TOTAL_HEIGHT_COUNT As Integer

    '============================================================
    ' this setting tweaks the mip biasing!
    Public GLOBAL_MIP_BIAS As Single = -0.75

    '============================================================
    'Render related
    Public T1_Y As Single
    Public T2_Y As Single
    Public DELTA_TIME As Single
    Public NORMAL_DISPLAY_MODE As Integer ' 0 None, 1 by vertex, 2 by face
    Public SHOW_BOUNDING_BOXES As Boolean
    '''<summary>Restrict the box overlay to GFX/volumetric instances.</summary>
    Public BOXES_VOLUMETRIC_ONLY As Boolean = True
    Public LOOP_COUNT As Integer = 200
    Public FPS_COUNTER As Integer
    Public FPS_TIME As Integer
    Public DONT_HIDE_HUD As Boolean = True
    Public DONT_HIDE_MINIMAP As Boolean = True
    Public SHOW_LOD_COLORS As Boolean
    'ascii characters
    Public ASCII_ID As GLTexture
    'wire flags
    Public WIRE_MODELS As Boolean
    Public WIRE_DECALS As Boolean
    Public WIRE_TERRAIN As Boolean
    Public WIRE_OUTLAND As Boolean
    'grid display
    Public SHOW_CHUNKS As Boolean
    Public SHOW_GRID As Boolean
    Public SHOW_BORDER As Boolean
    Public SHOW_CHUNK_IDs As Boolean
    'models
    Public DIRECTION_TEXTURE_ID As GLTexture
    Public MINI_WORLD_MOUSE_POSITION As Vector2
    Public MINI_MOUSE_CAPTURED As Boolean
    '============================================================
    ' background images
    Public nuTERRA_BG_IMAGE As GLTexture
    Public CHECKER_BOARD As GLTexture
    '============================================================
    Public LIGHT_POS As Vector3
    Public LIGHT_RADIUS As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_X As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE_Z As Single 'Used when orbiting the light
    Public LIGHT_ORBIT_ANGLE As Single
    Public PAUSE_ORBIT As Boolean = True
    Public LIGHT_SPEED As Single = 0.02F


    '============================================================
    'mouse camera related
    Public MOVE_CAM_Z, M_DOWN, MOVE_MOD, Z_MOVE, M_SPIN As Boolean ' mouse control booleans
    Public WASD_SPEED As Single = 0
    Public WASD_VECTOR As Point
    Public M_MOUSE As New Point
    Public SHOW_CURSOR As Integer
    '============================================================
    Public PROJECTIONMATRIX As New Matrix4
    Public VIEWMATRIX As New Matrix4
    '============================================================
    'Map related
    Public PLAYER_FIELD_CELL_SIZE As Single
    Public MAP_SIZE As Vector2
    Public MINI_MAP_SIZE As Integer = 240
    Public MINI_MAP_NEW_SIZE As Integer = 240
    Public MAP_NAME_NO_PATH As String = ""
    Public STARTUP_MAP As String ' optional map name passed on the command line
    Public MAP_LOADED As Boolean = False 'Rendering/settings clause
    Public TEMP_STORAGE As String 'Work are on users SSD/HDD
    Public DUMMY_TEXTURE_ID As GLTexture 'texture id 
    Public MAP_SELECT_BACKGROUND_ID As GLTexture 'texture id 
    '
    Public SHOW_MAPS_SCREEN As Boolean = False 'show pick menu screen
    Public SHOW_LOADING_SCREEN As Boolean = False 'show loading screen flag
    Public BLOCK_MOUSE As Boolean 'pick menu flag
    Public FINISH_MAPS As Boolean 'pick menu flag
    '
    Public EXPORT_STL_MAP As Boolean

    'Block loading flags. They are used for skipping loading of data.
    Public DONT_BLOCK_TERRAIN As Boolean
    Public DONT_BLOCK_OUTLAND As Boolean
    Public DONT_BLOCK_TREES As Boolean
    '''<summary>Tree placements that survived the frustum this frame, and in total.</summary>
    Public TREES_DRAWN As Integer
    Public TREES_TOTAL As Integer
    '''<summary>Tree placements drawn into the shadow map this update.</summary>
    Public TREES_CASTING As Integer

    '''<summary>Visible-tree split by LOD, e.g. "88/210/641", for the stats
    ''' panel - the direct check that distance is actually demoting geometry.</summary>
    Public TREES_LOD_TEXT As String = ""
    Public DONT_BLOCK_DECALS As Boolean
    '''<summary>Fade decals out at the box edge when their texture runs to its border.</summary>
    ''' <summary>
    ''' L2 spherical harmonics for the map's diffuse ambient, 9 RGB coefficients,
    ''' read from environments/&lt;env&gt;/probes/global/rem_sh.xml. The game lights
    ''' ambient with these instead of a flat colour, which is what gives surfaces
    ''' form in shade - a wall facing the sky picks up blue, one facing the ground
    ''' picks up warm bounce. Identity fallback is a flat grey so a map without
    ''' probes still renders.
    ''' </summary>
    Public SH_AMBIENT(8) As Vector3
    ''' <summary>Brightest luminance in the probe, from rem_sh.xml/max_lum.</summary>
    Public SH_MAX_LUM As Single = 1.0F
    ''' <summary>True once rem_sh.xml has been read for the current map.</summary>
    Public SH_AMBIENT_LOADED As Boolean
    ''' <summary>Use the SH probe for ambient instead of the flat constant.</summary>
    Public USE_SH_AMBIENT As Boolean = True

    ' ------------------------------------------------------------------------
    ' SH probe FIELD - probes/sh_grid/<hash>_sh_grid.dds
    '
    ' Where SH_AMBIENT above is ONE probe stretched over the whole map, this is
    ' a baked grid of them: an RGBA16F volume, 8 slices, one probe every 5 m
    ' (Abbey is 280x280 over a 1400 m box). Seven slices carry a probe's packed
    ' SH9; slice 6's alpha is that probe's reference height; slice 7 is padding.
    '
    ' FOLDED INTO THE REAL LIGHTING. deferred.frag evaluates the field and
    ' blends it over the flat global probe's irradiance by sh_grid_mix, inside
    ' the sh_grid_enabled branch. USE_SH_GRID_FX extends the same field to the
    ' FX volumetrics so smoke and the ground under it agree.
    ' ------------------------------------------------------------------------

    '''<summary>The volume texture, Nothing when the map ships no grid.</summary>
    Public SH_GRID_ID As GLTexture
    '''<summary>True once both WGSH and the volume have loaded for this map.</summary>
    Public SH_GRID_LOADED As Boolean
    '''<summary>Master switch for sampling the field at all.</summary>
    Public USE_SH_GRID As Boolean = True

    '''<summary>World box of the bake, from the space.bin WGSH section.</summary>
    Public SH_GRID_CENTRE As Vector3
    Public SH_GRID_SIZE As Vector3
    '''<summary>Metres over which the field fades out to the global probe.</summary>
    Public SH_GRID_FADE As Single = 15.0F
    '''<summary>Metres between probes - size.x / grid width. 5 m on most maps.</summary>
    Public SH_GRID_SPACING As Single = 5.0F
    '''<summary>True once the WGSH section has been read.</summary>
    Public WGSH_LOADED As Boolean

    ''' <summary>
    ''' The FIELD'S OWN fallback probe, from the rem_sh.xml sitting beside the
    ''' grid texture. Emphatically not probes/global/rem_sh.xml - on Abbey the
    ''' global one is about 1.8x brighter, and fading the field out to it put a
    ''' bright band across the top of every wall.
    ''' </summary>
    Public SH_GRID_SH9(8) As Vector3

    ''' <summary>
    ''' Metres to push the lookup along the surface normal. At 5 m spacing a
    ''' good number of probes sit INSIDE buildings and bake to near black; a
    ''' wall's surface lands on the boundary between those and the open-air
    ''' probes, so the tap mixes lit against black. Pushing the lookup outward
    ''' samples open air only. Moves the LOOKUP, never the height test.
    ''' </summary>
    Public SH_GRID_OFFSET As Single = 1.5F

    ''' <summary>
    ''' Light the FX volumetrics from the baked field as well as the ground.
    '''
    ''' DEFAULT OFF, and it is a look decision, not a bug fix. The field is
    ''' measurably darker than the flat global probe the FX uses today, so lit
    ''' smoke gets dimmer and shifts hue when this is on. Some of the smoke's
    ''' current visibility is the accident that the ground is already
    ''' grid-darkened and the smoke is not; this removes that. The smoke was
    ''' fought back from invisible once, which is why the owner turns this on,
    ''' not the code.
    '''
    ''' Only lit materials move - the ambient sits inside volumetric.vert's
    ''' g_enableAO gate, which additive fire usually authors False. On maps
    ''' that author additive AND lit together this DOES change fire.
    ''' </summary>
    Public USE_SH_GRID_FX As Boolean = False

    ''' <summary>
    ''' The FX pass's own normal push, SEPARATE from SH_GRID_OFFSET and zero by
    ''' default. The 1.5 m push exists to keep a wall's lookup out of the
    ''' near-black probes baked inside buildings - a job a smoke card floating
    ''' in open air does not have. Its normals are not a coherent billboard, so
    ''' a per-normal push scatters neighbouring vertices' lookups by up to a
    ''' whole probe cell and splits one column left to right. Kept as a
    ''' variable only so it can be A/B'd; 0 is the answer.
    ''' </summary>
    Public SH_GRID_OFFSET_FX As Single = 0.0F

    ''' <summary>
    ''' Bloom on the FX pass - the halo around fire.
    '''
    ''' Only possible now that the FX accumulate into a float target: the glow
    ''' is built from energy the old Rgba8 path had already flattened away.
    ''' Blending straight into an 8 bit buffer left nothing to glow with -
    ''' every hot pixel was white by the time the pass was done.
    ''' </summary>
    ''' <summary>
    ''' Debug view: render every model's VERTEX COLOUR stream as its albedo.
    '''
    ''' The stream is mostly unused - it defaults to white for any mesh that
    ''' ships no colour section - but it is load-bearing on the GFX volumetric
    ''' meshes, and it is the authored signal behind g_vertexColorMode. On the
    ''' D-Day trenches it marks a thin band of dark verts around the top rim at
    ''' ground level, which is the lip that dissolves into the terrain.
    '''
    ''' Driven by a #ifdef, not a uniform, so with the view off the shader
    ''' compiles to exactly the program it did before. Toggling recompiles, the
    ''' same way ModelPicker toggles PICK_MODELS.
    ''' </summary>
    Public SHOW_VERTEX_COLOURS As Boolean = False

    Public FX_GLOW As Boolean = True

    ' ----------------------------------------------------------------------
    ' Glow shape. HARD WIRED at the owner's call, after tuning them live
    ' against the fire on 19_monastery. Const, not variables: these are a
    ' settled look, and the sliders that set them are gone. Change them here
    ' and rebuild, or put the sliders back to re-tune.
    ' ----------------------------------------------------------------------

    ''' <summary>
    ''' How much of the blurred energy to add back. 1.0 would add it at the
    ''' strength it was emitted; 2.0 deliberately overdrives it, because the
    ''' halo is spread over far more pixels than the core it came from and at
    ''' unity it reads as a soft edge rather than as light coming off a fire.
    ''' </summary>
    Public Const FX_GLOW_STRENGTH As Single = 2.0F

    ''' <summary>
    ''' Where the bright pass starts keeping energy.
    '''
    ''' NOTE THIS IS BELOW 1.0, and that is a deliberate look choice, not an
    ''' oversight. 1.0 is the principled value - gFX_HDR holds the
    ''' premultiplied sum before composite_fx scales it back, so above 1.0 is
    ''' exactly the energy that used to clip against Rgba8, and glowing only
    ''' that is defensible from first principles.
    '''
    ''' 0.42 reaches below it, so SMOKE GLOWS TOO - and that is the point of
    ''' the exact value. It is a FLOOR found by eye, not a midpoint: under 0.42
    ''' the smoke starts glowing badly, and at 0.42 it is only lightly lit,
    ''' which is what was wanted. Lower it and the smoke will bloom; there is
    ''' no headroom below this number.
    '''
    ''' Any comment claiming the bright pass ignores smoke is describing the
    ''' 1.0 threshold, not this one.
    ''' </summary>
    Public Const FX_GLOW_THRESHOLD As Single = 0.42F

    ''' <summary>
    ''' How far the glow reaches, as a multiple of one blur texel.
    '''
    ''' Scales the STEP between the blur's taps. The kernel is a fixed 9 taps,
    ''' so widening this way costs nothing at all - but it also spreads those 9
    ''' taps thinner, and far enough out they stop overlapping and the halo can
    ''' show faint rings. FX_GLOW_PASSES is the cure for that, not a smaller
    ''' radius.
    '''
    ''' Taps land on texel centres at whole numbers and between them at
    ''' fractional ones, where the Linear filter averages two texels and hides
    ''' the gaps - which is why this is 2.7 and not 3.
    ''' </summary>
    Public Const FX_GLOW_RADIUS As Single = 2.7F

    ''' <summary>
    ''' How many horizontal+vertical blur pairs to run.
    '''
    ''' Convolving a Gaussian with itself N times widens it by sqrt(N), so this
    ''' is a much more expensive way to buy radius than FX_GLOW_RADIUS - but it
    ''' adds taps rather than spreading them, so it is what fills in a wide
    ''' radius that has started to band. Three pairs is six quarter-resolution
    ''' fullscreen draws, which is nothing.
    ''' </summary>
    Public Const FX_GLOW_PASSES As Integer = 3

    ''' <summary>
    ''' Replace the deferred pass with the probe field inspector - a separate
    ''' shader program, so nothing about this view can reach the real lighting.
    ''' </summary>
    Public SH_GRID_DEBUG As Boolean = False

    ''' <summary>
    ''' How far the ambient travels from the global probe toward the field.
    ''' 0 is the global probe alone, 1 is the field exactly, and above 1 keeps
    ''' going - exaggerating how far the field departs from the flat global
    ''' probe. Past 1 it is not physical, but this is a viewer.
    '''
    ''' FIXED at 0.5 - half way between the global probe and the field - by the
    ''' owner's call, not by derivation. It was 1.0, behind a "probe mix" slider
    ''' that has been removed, so this line is now the only thing that sets it:
    ''' nothing persists it and no UI moves it. Do not "restore" it to the field
    ''' value on the assumption that 1.0 is the correct one; 0.5 is the chosen
    ''' look. Put the slider back next to "normal offset m" in Window.vb if it
    ''' ever needs exploring again.
    ''' </summary>
    Public SH_GRID_MIX As Single = 0.5F

    ''' <summary>
    ''' Shape of the probe field's departure from the global probe, applied
    ''' before SH_GRID_MIX. Probes baked next to geometry are far darker than
    ''' open ones, so a straight mix drove contact shade to near black.
    '''
    ''' Both work on the RATIO field/global, so where the two agree nothing
    ''' moves. CURVE below 1 lifts the darks; FLOOR bounds how far under the
    ''' global probe the field may pull. The defaults are the identity, so the
    ''' frame is unchanged until one of them is moved.
    ''' </summary>
    ''' <summary>
    ''' Swap deferred.frag's specular for the game's model - GGX D, Schlick-
    ''' Gaussian Fresnel, Smith-Schlick visibility, and an env lookup indexed
    ''' (alphaRoughness, NdotV) instead of the current Phong lobe.
    '''
    ''' Off by default. The bar is that with it off the frame is bit-identical
    ''' to the one that shipped, which is checked by sha256.
    ''' </summary>
    Public PBR_SPEC As Boolean = False

    Public SH_GRID_CURVE As Single = 1.0F
    Public SH_GRID_FLOOR As Single = 0.0F

    '''<summary>Display gain for the inspector. Raw irradiance runs past 1.</summary>
    Public SH_GRID_EXPOSURE As Single = 0.25F

    '''<summary>Draw one line per probe, for counting cells against the world.</summary>
    Public SH_GRID_SHOW_LATTICE As Boolean = True

    Public DECAL_EDGE_FADE As Boolean = True
    Public DONT_BLOCK_MODELS As Boolean = False
    ''' <summary>
    ''' Draw the FX pass at all - the volumetric fire/smoke meshes AND the
    ''' particle cards, which modRender brackets together as one pass. Named for
    ''' the DONT_BLOCK_* convention the rest of Section Visibility uses.
    '''
    ''' Skipping the pass is safe for the state downstream inherits: draw_fx
    ''' already returns early when nothing is in the frustum, so the base rings
    ''' and the minimap cannot have been relying on the state it leaves.
    ''' </summary>
    Public DONT_BLOCK_FX As Boolean = True
    Public DONT_BLOCK_BASES As Boolean
    Public DONT_BLOCK_SKY As Boolean
    Public DONT_BLOCK_WATER As Boolean
    '---------------------
    Public WATER_LINE As Single
    '---------------------
    Public TEAM_1 As Vector3
    Public TEAM_2 As Vector3
    Public MAP_BB_UR As Vector2
    Public MAP_BB_BL As Vector2

End Module

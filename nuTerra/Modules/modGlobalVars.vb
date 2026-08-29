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
    ' LOADED AND UPLOADED, BUT NOT YET USED BY THE LIGHTING. deferred.frag
    ' samples it into a local and deliberately does not fold it into the ambient
    ' term - that integration is a separate, deliberate step.
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
    ''' Replace the deferred pass with the probe field inspector - a separate
    ''' shader program, so nothing about this view can reach the real lighting.
    ''' </summary>
    Public SH_GRID_DEBUG As Boolean = False

    '''<summary>Display gain for the inspector. Raw irradiance runs past 1.</summary>
    Public SH_GRID_EXPOSURE As Single = 0.25F

    '''<summary>Draw one line per probe, for counting cells against the world.</summary>
    Public SH_GRID_SHOW_LATTICE As Boolean = True

    Public DECAL_EDGE_FADE As Boolean = True
    Public DONT_BLOCK_MODELS As Boolean = False
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

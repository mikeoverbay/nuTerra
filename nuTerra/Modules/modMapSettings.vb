Imports System.IO
Imports System.Globalization
Imports System.Linq

''' <summary>
''' Per-map render settings, saved as one plain text file per space.
'''
''' Files live in a MapSettings folder next to the exe, and the same folder in
''' the project is copied there on build. So the working copy the app writes is
''' bin\Debug\net6.0-windows\MapSettings, and copying a file from there back into
''' nuTerra\MapSettings is what makes it permanent and puts it under git.
'''
''' Format is key=value, one per line, '#' comments, order irrelevant. An unknown
''' key is ignored and a missing key keeps whatever the map's environment.xml or
''' the global defaults already set, so a partial file is valid - you can save a
''' full one and then cut it down to only the lines that matter for that map.
''' </summary>
Public Module modMapSettings

    Private ReadOnly INV As IFormatProvider = CultureInfo.InvariantCulture

    '''<summary>What the last Save or Load did, shown in the settings panel.</summary>
    Public LAST_RESULT As String = ""

    ''' <summary>
    ''' The user's working copies, in the app's scratch area. Seeded from the
    ''' shipped defaults on startup, then read and written from here - so a user
    ''' can tune a map without touching what was installed, and deleting the
    ''' folder resets every map back to the shipped baseline.
    ''' </summary>
    Public ReadOnly Property WorkFolderPath As String
        Get
            Return Path.Combine(If(TEMP_STORAGE, Path.GetTempPath()), "MapSettings")
        End Get
    End Property

    ''' <summary>
    ''' Copies any shipped map settings file that is not already in the work
    ''' folder. Existing files are never overwritten, so a user's tuning survives
    ''' an update; a file they delete comes back as the shipped default.
    ''' </summary>
    Public Sub SeedWorkFolder()
        Try
            If Not Directory.Exists(ShippedFolderPath) Then
                LogThis("No shipped MapSettings at {0} - nothing to seed", ShippedFolderPath)
                Return
            End If

            Directory.CreateDirectory(WorkFolderPath)

            Dim copied = 0, kept = 0
            For Each src In Directory.GetFiles(ShippedFolderPath, "*.txt")
                Dim dst = Path.Combine(WorkFolderPath, Path.GetFileName(src))
                If File.Exists(dst) Then
                    kept += 1
                Else
                    File.Copy(src, dst)
                    copied += 1
                End If
            Next

            LogThis("Map settings: seeded {0}, kept {1} existing, in {2}", copied, kept, WorkFolderPath)
        Catch ex As Exception
            LogThis("Could not seed map settings: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Where the kept copies live: the MapSettings folder shipped next to the
    ''' exe, filled from nuTerra\MapSettings on build.
    ''' </summary>
    Public ReadOnly Property ShippedFolderPath As String
        Get
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapSettings")
        End Get
    End Property

    Public Function SaveFilePathFor(map_name As String) As String
        Return Path.Combine(WorkFolderPath, SafeName(map_name) & ".txt")
    End Function

    ''' <summary>
    ''' The file Load will actually read: the work copy wins so tuning in progress
    ''' is picked up, otherwise the shipped one. Nothing means neither exists.
    ''' </summary>
    Public Function LoadFilePathFor(map_name As String) As String
        Dim work = SaveFilePathFor(map_name)
        If File.Exists(work) Then Return work
        Dim shipped = Path.Combine(ShippedFolderPath, SafeName(map_name) & ".txt")
        If File.Exists(shipped) Then Return shipped
        Return Nothing
    End Function

    Private Function SafeName(map_name As String) As String
        Dim n = Path.GetFileNameWithoutExtension(If(map_name, ""))
        For Each c In Path.GetInvalidFileNameChars()
            n = n.Replace(c, "_"c)
        Next
        Return n
    End Function

    ''' <summary>Every value the UI can set, as name/get/set triples.</summary>
    Private Iterator Function Fields() As IEnumerable(Of (Name As String, Reader As Func(Of Single), Writer As Action(Of Single)))
        Yield ("ambient", Function() CommonProperties.AMBIENT, Sub(v) CommonProperties.AMBIENT = v)
        Yield ("ambient_sat", Function() CommonProperties.AMBIENT_SAT, Sub(v) CommonProperties.AMBIENT_SAT = v)
        Yield ("sun_tint", Function() CommonProperties.SUN_TINT, Sub(v) CommonProperties.SUN_TINT = v)
        Yield ("sun_strength", Function() CommonProperties.SUN_STRENGTH, Sub(v) CommonProperties.SUN_STRENGTH = v)
        Yield ("tonemap_exposure", Function() CommonProperties.TONEMAP_EXPOSURE, Sub(v) CommonProperties.TONEMAP_EXPOSURE = v)
        Yield ("brightness", Function() CommonProperties.BRIGHTNESS, Sub(v) CommonProperties.BRIGHTNESS = v)
        Yield ("specular", Function() CommonProperties.SPECULAR, Sub(v) CommonProperties.SPECULAR = v)
        Yield ("gray_level", Function() CommonProperties.GRAY_LEVEL, Sub(v) CommonProperties.GRAY_LEVEL = v)
        Yield ("gamma_level", Function() CommonProperties.GAMMA_LEVEL, Sub(v) CommonProperties.GAMMA_LEVEL = v)
        Yield ("fog_level", Function() CommonProperties.FOG_LEVEL, Sub(v) CommonProperties.FOG_LEVEL = v)
        Yield ("tess_level", Function() CommonProperties.tess_level, Sub(v) CommonProperties.tess_level = v)
        Yield ("blend_height", Function() CommonProperties.BLEND_HEIGHT, Sub(v) CommonProperties.BLEND_HEIGHT = v)
        Yield ("height_contrast", Function() CommonProperties.HEIGHT_CONTRAST, Sub(v) CommonProperties.HEIGHT_CONTRAST = v)
        Yield ("macro_fade", Function() CommonProperties.MACRO_FADE, Sub(v) CommonProperties.MACRO_FADE = v)
        Yield ("horizon_strength", Function() CommonProperties.HORIZON_STRENGTH, Sub(v) CommonProperties.HORIZON_STRENGTH = v)
        Yield ("shadow_strength", Function() CommonProperties.SHADOW_STRENGTH, Sub(v) CommonProperties.SHADOW_STRENGTH = v)
        Yield ("water_y_offset", Function() WATER_Y_OFFSET, Sub(v) WATER_Y_OFFSET = v)
        Yield ("water_exclude_band", Function() WATER_EXCLUDE_BAND, Sub(v) WATER_EXCLUDE_BAND = v)
        Yield ("water_fog_mul", Function() WATER_FOG_MUL, Sub(v) WATER_FOG_MUL = v)
        Yield ("mouse_damp", Function() ROT_DAMPING, Sub(v) ROT_DAMPING = v)

        ' Outland look. The tint/base pair feeds the albedo bake, which runs
        ' after settings are applied at load, so no re-bake hook is needed.
        Yield ("outland_global_base", Function() OUTLAND_GLOBAL_BASE, Sub(v) OUTLAND_GLOBAL_BASE = v)
        Yield ("outland_detail_tiles", Function() OUTLAND_DETAIL_TILES, Sub(v) OUTLAND_DETAIL_TILES = v)
        Yield ("outland_spec", Function() OUTLAND_SPEC, Sub(v) OUTLAND_SPEC = v)

        ' Shadow filtering and wet reflections.
        Yield ("msm_moment_bias", Function() MSM_MOMENT_BIAS, Sub(v) MSM_MOMENT_BIAS = v)
        Yield ("water_depth", Function() WATER_DEPTH, Sub(v) WATER_DEPTH = v)
        Yield ("water_mask_wet", Function() B2F(WATER_MASK_WET), Sub(v) WATER_MASK_WET = F2B(v))
        Yield ("water_mask_min", Function() WATER_MASK_MIN, Sub(v) WATER_MASK_MIN = v)
        Yield ("ssr_intensity", Function() SSR_INTENSITY, Sub(v) SSR_INTENSITY = v)
        Yield ("ssr_steps", Function() CSng(SSR_STEPS), Sub(v) SSR_STEPS = CInt(v))
        Yield ("ssr_thickness", Function() SSR_THICKNESS, Sub(v) SSR_THICKNESS = v)
        Yield ("ssr_stride", Function() SSR_STRIDE, Sub(v) SSR_STRIDE = v)
        Yield ("penumbra_lo", Function() SHADOW_PENUMBRA_LO, Sub(v) SHADOW_PENUMBRA_LO = v)
        Yield ("penumbra_hi", Function() SHADOW_PENUMBRA_HI, Sub(v) SHADOW_PENUMBRA_HI = v)

        Yield ("minimap_size", Function() CSng(MINI_MAP_NEW_SIZE), Sub(v) MINI_MAP_NEW_SIZE = CInt(v))

        ' The probe field's two look controls. Neither was persisted, so every
        ' launch reset the mix to 0.5 and the offset to its default - a tuning
        ' pass could not survive being closed.
        Yield ("sh_grid_mix", Function() SH_GRID_MIX, Sub(v) SH_GRID_MIX = v)
        Yield ("sh_grid_offset", Function() SH_GRID_OFFSET, Sub(v) SH_GRID_OFFSET = v)
        Yield ("sh_grid_curve", Function() SH_GRID_CURVE, Sub(v) SH_GRID_CURVE = v)
        Yield ("sh_grid_floor", Function() SH_GRID_FLOOR, Sub(v) SH_GRID_FLOOR = v)
        Yield ("pbr_spec", Function() B2F(PBR_SPEC), Sub(v) PBR_SPEC = F2B(v))

        ' booleans, stored as 0/1 so the file stays one shape throughout
        Yield ("use_sh_ambient", Function() B2F(USE_SH_AMBIENT), Sub(v) USE_SH_AMBIENT = F2B(v))
        Yield ("decal_edge_fade", Function() B2F(DECAL_EDGE_FADE), Sub(v) DECAL_EDGE_FADE = F2B(v))
        Yield ("draw_decals", Function() B2F(DONT_BLOCK_DECALS), Sub(v) DONT_BLOCK_DECALS = F2B(v))
        Yield ("draw_trees", Function() B2F(DONT_BLOCK_TREES), Sub(v) DONT_BLOCK_TREES = F2B(v))
        Yield ("draw_water", Function() B2F(DONT_BLOCK_WATER), Sub(v) DONT_BLOCK_WATER = F2B(v))
        Yield ("draw_bases", Function() B2F(DONT_BLOCK_BASES), Sub(v) DONT_BLOCK_BASES = F2B(v))
        Yield ("draw_models", Function() B2F(DONT_BLOCK_MODELS), Sub(v) DONT_BLOCK_MODELS = F2B(v))
        Yield ("draw_sky", Function() B2F(DONT_BLOCK_SKY), Sub(v) DONT_BLOCK_SKY = F2B(v))
        Yield ("draw_terrain", Function() B2F(DONT_BLOCK_TERRAIN), Sub(v) DONT_BLOCK_TERRAIN = F2B(v))
        Yield ("draw_outland", Function() B2F(DONT_BLOCK_OUTLAND), Sub(v) DONT_BLOCK_OUTLAND = F2B(v))
        Yield ("outland_global_tint", Function() B2F(OUTLAND_GLOBAL_TINT), Sub(v) OUTLAND_GLOBAL_TINT = F2B(v))
        Yield ("outland_use_detail", Function() B2F(OUTLAND_USE_DETAIL), Sub(v) OUTLAND_USE_DETAIL = F2B(v))
        Yield ("outland_pbr_nm", Function() B2F(OUTLAND_PBR_NM), Sub(v) OUTLAND_PBR_NM = F2B(v))
        Yield ("use_raster_culling", Function() B2F(USE_RASTER_CULLING), Sub(v) USE_RASTER_CULLING = F2B(v))
        Yield ("use_tessellation", Function() B2F(USE_TESSELLATION), Sub(v) USE_TESSELLATION = F2B(v))
        Yield ("msm_shadow", Function() B2F(MSM_SHADOW_ENABLED), Sub(v) MSM_SHADOW_ENABLED = F2B(v))
        Yield ("ssr_enabled", Function() B2F(SSR_ENABLED), Sub(v) SSR_ENABLED = F2B(v))
        Yield ("draw_minimap", Function() B2F(DONT_HIDE_MINIMAP), Sub(v) DONT_HIDE_MINIMAP = F2B(v))
        Yield ("fxaa", Function() B2F(FXAA_enable), Sub(v) FXAA_enable = F2B(v))
        ' shadow_mapping is deliberately NOT here any more. The live cascades
        ' are parked - no UI, off at startup (CommonProperties.Init) - but old
        ' per-map files still carry shadow_mapping=1 from when they had a
        ' checkbox, and restoring it silently re-enabled the whole pass with
        ' nothing on screen saying so. The cascades refit to the camera every
        ' FRAME_STEP frames, and that periodic refit pulsing the far-field
        ' shading during camera glides was the "settling-in lighting flicker".
        ' Load() logs old files' stray key as unknown and skips it; re-add the
        ' Yield here when the cascade controls come back.
        Yield ("baked_shadow", Function() B2F(BAKED_SHADOW_ENABLED), Sub(v) BAKED_SHADOW_ENABLED = F2B(v))
    End Function

    Private Function B2F(b As Boolean) As Single
        Return If(b, 1.0F, 0.0F)
    End Function

    Private Function F2B(v As Single) As Boolean
        Return v <> 0.0F
    End Function

    ' Values as of the last load or save for this map. Compared as the strings
    ' that would be written, so "changed" means the file would actually differ -
    ' float noise below the written precision does not count.
    Private baseline As Dictionary(Of String, String)
    Private baseline_map As String = ""

    ''' <summary>
    ''' The global defaults, captured once at startup after CommonProperties.Init
    ''' and the DONT_BLOCK flags have run, before any map is opened.
    ''' </summary>
    Private defaults As Dictionary(Of String, Single)

    ''' <summary>
    ''' Records the startup values as the fallback every map load resets to.
    ''' Call once, after the global defaults are established and before the first
    ''' map is loaded.
    ''' </summary>
    Public Sub CaptureDefaults()
        defaults = Fields().ToDictionary(Function(f) f.Name, Function(f) f.Reader())
        LogThis("Map settings: captured {0} global defaults", defaults.Count)
    End Sub

    ''' <summary>
    ''' Puts every setting back to its startup default.
    '''
    ''' This exists because Load only applies the keys a file actually contains,
    ''' and these values live in module state that outlives a map. Without a reset
    ''' an absent key did not mean "use the default" - it meant "keep whatever the
    ''' last map set", so opening a tuned map and then an untuned one silently
    ''' carried the first map's lighting across. The visible symptom was that
    ''' adding any new setting appeared to require appending it to all 65 files,
    ''' when the real problem was that a missing key never fell back.
    '''
    ''' Call at the very top of load_map, before get_environment_info: map data
    ''' from environment.xml and space.bin is applied after this and still wins,
    ''' so a map that authors its own blend_height or fog keeps it. The saved file
    ''' is applied last of all and overrides both.
    ''' </summary>
    Public Sub ResetToDefaults()
        If defaults Is Nothing Then Return

        For Each f In Fields()
            Dim v As Single
            If defaults.TryGetValue(f.Name, v) Then
                f.Writer.Invoke(v)
            End If
        Next
    End Sub

    Private Function CurrentValues() As Dictionary(Of String, String)
        Return Fields().ToDictionary(Function(f) f.Name,
                                     Function(f) f.Reader().ToString("0.######", INV))
    End Function

    ''' <summary>
    ''' Records the current values as the point of comparison for this map. Call
    ''' after loading a map, whether or not a saved file existed - with no file
    ''' the baseline is the defaults, so tuning away from them still counts.
    ''' </summary>
    Public Sub Snapshot(map_name As String)
        baseline_map = SafeName(map_name)
        baseline = CurrentValues()
    End Sub

    ''' <summary>True when a setting differs from the last load or save.</summary>
    Public Function HasChanged(map_name As String) As Boolean
        If baseline Is Nothing Then Return False
        If baseline_map <> SafeName(map_name) Then Return False

        For Each kv In CurrentValues()
            Dim was As String = Nothing
            If Not baseline.TryGetValue(kv.Key, was) Then Return True
            If was <> kv.Value Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' Writes the file only if something actually moved since the map was
    ''' loaded. Called on shutdown so a tuning session is not lost by forgetting
    ''' the button, without rewriting an untouched map every time it is opened.
    ''' </summary>
    Public Function SaveIfChanged(map_name As String) As Boolean
        If String.IsNullOrEmpty(map_name) Then Return False
        If Not HasChanged(map_name) Then
            LogThis("Map settings for {0} unchanged - nothing written", SafeName(map_name))
            Return False
        End If
        LogThis("Map settings for {0} changed since load - saving on exit", SafeName(map_name))
        Return Save(map_name)
    End Function

    ''' <summary>
    ''' Writes the current settings for this map. Overwrites any existing file.
    ''' </summary>
    Public Function Save(map_name As String) As Boolean
        If String.IsNullOrEmpty(map_name) Then Return False
        Try
            Directory.CreateDirectory(WorkFolderPath)
            Dim path = SaveFilePathFor(map_name)

            Using w As New StreamWriter(path, False)
                w.WriteLine("# nuTerra render settings for {0}", SafeName(map_name))
                w.WriteLine("# Written {0}. Copy this into nuTerra\MapSettings to keep it.", Date.Now.ToString("s"))
                w.WriteLine("# Delete any line to fall back to that setting's default for this map.")
                w.WriteLine()
                For Each f In Fields()
                    w.WriteLine("{0}={1}", f.Name, f.Reader().ToString("0.######", INV))
                Next
            End Using

            Snapshot(map_name)
            LAST_RESULT = "Saved to " & path
            LogThis("Saved map settings to {0}", path)
            Return True
        Catch ex As Exception
            LAST_RESULT = "SAVE FAILED: " & ex.Message
            LogThis("Could not save map settings for {0}: {1}", map_name, ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Applies the saved settings for this map if a file exists. Returns False
    ''' when there is nothing saved, which is not an error - the map just keeps
    ''' the values its environment.xml and the global defaults gave it.
    ''' </summary>
    Public Function Load(map_name As String) As Boolean
        If String.IsNullOrEmpty(map_name) Then Return False
        Dim path = LoadFilePathFor(map_name)
        If path Is Nothing Then
            LogThis("No saved settings for {0} - using defaults", SafeName(map_name))
            Return False
        End If
        LogThis("Reading map settings from {0}", path)

        Try
            Dim setters = Fields().ToDictionary(Function(f) f.Name, Function(f) f.Writer)
            Dim applied = 0

            For Each raw In File.ReadAllLines(path)
                Dim line = raw.Trim()
                If line.Length = 0 OrElse line.StartsWith("#") Then Continue For

                Dim eq = line.IndexOf("="c)
                If eq <= 0 Then Continue For

                Dim key = line.Substring(0, eq).Trim().ToLowerInvariant()
                Dim text = line.Substring(eq + 1).Trim()

                Dim setter As Action(Of Single) = Nothing
                If Not setters.TryGetValue(key, setter) Then
                    LogThis("  map settings: ignoring unknown key '{0}'", key)
                    Continue For
                End If

                Dim value As Single
                If Not Single.TryParse(text, NumberStyles.Float, INV, value) Then
                    LogThis("  map settings: '{0}' is not a number for key '{1}'", text, key)
                    Continue For
                End If

                setter(value)
                applied += 1
            Next

            LogThis("Applied {0} saved settings for {1}", applied, SafeName(map_name))
            LAST_RESULT = String.Format("Loaded {0} settings from {1}", applied, path)
            Return True
        Catch ex As Exception
            LogThis("Could not read map settings for {0}: {1}", map_name, ex.Message)
            Return False
        End Try
    End Function

End Module

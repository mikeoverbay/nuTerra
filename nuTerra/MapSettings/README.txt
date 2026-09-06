Per-map render settings.

One file per space, named after the space folder - so Abbey is 19_monastery.txt.
Everything in here is copied next to the exe on build, and the running app reads
from that copy.

Workflow
  1. Load the map and tune it with the sliders in Settings.
  2. Settings -> Map Settings -> "Save settings for this map".
     That writes to bin\Debug\net6.0-windows\MapSettings\<space>.txt - the
     working copy, which a rebuild will overwrite.
  3. Copy that file back into this folder to keep it and put it under git.

Format is key=value, one per line. '#' starts a comment and blank lines are
fine. Order does not matter.

A missing key is not an error - that setting simply keeps whatever the map's
environment.xml or the global defaults gave it. So a file can be trimmed down to
only the lines that actually differ for that map, which makes the diffs readable
and lets one map's file serve as a template for the next.

An unrecognised key is ignored and logged, so old files stay loadable after a
setting is renamed or dropped.
baked_shadow=1
horizon_strength=0.7

# fog, shafts, lamp shadows, probe field, FX - added 2026-09-06 at their defaults
fog_density=0.012
fog_height=40
fog_floor=0
fog_noise=0.15
fog_sky=0.6
fog_noise_m=350
fog_speed=0.03
fog_tint_r=-1
fog_tint_g=-1
fog_tint_b=-1
lamp_shafts=1
shaft_gain=0.45
shaft_density=0.07
shaft_phase=0.55
shaft_iso=0.35
shaft_steps=48
light_gain=8
lamp_shadows=1
lamp_shadow_bias=0.0015
lamp_shadow_nbias=0.06
lamp_shadow_soft=1
light_falloff=12
use_sh_grid=1
sh_grid_fx=0
sh_grid_offset_fx=0
fx_glow=1
draw_fx=1
boxes_volumetric_only=1

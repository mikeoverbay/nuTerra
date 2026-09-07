# The Light Bulb Placer and the campath bulb table

A BULB is a light attached to a MODEL: a bulb in a street lamp's hood, a flame
on a brazier. It is placed once, in the model's own space, and nuTerra puts one
light at every instance of that model on the map. The lamps Path Studio places
on the 2D map are a different thing - those are placed on the terrain - and both
end up in the same lamp path: the surface lighting, the shafts and the overlay.

Everything lives in the map's `.campath`. `tools/cam_path.py` documents the
record ("Bulb record"); `MapCamPath.vb` reads it and `SaveBulbs` rewrites only
that block. Path Studio never edits bulbs but always carries them through - its
`copy_with_lights` keeps the destination's bulb block on a route regenerate or a
light save. A file from before bulbs has zeros in the header and reads as none.

## The tool (`BulbPlacer.vb`)

Menu button **Light Bulb Placer**, or `placer=1` on the command line to open it
as soon as the map is up. It waits for the load to finish.

- **Left**: the fire and lamp models on this map, `[kind] name xN`. `*` marks a
  model that already has bulbs. Collected by `CollectLightModels` from inside
  `MapLoader`, because `MAP_MODELS` is erased at the end of the load and the
  names would be gone by the time a panel asked. Classification is the
  catalogue scan's: material first (all-volumetric = a GFX fire, glow = a lit
  glass), name second, with firewood, fire stairs and hydrants named out.
- **Centre**: one 3D view of the model on a GL surface sized to the pane. Left
  mouse orbits, wheel zooms, **Top / Front / Side** snap to exact orthographic
  views (orbiting returns to perspective), **Frame** recentres. The bulb is a
  crosshair: **right-drag** moves it in the horizontal plane, **Shift +
  right-drag** moves it up and down; in an ortho view a drag moves along the
  view's own axes and a pixel is a fixed number of metres. The range is a wire
  sphere in the light's colour, a cone is a wire cone to the aim point, an
  inverse cone the same in blue - it is the DARK part - and a dual cowl is TWO
  wire rims in green, one per cut, because what it lights is the band between
  them.
- **Right**: the lights on this model. Add, Duplicate, Remove. For the selected
  one: type as four **radio buttons**, which marker the right mouse moves (bulb
  or aim point), position and aim as numbers, the **two half angles**, edge
  blend, colour, level, range, fog mix, shaft curve. **Save to campath** writes
  this model's lights into the table; other models' bulbs, the route and the
  map lights are copied through.

The view is STANDALONE: the model is read again from the pkg by its own small
loader - position and normal, 24 bytes a vertex - and drawn by
`Developer/lampview` and `Developer/lampcursor`. The map's models are untouched.
The GL work runs from `OnRenderFrame` before the UI pass, never from inside the
panel (`docs/ui_panels.md`, "Keep GL out of the UI pass").

## From bulbs to lights (`MapCamPath.ExpandBulbs`)

After the file is read, and again after every save:

```
lights() = path_lights()                       the file's map lights, in order
         + for every bulb, for every instance of its model on this map:
               pos = bulb.pos transformed by the instance matrix
               dir = normalize(aim - pos), transformed the same way
               absolute = True
```

The instance transforms come from `MODEL_BATCH_LIST` and `MODEL_INDEX_LIST`,
which outlive the load. `world_pos(i)` is the ONE place a light's height is
resolved: metres above the terrain for a map light, already absolute for a bulb.
The surface lighting, the shadow bake, the shafts and the overlay all call it.

**The slot cap, sized from the driver.** `modRender.init_light_slots` runs once
at startup, asks for `GL_MAX_FRAGMENT_UNIFORM_VECTORS`, keeps 512 vec4 back for
the rest of `deferred.frag`, divides the remainder by 4 (that is how many vec4
arrays a light costs) and clamps to 32..256. The number is injected into the
shader as `MAX_PATH_LIGHTS` and the CPU arrays are sized to match - **they must
agree or the upload writes past what the shader declared**. It is logged:

```
path lights: 128 slot(s) - driver has 1024 fragment vec4s, 512 reserved, capped at 256
```

An unused slot costs uniform space and nothing per frame: the per-pixel loop
runs to `light_count`, which is what was actually uploaded. 32 was the old
fixed budget and monastery alone wants 54 - one bulb on a model with 47
instances is 47 lights - so lamps switched on and off as the camera moved.

`visible_lights(cam, MAX_PATH_LIGHTS)` returns the map lights first, in file
order, then the bulb lights nearest the camera until the slots are full;
`modRender.upload_path_lights` and `MapLampFog` upload exactly that set in that
order every frame, **and must be given the same cap** or a lamp lights the
ground with no shaft above it.

The sort is by distance from the camera alone - no view direction - so with
more lights than slots, a lamp behind you can still take a slot from one on
screen. Culling candidates against the frustum by their light sphere first
would fix that; not done, and moot while everything fits.

**Shadow cubes are baked for the map lights only** (`MapLampShadow.Bake` uses
`path_light_count`), so layer i is light i in every consumer; bulb lights are
lit unshadowed. Cubes for the nearest bulbs would need a rebake whenever the
set changes - not done.

## The four shapes

Every bulb carries TWO half angles from its axis, `ang0` and `ang1`, and each
kind reads them differently:

| kind | lit where | ang0 | ang1 |
|---|---|---|---|
| 0 point | everywhere | - | - |
| 1 cone | inside ang1 | inner hot edge | outer edge |
| 2 inverse cone | outside ang0 | dark edge | soft-out edge |
| 3 dual cowl | BETWEEN ang0 and ang1 | the cap cut | the base cut |

The **dual cowl** is two inverse lobes on ONE shared axis, so what it lights is
a toroidal band. It is the shape of a lamp on a vertical post: the cap swallows
the light going up, the post blocks it going down, and what escapes is a ring.
Band width is `ang1 - ang0`, and `blend` softens both of its edges.

`ang0 = ang1 = 0` means "derive the shape from `cone` and `blend`" - what every
bulb authored before these fields carries, and what a Path Studio map light
always carries. Those render exactly as they did.

**`MapCamPath.cone_cosines` is the one place that resolves the pair**, on the
CPU, for both upload paths. `deferred.frag` `lamp_cone_mask` and
`lamp_fog.frag` `cone_mask` are the same function on a surface and in the air,
and a shaft has the shape of the light that casts it - two copies of this
arithmetic would drift and the beam would stop matching its own pool.

```
c       = dot(from_lamp, dir)                 1 on the axis
cos_in  = cos(ang0)   cos_out = cos(ang1)     cos_in is the LARGER
m       = smoothstep(cos_out, cos_in, c)
cap     = smoothstep(cos_in,  mix(cos_in,  1, blend), c)
base    = smoothstep(cos_out, mix(cos_out, 1, blend), c)
mask    = point: 1 | cone: m | inverse: 1 - m | dual cowl: (1 - cap) * base
```

Multiplied into the lamp's visibility before the shadow, so a shadowed cone
stays a cone. `vol_mix` scales what a lamp scatters into fog; a map light has 1.

## Uniforms

`deferred.frag`: `pl_dir_cos[32]` (xyz aim, **w cos of the OUTER half angle**),
`pl_kind_blend[32]` (x kind, y blend, z vol_mix, **w cos of the INNER half
angle**). The second angle needed no new uniform array: that `w` was uploaded
as a hard 0 and never read. `lamp_fog.frag`: `lamp_dir`, `lamp_cos_out`,
`lamp_cos_in`, `lamp_kind`, `lamp_blend`, `lamp_vol_mix`.

## Growing the bulb record

The record went 224 -> 232 bytes for `ang0` / `ang1`, and that is the part to
be careful with. **Three readers had to be made stride-tolerant first**, or an
existing file would have been destroyed rather than upgraded:

- `MapCamPath.vb` guarded on `bulb_stride < BULB_STRIDE` and `Return`ed - which
  abandons the WHOLE campath, route and map lights included, not just the
  bulbs. It now guards on `BULB_STRIDE_MIN` (224) and reads the two angles only
  when the stride covers them.
- `cam_path.py` `_bulb_block` refused an older stride and reported "no bulbs",
  so a Path Studio route regenerate would have **silently wiped placed bulbs**.
  It now accepts anything from `BULB_STRIDE_MIN` up.
- `copy_with_lights` stamped the CURRENT stride onto a block copied at the
  file's own stride. It now carries the source stride through, so 224-byte
  records are never described as 232.

The precedent is the light record's `curve` field: the reader goes by the
stride the FILE declares, never by the size this build happens to write, and
the VB constant is the minimum accepted rather than what the writer emits. Any
future field follows the same rule.

## Not done

- Shadow cubes for bulb lights (above).
- A bulb whose model is not on the loaded map matches nothing and is kept in
  the file untouched; the log says how many.
- `light_catalogue.xml` and `map_lights.xml` are the earlier, guessed,
  per-model table and the scan of every map. Nothing reads them; the bulb table
  is the thing that lights lamps now.

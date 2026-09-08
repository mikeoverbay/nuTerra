# Handoff — the shading pass that was built, measured, and reverted

2026-09-08, evening, on `master` at `cb0ee1c8`. Written by Fable for Opus; the
owner was out of credit. Follows `HANDOFF_2026-09-08_bloom_and_bulbs.md`.

**The tree is clean at `cb0ee1c8`.** Everything in section 2 was built,
measured and running, then reverted on the owner's instruction ("revert all
the shader work. go back to last commit"). The whole diff is kept at
**`docs/patches/shading_pass_2026-09-08.patch`** (32 KB, `git apply` from the
repo root). The reason for the revert was not stated. **Ask before re-applying
any of it.**

Claims are marked. **VERIFIED** means measured or read off bytes. **REASONED**
means derived and not seen.

---

## 1. What is committed (the earlier part of the day)

Eight commits, `9e200e0e` .. `cb0ee1c8`: texture views, DX10/BC loaders, the
street-lamp panes and bulb sprite, lamp fog steps, the FX glow box-average,
the output dither, the smoke-card lighting, and the docs. See the banner at
the top of the bloom-and-bulbs handoff. The smoke cards were judged "better
and done" by the owner and are committed. Not pushed; the owner pushes.

## 2. What the patch contains, in the order it was built

All in `deferred.frag` plus the four VB files that carry a slider
(`modGlobalVars`, `modRender`, `modMapSettings`, `Window`), plus `lighting.md`
and `open_threads.md`. All of it behind switches; the legacy path
(`pbr_spec = 0`) untouched throughout.

1. **Environment specular connected.** `pmrem_decode()` (`rgb² · 2^(9a) / 8`)
   on the cube sample in the PBR block, direction through `invView` with no
   handedness flip (the pooled-water convention), `specAmbient` added after
   the sun lobe, occluded by the local/global probe luminance ratio and the
   model AO, faded under standing water, scaled by `props.AMBIENT`, gain
   slider **Env specular** (`ENV_SPEC`, `env_spec`).
2. **Metal energy conservation** on the sun diffuse, `1 - min(metal²·3.2, 1)`,
   PBR path, models only.
3. **The Tank Exporter gloss/metal curves**: `gloss = pow(R/0.8, 7)` floored
   at 0.02, `metal = min(pow(G/0.5, 5)·1.5, 1)`, read once into
   `pbr_gloss / pbr_metal` and shared by 2 and the specular block. Checkbox
   **GMM curves (Tank Exporter)** (`GMM_CURVE`, `gmm_curve`). Reference the
   owner pointed at: `C:\experiment\experiment\relaxed-bartik-61760d\shaders\mesh.frag`
   — "the only thing to take from this is how it curves the rough and metal
   channels."
4. **Fresnel slider** (`PBR_FRESNEL`, `pbr_fresnel`) scaling both grazing
   rises: the LUT bias in the env term and the Schlick-Gaussian `Fexp` in the
   sun lobe.
5. **Wet Spec Level** (`WET_SPECULAR`, `wet_specular`, seeded 0.024): the
   water highlight `water_spec` and the wet reflection's Phong scalar move
   off `props.SPECULAR`, which keeps the solid-surface lobe only.

The owner's reactions, in order: env specular "close"; curves "that is
better!"; then, against a game frame of the monastery milk cans, "this one
looks more like aluminum ... too much Fresnel going on"; then "drop the
specular down on water and wetness, add a control for just that; yours works
better but needs more specular"; then the revert.

## 3. What was learned — worth keeping whatever happens to the code

* **VERIFIED: the cube nuTerra loads is the game's `probes/global/pmrem.dds`**
  (`TerrainBuilder.vb:553`), DXT5, 6 faces, 8 mips, HDR exponent in alpha:
  the monastery one spans alpha 25..255 over 231 values, decoded mean 2.8
  against 0.64 read as sRGB. `lighting.md` §4 still says the cube "is not
  PMREM-encoded" — that premise is wrong, and the patch's `lighting.md`
  rewrite of §4 says so. **The doc correction is worth re-landing even if the
  code is not.**
* **VERIFIED: the BRDF LUT axes are right.** `system/maps/env_brdf_lut.dds`
  in `misc.pkg` is 128² A16B16G16R16F (PIL cannot open it; decode with numpy
  float16 after the 128-byte header). Bias peaks at smooth-and-grazing top
  left, the top row is grazing, the loader uploads rows unflipped, so
  `vec2(alphaR, NdotV)` is correct. Its R channel is 1.0 along the whole
  smooth column, so it is not the textbook A/B split; the transcript's
  `specTint * brdf.x + brdf.y` with no metal made every matte wall a mirror.
  The patch uses `specTint * metal * brdf.x + brdf.y` — REASONED from the sun
  term's F0, not read off the disassembly.
* **VERIFIED: connected raw, the frame went +116 levels mean** (cobbles 199).
  With the AMBIENT scale and the metal F0 it was +12 mean: cobbles +17..20,
  milk cans +19, matte wall +4. Null controls were 0 px on both builds.
* **VERIFIED: the model gloss/metal maps.** In all 18 `_GMM.dds` and the one
  `_MAO.dds` in `19_monastery.pkg` the BLUE channel is all zero; GREEN sits at
  60/255 = 0.235 on plain surfaces, the game's default metal 0.231; RED
  averages 0.35..0.42. The owner said "roughness is red, metal is blue" —
  the reference shader he then pointed at reads R and G (gloss, metal), which
  is what the patch does. The model shader does not store the map's blue in
  the G-buffer at all (`gGMF.rga = gm.rgr` on the plain path; baked occlusion
  in `.a` on the tiled path).
* **The live monastery settings** (`%TEMP%\nuTerra\MapSettings\19_monastery.txt`)
  had **Spec Level at 0.024**, which starves the GGX lobe; the game's
  aluminium look is mostly that lobe on a rough metal. Bright Level was set
  to 1.6 at the owner's request. The file now also carries `env_spec=1`,
  `gmm_curve=1`, `pbr_fresnel=0`, `wet_specular=0.024` from the reverted
  build — ignored by the current one, harmless, and **`pbr_fresnel=0` says
  the owner dragged Fresnel all the way off** before reverting. The repo copy
  of that file is out of step with the live one (Bright 1.384, fog on).
* **REASONED, unmeasured:** the wet, water and pooled-water cube taps still
  read the cube through `SRGBtoLINEAR` with gains tuned to that reading.

## 4. How the measurement was done — reuse it

```
nuTerra.exe 19_monastery cam=-9.8689,4.8497,-0.4833,72.8508,0,47.0268 freezefx still=1 out=<dir>
```

writes ONE still to `<dir>\still\still_NNN.png` and logs `record: still saved`;
kill the process after the line. `out=` keeps it off the owner's
`G:\nuTerra_ScreenCaps\still` (his `record_dir`; the C: folder the earlier
handoff looked at is not where stills go). Same build twice = 0 px at this
camera. A run the owner interrupted left a `still_000` at another camera in
the folders, so compare by the file the log names. The owner's reference
still is `G:\nuTerra_ScreenCaps\still\still_053.png`; his game screenshots
were pasted in chat only and are not on disk.

## 5. Etiquette learned today

* **Ask before stopping his instance** unless he hands the helm ("you have
  the helm"). One capture run was rejected for killing it.
* `git apply --unidiff-zero` misplaces zero-context hunks; the commit split
  was redone by building index blobs directly (`hash-object` +
  `update-index`). Never trust a partial stage without `git diff HEAD` empty
  at the end.
* Bash heredocs with apostrophes fail in this harness; payloads go through
  the Write tool as Python scripts with exactly-once anchors.

## 6. Pick-up

1. Ask the owner what in section 2 he wants back, and why he reverted.
2. Re-land the `lighting.md` §4 correction (cube IS PMREM) regardless.
3. If the env term comes back: seed `wet_specular` from the map's current
   `specular` on first load rather than a constant, and settle the F0
   question against the disassembly (`docs/terrain_blend.md`, "Reading the
   game's shaders").
4. The card-over-card silhouettes in the smoke and the water/wet cube taps
   remain open; see `open_threads.md`.

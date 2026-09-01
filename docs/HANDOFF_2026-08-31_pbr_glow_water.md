# Handoff — PBS_tank, PBR specular, glow occlusion, pooled water

Session of 2026-08-31, evening. Follows
`HANDOFF_2026-08-31_fx_and_holes.md` from earlier the same day. All work is on
**`master`**. **Nothing is pushed.**

Safety tag **`pre-pbr`** at `e1e7b83` — the point before any resolve work.
`git reset --hard pre-pbr` unwinds everything after it.

## What landed

| commit | what |
|---|---|
| `5908570` | Decal tangent frame off screen-space derivatives — the checkerboard fix |
| `301b369` | Removed the parallax chain that fix orphaned |
| `922a7e5` | docs index, `decals.md`, two corrections |
| `730cefd` | `game_PBS_tank.md` — the game's tank shader recovered |
| `e1e7b83` | Probe field: mix control restored, curve + floor added |
| `1491190` | PBR specular behind a toggle |
| `7602485` | FX glow depth-tested so nearer surfaces block it |
| `63f271d` | Glow occlusion hard wired at 0.85 / bias 0.003 |
| `cde12b9` | Pooled water: let a wet decal write its wetness |

## Read this part even if you skip the rest

**Four separate wrong answers this session came from a bad control, not bad
reasoning.** Every one looked convincing:

- A wide-camera A/B measured "27.3% of pixels changed". The null control at the
  same camera was **26.5%**. Bright, distant views drift run to run; close dark
  ones are bit-identical. Raising `settle` 200 → 1500 did not help.
- A fog test showed `fog_level` 0 vs 1 made no difference, "exonerating" the
  fog. The capture point (`fx_pass.png`) is written at `report_fx_diff`, and
  the fog pass runs ~50 lines LATER. The measurement could not see fog at all.
- A PBR negative control showed 63% of pixels changed with the toggle off,
  reading exactly like a broken code path. The baseline png was from earlier in
  the session and the map's **settings had been retuned in between**. Settings
  drive the render. Re-capturing with the change stashed gave bit-identical.
- A "bit-identical" verification was taken from a build that had **failed** —
  nuTerra was running, MSBuild could not replace the exe, and the old binary
  rendered the frame.

The rules that fall out, in order of how much time they would have saved:

1. **Run the null control** — same build, twice, same camera — and confirm it
   is 0 px before trusting any diff.
2. **A saved png is not a baseline** unless everything feeding it is frozen,
   including the per-map settings file.
3. **Check the build actually landed.** `exit=0` is not enough when the app may
   be open. Confirm a restamped binary, or a bin shader carrying the new text.
4. **Know where your capture point is.** `fx_pass.png` is `gColor` straight
   after the FX pass — it is BEFORE FXAA, base rings, fog and the minimap.

A related trap: a *failed* build can still copy `nuTerra.dll` while only
`apphost.exe` is locked, so new code can run out of a build that reported
errors. Do not assume failure means "old binary" either — check.

## Open: pooled water

**Status: the plumbing is fixed, the classifier is not, nothing seen on screen
yet.**

Terrain pooled water is a shader trick on wet-flagged decals. `cde12b9` fixed
three ways the wet path was being discarded (see the commit). What remains is
that **the wet flag is inferred, not read**:

```vb
' MapLoader.vb - a decal with no diffuse texture is assumed wet
If colour_fname.Length = 0 Then decal_item.wet = CUInt(1)
```

Measured, that heuristic finds:

| map | decals | flagged wet |
|---|---|---|
| 19_monastery | 2326 | 36 |
| 101_dday | 5669 | **0** |
| 08_ruinberg | 7834 | **0** |

Zero on D-Day and Ruinberg is not credible. The load now logs this line every
map, so the number is one glance away.

**The lead:** the WGSD decal record carries a **`materialType`** byte, read at
`modSpaceBinFunctions.vb:183`, `:324` and `:391`, stored in the struct at
`modSpacedBinVars.vb:739`, and consumed **nowhere** — the only reference is a
commented-out `Debug.WriteLine` at `MapLoader.vb:645`. That is very likely the
authored decal type.

**Next step:** survey `materialType` values against the no-diffuse guess. A
throwaway histogram in the decal loop answers it in one map load. If a distinct
value lines up with the wet decals on monastery and appears on D-Day too, that
is the classifier.

Also still true: `gGMF.r = 0.9` in the wet branch is dead code — `attach_CN`
enables only ColorAttachment0/1 and that output is at location 6. It does not
matter much, because `deferred.frag` already forces gloss/metal toward
`(0.4, 0.8)` by `water_mix`, but the shader reads as though it sets gloss.

Verify on **19_monastery** — it is the only map so far with any wet decals.

## Open: the probe field "swim"

The owner sees the low end swimming horizontally to the right at high probe
mix, on an untouched camera, and **`freezefx` does not stop it**.

Ruled out, each by measurement:

- **Not per-frame drift** — frames 200→204 are bit-identical.
- **Not the fog scroll**, despite fitting the symptom perfectly (`move_vector`
  = (0.3, 0.7), positive X, ungated by freeze). `fog_level` 0 vs 1 changes
  0.09% of pixels. **Caveat: that test used a capture point taken before the
  fog pass runs, so it is weaker evidence than it looks — re-test properly.**
- **Not ongoing** in a headless static-camera run: 200→3000 changes 46.5%, then
  3000→6000 is bit-identical. That settling is one-time.

**The owner's hypothesis, and the best lead: quantization.** `gPosition` is
`Rgb16f` (`FBO_main.vb:206`). The grid lookup reconstructs world position from
it, so the position feeding `eval_sh_grid` is half-float. At world magnitudes
the ULP is coarse, and a 3x mix amplifies every step. The grid texture itself
is RGBA16F, so it is not the storage.

**Next step:** test the position precision directly — snap the lookup position
to a half-float grid and see if the artefact reproduces at mix 1, or widen
`gPosition` and see if it goes.

## Open: PBR specular

`1491190` ports the game's model behind `pbr_spec`, off by default, verified
bit-identical off. GGX D, Schlick-Gaussian F, Smith-Schlick Vis, `alphaR =
1 - gloss^2`, dominant direction bent toward N, env LUT indexed
**(alphaRoughness, NdotV)**. What it replaces is a Phong lobe whose LUT was
indexed `(1 - NdotL*0.25, 1 - gloss)` — neither of its axes.

**The channel names in `deferred.frag` are wrong and will mislead you.**
`float metal = GM_in.r` is GLOSS; metal is `GM_in.g` and is currently spent as
`INTENSITY`. `model.frag` writes `gGMF.rg` from the metallicGlossMap, which the
PBS_tank decode proves is (GLOSS, METALLIC). The new block uses its own
`g_gloss` / `g_metal` so the mistake cannot ride along. **Renaming the outer
variable is worth doing before anyone builds on it.**

Deliberately NOT ported: the cubemap decode. nuTerra's cube is not PMREM
encoded the way the game's is (`rgb^2 * 2^(9a) / 8`), so only direction, mip
curve and LUT indexing crossed over.

**Next step:** judge it on a specular-heavy camera — wet ground, metal, glass.
On the Abbey fire camera it moves 8.2% of pixels, mean 1.79, because that view
is matte brick and tile. `pbr_spec` is a map-settings key, so it can be driven
headlessly.

## Reference: what was decoded

`game_PBS_tank.md` is new — the game's tank shader, carved from
`PBS_tank.10.dx11.fxo` (534 blobs, 216 distinct). It writes a five-target
G-buffer and does **no lighting**. Most of its value here is the channel
truth it settles and the naming traps it documents: `metallicGlossMap` is
(GLOSS, METALLIC, mask), the alpha test reads `normalMap` not `diffuseMap`,
`g_glossMin`/`g_glossMax` are a micro-detail window and not a gloss remap.

Two of its claims were refuted by the verification pass and are recorded
corrected rather than dropped — `o1.w` is `1 - AO`, not a wetness term.

## Environment

Everything in the earlier handoff still holds. Additions:

- **A build can fail on `apphost.exe` and still update `nuTerra.dll`.** New
  code runs out of a "failed" build. Check binaries, not exit codes.
- **Do not mix bash-visible paths into the Windows Python.** `/tmp/x` is fine
  in `sed` and invisible to `python`. One commit message was garbled this way,
  because the following line was not chained behind the failing step with
  `&&`.
- **`git commit -F` with an ASCII file** remains the way to write these.

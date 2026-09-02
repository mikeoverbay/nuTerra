# Open threads

Loose ends as of **2026-09-02**, written down so a compacted session or a new
one can pick them up cold. Each entry says what is known, what is NOT known,
and what the next concrete step is.

Ordered roughly by how much they will bite.

---

## 1. Three new maps crash natively

The 2026-09-01 game patch added three maps nuTerra has never seen:

    140_fall_tanks
    141_dash_to_go
    142_road_to_dash

All three have **21x21** heightmaps and all three die on load with
**0xC0000005 - an ACCESS VIOLATION**, not a managed exception. That matters:
`101_dday`'s crash was managed (0xE0434352) and turned out to be an array
bound. A native fault is a different animal - bad pointer, bad size, or a GL
call with a degenerate dimension.

**Not investigated at all.** No debugger run, no stack. 21x21 is far smaller
than anything the terrain code has met (the previous minimum was 06_ensk at
37, and the averaging branch is written around 37 explicitly), so a hard coded
dimension or a zero-size buffer is the first place to look.

Next step: run one of them under the debugger and read the fault address.

## 2. drop-dotnetzip is finished and unmerged

Branch `drop-dotnetzip`, commit `7fb84c3`. Replaces DotNetZip with
System.IO.Compression and closes both Dependabot alerts.

Verified: the rendered frame is **bit identical** to the DotNetZip build -
0 px of 960000, same camera, same settings - and seven of eight test maps load
clean. The eighth was dday, whose crash was the unrelated heightmap bug now
fixed on master.

The reported CVE was never reachable here: it is a directory traversal on
extract-to-DISK and every extract in this codebase went to a MemoryStream.
This is hygiene, not an incident.

Merge when convenient. It has not been rebased since master moved.

## 3. water_mask_wet is on in settings, off in code

`WATER_MASK_WET` defaults to **False** in `modGlobalVars`, but the Abbey config
that was propagated to all 64 maps carries `water_mask_wet=1`, so it is
effectively **on everywhere**.

It is the fix for the water plane covering the sky (the "marbling"), and lakes
were confirmed fine with it on. But it was shipped off-by-default deliberately,
and it is now live on maps it was never tested against.

Decide: make the default True to match reality, or strip the key from the
configs. The current split is the worst of both - the code says one thing and
every map says the other.

## 4. Abbey lighting is a baseline, not per-map lighting

`5797700` copied 19_monastery's tuned config to all 64 maps. Every map now has
baked shadow, the SH probe mix and a working exposure, where before they had
untuned defaults.

But Abbey is a **sunset town** and its `tonemap_exposure=3.627` /
`ambient=0.282` carry over literally. 33_fjord, an overcast daylight map, comes
out visibly dark. This is a starting point to tune from, not finished work, and
it should not be remembered as "lighting is done".

`water_y_offset`, `water_exclude_band` and `water_fog_mul` were deliberately
NOT copied - they are per-map water geometry. Backups of both the repo and work
copies are at `C:\nuTerra_backups\`.

## 5. 23_westfeld chunk heights look wrong

Owner's observation. Westfeld and dday are the only two maps the patch reshaped
to **133x133**; both crashed until `heightsTBL` was sized to max(69, mapsize).

They load now, but loading is not the same as being right. The terrain code was
written when every map was 69x69: the averaging branch still only special-cases
`mapsize < 69`, and the heightsTBL consumers (`get_Y_at_XZ`, mouse picking,
neighbour sampling) index it with coordinates derived from world position. The
crash was fixed by making the array big enough, which says nothing about
whether the INDEXING is right at 133.

Next step: compare a height lookup against a raw offline read of
`terrain2/heights` from the .pkg. If dday is wrong the same way, it is the
scaling maths, not the map.

## 6. The resolve samples the cubemap in view space

`deferred.frag` builds `R_env = reflect(-V, N)` from VIEW space vectors and
feeds it to a world oriented `cubeMap` at lines ~823 and ~856
(`prefilteredColor`). No `invView`. That makes the environment reflection
rotate with the camera for **every** specular surface in the scene.

Only the pooled-water path was fixed (it converts through `invView` and clamps
the horizon). The general specular path still has it.

Pre-existing, not introduced by any of this work, and not measured - it is
visible in the code, not in a screenshot. Worth an A/B before believing it
matters.

## 7. Pooled water: rim and reflection content

Two known-imperfect things in the shipped water:

- **The rim is hard edged.** `POOL_CUT` thresholds a low resolution wetness
  texture that is hugely magnified up close, so the boundary stair-steps along
  texel edges. Blending it needs a gradient, and gGMF.a cannot serve while that
  channel is max-blended flat against the terrain's own wetness.
- **Reflections are sky only.** `ssr.frag` skips sky by design and ADDS where
  water.frag MIXES, so an SSR building hit lands on top of the sky reflection
  instead of replacing it. Making SSR mix for pool pixels is the fix, and the
  surface-kind byte now identifies those pixels cheaply.

## 8. Camera flight

Step 1 (the 2.5 m ground clamp) is built. Steps 2-4 are designed and not
started - see `camera_flight_plan.md`, which includes why the bake has to be
its own FBO pass rather than a reinterpretation of the beauty pass.

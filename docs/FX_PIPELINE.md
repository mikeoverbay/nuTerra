# The FX pass — how fire, smoke and glow are composited

Covers everything between the water pass and the FXAA pass. If you are
changing how FX look, this is the document. `PARTICLES_HANDOFF.md` covers the
particle *simulation*, and `VFXBIN_PARTICLE_FORMAT.md` the data behind it.

## Why there is a pipeline here at all

`gColor` is **Rgba8** (`FBOs/FBO_main.vb`), and `deferred.frag` tonemaps
**inside the deferred pass** — `outColor = correct(final_color, exposure, 1.2)`,
where `correct` is `1 - exp(-x * exposure)`. The FX pass runs *after* that.

So for most of this project's life the FX blended into an already-tonemapped
8 bit buffer, and every blend clamped at 1.0. Fire is roughly
`(1.0, 0.6, 0.2)`, so overlapping additive cards pinned **red first**, green
then climbed to meet it, and orange became yellow became white. Measured
against a capture of the real game, a third of our fire pixels sat at
`R=G=255` where the game's frame had **one** such pixel in 29191.

That is the problem the rest of this document solves. Note what it is *not*:
not the material colours, not the tint, not the alpha. It is the compositing
arithmetic.

## The buffers

| name | format | what it is |
|---|---|---|
| `gFX_HDR` | Rgba16f | FX accumulation target. Premultiplied colour in rgb, accumulated coverage in a |
| `fx_fbo` | — | Framebuffer over `gFX_HDR`, **sharing `gDepth`** with the main FBO |
| `gFX_BloomA` / `B` | Rgba16f | Glow ping-pong pair, at `BLOOM_DIV` = 1/4 resolution on each axis |
| `bloom_fbo` | — | Swaps its colour attachment between the pair as the passes ping-pong |

`fx_fbo` shares the depth **texture**, it does not copy it. That is what keeps
the FX depth-testing against the scene exactly as before. Both FX passes run
`DepthMask(False)`, so nothing can write depth through the alias.

All eight of the main FBO's colour attachments were already in use — that is
why the FX target is a second framebuffer rather than attachment 8.

## Order of operations

```
  [particles.Draw]      alpha cards       ─┐
                                           ├─→ gFX_HDR   (cleared to 0,0,0,0)
  [draw_fx]             volumetric meshes ─┘
        │
  [build_fx_glow]       bright pass → blur ping-pong → gFX_BloomA
        │
  [composite_fx]        gFX_HDR + glow, rolled off, blended over gColor
```

### Cards first, meshes second — load-bearing

`particle.frag` emits `vec4(rgb * alpha, alpha)` unconditionally; there is no
additive branch, so cards **attenuate** whatever is behind them. The
volumetric meshes take `volumetric.frag`'s `mat.alphaTestEnable` branch and
emit `vec4(rgb * alpha, 0.0)`, which under premultiplied
`One / OneMinusSrcAlpha` reduces to `dst + src` — they add light and attenuate
nothing.

So meshes last is the only order in which card smoke cannot wash the fire out,
and it costs the additive draws nothing, because addition does not care what
came before it. **Do not move the particle call below `draw_fx`** — the
fire-after-smoke rule breaks silently, with no error.

`draw_fx` additionally partitions its own bucket so alpha FX composite before
additive FX within the multidraw.

### Why accumulating separately is not an approximation

Premultiplied "over" is **associative**. Compositing the FX among themselves
and then compositing that result over the scene gives the same answer as
compositing them one at a time over the scene. Additive materials emit alpha
0, so they add and attenuate nothing in either arrangement.

The only thing that changes is that intermediate sums live in float16 instead
of being clamped to [0,1] at every step — which is the entire point.

## The composite

`shaders/Final_render/fx_composite.frag`, driven by `composite_fx()`.

```glsl
fx.rgb += texture(bloomBuffer, uv).rgb * glow_strength;   // glow first
const float peak = max(fx.r, max(fx.g, fx.b));
fx.rgb /= max(1.0, peak);                                 // then roll off once
outColor = fx;
```

**Divide by the peak CHANNEL, not by luminance.** Luminance is the obvious
choice and it is wrong, because it does not bound the channels: a sum of
`(2.0, 1.2, 0.4)` has luminance 1.31, so dividing by it leaves red at 1.52,
which still clips. Measured, the luminance version left 15.9% of fire pixels
pinned; the peak version leaves 0.2%, against the game's 0.0%.

Dividing by the peak is a pure scale, so hue is preserved *exactly*:
`(2.0, 1.2, 0.4)` becomes `(1.0, 0.6, 0.2)` — full brightness, still orange. A
genuinely white-hot sum like `(5, 5, 4)` becomes `(1, 1, 0.8)` and stays a
white core, which is what the game's frame shows: orange everywhere, white
only in the middle.

**The glow is added BEFORE the roll-off.** Adding it after is the obvious
arrangement and it clips — measured 0.50% → 1.70% blown, handing back part of
what the float accumulation had just won. The worry that folding it in first
"scales the glow away" does not survive contact with what the roll-off does:
out in the halo, where the fire is absent and the glow *is* the signal, the
peak is the glow's own small value and nothing is scaled.

The buffer's alpha is passed through untouched, so the pass's
`One / OneMinusSrcAlpha` still lets smoke attenuate the scene and lets the
glow add light where coverage is zero.

## The glow

`build_fx_glow()` — bright pass, then a separable blur ping-pong.

- `fx_bright.frag` keeps `max(rgb - threshold, 0)` from `gFX_HDR`, rendered at
  quarter resolution so the downsample comes free with the smaller target.
- `msm_blur` is **reused as-is** for the blur. It is a plain 9 tap Gaussian
  along a uniform direction over a `sampler2D` — nothing in it is specific to
  shadow moments, and a second copy would just be another thing to keep in
  step.
- The ping-pong runs `FX_GLOW_PASSES` times, H then V, so the result always
  lands back in `gFX_BloomA`.

Quarter resolution is what gives the glow its **radius** as much as it saves
work: the kernel is a fixed 9 taps, so its reach in screen pixels is whatever
one texel at that size is worth. At full resolution the same kernel is a
barely visible smudge.

`build_fx_glow` leaves the viewport at the reduced size; its caller restores
it before compositing. Do not remove that line.

### Tuning constants — HARD WIRED

In `modGlobalVars.vb`. `Const`, not variables — the sliders that set them were
removed once the look was settled. Change them here and rebuild.

| constant | value | notes |
|---|---|---|
| `FX_GLOW_STRENGTH` | 2.0 | Deliberately overdriven; the halo covers far more pixels than the core it came from |
| `FX_GLOW_RADIUS` | 2.7 | Multiplies the blur's tap spacing. Fractional on purpose — taps land between texels, where the Linear filter averages two and hides the gaps |
| `FX_GLOW_PASSES` | 3 | H+V pairs. Widens by sqrt(N), but its real job is filling in a wide radius |
| `FX_GLOW_THRESHOLD` | 0.42 | Not what it looks like — see below |

**The threshold is a FLOOR, not a midpoint.** 1.0 is the principled value:
`gFX_HDR` holds the premultiplied sum before the roll-off, so above 1.0 is
exactly the energy that used to clip, and glowing only that ignores smoke for
free. 0.42 reaches below it, so **smoke glows too** — lightly, which is what
was wanted. Below 0.42 the smoke starts glowing badly. There is no headroom
underneath.

The obvious "correction" back to 1.0 would throw away the setting it was
chosen for. Several comments in the tree still describe the 1.0 behaviour when
explaining the principle; they say so explicitly.

`FX_GLOW` itself is still a live Boolean — checkbox under **Draw FX**, plus the
`noglow` launch argument, which is the only headless way to A/B the glow.

## FX lit from the baked probe field

`USE_SH_GRID_FX`, **off by default**. `volumetric.vert` carries the same
`sh_grid` uniforms and the same evaluation as `deferred.frag`, and folds the
field over the flat global probe by `sh_grid_mix` — so a smoke column standing
in a shaded courtyard and the ground under it read the same probe.

Two deliberate divergences from the deferred upload, both commented where they
happen in `draw_fx`:

- `sh_grid_enabled` is additionally gated on `USE_SH_GRID_FX`.
- `sh_grid_offset` is sent as **0**, not 1.5. That push exists to bias a
  *wall's* lookup out of the near-black probes baked inside buildings. A smoke
  card is in open air and its normals are not a coherent billboard, so
  inheriting the push scatters neighbouring lookups by a whole probe cell.

Unit 11 is bound **whenever the texture exists**, not gated on the toggle:
fire and smoke are one `MultiDrawElementsIndirect` through one program, so an
unbound `sampler3D` is an incomplete-texture condition for the whole draw,
fire included. `sh_grid_enabled` is uploaded *outside* the `If`, so a
grid-less map cannot inherit `enabled = 1` from a previous map.

Expect it to do very little on 19_monastery: only 1 of 11 volumetric materials
there is `lit=True`. The rest are additive fire with lighting authored off,
which the field cannot touch by design. `07_lakeville` authors many lit
materials and is the map to judge it on.

## What is locked

`shaders/Model_shaders/volumetric.{vert,frag}` and the `draw_fx` state
sequence. Fire and volumetric smoke work; walk the full call path and its
state assumptions before changing anything in it.

The probe-grid work above did touch `volumetric.vert`, additively, and was
accepted only because the negative control came back **bit-identical** — same
sha256 with the toggle off. That is the bar.

## Traps

- **Never A/B the FX pass with a live particle sim.** The sim advances on real
  dt and differs run to run. A glow measurement taken that way reported the
  added light as *green*; with `freezefx` pinning the sim it is R=21.4,
  G=2.6, B=-0.1. `freezefx` pins `FX_TIME` at 0 **and** stops particles
  spawning, so only the meshes are captured.
- `attach_*` names draw buffers but does **not** bind. A pass that assumes
  otherwise lands in whatever framebuffer the previous one left bound.
- Sampling an **attached** `gPosition` is a feedback loop. `draw_fx` and the
  particle pass both detach it for the pass and re-attach afterwards. Now that
  the FX draw into `fx_fbo`, which has no `gPosition` attachment, that dance is
  redundant — it is left in place deliberately rather than disturbed.
- A declared sampler bound to nothing does **not** read black; drivers return
  `(0,0,0,1)` for an incomplete texture, which is a plausible-looking wrong
  answer. Every sampler this path declares is bound unconditionally, with a
  dummy where needed.
- Screenshots are post-tonemap and post-LUT. Never reason numerically about
  pixel values from one. `%TEMP%\nuTerra\fx_pass.png` is `gColor` read
  straight off the GPU immediately after the FX pass — that one *is* the raw
  buffer, and it is written only on the FX-diff frame, one frame before the
  snapshot, so `snapquit` is required to get it.
- Codebase convention is that a pass **sets** what it needs and does not
  restore. `MapParticles.SaveState`/`RestoreState` is the deliberate exception.

## Measuring it

Run from `nuTerra\bin\Debug\net6.0-windows`, via PowerShell, never git-bash.
Kill `nuTerra.exe` first.

```
nuTerra.exe 19_monastery cam=-40,6.05,0.02,140.5372,9.2711,31.1427 freezefx settle=200 snapquit
nuTerra.exe 19_monastery cam=-40,6.05,0.02,140.5372,9.2711,31.1427 freezefx settle=200 snapquit noglow
```

Then diff the two `fx_pass.png`. Verify the file's write timestamp advanced
before copying it — a stale capture looks exactly like a null result.

The metric that settled the colour work: count pixels where
`R > 150 and R > B + 60` (fire), and of those, how many sit at `R >= 254 and
G >= 254` (blown). The game's own reference frame scores 0.0%.

# Decals

Box-projected deferred decals: craters, tracks, scorch, paint. They run after
the G-buffer is filled and rewrite `gColor`, `gNormal` and part of `gGMF` for
the pixels their box contains.

## The trap, first

There are two shaders with "decal" in the name and only one of them is this:

| file | what it actually is |
|---|---|
| `Developer/box_decals_color.frag` | **the decal pass.** Driven by `MapDecals.draw_decals` as `boxDecalsColorShader` |
| `Terrain_shaders/DecalProject.frag` | the **3D map cursor**, drawn by `MapCursor.vb` |

`DecalProject.frag` reads like a stripped-down decal shader because it is one -
it projects a texture through a box exactly the same way. It is not on the
decal path. Patching it to test a decal theory produces a bit-identical frame
and no error, which is a very convincing way to waste a build. Do not delete it
either; the cursor needs it.

## The pass

`Scene/MapDecals.vb`, called from `modRender` once the G-buffer is complete.

```
attach_CN()                         colour + normal, NOT position
gDepth        -> unit 0             the surface being projected onto
gGMF          -> unit 1 and 6
gSurfaceNormal-> unit 4             VIEW space, Rgb8
per decal: color_tex -> 3, normal_tex -> 2
CullFace off, Blend on, DepthMask FALSE, ColorMask alpha OFF
one 14-vertex triangle strip (a cube) per decal, in list order
```

`DepthMask(False)` is what keeps decals from z-fighting the surface they sit
on. The alpha channel of `gColor` is **wetness**, which is why `ColorMask`
masks it off - writing decal alpha there corrupts the wetness term and breaks
decal normal mapping downstream.

Two things worth knowing about the teardown: `unbind_textures(5)` clears units
**0-4 only**, so the gGMF binding on unit 6 survives the pass. And the pass
sets state without restoring all of it, which is the codebase convention - see
`FX_PIPELINE.md`.

## Reconstruction, and what it costs

Each fragment reconstructs the surface point by sampling `depthMap` and pushing
it back through `invMVP`, then clips it against the decal's unit box. Both the
box test and the decal UV therefore inherit **depth-buffer quantisation**.

That matters more than it sounds. The reconstructed position stair-steps, so
anything that differentiates it is differentiating a staircase.

## The tangent frame - do not derive it

`get_tbn` takes the tangent as a **uniform from the CPU** (`decal_tangent`),
alongside `decal_axis`. It used to build one from `dFdx`/`dFdy` instead, and
that was a real bug:

- the frame divided by the UV Jacobian determinant,
- the UV is reconstructed from the depth buffer (above),
- so at grazing angles the determinant collapses toward zero and the tangent
  explodes,
- and NVIDIA's `dFdx` defaults to **fine** derivatives, which differ between
  the even and odd pixels of a quad.

The result was a 1-pixel checkerboard written into `gNormal`, covering the
ground and re-shuffling on every camera movement. It reads exactly like
z-fighting and is not.

A decal's UV mapping is affine in its own box, so the tangent is exact,
constant across the decal, and free to compute. `MapDecals` sends local +X
rotated into view space; the sign follows the shader's own UV math - `tuv` runs
along local **-X**, flipped again when `uv_wrapping.X` is negative. `get_tbn`
then Gram-Schmidts it against the real surface normal, so the frame still lies
in the surface the decal landed on rather than in the decal's plane, and falls
back to a fixed perpendicular when the transform is degenerate.

## The three fades and the mask

**Kind mask.** A decal's `influenceType` is a bitmask over surface kinds; the
low 3 bits of `gGMF.b` name the kind. The test is `(influence >> kind) & 1`.
The game does the same thing through a 256x8 "bitwise LUT" texture, which is
only a precomputed bit extraction - which is why no such texture ships. See
`common.h`.

**Angle fade.** `ANGLE_FADE_MIN/MAX` (50 deg fully rejected, 40 deg fully kept)
against `|dot(surface_normal, decal_axis)|`. This is what stops a decal
smearing down a face it only skims. The `abs()` is deliberate: the axis points
down into the surface on essentially every ground decal while the view-space
normal faces the camera, so a signed test rejects everything.

A pixel no G-buffer writer touched holds the clear, which decodes to
`(-1,-1,-1)` with length² = 3. Outland ground is the real case - its normal
lands in `gAUX_Color`, not here - so those fragments are given no gate at all
rather than a wrong one.

**Edge fade.** `EDGE_FADE_WIDTH = 0.12` in decal-local units, from the box
edge inward. Applied to every decal: `DecalEdgeProbe` used to decide this per
texture by measuring how close its content ran to the border, but fading
everything reads better and the machinery was not worth it.

## Uniforms that are wired but unused

`v1`, `v2` and `vis` are uploaded every decal and read by nothing. They are
decal metadata kept wired for the next time something needs interrogating -
not an oversight, but do not assume they do anything.

## Measuring it

The failure mode here is high-frequency and view-dependent, so screenshots lie
and cross-run diffs need a control. The metric that settled the tangent bug is
**signed checker energy**: multiply each pixel's difference from its 4-neighbour
mean by `(-1)^(x+y)` and average. Real scene detail averages to ~0; a genuine
pixel-parity artefact does not.

```
nuTerra.exe 101_dday cam=-1.1303,4.4177,-0.235,-99.2617,0.1248,35.4169 freezefx settle=200 snapquit
```

| | checker mean |
|---|---|
| the bug | +0.540 |
| decals off | +0.005 |
| fixed | -0.003 |

Isolate by elimination through the per-map settings file - `draw_decals` was
the only toggle that moved it; SSR, baked shadow, FXAA and tessellation all
left it unchanged. That camera's null control is 0 px, so differences there are
attributable. Do not use a wide, brightly lit view for this: those drift ~25%
run to run. See `FX_PIPELINE.md` for why.

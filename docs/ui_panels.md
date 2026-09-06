# ImGui panels

Everything here was learned by getting it wrong first. The rules are short; the
reasons are the part worth keeping.

## Where a panel lives

**In `Window.vb`'s UI pass, never in `modRender`.** The ImGui frame is begun and
ended around that pass, so a `Begin` anywhere else either asserts or silently
draws into nothing.

Add the call next to the other `draw_*` panels, and a `SHOW_*` boolean in
`modGlobalVars` for the menu-bar button to toggle.

## Placement: use the helpers, not numbers

```vb
ImGui.SetNextWindowPos(panel_origin(), ImGuiCond.FirstUseEver)
ImGui.SetNextWindowSize(panel_size(300, 560), ImGuiCond.FirstUseEver)
```

- `panel_origin()` — 15 px in from the client edge, 15 px below the **last line
  of the menu bar**, taken from the `menubar_pos` / `menubar_size` that
  `SubmitUI` captures each frame.
- `panel_size(w, h)` — clamps the request to what is left of the client area,
  keeping the same 15 px margin on the far side.

The menu bar is `AlwaysAutoResize` and **grows** when the fps and clip counts
appear after a map loads. A hardcoded `y` is therefore right on the map picker
and wrong for the rest of the session. Measure it; do not guess it.

## The trap: `imgui.ini` beats `FirstUseEver`

**`ImGuiCond.FirstUseEver` means "unless the ini already has a value", and the
ini almost always does.**

ImGui writes `imgui.ini` next to the exe, keyed on the window's **id** — the
part after `###`, or the whole title if there is no `###`. Once a window has
been opened at any size, that size is saved and every later `FirstUseEver` size
is ignored, in this run and every future one.

This cost two rounds of "make it smaller" that changed nothing. The Light Bulb
Placer was first created at 700x840; the ini held

```
[Window][###LampView]
Pos=184,38
Size=700,840
```

and it kept opening 840 px tall on a shorter client, with its own controls off
the bottom of the screen and no way to reach them. Asking for 300x600 with
`FirstUseEver` did exactly nothing, twice.

**Two fixes, and they do different jobs:**

1. **Constrain every frame.** `SetNextWindowSizeConstraints` is applied by
   `Begin` whatever the ini says, and still lets the window be resized inside
   the limit:

   ```vb
   Dim o = panel_origin()
   ImGui.SetNextWindowSizeConstraints(
       New System.Numerics.Vector2(260, 200),
       New System.Numerics.Vector2(CSng(ClientSize.X) - o.X - 15.0F,
                                   CSng(ClientSize.Y) - o.Y - 15.0F))
   ```

   This is the one that keeps a panel on screen permanently. Add it to any
   panel whose content can grow.

2. **Change the `###` id** to abandon a bad saved entry. Renaming the window's
   *visible* title does not do this — the id is what the ini is keyed on, which
   is the whole point of the `###` suffix. `"Light Bulb Placer###LampView"`
   still loads the old 700x840; `"Light Bulb Placer###BulbPlacer"` starts
   fresh.

Keep the `###` id **stable** afterwards, so a position the owner drags to
survives a rename or a caption that changes per frame (the Flight Recorder's
title carries a live frame counter this way).

If a panel is misbehaving, read the ini before theorising — it is a few lines of
plain text in `bin\Debug\net8.0-windows\imgui.ini` and it settles the question
immediately.

## Sizing content

An ImGui window scrolls content that does not fit, so a too-tall *window* is a
placement bug, not a content bug. Render-to-texture panels are the usual cause
of one: draw the texture at a chosen display size rather than its native size,
and keep that size in one named constant.

The Light Bulb Placer renders its four panes at 320 px each and displays the
result at 248, so the lines stay crisp when scaled down instead of going
blocky. One constant, `SHOWN`, changes it.

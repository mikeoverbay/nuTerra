# Tk event traps in Path Studio

Four ways the map picker was made to load the wrong map, all found on
2026-09-09 and all fixed. They are written down because none of them is
visible in the source at the point where it goes wrong, and every one will
come back the next time someone adds a callback.

The rule they all violate, in the owner's words:

> Nothing should happen to the map list after you click a map name. It signals
> everything downstream. Never go back to it.

The list is a **signal source**. A click on it starts the load, the mask, the
route and the lights, and none of that is ever allowed back up to touch the
control that started it. The only thing that leaves the widget is a map
**name**.

## 1. A selection set from code fires the same event as a click

`<<ListboxSelect>>` is generated whenever the selection changes, and Tk gives
you no way to tell a click from a `selection_set` your own code just made. So
any write-back re-enters the handler that called you, against whatever the
widget looks like by then.

- `load_named` cleared the other picker's selection so the two would "agree".
- `refill_maps` restored the previously loaded map with `selection_set` + `see`.

Both are gone. Filtering rebuilds the rows and nothing else; after a refill
nothing is selected, which is the truth - the list has just changed under
whatever was chosen before.

`_refilling` guards the rebuild itself, because `delete(0, "end")` clears the
selection and that fires the event too.

## 2. A background thread rebuilding the list

`bake_selected` runs on a thread. `_bake_done` called `find_maps()`, which
rebuilds every row - so the list was deleted and re-inserted **whenever the
bake happened to finish**, seconds after the click that started it, while the
operator was scrolling or about to click. The rows moved under the cursor and
the next click picked a different map.

Nothing needed rebuilding: one map gained a height map, so one row changed
**colour**. `mark_row_baked` recolours that row in place. No delete, no
insert, no selection, no scroll.

## 3. A modal opened from inside the event handler

`messagebox.askyesno` runs its own event loop. Called from within
`<<ListboxSelect>>`, it stops Tk halfway through delivering the click and pumps
the rest of it - button release, the Listbox's class bindings - underneath the
dialog. Those land when the dialog closes and walk the selection to the next
row. Saying **No** to a bake moved the highlight down one, every time.

The dialog is queued with `after_idle` so the click finishes being delivered
first.

That is not sufficient on its own: `load_named` calls `update_idletasks()` to
paint its "loading" line, and **that flushes the idle queue** from the middle
of the next map's load. Traced live: picked `29_el_hallouf` (no bake), picked
`34_redshire` before the dialog appeared, and the dialog then asked about
`29_el_hallouf` while `34_redshire` was on screen. Answer Yes to that and you
bake a map you are not looking at. `ask_to_bake` now returns unless the map is
still the selected one.

## 4. `busy` silently discarding the click

`load_selected` began `if not sel or self.busy: return`. During a bake the
click was thrown away - while Tk still moved the highlight, because that is the
widget's own doing - and `_bake_done` then loaded **its** map. The list showed
what you picked and the app had loaded something else.

Busy is not a reason to discard a click. It is held in `pending_pick`, and
whatever finishes honours the last map picked.

## Two more, same shape as each other

Both were "one string in two places, and they drifted":

- Rows carried a `"    (no bake)"` suffix that every read had to strip. The
  strip worked only while its literal matched the one that built the row. Rows
  now hold the map name and nothing else; bake state is shown by **colour**.
- `bake_is_python` looked for `source=python-terrain` while the writer emitted
  `source=python-boxes`, so every Python bake reported as a nuTerra one - and
  the "Replace the real bake?" guard warned about overwriting bakes it had
  written itself, while staying silent for the case it exists to catch.

## The trace

`PS_PICK_LOG=<file>` turns on a line per event at every entry point that
touches either picker: what fired, the selected index, the row text, the
dropdown text, `busy`, `_refilling`, the currently loaded map, and the **call
chain through path_studio.py**. Off, and it costs nothing.

It is what found #3 and #4. Neither is reachable by reading the source, because
in both cases the caller is Tk's event loop and the question is *which* queued
thing it is delivering. Reach for it first, not after four guesses.

```
#019 ask_to_bake  ... loaded='34_redshire' [for='29_el_hallouf']
        <- ask_to_bake:1650 < load_named:1685 < load_selected:1576
```

That one line is the whole of trap #3: the dialog for one map, opening from
inside the load of another.

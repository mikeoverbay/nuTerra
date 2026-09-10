# Tk event traps in Path Studio

Six ways the map picker was made to load the wrong map, all found on
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

## 5. A key bound on the toplevel arrives AFTER the widget's class binding

Space was added to pause the live trace, bound on the root so it would work
wherever focus happened to be. It broke both pickers, because Tk delivers a key
along the focused widget's **bindtags**, in order:

```
.the.widget      Listbox      .           all
   widget          CLASS     toplevel
```

The class script runs **second**, the toplevel's fourth. So a handler bound on
the root cannot prevent what the class binding already did - returning
`"break"` there stops nothing, it is too late by then. And every control on
this panel already owns space:

| class | Tk's own binding | what it does |
|-------|------------------|--------------|
| `TButton`, `TCheckbutton` | `ttk::button::activate %W` | presses it |
| `Listbox` | `tk::ListboxBeginSelect %W [%W index active]` | selects the **active** row and fires `<<ListboxSelect>>` |
| `TCombobox` | *(none)* | the dropdown is innocent |

The Listbox one is the map picker again, arriving through the keyboard. The
**active** row is not the selected row: a click sets both, but any rebuild
resets active to 0 while the selection is put back where it was, and
`activestyle="none"` means the active row is not even drawn. Measured on a bare
Tk of five map names, row 3 clicked and active left at 0:

```
BEFORE:  space on the list fired ['01_karelia']   selection is now 01_karelia   button invoked 1 time(s)
FIXED:   space on the list fired []               selection is now 04_himmelsdorf  button invoked 0 time(s)
```

One press of space loaded `01_karelia` over the `04_himmelsdorf` that had been
clicked - trap #1 all over again, and no line of Path Studio does it.

The fix has to replace the class script, not fight it:

```python
for cls in ("TButton", "TCheckbutton", "TRadiobutton", "Listbox"):
    root.bind_class(cls, "<space>", self.on_space)
root.bind("<space>", self.on_space)
```

`bind_class` without `add=` **replaces** the class binding, so Tk's script
never runs. `Entry` and `Text` are deliberately left alone - a space typed into
the map search has to stay a space, and `on_space` returns early when focus is
in one.

That early return needs one exception: **`ttk.Combobox` IS a `ttk.Entry`**, so
the text-box test caught the dropdown too and the pause key went dead from the
moment a map was picked with it. Ours is `state="readonly"` - nothing can be
typed into it - so it is excluded explicitly. Counted from each place focus can
be, one press now reaches the trace exactly once:

```
focus map list      on_space x1   list=04_himmelsdorf
focus Trace button  on_space x1   list=04_himmelsdorf
focus combo         on_space x1   list=04_himmelsdorf
focus search box    on_space x0   list=04_himmelsdorf   (and the space is typed)
focus canvas        on_space x1   list=04_himmelsdorf
```

Exactly once matters as much as the map not moving: `on_space` is bound in two
places, and two calls per press is a pause that never pauses.

`on_space` then has to return `"break"` on every path out. It is bound twice
now, as a class binding and on the toplevel, and without `"break"` the toplevel
copy runs second and toggles the pause straight back off: one press, no effect.

The general rule: **before binding any plain key on the root, check what the
focused widget's class already does with it.**

```
python -c "import tkinter as tk; r=tk.Tk(); print(r.call('bind','Listbox','<space>'))"
```

`<Escape>`, `<BackSpace>` and `<Delete>` were safe on this panel only by luck -
no ttk control here binds them.

## 6. A press picks a map. The mouse moving afterwards picks another one

The last one, and the one that survived every fix above, because every test
written for those fired a CLEAN event: `<<ListboxSelect>>` directly, or a
`<Button-1>` with the pointer perfectly still. Nobody clicks like that.

`selectmode` is `browse` - the default - and Tk binds:

```
<B1-Motion>  tk::ListboxMotion %W [%W index @%x,%y]
<B1-Leave>   tk::ListboxAutoScan %W
```

Motion moves the selection to whatever row is under the pointer for as long as
the button is down, and **every move fires `<<ListboxSelect>>`**, which is a
load. AutoScan is the same thing off the edge of the widget, repeating every
50 ms and scrolling as it goes - and on a list eight rows tall the edge is
never far from wherever you clicked.

Measured, one press with the pointer drifting two pixels:

```
press on '03_campania_big' -> loaded '03_campania_big'
   ...drift 16 px          -> loaded '04_himmelsdorf'
   ...drift 32 px          -> loaded '05_prohorovka'
loads fired by ONE click-and-twitch: ['03_campania_big', '04_himmelsdorf', '05_prohorovka']
```

Three maps loaded, the last one wins, and the list ends up highlighting a row
nobody aimed at. In the owner's words: *"Why it would ever change after
selecting a map is baffling."*

This is a list you take ONE name from. There is nothing to drag and no range
to extend, so both bindings are simply refused:

```python
self.maps.bind("<B1-Motion>", lambda e: "break")
self.maps.bind("<B1-Leave>",  lambda e: "break")
```

Widget bindings run BEFORE class bindings, so `"break"` here really does stop
the class script - the opposite of trap 5, where the binding was on the root
and arrived too late to stop anything.

The dropdown is deliberately NOT given the same treatment. Its popdown is a
Listbox too, but press-drag-release is how a dropdown is *meant* to be used,
and `<<ComboboxSelected>>` fires on release rather than on every row crossed.

### ...and the wheel was jumping four rows

Same widget, found in the same sitting. Tk's binding is
`yview scroll [expr {-(%D/120)*4}] units` - four lines a notch, half of an
eight-row list, so it overshoots every time and puts a different set of rows
under the cursor than the one aimed at. Now one line per notch.

`tools/picker_click_test.py` clicks 25 rows across five scroll positions and
twitches the pointer after every press, one of them dragging clean off the
bottom edge, and checks each one loaded the row it landed on **once**. A clean
press was never the failure, so a test that only makes clean presses proves
nothing.

## Two more, same shape as each other

Both were "one string in two places, and they drifted":

- Rows carried a `"    (no bake)"` suffix that every read had to strip. The
  strip worked only while its literal matched the one that built the row. Rows
  now hold the map name and nothing else; bake state is shown by **colour**.
- `bake_is_python` looked for `source=python-terrain` while the writer emitted
  `source=python-boxes`, so every Python bake reported as a nuTerra one - and
  the "Replace the real bake?" guard warned about overwriting bakes it had
  written itself, while staying silent for the case it exists to catch.

## No lock

There was one, briefly - fences in the source and a checker that failed on any
edit inside them. It went in after trap 5 and came straight back out after
trap 6, at the owner's word: *"unlock that code."*

He was right, and trap 6 is why. The lock froze the picker in the shape five
fixes had left it - and the sixth bug was already there, in a Tk class binding,
untouched by any of them. A fence keeps the code the same; it does not make the
code correct, and it made the next fix a negotiation.

What actually catches these is a test that behaves like a person:
`tools/picker_click_test.py` clicks badly on purpose. Every trap on this page
would fall to a probe, and none of them fell to a rule.

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

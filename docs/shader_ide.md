# The in-app shader IDE

`nuTerra\Tools\ShaderIDE.vb`. Toolbar button **Shader IDE**. Edit any stage of
any program the app has loaded and recompile it in place, without leaving the
frame.

## What you see

* **Picker** at the top: every `Shader` the loader constructed, grouped by the
  folder its files live in under `shaders\` (`Final_render`, `Model_shaders`,
  `Tanks`, ...). The current one is marked `>`.
* **A tab per stage** the program has: `vert`, `tesc`, `tese`, `geom`, `comp`,
  `frag`. A `*` on the tab means edited since the last compile. Each tab is an
  editable text box with GLSL highlighting and a line-number gutter; Tab
  inserts a tab.
* **Bottom bar**: Compile, Reload from disk, Revert to opened copy, Exit, the
  status, and the compiler's own message in a scrollable box.

## How Compile keeps the app safe

1. The edited text of every stage is written to temp files under
   `%TEMP%\nuTerra\shader_ide\`.
2. A **throwaway program** is assembled from them through `assemble_shader`,
   the same path every startup compile takes, with the program's own
   `#define`s (`Shader.DefinesCopy`).
3. **Only if that links** are the real files written - the copy under
   `bin\...\shaders` the app reads, and the project copy under
   `nuTerra\shaders` so the change survives a rebuild and lands in git - and
   the live program rebuilt from them (`Shader.UpdateShader`).
4. A failed compile writes nothing and the frame keeps rendering with the old
   program. The message is in the box.

So the app never runs a broken shader, whatever is typed.

## The revert guard

Opening a shader saves a copy of every stage. **Revert to opened copy** puts
it back in the editor, on disk, and in the live program. When the last compile
failed and you press Exit, close the window, or pick another shader, a modal
asks whether to revert first: *Revert then continue*, *Keep files*, or
*Cancel*. The files on disk at that point hold the last text that compiled
this session, never the failed one.

## The highlighter

`InputTextMultiline` cannot colour its own text. The box is drawn with a
transparent text colour and the tokens are painted over it in the same
monospaced font at the same scroll, by re-entering the input's own child
window and reading its scroll. Cursor and selection are ImGui's.

**Re-enter that child BY LABEL, never by id.** A child window's identity is
its title, and `BeginChildEx` builds it two ways: `parent/name_id` when it is
given a name, `parent/id` when it is not. `InputTextEx` makes its multiline
child with `BeginChildEx(label, id, ...)` - it passes the label on purpose, so
the window reads sensibly in the metrics view - while the `BeginChild` overload
taking an `ImGuiID` passes no name at all. Matching the id alone therefore
misses, and what looks like a re-entry silently opens a SECOND child at the
parent's cursor, below the box: every line of highlight lands in the strip at
the bottom of the window and the editor, whose own text is transparent, looks
empty. Appending to the right window is a supported path - `EndChild` checks
`BeginCount > 1` and skips re-emitting the item into the parent, and position,
size and flags apply only on a window's first `Begin` of the frame, so the
geometry stays the input's. Only the
visible lines are painted; the whole text is re-tokenised when it changes,
which is a few thousand lines in well under a millisecond. Block comments
carry across lines; everything else is per line. Tabs are expanded to four
columns for both the measurement and the paint, which is how ImGui lays them
out.

## The gutter

Reserved to the left of the box with a `Dummy` before it, never painted over
it, so numbers and text cannot collide however long a line gets. Its width
follows the line count - three figures minimum, wider when the file needs it.

The numbers go in the PARENT window's draw list, not the input's child: the
child clips to the text area and the gutter is outside that. So the paint
inside the child carries its origin, line height, scroll and visible line span
back out, and the gutter draws on exactly the same baseline - a number cannot
slide off its line, because both come from one set of numbers. They are
clipped to the box's top and bottom edges and right aligned.

## The edit buffer

`InputTextMultiline`'s `ref string` overload copies the text into a native
buffer of the size asked for, plus a second one beside it to diff against, on
**every frame** the editor is open. The cap is therefore sized to the file -
twice its length plus a page - rather than a flat megabyte, which cost two
megabyte allocations and two megabyte copies per stage per frame while typing.

The monospaced face is Consolas from `C:\Windows\Fonts`, added in
`ImGuiController` when present (`ImGuiController.MONO_FONT`). Without it the
IDE falls back to the UI font and says so - the highlight will drift on long
lines because the columns no longer line up.

## Core touch points

Four, all small: `LAST_SHADER_ERROR` and `gl_error` appending to it, and the
`DefinesCopy` property, in `ShaderLoader.vb`; the font in
`ImGuiController.vb`; the button and the `ShaderIDE.Draw()` call in
`Window.vb`.

## Not yet

Undo beyond ImGui's own, find/replace, jumping to the line an error names,
editing `common.h` (an include, not a program). Those want a real text-editor
widget rather than `InputTextMultiline`, which has no API for the cursor
position, the selection or an undo stack - see the note below.

## If this needs to become a real editor

`InputTextMultiline` gives no way to read the caret, the selection or an undo
history, so find/replace and go-to-line cannot be built on it honestly. Two
ways out, neither taken yet:

* **ImGuiColorTextEdit**, the usual answer, is C++. It would have to be built
  into `nuTerraCPP` and hand-bound, against the same ImGui build ImGui.NET
  1.87.2 wraps - and ImGui.NET exposes no pointer-taking overloads that VB can
  call, so the binding would have to carry its own surface.
* **A separate editor window** - ScintillaNET or AvalonEdit in a WinForms
  form - gets line numbers, folding, find/replace and undo for free, at the
  cost of leaving the in-app overlay the IDE was built to be.

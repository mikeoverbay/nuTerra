# The in-app shader IDE

`nuTerra\Tools\ShaderIDE.vb`. Toolbar button **Shader IDE**. Edit any stage of
any program the app has loaded and recompile it in place, without leaving the
frame.

## What you see

* **Picker** at the top: every `Shader` the loader constructed, grouped by the
  folder its files live in under `shaders\` (`Final_render`, `Model_shaders`,
  `Tanks`, ...). The current one is marked `>`.
* **A tab per stage** the program has: `vert`, `tesc`, `tese`, `geom`, `comp`,
  `frag`. A `*` on the tab means edited since the last compile. Each tab is a
  full editor - line numbers, syntax colouring, selection, undo/redo, and
  error markers on the lines a failed compile named.
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

## The editor

The text box is **ImGuiColorTextEditNet** (`TextEditor`), the C# port of
ImGuiColorTextEdit. One instance per stage, created in `LoadStages` by
`NewEditor` and kept for the life of the stage - it holds its own text, cursor,
selection and undo stack across frames, so it is state, not an immediate-mode
call.

It draws its own line-number gutter, cursor, selection, current-line highlight
and error markers, and handles its own keys. Nothing is painted over it.

- **Reading the text back** costs a join of every line, so it happens only when
  `editor.Version` changes - that counter ticks on each modification.
- **Writing text in** from outside - a load, a revert - must go through
  `SetStageText`, which sets both `st.text` and `editor.AllText`. Set one
  without the other and the widget keeps showing what it had, then the next
  keystroke writes the stale copy back.
- The palette is set to the colours the old hand-rolled highlighter used, so
  the swap did not also change what the code looks like.

### GLSL is highlighted as C, and why

`CStyleHighlighter` is the only highlighter the package ships. It carries a
`LanguageDefinition.Glsl()`, but **nothing in the package consumes a
`LanguageDefinition`** - the regex-driven highlighting was never ported from
the C++ original.

Writing our own is not possible from VB: `ISyntaxHighlighter.Colorize` takes a
`Span(Of Glyph)`, and VB has no way to name a ByRef-like type in a signature.
So comments, strings, numbers, the preprocessor and C's keywords colour
correctly, while GLSL's own types and builtins - `vec3`, `mat4`, `normalize` -
read as plain identifiers. Recovering those means a small C# assembly holding
that one interface, which is the only reason this project would need one.

## Compile errors land on their lines

A failed compile puts each driver message on the line it names, as an error
marker in the gutter with the message as its tooltip, and scrolls that stage's
editor to the first one. The status line names the stage and line.

Two things make this possible. `ShaderLoader.gl_error` prefixes each stage's
log with `<name>_vertex didn't compile!` and the like, so the text arrives
already divided by stage - that header is what says which editor a line number
belongs to. And the line itself is matched against both vendor spellings:
NVIDIA's `0(123) : error C1503:` and the Mesa / AMD family's
`ERROR: 0:123:`. Anything matching neither still shows in the box below; it
just gets no marker.

Markers are cleared at the top of every compile, so a fixed error stops being
flagged the moment it compiles.

## Core touch points

Four, all small: `LAST_SHADER_ERROR` and `gl_error` appending to it, and the
`DefinesCopy` property, in `ShaderLoader.vb`; the font in
`ImGuiController.vb`; the button and the `ShaderIDE.Draw()` call in
`Window.vb`.

## Not yet

Find/replace, and editing `common.h` (an include, not a program). Undo/redo,
a line-number gutter and go-to-the-failing-line arrived with the editor swap.

Selecting a tab from code is also out of reach: it needs
`ImGuiTabItemFlags.SetSelected`, and every ImGui.NET `BeginTabItem` overload
that takes flags also takes `p_open` by reference, with no way to pass the null
ImGui reads as "no close button". Asking for the flag would put an X on every
tab, so a failed compile names its stage in the status line instead of jumping
to it.

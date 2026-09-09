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
  editable text box with GLSL highlighting; Tab inserts a tab.
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
window and reading its scroll. Cursor and selection are ImGui's. Only the
visible lines are painted; the whole text is re-tokenised when it changes,
which is a few thousand lines in well under a millisecond. Block comments
carry across lines; everything else is per line. Tabs are expanded to four
columns for both the measurement and the paint, which is how ImGui lays them
out.

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

Undo beyond ImGui's own, find/replace, a line-number gutter, jumping to the
line an error names, editing `common.h` (an include, not a program).

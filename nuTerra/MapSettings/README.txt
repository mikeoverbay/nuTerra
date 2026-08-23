Per-map render settings.

One file per space, named after the space folder - so Abbey is 19_monastery.txt.
Everything in here is copied next to the exe on build, and the running app reads
from that copy.

Workflow
  1. Load the map and tune it with the sliders in Settings.
  2. Settings -> Map Settings -> "Save settings for this map".
     That writes to bin\Debug\net6.0-windows\MapSettings\<space>.txt - the
     working copy, which a rebuild will overwrite.
  3. Copy that file back into this folder to keep it and put it under git.

Format is key=value, one per line. '#' starts a comment and blank lines are
fine. Order does not matter.

A missing key is not an error - that setting simply keeps whatever the map's
environment.xml or the global defaults gave it. So a file can be trimmed down to
only the lines that actually differ for that map, which makes the diffs readable
and lets one map's file serve as a template for the next.

An unrecognised key is ignored and logged, so old files stay loadable after a
setting is renamed or dropped.

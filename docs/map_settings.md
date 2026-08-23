# Per-map render settings

Every map keeps its own copy of the render settings, so a look tuned for one
space does not follow you to the next.

## Where the files live

```
nuTerra\MapSettings\<space>.txt        shipped defaults, under git
  -> bin\<config>\<tfm>\MapSettings\   copied on build, included in publish
  -> %TEMP%\nuTerra\MapSettings\       the user's working copies
```

On startup `modMapSettings.SeedWorkFolder` copies any shipped file that is not
already in the work folder. **Existing files are never overwritten**, so tuning
survives an app update. Delete one and it comes back as the shipped default;
delete the folder and every map resets.

Saving writes to the work folder. Loading prefers it and falls back to the
shipped copy.

To promote a tuned map to a shipped default, copy its file from the work folder
into `nuTerra\MapSettings\`.

## Format

`key=value`, one per line. `#` comments, blank lines fine, order irrelevant.

```
# nuTerra render settings for 19_monastery

ambient=0.4
sun_strength=1.261
tonemap_exposure=4
...
```

**A missing key is not an error.** That setting keeps whatever the map's
`environment.xml` and the global defaults already gave it. So a file can be cut
down to only the lines that actually differ for that map, which keeps diffs
readable and lets one map's file serve as the template for the next.

An unrecognised key is ignored and logged, so old files stay loadable after a
setting is renamed or dropped.

## When it saves

- **The button** - Settings -> Map Settings -> "Save settings for this map".
- **On close**, if anything changed since the map was loaded.

A baseline is taken when a map finishes loading, whether or not a file existed,
so tuning away from the defaults counts as a change. The comparison is on the
formatted strings that would be written, not raw floats, so "changed" means the
file would genuinely differ.

Both paths only fire on a clean exit. Killing the process loses the session, same
as the global `My.Settings`.

## Ordering

`modMapSettings.Load` is called after `MAP_LOADED = True`, which is well after
`get_environment_info`. So the file wins over anything the map's environment or
the global defaults set. `CommonProperties.Init()` runs once at startup and never
on map load, so it cannot stamp on a loaded file.

## Adding a setting

One line in `Fields()` in `Modules/modMapSettings.vb` - a name, a getter and a
setter. Booleans go through `B2F`/`F2B` so the file stays one shape. Old files
missing the new key simply keep the default.

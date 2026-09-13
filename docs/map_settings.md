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

## The work folder is NOT per session, and `set=` writes to it

Everything above assumes one person at one machine. With several Claude
sessions on the same box it stops holding, in two ways that both look like the
app misbehaving:

**`%TEMP%` is per USER, and per redirect.** A session that redirects TMP into
its own clone gets its own work folder; one that does not shares the owner's.
So two sessions can read *different settings for the same map* and compare
frames that were never comparable. Measured 2026-09-12: one clone's
`19_monastery` work copy held `brightness=1.384, fog_level=0.139` while the
shipped file and the owner's own work copy held `1.02` and `0.155`. An entire
evening's transfer-curve measurement described one instance and was reported as
if it described the other.

**A `set=` override persists.** `set=brightness=0.5` is applied after the file
loads, which makes it count as a change, so the on-close save writes it into
the work folder. The next run you believe is a baseline is not one. Before
trusting any A/B here: read the work copy, not the shipped file.

The log says which file was used - `Reading map settings from ...` - and
`Map settings: seeded N, kept M existing` says whether yours was freshly seeded
or is one you have been accumulating. Read those two lines before believing a
measurement.

Recovery is as above: delete the work copy and it comes back shipped.

*Added 2026-09-13 by nuTerra work. The single-user behaviour was already
documented here correctly and I rederived it from measurements instead of
reading this file first - which cost about an hour.*

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

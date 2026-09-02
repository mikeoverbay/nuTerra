# cam_paths

Baked camera flight paths, one `.campath` per map, named after the map
(`19_monastery.campath`). Copied into the build output alongside `MapSettings`,
so nuTerra can load a flight without the tools being present.

**Only `.campath` files belong here.** The project glob is
`cam_paths\**\*.campath`, so nothing else reaches the build output — this file
included. The CSV and the bank picture the exporter draws go to
`%TEMP%\nuTerra\flight\` with the other diagnostics rather than sitting here.

## Making one

    cd tools
    python export_cam_path.py 19_monastery

That flies the route with `radar_commit.py` at 5 m above ground, avoiding
anything it cannot clear, and writes position, heading, tilt and roll for every
point. It needs the bake in `%TEMP%\nuTerra\flight\`, which `MapFlightBake`
writes on map load.

## Format

Little endian throughout. A 64 byte header, then `count` fixed-size records.
`tools/cam_path.py` is the authority - run it with no arguments to print the
layout, or with a file to describe it.

    Header, 64 bytes
      0   char[4]   "NCP1"
      4   uint16    version, 1
      6   uint16    flags, bit 0 = closed loop
      8   uint32    count
      12  uint32    stride, 32 in version 1
      16  float32   total length, metres
      20  char[40]  map name, ASCII, null padded
      60  uint32    reserved

    Point, 32 bytes, 8 x float32
      0   x, y, z   eye position, world metres. y is absolute, not AGL.
      12  heading   yaw, radians. atan2(dx, dz) - 0 looks down +Z toward +X.
      16  tilt      pitch, radians. POSITIVE LOOKS UP.
      20  roll      bank, radians. POSITIVE BANKS RIGHT.
      24  s         distance from the first point, metres
      28  speed     metres per second

Skip records by `stride` rather than by 32, so a later version that appends
fields still reads on an old loader.

## Reading it in nuTerra

A `BinaryReader` loop - there is nothing to parse. Heading and tilt map onto
`MapCamera.CAM_X_ANGLE` and `CAM_Y_ANGLE`; note `CAM_Y_ANGLE` is clamped to
about -1.57 .. 1.3 there, and tilt is capped at 16 degrees on the way out, so it
already fits.

**Roll has nowhere to go yet.** The orbit rig has no roll axis, so playing the
bank back needs one adding to the view matrix. The value is written because
changing the format later costs far more than eight unused bytes now.
